namespace Press.Service.Http;

using System.Text.Json;
using Press.Service.Infrastructure;

public static class MesHttpEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapMesHttp(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/mes");

        api.MapGet("/outbox", async (string? status, int? limit, IMesOutbox outbox, CancellationToken ct) =>
        {
            var items = await outbox.ListAsync(status, limit ?? 50, ct);
            return Results.Json(new { items }, JsonOpts);
        });

        api.MapPost("/outbox/{messageId}/retry", async (string messageId, IMesOutbox outbox, CancellationToken ct) =>
        {
            var msg = await outbox.RetryAsync(messageId, ct);
            return msg is null ? Results.NotFound() : Results.Json(msg, JsonOpts);
        });

        api.MapGet("/sync-status", async (IMesOutbox outbox, Press.Adapters.Abstractions.IMesPublisher publisher, CancellationToken ct) =>
        {
            var status = await outbox.GetSyncStatusAsync(publisher.AdapterName, ct);
            return Results.Json(status, JsonOpts);
        });

        api.MapPost("/ingest-result", async (JsonElement body, IMesOutbox outbox, CancellationToken ct) =>
        {
            if (!body.TryGetProperty("idempotencyKey", out var keyEl))
                return Results.BadRequest(new { error = "idempotencyKey required" });
            var key = keyEl.GetString() ?? "";
            var payload = body.GetRawText();
            var msg = await outbox.EnqueueAsync(key, payload, ct);
            return Results.Json(msg, JsonOpts, statusCode: StatusCodes.Status202Accepted);
        });
    }
}
