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
    public DbSet<Division> Divisions { get; set; } = null!;
    public DbSet<ResourceLink> ResourceLinks { get; set; } = null!;
    public DbSet<PurchaseRequest> PurchaseRequests { get; set; } = null!;
    public DbSet<PRItem> PRItems { get; set; } = null!;
    public DbSet<ItemMaster> ItemMasters { get; set; } = null!;
    public DbSet<Delivery> Deliveries { get; set; } = null!;
    public DbSet<DeliveryItem> DeliveryItems { get; set; } = null!;
    public DbSet<Distribution> Distributions { get; set; } = null!;
    public DbSet<CalendarEvent> CalendarEvents { get; set; } = null!;

    // ── v1.1 Budget Planning (RAL-67 — docs/v1.1/DB_Model.md) ────────────────

    public DbSet<Office> Offices { get; set; } = null!;
    public DbSet<FundingSource> FundingSources { get; set; } = null!;
    public DbSet<ClimateChangeTypology> ClimateChangeTypologies { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<LdipRecord> LdipRecords { get; set; } = null!;
    public DbSet<LdipOffice> LdipOffices { get; set; } = null!;
    public DbSet<LdipProgram> LdipPrograms { get; set; } = null!;
    public DbSet<AipRecord> AipRecords { get; set; } = null!;
    public DbSet<AipOffice> AipOffices { get; set; } = null!;
    public DbSet<AipProgram> AipPrograms { get; set; } = null!;
    public DbSet<AipProject> AipProjects { get; set; } = null!;
    public DbSet<AipActivity> AipActivities { get; set; } = null!;

    /// <summary>AIP expenditure lines (v1.8.0 Phase 2 — V18-33). Pesos; see the entity's remarks.</summary>
    public DbSet<AipExpenditure> AipExpenditures { get; set; } = null!;

    /// <summary>
    /// AIP procurement line items (v1.8.0 Phase 3 — V18-80). Sourced from the Price Index; no
    /// period dimension, unlike <see cref="WfpProcurementItems"/>.
    /// </summary>
    public DbSet<AipProcurementItem> AipProcurementItems { get; set; } = null!;

    /// <summary>
    /// Inline AIP review comments (v1.8.0 Phase 4 — V18-53). Anchored to a tree row, resolvable
    /// only by the side that wrote them, and never deleted once resolved.
    /// </summary>
    public DbSet<AipReviewComment> AipReviewComments { get; set; } = null!;

    public DbSet<WfpRecord> WfpRecords { get; set; } = null!;
    public DbSet<WfpActivity> WfpActivities { get; set; } = null!;
    public DbSet<WfpExpenditureLine> WfpExpenditureLines { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    // ── v1.1.1 Announcements (RAL-83) ─────────────────────────────────────────

    public DbSet<Announcement> Announcements { get; set; } = null!;

    // ── v1.2 Budget Allocation (RAL-99) ──────────────────────────────────────

    public DbSet<BudgetCeiling>      BudgetCeilings      { get; set; } = null!;
    public DbSet<DivisionAllocation> DivisionAllocations { get; set; } = null!;
    public DbSet<ProgramDivision>    ProgramDivisions    { get; set; } = null!;

    // ── v1.4 WFP Rework (RAL-116) ─────────────────────────────────────────────

    public DbSet<PriceIndexItem> PriceIndexItems { get; set; } = null!;
    public DbSet<WfpExpenditure> WfpExpenditures { get; set; } = null!;
    public DbSet<WfpExpenditurePeriod> WfpExpenditurePeriods { get; set; } = null!;
    public DbSet<WfpProcurementItem> WfpProcurementItems { get; set; } = null!;
    public DbSet<WfpDivisionAllocationLedger> WfpDivisionAllocationLedgers { get; set; } = null!;

    /// <summary>
    /// V18-45 / PPDO-55 — the AIP's own reservation ledger. Its own table, mirroring the WFP one
    /// above rather than generalising it (DECISION A); the WFP ledger takes a zero diff.
    /// </summary>
    public DbSet<AipDivisionAllocationLedger> AipDivisionAllocationLedgers { get; set; } = null!;

    public DbSet<ProcurementPreset> ProcurementPresets { get; set; } = null!;
    public DbSet<ProcurementPresetItem> ProcurementPresetItems { get; set; } = null!;

    // ── v1.7 Warehouse stock input (RAL-193) ─────────────────────────────────

    public DbSet<StockBalance> StockBalances { get; set; } = null!;


    // ── v1.8 New AIP| eSRE Codes (RAL-248) ─────────────────────────────────
    public DbSet<EsreCode> EsreCodes { get; set; } = null!;

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
