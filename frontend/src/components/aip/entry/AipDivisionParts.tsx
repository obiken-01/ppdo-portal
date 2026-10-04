"use client";

/**
 * The division pieces of AIP Entry (PPDO-151, `Division_Submit_Spec.md` §6.1).
 *
 * ⚠️ **Wording only — never the lock itself.** Whether an activity can be written is the server's
 * `AipActivityDto.canEdit`, computed per caller. What lives here decides only which *sentence*
 * explains a lock the server already applied, so a rule changed in `AipDivisionContext` can never
 * make the page offer a write the API refuses. If a sentence and `canEdit` ever disagree, `canEdit`
 * wins and the sentence is the bug.
 */

import type { AipActivityDetail, AipDivisionStatus, AipDivisionStatusList } from "@/types";
import { isOfficeEditable } from "@/lib/aip-workflow";

/**
 * Where the signed-in user stands in their office's division flow, worked out once on the page.
 * Null when the office does not submit by division (no divisions, or FY ≤ 2027) — every consumer
 * then renders exactly what it did before PPDO-130.
 */
export interface AipDivisionView {
  list: AipDivisionStatusList;
  /** The caller's own division in this office, or null (a department head, or unassigned Staff). */
  mine: AipDivisionStatus | null;
  /**
   * Admin/SuperAdmin or the `CanReviewBudgetPlanning` holder — the server's `IsDepartmentHeadAsync`.
   * Only ever about the caller's OWN office, which is the only office this page edits.
   */
  isHead: boolean;
}

export function buildDivisionView(
  list: AipDivisionStatusList | null,
  myDivisionId: number | null,
  isHead: boolean,
): AipDivisionView | null {
  if (!list?.hasDivisions) return null;
  // ⚠️ Active only, like the server's `member` test: a user left on a deactivated division is not a
  // member of anything here, and is read-only until reassigned.
  const mine = list.divisions.find((d) => d.divisionId === myDivisionId && d.isActive) ?? null;
  return { list, mine, isHead };
}

/** Staff in a divisioned office with no division of it — read-only everywhere (spec §6.1 "No division"). */
export function isUnassignedEncoder(view: AipDivisionView | null): boolean {
  return view != null && view.mine == null && !view.isHead;
}

/**
 * Why a new activity cannot be added here, or null. An encoder whose division has submitted is
 * refused by the server (PPDO-148 deviation 2), so the control says so instead of failing on save.
 */
export function addActivityBlockedReason(view: AipDivisionView | null): string | null {
  if (!view || view.isHead || !view.mine) return null;
  return view.mine.status === "Submitted"
    ? `${view.mine.name} has been submitted — activities cannot be added unless your department head returns it.`
    : null;
}

/**
 * The sentence for an activity the server has locked **for a division reason**, or null when the
 * lock (if any) is the office's — that one already names its holder through `lockedReason`.
 */
export function activityDivisionLock(
  activity: AipActivityDetail, view: AipDivisionView | null,
): string | null {
  if (!view || activity.canEdit) return null;
  // ⚠️ `canEdit` is false for reasons that are not divisions too — the office sent on, or a
  // cross-office reviewer's write denial. Claim a division reason only where the division rule
  // is the one that bites, or the banner would blame a division for someone else's lock.
  if (!isOfficeEditable(view.list.officeWorkflowStatus)) return null;
  // The division lock never binds the department head (spec decision 2).
  if (view.isHead) return null;
  if (activity.divisionId == null) {
    return "This activity has no division yet. Ask your department head to assign it.";
  }
  if (view.mine && activity.divisionId === view.mine.divisionId) {
    return view.mine.status === "Submitted"
      ? "Submitted — your department head can return it if changes are needed."
      : null;
  }
  return `Belongs to ${activity.divisionName ?? "another division"}.`;
}

/** The small division tag on an activity (spec §6.1). Renders nothing outside the division flow. */
export function AipDivisionPill({
  activity, view, locked = false,
}: {
  activity: AipActivityDetail;
  view: AipDivisionView | null;
  /** Adds the lock glyph — the row cannot be written by this reader. */
  locked?: boolean;
}) {
  if (!view) return null;
  const label = activity.divisionName ?? "No division";
  const title = locked
    ? activity.divisionName ? `Belongs to ${activity.divisionName}` : "No division assigned yet"
    : `Division: ${label}`;
  return (
    <span
      title={title}
      className={`inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
        activity.divisionName ? "bg-green-50 text-green-800" : "bg-amber-50 text-amber-800"
      }`}
    >
      {/* An emoji, per DESIGN_SYSTEM.md §5 — not a lucide import. */}
      {locked && <span aria-label="Locked">🔒</span>}
      {label}
    </span>
  );
}

/**
 * The divisions a department head may put work in (PPDO-152): the office's active ones, plus the one
 * `keepId` already names when it has since been deactivated — so a select opened on such an
 * activity still shows where it is instead of silently reading as another division.
 */
export function divisionChoices(view: AipDivisionView, keepId: number | null = null): AipDivisionStatus[] {
  return view.list.divisions.filter((d) => d.isActive || d.divisionId === keepId);
}

/**
 * A plain division `<select>` (PPDO-152) — the re-tag control on an activity, and the "new activities
 * go to" choice on a project. Department head only; callers decide that, this only renders.
 *
 * ⚠️ No "No division" option. The server refuses to untag in a divisioned office (§4, `[JsonRequired]`),
 * so an untagged activity shows a disabled placeholder until one is picked.
 */
export function AipDivisionSelect({
  view, value, onChange, disabled = false, label, keepId = null,
}: {
  view: AipDivisionView;
  value: number | null;
  onChange: (divisionId: number) => void;
  disabled?: boolean;
  /** Accessible name — also shown as a small caption before the control. */
  label: string;
  keepId?: number | null;
}) {
  return (
    <label className="inline-flex items-center gap-1.5 text-xs text-slate-600">
      <span>{label}</span>
      <select
        value={value ?? ""}
        disabled={disabled}
        onChange={(e) => onChange(Number(e.target.value))}
        className="border border-slate-300 bg-white px-2 py-1 text-xs text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-50 disabled:text-slate-600"
      >
        {value == null && <option value="" disabled>Choose a division…</option>}
        {divisionChoices(view, keepId).map((d) => (
          <option key={d.divisionId} value={d.divisionId}>
            {d.name}{d.isActive ? "" : " (inactive)"}
          </option>
        ))}
      </select>
    </label>
  );
}

/** A submitted-at stamp in Manila time, as every other AIP hand-off shows it. */
export function fmtDivisionStamp(iso: string | null): string {
  if (!iso) return "";
  return new Date(iso).toLocaleDateString("en-PH", {
    timeZone: "Asia/Manila", year: "numeric", month: "short", day: "numeric",
  });
}
