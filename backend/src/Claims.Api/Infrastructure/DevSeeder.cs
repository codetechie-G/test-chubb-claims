using Claims.Api.Auth;
using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Infrastructure;

/// <summary>
/// Seeds demo claims by driving the real aggregate methods, so seeded data produces genuine
/// history and outbox events and the read models are populated by the consumers, not by hand.
/// </summary>
public static class DevSeeder
{
    public static async Task SeedAsync(ClaimsDbContext db, TimeProvider clock)
    {
        if (await db.Claims.AnyAsync()) return;

        var now = clock.GetUtcNow().UtcDateTime;
        Actor A(string username) { var u = DemoUsers.Find(username)!; return new Actor(u.Id, u.Name); }
        var (alice, ben, chloe) = (A("alice"), A("ben"), A("chloe"));
        var (hana, raj, mia) = (A("hana"), A("raj"), A("mia"));

        Claim New(Actor who, string market, ClaimType type, int daysAgo, string where, string what, decimal? amount) =>
            Claim.Submit(who, Market.Find(market)!, type, DateOnly.FromDateTime(now.AddDays(-daysAgo - 2)), where, what, amount, now.AddDays(-daysAgo));

        var claims = new List<Claim>();

        // Unassigned queue
        claims.Add(New(alice, "SG", ClaimType.Motor, 1, "PIE near Exit 12", "Rear-ended in slow traffic; bumper and tail lights damaged.", 3200m));
        claims.Add(New(ben, "HK", ClaimType.Property, 3, "Kowloon Tong apartment", "Burst pipe flooded kitchen and hallway.", 48000m));
        claims.Add(New(chloe, "AU", ClaimType.Property, 6, "Parramatta, NSW", "Hail damage to roof tiles and skylight.", 12500m));
        claims.Add(New(alice, "MY", ClaimType.Motor, 0, "Jalan Ampang, KL", "Side mirror clipped by motorcycle.", null));

        // Hana: under assessment / awaiting info
        var c = New(ben, "AU", ClaimType.Motor, 9, "M4 Motorway", "Windscreen cracked by road debris.", 900m);
        c.PickUp(hana, now.AddDays(-8)); c.UpdateReserve(hana, 750m, now.AddDays(-8));
        claims.Add(c);
        c = New(alice, "SG", ClaimType.Property, 12, "Tampines HDB flat", "Electrical fire in bedroom; smoke damage.", 26000m);
        c.PickUp(hana, now.AddDays(-11)); c.RequestInformation(hana, "Please upload the SCDF fire incident report and photos of the damage.", now.AddDays(-10));
        claims.Add(c);
        c = New(chloe, "NZ", ClaimType.Motor, 20, "State Highway 1, Auckland", "Collision at roundabout, third party at fault.", 7800m);
        c.PickUp(hana, now.AddDays(-19)); c.UpdateReserve(hana, 8400m, now.AddDays(-15));
        claims.Add(c);

        // Raj: mixed
        c = New(ben, "TH", ClaimType.Property, 15, "Sukhumvit condo, Bangkok", "Break-in; laptop and jewellery stolen.", 185000m);
        c.PickUp(raj, now.AddDays(-14)); c.RequestInformation(raj, "Please provide the police report number and receipts for stolen items.", now.AddDays(-13));
        claims.Add(c);
        c = New(alice, "HK", ClaimType.Motor, 7, "Tuen Mun Road", "Hit a barrier avoiding a lorry; front-end damage.", 36000m);
        c.PickUp(raj, now.AddDays(-6)); c.Approve(raj, 34500m, "Repair quote verified", now.AddDays(-2));
        claims.Add(c);

        // Closed claims (performance history)
        c = New(chloe, "AU", ClaimType.Motor, 25, "Brisbane CBD", "Car park scrape on passenger door.", 1400m);
        c.PickUp(mia, now.AddDays(-24)); c.Approve(mia, 1350m, null, now.AddDays(-21)); c.Settle(mia, now.AddDays(-20));
        claims.Add(c);
        c = New(ben, "SG", ClaimType.Motor, 18, "Bukit Timah Rd", "Claimed hit-and-run; damage inconsistent with account.", 9000m);
        c.PickUp(mia, now.AddDays(-17)); c.Reject(mia, "Damage pattern inconsistent with the reported incident; see assessor report.", now.AddDays(-10));
        claims.Add(c);
        c = New(alice, "MY", ClaimType.Property, 28, "Penang shophouse", "Storm blew off part of the roof.", 22000m);
        c.PickUp(hana, now.AddDays(-27)); c.Approve(hana, 19800m, null, now.AddDays(-12)); c.Settle(hana, now.AddDays(-11));
        claims.Add(c);
        c = New(chloe, "NZ", ClaimType.Property, 5, "Wellington villa", "Earthquake cracks in chimney.", 15000m);
        c.PickUp(mia, now.AddDays(-4));
        claims.Add(c);

        db.Claims.AddRange(claims);
        await db.SaveChangesAsync();
    }
}
