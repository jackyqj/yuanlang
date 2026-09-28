namespace Press.Service.Application;

using Press.Service.Infrastructure;

public static class SpcCalculator
{
    public sealed record Point(int Index, double Value, DateTimeOffset Time, string CycleId);

    public sealed record ChartResult(
        IReadOnlyList<Point> Points,
        double Mean,
        double Ucl,
        double Cl,
        double Lcl,
        double StdDev,
        double? Cpk,
        double? UsL,
        double? LsL,
        int SampleCount,
        string Metric);

    public sealed record HourBucket(string HourLabel, int Pcs);

    public static ChartResult BuildChart(
        IReadOnlyList<CycleRecord> cycles,
        string metric,
        double? usl,
        double? lsl)
    {
        var points = new List<Point>();
        for (var i = 0; i < cycles.Count; i++)
        {
            var c = cycles[i];
            var value = metric.ToLowerInvariant() switch
            {
                "max_force" or "maxforce" => c.MaxForceN,
                "position" or "end_position" => c.EndPositionMm,
                _ => c.EndForceN, // end_force default
            };
            points.Add(new Point(i + 1, value, c.CreatedAt, c.CycleId));
        }

        if (points.Count == 0)
        {
            return new ChartResult([], 0, 0, 0, 0, 0, null, usl, lsl, 0, metric);
        }

        var mean = points.Average(p => p.Value);
        var variance = points.Count == 1
            ? 0
            : points.Sum(p => Math.Pow(p.Value - mean, 2)) / (points.Count - 1);
        var std = Math.Sqrt(variance);
        var ucl = mean + 3 * std;
        var lcl = mean - 3 * std;

        double? cpk = null;
        if (usl is { } u && lsl is { } l && std > 1e-9)
        {
            var cpu = (u - mean) / (3 * std);
            var cpl = (mean - l) / (3 * std);
            cpk = Math.Min(cpu, cpl);
        }
        else if (usl is { } onlyU && std > 1e-9)
        {
            cpk = (onlyU - mean) / (3 * std);
        }
        else if (lsl is { } onlyL && std > 1e-9)
        {
            cpk = (mean - onlyL) / (3 * std);
        }

        return new ChartResult(points, mean, ucl, mean, lcl, std, cpk, usl, lsl, points.Count, metric);
    }

    public static IReadOnlyList<HourBucket> HourlyThroughput(IReadOnlyList<CycleRecord> cycles, DateTimeOffset day)
    {
        var localDay = day.ToOffset(TimeSpan.FromHours(8)); // Asia/Shanghai-ish display default
        var start = new DateTimeOffset(localDay.Year, localDay.Month, localDay.Day, 0, 0, 0, localDay.Offset);
        var buckets = Enumerable.Range(0, 24)
            .Select(h => new HourBucket($"{h:00}-{h + 1:00}h", 0))
            .ToArray();

        foreach (var c in cycles)
        {
            var t = c.CreatedAt.ToOffset(localDay.Offset);
            if (t < start || t >= start.AddDays(1)) continue;
            buckets[t.Hour] = buckets[t.Hour] with { Pcs = buckets[t.Hour].Pcs + 1 };
        }
        return buckets;
    }
}
