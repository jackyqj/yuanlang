namespace Press.Service.Application;

using Press.Judge;

/// <summary>Editable recipe document persisted in SQLite and applied to runtime as RuntimeJob.</summary>
public sealed class RecipeDocument
{
    public string RecipeVersionId { get; set; } = "rv-demo-1";
    public string ProductId { get; set; } = "demo";
    public string ProductName { get; set; } = "CPK";
    public string ConnectorName { get; set; } = "J1";
    public string PcbBarcode { get; set; } = "PCB-001";
    public string ConnectorBarcode { get; set; } = "CN-001";

    public double ContactForceN { get; set; } = 50;
    public double MaxForceN { get; set; } = 5000;
    public double MinForceN { get; set; } = 100;
    public double EndPositionMm { get; set; } = 36;
    public double EndPosTolMm { get; set; } = 0.5;
    public double CheckTolMm { get; set; } = 1.0;
    public double WorkOriginMm { get; set; } = 40;

    public double PvfsPercent { get; set; } = 25;
    public double PvfsStartMm { get; set; } = 37.0;
    public double PvfsDistanceMm { get; set; } = 0.2;
    public bool PvfsAutoLocate { get; set; }

    public double? StopAngleDeg { get; set; }
    public double HoldDelayS { get; set; } = 0.15;

    public double ApproachSpeedMmS { get; set; } = 15;
    public double PressSpeedMmS { get; set; } = 2;
    public double RetractSpeedMmS { get; set; } = 20;

    public string AlgorithmVersion { get; set; } = "sim-1";
    public DateTimeOffset? PublishedAt { get; set; }

    public RuntimeJob ToJob()
    {
        var recipe = new RecipeJudgementInput(
            AlgorithmVersion,
            ContactForceN,
            MaxForceN,
            MinForceN,
            EndPositionMm,
            EndPosTolMm,
            CheckTolMm,
            new PvfsInput(PvfsPercent, PvfsStartMm, PvfsDistanceMm, PvfsAutoLocate),
            StopAngleDeg,
            Envelope: null,
            HoldDelayS);

        return new RuntimeJob(
            ProductId,
            ProductName,
            RecipeVersionId,
            ConnectorName,
            PcbBarcode,
            ConnectorBarcode,
            recipe,
            ApproachSpeedMmS,
            PressSpeedMmS,
            RetractSpeedMmS,
            WorkOriginMm,
            HoldDelayS);
    }

    public static RecipeDocument FromJob(RuntimeJob job) => new()
    {
        RecipeVersionId = job.RecipeVersionId,
        ProductId = job.ProductId,
        ProductName = job.ProductName,
        ConnectorName = job.ConnectorName,
        PcbBarcode = job.PcbBarcode,
        ConnectorBarcode = job.ConnectorBarcode,
        ContactForceN = job.Recipe.ContactForceN,
        MaxForceN = job.Recipe.MaxForceN,
        MinForceN = job.Recipe.MinForceN,
        EndPositionMm = job.Recipe.EndPositionMm,
        EndPosTolMm = job.Recipe.EndPosTolMm,
        CheckTolMm = job.Recipe.CheckTolMm,
        WorkOriginMm = job.WorkOriginMm,
        PvfsPercent = job.Recipe.Pvfs?.Percent ?? 25,
        PvfsStartMm = job.Recipe.Pvfs?.StartPosMm ?? 37,
        PvfsDistanceMm = job.Recipe.Pvfs?.DistanceMm ?? 0.2,
        PvfsAutoLocate = job.Recipe.Pvfs?.AutoLocate ?? false,
        StopAngleDeg = job.Recipe.StopAngleDeg,
        HoldDelayS = job.HoldDelayS,
        ApproachSpeedMmS = job.ApproachSpeedMmS,
        PressSpeedMmS = job.PressSpeedMmS,
        RetractSpeedMmS = job.RetractSpeedMmS,
        AlgorithmVersion = job.Recipe.AlgorithmVersion,
    };

    public static RecipeDocument DefaultDemo() => new();
}

public sealed record RecipeSummary(
    string RecipeVersionId,
    string ProductId,
    string ProductName,
    string ConnectorName,
    DateTimeOffset PublishedAt);
