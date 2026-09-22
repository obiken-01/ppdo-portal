using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="AipExpenditure"/> (v1.8.0 Phase 2 — V18-33). snake_case table and
/// columns per <c>docs/NAMING_CONVENTIONS.md</c>; mirrors
/// <see cref="WfpExpenditureConfiguration"/>'s conventions without inheriting its scheduling
/// columns.
/// </summary>
public sealed class AipExpenditureConfiguration : IEntityTypeConfiguration<AipExpenditure>
{
    public void Configure(EntityTypeBuilder<AipExpenditure> builder)
    {
        builder.ToTable("aip_expenditures");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ActivityId)
            .HasColumnName("activity_id")
            .IsRequired();

        builder.Property(e => e.AccountId)
            .HasColumnName("account_id");

        builder.Property(e => e.AccountNumberSnapshot)
            .HasColumnName("account_number_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.AccountTitleSnapshot)
            .HasColumnName("account_title_snapshot")
            .HasMaxLength(300);

        builder.Property(e => e.FundingSourceId)
            .HasColumnName("funding_source_id");

        builder.Property(e => e.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.FundingSourceNameSnapshot)
            .HasColumnName("funding_source_name_snapshot")
            .HasMaxLength(100);

        // PESOS. This table has no thousands era — see the entity's remarks.
        builder.Property(e => e.Ps)
            .HasColumnName("ps")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Mooe)
            .HasColumnName("mooe")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Co)
            .HasColumnName("co")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Total)
            .HasColumnName("total")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // ── Concurrency + update tracking (V18-71 / PPDO-117) ─────────────────
        // created_at/updated_at above already existed; only the token and the actor are new.
        // This token also covers this line's procurement items, which are written only through
        // this row — see AipExpenditure.RowVersion for why that holds.
        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion();

        builder.Property(e => e.UpdatedById)
            .HasColumnName("updated_by_id");

        // Restrict — same reasoning as aip_activities.
        builder.HasOne(e => e.UpdatedBy)
            .WithMany()
            .HasForeignKey(e => e.UpdatedById)
            .HasConstraintName("FK_aip_expenditures_users_updated_by_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Every read is "this activity's lines", and V18-34's recompute runs on every write.
        builder.HasIndex(e => e.ActivityId)
            .HasDatabaseName("IX_aip_expenditures_activity_id");

        // Cascade: a line cannot outlive its activity.
        builder.HasOne(e => e.Activity)
            .WithMany()
            .HasForeignKey(e => e.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, deliberately: retiring a config row must not silently delete recorded money.
        // The snapshot columns are what keep a historical line readable after such a change, so
        // the FK's job here is only to point at the live row while it exists.
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FundingSource)
            .WithMany()
            .HasForeignKey(e => e.FundingSourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
