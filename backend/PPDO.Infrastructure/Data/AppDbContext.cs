using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data;

/// <summary>
/// EF Core database context for PPDO Portal.
/// All entity configurations live in Data/Configurations/ — discovered automatically
/// via <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>.
///
/// Usage rules (see CLAUDE.md):
///   - Inject only into Repository implementations — never into Application services or Functions.
///   - All queries must be async (ToListAsync, FirstOrDefaultAsync, etc.).
///   - Never use Include chains deeper than 2 levels.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── DbSets ────────────────────────────────────────────────────────────────

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<PermissionGroup> PermissionGroups { get; set; } = null!;
    public DbSet<ResourceLink> ResourceLinks { get; set; } = null!;
    public DbSet<PurchaseRequest> PurchaseRequests { get; set; } = null!;
    public DbSet<PRItem> PRItems { get; set; } = null!;
    public DbSet<ItemMaster> ItemMasters { get; set; } = null!;
    public DbSet<Delivery> Deliveries { get; set; } = null!;
    public DbSet<DeliveryItem> DeliveryItems { get; set; } = null!;
    public DbSet<Distribution> Distributions { get; set; } = null!;
    public DbSet<CalendarEvent> CalendarEvents { get; set; } = null!;

    // ── Model configuration ───────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Discover and apply all IEntityTypeConfiguration<T> classes in this assembly.
        // Each entity has its own configuration file under Data/Configurations/.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    // ── Audit timestamp interception ──────────────────────────────────────────

    /// <summary>
    /// Intercepts async saves to auto-stamp CreatedAt / UpdatedAt on all tracked entities
    /// that declare those properties. All timestamps are stored as UTC.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Intercepts synchronous saves (used by EF Core tooling / seeding).
    /// Prefer <see cref="SaveChangesAsync"/> in application code.
    /// </summary>
    public override int SaveChanges()
    {
        StampAuditTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// For every tracked entity in Added or Modified state:
    ///   - Added   → sets CreatedAt and UpdatedAt to DateTime.UtcNow
    ///   - Modified → sets UpdatedAt to DateTime.UtcNow
    /// Uses EF Core metadata to detect properties by name — no shared interface needed.
    /// </summary>
    private void StampAuditTimestamps()
    {
        DateTime now = DateTime.UtcNow;

        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Metadata.FindProperty("CreatedAt") is not null)
                    entry.Property("CreatedAt").CurrentValue = now;

                if (entry.Metadata.FindProperty("UpdatedAt") is not null)
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Metadata.FindProperty("UpdatedAt") is not null)
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }
}
