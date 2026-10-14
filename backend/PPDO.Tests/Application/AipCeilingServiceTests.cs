using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The AIP ceiling check (V18-46 / PPDO-56).
///
/// <para>
/// Its ticket names four ways to get this wrong, and every one of them is <b>permissive and
/// silent</b> — the check keeps returning "fine" and the first symptom is an office whose printed
/// AIP exceeds a ceiling nobody was told about. There is therefore <b>one test per trap, named
/// after the trap</b>, rather than a happy path plus an error case.
/// </para>
///
/// <para>
/// ⚠️ Every assertion below is a literal peso figure, never a restatement of the implementation
/// (<c>ceiling - encoded</c>, <c>mooe * factor</c>). A test that recomputes the thing it is
/// checking passes whatever the code happens to do, which is the failure
/// <see cref="AipWfpBoundaryTests"/> was written to stop repeating.
/// </para>
/// </summary>
public sealed class AipCeilingServiceTests
{
    private const int AipOfficeId    = 55;
    private const int ConfigOfficeId = 7;
    private const int FiscalYear     = 2028;
    private const int GeneralFundId  = 1;
    private const int GadFundId      = 2;

    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly Mock<IAipRepository>                _aipRepo   = new(MockBehavior.Strict);
    private readonly Mock<IAipExpenditureRepository>     _expRepo   = new(MockBehavior.Strict);
    private readonly Mock<IAipAllocationLedgerRepository> _ledgerRepo = new(MockBehavior.Strict);
    private readonly Mock<IAllocationRepository>         _allocationRepo = new(MockBehavior.Strict);
    private readonly Mock<IOfficeRepository>             _officeRepo = new(MockBehavior.Strict);
    private readonly Mock<IAllocationService>            _allocation = new(MockBehavior.Strict);

    private AipCeilingService Build() => new(
        _aipRepo.Object, _expRepo.Object, _ledgerRepo.Object, _allocationRepo.Object,
        _officeRepo.Object, _allocation.Object, NullLogger<AipCeilingService>.Instance);

    private const int AipRecordId = 13;

    /// <summary>
    /// An AIP office row belonging to config office 7, under an FY2028 record.
    ///
    /// ⚠️ The record is stubbed as a <b>separate</b> fetch, because
    /// <c>AipRepository.GetOfficeByIdAsync</c> does not <c>Include</c> the parent — reading
    /// <c>office.AipRecord</c> would be a null reference at runtime while looking perfectly
    /// sensible in the service. The fixture mirrors the real repository rather than a convenient
    /// one, which is the only way this test can catch that.
    /// </summary>
    private void GivenOffice()
    {
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipOffice
            {
                Id = AipOfficeId,
                OfficeId = ConfigOfficeId,
                AipRecordId = AipRecordId,
            });

        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = FiscalYear });
    }

    private void GivenGeneralFund(int? id = GeneralFundId)
    {
        _allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        // v1.9.0 (PPDO-216) — no other fund is enforced unless a test says so.
        GivenEnforcedFunds();
    }

    /// <summary>The non-GF funds flagged "Enforce AIP ceiling" (PPDO-216).</summary>
    private void GivenEnforcedFunds(params FundingSource[] funds)
        => _allocation.Setup(a => a.GetEnforcedAipCeilingFundsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(funds);

    private static FundingSource Fund(int id, string code, int? officeId = null)
        => new() { Id = id, Code = code, Name = code + " fund", OfficeId = officeId, EnforceAipCeiling = true };

    /// <summary>A ceiling on any fund; null = not set.</summary>
    private void GivenFundCeiling(int fundId, string code, decimal? amount)
        => _allocation.Setup(a => a.GetCeilingAsync(
                ConfigOfficeId, FiscalYear, fundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(amount is null
                ? ServiceResult<BudgetCeilingDto>.NotFound("unset")
                : ServiceResult<BudgetCeilingDto>.Ok(
                    new BudgetCeilingDto(fundId, ConfigOfficeId, FiscalYear, fundId, code, code + " fund", amount.Value)));

    private void GivenCeiling(decimal? amount)
    {
        _allocation.Setup(a => a.GetCeilingAsync(
                ConfigOfficeId, FiscalYear, GeneralFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(amount is null
                ? ServiceResult<BudgetCeilingDto>.NotFound("unset")
                : ServiceResult<BudgetCeilingDto>.Ok(
                    new BudgetCeilingDto(1, ConfigOfficeId, FiscalYear, GeneralFundId, "GF", "General Fund", amount.Value)));
    }

    /// <summary>Per-activity MOOE/CO for the fund the service asks about.</summary>
    private void GivenLines(int fundId, params (decimal Mooe, decimal Co)[] activities)
    {
        List<AipActivityFundTotalsDto> rows = activities
            .Select((a, i) => new AipActivityFundTotalsDto(1000 + i, a.Mooe, a.Co))
            .ToList();

        // ⚠️ Keyed on (record, CONFIG office), not on the AipOffice row the caller passes in — the
        // ceiling is one office-level bound over every sub-office group the office owns.
        _expRepo.Setup(r => r.SumMooeCoByConfigOfficeAndFundAsync(
                AipRecordId, ConfigOfficeId, fundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
    }

    // ── Trap 1: General Fund ONLY ─────────────────────────────────────────────

    /// <summary>
    /// ↩️ DECISION H (ceilings on every fund) was withdrawn 2026-08-26, one day after adoption.
    /// This has flipped twice. GAD money must not reach the check at all — not "be checked against
    /// its own ceiling", not "be added in".
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_Trap1_GadMoneyIsNotCountedAgainstTheGeneralFundCeiling()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        GivenLines(GeneralFundId, (Mooe: 400_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.Null(error);
        // The GAD fund was never queried. Had it been summed in, ₱400k + GAD would be the figure.
        _expRepo.Verify(r => r.SumMooeCoByConfigOfficeAndFundAsync(
            AipRecordId, ConfigOfficeId, GadFundId, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Trap 2: PS is exempt ──────────────────────────────────────────────────

    /// <summary>
    /// PS is exempt as an expense class, on top of the fund restriction. An office at its ceiling
    /// on MOOE + CO submits successfully however large its PS is — the repository is asked for
    /// MOOE and CO only, so a PS column cannot leak in.
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_Trap2_LargePsDoesNotBlockAnOfficeAtItsCeilingOnMooeAndCo()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        // Exactly at the ceiling on MOOE + CO. PS is not part of the projection at all.
        GivenLines(GeneralFundId, (Mooe: 600_000m, Co: 400_000m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.Null(error);
    }

    // ── Trap 3: non-GF excluded explicitly, and blank means ZERO ──────────────

    /// <summary>
    /// A missing ceiling row is <b>zero</b>, not unlimited — the same rule that governs a missing
    /// allocation. Any encoded General Fund money therefore blocks submit.
    ///
    /// ⚠️ And the message must say the ceiling is unset, not that the office is over by ₱X: the
    /// second sends an encoder to delete work that is not the problem.
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_Trap3_AnUnsetCeilingIsZeroNotUnlimited()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(null);
        GivenLines(GeneralFundId, (Mooe: 1_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.NotNull(error);
        Assert.Contains("no General Fund ceiling", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An office with a ceiling and nothing encoded is within it, not blocked by the zero rule.</summary>
    [Fact]
    public async Task ValidateForSubmit_Trap3_NothingEncodedUnderAnUnsetCeilingStillPasses()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(null);
        GivenLines(GeneralFundId);

        Assert.Null(await Build().ValidateForSubmitAsync(AipOfficeId));
    }

    // ── Trap 4: rounded, and BASE not uplifted ────────────────────────────────

    /// <summary>
    /// <b>Round every figure UP to the thousand, then sum</b> (DECISION 9) — not sum then round.
    ///
    /// Three ₱1,200 MOOE figures: rounded-then-summed is ₱6,000; summed-then-rounded is ₱4,000.
    /// Against a ₱5,000 ceiling the two orders disagree about whether submit is allowed, so this
    /// case distinguishes them by outcome rather than by arithmetic.
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_Trap4_EachFigureIsRoundedUpBeforeSummingNotAfter()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(5_000m);
        GivenLines(GeneralFundId,
            (Mooe: 1_200m, Co: 0m),
            (Mooe: 1_200m, Co: 0m),
            (Mooe: 1_200m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        // ₱6,000 encoded against a ₱5,000 ceiling. Sum-then-round would have produced ₱4,000
        // and passed silently.
        Assert.NotNull(error);
        Assert.Contains("6,000", error);
        Assert.Contains("5,000", error);
        Assert.Contains("1,000", error); // the overage
    }

    /// <summary>MOOE and CO are rounded as separate figures, the way the form prints them.</summary>
    [Fact]
    public async Task ValidateForSubmit_Trap4_MooeAndCoAreRoundedSeparatelyNotAsTheirSum()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(5_000m);
        // 500 + 500 = 1,000 exactly if summed first; 1,000 + 1,000 = 2,000 rounded separately.
        GivenLines(GeneralFundId, (Mooe: 500m, Co: 500m));

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.Equal(2_000m, status.EncodedBaseRounded);
    }

    /// <summary>
    /// The +30% uplift is presentation-only (DECISION G / tracker G3) and never reaches the check.
    /// An office exactly at its ceiling passes — and will print a document about 30% over it.
    /// ⚠️ That is intended, and AIP_Form_Spec §6.2 says so.
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_Trap4_TheThirtyPercentUpliftIsNotPartOfTheComparison()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(10_000_000m);
        GivenLines(GeneralFundId, (Mooe: 10_000_000m, Co: 0m));

        // Base is exactly at the ceiling. Uplifted would be ₱13,000,000 and would fail.
        Assert.Null(await Build().ValidateForSubmitAsync(AipOfficeId));
    }

    // ── Remaining is never clamped ────────────────────────────────────────────

    /// <summary>
    /// After PBO cuts a ceiling below encoded work, <c>Remaining</c> is legitimately negative. A
    /// <c>Math.Max(0, …)</c> anywhere hides the only signal the office gets (A5-b).
    /// </summary>
    [Fact]
    public async Task GetStatus_AfterACeilingCut_RemainingIsNegativeAndNotClamped()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        GivenLines(GeneralFundId, (Mooe: 3_000_000m, Co: 0m));

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.Equal(-2_000_000m, status.Remaining);
        Assert.False(status.WithinCeiling);
    }

    // ── The division-less (guest office) shape ────────────────────────────────

    /// <summary>
    /// A guest office has a ceiling and no divisions. The check runs at office level and returns a
    /// real answer — it does not fall over, and no synthetic division row is invented for it
    /// (spec §3.2). This is the shape V18-47 / PPDO-57 formalises.
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_GuestOfficeWithNoDivisions_IsCheckedAtOfficeLevel()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        GivenLines(GeneralFundId, (Mooe: 2_000_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.NotNull(error);
        // No division allocation was consulted and no ledger row was written.
        _ledgerRepo.VerifyNoOtherCalls();
    }

    // ── No General Fund configured at all ─────────────────────────────────────

    /// <summary>
    /// With no fund marked General Fund the check cannot be performed. It must refuse rather than
    /// pass — "could not check" is not "within ceiling".
    /// </summary>
    [Fact]
    public async Task ValidateForSubmit_WhenNoGeneralFundIsConfigured_RefusesRatherThanPasses()
    {
        GivenOffice();
        GivenGeneralFund(id: null);

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.NotNull(error);
        Assert.Contains("General Fund", error);
    }

    // ── The ledger: three shapes, two of which write nothing (V18-47 / PPDO-57) ──

    private const int    LedgerActivityId = 900;
    private const int    LedgerProjectId  = 800;
    private const int    LedgerProgramId  = 700;
    private const int    DivisionId       = 4;
    private const string OfficeRefCode    = "3000-000-1-01-013";
    private const string ProgramRefCode   = "1000-001";

    /// <summary>
    /// The activity → project → program → AIP office → record chain the ledger walks, plus the
    /// <b>config office</b> whose <c>IsHostOffice</c> flag decides which of the three shapes this
    /// is. Host is read from the flag, never from "does this office have divisions" and never from
    /// the code "PPDO" (DECISION F / RAL-258).
    /// </summary>
    /// <param name="fiscalYear">
    /// ↩️ PPDO-150: FY2028+ attributes by the activity's own tag, FY ≤ 2027 through
    /// <c>ProgramDivision</c> — the year decides which rule a test is about.
    /// </param>
    private void GivenActivityChain(bool isHostOffice, int fiscalYear = FiscalYear, int? activityDivisionId = null)
    {
        _aipRepo.Setup(r => r.GetActivityByIdAsync(LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity
            {
                Id = LedgerActivityId, ProjectId = LedgerProjectId, DivisionId = activityDivisionId,
            });

        _aipRepo.Setup(r => r.GetProjectByIdAsync(LedgerProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = LedgerProjectId, ProgramId = LedgerProgramId });

        _aipRepo.Setup(r => r.GetProgramByIdAsync(LedgerProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram
            {
                Id = LedgerProgramId, OfficeId = AipOfficeId, RefCode = ProgramRefCode,
            });

        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipOffice
            {
                Id = AipOfficeId, OfficeId = ConfigOfficeId,
                AipRecordId = AipRecordId, RefCode = OfficeRefCode,
            });

        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = fiscalYear });

        _officeRepo.Setup(r => r.GetByIdAsync(ConfigOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Office
            {
                Id = ConfigOfficeId, OfficeCode = isHostOffice ? "PPDO" : "GSO",
                IsHostOffice = isHostOffice,
            });
    }

    /// <summary>
    /// A guest office writes no ledger row — and, the assertion that actually separates the two
    /// shapes, <b>never consults <c>ProgramDivision</c> at all</b>.
    ///
    /// <para>
    /// Division is not a scoping axis for a guest office (<c>Permission_Matrix.md</c> §3.1,
    /// PPDO-4); it is not that they have none configured yet. If the service reached the assignment
    /// lookup and read its empty result as "no division", this shape would be one refactor away
    /// from being the misconfiguration below — which is exactly the collapse PPDO-57 removes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task UpsertLedger_GuestOffice_WritesNoRowAndNeverAsksAboutDivisions()
    {
        GivenActivityChain(isHostOffice: false);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _allocationRepo.Verify(r => r.FindProgramDivisionsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // No synthetic division row, and no expenditure read either — there is nothing to reserve.
        _ledgerRepo.VerifyNoOtherCalls();
        _expRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A host-office program that no <c>ProgramDivision</c> row claims reaches the same outcome —
    /// no row — down a different path, and it <b>is</b> a misconfiguration: the activity reserves
    /// nothing against any division allocation. The lookup having happened is what distinguishes it
    /// from the guest office above, and it is logged rather than shared across every division
    /// (which would make each division's figures overlap).
    /// </summary>
    [Fact]
    public async Task UpsertLedger_Fy2027_HostOfficeProgramUnassigned_ConsultsTheTableAndStillWritesNothing()
    {
        GivenActivityChain(isHostOffice: true, fiscalYear: 2027);
        _allocationRepo.Setup(r => r.FindProgramDivisionsAsync(
                OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProgramDivision>());

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _allocationRepo.Verify(r => r.FindProgramDivisionsAsync(
            OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()), Times.Once);
        _ledgerRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// FY ≤ 2027: an assigned host-office program still writes its reservation row against the
    /// division (PPDO-57), through <c>ProgramDivision</c> — unchanged by PPDO-150.
    /// </summary>
    [Fact]
    public async Task UpsertLedger_Fy2027_HostOfficeProgramAssignedToADivision_WritesTheRow()
    {
        GivenActivityChain(isHostOffice: true, fiscalYear: 2027);
        _allocationRepo.Setup(r => r.FindProgramDivisionsAsync(
                OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProgramDivision>
            {
                new() { Id = 1, OfficeRefCode = OfficeRefCode, ProgramRefCode = ProgramRefCode, DivisionId = DivisionId },
            });

        GivenLedgerWritePath(DivisionId, 2027);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        Assert.NotNull(_added);
        AipDivisionAllocationLedger row = _added!;
        Assert.Equal(DivisionId,      row.DivisionId);
        Assert.Equal(GeneralFundId,   row.FundingSourceId);
        Assert.Equal(LedgerActivityId, row.AipActivityId);
        // ⚠️ The ledger reserves RAW pesos, not the rounded-up figure the ceiling check sums. The
        // rounding is a property of the printed form, not of the reservation.
        Assert.Equal(500_000m, row.ReservedAmount);
        Assert.Equal(900_000m, row.AllocatedAmountSnapshot);
    }

    // ── PPDO-150: FY2028+ follows the activity's own tag ──────────────────────

    private AipDivisionAllocationLedger? _added;

    /// <summary>
    /// Everything the write path touches once attribution has picked <paramref name="division"/>:
    /// one ₱300K MOOE + ₱200K CO General Fund line, no prior row, a ₱900K allocation. The row
    /// written lands in <see cref="_added"/>.
    /// </summary>
    private void GivenLedgerWritePath(int division, int fiscalYear)
    {
        _added = null;
        _expRepo.Setup(r => r.GetByActivityIdAsync(LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipExpenditure>
            {
                new() { Id = 1, ActivityId = LedgerActivityId, FundingSourceId = GeneralFundId, Mooe = 300_000m, Co = 200_000m },
            });
        _ledgerRepo.Setup(r => r.DeleteForActivityOutsideDivisionAsync(
                LedgerActivityId, division, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _ledgerRepo.Setup(r => r.GetFundingSourceIdsForActivityAsync(
                LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>());
        _ledgerRepo.Setup(r => r.FindAsync(
                division, fiscalYear, GeneralFundId, LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipDivisionAllocationLedger?)null);
        _allocation.Setup(a => a.GetAllocationsAsync(
                ConfigOfficeId, fiscalYear, GeneralFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DivisionAllocationDto>
            {
                new(1, division, "Planning", fiscalYear, GeneralFundId, "GF", "General Fund", 900_000m),
            });
        _ledgerRepo.Setup(r => r.AddAsync(It.IsAny<AipDivisionAllocationLedger>(), It.IsAny<CancellationToken>()))
            .Callback<AipDivisionAllocationLedger, CancellationToken>((l, _) => _added = l)
            .Returns(Task.CompletedTask);
        _ledgerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    /// <summary>
    /// ⚠️ The tag wins, and <c>ProgramDivision</c> is <b>never consulted</b> for an entered year —
    /// the strict mock has no setup for it, so asking would throw. That is what stops a shared
    /// program's lowest-id division from quietly taking the reservation again.
    /// </summary>
    [Fact]
    public async Task UpsertLedger_Fy2028_TaggedActivity_PostsToItsTag_WithoutAskingProgramDivision()
    {
        const int tag = 6;
        GivenActivityChain(isHostOffice: true, activityDivisionId: tag);
        GivenLedgerWritePath(tag, FiscalYear);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        Assert.NotNull(_added);
        Assert.Equal(tag, _added!.DivisionId);
        Assert.Equal(500_000m, _added.ReservedAmount);
        _allocationRepo.VerifyNoOtherCalls();
    }

    /// <summary>A re-tag moves the reservation: the rows it left under any other division go.</summary>
    [Fact]
    public async Task UpsertLedger_Fy2028_ClearsTheActivitysRowsUnderAnyOtherDivision()
    {
        const int tag = 6;
        GivenActivityChain(isHostOffice: true, activityDivisionId: tag);
        GivenLedgerWritePath(tag, FiscalYear);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _ledgerRepo.Verify(r => r.DeleteForActivityOutsideDivisionAsync(
            LedgerActivityId, tag, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// An untagged FY2028+ host activity reserves nothing — and does not fall back to
    /// <c>ProgramDivision</c> either (decision: untagged work sits on the dashboard's "No division"
    /// row until its department head tags it).
    /// </summary>
    [Fact]
    public async Task UpsertLedger_Fy2028_UntaggedActivity_WritesNothing_AndDoesNotFallBack()
    {
        GivenActivityChain(isHostOffice: true, activityDivisionId: null);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _ledgerRepo.VerifyNoOtherCalls();
        _allocationRepo.VerifyNoOtherCalls();
        _expRepo.VerifyNoOtherCalls();
    }

    /// <summary>Guest offices still write no ledger row, tagged or not (decision: host office only).</summary>
    [Fact]
    public async Task UpsertLedger_Fy2028_GuestOffice_TaggedActivity_StillWritesNothing()
    {
        GivenActivityChain(isHostOffice: false, activityDivisionId: 6);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _ledgerRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The ceiling check itself is office-level for <b>both</b> shapes — it consults no division
    /// table for either. This is the half of V18-47 that PPDO-56 already satisfied, pinned so a
    /// later change cannot quietly route the check back through the division chain, where a guest
    /// office would read a missing allocation as a zero ceiling and be forbidden everything.
    /// </summary>
    [Fact]
    public async Task GetStatus_ForEitherOfficeShape_ReadsTheOfficeCeilingAndConsultsNoDivision()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(5_000_000m);
        GivenLines(GeneralFundId, (Mooe: 1_200_000m, Co: 0m));

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.True(status.CeilingSet);
        Assert.Equal(3_800_000m, status.Remaining);
        // Neither the division-assignment table nor the office table is touched: the ceiling is
        // keyed on the config office alone, which is what makes it work for an office with no
        // divisions at all.
        _allocationRepo.VerifyNoOtherCalls();
        _officeRepo.VerifyNoOtherCalls();
    }

    // ── Addressed by office + fiscal year, for PBO (V18-48 / PPDO-58) ─────────

    /// <summary>
    /// PBO asks by config office and fiscal year, because it sets ceilings for offices it does not
    /// belong to and has no <c>aipOfficeId</c> to hand. The answer is the same office-level figure
    /// <see cref="AipCeilingService.GetStatusAsync"/> gives.
    /// </summary>
    [Fact]
    public async Task GetStatusForOffice_ResolvesTheOfficeAndReturnsItsOfficeLevelPosition()
    {
        _aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = FiscalYear });
        _aipRepo.Setup(r => r.GetOfficesByAipIdNoTrackingAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipOffice>
            {
                new() { Id = 900, OfficeId = 99, AipRecordId = AipRecordId },          // another office
                new() { Id = AipOfficeId, OfficeId = ConfigOfficeId, AipRecordId = AipRecordId },
            });
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        GivenLines(GeneralFundId, (Mooe: 2_500_000m, Co: 0m));

        AipCeilingStatusDto? status =
            await Build().GetStatusForOfficeAsync(ConfigOfficeId, FiscalYear);

        Assert.NotNull(status);
        // ⚠️ Negative, and not clamped — this is the figure PBO must see after cutting a ceiling
        // below encoded work. A Math.Max(0, …) anywhere between here and the screen hides the only
        // signal that the cut created a problem (A5-b).
        Assert.Equal(-1_500_000m, status!.Remaining);
        Assert.False(status.WithinCeiling);
    }

    /// <summary>
    /// ⚠️ No AIP record for the year is <b>null</b>, never a zeroed status. PBO sets ceilings
    /// before offices encode anything, so this is an ordinary state — but reporting it as an
    /// encoded total of ₱0 would tell PBO the office had encoded nothing, which is a different and
    /// far more reassuring claim than "there is nothing to compare against yet".
    /// </summary>
    [Fact]
    public async Task GetStatusForOffice_WhenTheFiscalYearHasNoAipRecord_ReturnsNullNotAZeroedStatus()
    {
        _aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecord?)null);

        Assert.Null(await Build().GetStatusForOfficeAsync(ConfigOfficeId, FiscalYear));
    }

    /// <summary>Same rule when the record exists but this office holds no rows in it.</summary>
    [Fact]
    public async Task GetStatusForOffice_WhenTheOfficeHasNoRowsInThatRecord_ReturnsNull()
    {
        _aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = FiscalYear });
        _aipRepo.Setup(r => r.GetOfficesByAipIdNoTrackingAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipOffice>
            {
                new() { Id = 900, OfficeId = 99, AipRecordId = AipRecordId },
            });

        Assert.Null(await Build().GetStatusForOfficeAsync(ConfigOfficeId, FiscalYear));
    }

    // ── The rounding rule itself ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1_000)]
    [InlineData(999, 1_000)]
    [InlineData(1_000, 1_000)]        // exact multiples do not jump a thousand
    [InlineData(1_000.01, 2_000)]
    [InlineData(1_000_400, 1_001_000)] // the worked example in AIP_Form_Spec §6.1
    public void UpToThousand_RoundsUpAndLeavesExactMultiplesAlone(decimal pesos, decimal expected)
        => Assert.Equal(expected, AipRounding.UpToThousand(pesos));

    // ── v1.9.0 (PPDO-216): other funds flagged "Enforce AIP ceiling" ─────────────

    private const int PhilHealthId = 9;
    private const int OtherOfficeFundId = 12;

    /// <summary>
    /// The baseline every test below shares: the General Fund within its ceiling, so any refusal can
    /// only come from the other fund.
    /// </summary>
    private void GivenGeneralFundWithin()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(1_000_000m);
        GivenLines(GeneralFundId, (Mooe: 100_000m, Co: 0m));
    }

    [Fact]
    public async Task GetStatus_AnEnforcedFund_IsReportedWithItsOwnCeilingAndEncodedTotal()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", 500_000m);
        GivenLines(PhilHealthId, (Mooe: 400_200m, Co: 49_000m));   // rounds to 401,000 + 49,000

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        AipFundCeilingStatusDto ph = Assert.Single(status.OtherFunds);
        Assert.Equal(PhilHealthId, ph.FundingSourceId);
        Assert.Equal("TF-PH", ph.Code);
        Assert.True(ph.CeilingSet);
        Assert.Equal(500_000m, ph.Ceiling);
        Assert.Equal(450_000m, ph.EncodedBaseRounded);
        Assert.Equal(50_000m, ph.Remaining);
        Assert.True(ph.WithinCeiling);
        // The General Fund figures are untouched by the other fund.
        Assert.Equal(100_000m, status.EncodedBaseRounded);
    }

    /// <summary>A fund nobody flagged is not even queried (DECISION H stays withdrawn).</summary>
    [Fact]
    public async Task GetStatus_AnUnflaggedFund_IsNeverQueried()
    {
        GivenGeneralFundWithin();

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.Empty(status.OtherFunds);
        _expRepo.Verify(r => r.SumMooeCoByConfigOfficeAndFundAsync(
            AipRecordId, ConfigOfficeId, GadFundId, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>An enforced fund the office neither uses nor has a ceiling for is not listed at all.</summary>
    [Fact]
    public async Task GetStatus_AnEnforcedFundTheOfficeDoesNotUse_IsNotListed()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", null);
        GivenLines(PhilHealthId);

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.Empty(status.OtherFunds);
    }

    /// <summary>A ceiling set but nothing encoded is listed, and within (remaining = the ceiling).</summary>
    [Fact]
    public async Task GetStatus_ACeilingWithNothingEncoded_IsListedAndWithin()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", 300_000m);
        GivenLines(PhilHealthId);

        AipFundCeilingStatusDto ph = Assert.Single((await Build().GetStatusAsync(AipOfficeId)).OtherFunds);

        Assert.Equal(300_000m, ph.Remaining);
        Assert.True(ph.WithinCeiling);
    }

    /// <summary>Another office's own fund cannot hold this office's lines, so it is not checked here.</summary>
    [Fact]
    public async Task GetStatus_AnotherOfficesOwnFund_IsNotChecked()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(OtherOfficeFundId, "OWN-X", officeId: ConfigOfficeId + 1));

        AipCeilingStatusDto status = await Build().GetStatusAsync(AipOfficeId);

        Assert.Empty(status.OtherFunds);
        _expRepo.Verify(r => r.SumMooeCoByConfigOfficeAndFundAsync(
            AipRecordId, ConfigOfficeId, OtherOfficeFundId, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Follow-up B: this office's OWN fund can be enforced like a shared one.</summary>
    [Fact]
    public async Task GetStatus_ThisOfficesOwnEnforcedFund_IsChecked()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(OtherOfficeFundId, "OWN-7", officeId: ConfigOfficeId));
        GivenFundCeiling(OtherOfficeFundId, "OWN-7", 10_000m);
        GivenLines(OtherOfficeFundId, (Mooe: 5_000m, Co: 0m));

        Assert.Single((await Build().GetStatusAsync(AipOfficeId)).OtherFunds);
    }

    [Fact]
    public async Task ValidateForSubmit_OverAnEnforcedFund_IsRefusedNamingThatFund()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", 500_000m);
        GivenLines(PhilHealthId, (Mooe: 520_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.Equal(
            "TF-PH MOOE + CO totals ₱520,000.00, which is ₱20,000.00 over the ceiling of ₱500,000.00. "
            + "Reduce the encoded amounts or ask PPDO to raise the ceiling.",
            error);
    }

    /// <summary>Decision 3: unset is zero, and the message says the ceiling is missing, not "over".</summary>
    [Fact]
    public async Task ValidateForSubmit_EncodedOnAnEnforcedFundWithNoCeiling_SaysTheCeilingIsMissing()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", null);
        GivenLines(PhilHealthId, (Mooe: 10_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.NotNull(error);
        Assert.StartsWith("There is no TF-PH ceiling set for this office for the fiscal year", error);
    }

    /// <summary>Follow-up A: PS is exempt on every fund — the repository is asked for MOOE and CO only.</summary>
    [Fact]
    public async Task ValidateForSubmit_AnEnforcedFundExactlyAtItsCeilingOnMooeAndCo_Passes()
    {
        GivenGeneralFundWithin();
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", 500_000m);
        GivenLines(PhilHealthId, (Mooe: 300_000m, Co: 200_000m));

        Assert.Null(await Build().ValidateForSubmitAsync(AipOfficeId));
    }

    /// <summary>Decision 5: every failing fund in one refusal, General Fund first.</summary>
    [Fact]
    public async Task ValidateForSubmit_GeneralFundAndAnEnforcedFundBothOver_NamesBothGeneralFundFirst()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(100_000m);
        GivenLines(GeneralFundId, (Mooe: 150_000m, Co: 0m));
        GivenEnforcedFunds(Fund(PhilHealthId, "TF-PH"));
        GivenFundCeiling(PhilHealthId, "TF-PH", 500_000m);
        GivenLines(PhilHealthId, (Mooe: 520_000m, Co: 0m));

        string? error = await Build().ValidateForSubmitAsync(AipOfficeId);

        Assert.NotNull(error);
        int gf = error!.IndexOf("General Fund MOOE + CO", StringComparison.Ordinal);
        int ph = error.IndexOf("TF-PH MOOE + CO", StringComparison.Ordinal);
        Assert.True(gf >= 0 && ph > gf, error);
    }

    /// <summary>The General Fund's own messages are unchanged when no other fund fails.</summary>
    [Fact]
    public async Task ValidateForSubmit_OnlyTheGeneralFundOver_KeepsTheGeneralFundMessageExactly()
    {
        GivenOffice();
        GivenGeneralFund();
        GivenCeiling(100_000m);
        GivenLines(GeneralFundId, (Mooe: 150_000m, Co: 0m));

        Assert.Equal(
            "General Fund MOOE + CO totals ₱150,000.00, which is ₱50,000.00 over the ceiling of ₱100,000.00. "
            + "Reduce the encoded amounts or ask PPDO to raise the ceiling.",
            await Build().ValidateForSubmitAsync(AipOfficeId));
    }
}
