using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PermissionService"/>.
/// No mocks needed — PermissionService is pure logic with no external dependencies.
/// Coverage target: 90% (domain business logic).
/// </summary>
public sealed class PermissionServiceTests
{
    private readonly PermissionService _sut = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static User MakeUser(
        UserRole role,
        bool? overrideInventory        = null,
        bool? overrideReports          = null,
        bool? overrideManageUsers      = null,
        bool? overrideManageLinks      = null,
        bool  groupInventory           = false,
        bool  groupReports             = false,
        bool  groupManageUsers         = false,
        bool  groupManageLinks         = false,
        // v1.1 — budget planning flags
        bool? overrideBudgetPlanning   = null,
        bool? overrideUploadAip        = null,
        bool? overrideManageConfig     = null,
        bool  groupBudgetPlanning      = false,
        bool  groupUploadAip           = false,
        bool  groupManageConfig        = false,
        // v1.1 — office user discriminator (non-null = non-PPDO office user)
        int?  officeId                 = null)
    {
        PermissionGroup group = new()
        {
            Id                       = Guid.NewGuid(),
            Name                     = "Test Group",
            CanAccessInventory       = groupInventory,
            CanAccessReports         = groupReports,
            CanManageUsers           = groupManageUsers,
            CanManageResourceLinks   = groupManageLinks,
            CanAccessBudgetPlanning  = groupBudgetPlanning,
            CanUploadAip             = groupUploadAip,
            CanManageConfig          = groupManageConfig,
        };

        return new User
        {
            Id                            = Guid.NewGuid(),
            FullName                      = "Test User",
            Email                         = "test@ppdo.gov.ph",
            PasswordHash                  = "hash",
            Role                          = role,
            Division                      = officeId is null ? Division.Admin : null,
            OfficeId                      = officeId,
            Group                         = group,
            OverrideCanAccessInventory    = overrideInventory,
            OverrideCanAccessReports      = overrideReports,
            OverrideCanManageUsers        = overrideManageUsers,
            OverrideCanManageResourceLinks= overrideManageLinks,
            OverrideCanAccessBudgetPlanning = overrideBudgetPlanning,
            OverrideCanUploadAip            = overrideUploadAip,
            OverrideCanManageConfig         = overrideManageConfig,
        };
    }

    // ── CanAccessInventoryAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CanAccessInventory_SuperAdmin_ReturnsTrue()
    {
        User user = MakeUser(UserRole.SuperAdmin, groupInventory: false);
        Assert.True(await _sut.CanAccessInventoryAsync(user));
    }

    [Fact]
    public async Task CanAccessInventory_Admin_ReturnsTrue()
    {
        User user = MakeUser(UserRole.Admin, groupInventory: false);
        Assert.True(await _sut.CanAccessInventoryAsync(user));
    }

    [Fact]
    public async Task CanAccessInventory_Staff_InheritsGroupFlag_WhenOverrideIsNull()
    {
        User userWithAccess    = MakeUser(UserRole.Staff, groupInventory: true);
        User userWithoutAccess = MakeUser(UserRole.Staff, groupInventory: false);

        Assert.True(await _sut.CanAccessInventoryAsync(userWithAccess));
        Assert.False(await _sut.CanAccessInventoryAsync(userWithoutAccess));
    }

    [Fact]
    public async Task CanAccessInventory_Staff_OverrideGrant_ReturnsTrueRegardlessOfGroup()
    {
        User user = MakeUser(UserRole.Staff, overrideInventory: true, groupInventory: false);
        Assert.True(await _sut.CanAccessInventoryAsync(user));
    }

    [Fact]
    public async Task CanAccessInventory_Staff_OverrideDeny_ReturnsFalseRegardlessOfGroup()
    {
        User user = MakeUser(UserRole.Staff, overrideInventory: false, groupInventory: true);
        Assert.False(await _sut.CanAccessInventoryAsync(user));
    }

    [Fact]
    public async Task CanAccessInventory_Observer_InheritsGroupFlag_WhenOverrideIsNull()
    {
        User userWithAccess    = MakeUser(UserRole.Observer, groupInventory: true);
        User userWithoutAccess = MakeUser(UserRole.Observer, groupInventory: false);

        Assert.True(await _sut.CanAccessInventoryAsync(userWithAccess));
        Assert.False(await _sut.CanAccessInventoryAsync(userWithoutAccess));
    }

    [Fact]
    public async Task CanAccessInventory_Observer_OverrideDeny_ReturnsFalse()
    {
        User user = MakeUser(UserRole.Observer, overrideInventory: false, groupInventory: true);
        Assert.False(await _sut.CanAccessInventoryAsync(user));
    }

    // ── CanAccessReportsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CanAccessReports_SuperAdmin_ReturnsTrue()
    {
        Assert.True(await _sut.CanAccessReportsAsync(MakeUser(UserRole.SuperAdmin)));
    }

    [Fact]
    public async Task CanAccessReports_Admin_ReturnsTrue()
    {
        Assert.True(await _sut.CanAccessReportsAsync(MakeUser(UserRole.Admin)));
    }

    [Fact]
    public async Task CanAccessReports_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanAccessReportsAsync(MakeUser(UserRole.Staff, groupReports: true)));
        Assert.False(await _sut.CanAccessReportsAsync(MakeUser(UserRole.Staff, groupReports: false)));
    }

    [Fact]
    public async Task CanAccessReports_Staff_OverrideGrant_ReturnsTrueRegardlessOfGroup()
    {
        Assert.True(await _sut.CanAccessReportsAsync(
            MakeUser(UserRole.Staff, overrideReports: true, groupReports: false)));
    }

    [Fact]
    public async Task CanAccessReports_Staff_OverrideDeny_ReturnsFalseRegardlessOfGroup()
    {
        Assert.False(await _sut.CanAccessReportsAsync(
            MakeUser(UserRole.Staff, overrideReports: false, groupReports: true)));
    }

    // ── CanManageUsersAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CanManageUsers_SuperAdmin_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageUsersAsync(MakeUser(UserRole.SuperAdmin)));
    }

    [Fact]
    public async Task CanManageUsers_Admin_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageUsersAsync(MakeUser(UserRole.Admin)));
    }

    [Fact]
    public async Task CanManageUsers_Observer_AlwaysReturnsFalse_EvenWithOverrideGrant()
    {
        User user = MakeUser(UserRole.Observer, overrideManageUsers: true, groupManageUsers: true);
        Assert.False(await _sut.CanManageUsersAsync(user));
    }

    [Fact]
    public async Task CanManageUsers_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanManageUsersAsync(MakeUser(UserRole.Staff, groupManageUsers: true)));
        Assert.False(await _sut.CanManageUsersAsync(MakeUser(UserRole.Staff, groupManageUsers: false)));
    }

    [Fact]
    public async Task CanManageUsers_Staff_OverrideGrant_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageUsersAsync(
            MakeUser(UserRole.Staff, overrideManageUsers: true, groupManageUsers: false)));
    }

    [Fact]
    public async Task CanManageUsers_Staff_OverrideDeny_ReturnsFalse()
    {
        Assert.False(await _sut.CanManageUsersAsync(
            MakeUser(UserRole.Staff, overrideManageUsers: false, groupManageUsers: true)));
    }

    // ── CanManageResourceLinksAsync ───────────────────────────────────────────

    [Fact]
    public async Task CanManageResourceLinks_SuperAdmin_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageResourceLinksAsync(MakeUser(UserRole.SuperAdmin)));
    }

    [Fact]
    public async Task CanManageResourceLinks_Admin_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageResourceLinksAsync(MakeUser(UserRole.Admin)));
    }

    [Fact]
    public async Task CanManageResourceLinks_Observer_AlwaysReturnsFalse_EvenWithOverrideGrant()
    {
        User user = MakeUser(UserRole.Observer, overrideManageLinks: true, groupManageLinks: true);
        Assert.False(await _sut.CanManageResourceLinksAsync(user));
    }

    [Fact]
    public async Task CanManageResourceLinks_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanManageResourceLinksAsync(MakeUser(UserRole.Staff, groupManageLinks: true)));
        Assert.False(await _sut.CanManageResourceLinksAsync(MakeUser(UserRole.Staff, groupManageLinks: false)));
    }

    [Fact]
    public async Task CanManageResourceLinks_Staff_OverrideGrant_ReturnsTrue()
    {
        Assert.True(await _sut.CanManageResourceLinksAsync(
            MakeUser(UserRole.Staff, overrideManageLinks: true, groupManageLinks: false)));
    }

    [Fact]
    public async Task CanManageResourceLinks_Staff_OverrideDeny_ReturnsFalse()
    {
        Assert.False(await _sut.CanManageResourceLinksAsync(
            MakeUser(UserRole.Staff, overrideManageLinks: false, groupManageLinks: true)));
    }

    // ── CanAccessProfileAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Observer)]
    public async Task CanAccessProfile_AllRoles_AlwaysReturnsTrue(UserRole role)
    {
        Assert.True(await _sut.CanAccessProfileAsync(MakeUser(role)));
    }

    // ── CanAccessBudgetPlanningAsync (v1.1) ───────────────────────────────────
    // Resolution: SuperAdmin/Admin → true; Staff/Observer → override ?? group.
    // Observer is NOT hard-blocked — read-only access is allowed by role.

    [Fact]
    public async Task CanAccessBudgetPlanning_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanAccessBudgetPlanning_Admin_ReturnsTrue()
        => Assert.True(await _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanAccessBudgetPlanning_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Staff, groupBudgetPlanning: true)));
        Assert.False(await _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Staff, groupBudgetPlanning: false)));
    }

    [Fact]
    public async Task CanAccessBudgetPlanning_Staff_OverrideGrant_ReturnsTrueRegardlessOfGroup()
        => Assert.True(await _sut.CanAccessBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overrideBudgetPlanning: true, groupBudgetPlanning: false)));

    [Fact]
    public async Task CanAccessBudgetPlanning_Staff_OverrideDeny_ReturnsFalseRegardlessOfGroup()
        => Assert.False(await _sut.CanAccessBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overrideBudgetPlanning: false, groupBudgetPlanning: true)));

    [Fact]
    public async Task CanAccessBudgetPlanning_Observer_InheritsGroupFlag_AllowedReadOnly()
    {
        // Observer is allowed budget-planning access (read-only) — NOT hard-blocked.
        Assert.True(await  _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Observer, groupBudgetPlanning: true)));
        Assert.False(await _sut.CanAccessBudgetPlanningAsync(MakeUser(UserRole.Observer, groupBudgetPlanning: false)));
    }

    [Fact]
    public async Task CanAccessBudgetPlanning_OfficeUserStaff_InheritsGroupFlag()
    {
        // Office encoder (Staff + office_id) with the Office User Default group flag.
        Assert.True(await _sut.CanAccessBudgetPlanningAsync(
            MakeUser(UserRole.Staff, groupBudgetPlanning: true, officeId: 7)));
    }

    // ── CanUploadAipAsync (v1.1) ──────────────────────────────────────────────
    // PPDO users only. Observer never. Office users (office_id set) never.

    [Fact]
    public async Task CanUploadAip_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanUploadAipAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanUploadAip_Admin_ReturnsTrue()
        => Assert.True(await _sut.CanUploadAipAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanUploadAip_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanUploadAipAsync(MakeUser(UserRole.Staff, groupUploadAip: true)));
        Assert.False(await _sut.CanUploadAipAsync(MakeUser(UserRole.Staff, groupUploadAip: false)));
    }

    [Fact]
    public async Task CanUploadAip_Staff_OverrideGrant_ReturnsTrue()
        => Assert.True(await _sut.CanUploadAipAsync(
            MakeUser(UserRole.Staff, overrideUploadAip: true, groupUploadAip: false)));

    [Fact]
    public async Task CanUploadAip_Observer_AlwaysReturnsFalse_EvenWithOverrideGrant()
        => Assert.False(await _sut.CanUploadAipAsync(
            MakeUser(UserRole.Observer, overrideUploadAip: true, groupUploadAip: true)));

    [Fact]
    public async Task CanUploadAip_OfficeUser_AlwaysReturnsFalse_EvenWithOverrideGrant()
    {
        // Non-PPDO office user (office_id set) can never upload AIP — the file
        // contains every office's records, so upload is PPDO-only.
        Assert.False(await _sut.CanUploadAipAsync(
            MakeUser(UserRole.Staff, overrideUploadAip: true, groupUploadAip: true, officeId: 7)));
    }

    // ── CanManageConfigAsync (v1.1) ───────────────────────────────────────────
    // One flag for all config pages. Observer never.

    [Fact]
    public async Task CanManageConfig_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanManageConfigAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanManageConfig_Admin_ReturnsTrue()
        => Assert.True(await _sut.CanManageConfigAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanManageConfig_Staff_InheritsGroupFlag()
    {
        Assert.True(await  _sut.CanManageConfigAsync(MakeUser(UserRole.Staff, groupManageConfig: true)));
        Assert.False(await _sut.CanManageConfigAsync(MakeUser(UserRole.Staff, groupManageConfig: false)));
    }

    [Fact]
    public async Task CanManageConfig_Staff_OverrideGrant_ReturnsTrue()
        => Assert.True(await _sut.CanManageConfigAsync(
            MakeUser(UserRole.Staff, overrideManageConfig: true, groupManageConfig: false)));

    [Fact]
    public async Task CanManageConfig_Observer_AlwaysReturnsFalse_EvenWithOverrideGrant()
        => Assert.False(await _sut.CanManageConfigAsync(
            MakeUser(UserRole.Observer, overrideManageConfig: true, groupManageConfig: true)));
}
