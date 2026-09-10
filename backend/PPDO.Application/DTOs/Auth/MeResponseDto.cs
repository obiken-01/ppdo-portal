namespace PPDO.Application.DTOs.Auth;

/// <summary>Response body for <c>GET /api/auth/me</c>.</summary>
public sealed class MeResponseDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }

    /// <summary>Role name string, e.g. "SuperAdmin", "Admin", "Staff", "Observer".</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Division id (divisions.id). Null for SuperAdmin/Admin.</summary>
    public int? DivisionId { get; init; }

    /// <summary>Division name. Null for SuperAdmin/Admin.</summary>
    public string? Division { get; init; }

    /// <summary>Provincial office id, or null for PPDO-internal users. New in v1.1.</summary>
    public int? OfficeId { get; init; }

    /// <summary>Short office code, e.g. "PEO". Null for PPDO-internal users.</summary>
    public string? OfficeCode { get; init; }

    /// <summary>Full office name. Null for PPDO-internal users.</summary>
    public string? OfficeName { get; init; }

    /// <summary>
    /// Whether this user belongs to the host office and so holds cross-office authority
    /// (DECISION F, RAL-258). The client's single source for that question — it replaces
    /// <c>officeId == null</c>, which used to mean the same thing by proxy and no longer does
    /// now that every user has an office.
    /// </summary>
    public bool IsHostOffice { get; init; }

    public string? Position { get; init; }

    // -- Effective permission flags (resolved via PermissionService) ----------
    /// <summary>Resolved landing route, e.g. "/dashboard". Always reachable for this user.</summary>
    public string LandingPath { get; init; } = "/account";

    /// <summary>Stored preference as an enum name, or null when unset.</summary>
    public string? LandingPage { get; init; }

    public bool CanAccessInventory { get; init; }
    public bool CanAccessReports { get; init; }
    public bool CanManageUsers { get; init; }
    public bool CanAccessProfile { get; init; }
    public bool CanManageResourceLinks { get; init; }
    public bool CanAccessBudgetPlanning { get; init; }
    public bool CanUploadAip { get; init; }
    public bool CanManageConfig { get; init; }
    public bool CanManagePpdoAllocation { get; init; }
    public bool CanManagePboCeiling { get; init; }
    public bool CanReviewBudgetPlanning { get; init; }
    public bool CanReviewAllOffices { get; init; }

    // -- Password / recovery gates (RAL-266/RAL-267) ---------------------------

    /// <summary>
    /// True after an admin or self-service reset — the portal must block everything except
    /// changing the password until this clears.
    /// </summary>
    public bool MustChangePassword { get; init; }

    /// <summary>
    /// True when this account has no recovery question set yet — the portal must block
    /// everything except the one-time setup screen until this clears (RAL-266).
    /// </summary>
    public bool NeedsRecoverySetup { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent reset, only when it hasn't been acknowledged yet.
    /// Null means there is nothing to show — either no reset has happened, or the user
    /// already dismissed the notice for it. Non-blocking: shown as a dismissible banner,
    /// not a gate (RAL-267).
    /// </summary>
    public DateTime? UnacknowledgedPasswordResetAt { get; init; }
}
