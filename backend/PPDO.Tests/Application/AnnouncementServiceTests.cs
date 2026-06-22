using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Announcements;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AnnouncementService"/> (RAL-83).
/// Covers: HTML sanitization, status transitions, access control, and hard-delete restrictions.
/// </summary>
public sealed class AnnouncementServiceTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static User Admin() => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Admin,
        FullName = "Admin User", Username = "admin",
        PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User SuperAdmin() => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.SuperAdmin,
        FullName = "Super Admin", Username = "superadmin",
        PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User Staff() => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff,
        FullName = "Staff User", Username = "staff",
        PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Announcement Ann(
        AnnouncementStatus status = AnnouncementStatus.Draft,
        DateTime? publishedAt = null,
        Guid? id = null,
        Guid? createdById = null) => new()
    {
        Id          = id ?? Guid.NewGuid(),
        Title       = "Test Title",
        Content     = "<p>Hello</p>",
        Status      = status,
        PublishedAt = publishedAt,
        CreatedById = createdById ?? Guid.NewGuid(),
        CreatedAt   = DateTime.UtcNow,
        UpdatedAt   = DateTime.UtcNow,
        CreatedBy   = new User { FullName = "Author", Username = "author", PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
    };

    private static (AnnouncementService sut, Mock<IRepository<Announcement>> repo) Build(List<Announcement> seed)
    {
        Mock<IRepository<Announcement>> repo = new();

        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => seed.AsReadOnly());

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => seed.FirstOrDefault(a => a.Id == id));

        repo.Setup(r => r.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Callback<Announcement, CancellationToken>((a, _) => seed.Add(a))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.UpdateAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.DeleteAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Callback<Announcement, CancellationToken>((a, _) => seed.Remove(a))
            .Returns(Task.CompletedTask);

        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return (new AnnouncementService(repo.Object, NullLogger<AnnouncementService>.Instance), repo);
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SetsDraftStatus()
    {
        List<Announcement> seed = [];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<AnnouncementDto> result =
            await sut.CreateAsync(Admin(), new CreateAnnouncementDto("Title", "<p>Hello</p>"));

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Draft, result.Value!.Status);
        Assert.Single(seed);
    }

    [Fact]
    public async Task CreateAsync_SanitizesHtmlContent()
    {
        List<Announcement> seed = [];
        (AnnouncementService sut, _) = Build(seed);

        string malicious = "<p>Hello</p><script>alert('xss')</script>";
        ServiceResult<AnnouncementDto> result =
            await sut.CreateAsync(Admin(), new CreateAnnouncementDto("Title", malicious));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("<script>", result.Value!.Content);
        Assert.Contains("Hello", result.Value.Content);
    }

    [Fact]
    public async Task CreateAsync_NonAdmin_ReturnsForbidden()
    {
        List<Announcement> seed = [];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<AnnouncementDto> result =
            await sut.CreateAsync(Staff(), new CreateAnnouncementDto("Title", "<p>Content</p>"));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Empty(seed);
    }

    [Fact]
    public async Task CreateAsync_EmptyTitle_ReturnsBadRequest()
    {
        (AnnouncementService sut, _) = Build([]);

        ServiceResult<AnnouncementDto> result =
            await sut.CreateAsync(Admin(), new CreateAnnouncementDto("", "<p>Content</p>"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── UpdateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SanitizesHtmlContent()
    {
        Announcement existing = Ann();
        List<Announcement> seed = [existing];
        (AnnouncementService sut, _) = Build(seed);

        string malicious = "<b>Safe</b><script>evil()</script>";
        ServiceResult<AnnouncementDto> result =
            await sut.UpdateAsync(Admin(), existing.Id, new UpdateAnnouncementDto("New Title", malicious));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("<script>", result.Value!.Content);
        Assert.Contains("Safe", result.Value.Content);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        (AnnouncementService sut, _) = Build([]);

        ServiceResult<AnnouncementDto> result =
            await sut.UpdateAsync(Admin(), Guid.NewGuid(), new UpdateAnnouncementDto("T", "<p>C</p>"));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_NonAdmin_ReturnsForbidden()
    {
        Announcement existing = Ann();
        (AnnouncementService sut, _) = Build([existing]);

        ServiceResult<AnnouncementDto> result =
            await sut.UpdateAsync(Staff(), existing.Id, new UpdateAnnouncementDto("T", "<p>C</p>"));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── PublishAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_SetsStatusAndPublishedAt()
    {
        Announcement draft = Ann(AnnouncementStatus.Draft);
        (AnnouncementService sut, _) = Build([draft]);

        ServiceResult<AnnouncementDto> result = await sut.PublishAsync(Admin(), draft.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Published, result.Value!.Status);
        Assert.NotNull(result.Value.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublished_DoesNotChangePublishedAt()
    {
        DateTime original = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Announcement published = Ann(AnnouncementStatus.Published, publishedAt: original);
        (AnnouncementService sut, _) = Build([published]);

        ServiceResult<AnnouncementDto> result = await sut.PublishAsync(Admin(), published.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, result.Value!.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_NonAdmin_ReturnsForbidden()
    {
        Announcement draft = Ann();
        (AnnouncementService sut, _) = Build([draft]);

        ServiceResult<AnnouncementDto> result = await sut.PublishAsync(Staff(), draft.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── UnpublishAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UnpublishAsync_Published_SetsDraft()
    {
        Announcement published = Ann(AnnouncementStatus.Published, publishedAt: DateTime.UtcNow);
        (AnnouncementService sut, _) = Build([published]);

        ServiceResult<AnnouncementDto> result = await sut.UnpublishAsync(Admin(), published.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Draft, result.Value!.Status);
    }

    [Fact]
    public async Task UnpublishAsync_NotPublished_ReturnsBadRequest()
    {
        Announcement draft = Ann(AnnouncementStatus.Draft);
        (AnnouncementService sut, _) = Build([draft]);

        ServiceResult<AnnouncementDto> result = await sut.UnpublishAsync(Admin(), draft.Id);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── ArchiveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_Published_SetsArchived()
    {
        Announcement published = Ann(AnnouncementStatus.Published, publishedAt: DateTime.UtcNow);
        (AnnouncementService sut, _) = Build([published]);

        ServiceResult<AnnouncementDto> result = await sut.ArchiveAsync(Admin(), published.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Archived, result.Value!.Status);
    }

    [Fact]
    public async Task ArchiveAsync_Draft_ReturnsBadRequest()
    {
        Announcement draft = Ann(AnnouncementStatus.Draft);
        (AnnouncementService sut, _) = Build([draft]);

        ServiceResult<AnnouncementDto> result = await sut.ArchiveAsync(Admin(), draft.Id);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── DeleteAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Draft_Succeeds()
    {
        Announcement draft = Ann(AnnouncementStatus.Draft);
        List<Announcement> seed = [draft];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<bool> result = await sut.DeleteAsync(Admin(), draft.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(seed);
    }

    [Fact]
    public async Task DeleteAsync_Archived_Succeeds()
    {
        Announcement archived = Ann(AnnouncementStatus.Archived);
        List<Announcement> seed = [archived];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<bool> result = await sut.DeleteAsync(Admin(), archived.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(seed);
    }

    [Fact]
    public async Task DeleteAsync_Published_ReturnsConflict()
    {
        Announcement published = Ann(AnnouncementStatus.Published, publishedAt: DateTime.UtcNow);
        List<Announcement> seed = [published];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<bool> result = await sut.DeleteAsync(Admin(), published.Id);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Single(seed);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFound()
    {
        (AnnouncementService sut, _) = Build([]);

        ServiceResult<bool> result = await sut.DeleteAsync(Admin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteAsync_NonAdmin_ReturnsForbidden()
    {
        Announcement draft = Ann();
        (AnnouncementService sut, _) = Build([draft]);

        ServiceResult<bool> result = await sut.DeleteAsync(Staff(), draft.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── GetPublishedAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublishedAsync_ReturnsOnlyPublished_OrderedByPublishedAtDesc()
    {
        DateTime older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        List<Announcement> seed =
        [
            Ann(AnnouncementStatus.Published, publishedAt: older),
            Ann(AnnouncementStatus.Draft),
            Ann(AnnouncementStatus.Published, publishedAt: newer),
            Ann(AnnouncementStatus.Archived),
        ];
        (AnnouncementService sut, _) = Build(seed);

        IReadOnlyList<AnnouncementPublicDto> result = await sut.GetPublishedAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(newer, result[0].PublishedAt);
        Assert.Equal(older, result[1].PublishedAt);
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_Admin_ReturnsAllStatuses()
    {
        List<Announcement> seed =
        [
            Ann(AnnouncementStatus.Draft),
            Ann(AnnouncementStatus.Published, publishedAt: DateTime.UtcNow),
            Ann(AnnouncementStatus.Archived),
        ];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<IReadOnlyList<AnnouncementDto>> result = await sut.GetAllAsync(Admin());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public async Task GetAllAsync_NonAdmin_ReturnsForbidden()
    {
        (AnnouncementService sut, _) = Build([Ann()]);

        ServiceResult<IReadOnlyList<AnnouncementDto>> result = await sut.GetAllAsync(Staff());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── SuperAdmin access ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SuperAdmin_Succeeds()
    {
        List<Announcement> seed = [];
        (AnnouncementService sut, _) = Build(seed);

        ServiceResult<AnnouncementDto> result =
            await sut.CreateAsync(SuperAdmin(), new CreateAnnouncementDto("Title", "<p>Content</p>"));

        Assert.True(result.IsSuccess);
        Assert.Single(seed);
    }
}
