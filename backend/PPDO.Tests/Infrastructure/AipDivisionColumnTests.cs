using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The AIP is scoped by OFFICE, never by division (tracker B12-b).
///
/// <para>
/// ↩️ <b>Was <c>AipRecordShapeTests</c>, trimmed 2026-09-05 (PPDO-61).</b> Five tests pinned
/// <c>AipRecord.OfficeId</c> — its FK, its optionality, its restrict behaviour and its index — and
/// went with the column when the office-owned shape was withdrawn. The two that remain were never
/// about that shape: they are the B12-b decision, which is unchanged and still the thing most
/// likely to be undone by accident.
/// </para>
///
/// <para>
/// These are relational-mapping assertions rather than behaviour, because the thing worth pinning
/// is a <b>structural decision</b> — one that is easy to undo by accident, in a one-line change
/// that compiles and passes every behavioural test. No database is opened.
/// </para>
/// </summary>
public sealed class AipDivisionColumnTests
{
    private static IModel Model()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;

        using AppDbContext context = new(options);
        return context.Model;
    }

    private static IEntityType Entity<T>() => Model().FindEntityType(typeof(T))!;

    // ── ⚠️ The decision this file mainly exists for ───────────────────────────

    [Fact]
    public void AipOffice_HasNoDivisionColumn_AndMustNeverGrowOne()
    {
        // ⚠️ tracker B12-b, 2026-08-26. A division column is the FIRST thing this shape looks like
        // it wants, and it is explicitly ruled out: PPDO is an ORDINARY office, and its division of
        // work is carried on the PROGRAM through ProgramDivision, exactly as WFP does.
        //
        // The alternative — one AIP record per PPDO division — would make PPDO structurally unlike
        // all 18 other offices, and every downstream feature would carry two code paths forever.
        //
        // A sub-unit that genuinely prints is an AipOffice row SHARING the office ref code,
        // distinguished by (Sector, Name). That is already built and is how the province encodes.
        IEntityType aipOffice = Entity<AipOffice>();

        Assert.DoesNotContain(aipOffice.GetProperties(),
            p => p.Name.Contains("Division", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(aipOffice.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Division));
    }

    [Fact]
    public void AipRecord_HasNoDivisionColumnEither()
    {
        // Same rule one level up: the record is owned by an OFFICE, never by a division.
        IEntityType aipRecord = Entity<AipRecord>();

        Assert.DoesNotContain(aipRecord.GetProperties(),
            p => p.Name.Contains("Division", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(aipRecord.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Division));
    }

    // ── The one place a division IS carried: the activity (PPDO-130) ──────────

    [Fact]
    public void AipActivity_CarriesAnOptionalDivision_RestrictedOnDelete()
    {
        // PPDO-130 (Division_Submit_Spec.md decision 1) tags each ACTIVITY with the division whose
        // work it is. This does not relax the two rules above: the office and the record stay
        // division-free. This says who inside the office owns one leaf.
        //
        // ⚠️ Optional, because untagged is permanent for offices without divisions and FY ≤ 2027.
        // Restrict, because divisions are soft-deleted and a hard delete must not orphan the tags.
        IForeignKey fk = Assert.Single(Entity<AipActivity>().GetForeignKeys(),
            f => f.PrincipalEntityType.ClrType == typeof(Division));

        Assert.False(fk.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Equal("division_id", Assert.Single(fk.Properties).GetColumnName());
    }

    [Fact]
    public void AipDivisionSubmission_IsUniquePerRecordAndDivision()
    {
        // One row per division per fiscal year: a second submit updates the row rather than
        // adding a second one that disagrees with it.
        IEntityType submission = Entity<AipDivisionSubmission>();

        IIndex unique = Assert.Single(submission.GetIndexes(), i => i.IsUnique);
        Assert.Equal(
            new[] { "aip_record_id", "division_id" },
            unique.Properties.Select(p => p.GetColumnName()).ToArray());
    }

    [Fact]
    public void AipDivisionSubmission_StatusIsCheckedAgainstEveryKnownState()
    {
        // Check constraints live only in the design-time model, not the read-optimized one Model() returns.
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;
        using AppDbContext context = new(options);
        IModel designTime = context.GetService<IDesignTimeModel>().Model;

        ICheckConstraint check = Assert.Single(
            designTime.FindEntityType(typeof(AipDivisionSubmission))!.GetCheckConstraints());

        foreach (string status in PPDO.Application.Common.AipDivisionStatus.All)
            Assert.Contains($"'{status}'", check.Sql);
    }

}
