using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// AIP (Annual Investment Program) upload + status lifecycle (RAL-64).
/// Entry mode is always "Upload" (XLSM). Manual entry is deferred.
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only) → Archived.
/// </summary>
public interface IAipService
{
    Task<IReadOnlyList<AipRecordDto>> GetAllAsync(int? fiscalYear, string? status, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns a slim hierarchy (Id, RefCode, Name, amounts, funding source) for the WFP
    /// activity grid. Omits heavy free-text fields — ~10× smaller than GetByIdAsync.
    /// </summary>
    Task<ServiceResult<AipRecordSummaryDto>> GetSummaryByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Parses an XLSM stream and returns a preview without persisting anything.
    /// <paramref name="knownFundingSources"/> is passed in by the Functions layer so the
    /// service can flag unmatched funding source codes as warnings.
    /// </summary>
    Task<ServiceResult<AipImportPreviewDto>> ParsePreviewAsync(
        Stream xlsmStream,
        int fiscalYear,
        IReadOnlyList<FundingSource> knownFundingSources,
        CancellationToken ct = default);

    /// <summary>
    /// Persists the parsed hierarchy that was returned by <see cref="ParsePreviewAsync"/>.
    /// The client echoes back the full SectorOffices payload (stateless confirm).
    /// </summary>
    Task<ServiceResult<AipRecordDto>> ConfirmImportAsync(
        AipImportConfirmDto dto,
        Guid uploadedById,
        CancellationToken ct = default);

    // ── Manual entry (RAL-62) — one node at a time, Office → Program → Project → Activity ──

    /// <summary>Creates a blank Manual-entry AipRecord. Subject to the same one-active-AIP-per-
    /// fiscal-year guard as ConfirmImportAsync's create path.</summary>
    Task<ServiceResult<AipRecordDto>> CreateManualRecordAsync(
        CreateAipRecordDto dto, Guid createdById, CancellationToken ct = default);

    /// <summary>Adds an office (level 1) to a Draft AipRecord. RefCode is auto-derived from
    /// the sector prefix + the config Office's OfficeRefCode.</summary>
    Task<ServiceResult<AipOfficeDto>> AddOfficeAsync(
        int aipRecordId, CreateAipOfficeDto dto, CancellationToken ct = default);

    /// <summary>Adds a program (level 2) under an office. RefCode auto-increments within the office.</summary>
    Task<ServiceResult<AipProgramDto>> AddProgramAsync(
        int officeId, CreateAipProgramDto dto, CancellationToken ct = default);

    /// <summary>Adds a project (level 3) under a program. RefCode auto-increments within the program.</summary>
    Task<ServiceResult<AipProjectDto>> AddProjectAsync(
        int programId, CreateAipProjectDto dto, CancellationToken ct = default);

    /// <summary>Adds an activity (level 4, leaf) under a project. RefCode auto-increments within
    /// the project; Total is computed as Ps+Mooe+Co (null only when all three are blank).</summary>
    Task<ServiceResult<AipActivityDto>> AddActivityAsync(
        int projectId, CreateAipActivityDto dto, CancellationToken ct = default);

    /// <summary>RAL-179 — updates an existing activity's editable fields in place. RefCode,
    /// ProjectId, and identity are immutable; FundingSourceId re-resolves FundingSourceSnapshot.
    /// Only allowed while the parent AipRecord is Draft. <paramref name="aipRecordId"/> is a
    /// defensive cross-check that the activity actually belongs to that record.</summary>
    Task<ServiceResult<AipActivityDto>> UpdateActivityAsync(
        int aipRecordId, int activityId, UpdateAipActivityDto dto, CancellationToken ct = default);

    /// <summary>Deletes a program and its whole subtree (projects, activities). Draft-only.</summary>
    Task<ServiceResult<bool>> DeleteProgramAsync(int programId, CancellationToken ct = default);

    /// <summary>Deletes a project and its activities. Draft-only.</summary>
    Task<ServiceResult<bool>> DeleteProjectAsync(int projectId, CancellationToken ct = default);

    /// <summary>Deletes a single activity. Draft-only.</summary>
    Task<ServiceResult<bool>> DeleteActivityAsync(int activityId, CancellationToken ct = default);

    Task<ServiceResult<AipRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDto>> UnlockAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDto>> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Sets a program's WFP report function band (v1.4 Q1) — required, one of CORE/STRATEGIC/
    /// SUPPORT; null/empty is rejected with BadRequest. Captured during WFP data entry, not AIP
    /// import — see wfp/entry context picker. New programs default to CORE at import time
    /// (AipService.ConfirmImportAsync) so the field is never left unset.
    /// </summary>
    Task<ServiceResult<AipProgramDto>> UpdateProgramFunctionBandAsync(
        int programId, string? functionBand, CancellationToken ct = default);

    /// <summary>
    /// Sets an activity's "…-CREATION" PS flag (v1.4 Q2). No validation beyond existence.
    /// </summary>
    Task<ServiceResult<AipActivityDto>> UpdateActivityIsCreationAsync(
        int activityId, bool isCreation, CancellationToken ct = default);

    /// <summary>Wipes all AIP records (cascade removes hierarchy). Returns deleted AipRecord count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);
}
