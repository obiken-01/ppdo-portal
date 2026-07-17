"use client";

import { useCallback, useEffect, useState } from "react";
import api from "@/lib/api";
import { useMe } from "@/lib/me-cache";
import { getPendingEvents, deleteCalendarEvent } from "@/lib/dashboard";
import type { CalendarEventResponse } from "@/types";
import DashboardCalendar from "@/components/dashboard/DashboardCalendar";
import ResourceLinksWidget from "@/components/dashboard/ResourceLinksWidget";
import CalendarApprovalPanel from "@/components/dashboard/CalendarApprovalPanel";
import CreateEventModal from "@/components/dashboard/CreateEventModal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";

// Module-level SPA cache: survives route changes within the same tab.
// Keys are "yyyy-m". First visit populates; return visits show stale data
// immediately while a background refetch updates it silently.
const eventsCache = new Map<string, CalendarEventResponse[]>();

export default function DashboardPage() {
  const initYear  = new Date().getFullYear();
  const initMonth = new Date().getMonth() + 1;
  const initKey   = `${initYear}-${initMonth}`;

  const [events, setEvents]               = useState<CalendarEventResponse[]>(() => eventsCache.get(initKey) ?? []);
  const [eventsLoading, setEventsLoading] = useState(() => !eventsCache.has(initKey));
  const [activeYear, setActiveYear]       = useState(() => new Date().getFullYear());
  const [activeMonth, setActiveMonth]     = useState(() => new Date().getMonth() + 1);
  const [selectedEvent, setSelectedEvent] = useState<CalendarEventResponse | null>(null);
  const [editingEvent, setEditingEvent]   = useState<CalendarEventResponse | null>(null);
  const [dialog, setDialog]               = useState<ConfirmDialogProps | null>(null);
  const { toast } = useToast();

  // Dashboard has no special access requirement -- every authenticated portal user lands
  // here, so the permission check always passes. Reads the shared cached /auth/me instead
  // of fetching it separately (RAL-168).
  const me = useMe(() => true);
  const [pendingCount, setPendingCount]     = useState(0);
  const [approvalPanelOpen, setApprovalPanelOpen] = useState(false);
  const [createModalDate, setCreateModalDate]     = useState<string | null>(null);

  const isAdmin = me?.role === "Admin" || me?.role === "SuperAdmin";

  // ── Fetch events ───────────────────────────────────────────────────────────

  const fetchEvents = useCallback(async (year: number, month: number) => {
    const key = `${year}-${month}`;
    const cached = eventsCache.get(key);

    if (cached) {
      // Return visit: show stale data instantly, then refetch silently.
      setEvents(cached);
    } else {
      setEventsLoading(true);
    }

    try {
      const { data } = await api.get<CalendarEventResponse[]>(
        `/dashboard/events?year=${year}&month=${month}`
      );
      eventsCache.set(key, data);
      setEvents(data);
    } catch {
      if (!cached) setEvents([]);
    } finally {
      if (!cached) setEventsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchEvents(activeYear, activeMonth);
  }, [activeYear, activeMonth, fetchEvents]);

  // ── Pending count (admin only) ─────────────────────────────────────────────

  const refreshPendingCount = useCallback(async () => {
    if (!isAdmin) {
      setPendingCount(0);
      return;
    }
    try {
      const pending = await getPendingEvents();
      setPendingCount(pending.length);
    } catch {
      setPendingCount(0);
    }
  }, [isAdmin]);

  useEffect(() => {
    refreshPendingCount();
  }, [refreshPendingCount]);

  // ── Handlers ───────────────────────────────────────────────────────────────

  function handleMonthChange(year: number, month: number) {
    setActiveYear(year);
    setActiveMonth(month);
  }

  function handleReviewed() {
    refreshPendingCount();
    fetchEvents(activeYear, activeMonth);
  }

  function handleCreated() {
    setCreateModalDate(null);
    fetchEvents(activeYear, activeMonth);
    refreshPendingCount();
  }

  function handleEdited() {
    setEditingEvent(null);
    fetchEvents(activeYear, activeMonth);
    refreshPendingCount();
  }

  function handleDeleteClick(event: CalendarEventResponse) {
    if (!event.id) return;
    setDialog({
      title: "Delete this event?",
      message: `"${event.title}" will be permanently deleted. This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteCalendarEvent(event.id!);
          toast.success("Event deleted.");
          setSelectedEvent(null);
          fetchEvents(activeYear, activeMonth);
          refreshPendingCount();
        } catch {
          toast.error("Failed to delete event.");
        }
      },
      onClose: () => setDialog(null),
    });
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  const pendingChip = pendingCount > 0 ? (
    isAdmin ? (
      <button
        onClick={() => setApprovalPanelOpen(true)}
        className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium
                   bg-amber-100 text-amber-700 hover:bg-amber-200 transition-colors rounded"
      >
        ⏳ {pendingCount} event{pendingCount !== 1 ? "s" : ""} awaiting review
      </button>
    ) : (
      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs
                       bg-amber-50 text-amber-600 rounded">
        ⏳ {pendingCount} event{pendingCount !== 1 ? "s" : ""} pending approval
      </span>
    )
  ) : null;

  return (
    <div className="p-5 h-full flex flex-col">
      {/* Calendar + Resource Links */}
      <div className="flex gap-4 flex-1 min-h-0">
        {/* Calendar — takes most of the horizontal space */}
        <div className="flex-1 min-w-0">
          <DashboardCalendar
            events={events}
            loading={eventsLoading}
            onMonthChange={handleMonthChange}
            onEventClick={setSelectedEvent}
            onDateClick={(dateStr) => setCreateModalDate(dateStr)}
            headerRight={pendingChip}
          />
        </div>

        {/* Resource Links — narrow right panel */}
        <div className="w-60 shrink-0">
          <ResourceLinksWidget />
        </div>
      </div>

      {/* Event detail modal */}
      {selectedEvent && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4"
          onClick={() => setSelectedEvent(null)}
        >
          <div
            className="bg-white shadow-xl max-w-sm w-full p-5 border border-slate-200"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-start justify-between gap-2 mb-3">
              <div>
                <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${
                  selectedEvent.eventType === "Holiday"
                    ? "bg-amber-100 text-amber-500"
                    : selectedEvent.eventType === "Personal"
                    ? "bg-info-100 text-info-500"
                    : "bg-green-100 text-green-700"
                }`}>
                  {selectedEvent.eventType}
                </span>

                {/* Approval status badge */}
                {selectedEvent.status === "Pending" && (
                  <span className="ml-1.5 text-xs font-medium px-2 py-0.5 rounded-full bg-orange-100 text-orange-700">
                    Awaiting admin approval
                  </span>
                )}
                {selectedEvent.status === "Rejected" && (
                  <span className="ml-1.5 text-xs font-medium px-2 py-0.5 rounded-full bg-red-100 text-red-700">
                    Rejected
                  </span>
                )}

                <h3 className="text-base font-semibold text-slate-800 mt-1.5">
                  {selectedEvent.title}
                </h3>
              </div>
              <button
                onClick={() => setSelectedEvent(null)}
                className="text-slate-600 hover:text-slate-600 text-xl leading-none shrink-0"
              >
                ×
              </button>
            </div>

            {selectedEvent.description && (
              <p className="text-sm text-slate-600 mb-2">{selectedEvent.description}</p>
            )}

            {/* Rejection reason */}
            {selectedEvent.status === "Rejected" && selectedEvent.rejectionReason && (
              <div className="mb-3 p-2.5 bg-red-50 border border-red-200">
                <p className="text-xs font-medium text-red-700 mb-0.5">Reason</p>
                <p className="text-xs text-red-600">{selectedEvent.rejectionReason}</p>
              </div>
            )}

            <div className="flex items-center justify-between gap-2">
              <p className="text-xs text-slate-600">
                {new Date(selectedEvent.startDate).toLocaleDateString("en-PH", {
                  weekday: "long", year: "numeric", month: "long", day: "numeric",
                  timeZone: "Asia/Manila",
                })}
              </p>
              {/* Owner-only edit/delete — no admin override (RAL-168) */}
              {me != null && selectedEvent.createdById === me.userId && (
                <div className="flex items-center gap-3 shrink-0">
                  <button
                    onClick={() => {
                      setEditingEvent(selectedEvent);
                      setSelectedEvent(null);
                    }}
                    className="text-xs font-medium text-green-600 hover:underline"
                  >
                    Edit
                  </button>
                  <button
                    onClick={() => handleDeleteClick(selectedEvent)}
                    className="text-xs font-medium text-danger-500 hover:underline"
                  >
                    Delete
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Admin: event approval panel */}
      <CalendarApprovalPanel
        open={approvalPanelOpen}
        onClose={() => setApprovalPanelOpen(false)}
        onReviewed={handleReviewed}
      />

      {/* Create event modal — triggered by date click */}
      {createModalDate && (
        <CreateEventModal
          open={true}
          initialDate={createModalDate}
          isAdmin={isAdmin}
          onClose={() => setCreateModalDate(null)}
          onSaved={handleCreated}
        />
      )}

      {/* Edit event modal — triggered by the owner-only Edit button above */}
      {editingEvent && (
        <CreateEventModal
          open={true}
          initialDate={editingEvent.startDate.slice(0, 10)}
          isAdmin={isAdmin}
          editingEvent={editingEvent}
          onClose={() => setEditingEvent(null)}
          onSaved={handleEdited}
        />
      )}

      {/* Delete confirmation — triggered by the owner-only Delete button above */}
      {dialog && <ConfirmDialog {...dialog} />}
    </div>
  );
}
