"use client";

/**
 * Main Dashboard page — RAL-45.
 * Matches Penpot frame "03 Main Dashboard".
 *
 * Layout: FullCalendar (left, wide) + ResourceLinksWidget (right, narrow).
 *
 * Stat cards and inventory alerts belong on the Inventory Dashboard (frame 04)
 * and will be built in a later RAL.
 *
 * Data source:
 *   GET /api/dashboard/events?year&month → CalendarEventResponse[]
 */

import { useCallback, useEffect, useState } from "react";
import api from "@/lib/api";
import type { CalendarEventResponse } from "@/types";
import DashboardCalendar from "@/components/dashboard/DashboardCalendar";
import ResourceLinksWidget from "@/components/dashboard/ResourceLinksWidget";

export default function DashboardPage() {
  const [events, setEvents]               = useState<CalendarEventResponse[]>([]);
  const [eventsLoading, setEventsLoading] = useState(true);
  const [activeYear, setActiveYear]       = useState(() => new Date().getFullYear());
  const [activeMonth, setActiveMonth]     = useState(() => new Date().getMonth() + 1);
  const [selectedEvent, setSelectedEvent] = useState<CalendarEventResponse | null>(null);

  // ── Fetch events ───────────────────────────────────────────────────────────

  const fetchEvents = useCallback(async (year: number, month: number) => {
    setEventsLoading(true);
    try {
      const { data } = await api.get<CalendarEventResponse[]>(
        `/dashboard/events?year=${year}&month=${month}`
      );
      setEvents(data);
    } catch {
      setEvents([]);
    } finally {
      setEventsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchEvents(activeYear, activeMonth);
  }, [activeYear, activeMonth, fetchEvents]);

  function handleMonthChange(year: number, month: number) {
    setActiveYear(year);
    setActiveMonth(month);
  }

  // ── Render ─────────────────────────────────────────────────────────────────

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
            className="bg-white rounded-xl shadow-xl max-w-sm w-full p-5"
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
                <h3 className="text-base font-semibold text-slate-800 mt-1.5">
                  {selectedEvent.title}
                </h3>
              </div>
              <button
                onClick={() => setSelectedEvent(null)}
                className="text-slate-400 hover:text-slate-600 text-xl leading-none shrink-0"
              >
                ×
              </button>
            </div>

            {selectedEvent.description && (
              <p className="text-sm text-slate-600 mb-2">{selectedEvent.description}</p>
            )}

            <p className="text-xs text-slate-400">
              {new Date(selectedEvent.startDate).toLocaleDateString("en-PH", {
                weekday: "long", year: "numeric", month: "long", day: "numeric",
                timeZone: "Asia/Manila",
              })}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
