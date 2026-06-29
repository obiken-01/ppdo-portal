using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Allocation service — budget ceiling, division allocations, PPA→division assignment,
/// and the WFP setup-gate query (v1.2 — RAL-99).
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
    private readonly IRepository<BudgetCeiling>      _ceilingRepo;
    private readonly IRepository<DivisionAllocation> _allocationRepo;
    private readonly IAllocationRepository           _pdRepo;
    private readonly IRepository<Division>           _divisionRepo;
    private readonly IRepository<Office>             _officeRepo;
    private readonly IAipRepository                  _aipRepo;
    private readonly IAuditService                   _audit;
    private readonly CallerContext                   _caller;

    public AllocationService(
        IRepository<BudgetCeiling>      ceilingRepo,
        IRepository<DivisionAllocation> allocationRepo,
        IAllocationRepository           pdRepo,
        IRepository<Division>           divisionRepo,
        IRepository<Office>             officeRepo,
        IAipRepository                  aipRepo,
        IAuditService                   audit,
        CallerContext                   caller)
    {
        _ceilingRepo    = ceilingRepo;
        _allocationRepo = allocationRepo;
        _pdRepo         = pdRepo;
        _divisionRepo   = divisionRepo;
        _officeRepo     = officeRepo;
        _aipRepo        = aipRepo;
        _audit          = audit;
        _caller         = caller;
    }

    // ── Budget Ceiling ────────────────────────────────────────────────────────

    public async Task<ServiceResult<BudgetCeilingDto>> GetCeilingAsync(
        int officeId, int fiscalYear, CancellationToken ct = default)
    {
        BudgetCeiling? existing = await FindCeilingAsync(officeId, fiscalYear, ct);
        return existing is null
            ? ServiceResult<BudgetCeilingDto>.NotFound(
                $"No budget ceiling set for office {officeId}, FY {fiscalYear}.")
            : ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(existing));
    }

    public async Task<ServiceResult<BudgetCeilingDto>> UpsertCeilingAsync(
        int officeId, int fiscalYear, decimal amount, CancellationToken ct = default)
    {
        BudgetCeiling? existing = await FindCeilingAsync(officeId, fiscalYear, ct);

        if (existing is not null)
        {
            object oldSnap = new { existing.Amount };
            existing.Amount = amount;
            await _ceilingRepo.UpdateAsync(existing, ct);
            await _ceilingRepo.SaveChangesAsync(ct);
            await _audit.LogAsync("budget_ceilings", existing.Id, AuditAction.Update,
                oldSnap, new { Amount = amount }, ct);
            return ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(existing));
        }

        BudgetCeiling entity = new() { OfficeId = officeId, FiscalYear = fiscalYear, Amount = amount };
        await _ceilingRepo.AddAsync(entity, ct);
        await _ceilingRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("budget_ceilings", entity.Id, AuditAction.Create,
            null, new { entity.OfficeId, entity.FiscalYear, entity.Amount }, ct);
        return ServiceResult<BudgetCeilingDto>.Ok(MapCeiling(entity));
    }

    // ── Division Allocations ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default)
    {
        IReadOnlyList<Division>           allDivisions  = await _divisionRepo.GetAllAsync(ct);
        IReadOnlyList<DivisionAllocation> allAllocations = await _allocationRepo.GetAllAsync(ct);

        HashSet<int> officeDivIds = allDivisions
            .Where(d => d.OfficeId == officeId && d.IsActive)
            .Select(d => d.Id)
            .ToHashSet();

        Dictionary<int, string> divNames = allDivisions.ToDictionary(d => d.Id, d => d.Name);

        return allAllocations
            .Where(a => officeDivIds.Contains(a.DivisionId) && a.FiscalYear == fiscalYear)
            .Select(a => MapAllocation(a, divNames.GetValueOrDefault(a.DivisionId, string.Empty)))
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<DivisionAllocationDto>>> UpsertAllocationsAsync(
        int officeId, int fiscalYear,
        IReadOnlyList<UpsertDivisionAllocationDto> dtos,
        CancellationToken ct = default)
    {
        // Guard 1 — ceiling must exist.
        BudgetCeiling? ceiling = await FindCeilingAsync(officeId, fiscalYear, ct);
        if (ceiling is null)
            return ServiceResult<IReadOnlyList<DivisionAllocationDto>>.BadRequest(
                $"No budget ceiling set for office {officeId}, FY {fiscalYear}. Set a ceiling first.");

        // Guard 2 — Σ allocations ≤ ceiling.
        decimal total = dtos.Sum(d => d.Amount);
        if (total > ceiling.Amount)
            return ServiceResult<IReadOnlyList<DivisionAllocationDto>>.BadRequest(
                $"Σ allocations (₱{total:N2}) exceeds ceiling (₱{ceiling.Amount:N2}).");

        // Resolve all divisions of this office to validate ownership.
        IReadOnlyList<Division> allDivisions = await _divisionRepo.GetAllAsync(ct);
        HashSet<int> officeDivIds = allDivisions
            .Where(d => d.OfficeId == officeId)
            .Select(d => d.Id)
            .ToHashSet();
        Dictionary<int, string> divNames = allDivisions.ToDictionary(d => d.Id, d => d.Name);

        // Load existing allocations for this office+FY.
        IReadOnlyList<DivisionAllocation> existing = await _allocationRepo.GetAllAsync(ct);
        Dictionary<int, DivisionAllocation> existingByDiv = existing
            .Where(a => officeDivIds.Contains(a.DivisionId) && a.FiscalYear == fiscalYear)
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
                    oldSnap, new { row.DivisionId, row.FiscalYear, row.Amount }, ct);
                results.Add(MapAllocation(row, divNames.GetValueOrDefault(row.DivisionId, string.Empty)));
            }
            else
            {
                DivisionAllocation entity = new()
                {
                    DivisionId = dto.DivisionId,
                    FiscalYear = fiscalYear,
                    Amount     = dto.Amount,
                };
                await _allocationRepo.AddAsync(entity, ct);
                await _audit.LogAsync("division_allocations", entity.Id, AuditAction.Create,
                    null, new { entity.DivisionId, entity.FiscalYear, entity.Amount }, ct);
                results.Add(MapAllocation(entity, divNames.GetValueOrDefault(entity.DivisionId, string.Empty)));
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
        // HasCeiling
        BudgetCeiling? ceiling = await FindCeilingAsync(officeId, fiscalYear, ct);
        bool hasCeiling = ceiling is not null;

        // HasAllocation
        IReadOnlyList<DivisionAllocation> allAllocs = await _allocationRepo.GetAllAsync(ct);
        bool hasAllocation = allAllocs.Any(a => a.DivisionId == divisionId && a.FiscalYear == fiscalYear);

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BudgetCeiling?> FindCeilingAsync(int officeId, int fiscalYear, CancellationToken ct)
    {
        IReadOnlyList<BudgetCeiling> all = await _ceilingRepo.GetAllAsync(ct);
        return all.FirstOrDefault(c => c.OfficeId == officeId && c.FiscalYear == fiscalYear);
    }

    private static BudgetCeilingDto MapCeiling(BudgetCeiling c) =>
        new(c.Id, c.OfficeId, c.FiscalYear, c.Amount);

    private static DivisionAllocationDto MapAllocation(DivisionAllocation a, string divisionName) =>
        new(a.Id, a.DivisionId, divisionName, a.FiscalYear, a.Amount);
}
