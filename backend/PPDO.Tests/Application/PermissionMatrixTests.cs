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
        ["CanManagePboCeiling"]     = (s, u) => s.CanManagePboCeilingAsync(u),
        ["CanReviewBudgetPlanning"] = (s, u) => s.CanReviewBudgetPlanningAsync(u),
        ["CanReviewAllOffices"]     = (s, u) => s.CanReviewAllOfficesAsync(u),
        ["CanViewAuditLog"]         = (s, u) => s.CanViewAuditLogAsync(u),
    };

    /// <summary>The five flags that follow the plain role-bypass / override / division chain.</summary>
    private static readonly string[] StandardFlags =
    [
        "CanAccessInventory", "CanAccessReports", "CanManageUsers",
        "CanManageResourceLinks", "CanManageConfig",
    ];

    /// <summary>The four per-user grants: SuperAdmin only, Admin NOT auto-granted.</summary>
    private static readonly string[] PerUserGrants =
    [
        "CanManagePpdoAllocation", "CanManagePboCeiling",
        "CanReviewBudgetPlanning", "CanReviewAllOffices",
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
            case "CanManagePboCeiling":
                user.OverrideCanManagePboCeiling = overrideValue; break;
            case "CanReviewBudgetPlanning":
                user.OverrideCanReviewBudgetPlanning = overrideValue; break;
            case "CanReviewAllOffices":
                user.OverrideCanReviewAllOffices = overrideValue; break;
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
