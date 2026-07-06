using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class LdipProgramConfiguration : IEntityTypeConfiguration<LdipProgram>
{
    public void Configure(EntityTypeBuilder<LdipProgram> builder)
    {
        builder.ToTable("ldip_programs");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.LdipOfficeId)
            .HasColumnName("ldip_office_id")
            .IsRequired();

        builder.Property(p => p.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(500);

        // Stored in thousands (₱000), like AIP totals.
        builder.Property(p => p.Budget)
            .HasColumnName("budget")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // ── RAL-113 detail columns (upload-only; null for manually-added programs) ──
        // Several of these are genuinely open-ended, potentially multi-value free
        // text in the real file (e.g. CcTypologyCode has held comma/newline-separated
        // lists of 15+ codes, FundingSourceSnapshot has held combined strings like
        // "GF, LDRRMF, GADF, NGA (ER 1-94)") — a fixed-size guess already truncated
        // real data twice. Left unbounded (nvarchar(max)), matching how AIP treats
        // its own equally open-ended ImplementingOffice/ExpectedOutputs/
        // FundingSourceSnapshot columns. Only StartDate/EndDate (bare year strings)
        // are genuinely bounded.

        builder.Property(p => p.ImplementingOffice)
            .HasColumnName("implementing_office");  // nvarchar(max) — can combine multiple offices, e.g. "PGO Housing, PEO"

        builder.Property(p => p.StartDate)
            .HasColumnName("start_date")
            .HasMaxLength(50);

        builder.Property(p => p.EndDate)
            .HasColumnName("end_date")
            .HasMaxLength(50);

        builder.Property(p => p.ExpectedOutputs)
            .HasColumnName("expected_outputs");  // nvarchar(max), nullable

        builder.Property(p => p.FundingSourceId)
            .HasColumnName("funding_source_id");

        builder.Property(p => p.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot");  // nvarchar(max) — can list multiple funding sources

        builder.Property(p => p.Ps)
            .HasColumnName("ps")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Mooe)
            .HasColumnName("mooe")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Co)
            .HasColumnName("co")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcAdaptation)
            .HasColumnName("cc_adaptation")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcMitigation)
            .HasColumnName("cc_mitigation")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcTypologyCode)
            .HasColumnName("cc_typology_code");  // nvarchar(max) — can be a long comma/newline-separated list of codes

        builder.Property(p => p.PdpRdp)
            .HasColumnName("pdp_rdp");  // nvarchar(max)

        builder.Property(p => p.Sdgs)
            .HasColumnName("sdgs");  // nvarchar(max)

        builder.Property(p => p.SendaiFramework)
            .HasColumnName("sendai_framework");  // nvarchar(max)

        builder.Property(p => p.NdrrmPlan)
            .HasColumnName("ndrrm_plan");  // nvarchar(max)

        builder.Property(p => p.Nsp)
            .HasColumnName("nsp");  // nvarchar(max)

        builder.Property(p => p.Pdpdfp)
            .HasColumnName("pdpdfp");  // nvarchar(max)

        builder.HasIndex(p => new { p.LdipOfficeId, p.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_ldip_programs_ldip_office_id_ref_code");

        builder.HasIndex(p => p.LdipOfficeId)
            .HasDatabaseName("IX_ldip_programs_ldip_office_id");

        // Cascade: part of the LDIP hierarchy chain (ldip_records → ldip_offices → ldip_programs).
        builder.HasOne(p => p.Office)
            .WithMany(o => o.Programs)
            .HasForeignKey(p => p.LdipOfficeId)
            .HasConstraintName("FK_ldip_programs_ldip_offices_ldip_office_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(p => p.FundingSource)
            .WithMany()
            .HasForeignKey(p => p.FundingSourceId)
            .HasConstraintName("FK_ldip_programs_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
