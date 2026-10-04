/**
 * PGOM Investment Proposal types (v1.8.0 Demo 2.15 — `docs/v1.8/Investment_Proposal_Spec.md` §4).
 * Mirrors `PPDO.Application/DTOs/InvestmentProposal/InvestmentProposalDtos.cs`.
 */

/** "None" means the project has no proposal yet. */
export type ProposalStatus = "None" | "Draft" | "Final";

/** One row of `GET /budget-planning/proposals`: an AIP project and its proposal status. */
export interface ProposalListItem {
  aipProjectId: number;
  projectRefCode: string;
  projectName: string;
  programName: string;
  officeName: string;
  /** Full pesos, Σ activity totals, live. Not the Annex B figure (decision 7). */
  projectCost: number;
  proposalId: number | null;
  status: ProposalStatus;
  updatedAt: string | null;
  updatedByName: string | null;
}

export interface ProposalListPage {
  items: ProposalListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** One project of `GET /budget-planning/proposals/projects` — the editor's picker and AIP Entry's strip. */
export interface ProposalProjectOption {
  aipProjectId: number;
  projectRefCode: string;
  projectName: string;
  programId: number;
  programRefCode: string;
  programName: string;
  proposalId: number | null;
  status: ProposalStatus;
  updatedAt: string | null;
  updatedByName: string | null;
}

/**
 * The fields of `ProposalDto` the list and the strip read after a create. The editor (PPDO-160)
 * widens this to the whole document.
 */
export interface ProposalCreated {
  id: number;
  status: ProposalStatus;
}

// ── The proposal (GET /proposals/{id} and every write's response) — PPDO-160 ─────────────────────

export interface ProposalHeader {
  aipProjectId: number;
  programId: number;
  aipOfficeId: number;
  officeId: number | null;
  fiscalYear: number;
  programTitle: string;
  projectRefCode: string;
  projectTitle: string;
  proponent: string;
  scheduleStart: string | null;
  scheduleEnd: string | null;
  projectCost: number;
  fundingSources: string[];
  climateTypology: string;
  attributedGadBudget: number | null;
}

export interface ProposalActivityRef {
  activityId: number;
  refCode: string;
  name: string;
}

export interface ProposalItem {
  name: string;
  unitPrice: number;
  qty: number;
  unit: string;
  numberOfDays: number;
  lineTotal: number;
}

export interface ProposalExpenditure {
  accountCode: string | null;
  accountTitle: string | null;
  ps: number;
  mooe: number;
  co: number;
  total: number;
  fundName: string | null;
  items: ProposalItem[];
}

/** One AIP activity, read-only, for G's automatic columns and the H-1 preview. */
export interface ProposalAipRow {
  activityId: number;
  refCode: string;
  name: string;
  timeline: string | null;
  opr: string | null;
  ps: number;
  mooe: number;
  co: number;
  total: number;
  fundNames: string[];
  expenditures: ProposalExpenditure[];
}

export interface ProposalBeneficiary { id?: number | null; indicator: string | null; male: number | null; female: number | null }
export interface ProposalTargetBeneficiary { id?: number | null; kind: "Direct" | "Indirect"; name: string | null; male: number | null; female: number | null }
export interface ProposalBenefit { sector: string; benefit: string | null; cost: string | null }
export interface ProposalLogframe { level: "Impact" | "Outcome" | "Output" | "Input"; target: string | null; verification: string | null }
/** `clientKey` is how rows point at a group in one body; the server answers "g{id}". */
export interface ProposalGroup { id?: number | null; clientKey: string; label: string | null }
export interface ProposalWorkPlanRow {
  id?: number | null;
  /** Set for an AIP row; null for a proposal-only step, which has its own name/timeline/opr. */
  aipActivityId: number | null;
  name: string | null;
  groupKey: string | null;
  performanceTarget: string | null;
  genderIssues: string | null;
  timeline: string | null;
  opr: string | null;
}
export interface ProposalTeamMember {
  id?: number | null; name: string | null; sex: "M" | "F" | null;
  gadTrainings: string | null; expertise: string | null; requiredTraining: string | null;
}
export interface ProposalMonitoring { id?: number | null; phase: "Pre" | "During" | "Post"; activity: string | null; schedule: string | null; tools: string | null }
export interface ProposalRisk { id?: number | null; risk: string | null; prevention: string | null; monitoring: string | null }
export interface ProposalSignatory { slot: 1 | 2 | 3 | 4; label: string | null; name: string | null; position: string | null }

/** Everything the user edits — the PUT body's `content`. */
export interface ProposalContent {
  projectLocation: string | null;
  hgdgChecklist: string | null;
  hgdgScore: number | null;
  beneficiariesSummary: ProposalBeneficiary[];
  description: string | null;
  rationale: string | null;
  benefits: ProposalBenefit[];
  generalObjective: string | null;
  logframe: ProposalLogframe[];
  directSameAsSummary: boolean;
  targetBeneficiaries: ProposalTargetBeneficiary[];
  groups: ProposalGroup[];
  workPlan: ProposalWorkPlanRow[];
  projectSupervisor: string | null;
  projectManager: string | null;
  teamMembers: ProposalTeamMember[];
  partnershipSustainability: string | null;
  monitoring: ProposalMonitoring[];
  risks: ProposalRisk[];
  womensImpactStrategy: string | null;
  signatories: ProposalSignatory[];
}

export interface Proposal {
  id: number;
  status: "Draft" | "Final";
  rowVersion: string;
  finalizedAt: string | null;
  finalizedByName: string | null;
  updatedAt: string;
  updatedByName: string | null;
  aipChangedSinceFinal: boolean;
  canEdit: boolean;
  canReopen: boolean;
  header: ProposalHeader;
  warnings: { activitiesWithoutLines: ProposalActivityRef[] };
  content: ProposalContent;
  aipRows: ProposalAipRow[];
}

/** The data of a stale-version 409. */
export interface ProposalConflict {
  currentRowVersion: string;
  updatedByName: string | null;
  updatedAt: string;
}
