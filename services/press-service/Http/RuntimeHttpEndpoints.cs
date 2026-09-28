namespace Press.Service.Http;

using Press.Judge;
using Press.Service.Application;

public static class RuntimeHttpEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    public static void MapRuntimeHttp(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/runtime");

        api.MapGet("/snapshot", async (MachineRuntime runtime, CancellationToken ct) =>
        {
            var snap = await runtime.GetSnapshotAsync(ct);
            return Results.Json(ToDto(snap));
        });

        api.MapPost("/jobs", async (LoadJobBody body, MachineRuntime runtime, CancellationToken ct) =>
        {
            var recipe = new RecipeJudgementInput(
                AlgorithmVersion: "sim-1",
                ContactForceN: body.ContactForceN ?? 50,
                MaxForceN: body.MaxForceN ?? 5000,
                MinForceN: body.MinForceN ?? 100,
                EndPositionMm: body.EndPositionMm ?? 36.0,
                EndPosTolMm: body.EndPosTolMm ?? 0.5,
                CheckTolMm: body.CheckTolMm ?? 1.0,
                Pvfs: new PvfsInput(body.PvfsPercent ?? 25, body.PvfsStartMm ?? 37.0, body.PvfsDistanceMm ?? 0.2, false),
                StopAngleDeg: body.StopAngleDeg,
                Envelope: null,
                HoldDelayS: body.HoldDelayS ?? 0.2);

            var job = new RuntimeJob(
                ProductId: body.ProductId ?? "demo",
                ProductName: body.ProductName ?? "CPK",
                RecipeVersionId: body.RecipeVersionId ?? "rv-demo-1",
                ConnectorName: body.ConnectorName ?? "J1",
                PcbBarcode: body.PcbBarcode ?? "PCB-001",
                ConnectorBarcode: body.ConnectorBarcode ?? "CN-001",
                Recipe: recipe,
                ApproachSpeedMmS: body.ApproachSpeedMmS ?? 15,
                PressSpeedMmS: body.PressSpeedMmS ?? 2,
                RetractSpeedMmS: body.RetractSpeedMmS ?? 20,
                WorkOriginMm: body.WorkOriginMm ?? 40,
                HoldDelayS: recipe.HoldDelayS);

            await runtime.LoadJobAsync(job, ct);
            return Results.Json(ToDto(await runtime.GetSnapshotAsync(ct)));
        });

        api.MapPost("/cycles/start", async (MachineRuntime runtime, CancellationToken ct) =>
        {
            var cycleId = await runtime.StartCycleAsync(ct);
            return Results.Json(new { ok = true, cycleId });
        });

        api.MapPost("/abort", async (AbortBody body, MachineRuntime runtime, CancellationToken ct) =>
        {
            await runtime.AbortAsync(body.Reason ?? "operator abort", ct);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/reset", async (MachineRuntime runtime, CancellationToken ct) =>
        {
            await runtime.ResetFaultAsync(ct);
            return Results.Json(ToDto(await runtime.GetSnapshotAsync(ct)));
        });

        api.MapGet("/events", async (HttpContext http, MachineRuntime runtime, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            var reader = runtime.Subscribe();
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                object payload = evt switch
                {
                    SnapshotEvent s => new { type = "snapshot", data = (object)ToDto(s.Snapshot) },
                    StateEvent s => new { type = "state", data = (object)new { state = s.State.ToString() } },
                    MetricsEvent m => new { type = "metrics", data = (object)m.Metrics },
                    CurveBatchEvent c => new { type = "curve", data = (object)new { c.CycleId, count = c.Samples.Count, c.Samples } },
                    AlarmRaisedEvent a => new { type = "alarm", data = (object)a.Alarm },
                    CycleCompletedEvent c => new { type = "cycle_completed", data = (object)c },
                    _ => new { type = "unknown", data = (object)new { } }
                };
                await http.Response.WriteAsync(
                    $"data: {System.Text.Json.JsonSerializer.Serialize(payload, JsonOptions)}\n\n",
                    ct);
                await http.Response.Body.FlushAsync(ct);
            }
        });
    }

    private static object ToDto(RuntimeSnapshot s) => new
    {
        state = s.State.ToString(),
        job = s.Job is null ? null : new
        {
            s.Job.ProductId,
            s.Job.ProductName,
            s.Job.RecipeVersionId,
            s.Job.ConnectorName,
            s.Job.PcbBarcode
        },
        metrics = s.Metrics,
        axis = s.Axis,
        safety = s.Safety,
        alarms = s.Alarms,
        activeCycleId = s.ActiveCycleId,
        softwareVersion = s.SoftwareVersion
    };

    public sealed class LoadJobBody
    {
        public string? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? RecipeVersionId { get; set; }
        public string? ConnectorName { get; set; }
        public string? PcbBarcode { get; set; }
        public string? ConnectorBarcode { get; set; }
        public double? ContactForceN { get; set; }
        public double? MaxForceN { get; set; }
        public double? MinForceN { get; set; }
        public double? EndPositionMm { get; set; }
        public double? EndPosTolMm { get; set; }
        public double? CheckTolMm { get; set; }
        public double? PvfsPercent { get; set; }
        public double? PvfsStartMm { get; set; }
        public double? PvfsDistanceMm { get; set; }
        public double? StopAngleDeg { get; set; }
        public double? HoldDelayS { get; set; }
        public double? ApproachSpeedMmS { get; set; }
        public double? PressSpeedMmS { get; set; }
        public double? RetractSpeedMmS { get; set; }
        public double? WorkOriginMm { get; set; }
    }

    public sealed class AbortBody
    {
        public string? Reason { get; set; }
    }
}
