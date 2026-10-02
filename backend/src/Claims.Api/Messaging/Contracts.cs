using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Domain;

namespace Claims.Api.Messaging;

/// <summary>
/// Public integration-event contract published on the <c>claims.claim-events</c> topic.
/// Deliberately separate from the internal domain event so the wire format can be versioned
/// independently of the aggregate. Keyed by ClaimId so all events for one claim land on the
/// same partition and are consumed in order.
/// </summary>
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
