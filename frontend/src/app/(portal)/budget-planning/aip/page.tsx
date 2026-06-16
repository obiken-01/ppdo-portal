"use client";

/**
 * AIP List page — RAL-76.
 *
 * Lists all Annual Investment Program records with FY / status / source filters.
 * Reuses DataTable, ConfirmDialog, and Toast from the shared UI.
 *
 * Access: canAccessBudgetPlanning. "New AIP" / Finalize / Archive require canUploadAip.
 *
 * Endpoints (AipFunctions.cs, { data, error, message } envelope):
 *   GET    /api/budget-planning/aip?fiscalYear=&status=
 *   POST   /api/budget-planning/aip/{id}/finalize
 *   DELETE /api/budget-planning/aip/{id}    (archive)
 */

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { aipErrorMessage, archiveAip, finalizeAip, listAip } from "@/lib/aip";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type { AipRecordResponse, MeResponse } from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const CURRENT_YEAR = new Date().getFullYear();
const FY_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1, CURRENT_YEAR + 2];

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

// ---------------------------------------------------------------------------
// Badges
// ---------------------------------------------------------------------------

function SourceBadge({ source }: { source: string }) {
  const cls =
    source === "Upload"
      ? "bg-blue-100 text-blue-700"
      : "bg-slate-100 text-slate-600";
  return (
    <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{source}</span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-500";
  return (
    <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AipListPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [me, setMe] = useState<MeResponse | null>(null);
  const [records, setRecords] = useState<AipRecordResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [fy, setFy] = useState<number>(CURRENT_YEAR);
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [sourceFilter, setSourceFilter] = useState<string>("");

  // Confirm dialog
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // ---------------------------------------------------------------------------
  // Auth check
  // ---------------------------------------------------------------------------

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canAccessBudgetPlanning) {
        router.replace("/dashboard");
        return;
      }
      setMe(data);
    });
  }, [router]);

  // ---------------------------------------------------------------------------
  // Load AIP records
  // ---------------------------------------------------------------------------

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listAip({
        fiscalYear: fy,
        status: statusFilter || undefined,
      });
      setRecords(data);
    } catch (err) {
      setError(aipErrorMessage(err, "Failed to load AIP records."));
    } finally {
      setLoading(false);
    }
  }, [fy, statusFilter]);

  useEffect(() => {
    if (me) load();
  }, [me, load]);

  // ---------------------------------------------------------------------------
  // Client-side source filter
  // ---------------------------------------------------------------------------

  const visibleRecords = sourceFilter
    ? records.filter((r) => r.entrySource === sourceFilter)
    : records;

  // ---------------------------------------------------------------------------
  // Actions
  // ---------------------------------------------------------------------------

  function handleFinalize(rec: AipRecordResponse) {
    setConfirm({
      title: "Finalize AIP Record",
      message: `Finalize AIP FY${rec.fiscalYear}? Once finalized, the record will be locked and can only be unlocked by an admin.`,
      confirmLabel: "Finalize",
      cancelLabel: "Cancel",
      variant: "primary",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await finalizeAip(rec.id);
          toast.success("Finalized", `AIP FY${rec.fiscalYear} is now Final.`);
          load();
        } catch (err) {
          toast.error("Failed", aipErrorMessage(err, "Could not finalize record."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  function handleArchive(rec: AipRecordResponse) {
    setConfirm({
      title: "Archive AIP Record",
      message: `Archive AIP FY${rec.fiscalYear}? Archived records are read-only and cannot be used for WFP entry.`,
      confirmLabel: "Archive",
      cancelLabel: "Cancel",
      variant: "danger",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await archiveAip(rec.id);
          toast.success("Archived", `AIP FY${rec.fiscalYear} has been archived.`);
          load();
        } catch (err) {
          toast.error("Failed", aipErrorMessage(err, "Could not archive record."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ---------------------------------------------------------------------------
  // Table columns
  // ---------------------------------------------------------------------------

  const columns: Column<AipRecordResponse>[] = [
    {
      key: "fiscalYear",
      header: "FISCAL YR",
      sortable: true,
      render: (r) => <span className="font-medium">FY {r.fiscalYear}</span>,
      sortValue: (r) => r.fiscalYear,
    },
    {
      key: "entrySource",
      header: "SOURCE",
      render: (r) => <SourceBadge source={r.entrySource} />,
    },
    {
      key: "status",
      header: "STATUS",
      render: (r) => <StatusBadge status={r.status} />,
    },
    {
      key: "officeCount",
      header: "OFFICES",
      align: "right",
      sortable: true,
      render: (r) => r.officeCount,
      sortValue: (r) => r.officeCount,
    },
    {
      key: "ldipId",
      header: "LDIP REF",
      render: (r) => r.ldipId != null ? `LDIP-${r.ldipId}` : "—",
    },
    {
      key: "uploadedByName",
      header: "UPLOADED BY",
      render: (r) => r.uploadedByName ?? "—",
    },
    {
      key: "uploadedAt",
      header: "UPLOADED AT",
      sortable: true,
      render: (r) => formatDate(r.uploadedAt),
      sortValue: (r) => r.uploadedAt,
    },
    {
      key: "actions",
      header: "ACTIONS",
      render: (r) => (
        <div className="flex items-center gap-3 text-sm">
          <Link
            href={`/budget-planning/aip/${r.id}`}
            className="text-green-700 hover:underline"
          >
            View
          </Link>
          {r.status === "Draft" && me?.canUploadAip && (
            <>
              <button
                onClick={() => handleFinalize(r)}
                className="text-slate-600 hover:underline"
              >
                Finalize
              </button>
              <button
                onClick={() => handleArchive(r)}
                className="text-danger-500 hover:underline"
              >
                Archive
              </button>
            </>
          )}
          {r.status === "Final" && (
            <Link
              href={`/budget-planning/wfp?aip=${r.id}`}
              className="text-blue-600 hover:underline"
            >
              WFP
            </Link>
          )}
        </div>
      ),
    },
  ];

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="p-6 max-w-screen-xl mx-auto">
      {/* Header */}
      <div className="flex items-start justify-between mb-5">
        <div>
          <h1 className="text-xl font-bold text-slate-800">Annual Investment Program</h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Yearly investment allocations per sector and office
          </p>
        </div>
        {me?.canUploadAip && (
          <Link
            href="/budget-planning/aip/new"
            className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-800 transition-colors"
          >
            + New AIP
          </Link>
        )}
      </div>

      {/* Filter bar */}
      <div className="flex flex-wrap items-center gap-3 mb-4">
        {/* Fiscal Year */}
        <select
          value={fy}
          onChange={(e) => setFy(Number(e.target.value))}
          className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
        >
          {FY_OPTIONS.map((y) => (
            <option key={y} value={y}>
              FY {y}
            </option>
          ))}
        </select>

        {/* Status */}
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
        >
          <option value="">All Status</option>
          <option value="Draft">Draft</option>
          <option value="Final">Final</option>
          <option value="Archived">Archived</option>
        </select>

        {/* Source (client-side) */}
        <select
          value={sourceFilter}
          onChange={(e) => setSourceFilter(e.target.value)}
          className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
        >
          <option value="">All Sources</option>
          <option value="Upload">Upload</option>
          <option value="Manual">Manual</option>
        </select>
      </div>

      {/* Table */}
      <DataTable
        columns={columns}
        rows={visibleRecords}
        rowKey={(r) => r.id}
        loading={loading}
        error={error}
        onRetry={load}
        emptyMessage="No AIP records found for the selected filters."
        rowNoun={["record", "records"]}
      />

      {/* Confirm dialog */}
      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}
