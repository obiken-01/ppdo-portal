"use client";

import Link from "next/link";
import type { AppNotification } from "@/types";
import { relativeTime, safeLink, sourceLabel } from "@/lib/notifications";

/**
 * One entry, shared by the dropdown and the full page (v1.9.0 — PPDO-222, spec §6): an unread dot and
 * a tinted background while unread, the frozen text, then the source and a relative time.
 * It is a real link, so middle-click and keyboard work; `onOpen` marks it read.
 */
export default function NotificationRow({
  n, onOpen,
}: {
  n: AppNotification;
  onOpen: (n: AppNotification) => void;
}) {
  const unread = n.readAt == null;
  return (
    <Link
      href={safeLink(n.link)}
      onClick={() => onOpen(n)}
      className={`flex items-start gap-3 px-4 py-3 hover:bg-slate-50 focus:outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-green-600 ${unread ? "bg-green-50" : "bg-white"}`}
    >
      <span
        aria-hidden="true"
        className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${unread ? "bg-green-600" : "bg-transparent"}`}
      />
      <span className="min-w-0 flex-1">
        <span className={`block break-words text-sm ${unread ? "font-medium text-slate-800" : "text-slate-600"}`}>
          {unread && <span className="sr-only">Unread: </span>}
          {n.text}
        </span>
        <span className="mt-0.5 block text-xs text-slate-600">
          {sourceLabel(n)} · {relativeTime(n.createdAt)}
        </span>
      </span>
    </Link>
  );
}

/** Three rows shaped like the loaded list, so opening the dropdown does not jump (CLS). */
export function NotificationSkeleton() {
  return (
    <div aria-hidden="true">
      {[0, 1, 2].map((i) => (
        <div key={i} className="flex items-start gap-3 px-4 py-3">
          <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-slate-100" />
          <span className="flex-1">
            <span className="block h-4 w-4/5 animate-pulse bg-slate-100" />
            <span className="mt-1.5 block h-3 w-1/3 animate-pulse bg-slate-100" />
          </span>
        </div>
      ))}
    </div>
  );
}
