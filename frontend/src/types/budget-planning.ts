/** Budget Planning Dashboard types — mirrors PPDO.Application/DTOs/BudgetPlanning/ */

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
