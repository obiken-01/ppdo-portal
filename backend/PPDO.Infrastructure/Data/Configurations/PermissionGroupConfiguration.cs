using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    // Fixed seed GUIDs — deterministic and human-readable.
    // These IDs must never change after the initial migration is applied.
    private static readonly Guid AdminDivisionStaffId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PlanningStaffId      = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid RmStaffId            = new("10000000-0000-0000-0000-000000000003");
    private static readonly Guid MisStaffId           = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid SpdStaffId           = new("10000000-0000-0000-0000-000000000005");
    private static readonly Guid ObserverDefaultId    = new("10000000-0000-0000-0000-000000000006");
    private static readonly Guid OfficeUserDefaultId  = new("10000000-0000-0000-0000-000000000007");

    // Fixed timestamp so re-running the seed is idempotent.
    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PermissionGroup> builder)
    {
        builder.ToTable("PermissionGroups");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(100);

        // Division is nullable — the Observer Default group has no specific division.
        builder.Property(g => g.Division);

        builder.Property(g => g.Description);  // nvarchar(max), nullable

        builder.Property(g => g.CanAccessInventory)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(g => g.CanAccessReports)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(g => g.CanManageUsers)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(g => g.CanManageResourceLinks)
            .IsRequired()
            .HasDefaultValue(false);

        // ── Budget Planning flags (v1.1 — RAL-81) ─────────────────────────────
        builder.Property(g => g.CanAccessBudgetPlanning)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(g => g.CanUploadAip)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(g => g.CanManageConfig)
            .IsRequired()
            .HasDefaultValue(false);

        // Unique index: group names are referenced by name in user creation logic.
        builder.HasIndex(g => g.Name)
            .IsUnique()
            .HasDatabaseName("IX_PermissionGroups_Name");

        // ── Seed data ─────────────────────────────────────────────────────────
        // Six default groups as defined in CLAUDE.md > PermissionGroup Seed Data.
        // Group names must not be changed after seeding.
        // v1.1 (RAL-81): PPDO division groups gain CanAccessBudgetPlanning = true
        // (PPDO Staff get full budget-planning access per User_Roles_Permissions.md §8).
        // CanUploadAip / CanManageConfig default false — Admin role always has them, and
        // specific PPDO staff are granted them via per-user overrides.
        builder.HasData(
            new PermissionGroup
            {
                Id                     = AdminDivisionStaffId,
                Name                   = "Admin Division Staff",
                Division               = Division.Admin,
                CanAccessInventory     = true,
                CanAccessReports       = true,
                CanManageUsers         = false,
                CanManageResourceLinks = true,   // Admin division staff manage all resource links
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            new PermissionGroup
            {
                Id                     = PlanningStaffId,
                Name                   = "Planning Staff",
                Division               = Division.Planning,
                CanAccessInventory     = false,
                CanAccessReports       = true,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            new PermissionGroup
            {
                Id                     = RmStaffId,
                Name                   = "RM Staff",
                Division               = Division.RM,
                CanAccessInventory     = false,
                CanAccessReports       = true,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            new PermissionGroup
            {
                Id                     = MisStaffId,
                Name                   = "MIS Staff",
                Division               = Division.MIS,
                CanAccessInventory     = false,
                CanAccessReports       = true,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            new PermissionGroup
            {
                Id                     = SpdStaffId,
                Name                   = "SPD Staff",
                Division               = Division.SPD,
                CanAccessInventory     = false,
                CanAccessReports       = true,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            new PermissionGroup
            {
                Id                     = ObserverDefaultId,
                Name                   = "Observer Default",
                Division               = null,   // spans no specific division
                CanAccessInventory     = false,
                CanAccessReports       = false,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = false,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            },
            // v1.1 — the only group for non-PPDO office users (encoder or viewer).
            // Budget Planning is their sole feature; everything else is false.
            new PermissionGroup
            {
                Id                     = OfficeUserDefaultId,
                Name                   = "Office User Default",
                Division               = null,   // office users have no PPDO division
                CanAccessInventory     = false,
                CanAccessReports       = false,
                CanManageUsers         = false,
                CanManageResourceLinks = false,
                CanAccessBudgetPlanning = true,
                CanUploadAip           = false,
                CanManageConfig        = false,
                CreatedAt              = SeedDate,
                UpdatedAt              = SeedDate,
            }
        );
    }
}
