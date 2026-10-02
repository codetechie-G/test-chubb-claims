using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Domain;

namespace Claims.Api.Messaging;

// Wire contract for the claims.claim-events topic, versioned separately from the domain event.
public sealed record ClaimEventMessage(
    int SchemaVersion,
    Guid EventId,
    string EventType,
    DateTime OccurredAt,
    string ActorId,
    string ActorName,
    string? Detail,
    ClaimSnapshot Claim)
{
    public const int CurrentSchemaVersion = 1;

    public static ClaimEventMessage From(ClaimDomainEvent e) => new(
        CurrentSchemaVersion, e.EventId, e.EventType, e.OccurredAt, e.Actor.Id, e.Actor.Name, e.Detail, e.Claim);
}

public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>A consumer of claim events. Implementations must be idempotent (delivery is at-least-once).</summary>
public interface IClaimEventHandler
{
    Task HandleAsync(ClaimEventMessage message, CancellationToken ct);
}

/// <summary>One consumer group = one independent subscription to the claim event stream.</summary>
public sealed record ConsumerRegistration(string GroupId, Type HandlerType);

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddClaimEventHandler<THandler>(this IServiceCollection services, string groupId)
        where THandler : class, IClaimEventHandler
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new ConsumerRegistration(groupId, typeof(THandler)));
        return services;
    }
}
