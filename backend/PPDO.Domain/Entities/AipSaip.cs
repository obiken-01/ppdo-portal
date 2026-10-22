using PPDO.Domain.Common;

namespace PPDO.Domain.Entities;

/// <summary>
/// A Supplemental AIP (v1.10.0 — PPDO-246, <c>docs/v1.10/SAIP_Spec.md</c> §5): one office's request
/// to change its Consolidated AIP for one fiscal year.
///
/// <para>
/// <b>The changes live in a draft copy, not here.</b> Starting an SAIP copies the office's approved
/// tree into new <see cref="AipOffice"/> rows (one per sector group) whose
/// <see cref="AipOffice.SaipId"/> points at this row. Every copied row carries a <c>SourceId</c> back
/// to the approved row it came from. The approved AIP is untouched until approval (decision 4).
/// </para>
///
/// <para>
/// ⚠️ <b>Owned by (record, config office), not by one <c>aip_offices</c> row.</b> The spec sketched an
/// <c>aip_office_id</c>, but an office's AIP is several group rows (one per sector and sub-office —
/// PGO has eight). An SAIP covers all of them, so it keys on the same pair every office-level AIP
/// action keys on: <see cref="AipRecordId"/> and <see cref="OfficeId"/>. The copy's groups are found
/// by <see cref="AipOffice.SaipId"/>, which is why there is no <c>draft_aip_office_id</c> either.
/// </para>
/// </summary>
public sealed class AipSaip : IRowVersioned
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the AIP record (the fiscal year).</summary>
    public int AipRecordId { get; set; }

    /// <summary>FK to the config <c>offices</c> row whose AIP this SAIP changes.</summary>
    public int OfficeId { get; set; }

    /// <summary>SAIP 1, 2, … per office and fiscal year. Cancelled SAIPs keep their number.</summary>
    public int Number { get; set; }

    /// <summary>
    /// <c>Draft</c>, <c>DepartmentReview</c>, <c>SubmittedToPpdo</c>, <c>ReturnedByPpdo</c>,
    /// <c>Approved</c> or <c>Cancelled</c> (<c>SaipStatus</c>). The last two close it.
    /// </summary>
    public string Status { get; set; } = "Draft";

    // ── The signed request letter (decision 7; filled by T2) ──────────────────

    /// <summary>Storage key of the letter in <c>IFileStore</c>. Null until one is attached.</summary>
    public string? LetterFileKey { get; set; }

    /// <summary>The letter's original file name. Max 255 characters.</summary>
    public string? LetterFileName { get; set; }

    /// <summary>The letter's MIME type. Max 100 characters.</summary>
    public string? LetterContentType { get; set; }

    /// <summary>The letter's size in bytes.</summary>
    public long? LetterSizeBytes { get; set; }

    // ── Who and when ──────────────────────────────────────────────────────────

    /// <summary>The department head who started it.</summary>
    public Guid StartedById { get; set; }

    /// <summary>UTC time it was started.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Who sent it to PPDO last. Null until sent.</summary>
    public Guid? SubmittedById { get; set; }

    /// <summary>UTC time it was sent to PPDO last.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>The PPDO reviewer who approved or last returned it.</summary>
    public Guid? DecidedById { get; set; }

    /// <summary>UTC time of the last approve or return.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Who cancelled it. Null unless Cancelled.</summary>
    public Guid? CancelledById { get; set; }

    /// <summary>UTC time it was cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>Optimistic-concurrency token. SQL Server maintains it; never assign to it.</summary>
    public byte[] RowVersion { get; set; } = [];

    // ── Navigation ────────────────────────────────────────────────────────────

    public AipRecord? AipRecord { get; set; }
    public Office? Office { get; set; }
}
