using Claims.Api.Auth;
using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Infrastructure;

// Seeds through the real domain methods so history, events and read models are all genuine.
public static class DevSeeder
{
    public static async Task SeedAsync(ClaimsDbContext db, TimeProvider clock)
    {
        if (await db.Claims.AnyAsync()) return;

        var now = clock.GetUtcNow().UtcDateTime;
        Actor ActorFor(string username) { var user = DemoUsers.Find(username)!; return new Actor(user.Id, user.Name); }
        var (alice, ben, chloe) = (ActorFor("alice"), ActorFor("ben"), ActorFor("chloe"));
        var (hana, raj, mia) = (ActorFor("hana"), ActorFor("raj"), ActorFor("mia"));

        Claim Seed(Actor who, string market, ClaimType type, int daysAgo, string where, string what, decimal? amount) =>
            Claim.Submit(who, Market.Find(market)!, type, DateOnly.FromDateTime(now.AddDays(-daysAgo - 2)), where, what, amount, now.AddDays(-daysAgo));

        var claims = new List<Claim>();

        // Unassigned queue
        claims.Add(Seed(alice, "SG", ClaimType.Motor, 1, "PIE near Exit 12", "Rear-ended in slow traffic; bumper and tail lights damaged.", 3200m));
        claims.Add(Seed(ben, "HK", ClaimType.Property, 3, "Kowloon Tong apartment", "Burst pipe flooded kitchen and hallway.", 48000m));
        claims.Add(Seed(chloe, "AU", ClaimType.Property, 6, "Parramatta, NSW", "Hail damage to roof tiles and skylight.", 12500m));
        claims.Add(Seed(alice, "MY", ClaimType.Motor, 0, "Jalan Ampang, KL", "Side mirror clipped by motorcycle.", null));

        // Hana: in progress
        var claim = Seed(ben, "AU", ClaimType.Motor, 9, "M4 Motorway", "Windscreen cracked by road debris.", 900m);
        claim.PickUp(hana, now.AddDays(-8)); claim.UpdateReserve(hana, 750m, now.AddDays(-8));
        claims.Add(claim);
        claim = Seed(alice, "SG", ClaimType.Property, 12, "Tampines HDB flat", "Electrical fire in bedroom; smoke damage.", 26000m);
        claim.PickUp(hana, now.AddDays(-11)); claim.RequestInformation(hana, "Please upload the SCDF fire incident report and photos of the damage.", now.AddDays(-10));
        claims.Add(claim);
        claim = Seed(chloe, "NZ", ClaimType.Motor, 20, "State Highway 1, Auckland", "Collision at roundabout, third party at fault.", 7800m);
        claim.PickUp(hana, now.AddDays(-19)); claim.UpdateReserve(hana, 8400m, now.AddDays(-15));
        claims.Add(claim);

        // Raj: mixed
        claim = Seed(ben, "TH", ClaimType.Property, 15, "Sukhumvit condo, Bangkok", "Break-in; laptop and jewellery stolen.", 185000m);
        claim.PickUp(raj, now.AddDays(-14)); claim.RequestInformation(raj, "Please provide the police report number and receipts for stolen items.", now.AddDays(-13));
        claims.Add(claim);
        claim = Seed(alice, "HK", ClaimType.Motor, 7, "Tuen Mun Road", "Hit a barrier avoiding a lorry; front-end damage.", 36000m);
        claim.PickUp(raj, now.AddDays(-6)); claim.Approve(raj, 34500m, "Repair quote verified", now.AddDays(-2));
        claims.Add(claim);

        // Closed claims
        claim = Seed(chloe, "AU", ClaimType.Motor, 25, "Brisbane CBD", "Car park scrape on passenger door.", 1400m);
        claim.PickUp(mia, now.AddDays(-24)); claim.Approve(mia, 1350m, null, now.AddDays(-21)); claim.Settle(mia, now.AddDays(-20));
        claims.Add(claim);
        claim = Seed(ben, "SG", ClaimType.Motor, 18, "Bukit Timah Rd", "Claimed hit-and-run; damage inconsistent with account.", 9000m);
        claim.PickUp(mia, now.AddDays(-17)); claim.Reject(mia, "Damage pattern inconsistent with the reported incident; see assessor report.", now.AddDays(-10));
        claims.Add(claim);
        claim = Seed(alice, "MY", ClaimType.Property, 28, "Penang shophouse", "Storm blew off part of the roof.", 22000m);
        claim.PickUp(hana, now.AddDays(-27)); claim.Approve(hana, 19800m, null, now.AddDays(-12)); claim.Settle(hana, now.AddDays(-11));
        claims.Add(claim);
        claim = Seed(chloe, "NZ", ClaimType.Property, 5, "Wellington villa", "Earthquake cracks in chimney.", 15000m);
        claim.PickUp(mia, now.AddDays(-4));
        claims.Add(claim);

        db.Claims.AddRange(claims);
        await db.SaveChangesAsync();
    }
}
