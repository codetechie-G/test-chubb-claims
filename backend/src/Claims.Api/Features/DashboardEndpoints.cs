using Claims.Api.Auth;
using Claims.Api.Domain;
using Claims.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public sealed class ReportingOptions
{
    public string Currency { get; set; } = "USD";

    /// <summary>Units of reporting currency per 1 unit of local currency. Indicative, static for the demo.</summary>
    public Dictionary<string, decimal> FxRates { get; set; } = [];
}

public sealed record QueueStats(int Unassigned, double? OldestUnassignedAgeDays);

public sealed record HandlerWorkload(
    string HandlerId, string HandlerName, int Open, int UnderAssessment, int AwaitingInformation, int Approved,
    double? OldestOpenAgeDays, int ClosedLast30Days, double? AvgDaysToCloseLast30Days);

public sealed record WorkloadDto(DateTime? AsOf, QueueStats Queue, IReadOnlyList<HandlerWorkload> Handlers);

public sealed record ExposureByType(ClaimType Type, int OpenClaims, decimal Reserve);

public sealed record MarketExposure(
    string MarketCode, string MarketName, string Currency, int OpenClaims, decimal Reserve, decimal ReserveInReportingCurrency,
    IReadOnlyList<ExposureByType> ByType);

public sealed record StatusExposure(ClaimStatus Status, int OpenClaims, decimal ReserveInReportingCurrency);

public sealed record ExposureDto(
    DateTime? AsOf, string ReportingCurrency, string FxNote, int OpenClaims, decimal TotalReserveInReportingCurrency,
    IReadOnlyList<MarketExposure> Markets, IReadOnlyList<StatusExposure> ByStatus);

/// <summary>
/// Manager / team views. Served entirely from the event-fed <see cref="ClaimProjection"/> read model,
/// so they are eventually consistent (AsOf says how fresh) and never load the transactional tables.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard")
            .RequireAuthorization(p => p.RequireRole(Roles.Handler));

        group.MapGet("/workload", GetWorkload);
        group.MapGet("/exposure", GetExposure);
    }

    private static async Task<WorkloadDto> GetWorkload(ClaimsDbContext db, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-30);
        // Small data set; aggregate in memory (SQLite can't aggregate decimals/dates server-side via EF).
        var rows = await db.ClaimProjections.AsNoTracking()
            .Where(p => p.ClosedAt == null || p.ClosedAt >= since)
            .ToListAsync();

        var open = rows.Where(r => r.Status.IsOpen()).ToList();
        var unassigned = open.Where(r => r.Status == ClaimStatus.Submitted).ToList();

        var handlers = DemoUsers.All.Where(u => u.Role == Roles.Handler).Select(h =>
        {
            var mine = open.Where(r => r.AssigneeId == h.Id).ToList();
            var closed = rows.Where(r => r.AssigneeId == h.Id && r.ClosedAt is not null).ToList();
            return new HandlerWorkload(
                h.Id, h.Name, mine.Count,
                mine.Count(r => r.Status == ClaimStatus.UnderAssessment),
                mine.Count(r => r.Status == ClaimStatus.AwaitingInformation),
                mine.Count(r => r.Status == ClaimStatus.Approved),
                mine.Count > 0 ? Math.Round(mine.Max(r => (now - r.SubmittedAt).TotalDays), 1) : null,
                closed.Count,
                closed.Count > 0 ? Math.Round(closed.Average(r => (r.ClosedAt!.Value - r.SubmittedAt).TotalDays), 1) : null);
        }).ToList();

        return new WorkloadDto(
            rows.Count > 0 ? rows.Max(r => r.UpdatedAt) : null,
            new QueueStats(unassigned.Count,
                unassigned.Count > 0 ? Math.Round(unassigned.Max(r => (now - r.SubmittedAt).TotalDays), 1) : null),
            handlers);
    }

    private static async Task<ExposureDto> GetExposure(ClaimsDbContext db, Microsoft.Extensions.Options.IOptions<ReportingOptions> reporting)
    {
        var fx = reporting.Value;
        var all = await db.ClaimProjections.AsNoTracking().ToListAsync();
        var open = all.Where(r => r.Status.IsOpen()).ToList();

        decimal ToReporting(decimal amount, string currency) =>
            fx.FxRates.TryGetValue(currency, out var rate) ? Math.Round(amount * rate, 2) : 0m;

        var markets = Market.All.Select(m =>
        {
            var inMarket = open.Where(r => r.MarketCode == m.Code).ToList();
            var reserve = inMarket.Sum(r => r.ReserveAmount);
            return new MarketExposure(
                m.Code, m.Name, m.Currency, inMarket.Count, reserve, ToReporting(reserve, m.Currency),
                Enum.GetValues<ClaimType>()
                    .Select(t => new ExposureByType(t, inMarket.Count(r => r.Type == t), inMarket.Where(r => r.Type == t).Sum(r => r.ReserveAmount)))
                    .ToList());
        }).ToList();

        var byStatus = open.GroupBy(r => r.Status)
            .Select(g => new StatusExposure(g.Key, g.Count(), g.Sum(r => ToReporting(r.ReserveAmount, r.Currency))))
            .OrderBy(s => s.Status)
            .ToList();

        return new ExposureDto(
            all.Count > 0 ? all.Max(r => r.UpdatedAt) : null, // last event applied, incl. closures
            fx.Currency,
            $"Converted to {fx.Currency} at static indicative rates for comparison only; local-currency figures are authoritative.",
            open.Count,
            markets.Sum(m => m.ReserveInReportingCurrency),
            markets,
            byStatus);
    }
}
