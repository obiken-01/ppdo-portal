"use client";

/**
 * LDIP import preview page (RAL-113) — mirrors budget-planning/aip/import-preview.
 * Reads the upload response stashed in sessionStorage by ldip/new, shows counts +
 * a per-office breakdown + warnings, then confirms (creates one Draft record per
 * office found) or cancels.
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { confirmLdipImport, ldipErrorMessage } from "@/lib/ldip";
import { useMe } from "@/lib/me-cache";
import { useToast } from "@/components/ui/Toast";
import type { LdipImportPreviewResponse, LdipSector } from "@/types";

const PREVIEW_KEY = "ldip_import_preview";
const META_KEY    = "ldip_import_meta";

const SECTOR_ORDER: LdipSector[] = ["General", "Social", "Economic", "Others"];

interface ImportMeta {
  originalFilename: string;
  /**
   * RAL-114 — when set, the confirm re-uploads into this existing record (full-
   * replaces its hierarchy) instead of creating a new one. Set by ldip/new when
   * launched with ?replaceId=.
   */
  replaceId?: number | null;
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function StatTile({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-white border border-slate-200 px-5 py-4">
      <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide">{label}</p>
      <p className="mt-1.5 text-2xl font-bold text-slate-800 tabular-nums">{value.toLocaleString()}</p>
    </div>
  );
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      className={`w-4 h-4 text-slate-600 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
      fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
    </svg>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function LdipImportPreviewPage() {
  const router = useRouter();
  const { toast } = useToast();
  const me = useMe(
    (m) => m.canUploadAip,
    () => "/budget-planning/ldip"
  );

  const [preview, setPreview]           = useState<LdipImportPreviewResponse | null>(null);
  const [meta, setMeta]                 = useState<ImportMeta | null>(null);
  const [confirming, setConfirming]     = useState(false);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [warningsOpen, setWarningsOpen] = useState(false);

  useEffect(() => {
    const rawPreview = sessionStorage.getItem(PREVIEW_KEY);
    const rawMeta    = sessionStorage.getItem(META_KEY);
    if (!rawPreview || !rawMeta) { router.replace("/budget-planning/ldip/new"); return; }
    try {
      setPreview(JSON.parse(rawPreview) as LdipImportPreviewResponse);
      setMeta(JSON.parse(rawMeta) as ImportMeta);
    } catch {
      router.replace("/budget-planning/ldip/new");
    }
  }, [router]);

  async function handleConfirm() {
    if (!preview) return;
    setConfirmError(null);
    setConfirming(true);
    const replaceId = meta?.replaceId ?? null;
    try {
      const saved = await confirmLdipImport({
        fiscalYearStart: preview.fiscalYearStart,
        fiscalYearEnd:   preview.fiscalYearEnd,
        offices:         preview.offices,
        // RAL-114 — re-upload into the same record when launched with ?replaceId=.
        ...(replaceId != null ? { targetRecordId: replaceId } : {}),
      });
      sessionStorage.removeItem(PREVIEW_KEY);
      sessionStorage.removeItem(META_KEY);
      const officeSuffix = `${preview.offices.length} office${preview.offices.length !== 1 ? "s" : ""}`;
      toast.success(
        replaceId != null ? "Re-upload complete" : "Import complete",
        replaceId != null
          ? `${saved.refCode} updated from the file, covering ${officeSuffix}.`
          : `${saved.refCode} imported, covering ${officeSuffix}.`
      );
      // On re-upload the service returns the SAME record id, so this lands back on it.
      router.push(`/budget-planning/ldip/edit?id=${saved.id}`);
    } catch (err) {
      setConfirmError(ldipErrorMessage(err, "Confirm failed. Please try again."));
      setConfirming(false);
    }
  }

  function handleCancel() {
    const replaceId = meta?.replaceId ?? null;
    sessionStorage.removeItem(PREVIEW_KEY);
    sessionStorage.removeItem(META_KEY);
    router.push(
      replaceId != null
        ? `/budget-planning/ldip/new?replaceId=${replaceId}`
        : "/budget-planning/ldip/new"
    );
  }

  function programCountForSector(sector: LdipSector): number {
    if (!preview) return 0;
    return preview.offices
      .flatMap((o) => o.groups)
      .filter((g) => g.sector === sector)
      .reduce((n, g) => n + g.programs.length, 0);
  }

  if (!preview || !me) {
    return (
      <div className="p-6 flex items-center gap-3 text-sm text-slate-600">
        <span className="inline-block w-4 h-4 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />
        Loading preview…
      </div>
    );
  }

  return (
    <div className="p-6">
      {/* Header + actions */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold text-slate-800">
            {meta?.replaceId != null ? "Re-upload Preview" : "Import Preview"} — LDIP{" "}
            {preview.fiscalYearStart}–{preview.fiscalYearEnd}
          </h1>
          <p className="text-sm text-slate-600 mt-0.5">
            {meta?.replaceId != null
              ? "Review the data before confirming. This replaces the existing record's programs with the file's contents. This cannot be undone."
              : "Review the data before confirming. One Draft record is created, spanning every office found. This cannot be undone."}
          </p>
        </div>
        <div className="flex items-center gap-3 shrink-0 ml-6">
          <button
            onClick={handleConfirm}
            disabled={confirming || preview.offices.length === 0}
            className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
              confirming || preview.offices.length === 0
                ? "bg-green-300 cursor-not-allowed"
                : "bg-green-700 hover:bg-green-800"
            }`}
          >
            {confirming
              ? meta?.replaceId != null ? "Replacing…" : "Importing…"
              : meta?.replaceId != null ? "Confirm Re-upload" : "Confirm Import"}
          </button>
          <button
            onClick={handleCancel}
            disabled={confirming}
            className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </div>

      {confirmError && (
        <div className="mb-5 border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {confirmError}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-5 gap-6 items-start">
        {/* ── Left column: counts + office breakdown ── */}
        <div className="lg:col-span-3 space-y-5">
          <div className="grid grid-cols-3 gap-3">
            <StatTile label="Offices"  value={preview.counts.offices} />
            <StatTile label="Groups"   value={preview.counts.groups} />
            <StatTile label="Programs" value={preview.counts.programs} />
          </div>

          <div>
            <h2 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2">
              Programs Parsed by Sector
            </h2>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              {SECTOR_ORDER.map((sector) => (
                <StatTile key={sector} label={sector} value={programCountForSector(sector)} />
              ))}
            </div>
          </div>

          <div>
            <h2 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2">
              Breakdown by Office
            </h2>
            <div className="border border-slate-200 divide-y divide-slate-100 bg-white">
              {preview.offices.map((office) => {
                const groupCount = office.groups.length;
                const progCount = office.groups.reduce((n, g) => n + g.programs.length, 0);
                return (
                  <div key={office.officeId} className="px-4 py-3 grid grid-cols-3 items-center text-sm gap-2">
                    <span className="font-medium text-slate-700 col-span-1">
                      {office.officeCode} — {office.officeName}
                    </span>
                    <span className="text-slate-600 text-xs text-right">
                      {groupCount} group{groupCount !== 1 ? "s" : ""}
                    </span>
                    <span className="text-slate-600 text-xs text-right">
                      {progCount} program{progCount !== 1 ? "s" : ""}
                    </span>
                  </div>
                );
              })}
              {preview.offices.length === 0 && (
                <p className="px-4 py-6 text-center text-sm text-slate-600">
                  No office in the file matched a configured office_ref_code.
                </p>
              )}
            </div>
          </div>
        </div>

        {/* ── Right column: file info + warnings ── */}
        <div className="lg:col-span-2 space-y-4">
          <div className="bg-slate-50 border border-slate-200 p-5">
            <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-3">File Info</h3>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between gap-2">
                <dt className="text-slate-600">Filename</dt>
                <dd className="text-slate-700 font-medium truncate text-right max-w-[60%]" title={meta?.originalFilename}>
                  {meta?.originalFilename ?? "—"}
                </dd>
              </div>
              <div className="flex justify-between gap-2">
                <dt className="text-slate-600">Period</dt>
                <dd className="text-slate-700 font-medium">
                  {preview.fiscalYearStart}–{preview.fiscalYearEnd}
                </dd>
              </div>
            </dl>
          </div>

          {preview.warnings.length > 0 && (
            <div className="border border-amber-200 bg-amber-50">
              <button
                onClick={() => setWarningsOpen((o) => !o)}
                className="w-full flex items-center justify-between px-4 py-3 text-left"
              >
                <span className="flex items-center gap-2 text-sm font-semibold text-amber-800">
                  <span>⚠</span>
                  Warnings ({preview.warnings.length.toLocaleString()})
                </span>
                <Chevron open={warningsOpen} />
              </button>

              {warningsOpen && (
                <div className="border-t border-amber-200 px-4 py-3 max-h-80 overflow-y-auto">
                  <ul className="space-y-1.5">
                    {preview.warnings.map((w, i) => (
                      <li key={i} className="text-xs text-amber-800 flex items-start gap-2">
                        <span className="shrink-0 mt-0.5">·</span>
                        <span>{w}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
