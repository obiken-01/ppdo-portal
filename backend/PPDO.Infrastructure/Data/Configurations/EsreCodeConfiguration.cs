using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations
{
    /// <summary>
    /// snake_case mapping for <see cref="EsreCode"/> (RAL-248) — new tables use snake_case
    /// columns mapped from PascalCase properties, per docs/NAMING_CONVENTIONS.md.
    /// The unique index on <c>code</c> is what stops a second "SS" row being created.
    /// </summary>
    public sealed class EsreCodeConfiguration : IEntityTypeConfiguration<EsreCode>
    {
        public void Configure(EntityTypeBuilder<EsreCode> builder)
        {
            builder.ToTable("esre_codes");

            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).HasColumnName("id");

            builder.Property(t => t.Code)
                .HasColumnName("code")
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(t => t.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(t => t.Description)
                .HasColumnName("description");  // nvarchar(max), nullable

            builder.Property(t => t.IsActive)
                .HasColumnName("is_active")
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(t => t.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("GETUTCDATE()");

            builder.Property(t => t.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("GETUTCDATE()");

            builder.HasIndex(t => t.Code)
                .IsUnique()
                .HasDatabaseName("IX_esre_codes_code");
        }
    }
}