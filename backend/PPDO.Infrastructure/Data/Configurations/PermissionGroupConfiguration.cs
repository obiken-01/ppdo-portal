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

        // Unique index: group names are referenced by name in user creation logic.
        builder.HasIndex(g => g.Name)
            .IsUnique()
            .HasDatabaseName("IX_PermissionGroups_Name");

        // ── Seed data ─────────────────────────────────────────────────────────
        // Six default groups as defined in CLAUDE.md > PermissionGroup Seed Data.
        // Group names must not be changed after seeding.
        builder.HasData(
            new PermissionGroup
            {
                Id                 = AdminDivisionStaffId,
                Name               = "Admin Division Staff",
                Division           = Division.Admin,
                CanAccessInventory = true,
                CanAccessReports   = true,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            },
            new PermissionGroup
            {
                Id                 = PlanningStaffId,
                Name               = "Planning Staff",
                Division           = Division.Planning,
                CanAccessInventory = false,
                CanAccessReports   = true,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            },
            new PermissionGroup
            {
                Id                 = RmStaffId,
                Name               = "RM Staff",
                Division           = Division.RM,
                CanAccessInventory = false,
                CanAccessReports   = true,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            },
            new PermissionGroup
            {
                Id                 = MisStaffId,
                Name               = "MIS Staff",
                Division           = Division.MIS,
                CanAccessInventory = false,
                CanAccessReports   = true,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            },
            new PermissionGroup
            {
                Id                 = SpdStaffId,
                Name               = "SPD Staff",
                Division           = Division.SPD,
                CanAccessInventory = false,
                CanAccessReports   = true,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            },
            new PermissionGroup
            {
                Id                 = ObserverDefaultId,
                Name               = "Observer Default",
                Division           = null,   // spans no specific division
                CanAccessInventory = false,
                CanAccessReports   = false,
                CanManageUsers     = false,
                CreatedAt          = SeedDate,
                UpdatedAt          = SeedDate,
            }
        );
    }
}
