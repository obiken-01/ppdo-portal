using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Allocation service — budget ceiling, division allocations, PPA→division assignment,
/// and the WFP setup-gate query (v1.2 — RAL-99). Ceiling and division allocation gained a
/// funding-source dimension in v1.4.3 (RAL-154) — every ceiling/allocation row now belongs
/// to exactly one <see cref="FundingSource"/>, resolved by Code "GF" for the setup gate
/// (§2 D7 in docs/v1.4.3/v1.4.3_Requirements.md — only General Fund is mandatory to unlock
/// WFP entry; other funds are optional).
///
/// Amounts are in PESOS. AIP totals are in thousands — the ×1000 conversion lives in
/// the WFP page layer only and must never appear here.
///
/// Supplemental AIP carry-forward (D6): ProgramDivision rows are keyed by
/// (OfficeRefCode, ProgramRefCode) so they survive supplemental AIP re-uploads that
/// recreate aip_programs with new surrogate IDs.
/// </summary>
public sealed class AllocationService : IAllocationService
{
    private const string GeneralFundCode = "GF";

    private readonly IRepository<BudgetCeiling>      _ceilingRepo;
    private readonly IRepository<DivisionAllocation> _allocationRepo;
    private readonly IAllocationRepository           _pdRepo;
    private readonly IRepository<Division>           _divisionRepo;
    private readonly IRepository<Office>             _officeRepo;
    private readonly IRepository<FundingSource>      _fundingSourceRepo;
    private readonly IAipRepository                  _aipRepo;
    private readonly IAuditService                   _audit;
    private readonly CallerContext                   _caller;

    public AllocationService(
        IRepository<BudgetCeiling>      ceilingRepo,
        IRepository<DivisionAllocation> allocationRepo,
        IAllocationRepository           pdRepo,
        IRepository<Division>           divisionRepo,
        IRepository<Office>             officeRepo,
        IRepository<FundingSource>      fundingSourceRepo,
        IAipRepository                  aipRepo,
        IAuditService                   audit,
        CallerContext                   caller)
    {
        _ceilingRepo       = ceilingRepo;
        _allocationRepo    = allocationRepo;
        _pdRepo            = pdRepo;
        _divisionRepo      = divisionRepo;
        _officeRepo        = officeRepo;
        _fundingSourceRepo = fundingSourceRepo;
        _aipRepo           = aipRepo;
        _audit             = audit;
        _caller            = caller;
    }

    // ── Budget Ceiling ────────────────────────────────────────────────────────

    public async Task<ServiceResult<BudgetCeilingDto>> GetCeilingAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default)
    {
        BudgetCeiling? existing = await FindCeilingAsync(officeId, fiscalYear, fundingSourceId, ct);
        if (existing is null)
            return ServiceResult<BudgetCeilingDto>.NotFound(
                $"No budget ceiling set for office {officeId}, FY {fiscalYear}, funding source {fundingSourceId}.");

        Dictionary<int, FundingSource> fundsById = (await _fundingSourceRepo.GetAllAsync(ct))
            .ToDictionary(f => f.Id);
        return ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(existing, fundsById));
    }

    public async Task<IReadOnlyList<BudgetCeilingDto>> GetCeilingsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default)
    {
        Dictionary<int, FundingSource> fundsById = (await _fundingSourceRepo.GetAllAsync(ct))
            .ToDictionary(f => f.Id);
        IReadOnlyList<BudgetCeiling> all = await _ceilingRepo.GetAllAsync(ct);

        return all
            .Where(c => c.OfficeId == officeId && c.FiscalYear == fiscalYear)
            .Select(c => MapCeiling(c, fundsById))
            .ToList();
    }

    public async Task<ServiceResult<BudgetCeilingDto>> UpsertCeilingAsync(
        int officeId, int fiscalYear, int fundingSourceId, decimal amount, CancellationToken ct = default)
    {
        Dictionary<int, FundingSource> fundsById = (await _fundingSourceRepo.GetAllAsync(ct))
            .ToDictionary(f => f.Id);
        BudgetCeiling? existing = await FindCeilingAsync(officeId, fiscalYear, fundingSourceId, ct);

        if (existing is not null)
        {
            object oldSnap = new { existing.Amount };
            existing.Amount = amount;
            await _ceilingRepo.UpdateAsync(existing, ct);
            await _ceilingRepo.SaveChangesAsync(ct);
            await _audit.LogAsync("budget_ceilings", existing.Id, AuditAction.Update,
                oldSnap, new { Amount = amount }, ct);
            return ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(existing, fundsById));
        }

        BudgetCeiling entity = new()
        {
            OfficeId        = officeId,
            FiscalYear      = fiscalYear,
            FundingSourceId = fundingSourceId,
            Amount          = amount,
        };
        await _ceilingRepo.AddAsync(entity, ct);
        await _ceilingRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("budget_ceilings", entity.Id, AuditAction.Create,
            null, new { entity.OfficeId, entity.FiscalYear, entity.FundingSourceId, entity.Amount }, ct);
        return ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(entity, fundsById));
    }

    // ── Division Allocations ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default)
    {
        IReadOnlyList<Division>           allDivisions   = await _divisionRepo.GetAllAsync(ct);
        IReadOnlyList<DivisionAllocation> allAllocations = await _allocationRepo.GetAllAsync(ct);
        Dictionary<int, FundingSource>    fundsById      = (await _fundingSourceRepo.GetAllAsync(ct))
            .ToDictionary(f => f.Id);

        HashSet<int> officeDivIds = allDivisions
            .Where(d => d.OfficeId == officeId && d.IsActive)
            .Select(d => d.Id)
            .ToHashSet();

        Dictionary<int, string> divNames = allDivisions.ToDictionary(d => d.Id, d => d.Name);

        return allAllocations
            .Where(a => officeDivIds.Contains(a.DivisionId) && a.FiscalYear == fiscalYear
                     && a.FundingSourceId == fundingSourceId)
            .Select(a => MapAllocation(a, divNames.GetValueOrDefault(a.DivisionId, string.Empty), fundsById))
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<DivisionAllocationDto>>> UpsertAllocationsAsync(
        int officeId, int fiscalYear, int fundingSourceId,
        IReadOnlyList<UpsertDivisionAllocationDto> dtos,
        CancellationToken ct = default)
    {
        // Guard 1 — ceiling must exist for this fund.
        BudgetCeiling? ceiling = await FindCeilingAsync(officeId, fiscalYear, fundingSourceId, ct);
        if (ceiling is null)
            return ServiceResult<IReadOnlyList<DivisionAllocationDto>>.BadRequest(
                $"No budget ceiling set for office {officeId}, FY {fiscalYear}, funding source {fundingSourceId}. Set a ceiling first.");

        // Guard 2 — Σ allocations ≤ that fund's ceiling.
        decimal total = dtos.Sum(d => d.Amount);
        if (total > ceiling.Amount)
            return ServiceResult<IReadOnlyList<DivisionAllocationDto>>.BadRequest(
                $"Σ allocations (₱{total:N2}) exceeds ceiling (₱{ceiling.Amount:N2}).");

        Dictionary<int, FundingSource> fundsById = (await _fundingSourceRepo.GetAllAsync(ct))
            .ToDictionary(f => f.Id);

        // Resolve all divisions of this office to validate ownership.
        IReadOnlyList<Division> allDivisions = await _divisionRepo.GetAllAsync(ct);
        HashSet<int> officeDivIds = allDivisions
            .Where(d => d.OfficeId == officeId)
            .Select(d => d.Id)
            .ToHashSet();
        Dictionary<int, string> divNames = allDivisions.ToDictionary(d => d.Id, d => d.Name);

        // Load existing allocations for this office+FY+fund.
        IReadOnlyList<DivisionAllocation> existing = await _allocationRepo.GetAllAsync(ct);
        Dictionary<int, DivisionAllocation> existingByDiv = existing
            .Where(a => officeDivIds.Contains(a.DivisionId) && a.FiscalYear == fiscalYear
                     && a.FundingSourceId == fundingSourceId)
            .ToDictionary(a => a.DivisionId);

        List<DivisionAllocationDto> results = [];

        foreach (UpsertDivisionAllocationDto dto in dtos)
        {
            if (!officeDivIds.Contains(dto.DivisionId))
                continue;   // silently skip divisions not belonging to this office

            if (existingByDiv.TryGetValue(dto.DivisionId, out DivisionAllocation? row))
            {
                object oldSnap = new { row.Amount };
                row.Amount = dto.Amount;
                await _allocationRepo.UpdateAsync(row, ct);
                await _audit.LogAsync("division_allocations", row.Id, AuditAction.Update,
                    oldSnap, new { row.DivisionId, row.FiscalYear, row.FundingSourceId, row.Amount }, ct);
                results.Add(MapAllocation(row, divNames.GetValueOrDefault(row.DivisionId, string.Empty), fundsById));
            }
            else
            {
                DivisionAllocation entity = new()
                {
                    DivisionId      = dto.DivisionId,
                    FiscalYear      = fiscalYear,
                    FundingSourceId = fundingSourceId,
                    Amount          = dto.Amount,
                };
                await _allocationRepo.AddAsync(entity, ct);
                await _audit.LogAsync("division_allocations", entity.Id, AuditAction.Create,
                    null, new { entity.DivisionId, entity.FiscalYear, entity.FundingSourceId, entity.Amount }, ct);
                results.Add(MapAllocation(entity, divNames.GetValueOrDefault(entity.DivisionId, string.Empty), fundsById));
            }
        }

        await _allocationRepo.SaveChangesAsync(ct);
        return ServiceResult<IReadOnlyList<DivisionAllocationDto>>.Ok(results);
    }

    // ── PPA → Division Assignments ────────────────────────────────────────────

    public async Task<IReadOnlyList<ProgramAssignmentDto>> GetProgramAssignmentsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default)
    {
        // Resolve the config-office ref code (suffix used for AIP office matching).
        IReadOnlyList<Office> allOffices = await _officeRepo.GetAllAsync(ct);
        Office? office = allOffices.FirstOrDefault(o => o.Id == officeId);
        if (office?.OfficeRefCode is null) return [];

        // Find the non-archived AIP record for the fiscal year.
        IReadOnlyList<AipRecord> allAip = await _aipRepo.GetAllAsync(ct);
        AipRecord? aipRecord = allAip.FirstOrDefault(
            r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived);
        if (aipRecord is null) return [];

        // Load AIP offices and filter by ref-code suffix match (same as WFP).
        IReadOnlyList<AipOffice> aipOffices = await _aipRepo.GetOfficesByAipIdAsync(aipRecord.Id, ct);
        List<AipOffice> matchedOffices = aipOffices
            .Where(o => o.RefCode.EndsWith(office.OfficeRefCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchedOffices.Count == 0) return [];

        // Load programs for matched offices.
        List<int> officeIds = matchedOffices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, ct);

        // Bulk-load program_divisions for these offices.
        List<string> officeRefCodes = matchedOffices.Select(o => o.RefCode).ToList();
        IReadOnlyList<ProgramDivision> pds =
            await _pdRepo.GetProgramDivisionsByOfficeRefCodesAsync(officeRefCodes, ct);

        // Index pds by (officeRefCode, programRefCode) for O(1) lookup.
        Dictionary<(string, string), List<int>> divIdsByKey = [];
        foreach (ProgramDivision pd in pds)
        {
            (string, string) key = (pd.OfficeRefCode, pd.ProgramRefCode);
            if (!divIdsByKey.TryGetValue(key, out List<int>? list))
            {
                list = [];
                divIdsByKey[key] = list;
            }
            list.Add(pd.DivisionId);
        }

        // Build the AipOffice lookup for sector info.
        Dictionary<int, AipOffice> officeById = matchedOffices.ToDictionary(o => o.Id);

        return programs
            .Select(p =>
            {
                AipOffice parent = officeById[p.OfficeId];
                (string, string) key = (parent.RefCode, p.RefCode);
                List<int> divIds = divIdsByKey.GetValueOrDefault(key, []);
                return new ProgramAssignmentDto(
                    parent.RefCode, p.RefCode, p.Name, parent.Sector, divIds.AsReadOnly());
            })
            .ToList();
    }

    public async Task<ServiceResult<ProgramAssignmentDto>> UpsertProgramAssignmentAsync(
        UpsertProgramAssignmentDto dto, CancellationToken ct = default)
    {
        // Load current rows for this (officeRefCode, programRefCode) pair.
        IReadOnlyList<ProgramDivision> existing =
            await _pdRepo.FindProgramDivisionsAsync(dto.OfficeRefCode, dto.ProgramRefCode, ct);

        HashSet<int> currentDivIds  = existing.Select(pd => pd.DivisionId).ToHashSet();
        HashSet<int> desiredDivIds  = dto.DivisionIds.ToHashSet();

        // Removes: in current but not in desired.
        foreach (ProgramDivision pd in existing.Where(pd => !desiredDivIds.Contains(pd.DivisionId)))
        {
            await _pdRepo.DeleteAsync(pd, ct);
            await _audit.LogAsync("program_divisions", pd.Id, AuditAction.Delete,
                new { pd.OfficeRefCode, pd.ProgramRefCode, pd.DivisionId }, null, ct);
        }

        // Adds: in desired but not in current.
        foreach (int divId in desiredDivIds.Where(id => !currentDivIds.Contains(id)))
        {
            ProgramDivision entity = new()
            {
                OfficeRefCode  = dto.OfficeRefCode,
                ProgramRefCode = dto.ProgramRefCode,
                DivisionId     = divId,
            };
            await _pdRepo.AddAsync(entity, ct);
            await _audit.LogAsync("program_divisions", entity.Id, AuditAction.Create,
                null, new { entity.OfficeRefCode, entity.ProgramRefCode, entity.DivisionId }, ct);
        }

        await _pdRepo.SaveChangesAsync(ct);

        // Reload to return accurate state.
        IReadOnlyList<ProgramDivision> updated =
            await _pdRepo.FindProgramDivisionsAsync(dto.OfficeRefCode, dto.ProgramRefCode, ct);
        List<int> divIds = updated.Select(pd => pd.DivisionId).ToList();

        return ServiceResult<ProgramAssignmentDto>.Ok(
            new ProgramAssignmentDto(
                dto.OfficeRefCode, dto.ProgramRefCode,
                ProgramName: string.Empty,  // caller has the name; not reloaded from AIP here
                Sector: string.Empty,
                DivisionIds: divIds.AsReadOnly()));
    }

    // ── WFP Setup Gate ────────────────────────────────────────────────────────

    public async Task<AllocationSetupStatusDto> GetSetupStatusAsync(
        int officeId, int fiscalYear, int divisionId, CancellationToken ct = default)
    {
        // Only General Fund is mandatory to unlock WFP entry (§2 D7) — other funds are optional
        // and only enforced by WfpCeilingService once an expenditure actually selects them.
        int? gfId = await GetGeneralFundIdAsync(ct);
        bool hasCeiling = false, hasAllocation = false;

        if (gfId is int generalFundId)
        {
            // HasCeiling
            BudgetCeiling? ceiling = await FindCeilingAsync(officeId, fiscalYear, generalFundId, ct);
            hasCeiling = ceiling is not null;

            // HasAllocation — requires a positive amount, not just a row
            IReadOnlyList<DivisionAllocation> allAllocs = await _allocationRepo.GetAllAsync(ct);
            hasAllocation = allAllocs.Any(a =>
                a.DivisionId == divisionId && a.FiscalYear == fiscalYear
                && a.FundingSourceId == generalFundId && a.Amount > 0);
        }

        // HasProgramAssignment — at least one program assigned to this division for the office+FY.
        bool hasProgramAssignment = false;
        IReadOnlyList<Office> allOffices = await _officeRepo.GetAllAsync(ct);
        Office? office = allOffices.FirstOrDefault(o => o.Id == officeId);
        if (office?.OfficeRefCode is not null)
        {
            IReadOnlyList<AipRecord> allAip = await _aipRepo.GetAllAsync(ct);
            AipRecord? rec = allAip.FirstOrDefault(
                r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived);
            if (rec is not null)
            {
                IReadOnlyList<AipOffice> aipOffices = await _aipRepo.GetOfficesByAipIdAsync(rec.Id, ct);
                List<string> matchedRefs = aipOffices
                    .Where(o => o.RefCode.EndsWith(office.OfficeRefCode, StringComparison.OrdinalIgnoreCase))
                    .Select(o => o.RefCode)
                    .ToList();

                if (matchedRefs.Count > 0)
                {
                    IReadOnlyList<ProgramDivision> pds =
                        await _pdRepo.GetProgramDivisionsByOfficeRefCodesAsync(matchedRefs, ct);
                    hasProgramAssignment = pds.Any(pd => pd.DivisionId == divisionId);
                }
            }
        }

        return new AllocationSetupStatusDto(hasCeiling, hasAllocation, hasProgramAssignment);
    }

    public async Task<AllocationSetupOverviewDto> GetSetupOverviewAsync(
        int fiscalYear, CancellationToken ct = default)
    {
        // Only General Fund counts toward "fully set up" (§2 D7) — same rule as GetSetupStatusAsync.
        int? gfId = await GetGeneralFundIdAsync(ct);

        IReadOnlyList<Office> activeOffices = (await _officeRepo.GetAllAsync(ct))
            .Where(o => o.IsActive)
            .ToList();

        HashSet<int> officesWithCeiling = (await _ceilingRepo.GetAllAsync(ct))
            .Where(c => c.FiscalYear == fiscalYear && gfId is int gf1 && c.FundingSourceId == gf1)
            .Select(c => c.OfficeId)
            .ToHashSet();

        IReadOnlyList<Division> allDivisions = await _divisionRepo.GetAllAsync(ct);
        Dictionary<int, int> officeIdByDivisionId = allDivisions.ToDictionary(d => d.Id, d => d.OfficeId);
        Dictionary<int, decimal> allocatedByOfficeId = (await _allocationRepo.GetAllAsync(ct))
            .Where(a => a.FiscalYear == fiscalYear && officeIdByDivisionId.ContainsKey(a.DivisionId)
                     && gfId is int gf2 && a.FundingSourceId == gf2)
            .GroupBy(a => officeIdByDivisionId[a.DivisionId])
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        HashSet<int> officesWithProgramAssignment =
            await GetOfficesWithProgramAssignmentAsync(activeOffices, fiscalYear, ct);

        int fullySetup = 0, incomplete = 0, notStarted = 0;
        foreach (Office office in activeOffices)
        {
            bool hasCeiling = officesWithCeiling.Contains(office.Id);
            bool hasAllocation = allocatedByOfficeId.GetValueOrDefault(office.Id, 0m) > 0;
            bool hasAssignment = officesWithProgramAssignment.Contains(office.Id);

            if (hasCeiling && hasAllocation && hasAssignment) fullySetup++;
            else if (!hasCeiling && !hasAllocation && !hasAssignment) notStarted++;
            else incomplete++;
        }

        return new AllocationSetupOverviewDto(activeOffices.Count, fullySetup, incomplete, notStarted);
    }

    /// <summary>
    /// Bulk version of the office_ref_code-match + program-assignment check in
    /// GetProgramAssignmentsAsync/GetSetupStatusAsync — computed once for every active
    /// office instead of one office at a time, so GetSetupOverviewAsync stays O(1) queries
    /// regardless of office count.
    /// </summary>
    private async Task<HashSet<int>> GetOfficesWithProgramAssignmentAsync(
        IReadOnlyList<Office> activeOffices, int fiscalYear, CancellationToken ct)
    {
        HashSet<int> result = [];

        IReadOnlyList<AipRecord> allAip = await _aipRepo.GetAllAsync(ct);
        AipRecord? aipRecord = allAip.FirstOrDefault(
            r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived);
        if (aipRecord is null) return result;

        IReadOnlyList<AipOffice> aipOffices = await _aipRepo.GetOfficesByAipIdAsync(aipRecord.Id, ct);

        Dictionary<int, List<AipOffice>> matchedByOfficeId = [];
        foreach (Office office in activeOffices)
        {
            if (office.OfficeRefCode is null) continue;
            List<AipOffice> matches = aipOffices
                .Where(ao => ao.RefCode.EndsWith(office.OfficeRefCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 0) matchedByOfficeId[office.Id] = matches;
        }
        if (matchedByOfficeId.Count == 0) return result;

        List<AipOffice> allMatchedAipOffices = [.. matchedByOfficeId.Values.SelectMany(m => m)];
        List<int> matchedAipOfficeIds = allMatchedAipOffices.Select(o => o.Id).Distinct().ToList();
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsAsync(matchedAipOfficeIds, ct);
        Dictionary<int, AipOffice> aipOfficeById = allMatchedAipOffices.ToDictionary(o => o.Id);

        List<string> matchedOfficeRefCodes = allMatchedAipOffices.Select(o => o.RefCode).Distinct().ToList();
        IReadOnlyList<ProgramDivision> pds =
            await _pdRepo.GetProgramDivisionsByOfficeRefCodesAsync(matchedOfficeRefCodes, ct);
        HashSet<(string OfficeRefCode, string ProgramRefCode)> assignedKeys =
            pds.Select(pd => (pd.OfficeRefCode, pd.ProgramRefCode)).ToHashSet();

        foreach ((int officeId, List<AipOffice> matches) in matchedByOfficeId)
        {
            HashSet<int> matchedAipOfficeIdsForOffice = matches.Select(m => m.Id).ToHashSet();
            bool anyAssigned = programs
                .Where(p => matchedAipOfficeIdsForOffice.Contains(p.OfficeId))
                .Any(p => assignedKeys.Contains((aipOfficeById[p.OfficeId].RefCode, p.RefCode)));
            if (anyAssigned) result.Add(officeId);
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BudgetCeiling?> FindCeilingAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct)
    {
        IReadOnlyList<BudgetCeiling> all = await _ceilingRepo.GetAllAsync(ct);
        return all.FirstOrDefault(c =>
            c.OfficeId == officeId && c.FiscalYear == fiscalYear && c.FundingSourceId == fundingSourceId);
    }

    /// <inheritdoc />
    public async Task<int?> GetGeneralFundIdAsync(CancellationToken ct = default)
    {
        IReadOnlyList<FundingSource> all = await _fundingSourceRepo.GetAllAsync(ct);
        return all.FirstOrDefault(f => f.Code.Equals(GeneralFundCode, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static BudgetCeilingDto MapCeiling(BudgetCeiling c, IReadOnlyDictionary<int, FundingSource> fundsById)
    {
        (string code, string name) = FundInfo(c.FundingSourceId, fundsById);
        return new(c.Id, c.OfficeId, c.FiscalYear, c.FundingSourceId, code, name, c.Amount);
    }

    private static DivisionAllocationDto MapAllocation(
        DivisionAllocation a, string divisionName, IReadOnlyDictionary<int, FundingSource> fundsById)
    {
        (string code, string name) = FundInfo(a.FundingSourceId, fundsById);
        return new(a.Id, a.DivisionId, divisionName, a.FiscalYear, a.FundingSourceId, code, name, a.Amount);
    }

    private static (string Code, string Name) FundInfo(
        int fundingSourceId, IReadOnlyDictionary<int, FundingSource> fundsById) =>
        fundsById.TryGetValue(fundingSourceId, out FundingSource? f)
            ? (f.Code, f.Name)
            : (string.Empty, string.Empty);
}
