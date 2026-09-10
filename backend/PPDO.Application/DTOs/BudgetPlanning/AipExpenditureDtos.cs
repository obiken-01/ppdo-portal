namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One expenditure line as returned to the entry page (V18-42 / PPDO-52).
///
/// Amounts are <b>pesos</b> and <b>base</b> — no ×1000 and no uplift. The entry page converts to
/// ₱000 at its own edge, and the +30% uplift is applied at render time by the printed form only.
///
/// Carries the account and funding-source snapshots as well as the ids, because a line entered
/// last year must still print what the config said then, even after someone renames an account.
/// </summary>
public sealed record AipExpenditureDto(
    int      Id,
    int      ActivityId,
    int?     AccountId,
    string?  AccountNumber,
    string?  AccountTitle,
    int?     FundingSourceId,
    string?  FundingSourceCode,
    string?  FundingSourceName,
    decimal  Ps,
    decimal  Mooe,
    decimal  Co,
    decimal  Total,
    IReadOnlyList<AipProcurementItemDto> ProcurementItems);

/// <summary>
/// One procurement item under an AIP expenditure line (V18-80 / PPDO-54).
///
/// <b>No period, frequency, annual-quarter or reserve field appears here</b>, and none may be
/// added: those are WFP <i>schedule</i> concepts and an AIP activity carries one annual figure.
/// <see cref="NumberOfDays"/> is the deliberate exception — it was asked for in its own right, not
/// carried across for parity.
///
/// Name / Unit / UnitPrice are the values snapshotted at save time, not the Price Index's current
/// ones, so a saved plan still prints what it was costed at.
/// </summary>
public sealed record AipProcurementItemDto(
    int      Id,
    int?     PriceIndexItemId,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    decimal  Qty,
    decimal  NumberOfDays,
    decimal  LineTotal);

/// <summary>
/// One procurement item as submitted with its parent expenditure line (V18-80 / PPDO-54).
///
/// ⚠️ <see cref="AipProcurementItemDto.LineTotal"/> has no counterpart here on purpose: the total
/// is computed server-side from qty × unitPrice × numberOfDays and never accepted from a caller,
/// the same rule as the line's own Total.
/// </summary>
public sealed record SaveAipProcurementItemDto(
    int?     PriceIndexItemId,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    decimal  Qty,
    decimal  NumberOfDays);

/// <summary>
/// Body of <c>POST /api/budget-planning/aip/activities/{activityId}/expenditures</c>.
///
/// ⚠️ <b>One funding source per line</b> (Phase 2 decision 4). Multi-fund is expressed as several
/// lines, never as one line naming two funds — 60% of the FY2027 file's money rows name several
/// funds against one un-split amount, which is exactly what makes a General-Fund ceiling
/// uncomputable. The multi-fund toggle (V18-43) changes how many lines the UI offers, not this
/// shape.
///
/// ⚠️ <see cref="Total"/> is absent on purpose. It is computed from the three components on write
/// and never accepted from a caller — RAL-144's precedent, where a trusted source-file Total
/// desynced from its own parts while every reader believed it.
/// </summary>
public sealed record CreateAipExpenditureDto(
    int?     AccountId,
    int?     FundingSourceId,
    decimal  Ps,
    decimal  Mooe,
    decimal  Co,
    IReadOnlyList<SaveAipProcurementItemDto>? ProcurementItems = null);

/// <summary>
/// Body of <c>PUT /api/budget-planning/aip/expenditures/{id}</c>. Same shape as the create — a line
/// is small enough that a partial update would only add a way to leave it half-changed.
///
/// ⚠️ <see cref="ProcurementItems"/> <b>replaces the line's items wholesale</b>, so an empty list
/// removes them all and returns the line to a typed amount. Null and empty therefore mean
/// different things on an update: null leaves the existing items alone.
/// </summary>
public sealed record UpdateAipExpenditureDto(
    int?     AccountId,
    int?     FundingSourceId,
    decimal  Ps,
    decimal  Mooe,
    decimal  Co,
    IReadOnlyList<SaveAipProcurementItemDto>? ProcurementItems = null);

/// <summary>
/// What an expenditure write returns: the line, plus its activity's recomputed totals so the page
/// can update the row and its parent without a refetch.
///
/// ⚠️ <see cref="ActivityTotal"/> is null when the activity has <b>never</b> been costed, and
/// <c>0</c> when its lines were all deleted. Those are different states with the same
/// <see cref="LineCount"/>, and the submit checklist tells them apart (V18-34).
/// </summary>
public sealed record AipExpenditureWriteResultDto(
    AipExpenditureDto? Line,
    int                ActivityId,
    decimal?           ActivityPs,
    decimal?           ActivityMooe,
    decimal?           ActivityCo,
    decimal?           ActivityTotal,
    int                LineCount,
    /// <summary>
    /// The distinct funding-source codes the activity now draws on, in first-use order — the
    /// form's Funding Source column (7), rendered joined (PPDO-80).
    ///
    /// ⚠️ <b>It belongs on the write result and not only on the tree read</b> for the same reason
    /// the totals do: the entry page updates the row in place and never reloads the record, so
    /// without this the fund cell would go stale the moment a line naming a new fund was added —
    /// or keep naming a fund whose only line was just deleted.
    /// </summary>
    IReadOnlyList<string> ActivityFundCodes);
