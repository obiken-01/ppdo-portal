"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { createLdip, ldipErrorMessage } from "@/lib/ldip";
import { useToast } from "@/components/ui/Toast";
import type { LdipEntryMode, MeResponse } from "@/types";

const CURRENT_YEAR = new Date().getFullYear();

export default function LdipNewPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [me, setMe] = useState<MeResponse | null>(null);

  // Form fields
  const [title, setTitle] = useState("");
  const [entryMode, setEntryMode] = useState<LdipEntryMode>("New");
  const [fiscalYearStart, setFiscalYearStart] = useState(CURRENT_YEAR);
  const [fiscalYearEnd, setFiscalYearEnd] = useState(CURRENT_YEAR + 2);

  // UI state
  const [saving, setSaving] = useState(false);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});
  const [submitError, setSubmitError] = useState<string | null>(null);

  // ── Auth ──────────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canAccessBudgetPlanning) {
        router.replace("/dashboard");
        return;
      }
      setMe(data);
    });
  }, [router]);

  // ── Validation ────────────────────────────────────────────────────────────

  function validate(): boolean {
    const errs: Record<string, string> = {};
    if (!title.trim()) errs.title = "Title is required.";
    else if (title.trim().length > 500) errs.title = "Title must not exceed 500 characters.";
    if (!entryMode) errs.entryMode = "Entry mode is required.";
    if (!fiscalYearStart) errs.fiscalYearStart = "Fiscal year start is required.";
    if (!fiscalYearEnd) errs.fiscalYearEnd = "Fiscal year end is required.";
    else if (fiscalYearEnd < fiscalYearStart)
      errs.fiscalYearEnd = "Fiscal year end must be ≥ fiscal year start.";
    setValidationErrors(errs);
    return Object.keys(errs).length === 0;
  }

  // ── Save ──────────────────────────────────────────────────────────────────

  async function handleSaveDraft() {
    if (!validate() || !me) return;
    setSubmitError(null);
    setSaving(true);
    try {
      const created = await createLdip({
        title: title.trim(),
        entryMode,
        fiscalYearStart,
        fiscalYearEnd,
      });
      toast.success("Saved", `${created.refCode} created as Draft.`);
      router.push("/budget-planning/ldip");
    } catch (err) {
      setSubmitError(ldipErrorMessage(err, "Failed to save LDIP."));
    } finally {
      setSaving(false);
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────

  if (!me) return null;

  return (
    <div className="p-6 max-w-2xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-xl font-bold text-slate-800">Create New LDIP</h1>
        <p className="text-sm text-slate-500 mt-0.5">
          Fill in the details below. You can save as Draft at any time.
        </p>
      </div>

      {/* Form */}
      <div className="space-y-5">

        {/* Title */}
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Title <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            maxLength={500}
            placeholder="e.g. Provincial Development Investment Program 2027–2029"
            className={`w-full border text-sm px-3 py-2 text-slate-700 bg-white focus:outline-none focus:ring-1 ${
              validationErrors.title
                ? "border-red-400 focus:ring-red-400"
                : "border-slate-300 focus:ring-green-600"
            }`}
          />
          {validationErrors.title && (
            <p className="text-xs text-red-600 mt-1">{validationErrors.title}</p>
          )}
        </div>

        {/* Entry Mode */}
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Entry Mode <span className="text-red-500">*</span>
          </label>
          <select
            value={entryMode}
            onChange={(e) => setEntryMode(e.target.value as LdipEntryMode)}
            className={`border text-sm px-3 py-2 text-slate-700 bg-white focus:outline-none focus:ring-1 ${
              validationErrors.entryMode
                ? "border-red-400 focus:ring-red-400"
                : "border-slate-300 focus:ring-green-600"
            }`}
          >
            <option value="New">New</option>
            <option value="Amendment">Amendment</option>
            <option value="Supplemental">Supplemental</option>
          </select>
          {validationErrors.entryMode && (
            <p className="text-xs text-red-600 mt-1">{validationErrors.entryMode}</p>
          )}
        </div>

        {/* Fiscal Years */}
        <div className="flex gap-6">
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Fiscal Year Start <span className="text-red-500">*</span>
            </label>
            <input
              type="number"
              min={2020}
              max={2055}
              value={fiscalYearStart}
              onChange={(e) => setFiscalYearStart(Number(e.target.value) || CURRENT_YEAR)}
              className={`w-28 border text-sm px-3 py-2 text-slate-700 bg-white focus:outline-none focus:ring-1 ${
                validationErrors.fiscalYearStart
                  ? "border-red-400 focus:ring-red-400"
                  : "border-slate-300 focus:ring-green-600"
              }`}
            />
            {validationErrors.fiscalYearStart && (
              <p className="text-xs text-red-600 mt-1">{validationErrors.fiscalYearStart}</p>
            )}
          </div>
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Fiscal Year End <span className="text-red-500">*</span>
            </label>
            <input
              type="number"
              min={fiscalYearStart}
              max={2060}
              value={fiscalYearEnd}
              onChange={(e) => setFiscalYearEnd(Number(e.target.value) || CURRENT_YEAR + 2)}
              className={`w-28 border text-sm px-3 py-2 text-slate-700 bg-white focus:outline-none focus:ring-1 ${
                validationErrors.fiscalYearEnd
                  ? "border-red-400 focus:ring-red-400"
                  : "border-slate-300 focus:ring-green-600"
              }`}
            />
            {validationErrors.fiscalYearEnd && (
              <p className="text-xs text-red-600 mt-1">{validationErrors.fiscalYearEnd}</p>
            )}
          </div>
        </div>

        {/* Source LDIP — shown for Amendment / Supplemental; always disabled in v1.1 */}
        {entryMode !== "New" && (
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Source LDIP
            </label>
            <select
              disabled
              className="border border-slate-200 bg-slate-50 text-sm px-3 py-2 text-slate-400 w-72 cursor-not-allowed"
            >
              <option>Not linked</option>
            </select>
            <p className="text-xs text-slate-400 mt-1">
              Source LDIP linking is not yet supported in this version.
            </p>
          </div>
        )}
      </div>

      {/* Submit error */}
      {submitError && (
        <div className="mt-5 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {submitError}
        </div>
      )}

      {/* Buttons */}
      <div className="mt-6 flex items-center gap-3">
        <button
          onClick={handleSaveDraft}
          disabled={saving}
          className="px-5 py-2 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2"
        >
          {saving && (
            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          Save Draft
        </button>
        <Link
          href="/budget-planning/ldip"
          className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors"
        >
          Cancel
        </Link>
      </div>
    </div>
  );
}
