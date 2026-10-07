"use client";

import type { RecentActivity } from "@/types";

/**
 * RecentActivityList — the dashboard's Recent activity band body (PPDO-181, finding F5).
 *
 * Sentences grouped by day — Today, Yesterday, then the date — with the time as `h:mm a`. All of it
 * in Manila time: the server sends UTC, and a reader in Occidental Mindoro asks "what happened
 * today?" by the clock on their wall. No table name, action code or record id is ever shown; the
 * server writes the sentence (`RecentActivityDescriber`).
 */

const TZ = "Asia/Manila";

/** The Manila calendar day of an instant, as YYYY-MM-DD — a stable key to group and compare on. */
const dayKey = (d: Date): string =>
  new Intl.DateTimeFormat("en-CA", { timeZone: TZ, year: "numeric", month: "2-digit", day: "2-digit" }).format(d);

const timeLabel = (d: Date): string =>
  d.toLocaleTimeString("en-US", { timeZone: TZ, hour: "numeric", minute: "2-digit", hour12: true });

const dateLabel = (d: Date): string =>
  d.toLocaleDateString("en-US", { timeZone: TZ, month: "short", day: "numeric", year: "numeric" });

interface Group {
  key: string;
  label: string;
  entries: RecentActivity[];
}

/** Groups newest-first entries by Manila day. `now` is a parameter so the grouping is testable. */
export function groupByDay(entries: RecentActivity[], now: Date = new Date()): Group[] {
  const today = dayKey(now);
  const yesterday = dayKey(new Date(now.getTime() - 24 * 60 * 60 * 1000));
  const groups: Group[] = [];

  for (const entry of entries) {
    const when = new Date(entry.changedAt);
    const key = dayKey(when);
    let group = groups.find((g) => g.key === key);
    if (!group) {
      group = {
        key,
        label: key === today ? "Today" : key === yesterday ? "Yesterday" : dateLabel(when),
        entries: [],
      };
      groups.push(group);
    }
    group.entries.push(entry);
  }
  return groups;
}

export default function RecentActivityList({ entries }: { entries: RecentActivity[] }) {
  const groups = groupByDay(entries);

  return (
    <div>
      {groups.map((group) => (
        <div key={group.key}>
          <h3 className="px-5 py-1.5 bg-slate-50 border-b border-slate-100 text-xs font-semibold uppercase tracking-wide text-slate-600">
            {group.label}
          </h3>
          <ul className="divide-y divide-slate-50">
            {group.entries.map((entry) => (
              <li key={entry.id} className="px-5 py-3 flex items-start justify-between gap-4">
                <p className="text-sm text-slate-600">
                  <span className="font-medium text-slate-800">{entry.actorName}</span>{" "}
                  {entry.description}
                </p>
                <span className="text-xs text-slate-500 whitespace-nowrap shrink-0 tabular-nums">
                  {timeLabel(new Date(entry.changedAt))}
                </span>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  );
}
