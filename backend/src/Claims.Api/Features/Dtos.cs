using Claims.Api.Domain;

namespace Claims.Api.Features;

// ---- Requests -------------------------------------------------------------------------------

public sealed record SubmitClaimRequest(
    ClaimType Type,
    string MarketCode,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    decimal? ClaimedAmount);

public sealed record UpdateReserveRequest(decimal Amount);
public sealed record RequestInformationRequest(string Question);
public sealed record ProvideInformationRequest(string Response);
public sealed record ApproveClaimRequest(decimal SettlementAmount, string? Note);
public sealed record RejectClaimRequest(string Reason);
public sealed record LoginRequest(string Username);

// ---- Responses ------------------------------------------------------------------------------

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>ReserveAmount is null for claimants: the reserve is an internal insurer estimate.</summary>
public sealed record ClaimSummaryDto(
    Guid Id,
    string Reference,
    ClaimType Type,
    string MarketCode,
    string Currency,
    ClaimStatus Status,
    string ClaimantName,
    string? AssigneeId,
    string? AssigneeName,
    decimal? ClaimedAmount,
    decimal? ReserveAmount,
    int OpenInformationRequests,
    DateTime SubmittedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record ClaimDetailDto(
    Guid Id,
    string Reference,
    ClaimType Type,
    string MarketCode,
    string Currency,
    ClaimStatus Status,
    string ClaimantId,
    string ClaimantName,
    string? AssigneeId,
    string? AssigneeName,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    decimal? ClaimedAmount,
    decimal? ReserveAmount,
    decimal? SettlementAmount,
    string? DecisionReason,
    DateTime SubmittedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt,
    int Version,
    IReadOnlyList<InformationRequestDto> InformationRequests,
    IReadOnlyList<HistoryEntryDto> History);

public sealed record InformationRequestDto(
    Guid Id, string Question, string RequestedByName, DateTime RequestedAt,
    InformationRequestStatus Status, string? Response, DateTime? RespondedAt);

public sealed record HistoryEntryDto(int Version, string EventType, string ActorName, DateTime OccurredAt, string? Detail);

public sealed record UserDto(string Id, string Username, string Name, string Role);
public sealed record LoginResponse(string Token, UserDto User);

public sealed record NotificationDto(long Id, Guid ClaimId, string ClaimReference, string Message, DateTime CreatedAt, bool Read);

public static class ClaimMapping
{
    public static ClaimDetailDto ToDetail(this Claim c, bool forHandler) => new(
        c.Id, c.Reference, c.Type, c.MarketCode, c.Currency, c.Status, c.ClaimantId, c.ClaimantName,
        c.AssigneeId, c.AssigneeName, c.IncidentDate, c.IncidentLocation, c.Description, c.ClaimedAmount,
        forHandler ? c.ReserveAmount : null,
        c.SettlementAmount, c.DecisionReason, c.SubmittedAt, c.UpdatedAt, c.ClosedAt, c.Version,
        c.InformationRequests.OrderBy(r => r.RequestedAt)
            .Select(r => new InformationRequestDto(r.Id, r.Question, r.RequestedByName, r.RequestedAt, r.Status, r.Response, r.RespondedAt))
            .ToList(),
        c.History.Where(h => forHandler || h.EventType != ClaimEventTypes.ReserveChanged)
            .OrderBy(h => h.Version)
            .Select(h => new HistoryEntryDto(h.Version, h.EventType, h.ActorName, h.OccurredAt, h.Detail))
            .ToList());
}
