using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// <c>aip_saips</c> (v1.10.0 — PPDO-246, <c>docs/v1.10/SAIP_Spec.md</c> §5). See <see cref="AipSaip"/>
/// for why it keys on (record, config office) rather than one <c>aip_offices</c> row.
/// </summary>
public sealed class AipSaipConfiguration : IEntityTypeConfiguration<AipSaip>
{
    public void Configure(EntityTypeBuilder<AipSaip> builder)
    {
        builder.ToTable("aip_saips", t => t.HasCheckConstraint(
            "CK_aip_saips_status",
            $"[status] IN ({string.Join(", ", SaipStatus.All.Select(s => $"'{s}'"))})"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.AipRecordId).HasColumnName("aip_record_id").IsRequired();
        builder.Property(s => s.OfficeId).HasColumnName("office_id").IsRequired();
        builder.Property(s => s.Number).HasColumnName("number").IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue(SaipStatus.Draft);

        builder.Property(s => s.LetterFileKey).HasColumnName("letter_file_key").HasMaxLength(500);
        builder.Property(s => s.LetterFileName).HasColumnName("letter_file_name").HasMaxLength(255);
        builder.Property(s => s.LetterContentType).HasColumnName("letter_content_type").HasMaxLength(100);
        builder.Property(s => s.LetterSizeBytes).HasColumnName("letter_size_bytes");

        builder.Property(s => s.StartedById).HasColumnName("started_by_id").IsRequired();
        builder.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(s => s.SubmittedById).HasColumnName("submitted_by_id");
        builder.Property(s => s.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(s => s.DecidedById).HasColumnName("decided_by_id");
        builder.Property(s => s.DecidedAt).HasColumnName("decided_at");
        builder.Property(s => s.CancelledById).HasColumnName("cancelled_by_id");
        builder.Property(s => s.CancelledAt).HasColumnName("cancelled_at");

        builder.Property(s => s.RowVersion).HasColumnName("row_version").IsRowVersion();

        // SAIP 1, 2, … per office and fiscal year (decision 2).
        builder.HasIndex(s => new { s.AipRecordId, s.OfficeId, s.Number })
            .IsUnique()
            .HasDatabaseName("UX_aip_saips_record_office_number");

        // ⚠️ At most ONE open SAIP per office and year (decision 2). The service checks first so it
        // can name the open one; this index is what holds when two starts race.
        // ⚠️ "<> AND <>", not NOT IN: SQL Server refuses NOT IN in a filtered index predicate
        // ("Incorrect syntax near 'NOT'"), found by SqlServerTestDatabase building the model.
        builder.HasIndex(s => new { s.AipRecordId, s.OfficeId })
            .IsUnique()
            .HasFilter(string.Join(" AND ", SaipStatus.Closed.Select(s => $"[status] <> '{s}'")))
            .HasDatabaseName("UX_aip_saips_one_open_per_office");

        // Restrict, not Cascade: a fiscal year with SAIP history must not vanish with one delete,
        // and a config office must never take its SAIPs with it.
        builder.HasOne(s => s.AipRecord)
            .WithMany()
            .HasForeignKey(s => s.AipRecordId)
            .HasConstraintName("FK_aip_saips_aip_records_aip_record_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Office)
            .WithMany()
            .HasForeignKey(s => s.OfficeId)
            .HasConstraintName("FK_aip_saips_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.StartedById)
            .HasConstraintName("FK_aip_saips_users_started_by_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.SubmittedById)
            .HasConstraintName("FK_aip_saips_users_submitted_by_id").OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.DecidedById)
            .HasConstraintName("FK_aip_saips_users_decided_by_id").OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CancelledById)
            .HasConstraintName("FK_aip_saips_users_cancelled_by_id").OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
