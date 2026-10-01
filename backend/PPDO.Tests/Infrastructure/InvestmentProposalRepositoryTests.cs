using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="InvestmentProposalRepository"/> (PPDO-154). Runs against Sqlite in the pattern of
/// <see cref="AipDivisionSubmissionRepositoryTests"/>. The DDL mirrors the migration's columns.
/// The FKs to AIP tables and users are left out, because these tests never touch those tables.
/// The cascade and NO ACTION rules on the AIP side were checked on SQL Server when the migration
/// was applied (see the PR).
///
/// <para>
/// <c>row_version</c> is SQL Server <c>rowversion</c> in production. Here a default plus an
/// update trigger stand in for it, so a write really does change the stored version.
/// </para>
/// </summary>
public sealed class InvestmentProposalRepositoryTests : IDisposable
{
    private const int Project = 501;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InvestmentProposalRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            PRAGMA foreign_keys = ON;
            CREATE TABLE investment_proposals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                aip_project_id INTEGER NOT NULL UNIQUE,
                status TEXT NOT NULL DEFAULT 'Draft',
                project_location TEXT NULL, hgdg_checklist TEXT NULL, hgdg_score TEXT NULL,
                description TEXT NULL, rationale TEXT NULL, general_objective TEXT NULL,
                partnership_sustainability TEXT NULL, womens_impact_strategy TEXT NULL,
                project_supervisor TEXT NULL, project_manager TEXT NULL,
                direct_same_as_summary INTEGER NOT NULL DEFAULT 1,
                signatory1_label TEXT NULL, signatory1_name TEXT NULL, signatory1_position TEXT NULL,
                signatory2_label TEXT NULL, signatory2_name TEXT NULL, signatory2_position TEXT NULL,
                signatory3_label TEXT NULL, signatory3_name TEXT NULL, signatory3_position TEXT NULL,
                snapshot_json TEXT NULL, finalized_at TEXT NULL, finalized_by_user_id TEXT NULL,
                created_at TEXT NOT NULL, created_by_user_id TEXT NULL,
                updated_at TEXT NOT NULL, updated_by_user_id TEXT NULL,
                row_version BLOB NOT NULL DEFAULT (randomblob(8))
            );
            CREATE TRIGGER investment_proposals_row_version AFTER UPDATE ON investment_proposals
            BEGIN
                UPDATE investment_proposals SET row_version = randomblob(8) WHERE id = NEW.id;
            END;
            CREATE TABLE investment_proposal_beneficiaries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                section TEXT NOT NULL, label TEXT NOT NULL, male INTEGER NULL, female INTEGER NULL,
                sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_benefits (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                sector TEXT NOT NULL, benefit TEXT NULL, cost TEXT NULL
            );
            CREATE TABLE investment_proposal_logframe (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                level TEXT NOT NULL, target TEXT NULL, verification TEXT NULL
            );
            CREATE TABLE investment_proposal_groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                label TEXT NOT NULL, sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_work_plan_rows (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                aip_activity_id INTEGER NULL,
                group_id INTEGER NULL REFERENCES investment_proposal_groups(id),
                name TEXT NULL, performance_target TEXT NULL, gender_issues TEXT NULL,
                timeline TEXT NULL, opr TEXT NULL, sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_team_members (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                name TEXT NOT NULL, sex TEXT NOT NULL, gad_trainings TEXT NULL, expertise TEXT NULL,
                sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_capacity_trainings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                member_name TEXT NOT NULL, training TEXT NULL, sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_monitoring (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                phase TEXT NOT NULL, activity TEXT NOT NULL, schedule TEXT NULL, tools TEXT NULL,
                sort_order INTEGER NOT NULL
            );
            CREATE TABLE investment_proposal_risks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL REFERENCES investment_proposals(id) ON DELETE CASCADE,
                risk TEXT NOT NULL, prevention TEXT NULL, monitoring TEXT NULL,
                sort_order INTEGER NOT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    private static readonly string[] ChildTables =
    [
        "investment_proposal_beneficiaries", "investment_proposal_benefits",
        "investment_proposal_logframe", "investment_proposal_groups",
        "investment_proposal_work_plan_rows", "investment_proposal_team_members",
        "investment_proposal_capacity_trainings", "investment_proposal_monitoring",
        "investment_proposal_risks",
    ];

    /// <summary>A proposal with one row in every child table, the work-plan row inside the group.</summary>
    private static InvestmentProposal FullProposal()
    {
        InvestmentProposalGroup group = new() { Label = "Preparatory", SortOrder = 0 };
        return new InvestmentProposal
        {
            AipProjectId         = Project,
            Status               = InvestmentProposalStatus.Draft,
            ProjectLocation      = "San Jose",
            HgdgScore            = 15.5m,
            Description          = "<p>Seed support</p>",
            DirectSameAsSummary  = true,
            Signatory1Label      = "Prepared by",
            CreatedAt            = DateTime.UtcNow,
            UpdatedAt            = DateTime.UtcNow,
            Beneficiaries        = [new() { Section = InvestmentProposalBeneficiarySection.Summary, Label = "Farmers", Male = 10, Female = 12 }],
            Benefits             = [new() { Sector = "Economic", Benefit = "Higher yield" }],
            Logframe             = [new() { Level = InvestmentProposalLogframeLevel.Impact, Target = "Yield +10%" }],
            Groups               = [group],
            WorkPlanRows         = [new() { Name = "Preparation of Travel Order", Group = group, SortOrder = 0 }],
            TeamMembers          = [new() { Name = "Juan", Sex = InvestmentProposalSex.Male }],
            CapacityTrainings    = [new() { MemberName = "Juan", Training = "GAD 101" }],
            Monitoring           = [new() { Phase = InvestmentProposalMonitoringPhase.Pre, Activity = "Baseline survey" }],
            Risks                = [new() { Risk = "Typhoon", Prevention = "Reschedule" }],
        };
    }

    private async Task<int> SeedAsync(InvestmentProposal proposal)
    {
        await using AppDbContext db = new(_options);
        InvestmentProposalRepository repo = new(db);
        await repo.AddAsync(proposal);
        await repo.SaveChangesAsync();
        return proposal.Id;
    }

    private long CountRows(string table)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task SaveReloadDelete_WithEveryChildTable_RoundTripsAndDeletesChildren()
    {
        int id = await SeedAsync(FullProposal());

        await using (AppDbContext db = new(_options))
        {
            InvestmentProposalRepository repo = new(db);
            InvestmentProposal? loaded = await repo.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal(15.5m, loaded.HgdgScore);
            Assert.NotEmpty(loaded.RowVersion);
            Assert.Single(loaded.Beneficiaries);
            Assert.Single(loaded.Benefits);
            Assert.Single(loaded.Logframe);
            Assert.Single(loaded.Groups);
            Assert.Equal(loaded.Groups.Single().Id, Assert.Single(loaded.WorkPlanRows).GroupId);
            Assert.Single(loaded.TeamMembers);
            Assert.Single(loaded.CapacityTrainings);
            Assert.Single(loaded.Monitoring);
            Assert.Single(loaded.Risks);

            // The NO ACTION FK on group_id means a group can only go once nothing points at it.
            // The service's replace-all save clears the rows first. Removing the proposal does both.
            repo.Remove(loaded);
            await repo.SaveChangesAsync();
        }

        Assert.Equal(0, CountRows("investment_proposals"));
        Assert.All(ChildTables, t => Assert.Equal(0, CountRows(t)));
    }

    [Fact]
    public async Task Save_DirectSameAsSummaryFalse_IsNotOverwrittenByTheColumnDefault()
    {
        // The column defaults to 1. EF must still write an explicit false, or unticking
        // "Same as Section A" on a new proposal would come back ticked.
        InvestmentProposal proposal = FullProposal();
        proposal.DirectSameAsSummary = false;
        int id = await SeedAsync(proposal);

        await using AppDbContext db = new(_options);
        Assert.False((await new InvestmentProposalRepository(db).GetByIdAsync(id))!.DirectSameAsSummary);
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ReturnsNull()
    {
        await using AppDbContext db = new(_options);
        Assert.Null(await new InvestmentProposalRepository(db).GetByIdAsync(999));
    }

    [Fact]
    public async Task GetByProjectIdAsync_And_ExistsForProjectAsync_MatchOnlyThatProject()
    {
        int id = await SeedAsync(FullProposal());

        await using AppDbContext db = new(_options);
        InvestmentProposalRepository repo = new(db);

        Assert.Equal(id, (await repo.GetByProjectIdAsync(Project))!.Id);
        Assert.Null(await repo.GetByProjectIdAsync(Project + 1));
        Assert.True(await repo.ExistsForProjectAsync(Project));
        Assert.False(await repo.ExistsForProjectAsync(Project + 1));
    }

    [Fact]
    public async Task SaveChangesAsync_ExpectedVersionIsStale_ThrowsConflictAndWritesNothing()
    {
        int id = await SeedAsync(FullProposal());

        byte[] loadedVersion;
        await using (AppDbContext first = new(_options))
            loadedVersion = (await new InvestmentProposalRepository(first).GetByIdAsync(id))!.RowVersion;

        // Someone else saves in between, which changes the stored version.
        await using (AppDbContext other = new(_options))
        {
            InvestmentProposalRepository repo = new(other);
            InvestmentProposal p = (await repo.GetByIdAsync(id))!;
            p.ProjectLocation = "Mamburao";
            p.UpdatedAt = DateTime.UtcNow;
            await repo.SaveChangesAsync();
        }

        await using (AppDbContext db = new(_options))
        {
            InvestmentProposalRepository repo = new(db);
            InvestmentProposal p = (await repo.GetByIdAsync(id))!;
            repo.SetExpectedRowVersion(p, loadedVersion);
            p.ProjectLocation = "Sablayan";
            p.UpdatedAt = DateTime.UtcNow;

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repo.SaveChangesAsync());
        }

        await using AppDbContext verify = new(_options);
        Assert.Equal("Mamburao", (await new InvestmentProposalRepository(verify).GetByIdAsync(id))!.ProjectLocation);
    }

    [Fact]
    public async Task SaveChangesAsync_ExpectedVersionIsCurrent_Saves()
    {
        int id = await SeedAsync(FullProposal());

        await using (AppDbContext db = new(_options))
        {
            InvestmentProposalRepository repo = new(db);
            InvestmentProposal p = (await repo.GetByIdAsync(id))!;
            repo.SetExpectedRowVersion(p, p.RowVersion.ToArray());
            p.ProjectLocation = "Sablayan";
            p.UpdatedAt = DateTime.UtcNow;
            await repo.SaveChangesAsync();
        }

        await using AppDbContext verify = new(_options);
        Assert.Equal("Sablayan", (await new InvestmentProposalRepository(verify).GetByIdAsync(id))!.ProjectLocation);
    }
}
