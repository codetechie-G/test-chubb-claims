using Claims.Api.Domain;
using Claims.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Messaging;

// Keeps the dashboard projection up to date; ignores events older than what it has seen.
public sealed class ClaimProjector(ClaimsDbContext db) : IClaimEventHandler
{
    public const string GroupId = "claims-projector";

    public async Task HandleAsync(ClaimEventMessage message, CancellationToken ct)
    {
        var snapshot = message.Claim;
        var row = await db.ClaimProjections.FindAsync([snapshot.ClaimId], ct);
        if (row is null)
        {
            row = new ClaimProjection { ClaimId = snapshot.ClaimId };
            db.ClaimProjections.Add(row);
        }
        else if (row.LastVersion >= snapshot.Version)
        {
            return; // duplicate or stale delivery
        }

        row.Reference = snapshot.Reference;
        row.MarketCode = snapshot.MarketCode;
        row.Currency = snapshot.Currency;
        row.Type = snapshot.Type;
        row.Status = snapshot.Status;
        row.AssigneeId = snapshot.AssigneeId;
        row.AssigneeName = snapshot.AssigneeName;
        row.ReserveAmount = snapshot.ReserveAmount;
        row.SubmittedAt = snapshot.SubmittedAt;
        row.ClosedAt = snapshot.Status.IsOpen() ? null : message.OccurredAt;
        row.LastVersion = snapshot.Version;
        row.UpdatedAt = message.OccurredAt;

        await db.SaveChangesAsync(ct);
    }
}

// Writes claimant notifications; deduplicated on EventId.
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
