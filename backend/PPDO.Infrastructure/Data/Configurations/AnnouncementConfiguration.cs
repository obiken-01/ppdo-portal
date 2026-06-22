using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Content)
            .HasColumnName("content")
            .IsRequired();  // nvarchar(max) — no HasMaxLength → EF maps to nvarchar(max)

        // NOTE: intentionally NO .HasDefaultValue() on Status — avoids EF sentinel trap
        // (explicit Draft=0 inserts would be silently stored as the default if a model default were set)
        builder.Property(a => a.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(a => a.PublishedAt)
            .HasColumnName("published_at");

        builder.Property(a => a.CreatedById)
            .HasColumnName("created_by_id");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasOne(a => a.CreatedBy)
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .HasConstraintName("FK_announcements_Users_created_by_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Status)
            .HasDatabaseName("IX_announcements_status");

        builder.HasIndex(a => a.PublishedAt)
            .HasDatabaseName("IX_announcements_published_at");
    }
}
