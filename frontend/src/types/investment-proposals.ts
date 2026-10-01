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
