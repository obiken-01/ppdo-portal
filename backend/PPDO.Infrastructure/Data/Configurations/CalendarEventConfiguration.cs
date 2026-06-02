using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("CalendarEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.StartDate)
            .IsRequired();

        builder.Property(e => e.IsAllDay)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.EventType)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Office");

        // FK → Users — SetNull so deleting a user does not cascade-delete their events.
        builder.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .HasConstraintName("FK_CalendarEvents_Users_CreatedById")
            .OnDelete(DeleteBehavior.Restrict);

        // Index for efficient date-range queries (the most common query pattern).
        builder.HasIndex(e => e.StartDate)
            .HasDatabaseName("IX_CalendarEvents_StartDate");

        builder.HasIndex(e => new { e.EventType, e.StartDate })
            .HasDatabaseName("IX_CalendarEvents_EventType_StartDate");
    }
}
