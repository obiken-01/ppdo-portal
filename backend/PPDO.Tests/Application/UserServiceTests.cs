using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="UserService"/>.
/// IUserRepository is mocked; no database access occurs.
/// Coverage target: 80% (Application/Service layer).
/// </summary>
public sealed class UserServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeSuperAdmin() => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Super Admin",
        Email    = "superadmin@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.SuperAdmin,
        Division = Division.Admin,
        IsActive = true,
    };

    private static User MakeAdmin() => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Admin User",
        Email    = "admin@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.Admin,
        Division = Division.Admin,
        IsActive = true,
    };

    private static User MakeStaff(Division division = Division.Planning) => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Staff User",
        Email    = "staff@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.Staff,
        Division = division,
        IsActive = true,
        Group    = new PermissionGroup { Id = Guid.NewGuid(), Name = "Planning Staff" },
    };

    private static UserService BuildSut(Mock<IUserRepository> repoMock) =>
        new(repoMock.Object, NullLogger<UserService>.Instance);

    private static Mock<IUserRepository> RepoThatSaves()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllUsersMappedToDtos()
    {
        List<User> users = [MakeAdmin(), MakeStaff()];
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetAllWithGroupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        IReadOnlyList<UserResponseDto> result = await BuildSut(repo).GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).GetByIdAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetByIdAsync_UserFound_ReturnsOk()
    {
        User user = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).GetByIdAsync(user.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Email, result.Value!.Email);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_InvalidRole_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane@ppdo.gov.ph", "NotARole", "Admin", null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidDivision_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane@ppdo.gov.ph", "Staff", "NotADivision", null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_AdminCreatesAdmin_ReturnsForbidden()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane@ppdo.gov.ph", "Admin", "Admin", null, null);

        // Admin cannot create another Admin — only SuperAdmin can.
        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsConflict()
    {
        User existing = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByEmailAsync(existing.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        CreateUserDto dto = new("Jane", existing.Email, "Staff", "Planning", null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidStaff_ReturnsOk_AndAutoAssignsGroup()
    {
        User created = MakeStaff(Division.Planning);
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        // Reload after create
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        CreateUserDto dto = new("Jane Doe", "jane@ppdo.gov.ph", "Staff", "Planning", null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_SuperAdminCreatesAdmin_ReturnsOk()
    {
        User created = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        CreateUserDto dto = new("Admin Two", "admin2@ppdo.gov.ph", "Admin", "Admin", null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.True(result.IsSuccess);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateUserDto dto = new("New Name", null, null, null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_AdminUpdatesAdmin_ReturnsForbidden()
    {
        User target = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new("New Name", null, null, null, null, null, null, null, null, null);

        // Admin cannot update another Admin.
        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_ValidProfileFields_SavesChanges()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new("Updated Name", null, null, null, "New Position", "09171234567",
            null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", target.FullName);
        Assert.Equal("New Position", target.Position);
        Assert.Equal("09171234567", target.ContactNo);
    }

    [Fact]
    public async Task UpdateAsync_InvalidRole_ReturnsBadRequest()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new(null, "NotARole", null, null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_OverrideFlags_AreSaved()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new(null, null, null, null, null, null,
            OverrideCanAccessInventory:     true,
            OverrideCanAccessReports:       false,
            OverrideCanManageUsers:         null,
            OverrideCanManageResourceLinks: true);

        await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.True(target.OverrideCanAccessInventory);
        Assert.False(target.OverrideCanAccessReports);
        Assert.Null(target.OverrideCanManageUsers);
        Assert.True(target.OverrideCanManageResourceLinks);
    }

    // ── ResetPasswordAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPasswordAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTarget_ClearsRefreshToken()
    {
        User target = MakeStaff();
        target.RefreshToken = "active-session-token";
        target.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.Null(target.RefreshToken);
        Assert.Null(target.RefreshTokenExpiry);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTarget_ChangesPasswordHash()
    {
        User target = MakeStaff();
        string originalHash = target.PasswordHash;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.NotEqual(originalHash, target.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("PPDOUser2026!", target.PasswordHash));
    }

    // ── DeactivateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_SelfDeactivate_ReturnsBadRequest()
    {
        User admin = MakeAdmin();
        Mock<IUserRepository> repo = new();

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).DeactivateAsync(admin, admin.Id);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task DeactivateAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).DeactivateAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeactivateAsync_ValidTarget_SetsIsActiveFalse_AndClearsSession()
    {
        User target = MakeStaff();
        target.RefreshToken = "session";
        target.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).DeactivateAsync(MakeAdmin(), target.Id);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        Assert.Null(target.RefreshToken);
        Assert.Null(target.RefreshTokenExpiry);
    }

    // ── ReactivateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ReactivateAsync_AlreadyActive_ReturnsBadRequest()
    {
        User target = MakeStaff();
        target.IsActive = true;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), target.Id);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ReactivateAsync_InactiveTarget_SetsIsActiveTrue()
    {
        User target = MakeStaff();
        target.IsActive = false;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), target.Id);

        Assert.True(result.IsSuccess);
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task ReactivateAsync_AdminReactivatesAdmin_ReturnsForbidden()
    {
        User target = MakeAdmin();
        target.IsActive = false;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithGroupAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        // Admin cannot manage another Admin.
        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), target.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }
}
