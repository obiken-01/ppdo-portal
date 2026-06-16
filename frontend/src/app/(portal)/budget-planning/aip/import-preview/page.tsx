"use client";

/**
 * AIP Import Preview page — RAL-76.
 *
 * Reads the parsed AIP hierarchy from sessionStorage (written by the upload
 * page). Shows counts, sector breakdown, and warnings before the user commits
 * the import. "Confirm Import" posts to /confirm then redirects to the AIP list.
 *
 * NOTE — "Office Records" label (not "Offices"):  AIP Level-1 rows include
 * sub-offices that are not in the offices config table, so the count may be
 * higher than the 16 seeded top-level offices.
 *
 * Endpoints:
 *   POST /api/budget-planning/aip/confirm  (body: AipImportConfirmRequest)
 */

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { aipErrorMessage, confirmAipImport } from "@/lib/aip";
import { useToast } from "@/components/ui/Toast";
import type { AipImportPreviewResponse, MeResponse } from "@/types";

// ---------------------------------------------------------------------------
// sessionStorage keys
// ---------------------------------------------------------------------------

const PREVIEW_KEY = "aip_import_preview";
const META_KEY    = "aip_import_meta";

interface ImportMeta {
  originalFilename: string;
  ldipId: number | null;
}

// ---------------------------------------------------------------------------
// Stat tile
// ---------------------------------------------------------------------------

function StatTile({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-white border border-slate-200 px-5 py-5">
      <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">{label}</p>
      <p className="mt-2 text-2xl font-bold text-slate-800 tabular-nums">{value.toLocaleString()}</p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Sector display order
// ---------------------------------------------------------------------------

const SECTOR_ORDER = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"];
const SECTOR_LABEL: Record<string, string> = {
  GENERAL: "General Services",
  SOCIAL:  "Social Services",
  ECONOMIC: "Economic Services",
  OTHERS:  "Other Services",
};

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AipImportPreviewPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [me, setMe] = useState<MeResponse | null>(null);
  const [preview, setPreview] = useState<AipImportPreviewResponse | null>(null);
  const [meta, setMeta] = useState<ImportMeta | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [confirmError, setConfirmError] = useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Auth + read sessionStorage
  // ---------------------------------------------------------------------------

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canUploadAip) {
        router.replace("/budget-planning/aip");
        return;
      }
      setMe(data);
    });

    const rawPreview = sessionStorage.getItem(PREVIEW_KEY);
    const rawMeta   = sessionStorage.getItem(META_KEY);

    if (!rawPreview || !rawMeta) {
      router.replace("/budget-planning/aip/new");
      return;
    }

    try {
      setPreview(JSON.parse(rawPreview) as AipImportPreviewResponse);
      setMeta(JSON.parse(rawMeta) as ImportMeta);
    } catch {
      router.replace("/budget-planning/aip/new");
    }
  }, [router]);

  // ---------------------------------------------------------------------------
  // Sector breakdown helper
  // ---------------------------------------------------------------------------

  function getSectorStats(sector: string): { programs: number; activities: number } {
    if (!preview) return { programs: 0, activities: 0 };
    const offices = preview.sectorOffices[sector] ?? [];
    let programs = 0;
    let activities = 0;
    for (const off of offices) {
      programs += off.programs.length;
      for (const prog of off.programs) {
        for (const proj of prog.projects) {
          activities += proj.activities.length;
        }
      }
    }
    return { programs, activities };
  }

  // ---------------------------------------------------------------------------
  // Confirm import
  // ---------------------------------------------------------------------------

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

  // ---------------------------------------------------------------------------
  // Loading state (waiting for sessionStorage read)
  // ---------------------------------------------------------------------------

  if (!preview || !me) {
    return (
      <div className="p-6 flex items-center gap-3 text-sm text-slate-500">
        <span className="inline-block w-4 h-4 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />
        Loading preview…
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="p-6 max-w-3xl mx-auto">
      {/* Breadcrumb */}
      <p className="text-xs text-slate-500 mb-1">
        <Link href="/budget-planning/aip" className="hover:underline">Planning / AIP</Link>
        {" / Import Preview"}
      </p>

      {/* Header */}
      <h1 className="text-xl font-bold text-slate-800 mb-0.5">
        Import Preview — AIP FY{preview.fiscalYear}
      </h1>
      <p className="text-sm text-slate-500 mb-6">
        Review the data before confirming. This cannot be undone.
      </p>

      {/* Stat tiles */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-6">
        <StatTile label="Office Records" value={preview.counts.offices} />
        <StatTile label="Programs"       value={preview.counts.programs} />
        <StatTile label="Projects"       value={preview.counts.projects} />
        <StatTile label="Activities"     value={preview.counts.activities} />
      </div>

      {/* Sector breakdown */}
      <div className="mb-6">
        <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
          Preview by Sector
        </h2>
        <div className="border border-slate-200 divide-y divide-slate-100">
          {SECTOR_ORDER.map((sector) => {
            const stats = getSectorStats(sector);
            const hasData = (preview.sectorOffices[sector]?.length ?? 0) > 0;
            if (!hasData) return null;
            return (
              <div key={sector} className="px-4 py-3 flex items-center justify-between text-sm">
                <span className="font-medium text-slate-700">
                  {SECTOR_LABEL[sector] ?? sector}
                </span>
                <span className="text-slate-500">
                  {stats.programs} program{stats.programs !== 1 ? "s" : ""}{" "}
                  &middot; {stats.activities.toLocaleString()} activit{stats.activities !== 1 ? "ies" : "y"}
                </span>
              </div>
            );
          })}
        </div>
      </div>

      {/* Warnings */}
      {preview.warnings.length > 0 && (
        <div className="mb-6">
          <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
            Warnings ({preview.warnings.length})
          </h2>
          <div className="border border-amber-200 bg-amber-50 px-4 py-3">
            <ul className="space-y-1">
              {preview.warnings.map((w, i) => (
                <li key={i} className="text-sm text-amber-800 flex items-start gap-2">
                  <span className="mt-0.5 shrink-0">⚠</span>
                  <span>{w}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}

      {/* Confirm error */}
      {confirmError && (
        <div className="mb-4 border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {confirmError}
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center gap-3">
        <button
          onClick={handleConfirm}
          disabled={confirming}
          className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
            confirming
              ? "bg-green-300 cursor-not-allowed"
              : "bg-green-700 hover:bg-green-800"
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
  );
}
