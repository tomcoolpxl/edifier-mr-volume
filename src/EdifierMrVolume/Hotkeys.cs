using System.Runtime.InteropServices;

namespace EdifierMrVolume;

/// <summary>
/// Keyboard shortcuts that work in any app: modifiers + Up, Down and M. Registered with Windows'
/// RegisterHotKey on a thread of their own, like OutsideClick: with no window given, Windows posts
/// WM_HOTKEY to the registering thread's message queue (RegisterHotKey, Microsoft Learn). Only
/// while the setting is on; off by default.
/// </summary>
internal sealed partial class Hotkeys : IDisposable
{
    public enum Key
    {
        VolumeUp = 1,
        VolumeDown = 2,
        Mute = 3,
    }

    /// <summary>
    /// The modifier choices in the settings. No Windows key: Microsoft reserves shortcuts with it
    /// for the operating system (RegisterHotKey, Microsoft Learn).
    /// </summary>
    public static readonly string[] ModifierChoices = ["Ctrl + Alt", "Ctrl + Shift", "Alt + Shift", "Ctrl + Alt + Shift"];

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const uint VkUp = 0x26;
    private const uint VkDown = 0x28;
    private const uint VkM = 0x4D;

    private static readonly (Key Key, uint VirtualKey, string Name, bool Repeats)[] Keys =
    [
        (Key.VolumeUp, VkUp, "Up", true),   // held down, it keeps stepping up
        (Key.VolumeDown, VkDown, "Down", true),
        (Key.Mute, VkM, "M", false),        // held down, it would mute and unmute in turn
    ];

    private readonly Action<Key> _onKey;
    private readonly Action<string?> _onRegistered;
    private readonly Lock _gate = new();
    private Session? _session;

    /// <param name="onKey">A shortcut was pressed; called on the shortcut thread.</param>
    /// <param name="onRegistered">
    /// After registering: null, or which shortcuts another app holds ("Ctrl + Alt + M"); called on
    /// the shortcut thread.
    /// </param>
    public Hotkeys(Action<Key> onKey, Action<string?> onRegistered)
    {
        _onKey = onKey;
        _onRegistered = onRegistered;
    }

    private sealed class Session
    {
        public required string Modifiers;
        public Thread? Previous;
        public Thread? Thread;
        public uint ThreadId;
        public bool Stopped;
    }

    /// <summary>Registers the shortcuts with these modifiers (one of <see cref="ModifierChoices"/>).</summary>
    public void Start(string modifiers)
    {
        lock (_gate)
        {
            if (_session is { } running && running.Modifiers == modifiers) return;
            var previous = StopLocked();
            var session = _session = new Session { Modifiers = modifiers, Previous = previous };
            session.Thread = new Thread(() => Run(session)) { IsBackground = true, Name = "Keyboard shortcuts" };
            session.Thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    /// <summary>Ends the running session; returns its thread, which may still be unregistering.</summary>
    private Thread? StopLocked()
    {
        if (_session is not { } session) return null;
        _session = null;
        session.Stopped = true;
        if (session.ThreadId != 0) PostThreadMessageW(session.ThreadId, WmQuit, 0, 0);
        return session.Thread;
    }

    private void Run(Session session)
    {
        // A shortcut stays registered until the thread that registered it lets go: wait for the
        // previous session, or its keys would look taken (by this app).
        session.Previous?.Join(TimeSpan.FromSeconds(2));
        session.Previous = null;
        // Give the thread its message queue first, so Stop's WM_QUIT can't get lost.
        PeekMessageW(out _, 0, 0, 0, PmNoRemove);
        lock (_gate)
        {
            if (session.Stopped) return;
            session.ThreadId = GetCurrentThreadId();
        }

        var modifiers = ModifierFlags(session.Modifiers);
        var registered = new List<int>();
        var taken = new List<string>();
        var error = 0;
        foreach (var (key, virtualKey, name, repeats) in Keys)
        {
            if (RegisterHotKey(0, (int)key, modifiers | (repeats ? 0 : ModNoRepeat), virtualKey))
            {
                registered.Add((int)key);
            }
            else
            {
                taken.Add($"{session.Modifiers} + {name}");
                error = Marshal.GetLastPInvokeError(); // 1409: already registered
            }
        }
        Log.Write(taken.Count == 0
            ? $"Keyboard shortcuts on: {session.Modifiers} + Up, Down, M."
            : $"Keyboard shortcuts: {string.Join(", ", taken)} in use by another app (error {error}).");
        _onRegistered(taken.Count == 0 ? null : string.Join(", ", taken));

        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == WmHotkey && message.WParam is >= 1 and <= 3)
                _onKey((Key)(int)message.WParam);
        }
        foreach (var id in registered) UnregisterHotKey(0, id);
    }

    private static uint ModifierFlags(string choice) => choice switch
    {
        "Ctrl + Shift" => ModControl | ModShift,
        "Alt + Shift" => ModAlt | ModShift,
        "Ctrl + Alt + Shift" => ModControl | ModAlt | ModShift,
        _ => ModControl | ModAlt,
    };

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint window, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
