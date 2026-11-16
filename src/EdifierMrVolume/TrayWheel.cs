using System.Runtime.InteropServices;

namespace EdifierMrVolume;

/// <summary>
/// The mouse wheel over the notification-area icon. Windows tells a notification icon about
/// clicks and pointer moves but not the wheel, so, as VolumeScroller does
/// (https://github.com/markbrents/VolumeScroller), a low-level mouse hook watches the wheel and
/// Shell_NotifyIconGetRect says whether the pointer is over the icon. To keep the app light the
/// hook exists only while the pointer is over the icon: it is installed on the icon's first
/// pointer move and removed as soon as the pointer leaves, so the rest of the time no mouse
/// input passes through this app. It runs on its own thread, as Microsoft asks for low-level
/// hooks (a slow hook is silently removed by Windows).
/// </summary>
internal sealed partial class TrayWheel : IDisposable
{
    private const int WhMouseLl = 14;
    private const nint WmMouseMove = 0x0200;
    private const nint WmMouseWheel = 0x020A;
    private const uint WmQuit = 0x0012;
    private const int WheelDelta = 120;

    private readonly Guid _iconId;
    private readonly Action<int> _onNotch;
    private readonly HookProc _proc;
    private int _active;
    private uint _threadId;
    private Rect _icon;
    private int _accumulated;
    private bool _rectFailureLogged;
    private bool _firstUseLogged;

    /// <param name="onNotch">+1 or −1 per full notch (up = louder), called on the hook thread.</param>
    public TrayWheel(Guid iconId, Action<int> onNotch)
    {
        _iconId = iconId;
        _onNotch = onNotch;
        _proc = OnMouse; // kept in a field: Windows calls back through it
    }

    /// <summary>The pointer moved over the icon: watch the wheel until it leaves.</summary>
    public void PointerOverIcon()
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return;
        var result = GetIconRect(out _icon);
        if (result != 0)
        {
            if (!_rectFailureLogged) Log.Write($"Tray wheel: icon position not available (0x{result:X8}).");
            _rectFailureLogged = true;
            Interlocked.Exchange(ref _active, 0);
            return;
        }
        if (!_firstUseLogged) Log.Write($"Tray wheel: watching the wheel over the icon ({_icon.Left},{_icon.Top})-({_icon.Right},{_icon.Bottom}).");
        _firstUseLogged = true;
        new Thread(Run) { IsBackground = true, Name = "Tray wheel" }.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _accumulated = 0;
        var hook = SetWindowsHookExW(WhMouseLl, Marshal.GetFunctionPointerForDelegate(_proc), GetModuleHandleW(null), 0);
        if (hook == 0)
        {
            Log.Write($"Tray wheel: hook not installed ({Marshal.GetLastPInvokeError()}).");
        }
        else
        {
            // The hook is called from inside GetMessage; WM_QUIT (pointer left, or exit) ends it.
            while (GetMessageW(out _, 0, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
        }
        Interlocked.Exchange(ref _active, 0);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            // MSLLHOOKSTRUCT: pt.x, pt.y, mouseData (high word = wheel delta), ...
            var x = Marshal.ReadInt32(data, 0);
            var y = Marshal.ReadInt32(data, 4);
            var over = x >= _icon.Left && x < _icon.Right && y >= _icon.Top && y < _icon.Bottom;
            if (message == WmMouseWheel && over)
            {
                // A full notch is 120; precision touchpads send smaller amounts, so add them up.
                _accumulated += (short)(Marshal.ReadInt32(data, 8) >> 16);
                while (_accumulated >= WheelDelta)
                {
                    _accumulated -= WheelDelta;
                    _onNotch(+1);
                }
                while (_accumulated <= -WheelDelta)
                {
                    _accumulated += WheelDelta;
                    _onNotch(-1);
                }
                return 1; // handled: nothing under the icon should scroll as well
            }
            if (message == WmMouseMove && !over) PostQuitMessage(0);
        }
        return CallNextHookEx(0, code, message, data);
    }

    private int GetIconRect(out Rect rect)
    {
        var id = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Guid = _iconId,
        };
        return Shell_NotifyIconGetRect(ref id, out rect);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _active) != 0 && _threadId != 0) PostThreadMessageW(_threadId, WmQuit, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

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

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);

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
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
