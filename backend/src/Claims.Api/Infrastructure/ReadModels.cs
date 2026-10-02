using Claims.Api.Domain;

namespace Claims.Api.Infrastructure;

/// <summary>
/// Pending integration event, written in the same transaction as the claim change and
/// published to the message bus by <see cref="Messaging.OutboxDispatcher"/>.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; } // monotonic: preserves publish order
    public Guid EventId { get; set; }
    public string EventType { get; set; } = default!;
    public string Key { get; set; } = default!; // claim id -> Kafka partition key (per-claim ordering)
    public string Payload { get; set; } = default!;
    public DateTime OccurredAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Read model owned by the exposure/workload projector (a Kafka consumer). Denormalised so
/// dashboards never query the transactional claim tables.
/// </summary>
public class ClaimProjection
{
    public Guid ClaimId { get; set; }
    public string Reference { get; set; } = default!;
    public string MarketCode { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public ClaimType Type { get; set; }
    public ClaimStatus Status { get; set; }
    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public decimal ReserveAmount { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int LastVersion { get; set; } // only apply events newer than this -> idempotent, order-tolerant
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Claimant-facing notification produced by the notifier consumer.</summary>
public class Notification
{
    public long Id { get; set; }
    public Guid EventId { get; set; } // unique -> duplicate deliveries are ignored
    public string RecipientId { get; set; } = default!;
    public Guid ClaimId { get; set; }
    public string ClaimReference { get; set; } = default!;
    public string Message { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
