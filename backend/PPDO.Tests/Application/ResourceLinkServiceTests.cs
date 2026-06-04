using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ResourceLinks;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ResourceLinkService"/> — written first (TDD).
/// IRepository&lt;ResourceLink&gt; is mocked; no database access occurs.
/// </summary>
public sealed class ResourceLinkServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, Division = Division.Admin, IsActive = true,
    };

    private static User MakeSuperAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "SA", Email = "sa@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.SuperAdmin, Division = Division.Admin, IsActive = true,
    };

    private static User MakeStaff(bool canManageLinks = true) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, Division = Division.Planning, IsActive = true,
        Group = new PermissionGroup
        {
            Id = Guid.NewGuid(), Name = "Planning Staff",
            CanManageResourceLinks = canManageLinks,
        },
    };

    private static User MakeObserver() => new()
    {
        Id = Guid.NewGuid(), FullName = "Observer", Email = "obs@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Observer, Division = Division.Admin, IsActive = true,
        Group = new PermissionGroup { Id = Guid.NewGuid(), Name = "Observer Default" },
    };

    private static ResourceLink MakeLink(int linkOrder = 1) => new()
    {
        Id = Guid.NewGuid(), Title = "PR Monitoring", Url = "https://example.com",
        Category = "Supply", CategoryOrder = 1, LinkOrder = linkOrder,
        IsActive = true, IsAdminCreated = true,
    };

    private static Mock<IRepository<ResourceLink>> RepoThatSaves()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<ResourceLink>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ResourceLink>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    private static ResourceLinkService BuildSut(Mock<IRepository<ResourceLink>> repo) =>
        new(repo.Object, new PermissionService(), NullLogger<ResourceLinkService>.Instance);

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveLinks_GroupedByCategoryOrder()
    {
        ResourceLink active   = MakeLink(1);
        ResourceLink inactive = MakeLink(2); inactive.IsActive = false;

        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, inactive]);

        IReadOnlyList<ResourceLinkCategoryDto> result =
            await BuildSut(repo).GetAllAsync();

        Assert.Single(result);
        Assert.Equal("Supply", result[0].Category);
        Assert.Single(result[0].Links);
    }

    [Fact]
    public async Task GetAllAsync_SortsLinksWithinCategoryByLinkOrder()
    {
        ResourceLink first  = MakeLink(2); first.Category  = "Supply";
        ResourceLink second = MakeLink(1); second.Category = "Supply";

        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);

        IReadOnlyList<ResourceLinkCategoryDto> result =
            await BuildSut(repo).GetAllAsync();

        Assert.Equal(second.Id, result[0].Links[0].Id); // linkOrder=1 comes first
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Admin_ReturnsOk_WithIsAdminCreatedTrue()
    {
        Mock<IRepository<ResourceLink>> repo = RepoThatSaves();
        CreateResourceLinkDto dto = new("PR Monitoring", "https://example.com", "Supply", 1, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsAdminCreated);
    }

    [Fact]
    public async Task CreateAsync_StaffWithPermission_ReturnsOk_WithIsAdminCreatedFalse()
    {
        Mock<IRepository<ResourceLink>> repo = RepoThatSaves();
        CreateResourceLinkDto dto = new("My Link", "https://example.com", "General", 5, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeStaff(canManageLinks: true), dto);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsAdminCreated);
    }

    [Fact]
    public async Task CreateAsync_StaffWithoutPermission_ReturnsForbidden()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        CreateResourceLinkDto dto = new("Link", "https://example.com", "General", 5, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeStaff(canManageLinks: false), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_Observer_ReturnsForbidden()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        CreateResourceLinkDto dto = new("Link", "https://example.com", "General", 5, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeObserver(), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_EmptyTitle_ReturnsBadRequest()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        CreateResourceLinkDto dto = new("", "https://example.com", "General", 5, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_EmptyUrl_ReturnsBadRequest()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        CreateResourceLinkDto dto = new("Title", "", "General", 5, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Admin_ReturnsOk()
    {
        ResourceLink link = MakeLink();
        Mock<IRepository<ResourceLink>> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(link.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        UpdateResourceLinkDto dto = new("Updated Title", "https://new.com", "Records", 2, 2);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), link.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Title", link.Title);
        Assert.Equal("https://new.com", link.Url);
    }

    [Fact]
    public async Task UpdateAsync_Staff_ReturnsForbidden_EvenWithManagePermission()
    {
        ResourceLink link = MakeLink();
        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetByIdAsync(link.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        UpdateResourceLinkDto dto = new("Title", "https://example.com", "Supply", 1, 1);

        // Staff can only ADD — edit always requires Admin/SuperAdmin
        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).UpdateAsync(MakeStaff(canManageLinks: true), link.Id, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceLink?)null);

        UpdateResourceLinkDto dto = new("Title", "https://example.com", "Supply", 1, 1);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Admin_SetsIsActiveFalse()
    {
        ResourceLink link = MakeLink(); link.IsActive = true;
        Mock<IRepository<ResourceLink>> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(link.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).DeleteAsync(MakeAdmin(), link.Id);

        Assert.True(result.IsSuccess);
        Assert.False(link.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_Staff_ReturnsForbidden()
    {
        ResourceLink link = MakeLink();
        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetByIdAsync(link.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).DeleteAsync(MakeStaff(canManageLinks: true), link.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFound()
    {
        Mock<IRepository<ResourceLink>> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceLink?)null);

        ServiceResult<ResourceLinkDto> result =
            await BuildSut(repo).DeleteAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }
}
