/**
 * The investment proposal editor's rules, kept out of the components (Demo 2.15 — PPDO-160,
 * `Investment_Proposal_Spec.md` §6.2 and decisions 9, 14–18, 24, 28).
 *
 * - **Sections** — which content fields each section card owns, so "Save section" can send that
 *   section's edits plus every other section's LAST-SAVED values in the one PUT (decision 24).
 * - **Dirty** — compared on a normalised shape that ignores row ids and maps group keys to group
 *   positions. Every save replaces the child rows, so ids and "g{id}" keys change under a section
 *   nobody touched; comparing them raw would mark it unsaved after any other section's save.
 * - **The G / H-1 arrangement and the computation line** — a port of
 *   `InvestmentProposalDocumentBuilder` (PPDO-157). ⚠️ The preview must match the export line for
 *   line; change one, change both.
 * - **HGDG** — a mirror of `HgdgAttribution` and `HgdgChecklists` for the live GAD budget and the
 *   picker. ⚠️ Both are PROVISIONAL on the server until the PCW manual is checked; keep them in step.
 */

import type {
  ProposalAipRow, ProposalBeneficiary, ProposalContent, ProposalExpenditure, ProposalItem,
  ProposalWorkPlanRow,
} from "@/types";

// ── Sections ──────────────────────────────────────────────────────────────────────────────────────

export type SectionKey = "A" | "B" | "C" | "D" | "E" | "F" | "G" | "H" | "I" | "J" | "K" | "L" | "M" | "S";

type ContentField = keyof ProposalContent;

export interface SectionDef {
  key: SectionKey;
  /** The rail's short name. */
  short: string;
  /** The template heading, word for word (decision 30). */
  title: string;
  /** Filled from the AIP: no inputs, no Save (H and M). */
  fromAip: boolean;
  fields: ContentField[];
}

export const SECTIONS: SectionDef[] = [
  { key: "A", short: "Project Summary", title: "PROJECT SUMMARY", fromAip: false,
    fields: ["projectLocation", "hgdgChecklist", "hgdgScore", "beneficiariesSummary"] },
  { key: "B", short: "Project Description", title: "PROJECT DESCRIPTION", fromAip: false, fields: ["description"] },
  { key: "C", short: "Rationale", title: "RATIONALE/BACKGROUND", fromAip: false, fields: ["rationale"] },
  { key: "D", short: "Benefits and Costs", title: "PROJECT BENEFITS AND COSTS", fromAip: false, fields: ["benefits"] },
  { key: "E", short: "Logical Framework", title: "LOGICAL FRAMEWORK", fromAip: false, fields: ["generalObjective", "logframe"] },
  { key: "F", short: "Target Beneficiaries", title: "TARGET BENEFICIARIES", fromAip: false,
    fields: ["directSameAsSummary", "targetBeneficiaries"] },
  { key: "G", short: "Work Plan", title: "IMPLEMENTATION SCHEDULE /WORK PLAN", fromAip: false, fields: ["groups", "workPlan"] },
  { key: "H", short: "Annex H-1 Cost", title: "ESTIMATED COST/BUDGETARY REQUIREMENTS (Annex H-1)", fromAip: true, fields: [] },
  { key: "I", short: "Implementing Team", title: "IMPLEMENTING TEAM", fromAip: false,
    fields: ["projectSupervisor", "projectManager", "teamMembers"] },
  { key: "J", short: "Partnership", title: "PARTNERSHIP AND SUSTAINABILITY", fromAip: false, fields: ["partnershipSustainability"] },
  { key: "K", short: "Monitoring & Eval.", title: "MONITORING AND EVALUATION", fromAip: false, fields: ["monitoring"] },
  { key: "L", short: "Risk Management", title: "RISK MANAGEMENT", fromAip: false, fields: ["risks", "womensImpactStrategy"] },
  { key: "M", short: "CC Typology", title: "CLIMATE CHANGE EXPENDITURE TYPOLOGY", fromAip: true, fields: [] },
  { key: "S", short: "Signatories", title: "SIGNATORIES", fromAip: false, fields: ["signatories"] },
];

export const SECTION_BY_KEY = Object.fromEntries(SECTIONS.map((s) => [s.key, s])) as Record<SectionKey, SectionDef>;

/** The rail label: "A", …, and a pencil for the signatures (wireframe 1). */
export function sectionLetter(key: SectionKey): string {
  return key === "S" ? "✎" : key;
}

/** `target` with section `key`'s fields taken from `source`. */
export function withSection(target: ProposalContent, source: ProposalContent, key: SectionKey): ProposalContent {
  const next = { ...target } as Record<ContentField, unknown>;
  for (const f of SECTION_BY_KEY[key].fields) next[f] = source[f];
  return next as unknown as ProposalContent;
}

// ── Dirty ─────────────────────────────────────────────────────────────────────────────────────────

/** Ids dropped, empty strings as null, group keys as group positions. */
function normalised(content: ProposalContent, key: SectionKey): unknown {
  const groupIndex = new Map(content.groups.map((g, i) => [g.clientKey, i]));
  const strip = (v: unknown): unknown => {
    if (Array.isArray(v)) return v.map(strip);
    if (v && typeof v === "object") {
      const out: Record<string, unknown> = {};
      for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
        if (k === "id" || k === "clientKey") continue;
        out[k] = k === "groupKey" ? (val == null ? null : groupIndex.get(val as string) ?? -1) : strip(val);
      }
      return out;
    }
    return v === "" ? null : v;
  };
  return SECTION_BY_KEY[key].fields.map((f) => strip(content[f]));
}

export function isSectionDirty(working: ProposalContent, saved: ProposalContent, key: SectionKey): boolean {
  if (SECTION_BY_KEY[key].fromAip) return false;
  return JSON.stringify(normalised(working, key)) !== JSON.stringify(normalised(saved, key));
}

/** Whether a section has anything in it yet (the rail's "not started"). */
export function isSectionStarted(c: ProposalContent, key: SectionKey): boolean {
  const filled = (v: string | null | undefined) => v != null && v.trim() !== "";
  switch (key) {
    case "A": return filled(c.projectLocation) || c.hgdgChecklist != null || c.hgdgScore != null || c.beneficiariesSummary.length > 0;
    case "B": return filled(c.description);
    case "C": return filled(c.rationale);
    case "D": return c.benefits.some((b) => filled(b.benefit) || filled(b.cost));
    case "E": return filled(c.generalObjective) || c.logframe.some((l) => filled(l.target) || filled(l.verification));
    case "F": return c.targetBeneficiaries.length > 0 || c.beneficiariesSummary.length > 0;
    case "G": return c.workPlan.some((r) => filled(r.performanceTarget) || filled(r.genderIssues) || r.aipActivityId == null) || c.groups.length > 0;
    case "I": return filled(c.projectSupervisor) || filled(c.projectManager) || c.teamMembers.length > 0;
    case "J": return filled(c.partnershipSustainability);
    case "K": return c.monitoring.length > 0;
    case "L": return c.risks.length > 0 || filled(c.womensImpactStrategy);
    case "S": return c.signatories.some((s) => filled(s.name));
    default: return true;
  }
}

/** Which section a server error path belongs to: "teamMembers[1].sex" → I. */
export function sectionOfErrorPath(path: string): SectionKey | null {
  const field = path.split(/[.[]/)[0] as ContentField;
  return SECTIONS.find((s) => s.fields.includes(field))?.key ?? null;
}

// ── Beneficiaries ─────────────────────────────────────────────────────────────────────────────────

export function rowTotal(male: number | null, female: number | null): number | null {
  return male == null && female == null ? null : (male ?? 0) + (female ?? 0);
}

/** The TOTAL row; a column with no counts at all stays blank (the export's rule). */
export function beneficiaryTotals(rows: { male: number | null; female: number | null }[]) {
  const sum = (pick: (r: { male: number | null; female: number | null }) => number | null) =>
    rows.some((r) => pick(r) != null) ? rows.reduce((t, r) => t + (pick(r) ?? 0), 0) : null;
  return { male: sum((r) => r.male), female: sum((r) => r.female), total: sum((r) => rowTotal(r.male, r.female)) };
}

export function copySummaryToDirect(rows: ProposalBeneficiary[]) {
  return rows.map((b) => ({ kind: "Direct" as const, name: b.indicator, male: b.male, female: b.female }));
}

// ── HGDG (mirrors HgdgAttribution / HgdgChecklists — provisional, see the header) ────────────────

export const HGDG_CHECKLISTS: { code: string; name: string }[] = [
  { code: "GENERIC", name: "Generic checklist (design)" },
  { code: "AGRI", name: "Agriculture and agrarian reform" },
  { code: "NRM", name: "Natural resource management" },
  { code: "INFRA", name: "Infrastructure" },
  { code: "PSD", name: "Private sector development" },
  { code: "WMSE", name: "Women's micro and small enterprises" },
  { code: "EDUC", name: "Education" },
  { code: "HEALTH", name: "Health" },
  { code: "JUSTICE", name: "Justice" },
  { code: "MICROFIN", name: "Microfinance" },
  { code: "LABOR", name: "Labor and employment" },
  { code: "HOUSING", name: "Housing and settlement" },
  { code: "MIGRATION", name: "Migration" },
  { code: "TOURISM", name: "Tourism" },
  { code: "ICT", name: "Information and communications technology" },
  { code: "VAW", name: "Violence against women" },
  { code: "CHILDLAB", name: "Child labor" },
];

export function hgdgPercent(score: number | null): number | null {
  if (score == null || Number.isNaN(score) || score < 0 || score > 20) return null;
  return score < 4 ? 0 : score < 8 ? 25 : score < 15 ? 50 : score < 20 ? 75 : 100;
}

/** Project Cost × the band's percentage, to the centavo — the screen's live copy of the server's figure. */
export function attributedGadBudget(score: number | null, projectCost: number): number | null {
  const pct = hgdgPercent(score);
  return pct == null ? null : Math.round(projectCost * pct) / 100;
}

// ── G and H-1 (a port of InvestmentProposalDocumentBuilder.Arrange) ───────────────────────────────

export type Arranged =
  | { kind: "group"; label: string; groupKey: string }
  | { kind: "row"; row: ProposalWorkPlanRow; index: number; activity: ProposalAipRow | null; number: number };

/**
 * Decision 14: groups in their order, each followed by its rows in work-plan order; empty groups
 * skipped; ungrouped rows (and rows whose group no longer exists) after every group. Rows are
 * numbered 1, 2, 3… straight through. `index` is the row's position in `workPlan`, for editing.
 */
export function arrange(content: ProposalContent, aipRows: ProposalAipRow[]): Arranged[] {
  const byId = new Map(aipRows.map((a) => [a.activityId, a]));
  const rows = content.workPlan
    .map((row, index) => ({ row, index }))
    .filter(({ row }) => (row.aipActivityId != null ? byId.has(row.aipActivityId) : true));
  const keys = new Set(content.groups.map((g) => g.clientKey));
  const out: Arranged[] = [];
  let n = 0;
  const push = ({ row, index }: { row: ProposalWorkPlanRow; index: number }) =>
    out.push({ kind: "row", row, index, activity: row.aipActivityId != null ? byId.get(row.aipActivityId) ?? null : null, number: ++n });

  for (const g of content.groups) {
    const members = rows.filter((r) => r.row.groupKey === g.clientKey);
    if (members.length === 0) continue;
    out.push({ kind: "group", label: g.label ?? "", groupKey: g.clientKey });
    members.forEach(push);
  }
  rows.filter((r) => r.row.groupKey == null || !keys.has(r.row.groupKey)).forEach(push);
  return out;
}

const N2 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const COUNT = new Intl.NumberFormat("en-US", { maximumFractionDigits: 4 });

/** "35,460.00" — the export's money format. */
export function money(n: number): string {
  return N2.format(n);
}

/** "{Name}  {UnitPrice} x {Qty} {Unit}[ x {Days} days] = {LineTotal}" (decision 17). */
export function computationLine(i: ProposalItem): string {
  const unit = i.unit.trim() ? ` ${i.unit.trim()}` : "";
  const days = i.numberOfDays !== 1 ? ` x ${COUNT.format(i.numberOfDays)} days` : "";
  return `${i.name.trim()}  ${money(i.unitPrice)} x ${COUNT.format(i.qty)}${unit}${days} = ${money(i.lineTotal)}`;
}

export interface AnnexBlock { heading: string; lines: string[]; amount: number | null }
export interface AnnexCell { blocks: AnnexBlock[]; total: number }

/** One money cell: the class's expenditure blocks, then the activity's class total. Null when empty. */
export function annexCell(activity: ProposalAipRow, cls: "mooe" | "ps" | "co"): AnnexCell | null {
  const blocks = activity.expenditures
    .filter((e) => e[cls] !== 0)
    .map((e) => annexBlock(e, e[cls]));
  const total = activity[cls];
  return blocks.length === 0 && total === 0 ? null : { blocks, total };
}

function annexBlock(e: ProposalExpenditure, amount: number): AnnexBlock {
  const title = e.accountTitle?.trim() || e.accountCode?.trim() || "Other";
  const heading = title.endsWith(":") ? title : `${title}:`;
  // Item lines only when the expenditure sits in one class; split, they would add up to neither.
  const classes = [e.ps, e.mooe, e.co].filter((x) => x !== 0).length;
  return e.items.length > 0 && classes === 1
    ? { heading, lines: e.items.map(computationLine), amount: null }
    : { heading, lines: [], amount };
}
