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
 *   POST /api/purchase-requests/import   → upload populated .xlsx (raw binary body, bulk direct-create)
 *   POST /api/purchase-requests/import/gso-preview → upload a GSO-system PR export (raw binary
 *                                            body) → prefill this form, nothing created (RAL-196)
 *   GET  /api/items/lookup?term=         → autocomplete lookup
 *   GET  /api/config/accounts?search=    → Account No./Title bidirectional lookup (RAL-196)
 */

import {
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { fetchMe } from "@/lib/me-cache";
import { useInventoryDivisions } from "@/lib/inventory-divisions";
import { listAccounts } from "@/lib/config";
import { useToast } from "@/components/ui/Toast";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import type {
  AccountResponse,
  CreatePRItemRequest,
  CreatePRRequest,
  GsoPRImportPreviewResponse,
  ItemLookupResponse,
  MeResponse,
  PRResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

// Divisions come from the configurable divisions table (v1.2 — RAL-97), never a
// hard-coded list. See @/lib/inventory-divisions.
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
              <span className="ml-2 text-slate-600">
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
// AccountLookupInput
// Bidirectional Account No. <-> Account Title lookup against the Config Accounts
// table (RAL-196) — same debounced-dropdown pattern as LookupInput above, but the
// account isn't required to exist in the table, so a typed value that matches
// nothing is left as free text rather than blocked.
// ---------------------------------------------------------------------------

interface AccountLookupInputProps {
  value: string;
  placeholder: string;
  onType: (v: string) => void;
  onSelect: (account: AccountResponse) => void;
  displayKey: "accountNumber" | "accountTitle";
}

function AccountLookupInput({
  value, placeholder, onType, onSelect, displayKey,
}: AccountLookupInputProps) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [open, setOpen] = useState(false);
  const [suggestions, setSuggestions] = useState<AccountResponse[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    function handler(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  function handleType(v: string) {
    onType(v);
    if (debounceRef.current) clearTimeout(debounceRef.current);

    if (v.trim().length < 2) {
      setSuggestions([]);
      return;
    }

    setSearching(true);
    debounceRef.current = setTimeout(async () => {
      try {
        const data = await listAccounts({ search: v.trim(), active: "true" });
        setSuggestions(data);
        setOpen(data.length > 0);
      } catch {
        setSuggestions([]);
      } finally {
        setSearching(false);
      }
    }, 250);
  }

  return (
    <div ref={wrapRef} className="relative w-full">
      <input
        type="text"
        value={value}
        placeholder={placeholder}
        onChange={(e) => handleType(e.target.value)}
        onFocus={() => { if (suggestions.length > 0) setOpen(true); }}
        className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 focus:bg-white transition-colors"
      />
      {searching && (
        <span className="absolute right-8 top-1/2 -translate-y-1/2 w-3 h-3 border-2 border-green-400 border-t-transparent rounded-full animate-spin" />
      )}
      {open && suggestions.length > 0 && (
        <ul className="absolute z-50 top-full left-0 right-0 bg-white border border-slate-200 shadow-lg max-h-48 overflow-y-auto text-xs">
          {suggestions.map((a) => (
            <li
              key={a.id}
              onMouseDown={(e) => {
                e.preventDefault(); // prevent blur before click
                onSelect(a);
                setOpen(false);
              }}
              className="px-3 py-2 hover:bg-green-50 cursor-pointer border-b border-slate-100 last:border-0"
            >
              <span className="font-mono font-medium text-slate-800">
                {displayKey === "accountNumber" ? a.accountNumber : a.accountTitle}
              </span>
              <span className="ml-2 text-slate-600">
                {displayKey === "accountNumber" ? a.accountTitle : a.accountNumber}
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
    <label className="block text-xs font-medium text-slate-600 mb-1">
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
      className={`w-full px-3 py-2 text-sm border border-slate-200 bg-cell-auto text-slate-600 cursor-default select-none ${className}`}
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
  division: string;
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
  const [authChecked] = useState(true);

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

  // GSO import (prefill) state — RAL-196
  const gsoFileInputRef = useRef<HTMLInputElement>(null);
  const [gsoImporting, setGsoImporting] = useState(false);

  // Debounce timers per row — keyed by row _id
  const debounceRefs = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canAccessInventory) {
          router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
          return;
        }
        setMe(data);
        // Pre-fill from current user
        setHeader((h) => ({
          ...h,
          requestedBy: data.fullName,
          position:    data.position ?? "",
          division:    (data.division as string) ?? "",
        }));
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  const isStaff = me?.role === "Staff";

  // Staff are clamped to their own division by the backend, so only offer that one.
  const { divisions: divisionOptions } = useInventoryDivisions(me?.division ?? null, isStaff);

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

  // ── Prefill from GSO export ────────────────────────────────────────────────
  //
  // Unlike handleFileUpload above (our own template, bulk, direct-create), this parses a
  // single PR exported from the external GSO system — either its .xlsx or signed .pdf export,
  // auto-detected server-side — and drops the result into the existing form state — nothing is
  // created here. Division, Requested By, Position, Approved By, Approving Position, SAI No.,
  // and ALOBS No. are never in either export format, so they're left untouched (Requested
  // By/Position/Division are already prefilled from the current user by the auth-guard effect
  // above) for the user to fill in before Submit, same as typing a new PR by hand.

  async function handleGsoImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!e.target.files) return;
    e.target.value = "";
    if (!file) return;

    setGsoImporting(true);
    try {
      const { data } = await api.post<GsoPRImportPreviewResponse>(
        "/purchase-requests/import/gso-preview",
        file,
        { headers: { "Content-Type": file.type || "application/octet-stream" } }
      );

      patchHeader({
        prNo:         data.prNo         ?? header.prNo,
        fund:         data.fund         ?? header.fund,
        prDate:       data.prDate       ?? header.prDate,
        aipCode:      data.aipCode      ?? header.aipCode,
        accountNo:    data.accountNo    ?? header.accountNo,
        accountTitle: data.accountTitle ?? header.accountTitle,
        program:      data.program      ?? header.program,
        project:      data.project      ?? header.project,
        activity:     data.activity     ?? header.activity,
      });

      setItems(data.items.map((it): LineItem => ({
        _id:         uid(),
        stockNo:     it.stockNo ?? "",
        description: it.description,
        unit:        it.unit,
        quantity:    String(it.quantity),
        unitCost:    it.unitCost,
        itemType:    null,
        suggestions: [], suggestFor: null, suggesting: false,
        fromLookup:  false,
      })));
      setItemsError(null);

      const unknownCount = data.items.filter((it) => it.isUnknownStock).length;
      toast.success(
        "Prefilled from GSO export",
        `${data.items.length} item${data.items.length !== 1 ? "s" : ""} loaded` +
        (unknownCount > 0 ? ` (${unknownCount} not yet in the catalog)` : "") +
        ". Division, Requested By, and signatories still need to be filled in."
      );
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Could not read this file. Make sure it's a PR export from the GSO system.";
      toast.error("Import failed", String(msg).slice(0, 160));
    } finally {
      setGsoImporting(false);
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
      division:    (me?.division as string) ?? "",
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
              <span className="text-slate-600">PR No.</span>
              <span className="font-mono font-semibold text-slate-800">{submitted.prNo}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-600">Division</span>
              <span className="text-slate-600">{submitted.division}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-600">Items</span>
              <span className="text-slate-600">{submitted.items.length}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-600">Total Amount</span>
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
    <div className="min-h-full bg-slate-100">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-5">

        <ConfigPageHeader
          title="Create PR"
          description="Raise a new purchase request, or import one from an Excel template."
        />

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          {/* Download Template */}
          <button
            onClick={handleDownloadTemplate}
            className="flex items-center gap-2 px-4 py-2.5 text-sm border border-slate-200 bg-white text-slate-800 hover:bg-slate-50 shadow-sm transition-colors"
          >
            <span>⬇</span>
            Download Template
          </button>

          {/* Upload Excel */}
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={uploading}
            className="flex items-center gap-2 px-4 py-2.5 text-sm border border-slate-200 bg-white text-slate-800 hover:bg-slate-50 shadow-sm transition-colors disabled:opacity-60"
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

          {/* Prefill from GSO Export */}
          <button
            onClick={() => gsoFileInputRef.current?.click()}
            disabled={gsoImporting}
            title="Prefill this form from a PR exported by the GSO system (.xlsx or signed .pdf) — Division, Requested By, and signatories still need to be filled in manually"
            className="flex items-center gap-2 px-4 py-2.5 text-sm border border-slate-200 bg-white text-slate-800 hover:bg-slate-50 shadow-sm transition-colors disabled:opacity-60"
          >
            {gsoImporting
              ? <span className="w-4 h-4 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
              : <span>📄</span>}
            {gsoImporting ? "Reading…" : "Prefill from GSO Export"}
          </button>
          <input
            ref={gsoFileInputRef}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,.pdf,application/pdf"
            className="hidden"
            onChange={handleGsoImport}
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
                onChange={(e) => patchHeader({ division: e.target.value })}
                disabled={isStaff}
                className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-cell-auto disabled:text-slate-500 disabled:cursor-not-allowed"
              >
                <option value="">— Select Division —</option>
                {divisionOptions.map((d) => (
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
              <AccountLookupInput
                value={header.accountNo}
                placeholder="Account number"
                displayKey="accountNumber"
                onType={(v) => patchHeader({ accountNo: v })}
                onSelect={(a) => patchHeader({ accountNo: a.accountNumber, accountTitle: a.accountTitle })}
              />
            </div>

            {/* Row 7: AccountTitle (full width) */}
            <div className="md:col-span-2">
              <FieldLabel>Account Title</FieldLabel>
              <AccountLookupInput
                value={header.accountTitle}
                placeholder="Account title"
                displayKey="accountTitle"
                onType={(v) => patchHeader({ accountTitle: v })}
                onSelect={(a) => patchHeader({ accountNo: a.accountNumber, accountTitle: a.accountTitle })}
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
                className="font-semibold text-slate-800"
              />
            </div>

          </div>
        </div>

        {/* ── Section 2 — Line Items ────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
          <SectionHeading number="2" title="Items" />

          <div className="overflow-x-auto overflow-y-hidden">
            <table className="w-full text-xs border-collapse min-w-[980px]">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200 text-slate-600 uppercase tracking-wide">
                  <th className="sticky left-0 z-20 bg-slate-50 px-3 py-2.5 text-center font-medium w-10">#</th>
                  <th className="px-3 py-2.5 text-left font-medium w-36">Stock No.</th>
                  <th className="sticky left-10 z-20 bg-slate-50 px-3 py-2.5 text-left font-medium w-40 border-r border-slate-200">Description</th>
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

                  const rowBg = idx % 2 === 1 ? "bg-slate-50" : "bg-white";
                  return (
                    <tr key={row._id} className={rowBg}>
                      {/* # */}
                      <td className={`sticky left-0 z-10 px-3 py-1.5 text-center text-slate-600 ${rowBg}`}>{idx + 1}</td>

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
                      <td className={`sticky left-10 z-10 px-1.5 py-1.5 border-r border-slate-200 ${rowBg}`}>
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
                              ? "bg-cell-auto text-slate-600"
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
                              ? "bg-cell-auto text-slate-600 cursor-default"
                              : "bg-cell-fill"
                          }`}
                        />
                      </td>

                      {/* Total Cost — gray, computed */}
                      <td className="px-1.5 py-1.5">
                        <div className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-auto text-slate-600 text-right select-none">
                          {total > 0 ? fmt(total) : "—"}
                        </div>
                      </td>

                      {/* Item Type — gray (auto-filled) */}
                      <td className="px-1.5 py-1.5">
                        <div className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-auto text-slate-600 select-none truncate">
                          {row.itemType ?? "—"}
                        </div>
                      </td>

                      {/* Remove */}
                      <td className="px-1.5 py-1.5 text-center">
                        <button
                          onClick={() => removeRow(row._id)}
                          disabled={items.length === 1}
                          title="Remove row"
                          className="text-slate-600 hover:text-red-500 disabled:opacity-20 transition-colors text-base leading-none"
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
              <span className="text-xs text-slate-600">
                {filledRows(items).length} item{filledRows(items).length !== 1 ? "s" : ""}
              </span>
              <div className="text-sm font-semibold text-slate-800 tabular-nums">
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
