using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="AipRepository.SearchReviewNodesAsync"/> — the AIP Review search
/// (v1.8.0 Phase 4 — V18-75 / PPDO-76, <c>AIP_Review_Spec.md</c> §4.1).
///
/// <para>
/// <b>⚠️ These run against a real database, and that is the point.</b> Everything worth proving
/// here is a property of the SQL: that a three-level <c>UNION ALL</c> with paging and two facet
/// groupings <b>translates at all</b>, that OR-within / AND-across actually behaves that way once
/// it is a query rather than a description, and that the hand-built ref-code OR chain produces the
/// right rows. A mocked repository asserts none of it — it would only prove that the filters were
/// copied into a record.
/// </para>
///
/// <para>
/// ⚠️ The one thing SQLite cannot prove is index usage. The ref-code predicate is anchored
/// (<c>LIKE 'x%'</c>) so SQL Server can seek it; a leading <c>%</c> would pass every test in this
/// file and scan the table in production, which is why the spec forbids it by name rather than
/// leaving it to review.
/// </para>
///
/// Uses the Sqlite in-memory pattern from <see cref="AipExpenditureRepositoryTests"/> — hand-written
/// DDL for only the four hierarchy tables, matching their EF configurations' column names.
/// </summary>
public sealed class AipReviewSearchRepositoryTests : IDisposable
{
    private const int Record   = 44;
    private const int OtherRec = 45;

    // Config office ids.
    private const int Ppdo = 7;
    private const int Opa  = 15;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipReviewSearchRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
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
                is_synthetic INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE aip_activities (
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
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
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ── The fixture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Two offices, and <b>PPDO deliberately spans two sectors</b> — GENERAL under ref-code segment
    /// <c>1000</c> and SOCIAL under <c>3000</c>.
    ///
    /// ⚠️ That split is the whole reason the spec keeps the segment filters alongside the ref-code
    /// box: sector is segment 1 and office is segment 5, so "one office, all sectors" cannot be
    /// written as a prefix. A single-sector fixture would let a wrong implementation pass.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AppDbContext ctx = new(_options);

        ctx.Set<AipOffice>().AddRange(
            Office(1, Record, "1000-000-1-01-010", "PPDO", "GENERAL", Ppdo, AipWorkflowStatus.SubmittedToPpdo),
            Office(2, Record, "3000-000-1-01-010", "PPDO - SOCIAL", "SOCIAL", Ppdo, AipWorkflowStatus.SubmittedToPpdo),
            Office(3, Record, "1000-000-1-01-015", "OPA", "GENERAL", Opa, AipWorkflowStatus.Draft),
            // A different record entirely — must never appear, whatever the filters say.
            Office(4, OtherRec, "1000-000-1-01-010", "PPDO last year", "GENERAL", Ppdo, AipWorkflowStatus.Draft));

        ctx.Set<AipProgram>().AddRange(
            Program(10, 1, "1000-000-1-01-010-001", "Office Functionality Program"),
            Program(11, 2, "3000-000-1-01-010-001", "Social Welfare Program"),
            Program(12, 3, "1000-000-1-01-015-001", "Agricultural Productivity"),
            Program(13, 4, "1000-000-1-01-010-001", "Last year programme"));

        ctx.Set<AipProject>().AddRange(
            Project(20, 10, "1000-000-1-01-010-001-001", "Verification project"),
            Project(21, 11, "3000-000-1-01-010-001-001", "Aid or relief distribution"),
            Project(22, 12, "1000-000-1-01-015-001-001", "Rice support"),
            Project(23, 13, "1000-000-1-01-010-001-001", "Last year project"));

        ctx.Set<AipActivity>().AddRange(
            Activity(30, 20, "1000-000-1-01-010-001-001-001", "Training workshop"),
            Activity(31, 21, "3000-000-1-01-010-001-001-001", "Relief packing"),
            Activity(32, 22, "1000-000-1-01-015-001-001-001", "Seed distribution"),
            Activity(33, 23, "1000-000-1-01-010-001-001-001", "Last year activity"));

        await ctx.SaveChangesAsync();
    }

    private static AipOffice Office(int id, int rec, string refCode, string name, string sector,
        int? officeId, string status) => new()
    {
        Id = id, AipRecordId = rec, RefCode = refCode, Name = name, Sector = sector,
        OfficeId = officeId, WorkflowStatus = status,
    };

    // FunctionBand is set explicitly: the column is NOT NULL, and EF writes the property's null
    // rather than letting SQLite's DEFAULT apply.
    private static AipProgram Program(int id, int officeId, string refCode, string name)
        => new()
        {
            Id = id, OfficeId = officeId, RefCode = refCode, Name = name,
            FunctionBand = AipFunctionBand.Core,
        };

    private static AipProject Project(int id, int programId, string refCode, string name)
        => new() { Id = id, ProgramId = programId, RefCode = refCode, Name = name };

    private static AipActivity Activity(int id, int projectId, string refCode, string name)
        => new() { Id = id, ProjectId = projectId, RefCode = refCode, Name = name };

    private AipRepository Sut() => new(new AppDbContext(_options));

    private static AipReviewSearchQuery Query(
        IReadOnlyList<int>? officeIds = null,
        IReadOnlyList<string>? sectors = null,
        IReadOnlyList<string>? statuses = null,
        IReadOnlyList<string>? refCodes = null,
        string? title = null,
        int skip = 0, int take = 100)
        => new(Record, officeIds ?? [], sectors ?? [], statuses ?? [], refCodes ?? [], title, skip, take);

    // ── The tree comes back as rows, and only this record's ───────────────────

    [Fact]
    public async Task Search_WithNoFilters_ReturnsEveryNodeOfThisRecordAndNoOther()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query());

        // 3 offices × (1 program + 1 project + 1 activity) = 9. The fourth office belongs to
        // record 45 and contributes nothing.
        Assert.Equal(9, page.TotalCount);
        Assert.DoesNotContain(page.Items, r => r.Name.StartsWith("Last year"));

        // All three levels are represented — a query that silently dropped one would still look
        // plausible on a spot check.
        Assert.Equal(3, page.Items.Count(r => r.Level == "Program"));
        Assert.Equal(3, page.Items.Count(r => r.Level == "Project"));
        Assert.Equal(3, page.Items.Count(r => r.Level == "Activity"));
    }

    /// <summary>
    /// ⚠️ Every row carries the office it belongs to, because that is what the result link needs —
    /// a row the reviewer cannot open from is a dead end.
    /// </summary>
    [Fact]
    public async Task Search_CarriesTheOwningOfficeAndItsStateOnEveryRow()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query());

        AipReviewNodeRow social = page.Items.Single(r => r.Name == "Relief packing");
        Assert.Equal(Ppdo, social.OfficeId);
        Assert.Equal("SOCIAL", social.Sector);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, social.WorkflowStatus);
        Assert.Equal("PPDO - SOCIAL", social.AipOfficeName);
    }

    // ── The query the ref-code box cannot express (spec §4.1) ─────────────────

    /// <summary>
    /// <b>The headline case.</b> One office, all sectors — impossible as a ref-code prefix because
    /// sector is segment 1 and office is segment 5, so pinning the office means matching the middle
    /// of the string.
    /// </summary>
    [Fact]
    public async Task Search_ByOfficeWithSectorBlank_ReturnsBothOfThatOfficesSectors()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query(officeIds: [Ppdo]));

        Assert.Equal(6, page.TotalCount);                       // two groups × three levels
        Assert.Contains(page.Items, r => r.Sector == "GENERAL");
        Assert.Contains(page.Items, r => r.Sector == "SOCIAL");
        Assert.DoesNotContain(page.Items, r => r.OfficeId == Opa);
    }

    // ── OR within a field, AND across fields (decision 16) ────────────────────

    [Fact]
    public async Task Search_TwoOfficeIds_OrsThemTogether()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query(officeIds: [Ppdo, Opa]));

        Assert.Equal(9, page.TotalCount);
    }

    [Fact]
    public async Task Search_OfficeAndSectorTogether_AndsAcrossTheFields()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(officeIds: [Ppdo], sectors: ["SOCIAL"]));

        // PPDO AND SOCIAL — one group, three levels. Not "PPDO plus everything social".
        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, r => Assert.Equal("SOCIAL", r.Sector));
    }

    [Fact]
    public async Task Search_ByWorkflowStatus_NarrowsToOfficesInThatState()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(statuses: [AipWorkflowStatus.SubmittedToPpdo]));

        Assert.Equal(6, page.TotalCount);
        Assert.All(page.Items, r => Assert.Equal(Ppdo, r.OfficeId));
    }

    // ── Ref codes: prefix, OR-list, and never a leading wildcard ──────────────

    [Fact]
    public async Task Search_ByRefCodePrefix_MatchesTheSubtree()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(refCodes: ["3000-"]));

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, r => Assert.StartsWith("3000-", r.RefCode));
    }

    /// <summary>
    /// The typed OR-list from the ticket: <c>1000-…-010 OR 3000-…-010</c>. ⚠️ Flat, parsed to a
    /// set, never a tree — and each value is still an anchored prefix.
    /// </summary>
    [Fact]
    public async Task Search_ByRefCodeOrList_ReturnsTheUnionOfBothPrefixes()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(refCodes: ["1000-000-1-01-010", "3000-000-1-01-010"]));

        Assert.Equal(6, page.TotalCount);
        Assert.Contains(page.Items, r => r.Sector == "GENERAL");
        Assert.Contains(page.Items, r => r.Sector == "SOCIAL");
    }

    /// <summary>
    /// ⚠️ Anchored, not a substring. <c>010-001</c> appears in the MIDDLE of every PPDO code, so a
    /// <c>LIKE '%…%'</c> implementation returns rows here and an anchored one returns none. This is
    /// the test that fails if someone "fixes" the ref-code box by making it more forgiving.
    /// </summary>
    [Fact]
    public async Task Search_ByRefCodeFragment_MatchesNothingBecauseThePredicateIsAnchored()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(refCodes: ["010-001"]));

        Assert.Equal(0, page.TotalCount);
    }

    // ── Title: substring, and never OR-split ─────────────────────────────────

    [Fact]
    public async Task Search_ByTitle_MatchesASubstringOfTheNodeName()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query(title: "workshop"));

        AipReviewNodeRow row = Assert.Single(page.Items);
        Assert.Equal("Training workshop", row.Name);
    }

    /// <summary>
    /// ⚠️ <b>The title field is never OR-split</b> (spec §4.1). "Aid or relief distribution" is one
    /// project whose name legitimately contains the word — splitting on it would turn this search
    /// into "Aid" OR "relief distribution" and return rows the reviewer never asked for.
    /// </summary>
    [Fact]
    public async Task Search_ByTitleContainingTheWordOr_IsMatchedLiterally()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(title: "Aid or relief"));

        AipReviewNodeRow row = Assert.Single(page.Items);
        Assert.Equal("Aid or relief distribution", row.Name);
    }

    // ── Paging and facet counts ──────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <c>TotalCount</c> is the size of the whole match, not of the page — otherwise the pager
    /// says "1 of 1" on every page and the reviewer never learns there is more.
    /// </summary>
    [Fact]
    public async Task Search_Paged_ReturnsThePageButTheTotalOfTheWholeMatch()
    {
        await SeedAsync();

        AipReviewSearchPage first  = await Sut().SearchReviewNodesAsync(Query(skip: 0, take: 4));
        AipReviewSearchPage second = await Sut().SearchReviewNodesAsync(Query(skip: 4, take: 4));

        Assert.Equal(9, first.TotalCount);
        Assert.Equal(9, second.TotalCount);
        Assert.Equal(4, first.Items.Count);
        Assert.Equal(4, second.Items.Count);

        // Stable ordering: the two pages must not overlap.
        Assert.Empty(first.Items.Select(r => r.Level + r.NodeId)
            .Intersect(second.Items.Select(r => r.Level + r.NodeId)));
    }

    /// <summary>
    /// ⚠️ <b>A facet ignores its own field.</b> With SOCIAL selected, the sector chips must still
    /// report GENERAL's count — that is what tells the reviewer there is something else to combine
    /// with. Counting with every filter applied would show GENERAL as 0, which reads as "there is
    /// nothing there" and quietly makes multi-select pointless.
    /// </summary>
    [Fact]
    public async Task Search_SectorCounts_IgnoreTheSectorFilterItself()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(officeIds: [Ppdo], sectors: ["SOCIAL"]));

        Assert.Equal(3, page.TotalCount);                       // the results ARE narrowed
        Assert.Equal(3, page.SectorCounts["SOCIAL"]);
        Assert.Equal(3, page.SectorCounts["GENERAL"]);          // ...but the chip still offers it
    }

    /// <summary>
    /// The other half of the same rule, and it must hold for the OTHER field at the same time: the
    /// status facet drops the status filter but keeps the office filter.
    /// </summary>
    [Fact]
    public async Task Search_StatusCounts_DropTheStatusFilterButKeepTheOthers()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(
            Query(statuses: [AipWorkflowStatus.Draft]));

        Assert.Equal(3, page.TotalCount);                       // OPA only
        Assert.Equal(3, page.WorkflowStatusCounts[AipWorkflowStatus.Draft]);
        Assert.Equal(6, page.WorkflowStatusCounts[AipWorkflowStatus.SubmittedToPpdo]);
    }

    // ── Nothing matches ──────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MatchingNothing_IsAnEmptyPageNotAnError()
    {
        await SeedAsync();

        AipReviewSearchPage page = await Sut().SearchReviewNodesAsync(Query(title: "nothing here"));

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }
}
