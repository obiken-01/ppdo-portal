using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="AipExpenditureRepository"/> (v1.8.0 Phase 2 — V18-33).
///
/// These run against a real database rather than a mock because the two things worth proving are
/// both properties of the SQL: that <see cref="AipExpenditureRepository.SumByActivityIdAsync"/>
/// coalesces a SUM over zero rows to 0 rather than NULL, and that its GROUP BY translates at all.
/// A mocked repository would assert neither.
///
/// Uses the Sqlite in-memory pattern from <see cref="RepositoryTransactionTests"/> — only the one
/// table under test is created by hand, matching <c>AipExpenditureConfiguration</c>'s column
/// names, rather than running the whole SQL-Server-targeted model.
/// </summary>
public sealed class AipExpenditureRepositoryTests : IDisposable
{
    private const int ActivityA = 100;
    private const int ActivityB = 200;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipExpenditureRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_expenditures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                activity_id INTEGER NOT NULL,
                account_id INTEGER NULL,
                account_number_snapshot TEXT NULL,
                account_title_snapshot TEXT NULL,
                funding_source_id INTEGER NULL,
                funding_source_snapshot TEXT NULL,
                funding_source_name_snapshot TEXT NULL,
                ps TEXT NOT NULL DEFAULT '0',
                mooe TEXT NOT NULL DEFAULT '0',
                co TEXT NOT NULL DEFAULT '0',
                total TEXT NOT NULL DEFAULT '0',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AipExpenditure Line(int activityId, decimal ps = 0m, decimal mooe = 0m, decimal co = 0m)
    {
        AipExpenditure line = new()
        {
            ActivityId = activityId,
            Ps = ps, Mooe = mooe, Co = co,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        line.Recalculate();
        return line;
    }

    private async Task SeedAsync(params AipExpenditure[] lines)
    {
        await using AppDbContext ctx = new(_options);
        ctx.AipExpenditures.AddRange(lines);
        await ctx.SaveChangesAsync();
    }

    private AipExpenditureRepository NewRepo(AppDbContext ctx) => new(ctx);

    // ── Total is computed, never assigned ─────────────────────────────────────

    [Fact]
    public void Recalculate_SumsTheThreeComponents()
    {
        AipExpenditure line = Line(ActivityA, ps: 1_000m, mooe: 250.50m, co: 99.50m);

        Assert.Equal(1_350m, line.Total);
    }

    [Fact]
    public void Total_HasNoPublicSetter_SoItCannotBeAssignedFromOutside()
    {
        // The guarantee that replaces "remember to call the right repository method". RAL-144 is
        // the precedent: AipActivity.Total trusted its source file's own Total column, and a stale
        // cell there desynced it from its components while every reader believed it.
        System.Reflection.PropertyInfo total = typeof(AipExpenditure).GetProperty(nameof(AipExpenditure.Total))!;

        Assert.True(total.CanRead);
        Assert.False(total.SetMethod?.IsPublic ?? false);
    }

    [Fact]
    public async Task Total_RoundTripsThroughTheDatabase_DespiteItsPrivateSetter()
    {
        // EF sets private setters by reflection. If that ever stopped working the column would read
        // back as 0 and every downstream figure would be silently wrong, so it is worth pinning.
        await SeedAsync(Line(ActivityA, ps: 500m, mooe: 250m));

        await using AppDbContext ctx = new(_options);
        AipExpenditure stored = await ctx.AipExpenditures.SingleAsync();

        Assert.Equal(750m, stored.Total);
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByActivityIdAsync_ReturnsOnlyThatActivitysLines()
    {
        await SeedAsync(
            Line(ActivityA, ps: 100m),
            Line(ActivityA, mooe: 200m),
            Line(ActivityB, co: 999m));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<AipExpenditure> lines = await NewRepo(ctx).GetByActivityIdAsync(ActivityA);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(ActivityA, l.ActivityId));
    }

    [Fact]
    public async Task GetByActivityIdsAsync_EmptyInput_ReturnsEmpty_WithoutQuerying()
    {
        await using AppDbContext ctx = new(_options);

        Assert.Empty(await NewRepo(ctx).GetByActivityIdsAsync([]));
    }

    [Fact]
    public async Task GetByActivityIdsAsync_ReturnsLinesForEveryRequestedActivity()
    {
        // The batch form exists so rendering a whole AIP does not fire one query per activity —
        // the N+1 shape RAL-166 removed from the dashboard.
        await SeedAsync(Line(ActivityA, ps: 100m), Line(ActivityB, co: 50m));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<AipExpenditure> lines = await NewRepo(ctx).GetByActivityIdsAsync([ActivityA, ActivityB]);

        Assert.Equal(2, lines.Count);
    }

    // ── The aggregate V18-34 will depend on ───────────────────────────────────

    [Fact]
    public async Task SumByActivityIdAsync_SumsEachComponentSeparately()
    {
        await SeedAsync(
            Line(ActivityA, ps: 1_000m, mooe: 500m),
            Line(ActivityA, mooe: 250m, co: 2_000m));

        await using AppDbContext ctx = new(_options);
        AipExpenditureTotalsDto totals = await NewRepo(ctx).SumByActivityIdAsync(ActivityA);

        Assert.Equal(1_000m, totals.Ps);
        Assert.Equal(750m, totals.Mooe);
        Assert.Equal(2_000m, totals.Co);
        Assert.Equal(3_750m, totals.Total);
        Assert.Equal(2, totals.LineCount);
    }

    [Fact]
    public async Task SumByActivityIdAsync_NoLines_ReturnsZeroesAndZeroCount_NotNull()
    {
        // ⚠️ The test this class exists for. SUM over zero rows is SQL NULL, and V18-34 reads this
        // to decide whether to touch the parent activity. If "no lines" came back as null — or
        // threw — the recompute would have no way to tell it apart from a computed zero, and
        // FY≤2027 activities (which have no lines at all) would be silently zeroed.
        await using AppDbContext ctx = new(_options);
        AipExpenditureTotalsDto totals = await NewRepo(ctx).SumByActivityIdAsync(ActivityA);

        Assert.Equal(0m, totals.Ps);
        Assert.Equal(0m, totals.Mooe);
        Assert.Equal(0m, totals.Co);
        Assert.Equal(0m, totals.Total);
        Assert.Equal(0, totals.LineCount);
    }

    [Fact]
    public async Task SumByActivityIdAsync_IgnoresOtherActivitiesLines()
    {
        await SeedAsync(Line(ActivityA, ps: 100m), Line(ActivityB, ps: 9_999m));

        await using AppDbContext ctx = new(_options);
        AipExpenditureTotalsDto totals = await NewRepo(ctx).SumByActivityIdAsync(ActivityA);

        Assert.Equal(100m, totals.Ps);
        Assert.Equal(1, totals.LineCount);
    }

    [Fact]
    public async Task SumByActivityIdAsync_LineCountDistinguishesZeroValuedLinesFromNoLines()
    {
        // A line recorded as ₱0 is a decision somebody made; no line at all is not. The totals are
        // identical, so LineCount is the only thing that separates them — which is exactly what
        // V18-34 needs to avoid zeroing an activity that was never costed here.
        await SeedAsync(Line(ActivityA));

        await using AppDbContext ctx = new(_options);
        AipExpenditureTotalsDto totals = await NewRepo(ctx).SumByActivityIdAsync(ActivityA);

        Assert.Equal(0m, totals.Total);
        Assert.Equal(1, totals.LineCount);
    }
}
