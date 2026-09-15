namespace PPDO.Domain.Entities;

/// <summary>
/// One office a scoped <see cref="PartnerApiKey"/> may read (v1.8.0 — PPDO-15). Composite key
/// (<see cref="KeyId"/>, <see cref="OfficeId"/>) — a plain join row, no columns of its own.
/// Empty for an <see cref="PartnerApiKey.AllOffices"/> key. Cascade-deletes with its key.
/// </summary>
public sealed class PartnerApiKeyOffice
{
    /// <summary>FK to <see cref="PartnerApiKey.Id"/>. Half of the composite PK.</summary>
    public int KeyId { get; set; }

    /// <summary>FK to <see cref="Office.Id"/>. Half of the composite PK.</summary>
    public int OfficeId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public PartnerApiKey? Key { get; set; }

    public Office? Office { get; set; }
}
