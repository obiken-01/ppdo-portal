"use client";

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import { aipErrorMessage, getAipById, uploadAipFile, createManualAipRecord } from "@/lib/aip";

const CURRENT_YEAR = new Date().getFullYear();
const FY_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1, CURRENT_YEAR + 2];
const MAX_FILE_SIZE = 20 * 1024 * 1024;

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

type Tab = "upload" | "manual";

/**
 * @param replaceId  RAL-178 — when set, the confirmed import full-replaces this existing
 *   record's hierarchy (re-upload a corrected file) instead of creating a new record.
 *   Threaded to import-preview via the sessionStorage meta.
 */
function UploadTab({ replaceId }: { replaceId: number | null }) {
  const router = useRouter();

  const [fiscalYear, setFiscalYear] = useState<number>(CURRENT_YEAR);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // Re-upload (RAL-178): lock the fiscal year to the record's ORIGINAL year — a re-upload
  // corrects the file's contents, not the fiscal year. Pre-fill from the target record so the
  // confirmed year matches what the record already stores.
  useEffect(() => {
    if (replaceId == null) return;
    getAipById(replaceId)
      .then((rec) => setFiscalYear(rec.fiscalYear))
      .catch(() => { /* leave default; the confirm still guards the target server-side */ });
  }, [replaceId]);

  function validateAndSet(f: File | null) {
    setFileError(null);
    if (!f) { setFile(null); return; }
    if (!f.name.toLowerCase().endsWith(".xlsm")) {
      setFileError("Only .xlsm files are accepted.");
      setFile(null);
      return;
    }
    if (f.size > MAX_FILE_SIZE) {
      setFileError(`File is too large (${formatBytes(f.size)}). Maximum is 20 MB.`);
      setFile(null);
      return;
    }
    setFile(f);
  }

  const handleDragOver  = useCallback((e: React.DragEvent) => { e.preventDefault(); setDragging(true); }, []);
  const handleDragLeave = useCallback(() => setDragging(false), []);
  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragging(false);
    validateAndSet(e.dataTransfer.files[0] ?? null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    validateAndSet(e.target.files?.[0] ?? null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function handleUpload() {
    if (!file) { setFileError("Please select a file before uploading."); return; }
    setUploadError(null);
    setUploading(true);
    try {
      const preview = await uploadAipFile(file, fiscalYear);
      sessionStorage.setItem("aip_import_preview", JSON.stringify(preview));
      sessionStorage.setItem(
        "aip_import_meta",
        JSON.stringify({ originalFilename: file.name, ldipId: null, replaceId })
      );
      router.push("/budget-planning/aip/import-preview");
    } catch (err) {
      setUploadError(aipErrorMessage(err, "Upload failed. Please check the file and try again."));
      setUploading(false);
    }
  }

  return (
    <div className="space-y-3">
      {replaceId != null && (
        <div className="border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span className="font-semibold">Re-upload mode.</span>{" "}
          Confirming replaces the existing record&apos;s offices, programs, projects, and
          activities with this file&apos;s contents. The record keeps its ID and history — only
          its hierarchy changes.
        </div>
      )}

      {/* Fiscal Year */}
      <div>
        <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
          Fiscal Year
        </label>
        {replaceId != null ? (
          <p className="text-sm text-slate-700 px-3 py-2 bg-slate-100 border border-slate-200 w-40">
            FY {fiscalYear}
          </p>
        ) : (
          <select
            value={fiscalYear}
            onChange={(e) => setFiscalYear(Number(e.target.value))}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 w-40"
          >
            {FY_OPTIONS.map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        )}
      </div>

      {/* Link to LDIP */}
      <div>
        <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
          Link to LDIP <span className="text-slate-600 font-normal normal-case">(optional)</span>
        </label>
        <select
          disabled
          className="border border-slate-200 bg-slate-50 text-sm px-3 py-2 text-slate-400 w-64 cursor-not-allowed"
        >
          <option>No LDIP records available</option>
        </select>
      </div>

      {/* Dropzone */}
      <div>
        <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
          AIP File
        </label>

        {file ? (
          <div className="border border-slate-200 bg-slate-50 px-4 py-3 flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-slate-700">{file.name}</p>
              <p className="text-xs text-slate-600">{formatBytes(file.size)}</p>
            </div>
            <button
              onClick={() => { setFile(null); setFileError(null); }}
              className="text-xs text-danger-500 hover:underline ml-4"
            >
              ✕ Remove
            </button>
          </div>
        ) : (
          <label
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            className={`flex flex-col items-center justify-center border-2 border-dashed px-6 py-8 cursor-pointer transition-colors ${
              dragging ? "border-green-500 bg-green-50" : "border-slate-300 hover:border-slate-400"
            }`}
          >
            <span className="text-3xl mb-2">📁</span>
            <p className="text-sm text-slate-600 mb-1">Drag &amp; drop your .xlsm file here</p>
            <p className="text-xs text-slate-600 mb-3">or</p>
            <span className="px-4 py-1.5 border border-slate-300 text-sm text-slate-600 bg-white hover:bg-slate-50">
              Browse File
            </span>
            <p className="text-xs text-slate-600 mt-3">Accepts .xlsm files up to 20 MB</p>
            <input ref={fileInputRef} type="file" accept=".xlsm" className="hidden" onChange={handleFileInput} />
          </label>
        )}

        {fileError && <p className="text-xs text-danger-500 mt-1.5">{fileError}</p>}
      </div>

      {uploadError && (
        <div className="border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {uploadError}
        </div>
      )}

      <div className="flex items-center gap-3">
        <button
          onClick={handleUpload}
          disabled={uploading || !file}
          className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
            uploading || !file ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
          }`}
        >
          {uploading ? "Uploading…" : "Upload & Preview"}
        </button>
        <Link
          href={replaceId != null ? `/budget-planning/aip/detail?id=${replaceId}` : "/budget-planning/aip"}
          className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </Link>
      </div>
    </div>
  );
}

// ── Manual Entry tab (RAL-62, folded into the detail page for this session's Add/Edit/Delete
// follow-up) — this used to be its own multi-step wizard for building the whole hierarchy.
// It now only creates the blank Draft record; building it out (Add Office/Program/Project/
// Activity, plus editing) happens on the detail page's own inline table controls, so adding
// and editing an AIP share one UI instead of two.

function ManualEntryTab() {
  const router = useRouter();

  const [fiscalYear, setFiscalYear] = useState<number>(CURRENT_YEAR);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  async function handleStart() {
    setCreating(true);
    setCreateError(null);
    try {
      const rec = await createManualAipRecord({ fiscalYear });
      router.push(`/budget-planning/aip/detail?id=${rec.id}`);
    } catch (err) {
      setCreateError(aipErrorMessage(err, "Could not start a new AIP. Please try again."));
      setCreating(false);
    }
  }

  return (
    <div className="space-y-3">
      <div>
        <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
          Fiscal Year
        </label>
        <select
          value={fiscalYear}
          onChange={(e) => setFiscalYear(Number(e.target.value))}
          className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 w-40"
        >
          {FY_OPTIONS.map((y) => <option key={y} value={y}>{y}</option>)}
        </select>
      </div>
      {createError && (
        <div className="border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {createError}
        </div>
      )}
      <div className="flex items-center gap-3">
        <button
          onClick={handleStart}
          disabled={creating}
          className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
            creating ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
          }`}
        >
          {creating ? "Starting…" : "Start New AIP"}
        </button>
        <Link
          href="/budget-planning/aip"
          className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </Link>
      </div>
    </div>
  );
}

function AipNewInner() {
  // RAL-178 — re-upload into an existing record (?replaceId=). Upload-only, so it pins the
  // Upload tab and is passed through to the confirm step.
  const searchParams = useSearchParams();
  const rawReplaceId = searchParams.get("replaceId");
  const replaceId = rawReplaceId != null && /^\d+$/.test(rawReplaceId) ? Number(rawReplaceId) : null;

  // RAL-62 — page gate relaxed from canUploadAip to canAccessBudgetPlanning so office users
  // (who can never upload an .xlsm) can still reach the Manual Entry tab. me.canUploadAip
  // (checked below) independently gates the Upload tab itself.
  const me = useMe((m) => m.canAccessBudgetPlanning, "/budget-planning/aip");
  const canUpload = me?.canUploadAip === true;

  const [activeTab, setActiveTab] = useState<Tab>("upload");
  useEffect(() => {
    if (me && !canUpload) setActiveTab("manual");
  }, [me, canUpload]);

  return (
    <div className="px-6 py-4">
      {/* Header */}
      <h1 className="text-xl font-bold text-slate-800 mb-0.5">
        {replaceId != null ? "Re-upload AIP" : "Create New AIP"}
      </h1>
      <p className="text-sm text-slate-600 mb-4">
        {replaceId != null
          ? "Upload a corrected .xlsm file to replace this AIP's hierarchy."
          : "Upload an .xlsm file to import an Annual Investment Program."}
      </p>

      <div className="grid grid-cols-1 lg:grid-cols-5 gap-6 items-start">
        {/* ── Left column: form ── */}
        <div className="lg:col-span-3">
          {/* Tabs */}
          <div className="flex border-b border-slate-200 mb-4">
            {(replaceId == null || canUpload) && (
              <button
                onClick={() => setActiveTab("upload")}
                disabled={!canUpload}
                title={canUpload ? undefined : "Only PPDO uploaders can upload an .xlsm file"}
                className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
                  !canUpload
                    ? "border-transparent text-slate-300 cursor-not-allowed opacity-60"
                    : activeTab === "upload"
                    ? "border-green-700 text-green-700"
                    : "border-transparent text-slate-600 hover:text-slate-700"
                }`}
              >
                Upload File
              </button>
            )}
            {replaceId == null && (
              <button
                onClick={() => setActiveTab("manual")}
                className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
                  activeTab === "manual"
                    ? "border-green-700 text-green-700"
                    : "border-transparent text-slate-600 hover:text-slate-700"
                }`}
              >
                Manual Entry
              </button>
            )}
          </div>

          {activeTab === "upload" && canUpload && <UploadTab replaceId={replaceId} />}
          {activeTab === "manual" && <ManualEntryTab />}
        </div>

        {/* ── Right column: help panel ── */}
        <div className="lg:col-span-2 space-y-3">
          {activeTab === "manual" ? (
            <div className="bg-slate-50 border border-slate-200 p-4">
              <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2.5">How it works</h3>
              <ol className="space-y-2">
                {([
                  ["Start the record", "Pick a fiscal year — this creates a blank Draft AIP."],
                  ["You're taken to the record", "Building it out happens on the AIP's own page."],
                  ["Add offices, programs, projects, activities", "Use the same + Add and Edit controls you'll use later to make changes — one UI for building and editing."],
                  ["Finish anytime", "Finalize is available on that page once the AIP is ready."],
                ] as [string, string][]).map(([title, desc], i) => (
                  <li key={i} className="flex gap-3">
                    <span className="shrink-0 w-5 h-5 rounded-full bg-green-700 text-white text-xs font-bold flex items-center justify-center mt-0.5">
                      {i + 1}
                    </span>
                    <div>
                      <p className="text-sm font-medium text-slate-700">{title}</p>
                      <p className="text-xs text-slate-600 mt-0.5">{desc}</p>
                    </div>
                  </li>
                ))}
              </ol>
            </div>
          ) : (
            <>
              {/* Steps */}
              <div className="bg-slate-50 border border-slate-200 p-4">
                <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2.5">How it works</h3>
                <ol className="space-y-2">
                  {(replaceId != null
                    ? [
                        ["Fiscal year is locked", "Re-upload corrects this AIP's contents — the fiscal year cannot change."],
                        ["Upload the corrected file", "Drop the .xlsm file into the dropzone or browse to select it."],
                        ["Review the preview", "Check the import counts and any warnings before committing."],
                        ["Confirm re-upload", "Once confirmed, this record's hierarchy is replaced — same ID, corrected content."],
                      ]
                    : [
                        ["Select fiscal year", "Choose the year this AIP covers."],
                        ["Upload your file",   "Drop the .xlsm file into the dropzone or browse to select it."],
                        ["Review the preview", "Check the import counts and any warnings before committing."],
                        ["Confirm import",     "Once confirmed, the AIP is saved as a Draft record."],
                      ]
                  ).map(([title, desc], i) => (
                    <li key={i} className="flex gap-3">
                      <span className="shrink-0 w-5 h-5 rounded-full bg-green-700 text-white text-xs font-bold flex items-center justify-center mt-0.5">
                        {i + 1}
                      </span>
                      <div>
                        <p className="text-sm font-medium text-slate-700">{title}</p>
                        <p className="text-xs text-slate-600 mt-0.5">{desc}</p>
                      </div>
                    </li>
                  ))}
                </ol>
              </div>

              {/* File requirements */}
              <div className="bg-slate-50 border border-slate-200 p-4">
                <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2.5">File Requirements</h3>
                <ul className="space-y-1.5">
                  {[
                    "Must be an Excel macro file (.xlsm)",
                    "Maximum file size: 20 MB",
                    "Must contain sheets named GENERAL_*, SOCIAL_*, ECONOMIC_*, OTHERS_*",
                    "AIP ref codes must follow the 5–8 segment format",
                  ].map((req, i) => (
                    <li key={i} className="flex items-start gap-2 text-xs text-slate-600">
                      <span className="shrink-0 text-green-600 mt-0.5">✓</span>
                      {req}
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

// useSearchParams requires a Suspense boundary during prerender (Next.js app router).
export default function AipNewPage() {
  return (
    <Suspense fallback={null}>
      <AipNewInner />
    </Suspense>
  );
}
