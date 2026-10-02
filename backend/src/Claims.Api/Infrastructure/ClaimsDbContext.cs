using System.Text.Json;
using Claims.Api.Domain;
using Claims.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Infrastructure;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimHistoryEntry> ClaimHistory => Set<ClaimHistoryEntry>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ClaimProjection> ClaimProjections => Set<ClaimProjection>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Claim>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Reference).IsUnique();
            e.HasIndex(c => c.ClaimantId);
            e.HasIndex(c => new { c.Status, c.AssigneeId });
            e.Property(c => c.Type).HasConversion<string>();
            e.Property(c => c.Status).HasConversion<string>();
            e.Property(c => c.Version).IsConcurrencyToken();
            e.Ignore(c => c.PendingEvents);
            e.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId);
            e.HasMany(c => c.History).WithOne().HasForeignKey(h => h.ClaimId);
        });

        b.Entity<InformationRequest>(e =>
        {
            e.HasKey(r => r.Id);
            // Ids are assigned by the domain; without this EF treats new children as "Modified".
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.Status).HasConversion<string>();
        });

        b.Entity<ClaimHistoryEntry>(e => e.HasIndex(h => new { h.ClaimId, h.Version }));

        b.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(o => o.PublishedAt);
            e.HasIndex(o => o.EventId).IsUnique();
        });

        b.Entity<ClaimProjection>(e =>
        {
            e.HasKey(p => p.ClaimId);
            e.Property(p => p.Type).HasConversion<string>();
            e.Property(p => p.Status).HasConversion<string>();
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(n => n.EventId).IsUnique();
            e.HasIndex(n => new { n.RecipientId, n.CreatedAt });
        });
    }

    /// <summary>
    /// Transactional outbox: domain events raised by tracked claims are written as outbox rows in
    /// the same SaveChanges (same transaction) as the state change, so we never commit a change
    /// without its event or publish an event for a change that rolled back.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var claims = ChangeTracker.Entries<Claim>().Select(e => e.Entity).Where(c => c.PendingEvents.Count > 0).ToList();
        foreach (var claim in claims)
        {
            foreach (var evt in claim.PendingEvents)
            {
                Outbox.Add(new OutboxMessage
                {
                    EventId = evt.EventId,
                    EventType = evt.EventType,
                    Key = evt.Claim.ClaimId.ToString(),
                    Payload = JsonSerializer.Serialize(ClaimEventMessage.From(evt), MessagingJson.Options),
                    OccurredAt = evt.OccurredAt,
                });
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);
        claims.ForEach(c => c.ClearPendingEvents());
        return result;
    }
}
