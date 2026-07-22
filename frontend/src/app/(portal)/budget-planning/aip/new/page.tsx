"use client";

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import {
  aipErrorMessage, getAipById, uploadAipFile,
  createManualAipRecord, addAipOffice, addAipProgram, addAipProject, addAipActivity,
} from "@/lib/aip";
import { listOffices, listFundingSources } from "@/lib/config";
import MoneyInput from "@/components/ui/MoneyInput";
import type {
  AipRecordResponse, AipOfficeDetail, AipProgramDetail, AipProjectDetail,
  OfficeResponse, FundingSourceResponse,
} from "@/types";

const CURRENT_YEAR = new Date().getFullYear();
const FY_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1, CURRENT_YEAR + 2];
const MAX_FILE_SIZE = 20 * 1024 * 1024;

// ── Manual entry constants (RAL-62) ───────────────────────────────────────────

const SECTOR_OPTIONS = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"] as const;
// Numeric prefix each sector contributes to an office-level (5-segment) AIP ref code —
// {prefix}-000-1-{Office.OfficeRefCode}. Client-side mirror of AipSector.Prefixes on the
// backend, used only for the live ref-code preview; the server computes the real value.
const SECTOR_PREFIX: Record<string, string> = { GENERAL: "1000", SOCIAL: "3000", ECONOMIC: "8000", OTHERS: "9000" };
const ESRE_OPTIONS = [
  { value: "SS", label: "SS — Social Services" },
  { value: "ES", label: "ES — Economic Services" },
  { value: "ID", label: "ID — Infrastructure Development" },
  { value: "EN", label: "EN — Environment" },
];
const FUNCTION_BAND_OPTIONS = ["CORE", "STRATEGIC", "SUPPORT"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

/** Next zero-padded 3-digit segment after the highest existing sibling suffix — a client-side
 * preview only; the server (AipService.NextRefCode) computes the value actually persisted. */
function previewNextRefCode(parentRefCode: string, siblingRefCodes: string[]): string {
  const max = siblingRefCodes.reduce((m, rc) => {
    const n = parseInt(rc.split("-").pop() ?? "", 10);
    return Number.isFinite(n) && n > m ? n : m;
  }, 0);
  return `${parentRefCode}-${String(max + 1).padStart(3, "0")}`;
}

type EntryLevel = "office" | "program" | "project" | "activity";

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

// ── Manual Entry tab (RAL-62) ────────────────────────────────────────────────
// One node at a time: pick an Entry Level tab, pick the parent (for Program/Project/
// Activity), fill the level's fields, Add. The tree below shows everything added so far
// and doubles as the parent picker (click a node to select it).

function ManualEntryTab() {
  const router = useRouter();

  const [fiscalYear, setFiscalYear] = useState<number>(CURRENT_YEAR);
  const [aip, setAip] = useState<AipRecordResponse | null>(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const [offices, setOffices] = useState<AipOfficeDetail[]>([]);
  const [officeConfigs, setOfficeConfigs] = useState<OfficeResponse[]>([]);
  const [fundingSources, setFundingSources] = useState<FundingSourceResponse[]>([]);

  const [level, setLevel] = useState<EntryLevel>("office");
  const [selectedOfficeId, setSelectedOfficeId]   = useState<number | null>(null);
  const [selectedProgramId, setSelectedProgramId] = useState<number | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(null);

  // Office fields
  const [officeConfigId, setOfficeConfigId] = useState<string>("");
  const [officeName, setOfficeName] = useState<string>("");
  const [sector, setSector] = useState<string>("GENERAL");
  // Program fields
  const [programName, setProgramName]   = useState("");
  const [functionBand, setFunctionBand] = useState("CORE");
  // Project fields
  const [projectName, setProjectName] = useState("");
  // Activity fields
  const [activityName, setActivityName]             = useState("");
  const [esreCode, setEsreCode]                     = useState("");
  const [implementingOffice, setImplementingOffice] = useState("");
  const [startMonth, setStartMonth]                 = useState("");
  const [endMonth, setEndMonth]                     = useState("");
  const [expectedOutputs, setExpectedOutputs]       = useState("");
  const [fundingSourceRaw, setFundingSourceRaw]     = useState("");
  const [ps, setPs]                     = useState<number | null>(null);
  const [mooe, setMooe]                 = useState<number | null>(null);
  const [co, setCo]                     = useState<number | null>(null);
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(null);
  const [ccMitigation, setCcMitigation] = useState<number | null>(null);
  const [ccTypologyCode, setCcTypologyCode] = useState("");

  const [saving, setSaving]       = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    listOffices({ active: "true" }).then(setOfficeConfigs).catch(() => {});
    listFundingSources({ active: "true" }).then(setFundingSources).catch(() => {});
  }, []);

  async function handleStart() {
    setCreating(true);
    setCreateError(null);
    try {
      const rec = await createManualAipRecord({ fiscalYear });
      setAip(rec);
    } catch (err) {
      setCreateError(aipErrorMessage(err, "Could not start a new AIP. Please try again."));
    } finally {
      setCreating(false);
    }
  }

  const selectedOffice  = offices.find((o) => o.id === selectedOfficeId) ?? null;
  const selectedProgram = selectedOffice?.programs.find((p) => p.id === selectedProgramId) ?? null;
  const selectedProject = selectedProgram?.projects.find((p) => p.id === selectedProjectId) ?? null;

  const totalProgramCount  = offices.reduce((n, o) => n + o.programs.length, 0);
  const totalProjectCount  = offices.flatMap((o) => o.programs).reduce((n, p) => n + p.projects.length, 0);
  const totalActivityCount = offices.flatMap((o) => o.programs).flatMap((p) => p.projects)
    .reduce((n, p) => n + p.activities.length, 0);

  function selectOffice(o: AipOfficeDetail) {
    setSelectedOfficeId(o.id);
    setSelectedProgramId(null);
    setSelectedProjectId(null);
  }
  function selectProgram(o: AipOfficeDetail, p: AipProgramDetail) {
    setSelectedOfficeId(o.id);
    setSelectedProgramId(p.id);
    setSelectedProjectId(null);
  }
  function selectProject(o: AipOfficeDetail, p: AipProgramDetail, j: AipProjectDetail) {
    setSelectedOfficeId(o.id);
    setSelectedProgramId(p.id);
    setSelectedProjectId(j.id);
  }

  async function handleAddOffice() {
    if (!aip || !officeConfigId) return;
    setSaving(true);
    setSaveError(null);
    try {
      const off = await addAipOffice(aip.id, {
        officeConfigId: Number(officeConfigId), sector, name: officeName.trim() || null,
      });
      setOffices((prev) => [...prev, off]);
      selectOffice(off);
      setOfficeConfigId("");
      setOfficeName("");
      setLevel("program");
    } catch (err) {
      setSaveError(aipErrorMessage(err, "Could not add office."));
    } finally {
      setSaving(false);
    }
  }

  async function handleAddProgram() {
    if (!selectedOffice || !programName.trim()) return;
    setSaving(true);
    setSaveError(null);
    try {
      const prog = await addAipProgram(selectedOffice.id, { name: programName.trim(), functionBand });
      setOffices((prev) => prev.map((o) =>
        o.id === selectedOffice.id ? { ...o, programs: [...o.programs, prog] } : o
      ));
      selectProgram(selectedOffice, prog);
      setProgramName("");
      setLevel("project");
    } catch (err) {
      setSaveError(aipErrorMessage(err, "Could not add program."));
    } finally {
      setSaving(false);
    }
  }

  async function handleAddProject() {
    if (!selectedOffice || !selectedProgram || !projectName.trim()) return;
    setSaving(true);
    setSaveError(null);
    try {
      const proj = await addAipProject(selectedProgram.id, { name: projectName.trim() });
      setOffices((prev) => prev.map((o) =>
        o.id !== selectedOffice.id ? o : {
          ...o,
          programs: o.programs.map((p) =>
            p.id !== selectedProgram.id ? p : { ...p, projects: [...p.projects, proj] }
          ),
        }
      ));
      selectProject(selectedOffice, selectedProgram, proj);
      setProjectName("");
      setLevel("activity");
    } catch (err) {
      setSaveError(aipErrorMessage(err, "Could not add project."));
    } finally {
      setSaving(false);
    }
  }

  async function handleAddActivity() {
    if (!selectedOffice || !selectedProgram || !selectedProject || !activityName.trim()) return;
    setSaving(true);
    setSaveError(null);
    try {
      const act = await addAipActivity(selectedProject.id, {
        name: activityName.trim(),
        esreCode: esreCode || null,
        implementingOffice: implementingOffice.trim() || null,
        startDate: startMonth || null,
        endDate: endMonth || null,
        expectedOutputs: expectedOutputs.trim() || null,
        fundingSourceRaw: fundingSourceRaw || null,
        ps, mooe, co, ccAdaptation, ccMitigation,
        ccTypologyCode: ccTypologyCode.trim() || null,
      });
      setOffices((prev) => prev.map((o) =>
        o.id !== selectedOffice.id ? o : {
          ...o,
          programs: o.programs.map((p) =>
            p.id !== selectedProgram.id ? p : {
              ...p,
              projects: p.projects.map((j) =>
                j.id !== selectedProject.id ? j : { ...j, activities: [...j.activities, act] }
              ),
            }
          ),
        }
      ));
      setActivityName("");
      setEsreCode("");
      setImplementingOffice("");
      setStartMonth("");
      setEndMonth("");
      setExpectedOutputs("");
      setFundingSourceRaw("");
      setPs(null); setMooe(null); setCo(null);
      setCcAdaptation(null); setCcMitigation(null); setCcTypologyCode("");
    } catch (err) {
      setSaveError(aipErrorMessage(err, "Could not add activity."));
    } finally {
      setSaving(false);
    }
  }

  // ── Step 1: fiscal year + start ──────────────────────────────────────────
  if (!aip) {
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

  // ── Step 2: build the hierarchy ──────────────────────────────────────────
  return (
    <div className="space-y-4">
      <div className="border border-green-200 bg-green-50 px-4 py-2.5 text-sm text-green-800 flex items-center justify-between">
        <span>
          Building AIP FY {aip.fiscalYear} — {offices.length} office{offices.length !== 1 ? "s" : ""} ·{" "}
          {totalProgramCount} program{totalProgramCount !== 1 ? "s" : ""} ·{" "}
          {totalProjectCount} project{totalProjectCount !== 1 ? "s" : ""} ·{" "}
          {totalActivityCount} activit{totalActivityCount !== 1 ? "ies" : "y"}
        </span>
        <button
          onClick={() => router.push(`/budget-planning/aip/detail?id=${aip.id}`)}
          className="px-3 py-1 text-xs font-medium text-white bg-green-700 hover:bg-green-800 transition-colors whitespace-nowrap"
        >
          Done — View Record
        </button>
      </div>

      {/* Entry Level tabs */}
      <div className="flex border-b border-slate-200">
        {([
          ["office",   "Office"],
          ["program",  "Program"],
          ["project",  "Project"],
          ["activity", "Activity"],
        ] as [EntryLevel, string][]).map(([lv, label]) => (
          <button
            key={lv}
            onClick={() => setLevel(lv)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              level === lv ? "border-green-700 text-green-700" : "border-transparent text-slate-600 hover:text-slate-700"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {saveError && (
        <div className="border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700">
          {saveError}
        </div>
      )}

      {/* Office form */}
      {level === "office" && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Office</label>
              <select
                value={officeConfigId}
                onChange={(e) => {
                  setOfficeConfigId(e.target.value);
                  const picked = officeConfigs.find((o) => String(o.id) === e.target.value);
                  if (picked) setOfficeName(picked.officeName);
                }}
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
              >
                <option value="">Select an office…</option>
                {officeConfigs.map((o) => (
                  <option key={o.id} value={o.id} disabled={!o.officeRefCode}>
                    {o.officeName}{!o.officeRefCode ? " (no AIP ref code configured)" : ""}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Sector</label>
              <select
                value={sector}
                onChange={(e) => setSector(e.target.value)}
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
              >
                {SECTOR_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
          </div>
          {officeConfigId && (
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Office Name <span className="text-slate-600 font-normal normal-case">(editable — e.g. for a sub-office/program cluster)</span>
              </label>
              <input
                value={officeName}
                onChange={(e) => setOfficeName(e.target.value)}
                placeholder="e.g. Office of the Governor - Warden"
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
              />
              <p className="text-xs text-slate-600 mt-1">
                Defaults to the office&apos;s configured name. Override it when this AIP entry represents a
                sub-office or program cluster under the same office — the ref code stays the same either
                way, so you can add the same office more than once with a different name.
              </p>
            </div>
          )}
          {officeConfigId && (
            <p className="text-xs text-slate-600">
              Ref code preview:{" "}
              <span className="font-mono text-slate-700">
                {SECTOR_PREFIX[sector]}-000-1-{officeConfigs.find((o) => String(o.id) === officeConfigId)?.officeRefCode ?? "…"}
              </span>
            </p>
          )}
          <button
            onClick={handleAddOffice}
            disabled={saving || !officeConfigId}
            className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
              saving || !officeConfigId ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
            }`}
          >
            {saving ? "Adding…" : "Add Office"}
          </button>
        </div>
      )}

      {/* Program form */}
      {level === "program" && (
        !selectedOffice ? (
          <p className="text-sm text-slate-600">Add or select an office in the tree below first.</p>
        ) : (
          <div className="space-y-3">
            <p className="text-xs text-slate-600">
              Under office <span className="font-medium text-slate-700">{selectedOffice.name}</span>{" "}
              (<span className="font-mono">{selectedOffice.refCode}</span>)
            </p>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Program Name</label>
                <input
                  value={programName}
                  onChange={(e) => setProgramName(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Function Band</label>
                <select
                  value={functionBand}
                  onChange={(e) => setFunctionBand(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  {FUNCTION_BAND_OPTIONS.map((b) => <option key={b} value={b}>{b}</option>)}
                </select>
              </div>
            </div>
            <p className="text-xs text-slate-600">
              Ref code preview:{" "}
              <span className="font-mono text-slate-700">
                {previewNextRefCode(selectedOffice.refCode, selectedOffice.programs.map((p) => p.refCode))}
              </span>
            </p>
            <button
              onClick={handleAddProgram}
              disabled={saving || !programName.trim()}
              className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
                saving || !programName.trim() ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
              }`}
            >
              {saving ? "Adding…" : "Add Program"}
            </button>
          </div>
        )
      )}

      {/* Project form */}
      {level === "project" && (
        !selectedProgram ? (
          <p className="text-sm text-slate-600">Add or select a program in the tree below first.</p>
        ) : (
          <div className="space-y-3">
            <p className="text-xs text-slate-600">
              Under program <span className="font-medium text-slate-700">{selectedProgram.name}</span>{" "}
              (<span className="font-mono">{selectedProgram.refCode}</span>)
            </p>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Project Name</label>
              <input
                value={projectName}
                onChange={(e) => setProjectName(e.target.value)}
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
              />
            </div>
            <p className="text-xs text-slate-600">
              Ref code preview:{" "}
              <span className="font-mono text-slate-700">
                {previewNextRefCode(selectedProgram.refCode, selectedProgram.projects.map((p) => p.refCode))}
              </span>
            </p>
            <button
              onClick={handleAddProject}
              disabled={saving || !projectName.trim()}
              className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
                saving || !projectName.trim() ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
              }`}
            >
              {saving ? "Adding…" : "Add Project"}
            </button>
          </div>
        )
      )}

      {/* Activity form */}
      {level === "activity" && (
        !selectedProject ? (
          <p className="text-sm text-slate-600">Add or select a project in the tree below first.</p>
        ) : (
          <div className="space-y-3">
            <p className="text-xs text-slate-600">
              Under project <span className="font-medium text-slate-700">{selectedProject.name}</span>{" "}
              (<span className="font-mono">{selectedProject.refCode}</span>)
            </p>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Activity Description</label>
              <input
                value={activityName}
                onChange={(e) => setActivityName(e.target.value)}
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
              />
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">eSRE Code</label>
                <select
                  value={esreCode}
                  onChange={(e) => setEsreCode(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  <option value="">—</option>
                  {ESRE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Funding Source</label>
                <select
                  value={fundingSourceRaw}
                  onChange={(e) => setFundingSourceRaw(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  <option value="">—</option>
                  {fundingSources.map((f) => <option key={f.id} value={f.code}>{f.code} — {f.name}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Implementing Office</label>
                <input
                  value={implementingOffice}
                  onChange={(e) => setImplementingOffice(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Start Month</label>
                <select
                  value={startMonth}
                  onChange={(e) => setStartMonth(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  <option value="">—</option>
                  {MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">End Month</label>
                <select
                  value={endMonth}
                  onChange={(e) => setEndMonth(e.target.value)}
                  className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  <option value="">—</option>
                  {MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Expected Outputs</label>
              <textarea
                value={expectedOutputs}
                onChange={(e) => setExpectedOutputs(e.target.value)}
                rows={2}
                className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600 resize-vertical"
                style={{ minHeight: 44, maxHeight: 88 }}
              />
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">PS (₱000)</label>
                <MoneyInput value={ps} onChange={setPs} className="w-full" />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">MOOE (₱000)</label>
                <MoneyInput value={mooe} onChange={setMooe} className="w-full" />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">CO (₱000)</label>
                <MoneyInput value={co} onChange={setCo} className="w-full" />
              </div>
            </div>
            <details className="text-xs">
              <summary className="cursor-pointer text-slate-600 font-medium uppercase tracking-wide">
                Climate Change Tagging (optional)
              </summary>
              <div className="grid grid-cols-3 gap-3 mt-2">
                <div>
                  <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">CC Adaptation (₱000)</label>
                  <MoneyInput value={ccAdaptation} onChange={setCcAdaptation} className="w-full" />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">CC Mitigation (₱000)</label>
                  <MoneyInput value={ccMitigation} onChange={setCcMitigation} className="w-full" />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">CC Typology Code</label>
                  <input
                    value={ccTypologyCode}
                    onChange={(e) => setCcTypologyCode(e.target.value)}
                    className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
                  />
                </div>
              </div>
            </details>
            <p className="text-xs text-slate-600">
              Ref code preview:{" "}
              <span className="font-mono text-slate-700">
                {previewNextRefCode(selectedProject.refCode, selectedProject.activities.map((a) => a.refCode))}
              </span>
              {" · "}Total: ₱{((ps ?? 0) + (mooe ?? 0) + (co ?? 0)).toLocaleString("en-PH", { minimumFractionDigits: 2 })}
            </p>
            <button
              onClick={handleAddActivity}
              disabled={saving || !activityName.trim()}
              className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
                saving || !activityName.trim() ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
              }`}
            >
              {saving ? "Adding…" : "Add Activity"}
            </button>
          </div>
        )
      )}

      {/* Tree — everything added so far; click a node to select it as the parent */}
      {offices.length > 0 && (
        <div className="border border-slate-200">
          <p className="px-3 py-2 text-xs font-semibold text-slate-600 uppercase tracking-wide bg-slate-50 border-b border-slate-200">
            Added so far
          </p>
          <div className="max-h-80 overflow-y-auto divide-y divide-slate-100">
            {offices.map((o) => (
              <div key={o.id} className="px-3 py-1.5">
                <button
                  onClick={() => selectOffice(o)}
                  className={`text-left text-sm w-full px-1.5 py-1 ${
                    selectedOfficeId === o.id && !selectedProgramId ? "bg-green-50 text-green-800 font-medium" : "text-slate-700 hover:bg-slate-50"
                  }`}
                >
                  🏢 {o.name} <span className="text-xs text-slate-600 font-mono">{o.refCode}</span>{" "}
                  <span className="text-xs text-slate-600">({o.sector})</span>
                </button>
                {o.programs.map((p) => (
                  <div key={p.id} className="ml-5">
                    <button
                      onClick={() => selectProgram(o, p)}
                      className={`text-left text-sm w-full px-1.5 py-1 ${
                        selectedProgramId === p.id && !selectedProjectId ? "bg-green-50 text-green-800 font-medium" : "text-slate-700 hover:bg-slate-50"
                      }`}
                    >
                      📋 {p.name} <span className="text-xs text-slate-600 font-mono">{p.refCode}</span>
                    </button>
                    {p.projects.map((j) => (
                      <div key={j.id} className="ml-5">
                        <button
                          onClick={() => selectProject(o, p, j)}
                          className={`text-left text-sm w-full px-1.5 py-1 ${
                            selectedProjectId === j.id ? "bg-green-50 text-green-800 font-medium" : "text-slate-700 hover:bg-slate-50"
                          }`}
                        >
                          📁 {j.name} <span className="text-xs text-slate-600 font-mono">{j.refCode}</span>
                        </button>
                        {j.activities.length > 0 && (
                          <p className="ml-5 text-xs text-slate-600 py-0.5">
                            {j.activities.length} activit{j.activities.length !== 1 ? "ies" : "y"} added
                          </p>
                        )}
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            ))}
          </div>
        </div>
      )}
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
                  ["Start the record", "Pick a fiscal year — this creates a blank Draft AIP you'll build by hand."],
                  ["Add an office", "Pick a configured office and sector; the AIP ref code is derived automatically."],
                  ["Add programs, projects, activities", "Work down each Entry Level tab — every node is saved as soon as you Add it."],
                  ["Finish anytime", "Click \"Done — View Record\" to open the detail page; Finalize is available there once ready."],
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
