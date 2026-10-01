using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations
{
    /// <summary>
    /// snake_case mapping for <see cref="InvestmentPlanningSettings"/> (PPDO-136) — a new table,
    /// so snake_case columns per docs/NAMING_CONVENTIONS.md.
    ///
    /// <b>Single row, enforced twice:</b> the id is not an identity column and the migration seeds
    /// row 1, and <c>CK_investment_planning_settings_singleton</c> rejects any other id — so a
    /// second settings row cannot exist to disagree with the first.
    /// </summary>
    public sealed class InvestmentPlanningSettingsConfiguration : IEntityTypeConfiguration<InvestmentPlanningSettings>
    {
        public void Configure(EntityTypeBuilder<InvestmentPlanningSettings> builder)
        {
            builder.ToTable("investment_planning_settings", t => t.HasCheckConstraint(
                "CK_investment_planning_settings_singleton",
                $"[id] = {InvestmentPlanningSettings.SingletonId}"));

            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(s => s.DefaultFiscalYear)
                .HasColumnName("default_fiscal_year");   // int, nullable — null = unset

            // Investment proposal signatory defaults (PPDO-154). Nullable, no backfill.
            builder.Property(s => s.PpdcName).HasColumnName("ppdc_name").HasMaxLength(200);
            builder.Property(s => s.PpdcPosition).HasColumnName("ppdc_position").HasMaxLength(200);
            builder.Property(s => s.LceName).HasColumnName("lce_name").HasMaxLength(200);
            builder.Property(s => s.LcePosition).HasColumnName("lce_position").HasMaxLength(200);

            builder.Property(s => s.UpdatedAt)
                .HasColumnName("updated_at");            // datetime2, nullable — null until first set

            builder.Property(s => s.UpdatedById)
                .HasColumnName("updated_by_user_id");

            // Unidirectional FK — no inverse collection on User. SetNull: deleting a user keeps
            // the setting and only clears who last changed it.
            builder.HasOne(s => s.UpdatedBy)
                .WithMany()
                .HasForeignKey(s => s.UpdatedById)
                .HasConstraintName("FK_investment_planning_settings_users_updated_by_user_id")
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            // The one row, unset. Ships as today's behaviour on every page until someone sets it.
            builder.HasData(new InvestmentPlanningSettings
            {
                Id                = InvestmentPlanningSettings.SingletonId,
                DefaultFiscalYear = null,
                UpdatedAt         = null,
                UpdatedById       = null,
            });
        }
    }
}
