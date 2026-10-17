"use client";

/**
 * The full notification list (v1.9.0 — PPDO-222, `Notification_Centre_Spec.md` §6): the dropdown's
 * rows, paged, with the same All / Unread chips and "Mark all as read".
 */

import { useCallback, useEffect, useState } from "react";
import {
  PAGE_SIZE, announceNotificationsChanged, getNotifications, markAllNotificationsRead, markNotificationRead,
} from "@/lib/notifications";
import type { AppNotification } from "@/types";
import NotificationRow, { NotificationSkeleton } from "@/components/notifications/NotificationRow";

type Filter = "all" | "unread";

export default function NotificationsPage() {
  const [filter, setFilter] = useState<Filter>("all");
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<AppNotification[] | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState(false);

  const load = useCallback(async () => {
    setError(false);
    setItems(null);
    try {
      const res = await getNotifications(filter === "unread", page, PAGE_SIZE);
      setItems(res.items);
      setTotal(res.total);
    } catch {
      setError(true);
    }
  }, [filter, page]);

  useEffect(() => { void load(); }, [load]);

  function openEntry(n: AppNotification) {
    if (n.readAt != null) return;
    void markNotificationRead(n.id).finally(announceNotificationsChanged);
  }

  async function markAll() {
    try {
      await markAllNotificationsRead();
    } finally {
      announceNotificationsChanged();
      void load();
    }
  }

  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const chip = (f: Filter, label: string) => (
    <button
      type="button"
      onClick={() => { setFilter(f); setPage(1); }}
      aria-pressed={filter === f}
      className={`rounded-full px-3 py-1 text-xs font-medium ${
        filter === f ? "bg-green-600 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"}`}
    >
      {label}
    </button>
  );

  return (
    <div className="min-h-full bg-slate-100">
      <div className="mx-auto max-w-3xl space-y-4 px-3 py-4 sm:px-6 sm:py-6">
        <div>
          <h1 className="text-xl font-bold text-slate-800">Notifications</h1>
          <p className="text-sm text-slate-600">What happened that concerns you, from the last 90 days.</p>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex gap-2">{chip("all", "All")}{chip("unread", "Unread")}</div>
          <button type="button" onClick={() => void markAll()} className="text-sm font-medium text-green-700 hover:underline">
            Mark all as read
          </button>
        </div>

        <div className="divide-y divide-slate-200 border border-slate-200 bg-white">
          {error ? (
            <p role="alert" className="px-4 py-6 text-sm text-slate-600">
              Could not load notifications.{" "}
              <button type="button" onClick={() => void load()} className="font-medium text-green-700 hover:underline">Retry</button>
            </p>
          ) : items === null ? (
            <NotificationSkeleton />
          ) : items.length === 0 ? (
            <p className="px-4 py-8 text-center text-sm text-slate-600">
              {filter === "unread" ? "Nothing unread." : "You're all caught up."}
            </p>
          ) : (
            items.map((n) => <NotificationRow key={n.id} n={n} onOpen={openEntry} />)
          )}
        </div>

        {total > PAGE_SIZE && (
          <div className="flex items-center justify-between text-sm text-slate-600">
            <button
              type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}
              className="border border-slate-300 bg-white px-3 py-1.5 hover:bg-slate-50 disabled:text-slate-400"
            >
              Previous
            </button>
            <span className="tabular-nums">Page {page} of {pages}</span>
            <button
              type="button" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}
              className="border border-slate-300 bg-white px-3 py-1.5 hover:bg-slate-50 disabled:text-slate-400"
            >
              Next
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
