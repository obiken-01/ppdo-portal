using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// EF mapping for <see cref="PartnerApiKey"/> (v1.8.0 — PPDO-15). snake_case table and columns
/// per <c>docs/NAMING_CONVENTIONS.md</c>. See <c>docs/v1.8/External_AIP_API_Spec.md</c> §5.
/// </summary>
public sealed class PartnerApiKeyConfiguration : IEntityTypeConfiguration<PartnerApiKey>
{
    public void Configure(EntityTypeBuilder<PartnerApiKey> builder)
    {
        builder.ToTable("partner_api_keys");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).HasColumnName("id");

        builder.Property(k => k.PartnerName)
            .HasColumnName("partner_name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(k => k.KeyPrefix)
            .HasColumnName("key_prefix")
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(k => k.KeyHash)
            .HasColumnName("key_hash")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(k => k.AllOffices)
            .HasColumnName("all_offices")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(k => k.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(k => k.LastUsedAt)
            .HasColumnName("last_used_at");

        builder.Property(k => k.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(k => k.RevokedById)
            .HasColumnName("revoked_by_id");

        builder.Property(k => k.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(k => k.CreatedById)
            .HasColumnName("created_by_id")
            .IsRequired();

        // The auth lookup on every external call — must resolve a single row from the prefix alone.
        builder.HasIndex(k => k.KeyPrefix)
            .IsUnique()
            .HasDatabaseName("UX_partner_api_keys_key_prefix");

        // Restrict, matching AuditLogConfiguration: never delete an admin who has issued or
        // revoked a key still on record.
        builder.HasOne(k => k.CreatedBy)
            .WithMany()
            .HasForeignKey(k => k.CreatedById)
            .HasConstraintName("FK_partner_api_keys_Users_created_by")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(k => k.RevokedBy)
            .WithMany()
            .HasForeignKey(k => k.RevokedById)
            .HasConstraintName("FK_partner_api_keys_Users_revoked_by")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
