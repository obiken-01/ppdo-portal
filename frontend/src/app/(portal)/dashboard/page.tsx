"use client";

/**
 * Main Dashboard page — RAL-45.
 * Matches Penpot frame "03 Main Dashboard".
 *
 * Layout:
 *   Top row  — two stat card groups (PR status + Items)
 *   Main row — FullCalendar (left, wide) + ResourceLinksWidget (right, narrow)
 *
 * Data sources:
 *   GET /api/dashboard/stats          → DashboardStats
 *   GET /api/dashboard/events?year&month → CalendarEventResponse[]
 */

import { useCallback, useEffect, useState } from "react";
import api from "@/lib/api";
import type { CalendarEventResponse, DashboardStats } from "@/types";
import DashboardCalendar from "@/components/dashboard/DashboardCalendar";
import ResourceLinksWidget from "@/components/dashboard/ResourceLinksWidget";
import StatCard from "@/components/dashboard/StatCard";

// ── Stat card definitions ───────────────────────────────────────────────────

function PRStatCards({ stats }: { stats: DashboardStats }) {
  return (
    <div className="bg-white rounded-xl border border-slate-200 shadow-sm px-4 py-3">
      <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-2">
        📋 Purchase Requests
      </p>
      <div className="flex gap-3 flex-wrap">
        <StatCard
          label="Total PRs"
          value={stats.totalPRs}
          bgClass="bg-slate-50"
          valueClass="text-slate-700"
        />
        <StatCard
          label="Open"
          value={stats.openPRs}
          bgClass="bg-stat-blue"
          valueClass="text-info-500"
          icon="📂"
        />
        <StatCard
          label="Partial"
          value={stats.partiallyDeliveredPRs}
          bgClass="bg-stat-amber"
          valueClass="text-amber-500"
          icon="🚚"
        />
        <StatCard
          label="Completed"
          value={stats.fullyDeliveredPRs}
          bgClass="bg-stat-green"
          valueClass="text-green-600"
          icon="✅"
        />
      </div>
    </div>
  );
}

function ItemStatCards({ stats }: { stats: DashboardStats }) {
  return (
    <div className="bg-white rounded-xl border border-slate-200 shadow-sm px-4 py-3">
      <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-2">
        📦 Items Master
      </p>
      <div className="flex gap-3 flex-wrap">
        <StatCard
          label="Total Items"
          value={stats.totalItems}
          bgClass="bg-stat-green"
          valueClass="text-green-600"
          icon="🗃️"
        />
        <StatCard
          label="Pending Review"
          value={stats.newItemsPendingReview}
          bgClass="bg-stat-amber"
          valueClass="text-amber-500"
          icon="⭐"
        />
      </div>
    </div>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

export default function DashboardPage() {
  // Stats
  const [stats, setStats]           = useState<DashboardStats | null>(null);
  const [statsLoading, setStatsLoading] = useState(true);

  // Calendar events
  const [events, setEvents]         = useState<CalendarEventResponse[]>([]);
  const [eventsLoading, setEventsLoading] = useState(true);
  const [activeYear, setActiveYear]   = useState(() => new Date().getFullYear());
  const [activeMonth, setActiveMonth] = useState(() => new Date().getMonth() + 1);

  // Selected event detail
  const [selectedEvent, setSelectedEvent] = useState<CalendarEventResponse | null>(null);

  // ── Fetch stats ─────────────────────────────────────────────────────────

  useEffect(() => {
    setStatsLoading(true);
    api.get<DashboardStats>("/dashboard/stats")
      .then(({ data }) => setStats(data))
      .catch(() => {}) // fail silently — stats are supplementary
      .finally(() => setStatsLoading(false));
  }, []);

  // ── Fetch events ─────────────────────────────────────────────────────────

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

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="p-5 space-y-4 h-full flex flex-col">
      {/* Stat cards row */}
      <div className="flex gap-4 flex-wrap shrink-0">
        {statsLoading ? (
          <>
            <div className="flex-1 min-w-[240px] h-20 bg-white rounded-xl border border-slate-200 animate-pulse" />
            <div className="flex-1 min-w-[200px] h-20 bg-white rounded-xl border border-slate-200 animate-pulse" />
          </>
        ) : stats ? (
          <>
            <div className="flex-1 min-w-[240px]"><PRStatCards stats={stats} /></div>
            <div className="flex-1 min-w-[200px]"><ItemStatCards stats={stats} /></div>
          </>
        ) : null}
      </div>

      {/* Calendar + Resource Links */}
      <div className="flex gap-4 flex-1 min-h-0">
        {/* Calendar — takes most of the horizontal space */}
        <div className="flex-1 min-w-0 flex flex-col">
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

      {/* Event detail popover */}
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
                  {selectedEvent.source ? ` · ${selectedEvent.source}` : ""}
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
              {selectedEvent.endDate && selectedEvent.endDate !== selectedEvent.startDate && (
                <> — {new Date(selectedEvent.endDate).toLocaleDateString("en-PH", {
                  month: "long", day: "numeric", timeZone: "Asia/Manila",
                })}</>
              )}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
