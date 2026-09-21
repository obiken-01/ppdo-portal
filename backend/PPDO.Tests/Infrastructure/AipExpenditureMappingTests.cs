using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Relational mapping guarantees for <c>aip_expenditures</c> (v1.8.0 Phase 2 — V18-33 / PPDO-36).
///
/// <para>
/// <see cref="AipExpenditureRepositoryTests"/> proves the queries, but it runs against a Sqlite
/// table written by hand inside the test — so it cannot see the delete behaviour or the index,
/// which live only in <c>AipExpenditureConfiguration</c> and reach the database through the
/// migration. Those were part of V18-33's acceptance and shipped with nothing asserting them.
/// </para>
///
/// <para>
/// All three assertions below are load-bearing in opposite directions, which is why they are
/// pinned rather than left to review:
/// </para>
/// <list type="bullet">
///   <item>Activity → <b>Cascade</b>: deleting an activity must take its expenditure lines with
///   it, or the rows orphan and V18-34's recompute sums money belonging to nothing.</item>
///   <item>Account and FundingSource → <b>Restrict</b>: the exact opposite. These are config
///   tables. A cascade here would let editing the chart of accounts silently delete historical
///   AIP lines — which is what the <c>*_snapshot</c> columns exist to prevent in the first
///   place.</item>
///   <item>An index on <c>activity_id</c>: every read is "this activity's lines", and V18-34's
///   recompute runs on every write.</item>
/// </list>
///
/// No database is touched — the connection string is never opened.
/// </summary>
public sealed class AipExpenditureMappingTests
{
    private static IEntityType Expenditures()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;

        using AppDbContext context = new(options);
        return context.Model.FindEntityType(typeof(AipExpenditure))!;
    }

    private static IForeignKey ForeignKeyTo<TPrincipal>() =>
        Assert.Single(Expenditures().GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(TPrincipal));

    [Fact]
    public void AipExpenditure_MapsToTheSnakeCaseTable()
    {
        Assert.Equal("aip_expenditures", Expenditures().GetTableName());
    }

    [Fact]
    public void DeletingAnActivity_CascadesToItsExpenditureLines()
    {
        IForeignKey activity = ForeignKeyTo<AipActivity>();

        Assert.Equal(DeleteBehavior.Cascade, activity.DeleteBehavior);
        Assert.Equal(nameof(AipExpenditure.ActivityId), Assert.Single(activity.Properties).Name);
        Assert.True(activity.IsRequired); // activity_id is the owning FK — never optional
    }

    [Fact]
    public void DeletingAnAccount_IsRestricted_SoHistoricalLinesSurvive()
    {
        Assert.Equal(DeleteBehavior.Restrict, ForeignKeyTo<Account>().DeleteBehavior);
    }

    [Fact]
    public void DeletingAFundingSource_IsRestricted_SoHistoricalLinesSurvive()
    {
        Assert.Equal(DeleteBehavior.Restrict, ForeignKeyTo<FundingSource>().DeleteBehavior);
    }

    [Fact]
    public void ActivityId_IsIndexed_BecauseEveryReadFiltersOnIt()
    {
        Assert.Contains(Expenditures().GetIndexes(), index =>
            index.Properties.Count == 1 &&
            index.Properties[0].Name == nameof(AipExpenditure.ActivityId));
    }
}
