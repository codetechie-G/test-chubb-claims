using Claims.Api.Domain;
using Claims.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging;

/// <summary>
/// Maintains the <see cref="ClaimProjection"/> read model behind the workload and liability-exposure
/// dashboards. Idempotent and order-tolerant: a message is applied only if its claim version is
/// newer than what the projection has already seen.
/// </summary>
public sealed class ClaimProjector(ClaimsDbContext db) : IClaimEventHandler
{
    public const string GroupId = "claims-projector";

    public async Task HandleAsync(ClaimEventMessage message, CancellationToken ct)
    {
        var s = message.Claim;
        var row = await db.ClaimProjections.FindAsync([s.ClaimId], ct);
        if (row is null)
        {
            row = new ClaimProjection { ClaimId = s.ClaimId };
            db.ClaimProjections.Add(row);
        }
        else if (row.LastVersion >= s.Version)
        {
            return; // duplicate or stale delivery
        }

        row.Reference = s.Reference;
        row.MarketCode = s.MarketCode;
        row.Currency = s.Currency;
        row.Type = s.Type;
        row.Status = s.Status;
        row.AssigneeId = s.AssigneeId;
        row.AssigneeName = s.AssigneeName;
        row.ReserveAmount = s.ReserveAmount;
        row.SubmittedAt = s.SubmittedAt;
        row.ClosedAt = s.Status.IsOpen() ? null : message.OccurredAt;
        row.LastVersion = s.Version;
        row.UpdatedAt = message.OccurredAt;

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Turns claim events into claimant-facing notifications (in-app here; email/SMS would hang off
/// the same consumer). Deduplicated on EventId.
/// </summary>
public sealed class ClaimantNotifier(ClaimsDbContext db) : IClaimEventHandler
{
    public const string GroupId = "claimant-notifier";

    public async Task HandleAsync(ClaimEventMessage message, CancellationToken ct)
    {
        var text = Describe(message);
        if (text is null) return;
        if (await db.Notifications.AnyAsync(n => n.EventId == message.EventId, ct)) return;

        db.Notifications.Add(new Notification
        {
            EventId = message.EventId,
            RecipientId = message.Claim.ClaimantId,
            ClaimId = message.Claim.ClaimId,
            ClaimReference = message.Claim.Reference,
            Message = text,
            CreatedAt = message.OccurredAt,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a duplicate delivery (unique EventId index): already notified.
            if (!await db.Notifications.AsNoTracking().AnyAsync(n => n.EventId == message.EventId, ct)) throw;
        }
    }

    private static string? Describe(ClaimEventMessage m) => m.EventType switch
    {
        ClaimEventTypes.Submitted => $"We've received claim {m.Claim.Reference}. A claims officer will pick it up shortly.",
        ClaimEventTypes.Assigned => $"{m.Claim.AssigneeName} is now handling your claim {m.Claim.Reference}.",
        ClaimEventTypes.InformationRequested => $"We need more information for claim {m.Claim.Reference}: {m.Detail}",
        ClaimEventTypes.Approved => $"Claim {m.Claim.Reference} has been approved for {m.Claim.SettlementAmount:N2} {m.Claim.Currency}.",
        ClaimEventTypes.Settled => $"Payment of {m.Claim.SettlementAmount:N2} {m.Claim.Currency} for claim {m.Claim.Reference} has been made.",
        ClaimEventTypes.Rejected => $"Claim {m.Claim.Reference} has been declined: {m.Detail}",
        _ => null, // internal events (e.g. reserve changes) are not shown to claimants
    };
}
