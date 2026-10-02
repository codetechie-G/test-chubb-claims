namespace Claims.Api.Domain;

public enum ClaimType
{
    Motor,
    Property
}

/// <summary>
/// Lifecycle: Submitted -> UnderAssessment <-> AwaitingInformation -> Approved -> Settled,
/// with Rejected reachable from UnderAssessment / AwaitingInformation.
/// Settled and Rejected are terminal.
/// </summary>
public enum ClaimStatus
{
    Submitted,
    UnderAssessment,
    AwaitingInformation,
    Approved,
    Settled,
    Rejected
}

public enum InformationRequestStatus
{
    Open,
    Answered
}

public static class ClaimStatusExtensions
{
    public static bool IsOpen(this ClaimStatus status) =>
        status is not (ClaimStatus.Settled or ClaimStatus.Rejected);
}
