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
    }

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
    private void GivenActivityChain(bool isHostOffice)
    {
        _aipRepo.Setup(r => r.GetActivityByIdAsync(LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = LedgerActivityId, ProjectId = LedgerProjectId });

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
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = FiscalYear });

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
    public async Task UpsertLedger_HostOfficeProgramUnassigned_ConsultsTheTableAndStillWritesNothing()
    {
        GivenActivityChain(isHostOffice: true);
        _allocationRepo.Setup(r => r.FindProgramDivisionsAsync(
                OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProgramDivision>());

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        _allocationRepo.Verify(r => r.FindProgramDivisionsAsync(
            OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()), Times.Once);
        _ledgerRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// PPDO's division-level path is unchanged by PPDO-57 — an assigned host-office program still
    /// writes its reservation row against the division.
    /// </summary>
    [Fact]
    public async Task UpsertLedger_HostOfficeProgramAssignedToADivision_WritesTheRow()
    {
        GivenActivityChain(isHostOffice: true);
        _allocationRepo.Setup(r => r.FindProgramDivisionsAsync(
                OfficeRefCode, ProgramRefCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProgramDivision>
            {
                new() { Id = 1, OfficeRefCode = OfficeRefCode, ProgramRefCode = ProgramRefCode, DivisionId = DivisionId },
            });

        _expRepo.Setup(r => r.GetByActivityIdAsync(LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipExpenditure>
            {
                new() { Id = 1, ActivityId = LedgerActivityId, FundingSourceId = GeneralFundId, Mooe = 300_000m, Co = 200_000m },
            });

        _ledgerRepo.Setup(r => r.GetFundingSourceIdsForActivityAsync(
                LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>());
        _ledgerRepo.Setup(r => r.FindAsync(
                DivisionId, FiscalYear, GeneralFundId, LedgerActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipDivisionAllocationLedger?)null);

        _allocation.Setup(a => a.GetAllocationsAsync(
                ConfigOfficeId, FiscalYear, GeneralFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DivisionAllocationDto>
            {
                new(1, DivisionId, "Planning", FiscalYear, GeneralFundId, "GF", "General Fund", 900_000m),
            });

        AipDivisionAllocationLedger? added = null;
        _ledgerRepo.Setup(r => r.AddAsync(It.IsAny<AipDivisionAllocationLedger>(), It.IsAny<CancellationToken>()))
            .Callback<AipDivisionAllocationLedger, CancellationToken>((l, _) => added = l)
            .Returns(Task.CompletedTask);
        _ledgerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Build().UpsertLedgerForActivityAsync(LedgerActivityId);

        Assert.NotNull(added);
        Assert.Equal(DivisionId,      added!.DivisionId);
        Assert.Equal(GeneralFundId,   added.FundingSourceId);
        Assert.Equal(LedgerActivityId, added.AipActivityId);
        // ⚠️ The ledger reserves RAW pesos, not the rounded-up figure the ceiling check sums. The
        // rounding is a property of the printed form, not of the reservation.
        Assert.Equal(500_000m, added.ReservedAmount);
        Assert.Equal(900_000m, added.AllocatedAmountSnapshot);
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
}
