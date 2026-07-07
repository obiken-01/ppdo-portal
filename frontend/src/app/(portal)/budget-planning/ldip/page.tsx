"use client";

import { Suspense, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { archiveLdip, finalizeLdip, ldipErrorMessage, listLdip, unlockLdip } from "@/lib/ldip";
import { useMe } from "@/lib/me-cache";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type { LdipRecord, LdipStatus } from "@/types";

// ---------------------------------------------------------------------------
// Local helpers
// ---------------------------------------------------------------------------

function StatusBadge({ status }: { status: LdipStatus }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

function LdipListInner() {
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const me = useMe((m) => m.canAccessBudgetPlanning);
  const [records, setRecords] = useState<LdipRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [modeFilter, setModeFilter] = useState("");
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // Office carried from the dashboard nav (?officeId=). Office users are always
  // scoped server-side regardless of this param.
  const urlOfficeId = searchParams.get("officeId");
  const officeFilter = urlOfficeId != null ? Number(urlOfficeId) : undefined;

  // ── Data ──────────────────────────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setRecords(await listLdip({ officeId: officeFilter }));
    } catch (err) {
      setError(ldipErrorMessage(err, "Failed to load LDIP records."));
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [officeFilter]);

  useEffect(() => {
    load();
  }, [load]);

  // ── Client-side filter ────────────────────────────────────────────────────

  const visibleRecords = useMemo(() => {
    let result = records;
    if (search.trim()) {
      const q = search.trim().toLowerCase();
      result = result.filter(
        (r) =>
          r.title.toLowerCase().includes(q) ||
          r.refCode.toLowerCase().includes(q)
      );
    }
    if (statusFilter) result = result.filter((r) => r.status === statusFilter);
    if (modeFilter) result = result.filter((r) => r.entryMode === modeFilter);
    return result;
  }, [records, search, statusFilter, modeFilter]);

  // ── Handlers ─────────────────────────────────────────────────────────────

  function handleArchive(r: LdipRecord) {
    setConfirm({
      title: "Archive LDIP",
      message: `Archive ${r.refCode}? The record will be marked as Archived.`,
      confirmLabel: "Archive",
      cancelLabel: "Cancel",
      variant: "danger",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await archiveLdip(r.id);
          toast.success("Archived", `${r.refCode} has been archived.`);
          load();
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not archive LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  function handleFinalize(r: LdipRecord) {
    // The primary way to finalize is the edit form's ribbon — this is the fallback
    // for uploaded multi-office records (RAL-113), which are always read-only in
    // that form since there's no single office to edit against (mirrors AIP's own
    // list-level Finalize action, since AIP has no manual edit form at all).
    setConfirm({
      title: "Finalize LDIP",
      message: `Finalize ${r.refCode}? Once finalized, it is locked and can only be unlocked by an admin.`,
      confirmLabel: "Finalize",
      cancelLabel: "Cancel",
      variant: "primary",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await finalizeLdip(r.id);
          toast.success("Finalized", `${r.refCode} is now Final.`);
          load();
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not finalize LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  function handleUnlock(r: LdipRecord) {
    setConfirm({
      title: "Unlock LDIP",
      message: `Unlock ${r.refCode} to allow further editing?`,
      confirmLabel: "Unlock",
      cancelLabel: "Cancel",
      variant: "warning",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await unlockLdip(r.id);
          toast.success("Unlocked", `${r.refCode} is now editable.`);
          load();
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not unlock LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ── Columns ───────────────────────────────────────────────────────────────

  const columns: Column<LdipRecord>[] = useMemo(() => {
    const isAdmin =
      me?.role === "Admin" || me?.role === "SuperAdmin";

    return [
      {
        key: "refCode",
        header: "REF CODE",
        sortable: true,
        render: (r) => (
          <span className="font-mono text-slate-600 whitespace-nowrap">{r.refCode}</span>
        ),
      },
      {
        key: "title",
        header: "TITLE",
        sortable: true,
        render: (r) => (
          <span className="block max-w-xs truncate" title={r.title}>
            {r.title}
          </span>
        ),
      },
      {
        key: "officeName",
        header: "OFFICE",
        sortable: true,
        render: (r) => {
          const label = r.officeName ?? (r.entryMode === "Upload" ? "All Offices" : "—");
          return (
            <span className="block max-w-[180px] truncate" title={label}>
              {label}
            </span>
          );
        },
      },
      {
        key: "period",
        header: "PERIOD",
        sortable: true,
        sortValue: (r) => r.fiscalYearStart,
        render: (r) => (
          <span className="whitespace-nowrap">
            {r.fiscalYearStart}–{r.fiscalYearEnd}
          </span>
        ),
      },
      {
        key: "programCount",
        header: "PROGRAMS",
        sortable: true,
        align: "right",
        render: (r) => <span className="tabular-nums">{r.programCount}</span>,
      },
      {
        key: "entryMode",
        header: "ENTRY MODE",
        render: (r) => r.entryMode,
      },
      {
        key: "status",
        header: "STATUS",
        render: (r) => <StatusBadge status={r.status} />,
      },
      {
        key: "createdAt",
        header: "CREATED",
        sortable: true,
        render: (r) => fmtDate(r.createdAt),
        sortValue: (r) => r.createdAt,
      },
      {
        key: "actions",
        header: "ACTIONS",
        render: (r) => (
          <div className="flex items-center gap-3 text-sm">
            {r.status === "Draft" && (
              <Link
                href={`/budget-planning/ldip/edit?id=${r.id}`}
                className="text-green-700 hover:underline"
              >
                Edit
              </Link>
            )}
            {r.status !== "Draft" && (
              <Link
                href={`/budget-planning/ldip/edit?id=${r.id}`}
                className="text-slate-600 hover:underline"
              >
                View
              </Link>
            )}
            {r.status === "Draft" && (
              <button
                onClick={() => handleFinalize(r)}
                className="text-green-700 hover:underline"
              >
                Finalize
              </button>
            )}
            {r.status === "Draft" && (
              <button
                onClick={() => handleArchive(r)}
                className="text-red-500 hover:underline"
              >
                Archive
              </button>
            )}
            {r.status === "Final" && isAdmin && (
              <button
                onClick={() => handleUnlock(r)}
                className="text-amber-600 hover:underline"
              >
                Unlock
              </button>
            )}
          </div>
        ),
      },
    ];
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [me]);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="p-6 max-w-screen-xl mx-auto">
      {/* Header */}
      <div className="flex items-start justify-between mb-5">
        <div>
          <h1 className="text-xl font-bold text-slate-800">
            Local Development Investment Program
          </h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Multi-year investment planning records
          </p>
        </div>
        <Link
          href="/budget-planning/ldip/new"
          className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-800 transition-colors"
        >
          + New LDIP
        </Link>
      </div>

      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-3 mb-4">
        <input
          type="text"
          placeholder="Search by title or ref code…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="border border-slate-300 bg-white text-sm px-3 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 w-64"
        />
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
        <select
          value={modeFilter}
          onChange={(e) => setModeFilter(e.target.value)}
          className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
        >
          <option value="">All Entry Modes</option>
          <option value="New">New</option>
          <option value="Amendment">Amendment</option>
          <option value="Supplemental">Supplemental</option>
          <option value="Upload">Upload</option>
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
        emptyMessage="No LDIP records found for the selected filters."
      />

      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}

// useSearchParams requires a Suspense boundary during prerender (Next.js app router).
export default function LdipListPage() {
  return (
    <Suspense fallback={null}>
      <LdipListInner />
    </Suspense>
  );
}
