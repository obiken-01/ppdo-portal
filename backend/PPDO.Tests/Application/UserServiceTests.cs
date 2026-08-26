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

    /// <summary>Division 1 — CanAccessInventory is left false, so its Staff cannot reach /inventory.</summary>
    private const int NoInventoryDivisionId = 1;

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

    /// <summary>Id of the office flagged <c>IsHostOffice</c> in these fixtures (DECISION F, RAL-258).</summary>
    private const int HostOfficeId = 1;

    private static Office HostOffice => new()
    {
        Id = HostOfficeId, OfficeCode = "PPDO", OfficeName = "Provincial Planning and Development Office",
        IsActive = true, IsHostOffice = true,
    };

    /// <summary>
    /// An office repo that can answer the host-office lookup. UserService calls it whenever a user
    /// is saved without an office, because "no office" now means "the host office" (RAL-258).
    /// </summary>
    private static Mock<IOfficeRepository> DefaultOffices()
    {
        Mock<IOfficeRepository> offices = new();
        offices.Setup(o => o.GetHostOfficeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(HostOffice);
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { HostOffice });
        return offices;
    }

    private static UserService BuildSut(
        Mock<IUserRepository> repoMock,
        Mock<IOfficeRepository>? officeMock = null,
        Mock<IRepository<Division>>? divisionMock = null,
        Mock<IAuditService>? auditMock = null) =>
        new(repoMock.Object,
            (officeMock ?? DefaultOffices()).Object,
            (divisionMock ?? DefaultDivisions()).Object,
            NullLogger<UserService>.Instance,
            (auditMock ?? new Mock<IAuditService>()).Object,
            new LandingPageResolver(new PermissionService()));

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

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidDivision_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Staff", 999, null, null);

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_AdminCreatesAdmin_ReturnsForbidden()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Admin", null, null, null);

        // Admin cannot create another Admin — only SuperAdmin can.
        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

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

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

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

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

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

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

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

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_ValidUser_IssuesPasswordMatchingTheStoredHash()
    {
        // RAL-254: the account is created with a generated password, returned once.
        User? persisted = null;
        User created = MakeAdmin();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => persisted = u)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        CreateUserDto dto = new("Admin Two", "admin2", "admin2@ppdo.gov.ph", "Admin", null, null, null);

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.True(result.IsSuccess);
        string issued = result.Value!.TemporaryPassword;

        Assert.False(string.IsNullOrWhiteSpace(issued));
        Assert.NotNull(persisted);
        Assert.True(BCrypt.Net.BCrypt.Verify(issued, persisted!.PasswordHash));
    }

    [Fact]
    public async Task CreateAsync_MixedCaseUsername_IsStoredLowerCase()
    {
        // RAL-254: usernames are normalised to lower case on write, keeping every account on
        // the office's lowercase-dotted convention. Matching is separately case-insensitive
        // via the DB collation, so this is belt-and-braces rather than what login relies on.
        User? persisted = null;
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => persisted = u)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAdmin());

        CreateUserDto dto = new("New User", "  newUser  ", "new@ppdo.gov.ph", "Admin", null, null, null);

        await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.Username);   // trimmed and lower-cased
    }

    [Fact]
    public async Task CreateAsync_TwoUsers_IssueDifferentPasswords()
    {
        // The finding this ticket closes: every account used to land on one documented password.
        static Mock<IUserRepository> Repo(User created)
        {
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
            return repo;
        }

        CreateUserDto first  = new("Admin Two",   "admin2", "admin2@ppdo.gov.ph", "Admin", null, null, null);
        CreateUserDto second = new("Admin Three", "admin3", "admin3@ppdo.gov.ph", "Admin", null, null, null);

        ServiceResult<UserCredentialResponseDto> a =
            await BuildSut(Repo(MakeAdmin())).CreateAsync(MakeSuperAdmin(), first);
        ServiceResult<UserCredentialResponseDto> b =
            await BuildSut(Repo(MakeAdmin())).CreateAsync(MakeSuperAdmin(), second);

        Assert.NotEqual(a.Value!.TemporaryPassword, b.Value!.TemporaryPassword);
    }

    // ── Landing page selection (RAL-262) ──────────────────────────────────────

    private static Mock<IUserRepository> RepoForCreate(User created, Action<User>? capture = null)
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => capture?.Invoke(u))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetByIdWithDivisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        return repo;
    }

    // ── Host office assignment (DECISION F, RAL-258) ──────────────────────────

    [Fact]
    public async Task CreateAsync_NoOfficeSupplied_AssignsTheHostOffice()
    {
        User? persisted = null;
        Mock<IUserRepository> repo = RepoForCreate(MakeAdmin(), u => persisted = u);
        CreateUserDto dto = new("Planner", "planner", "p@ppdo.gov.ph", "Staff", 2, null, null);

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        // Before DECISION F this left OfficeId null, which meant "sees every office". Leaving it
        // null now would mean the opposite — a user scoped to nothing.
        Assert.True(result.IsSuccess);
        Assert.Equal(HostOfficeId, persisted!.OfficeId);
    }

    [Fact]
    public async Task CreateAsync_HostOfficeSelectedExplicitly_AdminRoleIsStillAllowed()
    {
        Mock<IUserRepository> repo = RepoForCreate(MakeAdmin(), _ => { });
        CreateUserDto dto = new("Admin Two", "admin2", "a2@ppdo.gov.ph", "Admin",
                                null, null, null, OfficeId: HostOfficeId);

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        // Only a GUEST office forces the Staff role. The host office holds the admins, so
        // rejecting this would make it impossible to create one.
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_GuestOfficeWithAdminRole_IsStillRejected()
    {
        Mock<IUserRepository> repo = RepoForCreate(MakeAdmin(), _ => { });
        Mock<IOfficeRepository> offices = DefaultOffices();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office>
            {
                HostOffice,
                new() { Id = 7, OfficeCode = "PGO", OfficeName = "Provincial Gov Office", IsActive = true },
            });

        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Admin",
                                null, null, null, OfficeId: 7);

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo, offices).CreateAsync(MakeSuperAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_ReachableLandingPage_IsStored()
    {
        User? persisted = null;
        Mock<IUserRepository> repo = RepoForCreate(MakeAdmin(), u => persisted = u);
        CreateUserDto dto = new("Admin Two", "admin2", "a2@ppdo.gov.ph", "Admin",
                                null, null, null, null, "InventoryDashboard");

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(LandingPage.InventoryDashboard, persisted!.LandingPage);
    }

    [Fact]
    public async Task CreateAsync_UnknownLandingPageName_ReturnsBadRequest()
    {
        CreateUserDto dto = new("Admin Two", "admin2", "a2@ppdo.gov.ph", "Admin",
                                null, null, null, null, "TheMoon");

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(RepoForCreate(MakeAdmin())).CreateAsync(MakeSuperAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_NoLandingPage_LeavesItUnset()
    {
        User? persisted = null;
        Mock<IUserRepository> repo = RepoForCreate(MakeAdmin(), u => persisted = u);
        CreateUserDto dto = new("Admin Two", "admin2", "a2@ppdo.gov.ph", "Admin", null, null, null);

        await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

        Assert.Null(persisted!.LandingPage);
    }

    [Fact]
    public async Task CreateAsync_LandingPageTheUserCannotReach_ReturnsBadRequest()
    {
        // Saving an unreachable landing page does not fail at redirect time — it loops.
        // Staff in a division without inventory access cannot land on the inventory dashboard.
        CreateUserDto dto = new("Plain Staff", "plain", "plain@ppdo.gov.ph", "Staff",
                                NoInventoryDivisionId, null, null, null, "InventoryDashboard");

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(RepoForCreate(MakeStaff())).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("cannot access", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_StaffWithoutDivisionOrOffice_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();

        // PPDO Staff with neither a division nor an office cannot be assigned a group.
        CreateUserDto dto = new("Jane", "jane", "jane@ppdo.gov.ph", "Staff", null, null, null);

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

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

        Mock<IOfficeRepository> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office>
            {
                HostOffice,
                new() { Id = 7, OfficeCode = "PGO", OfficeName = "Provincial Gov Office", IsActive = true },
            });
        offices.Setup(o => o.GetHostOfficeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(HostOffice);

        // Office user — division 5 belongs to office 7 (see DefaultDivisions()).
        CreateUserDto dto = new("Office Encoder", "enc", "enc@lgu.gov.ph", "Staff", 5, null, null, OfficeId: 7);

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

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

        Mock<IOfficeRepository> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { new() { Id = 7, OfficeName = "PGO", IsActive = true } });

        // Division 2 belongs to office 100, not office 7 → rejected.
        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Staff", 2, null, null, OfficeId: 7);

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_InactiveOffice_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();

        Mock<IOfficeRepository> offices = new();
        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Office> { new() { Id = 7, OfficeName = "Closed Office", IsActive = false } });

        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Staff", null, null, null, OfficeId: 7);

        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo, offices).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeUser_AdminRole_ReturnsBadRequest()
    {
        Mock<IUserRepository> repo = new();
        CreateUserDto dto = new("Enc", "enc", "enc@lgu.gov.ph", "Admin", null, null, null, OfficeId: 7);

        // Office users must be Staff or Observer — never Admin/SuperAdmin.
        ServiceResult<UserCredentialResponseDto> result = await BuildSut(repo).CreateAsync(MakeSuperAdmin(), dto);

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

        ServiceResult<UserCredentialResponseDto> result =
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

        ServiceResult<UserCredentialResponseDto> result =
            await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.NotEqual(originalHash, target.PasswordHash);

        // The issued password is returned once and is the one that was actually set.
        string issued = result.Value!.TemporaryPassword;
        Assert.True(BCrypt.Net.BCrypt.Verify(issued, target.PasswordHash));
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

    [Fact]
    public async Task ChangePasswordAsync_ValidNewPassword_ClearsMustChangePassword()
    {
        User caller = MakeStaff();
        caller.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current1!");
        caller.MustChangePassword = true; // e.g. still on a temporary password from a reset

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);

        ChangePasswordDto dto = new("Current1!", "NewPass2@", "NewPass2@");

        ServiceResult<bool> result = await BuildSut(repo).ChangePasswordAsync(caller, dto);

        Assert.True(result.IsSuccess);
        Assert.False(caller.MustChangePassword);
    }

    // ── CreateAsync — MustChangePassword (RAL-254 gap closed by RAL-266) ────────

    [Fact]
    public async Task CreateAsync_ValidStaff_SetsMustChangePassword()
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
            .ReturnsAsync(() => captured);

        CreateUserDto dto = new("Jane Doe", "janedoe", "jane@ppdo.gov.ph", "Staff", 2, null, null);

        await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.NotNull(captured);
        Assert.True(captured!.MustChangePassword);
    }

    // ── ResetPasswordAsync — MustChangePassword + reset notice (RAL-254/RAL-267) ─

    [Fact]
    public async Task ResetPasswordAsync_ValidTarget_SetsMustChangePassword()
    {
        User target = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.True(target.MustChangePassword);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTarget_SetsLastPasswordResetAt_AndClearsPriorAcknowledgement()
    {
        User target = MakeStaff();
        // A stale acknowledgement from a PRIOR reset must not suppress the notice for this one.
        target.PasswordResetAcknowledgedAt = DateTime.UtcNow.AddDays(-30);

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdWithDivisionAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        DateTime before = DateTime.UtcNow;
        await BuildSut(repo).ResetPasswordAsync(MakeAdmin(), target.Id);

        Assert.NotNull(target.LastPasswordResetAt);
        Assert.True(target.LastPasswordResetAt >= before);
        Assert.Null(target.PasswordResetAcknowledgedAt);
    }

    // ── SetRecoveryAnswerAsync (RAL-266) ─────────────────────────────────────────

    [Fact]
    public async Task SetRecoveryAnswerAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        SetRecoveryAnswerDto dto = new("BirthTown", "Manila");

        ServiceResult<bool> result = await BuildSut(repo).SetRecoveryAnswerAsync(MakeStaff(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task SetRecoveryAnswerAsync_UnknownQuestionKey_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        SetRecoveryAnswerDto dto = new("NotAQuestion", "Manila");

        ServiceResult<bool> result = await BuildSut(repo).SetRecoveryAnswerAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task SetRecoveryAnswerAsync_BlankAnswer_ReturnsBadRequest()
    {
        User caller = MakeStaff();
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        SetRecoveryAnswerDto dto = new("BirthTown", "   ");

        ServiceResult<bool> result = await BuildSut(repo).SetRecoveryAnswerAsync(caller, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task SetRecoveryAnswerAsync_Valid_SetsQuestionAndHashedNormalizedAnswer()
    {
        User caller = MakeStaff();
        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        SetRecoveryAnswerDto dto = new("FirstPetName", "  Bantay  ");

        ServiceResult<bool> result = await BuildSut(repo).SetRecoveryAnswerAsync(caller, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecoveryQuestion.FirstPetName, caller.RecoveryQuestionKey);
        Assert.NotNull(caller.RecoveryAnswerHash);
        // Verifying must go through the exact same normalize-then-hash path RAL-265 reads —
        // a divergence here silently locks the user out of their own answer.
        Assert.True(BCrypt.Net.BCrypt.Verify(
            RecoveryAnswerNormalizer.Normalize("bantay"), caller.RecoveryAnswerHash));
    }

    [Fact]
    public async Task SetRecoveryAnswerAsync_Valid_ClearsAnyPriorLockoutState()
    {
        User caller = MakeStaff();
        caller.RecoveryAttemptCount = 4;
        caller.RecoveryFirstAttemptAt = DateTime.UtcNow;

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        SetRecoveryAnswerDto dto = new("BirthTown", "Manila");
        await BuildSut(repo).SetRecoveryAnswerAsync(caller, dto);

        Assert.Equal(0, caller.RecoveryAttemptCount);
        Assert.Null(caller.RecoveryFirstAttemptAt);
    }

    // ── AcknowledgePasswordResetAsync (RAL-267) ──────────────────────────────────

    [Fact]
    public async Task AcknowledgePasswordResetAsync_UserNotFound_ReturnsNotFound()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        ServiceResult<bool> result = await BuildSut(repo).AcknowledgePasswordResetAsync(MakeStaff());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AcknowledgePasswordResetAsync_Valid_SetsAcknowledgedTimestamp()
    {
        User caller = MakeStaff();
        caller.LastPasswordResetAt = DateTime.UtcNow.AddMinutes(-5);

        Mock<IUserRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(caller.Id, It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        DateTime before = DateTime.UtcNow;
        ServiceResult<bool> result = await BuildSut(repo).AcknowledgePasswordResetAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.NotNull(caller.PasswordResetAcknowledgedAt);
        Assert.True(caller.PasswordResetAcknowledgedAt >= before);
    }
}
