using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PermissionService"/> (v1.2 — RAL-97 model).
///
///   SuperAdmin → true for everything (incl. allocation).
///   Admin      → true for every flag EXCEPT CanManageAllocation.
///   Staff      → Override ?? user.Division.&lt;flag&gt; ?? false.
///   CanUploadAip is PPDO-only (office users never).
///   CanManageAllocation is a per-user grant (SuperAdmin bypass; Admin not auto).
///
/// No mocks needed — PermissionService is pure logic. The "division" entity carries the flags.
/// </summary>
public sealed class PermissionServiceTests
{
    private readonly PermissionService _sut = new();

    private static User MakeUser(
        UserRole role,
        bool? overrideInventory        = null,
        bool? overrideReports          = null,
        bool? overrideManageUsers      = null,
        bool? overrideManageLinks      = null,
        bool  divInventory             = false,
        bool  divReports               = false,
        bool  divManageUsers           = false,
        bool  divManageLinks           = false,
        bool? overrideBudgetPlanning   = null,
        bool? overrideUploadAip        = null,
        bool? overrideManageConfig     = null,
        bool? overrideAllocation       = null,
        bool  divBudgetPlanning        = false,
        bool  divUploadAip             = false,
        bool  divManageConfig          = false,
        int?  officeId                 = null,
        bool  hasDivision              = true)
    {
        Division? division = hasDivision ? new Division
        {
            Id                       = 1,
            OfficeId                 = officeId ?? 100,
            Name                     = "Test Division",
            CanAccessInventory       = divInventory,
            CanAccessReports         = divReports,
            CanManageUsers           = divManageUsers,
            CanManageResourceLinks   = divManageLinks,
            CanAccessBudgetPlanning  = divBudgetPlanning,
            CanUploadAip             = divUploadAip,
            CanManageConfig          = divManageConfig,
        } : null;

        return new User
        {
            Id                            = Guid.NewGuid(),
            FullName                      = "Test User",
            Email                         = "test@ppdo.gov.ph",
            PasswordHash                  = "hash",
            Role                          = role,
            DivisionId                    = division?.Id,
            Division                      = division,
            OfficeId                      = officeId,
            OverrideCanAccessInventory    = overrideInventory,
            OverrideCanAccessReports      = overrideReports,
            OverrideCanManageUsers        = overrideManageUsers,
            OverrideCanManageResourceLinks= overrideManageLinks,
            OverrideCanAccessBudgetPlanning = overrideBudgetPlanning,
            OverrideCanUploadAip            = overrideUploadAip,
            OverrideCanManageConfig         = overrideManageConfig,
            OverrideCanManageAllocation     = overrideAllocation,
        };
    }

    // ── Admin/SuperAdmin bypass standard flags ────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public async Task StandardFlags_AdminOrAbove_AllTrue(UserRole role)
    {
        User user = MakeUser(role); // all division flags false
        Assert.True(await _sut.CanAccessInventoryAsync(user));
        Assert.True(await _sut.CanAccessReportsAsync(user));
        Assert.True(await _sut.CanManageUsersAsync(user));
        Assert.True(await _sut.CanManageResourceLinksAsync(user));
        Assert.True(await _sut.CanAccessBudgetPlanningAsync(user));
        Assert.True(await _sut.CanUploadAipAsync(user));
        Assert.True(await _sut.CanManageConfigAsync(user));
    }

    // ── Staff resolution: Override ?? Division flag ?? false ───────────────────

    [Fact]
    public async Task CanAccessInventory_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanAccessInventoryAsync(MakeUser(UserRole.Staff, divInventory: true)));
        Assert.False(await _sut.CanAccessInventoryAsync(MakeUser(UserRole.Staff, divInventory: false)));
    }

    [Fact]
    public async Task CanAccessInventory_Staff_OverrideWins()
    {
        Assert.True(await  _sut.CanAccessInventoryAsync(MakeUser(UserRole.Staff, overrideInventory: true,  divInventory: false)));
        Assert.False(await _sut.CanAccessInventoryAsync(MakeUser(UserRole.Staff, overrideInventory: false, divInventory: true)));
    }

    [Fact]
    public async Task CanAccessInventory_Staff_NullDivision_ReturnsFalse()
        => Assert.False(await _sut.CanAccessInventoryAsync(MakeUser(UserRole.Staff, hasDivision: false)));

    [Fact]
    public async Task CanManageUsers_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanManageUsersAsync(MakeUser(UserRole.Staff, divManageUsers: true)));
        Assert.False(await _sut.CanManageUsersAsync(MakeUser(UserRole.Staff, divManageUsers: false)));
    }

    [Fact]
    public async Task CanManageResourceLinks_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanManageResourceLinksAsync(MakeUser(UserRole.Staff, divManageLinks: true)));
        Assert.False(await _sut.CanManageResourceLinksAsync(MakeUser(UserRole.Staff, divManageLinks: false)));
    }

    [Fact]
    public async Task CanManageConfig_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanManageConfigAsync(MakeUser(UserRole.Staff, divManageConfig: true)));
        Assert.False(await _sut.CanManageConfigAsync(MakeUser(UserRole.Staff, divManageConfig: false)));
    }

    // ── CanAccessProfile — always true ────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task CanAccessProfile_AllRoles_True(UserRole role)
        => Assert.True(await _sut.CanAccessProfileAsync(MakeUser(role)));

    // ── Budget planning ───────────────────────────────────────────────────────

    [Fact]
    public async Task CanAccessBudgetPlanning_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Staff, divBudgetPlanning: true)));
        Assert.False(await _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Staff, divBudgetPlanning: false)));
    }

    // Office users' only feature is Budget Planning, and they can't be assigned a division
    // in the UI (scoped by office_id instead). So their access defaults ON — otherwise a
    // division-less office user resolves to false and gets locked out of their only feature.
    [Fact]
    public async Task CanAccessBudgetPlanning_OfficeUser_DefaultsTrue_WithoutDivision()
        => Assert.True(await _sut.CanAccessBudgetPlanningAsync(
            MakeUser(UserRole.Staff, officeId: 7, hasDivision: false)));

    // An explicit override can still turn it off for a specific office user.
    [Fact]
    public async Task CanAccessBudgetPlanning_OfficeUser_OverrideFalse_ReturnsFalse()
        => Assert.False(await _sut.CanAccessBudgetPlanningAsync(
            MakeUser(UserRole.Staff, officeId: 7, overrideBudgetPlanning: false, hasDivision: false)));

    // ── CanUploadAip — PPDO-only ──────────────────────────────────────────────

    [Fact]
    public async Task CanUploadAip_Staff_InheritsDivisionFlag()
    {
        Assert.True(await  _sut.CanUploadAipAsync(MakeUser(UserRole.Staff, divUploadAip: true)));
        Assert.False(await _sut.CanUploadAipAsync(MakeUser(UserRole.Staff, divUploadAip: false)));
    }

    [Fact]
    public async Task CanUploadAip_OfficeUser_AlwaysFalse_EvenWithOverride()
        => Assert.False(await _sut.CanUploadAipAsync(
            MakeUser(UserRole.Staff, overrideUploadAip: true, divUploadAip: true, officeId: 7)));

    // ── CanManageAllocation — per-user grant ──────────────────────────────────

    [Fact]
    public async Task CanManageAllocation_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanManageAllocationAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanManageAllocation_Admin_NotAutoGranted()
        => Assert.False(await _sut.CanManageAllocationAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanManageAllocation_Admin_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManageAllocationAsync(MakeUser(UserRole.Admin, overrideAllocation: true)));

    [Fact]
    public async Task CanManageAllocation_Staff_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManageAllocationAsync(MakeUser(UserRole.Staff, overrideAllocation: true)));

    [Fact]
    public async Task CanManageAllocation_Staff_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanManageAllocationAsync(MakeUser(UserRole.Staff)));

    // ── CanViewAuditLog — SuperAdmin-only, feature-flag gated ─────────────────

    [Fact]
    public async Task CanViewAuditLog_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanViewAuditLogAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanViewAuditLog_Admin_ReturnsFalse()
        => Assert.False(await _sut.CanViewAuditLogAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanViewAuditLog_Staff_ReturnsFalse()
        => Assert.False(await _sut.CanViewAuditLogAsync(MakeUser(UserRole.Staff)));
}
