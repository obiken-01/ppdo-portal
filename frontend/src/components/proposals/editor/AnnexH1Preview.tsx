"use client";

/**
 * Section H — Annex H-1, read-only, as the export prints it (PPDO-160, decisions 8, 14, 17, 18).
 *
 * ⚠️ **Built by the same rules as the Word file**: `arrange`, `annexCell` and `computationLine` in
 * lib/proposal-editor are a port of `InvestmentProposalDocumentBuilder`. It follows G's grouping and
 * order AS ON SCREEN (decision 28), so an unsaved regrouping shows here before it is saved; the
 * export always prints saved data.
 */

import { annexCell, arrange, money, type AnnexCell } from "@/lib/proposal-editor";
import type { ProposalAipRow, ProposalContent } from "@/types";

function Cell({ cell }: { cell: AnnexCell | null }) {
  if (!cell) return null;
  return (
    <div className="space-y-0.5 text-xs">
      {cell.blocks.map((b, i) => (
        <div key={i}>
          {b.amount != null ? (
            <p className="text-slate-800">{b.heading} {money(b.amount)}</p>
          ) : (
            <>
              <p className="text-slate-800">{b.heading}</p>
              {b.lines.map((l, k) => <p key={k} className="whitespace-pre pl-2 text-slate-600">{l}</p>)}
            </>
          )}
        </div>
      ))}
      <p className={`text-right tabular-nums ${cell.blocks.length ? "pt-1 font-semibold text-slate-800" : "text-slate-800"}`}>
        {cell.blocks.length ? `Total ${money(cell.total)}` : money(cell.total)}
      </p>
    </div>
  );
}

const td = "border border-slate-200 px-2 py-1.5 align-top";

export default function AnnexH1Preview({ content, aipRows }: { content: ProposalContent; aipRows: ProposalAipRow[] }) {
  // A step with no name is dropped on save, so it does not print either.
  const lines = arrange(
    { ...content, workPlan: content.workPlan.filter((r) => r.aipActivityId != null || r.name?.trim()) },
    aipRows,
  );
  const acts = lines.flatMap((l) => (l.kind === "row" && l.activity ? [l.activity] : []));
  const sum = (pick: (a: ProposalAipRow) => number) => acts.reduce((t, a) => t + pick(a), 0);

  return (
    <div>
      <p className="mb-3 text-xs leading-relaxed text-slate-600">
        Filled from the project&rsquo;s expenditure lines in AIP Entry, in Section G&rsquo;s grouping and order. Change the
        amounts there; change the order in Section G. Steps you added yourself carry no money.
      </p>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-sm" style={{ minWidth: 900 }}>
          <thead className="bg-slate-50 text-xs font-semibold text-slate-600">
            <tr>
              <th rowSpan={2} className={`${td} text-left`}>Input/Activities/Project Components</th>
              <th colSpan={3} className={`${td} text-center`}>Budgetary Requirements and Other Inputs</th>
              <th rowSpan={2} className={`${td} text-right`}>Total</th>
              <th rowSpan={2} className={`${td} text-left`}>Source of Fund</th>
            </tr>
            <tr>
              <th className={`${td} text-center`}>MOOE</th>
              <th className={`${td} text-center`}>PS</th>
              <th className={`${td} text-center`}>CO</th>
            </tr>
          </thead>
          <tbody>
            {lines.length === 0 && (
              <tr><td colSpan={6} className={`${td} text-slate-600`}>No activities in the AIP yet.</td></tr>
            )}
            {lines.map((l, i) =>
              l.kind === "group" ? (
                <tr key={i}><td colSpan={6} className={`${td} font-semibold text-slate-800`}>{l.label}</td></tr>
              ) : (
                <tr key={i}>
                  <td className={`${td} w-48 text-xs text-slate-800`}>{l.activity?.name ?? l.row.name}</td>
                  {l.activity ? (
                    <>
                      <td className={td}><Cell cell={annexCell(l.activity, "mooe")} /></td>
                      <td className={td}><Cell cell={annexCell(l.activity, "ps")} /></td>
                      <td className={td}><Cell cell={annexCell(l.activity, "co")} /></td>
                      <td className={`${td} text-right text-xs tabular-nums text-slate-800`}>{money(l.activity.total)}</td>
                      <td className={`${td} text-xs text-slate-800`}>{l.activity.fundNames.join(", ")}</td>
                    </>
                  ) : (
                    <><td className={td} /><td className={td} /><td className={td} /><td className={td} /><td className={td} /></>
                  )}
                </tr>
              ),
            )}
            <tr className="font-semibold text-slate-800">
              <td className={td}>TOTAL</td>
              <td className={`${td} text-right text-xs tabular-nums`}>{money(sum((a) => a.mooe))}</td>
              <td className={`${td} text-right text-xs tabular-nums`}>{money(sum((a) => a.ps))}</td>
              <td className={`${td} text-right text-xs tabular-nums`}>{money(sum((a) => a.co))}</td>
              <td className={`${td} text-right text-xs tabular-nums`}>{money(sum((a) => a.total))}</td>
              <td className={td} />
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}
