"use client";

/**
 * Investment Planning Settings → Proposal signatories (Demo 2.15 — PPDO-161,
 * `Investment_Proposal_Spec.md` decisions 19 and 25, §6.3).
 *
 * The PPDC's and the Local Chief Executive's names and positions. A new investment proposal copies
 * them into its "Submitted by" (slot 2) and "Noted by" (slot 3) signatures **when it is created**.
 * Changing them here never rewrites a proposal that already exists — each proposal's signatures are
 * its own from then on. Blank clears a value; a slot left without a name is created with its label
 * only and does not print.
 *
 * Same loading / error / success states as the default-fiscal-year card beside it. Shown only to
 * readers of this page, whose gate is the server's `CanManageInvestmentPlanningSettings`.
 *
 * ⚠️ **Read-only for the comment-only reviewer** (`readOnly`). The PUT runs `ReviewerWriteGuard`, like
 * every config write, so a host-office Admin who also holds the cross-office review grant reaches
 * this page but gets 403 on save. They see the values and why, instead of a Save that cannot work.
 *
 *   GET / PUT /api/config/investment-planning/signatory-defaults
 */

import { useCallback, useEffect, useState } from "react";
import { configErrorMessage, getSignatoryDefaults, updateSignatoryDefaults } from "@/lib/config";
import { useToast } from "@/components/ui/Toast";
import type { SignatoryDefaults } from "@/types";

const MAX = 200;
const EMPTY: SignatoryDefaults = { ppdcName: null, ppdcPosition: null, lceName: null, lcePosition: null };

const FIELDS: { key: keyof SignatoryDefaults; label: string; placeholder: string }[] = [
  { key: "ppdcName", label: "Name", placeholder: "e.g. Juan Dela Cruz, EnP" },
  { key: "ppdcPosition", label: "Position", placeholder: "Provincial Planning and Development Coordinator" },
  { key: "lceName", label: "Name", placeholder: "e.g. Hon. Maria Santos" },
  { key: "lcePosition", label: "Position", placeholder: "Provincial Governor" },
];

const inputCls =
  "w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100";

function normalise(v: SignatoryDefaults): SignatoryDefaults {
  const t = (s: string | null) => (s?.trim() ? s.trim() : null);
  return { ppdcName: t(v.ppdcName), ppdcPosition: t(v.ppdcPosition), lceName: t(v.lceName), lcePosition: t(v.lcePosition) };
}

export default function SignatoryDefaultsCard({ readOnly = false }: { readOnly?: boolean }) {
  const { toast } = useToast();
  const [saved, setSaved] = useState<SignatoryDefaults>(EMPTY);
  const [input, setInput] = useState<SignatoryDefaults>(EMPTY);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(false);
    try {
      const data = await getSignatoryDefaults();
      setSaved(data);
      setInput(data);
    } catch {
      setLoadError(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const dirty = JSON.stringify(normalise(input)) !== JSON.stringify(normalise(saved));
  const tooLong = FIELDS.some((f) => (input[f.key]?.trim().length ?? 0) > MAX);

  async function save() {
    if (tooLong) {
      setValidation(`Each name and position must be ${MAX} characters or fewer.`);
      return;
    }
    setSaving(true);
    setValidation(null);
    try {
      const next = await updateSignatoryDefaults(normalise(input));
      setSaved(next);
      setInput(next);
      toast.success("Signatory defaults saved.", "New proposals will use them. Existing proposals keep their own.");
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      const message = configErrorMessage(err, "The signatory defaults could not be saved.");
      // A 400 is a validation message for the fields; anything else is a save failure (input kept).
      if (status === 400) setValidation(message);
      else toast.error("Could not save", message);
    } finally {
      setSaving(false);
    }
  }

  function field(f: (typeof FIELDS)[number]) {
    const id = `signatory-${f.key}`;
    const value = input[f.key] ?? "";
    return (
      <div key={f.key}>
        <label htmlFor={id} className="block text-sm font-medium text-slate-800">{f.label}</label>
        <input
          id={id}
          type="text"
          value={value}
          placeholder={f.placeholder}
          disabled={saving}
          readOnly={readOnly}
          maxLength={MAX + 50}
          aria-invalid={value.trim().length > MAX ? true : undefined}
          onChange={(e) => { setInput((p) => ({ ...p, [f.key]: e.target.value || null })); setValidation(null); }}
          onKeyDown={(e) => { if (e.key === "Enter" && dirty) void save(); }}
          className={`mt-1 ${inputCls}`}
        />
      </div>
    );
  }

  return (
    <section className="bg-white border border-slate-200 px-5 py-5 max-w-xl">
      <h2 className="text-base font-semibold text-slate-800">Investment proposal signatories</h2>
      <p className="mt-1 text-sm text-slate-600">
        A new investment proposal fills its <strong>Submitted by</strong> and <strong>Noted by</strong> signatures
        from these. Proposals that already exist keep their own; edit those in the proposal itself.
      </p>

      {loading ? (
        <div className="mt-4 space-y-3" aria-hidden="true">
          {[0, 1, 2, 3].map((i) => <div key={i} className="h-9 bg-slate-100 animate-pulse" />)}
        </div>
      ) : loadError ? (
        <div role="alert"
          className="mt-4 flex items-center justify-between gap-3 border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          <span>Could not load the signatory defaults.</span>
          <button type="button" onClick={() => void load()} className="font-medium underline hover:no-underline">Retry</button>
        </div>
      ) : (
        <div className="mt-4 space-y-4">
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-slate-600">
              Submitted by — Provincial Planning and Development Coordinator
            </legend>
            {field(FIELDS[0])}
            {field(FIELDS[1])}
          </fieldset>
          <fieldset className="space-y-3">
            <legend className="text-xs font-semibold uppercase tracking-wide text-slate-600">
              Noted by — Local Chief Executive
            </legend>
            {field(FIELDS[2])}
            {field(FIELDS[3])}
          </fieldset>

          {validation && <p role="alert" className="text-sm text-red-600">{validation}</p>}

          {readOnly ? (
            <p className="text-sm text-slate-600">
              You can see these but not change them: the cross-office review role is read-only for settings.
            </p>
          ) : (
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() => void save()}
              disabled={saving || !dirty}
              className="px-4 py-2 text-sm font-medium bg-green-600 hover:bg-green-500 text-white disabled:opacity-50"
            >
              {saving ? "Saving…" : "Save"}
            </button>
            {dirty && !saving && (
              <button type="button" onClick={() => { setInput(saved); setValidation(null); }}
                className="text-sm font-medium text-green-600 hover:text-green-700 underline-offset-2 hover:underline">
                Discard changes
              </button>
            )}
            {!dirty && !saved.ppdcName && !saved.lceName && (
              <span className="text-sm text-slate-600">Not set — new proposals get the labels only.</span>
            )}
          </div>
          )}
        </div>
      )}
    </section>
  );
}
