using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary><c>aip_saip_changes</c> — an approved SAIP's before → after list (filled by T3).</summary>
public sealed class AipSaipChangeConfiguration : IEntityTypeConfiguration<AipSaipChange>
{
    public void Configure(EntityTypeBuilder<AipSaipChange> builder)
    {
        builder.ToTable("aip_saip_changes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.SaipId).HasColumnName("saip_id").IsRequired();
        builder.Property(c => c.NodeType).HasColumnName("node_type").IsRequired().HasMaxLength(20);
        builder.Property(c => c.NodeId).HasColumnName("node_id").IsRequired();
        builder.Property(c => c.Change).HasColumnName("change").IsRequired().HasMaxLength(20);
        builder.Property(c => c.Field).HasColumnName("field").HasMaxLength(100);
        builder.Property(c => c.Before).HasColumnName("before");
        builder.Property(c => c.After).HasColumnName("after");

        builder.HasIndex(c => c.SaipId).HasDatabaseName("IX_aip_saip_changes_saip_id");

        builder.HasOne(c => c.Saip)
            .WithMany()
            .HasForeignKey(c => c.SaipId)
            .HasConstraintName("FK_aip_saip_changes_aip_saips_saip_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
