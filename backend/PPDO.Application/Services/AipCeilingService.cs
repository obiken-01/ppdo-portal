using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="IAipCeilingService"/> (V18-46 / PPDO-56).
///
/// The whole check is four lines of arithmetic wrapped in a great deal of care about <i>which</i>
/// figures reach it. Read <see cref="IAipCeilingService"/>'s four traps before changing anything
/// here; each is pinned by a named test in <c>AipCeilingServiceTests</c>, and each failure mode is
/// permissive and silent.
///
/// <b>The comparison is office-level, for every office.</b> The submit action hands the whole
/// office's work to review in one go, so the bound is the office's own General Fund ceiling. The
/// division dimension lives in the reservation ledger, which exists for the deferred WFP netting
/// and for the per-division dashboard — it is not what gates submit. That is why a guest office
/// with no divisions needs no special case in the <i>check</i> and gets no synthetic division row
/// (spec §3.2).
///
/// <b>The ledger is where the two office shapes do diverge</b> (V18-47 / PPDO-57), and the
/// divergence is named rather than inferred: see
/// <see cref="UpsertLedgerForActivityAsync"/>. A guest office and a host-office program with no
/// division assignment both write no row, and only one of them is a problem — before PPDO-57 both
/// were a null division id and the problem was unreportable.
///
/// <b><see cref="IWfpCeilingService"/> takes a zero diff.</b> Its allocation check stays live for
/// FY2028+, and a WFP expenditure remains bound by the lesser of its AIP activity amount and the
/// fund's currently remaining allocation. Two bounds, both enforced.
/// </summary>
public sealed class AipCeilingService : IAipCeilingService
{
    private readonly IAipRepository                 _aipRepo;
    private readonly IAipExpenditureRepository      _expRepo;
    private readonly IAipAllocationLedgerRepository _ledgerRepo;
    private readonly IAllocationRepository          _allocationRepo;
    private readonly IOfficeRepository              _officeRepo;
    private readonly IAllocationService             _allocation;
    private readonly ILogger<AipCeilingService>     _logger;

    public AipCeilingService(
        IAipRepository                 aipRepo,
        IAipExpenditureRepository      expRepo,
        IAipAllocationLedgerRepository ledgerRepo,
        IAllocationRepository          allocationRepo,
        IOfficeRepository              officeRepo,
        IAllocationService             allocation,
        ILogger<AipCeilingService>     logger)
    {
        _aipRepo        = aipRepo;
        _expRepo        = expRepo;
        _ledgerRepo     = ledgerRepo;
        _allocationRepo = allocationRepo;
        _officeRepo     = officeRepo;
        _allocation     = allocation;
        _logger         = logger;
    }

    // ── Read status ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AipCeilingStatusDto> GetStatusAsync(
        int aipOfficeId, CancellationToken ct = default)
    {
        int? gfId = await _allocation.GetGeneralFundIdAsync(ct);
        if (gfId is null)
            return new AipCeilingStatusDto(null, false, 0m, 0m, 0m, WithinCeiling: false);

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(aipOfficeId, ct);
        if (office?.OfficeId is not int configOfficeId)
            return new AipCeilingStatusDto(gfId, false, 0m, 0m, 0m, WithinCeiling: false);

        // ⚠️ GetOfficeByIdAsync does not Include the parent record, so the fiscal year is a second
        // fetch. Reading office.AipRecord.FiscalYear here compiles and throws at runtime.
        AipRecord? record = await _aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (record is null)
            return new AipCeilingStatusDto(gfId, false, 0m, 0m, 0m, WithinCeiling: false);

        // ⚠️ Sequential awaits, not Task.WhenAll — DbContext is not thread-safe, and concurrent
        // queries on the shared context threw InvalidOperationException in prod once already
        // (CLAUDE.md, GetStatsAsync).
        ServiceResult<BudgetCeilingDto> ceilingResult =
            await _allocation.GetCeilingAsync(configOfficeId, record.FiscalYear, gfId.Value, ct);

        bool    ceilingSet = ceilingResult.IsSuccess;
        decimal ceiling    = ceilingSet ? ceilingResult.Value!.Amount : 0m;

        // ⚠️ record + CONFIG office, not the AipOffice row that was passed in. The ceiling is one
        // bound for the whole office and an office owns several group rows; summing only the row
        // the caller happened to hand over made every other group's General Fund work invisible to
        // its own ceiling.
        decimal encoded = await SumEncodedBaseRoundedAsync(
            record.Id, configOfficeId, gfId.Value, ct);

        // ⚠️ No Math.Max. A negative remaining is the signal a ceiling was cut below encoded work,
        // and it is exactly what must block submit (A5-b).
        decimal remaining = ceiling - encoded;

        return new AipCeilingStatusDto(
            gfId, ceilingSet, ceiling, encoded, remaining, WithinCeiling: remaining >= 0m);
    }

    // ── The submit gate ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string?> ValidateForSubmitAsync(
        int aipOfficeId, CancellationToken ct = default)
    {
        AipCeilingStatusDto status = await GetStatusAsync(aipOfficeId, ct);

        // "Could not check" is not "within ceiling". Refusing is the safe direction: the office
        // learns something is unconfigured instead of submitting past a check that never ran.
        if (status.GeneralFundId is null)
            return "No funding source is configured as the General Fund, so the ceiling cannot be "
                 + "checked. Ask an administrator to mark one in Configuration → Funding Sources.";

        if (status.WithinCeiling)
            return null;

        // ⚠️ An unset ceiling is ZERO, not unlimited — the same rule as a missing allocation row
        // (GetDivisionAllocationAsync → 0m). But the message must say so rather than reporting an
        // overage, or the encoder deletes work to fix a problem that is PBO's to fix.
        if (!status.CeilingSet)
            return $"There is no General Fund ceiling set for this office for the fiscal year, so "
                 + $"the encoded total of ₱{status.EncodedBaseRounded:N2} cannot be approved. "
                 + $"Ask the Provincial Budget Office to set the ceiling.";

        decimal overage = status.EncodedBaseRounded - status.Ceiling;
        return $"General Fund MOOE + CO totals ₱{status.EncodedBaseRounded:N2}, which is "
             + $"₱{overage:N2} over the ceiling of ₱{status.Ceiling:N2}. "
             + $"Reduce the encoded amounts or ask the Provincial Budget Office to raise the ceiling.";
    }

    // ── Ledger upsert ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpsertLedgerForActivityAsync(int aipActivityId, CancellationToken ct = default)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(aipActivityId, ct);
        if (activity is null) return;

        AipLedgerAttribution attribution = await ResolveAttributionAsync(activity, ct);

        // ⚠️ Only one of these four outcomes writes a row. Two of the other three matter and are
        // not the same as each other — collapsing them back into a null division is what
        // V18-47 / PPDO-57 exists to stop: one is the correct resting state of a guest office, the
        // other is a host-office misconfiguration that silently reserves nothing.
        switch (attribution.Kind)
        {
            case LedgerAttributionKind.GuestOffice:
                // Division is not a scoping axis for a guest office at all (Permission_Matrix
                // §3.1, PPDO-4) — not "no divisions configured yet". There is nothing to attribute
                // the reservation to and nothing is wrong. Their bound is the office-level ceiling
                // in GetStatusAsync, and no synthetic division row is invented for them.
                return;

            case LedgerAttributionKind.HostProgramUnassigned:
                // A host-office program that no ProgramDivision row claims. It reserves nothing
                // against any division allocation, and until this line nothing anywhere said so.
                // It is NOT shared across every division — that would make each division's figures
                // overlap, the same reason AipReadScope.FilterPrograms excludes it from a
                // division-scoped view. The allocation-setup panel is where it gets fixed.
                _logger.LogWarning(
                    "AIP activity reserves against no division: its program is unassigned in the "
                    + "host office. AipActivityId: {AipActivityId}, OfficeRefCode: {OfficeRefCode}, "
                    + "ProgramRefCode: {ProgramRefCode}",
                    aipActivityId, attribution.OfficeRefCode, attribution.ProgramRefCode);
                return;

            case LedgerAttributionKind.Unresolvable:
                return;
        }

        AipLedgerContext context = attribution.Context!;

        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdAsync(aipActivityId, ct);

        // Funds this activity reserves against now, plus funds it used to and no longer does.
        // ⚠️ The second half matters: without it a fund the encoder switched away from keeps its
        // last positive reservation forever — the staleness RAL-154 fixed on the WFP side.
        Dictionary<int, decimal> reservedByFund = lines
            .Where(l => l.FundingSourceId.HasValue)
            .GroupBy(l => l.FundingSourceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Mooe + l.Co));

        IReadOnlyList<int> previousFunds =
            await _ledgerRepo.GetFundingSourceIdsForActivityAsync(aipActivityId, ct);

        foreach (int fundId in reservedByFund.Keys.Union(previousFunds))
        {
            decimal reserved = reservedByFund.GetValueOrDefault(fundId, 0m);

            AipDivisionAllocationLedger? row = await _ledgerRepo.FindAsync(
                context.DivisionId, context.FiscalYear, fundId, aipActivityId, ct);

            if (row is null)
            {
                if (reserved == 0m) continue; // nothing to record, and no stale row to correct

                await _ledgerRepo.AddAsync(new AipDivisionAllocationLedger
                {
                    DivisionId              = context.DivisionId,
                    FiscalYear              = context.FiscalYear,
                    FundingSourceId         = fundId,
                    AipActivityId           = aipActivityId,
                    AllocatedAmountSnapshot = await GetAllocationSnapshotAsync(context, fundId, ct),
                    ReservedAmount          = reserved,
                    UpdatedAt               = DateTime.UtcNow,
                }, ct);
            }
            else
            {
                row.ReservedAmount          = reserved;
                row.AllocatedAmountSnapshot = await GetAllocationSnapshotAsync(context, fundId, ct);
                row.UpdatedAt               = DateTime.UtcNow;
                await _ledgerRepo.UpdateAsync(row, ct);
            }
        }

        await _ledgerRepo.SaveChangesAsync(ct);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Σ over the office's activities of <c>roundUp(MOOE) + roundUp(CO)</c>, General Fund only.
    ///
    /// ⚠️ <b>Round each figure, then add.</b> The province builds the form upward — every printed
    /// figure is a sum of already rounded figures — so summing first and rounding once produces a
    /// smaller number that disagrees with the document at every level above the row
    /// (<see cref="AipRounding"/>). MOOE and CO round separately because they print as separate
    /// columns.
    /// </summary>
    private async Task<decimal> SumEncodedBaseRoundedAsync(
        int aipRecordId, int configOfficeId, int generalFundId, CancellationToken ct)
    {
        IReadOnlyList<AipActivityFundTotalsDto> perActivity =
            await _expRepo.SumMooeCoByConfigOfficeAndFundAsync(
                aipRecordId, configOfficeId, generalFundId, ct);

        decimal total = 0m;
        foreach (AipActivityFundTotalsDto a in perActivity)
            total += AipRounding.UpToThousand(a.Mooe) + AipRounding.UpToThousand(a.Co);

        return total;
    }

    /// <summary>
    /// Which shape an activity's reservation is in — see <see cref="LedgerAttributionKind"/>
    /// (V18-47 / PPDO-57).
    ///
    /// ⚠️ <b>The guest-office branch returns before <c>ProgramDivision</c> is ever queried.</b>
    /// That ordering is the whole point: a guest office is not "a host office whose assignment
    /// lookup came back empty", so it must not reach a lookup whose empty result would be a
    /// misconfiguration. Asking and ignoring the answer would leave the two shapes one refactor
    /// away from being the same code path again.
    /// </summary>
    private async Task<AipLedgerAttribution> ResolveAttributionAsync(
        AipActivity activity, CancellationToken ct)
    {
        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null) return AipLedgerAttribution.Unresolvable;

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null) return AipLedgerAttribution.Unresolvable;

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null) return AipLedgerAttribution.Unresolvable;

        AipRecord? record = await _aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (record is null) return AipLedgerAttribution.Unresolvable;

        if (office.OfficeId is not int configOfficeId) return AipLedgerAttribution.Unresolvable;

        // ⚠️ Host is read from Office.IsHostOffice, never from "has divisions" and never from the
        // code "PPDO" (DECISION F / RAL-258). A host office that has not configured a division yet
        // is a host office with a misconfigured program, not a guest office — and a filtered unique
        // index guarantees at most one row carries the flag.
        Office? configOffice = await _officeRepo.GetByIdAsync(configOfficeId, ct);
        if (configOffice is null) return AipLedgerAttribution.Unresolvable;

        if (!configOffice.IsHostOffice)
            return AipLedgerAttribution.GuestOffice;

        int? divisionId = await ResolveDivisionIdAsync(office, program, ct);
        if (divisionId is null)
            return AipLedgerAttribution.Unassigned(office.RefCode, program.RefCode);

        return AipLedgerAttribution.ToDivision(
            new AipLedgerContext(divisionId.Value, configOfficeId, record.FiscalYear));
    }

    /// <summary>
    /// The division a host-office program's reservation is attributed to, or null when no
    /// <c>ProgramDivision</c> row claims it.
    ///
    /// ⚠️ <b>Only ever called for the host office</b> — see <see cref="ResolveAttributionAsync"/>.
    /// A null here means an unassigned host program, one specific and fixable state, never a guest
    /// office.
    ///
    /// ⚠️ Resolved through <c>ProgramDivision</c>, which is keyed on the program's <b>ref code</b>,
    /// not an FK. That is deliberate and must stay: the assignment is permanent across fiscal
    /// years, and an FK to <c>aip_programs.id</c> would pin it to one year
    /// (<c>ProgramDivision.cs</c>, RAL-249). PPDO-52 keeps it in step by re-linking on renumber.
    /// </summary>
    private async Task<int?> ResolveDivisionIdAsync(
        AipOffice office, AipProgram program, CancellationToken ct)
    {
        IReadOnlyList<ProgramDivision> assignments =
            await _allocationRepo.FindProgramDivisionsAsync(office.RefCode, program.RefCode, ct);

        // A program may span several divisions. The reservation is attributed to the first by id,
        // deterministically — splitting one activity's money across divisions is a modelling
        // question nobody has answered, and guessing a split here would be inventing policy.
        return assignments.Count == 0 ? null : assignments.Min(a => a.DivisionId);
    }

    private async Task<decimal> GetAllocationSnapshotAsync(
        AipLedgerContext context, int fundId, CancellationToken ct)
    {
        IReadOnlyList<DivisionAllocationDto> allocations =
            await _allocation.GetAllocationsAsync(context.ConfigOfficeId, context.FiscalYear, fundId, ct);

        // Divisions with no row are simply absent, and absent means ZERO — not unlimited. The
        // snapshot is history only; the live check always re-reads, so a stale one cannot mask a
        // ceiling cut.
        return allocations
            .Where(a => a.DivisionId == context.DivisionId)
            .Sum(a => a.Amount);
    }

    /// <summary>Where one activity's reservation is posted. Internal to this service.</summary>
    private sealed record AipLedgerContext(int DivisionId, int ConfigOfficeId, int FiscalYear);

    /// <summary>
    /// The four outcomes of resolving where an activity's reservation belongs — three of which
    /// write no ledger row, for three different reasons (V18-47 / PPDO-57).
    /// </summary>
    private enum LedgerAttributionKind
    {
        /// <summary>A host-office program assigned to a division. The row is written.</summary>
        Division,

        /// <summary>
        /// A guest office. Division is not a scoping axis for them, so there is nothing to
        /// attribute to and nothing wrong. Their bound is the office-level ceiling.
        /// </summary>
        GuestOffice,

        /// <summary>
        /// A host-office program no <c>ProgramDivision</c> row claims — a misconfiguration that
        /// silently reserves nothing. Logged.
        /// </summary>
        HostProgramUnassigned,

        /// <summary>
        /// A node in the chain does not exist, or an <c>AipOffice</c> the V18-32 backfill could not
        /// match to a config office. Nothing to do and nothing to say.
        /// </summary>
        Unresolvable,
    }

    /// <summary>
    /// The resolution result. A record rather than an <c>int?</c> so that "no division" has to be
    /// read as one of three named states at every call site — the collapse this ticket removes.
    /// </summary>
    private sealed record AipLedgerAttribution(
        LedgerAttributionKind Kind,
        AipLedgerContext?     Context        = null,
        string?               OfficeRefCode  = null,
        string?               ProgramRefCode = null)
    {
        public static readonly AipLedgerAttribution GuestOffice   = new(LedgerAttributionKind.GuestOffice);
        public static readonly AipLedgerAttribution Unresolvable  = new(LedgerAttributionKind.Unresolvable);

        public static AipLedgerAttribution ToDivision(AipLedgerContext context)
            => new(LedgerAttributionKind.Division, Context: context);

        public static AipLedgerAttribution Unassigned(string officeRefCode, string programRefCode)
            => new(LedgerAttributionKind.HostProgramUnassigned,
                   OfficeRefCode: officeRefCode, ProgramRefCode: programRefCode);
    }
}
