/**
 * PGOM Investment Proposal API calls (v1.8.0 Demo 2.15 — PPDO-159,
 * `docs/v1.8/Investment_Proposal_Spec.md` §4).
 *
 * ⚠️ **Scope is the server's.** A guest office is pinned to its own projects and an out-of-scope id
 * answers 404, whatever this file sends. Nothing here hides anything that matters.
 */

import api from "./api";
import type {
  ApiResponse, Proposal, ProposalConflict, ProposalContent, ProposalCreated, ProposalListPage,
  ProposalProjectOption,
} from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

export interface ProposalListParams {
  fiscalYear: number;
  /** Omitted, never null: the server reads "absent" as every office in the caller's scope. */
  officeId?: number | null;
  search?: string;
  page: number;
  pageSize: number;
}

/** The fiscal year's AIP projects in scope, each with its proposal status. Server-paginated. */
export async function getProposalList(p: ProposalListParams): Promise<ProposalListPage> {
  const params: Record<string, string | number> = { fiscalYear: p.fiscalYear, page: p.page, pageSize: p.pageSize };
  if (p.officeId != null) params.officeId = p.officeId;
  if (p.search?.trim()) params.search = p.search.trim();
  const { data } = await api.get<ApiResponse<ProposalListPage>>("/budget-planning/proposals", { params });
  return unwrap(data);
}

/**
 * One office's projects for the year, unpaged (AIP Entry's strip, the editor's picker). A guest
 * office's `officeId` is ignored by the server; a caller who sees several offices must pass one.
 */
export async function getProposalProjects(fiscalYear: number, officeId: number | null): Promise<ProposalProjectOption[]> {
  const params: Record<string, number> = { fiscalYear };
  if (officeId != null) params.officeId = officeId;
  const { data } = await api.get<ApiResponse<ProposalProjectOption[]>>("/budget-planning/proposals/projects", { params });
  return unwrap(data);
}

/** The outcome of a create: the new proposal, or the one that already exists (a 409). */
export type CreateProposalOutcome =
  | { kind: "created"; proposal: ProposalCreated }
  | { kind: "exists"; proposalId: number };

/**
 * Creates the proposal for an AIP project, pre-filled from the AIP (decisions 11, 12).
 *
 * ⚠️ **A 409 is not a failure here.** It means someone created this project's proposal since the
 * list loaded, and the server names it in `data.proposalId` — the caller opens that one instead.
 * Every other refusal is re-thrown for `proposalErrorMessage`.
 */
export async function createProposal(aipProjectId: number): Promise<CreateProposalOutcome> {
  try {
    const { data } = await api.post<ApiResponse<ProposalCreated>>("/budget-planning/proposals", { aipProjectId });
    return { kind: "created", proposal: unwrap(data) };
  } catch (err) {
    const res = (err as { response?: { status?: number; data?: ApiResponse<{ proposalId?: number }> } })?.response;
    const existing = res?.status === 409 ? res.data?.data?.proposalId : undefined;
    if (typeof existing === "number") return { kind: "exists", proposalId: existing };
    throw err;
  }
}

/** The server's sentence for a refused call, else `fallback`. */
export function proposalErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

/**
 * The file name the server sends, rebuilt here: Content-Disposition is not readable cross-origin.
 * Same rule as `InvestmentProposalDocumentBuilder.FileName` — characters Windows refuses become "_",
 * whitespace collapses, at most 120 characters with the extension.
 */
export function proposalFileName(projectRefCode: string, projectName: string): string {
  let stem = `Investment Proposal - ${projectRefCode} - ${projectName}`
    // eslint-disable-next-line no-control-regex
    .replace(/[\\/:*?"<>|\x00-\x1F]/g, "_")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/\.+$/, "");
  if (stem.length > 115) stem = stem.slice(0, 115).trimEnd();
  return `${stem}.docx`;
}

/**
 * Downloads the proposal as Word (PPDO-158). Axios rather than a link, because the JWT goes in the
 * Authorization header.
 *
 * ⚠️ **A refusal arrives as a Blob**, since the request asked for one. Its JSON envelope is parsed
 * back onto the error so `proposalErrorMessage` shows the server's sentence.
 */
export async function exportProposal(proposalId: number, fileName: string): Promise<void> {
  let response;
  try {
    response = await api.get<Blob>(`/budget-planning/proposals/${proposalId}/export`, { responseType: "blob" });
  } catch (err) {
    const res = (err as { response?: { data?: unknown } })?.response;
    if (res?.data instanceof Blob) {
      try {
        res.data = JSON.parse(await res.data.text()) as ApiResponse<unknown>;
      } catch {
        // Not an envelope — the caller's fallback message applies.
      }
    }
    throw err;
  }

  const url = URL.createObjectURL(response.data);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/** The editor's route for a proposal (PPDO-160). Query-string id: the app is a static export. */
export function proposalEditorHref(proposalId: number): string {
  return `/budget-planning/proposals/edit?id=${proposalId}`;
}

// ── The editor (PPDO-160) ────────────────────────────────────────────────────────────────────────

export async function getProposal(id: number): Promise<Proposal> {
  const { data } = await api.get<ApiResponse<Proposal>>(`/budget-planning/proposals/${id}`);
  return unwrap(data);
}

/**
 * A write's outcome. The two refusals the editor shows in place are typed; anything else (403, 404,
 * network) is `failed` with the server's sentence.
 *
 * - `stale` — someone saved since this tab loaded (409 with `data.currentRowVersion`). The message is
 *   the server's "{Name} saved this proposal at {time}. Reload to see their changes."
 * - `invalid` — field errors keyed by the client's own paths, e.g. `teamMembers[2].sex` (400).
 */
export type ProposalWriteOutcome =
  | { kind: "ok"; proposal: Proposal }
  | { kind: "stale"; message: string; conflict: ProposalConflict }
  | { kind: "invalid"; message: string; errors: Record<string, string[]> }
  | { kind: "failed"; message: string; status: number | null };

async function write(run: () => Promise<{ data: ApiResponse<Proposal> }>, fallback: string): Promise<ProposalWriteOutcome> {
  try {
    const { data } = await run();
    return { kind: "ok", proposal: unwrap(data) };
  } catch (err) {
    const res = (err as { response?: { status?: number; data?: ApiResponse<unknown> } })?.response;
    const body = res?.data;
    const message = body?.error ?? body?.message ?? fallback;
    const payload = body?.data as Partial<ProposalConflict> & { errors?: Record<string, string[]> } | null | undefined;
    if (res?.status === 409 && payload?.currentRowVersion)
      return { kind: "stale", message, conflict: payload as ProposalConflict };
    if (res?.status === 400 && payload?.errors)
      return { kind: "invalid", message, errors: payload.errors };
    return { kind: "failed", message, status: res?.status ?? null };
  }
}

/** PUT — the whole editable document, with the version this tab loaded. */
export function updateProposal(id: number, rowVersion: string, content: ProposalContent): Promise<ProposalWriteOutcome> {
  return write(
    () => api.put<ApiResponse<Proposal>>(`/budget-planning/proposals/${id}`, { rowVersion, content }),
    "Couldn't save. Your changes are still here.");
}

export function finalizeProposal(id: number, rowVersion: string): Promise<ProposalWriteOutcome> {
  return write(() => api.post<ApiResponse<Proposal>>(`/budget-planning/proposals/${id}/finalize`, { rowVersion }),
    "Couldn't finalize the proposal.");
}

export function reopenProposal(id: number, rowVersion: string): Promise<ProposalWriteOutcome> {
  return write(() => api.post<ApiResponse<Proposal>>(`/budget-planning/proposals/${id}/reopen`, { rowVersion }),
    "Couldn't reopen the proposal.");
}

/** DELETE. Resolves null on success, else the refusal's sentence. */
export async function deleteProposal(id: number, rowVersion: string): Promise<string | null> {
  try {
    await api.delete(`/budget-planning/proposals/${id}`, { params: { rowVersion } });
    return null;
  } catch (err) {
    return proposalErrorMessage(err, "Couldn't delete the proposal.");
  }
}
