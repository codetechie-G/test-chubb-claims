namespace Claims.Api.Domain;

/// <summary>Who performed an action. Comes from the authenticated principal, never from the request body.</summary>
public sealed record Actor(string Id, string Name);

public class InformationRequest
{
    private InformationRequest() { } // EF

    internal InformationRequest(Guid claimId, string question, Actor requestedBy, DateTime now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        Question = question;
        RequestedById = requestedBy.Id;
        RequestedByName = requestedBy.Name;
        RequestedAt = now;
        Status = InformationRequestStatus.Open;
    }

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public string Question { get; private set; } = default!;
    public string RequestedById { get; private set; } = default!;
    public string RequestedByName { get; private set; } = default!;
    public DateTime RequestedAt { get; private set; }
    public InformationRequestStatus Status { get; private set; }
    public string? Response { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    internal void Answer(string response, DateTime now)
    {
        if (Status != InformationRequestStatus.Open)
            throw new DomainException("This information request is no longer open.");
        Response = response;
        RespondedAt = now;
        Status = InformationRequestStatus.Answered;
    }

    internal void Withdraw() => Status = InformationRequestStatus.Withdrawn;
}

/// <summary>Append-only audit trail of everything that happened to a claim.</summary>
public class ClaimHistoryEntry
{
    private ClaimHistoryEntry() { } // EF

    internal ClaimHistoryEntry(Guid claimId, int version, string eventType, Actor actor, DateTime at, string? detail)
    {
        ClaimId = claimId;
        Version = version;
        EventType = eventType;
        ActorId = actor.Id;
        ActorName = actor.Name;
        OccurredAt = at;
        Detail = detail;
    }

    public long Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public int Version { get; private set; }
    public string EventType { get; private set; } = default!;
    public string ActorId { get; private set; } = default!;
    public string ActorName { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
    public string? Detail { get; private set; }
}

public static class ClaimEventTypes
{
    public const string Submitted = "ClaimSubmitted";
    public const string Assigned = "ClaimAssigned";
    public const string ReserveChanged = "ClaimReserveChanged";
    public const string InformationRequested = "ClaimInformationRequested";
    public const string InformationProvided = "ClaimInformationProvided";
    public const string Approved = "ClaimApproved";
    public const string Settled = "ClaimSettled";
    public const string Rejected = "ClaimRejected";
}

// Carries a full snapshot so consumers never need to call back into the API.
public sealed record ClaimDomainEvent(
    Guid EventId,
    string EventType,
    Actor Actor,
    DateTime OccurredAt,
    string? Detail,
    ClaimSnapshot Claim);

public sealed record ClaimSnapshot(
    Guid ClaimId,
    int Version,
    string Reference,
    string ClaimantId,
    string MarketCode,
    string Currency,
    ClaimType Type,
    ClaimStatus Status,
    string? AssigneeId,
    string? AssigneeName,
    decimal ReserveAmount,
    decimal? SettlementAmount,
    DateTime SubmittedAt)
{
    public static ClaimSnapshot From(Claim c) => new(
        c.Id, c.Version, c.Reference, c.ClaimantId, c.MarketCode, c.Currency, c.Type, c.Status,
        c.AssigneeId, c.AssigneeName, c.ReserveAmount, c.SettlementAmount, c.SubmittedAt);
}
