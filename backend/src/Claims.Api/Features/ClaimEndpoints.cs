using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Domain;
using Claims.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Claim = Claims.Api.Domain.Claim;

namespace Claims.Api.Features;

// Commands are REST because the user waits for the result; side effects run off the events.
public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var claims = app.MapGroup("/api/claims").WithTags("Claims").RequireAuthorization();

        claims.MapGet("/", ListClaims);
        claims.MapGet("/{id:guid}", GetClaim);
        claims.MapPost("/", SubmitClaim).RequireAuthorization(p => p.RequireRole(Roles.Claimant));
        claims.MapPost("/{id:guid}/information-requests/{requestId:guid}/response",
                (Guid id, Guid requestId, ProvideInformationRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                    Mutate(id, user, http, db, clock, (c, actor, now) => c.ProvideInformation(actor, requestId, body.Response, now)))
            .RequireAuthorization(p => p.RequireRole(Roles.Claimant));

        var staff = claims.MapGroup("/").RequireAuthorization(p => p.RequireRole(Roles.Handler));
        staff.MapPost("/{id:guid}/pickup",
            (Guid id, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.PickUp(actor, now)));
        staff.MapPut("/{id:guid}/reserve",
            (Guid id, UpdateReserveRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.UpdateReserve(actor, body.Amount, now)));
        staff.MapPost("/{id:guid}/information-requests",
            (Guid id, RequestInformationRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.RequestInformation(actor, body.Question, now)));
        staff.MapPost("/{id:guid}/approve",
            (Guid id, ApproveClaimRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.Approve(actor, body.SettlementAmount, body.Note, now)));
        staff.MapPost("/{id:guid}/settle",
            (Guid id, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.Settle(actor, now)));
        staff.MapPost("/{id:guid}/reject",
            (Guid id, RejectClaimRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Mutate(id, user, http, db, clock, (c, actor, now) => c.Reject(actor, body.Reason, now)));
    }

    /// <param name="scope">Handlers only: <c>queue</c> (unassigned), <c>mine</c> (my open claims) or <c>all</c>.</param>
    private static async Task<Ok<PagedResult<ClaimSummaryDto>>> ListClaims(
        ClaimsPrincipal user, ClaimsDbContext db,
        string? scope, ClaimStatus? status, string? market, ClaimType? type,
        int page = 1, int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var isHandler = user.IsHandler();
        var me = user.ToActor().Id;

        var query = db.Claims.AsNoTracking();
        if (!isHandler)
        {
            query = query.Where(c => c.ClaimantId == me).OrderByDescending(c => c.SubmittedAt);
        }
        else
        {
            query = scope?.ToLowerInvariant() switch
            {
                "queue" => query.Where(c => c.Status == ClaimStatus.Submitted).OrderBy(c => c.SubmittedAt), // oldest first
                "mine" => query.Where(c => c.AssigneeId == me
                                           && c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected)
                               .OrderBy(c => c.SubmittedAt),
                _ => query.OrderByDescending(c => c.SubmittedAt),
            };
        }

        if (status is not null) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(market)) query = query.Where(c => c.MarketCode == market.ToUpper());
        if (type is not null) query = query.Where(c => c.Type == type);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new ClaimSummaryDto(
                c.Id, c.Reference, c.Type, c.MarketCode, c.Currency, c.Status, c.ClaimantName,
                c.AssigneeId, c.AssigneeName, c.ClaimedAmount,
                isHandler ? c.ReserveAmount : null,
                c.InformationRequests.Count(r => r.Status == InformationRequestStatus.Open),
                c.SubmittedAt, c.UpdatedAt, c.Version))
            .ToListAsync();

        return TypedResults.Ok(new PagedResult<ClaimSummaryDto>(items, total, page, pageSize));
    }

    private static async Task<Results<Ok<ClaimDetailDto>, NotFound>> GetClaim(Guid id, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db)
    {
        var claim = await LoadAsync(db, id, tracking: false);
        if (claim is null || !CanSee(user, claim)) return TypedResults.NotFound();

        SetETag(http, claim);
        return TypedResults.Ok(claim.ToDetail(user.IsHandler()));
    }

    private static async Task<Results<Created<ClaimDetailDto>, ValidationProblem>> SubmitClaim(
        SubmitClaimRequest body, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock)
    {
        var market = Market.Find(body.MarketCode);
        if (market is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(body.MarketCode)] = [$"Unknown market. Valid: {string.Join(", ", Market.All.Select(m => m.Code))}"],
            });
        if (string.IsNullOrWhiteSpace(body.IncidentLocation))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(body.IncidentLocation)] = ["Incident location is required."],
            });

        var claim = Claim.Submit(user.ToActor(), market, body.Type, body.IncidentDate, body.IncidentLocation,
            body.Description ?? "", body.ClaimedAmount, clock.GetUtcNow().UtcDateTime);
        db.Claims.Add(claim);
        await db.SaveChangesAsync();

        SetETag(http, claim);
        return TypedResults.Created($"/api/claims/{claim.Id}", claim.ToDetail(forHandler: false));
    }

    // Load, check access and If-Match, apply the domain method, save (outbox in the same transaction).
    private static async Task<IResult> Mutate(
        Guid id, ClaimsPrincipal user, HttpContext http, ClaimsDbContext db, TimeProvider clock,
        Action<Claim, Actor, DateTime> action)
    {
        var claim = await LoadAsync(db, id, tracking: true);
        if (claim is null || !CanSee(user, claim)) return TypedResults.NotFound();

        var ifMatch = http.Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrEmpty(ifMatch) && ifMatch.Trim('"', 'W', '/') != claim.Version.ToString())
            return TypedResults.Problem(
                title: "Claim has changed",
                detail: $"Claim {claim.Reference} was updated by someone else. Reload and try again.",
                statusCode: StatusCodes.Status412PreconditionFailed);

        action(claim, user.ToActor(), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();

        SetETag(http, claim);
        return TypedResults.Ok(claim.ToDetail(user.IsHandler()));
    }

    private static Task<Claim?> LoadAsync(ClaimsDbContext db, Guid id, bool tracking)
    {
        var query = db.Claims.Include(c => c.InformationRequests).Include(c => c.History).AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == id);
    }

    // 404 rather than 403 so claim ids can't be probed.
    private static bool CanSee(ClaimsPrincipal user, Claim claim) =>
        user.IsHandler() || claim.ClaimantId == user.ToActor().Id;

    private static void SetETag(HttpContext http, Claim claim) =>
        http.Response.Headers.ETag = $"\"{claim.Version}\"";
}
