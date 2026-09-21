using Microsoft.EntityFrameworkCore;
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

}
