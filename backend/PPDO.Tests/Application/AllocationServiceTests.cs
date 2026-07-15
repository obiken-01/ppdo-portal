using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AllocationService"/> (RAL-99).
/// Covers ceiling upsert, Σ≤ceiling allocation rule, program-division assignment,
/// setup-status gate, and supplemental-AIP carry-forward (D6).
/// All repositories are mocked.
/// </summary>
public sealed class AllocationServiceTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    // GF is the well-known General Fund id every fixture defaults to, matching this suite's
    // pre-v1.4.3 single-fund behavior; GadFundId is used by the fund-independence tests.
    private const int GfFundId  = 1;
    private const int GadFundId = 2;

    private static Office MakeOffice(int id, string code = "PPDO", string? refCode = "01-010") =>
        new() { Id = id, OfficeCode = code, OfficeName = code, OfficeRefCode = refCode,
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static Division MakeDivision(int id, int officeId) =>
        new() { Id = id, OfficeId = officeId, Name = $"Division {id}",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static FundingSource MakeFundingSource(int id, string code, string name) =>
        new() { Id = id, Code = code, Name = name, IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static BudgetCeiling MakeCeiling(int officeId, int fy, decimal amount, int fundingSourceId = GfFundId) =>
        new() { Id = 1, OfficeId = officeId, FiscalYear = fy, FundingSourceId = fundingSourceId, Amount = amount };

    private static DivisionAllocation MakeAllocation(
        int id, int divId, int fy, decimal amount, int fundingSourceId = GfFundId) =>
        new() { Id = id, DivisionId = divId, FiscalYear = fy, FundingSourceId = fundingSourceId, Amount = amount };

    private static AipRecord MakeAipRecord(int id, int fy) =>
        new() { Id = id, FiscalYear = fy, EntrySource = "Upload", Status = "Draft",
                UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow };

    private static AipOffice MakeAipOffice(int id, int aipId, string refCode) =>
        new() { Id = id, AipRecordId = aipId, RefCode = refCode, Name = "Office", Sector = "Sector A" };

    private static AipProgram MakeAipProgram(int id, int officeId, string refCode) =>
        new() { Id = id, OfficeId = officeId, RefCode = refCode, Name = $"Program {refCode}" };

    private static ProgramDivision MakePD(int id, string offRef, string progRef, int divId) =>
        new() { Id = id, OfficeRefCode = offRef, ProgramRefCode = progRef, DivisionId = divId };

    // ── Build factory ─────────────────────────────────────────────────────────

    private static (
        AllocationService                    sut,
        Mock<IRepository<BudgetCeiling>>     ceilingRepo,
        Mock<IRepository<DivisionAllocation>> allocRepo,
        Mock<IAllocationRepository>          pdRepo,
        Mock<IRepository<Division>>          divRepo,
        Mock<IRepository<Office>>            officeRepo,
        Mock<IAipRepository>                 aipRepo,
        Mock<IAuditService>                  audit)
        Build(
            List<BudgetCeiling>?      ceilings       = null,
            List<DivisionAllocation>? allocations    = null,
            List<ProgramDivision>?    pds            = null,
            List<Division>?           divisions      = null,
            List<Office>?             offices        = null,
            List<AipRecord>?          aipRecords     = null,
            List<AipOffice>?          aipOffices     = null,
            List<AipProgram>?         aipPrograms    = null,
            List<FundingSource>?      fundingSources = null)
    {
        List<BudgetCeiling>      ceilingList  = ceilings    ?? [];
        List<DivisionAllocation> allocList    = allocations ?? [];
        List<ProgramDivision>    pdList       = pds         ?? [];
        List<Division>           divList      = divisions   ?? [];
        List<Office>             officeList   = offices     ?? [];
        List<AipRecord>          aipRecList   = aipRecords  ?? [];
        List<AipOffice>          aipOffList   = aipOffices  ?? [];
        List<AipProgram>         aipProgList  = aipPrograms ?? [];
        // Every fixture defaults to a GF row so GetGeneralFundIdAsync resolves — matches this
        // suite's pre-v1.4.3 implicit single-fund (GF-equivalent) behavior.
        List<FundingSource>      fundList     = fundingSources ?? [MakeFundingSource(GfFundId, "GF", "General Fund")];

        Mock<IRepository<BudgetCeiling>>      ceilingRepo = new();
        Mock<IRepository<DivisionAllocation>> allocRepo   = new();
        Mock<IAllocationRepository>           pdRepo      = new();
        Mock<IRepository<Division>>           divRepo     = new();
        Mock<IRepository<Office>>             officeRepo  = new();
        Mock<IRepository<FundingSource>>      fundingSourceRepo = new();
        Mock<IAipRepository>                  aipRepo     = new();
        Mock<IAuditService>                   audit       = new();

        fundingSourceRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fundList);

        // BudgetCeiling repo
        int nextCeilingId = 10;
        ceilingRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ceilingList);
        ceilingRepo.Setup(r => r.AddAsync(It.IsAny<BudgetCeiling>(), It.IsAny<CancellationToken>()))
            .Callback<BudgetCeiling, CancellationToken>((e, _) => { e.Id = nextCeilingId++; ceilingList.Add(e); })
            .Returns(Task.CompletedTask);
        ceilingRepo.Setup(r => r.UpdateAsync(It.IsAny<BudgetCeiling>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ceilingRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // DivisionAllocation repo
        int nextAllocId = 20;
        allocRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(allocList);
        allocRepo.Setup(r => r.AddAsync(It.IsAny<DivisionAllocation>(), It.IsAny<CancellationToken>()))
            .Callback<DivisionAllocation, CancellationToken>((e, _) => { e.Id = nextAllocId++; allocList.Add(e); })
            .Returns(Task.CompletedTask);
        allocRepo.Setup(r => r.UpdateAsync(It.IsAny<DivisionAllocation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        allocRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ProgramDivision repo (IAllocationRepository)
        int nextPdId = 30;
        pdRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(pdList);
        pdRepo.Setup(r => r.AddAsync(It.IsAny<ProgramDivision>(), It.IsAny<CancellationToken>()))
            .Callback<ProgramDivision, CancellationToken>((e, _) => { e.Id = nextPdId++; pdList.Add(e); })
            .Returns(Task.CompletedTask);
        pdRepo.Setup(r => r.DeleteAsync(It.IsAny<ProgramDivision>(), It.IsAny<CancellationToken>()))
            .Callback<ProgramDivision, CancellationToken>((e, _) => pdList.Remove(e))
            .Returns(Task.CompletedTask);
        pdRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        pdRepo.Setup(r => r.FindProgramDivisionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string oRef, string pRef, CancellationToken _) =>
                (IReadOnlyList<ProgramDivision>)pdList
                    .Where(pd => pd.OfficeRefCode == oRef && pd.ProgramRefCode == pRef).ToList());

        pdRepo.Setup(r => r.GetProgramDivisionsByOfficeRefCodesAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> refs, CancellationToken _) =>
                (IReadOnlyList<ProgramDivision>)pdList
                    .Where(pd => refs.Contains(pd.OfficeRefCode)).ToList());

        // Division repo
        divRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(divList);

        // Office repo
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(officeList);

        // AIP repo
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aipRecList);
        aipRepo.Setup(r => r.GetOfficesByAipIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                (IReadOnlyList<AipOffice>)aipOffList.Where(o => o.AipRecordId == id).ToList());
        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipProgram>)aipProgList.Where(p => ids.Contains(p.OfficeId)).ToList());

        // Audit — allow any call
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CallerContext caller = new();
        caller.SetUserId(Guid.NewGuid());

        AllocationService sut = new(
            ceilingRepo.Object, allocRepo.Object, pdRepo.Object,
            divRepo.Object, officeRepo.Object, fundingSourceRepo.Object, aipRepo.Object,
            audit.Object, caller);

        return (sut, ceilingRepo, allocRepo, pdRepo, divRepo, officeRepo, aipRepo, audit);
    }

    // ── Ceiling tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertCeiling_CreatesNewRow_WhenNoneExists()
    {
        (AllocationService sut, Mock<IRepository<BudgetCeiling>> ceilingRepo, _, _, _, _, _, _) =
            Build(offices: [MakeOffice(1)]);

        ServiceResult<BudgetCeilingDto> result = await sut.UpsertCeilingAsync(1, 2027, GfFundId, 1_000_000m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000_000m, result.Value!.Amount);
        Assert.Equal(1, result.Value.OfficeId);
        Assert.Equal(2027, result.Value.FiscalYear);
        ceilingRepo.Verify(r => r.AddAsync(It.IsAny<BudgetCeiling>(), It.IsAny<CancellationToken>()), Times.Once);
        ceilingRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertCeiling_UpdatesExistingRow_WhenAlreadyExists()
    {
        BudgetCeiling existing = MakeCeiling(1, 2027, 500_000m);
        (AllocationService sut, Mock<IRepository<BudgetCeiling>> ceilingRepo, _, _, _, _, _, _) =
            Build(ceilings: [existing], offices: [MakeOffice(1)]);

        ServiceResult<BudgetCeilingDto> result = await sut.UpsertCeilingAsync(1, 2027, GfFundId, 1_000_000m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000_000m, result.Value!.Amount);
        ceilingRepo.Verify(r => r.AddAsync(It.IsAny<BudgetCeiling>(), It.IsAny<CancellationToken>()), Times.Never);
        ceilingRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCeiling_ReturnsNotFound_WhenNoneExists()
    {
        (AllocationService sut, _, _, _, _, _, _, _) = Build(offices: [MakeOffice(1)]);

        ServiceResult<BudgetCeilingDto> result = await sut.GetCeilingAsync(1, 2027, GfFundId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Division Allocation tests ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertAllocations_RejectsBadRequest_WhenNoCeilingExists()
    {
        Division div1 = MakeDivision(1, 1);
        (AllocationService sut, _, _, _, _, _, _, _) =
            Build(divisions: [div1], offices: [MakeOffice(1)]);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GfFundId,
                [new UpsertDivisionAllocationDto(1, 500_000m)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("ceiling", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertAllocations_RejectsWhenSumExceedsCeiling()
    {
        Division div1 = MakeDivision(1, 1);
        Division div2 = MakeDivision(2, 1);
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        (AllocationService sut, _, _, _, _, _, _, _) =
            Build(ceilings: [ceiling], divisions: [div1, div2], offices: [MakeOffice(1)]);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GfFundId,
                [new(1, 700_000m), new(2, 600_000m)]);   // 1_300_000 > 1_000_000

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("exceeds ceiling", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertAllocations_AcceptsWhenSumEqualsCeiling()
    {
        Division div1 = MakeDivision(1, 1);
        Division div2 = MakeDivision(2, 1);
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        (AllocationService sut, _, _, _, _, _, _, _) =
            Build(ceilings: [ceiling], divisions: [div1, div2], offices: [MakeOffice(1)]);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GfFundId,
                [new(1, 600_000m), new(2, 400_000m)]);   // exactly 1_000_000

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task UpsertAllocations_AcceptsPartialAllocation()
    {
        Division div1 = MakeDivision(1, 1);
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        (AllocationService sut, _, Mock<IRepository<DivisionAllocation>> allocRepo, _, _, _, _, _) =
            Build(ceilings: [ceiling], divisions: [div1], offices: [MakeOffice(1)]);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GfFundId,
                [new(1, 300_000m)]);   // 300_000 < 1_000_000

        Assert.True(result.IsSuccess);
        allocRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fund-scoping tests (v1.4.3 — RAL-154) ─────────────────────────────────

    [Fact]
    public async Task UpsertAllocations_GadCeiling_IsIndependentFromGfCeiling()
    {
        Division div1 = MakeDivision(1, 1);
        BudgetCeiling gfCeiling  = MakeCeiling(1, 2027, 1_000_000m, GfFundId);
        BudgetCeiling gadCeiling = MakeCeiling(1, 2027, 200_000m, GadFundId);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [gfCeiling, gadCeiling], divisions: [div1], offices: [MakeOffice(1)],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        // GAD allocation of 200_000 is within the GAD ceiling (200_000) — should succeed even
        // though it would blow through nothing GF-related, since the two funds are independent.
        ServiceResult<IReadOnlyList<DivisionAllocationDto>> gadResult =
            await sut.UpsertAllocationsAsync(1, 2027, GadFundId, [new(1, 200_000m)]);
        Assert.True(gadResult.IsSuccess);

        // GF allocation of 900_000 is within the GF ceiling (1_000_000) — independently succeeds.
        ServiceResult<IReadOnlyList<DivisionAllocationDto>> gfResult =
            await sut.UpsertAllocationsAsync(1, 2027, GfFundId, [new(1, 900_000m)]);
        Assert.True(gfResult.IsSuccess);
    }

    [Fact]
    public async Task UpsertAllocations_GadSumExceedsGadCeiling_RejectedIndependentlyOfGf()
    {
        Division div1 = MakeDivision(1, 1);
        BudgetCeiling gfCeiling  = MakeCeiling(1, 2027, 1_000_000m, GfFundId);
        BudgetCeiling gadCeiling = MakeCeiling(1, 2027, 200_000m, GadFundId);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [gfCeiling, gadCeiling], divisions: [div1], offices: [MakeOffice(1)],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        // 250_000 > the GAD ceiling of 200_000 — rejected, even though it's well within GF's
        // much larger 1_000_000 ceiling, proving the guard checks the GAD ceiling specifically.
        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GadFundId, [new(1, 250_000m)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("exceeds ceiling", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertAllocations_NoGadCeiling_RejectedEvenThoughGfCeilingExists()
    {
        Division div1 = MakeDivision(1, 1);
        BudgetCeiling gfCeiling = MakeCeiling(1, 2027, 1_000_000m, GfFundId);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [gfCeiling], divisions: [div1], offices: [MakeOffice(1)],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await sut.UpsertAllocationsAsync(1, 2027, GadFundId, [new(1, 50_000m)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("ceiling", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCeiling_ReturnsFundingSourceCodeAndName()
    {
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 500_000m, GadFundId);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceiling], offices: [MakeOffice(1)],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        ServiceResult<BudgetCeilingDto> result = await sut.GetCeilingAsync(1, 2027, GadFundId);

        Assert.True(result.IsSuccess);
        Assert.Equal("GAD", result.Value!.FundingSourceCode);
        Assert.Equal("5% GAD Fund", result.Value.FundingSourceName);
    }

    [Fact]
    public async Task GetCeilings_ReturnsOneRowPerFundingSource()
    {
        BudgetCeiling gfCeiling  = MakeCeiling(1, 2027, 1_000_000m, GfFundId);
        BudgetCeiling gadCeiling = MakeCeiling(1, 2027, 200_000m, GadFundId);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [gfCeiling, gadCeiling], offices: [MakeOffice(1)],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        IReadOnlyList<BudgetCeilingDto> result = await sut.GetCeilingsAsync(1, 2027);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.FundingSourceId == GfFundId && c.Amount == 1_000_000m);
        Assert.Contains(result, c => c.FundingSourceId == GadFundId && c.Amount == 200_000m);
    }

    // ── Program assignment tests ──────────────────────────────────────────────

    [Fact]
    public async Task UpsertProgramAssignment_SetsTwoDivisions()
    {
        Division div1 = MakeDivision(1, 1);
        Division div2 = MakeDivision(2, 1);
        (AllocationService sut, _, _, Mock<IAllocationRepository> pdRepo, _, _, _, _) =
            Build(divisions: [div1, div2]);

        ServiceResult<ProgramAssignmentDto> result = await sut.UpsertProgramAssignmentAsync(
            new("I-PPDO-01-010-01", "I-PPDO-01-010-01-001", [1, 2]));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.DivisionIds.Count);
        pdRepo.Verify(r => r.AddAsync(It.IsAny<ProgramDivision>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        pdRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertProgramAssignment_ReplacesDivisions()
    {
        // Pre-existing: div1 and div2. Replace with div2 and div3.
        Division div1 = MakeDivision(1, 1);
        Division div2 = MakeDivision(2, 1);
        Division div3 = MakeDivision(3, 1);
        ProgramDivision pd1 = MakePD(1, "OFF-01", "PRG-01", 1);
        ProgramDivision pd2 = MakePD(2, "OFF-01", "PRG-01", 2);

        (AllocationService sut, _, _, Mock<IAllocationRepository> pdRepo, _, _, _, _) =
            Build(pds: [pd1, pd2], divisions: [div1, div2, div3]);

        ServiceResult<ProgramAssignmentDto> result = await sut.UpsertProgramAssignmentAsync(
            new("OFF-01", "PRG-01", [2, 3]));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.DivisionIds.Count);
        Assert.Contains(2, result.Value.DivisionIds);
        Assert.Contains(3, result.Value.DivisionIds);
        // div1 removed, div3 added
        pdRepo.Verify(r => r.DeleteAsync(pd1, It.IsAny<CancellationToken>()), Times.Once);
        pdRepo.Verify(r => r.AddAsync(
            It.Is<ProgramDivision>(pd => pd.DivisionId == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertProgramAssignment_ClearsAll_WhenEmptyList()
    {
        ProgramDivision pd1 = MakePD(1, "OFF-01", "PRG-01", 1);
        ProgramDivision pd2 = MakePD(2, "OFF-01", "PRG-01", 2);

        (AllocationService sut, _, _, Mock<IAllocationRepository> pdRepo, _, _, _, _) =
            Build(pds: [pd1, pd2]);

        ServiceResult<ProgramAssignmentDto> result = await sut.UpsertProgramAssignmentAsync(
            new("OFF-01", "PRG-01", []));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.DivisionIds);
        pdRepo.Verify(r => r.DeleteAsync(It.IsAny<ProgramDivision>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        pdRepo.Verify(r => r.AddAsync(It.IsAny<ProgramDivision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Setup status tests ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSetupStatus_AllTrue_WhenFullyConfigured()
    {
        // AipOffice.RefCode must END WITH Office.OfficeRefCode (suffix match).
        const string offRefCode = "I-PPDO-01-010";      // ends with "01-010"
        const string progRefCode = "I-PPDO-01-010-001";
        Office office      = MakeOffice(1, refCode: "01-010");
        Division div       = MakeDivision(1, 1);
        BudgetCeiling ceil = MakeCeiling(1, 2027, 1_000_000m);
        DivisionAllocation alloc = MakeAllocation(1, 1, 2027, 500_000m);
        AipRecord rec     = MakeAipRecord(1, 2027);
        AipOffice aipOff  = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog   = MakeAipProgram(1, 1, progRefCode);
        ProgramDivision pd = MakePD(1, offRefCode, progRefCode, 1);

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceil], allocations: [alloc], pds: [pd],
            divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog]);

        AllocationSetupStatusDto status = await sut.GetSetupStatusAsync(1, 2027, 1);

        Assert.True(status.HasCeiling);
        Assert.True(status.HasAllocation);
        Assert.True(status.HasProgramAssignment);
    }

    [Fact]
    public async Task GetSetupStatus_OnlyGeneralFundGates_OtherFundCeilingAloneIsNotEnough()
    {
        // A GAD ceiling+allocation exists, but NO General Fund ceiling/allocation — the gate
        // must stay closed, since only GF is mandatory to unlock WFP entry (§2 D7).
        const string offRefCode  = "I-PPDO-01-010";
        const string progRefCode = "I-PPDO-01-010-001";
        Office office = MakeOffice(1, refCode: "01-010");
        Division div  = MakeDivision(1, 1);
        BudgetCeiling gadCeiling = MakeCeiling(1, 2027, 200_000m, GadFundId);
        DivisionAllocation gadAlloc = MakeAllocation(1, 1, 2027, 100_000m, GadFundId);
        AipRecord rec     = MakeAipRecord(1, 2027);
        AipOffice aipOff  = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog   = MakeAipProgram(1, 1, progRefCode);
        ProgramDivision pd = MakePD(1, offRefCode, progRefCode, 1);

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [gadCeiling], allocations: [gadAlloc], pds: [pd],
            divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog],
            fundingSources: [
                MakeFundingSource(GfFundId, "GF", "General Fund"),
                MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);

        AllocationSetupStatusDto status = await sut.GetSetupStatusAsync(1, 2027, 1);

        Assert.False(status.HasCeiling);
        Assert.False(status.HasAllocation);
        Assert.True(status.HasProgramAssignment);   // unrelated to fund scoping
    }

    [Fact]
    public async Task GetSetupStatus_HasCeilingFalse_WhenNoCeiling()
    {
        Office office    = MakeOffice(1, refCode: "01-010");
        Division div     = MakeDivision(1, 1);
        DivisionAllocation alloc = MakeAllocation(1, 1, 2027, 500_000m);

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            allocations: [alloc], divisions: [div], offices: [office]);

        AllocationSetupStatusDto status = await sut.GetSetupStatusAsync(1, 2027, 1);

        Assert.False(status.HasCeiling);
    }

    [Fact]
    public async Task GetSetupStatus_HasProgramFalse_WhenNoProgramAssigned()
    {
        const string offRefCode = "I-PPDO-01-010";      // ends with "01-010"
        Office office      = MakeOffice(1, refCode: "01-010");
        Division div       = MakeDivision(1, 1);
        BudgetCeiling ceil = MakeCeiling(1, 2027, 1_000_000m);
        DivisionAllocation alloc = MakeAllocation(1, 1, 2027, 500_000m);
        AipRecord rec     = MakeAipRecord(1, 2027);
        AipOffice aipOff  = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog   = MakeAipProgram(1, 1, "I-PPDO-01-010-001");
        // No ProgramDivision row → unassigned

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceil], allocations: [alloc],
            divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog]);

        AllocationSetupStatusDto status = await sut.GetSetupStatusAsync(1, 2027, 1);

        Assert.False(status.HasProgramAssignment);
    }

    // ── Supplemental AIP carry-forward (D6) ──────────────────────────────────

    [Fact]
    public async Task GetProgramAssignments_PreservesExistingAssignments_AfterSupplementalUpload()
    {
        // AipOffice.RefCode must END WITH Office.OfficeRefCode (suffix match).
        const string offRefCode  = "I-PPDO-01-010";
        const string progRefCode = "I-PPDO-01-010-001";
        Office office = MakeOffice(1, refCode: "01-010");
        Division div  = MakeDivision(1, 1);
        ProgramDivision pd = MakePD(1, offRefCode, progRefCode, 1);

        // Simulate supplemental upload: same ref codes but NEW surrogate IDs (Id=999 vs old Id=1).
        AipRecord rec    = MakeAipRecord(1, 2027);
        AipOffice aipOff = MakeAipOffice(7, 1, offRefCode);       // new Id=7
        AipProgram prog  = MakeAipProgram(99, 7, progRefCode);    // new Id=99

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            pds: [pd], divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog]);

        IReadOnlyList<ProgramAssignmentDto> result = await sut.GetProgramAssignmentsAsync(1, 2027);

        // The existing assignment (div 1) still appears despite new surrogate IDs.
        Assert.Single(result);
        Assert.Contains(1, result[0].DivisionIds);
        Assert.Equal(progRefCode, result[0].ProgramRefCode);
    }

    [Fact]
    public async Task GetProgramAssignments_ShowsNewProgramAsUnassigned_AfterSupplementalUpload()
    {
        const string offRefCode     = "I-PPDO-01-010";
        const string existingProg   = "I-PPDO-01-010-001";
        const string newProg        = "I-PPDO-01-010-002";   // new program, no assignment
        Office office = MakeOffice(1, refCode: "01-010");
        Division div  = MakeDivision(1, 1);
        ProgramDivision pd = MakePD(1, offRefCode, existingProg, 1);

        AipRecord rec    = MakeAipRecord(1, 2027);
        AipOffice aipOff = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog1 = MakeAipProgram(1, 1, existingProg);
        AipProgram prog2 = MakeAipProgram(2, 1, newProg);   // newly added in supplemental

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            pds: [pd], divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog1, prog2]);

        IReadOnlyList<ProgramAssignmentDto> result = await sut.GetProgramAssignmentsAsync(1, 2027);

        Assert.Equal(2, result.Count);
        ProgramAssignmentDto existing = result.First(r => r.ProgramRefCode == existingProg);
        ProgramAssignmentDto @new     = result.First(r => r.ProgramRefCode == newProg);
        Assert.NotEmpty(existing.DivisionIds);
        Assert.Empty(@new.DivisionIds);   // no assignment → unassigned
    }

    // ── Setup overview tests (RAL-60 — "All Offices" dashboard view) ──────────

    [Fact]
    public async Task GetSetupOverview_NoActiveOffices_ReturnsAllZero()
    {
        (AllocationService sut, _, _, _, _, _, _, _) = Build();

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(0, result.TotalOffices);
        Assert.Equal(0, result.FullySetupCount);
        Assert.Equal(0, result.IncompleteCount);
        Assert.Equal(0, result.NotStartedCount);
    }

    [Fact]
    public async Task GetSetupOverview_OfficeWithNothing_CountsAsNotStarted()
    {
        Office office = MakeOffice(1, refCode: "01-010");
        (AllocationService sut, _, _, _, _, _, _, _) = Build(offices: [office]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.TotalOffices);
        Assert.Equal(1, result.NotStartedCount);
        Assert.Equal(0, result.FullySetupCount);
        Assert.Equal(0, result.IncompleteCount);
    }

    [Fact]
    public async Task GetSetupOverview_OfficeWithCeilingOnly_CountsAsIncomplete()
    {
        Office office = MakeOffice(1, refCode: "01-010");
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(ceilings: [ceiling], offices: [office]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.IncompleteCount);
        Assert.Equal(0, result.NotStartedCount);
        Assert.Equal(0, result.FullySetupCount);
    }

    [Fact]
    public async Task GetSetupOverview_OfficeWithCeilingAndAllocationButNoProgram_CountsAsIncomplete()
    {
        Office office = MakeOffice(1, refCode: "01-010");
        Division div = MakeDivision(1, 1);
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        DivisionAllocation alloc = MakeAllocation(1, 1, 2027, 500_000m);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceiling], allocations: [alloc], divisions: [div], offices: [office]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.IncompleteCount);
    }

    [Fact]
    public async Task GetSetupOverview_FullyConfiguredOffice_CountsAsFullySetup()
    {
        const string offRefCode = "I-PPDO-01-010";
        const string progRefCode = "I-PPDO-01-010-001";
        Office office = MakeOffice(1, refCode: "01-010");
        Division div = MakeDivision(1, 1);
        BudgetCeiling ceiling = MakeCeiling(1, 2027, 1_000_000m);
        DivisionAllocation alloc = MakeAllocation(1, 1, 2027, 500_000m);
        AipRecord rec = MakeAipRecord(1, 2027);
        AipOffice aipOff = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog = MakeAipProgram(1, 1, progRefCode);
        ProgramDivision pd = MakePD(1, offRefCode, progRefCode, 1);

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceiling], allocations: [alloc], pds: [pd],
            divisions: [div], offices: [office],
            aipRecords: [rec], aipOffices: [aipOff], aipPrograms: [prog]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.FullySetupCount);
        Assert.Equal(0, result.IncompleteCount);
        Assert.Equal(0, result.NotStartedCount);
    }

    [Fact]
    public async Task GetSetupOverview_InactiveOffice_ExcludedFromTotal()
    {
        Office active = MakeOffice(1, refCode: "01-010");
        Office inactive = MakeOffice(2, code: "OLD", refCode: "01-011");
        inactive.IsActive = false;
        (AllocationService sut, _, _, _, _, _, _, _) = Build(offices: [active, inactive]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.TotalOffices);
    }

    [Fact]
    public async Task GetSetupOverview_MixedOffices_BucketsEachCorrectly()
    {
        const string offRefCode = "I-PPDO-01-010";
        const string progRefCode = "I-PPDO-01-010-001";

        // Office 1 — fully set up.
        Office off1 = MakeOffice(1, code: "O1", refCode: "01-010");
        Division div1 = MakeDivision(1, 1);
        BudgetCeiling ceil1 = MakeCeiling(1, 2027, 1_000_000m);
        DivisionAllocation alloc1 = MakeAllocation(1, 1, 2027, 500_000m);
        AipRecord rec = MakeAipRecord(1, 2027);
        AipOffice aipOff1 = MakeAipOffice(1, 1, offRefCode);
        AipProgram prog1 = MakeAipProgram(1, 1, progRefCode);
        ProgramDivision pd1 = MakePD(1, offRefCode, progRefCode, 1);

        // Office 2 — ceiling only (incomplete).
        Office off2 = MakeOffice(2, code: "O2", refCode: "01-011");
        BudgetCeiling ceil2 = MakeCeiling(2, 2027, 200_000m);

        // Office 3 — nothing (not started).
        Office off3 = MakeOffice(3, code: "O3", refCode: "01-012");

        (AllocationService sut, _, _, _, _, _, _, _) = Build(
            ceilings: [ceil1, ceil2], allocations: [alloc1], pds: [pd1],
            divisions: [div1], offices: [off1, off2, off3],
            aipRecords: [rec], aipOffices: [aipOff1], aipPrograms: [prog1]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(3, result.TotalOffices);
        Assert.Equal(1, result.FullySetupCount);
        Assert.Equal(1, result.IncompleteCount);
        Assert.Equal(1, result.NotStartedCount);
    }

    [Fact]
    public async Task GetSetupOverview_DifferentFiscalYear_NotCounted()
    {
        // Ceiling exists but for a different FY — office should be "not started" for 2027.
        Office office = MakeOffice(1, refCode: "01-010");
        BudgetCeiling ceiling = MakeCeiling(1, 2026, 1_000_000m);
        (AllocationService sut, _, _, _, _, _, _, _) = Build(ceilings: [ceiling], offices: [office]);

        AllocationSetupOverviewDto result = await sut.GetSetupOverviewAsync(2027);

        Assert.Equal(1, result.NotStartedCount);
    }
}
