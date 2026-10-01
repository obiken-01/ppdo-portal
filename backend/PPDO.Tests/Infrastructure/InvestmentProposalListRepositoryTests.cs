using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="InvestmentProposalRepository.ListProjectsAsync"/> (PPDO-155): the SQL that lists
/// projects with their proposal status. The scope arrives already resolved, so these tests pin that
/// the query applies exactly the scope it is given: the office set, the division pair, the search,
/// and paging. Runs on Sqlite, in the pattern of <see cref="AipDivisionSubmissionRepositoryTests"/>.
/// </summary>
public sealed class InvestmentProposalListRepositoryTests : IDisposable
{
    private const int Record = 28;
    private const int OpaOffice = 100, PtoOffice = 101;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InvestmentProposalListRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_offices (id INTEGER PRIMARY KEY, aip_record_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL);
            CREATE TABLE aip_programs (id INTEGER PRIMARY KEY, office_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL);
            CREATE TABLE aip_projects (id INTEGER PRIMARY KEY, program_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL);
            CREATE TABLE aip_activities (id INTEGER PRIMARY KEY, project_id INTEGER NOT NULL, total TEXT NULL);
            CREATE TABLE Users (Id TEXT PRIMARY KEY, FullName TEXT NOT NULL);
            CREATE TABLE investment_proposals (
                id INTEGER PRIMARY KEY, aip_project_id INTEGER NOT NULL, status TEXT NOT NULL,
                updated_at TEXT NOT NULL, updated_by_user_id TEXT NULL);

            INSERT INTO aip_offices VALUES (100, 28, '1000', 'OPA'), (101, 28, '2000', 'PTO'), (102, 29, '1000', 'OPA 2029');
            INSERT INTO aip_programs VALUES
                (200, 100, '001', 'Rice Program'), (201, 100, '002', 'Corn Program'),
                (202, 101, '001', 'Revenue Program'), (203, 102, '001', 'Next Year');
            INSERT INTO aip_projects VALUES
                (300, 200, '001', 'Rice Seed Support'), (301, 201, '001', 'Corn Seed Support'),
                (302, 202, '001', 'Tax Mapping'), (303, 203, '001', 'Next Year Project');
            INSERT INTO aip_activities VALUES (1, 300, '100.50'), (2, 300, '200.25'), (3, 301, NULL);
            INSERT INTO Users VALUES ('11111111-1111-1111-1111-111111111111', 'Office Encoder');
            INSERT INTO investment_proposals VALUES (1, 300, 'Draft', '2028-01-05 03:00:00', '11111111-1111-1111-1111-111111111111');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<ProposalProjectPage> ListAsync(
        IReadOnlyList<int>? offices = null, IReadOnlyList<int>? narrowed = null, IReadOnlyList<string>? allowed = null,
        string? search = null, int skip = 0, int? take = null)
    {
        await using AppDbContext db = new(_options);
        return await new InvestmentProposalRepository(db).ListProjectsAsync(
            new ProposalProjectQuery(Record, offices, narrowed ?? [], allowed ?? [], search, skip, take));
    }

    [Fact]
    public async Task EveryOffice_ListsTheRecordsProjects_WithCostAndProposalStatus()
    {
        ProposalProjectPage page = await ListAsync();

        Assert.Equal(3, page.TotalCount);   // record 29's project is not listed
        Assert.Equal([300, 301, 302], page.Items.Select(r => r.AipProjectId).ToList());

        ProposalProjectRow rice = page.Items[0];
        Assert.Equal(300.75m, rice.ProjectCost);
        Assert.Equal(("Draft", "Office Encoder"), (rice.ProposalStatus, rice.UpdatedByName));
        Assert.Null(page.Items[1].ProposalStatus);   // no proposal yet: the service shows "None"
        Assert.Equal(0m, page.Items[1].ProjectCost); // a null total counts as zero
    }

    [Fact]
    public async Task OfficeSet_ListsOnlyThoseOffices()
        => Assert.Equal([302], (await ListAsync(offices: [PtoOffice])).Items.Select(r => r.AipProjectId).ToList());

    [Fact]
    public async Task EmptyOfficeSet_ListsNothing()
        => Assert.Equal(0, (await ListAsync(offices: [])).TotalCount);

    [Fact]
    public async Task DivisionPair_NarrowsOnlyTheNarrowedOfficesPrograms()
    {
        // The caller's own office (OPA) is narrowed to program 001; PTO is untouched.
        ProposalProjectPage page = await ListAsync(narrowed: [OpaOffice], allowed: ["001"]);
        Assert.Equal([300, 302], page.Items.Select(r => r.AipProjectId).ToList());
    }

    [Fact]
    public async Task DivisionPair_NothingAllowed_HidesTheNarrowedOffice()
    {
        // A host Staff member with no division: none of their own office's programs.
        ProposalProjectPage page = await ListAsync(narrowed: [OpaOffice], allowed: []);
        Assert.Equal([302], page.Items.Select(r => r.AipProjectId).ToList());
    }

    [Fact]
    public async Task Search_MatchesProjectOrProgramName()
    {
        // Same casing as stored: Sqlite's LIKE-free Contains is case-sensitive. SQL Server's
        // default collation is not, so production matches "corn" too.
        Assert.Equal([301], (await ListAsync(search: "Corn")).Items.Select(r => r.AipProjectId).ToList());
        Assert.Equal([302], (await ListAsync(search: "Revenue")).Items.Select(r => r.AipProjectId).ToList());
    }

    [Fact]
    public async Task Paging_CountsTheWholeMatch()
    {
        ProposalProjectPage page = await ListAsync(skip: 1, take: 1);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal([301], page.Items.Select(r => r.AipProjectId).ToList());
    }
}
