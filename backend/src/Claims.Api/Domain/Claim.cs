namespace Claims.Api.Domain;

/// <summary>
/// Claim aggregate root. All state changes go through methods on this class so the
/// lifecycle rules live in one place; each change bumps <see cref="Version"/> (used as the
/// optimistic-concurrency token) and raises a domain event that is persisted to the outbox
/// in the same transaction.
/// </summary>
public class Claim
{
    private readonly List<InformationRequest> _informationRequests = [];
    private readonly List<ClaimHistoryEntry> _history = [];
    private readonly List<ClaimDomainEvent> _pendingEvents = [];

    private Claim() { } // EF

    public Guid Id { get; private set; }
    public string Reference { get; private set; } = default!;
    public string ClaimantId { get; private set; } = default!;
    public string ClaimantName { get; private set; } = default!;
    public string MarketCode { get; private set; } = default!;
    public string Currency { get; private set; } = default!;
    public ClaimType Type { get; private set; }
    public ClaimStatus Status { get; private set; }
    public DateOnly IncidentDate { get; private set; }
    public string IncidentLocation { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    /// <summary>Claimant's own estimate of the loss, if given.</summary>
    public decimal? ClaimedAmount { get; private set; }

    /// <summary>Case reserve: the insurer's current estimate of liability. Zero once closed.</summary>
    public decimal ReserveAmount { get; private set; }

    public decimal? SettlementAmount { get; private set; }
    public string? DecisionReason { get; private set; }
    public string? AssigneeId { get; private set; }
    public string? AssigneeName { get; private set; }
    public DateTime SubmittedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyList<InformationRequest> InformationRequests => _informationRequests;
    public IReadOnlyList<ClaimHistoryEntry> History => _history;
    public IReadOnlyList<ClaimDomainEvent> PendingEvents => _pendingEvents;

    public static Claim Submit(
        Actor claimant,
        Market market,
        ClaimType type,
        DateOnly incidentDate,
        string incidentLocation,
        string description,
        decimal? claimedAmount,
        DateTime now)
    {
        if (incidentDate > DateOnly.FromDateTime(now))
            throw new DomainException("Incident date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A description of the incident is required.");
        if (claimedAmount is < 0)
            throw new DomainException("Claimed amount cannot be negative.");

        var id = Guid.NewGuid();
        var claim = new Claim
        {
            Id = id,
            Reference = $"{market.Code}-{now:yyMMdd}-{id.ToString("N")[..6].ToUpperInvariant()}",
            ClaimantId = claimant.Id,
            ClaimantName = claimant.Name,
            MarketCode = market.Code,
            Currency = market.Currency,
            Type = type,
            Status = ClaimStatus.Submitted,
            IncidentDate = incidentDate,
            IncidentLocation = incidentLocation.Trim(),
            Description = description.Trim(),
            ClaimedAmount = claimedAmount,
            // Initial reserve defaults to the claimant's estimate until a handler reviews it.
            ReserveAmount = claimedAmount ?? 0m,
            SubmittedAt = now,
        };
        claim.Record(ClaimEventTypes.Submitted, claimant, now, $"Claim submitted for {type} incident on {incidentDate:yyyy-MM-dd}.");
        return claim;
    }

    /// <summary>A handler takes ownership of an unassigned claim from the queue.</summary>
    public void PickUp(Actor handler, DateTime now)
    {
        if (Status != ClaimStatus.Submitted)
            throw new DomainException($"Claim {Reference} has already been picked up.");

        AssigneeId = handler.Id;
        AssigneeName = handler.Name;
        Status = ClaimStatus.UnderAssessment;
        Record(ClaimEventTypes.Assigned, handler, now, $"Picked up by {handler.Name}.");
    }

    public void UpdateReserve(Actor handler, decimal amount, DateTime now)
    {
        EnsureAssignee(handler);
        EnsureStatus(ClaimStatus.UnderAssessment, ClaimStatus.AwaitingInformation);
        if (amount < 0) throw new DomainException("Reserve cannot be negative.");

        var previous = ReserveAmount;
        ReserveAmount = amount;
        Record(ClaimEventTypes.ReserveChanged, handler, now, $"Reserve changed from {previous:N2} to {amount:N2} {Currency}.");
    }

    public InformationRequest RequestInformation(Actor handler, string question, DateTime now)
    {
        EnsureAssignee(handler);
        EnsureStatus(ClaimStatus.UnderAssessment, ClaimStatus.AwaitingInformation);
        if (string.IsNullOrWhiteSpace(question)) throw new DomainException("A question is required.");

        var request = new InformationRequest(Id, question.Trim(), handler, now);
        _informationRequests.Add(request);
        Status = ClaimStatus.AwaitingInformation;
        Record(ClaimEventTypes.InformationRequested, handler, now, request.Question);
        return request;
    }

    public void ProvideInformation(Actor claimant, Guid requestId, string response, DateTime now)
    {
        if (claimant.Id != ClaimantId) throw new DomainException("Only the claimant can respond to this request.");
        var request = _informationRequests.FirstOrDefault(r => r.Id == requestId)
                      ?? throw new DomainException("Information request not found.");
        if (string.IsNullOrWhiteSpace(response)) throw new DomainException("A response is required.");

        request.Answer(response.Trim(), now);
        if (Status == ClaimStatus.AwaitingInformation && _informationRequests.All(r => r.Status == InformationRequestStatus.Answered))
            Status = ClaimStatus.UnderAssessment;
        Record(ClaimEventTypes.InformationProvided, claimant, now, request.Response);
    }

    public void Approve(Actor handler, decimal settlementAmount, string? note, DateTime now)
    {
        EnsureAssignee(handler);
        EnsureStatus(ClaimStatus.UnderAssessment);
        if (settlementAmount <= 0) throw new DomainException("Settlement amount must be positive.");

        SettlementAmount = settlementAmount;
        ReserveAmount = settlementAmount; // liability is now known exactly, still outstanding until paid
        DecisionReason = note?.Trim();
        Status = ClaimStatus.Approved;
        Record(ClaimEventTypes.Approved, handler, now, $"Approved for {settlementAmount:N2} {Currency}.");
    }

    public void Settle(Actor handler, DateTime now)
    {
        EnsureAssignee(handler);
        EnsureStatus(ClaimStatus.Approved);

        ReserveAmount = 0m;
        Status = ClaimStatus.Settled;
        ClosedAt = now;
        Record(ClaimEventTypes.Settled, handler, now, $"Settlement of {SettlementAmount:N2} {Currency} paid.");
    }

    public void Reject(Actor handler, string reason, DateTime now)
    {
        EnsureAssignee(handler);
        EnsureStatus(ClaimStatus.UnderAssessment, ClaimStatus.AwaitingInformation);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A rejection reason is required.");

        ReserveAmount = 0m;
        DecisionReason = reason.Trim();
        Status = ClaimStatus.Rejected;
        ClosedAt = now;
        Record(ClaimEventTypes.Rejected, handler, now, DecisionReason);
    }

    public void ClearPendingEvents() => _pendingEvents.Clear();

    private void EnsureAssignee(Actor handler)
    {
        if (AssigneeId != handler.Id)
            throw new DomainException($"Claim {Reference} is assigned to {AssigneeName ?? "nobody"}; only the assignee can act on it.");
    }

    private void EnsureStatus(params ClaimStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new DomainException($"Action not allowed while claim is {Status}.");
    }

    private void Record(string eventType, Actor actor, DateTime now, string? detail)
    {
        Version++;
        UpdatedAt = now;
        _history.Add(new ClaimHistoryEntry(Id, Version, eventType, actor, now, detail));
        _pendingEvents.Add(new ClaimDomainEvent(Guid.NewGuid(), eventType, actor, now, detail, ClaimSnapshot.From(this)));
    }
}
