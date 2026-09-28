namespace Press.Service.Infrastructure;

public sealed record OutboxMessage(
    string MessageId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    int RetryCount,
    string? LastError,
    string PayloadJson,
    string IdempotencyKey);

public sealed record MesSyncStatus(
    string Adapter,
    bool Online,
    int PendingCount,
    int DeadCount,
    DateTimeOffset? LastAckAt);

public interface IMesOutbox
{
    Task EnsureSchemaAsync(CancellationToken ct);
    Task<OutboxMessage> EnqueueAsync(string idempotencyKey, string payloadJson, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> ListAsync(string? status, int limit, CancellationToken ct);
    Task<OutboxMessage?> RetryAsync(string messageId, CancellationToken ct);
    Task<MesSyncStatus> GetSyncStatusAsync(string adapterName, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(int batchSize, CancellationToken ct);
    Task MarkAckedAsync(string messageId, CancellationToken ct);
    Task MarkFailedAsync(string messageId, string error, int maxAttempts, CancellationToken ct);
}
