"use client";

/**
 * LDIP create page (RAL-61 stub; RAL-113 adds the Upload File tab).
 * Upload mirrors the AIP upload pattern: parse-only preview -> sessionStorage
 * stash -> confirm. Manual Entry is the existing LdipForm flow, unchanged.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { listOffices } from "@/lib/config";
import { ldipErrorMessage, uploadLdipFile } from "@/lib/ldip";
import { useMe } from "@/lib/me-cache";
import OfficeSelect from "@/components/ui/OfficeSelect";
import LdipForm from "../LdipForm";
import type { MeResponse, OfficeResponse } from "@/types";

const CURRENT_YEAR = new Date().getFullYear();
const MAX_FILE_SIZE = 20 * 1024 * 1024;

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

type Tab = "upload" | "manual";

function UploadTab({ me }: { me: MeResponse }) {
  const router = useRouter();

  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [officeId, setOfficeId] = useState<number | null>(null);
  const [yearStart, setYearStart] = useState(CURRENT_YEAR + 1);
  const [yearEnd, setYearEnd] = useState(CURRENT_YEAR + 3);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    listOffices({ active: "true" }).then(setOffices).catch(() => setOffices([]));
  }, []);

  useEffect(() => {
    if (me.officeId != null) setOfficeId(me.officeId);
  }, [me]);

  const isOfficeUser = me.officeId != null;
  const office = offices.find((o) => o.id === officeId) ?? null;

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
    if (officeId == null) { setUploadError("Office is required."); return; }
    if (yearStart > yearEnd) { setUploadError("Year start must be on or before year end."); return; }
    setUploadError(null);
    setUploading(true);
    try {
      const preview = await uploadLdipFile(file, yearStart, yearEnd, officeId);
      sessionStorage.setItem("ldip_import_preview", JSON.stringify(preview));
      sessionStorage.setItem("ldip_import_meta", JSON.stringify({ originalFilename: file.name }));
      router.push("/budget-planning/ldip/import-preview");
    } catch (err) {
      setUploadError(ldipErrorMessage(err, "Upload failed. Please check the file and try again."));
      setUploading(false);
    }
  }

  return (
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
              onChange={(e) => setYearStart(Number(e.target.value) || CURRENT_YEAR + 1)}
              className="w-full border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
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
              onChange={(e) => setYearEnd(Number(e.target.value) || CURRENT_YEAR + 3)}
              className="w-full border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
            />
          </div>
        </div>

        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Office
          </label>
          {isOfficeUser ? (
            <span className="inline-block text-sm text-slate-700 bg-slate-100 border border-slate-200 px-3 py-2">
              {office ? `${office.officeCode} — ${office.officeName}` : "—"}
            </span>
          ) : (
            <OfficeSelect
              className="w-96 max-w-full"
              offices={offices}
              value={officeId}
              onChange={setOfficeId}
              placeholder="— select office —"
            />
          )}
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
                <p className="text-xs text-slate-500">{formatBytes(file.size)}</p>
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
              <p className="text-xs text-slate-400 mb-3">or</p>
              <span className="px-4 py-1.5 border border-slate-300 text-sm text-slate-600 bg-white hover:bg-slate-50">
                Browse File
              </span>
              <p className="text-xs text-slate-400 mt-3">Accepts .xlsx files up to 20 MB</p>
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
            href="/budget-planning/ldip"
            className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors"
          >
            Cancel
          </Link>
        </div>
      </div>

      {/* ── Right column: help panel ── */}
      <div className="lg:col-span-2 space-y-3">
        <div className="bg-slate-50 border border-slate-200 p-4">
          <h3 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2.5">How it works</h3>
          <ol className="space-y-2">
            {[
              ["Set the year range and office", "Choose the planning period and confirm the office this document covers."],
              ["Upload your file",   "Drop the .xlsx file into the dropzone or browse to select it."],
              ["Review the preview", "Check the import counts and any warnings before committing."],
              ["Confirm import",     "Once confirmed, the LDIP is saved as a Draft record."],
            ].map(([title, desc], i) => (
              <li key={i} className="flex gap-3">
                <span className="shrink-0 w-5 h-5 rounded-full bg-green-700 text-white text-xs font-bold flex items-center justify-center mt-0.5">
                  {i + 1}
                </span>
                <div>
                  <p className="text-sm font-medium text-slate-700">{title}</p>
                  <p className="text-xs text-slate-500 mt-0.5">{desc}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>

        <div className="bg-slate-50 border border-slate-200 p-4">
          <h3 className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2.5">File Requirements</h3>
          <ul className="space-y-1.5">
            {[
              "Must be an Excel file (.xlsx)",
              "Maximum file size: 20 MB",
              "Must contain sheets named General, Social, Economic, Others",
              "Only rows matching the selected office's AIP ref code are imported",
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
  );
}

function TabBar({
  activeTab, onChange, canUpload,
}: { activeTab: Tab; onChange: (t: Tab) => void; canUpload: boolean }) {
  return (
    <div className="flex border-b border-slate-200 mb-4">
      {canUpload ? (
        <button
          onClick={() => onChange("upload")}
          className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
            activeTab === "upload"
              ? "border-green-700 text-green-700"
              : "border-transparent text-slate-500 hover:text-slate-700"
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
      <button
        onClick={() => onChange("manual")}
        className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
          activeTab === "manual"
            ? "border-green-700 text-green-700"
            : "border-transparent text-slate-500 hover:text-slate-700"
        }`}
      >
        Manual Entry
      </button>
    </div>
  );
}

export default function LdipNewPage() {
  const me = useMe((m) => m.canAccessBudgetPlanning, (m) => (m.officeId != null ? "/account" : "/dashboard"));
  const canUpload = me?.canUploadAip === true;

  // Office users (no upload rights) land straight on Manual Entry — the Upload
  // tab is PPDO-only.
  const [activeTab, setActiveTab] = useState<Tab>("upload");
  useEffect(() => {
    if (me && !canUpload) setActiveTab("manual");
  }, [me, canUpload]);

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
      <h1 className="text-xl font-bold text-slate-800 mb-0.5">Create New LDIP</h1>
      <p className="text-sm text-slate-500 mb-4">
        Upload an .xlsx file to import an LDIP document, or enter programs manually.
      </p>
      <TabBar activeTab={activeTab} onChange={setActiveTab} canUpload={canUpload} />
      <UploadTab me={me} />
    </div>
  );
}
