using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="NotificationRepository"/> (PPDO-221) against a real database (Sqlite), in the pattern of
/// <see cref="InvestmentProposalCommentRepositoryTests"/>; the user FK is left out because no query
/// here joins to users.
///
/// <para>
/// ⚠️ <b>The cross-user rule lives in these WHERE clauses</b>: a user only ever reads or marks their
/// own rows, and another user's id must behave exactly like a missing one. Red-tested by dropping
/// <c>n.UserId == userId</c> from <c>MarkReadAsync</c> (see the PR).
/// </para>
/// </summary>
public sealed class NotificationRepositoryTests : IDisposable
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob   = Guid.NewGuid();
    private static readonly DateTime Now   = new(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Since = Now.AddDays(-90);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public NotificationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE notifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL,
                type TEXT NOT NULL,
                text TEXT NOT NULL,
                link TEXT NOT NULL,
                created_at TEXT NOT NULL,
                read_at TEXT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Raw SQL, not <c>db.Add</c>: <c>AppDbContext</c> stamps <c>CreatedAt</c> with the real clock on
    /// every save, which would erase the back-dated rows the 90-day window is tested with.
    /// </summary>
    private long Seed(Guid user, DateTime createdAt, bool read = false)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notifications (user_id, type, text, link, created_at, read_at)
            VALUES ($user, 'ProposalComment', 't', '/l', $created, $read);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$user", user.ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$created", createdAt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"));
        cmd.Parameters.AddWithValue("$read", read ? createdAt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF") : DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    private Notification Row(long id)
    {
        using AppDbContext db = new(_options);
        return db.Notifications.AsNoTracking().Single(n => n.Id == id);
    }

    // ── Cross-user ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkRead_AnotherUsersRow_IsFalse_AndTheRowStaysUnread()
    {
        long bobs = Seed(Bob, Now);

        using AppDbContext db = new(_options);
        Assert.False(await new NotificationRepository(db).MarkReadAsync(bobs, Alice, Now));
        Assert.Null(Row(bobs).ReadAt);
    }

    [Fact]
    public async Task MarkRead_AMissingId_IsFalse_TheSameAnswerAsAnotherUsersRow()
    {
        using AppDbContext db = new(_options);
        Assert.False(await new NotificationRepository(db).MarkReadAsync(999, Alice, Now));
    }

    [Fact]
    public async Task MarkRead_OwnRow_IsTrue_AndIdempotent_KeepingTheFirstReadTime()
    {
        long mine = Seed(Alice, Now.AddHours(-2));

        using AppDbContext db = new(_options);
        NotificationRepository repo = new(db);
        Assert.True(await repo.MarkReadAsync(mine, Alice, Now.AddHours(-1)));
        Assert.True(await repo.MarkReadAsync(mine, Alice, Now));

        Assert.Equal(Now.AddHours(-1), Row(mine).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_TouchesOnlyTheCallersRows()
    {
        long a1 = Seed(Alice, Now), a2 = Seed(Alice, Now), b1 = Seed(Bob, Now);

        using AppDbContext db = new(_options);
        Assert.Equal(2, await new NotificationRepository(db).MarkAllReadAsync(Alice, Now));

        Assert.NotNull(Row(a1).ReadAt);
        Assert.NotNull(Row(a2).ReadAt);
        Assert.Null(Row(b1).ReadAt);
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountUnread_IsOwnUnreadRowsInTheWindow()
    {
        Seed(Alice, Now); Seed(Alice, Now.AddDays(-10));
        Seed(Alice, Now, read: true);           // read
        Seed(Alice, Now.AddDays(-91));          // older than 90 days
        Seed(Bob, Now);                         // another user's

        using AppDbContext db = new(_options);
        Assert.Equal(2, await new NotificationRepository(db).CountUnreadAsync(Alice, Since));
    }

    [Fact]
    public async Task GetPage_IsOwnRowsNewestFirst_WithTheTotal_AndTheUnreadFilter()
    {
        long older = Seed(Alice, Now.AddDays(-2));
        long newer = Seed(Alice, Now.AddDays(-1), read: true);
        long newest = Seed(Alice, Now);
        Seed(Alice, Now.AddDays(-100));
        Seed(Bob, Now);

        using AppDbContext db = new(_options);
        NotificationRepository repo = new(db);

        (IReadOnlyList<Notification> all, int total) = await repo.GetPageAsync(Alice, Since, false, 0, 2);
        Assert.Equal(3, total);
        Assert.Equal([newest, newer], all.Select(n => n.Id));

        (IReadOnlyList<Notification> unread, int unreadTotal) = await repo.GetPageAsync(Alice, Since, true, 0, 20);
        Assert.Equal(2, unreadTotal);
        Assert.Equal([newest, older], unread.Select(n => n.Id));
    }

    [Fact]
    public async Task AddRange_StagesWithoutSaving_SoTheEventsOwnSaveWritesThem()
    {
        using AppDbContext db = new(_options);
        NotificationRepository repo = new(db);
        await repo.AddRangeAsync([new Notification { UserId = Alice, Type = "t", Text = "x", Link = "/l", CreatedAt = Now }]);

        using (AppDbContext other = new(_options))
            Assert.Equal(0, other.Notifications.Count());

        await db.SaveChangesAsync();
        using AppDbContext after = new(_options);
        Assert.Equal(1, after.Notifications.Count());
    }
}
