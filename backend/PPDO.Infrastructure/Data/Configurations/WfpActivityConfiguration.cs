using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpActivityConfiguration : IEntityTypeConfiguration<WfpActivity>
{
    public void Configure(EntityTypeBuilder<WfpActivity> builder)
    {
        builder.ToTable("wfp_activities");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");

        builder.Property(w => w.WfpId)
            .HasColumnName("wfp_id")
            .IsRequired();

        builder.Property(w => w.AipActivityId)
            .HasColumnName("aip_activity_id")
            .IsRequired();

        builder.HasIndex(w => new { w.WfpId, w.AipActivityId })
            .IsUnique()
            .HasDatabaseName("UX_wfp_activities_wfp_id_aip_activity_id");

        builder.HasIndex(w => w.WfpId)
            .HasDatabaseName("IX_wfp_activities_wfp_id");

        builder.HasIndex(w => w.AipActivityId)
            .HasDatabaseName("IX_wfp_activities_aip_act_id");

        // Cascade: deleting a WFP record removes its activities and expenditure lines.
        builder.HasOne(w => w.Wfp)
            .WithMany(r => r.Activities)
            .HasForeignKey(w => w.WfpId)
            .HasConstraintName("FK_wfp_activities_wfp_records_wfp_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: an AIP activity cannot be deleted while included in a WFP.
        builder.HasOne(w => w.AipActivity)
            .WithMany()
            .HasForeignKey(w => w.AipActivityId)
            .HasConstraintName("FK_wfp_activities_aip_activities_aip_activity_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
