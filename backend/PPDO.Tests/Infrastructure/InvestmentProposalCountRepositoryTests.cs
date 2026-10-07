using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The dashboard's proposal counts (PPDO-180): <see cref="InvestmentProposalRepository.CountByOfficeAndStatusAsync"/>
/// and <see cref="InvestmentProposalRepository.ListNeedingAttentionAsync"/>. On Sqlite, in the pattern
/// of <see cref="InvestmentProposalListRepositoryTests"/>, because the grouping and the shared scope
/// are the risk: the counts must agree with the list for the same scope.
///
/// <para>Record 28: OPA (config office 15) has projects 300 (Draft), 301 (none), 304 (Final) and 305
/// (none, program 002); PTO (config office 3) has 302 (Final) and 303 (none). Record 29 has 306.</para>
/// </summary>
public sealed class InvestmentProposalCountRepositoryTests : IDisposable
{
    private const int Record = 28;
    private const int OpaGroup = 100, PtoGroup = 101;
    private const int OpaOffice = 15, PtoOffice = 3;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InvestmentProposalCountRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_offices (id INTEGER PRIMARY KEY, aip_record_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL, office_id INTEGER NULL);
            CREATE TABLE aip_programs (id INTEGER PRIMARY KEY, office_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL);
            CREATE TABLE aip_projects (id INTEGER PRIMARY KEY, program_id INTEGER NOT NULL, ref_code TEXT NOT NULL, name TEXT NOT NULL);
            CREATE TABLE investment_proposals (id INTEGER PRIMARY KEY, aip_project_id INTEGER NOT NULL, status TEXT NOT NULL);

            INSERT INTO aip_offices VALUES (100, 28, '1000', 'OPA', 15), (101, 28, '2000', 'PTO', 3), (102, 29, '1000', 'OPA 2029', 15);
            INSERT INTO aip_programs VALUES
                (200, 100, '001', 'Rice Program'), (201, 100, '002', 'Corn Program'),
                (202, 101, '001', 'Revenue Program'), (203, 102, '001', 'Next Year');
            INSERT INTO aip_projects VALUES
                (300, 200, '002', 'Rice Seed Support'), (301, 200, '003', 'Rice Training'),
                (304, 200, '001', 'Rice Mill'),         (305, 201, '001', 'Corn Seed Support'),
                (302, 202, '001', 'Tax Mapping'),       (303, 202, '002', 'Tax Training'),
                (306, 203, '001', 'Next Year Project');
            INSERT INTO investment_proposals VALUES (1, 300, 'Draft'), (2, 304, 'Final'), (3, 302, 'Final'), (4, 306, 'Final');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private static ProposalProjectQuery Scope(
        IReadOnlyList<int>? groups = null, IReadOnlyList<int>? narrowed = null, IReadOnlyList<string>? allowed = null)
        => new(Record, groups, narrowed ?? [], allowed ?? [], Search: null, Skip: 0, Take: null);

    private async Task<Dictionary<(int? Office, string? Status), int>> CountAsync(ProposalProjectQuery query)
    {
        await using AppDbContext db = new(_options);
        IReadOnlyList<ProposalStatusCount> rows = await new InvestmentProposalRepository(db).CountByOfficeAndStatusAsync(query);
        return rows.ToDictionary(r => (r.OfficeId, r.Status), r => r.Count);
    }

    [Fact]
    public async Task CountByOfficeAndStatus_EveryOffice_GroupsPerConfigOfficeAndStatus()
    {
        // Record 29's Final proposal does not count. Red-tested by dropping the record filter.
        Dictionary<(int?, string?), int> counts = await CountAsync(Scope());

        Assert.Equal(new Dictionary<(int?, string?), int>
        {
            [(OpaOffice, "Draft")] = 1, [(OpaOffice, "Final")] = 1, [(OpaOffice, null)] = 2,
            [(PtoOffice, "Final")] = 1, [(PtoOffice, null)] = 1,
        }, counts);
    }

    [Fact]
    public async Task CountByOfficeAndStatus_OneOffice_CountsOnlyIt()
    {
        Dictionary<(int?, string?), int> counts = await CountAsync(Scope(groups: [PtoGroup]));

        Assert.Equal(2, counts.Values.Sum());
        Assert.All(counts.Keys, k => Assert.Equal(PtoOffice, k.Item1));
    }

    [Fact]
    public async Task CountByOfficeAndStatus_DivisionPair_CountsWhatTheListShows()
    {
        // A division encoder of OPA allowed program 002 only: just project 305 (none).
        Dictionary<(int?, string?), int> counts =
            await CountAsync(Scope(groups: [OpaGroup], narrowed: [OpaGroup], allowed: ["002"]));

        Assert.Equal(new Dictionary<(int?, string?), int> { [(OpaOffice, null)] = 1 }, counts);
    }

    [Fact]
    public async Task ListNeedingAttention_NoneFirstThenDrafts_NeverFinal_ByRefCode()
    {
        await using AppDbContext db = new(_options);
        IReadOnlyList<ProposalAttentionRow> rows = await new InvestmentProposalRepository(db)
            .ListNeedingAttentionAsync(Scope(groups: [OpaGroup]), take: 10);

        // None: 301 (program 001, project 003), 305 (program 002). Then the Draft 300. 304 is Final.
        Assert.Equal([301, 305, 300], rows.Select(r => r.AipProjectId).ToList());
        Assert.Equal((1, "Draft"), (rows[2].ProposalId!.Value, rows[2].ProposalStatus!));
        Assert.Null(rows[0].ProposalId);
    }

    [Fact]
    public async Task ListNeedingAttention_TakesOnlyTheFirstFew()
    {
        await using AppDbContext db = new(_options);
        IReadOnlyList<ProposalAttentionRow> rows = await new InvestmentProposalRepository(db)
            .ListNeedingAttentionAsync(Scope(groups: [OpaGroup]), take: 1);

        Assert.Equal([301], rows.Select(r => r.AipProjectId).ToList());
    }
}
