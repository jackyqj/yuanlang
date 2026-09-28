namespace Press.Service;

using Press.Judge;
using Press.Service.Application;

/// <summary>
/// When PRESS_DEMO=1 (default in Development), auto load a job and run one cycle for smoke verification.
/// </summary>
internal sealed class DemoCycleHostedService(
    MachineRuntime runtime,
    IHostEnvironment env,
    ILogger<DemoCycleHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = Environment.GetEnvironmentVariable("PRESS_DEMO");
        var runDemo = enabled is "1" or "true"
                      || (enabled is null && env.IsDevelopment());
        if (!runDemo) return;

        try
        {
            await Task.Delay(500, stoppingToken);
            log.LogInformation("Demo: loading job…");
            await runtime.LoadJobAsync(new RuntimeJob(
                "demo", "CPK", "rv-demo-1", "J1", "PCB-DEMO", "CN-DEMO",
                new RecipeJudgementInput(
                    "sim-1", 50, 5000, 80, 36.0, 0.8, 1.0,
                    new PvfsInput(25, 37.2, 0.3, false),
                    StopAngleDeg: null,
                    Envelope: null,
                    HoldDelayS: 0.15),
                ApproachSpeedMmS: 20,
                PressSpeedMmS: 3,
                RetractSpeedMmS: 25,
                WorkOriginMm: 40,
                HoldDelayS: 0.15), stoppingToken);

            log.LogInformation("Demo: starting cycle…");
            await runtime.StartCycleAsync(stoppingToken);

            for (var i = 0; i < 600 && !stoppingToken.IsCancellationRequested; i++)
            {
                var snap = await runtime.GetSnapshotAsync(stoppingToken);
                if (snap.State == RuntimeState.Ready && snap.Metrics.MaxForceN > 0 && i > 20)
                {
                    log.LogInformation(
                        "Demo finished: state={State} maxF={MaxF:F1}N pos={Pos:F3}mm",
                        snap.State, snap.Metrics.MaxForceN, snap.Metrics.PositionMm);
                    break;
                }
                if (snap.State == RuntimeState.Faulted && i > 5)
                {
                    log.LogWarning("Demo finished in Faulted state");
                    break;
                }
                await Task.Delay(100, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Demo cycle failed");
        }
    }
}
