namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 4 (leaf) — an activity (8-segment ref code).
/// RefCode is unique within its parent AIP project.
/// StartDate / EndDate are stored as strings because the source file uses
/// month names ("January", "December"), not proper dates.
/// FundingSourceSnapshot preserves the funding source code at import time so
/// historical records stay accurate if config is edited later.
/// </summary>
public sealed class AipActivity
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP project (level 3).</summary>
    public int ProjectId { get; set; }

    /// <summary>8-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Activity description. Max 1000 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ESRE classification code — "SS", "ES", "ID", or "EN". Max 10 characters.</summary>
    public string? EsreCode { get; set; }

    /// <summary>Implementing office name as written in the AIP. Max 200 characters.</summary>
    public string? ImplementingOffice { get; set; }

    /// <summary>Schedule start — stored as string (e.g. "January"). Max 50 characters.</summary>
    public string? StartDate { get; set; }

    /// <summary>Schedule end — stored as string (e.g. "December"). Max 50 characters.</summary>
    public string? EndDate { get; set; }

    /// <summary>Expected outputs free text.</summary>
    public string? ExpectedOutputs { get; set; }

    /// <summary>FK to the funding source config record. Null when unmatched at import.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>Snapshot of FundingSource.Code at import time. Max 20 characters.</summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Personal Services amount.</summary>
    public decimal? Ps { get; set; }

    /// <summary>Maintenance and Other Operating Expenses amount.</summary>
    public decimal? Mooe { get; set; }

    /// <summary>Capital Outlay amount.</summary>
    public decimal? Co { get; set; }

    /// <summary>
    /// Ps + Mooe + Co, computed at import time (RAL-144) — never read from the source file's own
    /// Total column, since a blank/stale source cell there would desync from the real components
    /// (WfpCeilingService treats this as the activity's whole AIP budget). Null only when
    /// Ps/Mooe/Co are all blank (no budget entered for this activity at all).
    /// </summary>
    public decimal? Total { get; set; }

    /// <summary>Climate change adaptation amount.</summary>
    public decimal? CcAdaptation { get; set; }

    /// <summary>Climate change mitigation amount.</summary>
    public decimal? CcMitigation { get; set; }

    /// <summary>Climate change typology code. Max 50 characters.</summary>
    public string? CcTypologyCode { get; set; }

    /// <summary>
    /// True when this activity's PS expenditures should be treated as "…-CREATION" (v1.4 WFP
    /// Rework open question Q2 — General-Fund, Personal-Services, position-creation activities
    /// only). Default false. Captured during WFP data entry, not AIP import.
    /// </summary>
    public bool IsCreation { get; set; }

    /// <summary>
    /// True when this activity was materialized to hold a line item recorded directly on its
    /// parent program or project row (RAL-108) — it has no corresponding row of its own in the
    /// source file. Financial data must always live on an activity so it flows into WFP,
    /// reports, and the external AIP API the same way every other activity does.
    /// </summary>
    public bool IsSynthetic { get; set; }

    // ── Concurrency + update tracking (V18-71 / PPDO-117) ─────────────────────

    /// <summary>
    /// Optimistic-concurrency token. <b>SQL Server maintains this — never assign to it.</b>
    /// Mapped with <c>.IsRowVersion()</c>, so EF puts it in the <c>WHERE</c> clause of every
    /// UPDATE and raises <c>DbUpdateConcurrencyException</c> when zero rows match, i.e. when
    /// someone else saved this row since it was loaded.
    ///
    /// Chosen over comparing <see cref="UpdatedAt"/> because it <b>cannot be forgotten</b>: the
    /// database bumps it on every update, whereas a timestamp comparison would depend on every
    /// one of the write paths remembering to set it.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// When this activity was last changed. Null for rows untouched since PPDO-117 shipped.
    ///
    /// ⚠️ This is <b>not</b> the concurrency check — <see cref="RowVersion"/> is. This exists so a
    /// rejected save can say <i>when</i> the other edit happened. Stored UTC; render as UTC+8.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who last changed this activity. Null for rows untouched since PPDO-117 shipped. Supplies
    /// the name in the conflict message; like <see cref="UpdatedAt"/> it is for the message, not
    /// the detection.
    /// </summary>
    public Guid? UpdatedById { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP project.</summary>
    public AipProject Project { get; set; } = null!;

    /// <summary>The funding source config record. Null when unmatched.</summary>
    public FundingSource? FundingSource { get; set; }

    /// <summary>The user behind <see cref="UpdatedById"/>. Null until first edited.</summary>
    public User? UpdatedBy { get; set; }
}
