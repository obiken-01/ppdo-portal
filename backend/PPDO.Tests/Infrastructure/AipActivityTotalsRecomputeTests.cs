using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The derived-totals recompute (v1.8.0 Phase 2 — V18-34): an activity's stored
/// <c>Ps</c>/<c>Mooe</c>/<c>Co</c>/<c>Total</c> follow the sum of its <c>aip_expenditures</c> lines.
///
/// <para>
/// Runs against a real (Sqlite) database rather than mocks because the thing worth proving is the
/// interaction between a SQL <c>GROUP BY</c> over child rows and a write to the parent — a mocked
/// repository would assert only that the code calls itself in the order it was written.
/// </para>
///
/// <para>
/// ⚠️ <b>The case that matters most is <see cref="Recalculate_ActivityWithNoLines_IsLeftUntouched"/>.</b>
/// Every FY≤2027 activity was imported from the province's workbook and has no expenditure rows at
/// all. An unguarded recompute writes 0 over all of them, silently, and the first symptom is a
/// historical AIP printing ₱0 long after the change that caused it.
/// </para>
/// </summary>
public sealed class AipActivityTotalsRecomputeTests : IDisposable
{
    private const int ImportedActivityId = 100;   // FY2027 shape: amounts, no child lines
    private const int CostedActivityId   = 200;   // Phase 3 shape: amounts derived from lines
    private const int OtherActivityId    = 300;

    /// <summary>What an imported FY2027 activity carries, in pesos, from the workbook.</summary>
    private const decimal ImportedTotal = 250_000m;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipActivityTotalsRecomputeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_activities (
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL,
                name TEXT NOT NULL,
                esre_code TEXT NULL,
                implementing_office TEXT NULL,
                start_date TEXT NULL,
                end_date TEXT NULL,
                expected_outputs TEXT NULL,
                funding_source_id INTEGER NULL,
                funding_source_snapshot TEXT NULL,
                ps TEXT NULL,
                mooe TEXT NULL,
                co TEXT NULL,
                total TEXT NULL,
                cc_adaptation TEXT NULL,
                cc_mitigation TEXT NULL,
                cc_typology_code TEXT NULL,
                is_creation INTEGER NOT NULL DEFAULT 0,
                is_synthetic INTEGER NOT NULL DEFAULT 0
            );
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

    private static AipActivity Activity(int id, decimal? ps, decimal? mooe, decimal? co, decimal? total) => new()
    {
        Id = id, ProjectId = 1, RefCode = $"1000-000-1-01-010-001-001-{id}", Name = $"Activity {id}",
        Ps = ps, Mooe = mooe, Co = co, Total = total,
    };

    private static AipExpenditure Line(int activityId, decimal ps = 0m, decimal mooe = 0m, decimal co = 0m)
    {
        AipExpenditure line = new()
        {
            ActivityId = activityId, Ps = ps, Mooe = mooe, Co = co,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        line.Recalculate();
        return line;
    }

    private async Task SeedAsync(params object[] rows)
    {
        await using AppDbContext ctx = new(_options);
        foreach (object row in rows) ctx.Add(row);
        await ctx.SaveChangesAsync();
    }

    /// <summary>Runs the recompute the way the Application service does: sum, then apply, sequentially.</summary>
    private async Task<bool> RecalculateAsync(int activityId, bool zeroWhenNoLines = false)
    {
        await using AppDbContext ctx = new(_options);
        AipExpenditureRepository expenditures = new(ctx);
        AipRepository activities = new(ctx);

        AipExpenditureTotalsDto totals = await expenditures.SumByActivityIdAsync(activityId);
        bool changed = await activities.ApplyActivityTotalsAsync(activityId, totals, zeroWhenNoLines);
        if (changed) await activities.SaveChangesAsync();
        return changed;
    }

    private async Task<AipActivity> ReloadAsync(int activityId)
    {
        await using AppDbContext ctx = new(_options);
        return await ctx.Set<AipActivity>().SingleAsync(a => a.Id == activityId);
    }

    // ── The guard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recalculate_ActivityWithNoLines_IsLeftUntouched()
    {
        // An FY2027 activity: real imported amounts, no expenditure rows, and none coming.
        await SeedAsync(Activity(ImportedActivityId, 100_000m, 150_000m, null, ImportedTotal));

        bool changed = await RecalculateAsync(ImportedActivityId);

        Assert.False(changed);
        AipActivity after = await ReloadAsync(ImportedActivityId);
        Assert.Equal(ImportedTotal, after.Total);   // ₱250,000, not ₱0
        Assert.Equal(100_000m, after.Ps);
        Assert.Equal(150_000m, after.Mooe);
        Assert.Null(after.Co);                      // a null component stays null, not 0
    }

    [Fact]
    public async Task Recalculate_ActivityWithNoLines_IsUntouchedEvenAcrossRepeatedRuns()
    {
        // A bulk or defensive recompute must be safe to run as often as anyone likes.
        await SeedAsync(Activity(ImportedActivityId, null, null, null, ImportedTotal));

        await RecalculateAsync(ImportedActivityId);
        await RecalculateAsync(ImportedActivityId);
        await RecalculateAsync(ImportedActivityId);

        Assert.Equal(ImportedTotal, (await ReloadAsync(ImportedActivityId)).Total);
    }

    [Fact]
    public async Task Recalculate_OnlyTouchesTheActivityItWasAskedAbout()
    {
        await SeedAsync(
            Activity(CostedActivityId, null, null, null, null),
            Activity(OtherActivityId, null, null, null, ImportedTotal),
            Line(CostedActivityId, ps: 1_000m));

        await RecalculateAsync(CostedActivityId);

        Assert.Equal(1_000m, (await ReloadAsync(CostedActivityId)).Total);
        Assert.Equal(ImportedTotal, (await ReloadAsync(OtherActivityId)).Total);
    }

    // ── Add, edit, delete ─────────────────────────────────────────────────────

    [Fact]
    public async Task Recalculate_AfterAddingALine_ParentMatchesTheLine()
    {
        await SeedAsync(Activity(CostedActivityId, null, null, null, null));
        await SeedAsync(Line(CostedActivityId, ps: 5_000m, mooe: 2_500m, co: 500m));

        Assert.True(await RecalculateAsync(CostedActivityId));

        AipActivity after = await ReloadAsync(CostedActivityId);
        Assert.Equal(5_000m, after.Ps);
        Assert.Equal(2_500m, after.Mooe);
        Assert.Equal(500m,   after.Co);
        Assert.Equal(8_000m, after.Total);
    }

    [Fact]
    public async Task Recalculate_ThreeLines_SumPerComponent()
    {
        await SeedAsync(Activity(CostedActivityId, null, null, null, null));
        await SeedAsync(
            Line(CostedActivityId, ps: 1_000m, mooe: 100m),
            Line(CostedActivityId, ps: 2_000m, co: 250m),
            Line(CostedActivityId, mooe: 400m, co: 750m));

        await RecalculateAsync(CostedActivityId);

        AipActivity after = await ReloadAsync(CostedActivityId);
        Assert.Equal(3_000m, after.Ps);     // 1,000 + 2,000
        Assert.Equal(500m,   after.Mooe);   //   100 +   400
        Assert.Equal(1_000m, after.Co);     //   250 +   750
        Assert.Equal(4_500m, after.Total);
    }

    [Fact]
    public async Task Recalculate_AfterEditingALine_ParentFollowsTheNewAmount()
    {
        await SeedAsync(Activity(CostedActivityId, null, null, null, null));
        await SeedAsync(Line(CostedActivityId, ps: 5_000m));
        await RecalculateAsync(CostedActivityId);

        await using (AppDbContext ctx = new(_options))
        {
            AipExpenditure line = await ctx.Set<AipExpenditure>().SingleAsync();
            line.Ps = 9_000m;
            line.Recalculate();
            await ctx.SaveChangesAsync();
        }

        await RecalculateAsync(CostedActivityId);

        Assert.Equal(9_000m, (await ReloadAsync(CostedActivityId)).Total);
    }

    [Fact]
    public async Task Recalculate_AfterDeletingOneOfTwoLines_ParentDropsToTheRemainder()
    {
        await SeedAsync(Activity(CostedActivityId, null, null, null, null));
        await SeedAsync(
            Line(CostedActivityId, ps: 5_000m),
            Line(CostedActivityId, mooe: 3_000m));
        await RecalculateAsync(CostedActivityId);

        await using (AppDbContext ctx = new(_options))
        {
            AipExpenditure first = await ctx.Set<AipExpenditure>().OrderBy(e => e.Id).FirstAsync();
            ctx.Remove(first);
            await ctx.SaveChangesAsync();
        }

        // One line remains, so the ordinary recompute is enough — no ambiguity to resolve.
        await RecalculateAsync(CostedActivityId, zeroWhenNoLines: true);

        AipActivity after = await ReloadAsync(CostedActivityId);
        Assert.Equal(0m,     after.Ps);
        Assert.Equal(3_000m, after.Mooe);
        Assert.Equal(3_000m, after.Total);
    }

    // ── The ambiguity: no lines, two opposite meanings ────────────────────────

    [Fact]
    public async Task Recalculate_AfterDeletingTheLastLine_TotalBecomesZero_NotNull()
    {
        await SeedAsync(Activity(CostedActivityId, null, null, null, null));
        await SeedAsync(Line(CostedActivityId, ps: 5_000m));
        await RecalculateAsync(CostedActivityId);

        await using (AppDbContext ctx = new(_options))
        {
            ctx.RemoveRange(await ctx.Set<AipExpenditure>().ToListAsync());
            await ctx.SaveChangesAsync();
        }

        // The caller just deleted this activity's last line, so it knows the activity was
        // expenditure-derived a moment ago — which is the only way to tell this apart from an
        // imported FY2027 activity that never had lines at all.
        Assert.True(await RecalculateAsync(CostedActivityId, zeroWhenNoLines: true));

        AipActivity after = await ReloadAsync(CostedActivityId);
        Assert.Equal(0m, after.Total);   // 0, never null — null meant "never computed"
        Assert.NotNull(after.Total);
        Assert.Equal(0m, after.Ps);
        Assert.Equal(0m, after.Mooe);
        Assert.Equal(0m, after.Co);
    }

    [Fact]
    public async Task Recalculate_ZeroValuedLines_AreNotTheSameAsNoLines()
    {
        // Both sum to 0. Only LineCount separates them, which is why SumByActivityIdAsync carries
        // it — and why an activity costed explicitly at zero DOES get written.
        await SeedAsync(Activity(CostedActivityId, null, null, null, ImportedTotal));
        await SeedAsync(Line(CostedActivityId), Line(CostedActivityId));

        Assert.True(await RecalculateAsync(CostedActivityId));

        Assert.Equal(0m, (await ReloadAsync(CostedActivityId)).Total);
    }

    [Fact]
    public async Task Recalculate_UnknownActivityId_ReportsNoChange_RatherThanThrowing()
    {
        await SeedAsync(Line(9_999, ps: 1_000m));

        Assert.False(await RecalculateAsync(9_999));
    }
}
