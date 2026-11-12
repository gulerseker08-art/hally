using Hally.Domain.Capture;
using Hally.Domain.Common;
using Hally.Domain.Families;
using Hally.Domain.Notifications;
using Hally.Domain.Planning;
using Hally.Domain.Users;
using Hally.Infrastructure.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Hally.Infrastructure.Persistence;

public class HallyDbContext(DbContextOptions<HallyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<FamilyTask> Tasks => Set<FamilyTask>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<Capture> Captures => Set<Capture>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Locale).HasMaxLength(5).IsRequired();
            e.Property(x => x.EveningPrepEnabled).HasDefaultValue(true);
            e.Property(x => x.EveningPrepHour).HasDefaultValue(20);
            e.HasIndex(x => x.Email);
            e.HasMany(x => x.Logins).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Logins).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<ExternalLogin>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(255).IsRequired();
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Family>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Children).WithOne().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Children).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<FamilyMember>(e =>
        {
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.FamilyId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Child>(e => e.Property(x => x.FirstName).HasMaxLength(40).IsRequired());

        b.Entity<Invitation>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Family).WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => new { x.Scope, x.Key });
            e.Property(x => x.Scope).HasMaxLength(64);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ResponseBody).HasColumnType("jsonb");
        });

        b.Entity<AnalyticsEvent>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.Properties).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Name, x.OccurredAt });
            e.HasIndex(x => new { x.FamilyId, x.OccurredAt });
        });

        b.Entity<FamilyTask>(e =>
        {
            e.ToTable("tasks");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.FamilyId, x.State, x.DueAt });
            e.HasIndex(x => x.SourceProposalId).IsUnique();
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarEvent>(e =>
        {
            e.ToTable("events");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Location).HasMaxLength(200);
            e.Ignore(x => x.EffectiveEnd);
            e.HasIndex(x => new { x.FamilyId, x.StartsAt });
            e.HasIndex(x => x.SourceProposalId).IsUnique();
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Capture>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Text).HasMaxLength(20000);
            e.Property(x => x.StorageKey).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.FailureCode).HasMaxLength(64);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.HasIndex(x => new { x.FamilyId, x.CreatedAt });
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Proposal>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.FamilyId, x.Status });
            e.HasIndex(x => new { x.FamilyId, x.Fingerprint });
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Capture>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_log");
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasIndex(x => new { x.FamilyId, x.At });
        });

        b.Entity<Reminder>(e =>
        {
            e.Property(x => x.Target).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.FireAt });
            e.HasIndex(x => new { x.FamilyId, x.TargetId });
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.Platform).HasMaxLength(10).IsRequired();
            e.Property(x => x.Token).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TargetKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        });

        // Ids are generated by the domain, so EF must insert (not update) new children of tracked aggregates.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(type.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }
}

/// <summary>Stored response of a state-changing POST, keyed by caller scope and Idempotency-Key.</summary>
public class IdempotencyRecord
{
    public string Scope { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string RequestHash { get; set; } = default!;
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
