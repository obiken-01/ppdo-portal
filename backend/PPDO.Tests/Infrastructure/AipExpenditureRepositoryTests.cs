using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
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

        // The hierarchy SumMooeCoByConfigOfficeAndFundAsync joins through, minimal columns only:
        // office → program → project → activity. Kept as raw DDL to match this file's existing
        // "only the tables under test" approach rather than EnsureCreated over the whole model.
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_offices (
                id INTEGER PRIMARY KEY,
                aip_record_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                sector TEXT NOT NULL DEFAULT '',
                office_id INTEGER NULL,
                workflow_status TEXT NOT NULL DEFAULT 'Draft'
            );
            CREATE TABLE aip_programs (
                id INTEGER PRIMARY KEY,
                office_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                function_band TEXT NOT NULL DEFAULT 'Core'
            );
            CREATE TABLE aip_projects (
                id INTEGER PRIMARY KEY,
                program_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                description TEXT NULL,
                objective TEXT NULL,
                is_synthetic INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE aip_activities (
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                is_creation INTEGER NOT NULL DEFAULT 0,
                is_synthetic INTEGER NOT NULL DEFAULT 0,
                funding_source_id INTEGER NULL
            );
            -- PPDO-128: the usage counts group by the record's fiscal year.
            CREATE TABLE aip_records (
                id INTEGER PRIMARY KEY,
                fiscal_year INTEGER NOT NULL
            );
            -- PPDO-128: WFP lines reach their office through the AIP activity they hang off.
            CREATE TABLE wfp_activities (
                id INTEGER PRIMARY KEY,
                aip_activity_id INTEGER NOT NULL
            );
            CREATE TABLE wfp_expenditures (
                id INTEGER PRIMARY KEY,
                wfp_activity_id INTEGER NOT NULL,
                funding_source_id INTEGER NULL
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

    // ── The office-wide ceiling sum (V18-46, corrected 2026-09-07) ────────────

    /// <summary>
    /// ⚠️ <b>The ceiling spans every sub-office group the office owns, not one of them.</b>
    ///
    /// <para>
    /// This query used to take an <c>aipOfficeId</c> — a single group row — while its caller passed
    /// <c>Groups[0]</c> and the ceiling it was compared against was the whole office's. PGO owns
    /// eight group rows in FY2028, so seven of them could hold unlimited General Fund money that no
    /// ceiling check ever saw, and the office would submit cleanly. Found by live-testing: a
    /// ₱500,000 MOOE line sat under AKAP-HUB and the strip read "Encoded —".
    /// </para>
    ///
    /// <para>
    /// The fixture is built to fail the old behaviour specifically: the money is in the SECOND
    /// group, and there is a third group belonging to a DIFFERENT config office holding a large
    /// amount that must not be counted.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SumMooeCoByConfigOfficeAndFund_SpansEveryGroupOfTheOffice_AndNoOtherOffice()
    {
        const int record = 44, ourOffice = 1, otherOffice = 9, gf = 1, otherFund = 3;

        await SeedTreeAsync(
            // (aipOfficeId, aipRecordId, configOfficeId, activityId)
            (560, record, ourOffice,   9001),   // group 1 — the one the old code looked at
            (561, record, ourOffice,   9002),   // group 2 — invisible before this fix
            (562, record, ourOffice,   9003),   // group 3 — wrong fund, must be ignored
            (563, record, otherOffice, 9004),   // another office entirely
            (564, 43,     ourOffice,   9005));  // our office, but a different (archived) record

        await SeedAsync(
            WithFund(Line(9001, mooe: 100_000m, co: 0m), gf),
            WithFund(Line(9002, mooe: 500_000m, co: 25_000m), gf),
            WithFund(Line(9003, mooe: 999_000m, co: 0m), otherFund),
            WithFund(Line(9004, mooe: 888_000m, co: 0m), gf),
            WithFund(Line(9005, mooe: 777_000m, co: 0m), gf));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<AipActivityFundTotalsDto> rows = await NewRepo(ctx)
            .SumMooeCoByConfigOfficeAndFundAsync(record, ourOffice, gf);

        // Both of our office's GF activities, from two different group rows.
        Assert.Equal([9001, 9002], rows.Select(r => r.ActivityId).OrderBy(i => i).ToArray());
        Assert.Equal(600_000m, rows.Sum(r => r.Mooe));
        Assert.Equal(25_000m,  rows.Sum(r => r.Co));
    }

    // ── Fund usage outside one office (PPDO-128) ──────────────────────────────
    // The guard behind limiting a fund to one office. Worth a real database because both halves of
    // the rule are SQL properties: the four-join office attribution, and that an AIP office with a
    // NULL config office id counts as OUTSIDE (C# null semantics, not SQL's NULL != x → unknown).

    private const int FundUnderTest = 17, OurOffice = 1, OtherOffice = 9;

    /// <summary>Our office (9001), another office (9004), and an unattributed group (9006).</summary>
    private async Task SeedUsageTreeAsync()
    {
        await SeedTreeAsync(
            (570, 44, OurOffice,   9001),
            (571, 44, OtherOffice, 9004));

        await using AppDbContext ctx = new(_options);
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO aip_records (id, fiscal_year) VALUES (44, 2027);
            INSERT INTO aip_offices (id, aip_record_id, ref_code, name, sector, office_id)
                VALUES (572, 44, 'rc', 'UNMATCHED', 'GENERAL', NULL);
            INSERT INTO aip_programs (id, office_id, ref_code, name) VALUES (5720, 572, 'p', 'Program');
            INSERT INTO aip_projects (id, program_id, ref_code, name) VALUES (57200, 5720, 'pr', 'Project');
            INSERT INTO aip_activities (id, project_id, ref_code, name) VALUES (9006, 57200, 'a', 'Activity');
            """);
    }

    [Fact]
    public async Task CountByFundingSourceOutsideOffice_CountsOtherOfficesAndUnattributed_NotOurOwn()
    {
        await SeedUsageTreeAsync();
        await SeedAsync(
            WithFund(Line(9001, mooe: 1m), FundUnderTest),   // ours — the fund stays visible to it
            WithFund(Line(9001, mooe: 1m), FundUnderTest),
            WithFund(Line(9004, mooe: 1m), FundUnderTest),   // another office → counts
            WithFund(Line(9006, mooe: 1m), FundUnderTest),   // owner unknown → counts
            WithFund(Line(9004, mooe: 1m), 3));              // another fund → never counts
        await using (AppDbContext setup = new(_options))
        {
            // An activity-level fund (import / FY≤2027) is usage too — the other half of the count.
            await setup.Database.ExecuteSqlRawAsync(
                $"UPDATE aip_activities SET funding_source_id = {FundUnderTest} WHERE id IN (9001, 9004)");
        }

        await using AppDbContext ctx = new(_options);
        IReadOnlyDictionary<int, int> outside =
            await NewRepo(ctx).CountByFundingSourceOutsideOfficeAsync(FundUnderTest, OurOffice);

        // 9004 line + 9006 line + 9004 activity, all in the FY2027 record. None of 9001's three rows.
        Assert.Equal(new Dictionary<int, int> { [2027] = 3 }, outside);
    }

    [Fact]
    public async Task CountByFundingSourceOutsideOffice_UsedOnlyByThatOffice_ReturnsZero()
    {
        await SeedUsageTreeAsync();
        await SeedAsync(WithFund(Line(9001, mooe: 1m), FundUnderTest));

        await using AppDbContext ctx = new(_options);

        // Absent rather than a 0 entry — a year with no rows produces no group.
        Assert.Empty(await NewRepo(ctx).CountByFundingSourceOutsideOfficeAsync(FundUnderTest, OurOffice));
        // …and the same row IS outside from any other office's point of view.
        Assert.Equal(new Dictionary<int, int> { [2027] = 1 },
            await NewRepo(ctx).CountByFundingSourceOutsideOfficeAsync(FundUnderTest, OtherOffice));
    }

    [Fact]
    public async Task WfpCountByFundingSourceOutsideOffice_AttributesThroughTheAipActivity()
    {
        await SeedUsageTreeAsync();
        await using (AppDbContext setup = new(_options))
        {
            await setup.Database.ExecuteSqlRawAsync($"""
                INSERT INTO wfp_activities (id, aip_activity_id) VALUES (1, 9001), (2, 9004), (3, 9006);
                INSERT INTO wfp_expenditures (id, wfp_activity_id, funding_source_id) VALUES
                    (1, 1, {FundUnderTest}),   -- ours
                    (2, 2, {FundUnderTest}),   -- another office
                    (3, 3, {FundUnderTest}),   -- unattributed
                    (4, 2, 3);                 -- another fund
                """);
        }

        await using AppDbContext ctx = new(_options);
        IReadOnlyDictionary<int, int> outside = await new WfpExpenditureRepository(ctx)
            .CountByFundingSourceOutsideOfficeAsync(FundUnderTest, OurOffice);

        Assert.Equal(new Dictionary<int, int> { [2027] = 2 }, outside);
    }

    [Fact]
    public async Task SumMooeCoByConfigOffice_GroupsPerActivityAndFund_ForThatOfficeAndRecordOnly()
    {
        // The FY2028 division table's source (2026-09-24): every fund at once, per activity, with
        // the program ref code that division assignments key on.
        const int record = 44, ourOffice = 1, otherOffice = 9, gf = 1, gad = 2;
        await SeedTreeAsync(
            (560, record, ourOffice,   9001),
            (561, record, ourOffice,   9002),   // a second group row of the same office
            (563, record, otherOffice, 9004),   // another office — excluded
            (564, 43,     ourOffice,   9005));  // our office, another record — excluded

        await SeedAsync(
            WithFund(Line(9001, ps: 500_000m, mooe: 100m, co: 0m), gf),
            WithFund(Line(9001, mooe: 50m, co: 25m), gf),       // same activity + fund → summed
            WithFund(Line(9001, mooe: 7m), gad),                 // same activity, other fund → own row
            WithFund(Line(9002, co: 300m), gf),
            Line(9002, mooe: 999m),                              // no fund → omitted
            WithFund(Line(9004, mooe: 888m), gf),
            WithFund(Line(9005, mooe: 777m), gf));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<AipActivityProgramFundTotalsDto> rows =
            await NewRepo(ctx).SumMooeCoByConfigOfficeAsync(record, ourOffice);

        Assert.Equal(
            new[]
            {
                new AipActivityProgramFundTotalsDto("p", 9001, gf,  150m, 25m),   // PS never returned
                new AipActivityProgramFundTotalsDto("p", 9001, gad, 7m,   0m),
                new AipActivityProgramFundTotalsDto("p", 9002, gf,  0m,   300m),
            },
            rows.OrderBy(r => r.ActivityId).ThenBy(r => r.FundingSourceId).ToArray());
    }

    // ── The form's Funding Source column (PPDO-80) ────────────────────────────

    [Fact]
    public async Task GetFundCodesByActivityId_DeduplicatesAndKeepsFirstUseOrder()
    {
        // GAD is used first, GF second, then GAD again. The printed cell must read "GAD Fund/GF"
        // — the order the encoder entered them — and must not repeat GAD.
        //
        // ⚠️ Alphabetical order would pass a naive assertion here by accident, so GF is
        // deliberately the code that sorts FIRST while being used SECOND.
        await SeedAsync(
            WithFundCode(Line(ActivityA, mooe: 100m), "GAD Fund"),
            WithFundCode(Line(ActivityA, mooe: 200m), "GF"),
            WithFundCode(Line(ActivityA, co:   300m), "GAD Fund"));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<string> codes = await NewRepo(ctx).GetFundCodesByActivityIdAsync(ActivityA);

        Assert.Equal(["GAD Fund", "GF"], codes);
    }

    [Fact]
    public async Task GetFundCodesByActivityId_IgnoresLinesNamingNoFund()
    {
        // A fundless line is a real state — it is what V18-49's `LinesWithoutFund` check exists to
        // catch — so this read must skip it rather than print an empty segment into "GF/".
        await SeedAsync(
            WithFundCode(Line(ActivityA, mooe: 100m), "GF"),
            Line(ActivityA, mooe: 200m));

        await using AppDbContext ctx = new(_options);

        Assert.Equal(["GF"], await NewRepo(ctx).GetFundCodesByActivityIdAsync(ActivityA));
    }

    [Fact]
    public async Task GetFundCodesByActivityId_NoLines_ReturnsEmpty()
    {
        // An uncosted activity, and every FY≤2027 uploaded one. Empty, not an error — the caller
        // falls back to the activity's own snapshot.
        await using AppDbContext ctx = new(_options);

        Assert.Empty(await NewRepo(ctx).GetFundCodesByActivityIdAsync(ActivityA));
    }

    [Fact]
    public async Task GetFundCodesByAipRecord_GroupsPerActivity_AndStopsAtTheRecordBoundary()
    {
        const int record = 77, otherRecord = 78, configOffice = 1;

        await SeedTreeAsync(
            (700, record,      configOffice, 7001),
            (701, record,      configOffice, 7002),
            (702, otherRecord, configOffice, 7003));

        await SeedAsync(
            WithFundCode(Line(7001, mooe: 10m), "GF"),
            WithFundCode(Line(7001, mooe: 20m), "GAD Fund"),
            WithFundCode(Line(7002, co:   30m), "20% DF"),
            // Another record entirely. Included because the record filter is the whole reason this
            // method takes a record id rather than the caller's activity ids.
            WithFundCode(Line(7003, mooe: 40m), "NGA"));

        await using AppDbContext ctx = new(_options);
        IReadOnlyList<AipActivityFundCodeDto> rows =
            await NewRepo(ctx).GetFundCodesByAipRecordAsync(record);

        Assert.Equal(["GF", "GAD Fund"], rows.Where(r => r.ActivityId == 7001).Select(r => r.Code));
        Assert.Equal(["20% DF"],         rows.Where(r => r.ActivityId == 7002).Select(r => r.Code));
        Assert.DoesNotContain(rows, r => r.ActivityId == 7003);
    }

    /// <summary>
    /// One activity per group row, wired office → program → project → activity.
    ///
    /// ⚠️ Inserted with raw SQL, not EF. The harness creates only the columns this query joins
    /// through, and an EF insert writes every mapped column — so entity inserts would force the
    /// fixture to mirror four entities' full schemas just to test one JOIN.
    /// </summary>
    private async Task SeedTreeAsync(
        params (int AipOfficeId, int AipRecordId, int ConfigOfficeId, int ActivityId)[] groups)
    {
        await using AppDbContext ctx = new(_options);
        foreach ((int officeId, int recordId, int configOfficeId, int activityId) in groups)
        {
            await ctx.Database.ExecuteSqlRawAsync($"""
                INSERT INTO aip_offices (id, aip_record_id, ref_code, name, sector, office_id)
                    VALUES ({officeId}, {recordId}, 'rc', 'GROUP {officeId}', 'GENERAL', {configOfficeId});
                INSERT INTO aip_programs (id, office_id, ref_code, name)
                    VALUES ({officeId * 10}, {officeId}, 'p', 'Program');
                INSERT INTO aip_projects (id, program_id, ref_code, name)
                    VALUES ({officeId * 100}, {officeId * 10}, 'pr', 'Project');
                INSERT INTO aip_activities (id, project_id, ref_code, name)
                    VALUES ({activityId}, {officeId * 100}, 'a', 'Activity');
                """);
        }
    }

    private static AipExpenditure WithFund(AipExpenditure line, int fundingSourceId)
    {
        line.FundingSourceId = fundingSourceId;
        return line;
    }

    /// <summary>
    /// A line carrying the funding-source CODE as snapshotted at entry, which is what the AIP form
    /// prints — not the FK. The two are set independently on purpose here: the fund-code reads
    /// must be proven to follow the snapshot, since that is the value that survives a rename.
    /// </summary>
    private static AipExpenditure WithFundCode(AipExpenditure line, string code)
    {
        line.FundingSourceSnapshot = code;
        return line;
    }
}
