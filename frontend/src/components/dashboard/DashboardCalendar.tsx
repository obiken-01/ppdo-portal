"use client";

/**
 * FullCalendar wrapper for the Main Dashboard.
 *
 * Event colour mapping (PPDO design tokens):
 *   Office   → green-600  #1F7A45  (shared office events)
 *   Personal → info-500   #378ADD  (creator-only events)
 *   Holiday  → amber-500  #EF9F27  (PH public holidays)
 */

import FullCalendar from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/daygrid";
import interactionPlugin from "@fullcalendar/interaction";
import type { EventInput, EventClickArg, DatesSetArg } from "@fullcalendar/core";
import type { CalendarEventResponse } from "@/types";

const EVENT_COLORS: Record<string, { bg: string; border: string }> = {
  Office:   { bg: "#1F7A45", border: "#196638" },
  Personal: { bg: "#378ADD", border: "#1D6FBF" },
  Holiday:  { bg: "#EF9F27", border: "#D4880E" },
};

function toFcEvent(e: CalendarEventResponse): EventInput {
  const color = EVENT_COLORS[e.eventType] ?? EVENT_COLORS.Office;
  return {
    id:              e.id ?? `holiday-${e.title}-${e.startDate}`,
    title:           e.title,
    start:           e.startDate,
    end:             e.endDate ?? undefined,
    allDay:          e.isAllDay,
    backgroundColor: color.bg,
    borderColor:     color.border,
    textColor:       "#ffffff",
    extendedProps:   { eventType: e.eventType, description: e.description, source: e.source },
  };
}

interface DashboardCalendarProps {
  events: CalendarEventResponse[];
  loading: boolean;
  /** Fires when the visible month changes so the parent can refetch events. */
  onMonthChange: (year: number, month: number) => void;
  onEventClick?: (event: CalendarEventResponse) => void;
}

export default function DashboardCalendar({
  events,
  loading,
  onMonthChange,
  onEventClick,
}: DashboardCalendarProps) {
  function handleDatesSet(arg: DatesSetArg) {
    // FullCalendar passes the first day of the current view.
    const d = arg.view.currentStart;
    onMonthChange(d.getFullYear(), d.getMonth() + 1);
  }

  function handleEventClick(arg: EventClickArg) {
    if (!onEventClick) return;
    const ep = arg.event.extendedProps as { eventType: string; description: string | null; source: string | null };
    onEventClick({
      id:          arg.event.id.startsWith("holiday-") ? null : arg.event.id,
      title:       arg.event.title,
      description: ep.description ?? null,
      startDate:   arg.event.startStr,
      endDate:     arg.event.endStr || null,
      isAllDay:    arg.event.allDay,
      eventType:   ep.eventType,
      source:      ep.source ?? null,
    });
  }

  return (
    <div className="relative bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
      {/* Loading overlay */}
      {loading && (
        <div className="absolute inset-0 bg-white/70 z-10 flex items-center justify-center rounded-xl">
          <div className="w-6 h-6 border-3 border-green-600 border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      {/* Legend */}
      <div className="flex items-center gap-4 px-4 pt-3 pb-1">
        {Object.entries(EVENT_COLORS).map(([type, color]) => (
          <div key={type} className="flex items-center gap-1.5">
            <span className="w-3 h-3 rounded-sm inline-block" style={{ backgroundColor: color.bg }} />
            <span className="text-xs text-slate-500">{type}</span>
          </div>
        ))}
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
          height="auto"
          dayMaxEvents={3}
          eventDisplay="block"
        />
      </div>
    </div>
  );
}
