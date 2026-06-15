"use client";

/**
 * Create PR page — RAL-53.
 * Matches Penpot frame "04b Create PR".
 *
 * Access guard: canAccessInventory required.
 *
 * Layout:
 *   Toolbar   — Download Template | Upload Excel | Submit PR
 *   Section 1 — 18 header fields (PR Details)
 *   Section 2 — Line items grid with StockNo ↔ Description autocomplete
 *
 * Cell colour convention (from PPDO design tokens):
 *   Yellow (#FFFDE7 → bg-cell-fill)  — user fills in
 *   Gray   (#F1F3F5 → bg-cell-auto)  — auto-filled / read-only
 *
 * Textarea rule (CLAUDE.md):
 *   Program, Project, Activity → <textarea> min-height 44px, max-height 88px, resize vertical
 *
 * Bidirectional autocomplete:
 *   Typing in StockNo or Description calls GET /api/items/lookup?term=
 *   Selecting a result auto-fills: StockNo, Description, Unit, UnitCost, ItemType (gray cells)
 *
 * API endpoints:
 *   POST /api/purchase-requests          → submit PR
 *   GET  /api/purchase-requests/template → download blank .xlsx template
 *   POST /api/purchase-requests/import   → upload populated .xlsx (raw binary body)
 *   GET  /api/items/lookup?term=         → autocomplete lookup
 */

import {
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import type {
  CreatePRItemRequest,
  CreatePRRequest,
  Division,
  ItemLookupResponse,
  MeResponse,
  PRResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIVISIONS: Division[] = ["Admin", "Planning", "RM", "MIS", "SPD"];
const TODAY = new Date().toISOString().slice(0, 10); // YYYY-MM-DD

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n);
}

function uid() {
  return `${Date.now()}-${Math.random()}`;
}

// ---------------------------------------------------------------------------
// Line item — client-side shape (includes display state)
// ---------------------------------------------------------------------------

interface LineItem {
  _id: string;                              // client-only key
  stockNo: string;
  description: string;
  unit: string;
  quantity: string;                         // string so input stays controlled
  unitCost: number;
  itemType: string | null;
  // autocomplete
  suggestions: ItemLookupResponse[];
  suggestFor: "stockNo" | "description" | null;
  suggesting: boolean;
  /** true when this row's fields were filled from a catalog lookup selection.
   *  Used to decide whether retyping in either field should clear the other.
   *  Manually typed new items keep this false so StockNo is never wiped. */
  fromLookup: boolean;
}

function blankLine(): LineItem {
  return {
    _id: uid(),
    stockNo: "", description: "", unit: "",
    quantity: "", unitCost: 0, itemType: null,
    suggestions: [], suggestFor: null, suggesting: false,
    fromLookup: false,
  };
}

const INITIAL_ROW_COUNT = 5;

// ---------------------------------------------------------------------------
// LookupInput
// Uncontrolled-style: uses a ref for the debounce timer.
// Renders a yellow text input with a floating suggestion dropdown.
// ---------------------------------------------------------------------------

interface LookupInputProps {
  value: string;
  placeholder: string;
  disabled?: boolean;
  onType: (v: string) => void;         // updates parent state
  onSelect: (item: ItemLookupResponse) => void;
  suggestions: ItemLookupResponse[];
  suggesting: boolean;
  displayKey: "stockNo" | "description";
}

function LookupInput({
  value, placeholder, disabled,
  onType, onSelect,
  suggestions, suggesting, displayKey,
}: LookupInputProps) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);

  // Close when clicking outside
  useEffect(() => {
    function handler(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  useEffect(() => {
    setOpen(suggestions.length > 0);
  }, [suggestions]);

  return (
    <div ref={wrapRef} className="relative w-full">
      <input
        type="text"
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        onChange={(e) => onType(e.target.value)}
        onFocus={() => { if (suggestions.length > 0) setOpen(true); }}
        className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500 focus:bg-white transition-colors disabled:bg-cell-auto disabled:cursor-not-allowed"
      />
      {suggesting && (
        <span className="absolute right-2 top-1/2 -translate-y-1/2 w-3 h-3 border-2 border-green-400 border-t-transparent rounded-full animate-spin" />
      )}
      {open && suggestions.length > 0 && (
        <ul className="absolute z-50 top-full left-0 right-0 bg-white border border-slate-200 shadow-lg max-h-48 overflow-y-auto text-xs">
          {suggestions.map((item) => (
            <li
              key={item.id}
              onMouseDown={(e) => {
                e.preventDefault(); // prevent blur before click
                onSelect(item);
                setOpen(false);
              }}
              className="px-3 py-2 hover:bg-green-50 cursor-pointer border-b border-slate-100 last:border-0"
            >
              <span className="font-medium text-slate-800">
                {displayKey === "stockNo" ? item.stockNo : item.description}
              </span>
              <span className="ml-2 text-slate-400">
                {displayKey === "stockNo" ? item.description : item.stockNo}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Field wrapper helpers
// ---------------------------------------------------------------------------

function FieldLabel({ children, required }: { children: React.ReactNode; required?: boolean }) {
  return (
    <label className="block text-xs font-medium text-slate-500 mb-1">
      {children}
      {required && <span className="text-red-500 ml-0.5">*</span>}
    </label>
  );
}

function YellowInput({
  value, onChange, placeholder, type = "text", disabled, className = "",
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: string;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <input
      type={type}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      className={`w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 focus:bg-white transition-colors disabled:opacity-60 ${className}`}
    />
  );
}

function GrayInput({ value, className = "" }: { value: string; className?: string }) {
  return (
    <input
      type="text"
      value={value}
      readOnly
      tabIndex={-1}
      className={`w-full px-3 py-2 text-sm border border-slate-200 bg-cell-auto text-slate-500 cursor-default select-none ${className}`}
    />
  );
}

function YellowTextarea({
  value, onChange, placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <textarea
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      rows={2}
      className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 focus:bg-white transition-colors resize-vertical"
      style={{ minHeight: 44, maxHeight: 88 }}
    />
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) return null;
  return <p className="text-xs text-red-500 mt-0.5">{message}</p>;
}

// ---------------------------------------------------------------------------
// Section heading
// ---------------------------------------------------------------------------

function SectionHeading({ number, title }: { number: string; title: string }) {
  return (
    <div className="flex items-center gap-3 px-6 py-3 bg-green-600 text-white">
      <span className="w-6 h-6 bg-white text-green-700 flex items-center justify-center text-xs font-bold shrink-0">
        {number}
      </span>
      <span className="text-sm font-semibold tracking-wide uppercase">{title}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Header form state + validation
// ---------------------------------------------------------------------------

type HeaderForm = {
  prDate: string;
  prNo: string;               // optional — blank means backend auto-generates
  department: string;
  division: Division | "";
  fund: string;
  requestedBy: string;
  position: string;
  approvedBy: string;
  approvingPosition: string;
  aipCode: string;
  accountNo: string;
  accountTitle: string;
  program: string;
  project: string;
  activity: string;
  saiNo: string;
  alobsNo: string;
};

type HeaderErrors = Partial<Record<keyof HeaderForm, string>>;

function blankHeader(): HeaderForm {
  return {
    prDate: TODAY,
    prNo: "",
    department: "PPDO",
    division: "",
    fund: "",
    requestedBy: "",
    position: "",
    approvedBy: "",
    approvingPosition: "",
    aipCode: "",
    accountNo: "",
    accountTitle: "",
    program: "",
    project: "",
    activity: "",
    saiNo: "",
    alobsNo: "",
  };
}

function validateHeader(f: HeaderForm): HeaderErrors {
  const e: HeaderErrors = {};
  if (!f.prDate)       e.prDate       = "PR Date is required.";
  if (!f.division)     e.division     = "Division is required.";
  if (!f.fund.trim())  e.fund         = "Fund is required.";
  if (!f.requestedBy.trim()) e.requestedBy = "Requested By is required.";
  if (!f.position.trim())    e.position    = "Position is required.";
  return e;
}

/** Returns only rows the user has actually started filling in. */
function filledRows(items: LineItem[]): LineItem[] {
  return items.filter(
    (r) => r.description.trim() || r.stockNo.trim() || r.quantity.trim()
  );
}

function validateItems(items: LineItem[]): string | null {
  const filled = filledRows(items);
  if (filled.length === 0) return "At least one line item is required.";
  for (let i = 0; i < filled.length; i++) {
    const it = filled[i];
    if (!it.description.trim()) return `Row ${i + 1}: Description is required.`;
    if (!it.unit.trim())        return `Row ${i + 1}: Unit is required.`;
    const qty = parseFloat(it.quantity);
    if (isNaN(qty) || qty <= 0) return `Row ${i + 1}: Quantity must be greater than 0.`;
  }
  return null;
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function CreatePRPage() {
  const router    = useRouter();
  const { toast } = useToast();

  // Auth guard
  const [me, setMe]               = useState<MeResponse | null>(null);
  const [authChecked, setAuthChecked] = useState(false);

  // Form state
  const [header, setHeader]       = useState<HeaderForm>(blankHeader());
  const [headerErrors, setHeaderErrors] = useState<HeaderErrors>({});
  const [items, setItems]         = useState<LineItem[]>(() =>
    Array.from({ length: INITIAL_ROW_COUNT }, blankLine)
  );
  const [itemsError, setItemsError] = useState<string | null>(null);

  // Submit state
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted]   = useState<PRResponse | null>(null);

  // Upload state
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);

  // Debounce timers per row — keyed by row _id
  const debounceRefs = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) {
          router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
          return;
        }
        setMe(data);
        setAuthChecked(true);
        // Pre-fill from current user
        setHeader((h) => ({
          ...h,
          requestedBy: data.fullName,
          position:    data.position ?? "",
          division:    (data.division as Division) ?? "",
        }));
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  const isStaff = me?.role === "Staff" || me?.role === "Observer";

  // ── Header field patch ─────────────────────────────────────────────────────

  function patchHeader(patch: Partial<HeaderForm>) {
    setHeader((h) => ({ ...h, ...patch }));
    // Clear errors for patched keys
    const cleared: HeaderErrors = {};
    for (const k of Object.keys(patch) as (keyof HeaderForm)[]) {
      cleared[k] = undefined;
    }
    setHeaderErrors((e) => ({ ...e, ...cleared }));
  }

  // ── Items grid helpers ─────────────────────────────────────────────────────

  function addRow() {
    setItems((rows) => [...rows, blankLine()]);
    setItemsError(null);
  }

  function removeRow(id: string) {
    setItems((rows) => rows.filter((r) => r._id !== id));
  }

  function patchRow(id: string, patch: Partial<LineItem>) {
    setItems((rows) =>
      rows.map((r) => (r._id === id ? { ...r, ...patch } : r))
    );
  }

  // ── Autocomplete ───────────────────────────────────────────────────────────

  function handleLookupType(
    rowId: string,
    value: string,
    field: "stockNo" | "description"
  ) {
    // Find whether this row was previously filled from a catalog selection.
    // Only clear the sibling field in that case — if the user manually typed
    // both fields (new item not in catalog) we must NOT wipe their StockNo
    // just because they moved to Description, and vice versa.
    const row = items.find((r) => r._id === rowId);
    const wasFromLookup = row?.fromLookup ?? false;

    patchRow(rowId, {
      [field]: value,
      suggestions: [],
      suggestFor: null,
      fromLookup: false,           // user is now typing manually
      // Only clear auto-filled companion fields when overriding a lookup result
      ...(wasFromLookup
        ? field === "stockNo"
          ? { description: "", unit: "", unitCost: 0, itemType: null }
          : { stockNo: "", unit: "", unitCost: 0, itemType: null }
        : {}),
    });

    // Debounce lookup
    if (debounceRefs.current[rowId]) clearTimeout(debounceRefs.current[rowId]);
    if (value.trim().length < 2) return;

    patchRow(rowId, { suggesting: true });
    debounceRefs.current[rowId] = setTimeout(async () => {
      try {
        const { data } = await api.get<ItemLookupResponse[]>(
          `/items/lookup?term=${encodeURIComponent(value.trim())}`
        );
        patchRow(rowId, { suggestions: data, suggestFor: field, suggesting: false });
      } catch {
        patchRow(rowId, { suggestions: [], suggestFor: null, suggesting: false });
      }
    }, 250);
  }

  function handleLookupSelect(rowId: string, item: ItemLookupResponse) {
    // Clear debounce
    if (debounceRefs.current[rowId]) clearTimeout(debounceRefs.current[rowId]);
    patchRow(rowId, {
      stockNo:     item.stockNo,
      description: item.description,
      unit:        item.unit,
      unitCost:    item.unitCost,
      itemType:    null,               // not in lookup response
      suggestions: [],
      suggestFor:  null,
      suggesting:  false,
      fromLookup:  true,               // mark so retyping later can clear companion fields
    });
    setItemsError(null);
  }

  // ── Computed total ─────────────────────────────────────────────────────────

  const totalAmount = useMemo(() => {
    return items.reduce((sum, r) => {
      const qty = parseFloat(r.quantity) || 0;
      return sum + qty * r.unitCost;
    }, 0);
  }, [items]);

  // ── Download Template ──────────────────────────────────────────────────────

  async function handleDownloadTemplate() {
    try {
      const response = await api.get("/purchase-requests/template", {
        responseType: "blob",
      });
      const url  = URL.createObjectURL(response.data as Blob);
      const link = document.createElement("a");
      link.href  = url;
      link.download = "PR_Import_Template.xlsx";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Download failed", "Could not download the template. Please try again.");
    }
  }

  // ── Upload Excel ───────────────────────────────────────────────────────────

  async function handleFileUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!e.target.files) return;
    // Reset input so the same file can be re-uploaded
    e.target.value = "";
    if (!file) return;

    setUploading(true);
    try {
      const { data } = await api.post<PRResponse[]>(
        "/purchase-requests/import",
        file,
        { headers: { "Content-Type": file.type || "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }
      );
      const count = data.length;
      toast.success(
        "Import successful",
        `${count} PR${count !== 1 ? "s" : ""} imported from Excel.`
      );
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Import failed. Ensure the file follows the template format.";
      toast.error("Import failed", String(msg).slice(0, 120));
    } finally {
      setUploading(false);
    }
  }

  // ── Submit PR ──────────────────────────────────────────────────────────────

  async function handleSubmit() {
    // Validate header
    const hErrors = validateHeader(header);
    if (Object.keys(hErrors).length > 0) {
      setHeaderErrors(hErrors);
      toast.warn("Validation error", "Please fill in all required fields.");
      return;
    }

    // Validate items
    const iError = validateItems(items);
    if (iError) {
      setItemsError(iError);
      toast.warn("Validation error", iError);
      return;
    }

    setSubmitting(true);
    setHeaderErrors({});
    setItemsError(null);

    const body: CreatePRRequest = {
      prDate:            header.prDate,
      prNo:              header.prNo.trim() || null,
      department:        header.department,
      division:          header.division as string,
      fund:              header.fund.trim(),
      requestedBy:       header.requestedBy.trim(),
      position:          header.position.trim(),
      approvedBy:        header.approvedBy.trim()        || null,
      approvingPosition: header.approvingPosition.trim() || null,
      aipCode:           header.aipCode.trim()           || null,
      accountNo:         header.accountNo.trim()         || null,
      accountTitle:      header.accountTitle.trim()      || null,
      program:           header.program.trim()           || null,
      project:           header.project.trim()           || null,
      activity:          header.activity.trim()          || null,
      saiNo:             header.saiNo.trim()             || null,
      alobsNo:           header.alobsNo.trim()           || null,
      items: filledRows(items).map((r): CreatePRItemRequest => ({
        stockNo:     r.stockNo.trim()     || null,
        description: r.description.trim(),
        unit:        r.unit.trim(),
        quantity:    parseFloat(r.quantity),
        unitCost:    r.unitCost,
        itemType:    r.itemType,
      })),
    };

    try {
      const { data } = await api.post<PRResponse>("/purchase-requests", body);
      setSubmitted(data);
      toast.success("PR submitted", `PR No. ${data.prNo} has been created.`);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Failed to submit PR. Please try again.";
      toast.error("Submission failed", String(msg).slice(0, 120));
    } finally {
      setSubmitting(false);
    }
  }

  // ── Reset form ─────────────────────────────────────────────────────────────

  function handleReset() {
    setHeader({
      ...blankHeader(),
      requestedBy: me?.fullName ?? "",
      position:    me?.position ?? "",
      division:    (me?.division as Division) ?? "",
    });
    setItems(Array.from({ length: INITIAL_ROW_COUNT }, blankLine));
    setHeaderErrors({});
    setItemsError(null);
    setSubmitted(null);
  }

  // ── Guards ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Success state ──────────────────────────────────────────────────────────

  if (submitted) {
    return (
      <div className="min-h-screen bg-slate-100 flex items-center justify-center p-6">
        <div className="bg-white border border-slate-200 shadow-sm p-10 max-w-md w-full text-center space-y-4">
          <div className="w-14 h-14 bg-green-100 flex items-center justify-center mx-auto text-2xl">
            ✅
          </div>
          <h2 className="text-lg font-bold text-slate-800">PR Submitted</h2>
          <p className="text-sm text-slate-600">
            Your Purchase Request has been successfully submitted.
          </p>
          <div className="bg-slate-50 border border-slate-200 px-4 py-3 text-left space-y-1 text-sm">
            <div className="flex justify-between">
              <span className="text-slate-500">PR No.</span>
              <span className="font-mono font-semibold text-slate-800">{submitted.prNo}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Division</span>
              <span className="text-slate-700">{submitted.division}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Items</span>
              <span className="text-slate-700">{submitted.items.length}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Total Amount</span>
              <span className="font-semibold text-slate-800">₱{fmt(submitted.totalAmount)}</span>
            </div>
          </div>
          <div className="flex gap-3 justify-center pt-2">
            <button
              onClick={handleReset}
              className="px-5 py-2 text-sm bg-green-600 text-white font-medium hover:bg-green-500 transition-colors"
            >
              Create Another PR
            </button>
            <button
              onClick={() => router.push("/inventory")}
              className="px-5 py-2 text-sm border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
            >
              Back to Dashboard
            </button>
          </div>
        </div>
      </div>
    );
  }

  // ── Main render ────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-5">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          {/* Download Template */}
          <button
            onClick={handleDownloadTemplate}
            className="flex items-center gap-2 px-4 py-2.5 text-sm border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 shadow-sm transition-colors"
          >
            <span>⬇</span>
            Download Template
          </button>

          {/* Upload Excel */}
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={uploading}
            className="flex items-center gap-2 px-4 py-2.5 text-sm border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 shadow-sm transition-colors disabled:opacity-60"
          >
            {uploading
              ? <span className="w-4 h-4 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
              : <span>⬆</span>}
            {uploading ? "Importing…" : "Upload PR Excel"}
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="hidden"
            onChange={handleFileUpload}
          />

          <div className="flex-1" />

          {/* Submit */}
          <button
            onClick={handleSubmit}
            disabled={submitting}
            className="flex items-center gap-2 px-6 py-2.5 text-sm bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : <span>✓</span>}
            {submitting ? "Submitting…" : "Submit PR"}
          </button>
        </div>

        {/* ── Section 1 — PR Details ────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
          <SectionHeading number="1" title="PR Details" />

          <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4">

            {/* Row 1: PRDate | PRNo */}
            <div>
              <FieldLabel required>PR Date</FieldLabel>
              <YellowInput
                type="date"
                value={header.prDate}
                onChange={(v) => patchHeader({ prDate: v })}
              />
              <FieldError message={headerErrors.prDate} />
            </div>
            <div>
              <FieldLabel>PR No.</FieldLabel>
              <YellowInput
                value={header.prNo}
                onChange={(v) => patchHeader({ prNo: v })}
                placeholder="Leave blank to auto-generate"
              />
            </div>

            {/* Row 2: Department | Division */}
            <div>
              <FieldLabel>Department</FieldLabel>
              <GrayInput value={header.department} />
            </div>
            <div>
              <FieldLabel required>Division</FieldLabel>
              <select
                value={header.division}
                onChange={(e) => patchHeader({ division: e.target.value as Division })}
                disabled={isStaff}
                className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-cell-auto disabled:text-slate-500 disabled:cursor-not-allowed"
              >
                <option value="">— Select Division —</option>
                {DIVISIONS.map((d) => (
                  <option key={d} value={d}>{d}</option>
                ))}
              </select>
              <FieldError message={headerErrors.division} />
            </div>

            {/* Row 3: Fund (full width) */}
            <div className="md:col-span-2">
              <FieldLabel required>Fund</FieldLabel>
              <YellowInput
                value={header.fund}
                onChange={(v) => patchHeader({ fund: v })}
                placeholder="e.g. General Fund"
              />
              <FieldError message={headerErrors.fund} />
            </div>

            {/* Row 4: RequestedBy | Position */}
            <div>
              <FieldLabel required>Requested By</FieldLabel>
              <YellowInput
                value={header.requestedBy}
                onChange={(v) => patchHeader({ requestedBy: v })}
                placeholder="Full name"
              />
              <FieldError message={headerErrors.requestedBy} />
            </div>
            <div>
              <FieldLabel required>Position</FieldLabel>
              <YellowInput
                value={header.position}
                onChange={(v) => patchHeader({ position: v })}
                placeholder="e.g. Planning Officer II"
              />
              <FieldError message={headerErrors.position} />
            </div>

            {/* Row 5: ApprovedBy | ApprovingPosition */}
            <div>
              <FieldLabel>Approved By</FieldLabel>
              <YellowInput
                value={header.approvedBy}
                onChange={(v) => patchHeader({ approvedBy: v })}
                placeholder="Approving officer name"
              />
            </div>
            <div>
              <FieldLabel>Approving Position</FieldLabel>
              <YellowInput
                value={header.approvingPosition}
                onChange={(v) => patchHeader({ approvingPosition: v })}
                placeholder="e.g. Provincial Planning Officer"
              />
            </div>

            {/* Row 6: AIPCode | AccountNo */}
            <div>
              <FieldLabel>AIP Code</FieldLabel>
              <YellowInput
                value={header.aipCode}
                onChange={(v) => patchHeader({ aipCode: v })}
                placeholder="AIP code"
              />
            </div>
            <div>
              <FieldLabel>Account No.</FieldLabel>
              <YellowInput
                value={header.accountNo}
                onChange={(v) => patchHeader({ accountNo: v })}
                placeholder="Account number"
              />
            </div>

            {/* Row 7: AccountTitle (full width) */}
            <div className="md:col-span-2">
              <FieldLabel>Account Title</FieldLabel>
              <YellowInput
                value={header.accountTitle}
                onChange={(v) => patchHeader({ accountTitle: v })}
                placeholder="Account title"
              />
            </div>

            {/* Row 8–10: Program / Project / Activity — textareas */}
            <div className="md:col-span-2">
              <FieldLabel>Program</FieldLabel>
              <YellowTextarea
                value={header.program}
                onChange={(v) => patchHeader({ program: v })}
                placeholder="Program name (long text supported)"
              />
            </div>
            <div className="md:col-span-2">
              <FieldLabel>Project</FieldLabel>
              <YellowTextarea
                value={header.project}
                onChange={(v) => patchHeader({ project: v })}
                placeholder="Project name (long text supported)"
              />
            </div>
            <div className="md:col-span-2">
              <FieldLabel>Activity</FieldLabel>
              <YellowTextarea
                value={header.activity}
                onChange={(v) => patchHeader({ activity: v })}
                placeholder="Activity description (long text supported)"
              />
            </div>

            {/* Row 11: SAINo | ALOBSNo */}
            <div>
              <FieldLabel>SAI No.</FieldLabel>
              <YellowInput
                value={header.saiNo}
                onChange={(v) => patchHeader({ saiNo: v })}
                placeholder="SAI number"
              />
            </div>
            <div>
              <FieldLabel>ALOBS No.</FieldLabel>
              <YellowInput
                value={header.alobsNo}
                onChange={(v) => patchHeader({ alobsNo: v })}
                placeholder="ALOBS number"
              />
            </div>

            {/* Row 12: Total Amount (gray, computed) */}
            <div>
              <FieldLabel>Total Amount</FieldLabel>
              <GrayInput
                value={`₱ ${fmt(totalAmount)}`}
                className="font-semibold text-slate-700"
              />
            </div>

          </div>
        </div>

        {/* ── Section 2 — Line Items ────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
          <SectionHeading number="2" title="Items" />

          <div className="overflow-x-auto">
            <table className="w-full text-xs border-collapse">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200 text-slate-500 uppercase tracking-wide">
                  <th className="px-3 py-2.5 text-center font-medium w-10">#</th>
                  <th className="px-3 py-2.5 text-left font-medium w-36">Stock No.</th>
                  <th className="px-3 py-2.5 text-left font-medium min-w-56">Description</th>
                  <th className="px-3 py-2.5 text-left font-medium w-24">Unit</th>
                  <th className="px-3 py-2.5 text-right font-medium w-24">Qty</th>
                  <th className="px-3 py-2.5 text-right font-medium w-28">Unit Cost</th>
                  <th className="px-3 py-2.5 text-right font-medium w-28">Total Cost</th>
                  <th className="px-3 py-2.5 text-left font-medium w-28">Item Type</th>
                  <th className="px-3 py-2.5 w-10" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {items.map((row, idx) => {
                  const qty   = parseFloat(row.quantity) || 0;
                  const total = qty * row.unitCost;

                  return (
                    <tr key={row._id} className={idx % 2 === 1 ? "bg-slate-50" : "bg-white"}>
                      {/* # */}
                      <td className="px-3 py-1.5 text-center text-slate-400">{idx + 1}</td>

                      {/* Stock No — yellow, autocomplete */}
                      <td className="px-1.5 py-1.5">
                        <LookupInput
                          value={row.stockNo}
                          placeholder="Stock No."
                          onType={(v) => handleLookupType(row._id, v, "stockNo")}
                          onSelect={(item) => handleLookupSelect(row._id, item)}
                          suggestions={row.suggestFor === "stockNo" ? row.suggestions : []}
                          suggesting={row.suggesting && row.suggestFor === "stockNo"}
                          displayKey="stockNo"
                        />
                      </td>

                      {/* Description — yellow, autocomplete */}
                      <td className="px-1.5 py-1.5">
                        <LookupInput
                          value={row.description}
                          placeholder="Item description *"
                          onType={(v) => handleLookupType(row._id, v, "description")}
                          onSelect={(item) => handleLookupSelect(row._id, item)}
                          suggestions={row.suggestFor === "description" ? row.suggestions : []}
                          suggesting={row.suggesting && row.suggestFor === "description"}
                          displayKey="description"
                        />
                      </td>

                      {/* Unit — gray when auto-filled, yellow when empty */}
                      <td className="px-1.5 py-1.5">
                        <input
                          type="text"
                          value={row.unit}
                          onChange={(e) => patchRow(row._id, { unit: e.target.value })}
                          placeholder="unit"
                          className={`w-full px-2 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500 transition-colors ${
                            row.unit && row.stockNo
                              ? "bg-cell-auto text-slate-500"
                              : "bg-cell-fill"
                          }`}
                        />
                      </td>

                      {/* Qty — yellow */}
                      <td className="px-1.5 py-1.5">
                        <input
                          type="number"
                          min={0}
                          step="any"
                          value={row.quantity}
                          onChange={(e) => patchRow(row._id, { quantity: e.target.value })}
                          placeholder="0"
                          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill text-right focus:outline-none focus:ring-1 focus:ring-green-500 transition-colors"
                        />
                      </td>

                      {/* Unit Cost — gray + locked only when filled from catalog lookup.
                          Manually typed new items (fromLookup=false) keep it yellow & editable. */}
                      <td className="px-1.5 py-1.5">
                        <input
                          type="number"
                          min={0}
                          step="any"
                          value={row.unitCost || ""}
                          readOnly={row.fromLookup}
                          onChange={(e) => {
                            if (!row.fromLookup) {
                              patchRow(row._id, { unitCost: parseFloat(e.target.value) || 0 });
                            }
                          }}
                          placeholder="0.00"
                          tabIndex={row.fromLookup ? -1 : 0}
                          className={`w-full px-2 py-1.5 text-xs border border-slate-200 text-right focus:outline-none focus:ring-1 focus:ring-green-500 transition-colors ${
                            row.fromLookup
                              ? "bg-cell-auto text-slate-500 cursor-default"
                              : "bg-cell-fill"
                          }`}
                        />
                      </td>

                      {/* Total Cost — gray, computed */}
                      <td className="px-1.5 py-1.5">
                        <div className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-auto text-slate-500 text-right select-none">
                          {total > 0 ? fmt(total) : "—"}
                        </div>
                      </td>

                      {/* Item Type — gray (auto-filled) */}
                      <td className="px-1.5 py-1.5">
                        <div className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-auto text-slate-500 select-none truncate">
                          {row.itemType ?? "—"}
                        </div>
                      </td>

                      {/* Remove */}
                      <td className="px-1.5 py-1.5 text-center">
                        <button
                          onClick={() => removeRow(row._id)}
                          disabled={items.length === 1}
                          title="Remove row"
                          className="text-slate-300 hover:text-red-500 disabled:opacity-20 transition-colors text-base leading-none"
                        >
                          ✕
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Items footer: error + add row + grand total */}
          <div className="px-4 py-3 border-t border-slate-100 flex items-center gap-4 flex-wrap">
            <button
              onClick={addRow}
              className="flex items-center gap-1.5 text-sm text-green-600 hover:text-green-500 font-medium transition-colors"
            >
              <span className="text-base leading-none">+</span>
              Add Row
            </button>

            {itemsError && (
              <p className="text-xs text-red-500 flex-1">{itemsError}</p>
            )}

            <div className="ml-auto flex items-center gap-3">
              <span className="text-xs text-slate-500">
                {filledRows(items).length} item{filledRows(items).length !== 1 ? "s" : ""}
              </span>
              <div className="text-sm font-semibold text-slate-700 tabular-nums">
                Total: ₱ {fmt(totalAmount)}
              </div>
            </div>
          </div>
        </div>

        {/* ── Bottom submit ─────────────────────────────────────────────────── */}
        <div className="flex justify-end pb-4">
          <button
            onClick={handleSubmit}
            disabled={submitting}
            className="flex items-center gap-2 px-8 py-3 text-sm bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : <span>✓</span>}
            {submitting ? "Submitting…" : "Submit Purchase Request"}
          </button>
        </div>

      </div>
    </div>
  );
}
