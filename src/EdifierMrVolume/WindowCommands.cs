using Windows.Graphics;

namespace EdifierMrVolume;

/// <summary>What the panel can ask the app to do.</summary>
internal sealed record WindowCommands(
    Func<Task> Reconnect,
    Func<Task> PowerOff,
    Func<bool, Task> SetConnected,
    Action<bool> SetAutostart,
    Action Exit,
    Action SettingsChanged,
    Func<RectInt32?> IconBounds);
