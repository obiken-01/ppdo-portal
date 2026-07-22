/** Budget Planning types — mirrors PPDO.Application/DTOs/BudgetPlanning/ */

// ── AIP list ─────────────────────────────────────────────────────────────────

export interface AipRecordResponse {
  id: number;
  fiscalYear: number;
  entrySource: string;
  originalFilename: string | null;
  uploadedById: string;
  uploadedAt: string;
  status: string;
  ldipId: number | null;
  sourceId: number | null;
  officeCount: number;
  uploadedByName: string | null;
}

// ── AIP import preview / confirm ──────────────────────────────────────────────

export interface ParsedAipActivityResponse {
  refCode: string;
  name: string;
  esreCode: string | null;
  implementingOffice: string | null;
  startDate: string | null;
  endDate: string | null;
  expectedOutputs: string | null;
  fundingSourceRaw: string | null;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  total: number | null;
  ccAdaptation: number | null;
  ccMitigation: number | null;
  ccTypologyCode: string | null;
}

export interface ParsedAipProjectResponse {
  refCode: string;
  name: string;
  activities: ParsedAipActivityResponse[];
  /** RAL-108: set when this project row also carries its own line item — materialized as a synthetic activity at confirm time. */
  lineItem: ParsedAipActivityResponse | null;
}

export interface ParsedAipProgramResponse {
  refCode: string;
  name: string;
  projects: ParsedAipProjectResponse[];
  /** RAL-108: set when this program row also carries its own line item — materialized as a synthetic project+activity at confirm time. */
  lineItem: ParsedAipActivityResponse | null;
}

export interface ParsedAipOfficeResponse {
  refCode: string;
  name: string;
  sector: string;
  programs: ParsedAipProgramResponse[];
}

export interface AipImportCountsResponse {
  offices: number;
  programs: number;
  projects: number;
  activities: number;
}

export interface AipImportPreviewResponse {
  fiscalYear: number;
  sectorOffices: Record<string, ParsedAipOfficeResponse[]>;
  counts: AipImportCountsResponse;
  warnings: string[];
}

export interface AipImportConfirmRequest {
  fiscalYear: number;
  originalFilename: string;
  ldipId: number | null;
  sectorOffices: Record<string, ParsedAipOfficeResponse[]>;
  /** RAL-178: when set, the confirm re-uploads into this existing record instead of creating a new one. */
  targetRecordId?: number | null;
}

// ── AIP manual entry (RAL-62) — one node at a time ────────────────────────────

export interface CreateAipRecordRequest {
  fiscalYear: number;
}

export interface CreateAipOfficeRequest {
  officeConfigId: number;
  sector: string;
}

export interface CreateAipProgramRequest {
  name: string;
  functionBand?: string | null;
}

export interface CreateAipProjectRequest {
  name: string;
}

export interface CreateAipActivityRequest {
  name: string;
  esreCode?: string | null;
  implementingOffice?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  expectedOutputs?: string | null;
  fundingSourceRaw?: string | null;
  ps?: number | null;
  mooe?: number | null;
  co?: number | null;
  ccAdaptation?: number | null;
  ccMitigation?: number | null;
  ccTypologyCode?: string | null;
}

// ── AIP inline activity edit (RAL-179) ────────────────────────────────────────

export interface UpdateAipActivityRequest {
  name: string;
  esreCode?: string | null;
  implementingOffice?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  expectedOutputs?: string | null;
  /** Direct FK to a config FundingSource row — the UI offers a dropdown, so there's nothing to match. */
  fundingSourceId?: number | null;
  ps?: number | null;
  mooe?: number | null;
  co?: number | null;
  ccAdaptation?: number | null;
  ccMitigation?: number | null;
  ccTypologyCode?: string | null;
}

// ── AIP detail (stored hierarchy) ────────────────────────────────────────────

export interface AipActivityDetail {
  id: number;
  projectId: number;
  refCode: string;
  name: string;
  esreCode: string | null;
  implementingOffice: string | null;
  startDate: string | null;
  endDate: string | null;
  expectedOutputs: string | null;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  total: number | null;
  ccAdaptation: number | null;
  ccMitigation: number | null;
  ccTypologyCode: string | null;
  isCreation: boolean;
  /** RAL-108: true when this activity was materialized from a program/project-level line item. */
  isSynthetic: boolean;
}

export interface AipProjectDetail {
  id: number;
  programId: number;
  refCode: string;
  name: string;
  activities: AipActivityDetail[];
  /** RAL-108: true when this project was materialized to hold its parent program's line item. */
  isSynthetic: boolean;
}

export interface AipProgramDetail {
  id: number;
  officeId: number;
  refCode: string;
  name: string;
  projects: AipProjectDetail[];
  functionBand: string | null;
}

export interface AipOfficeDetail {
  id: number;
  aipRecordId: number;
  refCode: string;
  name: string;
  sector: string;
  programs: AipProgramDetail[];
}

export interface AipRecordDetail {
  id: number;
  fiscalYear: number;
  entrySource: string;
  originalFilename: string | null;
  uploadedById: string;
  uploadedAt: string;
  status: string;
  ldipId: number | null;
  sourceId: number | null;
  offices: AipOfficeDetail[];
  /** True when a WFP has been built from this AIP — re-upload is blocked in that case. */
  hasWfpUsage: boolean;
}

// ── AIP summary — slim WFP-grid types (RAL-89) ───────────────────────────────

export interface AipActivitySummary {
  id: number;
  refCode: string;
  name: string;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  total: number | null;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  isCreation: boolean;
}

export interface AipProjectSummary {
  id: number;
  refCode: string;
  name: string;
  activities: AipActivitySummary[];
}

export interface AipProgramSummary {
  id: number;
  refCode: string;
  name: string;
  projects: AipProjectSummary[];
  functionBand: string | null;
}

export interface AipOfficeSummary {
  id: number;
  refCode: string;
  name: string;
  sector: string;
  programs: AipProgramSummary[];
}

export interface AipRecordSummary {
  id: number;
  fiscalYear: number;
  offices: AipOfficeSummary[];
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export interface StatusBreakdown {
  status: string;
  count: number;
}

/**
 * One division's allocated amount in one fund, plus how much of it this division has actually
 * used in WFP so far and how much is left (v1.4.5 — RAL-161; used/remaining added RAL-176).
 * remaining = amount - used — the same division-scoped figure the WFP Entry Wizard shows, NOT
 * FundCeiling.remaining (that one is an office-wide unallocated-ceiling fact, intentionally
 * identical across every division).
 */
export interface DivisionFundAmount {
  fundingSourceId: number;
  fundCode: string;
  fundName: string;
  amount: number;
  used: number;
  remaining: number;
}

/** One division's WFP status + activity coverage + allocation, PPDO-scoped (v1.4.5 — RAL-161). */
export interface DivisionWfpStatus {
  divisionId: number;
  divisionCode: string | null;
  divisionName: string;
  wfpStatus: "Draft" | "Final" | "Not started";
  activitiesWithExpenditures: number;
  totalActivities: number;
  totalAllocated: number;
  allocationByFund: DivisionFundAmount[];
}

/** One division's share of a fund's office-wide ceiling. */
export interface FundDivisionShare {
  divisionId: number;
  divisionCode: string | null;
  divisionName: string;
  amount: number;
}

/**
 * One funding source's office-wide ceiling + per-division allocation breakdown
 * (v1.4.5 — RAL-161). Ceiling/remaining are office-level figures, NOT per division —
 * a division's amount inside byDivision is its own allocated share of this same ceiling.
 */
export interface FundCeiling {
  fundingSourceId: number;
  fundCode: string;
  fundName: string;
  ceiling: number;
  remaining: number;
  byDivision: FundDivisionShare[];
}

/** Just the fiscal-year picker (RAL-166 follow-up) — see GET /budget-planning/fiscal-years. */
export interface FiscalYears {
  fiscalYear: number;
  availableFiscalYears: number[];
}

/**
 * The PPDO-scoped Budget Planning Dashboard (v1.4.5 — RAL-161). Replaces the old
 * multi-office PlanningDashboard — Budget Planning is permanently scoped to PPDO.
 * For a division-scoped Staff caller, the server clamps wfpByDivision and every
 * FundCeiling.byDivision entry to just that caller's own division.
 */
export interface PpdoDashboard {
  fiscalYear: number;
  availableFiscalYears: number[];
  officeId: number;
  officeCode: string;
  officeName: string;
  ldip: OfficeLdipSummary;
  aip: OfficeAipSummary;
  wfpByDivision: DivisionWfpStatus[];
  ceilingByFund: FundCeiling[];
}

export interface RecentActivity {
  id: number;
  changedAt: string; // ISO 8601
  tableName: string;
  action: string;
  // Exactly one of recordId/recordGuid is set, depending on the table's PK type
  // (int-keyed tables like wfp_expenditures vs Guid-keyed tables like users).
  recordId: number | null;
  recordGuid: string | null;
  actorName: string;
}

// ── Office-scoped dashboard (RAL-60) ─────────────────────────────────────────

export interface AllocationSetupSummary {
  ceilingAmount: number | null;
  allocated: number;
  remaining: number | null;
  isOverAllocated: boolean;
  assignedProgramCount: number;
  unassignedProgramCount: number;
}

/** scopingSupported is false until RAL-61 adds ldip_records.office_id. */
export interface OfficeLdipSummary {
  scopingSupported: boolean;
  total: number;
  breakdown: StatusBreakdown[];
}

export interface OfficeAipSummary {
  exists: boolean;
  status: string | null;
  programCount: number;
  projectCount: number;
  activityCount: number;
}

export interface OfficeDashboard {
  officeId: number;
  fiscalYear: number;
  allocation: AllocationSetupSummary;
  ldip: OfficeLdipSummary;
  aip: OfficeAipSummary;
}

// ── WFP ──────────────────────────────────────────────────────────────────────

export type ExpenditureType = "PS" | "MOOE" | "CO";

export interface WfpExpenditureLine {
  id: number;
  wfpActivityId: number;
  expenditureType: ExpenditureType;
  resourcesNeeded: string | null;
  responsibleUnit: string | null;
  successIndicator: string | null;
  meansOfVerification: string | null;
  accountId: number | null;
  accountNumberSnapshot: string | null;
  accountTitleSnapshot: string | null;
  totalAppropriation: number;
  applyReserve: boolean;
  reserveAmount: number;
  netAppropriation: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  quarterlyTotal: number;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  sortOrder: number;
}

export interface WfpActivity {
  id: number;
  wfpId: number;
  aipActivityId: number;
  lines: WfpExpenditureLine[];
}

export interface WfpRecord {
  id: number;
  aipRecordId: number;
  officeId: number;
  divisionId: number | null;
  fiscalYear: number;
  status: "Draft" | "Final";
  createdById: string;
  createdAt: string;
  updatedAt: string | null;
  finalizedAt: string | null;
}

export interface WfpRecordDetail extends WfpRecord {
  activities: WfpActivity[];
}

export interface SaveWfpLine {
  expenditureType: ExpenditureType;
  resourcesNeeded: string | null;
  responsibleUnit: string | null;
  successIndicator: string | null;
  meansOfVerification: string | null;
  accountId: number | null;
  totalAppropriation: number;
  applyReserve: boolean;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  fundingSourceId: number | null;
  sortOrder: number;
}

export interface SaveWfpActivityRequest {
  aipActivityId: number;
  lines: SaveWfpLine[];
}

export interface SaveWfpRequest {
  aipRecordId: number;
  officeId: number;
  fiscalYear: number;
  divisionId: number | null;
  activities: SaveWfpActivityRequest[];
}

// ── v1.4 WFP expenditure (RAL-120/121/122/123) ───────────────────────────────
// Replaces the WfpExpenditureLine model above for new entries — schema+math live
// server-side (WfpExpenditureCalculator); Q1-4/Net/Total are always server-computed.

export type WfpExpenditureNature = "Procurement" | "Non-Procurement" | "Combined";
export type WfpExpenditureFrequency = "M" | "Q" | "B" | "A";

export interface WfpExpenditurePeriodDto {
  periodNo: number;
  amount: number;
}

export interface WfpProcurementItemDto {
  periodNo: number;
  priceIndexItemId: number | null;
  name: string;
  unit: string;
  unitPrice: number;
  qty: number;
  numberOfDays: number;
  lineTotal: number;
}

export interface WfpExpenditureDto {
  id: number;
  wfpActivityId: number;
  accountId: number | null;
  accountNumberSnapshot: string | null;
  accountTitleSnapshot: string | null;
  nature: WfpExpenditureNature;
  frequency: WfpExpenditureFrequency;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  fundingSourceNameSnapshot: string | null;
  applyReserve: boolean;
  reserveAmount: number;
  annualQuarterChoice: number | null;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  netAppropriation: number;
  totalAppropriation: number;
  periods: WfpExpenditurePeriodDto[];
  procurementItems: WfpProcurementItemDto[];
}

export interface SaveWfpExpenditurePeriodRequest {
  periodNo: number;
  amount: number;
}

export interface SaveWfpProcurementItemRequest {
  periodNo: number;
  priceIndexItemId: number | null;
  name: string;
  unit: string;
  unitPrice: number;
  qty: number;
  numberOfDays: number;
}

/** ReserveAmount null = "not specified" — server defaults to the reserve rate × Net. */
export interface SaveWfpExpenditureRequest {
  id: number | null;
  wfpActivityId: number;
  accountId: number | null;
  nature: WfpExpenditureNature;
  frequency: WfpExpenditureFrequency;
  fundingSourceId: number | null;
  applyReserve: boolean;
  reserveAmount: number | null;
  annualQuarterChoice: number | null;
  periods: SaveWfpExpenditurePeriodRequest[];
  procurementItems: SaveWfpProcurementItemRequest[];
}

export interface WfpReserveRateDto {
  rate: number;
}

/** One funding source's division-allocation status (v1.4.3 — RAL-154). */
export interface WfpFundCeilingDto {
  fundingSourceId: number;
  fundingSourceCode: string;
  fundingSourceName: string;
  allocation: number;
  remaining: number;
}

export interface WfpCeilingStatusDto {
  aipBudget: number;
  aipUsed: number;
  /** General Fund's allocation specifically (v1.4.3 — RAL-154) — see `funds` for other sources. */
  divisionAllocation: number;
  /** General Fund's remaining specifically (v1.4.3 — RAL-154) — see `funds` for other sources. */
  divisionRemaining: number;
  /** One entry per active funding source (v1.4.3 — RAL-154). */
  funds: WfpFundCeilingDto[];
}

export interface EnsureWfpActivityRequest {
  aipRecordId: number;
  officeId: number;
  divisionId: number | null;
  fiscalYear: number;
  aipActivityId: number;
}

export interface WfpActivityRefDto {
  wfpRecordId: number;
  wfpActivityId: number;
  wfpStatus: "Draft" | "Final";
}

// ── WFP Report preview (RAL-132) ───────────────────────────────────────────────
// Mirrors PPDO.Application/DTOs/BudgetPlanning/WfpReportDtos.cs.

export interface WfpReportOfficeDto {
  officeId: number;
  officeCode: string;
  officeName: string;
  wfpStatus: "Draft" | "Final";
}

export interface WfpReportAmountsDto {
  totalAppropriation: number;
  reserved: number;
  netAppropriation: number;
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  amountToBeReleased: number;
}

export interface WfpReportRowDto {
  sector: string;
  nature: string;
  accountNumber: string | null;
  accountTitle: string | null;
  amounts: WfpReportAmountsDto;
}

export interface WfpReportExpenseClassGroupDto {
  expenseClass: string;
  expenseClassLabel: string;
  rows: WfpReportRowDto[];
  subTotal: WfpReportAmountsDto;
}

export interface WfpReportActivityDto {
  refCode: string;
  name: string;
  isCreation: boolean;
  expenseClasses: WfpReportExpenseClassGroupDto[];
  grandTotal: WfpReportAmountsDto;
}

export interface WfpReportProjectDto {
  refCode: string;
  name: string;
  activities: WfpReportActivityDto[];
  grandTotal: WfpReportAmountsDto;
}

export interface WfpReportProgramDto {
  refCode: string;
  name: string;
  projects: WfpReportProjectDto[];
  grandTotal: WfpReportAmountsDto;
}

export interface WfpReportFunctionBandSectionDto {
  functionBand: string;
  functionBandLabel: string;
  programs: WfpReportProgramDto[];
}

/** Appears once per fund source (after its last section), not once per function-band section. */
export interface WfpReportBreakdownDto {
  personalServices: WfpReportAmountsDto;
  mooeExcludingCreation: WfpReportAmountsDto;
  capitalOutlay: WfpReportAmountsDto;
  personalServicesCreation: WfpReportAmountsDto;
  mooeCreation: WfpReportAmountsDto;
  grandTotal: WfpReportAmountsDto;
}

export interface WfpReportFundSourceDto {
  fundSourceName: string;
  sections: WfpReportFunctionBandSectionDto[];
  breakdown: WfpReportBreakdownDto;
}

export interface WfpReportDto {
  fiscalYear: number;
  officeCode: string;
  officeName: string;
  reserveRate: number;
  fundSourceReports: WfpReportFundSourceDto[];
}

// ── Allocation (RAL-99/101) ───────────────────────────────────────────────────

export interface BudgetCeilingDto {
  id: number;
  officeId: number;
  fiscalYear: number;
  fundingSourceId: number;
  fundingSourceCode: string;
  fundingSourceName: string;
  amount: number;
}

export interface DivisionAllocationDto {
  id: number;
  divisionId: number;
  divisionName: string;
  fiscalYear: number;
  fundingSourceId: number;
  fundingSourceCode: string;
  fundingSourceName: string;
  amount: number;
}

export interface ProgramAssignmentDto {
  officeRefCode: string;
  programRefCode: string;
  programName: string;
  sector: string;
  divisionIds: number[];
}

export interface AllocationSetupStatusDto {
  hasCeiling: boolean;
  hasAllocation: boolean;
  hasProgramAssignment: boolean;
}

export interface UpsertCeilingRequest {
  officeId: number;
  fiscalYear: number;
  fundingSourceId: number;
  amount: number;
}

export interface UpsertDivisionAllocationItem {
  divisionId: number;
  amount: number;
}

export interface UpsertAllocationsRequest {
  officeId: number;
  fiscalYear: number;
  fundingSourceId: number;
  allocations: UpsertDivisionAllocationItem[];
}

export interface UpsertProgramAssignmentRequest {
  officeRefCode: string;
  programRefCode: string;
  divisionIds: number[];
}

// ── LDIP ──────────────────────────────────────────────────────────────────────

export type LdipEntryMode = "New" | "Amendment" | "Supplemental" | "Upload";
export type LdipStatus    = "Draft" | "Final" | "Archived";
export type LdipSector    = "General" | "Social" | "Economic" | "Others";

export interface LdipRecord {
  id: number;
  refCode: string;
  title: string;
  fiscalYearStart: number;
  fiscalYearEnd: number;
  entryMode: LdipEntryMode;
  status: LdipStatus;
  sourceId: number | null;
  createdById: string;
  createdAt: string;
  updatedAt: string;
  officeId: number | null;
  officeName: string | null;
  programCount: number;
}

// ── LDIP hierarchy (RAL-61) — ref codes are server-computed, never client-sent ──

/**
 * One program row. Budget is in thousands (₱000), like AIP totals.
 * The detail fields below (RAL-113) are populated only for upload-derived
 * programs — null for programs added through the manual "+ Add Program" flow.
 */
export interface LdipProgram {
  id: number;
  refCode: string;
  name: string;
  budget: number;
  implementingOffice: string | null;
  startDate: string | null;
  endDate: string | null;
  expectedOutputs: string | null;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  ccAdaptation: number | null;
  ccMitigation: number | null;
  ccTypologyCode: string | null;
  pdpRdp: string | null;
  sdgs: string | null;
  sendaiFramework: string | null;
  ndrrmPlan: string | null;
  nsp: string | null;
  pdpdfp: string | null;
}

/**
 * One sector group under a document. Name is the office/sub-office display name —
 * it may differ per sector while sharing the same config office ref code.
 */
export interface LdipOfficeGroup {
  id: number;
  refCode: string;
  name: string;
  sector: LdipSector;
  programs: LdipProgram[];
}

export interface LdipRecordDetail extends LdipRecord {
  groups: LdipOfficeGroup[];
}

/**
 * Detail fields (RAL-113) are only ever set by the upload-confirm flow (echoed
 * back from the preview response) — the manual "+ Add Program" form only ever
 * sends name/budget, leaving the rest undefined.
 */
export interface SaveLdipProgram {
  name: string;
  budget: number;
  implementingOffice?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  expectedOutputs?: string | null;
  fundingSourceRaw?: string | null;
  ps?: number | null;
  mooe?: number | null;
  co?: number | null;
  ccAdaptation?: number | null;
  ccMitigation?: number | null;
  ccTypologyCode?: string | null;
  pdpRdp?: string | null;
  sdgs?: string | null;
  sendaiFramework?: string | null;
  ndrrmPlan?: string | null;
  nsp?: string | null;
  pdpdfp?: string | null;
}

export interface SaveLdipGroup {
  sector: LdipSector;
  name: string;
  programs: SaveLdipProgram[];
}

export interface CreateLdipRequest {
  /** Blank = server auto-generates "LDIP {start}-{end} — {office name}". */
  title: string;
  fiscalYearStart: number;
  fiscalYearEnd: number;
  entryMode: LdipEntryMode;
  officeId: number;
  groups: SaveLdipGroup[];
}

export interface UpdateLdipRequest {
  title: string;
  fiscalYearStart: number;
  fiscalYearEnd: number;
  entryMode: LdipEntryMode;
  officeId: number;
  groups: SaveLdipGroup[];
}

// ── LDIP file upload (RAL-113) ────────────────────────────────────────────────
// The workbook covers every office — there is no office picker. Every office
// block found in the file is matched to a Config office by AIP ref code and
// grouped below; Confirm creates one Draft LDIP record per office.

export interface LdipImportOfficeResult {
  officeId: number;
  officeCode: string;
  officeName: string;
  groups: SaveLdipGroup[];
}

export interface LdipImportCounts {
  offices: number;
  groups: number;
  programs: number;
}

/**
 * Returned by POST /api/budget-planning/ldip/upload. Each entry in `offices` is
 * echoed straight back to /confirm.
 */
export interface LdipImportPreviewResponse {
  fiscalYearStart: number;
  fiscalYearEnd: number;
  offices: LdipImportOfficeResult[];
  counts: LdipImportCounts;
  warnings: string[];
}

export interface LdipImportConfirmRequest {
  fiscalYearStart: number;
  fiscalYearEnd: number;
  offices: LdipImportOfficeResult[];
  /**
   * RAL-114 — when set, re-uploads a corrected file INTO this existing record
   * (full-replaces its hierarchy, same Id/RefCode) instead of creating a new one.
   * The target must be a Draft, Upload-entry-mode record. Omit to create a new record.
   */
  targetRecordId?: number;
}
