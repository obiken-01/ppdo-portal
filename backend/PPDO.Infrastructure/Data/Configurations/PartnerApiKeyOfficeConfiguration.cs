using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="PartnerApiKeyOffice"/> (v1.8.0 — PPDO-15) — a plain join row, so it
/// takes the composite PK the spec calls for (<c>docs/v1.8/External_AIP_API_Spec.md</c> §5) rather
/// than a surrogate id.
/// </summary>
public sealed class PartnerApiKeyOfficeConfiguration : IEntityTypeConfiguration<PartnerApiKeyOffice>
{
    public void Configure(EntityTypeBuilder<PartnerApiKeyOffice> builder)
    {
        builder.ToTable("partner_api_key_offices");

        builder.HasKey(o => new { o.KeyId, o.OfficeId });

        builder.Property(o => o.KeyId).HasColumnName("key_id");
        builder.Property(o => o.OfficeId).HasColumnName("office_id");

        // Cascade: the scope row cannot outlive the key it scopes.
        builder.HasOne(o => o.Key)
            .WithMany(k => k.Offices)
            .HasForeignKey(o => o.KeyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching every other Office FK in this codebase: offices are soft-deleted
        // (IsActive) only, never hard-deleted, so this never actually fires — it documents that
        // invariant rather than working around its absence.
        builder.HasOne(o => o.Office)
            .WithMany()
            .HasForeignKey(o => o.OfficeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
