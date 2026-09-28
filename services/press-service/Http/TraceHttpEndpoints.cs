namespace Press.Service.Http;

using Press.Service.Infrastructure;

public static class TraceHttpEndpoints
{
    public static void MapTraceHttp(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/trace");

        api.MapGet("/cycles", async (string? pcb, string? product, bool? errorsOnly, int? limit, IPressStore store, CancellationToken ct) =>
        {
            var rows = await store.QueryCyclesAsync(
                new CycleQuery(
                    PcbBarcode: pcb,
                    ProductName: product,
                    ErrorsOnly: errorsOnly ?? false,
                    Limit: limit ?? 50), ct);
            return Results.Json(rows);
        });

        api.MapGet("/cycles/{cycleId}", async (string cycleId, IPressStore store, CancellationToken ct) =>
        {
            var detail = await store.GetCycleAsync(cycleId, ct);
            return detail is null ? Results.NotFound() : Results.Json(detail);
        });
    }
}
