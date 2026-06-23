"use client";

/**
 * Office Configuration page — RAL-73.
 *
 * Provincial offices used as planning scope across AIP and WFP. Second of the
 * v1.1 config pages; built on the same reusable pattern as the Account Config
 * page (RAL-72) with the shared UI components:
 *   DataTable · Modal · MessageDialog · ConfirmDialog · CsvUploadButton ·
 *   CsvDownloadButton · Toast.
 *
 * Simpler than Accounts: two user-facing fields (office_code, office_name) plus
 * is_active, no type filter — just search + status. office_code is the unique
 * key (readonly on edit). Codes are short alphabetic strings (PGO, SPO, PPDO).
 *
 * Access guard: only users with canManageConfig may view this page.
 *
 * CSV upload is the seeding path — the initial 16 offices are loaded by
 * uploading offices.csv here (no seed migration exists).
 *
 * Endpoints (ConfigOfficeFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/offices?search=&active=
 *   POST   /api/config/offices
 *   PUT    /api/config/offices/{id}
 *   DELETE /api/config/offices/{id}        (soft delete)
 *   GET    /api/config/offices/csv         (export)
 *   POST   /api/config/offices/csv         (upsert)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createOffice,
  deactivateOffice,
  exportOfficesCsv,
  importOfficesCsv,
  listOffices,
  updateOffice,
} from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import Modal from "@/components/ui/Modal";
import MessageDialog from "@/components/ui/MessageDialog";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import CsvUploadButton from "@/components/ui/CsvUploadButton";
import CsvDownloadButton from "@/components/ui/CsvDownloadButton";
import { useToast } from "@/components/ui/Toast";
import type {
  ActiveFilter,
  CsvImportResult,
  OfficeResponse,
  UpsertOfficeRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Filter option types
// ---------------------------------------------------------------------------

type StatusFilter = "Active" | "Inactive" | "All";

const STATUS_OPTIONS: StatusFilter[] = ["Active", "Inactive", "All"];

const STATUS_TO_ACTIVE: Record<StatusFilter, ActiveFilter> = {
  Active: "true",
  Inactive: "false",
  All: "all",
};

// ---------------------------------------------------------------------------
// Status badge
// ---------------------------------------------------------------------------

function StatusBadge({ active }: { active: boolean }) {
  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 text-xs font-medium ${
        active ? "bg-green-100 text-green-700" : "bg-danger-100 text-danger-500"
      }`}
    >
      {active ? "Active" : "Inactive"}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

interface FormState {
  officeCode: string;
  officeName: string;
  officeRefCode: string;
}

const blankForm = (): FormState => ({ officeCode: "", officeName: "", officeRefCode: "" });

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function OfficeConfigPage() {
  const router = useRouter();
  const { toast } = useToast();

  // Auth / permission guard
  const [authChecked] = useState(true);

  // Data
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Filters
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  // Add / Edit modal
  const [showForm, setShowForm] = useState(false);
  const [editTarget, setEditTarget] = useState<OfficeResponse | null>(null);
  const [form, setForm] = useState<FormState>(blankForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [codeError, setCodeError] = useState<string | null>(null);
  const [checkingCode, setCheckingCode] = useState(false);

  // Confirm / message dialogs
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);
  const [pendingCsv, setPendingCsv] = useState<File | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<CsvImportResult | null>(null);

  // ── Auth check ──────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canManageConfig) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Debounce search ─────────────────────────────────────────────────────────

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(t);
  }, [search]);

  // ── Load (server-side filtering) ─────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const data = await listOffices({
        search: debouncedSearch,
        active: STATUS_TO_ACTIVE[statusFilter],
      });
      setOffices(data);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load offices. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, statusFilter]);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

  // ── Add / Edit ────────────────────────────────────────────────────────────────

  function openAdd() {
    setEditTarget(null);
    setForm(blankForm());
    setFormError(null);
    setCodeError(null);
    setShowForm(true);
  }

  function openEdit(office: OfficeResponse) {
    setEditTarget(office);
    setForm({
      officeCode: office.officeCode,
      officeName: office.officeName,
      officeRefCode: office.officeRefCode ?? "",
    });
    setFormError(null);
    setCodeError(null);
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditTarget(null);
  }

  // Uniqueness check on blur of office_code (add mode only — code is readonly on edit).
  async function checkCodeUnique() {
    if (editTarget) return;
    const code = form.officeCode.trim();
    setCodeError(null);
    if (!code) return;

    setCheckingCode(true);
    try {
      const matches = await listOffices({ search: code, active: "all" });
      const clash = matches.some((o) => o.officeCode.toLowerCase() === code.toLowerCase());
      if (clash) setCodeError("An office with this code already exists.");
    } catch {
      // Non-blocking — the server still enforces uniqueness on submit.
    } finally {
      setCheckingCode(false);
    }
  }

  async function handleSubmit() {
    const code = form.officeCode.trim();
    const name = form.officeName.trim();
    if (!code || !name) {
      setFormError("Office code and office name are required.");
      return;
    }
    if (codeError) {
      setFormError(codeError);
      return;
    }

    const body: UpsertOfficeRequest = {
      officeCode: code,
      officeName: name,
      officeRefCode: form.officeRefCode.trim() || null,
      // Modal does not edit status; preserve it on update, default active on create.
      isActive: editTarget ? editTarget.isActive : true,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updateOffice(editTarget.id, body);
        toast.success("Office updated", `${body.officeCode} saved.`);
      } else {
        await createOffice(body);
        toast.success("Office created", `${body.officeCode} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the office. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  // ── Deactivate / Reactivate ───────────────────────────────────────────────────

  function confirmDeactivate(office: OfficeResponse) {
    setConfirm({
      title: "Deactivate office?",
      message: `${office.officeName} (${office.officeCode}) will be hidden from dropdowns. Existing records that reference it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(office),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(office: OfficeResponse) {
    try {
      await deactivateOffice(office.id);
      toast.success("Office deactivated", `${office.officeCode} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(office: OfficeResponse) {
    try {
      await updateOffice(office.id, {
        officeCode: office.officeCode,
        officeName: office.officeName,
        officeRefCode: office.officeRefCode,
        isActive: true,
      });
      toast.success("Office reactivated", `${office.officeCode} is now active.`);
      await load();
    } catch (err) {
      toast.error("Reactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  // ── CSV import ────────────────────────────────────────────────────────────────

  async function doImport() {
    if (!pendingCsv) return;
    setImporting(true);
    try {
      const text = await pendingCsv.text();
      const result = await importOfficesCsv(text);
      setPendingCsv(null);
      setImportResult(result);
      toast.success(
        "Import complete",
        `${result.new} added, ${result.updated} updated, ${result.skipped} skipped.`,
      );
      await load();
    } catch (err) {
      setPendingCsv(null);
      toast.error("Import failed", configErrorMessage(err, "The CSV could not be imported."));
    } finally {
      setImporting(false);
    }
  }

  // ── Columns ───────────────────────────────────────────────────────────────────

  const columns: Column<OfficeResponse>[] = [
    {
      key: "officeCode",
      header: "Office Code",
      sortable: true,
      render: (o) => <span className="font-mono text-slate-800">{o.officeCode}</span>,
    },
    {
      key: "officeRefCode",
      header: "AIP Ref",
      render: (o) => (
        <span className="font-mono text-xs text-slate-500">{o.officeRefCode ?? "—"}</span>
      ),
    },
    {
      key: "officeName",
      header: "Office Name",
      sortable: true,
      render: (o) => <span className="font-medium text-slate-800">{o.officeName}</span>,
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: (o) => (o.isActive ? 1 : 0),
      render: (o) => <StatusBadge active={o.isActive} />,
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (o) => (
        <div className="flex items-center justify-end gap-2 text-sm">
          <TextAction onClick={() => openEdit(o)}>Edit</TextAction>
          <span className="text-slate-300">·</span>
          {o.isActive ? (
            <TextAction danger onClick={() => confirmDeactivate(o)}>
              Deactivate
            </TextAction>
          ) : (
            <TextAction onClick={() => void doReactivate(o)}>Reactivate</TextAction>
          )}
        </div>
      ),
    },
  ];

  // ── Auth gate ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-full flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  const filtersActive = debouncedSearch !== "" || statusFilter !== "Active";

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-800">Offices</h1>
            <p className="text-sm text-slate-500">
              Provincial government offices used as planning scope across AIP and WFP.
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <CsvDownloadButton
              filename="offices.csv"
              fetchCsv={exportOfficesCsv}
              onError={(msg) => toast.error("Export failed", msg)}
            />
            <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
            <button
              onClick={openAdd}
              className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
            >
              <span className="text-base leading-none">+</span>
              Add Office
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by office code or name…"
            className="flex-1 min-w-[220px] px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
          />

          {/* Status toggle */}
          <div className="flex items-center border border-slate-200 overflow-hidden">
            {STATUS_OPTIONS.map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(s)}
                className={`px-3 py-2 text-sm font-medium transition-colors ${
                  statusFilter === s
                    ? "bg-green-600 text-white"
                    : "bg-white text-slate-500 hover:bg-slate-50"
                }`}
              >
                {s}
              </button>
            ))}
          </div>

          {filtersActive && (
            <button
              onClick={() => {
                setSearch("");
                setStatusFilter("Active");
              }}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-1"
            >
              Reset
            </button>
          )}
        </div>

        {/* Table */}
        <DataTable
          columns={columns}
          rows={offices}
          rowKey={(o) => o.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage={
            filtersActive
              ? "No offices match your filters."
              : "No offices yet. Upload offices.csv to get started."
          }
          pageSize={25}
          rowNoun={["office", "offices"]}
        />
      </div>

      {/* ── Add / Edit modal ──────────────────────────────────────────────────── */}
      {showForm && (
        <Modal
          title={editTarget ? `Edit Office — ${editTarget.officeCode}` : "Add Office"}
          size="md"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
                {editTarget ? "Save Changes" : "Create Office"}
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-4">
            {/* Office Code */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Office Code *</label>
              <div className="relative">
                <input
                  value={form.officeCode}
                  onChange={(e) => {
                    setForm((f) => ({ ...f, officeCode: e.target.value }));
                    setCodeError(null);
                  }}
                  onBlur={checkCodeUnique}
                  disabled={!!editTarget}
                  placeholder="PPDO"
                  className={`w-full px-3 py-2 text-sm font-mono border focus:outline-none focus:ring-2 disabled:bg-slate-100 disabled:text-slate-400 ${
                    codeError
                      ? "border-danger-500 focus:ring-danger-500"
                      : "border-slate-200 focus:ring-green-600"
                  }`}
                />
                {checkingCode && (
                  <span className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />
                )}
              </div>
              {codeError ? (
                <p className="mt-1 text-xs text-danger-500">{codeError}</p>
              ) : editTarget ? (
                <p className="mt-1 text-[11px] text-slate-400">
                  The office code is the unique key and cannot be changed.
                </p>
              ) : (
                <p className="mt-1 text-[11px] text-slate-400">
                  Short code shown as stored, e.g. PGO, SPO, PPDO.
                </p>
              )}
            </div>

            {/* Office Name */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Office Name *</label>
              <input
                value={form.officeName}
                onChange={(e) => setForm((f) => ({ ...f, officeName: e.target.value }))}
                placeholder="Provincial Planning and Development Office"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            {/* AIP Ref Code Suffix */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">
                AIP Ref Code Suffix <span className="font-normal text-slate-400">(optional)</span>
              </label>
              <input
                value={form.officeRefCode}
                onChange={(e) => setForm((f) => ({ ...f, officeRefCode: e.target.value }))}
                placeholder="01-010"
                className="w-full px-3 py-2 text-sm font-mono border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              <p className="mt-1 text-[11px] text-slate-400">
                Last two segments of the AIP office ref code (e.g. <span className="font-mono">01-010</span> from{" "}
                <span className="font-mono">8000-000-1-01-010</span>). Used to link this office to AIP entries in the WFP page.
              </p>
            </div>

            {formError && (
              <div className="bg-danger-100 border border-danger-500/30 px-4 py-3">
                <p className="text-sm text-danger-500">{formError}</p>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* ── CSV import confirm ─────────────────────────────────────────────────── */}
      {pendingCsv && (
        <Modal
          title="Import offices from CSV"
          size="sm"
          onClose={() => !importing && setPendingCsv(null)}
          footer={
            <>
              <Modal.SecondaryButton onClick={() => setPendingCsv(null)} disabled={importing}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={doImport} loading={importing}>
                Import
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              Import <span className="font-medium text-slate-800">{pendingCsv.name}</span>?
            </p>
            <p>
              Rows are matched by <span className="font-mono text-xs">office_code</span>: new codes are
              added and existing ones are updated. Nothing is deleted.
            </p>
            <p className="text-xs text-slate-400">Expected columns: office_code, office_name, is_active, office_ref_code (optional).</p>
          </div>
        </Modal>
      )}

      {/* ── CSV import summary ─────────────────────────────────────────────────── */}
      {importResult && (
        <MessageDialog
          title="Import complete"
          variant={importResult.errors.length > 0 ? "warning" : "success"}
          size="md"
          onClose={() => setImportResult(null)}
        >
          <div className="space-y-3">
            <div className="flex gap-4">
              <Stat label="Added" value={importResult.new} tone="green" />
              <Stat label="Updated" value={importResult.updated} tone="blue" />
              <Stat label="Skipped" value={importResult.skipped} tone="slate" />
            </div>
            {importResult.errors.length > 0 && (
              <div>
                <p className="text-xs font-semibold text-amber-500 uppercase tracking-wide mb-1">
                  {importResult.errors.length} row{importResult.errors.length === 1 ? "" : "s"} skipped
                </p>
                <ul className="max-h-40 overflow-y-auto text-xs text-slate-500 list-disc pl-4 space-y-0.5">
                  {importResult.errors.map((e, i) => (
                    <li key={i}>{e}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </MessageDialog>
      )}

      {/* ── Deactivate confirm ─────────────────────────────────────────────────── */}
      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------

function TextAction({
  children,
  danger,
  onClick,
}: {
  children: React.ReactNode;
  danger?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`font-medium transition-colors ${
        danger ? "text-danger-500 hover:text-red-600" : "text-green-600 hover:text-green-700"
      } hover:underline`}
    >
      {children}
    </button>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone: "green" | "blue" | "slate" }) {
  const cls: Record<typeof tone, string> = {
    green: "text-green-700",
    blue: "text-info-500",
    slate: "text-slate-500",
  };
  return (
    <div className="flex-1 border border-slate-200 px-3 py-2 text-center">
      <div className={`text-2xl font-bold ${cls[tone]}`}>{value}</div>
      <div className="text-[11px] text-slate-400 uppercase tracking-wide">{label}</div>
    </div>
  );
}
