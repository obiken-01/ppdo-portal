using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class ResourceLinkConfiguration : IEntityTypeConfiguration<ResourceLink>
{
    // Fixed seed GUIDs — deterministic prefix 30000000.
    // Must never change after the migration is applied.
    private static Guid LinkId(int n) => new($"30000000-0000-0000-0000-{n:D12}");

    // Fixed timestamp so re-running the seed is idempotent.
    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Placeholder URL — replace with real Google Drive / Sheet URLs before first production deploy.
    private const string Placeholder = "https://placeholder.example.com";

    public void Configure(EntityTypeBuilder<ResourceLink> builder)
    {
        builder.ToTable("ResourceLinks");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Url)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(r => r.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(r => r.IsAdminCreated)
            .IsRequired();

        // Unidirectional FK — no inverse collection on User.
        // SetNull: deactivating/deleting a user retains the link but clears the submitter FK.
        builder.HasOne(r => r.SubmittedBy)
            .WithMany()
            .HasForeignKey(r => r.SubmittedById)
            .HasConstraintName("FK_ResourceLinks_Users_SubmittedById")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(r => new { r.Category, r.LinkOrder })
            .HasDatabaseName("IX_ResourceLinks_Category_LinkOrder");

        builder.HasIndex(r => r.IsActive)
            .HasDatabaseName("IX_ResourceLinks_IsActive");

        // ── Seed data — 23 default resource links ─────────────────────────────
        // Source: CLAUDE.md > Resource Links — Default Seed Data
        // All IsAdminCreated = true, IsActive = true, SubmittedById = null.
        // URLs are placeholders — replace before production deploy.
        builder.HasData(

            // ── Supply & Property Management (CategoryOrder = 1) ──────────────
            Link(1,  "Inventory of Supplies/Property & Equipment", "Supply & Property Management", 1, 1),
            Link(2,  "PPDO Transactions Tracker",                  "Supply & Property Management", 1, 2),
            Link(3,  "PPMP",                                        "Supply & Property Management", 1, 3),
            Link(4,  "PR Monitoring",                               "Supply & Property Management", 1, 4),

            // ── Records Management (CategoryOrder = 2) ────────────────────────
            Link(5,  "Administrative Division Files", "Records Management", 2, 1),
            Link(6,  "Calendar of Activities",         "Records Management", 2, 2),
            Link(7,  "PDC Files",                      "Records Management", 2, 3),
            Link(8,  "Planning Division Files",        "Records Management", 2, 4),
            Link(9,  "RMED Files",                     "Records Management", 2, 5),
            Link(10, "Incoming Communications",        "Records Management", 2, 6),

            // ── Human Resource Management (CategoryOrder = 3) ─────────────────
            Link(11, "Personnel Profile",      "Human Resource Management", 3, 1),
            Link(12, "201 Files",              "Human Resource Management", 3, 2),
            Link(13, "IPCR/DPCR",             "Human Resource Management", 3, 3),
            Link(14, "Leave",                  "Human Resource Management", 3, 4),
            Link(15, "Training/s Attended",    "Human Resource Management", 3, 5),

            // ── Financial Management (CategoryOrder = 4) ──────────────────────
            Link(16, "WFP",                            "Financial Management", 4, 1),
            Link(17, "AIP",                            "Financial Management", 4, 2),
            Link(18, "SAIP",                           "Financial Management", 4, 3),
            Link(19, "GAD WFP",                        "Financial Management", 4, 4),
            Link(20, "20% Development Funds Report",   "Financial Management", 4, 5),

            // ── General (CategoryOrder = 5) ───────────────────────────────────
            Link(21, "E-Directory",         "General", 5, 1),
            Link(22, "Organizational Chart", "General", 5, 2),
            Link(23, "Citizen's Charter",    "General", 5, 3)
        );
    }

    private static ResourceLink Link(
        int n,
        string title,
        string category,
        int categoryOrder,
        int linkOrder) => new()
    {
        Id             = LinkId(n),
        Title          = title,
        Url            = Placeholder,
        Category       = category,
        CategoryOrder  = categoryOrder,
        LinkOrder      = linkOrder,
        IsActive       = true,
        IsAdminCreated = true,
        SubmittedById  = null,
        CreatedAt      = SeedDate,
        UpdatedAt      = SeedDate,
    };
}
