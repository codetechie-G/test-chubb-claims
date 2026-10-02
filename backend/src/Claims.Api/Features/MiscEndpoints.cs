using System.Security.Claims;
using Claims.Api.Auth;
using Claims.Api.Domain;
using Claims.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public static class MiscEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        // Demo-only: lets the UI render a user picker. Would not exist with a real IdP.
        auth.MapGet("/demo-users", () => DemoUsers.All.Select(ToDto));

        auth.MapPost("/login", Results<Ok<LoginResponse>, UnauthorizedHttpResult> (LoginRequest body, TokenService tokens) =>
        {
            var user = DemoUsers.Find(body.Username);
            return user is null
                ? TypedResults.Unauthorized()
                : TypedResults.Ok(new LoginResponse(tokens.Issue(user), ToDto(user)));
        });

        app.MapGet("/api/markets", () => Market.All).WithTags("Reference");
    }

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, ClaimsDbContext db) =>
        {
            var me = user.ToActor().Id;
            return await db.Notifications.AsNoTracking()
                .Where(n => n.RecipientId == me)
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Take(50)
                .Select(n => new NotificationDto(n.Id, n.ClaimId, n.ClaimReference, n.Message, n.CreatedAt, n.ReadAt != null))
                .ToListAsync();
        });

        group.MapPost("/read-all", async (ClaimsPrincipal user, ClaimsDbContext db, TimeProvider clock) =>
        {
            var me = user.ToActor().Id;
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Notifications.Where(n => n.RecipientId == me && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
            return TypedResults.NoContent();
        });
    }

    private static UserDto ToDto(DemoUser u) => new(u.Id, u.Username, u.Name, u.Role);
}
