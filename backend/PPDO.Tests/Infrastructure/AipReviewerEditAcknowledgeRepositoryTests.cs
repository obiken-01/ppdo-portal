using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The two reads/writes behind the office's "PPDO edited your AIP" notice (v1.9.0 — PPDO-213):
/// <see cref="AipReviewCommentRepository.CountUnacknowledgedReviewerEditsAsync"/> and
/// <see cref="AipReviewCommentRepository.AcknowledgeReviewerEditsAsync"/>.
///
/// <para>
/// ⚠️ <b>Against a real database</b>, because both are set-based: a GROUP BY count and a bulk
/// UPDATE. Whether they translate, and whether the WHERE touches only reviewer-edit rows of the
/// given groups, only a query that runs can show. Hand-written DDL with the columns these touch —
/// the Sqlite in-memory pattern from <see cref="AipNotificationRepositoryTests"/>.
/// </para>
/// </summary>
public sealed class AipReviewerEditAcknowledgeRepositoryTests : IDisposable
{
    private const int OfficeXGroup = 1;
    private const int OfficeYGroup = 2;

    private static readonly Guid Reviewer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Earlier  = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Encoder  = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipReviewerEditAcknowledgeRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw($"""
            CREATE TABLE aip_review_comments (
                id INTEGER PRIMARY KEY,
                aip_office_id INTEGER NOT NULL,
                node_type TEXT NOT NULL,
                node_id INTEGER NOT NULL,
                author_id TEXT NOT NULL,
                author_side TEXT NOT NULL,
                body TEXT NOT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT NULL,
                resolved_by_id TEXT NULL,
                is_reviewer_edit INTEGER NOT NULL DEFAULT 0,
                acknowledged_at TEXT NULL,
                acknowledged_by_id TEXT NULL,
                parent_comment_id INTEGER NULL
            );

            INSERT INTO aip_review_comments
                (id, aip_office_id, node_type, node_id, author_id, author_side, body, created_at,
                 resolved_at, resolved_by_id, is_reviewer_edit, acknowledged_at, acknowledged_by_id) VALUES
                -- Office X: two pending edits, one already seen, one ordinary PPDO comment.
                (1, {OfficeXGroup}, 'Activity', 41, '{Reviewer}', 'Ppdo', 'PPDO edited A', '2026-10-14 01:00:00', '2026-10-14 01:00:00', '{Reviewer}', 1, NULL, NULL),
                (2, {OfficeXGroup}, 'Activity', 42, '{Reviewer}', 'Ppdo', 'PPDO added B',  '2026-10-14 01:05:00', '2026-10-14 01:05:00', '{Reviewer}', 1, NULL, NULL),
                (3, {OfficeXGroup}, 'Activity', 43, '{Reviewer}', 'Ppdo', 'PPDO edited C', '2026-10-13 01:00:00', '2026-10-13 01:00:00', '{Reviewer}', 1, '2026-10-13 05:00:00', '{Earlier}'),
                (4, {OfficeXGroup}, 'Activity', 41, '{Reviewer}', 'Ppdo', 'Please check',  '2026-10-14 02:00:00', NULL, NULL, 0, NULL, NULL),
                -- Office Y: one pending edit, which X's acknowledgement must not touch.
                (5, {OfficeYGroup}, 'Activity', 51, '{Reviewer}', 'Ppdo', 'PPDO edited D', '2026-10-14 03:00:00', '2026-10-14 03:00:00', '{Reviewer}', 1, NULL, NULL);
            """);
    }

    public void Dispose() => _connection.Dispose();

    private AipReviewCommentRepository Repo(out AppDbContext ctx)
    {
        ctx = new AppDbContext(_options);
        return new AipReviewCommentRepository(ctx);
    }

    [Fact]
    public async Task CountUnacknowledgedReviewerEdits_CountsOnlyPendingEditsPerGroup()
    {
        AipReviewCommentRepository repo = Repo(out AppDbContext ctx);
        using (ctx)
        {
            IReadOnlyDictionary<int, int> counts =
                await repo.CountUnacknowledgedReviewerEditsAsync([OfficeXGroup, OfficeYGroup]);

            Assert.Equal(2, counts[OfficeXGroup]);   // not row 3 (seen), not row 4 (ordinary)
            Assert.Equal(1, counts[OfficeYGroup]);
        }
    }

    [Fact]
    public async Task CountUnacknowledgedReviewerEdits_OnlyLooksAtTheGroupsItIsGiven()
    {
        AipReviewCommentRepository repo = Repo(out AppDbContext ctx);
        using (ctx)
        {
            IReadOnlyDictionary<int, int> counts = await repo.CountUnacknowledgedReviewerEditsAsync([OfficeXGroup]);

            Assert.False(counts.ContainsKey(OfficeYGroup));
            Assert.Empty(await repo.CountUnacknowledgedReviewerEditsAsync([]));
        }
    }

    [Fact]
    public async Task AcknowledgeReviewerEdits_StampsOnlyThatOfficesPendingEdits()
    {
        DateTime at = new(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc);
        AipReviewCommentRepository repo = Repo(out AppDbContext ctx);
        using (ctx)
        {
            int stamped = await repo.AcknowledgeReviewerEditsAsync([OfficeXGroup], Encoder, at);
            Assert.Equal(2, stamped);
        }

        AipReviewCommentRepository check = Repo(out AppDbContext ctx2);
        using (ctx2)
        {
            IReadOnlyDictionary<int, int> counts =
                await check.CountUnacknowledgedReviewerEditsAsync([OfficeXGroup, OfficeYGroup]);
            Assert.False(counts.ContainsKey(OfficeXGroup));
            Assert.Equal(1, counts[OfficeYGroup]);   // another office is untouched

            var rows = await ctx2.AipReviewComments.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
            Assert.Equal(Encoder, rows[0].AcknowledgedById);
            Assert.Equal(Encoder, rows[1].AcknowledgedById);
            Assert.Equal(Earlier, rows[2].AcknowledgedById);   // an earlier acknowledgement is kept
            Assert.Null(rows[3].AcknowledgedAt);               // an ordinary comment is never stamped
            Assert.Null(rows[4].AcknowledgedAt);
        }
    }

    [Fact]
    public async Task AcknowledgeReviewerEdits_IsIdempotent()
    {
        DateTime at = DateTime.UtcNow;
        AipReviewCommentRepository repo = Repo(out AppDbContext ctx);
        using (ctx)
        {
            await repo.AcknowledgeReviewerEditsAsync([OfficeXGroup], Encoder, at);
            Assert.Equal(0, await repo.AcknowledgeReviewerEditsAsync([OfficeXGroup], Encoder, at));
            Assert.Equal(0, await repo.AcknowledgeReviewerEditsAsync([], Encoder, at));
        }
    }
}
