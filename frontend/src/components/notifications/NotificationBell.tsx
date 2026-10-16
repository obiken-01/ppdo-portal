"use client";

/**
 * The notification bell in the top bar (v1.9.0 — PPDO-222, `Notification_Centre_Spec.md` §6).
 *
 * A button with an unread badge ("99+" cap). It opens a dropdown of entries — newest first, All /
 * Unread chips, "Mark all as read", "See all notifications". Clicking an entry marks it read and
 * navigates to its link; the dropdown closes. Escape and an outside click close it too.
 *
 * ⚠️ **The count is polled, never pushed** (see `useUnreadCount`), and a failed poll is silent. A
 * failed LIST read is not: the dropdown says so and offers Retry. Opening an entry never waits on the
 * mark-read call — the navigation must not hang on it — so a failed mark-read only leaves the entry
 * unread until the next read.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import {
  PAGE_SIZE, badgeLabel, getNotifications, markAllNotificationsRead, markNotificationRead,
} from "@/lib/notifications";
import type { AppNotification } from "@/types";
import NotificationRow, { NotificationSkeleton } from "./NotificationRow";
import { useUnreadCount } from "./useUnreadCount";

type Filter = "all" | "unread";

export default function NotificationBell() {
  const { count, setCount, refresh } = useUnreadCount();
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState<Filter>("all");
  const [items, setItems] = useState<AppNotification[] | null>(null);
  const [error, setError] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  const load = useCallback(async (f: Filter) => {
    setError(false);
    setItems(null);
    try {
      setItems((await getNotifications(f === "unread", 1, PAGE_SIZE)).items);
    } catch {
      setError(true);
    }
  }, []);

  useEffect(() => {
    if (open) void load(filter);
  }, [open, filter, load]);

  // Escape (focus back to the bell) and an outside click close the dropdown.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") { setOpen(false); buttonRef.current?.focus(); }
    };
    const onDown = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onDown);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onDown);
    };
  }, [open]);

  function openEntry(n: AppNotification) {
    setOpen(false);
    if (n.readAt != null) return;
    // Optimistic: the badge drops at once; the server call follows and the next poll corrects any drift.
    setItems((prev) => prev?.map((x) => (x.id === n.id ? { ...x, readAt: new Date().toISOString() } : x)) ?? null);
    setCount(Math.max(0, count - 1));
    void markNotificationRead(n.id).then(refresh, refresh);
  }

  async function markAll() {
    setItems((prev) => prev?.map((x) => (x.readAt == null ? { ...x, readAt: new Date().toISOString() } : x)) ?? null);
    setCount(0);
    try {
      await markAllNotificationsRead();
    } finally {
      void refresh();
    }
    if (filter === "unread") setItems([]);
  }

  const badge = badgeLabel(count);
  const chip = (f: Filter, label: string) => (
    <button
      type="button"
      onClick={() => setFilter(f)}
      aria-pressed={filter === f}
      className={`rounded-full px-3 py-1 text-xs font-medium ${
        filter === f ? "bg-green-600 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"}`}
    >
      {label}
    </button>
  );

  return (
    <div ref={wrapRef} className="relative">
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={badge ? `Notifications, ${count} unread` : "Notifications"}
        className="relative flex h-11 w-11 items-center justify-center text-slate-600 transition-colors hover:bg-slate-100 sm:h-9 sm:w-9"
      >
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
          <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
        </svg>
        {badge && (
          <span className="absolute right-0.5 top-0.5 min-w-[1.1rem] rounded-full bg-danger-500 px-1 text-center text-[0.65rem] font-semibold leading-[1.1rem] text-white tabular-nums">
            {badge}
          </span>
        )}
      </button>

      {open && (
        <div
          role="region"
          aria-label="Notifications"
          className="fixed inset-x-0 top-14 z-40 border border-slate-200 bg-white shadow-lg sm:absolute sm:inset-x-auto sm:right-0 sm:top-full sm:mt-1 sm:w-[24rem]"
        >
          <div className="flex items-center justify-between gap-2 border-b border-slate-200 px-4 py-2">
            <h2 className="text-sm font-semibold text-slate-800">Notifications</h2>
            <button
              type="button"
              onClick={() => void markAll()}
              disabled={count === 0}
              className="text-xs font-medium text-green-700 hover:underline disabled:text-slate-400 disabled:no-underline"
            >
              Mark all as read
            </button>
          </div>
          <div className="flex gap-2 border-b border-slate-200 px-4 py-2">
            {chip("all", "All")}
            {chip("unread", "Unread")}
          </div>

          <div className="max-h-[min(28rem,70vh)] overflow-y-auto divide-y divide-slate-200">
            {error ? (
              <p role="alert" className="px-4 py-6 text-sm text-slate-600">
                Could not load notifications.{" "}
                <button type="button" onClick={() => void load(filter)} className="font-medium text-green-700 hover:underline">Retry</button>
              </p>
            ) : items === null ? (
              <NotificationSkeleton />
            ) : items.length === 0 ? (
              <p className="px-4 py-6 text-center text-sm text-slate-600">
                {filter === "unread" ? "Nothing unread." : "You're all caught up."}
              </p>
            ) : (
              items.map((n) => <NotificationRow key={n.id} n={n} onOpen={openEntry} />)
            )}
          </div>

          <div className="border-t border-slate-200 px-4 py-2 text-center">
            <Link href="/notifications" onClick={() => setOpen(false)} className="text-xs font-medium text-green-700 hover:underline">
              See all notifications
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
