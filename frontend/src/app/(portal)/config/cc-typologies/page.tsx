"use client";

/**
 * Climate Change Typologies configuration page — RAL-247.
 *
 * The CCET (Climate Change Expenditure Tagging) vocabulary — codes like A113-08 and M314-03 —
 * that tags an AIP activity's climate-change contribution. Replaces the free string on
 * AipActivity.CcTypologyCode, which is on a document that gets audited and had no controlled
 * vocabulary. Built on the same pattern as the Funding Sources page (RAL-74).
 *
 * ⚠️ This is the vocabulary, not the assignment. 18 of the 167 tagged FY2027 activities carry
 * TWO codes in the one field, comma- or semicolon-separated, so the Phase 2 backfill that puts
 * activities onto references needs a join table, not a single FK column. The form refuses a
 * pasted multi-code value for the same reason.
 *
 * Access guard: only users with canManageConfig may view this page. The list endpoint itself is
 * readable by any authenticated user, because an AIP activity picker needs it.
 *
 * Endpoints (ConfigClimateChangeTypologyFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/cc-typologies?search=&active=
 *   POST   /api/config/cc-typologies
 *   PUT    /api/config/cc-typologies/{id}
 *   DELETE /api/config/cc-typologies/{id}    (soft delete)
 *   GET    /api/config/cc-typologies/csv
 *   POST   /api/config/cc-typologies/csv     (upsert by code)
 *
 * CSV import/export (PPDO-19) follows the funding-sources page. The import reuses the same
 * validation the form applies, so a pasted multi-code value is refused there too.
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createCcTypology,
  deactivateCcTypology,
  exportCcTypologiesCsv,
  importCcTypologiesCsv,
  listCcTypologies,
  updateCcTypology,
} from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import Modal from "@/components/ui/Modal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import CsvImportSummary from "@/components/ui/CsvImportSummary";
import CsvUploadButton from "@/components/ui/CsvUploadButton";
import CsvDownloadButton from "@/components/ui/CsvDownloadButton";
import { useToast } from "@/components/ui/Toast";
import RowActions, { type RowAction } from "@/components/ui/RowActions";
import type {
  ActiveFilter,
  ClimateChangeTypologyResponse,
  CsvImportResult,
  UpsertClimateChangeTypologyRequest,
} from "@/types";

type StatusFilter = "Active" | "Inactive" | "All";

const STATUS_OPTIONS: StatusFilter[] = ["Active", "Inactive", "All"];

const STATUS_TO_ACTIVE: Record<StatusFilter, ActiveFilter> = {
  Active: "true",
  Inactive: "false",
  All: "all",
};

/** Mirrors the server's whitelist in ClimateChangeTypologyService.Categories. */
const CATEGORIES = ["Adaptation", "Mitigation", "Unclassified"] as const;

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

function CategoryBadge({ category }: { category: string }) {
  const tone =
    category === "Adaptation"
      ? "bg-info-100 text-info-500"
      : category === "Mitigation"
      ? "bg-green-100 text-green-700"
      : "bg-amber-100 text-amber-500";
  return <span className={`inline-flex items-center px-2 py-0.5 text-xs font-medium ${tone}`}>{category}</span>;
}

interface FormState {
  code: string;
  name: string;
  category: string;
  description: string;
}

const EMPTY_FORM: FormState = { code: "", name: "", category: "Adaptation", description: "" };

export default function CcTypologiesPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [typologies, setTypologies] = useState<ClimateChangeTypologyResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  const [formOpen, setFormOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<ClimateChangeTypologyResponse | null>(null);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // CSV import (PPDO-19): a chosen file waits for confirmation, then its result is shown once.
  const [pendingCsv, setPendingCsv] = useState<File | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<CsvImportResult | null>(null);

  // Permission guard — mirrors the other config pages.
  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canManageConfig) router.replace(!data.isHostOffice ? "/budget-planning" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(t);
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      setTypologies(
        await listCcTypologies({
          search: debouncedSearch || undefined,
          active: STATUS_TO_ACTIVE[statusFilter],
        }),
      );
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load climate change typologies."));
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, statusFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  function openAdd() {
    setEditTarget(null);
    setForm(EMPTY_FORM);
    setFormError(null);
    setFormOpen(true);
  }

  function openEdit(t: ClimateChangeTypologyResponse) {
    setEditTarget(t);
    setForm({
      code: t.code,
      name: t.name,
      category: t.category,
      description: t.description ?? "",
    });
    setFormError(null);
    setFormOpen(true);
  }

  function closeForm() {
    setFormOpen(false);
    setEditTarget(null);
    setFormError(null);
  }

  async function handleSubmit() {
    const code = form.code.trim();
    const name = form.name.trim();
    if (!code || !name) {
      setFormError("Code and name are required.");
      return;
    }
    // Same guard the server applies — a pasted "A222-03, A224-05" is two codes, not one, and
    // letting it in recreates the free-text field this table replaces.
    if (code.includes(",") || code.includes(";")) {
      setFormError("Code must be a single code — add each part of a multi-code value separately.");
      return;
    }

    const body: UpsertClimateChangeTypologyRequest = {
      code,
      name,
      category: form.category,
      description: form.description.trim() || null,
      isActive: editTarget ? editTarget.isActive : true,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updateCcTypology(editTarget.id, body);
        toast.success("Typology updated", `${body.code} saved.`);
      } else {
        await createCcTypology(body);
        toast.success("Typology created", `${body.code} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the typology. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDeactivate(t: ClimateChangeTypologyResponse) {
    setConfirm({
      title: "Deactivate typology?",
      message: `${t.code} will be hidden from pickers. AIP activities that already reference it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(t),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(t: ClimateChangeTypologyResponse) {
    try {
      await deactivateCcTypology(t.id);
      toast.success("Typology deactivated", `${t.code} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(t: ClimateChangeTypologyResponse) {
    try {
      await updateCcTypology(t.id, {
        code: t.code,
        name: t.name,
        category: t.category,
        description: t.description,
        isActive: true,
      });
      toast.success("Typology reactivated", `${t.code} is now active.`);
      await load();
    } catch (err) {
      toast.error("Reactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  // ── CSV import ───────────────────────────────────────────────────

  async function doImport() {
    if (!pendingCsv) return;
    setImporting(true);
    try {
      const text = await pendingCsv.text();
      const result = await importCcTypologiesCsv(text);
      setPendingCsv(null);
      setImportResult(result);
      toast.success(
        "Import complete",
        `${result.new} added, ${result.updated} updated, ${result.skipped} skipped.`
      );
      await load();
    } catch (err) {
      setPendingCsv(null);
      toast.error("Import failed", configErrorMessage(err, "The CSV could not be imported."));
    } finally {
      setImporting(false);
    }
  }

  const columns: Column<ClimateChangeTypologyResponse>[] = [
    { key: "code", header: "Code", sortable: true, className: "font-mono" },
    { key: "name", header: "Name", sortable: true },
    {
      key: "category",
      header: "Category",
      sortable: true,
      render: (t) => <CategoryBadge category={t.category} />,
    },
    {
      key: "description",
      header: "Description",
      render: (t) =>
        t.description ? <span>{t.description}</span> : <span className="text-slate-600">—</span>,
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: (t) => (t.isActive ? 1 : 0),
      render: (t) => <StatusBadge active={t.isActive} />,
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      className: "whitespace-nowrap",
      render: (t) => {
        const actions: RowAction[] = [{ key: "edit", label: "Edit", onClick: () => openEdit(t) }];
        if (t.isActive) {
          actions.push({
            key: "deactivate",
            label: "Deactivate",
            onClick: () => confirmDeactivate(t),
            variant: "danger",
          });
        } else {
          actions.push({ key: "reactivate", label: "Reactivate", onClick: () => void doReactivate(t) });
        }
        return <RowActions actions={actions} btnPaddingX="px-1" />;
      },
    },
  ];

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        <ConfigPageHeader
          title="Climate Change Typologies"
          description="CCET codes used to tag an AIP activity's climate-change contribution."
          actions={
            <>
              <CsvDownloadButton
                filename="cc_typologies.csv"
                fetchCsv={exportCcTypologiesCsv}
                onError={(msg) => toast.error("Export failed", msg)}
              />
              <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
              <button
                onClick={openAdd}
                className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
              >
                <span className="text-base leading-none">+</span>
                Add Typology
              </button>
            </>
          }
        />

        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by code or name…"
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
                    : "bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        {fetchError && (
          <div className="bg-danger-100 border border-danger-500 text-danger-500 px-4 py-3 text-sm">
            {fetchError}
          </div>
        )}

        <DataTable
          columns={columns}
          rows={typologies}
          rowKey={(t) => t.id}
          loading={loading}
          emptyMessage="No climate change typologies match these filters."
        />
      </div>

      {formOpen && (
        <Modal
          title={editTarget ? `Edit ${editTarget.code}` : "Add Typology"}
          size="sm"
          onClose={closeForm}
          footer={
            <>
              <button
                onClick={closeForm}
                className="px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100 transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => void handleSubmit()}
                disabled={saving}
                className="px-4 py-2 bg-green-600 text-white text-sm font-semibold hover:bg-green-500 transition-colors disabled:opacity-50"
              >
                {saving ? "Saving…" : "Save"}
              </button>
            </>
          }
        >
          <div className="space-y-3">
            {formError && (
              <div className="bg-danger-100 border border-danger-500 text-danger-500 px-3 py-2 text-sm">
                {formError}
              </div>
            )}

            <label className="block">
              <span className="block text-sm font-medium text-slate-800 mb-1">Code *</span>
              <input
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                placeholder="A113-08"
                className="w-full px-3 py-2 text-sm border border-slate-200 font-mono focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              <span className="block text-xs text-slate-600 mt-1">
                One code per row. Saved in upper case.
              </span>
            </label>

            <label className="block">
              <span className="block text-sm font-medium text-slate-800 mb-1">Name *</span>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </label>

            <label className="block">
              <span className="block text-sm font-medium text-slate-800 mb-1">Category *</span>
              <select
                value={form.category}
                onChange={(e) => setForm({ ...form, category: e.target.value })}
                className="w-full px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
              >
                {CATEGORIES.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </label>

            <label className="block">
              <span className="block text-sm font-medium text-slate-800 mb-1">Description</span>
              <textarea
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                rows={3}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </label>
          </div>
        </Modal>
      )}

      {/* ── CSV import confirm ─────────────────────────────────── */}
      {pendingCsv && (
        <Modal
          title="Import typologies from CSV"
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
              Rows are matched by <span className="font-mono text-xs">code</span>: new codes are
              added and existing ones are updated. Nothing is deleted.
            </p>
            <p className="text-xs text-slate-600">
              Expected columns: code, name, category, description, is_active. Category must be
              Adaptation, Mitigation or Unclassified.
            </p>
          </div>
        </Modal>
      )}

      {importResult && (
        <CsvImportSummary result={importResult} onClose={() => setImportResult(null)} />
      )}

      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}
