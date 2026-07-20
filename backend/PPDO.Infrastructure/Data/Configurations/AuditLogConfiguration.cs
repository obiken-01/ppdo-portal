using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");  // bigint identity

        builder.Property(a => a.TableName)
            .HasColumnName("table_name")
            .IsRequired()
            .HasMaxLength(100);

        // Not a real FK — points into whichever table TableName names. Nullable because
        // exactly one of RecordId/RecordGuid is set, depending on the table's PK type.
        builder.Property(a => a.RecordId)
            .HasColumnName("record_id");

        builder.Property(a => a.RecordGuid)
            .HasColumnName("record_guid");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(a => a.ChangedById)
            .HasColumnName("changed_by")
            .IsRequired();

        builder.Property(a => a.ChangedAt)
            .HasColumnName("changed_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(a => a.OldValues)
            .HasColumnName("old_values");  // nvarchar(max) JSON, nullable

        builder.Property(a => a.NewValues)
            .HasColumnName("new_values");  // nvarchar(max) JSON, nullable

        builder.HasIndex(a => new { a.TableName, a.RecordId })
            .HasDatabaseName("IX_audit_log_table_record");

        builder.HasIndex(a => new { a.TableName, a.RecordGuid })
            .HasDatabaseName("IX_audit_log_table_record_guid");

        builder.HasIndex(a => a.ChangedAt)
            .HasDatabaseName("IX_audit_log_changed_at");

        // Restrict: never delete a user who has audit history.
        builder.HasOne(a => a.ChangedBy)
            .WithMany()
            .HasForeignKey(a => a.ChangedById)
            .HasConstraintName("FK_audit_log_Users_changed_by")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
