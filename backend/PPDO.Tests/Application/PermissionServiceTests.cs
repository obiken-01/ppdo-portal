using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PermissionService"/> (v1.2 — RAL-97 model).
///
///   SuperAdmin → true for everything (incl. allocation).
///   Admin      → true for every flag EXCEPT CanManagePpdoAllocation and CanManagePboCeiling.
///   Staff      → Override ?? user.Division.&lt;flag&gt; ?? false.
///   CanUploadAip is host-office-only (guest offices never).
///   CanManagePpdoAllocation is a per-user grant (SuperAdmin bypass; Admin not auto).
///   CanManagePboCeiling is the same shape but a SEPARATE authority (RAL-243) --
///   holding one must never resolve the other true.
///   CanReviewBudgetPlanning is the same shape again (RAL-244) and is purely ADDITIVE --
///   the reviewer write-denial belongs to RAL-256's guard, not to this service.
///   CanReviewAllOffices (RAL-257) is the one flag that WIDENS scope past the caller's own
///   office. Resolved separately from CanReviewBudgetPlanning even when both are held.
///
/// No mocks needed — PermissionService is pure logic. The "division" entity carries the flags.
/// </summary>
public sealed class PermissionServiceTests
{
    private readonly PermissionService _sut = new();

    /// <summary>Id of the office flagged <c>IsHostOffice</c> in these fixtures.</summary>
    private const int HostOfficeId = 1;

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
        bool? overridePboCeiling       = null,
        bool? overrideReviewer         = null,
        bool? overrideAllOffices       = null,
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
            // DECISION F (RAL-258): everyone has an office, and the host-office flag — not a null
            // office id — is what grants cross-office authority. `officeId: null` in these tests
            // therefore means "in the host office", which is what it used to mean by proxy.
            OfficeId                      = officeId ?? HostOfficeId,
            Office                        = new Office
            {
                Id           = officeId ?? HostOfficeId,
                OfficeCode   = officeId is null ? "PPDO" : $"OFF{officeId}",
                IsHostOffice = officeId is null,
            },
            OverrideCanAccessInventory    = overrideInventory,
            OverrideCanAccessReports      = overrideReports,
            OverrideCanManageUsers        = overrideManageUsers,
            OverrideCanManageResourceLinks= overrideManageLinks,
            OverrideCanAccessBudgetPlanning = overrideBudgetPlanning,
            OverrideCanUploadAip            = overrideUploadAip,
            OverrideCanManageConfig         = overrideManageConfig,
            OverrideCanManagePpdoAllocation     = overrideAllocation,
            OverrideCanManagePboCeiling         = overridePboCeiling,
            OverrideCanReviewBudgetPlanning     = overrideReviewer,
            OverrideCanReviewAllOffices         = overrideAllOffices,
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

    // ── CanManagePpdoAllocation — per-user grant ──────────────────────────────────

    [Fact]
    public async Task CanManagePpdoAllocation_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanManagePpdoAllocationAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanManagePpdoAllocation_Admin_NotAutoGranted()
        => Assert.False(await _sut.CanManagePpdoAllocationAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanManagePpdoAllocation_Admin_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManagePpdoAllocationAsync(MakeUser(UserRole.Admin, overrideAllocation: true)));

    [Fact]
    public async Task CanManagePpdoAllocation_Staff_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManagePpdoAllocationAsync(MakeUser(UserRole.Staff, overrideAllocation: true)));

    [Fact]
    public async Task CanManagePpdoAllocation_Staff_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanManagePpdoAllocationAsync(MakeUser(UserRole.Staff)));

    // -- CanManagePboCeiling -- per-user grant (RAL-243) ---------------------------

    [Fact]
    public async Task CanManagePboCeiling_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanManagePboCeiling_Admin_NotAutoGranted()
        => Assert.False(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanManagePboCeiling_Admin_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.Admin, overridePboCeiling: true)));

    [Fact]
    public async Task CanManagePboCeiling_Staff_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.Staff, overridePboCeiling: true)));

    [Fact]
    public async Task CanManagePboCeiling_Staff_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.Staff)));

    [Fact]
    public async Task CanManagePboCeiling_Staff_OverrideFalse_ReturnsFalse()
        => Assert.False(await _sut.CanManagePboCeilingAsync(MakeUser(UserRole.Staff, overridePboCeiling: false)));

    [Fact]
    public async Task CanManagePboCeiling_OfficeUser_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanManagePboCeilingAsync(
            MakeUser(UserRole.Staff, overridePboCeiling: true, officeId: 7)));

    // The two allocation grants are separate authorities: neither implies the other.
    // If either of these fails, someone has OR-ed them together in PermissionService.

    [Fact]
    public async Task CanManagePboCeiling_PpdoAllocationHolder_DoesNotImplyCeiling()
        => Assert.False(await _sut.CanManagePboCeilingAsync(
            MakeUser(UserRole.Staff, overrideAllocation: true)));

    [Fact]
    public async Task CanManagePpdoAllocation_PboCeilingHolder_DoesNotImplyAllocation()
        => Assert.False(await _sut.CanManagePpdoAllocationAsync(
            MakeUser(UserRole.Staff, overridePboCeiling: true)));

    // -- CanReviewBudgetPlanning -- per-user grant (RAL-244) -----------------------

    [Fact]
    public async Task CanReviewBudgetPlanning_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanReviewBudgetPlanning_Admin_NotAutoGranted()
        => Assert.False(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanReviewBudgetPlanning_Admin_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.Admin, overrideReviewer: true)));

    [Fact]
    public async Task CanReviewBudgetPlanning_Staff_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.Staff, overrideReviewer: true)));

    [Fact]
    public async Task CanReviewBudgetPlanning_Staff_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.Staff)));

    [Fact]
    public async Task CanReviewBudgetPlanning_Staff_OverrideFalse_ReturnsFalse()
        => Assert.False(await _sut.CanReviewBudgetPlanningAsync(MakeUser(UserRole.Staff, overrideReviewer: false)));

    // The reviewer is an office user in every real deployment -- this is the case that matters.
    [Fact]
    public async Task CanReviewBudgetPlanning_OfficeUser_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanReviewBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overrideReviewer: true, officeId: 7)));

    [Fact]
    public async Task CanReviewBudgetPlanning_OfficeUser_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanReviewBudgetPlanningAsync(
            MakeUser(UserRole.Staff, officeId: 7)));

    // The reviewer grant is ADDITIVE (RAL-244): it must not remove a write the user already
    // had. The write-denial that distinguishes the two reviewer kinds is RAL-256's own guard.
    // If this fails, a denial has leaked into PermissionService, which is the wrong layer.
    [Fact]
    public async Task CanReviewBudgetPlanning_DoesNotRevokeExistingGrants()
    {
        User reviewer = MakeUser(
            UserRole.Staff, overrideReviewer: true, overrideBudgetPlanning: true, divBudgetPlanning: true);
        Assert.True(await _sut.CanReviewBudgetPlanningAsync(reviewer));
        Assert.True(await _sut.CanAccessBudgetPlanningAsync(reviewer));
    }

    // Reviewer and the two allocation grants are independent authorities.
    [Fact]
    public async Task CanReviewBudgetPlanning_IsIndependentOfAllocationGrants()
    {
        Assert.False(await _sut.CanReviewBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overrideAllocation: true)));
        Assert.False(await _sut.CanReviewBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overridePboCeiling: true)));
        Assert.False(await _sut.CanManagePpdoAllocationAsync(
            MakeUser(UserRole.Staff, overrideReviewer: true)));
        Assert.False(await _sut.CanManagePboCeilingAsync(
            MakeUser(UserRole.Staff, overrideReviewer: true)));
    }

    // -- CanReviewAllOffices -- per-user cross-office grant (RAL-257) ---------------

    [Fact]
    public async Task CanReviewAllOffices_SuperAdmin_ReturnsTrue()
        => Assert.True(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.SuperAdmin)));

    [Fact]
    public async Task CanReviewAllOffices_Admin_NotAutoGranted()
        => Assert.False(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.Admin)));

    [Fact]
    public async Task CanReviewAllOffices_Admin_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.Admin, overrideAllOffices: true)));

    [Fact]
    public async Task CanReviewAllOffices_Staff_WithOverride_ReturnsTrue()
        => Assert.True(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.Staff, overrideAllOffices: true)));

    [Fact]
    public async Task CanReviewAllOffices_Staff_NoOverride_ReturnsFalse()
        => Assert.False(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.Staff)));

    [Fact]
    public async Task CanReviewAllOffices_Staff_OverrideFalse_ReturnsFalse()
        => Assert.False(await _sut.CanReviewAllOfficesAsync(MakeUser(UserRole.Staff, overrideAllOffices: false)));

    /// <summary>
    /// The case the ticket calls out as most likely to be got wrong: the holder sits in a real
    /// office. Their own office must not narrow the grant.
    /// </summary>
    [Fact]
    public async Task CanReviewAllOffices_HolderInAGuestOffice_StillResolvesTrue()
        => Assert.True(await _sut.CanReviewAllOfficesAsync(
            MakeUser(UserRole.Staff, overrideAllOffices: true, officeId: 7)));

    // The two reviewer flags are resolved SEPARATELY (RAL-257). "Reviewer + all offices" would
    // make the cross-office holder inherit the department-head reviewer's write rule, which is a
    // different rule with a different intent -- see RAL-256.

    [Fact]
    public async Task CanReviewAllOffices_ReviewerFlagAlone_DoesNotGrantCrossOffice()
        => Assert.False(await _sut.CanReviewAllOfficesAsync(
            MakeUser(UserRole.Staff, overrideReviewer: true)));

    [Fact]
    public async Task CanReviewBudgetPlanning_CrossOfficeFlagAlone_DoesNotGrantReviewer()
        => Assert.False(await _sut.CanReviewBudgetPlanningAsync(
            MakeUser(UserRole.Staff, overrideAllOffices: true)));

    [Fact]
    public async Task ReviewFlags_BothHeld_BothResolveTrueIndependently()
    {
        User both = MakeUser(UserRole.Staff, overrideReviewer: true, overrideAllOffices: true, officeId: 7);
        Assert.True(await _sut.CanReviewBudgetPlanningAsync(both));
        Assert.True(await _sut.CanReviewAllOfficesAsync(both));
    }

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
