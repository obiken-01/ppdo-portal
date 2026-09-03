using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The FY≥2028 AIP record shape (v1.8.0 Phase 2 — V18-40 / PPDO-39): office-owned, LDIP-like.
///
/// <para>
/// These are relational-mapping assertions rather than behaviour, because the thing worth pinning
/// is a <b>structural decision</b> — one that is easy to undo by accident, in a one-line change
/// that compiles and passes every behavioural test. No database is opened.
/// </para>
/// </summary>
public sealed class AipRecordShapeTests
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

    // ── The owning-office FK ──────────────────────────────────────────────────

    [Fact]
    public void AipRecord_IsOwnedByAnOffice_ByForeignKey()
    {
        IForeignKey office = Assert.Single(Entity<AipRecord>().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Office));

        Assert.Equal(nameof(AipRecord.OfficeId), Assert.Single(office.Properties).Name);
    }

    [Fact]
    public void AipRecord_OwningOffice_IsOptional_BecauseLegacyRecordsHaveNoSingleOwner()
    {
        // ⚠️ Null here is PERMANENT and correct, not a backfill that has yet to run — the
        // difference from AipOffice.OfficeId, whose nulls are unmatched rows to resolve. A legacy
        // record spans every office in the province, so there is no owner to fill in. Making this
        // required would force a migration to invent one.
        IForeignKey office = Assert.Single(Entity<AipRecord>().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Office));

        Assert.False(office.IsRequired);
    }

    [Fact]
    public void AipRecord_DeletingAnOffice_IsRestricted_SoItsAipHistorySurvives()
    {
        IForeignKey office = Assert.Single(Entity<AipRecord>().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Office));

        Assert.Equal(DeleteBehavior.Restrict, office.DeleteBehavior);
    }

    [Fact]
    public void AipRecord_IsIndexedByOfficeAndFiscalYear_TheOfficeOwnedReadPath()
    {
        Assert.Contains(Entity<AipRecord>().GetIndexes(), index =>
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(AipRecord.OfficeId) &&
            index.Properties[1].Name == nameof(AipRecord.FiscalYear));
    }

    [Fact]
    public void AipRecord_OfficeAndFiscalYearIndex_IsNotUnique()
    {
        // The one-per-(office, FY) rule counts only NON-ARCHIVED records, which a unique index
        // cannot express — archiving and re-creating is a supported flow. The service owns the
        // rule; the index exists for the read. LdipRecord made the same call.
        IIndex index = Assert.Single(Entity<AipRecord>().GetIndexes(), i =>
            i.Properties.Count == 2 &&
            i.Properties[0].Name == nameof(AipRecord.OfficeId));

        Assert.False(index.IsUnique);
    }
}
