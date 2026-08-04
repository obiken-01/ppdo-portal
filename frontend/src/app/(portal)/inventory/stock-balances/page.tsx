"use client";

/**
 * Warehouse Stock Input page — RAL-193.
 *
 * A recurring physical-count ledger. Stock that exists in the warehouse but never passed
 * through a PR + Delivery in this system was previously invisible on the Inventory
 * Dashboard / Stock Overview — this page lets CanAccessInventory holders record a physical
 * count per item (one at a time, or in bulk via Excel), and see that item's count history
 * with the system-vs-counted variance (the reconciliation view).
 *
 * Note: the resulting variance only affects the Admin/SuperAdmin unscoped Stock Overview
 * view — Staff/Observer's division-scoped on-hand is unaffected (see InventoryService).
 *
 * Unknown StockNo handling mirrors Create PR: typing a StockNo that matches Items Master
 * auto-fills Description/Unit/Unit Cost (read-only) from the catalog. Typing one that
 * doesn't match shows Description/Unit input fields — submitting then auto-creates the
 * Items Master entry (flagged for admin review), exactly like an unrecognized Create PR
 * line item. Same handling applies to the bulk Excel upload.
 *
 * Access guard: canAccessInventory permission required.
 *
 * API endpoints (StockBalanceFunctions.cs):
 *   GET    /api/inventory/stock-balances?stockNo=              → history for one item
 *   GET    /api/inventory/stock-balances/system-on-hand?stockNo= → current computed on-hand
 *                                                                  (reference shown before submit)
 *   POST   /api/inventory/stock-balances                       → record a new count
 *   PUT    /api/inventory/stock-balances/{id}                   → edit a count
 *   DELETE /api/inventory/stock-balances/{id}                   → remove a count
 *   POST   /api/inventory/stock-balances/import/preview         → parse an uploaded workbook
 *   POST   /api/inventory/stock-balances/import/commit          → persist the reviewed rows
 *   GET    /api/inventory/stock-balances/import/template        → download the blank .xlsx template
 */

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { fetchMe } from "@/lib/me-cache";
import { useToast } from "@/components/ui/Toast";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import CsvUploadButton from "@/components/ui/CsvUploadButton";
import type {
  CreateStockBalanceRequest,
  ItemLookupResponse,
  StockBalanceImportPreviewResponse,
  StockBalanceImportResultResponse,
  StockBalanceImportRowResponse,
  StockBalanceResponse,
  SystemOnHandResponse,
  UpdateStockBalanceRequest,
} from "@/types";

const todayIso = () => new Date().toISOString().slice(0, 10);

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export default function StockBalancesPage() {
  const router = useRouter();
  const { toast } = useToast();

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canAccessInventory) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── New-entry form ─────────────────────────────────────────────────────────

  const [stockNo, setStockNo] = useState("");
  // Set once the StockNo is matched to an Items Master row via the lookup — its own
  // Description/Unit/Unit Cost win over anything typed below (mirrors Create PR).
  const [matchedItem, setMatchedItem] = useState<ItemLookupResponse | null>(null);
  const [countedQty, setCountedQty] = useState("");
  const [effectiveDate, setEffectiveDate] = useState(todayIso());
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);

  // Only used when stockNo doesn't match an existing catalog item — required to auto-create
  // it (IsNewItem = true, pending admin review), same fields Create PR asks for.
  const [newItemDescription, setNewItemDescription] = useState("");
  const [newItemUnit, setNewItemUnit] = useState("");
  const [newItemUnitCost, setNewItemUnitCost] = useState("");
  const [newItemType, setNewItemType] = useState("");

  const [suggestions, setSuggestions] = useState<ItemLookupResponse[]>([]);
  const [suggesting, setSuggesting] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function handleStockNoInput(value: string) {
    setStockNo(value);
    setMatchedItem(null);
    setCurrentOnHand(null);

    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (value.trim().length < 2) { setSuggestions([]); return; }

    setSuggesting(true);
    debounceRef.current = setTimeout(async () => {
      try {
        const { data } = await api.get<ItemLookupResponse[]>(
          `/items/lookup?term=${encodeURIComponent(value.trim())}`
        );
        setSuggestions(data);
      } catch {
        setSuggestions([]);
      } finally {
        setSuggesting(false);
      }
    }, 250);
  }

  function handleSelectSuggestion(item: ItemLookupResponse) {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setStockNo(item.stockNo);
    setMatchedItem(item);
    setSuggestions([]);
    loadHistory(item.stockNo);
    loadSystemOnHand(item.stockNo);
  }

  // ── Current system on-hand (reference, shown before the user submits a count) ────

  const [currentOnHand, setCurrentOnHand] = useState<number | null>(null);
  const [currentOnHandLoading, setCurrentOnHandLoading] = useState(false);

  async function loadSystemOnHand(forStockNo: string) {
    setCurrentOnHandLoading(true);
    try {
      const { data } = await api.get<SystemOnHandResponse>(
        `/inventory/stock-balances/system-on-hand?stockNo=${encodeURIComponent(forStockNo)}`
      );
      setCurrentOnHand(data.onHand);
    } catch {
      setCurrentOnHand(null);
    } finally {
      setCurrentOnHandLoading(false);
    }
  }

  // ── History (reconciliation view) ─────────────────────────────────────────

  const [history, setHistory] = useState<StockBalanceResponse[] | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyStockNo, setHistoryStockNo] = useState<string | null>(null);

  async function loadHistory(forStockNo: string) {
    setHistoryLoading(true);
    setHistoryStockNo(forStockNo);
    try {
      const { data } = await api.get<StockBalanceResponse[]>(
        `/inventory/stock-balances?stockNo=${encodeURIComponent(forStockNo)}`
      );
      setHistory(data);
    } catch {
      setHistory([]);
      toast.error("Failed to load history", "Please try again.");
    } finally {
      setHistoryLoading(false);
    }
  }

  // ── Submit new entry ───────────────────────────────────────────────────────

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();

    const trimmedStockNo = stockNo.trim();
    const qty = Number(countedQty);
    if (!trimmedStockNo) { toast.error("StockNo is required."); return; }
    if (!countedQty || Number.isNaN(qty) || qty < 0) { toast.error("Counted quantity must be a non-negative number."); return; }
    if (!effectiveDate) { toast.error("Effective date is required."); return; }

    const trimmedNewDescription = newItemDescription.trim();
    const trimmedNewUnit = newItemUnit.trim();
    if (!matchedItem) {
      if (!trimmedNewDescription) { toast.error("Description is required for a new item."); return; }
      if (!trimmedNewUnit) { toast.error("Unit is required for a new item."); return; }
    }

    setSubmitting(true);
    try {
      const body: CreateStockBalanceRequest = {
        stockNo: trimmedStockNo,
        countedQty: qty,
        effectiveDate,
        reason: reason.trim() || null,
        description: matchedItem ? null : trimmedNewDescription,
        unit: matchedItem ? null : trimmedNewUnit,
        unitCost: matchedItem ? null : (newItemUnitCost ? Number(newItemUnitCost) : null),
        itemType: matchedItem ? null : (newItemType.trim() || null),
      };
      const { data } = await api.post<StockBalanceResponse>("/inventory/stock-balances", body);
      const varianceMsg = `Variance: ${data.varianceQty > 0 ? "+" : ""}${data.varianceQty} (system expected ${data.systemOnHandAtEntry}).`;
      toast.success(
        "Count recorded",
        data.itemWasAutoCreated
          ? `New item added to Items Master (pending review). ${varianceMsg}`
          : varianceMsg
      );
      // The StockNo is now cataloged — treat it as matched so a follow-up entry for the
      // same item doesn't ask for Description/Unit again.
      if (data.itemWasAutoCreated) {
        setMatchedItem({
          id: "", stockNo: trimmedStockNo, description: trimmedNewDescription,
          unit: trimmedNewUnit, unitCost: Number(newItemUnitCost) || 0,
        });
      }
      setCountedQty("");
      setReason("");
      // The just-saved entry closes the gap — system on-hand for this StockNo is now
      // exactly what was counted, without a round-trip to re-fetch it.
      setCurrentOnHand(data.countedQty);
      loadHistory(trimmedStockNo);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: string } })?.response?.data ?? "Failed to record count.";
      toast.error("Save failed", String(msg).slice(0, 160));
    } finally {
      setSubmitting(false);
    }
  }

  // ── Edit / delete existing entries ────────────────────────────────────────

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValues, setEditValues] = useState({ countedQty: "", effectiveDate: "", reason: "" });
  const [confirmDialog, setConfirmDialog] = useState<ConfirmDialogProps | null>(null);

  function startEdit(entry: StockBalanceResponse) {
    setEditingId(entry.id);
    setEditValues({
      countedQty: String(entry.countedQty),
      effectiveDate: entry.effectiveDate,
      reason: entry.reason ?? "",
    });
  }

  async function saveEdit(id: string) {
    const qty = Number(editValues.countedQty);
    if (!editValues.countedQty || Number.isNaN(qty) || qty < 0) { toast.error("Counted quantity must be a non-negative number."); return; }

    try {
      const body: UpdateStockBalanceRequest = {
        countedQty: qty,
        effectiveDate: editValues.effectiveDate || null,
        reason: editValues.reason.trim() || null,
      };
      await api.put(`/inventory/stock-balances/${id}`, body);
      toast.success("Entry updated");
      setEditingId(null);
      if (historyStockNo) loadHistory(historyStockNo);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: string } })?.response?.data ?? "Failed to update entry.";
      toast.error("Update failed", String(msg).slice(0, 160));
    }
  }

  function confirmDelete(entry: StockBalanceResponse) {
    setConfirmDialog({
      title: "Delete this entry?",
      message: `Removing the ${entry.effectiveDate} count for ${entry.stockNo} will change its computed on-hand.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await api.delete(`/inventory/stock-balances/${entry.id}`);
          toast.success("Entry deleted");
          if (historyStockNo) loadHistory(historyStockNo);
        } catch {
          toast.error("Delete failed", "Please try again.");
        }
      },
      onClose: () => setConfirmDialog(null),
    });
  }

  // ── Bulk Excel upload ──────────────────────────────────────────────────────

  const [previewRows, setPreviewRows] = useState<StockBalanceImportRowResponse[] | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [committing, setCommitting] = useState(false);
  const [downloadingTemplate, setDownloadingTemplate] = useState(false);

  async function handleDownloadTemplate() {
    setDownloadingTemplate(true);
    try {
      const response = await api.get("/inventory/stock-balances/import/template", {
        responseType: "blob",
      });
      const url  = URL.createObjectURL(response.data as Blob);
      const link = document.createElement("a");
      link.href  = url;
      link.download = "Stock_Balance_Import_Template.xlsx";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Download failed", "Could not download the template. Please try again.");
    } finally {
      setDownloadingTemplate(false);
    }
  }

  async function handleFileSelect(file: File) {
    setPreviewing(true);
    setPreviewRows(null);
    try {
      const { data } = await api.post<StockBalanceImportPreviewResponse>(
        "/inventory/stock-balances/import/preview",
        file,
        { headers: { "Content-Type": file.type || "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }
      );
      setPreviewRows(data.rows);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: string } })?.response?.data ?? "Failed to read the uploaded file.";
      toast.error("Import failed", String(msg).slice(0, 160));
    } finally {
      setPreviewing(false);
    }
  }

  async function handleCommitImport() {
    if (!previewRows) return;
    const validRows = previewRows.filter((r) => !r.error);
    if (validRows.length === 0) { toast.error("No valid rows to import."); return; }

    setCommitting(true);
    try {
      const body = {
        rows: validRows.map((r): CreateStockBalanceRequest => ({
          stockNo: r.stockNo!,
          countedQty: r.countedQty!,
          effectiveDate: r.effectiveDate!,
          reason: r.reason,
          description: r.description,
          unit: r.unit,
          unitCost: r.unitCost,
          itemType: r.itemType,
        })),
      };
      const { data } = await api.post<StockBalanceImportResultResponse>(
        "/inventory/stock-balances/import/commit", body
      );
      const newItemCount = data.entries.filter((e) => e.itemWasAutoCreated).length;
      toast.success(
        "Import committed",
        `${data.inserted} inserted, ${data.updated} updated.`
        + (newItemCount > 0 ? ` ${newItemCount} new item(s) added to Items Master (pending review).` : "")
      );
      setPreviewRows(null);
      if (historyStockNo && data.entries.some((e) => e.stockNo === historyStockNo)) {
        loadHistory(historyStockNo);
      }
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: string } })?.response?.data ?? "Import commit failed.";
      toast.error("Import failed", String(msg).slice(0, 160));
    } finally {
      setCommitting(false);
    }
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-lg mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">

        {/* ── Record a count ─────────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm p-5">
          <h2 className="text-base font-semibold text-slate-800 mb-1">Record a physical count</h2>
          <p className="text-sm text-slate-500 mb-4">
            Enter what you physically counted in the warehouse for one item. The system
            compares it against computed on-hand and records the difference as a variance.
          </p>

          <form onSubmit={handleSubmit} className="grid grid-cols-1 sm:grid-cols-4 gap-3">
            <div className="relative sm:col-span-2">
              <label className="block text-xs font-medium text-slate-600 mb-1">StockNo / Description</label>
              <input
                value={matchedItem ? `${stockNo} — ${matchedItem.description}` : stockNo}
                onChange={(e) => handleStockNoInput(e.target.value)}
                placeholder="Type to search…"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              {suggesting && (
                <p className="absolute text-xs text-slate-400 mt-1">Searching…</p>
              )}
              {suggestions.length > 0 && (
                <div className="absolute z-20 top-full left-0 w-full bg-white border border-slate-200 shadow-lg max-h-56 overflow-y-auto">
                  {suggestions.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => handleSelectSuggestion(item)}
                      className="w-full text-left px-3 py-1.5 text-sm hover:bg-green-50 hover:text-green-800 border-b border-slate-50 last:border-0"
                    >
                      <span className="font-medium">{item.stockNo}</span> — {item.description}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* Matched item — read-only, catalog's own values win (mirrors Create PR) */}
            {matchedItem && (
              <div className="sm:col-span-4 -mt-1 text-xs text-slate-500">
                Unit: <span className="font-medium text-slate-700">{matchedItem.unit}</span>
                {" · "}Unit Cost: <span className="font-medium text-slate-700">₱{matchedItem.unitCost.toFixed(2)}</span>
                {" · "}Current system on-hand:{" "}
                {currentOnHandLoading ? (
                  <span className="italic">loading…</span>
                ) : (
                  <span className="font-medium text-slate-700">{currentOnHand ?? "—"}</span>
                )}
              </div>
            )}

            {/* Unmatched StockNo — ask for the fields needed to auto-create the catalog
                entry, same as an unrecognized Create PR line item. */}
            {!matchedItem && stockNo.trim().length > 0 && (
              <div className="sm:col-span-4 bg-amber-50 border border-amber-200 px-4 py-3 space-y-2">
                <p className="text-xs text-amber-800">
                  <span className="font-semibold">{stockNo.trim()}</span> isn&apos;t in Items Master yet —
                  it will be added automatically (pending admin review) using the details below.
                </p>
                <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
                  <div className="sm:col-span-2">
                    <label className="block text-xs font-medium text-slate-600 mb-1">Description *</label>
                    <input
                      value={newItemDescription}
                      onChange={(e) => setNewItemDescription(e.target.value)}
                      placeholder="e.g. Bond paper A4 80gsm"
                      className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-slate-600 mb-1">Unit *</label>
                    <input
                      value={newItemUnit}
                      onChange={(e) => setNewItemUnit(e.target.value)}
                      placeholder="e.g. ream"
                      className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-slate-600 mb-1">Unit Cost (optional)</label>
                    <input
                      type="number" min={0} step="any"
                      value={newItemUnitCost}
                      onChange={(e) => setNewItemUnitCost(e.target.value)}
                      className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                    />
                  </div>
                  <div className="sm:col-span-4">
                    <label className="block text-xs font-medium text-slate-600 mb-1">Item Type (optional)</label>
                    <input
                      value={newItemType}
                      onChange={(e) => setNewItemType(e.target.value)}
                      placeholder="e.g. Office Supplies"
                      className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                    />
                  </div>
                </div>
              </div>
            )}

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Counted Qty</label>
              <input
                type="number" min={0} step="any"
                value={countedQty}
                onChange={(e) => setCountedQty(e.target.value)}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Effective Date</label>
              <input
                type="date"
                value={effectiveDate}
                max={todayIso()}
                onChange={(e) => setEffectiveDate(e.target.value)}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            <div className="sm:col-span-3">
              <label className="block text-xs font-medium text-slate-600 mb-1">Reason (optional)</label>
              <input
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="e.g. Quarterly physical count"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            <div className="flex items-end">
              <button
                type="submit"
                disabled={submitting}
                className="w-full bg-green-600 text-white font-semibold text-sm px-4 py-2 hover:bg-green-500 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {submitting ? "Saving…" : "Record Count"}
              </button>
            </div>
          </form>
        </div>

        {/* ── History / reconciliation view ─────────────────────────────── */}
        {historyStockNo && (
          <div className="bg-white border border-slate-200 shadow-sm p-5">
            <h2 className="text-base font-semibold text-slate-800 mb-3">
              History — {historyStockNo}
              {matchedItem?.stockNo === historyStockNo && matchedItem.description && (
                <span className="font-normal text-slate-500"> — {matchedItem.description}</span>
              )}
            </h2>

            {historyLoading ? (
              <p className="text-sm text-slate-500">Loading…</p>
            ) : !history || history.length === 0 ? (
              <p className="text-sm text-slate-500">No physical counts recorded yet for this item.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-slate-500 border-b border-slate-200">
                      <th className="py-2 pr-3">Effective Date</th>
                      <th className="py-2 pr-3">Counted</th>
                      <th className="py-2 pr-3">System On-Hand</th>
                      <th className="py-2 pr-3">Variance</th>
                      <th className="py-2 pr-3">Reason</th>
                      <th className="py-2 pr-3">Recorded By</th>
                      <th className="py-2 pr-3" />
                    </tr>
                  </thead>
                  <tbody>
                    {history.map((entry) => (
                      <tr key={entry.id} className="border-b border-slate-100 last:border-0">
                        {editingId === entry.id ? (
                          <>
                            <td className="py-2 pr-3">
                              <input type="date" value={editValues.effectiveDate} max={todayIso()}
                                onChange={(e) => setEditValues((v) => ({ ...v, effectiveDate: e.target.value }))}
                                className="w-full px-2 py-1 border border-slate-200 text-sm" />
                            </td>
                            <td className="py-2 pr-3">
                              <input type="number" min={0} step="any" value={editValues.countedQty}
                                onChange={(e) => setEditValues((v) => ({ ...v, countedQty: e.target.value }))}
                                className="w-20 px-2 py-1 border border-slate-200 text-sm" />
                            </td>
                            <td className="py-2 pr-3 text-slate-400">{entry.systemOnHandAtEntry}</td>
                            <td className="py-2 pr-3 text-slate-400">{entry.varianceQty}</td>
                            <td className="py-2 pr-3">
                              <input value={editValues.reason}
                                onChange={(e) => setEditValues((v) => ({ ...v, reason: e.target.value }))}
                                className="w-full px-2 py-1 border border-slate-200 text-sm" />
                            </td>
                            <td className="py-2 pr-3 text-slate-400">{entry.recordedByName ?? "—"}</td>
                            <td className="py-2 pr-3 whitespace-nowrap">
                              <button onClick={() => saveEdit(entry.id)} className="text-green-700 hover:underline mr-2">Save</button>
                              <button onClick={() => setEditingId(null)} className="text-slate-500 hover:underline">Cancel</button>
                            </td>
                          </>
                        ) : (
                          <>
                            <td className="py-2 pr-3">{entry.effectiveDate}</td>
                            <td className="py-2 pr-3">{entry.countedQty}</td>
                            <td className="py-2 pr-3">{entry.systemOnHandAtEntry}</td>
                            <td className={`py-2 pr-3 font-medium ${entry.varianceQty > 0 ? "text-green-700" : entry.varianceQty < 0 ? "text-red-600" : "text-slate-500"}`}>
                              {entry.varianceQty > 0 ? "+" : ""}{entry.varianceQty}
                            </td>
                            <td className="py-2 pr-3 text-slate-600">{entry.reason ?? "—"}</td>
                            <td className="py-2 pr-3 text-slate-600">{entry.recordedByName ?? "—"}</td>
                            <td className="py-2 pr-3 whitespace-nowrap">
                              <button onClick={() => startEdit(entry)} className="text-slate-600 hover:underline mr-2">Edit</button>
                              <button onClick={() => confirmDelete(entry)} className="text-red-600 hover:underline">Delete</button>
                            </td>
                          </>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* ── Bulk upload ────────────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm p-5">
          <h2 className="text-base font-semibold text-slate-800 mb-1">Bulk upload</h2>
          <p className="text-sm text-slate-500 mb-3">
            Upload an .xlsx with columns StockNo | CountedQty | EffectiveDate | Reason |
            Description | Unit | UnitCost | ItemType (row 1 is the header; the last four are
            optional, only needed for a StockNo not already in Items Master — it&apos;ll be added
            automatically, pending admin review). Re-uploading the same StockNo + Effective Date
            overwrites that entry.
          </p>

          <div className="flex flex-wrap items-center gap-3">
            <button
              onClick={handleDownloadTemplate}
              disabled={downloadingTemplate}
              className="flex items-center gap-2 px-4 py-2 text-sm border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 shadow-sm transition-colors disabled:opacity-60"
            >
              <span>⬇</span>
              Download Template
            </button>

            <CsvUploadButton
              label="Upload .xlsx"
              accept=".xlsx"
              disabled={previewing}
              onSelect={handleFileSelect}
            />
          </div>

          {previewing && <p className="text-sm text-slate-500 mt-3">Parsing file…</p>}

          {previewRows && (
            <div className="mt-4">
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-slate-500 border-b border-slate-200">
                      <th className="py-2 pr-3">Row</th>
                      <th className="py-2 pr-3">StockNo</th>
                      <th className="py-2 pr-3">Counted Qty</th>
                      <th className="py-2 pr-3">Effective Date</th>
                      <th className="py-2 pr-3">Reason</th>
                      <th className="py-2 pr-3">Description</th>
                      <th className="py-2 pr-3">Unit</th>
                      <th className="py-2 pr-3">Unit Cost</th>
                      <th className="py-2 pr-3">Item Type</th>
                      <th className="py-2 pr-3">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {previewRows.map((row) => (
                      <tr key={row.rowNumber} className={`border-b border-slate-100 last:border-0 ${row.error ? "bg-red-50" : ""}`}>
                        <td className="py-2 pr-3">{row.rowNumber}</td>
                        <td className="py-2 pr-3">{row.stockNo ?? "—"}</td>
                        <td className="py-2 pr-3">{row.countedQty ?? "—"}</td>
                        <td className="py-2 pr-3">{row.effectiveDate ?? "—"}</td>
                        <td className="py-2 pr-3">{row.reason ?? "—"}</td>
                        <td className="py-2 pr-3">{row.description ?? "—"}</td>
                        <td className="py-2 pr-3">{row.unit ?? "—"}</td>
                        <td className="py-2 pr-3">{row.unitCost ?? "—"}</td>
                        <td className="py-2 pr-3">{row.itemType ?? "—"}</td>
                        <td className="py-2 pr-3">
                          {row.error
                            ? <span className="text-red-600">{row.error}</span>
                            : <span className="text-green-700">OK</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="flex items-center gap-3 mt-3">
                <button
                  onClick={handleCommitImport}
                  disabled={committing || previewRows.every((r) => r.error)}
                  className="bg-green-600 text-white font-semibold text-sm px-4 py-2 hover:bg-green-500 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  {committing ? "Importing…" : `Commit ${previewRows.filter((r) => !r.error).length} row(s)`}
                </button>
                <button
                  onClick={() => setPreviewRows(null)}
                  disabled={committing}
                  className="text-sm text-slate-600 hover:underline"
                >
                  Cancel
                </button>
              </div>
            </div>
          )}
        </div>
      </div>

      {confirmDialog && <ConfirmDialog {...confirmDialog} />}
    </div>
  );
}
