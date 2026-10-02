using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Domain;
using Claims.Api.Features;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Claims.Tests;

/// <summary>Spins up the real API (in-memory transport, fresh SQLite file per test class).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"claims-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Claims", $"Data Source={_dbPath}");
        builder.UseSetting("SeedDemoData", "false");
        builder.UseSetting("Messaging:Transport", "InMemory");
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> As(string username)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private static async Task<ClaimDetailDto> Submit(HttpClient claimant, decimal claimed = 1000m)
    {
        var res = await claimant.PostAsJsonAsync("/api/claims",
            new SubmitClaimRequest(ClaimType.Motor, "AU", new DateOnly(2026, 1, 10), "Sydney", "Bumper damage", claimed), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ClaimDetailDto>(Json))!;
    }

    [Fact]
    public async Task Claimant_cannot_see_another_claimants_claim()
    {
        var alice = await As("alice");
        var ben = await As("ben");
        var claim = await Submit(alice);

        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/claims/{claim.Id}")).StatusCode);
        var benList = await ben.GetFromJsonAsync<PagedResult<ClaimSummaryDto>>("/api/claims", Json);
        Assert.DoesNotContain(benList!.Items, c => c.Id == claim.Id);
    }

    [Fact]
    public async Task Claimant_never_sees_reserve()
    {
        var alice = await As("alice");
        var claim = await Submit(alice, 5000m);

        Assert.Null(claim.ReserveAmount);
        var hana = await As("hana");
        var staffView = await hana.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{claim.Id}", Json);
        Assert.Equal(5000m, staffView!.ReserveAmount);
    }

    [Fact]
    public async Task Claimant_cannot_call_staff_endpoints_or_dashboards()
    {
        var alice = await As("alice");
        var claim = await Submit(alice);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/claims/{claim.Id}/pickup", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/dashboard/exposure")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_pickup_assigns_exactly_one_handler()
    {
        var claim = await Submit(await As("alice"));
        var hana = await As("hana");
        var raj = await As("raj");

        var results = await Task.WhenAll(
            hana.PostAsync($"/api/claims/{claim.Id}/pickup", null),
            raj.PostAsync($"/api/claims/{claim.Id}/pickup", null));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Stale_if_match_is_rejected()
    {
        var claim = await Submit(await As("alice"));
        var hana = await As("hana");
        (await hana.PostAsync($"/api/claims/{claim.Id}/pickup", null)).EnsureSuccessStatusCode();

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/claims/{claim.Id}/reserve")
        {
            Content = JsonContent.Create(new UpdateReserveRequest(10m)),
        };
        req.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{claim.Version}\"")); // version before pickup

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await hana.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_events_flow_to_exposure_dashboard_and_notifications()
    {
        var chloe = await As("chloe");
        var hana = await As("hana");
        var claim = await Submit(chloe, 2000m);

        (await hana.PostAsync($"/api/claims/{claim.Id}/pickup", null)).EnsureSuccessStatusCode();
        (await hana.PutAsJsonAsync($"/api/claims/{claim.Id}/reserve", new UpdateReserveRequest(2500m))).EnsureSuccessStatusCode();

        // Read model is eventually consistent: poll until the projector has caught up.
        await Eventually(async () =>
        {
            var exposure = await hana.GetFromJsonAsync<ExposureDto>("/api/dashboard/exposure", Json);
            return exposure!.Markets.Single(m => m.MarketCode == "AU").Reserve >= 2500m;
        });

        (await hana.PostAsJsonAsync($"/api/claims/{claim.Id}/reject", new RejectClaimRequest("Not covered"))).EnsureSuccessStatusCode();

        await Eventually(async () =>
        {
            var notes = await chloe.GetFromJsonAsync<List<NotificationDto>>("/api/notifications", Json);
            return notes!.Any(n => n.ClaimId == claim.Id && n.Message.Contains("declined"));
        });
        // Reserve-change events are internal and never produce a claimant notification.
        var all = await chloe.GetFromJsonAsync<List<NotificationDto>>("/api/notifications", Json);
        Assert.DoesNotContain(all!, n => n.ClaimId == claim.Id && n.Message.Contains("Reserve"));
    }

    private static async Task Eventually(Func<Task<bool>> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Condition not met before timeout (consumer did not catch up).");
    }
}
