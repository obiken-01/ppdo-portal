"use client";

export function generateStaticParams() {
  return [{ id: "__placeholder__" }];
}

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import api from "@/lib/api";
import {
  finalizeLdip,
  getLdipById,
  ldipErrorMessage,
  unlockLdip,
  updateLdip,
} from "@/lib/ldip";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type { LdipEntryMode, LdipRecord, LdipStatus, MeResponse } from "@/types";

const CURRENT_YEAR = new Date().getFullYear();

function StatusBadge({ status }: { status: LdipStatus }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

export default function LdipEditPage() {
  const router = useRouter();
  const params = useParams();
  const rawId = Array.isArray(params.id) ? params.id[0] : (params.id as string);
  const { toast } = useToast();

  const [me, setMe] = useState<MeResponse | null>(null);
  const [record, setRecord] = useState<LdipRecord | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Form fields
  const [title, setTitle] = useState("");
  const [entryMode, setEntryMode] = useState<LdipEntryMode>("New");
  const [fiscalYearStart, setFiscalYearStart] = useState(CURRENT_YEAR);
  const [fiscalYearEnd, setFiscalYearEnd] = useState(CURRENT_YEAR + 2);

  // UI state
  const [saving, setSaving] = useState(false);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

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

  // ── Load record ───────────────────────────────────────────────────────────

  useEffect(() => {
    if (!me || !rawId) return;
    getLdipById(Number(rawId))
      .then((rec) => {
        setRecord(rec);
        setTitle(rec.title);
        setEntryMode(rec.entryMode);
        setFiscalYearStart(rec.fiscalYearStart);
        setFiscalYearEnd(rec.fiscalYearEnd);
      })
      .catch((err) =>
        setLoadError(ldipErrorMessage(err, "Failed to load LDIP record."))
      );
  }, [me, rawId]);

  // ── Derived ───────────────────────────────────────────────────────────────

  const isReadOnly = record?.status !== "Draft";
  const isAdmin = me?.role === "Admin" || me?.role === "SuperAdmin";

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

  // ── Handlers ─────────────────────────────────────────────────────────────

  async function handleSaveDraft() {
    if (!validate() || !record) return;
    setSubmitError(null);
    setSaving(true);
    try {
      const updated = await updateLdip(record.id, {
        title: title.trim(),
        entryMode,
        fiscalYearStart,
        fiscalYearEnd,
      });
      setRecord(updated);
      toast.success("Saved", `${updated.refCode} updated.`);
    } catch (err) {
      setSubmitError(ldipErrorMessage(err, "Failed to save LDIP."));
    } finally {
      setSaving(false);
    }
  }

  function handleFinalize() {
    if (!record) return;
    setConfirm({
      title: "Finalize LDIP",
      message: `Finalize ${record.refCode}? Once finalized, it is locked and can only be unlocked by an admin.`,
      confirmLabel: "Finalize",
      cancelLabel: "Cancel",
      variant: "primary",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await finalizeLdip(record.id);
          toast.success("Finalized", `${record.refCode} is now Final.`);
          router.push("/budget-planning/ldip");
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not finalize LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  function handleUnlock() {
    if (!record) return;
    setConfirm({
      title: "Unlock LDIP",
      message: `Unlock ${record.refCode} to allow further editing?`,
      confirmLabel: "Unlock",
      cancelLabel: "Cancel",
      variant: "warning",
      onConfirm: async () => {
        setConfirm(null);
        try {
          const updated = await unlockLdip(record.id);
          setRecord(updated);
          toast.success("Unlocked", `${record.refCode} is now editable.`);
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not unlock LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ── Render ────────────────────────────────────────────────────────────────

  if (!me) return null;

  // Loading spinner
  if (!record && !loadError) {
    return (
      <div className="p-6 flex items-center gap-2 text-slate-500 text-sm">
        <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
        Loading…
      </div>
    );
  }

  const fieldCls = (hasError: boolean) =>
    `w-full border text-sm px-3 py-2 focus:outline-none focus:ring-1 ${
      hasError ? "border-red-400 focus:ring-red-400" : "border-slate-300 focus:ring-green-600"
    } ${isReadOnly ? "bg-slate-50 text-slate-500 cursor-not-allowed" : "bg-white text-slate-700"}`;

  return (
    <div className="p-6 max-w-2xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <div className="flex items-center gap-3">
          <h1 className="text-xl font-bold text-slate-800">
            {record ? record.refCode : "LDIP"}
          </h1>
          {record && <StatusBadge status={record.status} />}
        </div>
        <p className="text-sm text-slate-500 mt-0.5">
          {isReadOnly
            ? "This record is read-only."
            : "Edit the LDIP draft below."}
        </p>
      </div>

      {/* Load error */}
      {loadError && (
        <div className="mb-5 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {loadError}
        </div>
      )}

      {record && (
        <>
          {/* Form fields */}
          <div className="space-y-5">

            {/* Title */}
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Title {!isReadOnly && <span className="text-red-500">*</span>}
              </label>
              <input
                type="text"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                disabled={isReadOnly}
                maxLength={500}
                placeholder="e.g. Provincial Development Investment Program 2027–2029"
                className={fieldCls(!!validationErrors.title)}
              />
              {validationErrors.title && (
                <p className="text-xs text-red-600 mt-1">{validationErrors.title}</p>
              )}
            </div>

            {/* Entry Mode */}
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Entry Mode {!isReadOnly && <span className="text-red-500">*</span>}
              </label>
              <select
                value={entryMode}
                onChange={(e) => setEntryMode(e.target.value as LdipEntryMode)}
                disabled={isReadOnly}
                className={`border text-sm px-3 py-2 focus:outline-none focus:ring-1 ${
                  validationErrors.entryMode
                    ? "border-red-400 focus:ring-red-400"
                    : "border-slate-300 focus:ring-green-600"
                } ${isReadOnly ? "bg-slate-50 text-slate-500 cursor-not-allowed" : "bg-white text-slate-700"}`}
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
                  Fiscal Year Start {!isReadOnly && <span className="text-red-500">*</span>}
                </label>
                <input
                  type="number"
                  min={2020}
                  max={2055}
                  value={fiscalYearStart}
                  onChange={(e) =>
                    setFiscalYearStart(Number(e.target.value) || CURRENT_YEAR)
                  }
                  disabled={isReadOnly}
                  className={`w-28 border text-sm px-3 py-2 focus:outline-none focus:ring-1 ${
                    validationErrors.fiscalYearStart
                      ? "border-red-400 focus:ring-red-400"
                      : "border-slate-300 focus:ring-green-600"
                  } ${isReadOnly ? "bg-slate-50 text-slate-500 cursor-not-allowed" : "bg-white text-slate-700"}`}
                />
                {validationErrors.fiscalYearStart && (
                  <p className="text-xs text-red-600 mt-1">{validationErrors.fiscalYearStart}</p>
                )}
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                  Fiscal Year End {!isReadOnly && <span className="text-red-500">*</span>}
                </label>
                <input
                  type="number"
                  min={fiscalYearStart}
                  max={2060}
                  value={fiscalYearEnd}
                  onChange={(e) =>
                    setFiscalYearEnd(Number(e.target.value) || CURRENT_YEAR + 2)
                  }
                  disabled={isReadOnly}
                  className={`w-28 border text-sm px-3 py-2 focus:outline-none focus:ring-1 ${
                    validationErrors.fiscalYearEnd
                      ? "border-red-400 focus:ring-red-400"
                      : "border-slate-300 focus:ring-green-600"
                  } ${isReadOnly ? "bg-slate-50 text-slate-500 cursor-not-allowed" : "bg-white text-slate-700"}`}
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
                  <option>
                    {record.sourceId != null
                      ? `LDIP #${record.sourceId} (read-only)`
                      : "Not linked"}
                  </option>
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
            {!isReadOnly && (
              <>
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
                <button
                  onClick={handleFinalize}
                  className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-700 bg-white hover:bg-slate-50 transition-colors"
                >
                  Finalize
                </button>
              </>
            )}
            {isReadOnly && record.status === "Final" && isAdmin && (
              <button
                onClick={handleUnlock}
                className="px-5 py-2 text-sm font-medium border border-amber-300 text-amber-700 bg-amber-50 hover:bg-amber-100 transition-colors"
              >
                Unlock
              </button>
            )}
            <Link
              href="/budget-planning/ldip"
              className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors"
            >
              {isReadOnly ? "Back" : "Cancel"}
            </Link>
          </div>
        </>
      )}

      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}
