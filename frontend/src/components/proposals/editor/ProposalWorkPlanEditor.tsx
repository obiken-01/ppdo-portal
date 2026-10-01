"use client";

/**
 * Section G — the work plan (PPDO-160, spec §6.2 "G", decisions 14–16).
 *
 * - Rows are the project's AIP activities ("From AIP": name, timeline and OPR read-only) and
 *   proposal-only steps ("Proposal only": everything typed, never money).
 * - Groups are the proposal's own. ONE grouping and order drives G and Annex H-1, so this editor's
 *   order is the preview's order (`arrange` in lib/proposal-editor).
 * - Ordering is ↑/↓, not drag (the portal's convention). A row moves within its group; its group is
 *   chosen from the select on the row.
 * - Rows are numbered 1, 2, 3… straight through, ignoring groups, as printed.
 *
 * ⚠️ The editor shows EMPTY groups (so a new group can be named and filled) under a "not printed"
 * note; the export skips them. Everything else is the export's arrangement.
 */

import type { ProposalAipRow, ProposalContent, ProposalGroup, ProposalWorkPlanRow } from "@/types";
import {
  FieldErrors, FromAipTag, ProposalOnlyTag, RowButton, TextArea, TextInput, inputCls, move, removeAt, replaceAt, tdCls,
} from "./fields";

let keySeq = 0;
function newGroupKey(): string {
  keySeq += 1;
  return `new-${Date.now().toString(36)}-${keySeq}`;
}

interface Bucket {
  group: ProposalGroup | null;
  groupIndex: number;
  /** Positions in `workPlan`, in work-plan order. */
  rows: number[];
}

export default function ProposalWorkPlanEditor({
  content, aipRows, update, readOnly, errors,
}: {
  content: ProposalContent;
  aipRows: ProposalAipRow[];
  update: (patch: Partial<ProposalContent>) => void;
  readOnly: boolean;
  errors: Record<string, string[]>;
}) {
  const { groups, workPlan } = content;
  const byId = new Map(aipRows.map((a) => [a.activityId, a]));
  const keys = new Set(groups.map((g) => g.clientKey));
  const visible = workPlan
    .map((r, i) => ({ r, i }))
    .filter(({ r }) => r.aipActivityId == null || byId.has(r.aipActivityId));

  const buckets: Bucket[] = [
    ...groups.map((g, gi) => ({ group: g, groupIndex: gi, rows: visible.filter(({ r }) => r.groupKey === g.clientKey).map(({ i }) => i) })),
    {
      group: null, groupIndex: -1,
      rows: visible.filter(({ r }) => r.groupKey == null || !keys.has(r.groupKey)).map(({ i }) => i),
    },
  ];

  // Numbered straight through, in printed order (empty groups print nothing, so they take no number).
  const numberOf = new Map<number, number>();
  let n = 0;
  for (const b of buckets) for (const i of b.rows) numberOf.set(i, ++n);

  const setRow = (i: number, row: ProposalWorkPlanRow) => update({ workPlan: replaceAt(workPlan, i, row) });

  /** Swaps two rows of one bucket in `workPlan`, which swaps them on screen and in print. */
  function moveRow(bucket: Bucket, pos: number, delta: number) {
    const a = bucket.rows[pos];
    const b = bucket.rows[pos + delta];
    if (a == null || b == null) return;
    const next = [...workPlan];
    [next[a], next[b]] = [next[b], next[a]];
    update({ workPlan: next });
  }

  function removeGroup(gi: number) {
    const key = groups[gi].clientKey;
    update({
      groups: removeAt(groups, gi),
      // The group's rows fall back to ungrouped (spec §6.2).
      workPlan: workPlan.map((r) => (r.groupKey === key ? { ...r, groupKey: null } : r)),
    });
  }

  const cols = readOnly ? 6 : 8;
  const headers = [
    "No.", "Inputs/ Activities/Project Components", "Performance Target and/or Indicator",
    "Gender Issues to be addressed", "Timeline/ Duration", "OPR", ...(readOnly ? [] : ["Group", ""]),
  ];

  return (
    <div>
      <p className="mb-3 text-xs leading-relaxed text-slate-600">
        List the steps in order. Rows marked <strong>From AIP</strong> are the project&rsquo;s activities: their timeline
        and office come from the AIP. The grouping and order here also shape Annex H-1. Rows are numbered straight
        through, ignoring groups, as printed.
      </p>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-sm" style={{ minWidth: readOnly ? 760 : 1080 }}>
          <thead>
            <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-600">
              {headers.map((h, i) => <th key={i} className="px-2 py-2 align-bottom">{h}</th>)}
            </tr>
          </thead>
          <tbody>
            {visible.length === 0 && groups.length === 0 && (
              <tr><td colSpan={cols} className={`${tdCls} text-slate-600`}>
                This project has no activities in the AIP yet. Add steps below, or add activities in AIP Entry.
              </td></tr>
            )}
            {buckets.map((b) => {
              const showHeader = b.group != null || (b.rows.length > 0 && groups.length > 0);
              return [
                showHeader && (
                  <tr key={`h-${b.group?.clientKey ?? "none"}`} className="bg-slate-50">
                    <td colSpan={cols} className={tdCls}>
                      {b.group == null ? (
                        <span className="text-xs font-semibold uppercase tracking-wide text-slate-600">Not in a group</span>
                      ) : readOnly ? (
                        <span className="font-semibold text-slate-800">{b.group.label}</span>
                      ) : (
                        <div className="flex flex-wrap items-center gap-2">
                          <div className="w-72 max-w-full">
                            <input
                              value={b.group.label ?? ""}
                              onChange={(e) => update({ groups: replaceAt(groups, b.groupIndex, { ...b.group!, label: e.target.value || null }) })}
                              aria-label="Group name"
                              placeholder="Group name, e.g. Capability Building"
                              className={`${inputCls} font-semibold${errors[`groups[${b.groupIndex}].label`] ? " border-red-600" : ""}`}
                            />
                            <FieldErrors messages={errors[`groups[${b.groupIndex}].label`]} />
                          </div>
                          <RowButton label="↑" title="Move group up" disabled={b.groupIndex === 0}
                            onClick={() => update({ groups: move(groups, b.groupIndex, -1) })} />
                          <RowButton label="↓" title="Move group down" disabled={b.groupIndex === groups.length - 1}
                            onClick={() => update({ groups: move(groups, b.groupIndex, 1) })} />
                          <button type="button" onClick={() => removeGroup(b.groupIndex)} className="text-xs font-medium text-danger-500 hover:underline">
                            Remove group
                          </button>
                          {b.rows.length === 0 && (
                            <span className="text-xs text-slate-600">Empty — not printed until a row is moved into it.</span>
                          )}
                        </div>
                      )}
                    </td>
                  </tr>
                ),
                ...b.rows.map((i, pos) => {
                  const r = workPlan[i];
                  const act = r.aipActivityId != null ? byId.get(r.aipActivityId) ?? null : null;
                  const at = `workPlan[${i}]`;
                  return (
                    <tr key={`r-${i}`}>
                      <td className={`${tdCls} w-10 tabular-nums text-slate-800`}>{numberOf.get(i)}.</td>
                      <td className={`${tdCls} min-w-[12rem]`}>
                        {act ? (
                          <span className="text-sm text-slate-800">{act.name}<FromAipTag /></span>
                        ) : readOnly ? (
                          <span className="text-sm text-slate-800">{r.name}</span>
                        ) : (
                          <>
                            <ProposalOnlyTag />
                            <TextInput value={r.name} onChange={(v) => setRow(i, { ...r, name: v })} readOnly={false}
                              errors={errors[`${at}.name`]} ariaLabel={`Step ${numberOf.get(i)} name`} placeholder="Step name" />
                          </>
                        )}
                        <FieldErrors messages={errors[`${at}.aipActivityId`]} />
                      </td>
                      <td className={tdCls}>
                        <TextArea value={r.performanceTarget} onChange={(v) => setRow(i, { ...r, performanceTarget: v })} readOnly={readOnly}
                          errors={errors[`${at}.performanceTarget`]} ariaLabel={`Row ${numberOf.get(i)} performance target`} />
                      </td>
                      <td className={tdCls}>
                        <TextArea value={r.genderIssues} onChange={(v) => setRow(i, { ...r, genderIssues: v })} readOnly={readOnly}
                          errors={errors[`${at}.genderIssues`]} ariaLabel={`Row ${numberOf.get(i)} gender issues`} />
                      </td>
                      <td className={`${tdCls} w-36`}>
                        {act ? <span className="text-sm text-slate-800">{act.timeline ?? "—"}</span> : (
                          <TextInput value={r.timeline} onChange={(v) => setRow(i, { ...r, timeline: v })} readOnly={readOnly}
                            errors={errors[`${at}.timeline`]} ariaLabel={`Step ${numberOf.get(i)} timeline`} />
                        )}
                      </td>
                      <td className={`${tdCls} w-28`}>
                        {act ? <span className="text-sm text-slate-800">{act.opr ?? "—"}</span> : (
                          <TextInput value={r.opr} onChange={(v) => setRow(i, { ...r, opr: v })} readOnly={readOnly}
                            errors={errors[`${at}.opr`]} ariaLabel={`Step ${numberOf.get(i)} OPR`} />
                        )}
                      </td>
                      {!readOnly && (
                        <>
                          <td className={`${tdCls} w-40`}>
                            <select
                              value={r.groupKey != null && keys.has(r.groupKey) ? r.groupKey : ""}
                              onChange={(e) => setRow(i, { ...r, groupKey: e.target.value || null })}
                              aria-label={`Row ${numberOf.get(i)} group`}
                              className={inputCls}
                            >
                              <option value="">— none —</option>
                              {groups.map((g) => <option key={g.clientKey} value={g.clientKey}>{g.label || "(unnamed group)"}</option>)}
                            </select>
                            <FieldErrors messages={errors[`${at}.groupKey`]} />
                          </td>
                          <td className={`${tdCls} w-28 whitespace-nowrap text-right`}>
                            <span className="inline-flex gap-1">
                              <RowButton label="↑" title={`Move row ${numberOf.get(i)} up`} disabled={pos === 0} onClick={() => moveRow(b, pos, -1)} />
                              <RowButton label="↓" title={`Move row ${numberOf.get(i)} down`} disabled={pos === b.rows.length - 1} onClick={() => moveRow(b, pos, 1)} />
                              {!act && (
                                <RowButton label="✕" title={`Remove step ${numberOf.get(i)}`} danger
                                  onClick={() => update({ workPlan: removeAt(workPlan, i) })} />
                              )}
                            </span>
                          </td>
                        </>
                      )}
                    </tr>
                  );
                }),
              ];
            })}
          </tbody>
        </table>
      </div>
      {!readOnly && (
        <div className="mt-2 flex flex-wrap gap-4">
          <button type="button" className="text-sm font-medium text-green-700 hover:underline"
            onClick={() => update({ groups: [...groups, { clientKey: newGroupKey(), label: null }] })}>
            + Add group
          </button>
          <button type="button" className="text-sm font-medium text-green-700 hover:underline"
            onClick={() => update({
              workPlan: [...workPlan, {
                aipActivityId: null, name: null, groupKey: null, performanceTarget: null, genderIssues: null, timeline: null, opr: null,
              }],
            })}>
            + Add step (proposal only)
          </button>
        </div>
      )}
    </div>
  );
}
