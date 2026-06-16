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
}

export interface ParsedAipProgramResponse {
  refCode: string;
  name: string;
  projects: ParsedAipProjectResponse[];
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
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export interface StatusBreakdown {
  status: string;
  count: number;
}

export interface LdipSummary {
  total: number;
  breakdown: StatusBreakdown[];
}

export interface AipSummary {
  total: number;
  breakdown: StatusBreakdown[];
}

export interface WfpSummary {
  finalCount: number;
  activeOfficeCount: number;
}

export interface WfpOfficeStatus {
  officeId: number;
  officeName: string;
  wfpStatus: "Draft" | "Final" | "Not started";
  aipRecordId: number | null;
}

export interface PlanningDashboard {
  fiscalYear: number;
  availableFiscalYears: number[];
  ldip: LdipSummary;
  aip: AipSummary;
  wfp: WfpSummary;
  wfpByOffice: WfpOfficeStatus[];
}

export interface RecentActivity {
  id: number;
  changedAt: string; // ISO 8601
  tableName: string;
  action: string;
  recordId: number;
  actorName: string;
}
