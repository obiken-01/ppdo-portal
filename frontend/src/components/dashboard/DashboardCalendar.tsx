"use client";

import FullCalendar from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/daygrid";
import interactionPlugin from "@fullcalendar/interaction";
import type { EventInput, EventClickArg, DatesSetArg } from "@fullcalendar/core";
import type { CalendarEventResponse } from "@/types";

const EVENT_COLORS: Record<string, { bg: string; border: string }> = {
  Office:   { bg: "#1F7A45", border: "#196638" },
  Pending:  { bg: "#f97316", border: "#ea580c" },
  Rejected: { bg: "#ef4444", border: "#dc2626" },
  Personal: { bg: "#378ADD", border: "#1D6FBF" },
  Holiday:  { bg: "#EF9F27", border: "#D4880E" },
};

// Legend shows categories + approval sub-states for Office events
const LEGEND_ENTRIES: Array<{ key: string; label: string }> = [
  { key: "Office",   label: "Office (Approved)" },
  { key: "Pending",  label: "Pending" },
  { key: "Rejected", label: "Rejected" },
  { key: "Personal", label: "Personal" },
  { key: "Holiday",  label: "Holiday" },
];

function colorKey(e: CalendarEventResponse): string {
  if (e.eventType === "Office") {
    if (e.status === "Pending") return "Pending";
    if (e.status === "Rejected") return "Rejected";
  }
  return e.eventType;
}

function toFcEvent(e: CalendarEventResponse): EventInput {
  const color = EVENT_COLORS[colorKey(e)] ?? EVENT_COLORS.Office;
  return {
    id:              e.id ?? `holiday-${e.title}-${e.startDate}`,
    title:           e.title,
    start:           e.startDate,
    end:             e.endDate ?? undefined,
    allDay:          e.isAllDay,
    backgroundColor: color.bg,
    borderColor:     color.border,
    textColor:       "#ffffff",
    extendedProps:   {
      eventType:       e.eventType,
      description:     e.description,
      source:          e.source,
      status:          e.status,
      rejectionReason: e.rejectionReason,
    },
  };
}

interface DashboardCalendarProps {
  events: CalendarEventResponse[];
  loading: boolean;
  /** Fires when the visible month changes so the parent can refetch events. */
  onMonthChange: (year: number, month: number) => void;
  onEventClick?: (event: CalendarEventResponse) => void;
  /** Fires when the user clicks an empty calendar date cell. */
  onDateClick?: (dateStr: string) => void;
  /** Optional content rendered on the right side of the legend bar (e.g. pending chip). */
  headerRight?: React.ReactNode;
}

export default function DashboardCalendar({
  events,
  loading,
  onMonthChange,
  onEventClick,
  onDateClick,
  headerRight,
}: DashboardCalendarProps) {
  function handleDatesSet(arg: DatesSetArg) {
    const d = arg.view.currentStart;
    onMonthChange(d.getFullYear(), d.getMonth() + 1);
  }

  function handleEventClick(arg: EventClickArg) {
    if (!onEventClick) return;
    const ep = arg.event.extendedProps as {
      eventType: string;
      description: string | null;
      source: string | null;
      status: "Pending" | "Approved" | "Rejected" | null;
      rejectionReason: string | null;
    };
    onEventClick({
      id:              arg.event.id.startsWith("holiday-") ? null : arg.event.id,
      title:           arg.event.title,
      description:     ep.description ?? null,
      startDate:       arg.event.startStr,
      endDate:         arg.event.endStr || null,
      isAllDay:        arg.event.allDay,
      eventType:       ep.eventType,
      source:          ep.source ?? null,
      status:          ep.status,
      rejectionReason: ep.rejectionReason,
    });
  }

  function handleDateClick(arg: { dateStr: string }) {
    onDateClick?.(arg.dateStr);
  }

  return (
    <div className="relative bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
      {/* Loading overlay */}
      {loading && (
        <div className="absolute inset-0 bg-white/70 z-10 flex items-center justify-center rounded-xl">
          <div className="w-6 h-6 border-3 border-green-600 border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      {/* Legend + optional header-right slot */}
      <div className="flex items-center justify-between gap-4 px-4 pt-3 pb-1">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
          {LEGEND_ENTRIES.map(({ key, label }) => (
            <div key={key} className="flex items-center gap-1.5">
              <span className="w-3 h-3 rounded-sm inline-block" style={{ backgroundColor: EVENT_COLORS[key].bg }} />
              <span className="text-xs text-slate-500">{label}</span>
            </div>
          ))}
        </div>
        {headerRight && <div className="shrink-0">{headerRight}</div>}
      </div>

      {/* Calendar */}
      <div className="px-3 pb-3 fc-ppdo">
        <FullCalendar
          plugins={[dayGridPlugin, interactionPlugin]}
          initialView="dayGridMonth"
          headerToolbar={{
            left:   "prev,next today",
            center: "title",
            right:  "",
          }}
          events={events.map(toFcEvent)}
          datesSet={handleDatesSet}
          eventClick={handleEventClick}
          dateClick={handleDateClick}
          height="auto"
          dayMaxEvents={3}
          eventDisplay="block"
        />
      </div>
    </div>
  );
}
