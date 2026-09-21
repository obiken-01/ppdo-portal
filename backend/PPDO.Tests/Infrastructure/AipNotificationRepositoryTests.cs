using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The two reads behind the notifications endpoint (V18-58 / PPDO-75) —
/// <see cref="AipRepository.CountOfficesAtStatusByFiscalYearAsync"/> and
/// <see cref="AipRepository.GetOfficeStatusesAsync"/>.
///
/// <para>
/// ⚠️ <b>Against a real database, because the count is the risk.</b> "Distinct offices, grouped by
/// year" is a <c>Distinct</c> under a <c>GroupBy</c>: whether that translates at all, and whether it
/// counts an office's two sector groups once, only a query that runs can show.
/// </para>
///
/// Rows are inserted with raw SQL against hand-written DDL holding only the columns these queries
/// touch — the Sqlite in-memory pattern from <see cref="AipReviewSearchRepositoryTests"/>.
/// </summary>
public sealed class AipNotificationRepositoryTests : IDisposable
{
    private const int Ppdo = 7;
    private const int Opa  = 15;
    private const int Gso  = 20;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipNotificationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_records (
                id INTEGER PRIMARY KEY,
                fiscal_year INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft'
            );
            CREATE TABLE aip_offices (
                id INTEGER PRIMARY KEY,
                aip_record_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                sector TEXT NOT NULL DEFAULT '',
                office_id INTEGER NULL,
                workflow_status TEXT NOT NULL DEFAULT 'Draft'
            );

            INSERT INTO aip_records (id, fiscal_year, status) VALUES
                (10, 2028, 'Draft'),
                (11, 2029, 'Draft'),
                (12, 2028, 'Archived'),
                (13, 2030, 'Final'),
                (14, 2027, 'Draft');

            INSERT INTO aip_offices (id, aip_record_id, office_id, workflow_status) VALUES
                -- FY2028 open: PPDO at PPDO in TWO sector groups, OPA at PPDO, GSO still in review.
                (1, 10, 7,  'SubmittedToPpdo'),
                (2, 10, 7,  'SubmittedToPpdo'),
                (3, 10, 15, 'SubmittedToPpdo'),
                (4, 10, 20, 'DepartmentReview'),
                -- A legacy group matched to no config office.
                (5, 10, NULL, 'SubmittedToPpdo'),
                -- FY2029 open: OPA again.
                (6, 11, 15, 'SubmittedToPpdo'),
                -- Closed and old years: never counted.
                (7, 12, 20, 'SubmittedToPpdo'),
                (8, 13, 20, 'SubmittedToPpdo'),
                (9, 14, 20, 'SubmittedToPpdo');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private AipRepository Sut() => new(new AppDbContext(_options));

    [Fact]
    public async Task CountOfficesAtStatusByFiscalYear_CountsOfficesNotGroups_InOpenEnteredYearsOnly()
    {
        IReadOnlyDictionary<int, int> byYear = await Sut().CountOfficesAtStatusByFiscalYearAsync(
            AipWorkflowStatus.SubmittedToPpdo, PlanningStatus.Draft, AipFiscalYears.FirstEnteredFiscalYear);

        // FY2028: PPDO (two groups, once) + OPA = 2 — the unmatched group is not an office.
        // FY2029: OPA = 1. Archived 2028, Final 2030 and FY2027 are absent.
        Assert.Equal(2, byYear.Count);
        Assert.Equal(2, byYear[2028]);
        Assert.Equal(1, byYear[2029]);
    }

    [Fact]
    public async Task CountOfficesAtStatusByFiscalYear_NothingAtTheStatus_IsEmpty()
    {
        IReadOnlyDictionary<int, int> byYear = await Sut().CountOfficesAtStatusByFiscalYearAsync(
            AipWorkflowStatus.Consolidated, PlanningStatus.Draft, AipFiscalYears.FirstEnteredFiscalYear);

        Assert.Empty(byYear);
    }

    [Fact]
    public async Task GetOfficeStatuses_ReturnsOnlyThatOfficesGroupsInOpenEnteredYears()
    {
        IReadOnlyList<AipOfficeStatusRow> rows = await Sut().GetOfficeStatusesAsync(
            Gso, PlanningStatus.Draft, AipFiscalYears.FirstEnteredFiscalYear);

        AipOfficeStatusRow row = Assert.Single(rows);
        Assert.Equal(new AipOfficeStatusRow(10, 2028, 4, AipWorkflowStatus.DepartmentReview), row);
    }

    [Fact]
    public async Task GetOfficeStatuses_ReturnsEverySectorGroup()
    {
        IReadOnlyList<AipOfficeStatusRow> rows = await Sut().GetOfficeStatusesAsync(
            Ppdo, PlanningStatus.Draft, AipFiscalYears.FirstEnteredFiscalYear);

        Assert.Equal([1, 2], rows.Select(r => r.GroupId).OrderBy(id => id));
    }
}
