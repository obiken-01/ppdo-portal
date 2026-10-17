using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// The shared half of every job table (v1.9.0 — PPDO-228, spec §5). snake_case: new tables.
///
/// <para>
/// <b>TPC, table-per-concrete-type.</b> <see cref="BackgroundJob"/> is abstract and gets no table. Each
/// concrete kind maps to its own table, and every column configured here appears in each of them. A new
/// kind adds a class and its own configuration (see <see cref="InitialWfpExportJobConfiguration"/>);
/// nothing here changes.
/// </para>
///
/// <para>
/// ⚠️ <b>Ids come from one sequence, <c>background_job_ids</c>, not an identity column.</b> An identity is
/// per table, so two kinds would both have a job 1, and EF, which tracks a <see cref="BackgroundJob"/> by
/// id alone, could not tell them apart. A shared sequence keeps every id unique across all job tables.
/// </para>
///
/// <para>
/// The <c>requested_by_id</c> foreign key is left to EF's default name. Under TPC it is created once per
/// job table, and SQL Server needs each constraint name to be unique, which the default
/// (<c>FK_{table}_Users_requested_by_id</c>) already is.
/// </para>
/// </summary>
public sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public const string IdSequence = "background_job_ids";

    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.UseTpcMappingStrategy();

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseSequence(IdSequence);

        builder.Property(e => e.Status).HasColumnName("status")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.RequestedById).HasColumnName("requested_by_id").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(e => e.HeartbeatAt).HasColumnName("heartbeat_at");
        builder.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(400);
        builder.Property(e => e.FinishedAt).HasColumnName("finished_at");

        // Restrict: users are deactivated, not deleted, and a job row is history (decision 12).
        builder.HasOne(e => e.RequestedBy)
            .WithMany()
            .HasForeignKey(e => e.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
