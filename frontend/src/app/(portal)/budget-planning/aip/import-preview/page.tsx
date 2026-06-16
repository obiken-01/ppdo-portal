"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { aipErrorMessage, confirmAipImport } from "@/lib/aip";
import { useToast } from "@/components/ui/Toast";
import type { AipImportPreviewResponse, MeResponse } from "@/types";

const PREVIEW_KEY = "aip_import_preview";
const META_KEY    = "aip_import_meta";

interface ImportMeta {
  originalFilename: string;
  ldipId: number | null;
}

const SECTOR_ORDER = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"];
const SECTOR_LABEL: Record<string, string> = {
  GENERAL:  "General Services",
  SOCIAL:   "Social Services",
  ECONOMIC: "Economic Services",
  OTHERS:   "Other Services",
};

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function StatTile({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-white border border-slate-200 px-5 py-4">
      <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">{label}</p>
      <p className="mt-1.5 text-2xl font-bold text-slate-800 tabular-nums">{value.toLocaleString()}</p>
    </div>
  );
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      className={`w-4 h-4 text-slate-500 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
      fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
    </svg>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AipImportPreviewPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [me, setMe]                       = useState<MeResponse | null>(null);
  const [preview, setPreview]             = useState<AipImportPreviewResponse | null>(null);
  const [meta, setMeta]                   = useState<ImportMeta | null>(null);
  const [confirming, setConfirming]       = useState(false);
  const [confirmError, setConfirmError]   = useState<string | null>(null);
  const [warningsOpen, setWarningsOpen]   = useState(false);

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canUploadAip) { router.replace("/budget-planning/aip"); return; }
      setMe(data);
    });

    const rawPreview = sessionStorage.getItem(PREVIEW_KEY);
    const rawMeta   = sessionStorage.getItem(META_KEY);
    if (!rawPreview || !rawMeta) { router.replace("/budget-planning/aip/new"); return; }
    try {
      setPreview(JSON.parse(rawPreview) as AipImportPreviewResponse);
      setMeta(JSON.parse(rawMeta) as ImportMeta);
    } catch {
      router.replace("/budget-planning/aip/new");
    }
  }, [router]);

  function getSectorStats(sector: string): { programs: number; projects: number; activities: number } {
    if (!preview) return { programs: 0, projects: 0, activities: 0 };
    const offices = preview.sectorOffices[sector] ?? [];
    let programs = 0, projects = 0, activities = 0;
    for (const off of offices) {
      programs += off.programs.length;
      for (const prog of off.programs) {
        projects += prog.projects.length;
        for (const proj of prog.projects) activities += proj.activities.length;
      }
    }
    return { programs, projects, activities };
  }

  async function handleConfirm() {
    if (!preview || !meta) return;
    setConfirmError(null);
    setConfirming(true);
    try {
      await confirmAipImport({
        fiscalYear:       preview.fiscalYear,
        originalFilename: meta.originalFilename,
        ldipId:           meta.ldipId,
        sectorOffices:    preview.sectorOffices,
      });
      sessionStorage.removeItem(PREVIEW_KEY);
      sessionStorage.removeItem(META_KEY);
      toast.success("Import complete", `AIP FY${preview.fiscalYear} imported successfully.`);
      router.push("/budget-planning/aip");
    } catch (err) {
      setConfirmError(aipErrorMessage(err, "Confirm failed. Please try again."));
      setConfirming(false);
    }
  }

  function handleCancel() {
    sessionStorage.removeItem(PREVIEW_KEY);
    sessionStorage.removeItem(META_KEY);
    router.push("/budget-planning/aip");
  }

  if (!preview || !me) {
    return (
      <div className="p-6 flex items-center gap-3 text-sm text-slate-500">
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
          <h1 className="text-xl font-bold text-slate-800">Import Preview — AIP FY{preview.fiscalYear}</h1>
          <p className="text-sm text-slate-500 mt-0.5">Review the data before confirming. This cannot be undone.</p>
        </div>
        <div className="flex items-center gap-3 shrink-0 ml-6">
          <button
            onClick={handleConfirm}
            disabled={confirming}
            className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
              confirming ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
            }`}
          >
            {confirming ? "Importing…" : "Confirm Import"}
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
        {/* ── Left column: counts + sector breakdown ── */}
        <div className="lg:col-span-3 space-y-5">
          {/* Stat tiles */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <StatTile label="Office Records" value={preview.counts.offices} />
            <StatTile label="Programs"       value={preview.counts.programs} />
            <StatTile label="Projects"       value={preview.counts.projects} />
            <StatTile label="Activities"     value={preview.counts.activities} />
          </div>

          {/* Sector breakdown */}
          <div>
            <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
              Breakdown by Sector
            </h2>
            <div className="border border-slate-200 divide-y divide-slate-100 bg-white">
              {SECTOR_ORDER.map((sector) => {
                const stats = getSectorStats(sector);
                const officeCount = preview.sectorOffices[sector]?.length ?? 0;
                if (!officeCount) return null;
                return (
                  <div key={sector} className="px-4 py-3 grid grid-cols-4 items-center text-sm gap-2">
                    <span className="font-medium text-slate-700 col-span-1">
                      {SECTOR_LABEL[sector] ?? sector}
                    </span>
                    <span className="text-slate-500 text-xs text-right">
                      {officeCount} office{officeCount !== 1 ? "s" : ""}
                    </span>
                    <span className="text-slate-500 text-xs text-right">
                      {stats.programs} program{stats.programs !== 1 ? "s" : ""}
                    </span>
                    <span className="text-slate-500 text-xs text-right">
                      {stats.activities.toLocaleString()} activit{stats.activities !== 1 ? "ies" : "y"}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* ── Right column: file info + warnings ── */}
        <div className="lg:col-span-2 space-y-4">
          {/* File info */}
          <div className="bg-slate-50 border border-slate-200 p-5">
            <h3 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-3">File Info</h3>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between gap-2">
                <dt className="text-slate-500">Filename</dt>
                <dd className="text-slate-700 font-medium truncate text-right max-w-[60%]" title={meta?.originalFilename}>
                  {meta?.originalFilename ?? "—"}
                </dd>
              </div>
              <div className="flex justify-between gap-2">
                <dt className="text-slate-500">Fiscal Year</dt>
                <dd className="text-slate-700 font-medium">FY {preview.fiscalYear}</dd>
              </div>
              <div className="flex justify-between gap-2">
                <dt className="text-slate-500">LDIP Link</dt>
                <dd className="text-slate-400">None</dd>
              </div>
            </dl>
          </div>

          {/* Warnings — collapsible */}
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
