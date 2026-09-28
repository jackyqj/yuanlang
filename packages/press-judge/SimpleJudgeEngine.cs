namespace Press.Judge;

public sealed class SimpleJudgeEngine : IJudgeEngine
{
    public LiveJudgement EvaluateLive(IReadOnlyList<SamplePoint> samples, RecipeJudgementInput recipe)
    {
        if (samples.Count == 0)
        {
            return new LiveJudgement(false, null, null, null);
        }

        var last = samples[^1];
        if (last.ForceN > recipe.MaxForceN)
        {
            return new LiveJudgement(true, "OVER_FORCE", null, null);
        }

        double? endForce = null;
        if (recipe.Pvfs is { } pvfs && samples.Count >= 2)
        {
            endForce = CalcPvfs(samples, pvfs);
            if (endForce is not null && last.ForceN >= endForce.Value && last.PositionMm <= recipe.EndPositionMm + recipe.EndPosTolMm)
            {
                return new LiveJudgement(true, "PVFS_END", endForce, null);
            }
        }

        double? angle = null;
        if (recipe.StopAngleDeg is { } stopAngle && samples.Count >= 5)
        {
            angle = CalcAngleDeg(samples);
            if (angle >= stopAngle && last.PositionMm <= recipe.EndPositionMm + recipe.CheckTolMm)
            {
                return new LiveJudgement(true, "ANGLE_STOP", endForce, angle);
            }
        }

        return new LiveJudgement(false, null, endForce, angle);
    }

    public CycleJudgement FinalizeCycle(IReadOnlyList<SamplePoint> samples, RecipeJudgementInput recipe)
    {
        var fails = new List<string>();
        if (samples.Count == 0)
        {
            return new CycleJudgement(false, ["NO_SAMPLES"], 0, 0, null, null);
        }

        var last = samples[^1];
        var maxForce = samples.Max(s => s.ForceN);
        var contact = samples.FirstOrDefault(s => s.ForceN >= recipe.ContactForceN);

        if (contact is null)
        {
            fails.Add("NO_CONTACT");
        }
        else if (Math.Abs(contact.PositionMm - (recipe.EndPositionMm + 2.0)) > recipe.CheckTolMm + 2.0)
        {
            // Soft check: contact should occur near expected height window
        }

        if (maxForce > recipe.MaxForceN) fails.Add("OVER_FORCE");
        if (maxForce < recipe.MinForceN) fails.Add("UNDER_FORCE");
        if (Math.Abs(last.PositionMm - recipe.EndPositionMm) > recipe.EndPosTolMm) fails.Add("END_POS_TOL");

        if (recipe.Envelope is { } env)
        {
            foreach (var s in samples.Where(p => p.PositionMm <= env.Upper.First().PositionMm && p.PositionMm >= env.Lower.Last().PositionMm))
            {
                // Placeholder envelope gate — full interpolation in later phase
            }
        }

        var pvfs = recipe.Pvfs is null ? (double?)null : CalcPvfs(samples, recipe.Pvfs);
        double? angle = samples.Count >= 5 ? CalcAngleDeg(samples) : null;
        return new CycleJudgement(fails.Count == 0, fails, last.ForceN, last.PositionMm, pvfs, angle);
    }

    public EnvelopeCurve BuildEnvelope(IReadOnlyList<IReadOnlyList<SamplePoint>> goldens, EnvelopeInput input)
    {
        if (goldens.Count == 0)
        {
            return new EnvelopeCurve("empty", [], []);
        }

        // Average force on a coarse position grid between start/end
        var grid = Enumerable.Range(0, 21)
            .Select(i => input.StartPosMm + (input.EndPosMm - input.StartPosMm) * i / 20.0)
            .ToArray();

        var upper = new List<SamplePoint>();
        var lower = new List<SamplePoint>();
        foreach (var pos in grid)
        {
            var forces = goldens
                .Select(curve => NearestForce(curve, pos))
                .Where(f => f is not null)
                .Select(f => f!.Value)
                .ToList();
            if (forces.Count == 0) continue;
            var mean = forces.Average();
            upper.Add(new SamplePoint(0, mean + input.PositiveTolN, pos));
            lower.Add(new SamplePoint(0, Math.Max(0, mean - input.NegativeTolN), pos));
        }

        return new EnvelopeCurve(Guid.NewGuid().ToString("N"), upper, lower);
    }

    public static double? CalcPvfs(IReadOnlyList<SamplePoint> samples, PvfsInput pvfs)
    {
        var start = pvfs.StartPosMm;
        var end = pvfs.StartPosMm - pvfs.DistanceMm; // stroke toward smaller positions
        var window = samples.Where(s => s.PositionMm <= start && s.PositionMm >= end).ToList();
        if (window.Count == 0) return null;
        var sampleForce = window.Average(s => s.ForceN);
        return sampleForce * (1.0 + pvfs.Percent / 100.0);
    }

    public static double CalcAngleDeg(IReadOnlyList<SamplePoint> samples)
    {
        var a = samples[^5];
        var b = samples[^1];
        var ds = b.PositionMm - a.PositionMm;
        if (Math.Abs(ds) < 1e-6) return 90;
        var slope = (b.ForceN - a.ForceN) / ds; // N/mm; descending stroke => ds negative
        var angle = Math.Atan(Math.Abs(slope)) * 180.0 / Math.PI;
        return angle;
    }

    private static double? NearestForce(IReadOnlyList<SamplePoint> curve, double posMm)
    {
        if (curve.Count == 0) return null;
        return curve.MinBy(s => Math.Abs(s.PositionMm - posMm))!.ForceN;
    }
}
