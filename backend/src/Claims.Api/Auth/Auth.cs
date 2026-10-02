using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Claims.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Claims.Api.Auth;

public static class Roles
{
    public const string Claimant = "Claimant";
    public const string Handler = "Handler";
}

public sealed record DemoUser(string Id, string Username, string Name, string Role);

// Demo users only. Real setup: customer IdP for claimants, corporate SSO for staff.
public static class DemoUsers
{
    public static readonly IReadOnlyList<DemoUser> All =
    [
        new("cl-alice", "alice", "Alice Tan", Roles.Claimant),
        new("cl-ben", "ben", "Ben Wong", Roles.Claimant),
        new("cl-chloe", "chloe", "Chloe Nguyen", Roles.Claimant),
        new("hd-hana", "hana", "Hana Lee", Roles.Handler),
        new("hd-raj", "raj", "Raj Patel", Roles.Handler),
        new("hd-mia", "mia", "Mia Chen", Roles.Handler),
    ];

    public static DemoUser? Find(string username) =>
        All.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "chubb-claims-demo";
    public string Audience { get; set; } = "chubb-claims-ui";
    public string SigningKey { get; set; } = default!;
    public int LifetimeMinutes { get; set; } = 480;

    public SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class TokenService(JwtOptions options)
{
    public const string RoleClaim = "role";

    public string Issue(DemoUser user)
    {
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new System.Security.Claims.Claim(JwtRegisteredClaimNames.Name, user.Name),
                new System.Security.Claims.Claim(RoleClaim, user.Role),
            ],
            expires: DateTime.UtcNow.AddMinutes(options.LifetimeMinutes),
            signingCredentials: new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class PrincipalExtensions
{
    public static Actor ToActor(this ClaimsPrincipal user) => new(
        user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? throw new InvalidOperationException("No subject claim"),
        user.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "Unknown");

    public static bool IsHandler(this ClaimsPrincipal user) => user.IsInRole(Roles.Handler);
}
