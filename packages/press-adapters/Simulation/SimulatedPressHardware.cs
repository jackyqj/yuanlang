namespace Press.Adapters.Simulation;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Press.Adapters.Abstractions;

/// <summary>In-process simulated press axis + force curve for CI / no-hardware runs.</summary>
public sealed class SimulatedPressHardware : IMotionDevice, IForceDevice, IDigitalIoDevice, IBarcodeDevice
{
    private readonly object _gate = new();
    private readonly Channel<ForceSample> _samples = Channel.CreateUnbounded<ForceSample>();
    private readonly Channel<BarcodeScan> _scans = Channel.CreateUnbounded<BarcodeScan>();

    private bool _enabled;
    private bool _acquiring;
    private double _positionMm = 40.0;
    private double _speedMmS;
    private double _targetMm = 40.0;
    private double _commandSpeedMmS = 5.0;
    private double _timeS;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public double ContactPositionMm { get; set; } = 38.0;
    public double EndPositionMm { get; set; } = 36.0;
    public double PeakForceN { get; set; } = 1500;

    public IAsyncEnumerable<ForceSample> Samples => ReadChannel(_samples.Reader);
    public IAsyncEnumerable<BarcodeScan> Scans => ReadChannel(_scans.Reader);

    public Task ConnectAsync(CancellationToken ct)
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();
        return ValueTask.CompletedTask;
    }

    public Task EnableAsync(bool enabled, CancellationToken ct)
    {
        lock (_gate) { _enabled = enabled; }
        return Task.CompletedTask;
    }

    public Task HomeAsync(bool hardwareHome, CancellationToken ct)
    {
        lock (_gate)
        {
            _positionMm = hardwareHome ? 45.0 : 40.0;
            _targetMm = _positionMm;
            _speedMmS = 0;
        }
        return Task.CompletedTask;
    }

    public Task MoveAbsoluteAsync(double positionMm, double speedMmS, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_enabled) throw new InvalidOperationException("Axis not enabled.");
            _targetMm = positionMm;
            _commandSpeedMmS = Math.Max(0.01, speedMmS);
        }
        return Task.CompletedTask;
    }

    public Task SetVelocityProfileAsync(VelocityProfile profile, CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(StopMode mode, CancellationToken ct)
    {
        lock (_gate)
        {
            _targetMm = _positionMm;
            _speedMmS = 0;
        }
        return Task.CompletedTask;
    }

    public Task<AxisStatus> GetStatusAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(new AxisStatus(
                _positionMm, _speedMmS, _enabled,
                AtHome: Math.Abs(_positionMm - 45.0) < 0.05,
                SoftLimit: false, HardLimit: false));
        }
    }

    public Task StartAcquireAsync(double sampleRateHz, CancellationToken ct)
    {
        lock (_gate) { _acquiring = true; }
        return Task.CompletedTask;
    }

    public Task StopAcquireAsync(CancellationToken ct)
    {
        lock (_gate) { _acquiring = false; }
        return Task.CompletedTask;
    }

    public Task ZeroAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<ForceSample> GetLatestAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(new ForceSample(_timeS, ForceAt(_positionMm), _positionMm));
        }
    }

    public Task<IoSnapshot> ReadAsync(CancellationToken ct) =>
        Task.FromResult(new IoSnapshot(
            new Dictionary<string, bool> { ["start"] = true },
            new Dictionary<string, bool>()));

    public Task WriteOutputsAsync(IReadOnlyDictionary<string, bool> outputs, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<SafetyStatus> GetSafetyStatusAsync(CancellationToken ct) =>
        Task.FromResult(new SafetyStatus(EStop: false, LightCurtainOk: true, DoorClosed: true, StartEnabled: true));

    public void InjectBarcode(string code) =>
        _scans.Writer.TryWrite(new BarcodeScan(code, DateTimeOffset.UtcNow));

    private async Task LoopAsync(CancellationToken ct)
    {
        const double dt = 0.01; // 100 Hz sim tick
        while (!ct.IsCancellationRequested)
        {
            ForceSample? sample = null;
            lock (_gate)
            {
                _timeS += dt;
                var err = _targetMm - _positionMm;
                if (Math.Abs(err) < 0.001 || !_enabled)
                {
                    _speedMmS = 0;
                    _positionMm = _targetMm;
                }
                else
                {
                    var step = Math.Sign(err) * Math.Min(Math.Abs(err), _commandSpeedMmS * dt);
                    _positionMm += step;
                    _speedMmS = step / dt;
                }

                if (_acquiring)
                {
                    sample = new ForceSample(_timeS, ForceAt(_positionMm), _positionMm);
                }
            }

            if (sample is not null)
            {
                _samples.Writer.TryWrite(sample);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(dt), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private double ForceAt(double positionMm)
    {
        if (positionMm >= ContactPositionMm) return 0;
        var travel = ContactPositionMm - positionMm;
        var total = Math.Max(0.001, ContactPositionMm - EndPositionMm);
        var x = Math.Clamp(travel / total, 0, 1.2);
        // Characteristic press-fit hump then rise
        var hump = PeakForceN * 0.55 * Math.Sin(Math.PI * Math.Min(x, 1.0));
        var endRise = PeakForceN * Math.Pow(Math.Max(0, x - 0.65) / 0.35, 2);
        return Math.Max(0, hump + endRise);
    }

    private static async IAsyncEnumerable<T> ReadChannel<T>(
        ChannelReader<T> reader,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            yield return item;
        }
    }
}
