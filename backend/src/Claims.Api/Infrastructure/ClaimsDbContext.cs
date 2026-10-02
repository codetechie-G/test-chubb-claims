using System.Text.Json;
using Claims.Api.Domain;
using Claims.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Claims.Api.Infrastructure;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistoryEntry> ClaimHistory => Set<ClaimHistoryEntry>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ClaimProjection> ClaimProjections => Set<ClaimProjection>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // SQLite returns DateTimeKind.Unspecified; force UTC so the API always emits "Z" timestamps.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Claim>(claim =>
        {
            claim.HasKey(c => c.Id);
            claim.HasIndex(c => c.Reference).IsUnique();
            claim.HasIndex(c => c.ClaimantId);
            claim.HasIndex(c => new { c.Status, c.AssigneeId });
            claim.Property(c => c.Type).HasConversion<string>();
            claim.Property(c => c.Status).HasConversion<string>();
            claim.Property(c => c.Version).IsConcurrencyToken();
            claim.Ignore(c => c.PendingEvents);
            claim.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId);
            claim.HasMany(c => c.History).WithOne().HasForeignKey(h => h.ClaimId);
        });

        modelBuilder.Entity<InformationRequest>(request =>
        {
            request.HasKey(r => r.Id);
            // Id is set by the domain; without this EF would treat new requests as updates.
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.Status).HasConversion<string>();
        });

        modelBuilder.Entity<ClaimHistoryEntry>(history => history.HasIndex(h => new { h.ClaimId, h.Version }));

        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.HasIndex(o => o.PublishedAt);
            outbox.HasIndex(o => o.EventId).IsUnique();
        });

        modelBuilder.Entity<ClaimProjection>(projection =>
        {
            projection.HasKey(p => p.ClaimId);
            projection.Property(p => p.Type).HasConversion<string>();
            projection.Property(p => p.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.HasIndex(n => n.EventId).IsUnique();
            notification.HasIndex(n => new { n.RecipientId, n.CreatedAt });
        });
    }

    // Outbox: events are saved in the same transaction as the claim change.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var changedClaims = StageOutboxMessages();
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        changedClaims.ForEach(claim => claim.ClearPendingEvents());
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changedClaims = StageOutboxMessages();
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        changedClaims.ForEach(claim => claim.ClearPendingEvents());
        return saved;
    }

    private List<Claim> StageOutboxMessages()
    {
        var changedClaims = ChangeTracker.Entries<Claim>()
            .Select(entry => entry.Entity)
            .Where(claim => claim.PendingEvents.Count > 0)
            .ToList();

        foreach (var domainEvent in changedClaims.SelectMany(claim => claim.PendingEvents))
        {
            Outbox.Add(new OutboxMessage
            {
                EventId = domainEvent.EventId,
                EventType = domainEvent.EventType,
                Key = domainEvent.Claim.ClaimId.ToString(),
                Payload = JsonSerializer.Serialize(ClaimEventMessage.From(domainEvent), MessagingJson.Options),
                OccurredAt = domainEvent.OccurredAt,
            });
        }

        return changedClaims;
    }
}
