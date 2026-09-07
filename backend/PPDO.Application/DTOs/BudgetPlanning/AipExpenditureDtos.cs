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
    decimal  Total);

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
    decimal  Co);

/// <summary>
/// Body of <c>PUT /api/budget-planning/aip/expenditures/{id}</c>. Same shape as the create — a line
/// is small enough that a partial update would only add a way to leave it half-changed.
/// </summary>
public sealed record UpdateAipExpenditureDto(
    int?     AccountId,
    int?     FundingSourceId,
    decimal  Ps,
    decimal  Mooe,
    decimal  Co);

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
    int                LineCount);
