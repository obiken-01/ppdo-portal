"use client";

import { useCallback, useEffect, useState } from "react";
import Modal from "@/components/ui/Modal";
import DataTable, { type Column } from "@/components/ui/DataTable";
import { useToast } from "@/components/ui/Toast";
import { getPendingEvents, reviewCalendarEvent } from "@/lib/dashboard";
import type { PendingCalendarEvent } from "@/types";

interface CalendarApprovalPanelProps {
  open: boolean;
  onClose: () => void;
  /** Called after any approve/reject action so parent can refresh counts + events. */
  onReviewed: () => void;
}

export default function CalendarApprovalPanel({
  open,
  onClose,
  onReviewed,
}: CalendarApprovalPanelProps) {
  const { toast } = useToast();

  const [events, setEvents]           = useState<PendingCalendarEvent[]>([]);
  const [loading, setLoading]         = useState(false);
  const [error, setError]             = useState<string | null>(null);
  // Track which row is in "reject mode" (showing reason input)
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [actioning, setActioning]     = useState<string | null>(null);

  const fetchPending = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await getPendingEvents();
      setEvents(data);
    } catch {
      setError("Failed to load pending events.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (open) fetchPending();
  }, [open, fetchPending]);

  async function handleApprove(id: string) {
    setActioning(id);
    try {
      await reviewCalendarEvent(id, true);
      setEvents((prev) => prev.filter((e) => e.id !== id));
      toast.success("Event approved.");
      onReviewed();
    } catch {
      toast.error("Failed to approve event.");
    } finally {
      setActioning(null);
    }
  }

  async function handleRejectConfirm(id: string) {
    if (!rejectReason.trim()) {
      toast.error("Rejection reason is required.");
      return;
    }
    setActioning(id);
    try {
      await reviewCalendarEvent(id, false, rejectReason.trim());
      setEvents((prev) => prev.filter((e) => e.id !== id));
      setRejectingId(null);
      setRejectReason("");
      toast.success("Event rejected.");
      onReviewed();
    } catch {
      toast.error("Failed to reject event.");
    } finally {
      setActioning(null);
    }
  }

  const columns: Column<PendingCalendarEvent>[] = [
    {
      key: "title",
      header: "Title",
      sortable: true,
      render: (row) => (
        <div>
          <p className="font-medium text-slate-800 text-sm">{row.title}</p>
          {row.description && (
            <p className="text-xs text-slate-500 truncate max-w-xs">{row.description}</p>
          )}
        </div>
      ),
    },
    {
      key: "startDate",
      header: "Date",
      sortable: true,
      sortValue: (row) => row.startDate,
      render: (row) => (
        <span className="text-sm text-slate-700 whitespace-nowrap">
          {new Date(row.startDate).toLocaleDateString("en-PH", {
            year: "numeric",
            month: "short",
            day: "numeric",
            timeZone: "Asia/Manila",
          })}
        </span>
      ),
    },
    {
      key: "createdByName",
      header: "Submitted By",
      sortable: true,
      render: (row) => <span className="text-sm text-slate-700">{row.createdByName}</span>,
    },
    {
      key: "createdAt",
      header: "Submitted",
      sortable: true,
      sortValue: (row) => row.createdAt,
      render: (row) => (
        <span className="text-xs text-slate-500 whitespace-nowrap">
          {new Date(row.createdAt).toLocaleDateString("en-PH", {
            year: "numeric",
            month: "short",
            day: "numeric",
            timeZone: "Asia/Manila",
          })}
        </span>
      ),
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (row) => {
        const isBusy = actioning === row.id;
        if (rejectingId === row.id) {
          return (
            <div className="flex items-center gap-2 justify-end">
              <input
                type="text"
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
                placeholder="Reason for rejection"
                className="border border-slate-300 px-2 py-1 text-xs text-slate-800 focus:outline-none focus:ring-1 focus:ring-red-400 focus:border-red-400 w-48"
                autoFocus
              />
              <button
                onClick={() => handleRejectConfirm(row.id)}
                disabled={isBusy}
                className="px-2 py-1 text-xs bg-red-600 text-white hover:bg-red-500 disabled:opacity-60"
              >
                {isBusy ? "…" : "Confirm"}
              </button>
              <button
                onClick={() => { setRejectingId(null); setRejectReason(""); }}
                className="px-2 py-1 text-xs border border-slate-200 text-slate-600 hover:bg-slate-50"
              >
                Cancel
              </button>
            </div>
          );
        }

        return (
          <div className="flex items-center gap-2 justify-end">
            <button
              onClick={() => handleApprove(row.id)}
              disabled={isBusy || actioning !== null}
              className="px-2 py-1 text-xs bg-green-600 text-white hover:bg-green-500 disabled:opacity-60"
            >
              {isBusy ? "…" : "Approve"}
            </button>
            <button
              onClick={() => { setRejectingId(row.id); setRejectReason(""); }}
              disabled={actioning !== null}
              className="px-2 py-1 text-xs border border-red-300 text-red-600 hover:bg-red-50 disabled:opacity-60"
            >
              Reject
            </button>
          </div>
        );
      },
    },
  ];

  if (!open) return null;

  return (
    <Modal
      title={`Pending Event Approvals${events.length > 0 ? ` (${events.length})` : ""}`}
      size="xl"
      onClose={onClose}
    >
      <DataTable
        columns={columns}
        rows={events}
        rowKey={(r) => r.id}
        loading={loading}
        error={error}
        onRetry={fetchPending}
        emptyMessage="No pending events — all caught up!"
      />
    </Modal>
  );
}
