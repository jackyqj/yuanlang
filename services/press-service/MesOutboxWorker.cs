namespace Press.Service;

using Press.Adapters.Abstractions;
using Press.Service.Infrastructure;

internal sealed class MesOutboxBootstrapper(IMesOutbox outbox) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => outbox.EnsureSchemaAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class MesOutboxWorker(
    IMesOutbox outbox,
    IMesPublisher publisher,
    IConfiguration config,
    ILogger<MesOutboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMs = config.GetValue("Mes:PollIntervalMs", 1000);
        var maxAttempts = config.GetValue("Mes:MaxAttempts", 5);
        var batch = config.GetValue("Mes:BatchSize", 10);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await outbox.ClaimPendingAsync(batch, stoppingToken);
                foreach (var msg in claimed)
                {
                    try
                    {
                        await publisher.PublishAsync(
                            new CycleResultMessage(msg.IdempotencyKey, msg.PayloadJson),
                            stoppingToken);
                        await outbox.MarkAckedAsync(msg.MessageId, stoppingToken);
                        log.LogInformation("MES acked {Key} via {Adapter}", msg.IdempotencyKey, publisher.AdapterName);
                    }
                    catch (Exception ex)
                    {
                        await outbox.MarkFailedAsync(msg.MessageId, ex.Message, maxAttempts, stoppingToken);
                        log.LogWarning(ex, "MES publish failed for {Key}", msg.IdempotencyKey);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "MES outbox worker loop error");
            }

            try
            {
                await Task.Delay(intervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
