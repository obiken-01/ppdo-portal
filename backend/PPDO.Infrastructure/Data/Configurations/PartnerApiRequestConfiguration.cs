using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="PartnerApiRequest"/> (v1.8.0 — PPDO-15/PPDO-13). snake_case table
/// and columns per <c>docs/NAMING_CONVENTIONS.md</c>. See
/// <c>docs/v1.8/External_AIP_API_Spec.md</c> §5.
/// </summary>
public sealed class PartnerApiRequestConfiguration : IEntityTypeConfiguration<PartnerApiRequest>
{
    public void Configure(EntityTypeBuilder<PartnerApiRequest> builder)
    {
        builder.ToTable("partner_api_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");  // bigint identity

        builder.Property(r => r.KeyId)
            .HasColumnName("key_id")
            .IsRequired();

        builder.Property(r => r.RequestedAt)
            .HasColumnName("requested_at")
            .IsRequired();

        builder.Property(r => r.Route)
            .HasColumnName("route")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.OfficeCode)
            .HasColumnName("office_code")
            .HasMaxLength(20);

        builder.Property(r => r.FiscalYear)
            .HasColumnName("fiscal_year");

        builder.Property(r => r.StatusCode)
            .HasColumnName("status_code")
            .IsRequired();

        // The usage list: one key's calls, newest first.
        builder.HasIndex(r => new { r.KeyId, r.RequestedAt })
            .HasDatabaseName("IX_partner_api_requests_key_id_requested_at");

        // Cascade: a request log row is meaningless once its key is gone. Keys are never
        // hard-deleted in practice (revoke, not delete), but nothing enforces that at the DB
        // level, so the FK still needs an explicit behaviour.
        builder.HasOne(r => r.Key)
            .WithMany(k => k.Requests)
            .HasForeignKey(r => r.KeyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
