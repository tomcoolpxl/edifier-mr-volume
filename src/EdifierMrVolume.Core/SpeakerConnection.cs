using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace EdifierMrVolume.Core;

public enum SpeakerState
{
    /// <summary>Not trying to connect (user released the speaker, e.g. for the phone app).</summary>
    Stopped,
    /// <summary>Scanning for the speaker's BLE advertisement.</summary>
    Searching,
    /// <summary>Found it; opening the GATT control service.</summary>
    Connecting,
    /// <summary>Connected and receiving volume updates.</summary>
    Connected,
}

/// <summary>
/// A volume update. <see cref="FromSpeaker"/> is true for changes made on the speaker itself
/// (knob, phone app); false for echoes of this app's own requests and for the first reading
/// after connecting.
/// </summary>
public readonly record struct VolumeChange(VolumeLevel Level, bool FromSpeaker, bool IsFirstReading);

/// <summary>A snapshot for the Diagnose view.</summary>
public sealed record SpeakerDiagnostics(
    SpeakerState State,
    string? Model,
    string? DeviceName,
    string? Firmware,
    string? Address,
    DateTimeOffset? ConnectedSince,
    string? LastError,
    int Reconnects);

/// <summary>
/// Keeps one BLE control connection to an Edifier MR speaker open: finds it, connects, polls
/// the volume, applies volume requests and reconnects when the link drops. Events are raised
/// on a background thread.
/// </summary>
public sealed class SpeakerConnection : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long Windows may report "disconnected" before we believe it. It sometimes does so
    /// right after connecting (e.g. when another process has just released the link) while
    /// the link stays usable.
    /// </summary>
    private const long BlipGraceMs = 1500;

    /// <summary>Volume changes arriving this soon after our own request are its echo.</summary>
    private const long OwnChangeWindowMs = 1500;

    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _kick = new(0);
    private readonly SemaphoreSlim _wake = new(0);
    private readonly HashSet<ulong> _ignored = [];
    /// <summary>Commands waiting to be written (sound mode switches); sent before the next poll.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _outbox = new();
    private EdifierProtocol.SoundState? _sound;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _pendingSet = -1;
    private long _lastSetAt = long.MinValue / 2;

    private SpeakerState _state = SpeakerState.Stopped;
    private VolumeLevel? _volume;
    private string? _detail;
    private string? _lastError;
    private string? _firmware;
    private string? _deviceName;
    private string? _address;
    private DateTimeOffset? _connectedSince;
    private int _connects;

    // Live link. Only the loop task creates and tears these down.
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private GattDeviceService? _service;
    private GattCharacteristic? _notify;
    private GattCharacteristic? _write;
    private GattWriteOption _writeOption;
    private volatile bool _linkSuspect;
    private volatile bool _linkLost;
    private long _suspectSince;
    private long _lastRxTicks;

    public SpeakerConnection(Action<string>? log = null) => _log = log ?? (_ => { });

    public event EventHandler? StateChanged;
    public event EventHandler<VolumeChange>? VolumeChanged;

    public SpeakerState State { get { lock (_gate) return _state; } }

    /// <summary>Last volume the speaker reported on the current link, or null.</summary>
    public VolumeLevel? Volume { get { lock (_gate) return _volume; } }

    /// <summary>Human-readable reason for the current state (last error), if any.</summary>
    public string? Detail { get { lock (_gate) return _detail; } }

    public string? ModelName { get; private set; }

    /// <summary>Sound mode and room tuning as the speaker last reported them, or null.</summary>
    public EdifierProtocol.SoundState? SoundMode { get { lock (_gate) return _sound; } }

    /// <summary>
    /// Switches Monitor / Music / Custom. Only possible once the speaker has reported its tuning,
    /// because the switch must echo those bytes unchanged.
    /// </summary>
    public bool RequestSoundMode(int mode)
    {
        var current = SoundMode;
        if (current is null || current.Mode == mode || State != SpeakerState.Connected) return false;
        _outbox.Enqueue(EdifierProtocol.SetSoundMode(current, mode));
        Kick();
        return true;
    }

    public bool IsRunning { get { lock (_gate) return _cts is not null; } }

    public SpeakerDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            return new SpeakerDiagnostics(_state, ModelName, _deviceName, _firmware, _address,
                _connectedSince, _lastError, Math.Max(0, _connects - 1));
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }
    }

    /// <summary>Drops the connection and stops reconnecting, so another client can connect.</summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }
        if (cts is null) return;
        cts.Cancel();
        Kick();
        try
        {
            if (loop is not null) await loop.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log($"Stop: {ex.Message}");
        }
        cts.Dispose();
        SetState(SpeakerState.Stopped, null);
    }

    /// <summary>
    /// Asks the speaker to go to this level. Rapid calls (slider drags) are coalesced:
    /// only the latest value is sent.
    /// </summary>
    public void RequestVolume(int level)
    {
        var max = Volume?.Max ?? EdifierProtocol.DefaultMaxVolume;
        if (VolumeLimit is { } limit) max = Math.Min(max, limit);
        Interlocked.Exchange(ref _pendingSet, Math.Clamp(level, 0, max));
        Kick();
    }

    /// <summary>
    /// The highest level <see cref="RequestVolume"/> will ever send, or null for no limit.
    /// The knob on the speaker is not limited.
    /// </summary>
    public int? VolumeLimit { get; set; }

    /// <summary>Shown when the speaker hasn't been found after a while.</summary>
    public const string NotFoundHint =
        "Not found yet. Is the speaker switched on? Only one device can control it at a time, so close EDIFIER ConneX on your phone.";

    private void SetHintIfStillSearching()
    {
        lock (_gate)
        {
            if (_state != SpeakerState.Searching || _detail is not null) return;
            _detail = NotFoundHint;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Retry straight away with a fresh back-off, e.g. after Windows wakes from sleep.
    /// A connection that looks fine is left alone; the silence check catches a dead one.
    /// </summary>
    public void Nudge()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
        Kick();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void Kick()
    {
        if (_kick.CurrentCount == 0) _kick.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(SpeakerState.Searching, failures == 0 ? null : Detail);
                ulong address;
                // After 15 s without finding it, say what to check.
                using (new Timer(_ => SetHintIfStillSearching(), null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan))
                {
                    address = await FindAsync(_ignored.ToHashSet(), ct).ConfigureAwait(false);
                }
                _log($"Found an Edifier control interface at {FormatAddress(address)}.");
                SetState(SpeakerState.Connecting, null);
                try
                {
                    await ConnectAsync(address, ct).ConfigureAwait(false);
                }
                catch (UnsupportedSpeakerException)
                {
                    // Some other Edifier BLE device; don't keep reconnecting to it this session.
                    _ignored.Add(address);
                    throw;
                }
                failures = 0;
                _wake.Wait(0); // a wake-up from before this connection is no longer needed
                lock (_gate)
                {
                    _connectedSince = DateTimeOffset.Now;
                    _connects++;
                    _address = FormatAddress(address);
                }
                SetState(SpeakerState.Connected, null);
                await PumpAsync(ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested) _log("Link lost; reconnecting.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                _log($"Connection attempt {failures} failed: {ex.Message}");
                lock (_gate)
                {
                    _detail = ex.Message;
                    _lastError = $"{DateTime.Now:HH:mm:ss} {ex.Message}";
                }
            }
            finally
            {
                await TearDownAsync().ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested) break;
            var delay = TimeSpan.FromSeconds(Math.Min(30, 2 * Math.Max(1, failures)));
            try
            {
                if (await _wake.WaitAsync(delay, ct).ConfigureAwait(false))
                {
                    _log("Woken up; retrying now.");
                    failures = 0;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<ulong> FindAsync(IReadOnlySet<ulong> ignored, CancellationToken ct)
    {
        var found = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, args) =>
        {
            if (ignored.Contains(args.BluetoothAddress)) return;
            var adv = args.Advertisement;
            if (EdifierProtocol.IsControlAdvertisement(
                    adv.ServiceUuids, adv.LocalName, adv.ManufacturerData.Select(m => m.CompanyId)))
            {
                found.TrySetResult(args.BluetoothAddress);
            }
        };
        watcher.Stopped += (_, args) =>
        {
            if (args.Error != BluetoothError.Success)
            {
                found.TrySetException(new InvalidOperationException(args.Error == BluetoothError.RadioNotAvailable
                    ? "Bluetooth is off or no adapter is available."
                    : $"Bluetooth scan stopped ({args.Error})."));
            }
        };

        using var registration = ct.Register(() => found.TrySetCanceled(ct));
        watcher.Start();
        try
        {
            return await found.Task.ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
        }
    }

    private async Task ConnectAsync(ulong address, CancellationToken ct)
    {
        _linkLost = false;
        _linkSuspect = false;
        // A request made before this link existed is stale; don't apply it now.
        Interlocked.Exchange(ref _pendingSet, -1);

        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Windows could not open the speaker.");
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Keep the link up while we hold it, instead of letting Windows drop idle connections.
        _session = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct).ConfigureAwait(false);
        _session.MaintainConnection = true;

        // One lookup, then pick the first service that belongs to a known model.
        var services = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
        if (services.Status == GattCommunicationStatus.Unreachable)
            throw new InvalidOperationException("The speaker is out of reach, or another device holds its control link.");
        if (services.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Could not read the speaker's services ({services.Status}).");
        foreach (var service in services.Services)
        {
            if (_service is null && EdifierProtocol.ModelForService(service.Uuid) is { } model)
            {
                _service = service;
                ModelName = model.Name;
            }
            else
            {
                service.Dispose();
            }
        }
        if (_service is null) throw new UnsupportedSpeakerException("This Edifier device has no supported MR control service.");

        _notify = await GetCharacteristicAsync(_service, EdifierProtocol.NotifyCharacteristic, ct).ConfigureAwait(false);
        _write = await GetCharacteristicAsync(_service, EdifierProtocol.WriteCharacteristic, ct).ConfigureAwait(false);
        _writeOption = _write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        _notify.ValueChanged += OnNotification;
        var subscribed = await _notify.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct).ConfigureAwait(false);
        if (subscribed != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Could not subscribe to speaker updates ({subscribed}).");

        _log($"Connected to {ModelName} ({FormatAddress(address)}), write mode {_writeOption}.");
    }

    private static async Task<GattCharacteristic> GetCharacteristicAsync(GattDeviceService service, Guid uuid, CancellationToken ct)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
            throw new InvalidOperationException($"Control characteristic {uuid} not available ({result.Status}).");
        return result.Characteristics[0];
    }

    /// <summary>Runs while connected: polls the volume and sends pending volume requests.</summary>
    private async Task PumpAsync(CancellationToken ct)
    {
        Interlocked.Exchange(ref _lastRxTicks, Environment.TickCount64);
        var nextPoll = 0L;
        var nextModePoll = 0L;
        var askedForInfo = false;
        while (!ct.IsCancellationRequested)
        {
            if (_linkSuspect)
            {
                // Windows said "disconnected". Believe it only if it lasts.
                if (_device?.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    _linkSuspect = false;
                    _log("Windows briefly reported the link as down; it is still up.");
                }
                else if (Environment.TickCount64 - Interlocked.Read(ref _suspectSince) > BlipGraceMs)
                {
                    _linkLost = true;
                    return;
                }
                else
                {
                    await _kick.WaitAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
                    continue;
                }
            }

            var wait = nextPoll - Environment.TickCount64;
            if (wait > 0) await _kick.WaitAsync(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || _linkSuspect) continue;

            var target = Interlocked.Exchange(ref _pendingSet, -1);
            if (target >= 0)
            {
                Interlocked.Exchange(ref _lastSetAt, Environment.TickCount64);
                await WriteAsync(EdifierProtocol.SetVolume(target, Volume?.Max ?? EdifierProtocol.DefaultMaxVolume), ct)
                    .ConfigureAwait(false);
                // The speaker pushes its new level after a set; poll again soon regardless.
                nextPoll = Environment.TickCount64 + 250;
                continue;
            }

            while (_outbox.TryDequeue(out var queued))
            {
                await WriteAsync(queued, ct).ConfigureAwait(false);
                nextModePoll = Environment.TickCount64 + 400; // read the result back soon
            }

            if (!askedForInfo)
            {
                askedForInfo = true;
                await WriteAsync(EdifierProtocol.BuildCommand(EdifierProtocol.CmdGetFirmware), ct).ConfigureAwait(false);
                await WriteAsync(EdifierProtocol.BuildCommand(EdifierProtocol.CmdGetModelName), ct).ConfigureAwait(false);
            }

            // The sound mode changes rarely (button on the speaker, the phone app): every 5 s.
            if (Environment.TickCount64 >= nextModePoll)
            {
                await WriteAsync(EdifierProtocol.BuildCommand(EdifierProtocol.CmdGetSoundMode), ct).ConfigureAwait(false);
                nextModePoll = Environment.TickCount64 + 5000;
            }

            if (Environment.TickCount64 >= nextPoll)
            {
                await WriteAsync(EdifierProtocol.GetVolume(), ct).ConfigureAwait(false);
                nextPoll = Environment.TickCount64 + (long)PollInterval.TotalMilliseconds;
            }

            if (Environment.TickCount64 - Interlocked.Read(ref _lastRxTicks) > SilenceTimeout.TotalMilliseconds)
                throw new TimeoutException("The speaker stopped answering.");
        }
    }

    private async Task WriteAsync(byte[] packet, CancellationToken ct)
    {
        var status = await _write!.WriteValueAsync(packet.AsBuffer(), _writeOption).AsTask(ct).ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            throw new IOException($"Write to speaker failed ({status}).");
    }

    private void OnNotification(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        Interlocked.Exchange(ref _lastRxTicks, Environment.TickCount64);
        foreach (var frame in EdifierProtocol.ParseFrames(args.CharacteristicValue.ToArray()))
        {
            if (EdifierProtocol.TryDecodeVolume(frame, out var level))
            {
                VolumeLevel? previous;
                lock (_gate)
                {
                    previous = _volume;
                    _volume = level;
                }
                if (previous == level) continue;
                var ownEcho = Environment.TickCount64 - Interlocked.Read(ref _lastSetAt) < OwnChangeWindowMs;
                VolumeChanged?.Invoke(this, new VolumeChange(level,
                    FromSpeaker: previous is not null && !ownEcho,
                    IsFirstReading: previous is null));
            }
            else if (EdifierProtocol.TryDecodeSoundMode(frame, out var sound))
            {
                bool changed;
                lock (_gate)
                {
                    changed = _sound is null || !_sound.Equals(sound);
                    _sound = sound;
                }
                if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (EdifierProtocol.TryDecodeFirmware(frame, out var firmware))
            {
                lock (_gate) _firmware = firmware;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (EdifierProtocol.TryDecodeModelName(frame, out var name))
            {
                lock (_gate) _deviceName = name;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Interlocked.Exchange(ref _suspectSince, Environment.TickCount64);
            _linkSuspect = true;
        }
        else
        {
            _linkSuspect = false;
        }
        Kick();
    }

    private async Task TearDownAsync()
    {
        Interlocked.Exchange(ref _pendingSet, -1);
        while (_outbox.TryDequeue(out _)) { } // a mode switch for a link that's gone is stale
        lock (_gate)
        {
            _volume = null;
            _sound = null;
            _connectedSince = null;
        }

        var notify = _notify;
        if (notify is not null)
        {
            notify.ValueChanged -= OnNotification;
            if (!_linkLost)
            {
                try
                {
                    await notify.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort; the link is going away anyway.
                }
            }
        }
        if (_device is not null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _service?.Dispose();
        if (_session is not null)
        {
            _session.MaintainConnection = false;
            _session.Dispose();
        }
        _device?.Dispose();
        _notify = null;
        _write = null;
        _service = null;
        _session = null;
        _device = null;
    }

    private void SetState(SpeakerState state, string? detail)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != state || _detail != detail;
            _state = state;
            _detail = detail;
        }
        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}

/// <summary>The device answered but has none of the known MR control services.</summary>
public sealed class UnsupportedSpeakerException(string message) : Exception(message);
