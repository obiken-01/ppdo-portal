using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The executable half of <c>docs/v1.8/Permission_Matrix.md</c> (v1.8.0 — RAL-245).
///
/// Every row of the matrix document is a row in <see cref="Rows"/>, so the doc cannot silently go
/// stale: change a resolution rule and the corresponding row fails until both are updated.
/// <see cref="PermissionServiceTests"/> stays as-is — it explains individual rules and the
/// reasoning behind the awkward ones; this file is the exhaustive grid.
///
/// Read the document for the prose. The short version:
///   standard flags     role bypass (SuperAdmin/Admin) -> Override ?? Division ?? false
///   per-user grants    SuperAdmin only -> Override ?? false   (Admin NOT auto-granted)
///   two special cases  CanAccessBudgetPlanning defaults ON for guest offices;
///                      CanUploadAip is host-office-only and can never be granted to a guest
/// </summary>
public sealed class PermissionMatrixTests
{
    private readonly PermissionService _sut = new();

    private const int HostOfficeId  = 1;
    private const int GuestOfficeId = 7;

    /// <summary>
    /// Every flag on <see cref="IPermissionService"/>, keyed by the name used in the matrix doc.
    /// A flag added to the interface without a row here is caught by
    /// <see cref="Matrix_CoversEveryFlagOnThePermissionService"/>.
    /// </summary>
    private static readonly Dictionary<string, Func<PermissionService, User, Task<bool>>> Resolvers = new()
    {
        ["CanAccessInventory"]      = (s, u) => s.CanAccessInventoryAsync(u),
        ["CanAccessReports"]        = (s, u) => s.CanAccessReportsAsync(u),
        ["CanManageUsers"]          = (s, u) => s.CanManageUsersAsync(u),
        ["CanManageResourceLinks"]  = (s, u) => s.CanManageResourceLinksAsync(u),
        ["CanManageConfig"]         = (s, u) => s.CanManageConfigAsync(u),
        ["CanAccessBudgetPlanning"] = (s, u) => s.CanAccessBudgetPlanningAsync(u),
        ["CanUploadAip"]            = (s, u) => s.CanUploadAipAsync(u),
        ["CanAccessProfile"]        = (s, u) => s.CanAccessProfileAsync(u),
        ["CanManagePpdoAllocation"] = (s, u) => s.CanManagePpdoAllocationAsync(u),
        ["CanManageOfficeCeilings"]     = (s, u) => s.CanManageOfficeCeilingsAsync(u),
        ["CanReviewBudgetPlanning"] = (s, u) => s.CanReviewBudgetPlanningAsync(u),
        ["CanReviewAllOffices"]     = (s, u) => s.CanReviewAllOfficesAsync(u),
        ["CanViewAuditLog"]         = (s, u) => s.CanViewAuditLogAsync(u),
        ["CanManageApiKeys"]        = (s, u) => s.CanManageApiKeysAsync(u),
        ["CanManageOfficeSetup"]    = (s, u) => s.CanManageOfficeSetupAsync(u),
        ["CanManageInvestmentPlanningSettings"] = (s, u) => s.CanManageInvestmentPlanningSettingsAsync(u),
        // Per office: the grid asks about the caller's OWN office. The other-office rows, which are
        // the point of this flag, are CanReopenInvestmentProposal_AnotherOffice below.
        ["CanReopenInvestmentProposal"] = (s, u) => s.CanReopenInvestmentProposalAsync(u, u.OfficeId ?? 0),
    };

    /// <summary>The five flags that follow the plain role-bypass / override / division chain.</summary>
    private static readonly string[] StandardFlags =
    [
        "CanAccessInventory", "CanAccessReports", "CanManageUsers",
        "CanManageResourceLinks", "CanManageConfig",
    ];

    /// <summary>The six per-user grants: SuperAdmin only, Admin NOT auto-granted.</summary>
    private static readonly string[] PerUserGrants =
    [
        "CanManagePpdoAllocation", "CanManageOfficeCeilings",
        "CanReviewBudgetPlanning", "CanReviewAllOffices",
        "CanManageApiKeys", "CanManageOfficeSetup",
    ];

    public static TheoryData<string, UserRole, bool?, bool, bool, bool> Rows()
    {
        // flag, role, override, divisionFlag, isHostOffice, expected
        TheoryData<string, UserRole, bool?, bool, bool, bool> rows = new();

        // ── Standard flags: role bypass -> Override ?? Division ?? false ───────
        foreach (string flag in StandardFlags)
        {
            rows.Add(flag, UserRole.SuperAdmin, null,  false, true,  true);
            rows.Add(flag, UserRole.Admin,      null,  false, true,  true);
            rows.Add(flag, UserRole.Staff,      null,  false, true,  false);
            rows.Add(flag, UserRole.Staff,      null,  true,  true,  true);
            rows.Add(flag, UserRole.Staff,      true,  false, true,  true);
            rows.Add(flag, UserRole.Staff,      false, true,  true,  false);
        }

        // ── Per-user grants: SuperAdmin -> true, else Override ?? false ────────
        // Admin is deliberately NOT auto-granted. The office is irrelevant to all four.
        foreach (string flag in PerUserGrants)
        {
            rows.Add(flag, UserRole.SuperAdmin, null,  false, true,  true);
            rows.Add(flag, UserRole.Admin,      null,  false, true,  false);
            rows.Add(flag, UserRole.Admin,      true,  false, true,  true);
            rows.Add(flag, UserRole.Staff,      null,  false, true,  false);
            rows.Add(flag, UserRole.Staff,      true,  false, true,  true);
            rows.Add(flag, UserRole.Staff,      false, false, true,  false);
            rows.Add(flag, UserRole.Staff,      true,  false, false, true);   // guest office: no effect
        }

        // ── CanAccessBudgetPlanning: defaults ON for a guest office ───────────
        // A guest-office user has no division to inherit from and Budget Planning is their only
        // feature, so a blank override means granted — the one flag whose default flips by office.
        rows.Add("CanAccessBudgetPlanning", UserRole.SuperAdmin, null,  false, true,  true);
        rows.Add("CanAccessBudgetPlanning", UserRole.Admin,      null,  false, true,  true);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      null,  false, true,  false);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      null,  true,  true,  true);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      true,  false, true,  true);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      false, true,  true,  false);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      null,  false, false, true);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      false, false, false, false);
        rows.Add("CanAccessBudgetPlanning", UserRole.Staff,      true,  false, false, true);

        // ── CanUploadAip: host-office only, never grantable to a guest ────────
        // The uploaded file contains every office's records, so a guest office can never hold it
        // however the flags are set.
        rows.Add("CanUploadAip", UserRole.SuperAdmin, null,  false, true,  true);
        rows.Add("CanUploadAip", UserRole.Admin,      null,  false, true,  true);
        rows.Add("CanUploadAip", UserRole.Staff,      null,  false, true,  false);
        rows.Add("CanUploadAip", UserRole.Staff,      null,  true,  true,  true);
        rows.Add("CanUploadAip", UserRole.Staff,      true,  false, true,  true);
        rows.Add("CanUploadAip", UserRole.Staff,      false, true,  true,  false);
        rows.Add("CanUploadAip", UserRole.Staff,      true,  true,  false, false);  // guest: never

        // ── CanAccessProfile: always true, every role ─────────────────────────
        rows.Add("CanAccessProfile", UserRole.SuperAdmin, null, false, true,  true);
        rows.Add("CanAccessProfile", UserRole.Admin,      null, false, true,  true);
        rows.Add("CanAccessProfile", UserRole.Staff,      null, false, true,  true);
        rows.Add("CanAccessProfile", UserRole.Staff,      null, false, false, true);

        // ── CanViewAuditLog: feature-flag gated, SuperAdmin-only while on ─────
        // No override or division input at all — the only flag with neither.
        rows.Add("CanViewAuditLog", UserRole.SuperAdmin, null, false, true, FeatureFlags.AuditLogPageEnabled);
        rows.Add("CanViewAuditLog", UserRole.Admin,      null, true,  true, false);
        rows.Add("CanViewAuditLog", UserRole.Staff,      true,  true, true, false);

        // ── CanManageInvestmentPlanningSettings: CanManageConfig AND host office (PPDO-136) ──
        // A province-wide value, so PPDO's to set. SuperAdmin keeps it anywhere (support access);
        // ⚠️ Admin does NOT bypass the office check — unlike CanUploadAip, whose Admin row above
        // passes before the office is read. The override/division inputs are CanManageConfig's.
        const string ips = "CanManageInvestmentPlanningSettings";
        rows.Add(ips, UserRole.SuperAdmin, null,  false, true,  true);
        rows.Add(ips, UserRole.SuperAdmin, null,  false, false, true);   // guest office: support exemption
        rows.Add(ips, UserRole.Admin,      null,  false, true,  true);
        rows.Add(ips, UserRole.Admin,      null,  false, false, false);  // guest-office Admin: never
        rows.Add(ips, UserRole.Staff,      null,  false, true,  false);
        rows.Add(ips, UserRole.Staff,      null,  true,  true,  true);
        rows.Add(ips, UserRole.Staff,      true,  false, true,  true);
        rows.Add(ips, UserRole.Staff,      false, true,  true,  false);
        rows.Add(ips, UserRole.Staff,      true,  true,  false, false);  // guest office: never, however set

        // ── CanReopenInvestmentProposal, own office (PPDO-155) ────────────────
        // SuperAdmin; host-office Admin; otherwise the office's own department head. The override
        // input is CanReviewBudgetPlanning's. The division flag plays no part.
        const string reopen = "CanReopenInvestmentProposal";
        rows.Add(reopen, UserRole.SuperAdmin, null,  false, true,  true);
        rows.Add(reopen, UserRole.SuperAdmin, null,  false, false, true);
        rows.Add(reopen, UserRole.Admin,      null,  false, true,  true);
        rows.Add(reopen, UserRole.Admin,      null,  false, false, false);  // guest-office Admin: not by role
        rows.Add(reopen, UserRole.Admin,      true,  false, false, true);   // ...but as their office's dept head
        rows.Add(reopen, UserRole.Staff,      null,  true,  true,  false);  // encoders finalize, never reopen
        rows.Add(reopen, UserRole.Staff,      true,  false, true,  true);
        rows.Add(reopen, UserRole.Staff,      true,  false, false, true);
        rows.Add(reopen, UserRole.Staff,      false, false, false, false);

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Matrix(
        string flag, UserRole role, bool? overrideValue, bool divisionFlag, bool isHostOffice, bool expected)
    {
        User user = MakeUser(flag, role, overrideValue, divisionFlag, isHostOffice);

        bool actual = await Resolvers[flag](_sut, user);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A flag added to <see cref="IPermissionService"/> without a matrix row would otherwise be
    /// silently untested and — worse — silently missing from the document. This fails the build
    /// until both are updated.
    /// </summary>
    [Fact]
    public void Matrix_CoversEveryFlagOnThePermissionService()
    {
        IEnumerable<string> onInterface = typeof(IPermissionService)
            .GetMethods()
            .Where(m => m.Name.StartsWith("Can") && m.Name.EndsWith("Async"))
            .Select(m => m.Name[..^"Async".Length]);

        string[] missing = onInterface.Except(Resolvers.Keys).ToArray();

        Assert.True(missing.Length == 0,
            $"IPermissionService flags with no row in the permission matrix: {string.Join(", ", missing)}. " +
            "Add them here AND to docs/v1.8/Permission_Matrix.md — the doc and this grid are a pair.");
    }

    /// <summary>Every flag named in the grid must actually be exercised by a row.</summary>
    [Fact]
    public void Matrix_ExercisesEveryFlagItKnowsAbout()
    {
        HashSet<string> exercised = Rows().Select(r => (string)r[0]!).ToHashSet();

        string[] unexercised = Resolvers.Keys.Except(exercised).ToArray();

        Assert.True(unexercised.Length == 0,
            $"Flags with a resolver but no matrix row: {string.Join(", ", unexercised)}.");
    }

    /// <summary>
    /// An unassigned user (null <c>office_id</c>) is not the host office — DECISION F (RAL-258).
    /// Only SuperAdmin clears the office check without one. The grid cannot express this row:
    /// its fixture always assigns an office.
    /// </summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin, true)]
    [InlineData(UserRole.Admin,      false)]
    [InlineData(UserRole.Staff,      false)]
    public async Task CanManageInvestmentPlanningSettings_NoOffice_OnlySuperAdmin(UserRole role, bool expected)
    {
        User user = MakeUser("CanManageInvestmentPlanningSettings", role, true, true, isHostOffice: true);
        user.OfficeId = null;
        user.Office   = null;

        Assert.Equal(expected, await _sut.CanManageInvestmentPlanningSettingsAsync(user));
    }

    /// <summary>
    /// Reopen is per office, and the office is the caller's own, compared directly — never
    /// <c>OfficeScope.Resolve</c>, which answers SeeAll for a host-office user (Permission_Matrix.md
    /// §4a). Without that, a PPDO department head would reopen every office's proposals.
    /// </summary>
    [Fact]
    public async Task HostOfficeDeptHead_CannotReopenAnotherOffice()
    {
        User head = MakeUser("CanReopenInvestmentProposal", UserRole.Staff, true, false, isHostOffice: true);

        Assert.True(await _sut.CanReopenInvestmentProposalAsync(head, HostOfficeId));
        Assert.False(await _sut.CanReopenInvestmentProposalAsync(head, GuestOfficeId));
    }

    [Theory]
    [InlineData(UserRole.SuperAdmin, true,  true)]    // support access, any office
    [InlineData(UserRole.Admin,      true,  true)]    // host-office Admin: any office
    [InlineData(UserRole.Admin,      false, false)]   // guest-office Admin: not another office
    [InlineData(UserRole.Staff,      false, false)]   // guest dept head: own office only
    public async Task CanReopenInvestmentProposal_AnotherOffice(UserRole role, bool isHostOffice, bool expected)
    {
        User user = MakeUser("CanReopenInvestmentProposal", role, true, false, isHostOffice);
        int otherOffice = isHostOffice ? GuestOfficeId : HostOfficeId;

        Assert.Equal(expected, await _sut.CanReopenInvestmentProposalAsync(user, otherOffice));
    }

    // ── §3.2 The AIP division lock (PPDO-148) ─────────────────────────────────
    //
    // Not a flag — a rule over (who the caller is in this office) × (whose activity) × (has that
    // division submitted). Each row below is a row of Permission_Matrix.md §3.2; change one and
    // change the other. Everything here assumes the office uses the division flow (FY2028+, at
    // least one active division) and that the office-state guard has already passed.

    public static TheoryData<string, string, bool, bool> DivisionLockRows() => new()
    {
        // caller,        activity,   its division submitted, may edit
        { "encoder-A",    "A",        false, true  },
        { "encoder-A",    "A",        true,  false },
        { "encoder-A",    "B",        false, false },
        { "encoder-A",    "B",        true,  false },
        { "encoder-A",    "untagged", false, false },
        { "no-division",  "A",        false, false },
        { "no-division",  "untagged", false, false },
        { "dept-head",    "A",        false, true  },
        { "dept-head",    "A",        true,  true  },
        { "dept-head",    "untagged", false, true  },
        { "admin",        "B",        true,  true  },
    };

    [Theory]
    [MemberData(nameof(DivisionLockRows))]
    public void DivisionLock_EditActivity(string caller, string activity, bool submitted, bool expected)
    {
        const int a = 1, b = 2;
        int? activityDivision = activity switch { "A" => a, "B" => b, _ => null };
        bool head = caller is "dept-head" or "admin";
        int? callerDivision = caller == "encoder-A" ? a : null;
        HashSet<int> submittedIds = submitted && activityDivision is int d ? [d] : [];

        AipDivisionContext ctx = new(
            hasDivisions: true, isDepartmentHead: head, callerDivisionId: callerDivision,
            divisions: new Dictionary<int, Division>
            {
                [a] = new() { Id = a, Name = "A", IsActive = true },
                [b] = new() { Id = b, Name = "B", IsActive = true },
            },
            submittedDivisionIds: submittedIds);

        Assert.Equal(expected, ctx.CanWriteActivity(activityDivision));
        // Re-tag is the department head's alone, in any state (spec §3.1).
        Assert.Equal(head, ctx.IsDepartmentHead);
    }

    /// <summary>§3.2's last row: an office outside the division flow keeps today's rules for everyone.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void DivisionLock_OfficeWithoutDivisions_AllowsEveryone(int? activityDivision)
        => Assert.True(AipDivisionContext.None.CanWriteActivity(activityDivision));

    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a user for one matrix row. The division flag is applied only to the flag under
    /// test — every other division flag stays false, so a row can never pass because some
    /// unrelated flag happened to be set.
    /// </summary>
    private static User MakeUser(
        string flag, UserRole role, bool? overrideValue, bool divisionFlag, bool isHostOffice)
    {
        int officeId = isHostOffice ? HostOfficeId : GuestOfficeId;

        User user = new()
        {
            Id         = Guid.NewGuid(),
            Role       = role,
            DivisionId = 3,
            Division   = new Division { Id = 3, Name = "Test Division" },
            OfficeId   = officeId,
            Office     = new Office
            {
                Id           = officeId,
                OfficeCode   = isHostOffice ? "PPDO" : "GSO",
                IsHostOffice = isHostOffice,
            },
        };

        switch (flag)
        {
            case "CanAccessInventory":
                user.Division!.CanAccessInventory = divisionFlag;
                user.OverrideCanAccessInventory = overrideValue; break;
            case "CanAccessReports":
                user.Division!.CanAccessReports = divisionFlag;
                user.OverrideCanAccessReports = overrideValue; break;
            case "CanManageUsers":
                user.Division!.CanManageUsers = divisionFlag;
                user.OverrideCanManageUsers = overrideValue; break;
            case "CanManageResourceLinks":
                user.Division!.CanManageResourceLinks = divisionFlag;
                user.OverrideCanManageResourceLinks = overrideValue; break;
            case "CanManageConfig":
            case "CanManageInvestmentPlanningSettings":   // reads CanManageConfig's inputs
                user.Division!.CanManageConfig = divisionFlag;
                user.OverrideCanManageConfig = overrideValue; break;
            case "CanAccessBudgetPlanning":
                user.Division!.CanAccessBudgetPlanning = divisionFlag;
                user.OverrideCanAccessBudgetPlanning = overrideValue; break;
            case "CanUploadAip":
                user.Division!.CanUploadAip = divisionFlag;
                user.OverrideCanUploadAip = overrideValue; break;
            case "CanManagePpdoAllocation":
                user.OverrideCanManagePpdoAllocation = overrideValue; break;
            case "CanManageOfficeCeilings":
                user.OverrideCanManageOfficeCeilings = overrideValue; break;
            case "CanReviewBudgetPlanning":
            case "CanReopenInvestmentProposal":   // reads the department-head grant
                user.OverrideCanReviewBudgetPlanning = overrideValue; break;
            case "CanReviewAllOffices":
                user.OverrideCanReviewAllOffices = overrideValue; break;
            case "CanManageApiKeys":
                user.OverrideCanManageApiKeys = overrideValue; break;
            case "CanManageOfficeSetup":
                user.OverrideCanManageOfficeSetup = overrideValue; break;
            case "CanAccessProfile":
            case "CanViewAuditLog":
                break;  // neither reads an override or a division flag
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(flag), flag, "No fixture wiring for this flag — add it alongside its matrix row.");
        }

        return user;
    }
}
