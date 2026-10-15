using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for <see cref="Notification"/> (v1.9.0 — PPDO-221). snake_case: a new table.
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").IsRequired().HasMaxLength(40);
        builder.Property(e => e.Text).HasColumnName("text").IsRequired().HasMaxLength(400);
        builder.Property(e => e.Link).HasColumnName("link").IsRequired().HasMaxLength(400);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.ReadAt).HasColumnName("read_at");

        // Serves the user_id FK, the unread count (user_id, read_at IS NULL, created_at >= since) and
        // the newest-first page (spec §5).
        builder.HasIndex(e => new { e.UserId, e.ReadAt, e.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_notifications_user_read_created");

        // A user's feed goes with the user. Users are deactivated rather than deleted in practice, so
        // this cascade is a tidy-up, not a path anything relies on.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_notifications_Users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
