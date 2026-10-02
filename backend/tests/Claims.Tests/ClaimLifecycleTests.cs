using Claims.Api.Domain;

namespace Claims.Tests;

public class ClaimLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Actor Claimant = new("c-1", "Alice Claimant");
    private static readonly Actor Handler = new("h-1", "Harry Handler");
    private static readonly Actor OtherHandler = new("h-2", "Olive Handler");

    private static Claim NewClaim(decimal? claimed = 5000m) => Claim.Submit(
        Claimant, Market.Find("SG")!, ClaimType.Motor, new DateOnly(2026, 9, 28),
        "Orchard Rd", "Rear-ended at lights", claimed, Now);

    [Fact]
    public void Submit_sets_initial_state_and_raises_event()
    {
        var claim = NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal("SGD", claim.Currency);
        Assert.Equal(5000m, claim.ReserveAmount);
        Assert.Equal(1, claim.Version);
        Assert.StartsWith("SG-261001-", claim.Reference);
        var evt = Assert.Single(claim.PendingEvents);
        Assert.Equal(ClaimEventTypes.Submitted, evt.EventType);
        Assert.Equal(1, evt.Claim.Version);
    }

    [Fact]
    public void Submit_rejects_future_incident_date()
    {
        Assert.Throws<DomainException>(() => Claim.Submit(
            Claimant, Market.Find("AU")!, ClaimType.Property, new DateOnly(2026, 10, 2),
            "Sydney", "Flood", null, Now));
    }

    [Fact]
    public void Full_happy_path_reaches_settled_with_zero_reserve()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        var request = claim.RequestInformation(Handler, "Please upload the police report", Now);
        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Status);

        claim.ProvideInformation(Claimant, request.Id, "Report #123 attached", Now);
        Assert.Equal(ClaimStatus.UnderAssessment, claim.Status);

        claim.UpdateReserve(Handler, 4200m, Now);
        claim.Approve(Handler, 4100m, null, Now);
        Assert.Equal(4100m, claim.ReserveAmount);

        claim.Settle(Handler, Now);
        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(0m, claim.ReserveAmount);
        Assert.NotNull(claim.ClosedAt);
        Assert.Equal(7, claim.Version);
        Assert.Equal(7, claim.History.Count);
    }

    [Fact]
    public void Cannot_pick_up_a_claim_twice()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);

        Assert.Throws<DomainException>(() => claim.PickUp(OtherHandler, Now));
    }

    [Fact]
    public void Only_assignee_can_decide()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);

        Assert.Throws<DomainException>(() => claim.Approve(OtherHandler, 100m, null, Now));
        Assert.Throws<DomainException>(() => claim.Reject(OtherHandler, "no", Now));
    }

    [Fact]
    public void Cannot_approve_while_awaiting_information()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        claim.RequestInformation(Handler, "Photos?", Now);

        Assert.Throws<DomainException>(() => claim.Approve(Handler, 100m, null, Now));
    }

    [Fact]
    public void Claim_stays_awaiting_until_all_requests_answered()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        var r1 = claim.RequestInformation(Handler, "Photos?", Now);
        claim.RequestInformation(Handler, "Repair quote?", Now);

        claim.ProvideInformation(Claimant, r1.Id, "Attached", Now);

        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Status);
    }

    [Fact]
    public void Another_claimant_cannot_answer()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        var r = claim.RequestInformation(Handler, "Photos?", Now);

        Assert.Throws<DomainException>(() => claim.ProvideInformation(new Actor("c-2", "Mallory"), r.Id, "x", Now));
    }

    [Fact]
    public void Declining_withdraws_open_questions_and_blocks_late_answers()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        var question = claim.RequestInformation(Handler, "Photos?", Now);

        claim.Reject(Handler, "Not covered", Now);

        Assert.Equal(InformationRequestStatus.Withdrawn, question.Status);
        Assert.Throws<DomainException>(() => claim.ProvideInformation(Claimant, question.Id, "late", Now));
    }

    [Fact]
    public void Rejected_claim_is_terminal_and_releases_reserve()
    {
        var claim = NewClaim();
        claim.PickUp(Handler, Now);
        claim.Reject(Handler, "Policy excludes racing", Now);

        Assert.Equal(0m, claim.ReserveAmount);
        Assert.False(claim.Status.IsOpen());
        Assert.Throws<DomainException>(() => claim.UpdateReserve(Handler, 10m, Now));
    }
}
