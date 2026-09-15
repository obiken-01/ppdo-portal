"use client";

/**
 * Configuration → API Access — partner API key management (v1.8.0 — PPDO-86,
 * docs/v1.8/External_AIP_API_Spec.md §4.2/§6.1).
 *
 * Issues, lists and revokes the keys that authenticate partner government systems (GSO, PBO,
 * etc.) against the read-only external AIP API (`/api/external/v1/*`, PPDO-12/13/14). Every
 * route here — list included — requires CanManageApiKeys (build spec §3.3), unlike most config
 * pages whose list is broadly readable.
 *
 * The issued key is shown exactly once, in a non-dismissible modal (IssuedApiKeyDialog) — it is
 * never recoverable afterwards; a lost key means issuing a new one and revoking the old.
 *
 * Endpoints (ConfigApiKeysFunctions.cs, { data, error, message } envelope):
 *   GET  /api/config/api-keys
 *   POST /api/config/api-keys
 *   POST /api/config/api-keys/{id}/revoke
 *   GET  /api/config/api-keys/{id}/requests?page=
 */

import { useCallback, useEffect, useState } from "react";
import { useMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createApiKey,
  listApiKeyRequests,
  listApiKeys,
  listOffices,
  revokeApiKey,
} from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import Modal from "@/components/ui/Modal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import IssuedApiKeyDialog from "@/components/ui/IssuedApiKeyDialog";
import { useToast } from "@/components/ui/Toast";
import RowActions, { type RowAction } from "@/components/ui/RowActions";
import type {
  ApiKeyListItem,
  ApiKeyRequestLogItem,
  ApiKeyStatus,
  OfficeResponse,
} from "@/types";

const STATUS_BADGE: Record<ApiKeyStatus, string> = {
  Active: "bg-green-100 text-green-700",
  Expired: "bg-amber-100 text-amber-700",
  Revoked: "bg-slate-200 text-slate-600",
};

function StatusBadge({ status }: { status: ApiKeyStatus }) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 text-xs font-medium ${STATUS_BADGE[status]}`}>
      {status}
    </span>
  );
}

function OfficesCell({ item }: { item: ApiKeyListItem }) {
  if (item.allOffices) return <span>All offices</span>;
  if (item.offices.length === 0) return <span className="text-slate-600">—</span>;
  const shown = item.offices.slice(0, 3);
  const extra = item.offices.length - shown.length;
  return (
    <span>
      {shown.map((o) => o.code).join(", ")}
      {extra > 0 && <span className="text-slate-600"> +{extra}</span>}
    </span>
  );
}

function formatDateTime(iso: string | null): string {
  if (!iso) return "Never";
  return new Date(iso).toLocaleString("en-PH", {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

interface FormState {
  partnerName: string;
  allOffices: boolean;
  officeIds: number[];
  expiresAt: string;
}

const EMPTY_FORM: FormState = { partnerName: "", allOffices: true, officeIds: [], expiresAt: "" };

export default function ApiAccessPage() {
  const { toast } = useToast();
  const me = useMe(
    (m) => m.canManageApiKeys,
    (m) => (!m.isHostOffice ? "/budget-planning" : "/dashboard"),
  );

  const [keys, setKeys] = useState<ApiKeyListItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [offices, setOffices] = useState<OfficeResponse[]>([]);

  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [issued, setIssued] = useState<{ partnerName: string; plaintextKey: string } | null>(null);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  const [usageTarget, setUsageTarget] = useState<ApiKeyListItem | null>(null);
  const [usageItems, setUsageItems] = useState<ApiKeyRequestLogItem[]>([]);
  const [usageTotal, setUsageTotal] = useState(0);
  const [usagePage, setUsagePage] = useState(1);
  const [usageLoading, setUsageLoading] = useState(false);
  const [usageError, setUsageError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      setKeys(await listApiKeys());
    } catch (err) {
      setFetchError(configErrorMessage(err, "API keys could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    listOffices({ active: "true" })
      .then(setOffices)
      .catch(() => setOffices([]));
  }, []);

  function openAdd() {
    setForm(EMPTY_FORM);
    setFormError(null);
    setFormOpen(true);
  }

  function closeForm() {
    if (saving) return;
    setFormOpen(false);
    setFormError(null);
  }

  function toggleOffice(id: number) {
    setForm((f) => ({
      ...f,
      officeIds: f.officeIds.includes(id)
        ? f.officeIds.filter((o) => o !== id)
        : [...f.officeIds, id],
    }));
  }

  async function handleSubmit() {
    const partnerName = form.partnerName.trim();
    if (!partnerName) {
      setFormError("Partner name is required (up to 100 characters).");
      return;
    }
    if (!form.allOffices && form.officeIds.length === 0) {
      setFormError("Choose at least one office, or all offices.");
      return;
    }

    setSaving(true);
    setFormError(null);
    try {
      const result = await createApiKey({
        partnerName,
        allOffices: form.allOffices,
        officeIds: form.allOffices ? [] : form.officeIds,
        expiresAt: form.expiresAt || null,
      });
      setFormOpen(false);
      setIssued({ partnerName: result.key.partnerName, plaintextKey: result.plaintextKey });
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to issue the API key. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  function confirmRevoke(key: ApiKeyListItem) {
    setConfirm({
      title: "Revoke this API key?",
      message: `Revoke the key for ${key.partnerName}? Their system will be refused on its next request.`,
      confirmLabel: "Revoke",
      variant: "danger",
      onConfirm: () => void doRevoke(key),
      onClose: () => setConfirm(null),
    });
  }

  async function doRevoke(key: ApiKeyListItem) {
    try {
      await revokeApiKey(key.id);
      toast.success("Key revoked", `${key.partnerName}'s key has been revoked.`);
      await load();
    } catch (err) {
      toast.error("Revoke failed", configErrorMessage(err, "Please try again."));
    }
  }

  function openUsage(key: ApiKeyListItem) {
    setUsageTarget(key);
    setUsagePage(1);
  }

  const loadUsage = useCallback(async (id: number, page: number) => {
    setUsageLoading(true);
    setUsageError(null);
    try {
      const result = await listApiKeyRequests(id, page);
      setUsageItems(result.items);
      setUsageTotal(result.total);
    } catch (err) {
      setUsageError(configErrorMessage(err, "Usage history could not be loaded."));
    } finally {
      setUsageLoading(false);
    }
  }, []);

  useEffect(() => {
    if (usageTarget) void loadUsage(usageTarget.id, usagePage);
  }, [usageTarget, usagePage, loadUsage]);

  const columns: Column<ApiKeyListItem>[] = [
    { key: "partnerName", header: "Partner", sortable: true },
    {
      key: "keyPrefix",
      header: "Key",
      className: "font-mono text-xs",
      render: (k) => <span>ppdo_{k.keyPrefix}_…</span>,
    },
    { key: "offices", header: "Offices", render: (k) => <OfficesCell item={k} /> },
    {
      key: "status",
      header: "Status",
      sortable: true,
      render: (k) => <StatusBadge status={k.status} />,
    },
    {
      key: "expiresAt",
      header: "Expires",
      render: (k) => <span>{k.expiresAt ? formatDateTime(k.expiresAt) : "Never"}</span>,
    },
    {
      key: "lastUsedAt",
      header: "Last used",
      render: (k) => <span>{formatDateTime(k.lastUsedAt)}</span>,
    },
    { key: "createdByName", header: "Created by" },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      className: "whitespace-nowrap",
      render: (k) => {
        const actions: RowAction[] = [
          { key: "usage", label: "Usage", onClick: () => openUsage(k) },
        ];
        if (k.status === "Active") {
          actions.push({
            key: "revoke",
            label: "Revoke",
            onClick: () => confirmRevoke(k),
            variant: "danger",
          });
        }
        return <RowActions actions={actions} btnPaddingX="px-1" />;
      },
    },
  ];

  const usageColumns: Column<ApiKeyRequestLogItem>[] = [
    { key: "requestedAt", header: "Requested", render: (r) => <span>{formatDateTime(r.requestedAt)}</span> },
    { key: "route", header: "Route", className: "font-mono text-xs" },
    { key: "officeCode", header: "Office", render: (r) => <span>{r.officeCode ?? "All"}</span> },
    { key: "fiscalYear", header: "FY", render: (r) => <span>{r.fiscalYear ?? "—"}</span> },
    { key: "statusCode", header: "Status" },
  ];

  if (!me) return null;

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        <ConfigPageHeader
          title="API Access"
          description="Partner API keys for the external AIP API (GSO, PBO, and other provincial systems)."
          actions={
            <button
              onClick={openAdd}
              className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
            >
              <span className="text-base leading-none">+</span>
              New API key
            </button>
          }
        />

        <DataTable
          columns={columns}
          rows={keys}
          rowKey={(k) => k.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage="No partner system has an API key yet."
          minWidth={900}
        />
      </div>

      {formOpen && (
        <Modal
          title="New API key"
          size="sm"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={() => void handleSubmit()} loading={saving}>
                Issue key
              </Modal.PrimaryButton>
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
              <span className="block text-sm font-medium text-slate-800 mb-1">Partner name *</span>
              <input
                value={form.partnerName}
                onChange={(e) => setForm({ ...form, partnerName: e.target.value })}
                placeholder="GSO WFP system"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </label>

            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={form.allOffices}
                onChange={(e) => setForm({ ...form, allOffices: e.target.checked })}
                className="h-4 w-4"
              />
              <span className="text-sm font-medium text-slate-800">All offices</span>
            </label>

            <div>
              <span className="block text-sm font-medium text-slate-800 mb-1">Offices</span>
              <div
                className={`max-h-40 overflow-y-auto border border-slate-200 divide-y divide-slate-100 ${
                  form.allOffices ? "opacity-50 pointer-events-none" : ""
                }`}
              >
                {offices.map((o) => (
                  <label key={o.id} className="flex items-center gap-2 px-3 py-1.5 text-sm hover:bg-slate-50">
                    <input
                      type="checkbox"
                      checked={form.officeIds.includes(o.id)}
                      onChange={() => toggleOffice(o.id)}
                      disabled={form.allOffices}
                      className="h-4 w-4"
                    />
                    <span className="text-slate-800">{o.officeCode}</span>
                    <span className="text-slate-600">— {o.officeName}</span>
                  </label>
                ))}
              </div>
            </div>

            <label className="block">
              <span className="block text-sm font-medium text-slate-800 mb-1">Expiry date</span>
              <input
                type="date"
                value={form.expiresAt}
                onChange={(e) => setForm({ ...form, expiresAt: e.target.value })}
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              <span className="block text-xs text-slate-600 mt-1">Optional. Leave blank for no expiry.</span>
            </label>
          </div>
        </Modal>
      )}

      {usageTarget && (
        <Modal title={`Usage — ${usageTarget.partnerName}`} size="lg" onClose={() => setUsageTarget(null)}>
          <DataTable
            columns={usageColumns}
            rows={usageItems}
            rowKey={(r) => `${r.requestedAt}-${r.route}-${r.officeCode ?? "all"}`}
            loading={usageLoading}
            error={usageError}
            onRetry={() => void loadUsage(usageTarget.id, usagePage)}
            emptyMessage="This key has not been used yet."
            serverPagination={{
              page: usagePage,
              pageSize: 50,
              totalCount: usageTotal,
              onPageChange: setUsagePage,
            }}
          />
        </Modal>
      )}

      {issued && (
        <IssuedApiKeyDialog
          partnerName={issued.partnerName}
          plaintextKey={issued.plaintextKey}
          onClose={() => setIssued(null)}
        />
      )}

      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}
