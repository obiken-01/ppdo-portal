"use client";

/**
 * LDIP create page (RAL-61 stub; RAL-113 adds the Upload File tab).
 * Upload mirrors the AIP upload pattern: parse-only preview -> sessionStorage
 * stash -> confirm. Manual Entry is the existing LdipForm flow, unchanged.
 *
 * The uploaded workbook covers every office in one file — there is no office
 * picker. The server auto-detects every office block in the file (matched by
 * AIP ref code against Config -> Offices) and Confirm creates ONE Draft LDIP
 * record spanning every office found (mirrors AIP's own upload — one document
 * holds all offices, not one document per office).
 */

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { getLdipById, ldipErrorMessage, uploadLdipFile } from "@/lib/ldip";
import { useMe } from "@/lib/me-cache";
import LdipForm from "../LdipForm";

const CURRENT_YEAR = new Date().getFullYear();
const MAX_FILE_SIZE = 20 * 1024 * 1024;

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

type Tab = "upload" | "manual";

/**
 * @param replaceId  RAL-114 — when set, the confirmed import full-replaces this
 *   existing record's hierarchy (re-upload a corrected file) instead of creating a
 *   new record. Threaded to import-preview via the sessionStorage meta.
 */
function UploadTab({ replaceId }: { replaceId: number | null }) {
  const router = useRouter();

  const [yearStart, setYearStart] = useState(CURRENT_YEAR + 1);
  const [yearEnd, setYearEnd] = useState(CURRENT_YEAR + 3);
  const [replaceRefCode, setReplaceRefCode] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // Re-upload (RAL-114): lock the year range to the record's ORIGINAL period — a
  // re-upload corrects the file's contents, not the planning period. Pre-fill from
  // the target record so the confirmed years match what the record already stores.
  useEffect(() => {
    if (replaceId == null) return;
    getLdipById(replaceId)
      .then((rec) => {
        setYearStart(rec.fiscalYearStart);
        setYearEnd(rec.fiscalYearEnd);
        setReplaceRefCode(rec.refCode);
      })
      .catch(() => { /* leave defaults; the confirm still guards the target server-side */ });
  }, [replaceId]);

  function validateAndSet(f: File | null) {
    setFileError(null);
    if (!f) { setFile(null); return; }
    if (!f.name.toLowerCase().endsWith(".xlsx")) {
      setFileError("Only .xlsx files are accepted.");
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
    if (yearStart > yearEnd) { setUploadError("Year start must be on or before year end."); return; }
    setUploadError(null);
    setUploading(true);
    try {
      const preview = await uploadLdipFile(file, yearStart, yearEnd);
      sessionStorage.setItem("ldip_import_preview", JSON.stringify(preview));
      sessionStorage.setItem(
        "ldip_import_meta",
        JSON.stringify({ originalFilename: file.name, replaceId })
      );
      router.push("/budget-planning/ldip/import-preview");
    } catch (err) {
      setUploadError(ldipErrorMessage(err, "Upload failed. Please check the file and try again."));
      setUploading(false);
    }
  }

  return (
    <div>
      {replaceId != null && (
        <div className="mb-4 border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span className="font-semibold">Re-upload mode.</span>{" "}
          Confirming replaces{" "}
          {replaceRefCode ? <span className="font-semibold">{replaceRefCode}</span> : "the existing record"}
          &apos;s programs with this file&apos;s contents. The record keeps its reference code, planning
          period, and history — only its programs change.
        </div>
      )}
      <p className="text-sm text-slate-600 mb-4">
        Upload an .xlsx file to import an LDIP document, or enter programs manually.
      </p>
      <div className="grid grid-cols-1 lg:grid-cols-5 gap-6 items-start">
        {/* ── Left column: form ── */}
        <div className="lg:col-span-3 space-y-3">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Year Start
              </label>
              <input
                type="number"
                min={2020}
                max={2055}
                value={yearStart}
                disabled={replaceId != null}
                onChange={(e) => setYearStart(Number(e.target.value) || CURRENT_YEAR + 1)}
                className="w-full border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-500 disabled:cursor-not-allowed"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Year End
              </label>
              <input
                type="number"
                min={2020}
                max={2060}
                value={yearEnd}
                disabled={replaceId != null}
                onChange={(e) => setYearEnd(Number(e.target.value) || CURRENT_YEAR + 3)}
                className="w-full border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-500 disabled:cursor-not-allowed"
              />
            </div>
          </div>

          {/* Dropzone */}
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              LDIP File
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
                <p className="text-sm text-slate-600 mb-1">Drag &amp; drop your .xlsx file here</p>
                <p className="text-xs text-slate-600 mb-3">or</p>
                <span className="px-4 py-1.5 border border-slate-300 text-sm text-slate-600 bg-white hover:bg-slate-50">
                  Browse File
                </span>
                <p className="text-xs text-slate-600 mt-3">Accepts .xlsx files up to 20 MB</p>
                <input ref={fileInputRef} type="file" accept=".xlsx" className="hidden" onChange={handleFileInput} />
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
              href={replaceId != null ? `/budget-planning/ldip/edit?id=${replaceId}` : "/budget-planning/ldip"}
              className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors"
            >
              Cancel
            </Link>
          </div>
        </div>

        {/* ── Right column: help panel ── */}
        <div className="lg:col-span-2 space-y-3">
          <div className="bg-slate-50 border border-slate-200 p-4">
            <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2.5">How it works</h3>
            <ol className="space-y-2">
              {[
                ["Set the year range", "Choose the planning period this document covers."],
                ["Upload your file",   "Drop the .xlsx file into the dropzone or browse to select it."],
                ["Review the preview", "Every office found in the file is listed with its counts and any warnings."],
                ["Confirm import",     "Once confirmed, one Draft LDIP record is created, spanning every office found."],
              ].map(([title, desc], i) => (
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

          <div className="bg-slate-50 border border-slate-200 p-4">
            <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2.5">File Requirements</h3>
            <ul className="space-y-1.5">
              {[
                "Must be an Excel file (.xlsx)",
                "Maximum file size: 20 MB",
                "Must contain sheets named General, Social, Economic, Others",
                "The file covers every office — all of them are imported into one Draft record",
                "Only PPDO staff can upload an LDIP file (same as AIP)",
              ].map((req, i) => (
                <li key={i} className="flex items-start gap-2 text-xs text-slate-600">
                  <span className="shrink-0 text-green-600 mt-0.5">✓</span>
                  {req}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

function TabBar({
  activeTab, onChange, canUpload, hideManual = false,
}: { activeTab: Tab; onChange: (t: Tab) => void; canUpload: boolean; hideManual?: boolean }) {
  return (
    <div className="flex border-b border-slate-200 mb-4">
      {canUpload ? (
        <button
          onClick={() => onChange("upload")}
          className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
            activeTab === "upload"
              ? "border-green-700 text-green-700"
              : "border-transparent text-slate-600 hover:text-slate-700"
          }`}
        >
          Upload File
        </button>
      ) : (
        <span
          title="Only PPDO can upload an LDIP file"
          className="px-5 py-2.5 text-sm font-medium border-b-2 border-transparent text-slate-300 cursor-not-allowed"
        >
          Upload File
        </span>
      )}
      {!hideManual && (
        <button
          onClick={() => onChange("manual")}
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
  );
}

function LdipNewInner() {
  const me = useMe((m) => m.canAccessBudgetPlanning, (m) => (m.officeId != null ? "/account" : "/dashboard"));
  const canUpload = me?.canUploadAip === true;

  // RAL-114 — re-upload into an existing record (?replaceId=). Upload-only, so it
  // pins the Upload tab and is passed through to the confirm step.
  const searchParams = useSearchParams();
  const rawReplaceId = searchParams.get("replaceId");
  const replaceId = rawReplaceId != null && /^\d+$/.test(rawReplaceId) ? Number(rawReplaceId) : null;

  // Office users (no upload rights) land straight on Manual Entry — the Upload
  // tab is PPDO-only. Re-upload always stays on the Upload tab.
  const [activeTab, setActiveTab] = useState<Tab>("upload");
  useEffect(() => {
    if (me && !canUpload && replaceId == null) setActiveTab("manual");
  }, [me, canUpload, replaceId]);

  if (!me) return null;

  // LdipForm renders its own full-page header ("LDIP Entry Form") and p-6/max-w-4xl
  // wrapper, so the tab bar sits just above it rather than duplicating a page title
  // or nesting a second padded container.
  if (activeTab === "manual") {
    return (
      <div>
        <div className="px-6 pt-4">
          <TabBar activeTab={activeTab} onChange={setActiveTab} canUpload={canUpload} />
        </div>
        <LdipForm />
      </div>
    );
  }

  return (
    <div className="px-6 py-4">
      <TabBar
        activeTab={activeTab}
        onChange={setActiveTab}
        canUpload={canUpload}
        hideManual={replaceId != null}
      />
      <UploadTab replaceId={replaceId} />
    </div>
  );
}

// useSearchParams requires a Suspense boundary during prerender (Next.js app router).
export default function LdipNewPage() {
  return (
    <Suspense fallback={null}>
      <LdipNewInner />
    </Suspense>
  );
}
