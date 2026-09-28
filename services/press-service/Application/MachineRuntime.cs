namespace Press.Service.Application;

using System.Threading.Channels;
using Press.Adapters.Abstractions;
using Press.Adapters.Simulation;
using Press.Judge;
using Press.Service.Infrastructure;

public enum RuntimeState
{
    Offline,
    Idle,
    Loading,
    Ready,
    Approaching,
    Pressing,
    Holding,
    Retracting,
    Completed,
    Faulted,
    Estop
}

public sealed record RuntimeJob(
    string ProductId,
    string ProductName,
    string RecipeVersionId,
    string ConnectorName,
    string PcbBarcode,
    string ConnectorBarcode,
    RecipeJudgementInput Recipe,
    double ApproachSpeedMmS,
    double PressSpeedMmS,
    double RetractSpeedMmS,
    double WorkOriginMm,
    double HoldDelayS);

public sealed record RuntimeMetrics(
    double ForceN,
    double PositionMm,
    double SpeedMmS,
    double MaxForceN,
    double MaxPositionMm,
    double CycleElapsedS);

public sealed record RuntimeAlarm(string AlarmId, string Code, string Message, DateTimeOffset RaisedAt, bool Acknowledged);

public sealed record RuntimeSnapshot(
    RuntimeState State,
    RuntimeJob? Job,
    RuntimeMetrics Metrics,
    AxisStatus Axis,
    SafetyStatus Safety,
    IReadOnlyList<RuntimeAlarm> Alarms,
    string? ActiveCycleId,
    string SoftwareVersion);

public abstract record RuntimeEvent
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SnapshotEvent(RuntimeSnapshot Snapshot) : RuntimeEvent;
public sealed record StateEvent(RuntimeState State) : RuntimeEvent;
public sealed record MetricsEvent(RuntimeMetrics Metrics) : RuntimeEvent;
public sealed record CurveBatchEvent(string CycleId, IReadOnlyList<SamplePoint> Samples) : RuntimeEvent;
public sealed record AlarmRaisedEvent(RuntimeAlarm Alarm) : RuntimeEvent;
public sealed record CycleCompletedEvent(
    string CycleId,
    bool Pass,
    IReadOnlyList<string> FailCodes,
    double EndForceN,
    double EndPositionMm,
    double CycleTimeS) : RuntimeEvent;

public sealed class MachineRuntime : IAsyncDisposable
{
    private readonly SimulatedPressHardware _hw;
    private readonly IJudgeEngine _judge;
    private readonly IPressStore _store;
    private readonly IMesOutbox _mesOutbox;
    private readonly ILogger<MachineRuntime> _log;
    private readonly object _gate = new();
    private readonly List<Channel<RuntimeEvent>> _subscribers = [];
    private readonly List<SamplePoint> _cycleSamples = [];
    private readonly List<RuntimeAlarm> _alarms = [];

    private RuntimeState _state = RuntimeState.Idle;
    private RuntimeJob? _job;
    private RuntimeMetrics _metrics = new(0, 40, 0, 0, 40, 0);
    private string? _cycleId;
    private DateTimeOffset _cycleStart;
    private CancellationTokenSource? _cycleCts;
    private Task? _cycleTask;

    public string SoftwareVersion { get; } = "0.1.0-sim";

    public MachineRuntime(
        SimulatedPressHardware hw,
        IJudgeEngine judge,
        IPressStore store,
        IMesOutbox mesOutbox,
        ILogger<MachineRuntime> log)
    {
        _hw = hw;
        _judge = judge;
        _store = store;
        _mesOutbox = mesOutbox;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _hw.ConnectAsync(ct);
        await _hw.EnableAsync(true, ct);
        await _hw.HomeAsync(hardwareHome: false, ct);
        Publish(new SnapshotEvent(await GetSnapshotAsync(ct)));
    }

    public async ValueTask DisposeAsync()
    {
        _cycleCts?.Cancel();
        await _hw.DisposeAsync();
    }

    public ChannelReader<RuntimeEvent> Subscribe()
    {
        var ch = Channel.CreateUnbounded<RuntimeEvent>();
        lock (_gate) { _subscribers.Add(ch); }
        _ = Task.Run(async () =>
        {
            try { ch.Writer.TryWrite(new SnapshotEvent(await GetSnapshotAsync(CancellationToken.None))); }
            catch { /* ignore */ }
        });
        return ch.Reader;
    }

    public async Task<RuntimeSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var axis = await _hw.GetStatusAsync(ct);
        var safety = await _hw.GetSafetyStatusAsync(ct);
        lock (_gate)
        {
            return new RuntimeSnapshot(_state, _job, _metrics, axis, safety, _alarms.ToList(), _cycleId, SoftwareVersion);
        }
    }

    public async Task LoadJobAsync(RuntimeJob job, CancellationToken ct)
    {
        EnsureNotBusy();
        SetState(RuntimeState.Loading);
        _hw.ContactPositionMm = job.WorkOriginMm - 2.0;
        _hw.EndPositionMm = job.Recipe.EndPositionMm;
        _hw.PeakForceN = Math.Max(job.Recipe.MaxForceN * 0.3, job.Recipe.MinForceN * 2);
        lock (_gate) { _job = job; }
        await _store.UpsertRecipeAsync(job, ct);
        await _hw.MoveAbsoluteAsync(job.WorkOriginMm, job.ApproachSpeedMmS, ct);
        await WaitNearAsync(job.WorkOriginMm, ct);
        SetState(RuntimeState.Ready);
        Publish(new SnapshotEvent(await GetSnapshotAsync(ct)));
    }

    public Task<string> StartCycleAsync(CancellationToken ct)
    {
        RuntimeJob job;
        lock (_gate)
        {
            if (_state is not RuntimeState.Ready and not RuntimeState.Completed and not RuntimeState.Idle)
            {
                throw new InvalidOperationException($"Cannot start from state {_state}");
            }
            job = _job ?? throw new InvalidOperationException("No job loaded.");
            if (_cycleTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Cycle already running.");
            }
            _cycleId = Guid.NewGuid().ToString("N");
            _cycleSamples.Clear();
            _cycleStart = DateTimeOffset.UtcNow;
            _cycleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var cycleId = _cycleId;
            _cycleTask = Task.Run(() => RunCycleAsync(job, cycleId, _cycleCts.Token));
            return Task.FromResult(cycleId);
        }
    }

    public async Task AbortAsync(string reason, CancellationToken ct)
    {
        _cycleCts?.Cancel();
        await _hw.StopAsync(StopMode.QuickStop, ct);
        RaiseAlarm("ABORT", reason);
        SetState(RuntimeState.Faulted);
        Publish(new SnapshotEvent(await GetSnapshotAsync(ct)));
    }

    public async Task ResetFaultAsync(CancellationToken ct)
    {
        lock (_gate) { _alarms.RemoveAll(a => a.Acknowledged); }
        if (_job is null) SetState(RuntimeState.Idle);
        else SetState(RuntimeState.Ready);
        Publish(new SnapshotEvent(await GetSnapshotAsync(ct)));
    }

    private async Task RunCycleAsync(RuntimeJob job, string cycleId, CancellationToken ct)
    {
        try
        {
            await _hw.StartAcquireAsync(100, ct);
            SetState(RuntimeState.Approaching);

            // Approach to just above contact
            var approachPos = _hw.ContactPositionMm + 0.5;
            await _hw.MoveAbsoluteAsync(approachPos, job.ApproachSpeedMmS, ct);
            await ConsumeSamplesUntilAsync(job, cycleId, async token =>
            {
                var st = await _hw.GetStatusAsync(token);
                return Math.Abs(st.PositionMm - approachPos) < 0.08;
            }, ct);

            SetState(RuntimeState.Pressing);
            await _hw.MoveAbsoluteAsync(job.Recipe.EndPositionMm, job.PressSpeedMmS, ct);

            var stop = false;
            await ConsumeSamplesUntilAsync(job, cycleId, async token =>
            {
                List<SamplePoint> copy;
                lock (_gate) { copy = _cycleSamples.ToList(); }
                var live = _judge.EvaluateLive(copy, job.Recipe);
                if (live.ShouldStop)
                {
                    stop = true;
                    return true;
                }
                var st = await _hw.GetStatusAsync(token);
                return Math.Abs(st.PositionMm - job.Recipe.EndPositionMm) < 0.08;
            }, ct);

            if (stop)
            {
                await _hw.StopAsync(StopMode.Controlled, ct);
            }

            if (job.HoldDelayS > 0)
            {
                SetState(RuntimeState.Holding);
                await Task.Delay(TimeSpan.FromSeconds(job.HoldDelayS), ct);
            }

            List<SamplePoint> pressSamples;
            lock (_gate) { pressSamples = _cycleSamples.ToList(); }
            var judgement = _judge.FinalizeCycle(pressSamples, job.Recipe);

            SetState(RuntimeState.Retracting);
            await _hw.MoveAbsoluteAsync(job.WorkOriginMm, job.RetractSpeedMmS, ct);
            await WaitNearAsync(job.WorkOriginMm, ct);

            await _hw.StopAcquireAsync(ct);
            await DrainSamplesAsync();

            var elapsed = (DateTimeOffset.UtcNow - _cycleStart).TotalSeconds;
            var maxForce = pressSamples.Count == 0 ? 0 : pressSamples.Max(s => s.ForceN);

            try
            {
                var saved = await _store.SaveCycleReturningAsync(new CycleRecord(
                    cycleId,
                    job.ProductName,
                    job.PcbBarcode,
                    job.ConnectorBarcode,
                    job.ConnectorName,
                    job.RecipeVersionId,
                    judgement.Pass ? "pass" : "fail",
                    judgement.EndForceN,
                    judgement.EndPositionMm,
                    maxForce,
                    elapsed,
                    string.Join(',', judgement.FailCodes),
                    CurvePath: "",
                    SoftwareVersion,
                    OperatorName: "hmi",
                    DateTimeOffset.UtcNow), pressSamples, CancellationToken.None);

                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    idempotencyKey = saved.CycleId,
                    productName = saved.ProductName,
                    productSN = saved.ProductSn,
                    connectBar = saved.ConnectBar,
                    connectName = saved.ConnectName,
                    recipeVersionId = saved.RecipeVersionId,
                    pressMax = saved.MaxForceN,
                    pressSet = judgement.EndForceN,
                    position = saved.EndPositionMm,
                    operatorName = saved.OperatorName,
                    time = saved.CreatedAt,
                    saveDrwName = saved.CurvePath,
                    softwareVersion = saved.SoftwareVersion,
                    result = saved.Result,
                    alarmCodes = judgement.FailCodes.ToArray(),
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                });
                await _mesOutbox.EnqueueAsync(saved.CycleId, payload, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to persist/enqueue cycle {CycleId}", cycleId);
            }

            SetState(RuntimeState.Completed);
            Publish(new CycleCompletedEvent(
                cycleId, judgement.Pass, judgement.FailCodes.ToList(),
                judgement.EndForceN, judgement.EndPositionMm, elapsed));
            Publish(new SnapshotEvent(await GetSnapshotAsync(CancellationToken.None)));

            _log.LogInformation(
                "Cycle {CycleId} {Result} endF={Force:F1}N endS={Pos:F3}mm t={Time:F2}s codes={Codes}",
                cycleId, judgement.Pass ? "PASS" : "FAIL",
                judgement.EndForceN, judgement.EndPositionMm, elapsed,
                string.Join(',', judgement.FailCodes));

            SetState(RuntimeState.Ready);
        }
        catch (OperationCanceledException)
        {
            await _hw.StopAcquireAsync(CancellationToken.None);
            SetState(RuntimeState.Faulted);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Cycle failed");
            RaiseAlarm("CYCLE_ERROR", ex.Message);
            SetState(RuntimeState.Faulted);
            try { await _hw.StopAcquireAsync(CancellationToken.None); } catch { /* ignore */ }
        }
    }

    private async Task DrainSamplesAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            await foreach (var _ in _hw.Samples.WithCancellation(cts.Token))
            {
                // discard late samples so the next cycle starts clean
            }
        }
        catch (OperationCanceledException)
        {
            // expected — short drain window
        }
    }

    private async Task ConsumeSamplesUntilAsync(
        RuntimeJob job,
        string cycleId,
        Func<CancellationToken, Task<bool>> done,
        CancellationToken ct)
    {
        var batch = new List<SamplePoint>();
        await foreach (var sample in _hw.Samples.WithCancellation(ct))
        {
            var point = new SamplePoint(sample.TimeS, sample.ForceN, sample.PositionMm);
            lock (_gate)
            {
                _cycleSamples.Add(point);
                var maxF = Math.Max(_metrics.MaxForceN, sample.ForceN);
                // Position decreases during press; track min as "max travel" display uses absolute metrics field loosely
                var maxP = Math.Max(_metrics.MaxPositionMm, sample.PositionMm);
                _metrics = new RuntimeMetrics(
                    sample.ForceN, sample.PositionMm, 0, maxF, maxP,
                    (DateTimeOffset.UtcNow - _cycleStart).TotalSeconds);
            }
            batch.Add(point);
            if (batch.Count >= 5)
            {
                Publish(new CurveBatchEvent(cycleId, batch.ToList()));
                Publish(new MetricsEvent(_metrics));
                batch.Clear();
            }
            if (await done(ct)) break;
        }
        if (batch.Count > 0)
        {
            Publish(new CurveBatchEvent(cycleId, batch.ToList()));
        }
    }

    private async Task WaitNearAsync(double positionMm, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (await PositionNearAsync(positionMm, ct)) return;
            await Task.Delay(20, ct);
        }
    }

    private async Task<bool> PositionNearAsync(double positionMm, CancellationToken ct)
    {
        var status = await _hw.GetStatusAsync(ct);
        return Math.Abs(status.PositionMm - positionMm) < 0.05;
    }

    private void EnsureNotBusy()
    {
        lock (_gate)
        {
            if (_state is RuntimeState.Approaching or RuntimeState.Pressing or RuntimeState.Holding or RuntimeState.Retracting)
            {
                throw new InvalidOperationException("Busy");
            }
        }
    }

    private void SetState(RuntimeState state)
    {
        lock (_gate) { _state = state; }
        Publish(new StateEvent(state));
    }

    private void RaiseAlarm(string code, string message)
    {
        var alarm = new RuntimeAlarm(Guid.NewGuid().ToString("N"), code, message, DateTimeOffset.UtcNow, false);
        lock (_gate) { _alarms.Add(alarm); }
        Publish(new AlarmRaisedEvent(alarm));
    }

    private void Publish(RuntimeEvent evt)
    {
        List<Channel<RuntimeEvent>> subs;
        lock (_gate) { subs = _subscribers.ToList(); }
        foreach (var ch in subs)
        {
            if (!ch.Writer.TryWrite(evt))
            {
                lock (_gate) { _subscribers.Remove(ch); }
            }
        }
    }
}
