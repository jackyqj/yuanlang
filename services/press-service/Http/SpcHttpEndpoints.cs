namespace Press.Service.Http;

using Press.Service.Application;
using Press.Service.Infrastructure;

public static class SpcHttpEndpoints
{
    public static void MapSpcHttp(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/spc");

        api.MapGet("/chart", async (
            string? product,
            string? connector,
            string? metric,
            double? usl,
            double? lsl,
            int? limit,
            IPressStore store,
            CancellationToken ct) =>
        {
            var cycles = (await store.QueryCyclesAsync(new CycleQuery(
                ProductName: product,
                ConnectName: connector,
                PassOnly: true,
                Limit: limit ?? 200), ct))
                .OrderBy(c => c.CreatedAt)
                .ToList();

            var chart = SpcCalculator.BuildChart(cycles, metric ?? "end_force", usl, lsl);
            return Results.Json(chart);
        });

        api.MapGet("/hourly", async (
            string? day,
            string? product,
            IPressStore store,
            CancellationToken ct) =>
        {
            var dayDt = string.IsNullOrWhiteSpace(day)
                ? DateTimeOffset.UtcNow
                : DateTimeOffset.Parse(day);

            var from = new DateTimeOffset(dayDt.Year, dayDt.Month, dayDt.Day, 0, 0, 0, TimeSpan.FromHours(8));
            var to = from.AddDays(1);

            var cycles = await store.QueryCyclesAsync(new CycleQuery(
                ProductName: product,
                From: from.ToUniversalTime(),
                To: to.ToUniversalTime(),
                Limit: 2000), ct);

            var buckets = SpcCalculator.HourlyThroughput(cycles, from);
            return Results.Json(new { day = from.ToString("yyyy-MM-dd"), buckets });
        });
    }
}
