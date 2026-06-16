"use client";

/**
 * AIP Upload page — RAL-76.
 *
 * Accepts an .xlsm file (max 20 MB), posts it as raw binary to the upload
 * endpoint, stores the parsed preview in sessionStorage, then redirects to
 * the import-preview page for confirmation.
 *
 * "Manual Entry" tab is a disabled placeholder (deferred, v1.1).
 *
 * Endpoint: POST /api/budget-planning/aip/upload?fiscalYear=
 *   Body: raw .xlsm bytes (Content-Type: application/octet-stream)
 *   Response: { data: AipImportPreviewResponse }
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import api from "@/lib/api";
import { aipErrorMessage, uploadAipFile } from "@/lib/aip";
import type { MeResponse } from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const CURRENT_YEAR = new Date().getFullYear();
const FY_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1, CURRENT_YEAR + 2];
const MAX_FILE_SIZE = 20 * 1024 * 1024; // 20 MB

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

type Tab = "upload" | "manual";

export default function AipNewPage() {
  const router = useRouter();

  const [activeTab, setActiveTab] = useState<Tab>("upload");
  const [fiscalYear, setFiscalYear] = useState<number>(CURRENT_YEAR);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // ---------------------------------------------------------------------------
  // Auth check
  // ---------------------------------------------------------------------------

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canUploadAip) {
        router.replace("/budget-planning/aip");
        return;
      }
    });
  }, [router]);

  // ---------------------------------------------------------------------------
  // File validation
  // ---------------------------------------------------------------------------

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

  // ---------------------------------------------------------------------------
  // Dropzone handlers
  // ---------------------------------------------------------------------------

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragging(true);
  }, []);

  const handleDragLeave = useCallback(() => {
    setDragging(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const dropped = e.dataTransfer.files[0] ?? null;
    validateAndSet(dropped);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0] ?? null;
    validateAndSet(picked);
    // Reset input so the same file can be re-selected after removal
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ---------------------------------------------------------------------------
  // Upload & Preview
  // ---------------------------------------------------------------------------

  async function handleUpload() {
    if (!file) { setFileError("Please select a file before uploading."); return; }
    setUploadError(null);
    setUploading(true);
    try {
      const preview = await uploadAipFile(file, fiscalYear);
      sessionStorage.setItem("aip_import_preview", JSON.stringify(preview));
      sessionStorage.setItem(
        "aip_import_meta",
        JSON.stringify({ originalFilename: file.name, ldipId: null })
      );
      router.push("/budget-planning/aip/import-preview");
    } catch (err) {
      setUploadError(aipErrorMessage(err, "Upload failed. Please check the file and try again."));
      setUploading(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="p-6 max-w-2xl mx-auto">
      {/* Breadcrumb */}
      <p className="text-xs text-slate-500 mb-1">
        <Link href="/budget-planning/aip" className="hover:underline">Planning / AIP</Link>
        {" / New AIP"}
      </p>

      {/* Header */}
      <h1 className="text-xl font-bold text-slate-800 mb-0.5">Create New AIP</h1>
      <p className="text-sm text-slate-500 mb-6">
        Upload an .xlsm file or enter data manually.
      </p>

      {/* Tabs */}
      <div className="flex border-b border-slate-200 mb-6">
        <button
          onClick={() => setActiveTab("upload")}
          className={`px-5 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
            activeTab === "upload"
              ? "border-green-700 text-green-700"
              : "border-transparent text-slate-500 hover:text-slate-700"
          }`}
        >
          Upload File
        </button>
        <button
          disabled
          title="Manual entry is coming soon"
          className="px-5 py-2.5 text-sm font-medium border-b-2 border-transparent text-slate-300 cursor-not-allowed opacity-60"
        >
          Manual Entry
        </button>
      </div>

      {/* Upload tab content */}
      {activeTab === "upload" && (
        <div className="space-y-5">
          {/* Fiscal Year */}
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Fiscal Year
            </label>
            <select
              value={fiscalYear}
              onChange={(e) => setFiscalYear(Number(e.target.value))}
              className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 w-40"
            >
              {FY_OPTIONS.map((y) => (
                <option key={y} value={y}>{y}</option>
              ))}
            </select>
          </div>

          {/* Link to LDIP (placeholder — no LDIP records in v1.1) */}
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Link to LDIP <span className="text-slate-400 font-normal normal-case">(optional)</span>
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
              /* Selected file display */
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
              /* Drop zone */
              <label
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                className={`flex flex-col items-center justify-center border-2 border-dashed px-6 py-10 cursor-pointer transition-colors ${
                  dragging
                    ? "border-green-500 bg-green-50"
                    : "border-slate-300 hover:border-slate-400"
                }`}
              >
                <span className="text-3xl mb-2">📁</span>
                <p className="text-sm text-slate-600 mb-1">
                  Drag &amp; drop your .xlsm file here
                </p>
                <p className="text-xs text-slate-400 mb-3">or</p>
                <span className="px-4 py-1.5 border border-slate-300 text-sm text-slate-600 bg-white hover:bg-slate-50">
                  Browse File
                </span>
                <p className="text-xs text-slate-400 mt-3">
                  Accepts .xlsm files up to 20 MB
                </p>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".xlsm"
                  className="hidden"
                  onChange={handleFileInput}
                />
              </label>
            )}

            {fileError && (
              <p className="text-xs text-danger-500 mt-1.5">{fileError}</p>
            )}
          </div>

          {/* Upload error */}
          {uploadError && (
            <div className="border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
              {uploadError}
            </div>
          )}

          {/* Actions */}
          <div className="flex items-center gap-3 pt-1">
            <button
              onClick={handleUpload}
              disabled={uploading || !file}
              className={`px-5 py-2 text-sm font-medium text-white transition-colors ${
                uploading || !file
                  ? "bg-green-300 cursor-not-allowed"
                  : "bg-green-700 hover:bg-green-800"
              }`}
            >
              {uploading ? "Uploading…" : "Upload & Preview"}
            </button>
            <Link
              href="/budget-planning/aip"
              className="px-5 py-2 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors"
            >
              Cancel
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
