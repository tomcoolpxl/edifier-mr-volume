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
/// Keeps one BLE control connection to an MR4.5 open: finds it, connects, polls the volume,
/// applies volume requests and reconnects when the link drops. Events are raised on a
/// background thread.
/// </summary>
public sealed class SpeakerConnection : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(5);

    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _kick = new(0);
    private readonly HashSet<ulong> _ignored = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _pendingSet = -1;

    private SpeakerState _state = SpeakerState.Stopped;
    private VolumeLevel? _volume;
    private string? _detail;

    // Live link. Only the loop task creates and tears these down.
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private GattDeviceService? _service;
    private GattCharacteristic? _notify;
    private GattCharacteristic? _write;
    private GattWriteOption _writeOption;
    private volatile bool _linkLost;
    private long _lastRxTicks;

    public SpeakerConnection(Action<string>? log = null) => _log = log ?? (_ => { });

    public event EventHandler? StateChanged;
    public event EventHandler<VolumeLevel>? VolumeChanged;

    public SpeakerState State { get { lock (_gate) return _state; } }

    /// <summary>Last volume the speaker reported, or null before the first reply.</summary>
    public VolumeLevel? Volume { get { lock (_gate) return _volume; } }

    /// <summary>Human-readable reason for the current state (last error), if any.</summary>
    public string? Detail { get { lock (_gate) return _detail; } }

    public string? ModelName { get; private set; }

    public bool IsRunning { get { lock (_gate) return _cts is not null; } }

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
        Interlocked.Exchange(ref _pendingSet, Math.Clamp(level, 0, max));
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
                var address = await FindAsync(_ignored.ToHashSet(), ct).ConfigureAwait(false);
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
                lock (_gate) _detail = ex.Message;
            }
            finally
            {
                await TearDownAsync().ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested) break;
            var delay = TimeSpan.FromSeconds(Math.Min(30, 2 * Math.Max(1, failures)));
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
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
        while (!ct.IsCancellationRequested && !_linkLost)
        {
            var wait = nextPoll - Environment.TickCount64;
            if (wait > 0) await _kick.WaitAsync(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false);
            if (_linkLost || ct.IsCancellationRequested) break;

            var target = Interlocked.Exchange(ref _pendingSet, -1);
            if (target >= 0)
            {
                await WriteAsync(EdifierProtocol.SetVolume(target, Volume?.Max ?? EdifierProtocol.DefaultMaxVolume), ct)
                    .ConfigureAwait(false);
                // The speaker pushes its new level after a set; poll again soon regardless.
                nextPoll = Environment.TickCount64 + 250;
                continue;
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
            if (!EdifierProtocol.TryDecodeVolume(frame, out var level)) continue;
            bool changed;
            lock (_gate)
            {
                changed = _volume != level;
                _volume = level;
            }
            if (changed) VolumeChanged?.Invoke(this, level);
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
        _linkLost = true;
        Kick();
    }

    private async Task TearDownAsync()
    {
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
