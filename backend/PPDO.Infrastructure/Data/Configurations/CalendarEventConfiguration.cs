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

        // Approval workflow (v1.1.1, RAL-82).
        // NOTE: intentionally NO .HasDefaultValue() on Status. The CLR default of the enum is
        // Pending(0); a model-level default of Approved would make EF treat an explicit
        // Status = Pending on insert as "unset" (store-generated sentinel) and silently persist
        // Approved instead — which would break RAL-84's "non-admin Office event -> Pending".
        // Existing rows are backfilled to Approved via defaultValue:1 in the migration instead.
        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.RejectionReason)
            .HasMaxLength(500);

        // FK → Users — SetNull so deleting a user does not cascade-delete their events.
        builder.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .HasConstraintName("FK_CalendarEvents_Users_CreatedById")
            .OnDelete(DeleteBehavior.Restrict);

        // FK → Users (reviewer). Restrict so reviewer accounts can't be deleted out from under
        // a reviewed event. Nullable — null until an admin reviews.
        builder.HasOne(e => e.ReviewedBy)
            .WithMany()
            .HasForeignKey(e => e.ReviewedById)
            .HasConstraintName("FK_CalendarEvents_Users_ReviewedById")
            .OnDelete(DeleteBehavior.Restrict);

        // Index for efficient date-range queries (the most common query pattern).
        builder.HasIndex(e => e.StartDate)
            .HasDatabaseName("IX_CalendarEvents_StartDate");

        builder.HasIndex(e => new { e.EventType, e.StartDate })
            .HasDatabaseName("IX_CalendarEvents_EventType_StartDate");

        // Index for the pending-events admin queue (RAL-84 GetPendingEventsAsync).
        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_CalendarEvents_Status");
    }
}
