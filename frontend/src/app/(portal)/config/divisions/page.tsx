"use client";

/**
 * Division Configuration page — RAL-98.
 *
 * Configurable per-office divisions that carry both data scope (which office's
 * data a user sees) AND feature-permission flags (replacing the retired
 * PermissionGroup table). Each division row = one "group" for Staff users.
 *
 * Pattern mirrors Offices (RAL-73): DataTable + Modal + CsvUpload/Download.
 * Upsert key = name (within office_code) — name is readonly on edit.
 * Code is optional (some PPDO divisions have no official short code).
 *
 * CSV columns (§5 of Allocation_Requirements.md):
 *   office_code, code, name, is_active,
 *   can_access_budget_planning, can_access_inventory, can_access_reports,
 *   can_manage_config, can_upload_aip, can_manage_users, can_manage_resource_links
 *
 * Access guard: only users with canManageConfig may view this page.
 *
 * Endpoints (ConfigDivisionFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/divisions?active=&officeId=
 *   POST   /api/config/divisions
 *   PUT    /api/config/divisions/{id}
 *   DELETE /api/config/divisions/{id}        (soft delete)
 *   GET    /api/config/divisions/csv         (export)
 *   POST   /api/config/divisions/csv         (upsert)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createDivision,
  deactivateDivision,
  exportDivisionsCsv,
  importDivisionsCsv,
  listDivisions,
  listOffices,
  updateDivision,
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
  DivisionResponse,
  OfficeResponse,
  UpsertDivisionRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const FLAG_FIELDS: { key: keyof UpsertDivisionRequest & `can${string}`; label: string }[] = [
  { key: "canAccessBudgetPlanning", label: "Budget Planning" },
  { key: "canAccessInventory",      label: "Inventory" },
  { key: "canAccessReports",        label: "Reports" },
  { key: "canManageConfig",         label: "Manage Config" },
  { key: "canUploadAip",            label: "Upload AIP" },
  { key: "canManageUsers",          label: "Manage Users" },
  { key: "canManageResourceLinks",  label: "Resource Links" },
];

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
// Flag badge row (compact inline list for table cell)
// ---------------------------------------------------------------------------

function FlagBadges({ division }: { division: DivisionResponse }) {
  const active = FLAG_FIELDS.filter((f) => division[f.key as keyof DivisionResponse] === true);
  if (active.length === 0) return <span className="text-slate-400 text-xs">None</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {active.map((f) => (
        <span
          key={f.key}
          className="inline-flex items-center px-1.5 py-0.5 text-[10px] font-medium bg-blue-50 text-blue-600"
        >
          {f.label}
        </span>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

const blankFlags = (): Pick<UpsertDivisionRequest, `can${string}` & keyof UpsertDivisionRequest> => ({
  canAccessBudgetPlanning: false,
  canAccessInventory:      false,
  canAccessReports:        false,
  canManageConfig:         false,
  canUploadAip:            false,
  canManageUsers:          false,
  canManageResourceLinks:  false,
});

interface FormState {
  officeId: number | "";
  code: string;
  name: string;
  isActive: boolean;
  canAccessBudgetPlanning: boolean;
  canAccessInventory:      boolean;
  canAccessReports:        boolean;
  canManageConfig:         boolean;
  canUploadAip:            boolean;
  canManageUsers:          boolean;
  canManageResourceLinks:  boolean;
}

const blankForm = (): FormState => ({
  officeId: "",
  code: "",
  name: "",
  isActive: true,
  ...blankFlags(),
});

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function DivisionConfigPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [authChecked] = useState(true);
  const [divisions, setDivisions] = useState<DivisionResponse[]>([]);
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  const [showForm, setShowForm] = useState(false);
  const [editTarget, setEditTarget] = useState<DivisionResponse | null>(null);
  const [form, setForm] = useState<FormState>(blankForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

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

  // ── Load offices (for form dropdown) ────────────────────────────────────────

  useEffect(() => {
    listOffices({ active: "true" }).then(setOffices).catch(() => {/* non-fatal */});
  }, []);

  // ── Load divisions ───────────────────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const active = STATUS_TO_ACTIVE[statusFilter];
      const data = await listDivisions({ active });
      // Client-side search filter (server doesn't support name search yet — small dataset)
      const q = debouncedSearch.toLowerCase();
      const filtered = q
        ? data.filter(
            (d) =>
              d.name.toLowerCase().includes(q) ||
              (d.code ?? "").toLowerCase().includes(q) ||
              (d.officeName ?? "").toLowerCase().includes(q) ||
              (d.officeCode ?? "").toLowerCase().includes(q),
          )
        : data;
      setDivisions(filtered);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load divisions. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, statusFilter]);

  useEffect(() => {
    if (authChecked) void load();
  }, [authChecked, load]);

  // ── Add / Edit ────────────────────────────────────────────────────────────────

  function openAdd() {
    setEditTarget(null);
    setForm(blankForm());
    setFormError(null);
    setShowForm(true);
  }

  function openEdit(division: DivisionResponse) {
    setEditTarget(division);
    setForm({
      officeId:                division.officeId,
      code:                    division.code ?? "",
      name:                    division.name,
      isActive:                division.isActive,
      canAccessBudgetPlanning: division.canAccessBudgetPlanning,
      canAccessInventory:      division.canAccessInventory,
      canAccessReports:        division.canAccessReports,
      canManageConfig:         division.canManageConfig,
      canUploadAip:            division.canUploadAip,
      canManageUsers:          division.canManageUsers,
      canManageResourceLinks:  division.canManageResourceLinks,
    });
    setFormError(null);
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditTarget(null);
  }

  async function handleSubmit() {
    if (form.officeId === "") { setFormError("Office is required."); return; }
    if (!form.name.trim()) { setFormError("Division name is required."); return; }

    const body: UpsertDivisionRequest = {
      officeId:                form.officeId as number,
      code:                    form.code.trim() || null,
      name:                    form.name.trim(),
      isActive:                editTarget ? editTarget.isActive : true,
      canAccessBudgetPlanning: form.canAccessBudgetPlanning,
      canAccessInventory:      form.canAccessInventory,
      canAccessReports:        form.canAccessReports,
      canManageConfig:         form.canManageConfig,
      canUploadAip:            form.canUploadAip,
      canManageUsers:          form.canManageUsers,
      canManageResourceLinks:  form.canManageResourceLinks,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updateDivision(editTarget.id, body);
        toast.success("Division updated", `${body.name} saved.`);
      } else {
        await createDivision(body);
        toast.success("Division created", `${body.name} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the division. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  // ── Deactivate / Reactivate ───────────────────────────────────────────────────

  function confirmDeactivate(division: DivisionResponse) {
    setConfirm({
      title: "Deactivate division?",
      message: `${division.name} will be hidden from dropdowns. Users assigned to it and existing data that references it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(division),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(division: DivisionResponse) {
    try {
      await deactivateDivision(division.id);
      toast.success("Division deactivated", `${division.name} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(division: DivisionResponse) {
    try {
      await updateDivision(division.id, {
        officeId:                division.officeId,
        code:                    division.code,
        name:                    division.name,
        isActive:                true,
        canAccessBudgetPlanning: division.canAccessBudgetPlanning,
        canAccessInventory:      division.canAccessInventory,
        canAccessReports:        division.canAccessReports,
        canManageConfig:         division.canManageConfig,
        canUploadAip:            division.canUploadAip,
        canManageUsers:          division.canManageUsers,
        canManageResourceLinks:  division.canManageResourceLinks,
      });
      toast.success("Division reactivated", `${division.name} is now active.`);
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
      const result = await importDivisionsCsv(text);
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

  const columns: Column<DivisionResponse>[] = [
    {
      key: "code",
      header: "Code",
      render: (d) => (
        <span className="font-mono text-xs text-slate-500">{d.code ?? "—"}</span>
      ),
    },
    {
      key: "name",
      header: "Name",
      sortable: true,
      render: (d) => <span className="font-medium text-slate-800">{d.name}</span>,
    },
    {
      key: "officeName",
      header: "Office",
      sortable: true,
      render: (d) => (
        <span className="text-slate-600 text-sm">
          {d.officeCode ? (
            <>
              <span className="font-mono text-xs text-slate-400 mr-1">{d.officeCode}</span>
              {d.officeName ?? "—"}
            </>
          ) : (
            d.officeName ?? "—"
          )}
        </span>
      ),
    },
    {
      key: "flags",
      header: "Flags",
      render: (d) => <FlagBadges division={d} />,
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: (d) => (d.isActive ? 1 : 0),
      render: (d) => <StatusBadge active={d.isActive} />,
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (d) => (
        <div className="flex items-center justify-end gap-2 text-sm">
          <TextAction onClick={() => openEdit(d)}>Edit</TextAction>
          <span className="text-slate-300">·</span>
          {d.isActive ? (
            <TextAction danger onClick={() => confirmDeactivate(d)}>
              Deactivate
            </TextAction>
          ) : (
            <TextAction onClick={() => void doReactivate(d)}>Reactivate</TextAction>
          )}
        </div>
      ),
    },
  ];

  // ── Render ────────────────────────────────────────────────────────────────────

  const filtersActive = debouncedSearch !== "" || statusFilter !== "Active";

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-800">Divisions</h1>
            <p className="text-sm text-slate-500">
              Configurable per-office divisions that carry data scope and feature-permission flags.
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <CsvDownloadButton
              filename="divisions.csv"
              fetchCsv={exportDivisionsCsv}
              onError={(msg) => toast.error("Export failed", msg)}
            />
            <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
            <button
              onClick={openAdd}
              className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
            >
              <span className="text-base leading-none">+</span>
              Add Division
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, code, or office…"
            className="flex-1 min-w-[220px] px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
          />
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
              onClick={() => { setSearch(""); setStatusFilter("Active"); }}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-1"
            >
              Reset
            </button>
          )}
        </div>

        {/* Table */}
        <DataTable
          columns={columns}
          rows={divisions}
          rowKey={(d) => d.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage={
            filtersActive
              ? "No divisions match your filters."
              : "No divisions yet. Upload divisions_seed.csv to get started."
          }
          pageSize={25}
          rowNoun={["division", "divisions"]}
        />
      </div>

      {/* ── Add / Edit modal ──────────────────────────────────────────────────── */}
      {showForm && (
        <Modal
          title={editTarget ? `Edit Division — ${editTarget.name}` : "Add Division"}
          size="md"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
                {editTarget ? "Save Changes" : "Create Division"}
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-4">
            {/* Office */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Office *</label>
              <select
                value={form.officeId}
                onChange={(e) => setForm((f) => ({ ...f, officeId: Number(e.target.value) || "" }))}
                disabled={!!editTarget}
                className="w-full px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
              >
                <option value="">Select an office…</option>
                {offices.map((o) => (
                  <option key={o.id} value={o.id}>{o.officeName} ({o.officeCode})</option>
                ))}
              </select>
              {editTarget && (
                <p className="mt-1 text-[11px] text-slate-400">Office cannot be changed.</p>
              )}
            </div>

            {/* Name */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Division Name *</label>
              <input
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                disabled={!!editTarget}
                placeholder="Administrative Division"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
              />
              {editTarget ? (
                <p className="mt-1 text-[11px] text-slate-400">
                  The division name is the unique key and cannot be changed.
                </p>
              ) : (
                <p className="mt-1 text-[11px] text-slate-400">
                  Full division name — the upsert key within the office.
                </p>
              )}
            </div>

            {/* Code (optional) */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">
                Short Code <span className="font-normal text-slate-400">(optional)</span>
              </label>
              <input
                value={form.code}
                onChange={(e) => setForm((f) => ({ ...f, code: e.target.value }))}
                placeholder="ADMIN"
                className="w-full px-3 py-2 text-sm font-mono border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              <p className="mt-1 text-[11px] text-slate-400">
                Optional short code for display in narrow grids (e.g. ADMIN, ICT). Leave blank if none.
              </p>
            </div>

            {/* Feature flags */}
            <div>
              <p className="text-xs font-medium text-slate-600 mb-2">Feature Flags</p>
              <div className="grid grid-cols-2 gap-2">
                {FLAG_FIELDS.map((f) => (
                  <label key={f.key} className="flex items-center gap-2 text-sm text-slate-700 cursor-pointer select-none">
                    <input
                      type="checkbox"
                      checked={form[f.key as keyof FormState] as boolean}
                      onChange={(e) => setForm((prev) => ({ ...prev, [f.key]: e.target.checked }))}
                      className="w-4 h-4 accent-green-600"
                    />
                    {f.label}
                  </label>
                ))}
              </div>
              <p className="mt-2 text-[11px] text-slate-400">
                Staff users inherit these flags from their division (per-user overrides take precedence).
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
          title="Import divisions from CSV"
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
              Rows are matched by <span className="font-mono text-xs">(office_code, name)</span>: new
              entries are added and existing ones are updated. Nothing is deleted.
            </p>
            <p className="text-xs text-slate-400">
              Expected columns: office_code, code, name, is_active, can_access_budget_planning,
              can_access_inventory, can_access_reports, can_manage_config, can_upload_aip,
              can_manage_users, can_manage_resource_links.
            </p>
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
