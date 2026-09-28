namespace Press.Adapters.Abstractions;

/// <summary>
/// Device adapters are the only process boundary that talks vendor SDKs / buses.
/// Units: force=N, length=mm, speed=mm/s. Clock is service-owned.
/// </summary>

public interface IMotionDevice : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task EnableAsync(bool enabled, CancellationToken ct);
    Task HomeAsync(bool hardwareHome, CancellationToken ct);
    Task MoveAbsoluteAsync(double positionMm, double speedMmS, CancellationToken ct);
    Task SetVelocityProfileAsync(VelocityProfile profile, CancellationToken ct);
    Task StopAsync(StopMode mode, CancellationToken ct);
    Task<AxisStatus> GetStatusAsync(CancellationToken ct);
}

public interface IForceDevice : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task StartAcquireAsync(double sampleRateHz, CancellationToken ct);
    Task StopAcquireAsync(CancellationToken ct);
    Task ZeroAsync(CancellationToken ct);
    IAsyncEnumerable<ForceSample> Samples { get; }
    Task<ForceSample> GetLatestAsync(CancellationToken ct);
}

public interface IDigitalIoDevice : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task<IoSnapshot> ReadAsync(CancellationToken ct);
    Task WriteOutputsAsync(IReadOnlyDictionary<string, bool> outputs, CancellationToken ct);
    /// <summary>Safety inputs are read-only; hard E-stop is not software-cleared.</summary>
    Task<SafetyStatus> GetSafetyStatusAsync(CancellationToken ct);
}

public interface IBarcodeDevice : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    IAsyncEnumerable<BarcodeScan> Scans { get; }
}

public interface ICalibratorDevice : IAsyncDisposable
{
    Task ConnectAsync(string port, int baud, CancellationToken ct);
    Task<double> ReadForceNAsync(CancellationToken ct);
}

public interface IMesPublisher
{
    string AdapterName { get; }
    Task PublishAsync(CycleResultMessage message, CancellationToken ct);
}

public enum StopMode
{
    Controlled,
    QuickStop,
    Disable
}

public sealed record VelocityProfile(
    double V1, double V2, double V3, double V4, double V5,
    double UpPercent, double DownPercent);

public sealed record AxisStatus(
    double PositionMm,
    double SpeedMmS,
    bool Enabled,
    bool AtHome,
    bool SoftLimit,
    bool HardLimit);

public sealed record ForceSample(double TimeS, double ForceN, double PositionMm);

public sealed record IoSnapshot(
    IReadOnlyDictionary<string, bool> Inputs,
    IReadOnlyDictionary<string, bool> Outputs);

public sealed record SafetyStatus(
    bool EStop,
    bool LightCurtainOk,
    bool DoorClosed,
    bool StartEnabled);

public sealed record BarcodeScan(string Code, DateTimeOffset Time);

public sealed record CycleResultMessage(
    string IdempotencyKey,
    string PayloadJson);
