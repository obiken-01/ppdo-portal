/**
 * Notification bell API calls and pure helpers (v1.9.0 — PPDO-222,
 * `docs/v1.9/Notification_Centre_Spec.md`).
 *
 * ⚠️ **A user only ever reads and updates their own rows** — the server answers 404 for anyone
 * else's id, whatever this file sends.
 */

import api from "./api";
import type { ApiResponse, AppNotification, NotificationCount, NotificationPage } from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

/** How often the badge re-reads while the tab is visible (spec decision 7). */
export const POLL_INTERVAL_MS = 60_000;

/** Page size of the dropdown; the full page uses the same. */
export const PAGE_SIZE = 20;

export async function getUnreadCount(): Promise<number> {
  const { data } = await api.get<ApiResponse<NotificationCount>>("/notifications/unread-count");
  return unwrap(data).count;
}

export async function getNotifications(unreadOnly: boolean, page: number, pageSize = PAGE_SIZE): Promise<NotificationPage> {
  const { data } = await api.get<ApiResponse<NotificationPage>>("/notifications", {
    params: { unreadOnly, page, pageSize },
  });
  return unwrap(data);
}

/** Idempotent. */
export async function markNotificationRead(id: number): Promise<void> {
  await api.post(`/notifications/${id}/read`);
}

export async function markAllNotificationsRead(): Promise<void> {
  await api.post("/notifications/read-all");
}

// ── Change signal: the page and the bell share one badge ──────────────────────────────────────────

const CHANGED = "notifications:changed";

/** Tells the bell to re-read its count now (e.g. after the full page marks entries read). */
export function announceNotificationsChanged(): void {
  if (typeof window !== "undefined") window.dispatchEvent(new Event(CHANGED));
}

export function onNotificationsChanged(handler: () => void): () => void {
  window.addEventListener(CHANGED, handler);
  return () => window.removeEventListener(CHANGED, handler);
}

// ── Pure helpers ──────────────────────────────────────────────────────────────────────────────────

/** The badge text: none for 0, the number up to 99, "99+" above (spec §3.2). */
export function badgeLabel(count: number): string | null {
  if (!Number.isFinite(count) || count <= 0) return null;
  return count > 99 ? "99+" : String(Math.floor(count));
}

/** Polls only while the tab is visible (spec decision 7). */
export function shouldPoll(visibility: string): boolean {
  return visibility === "visible";
}

/** "just now", "5 min ago", "2 h ago", "3 d ago", else a short Manila date. */
export function relativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const seconds = Math.floor((now.getTime() - then.getTime()) / 1000);
  if (!Number.isFinite(seconds) || seconds < 45) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days} d ago`;
  return then.toLocaleDateString("en-PH", { timeZone: "Asia/Manila", day: "numeric", month: "short", year: "numeric" });
}

/**
 * The small source line under an entry: "Investment proposal", or "AIP FY 2031" read from the link's
 * `fiscalYear` (the link is the only place the year lives; the text is frozen).
 */
export function sourceLabel(n: Pick<AppNotification, "type" | "link">): string {
  if (n.type.startsWith("Proposal")) return "Investment proposal";
  if (n.type.startsWith("Aip")) {
    const fy = /[?&]fiscalYear=(\d{4})/.exec(n.link)?.[1];
    return fy ? `AIP FY ${fy}` : "AIP";
  }
  return "Notification";
}

/** Only portal paths are followed; anything else falls back to the full page rather than navigating away. */
export function safeLink(link: string): string {
  return link.startsWith("/") && !link.startsWith("//") ? link : "/notifications";
}
