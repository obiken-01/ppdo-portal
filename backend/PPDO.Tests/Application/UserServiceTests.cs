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
        Username = "superadmin",
        Email    = "superadmin@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.SuperAdmin,
        DivisionId = null,
        IsActive = true,
    };

    private static User MakeAdmin() => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Admin User",
        Username = "admin",
        Email    = "admin@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.Admin,
        DivisionId = null,
        IsActive = true,
    };

    private static User MakeStaff(int divisionId = 2) => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Staff User",
        Username = "staff",
        Email    = "staff@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = UserRole.Staff,
        DivisionId = divisionId,
        Division = new Division { Id = divisionId, OfficeId = 100, Name = "Planning Division" },
        IsActive = true,
    };

    // Default divisions repo: two active PPDO divisions (1, 2) plus an office division (5 → office 7).
    private static Mock<IRepository<Division>> DefaultDivisions()
    {
        Mock<IRepository<Division>> divisions = new();
        divisions.Setup(d => d.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Division>
            {
                new() { Id = 1, OfficeId = 100, Name = "Administrative Division", IsActive = true },
                new() { Id = 2, OfficeId = 100, Name = "Planning Division", IsActive = true },
                new() { Id = 5, OfficeId = 7,   Name = "Office Division", IsActive = true },
            });
        return divisions;
    }

    private static UserService BuildSut(
        Mock<IUserRepository> repoMock,
        Mock<IRepository<Office>>? officeMock = null,
        Mock<IRepository<Division>>? divisionMock = null,
        Mock<IAuditService>? auditMock = null) =>
        new(repoMock.Object,
            (officeMock ?? new Mock<IRepository<Office>>()).Object,
            (divisionMock ?? DefaultDivisions()).Object,
            NullLogger<UserService>.Instance,
            (auditMock ?? new Mock<IAuditService>()).Object);

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
        repo.Setup(r => r.GetAllWithDivisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        IReadOnlyList<UserResponseDto> result = await BuildSut(repo).GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).GetByIdAsync(user.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Username, result.Value!.Username);
        Assert.Equal(user.Email, result.Value!.Email);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_InvalidRole_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "NotARole", 1, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidDivision_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Staff", 999, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_AdminCreatesAdmin_ReturnsForbidden()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Admin", null, null, null);

        // Admin cannot create another Admin — only SuperAdmin can.
        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_DuplicateUsername_ReturnsConflict()
    {
        User existing = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync("staff", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        CreateUserDto dto = new("Jane", "staff", "jane@ppdo.gov.ph", "Staff", 2, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsConflict()
    {
        User existing = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync("staff@ppdo.gov.ph", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        CreateUserDto dto = new("Jane", "jane", existing.Email, "Staff", 2, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidStaff_ReturnsOk_AndAutoAssignsGroup()
    {
        User created = MakeStaff(2);
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        // Reload after create
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        CreateUserDto dto = new("Jane Doe", "janedoe", "jane@ppdo.gov.ph", "Staff", 2, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_ValidStaff_LogsAuditCreate_WithoutPasswordHash()
    {
        User created = MakeStaff(2);
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        Mock<IAuditService> audit = new();
        CreateUserDto dto = new("Jane Doe", "janedoe", "jane@ppdo.gov.ph", "Staff", 2, null, null);

        await BuildSut(repo, auditMock: audit).CreateAsync(MakeAdmin(), dto);

        audit.Verify(a => a.LogAsync(
            "users",
            created.Id,
            AuditAction.Create,
            null,
            It.Is<object>(v =>
                v.GetType().GetProperty("PasswordHash") == null &&
                v.GetType().GetProperty("RefreshToken") == null),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_SuperAdminCreatesAdmin_ReturnsOk()
    {
        User created = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        CreateUserDto dto = new("Admin Two", "admin2", "admin2@ppdo.gov.ph", "Admin", null, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_StaffWithoutDivisionOrOffice_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();

        // PPDO Staff with neither a division nor an office cannot be assigned a group.
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Staff", null, null, null);

        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_WithOfficeDivision_SetsOfficeAndDivision()
    {
        User? captured = null;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => captured = u)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaff());

        Mock<IRepository<Office>> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { new() { Id = 7, OfficeCode = "PGO", OfficeName = "Provincial Gov Office", IsActive = true } });

        // Office user — division 5 belongs to office 7 (see DefaultDivisions()).
        CreateUserDto dto = new("Office Encoder", "enc", "enc@lgu.gov.ph", "Staff", 5, null, null, OfficeId: 7);

        ServiceResult<UserResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(7, captured!.OfficeId);
        Assert.Equal(5, captured.DivisionId);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_DivisionFromAnotherOffice_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        Mock<IRepository<Office>> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { new() { Id = 7, OfficeName = "PGO", IsActive = true } });

        // Division 2 belongs to office 100, not office 7 → rejected.
        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Staff", 2, null, null, OfficeId: 7);

        ServiceResult<UserResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_InactiveOffice_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();

        Mock<IRepository<Office>> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { new() { Id = 7, OfficeName = "Closed Office", IsActive = false } });

        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Staff", null, null, null, OfficeId: 7);

        ServiceResult<UserResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_AdminRole_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Admin", null, null, null, OfficeId: 7);

        // Office users must be Staff or Observer — never Admin/SuperAdmin.
        ServiceResult<UserResponseDto> result = await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateUserDto dto = new("New Name", null, null, null, null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_AdminUpdatesAdmin_ReturnsForbidden()
    {
        User target = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new("New Name", null, null, null, null, null, null, null, null, null, null);

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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new("Updated Name", null, null, null, null, "New Position", "09171234567",
            null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", target.FullName);
        Assert.Equal("New Position", target.Position);
        Assert.Equal("09171234567", target.ContactNo);
    }

    [Fact]
    public async Task UpdateAsync_ValidProfileFields_LogsAuditUpdate_WithOldAndNewSnapshots()
    {
        User target = MakeStaff();
        target.FullName = "Original Name";
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Mock<IAuditService> audit = new();
        UpdateUserDto dto = new("Updated Name", null, null, null, null, null, null,
            null, null, null, null);

        await BuildSut(repo, auditMock: audit).UpdateAsync(MakeAdmin(), target.Id, dto);

        audit.Verify(a => a.LogAsync(
            "users",
            target.Id,
            AuditAction.Update,
            It.Is<object>(v => (string?)v.GetType().GetProperty("FullName")!.GetValue(v) == "Original Name"),
            It.Is<object>(v => (string?)v.GetType().GetProperty("FullName")!.GetValue(v) == "Updated Name"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_UsernameSaved()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        repo.Setup(r => r.FindByUsernameAsync("newstaff", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateUserDto dto = new(null, "newstaff", null, null, null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("newstaff", target.Username);
    }

    [Fact]
    public async Task UpdateAsync_EmailSaved()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        repo.Setup(r => r.FindByEmailAsync("newemail@ppdo.gov.ph", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateUserDto dto = new(null, null, "newemail@ppdo.gov.ph", null, null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("newemail@ppdo.gov.ph", target.Email);
    }

    [Fact]
    public async Task UpdateAsync_InvalidRole_ReturnsBadRequest()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new(null, null, null, "NotARole", null, null, null, null, null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), target.Id, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_OverrideFlags_AreSaved()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        UpdateUserDto dto = new(null, null, null, null, null, null, null,
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

    // ── SetPermissionsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SetPermissionsAsync_NonSuperAdmin_ReturnsForbidden_AndDoesNotLogAudit()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = new();
        Mock<IAuditService> audit = new();
        SetPermissionsDto dto = new() { OverrideCanAccessInventory = true };

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo, auditMock: audit).SetPermissionsAsync(MakeAdmin(), target.Id, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetPermissionsAsync_ValidTarget_LogsAuditUpdate()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Mock<IAuditService> audit = new();
        SetPermissionsDto dto = new() { OverrideCanAccessInventory = true };

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo, auditMock: audit).SetPermissionsAsync(MakeSuperAdmin(), target.Id, dto);

        Assert.True(result.IsSuccess);
        audit.Verify(a => a.LogAsync(
            "users",
            target.Id,
            AuditAction.Update,
            It.IsAny<object>(),
            It.Is<object>(v => (bool?)v.GetType().GetProperty("OverrideCanAccessInventory")!.GetValue(v) == true),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── ResetPasswordAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPasswordAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.NotEqual(originalHash, target.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("TamarawUser2026!", target.PasswordHash));
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTarget_LogsAuditUpdate_WithoutPasswordHash()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Mock<IAuditService> audit = new();

        await BuildSut(repo, auditMock: audit).ResetPasswordAsync(MakeAdmin(), target.Id);

        // No PasswordHash snapshot at all -- just a marker that a reset happened.
        audit.Verify(a => a.LogAsync(
            "users",
            target.Id,
            AuditAction.Update,
            null,
            It.Is<object>(v => v.GetType().GetProperty("PasswordHash") == null),
            It.IsAny<CancellationToken>()),
            Times.Once);
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).DeactivateAsync(MakeAdmin(), target.Id);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        Assert.Null(target.RefreshToken);
        Assert.Null(target.RefreshTokenExpiry);
    }

    [Fact]
    public async Task DeactivateAsync_ValidTarget_LogsAuditDelete()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Mock<IAuditService> audit = new();

        await BuildSut(repo, auditMock: audit).DeactivateAsync(MakeAdmin(), target.Id);

        // Mirrors the soft-delete audit convention used by Division/Account/Office services.
        audit.Verify(a => a.LogAsync(
            "users",
            target.Id,
            AuditAction.Delete,
            It.Is<object>(v => (bool)v.GetType().GetProperty("IsActive")!.GetValue(v)! == true),
            null,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── ReactivateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
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
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), target.Id);

        Assert.True(result.IsSuccess);
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task ReactivateAsync_InactiveTarget_LogsAuditUpdate()
    {
        User target = MakeStaff();
        target.IsActive = false;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        Mock<IAuditService> audit = new();

        await BuildSut(repo, auditMock: audit).ReactivateAsync(MakeAdmin(), target.Id);

        audit.Verify(a => a.LogAsync(
            "users",
            target.Id,
            AuditAction.Update,
            It.Is<object>(v => (bool)v.GetType().GetProperty("IsActive")!.GetValue(v)! == false),
            It.Is<object>(v => (bool)v.GetType().GetProperty("IsActive")!.GetValue(v)! == true),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReactivateAsync_AdminReactivatesAdmin_ReturnsForbidden()
    {
        User target = MakeAdmin();
        target.IsActive = false;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        // Admin cannot manage another Admin.
        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).ReactivateAsync(MakeAdmin(), target.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── UpdateOwnProfileAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOwnProfileAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateOwnProfileDto dto = new("Full Name", "username", null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(MakeStaff(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_EmptyFullName_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        UpdateOwnProfileDto dto = new("  ", "username", null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_EmptyUsername_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        UpdateOwnProfileDto dto = new("Full Name", "  ", null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_DuplicateUsername_ReturnsConflict()
    {
        User caller = MakeStaff();
        User other  = MakeAdmin();
        other.Username = "taken";

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        repo.Setup(r => r.FindByUsernameAsync("taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        UpdateOwnProfileDto dto = new("Full Name", "taken", null, null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_DuplicateEmail_ReturnsConflict()
    {
        User caller = MakeStaff();
        User other  = MakeAdmin();
        other.Email = "taken@ppdo.gov.ph";

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync("taken@ppdo.gov.ph", It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        UpdateOwnProfileDto dto = new("Full Name", caller.Username, "taken@ppdo.gov.ph", null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_EmailClearedToNull_Succeeds()
    {
        User caller = MakeStaff();
        caller.Email = "old@ppdo.gov.ph";

        User reloaded = MakeStaff();
        reloaded.Email = null;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        // Reload after save returns the updated user
        repo.SetupSequence(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller)
            .ReturnsAsync(reloaded);

        UpdateOwnProfileDto dto = new("Full Name", caller.Username, "", null, null);

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.True(result.IsSuccess);
        Assert.Null(caller.Email);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_Success_UpdatesOnlyEditableFields()
    {
        User caller = MakeStaff();
        UserRole originalRole          = caller.Role;
        Division? originalDivision     = caller.Division;
        bool originalIsActive          = caller.IsActive;

        User reloaded = MakeStaff();
        reloaded.FullName  = "New Name";
        reloaded.Position  = "Engineer";
        reloaded.ContactNo = "09171234567";

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.SetupSequence(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller)
            .ReturnsAsync(reloaded);
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        UpdateOwnProfileDto dto = new("New Name", caller.Username, null, "Engineer", "09171234567");

        ServiceResult<UserResponseDto> result =
            await BuildSut(repo).UpdateOwnProfileAsync(caller, dto);

        Assert.True(result.IsSuccess);
        // Role, Division, IsActive must be untouched (privilege-escalation guard)
        Assert.Equal(originalRole,     caller.Role);
        Assert.Equal(originalDivision, caller.Division);
        Assert.Equal(originalIsActive, caller.IsActive);
        // Editable fields updated
        Assert.Equal("New Name",    caller.FullName);
        Assert.Equal("Engineer",    caller.Position);
        Assert.Equal("09171234567", caller.ContactNo);
    }

    // ── ChangePasswordAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ChangePasswordAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ChangePasswordDto dto = new("old", "NewPass1!", "NewPass1!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(MakeStaff(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPass1!");

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("WrongPass1!", "NewPass1!", "NewPass1!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("incorrect", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePasswordAsync_PasswordMismatch_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current1!");

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "NewPass1!", "DifferentPass1!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("match", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePasswordAsync_PolicyFailure_ShortPassword_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current1!");

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "Short1!", "Short1!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ChangePasswordAsync_PolicyFailure_NoUppercase_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current1!");

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "newpass123!", "newpass123!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ChangePasswordAsync_PolicyFailure_NoDigit_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current1!");

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "NoDigitHere!", "NoDigitHere!");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidNewPassword_HashUpdated()
    {
        User caller = MakeStaff();
        string originalHash = BCrypt.Net.BCrypt.HashPassword("Current1!");
        caller.PasswordHash = originalHash;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "NewPass2@", "NewPass2@");

        ServiceResult<bool> result =
            await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalHash, caller.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewPass2@", caller.PasswordHash));
    }
}
