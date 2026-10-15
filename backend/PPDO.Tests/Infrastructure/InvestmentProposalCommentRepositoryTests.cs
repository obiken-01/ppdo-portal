using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The two counts of <see cref="InvestmentProposalCommentRepository"/> (PPDO-219): the Finalize gate's
/// <c>CountOpenAsync</c> and the list pill's grouped count. Sqlite, in the pattern of
/// <see cref="InvestmentProposalRepositoryTests"/>; the FKs are left out because these queries never
/// join to proposals or users. The cascade rule was checked on SQL Server (see the PR).
/// </summary>
public sealed class InvestmentProposalCommentRepositoryTests : IDisposable
{
    private static readonly Guid Author = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InvestmentProposalCommentRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE investment_proposal_comments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                proposal_id INTEGER NOT NULL,
                body TEXT NOT NULL,
                author_side TEXT NOT NULL,
                author_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT NULL,
                resolved_by_id TEXT NULL
            );
            """);

        // proposal 1: two open + one resolved; proposal 2: one open; proposal 3: only resolved.
        Seed(1, open: true); Seed(1, open: true); Seed(1, open: false);
        Seed(2, open: true);
        Seed(3, open: false);
    }

    private void Seed(int proposalId, bool open)
    {
        using AppDbContext db = new(_options);
        db.InvestmentProposalComments.Add(new InvestmentProposalComment
        {
            ProposalId = proposalId, Body = "x", AuthorSide = ProposalCommentSide.Ppdo, AuthorId = Author,
            CreatedAt = DateTime.UtcNow,
            ResolvedAt = open ? null : DateTime.UtcNow, ResolvedById = open ? null : Author,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CountOpenAsync_CountsOnlyThisProposalsOpenComments()
    {
        using AppDbContext db = new(_options);
        InvestmentProposalCommentRepository repo = new(db);

        Assert.Equal(2, await repo.CountOpenAsync(1));
        Assert.Equal(1, await repo.CountOpenAsync(2));
        Assert.Equal(0, await repo.CountOpenAsync(3));   // resolved only
        Assert.Equal(0, await repo.CountOpenAsync(99));  // no comments
    }

    [Fact]
    public async Task CountOpenByProposalIdsAsync_GroupsInOneQuery_AndOmitsProposalsWithNone()
    {
        using AppDbContext db = new(_options);
        InvestmentProposalCommentRepository repo = new(db);

        IReadOnlyDictionary<int, int> counts = await repo.CountOpenByProposalIdsAsync([1, 2, 3, 99]);

        Assert.Equal(2, counts[1]);
        Assert.Equal(1, counts[2]);
        Assert.False(counts.ContainsKey(3));
        Assert.False(counts.ContainsKey(99));
    }

    [Fact]
    public async Task CountOpenByProposalIdsAsync_EmptyInput_ReturnsEmpty()
    {
        using AppDbContext db = new(_options);
        Assert.Empty(await new InvestmentProposalCommentRepository(db).CountOpenByProposalIdsAsync([]));
    }

    public void Dispose() => _connection.Dispose();
}
