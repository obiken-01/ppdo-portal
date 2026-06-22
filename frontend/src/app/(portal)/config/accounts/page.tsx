"use client";

/**
 * Account Configuration page — RAL-72.
 *
 * Chart of Accounts CRUD + CSV round-trip for the Budget Planning module.
 * First of the v1.1 config pages (Accounts → Offices → Funding Sources); it
 * establishes the reusable pattern built on the shared UI components:
 *   DataTable · Modal · MessageDialog · ConfirmDialog · CsvUploadButton ·
 *   CsvDownloadButton · Toast.
 *
 * Access guard: only users with canManageConfig may view this page; others are
 * redirected to /dashboard (office users are bounced by the portal layout).
 *
 * Filtering is server-side (the API translates accountType → account_number
 * prefix and applies search / active filters); DataTable handles sort + paging.
 *
 * CSV upload is the seeding path — the initial 143 accounts are loaded by
 * uploading chart_of_accounts.csv here (no seed migration exists).
 *
 * Endpoints (ConfigAccountFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/accounts?search=&accountType=&active=
 *   POST   /api/config/accounts
 *   PUT    /api/config/accounts/{id}
 *   DELETE /api/config/accounts/{id}        (soft delete)
 *   GET    /api/config/accounts/csv         (export)
 *   POST   /api/config/accounts/csv         (upsert)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import {
  configErrorMessage,
  createAccount,
  deactivateAccount,
  exportAccountsCsv,
  importAccountsCsv,
  listAccounts,
  updateAccount,
} from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import Modal from "@/components/ui/Modal";
import MessageDialog from "@/components/ui/MessageDialog";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import CsvUploadButton from "@/components/ui/CsvUploadButton";
import CsvDownloadButton from "@/components/ui/CsvDownloadButton";
import { useToast } from "@/components/ui/Toast";
import type {
  AccountResponse,
  AccountType,
  ActiveFilter,
  CsvImportResult,
  MeResponse,
  UpsertAccountRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Filter option types
// ---------------------------------------------------------------------------

type TypeFilter = "All" | "PS" | "MOOE" | "CO";
type StatusFilter = "Active" | "Inactive" | "All";

const TYPE_OPTIONS: TypeFilter[] = ["All", "PS", "MOOE", "CO"];
const STATUS_OPTIONS: StatusFilter[] = ["Active", "Inactive", "All"];

const STATUS_TO_ACTIVE: Record<StatusFilter, ActiveFilter> = {
  Active: "true",
  Inactive: "false",
  All: "all",
};

// ---------------------------------------------------------------------------
// Expenditure-type badge (derived from account_number prefix)
// ---------------------------------------------------------------------------

const TYPE_BADGE: Record<AccountType, string> = {
  PS: "bg-info-100 text-info-500",
  MOOE: "bg-amber-100 text-amber-500",
  CO: "bg-green-100 text-green-700",
  Other: "bg-slate-100 text-slate-500",
};

function TypeBadge({ type }: { type: AccountType }) {
  return (
    <span className={`inline-flex items-center px-1.5 py-0.5 text-[10px] font-semibold ${TYPE_BADGE[type]}`}>
      {type}
    </span>
  );
}

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
  accountTitle: string;
  accountNumber: string;
  normalBalance: string;
  description: string;
}

const blankForm = (): FormState => ({
  accountTitle: "",
  accountNumber: "",
  normalBalance: "",
  description: "",
});

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AccountConfigPage() {
  const router = useRouter();
  const { toast } = useToast();

  // Auth / permission guard
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Filters
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState<TypeFilter>("All");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  // Add / Edit modal
  const [showForm, setShowForm] = useState(false);
  const [editTarget, setEditTarget] = useState<AccountResponse | null>(null);
  const [form, setForm] = useState<FormState>(blankForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [numberError, setNumberError] = useState<string | null>(null);
  const [checkingNumber, setCheckingNumber] = useState(false);

  // Confirm / message dialogs
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);
  const [pendingCsv, setPendingCsv] = useState<File | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<CsvImportResult | null>(null);

  // ── Auth check ──────────────────────────────────────────────────────────────

  useEffect(() => {
    api
      .get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canManageConfig) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
        else setAuthChecked(true);
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
      const data = await listAccounts({
        search: debouncedSearch,
        accountType: typeFilter === "All" ? null : typeFilter,
        active: STATUS_TO_ACTIVE[statusFilter],
      });
      setAccounts(data);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load accounts. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, typeFilter, statusFilter]);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

  // ── Add / Edit ────────────────────────────────────────────────────────────────

  function openAdd() {
    setEditTarget(null);
    setForm(blankForm());
    setFormError(null);
    setNumberError(null);
    setShowForm(true);
  }

  function openEdit(account: AccountResponse) {
    setEditTarget(account);
    setForm({
      accountTitle: account.accountTitle,
      accountNumber: account.accountNumber,
      normalBalance: account.normalBalance ?? "",
      description: account.description ?? "",
    });
    setFormError(null);
    setNumberError(null);
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditTarget(null);
  }

  // Uniqueness check on blur of account_number.
  async function checkNumberUnique() {
    const number = form.accountNumber.trim();
    setNumberError(null);
    if (!number) return;
    // Unchanged in edit mode — nothing to check.
    if (editTarget && editTarget.accountNumber === number) return;

    setCheckingNumber(true);
    try {
      const matches = await listAccounts({ search: number, active: "all" });
      const clash = matches.some(
        (a) => a.accountNumber.toLowerCase() === number.toLowerCase() && a.id !== editTarget?.id,
      );
      if (clash) setNumberError("An account with this number already exists.");
    } catch {
      // Non-blocking — the server still enforces uniqueness on submit.
    } finally {
      setCheckingNumber(false);
    }
  }

  async function handleSubmit() {
    const title = form.accountTitle.trim();
    const number = form.accountNumber.trim();
    if (!title || !number) {
      setFormError("Account title and account number are required.");
      return;
    }
    if (numberError) {
      setFormError(numberError);
      return;
    }

    const body: UpsertAccountRequest = {
      accountTitle: title,
      accountNumber: number,
      normalBalance: form.normalBalance.trim() || null,
      description: form.description.trim() || null,
      // Modal does not edit status; preserve it on update, default active on create.
      isActive: editTarget ? editTarget.isActive : true,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updateAccount(editTarget.id, body);
        toast.success("Account updated", `${body.accountNumber} saved.`);
      } else {
        await createAccount(body);
        toast.success("Account created", `${body.accountNumber} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the account. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  // ── Deactivate / Reactivate ───────────────────────────────────────────────────

  function confirmDeactivate(account: AccountResponse) {
    setConfirm({
      title: "Deactivate account?",
      message: `${account.accountTitle} (${account.accountNumber}) will be hidden from dropdowns. Existing records that reference it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(account),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(account: AccountResponse) {
    try {
      await deactivateAccount(account.id);
      toast.success("Account deactivated", `${account.accountNumber} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(account: AccountResponse) {
    try {
      await updateAccount(account.id, {
        accountTitle: account.accountTitle,
        accountNumber: account.accountNumber,
        normalBalance: account.normalBalance,
        description: account.description,
        isActive: true,
      });
      toast.success("Account reactivated", `${account.accountNumber} is now active.`);
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
      const result = await importAccountsCsv(text);
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

  const columns: Column<AccountResponse>[] = [
    {
      key: "accountNumber",
      header: "Account Number",
      sortable: true,
      render: (a) => (
        <span className="inline-flex items-center gap-2">
          <span className="font-mono text-slate-800">{a.accountNumber}</span>
          <TypeBadge type={a.accountType} />
        </span>
      ),
    },
    {
      key: "accountTitle",
      header: "Account Title",
      sortable: true,
      render: (a) => (
        <div>
          <div className="font-medium text-slate-800">{a.accountTitle}</div>
          {a.description && <div className="text-xs text-slate-400 truncate max-w-md">{a.description}</div>}
        </div>
      ),
    },
    {
      key: "normalBalance",
      header: "Normal Balance",
      sortable: true,
      render: (a) => a.normalBalance ?? <span className="text-slate-300">—</span>,
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: (a) => (a.isActive ? 1 : 0),
      render: (a) => <StatusBadge active={a.isActive} />,
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (a) => (
        <div className="flex items-center justify-end gap-2 text-sm">
          <TextAction onClick={() => openEdit(a)}>Edit</TextAction>
          <span className="text-slate-300">·</span>
          {a.isActive ? (
            <TextAction danger onClick={() => confirmDeactivate(a)}>
              Deactivate
            </TextAction>
          ) : (
            <TextAction onClick={() => void doReactivate(a)}>Reactivate</TextAction>
          )}
        </div>
      ),
    },
  ];

  const filtersActive = debouncedSearch !== "" || typeFilter !== "All" || statusFilter !== "Active";

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-800">Chart of Accounts</h1>
            <p className="text-sm text-slate-500">
              Expense accounts (PS / MOOE / CO) used across AIP and WFP budget planning.
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <CsvDownloadButton
              filename="accounts.csv"
              fetchCsv={exportAccountsCsv}
              onError={(msg) => toast.error("Export failed", msg)}
            />
            <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
            <button
              onClick={openAdd}
              className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
            >
              <span className="text-base leading-none">+</span>
              Add Account
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by account number or title…"
            className="flex-1 min-w-[220px] px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
          />

          {/* Type dropdown */}
          <div className="flex items-center gap-2">
            <label className="text-xs font-medium text-slate-500">Type</label>
            <select
              value={typeFilter}
              onChange={(e) => setTypeFilter(e.target.value as TypeFilter)}
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
            >
              {TYPE_OPTIONS.map((t) => (
                <option key={t} value={t}>
                  {t === "All" ? "All types" : t}
                </option>
              ))}
            </select>
          </div>

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
                setTypeFilter("All");
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
          rows={accounts}
          rowKey={(a) => a.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage={
            filtersActive
              ? "No accounts match your filters."
              : "No accounts yet. Upload chart_of_accounts.csv to get started."
          }
          pageSize={25}
          rowNoun={["account", "accounts"]}
        />
      </div>

      {/* ── Add / Edit modal ──────────────────────────────────────────────────── */}
      {showForm && (
        <Modal
          title={editTarget ? `Edit Account — ${editTarget.accountNumber}` : "Add Account"}
          size="md"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
                {editTarget ? "Save Changes" : "Create Account"}
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-4">
            {/* Account Title */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Account Title *</label>
              <input
                value={form.accountTitle}
                onChange={(e) => setForm((f) => ({ ...f, accountTitle: e.target.value }))}
                placeholder="Salaries and Wages – Regular"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            {/* Account Number */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Account Number *</label>
              <div className="relative">
                <input
                  value={form.accountNumber}
                  onChange={(e) => {
                    setForm((f) => ({ ...f, accountNumber: e.target.value }));
                    setNumberError(null);
                  }}
                  onBlur={checkNumberUnique}
                  placeholder="5-01-01-010"
                  className={`w-full px-3 py-2 text-sm font-mono border focus:outline-none focus:ring-2 ${
                    numberError
                      ? "border-danger-500 focus:ring-danger-500"
                      : "border-slate-200 focus:ring-green-600"
                  }`}
                />
                {checkingNumber && (
                  <span className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />
                )}
              </div>
              {numberError ? (
                <p className="mt-1 text-xs text-danger-500">{numberError}</p>
              ) : (
                <p className="mt-1 text-[11px] text-slate-400">
                  Prefix sets the type: 5-01- = PS · 5-02- = MOOE · 5-03- = CO.
                </p>
              )}
            </div>

            {/* Normal Balance */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Normal Balance</label>
              <input
                value={form.normalBalance}
                onChange={(e) => setForm((f) => ({ ...f, normalBalance: e.target.value }))}
                placeholder="Debit / Credit"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            {/* Description */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Description</label>
              <textarea
                value={form.description}
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                placeholder="Optional notes about this account."
                rows={3}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 resize-y min-h-[44px]"
              />
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
          title="Import accounts from CSV"
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
              Rows are matched by <span className="font-mono text-xs">account_number</span>: new numbers are
              added and existing ones are updated. Nothing is deleted.
            </p>
            <p className="text-xs text-slate-400">
              Expected columns: account_title, account_number, normal_balance, description, is_active.
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
