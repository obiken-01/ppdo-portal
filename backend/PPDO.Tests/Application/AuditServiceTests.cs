using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AuditService"/> (RAL-77).
/// Verifies that LogAsync stores the correct action, snapshots, user ID,
/// and raises for an unauthenticated caller.
/// IRepository&lt;AuditLog&gt; and ICurrentUserService are mocked — no DB access.
/// </summary>
public sealed class AuditServiceTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static (AuditService sut, List<AuditLog> captured) Build(Guid? userId = null)
    {
        List<AuditLog> captured = [];

        Mock<IRepository<AuditLog>> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => captured.Add(log))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<ICurrentUserService> currentUser = new();
        currentUser.Setup(u => u.UserId).Returns(userId ?? UserId);

        return (new AuditService(repo.Object, currentUser.Object), captured);
    }

    // ── action / snapshot correctness ────────────────────────────────────────

    [Fact]
    public async Task LogAsync_Create_OldValuesIsNull()
    {
        (AuditService sut, List<AuditLog> captured) = Build();

        await sut.LogAsync("accounts", 7, AuditAction.Create,
            oldValues: null, newValues: new { AccountTitle = "Salaries", IsActive = true });

        Assert.Single(captured);
        AuditLog log = captured[0];
        Assert.Null(log.OldValues);
        Assert.NotNull(log.NewValues);
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal("accounts", log.TableName);
        Assert.Equal(7, log.RecordId);
    }

    [Fact]
    public async Task LogAsync_Delete_NewValuesIsNull()
    {
        (AuditService sut, List<AuditLog> captured) = Build();

        await sut.LogAsync("offices", 3, AuditAction.Delete,
            oldValues: new { IsActive = true }, newValues: null);

        Assert.Single(captured);
        AuditLog log = captured[0];
        Assert.NotNull(log.OldValues);
        Assert.Null(log.NewValues);
        Assert.Equal(AuditAction.Delete, log.Action);
    }

    [Fact]
    public async Task LogAsync_Update_BothSnapshotsSerialized()
    {
        (AuditService sut, List<AuditLog> captured) = Build();

        await sut.LogAsync("funding_sources", 2, AuditAction.Update,
            oldValues: new { Name = "Old Name", IsActive = true },
            newValues: new { Name = "New Name", IsActive = true });

        Assert.Single(captured);
        AuditLog log = captured[0];
        Assert.NotNull(log.OldValues);
        Assert.NotNull(log.NewValues);
        Assert.Contains("Old Name", log.OldValues, StringComparison.Ordinal);
        Assert.Contains("New Name", log.NewValues, StringComparison.Ordinal);
        Assert.Equal(AuditAction.Update, log.Action);
    }

    // ── user identity ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_SetsChangedByFromCurrentUser()
    {
        Guid expectedId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        (AuditService sut, List<AuditLog> captured) = Build(userId: expectedId);

        await sut.LogAsync("accounts", 1, AuditAction.Create,
            oldValues: null, newValues: new { AccountTitle = "Test" });

        Assert.Equal(expectedId, captured[0].ChangedById);
    }

    [Fact]
    public async Task LogAsync_NullUserId_Throws()
    {
        (AuditService sut, _) = Build(userId: null);

        // Override the mock so UserId returns null specifically
        Mock<IRepository<AuditLog>> repo = new();
        Mock<ICurrentUserService> anonUser = new();
        anonUser.Setup(u => u.UserId).Returns((Guid?)null);
        AuditService svcWithNoUser = new(repo.Object, anonUser.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svcWithNoUser.LogAsync("accounts", 1, AuditAction.Create,
                oldValues: null, newValues: new { AccountTitle = "Test" }));
    }
}
