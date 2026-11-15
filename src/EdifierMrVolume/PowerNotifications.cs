using System.Runtime.InteropServices;

namespace EdifierMrVolume;

/// <summary>
/// Sleep and wake notifications from Windows (PowerRegisterSuspendResumeNotification with a
/// callback). Unlike most power events, Windows waits for this callback before it sleeps, so
/// the app can still reach the speaker.
/// </summary>
internal static partial class PowerNotifications
{
    public enum Event { Suspend, Resume }

    private const uint DeviceNotifyCallback = 2;
    private const uint PbtApmSuspend = 0x4;
    private const uint PbtApmResumeSuspend = 0x7;
    private const uint PbtApmResumeAutomatic = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerRegisterSuspendResumeNotification(uint flags, nint recipient, out nint registrationHandle);

    // Kept alive for the life of the process: Windows calls back through them.
    private static DeviceNotifyCallbackRoutine? _callback;
    private static nint _parameters;
    private static nint _registration;
    private static Action<Event>? _handler;

    public static bool Register(Action<Event> handler)
    {
        if (_registration != 0) return true;
        _handler = handler;
        _callback = OnPowerEvent;
        _parameters = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceNotifySubscribeParameters>());
        Marshal.StructureToPtr(new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = 0,
        }, _parameters, fDeleteOld: false);
        var result = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, _parameters, out _registration);
        return result == 0;
    }

    private static uint OnPowerEvent(nint context, uint type, nint setting)
    {
        try
        {
            switch (type)
            {
                case PbtApmSuspend:
                    _handler?.Invoke(Event.Suspend);
                    break;
                case PbtApmResumeSuspend:
                case PbtApmResumeAutomatic:
                    _handler?.Invoke(Event.Resume);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Power notification handler failed: {ex.Message}");
        }
        return 0;
    }
}
