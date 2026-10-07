"use client";

/**
 * The proposal editor's section bodies except G and H (PPDO-160, spec §6.2 "Section specifics").
 *
 * Headings, column headings and fixed row labels are the template's, word for word (decision 30).
 * The template's italic instructions appear as helper text, never in the export (decision 21).
 * Every section reads the WORKING content, so a section that shows another one (F's "Same as
 * Section A") shows what is on screen, saved or not (decision 28).
 */

import { useState } from "react";
import Lookup from "@/components/ui/Lookup";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import dynamic from "next/dynamic";
import { RichTextView } from "@/components/ui/RichTextView";
import RichTextEditorSkeleton from "@/components/ui/RichTextEditorSkeleton";

// TipTap is ~80 kB of the editor page; load it as the editor mounts, behind a same-size skeleton (PPDO-188 / O14).
const RichTextEditor = dynamic(() => import("@/components/ui/RichTextEditor"), {
  ssr: false,
  loading: () => <RichTextEditorSkeleton />,
});
import {
  HGDG_CHECKLISTS, attributedGadBudget, beneficiaryTotals, copySummaryToDirect, money, rowTotal,
} from "@/lib/proposal-editor";
import type {
  Proposal, ProposalBeneficiary, ProposalContent, ProposalMonitoring, ProposalTargetBeneficiary,
  ProposalTeamMember,
} from "@/types";
import {
  AddRowButton, CountInput, FieldErrors, FromAipTag, Guidance, RowButton, SummaryRow, Table, TextArea,
  TextInput, inputCls, removeAt, replaceAt, tdCls,
} from "./fields";

export interface SectionProps {
  proposal: Proposal;
  /** The working copy — what is on screen. */
  content: ProposalContent;
  update: (patch: Partial<ProposalContent>) => void;
  readOnly: boolean;
  /** Every field error from the last save, keyed by path ("teamMembers[1].sex"). */
  errors: Record<string, string[]>;
}

function Rich({ value, onChange, readOnly, errors, label }: {
  value: string | null; onChange: (v: string | null) => void; readOnly: boolean; errors?: string[]; label: string;
}) {
  if (readOnly) return <RichTextView html={value} />;
  return (
    <>
      <RichTextEditor value={value} onChange={onChange} ariaLabel={label} />
      <FieldErrors messages={errors} />
    </>
  );
}

const peso = (n: number | null) => (n == null ? "—" : `₱${money(n)}`);

// ── A ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionA({ proposal, content, update, readOnly, errors }: SectionProps) {
  const h = proposal.header;
  // Live as the score changes (spec §6.2): the screen's copy of HgdgAttribution's figure.
  const gad = attributedGadBudget(content.hgdgScore, h.projectCost);
  const rows = content.beneficiariesSummary;
  const totals = beneficiaryTotals(rows);
  const setRow = (i: number, row: ProposalBeneficiary) => update({ beneficiariesSummary: replaceAt(rows, i, row) });
  const checklistItems = HGDG_CHECKLISTS.map((c, i) => ({ ...c, id: i + 1 }));
  const checklistId = checklistItems.find((c) => c.code === content.hgdgChecklist)?.id ?? null;

  return (
    <div>
      <div className="mb-4">
        <SummaryRow label="Program Title"><span className="text-sm text-slate-800">{h.programTitle}</span><FromAipTag /></SummaryRow>
        <SummaryRow label="Project Title"><span className="text-sm text-slate-800">{h.projectTitle}</span><FromAipTag /></SummaryRow>
        <SummaryRow label="Project Proponent"><span className="text-sm text-slate-800">{h.proponent}</span><FromAipTag /></SummaryRow>
        <SummaryRow label="Project Type">
          <span className="text-sm text-slate-600">Printed blank for now. The PDC has not set the list yet.</span>
        </SummaryRow>
        <SummaryRow label="Project Location">
          <TextInput value={content.projectLocation} onChange={(v) => update({ projectLocation: v })}
            readOnly={readOnly} errors={errors.projectLocation} ariaLabel="Project Location"
            placeholder="Municipalities or barangays" />
        </SummaryRow>
        <SummaryRow label="Implementation Schedule">
          <span className="text-sm text-slate-800">
            <em>Start:</em> {h.scheduleStart ?? "—"} &nbsp; <em>End:</em> {h.scheduleEnd ?? "—"}
          </span>
          <FromAipTag />
        </SummaryRow>
        <SummaryRow label="Project Cost">
          <span className="text-sm tabular-nums text-slate-800">{peso(h.projectCost)}</span><FromAipTag />
          <p className="mt-0.5 text-xs text-slate-600">
            Full value as encoded. It is not rounded and has no +30%, so it can differ from this project&rsquo;s
            figure on the Consolidated AIP.
          </p>
        </SummaryRow>
        <SummaryRow label="Attributed GAD Budget">
          <span className="text-sm tabular-nums text-slate-800">{content.hgdgScore == null ? "—" : peso(gad)}</span>
          <p className="mt-0.5 text-xs text-slate-600">Project Cost × the HGDG score&rsquo;s attribution. Computed, never typed.</p>
        </SummaryRow>
        <SummaryRow label="Funding Source">
          <span className="text-sm text-slate-800">{h.fundingSources.join(", ") || "—"}</span><FromAipTag />
        </SummaryRow>
        <SummaryRow label="HGDG Checklist Used">
          {readOnly ? (
            <span className="text-sm text-slate-800">
              {HGDG_CHECKLISTS.find((c) => c.code === content.hgdgChecklist)?.name ?? "—"}
            </span>
          ) : (
            <>
              <Lookup
                items={checklistItems}
                value={checklistId}
                onChange={(id) => update({ hgdgChecklist: checklistItems.find((c) => c.id === id)?.code ?? null })}
                getId={(c) => c.id}
                getLabel={(c) => c.name}
                allOptionLabel="— None —"
                placeholder="Search checklists…"
              />
              <FieldErrors messages={errors.hgdgChecklist} />
            </>
          )}
        </SummaryRow>
        <SummaryRow label="HGDG Score">
          {readOnly ? (
            <span className="text-sm text-slate-800">{content.hgdgScore ?? "—"}</span>
          ) : (
            <>
              <input
                type="number" min={0} max={20} step={0.1} inputMode="decimal"
                value={content.hgdgScore ?? ""}
                onChange={(e) => update({ hgdgScore: e.target.value === "" ? null : Number(e.target.value) })}
                aria-label="HGDG Score"
                aria-invalid={errors.hgdgScore?.length ? true : undefined}
                className={`${inputCls} w-28 tabular-nums${errors.hgdgScore?.length ? " border-red-600" : ""}`}
              />
              <span className="ml-2 text-xs text-slate-600">0 to 20, one decimal</span>
              <FieldErrors messages={errors.hgdgScore} />
            </>
          )}
        </SummaryRow>
      </div>

      <h4 className="mb-1 text-sm font-semibold text-slate-800">Disaggregated Data of Intended Beneficiaries</h4>
      <BeneficiaryTable
        firstHeading="Indicator"
        rows={rows.map((r) => ({ label: r.indicator, male: r.male, female: r.female }))}
        totals={totals}
        readOnly={readOnly}
        errors={errors}
        path="beneficiariesSummary"
        labelField="indicator"
        onChange={(i, r) => setRow(i, { ...rows[i], indicator: r.label, male: r.male, female: r.female })}
        onRemove={(i) => update({ beneficiariesSummary: removeAt(rows, i) })}
        onAdd={() => update({ beneficiariesSummary: [...rows, { indicator: null, male: null, female: null }] })}
      />
    </div>
  );
}

interface BenRow { label: string | null; male: number | null; female: number | null }

function BeneficiaryTable({
  firstHeading, rows, totals, readOnly, errors, path, labelField, onChange, onRemove, onAdd, indexOf = (i) => i,
  hideTotal = false, groupLabel,
}: {
  firstHeading: string;
  rows: BenRow[];
  totals: { male: number | null; female: number | null; total: number | null };
  readOnly: boolean;
  errors: Record<string, string[]>;
  path: string;
  labelField: "indicator" | "name";
  onChange: (i: number, r: BenRow) => void;
  onRemove: (i: number) => void;
  onAdd: () => void;
  /** A row's position in the server's array, for error paths (F's Direct and Indirect share one). */
  indexOf?: (i: number) => number;
  hideTotal?: boolean;
  groupLabel?: string;
}) {
  return (
    <div>
      <Table headers={[firstHeading, "Male", "Female", "Total", ...(readOnly ? [] : [""])]} minWidth={560}>
        {groupLabel && (
          <tr><td colSpan={readOnly ? 4 : 5} className={`${tdCls} font-semibold text-slate-800`}>{groupLabel}</td></tr>
        )}
        {rows.length === 0 && (
          <tr><td colSpan={readOnly ? 4 : 5} className={`${tdCls} text-slate-600`}>No rows yet.</td></tr>
        )}
        {rows.map((r, i) => {
          const at = `${path}[${indexOf(i)}]`;
          return (
            <tr key={i}>
              <td className={tdCls}>
                <TextInput value={r.label} onChange={(v) => onChange(i, { ...r, label: v })} readOnly={readOnly}
                  errors={errors[`${at}.${labelField}`]} ariaLabel={`${firstHeading} ${i + 1}`} />
              </td>
              <td className={tdCls}>
                <CountInput value={r.male} onChange={(v) => onChange(i, { ...r, male: v })} readOnly={readOnly}
                  errors={errors[`${at}.male`]} ariaLabel={`Male, row ${i + 1}`} />
              </td>
              <td className={tdCls}>
                <CountInput value={r.female} onChange={(v) => onChange(i, { ...r, female: v })} readOnly={readOnly}
                  errors={errors[`${at}.female`]} ariaLabel={`Female, row ${i + 1}`} />
              </td>
              <td className={`${tdCls} text-right tabular-nums text-slate-800`}>{rowTotal(r.male, r.female) ?? ""}</td>
              {!readOnly && (
                <td className={`${tdCls} text-right`}>
                  <RowButton onClick={() => onRemove(i)} label="✕" title={`Remove row ${i + 1}`} danger />
                </td>
              )}
            </tr>
          );
        })}
        {!hideTotal && (
          <tr className="font-semibold text-slate-800">
            <td className={tdCls}>TOTAL</td>
            <td className={`${tdCls} text-right tabular-nums`}>{totals.male ?? ""}</td>
            <td className={`${tdCls} text-right tabular-nums`}>{totals.female ?? ""}</td>
            <td className={`${tdCls} text-right tabular-nums`}>{totals.total ?? ""}</td>
            {!readOnly && <td className={tdCls} />}
          </tr>
        )}
      </Table>
      {!readOnly && <AddRowButton onClick={onAdd} />}
    </div>
  );
}

// ── B, C, J ───────────────────────────────────────────────────────────────────────────────────────

export function SectionB({ content, update, readOnly, errors }: SectionProps) {
  return (
    <div>
      <Guidance>Provide a brief description of the project (3–5 sentences).</Guidance>
      <Rich value={content.description} onChange={(v) => update({ description: v })} readOnly={readOnly}
        errors={errors.description} label="Project Description" />
    </div>
  );
}

export function SectionC({ content, update, readOnly, errors }: SectionProps) {
  return (
    <div>
      <Guidance>
        The primary gender issues and other issues identified, and how; whether women and men were consulted; the
        project&rsquo;s alignment with the SDGs, PDP, RDP, PDPFP and other plans; and the gender concerns it addresses.
      </Guidance>
      <Rich value={content.rationale} onChange={(v) => update({ rationale: v })} readOnly={readOnly}
        errors={errors.rationale} label="Rationale/Background" />
    </div>
  );
}

export function SectionJ({ content, update, readOnly, errors }: SectionProps) {
  return (
    <div>
      <Guidance>
        Partnerships with the private sector and other stakeholders, the roles of each, and how the project and its GAD
        benefits are sustained after it ends.
      </Guidance>
      <Rich value={content.partnershipSustainability} onChange={(v) => update({ partnershipSustainability: v })}
        readOnly={readOnly} errors={errors.partnershipSustainability} label="Partnership and Sustainability" />
    </div>
  );
}

// ── D ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionD({ content, update, readOnly, errors }: SectionProps) {
  const set = (i: number, field: "benefit" | "cost", v: string | null) =>
    update({ benefits: replaceAt(content.benefits, i, { ...content.benefits[i], [field]: v }) });
  return (
    <div>
      <Guidance>
        Benefits are the positive outcomes the project delivers, tangible or intangible. Costs are the negative effects,
        temporary or long-term, that may result from implementing it.
      </Guidance>
      <div className="space-y-4">
        {content.benefits.map((b, i) => (
          <div key={b.sector} className="border border-slate-200">
            <div className="border-b border-slate-200 bg-slate-50 px-3 py-1.5 text-sm font-semibold text-slate-800">{b.sector}</div>
            <div className="grid grid-cols-1 gap-3 p-3 lg:grid-cols-2">
              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-600">Project Benefit</p>
                <Rich value={b.benefit} onChange={(v) => set(i, "benefit", v)} readOnly={readOnly}
                  errors={errors[`benefits[${i}].benefit`]} label={`${b.sector} benefit`} />
              </div>
              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-600">Project Cost (negative effects)</p>
                <Rich value={b.cost} onChange={(v) => set(i, "cost", v)} readOnly={readOnly}
                  errors={errors[`benefits[${i}].cost`]} label={`${b.sector} cost`} />
              </div>
            </div>
          </div>
        ))}
      </div>
      <FieldErrors messages={errors.benefits} />
    </div>
  );
}

// ── E ─────────────────────────────────────────────────────────────────────────────────────────────

const LOGFRAME_LABEL: Record<string, string> = {
  Impact: "Impact", Outcome: "Outcome", Output: "Output", Input: "Input/Activities",
};

export function SectionE({ content, update, readOnly, errors }: SectionProps) {
  const set = (i: number, field: "target" | "verification", v: string | null) =>
    update({ logframe: replaceAt(content.logframe, i, { ...content.logframe[i], [field]: v }) });
  return (
    <div>
      <Guidance>
        The objective states what the project wants to achieve, how, and who benefits — SMART. Impact is the long-term
        change; Outcome the intermediate change; Output the immediate result, referring explicitly to women and men;
        Input/Activities the money, people, materials and actions.
      </Guidance>
      <p className="mb-1 text-sm font-semibold text-slate-800">General Goals/Objectives</p>
      <Rich value={content.generalObjective} onChange={(v) => update({ generalObjective: v })} readOnly={readOnly}
        errors={errors.generalObjective} label="General Goals/Objectives" />
      <div className="mt-4">
        <Table headers={["Project Structure", "Performance Target and/or Indicator", "Means of Verification"]} minWidth={640}>
          {content.logframe.map((l, i) => (
            <tr key={l.level}>
              <td className={`${tdCls} w-40 font-semibold text-slate-800`}>{LOGFRAME_LABEL[l.level]}</td>
              <td className={tdCls}>
                <TextArea value={l.target} onChange={(v) => set(i, "target", v)} readOnly={readOnly}
                  errors={errors[`logframe[${i}].target`]} ariaLabel={`${LOGFRAME_LABEL[l.level]} target`} rows={3} />
              </td>
              <td className={tdCls}>
                <TextArea value={l.verification} onChange={(v) => set(i, "verification", v)} readOnly={readOnly}
                  errors={errors[`logframe[${i}].verification`]} ariaLabel={`${LOGFRAME_LABEL[l.level]} means of verification`} rows={3} />
              </td>
            </tr>
          ))}
        </Table>
      </div>
    </div>
  );
}

// ── F ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionF({ content, update, readOnly, errors }: SectionProps) {
  const [confirmSame, setConfirmSame] = useState(false);
  const all = content.targetBeneficiaries;
  const directIdx = all.map((t, i) => (t.kind === "Direct" ? i : -1)).filter((i) => i >= 0);
  const indirectIdx = all.map((t, i) => (t.kind === "Indirect" ? i : -1)).filter((i) => i >= 0);
  const same = content.directSameAsSummary;

  // Decision 10/28: while ticked, Direct IS Section A's rows as on screen.
  const directRows: BenRow[] = same
    ? content.beneficiariesSummary.map((b) => ({ label: b.indicator, male: b.male, female: b.female }))
    : directIdx.map((i) => ({ label: all[i].name, male: all[i].male, female: all[i].female }));
  const indirectRows: BenRow[] = indirectIdx.map((i) => ({ label: all[i].name, male: all[i].male, female: all[i].female }));
  const totals = beneficiaryTotals([...directRows, ...indirectRows]);

  const setAt = (serverIndex: number, r: BenRow) =>
    update({ targetBeneficiaries: replaceAt(all, serverIndex, { ...all[serverIndex], name: r.label, male: r.male, female: r.female }) });
  const add = (kind: "Direct" | "Indirect") =>
    update({ targetBeneficiaries: [...all, { kind, name: null, male: null, female: null } as ProposalTargetBeneficiary] });

  function toggleSame(next: boolean) {
    if (!next) {
      // Unticking copies A's current rows into F-Direct, edited independently from then on.
      update({
        directSameAsSummary: false,
        targetBeneficiaries: [...copySummaryToDirect(content.beneficiariesSummary), ...all.filter((t) => t.kind === "Indirect")],
      });
    } else if (directIdx.length > 0) {
      setConfirmSame(true);
    } else {
      update({ directSameAsSummary: true });
    }
  }

  return (
    <div>
      <Guidance>
        Direct and indirect beneficiaries, disaggregated by sex and, where it applies, by location and vulnerable sector
        (indigenous peoples, children, persons with disability, the elderly).
      </Guidance>
      {!readOnly && (
        <label className="mb-3 inline-flex items-center gap-2 text-sm text-slate-800">
          <input type="checkbox" checked={same} onChange={(e) => toggleSame(e.target.checked)} className="accent-green-700" />
          Direct beneficiaries: same as Section A
        </label>
      )}
      {readOnly && same && <p className="mb-2 text-xs text-slate-600">Direct beneficiaries are the same as Section A.</p>}

      <BeneficiaryTable
        firstHeading="Target Beneficiaries"
        groupLabel="Direct Beneficiaries"
        rows={directRows}
        totals={totals}
        hideTotal
        readOnly={readOnly || same}
        errors={errors}
        path="targetBeneficiaries"
        labelField="name"
        indexOf={(i) => directIdx[i] ?? -1}
        onChange={(i, r) => setAt(directIdx[i], r)}
        onRemove={(i) => update({ targetBeneficiaries: removeAt(all, directIdx[i]) })}
        onAdd={() => add("Direct")}
      />
      {same && !readOnly && <p className="mt-1 text-xs text-slate-600">Edit these in Section A, or untick the box to list them separately.</p>}
      <div className="mt-3">
        <BeneficiaryTable
          firstHeading="Target Beneficiaries"
          groupLabel="Indirect Beneficiaries"
          rows={indirectRows}
          totals={totals}
          readOnly={readOnly}
          errors={errors}
          path="targetBeneficiaries"
          labelField="name"
          indexOf={(i) => indirectIdx[i]}
          onChange={(i, r) => setAt(indirectIdx[i], r)}
          onRemove={(i) => update({ targetBeneficiaries: removeAt(all, indirectIdx[i]) })}
          onAdd={() => add("Indirect")}
        />
      </div>

      {confirmSame && (
        <ConfirmDialog
          title="Use Section A's rows again?"
          message="Direct beneficiaries will show Section A's rows, and the rows typed here will be removed."
          confirmLabel="Use Section A"
          cancelLabel="Keep my rows"
          variant="warning"
          onConfirm={() => update({ directSameAsSummary: true, targetBeneficiaries: all.filter((t) => t.kind === "Indirect") })}
          onClose={() => setConfirmSame(false)}
        />
      )}
    </div>
  );
}

// ── I ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionI({ content, update, readOnly, errors }: SectionProps) {
  const members = content.teamMembers;
  const setMember = (i: number, m: ProposalTeamMember) => update({ teamMembers: replaceAt(members, i, m) });

  function sameForAll() {
    const first = members.find((m) => m.requiredTraining?.trim())?.requiredTraining ?? null;
    if (!first) return;
    update({ teamMembers: members.map((m) => (m.requiredTraining?.trim() ? m : { ...m, requiredTraining: first })) });
  }

  return (
    <div>
      <Guidance>
        Who implements and supervises the project, the team&rsquo;s composition by sex and its GAD-related capability,
        and the capacity development training the team needs.
      </Guidance>
      <SummaryRow label="Overall Project Supervisor:">
        <TextInput value={content.projectSupervisor} onChange={(v) => update({ projectSupervisor: v })} readOnly={readOnly}
          errors={errors.projectSupervisor} ariaLabel="Overall Project Supervisor" />
      </SummaryRow>
      <SummaryRow label="Project Manager">
        <TextInput value={content.projectManager} onChange={(v) => update({ projectManager: v })} readOnly={readOnly}
          errors={errors.projectManager} ariaLabel="Project Manager" />
      </SummaryRow>

      <div className="mt-4">
        <Table headers={["Members of the Implementation Team", "Sex", "GAD-related Trainings Attended", "Expertise", ...(readOnly ? [] : [""])]} minWidth={720}>
          {members.length === 0 && (
            <tr><td colSpan={readOnly ? 4 : 5} className={`${tdCls} text-slate-600`}>No members yet.</td></tr>
          )}
          {members.map((m, i) => (
            <tr key={i}>
              <td className={tdCls}>
                <TextInput value={m.name} onChange={(v) => setMember(i, { ...m, name: v })} readOnly={readOnly}
                  errors={errors[`teamMembers[${i}].name`]} ariaLabel={`Member ${i + 1} name`} />
              </td>
              <td className={`${tdCls} w-24`}>
                {readOnly ? (
                  <span className="text-sm text-slate-800">{m.sex ?? "—"}</span>
                ) : (
                  <>
                    <select value={m.sex ?? ""} onChange={(e) => setMember(i, { ...m, sex: (e.target.value || null) as "M" | "F" | null })}
                      aria-label={`Member ${i + 1} sex`} className={inputCls}>
                      <option value="">—</option>
                      <option value="M">M</option>
                      <option value="F">F</option>
                    </select>
                    <FieldErrors messages={errors[`teamMembers[${i}].sex`]} />
                  </>
                )}
              </td>
              <td className={tdCls}>
                <TextArea value={m.gadTrainings} onChange={(v) => setMember(i, { ...m, gadTrainings: v })} readOnly={readOnly}
                  errors={errors[`teamMembers[${i}].gadTrainings`]} ariaLabel={`Member ${i + 1} GAD-related trainings`} />
              </td>
              <td className={tdCls}>
                <TextInput value={m.expertise} onChange={(v) => setMember(i, { ...m, expertise: v })} readOnly={readOnly}
                  errors={errors[`teamMembers[${i}].expertise`]} ariaLabel={`Member ${i + 1} expertise`} />
              </td>
              {!readOnly && (
                <td className={`${tdCls} text-right`}>
                  <RowButton onClick={() => update({ teamMembers: removeAt(members, i) })} label="✕" title={`Remove member ${i + 1}`} danger />
                </td>
              )}
            </tr>
          ))}
        </Table>
        {!readOnly && (
          <AddRowButton onClick={() => update({
            teamMembers: [...members, { name: null, sex: null, gadTrainings: null, expertise: null, requiredTraining: null }],
          })} />
        )}
      </div>

      <div className="mt-5">
        <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
          <h4 className="text-sm font-semibold text-slate-800">Required Capacity Development Training of the Implementation Team</h4>
          {!readOnly && members.length > 1 && (
            <button type="button" onClick={sameForAll} className="text-xs font-medium text-green-700 hover:underline">
              Same for all members
            </button>
          )}
        </div>
        <Table headers={["Members of the Implementation Team", "Required Capacity Development Training"]} minWidth={560}>
          {members.length === 0 && (
            <tr><td colSpan={2} className={`${tdCls} text-slate-600`}>Add members above; each gets a row here.</td></tr>
          )}
          {members.map((m, i) => (
            <tr key={i}>
              <td className={`${tdCls} w-64 text-slate-800`}>{m.name?.trim() || <span className="text-slate-600">(no name yet)</span>}</td>
              <td className={tdCls}>
                <TextInput value={m.requiredTraining} onChange={(v) => setMember(i, { ...m, requiredTraining: v })} readOnly={readOnly}
                  errors={errors[`teamMembers[${i}].requiredTraining`]} ariaLabel={`Required training for member ${i + 1}`} />
              </td>
            </tr>
          ))}
        </Table>
        <p className="mt-1 text-xs text-slate-600">Identical trainings for neighbouring members print as one merged cell.</p>
      </div>
    </div>
  );
}

// ── K ─────────────────────────────────────────────────────────────────────────────────────────────

const PHASES: { code: ProposalMonitoring["phase"]; label: string }[] = [
  { code: "Pre", label: "PRE-IMPLEMENTATION" },
  { code: "During", label: "DURING IMPLEMENTATION" },
  { code: "Post", label: "POST-IMPLEMENTATION" },
];

export function SectionK({ content, update, readOnly, errors }: SectionProps) {
  const all = content.monitoring;
  const set = (i: number, m: ProposalMonitoring) => update({ monitoring: replaceAt(all, i, m) });
  return (
    <div>
      <Guidance>
        Baseline and end-line data disaggregated by sex, the M&amp;E team&rsquo;s roles and competencies, whether
        beneficiaries take part, and the gender equality indicators measured.
      </Guidance>
      <Table headers={["M&E Activity/Scheme/Mechanism", "Schedule/Frequency", "Monitoring Tools to be Used", ...(readOnly ? [] : [""])]} minWidth={680}>
        {PHASES.map((p) => {
          const idx = all.map((m, i) => (m.phase === p.code ? i : -1)).filter((i) => i >= 0);
          return [
            <tr key={p.code}>
              <td colSpan={readOnly ? 3 : 4} className={`${tdCls} bg-slate-50 font-semibold text-slate-800`}>
                <div className="flex items-center justify-between gap-2">
                  {p.label}
                  {!readOnly && (
                    <button type="button" className="text-xs font-medium text-green-700 hover:underline"
                      onClick={() => update({ monitoring: [...all, { phase: p.code, activity: null, schedule: null, tools: null }] })}>
                      + Add row
                    </button>
                  )}
                </div>
              </td>
            </tr>,
            ...idx.map((i) => (
              <tr key={`${p.code}-${i}`}>
                <td className={tdCls}>
                  <TextArea value={all[i].activity} onChange={(v) => set(i, { ...all[i], activity: v })} readOnly={readOnly}
                    errors={errors[`monitoring[${i}].activity`]} ariaLabel={`${p.label} activity`} />
                  <FieldErrors messages={errors[`monitoring[${i}].phase`]} />
                </td>
                <td className={tdCls}>
                  <TextInput value={all[i].schedule} onChange={(v) => set(i, { ...all[i], schedule: v })} readOnly={readOnly}
                    errors={errors[`monitoring[${i}].schedule`]} ariaLabel={`${p.label} schedule`} />
                </td>
                <td className={tdCls}>
                  <TextInput value={all[i].tools} onChange={(v) => set(i, { ...all[i], tools: v })} readOnly={readOnly}
                    errors={errors[`monitoring[${i}].tools`]} ariaLabel={`${p.label} tools`} />
                </td>
                {!readOnly && (
                  <td className={`${tdCls} text-right`}>
                    <RowButton onClick={() => update({ monitoring: removeAt(all, i) })} label="✕" title="Remove row" danger />
                  </td>
                )}
              </tr>
            )),
          ];
        })}
      </Table>
    </div>
  );
}

// ── L ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionL({ content, update, readOnly, errors }: SectionProps) {
  const risks = content.risks;
  return (
    <div>
      <Guidance>
        Possible risks to the project, including those that may negatively affect or exploit marginalized women and girl
        children, how to prevent them and how to watch for them.
      </Guidance>
      <Table headers={["Possible Risks", "Preventive measures and strategies", "Mechanisms to Monitor Risks", ...(readOnly ? [] : [""])]} minWidth={680}>
        {risks.length === 0 && (
          <tr><td colSpan={readOnly ? 3 : 4} className={`${tdCls} text-slate-600`}>No risks listed yet.</td></tr>
        )}
        {risks.map((r, i) => (
          <tr key={i}>
            <td className={tdCls}>
              <TextArea value={r.risk} onChange={(v) => update({ risks: replaceAt(risks, i, { ...r, risk: v }) })} readOnly={readOnly}
                errors={errors[`risks[${i}].risk`]} ariaLabel={`Risk ${i + 1}`} />
            </td>
            <td className={tdCls}>
              <TextArea value={r.prevention} onChange={(v) => update({ risks: replaceAt(risks, i, { ...r, prevention: v }) })} readOnly={readOnly}
                errors={errors[`risks[${i}].prevention`]} ariaLabel={`Risk ${i + 1} prevention`} />
            </td>
            <td className={tdCls}>
              <TextArea value={r.monitoring} onChange={(v) => update({ risks: replaceAt(risks, i, { ...r, monitoring: v }) })} readOnly={readOnly}
                errors={errors[`risks[${i}].monitoring`]} ariaLabel={`Risk ${i + 1} monitoring`} />
            </td>
            {!readOnly && (
              <td className={`${tdCls} text-right`}>
                <RowButton onClick={() => update({ risks: removeAt(risks, i) })} label="✕" title={`Remove risk ${i + 1}`} danger />
              </td>
            )}
          </tr>
        ))}
      </Table>
      {!readOnly && <AddRowButton onClick={() => update({ risks: [...risks, { risk: null, prevention: null, monitoring: null }] })} />}
      <div className="mt-4">
        <p className="mb-1 text-sm font-semibold text-slate-800">
          Strategies to avoid/minimize negative impact on women&rsquo;s status and welfare
        </p>
        <TextArea value={content.womensImpactStrategy} onChange={(v) => update({ womensImpactStrategy: v })} readOnly={readOnly}
          errors={errors.womensImpactStrategy} ariaLabel="Strategies to avoid or minimize negative impact on women" rows={3} />
      </div>
    </div>
  );
}

// ── M ─────────────────────────────────────────────────────────────────────────────────────────────

export function SectionM({ proposal }: SectionProps) {
  return (
    <div>
      <p className="text-sm text-slate-800">
        {proposal.header.climateTypology || "N/A"}
        <FromAipTag />
      </p>
      <p className="mt-1 text-xs text-slate-600">
        The climate change typologies of the project&rsquo;s activities in AIP Entry. Change them there.
      </p>
    </div>
  );
}

// ── Signatories ───────────────────────────────────────────────────────────────────────────────────

export function SectionS({ content, update, readOnly, errors }: SectionProps) {
  const set = (i: number, field: "label" | "name" | "position", v: string | null) =>
    update({ signatories: replaceAt(content.signatories, i, { ...content.signatories[i], [field]: v }) });
  return (
    <div>
      <Guidance>Up to four signatures, printed two per row. A slot with no name is not printed.</Guidance>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        {content.signatories.map((s, i) => {
          const printed = !!s.name?.trim();
          return (
            <div key={s.slot} className="border border-slate-200 p-3">
              <div className="mb-2 flex items-center justify-between">
                <span className="text-xs font-semibold uppercase tracking-wide text-slate-600">Slot {s.slot}</span>
                {!printed && <span className="text-xs text-slate-600">Not printed</span>}
              </div>
              <div className="space-y-2">
                <label className="block text-xs text-slate-600">Label
                  <TextInput value={s.label} onChange={(v) => set(i, "label", v)} readOnly={readOnly}
                    errors={errors[`signatories[${i}].label`]} placeholder="e.g. Reviewed by" />
                </label>
                <label className="block text-xs text-slate-600">Name
                  <TextInput value={s.name} onChange={(v) => set(i, "name", v)} readOnly={readOnly}
                    errors={errors[`signatories[${i}].name`]} />
                </label>
                <label className="block text-xs text-slate-600">Position
                  <TextInput value={s.position} onChange={(v) => set(i, "position", v)} readOnly={readOnly}
                    errors={errors[`signatories[${i}].position`]} />
                </label>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
