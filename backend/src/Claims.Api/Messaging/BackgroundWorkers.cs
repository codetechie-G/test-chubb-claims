using System.Text.Json;
using Claims.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging;

// Publishes outbox rows in order; stops at the first failure so per-claim ordering holds.
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IMessageTransport transport,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        var batch = await db.Outbox.Where(o => o.PublishedAt == null).OrderBy(o => o.Id).Take(100).ToListAsync(ct);
        foreach (var message in batch)
        {
            try
            {
                await transport.PublishAsync(message.Key, message.Payload, ct);
                message.PublishedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message;
                logger.LogWarning(ex, "Failed to publish outbox message {EventId} (attempt {Attempt})", message.EventId, message.Attempts);
                break;
            }
        }

        if (batch.Count > 0) await db.SaveChangesAsync(ct);
    }
}

// One consume loop per consumer group, with a small retry before a message is skipped.
public sealed class ConsumerHost(
    IEnumerable<ConsumerRegistration> registrations,
    IMessageTransport transport,
    IServiceScopeFactory scopes,
    ILogger<ConsumerHost> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(registrations.Select(registration => transport.ConsumeAsync(registration.GroupId, (payload, token) => HandleAsync(registration, payload, token), ct)));

    private async Task HandleAsync(ConsumerRegistration registration, string payload, CancellationToken ct)
    {
        ClaimEventMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ClaimEventMessage>(payload, MessagingJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "[{Group}] Unreadable message skipped", registration.GroupId);
            return;
        }
        if (message is null) return;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var handler = (IClaimEventHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
                await handler.HandleAsync(message, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= MaxAttempts)
                {
                    logger.LogError(ex, "[{Group}] Giving up on {EventType} {EventId} after {Attempts} attempts (dead-letter)",
                        registration.GroupId, message.EventType, message.EventId, attempt);
                    return;
                }
                logger.LogWarning(ex, "[{Group}] Retrying {EventId} (attempt {Attempt})", registration.GroupId, message.EventId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
        }
    }
}
