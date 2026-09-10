/**
 * The money the AIP form prints on a row — PS, MOOE, CO, Total, and the two climate-change
 * columns — plus the funding sources that row draws on (PPDO-80).
 *
 * ⚠️ **Why the entry tree needed this at all.** The tree showed one figure per activity, the
 * Total, and nothing at the office level. The printed Annex B shows the office subtotal across
 * columns (8)–(13) and each activity's own split, and an encoder reconciles the screen against
 * that sheet cell by cell. A single Total cannot be checked against a form that never prints one
 * on its own; it can only be checked against the sum of three columns the screen was not showing.
 *
 * ⚠️ **Office and activity rows only, deliberately.** Program and project rows print blank on the
 * form, and filling them in here would invent a reconciliation step the province does not do. The
 * levels in between are structure, not money.
 *
 * ⚠️ **Every figure is ₱000**, like every other read-only amount on an AIP surface — `fmtThousands`
 * from `lib/aip-units`, never a bare `toLocaleString`. The strip carries its own unit caption for
 * that reason: the office row sits above a tree of figures in the same unit, and a strip that did
 * not name its unit is exactly how pesos ended up under a `(in ₱000)` header once already.
 *
 * Kept in `components/` rather than inline because Phase 4's review tab renders the same rows —
 * the same argument that put `AipHierarchy` here.
 */

import { fmtThousands } from "@/lib/aip-units";

/**
 * What one row's figures are, whatever level produced them.
 *
 * `total` is separate from PS+MOOE+CO rather than derived, because on an activity it carries a
 * meaning the components cannot: **null is "never costed" and 0 is "costed at zero"** (V18-34),
 * and adding three nulls would collapse that distinction into 0.
 */
export interface AipRowAmounts {
  ps: number | null;
  mooe: number | null;
  co: number | null;
  total: number | null;
  ccAdaptation: number | null;
  ccMitigation: number | null;
}

/** The zero-activity office: every column blank, not `0.00`. Matches the form's empty cells. */
const EMPTY: AipRowAmounts = {
  ps: null, mooe: null, co: null, total: null, ccAdaptation: null, ccMitigation: null,
};

/**
 * Sums a set of activities into one row's worth of figures.
 *
 * ⚠️ **Null-preserving, and that is the point.** A column where no activity carries a value stays
 * null and renders blank; one where any activity does becomes a number. Seeding the accumulator at
 * `0` instead would print `0.00` in every climate-change column of every office in the province,
 * which reads as "measured, and it is zero" rather than "not applicable here".
 *
 * ℹ️ Summed on the client because the tree already holds every activity in the group — the figures
 * are in memory, and an endpoint to re-add them would be a round trip for arithmetic already done.
 * That stops being true if the tree is ever paginated; at that point this moves to SQL.
 */
export function sumActivityAmounts(
  activities: readonly AipRowAmounts[]
): AipRowAmounts {
  if (activities.length === 0) return EMPTY;

  const add = (a: number | null, b: number | null): number | null =>
    a == null && b == null ? null : (a ?? 0) + (b ?? 0);

  return activities.reduce<AipRowAmounts>((acc, a) => ({
    ps:           add(acc.ps, a.ps),
    mooe:         add(acc.mooe, a.mooe),
    co:           add(acc.co, a.co),
    total:        add(acc.total, a.total),
    ccAdaptation: add(acc.ccAdaptation, a.ccAdaptation),
    ccMitigation: add(acc.ccMitigation, a.ccMitigation),
  }), EMPTY);
}

/**
 * The form's Funding Source column (7), joined the way the province writes it.
 *
 * ⚠️ **Two sources, and which one answers depends on the fiscal year.** An entered year (FY2028+)
 * carries the funds on its expenditure lines, so `fundCodes` is the answer and the activity's own
 * `fundingSourceSnapshot` is null. An uploaded year (FY≤2027) has no lines at all, so the snapshot
 * is the only answer. Reading either one alone leaves half the records showing no fund.
 *
 * The separator is a bare `/`, matching the real FY2027 sheet (`5% CF/ NGA`).
 */
export function activityFundLabel(activity: {
  fundCodes?: string[] | null;
  fundingSourceSnapshot?: string | null;
}): string | null {
  const codes = activity.fundCodes ?? [];
  if (codes.length > 0) return codes.join("/");
  const snapshot = activity.fundingSourceSnapshot?.trim();
  return snapshot ? snapshot : null;
}

/**
 * The funding sources as a pill.
 *
 * `rounded-full` is deliberate and allowed — CLAUDE.md flattens portal cards and inputs but exempts
 * pills and badges. Renders nothing at all when there is no fund: an "—" here would sit beside six
 * numeric columns and read as a zero amount rather than as an unanswered question.
 */
export function AipFundPill({ label }: { label: string | null }) {
  if (!label) return null;
  return (
    <span
      className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-600"
      title={`Funding source: ${label}`}
    >
      {label}
    </span>
  );
}

/**
 * One row's six figures, in the form's own column order.
 *
 * ⚠️ **Each figure is labelled, never positional.** The strip wraps on a narrow window — the
 * portal shell goes off-canvas below `lg` and this sits inside an indented tree — and an unlabelled
 * `— / 1,066.00 / — / 1,066.00` that has rewrapped is unreadable and, worse, misreadable: MOOE and
 * CO are adjacent columns holding the same kind of number.
 *
 * ⚠️ `tabular-nums` on every figure so digits line up down the column between rows. Without it the
 * proportional font makes an office subtotal and its activities visibly fail to align, which reads
 * as a data error.
 */
export function AipFigureStrip({
  amounts, emphasis = "normal",
}: {
  amounts: AipRowAmounts;
  /** `strong` for the office subtotal — the row the encoder checks against the sheet. */
  emphasis?: "normal" | "strong";
}) {
  const strong = emphasis === "strong";
  return (
    <div className="flex flex-wrap items-end gap-x-4 gap-y-1">
      <Figure label="PS"    value={amounts.ps}    strong={strong} />
      <Figure label="MOOE"  value={amounts.mooe}  strong={strong} />
      <Figure label="CO"    value={amounts.co}    strong={strong} />
      <Figure label="Total" value={amounts.total} strong={strong} isTotal />
      {/* Separated from the three expense classes because they are NOT part of the Total — the
          form counts climate-change expenditure in its own pair of columns (12)(13), over money
          already counted in (8)–(10). Running all six together would invite adding them. */}
      <span aria-hidden className="hidden self-stretch border-l border-slate-200 sm:block" />
      <Figure label="CC adapt."  value={amounts.ccAdaptation} strong={strong} />
      <Figure label="CC mitig."  value={amounts.ccMitigation} strong={strong} />
    </div>
  );
}

/**
 * One labelled figure.
 *
 * ⚠️ The label is `slate-600`, the AA-safe token — it is content an encoder reads, not decoration
 * (`docs/DESIGN_SYSTEM.md` §1 / RAL-133). `slate-500` would be a contrast failure at 10px.
 */
function Figure({
  label, value, strong, isTotal = false,
}: { label: string; value: number | null; strong: boolean; isTotal?: boolean }) {
  return (
    <span className="flex flex-col items-end">
      <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-600">
        {label}
      </span>
      <span
        className={`tabular-nums text-slate-800 ${
          isTotal || strong ? "text-sm font-semibold" : "text-sm"
        }`}
      >
        {/* ⚠️ null and 0 are different states — never costed vs costed at zero (V18-34).
            `fmtThousands` renders both as an em dash, matching the form's blank cells. */}
        {fmtThousands(value)}
      </span>
    </span>
  );
}

/** The unit these figures are in, said once per block rather than on each of the six. */
export function AipUnitCaption() {
  return <span className="text-xs text-slate-600">Amounts in thousand pesos</span>;
}
