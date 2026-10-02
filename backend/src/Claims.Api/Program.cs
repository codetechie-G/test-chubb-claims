using System.Text.Json.Serialization;
using Claims.Api.Auth;
using Claims.Api.Domain;
using Claims.Api.Features;
using Claims.Api.Infrastructure;
using Claims.Api.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// --- Persistence ---------------------------------------------------------------------------
builder.Services.AddDbContext<ClaimsDbContext>(o => o.UseSqlite(config.GetConnectionString("Claims")));
builder.Services.AddSingleton(TimeProvider.System);

// --- Messaging (outbox -> transport -> consumer groups) --------------------------------------
var messaging = config.GetSection("Messaging").Get<MessagingOptions>() ?? new MessagingOptions();
builder.Services.AddSingleton(messaging);
if (string.Equals(messaging.Transport, "Kafka", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IMessageTransport, KafkaTransport>();
else
    builder.Services.AddSingleton<IMessageTransport, InMemoryTransport>();

builder.Services.AddClaimEventHandler<ClaimProjector>(ClaimProjector.GroupId);
builder.Services.AddClaimEventHandler<ClaimantNotifier>(ClaimantNotifier.GroupId);
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ConsumerHost>();

// --- Auth ----------------------------------------------------------------------------------
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt config missing");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.Key,
        NameClaimType = "name",
        RoleClaimType = TokenService.RoleClaim,
    };
});
builder.Services.AddAuthorization();

// --- HTTP ----------------------------------------------------------------------------------
builder.Services.Configure<ReportingOptions>(config.GetSection("Reporting"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Domain rule violations -> 409, lost optimistic-concurrency race -> 409, malformed body -> 400.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title, detail) = ex switch
    {
        DomainException d => (StatusCodes.Status409Conflict, "Action not allowed", d.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Claim has changed",
            "Someone else updated this claim at the same time. Reload and try again."),
        BadHttpRequestException b => (StatusCodes.Status400BadRequest, "Invalid request", b.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", (string?)null),
    };
    ctx.Response.StatusCode = status;
    await ctx.RequestServices.GetRequiredService<IProblemDetailsService>()
        .WriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = { Status = status, Title = title, Detail = detail } });
}));

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(); // interactive docs at /scalar

app.MapAuthEndpoints();
app.MapClaimEndpoints();
app.MapDashboardEndpoints();
app.MapNotificationEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok", transport = messaging.Transport })).WithTags("Health");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    // Prototype shortcut: EnsureCreated instead of migrations (see README "Shortcuts").
    await db.Database.EnsureCreatedAsync();
    if (config.GetValue("SeedDemoData", true))
        await DevSeeder.SeedAsync(db, app.Services.GetRequiredService<TimeProvider>());
}

app.Run();

public partial class Program; // for WebApplicationFactory in tests
