namespace Press.Judge;

/// <summary>
/// Pure judgement API — no device I/O. Call from press-service Application layer.
/// </summary>
public interface IJudgeEngine
{
    LiveJudgement EvaluateLive(IReadOnlyList<SamplePoint> samples, RecipeJudgementInput recipe);
    CycleJudgement FinalizeCycle(IReadOnlyList<SamplePoint> samples, RecipeJudgementInput recipe);
    EnvelopeCurve BuildEnvelope(IReadOnlyList<IReadOnlyList<SamplePoint>> goldens, EnvelopeInput input);
}

public sealed record SamplePoint(double TimeS, double ForceN, double PositionMm);

public sealed record RecipeJudgementInput(
    string AlgorithmVersion,
    double ContactForceN,
    double MaxForceN,
    double MinForceN,
    double EndPositionMm,
    double EndPosTolMm,
    double CheckTolMm,
    PvfsInput? Pvfs,
    double? StopAngleDeg,
    EnvelopeCurve? Envelope,
    double HoldDelayS);

public sealed record PvfsInput(double Percent, double StartPosMm, double DistanceMm, bool AutoLocate);

public sealed record EnvelopeInput(
    double PositiveTolN,
    double NegativeTolN,
    double Filter,
    double StartPosMm,
    double EndPosMm);

public sealed record EnvelopeCurve(
    string EnvelopeId,
    IReadOnlyList<SamplePoint> Upper,
    IReadOnlyList<SamplePoint> Lower);

public sealed record LiveJudgement(
    bool ShouldStop,
    string? StopReason,
    double? ComputedEndForceN,
    double? CurrentAngleDeg);

public sealed record CycleJudgement(
    bool Pass,
    IReadOnlyList<string> FailCodes,
    double EndForceN,
    double EndPositionMm,
    double? PvfsSampleN,
    double? PeakAngleDeg);
