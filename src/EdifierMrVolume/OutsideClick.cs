using System.Runtime.InteropServices;

namespace EdifierMrVolume;

/// <summary>
/// Reports mouse clicks anywhere on screen while the panel is open, so a click outside closes it.
/// The panel also closes when Windows deactivates it, but Windows may refuse to activate it at all:
/// the right to take the foreground "is lost the next time the user generates input"
/// (SetForegroundWindow, Microsoft Learn). A panel that was never active is never deactivated, so
/// it stayed open on clicks elsewhere. Like TrayWheel, a low-level mouse hook on its own thread, as Microsoft asks for
/// low-level hooks, and only while the panel is open, so the rest of the time no mouse input
/// passes through this app. The hook only passes the click point on; the caller decides on the
/// UI thread whether it was outside.
/// </summary>
internal sealed partial class OutsideClick : IDisposable
{
    private const int WhMouseLl = 14;
    private const nint WmLButtonDown = 0x0201;
    private const nint WmRButtonDown = 0x0204;
    private const nint WmMButtonDown = 0x0207;
    private const nint WmXButtonDown = 0x020B;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly Action<int, int> _onClick;
    private readonly HookProc _proc;
    private readonly Lock _gate = new();
    private Session? _session;
    private bool _failureLogged;

    /// <param name="onClick">Screen point of a button press, in physical pixels; called on the hook thread.</param>
    public OutsideClick(Action<int, int> onClick)
    {
        _onClick = onClick;
        _proc = OnMouse; // kept in a field: Windows calls back through it
    }

    /// <summary>One hook thread; a quick Stop then Start can't leave an old one running.</summary>
    private sealed class Session
    {
        public uint ThreadId;
        public bool Stopped;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_session is not null) return;
            var session = _session = new Session();
            new Thread(() => Run(session)) { IsBackground = true, Name = "Outside click" }.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is not { } session) return;
            _session = null;
            session.Stopped = true;
            if (session.ThreadId != 0) PostThreadMessageW(session.ThreadId, WmQuit, 0, 0);
        }
    }

    private void Run(Session session)
    {
        // Give the thread its message queue first, so Stop's WM_QUIT can't get lost.
        PeekMessageW(out _, 0, 0, 0, PmNoRemove);
        lock (_gate)
        {
            if (session.Stopped) return;
            session.ThreadId = GetCurrentThreadId();
        }
        var hook = SetWindowsHookExW(WhMouseLl, Marshal.GetFunctionPointerForDelegate(_proc), GetModuleHandleW(null), 0);
        if (hook == 0)
        {
            if (!_failureLogged) Log.Write($"Outside click: hook not installed ({Marshal.GetLastPInvokeError()}).");
            _failureLogged = true;
            return;
        }
        // The hook is called from inside GetMessage; WM_QUIT (panel closed, or exit) ends it.
        while (GetMessageW(out _, 0, 0, 0) > 0) { }
        UnhookWindowsHookEx(hook);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
        {
            // MSLLHOOKSTRUCT starts with pt.x, pt.y.
            _onClick(Marshal.ReadInt32(data, 0), Marshal.ReadInt32(data, 4));
        }
        return CallNextHookEx(0, code, message, data);
    }

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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint HookProc(int code, nint message, nint data);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int hookId, nint proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint window, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
