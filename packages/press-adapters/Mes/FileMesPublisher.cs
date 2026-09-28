namespace Press.Adapters.Mes;

using Press.Adapters.Abstractions;

/// <summary>Writes cycle payloads as JSON files — stand-in for plant MES during simulation.</summary>
public sealed class FileMesPublisher : IMesPublisher
{
    private readonly string _dir;

    public string AdapterName => "file";

    public FileMesPublisher(string outputDirectory)
    {
        _dir = outputDirectory;
        Directory.CreateDirectory(_dir);
    }

    public async Task PublishAsync(CycleResultMessage message, CancellationToken ct)
    {
        var path = Path.Combine(_dir, $"{message.IdempotencyKey}.json");
        await File.WriteAllTextAsync(path, message.PayloadJson, ct);
    }
}

/// <summary>Always fails — useful to exercise dead-letter / retry without real MES.</summary>
public sealed class FailingMesPublisher : IMesPublisher
{
    public string AdapterName => "fail";

    public Task PublishAsync(CycleResultMessage message, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated MES outage (Mes:Adapter=fail)");
}
