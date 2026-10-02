namespace Claims.Api.Domain;

public enum ClaimType
{
    Motor,
    Property
}

// Submitted -> UnderAssessment <-> AwaitingInformation -> Approved -> Settled; Rejected is terminal too.
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
    Answered,
    Withdrawn // claim was closed before the claimant answered
}

public static class ClaimStatusExtensions
{
    public static bool IsOpen(this ClaimStatus status) =>
        status is not (ClaimStatus.Settled or ClaimStatus.Rejected);
}
