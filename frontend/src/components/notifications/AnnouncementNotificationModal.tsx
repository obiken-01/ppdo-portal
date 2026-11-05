"use client";

/**
 * An announcement opened from the notification bell (PPDO-284): its title, publish date and content,
 * read from the public list the landing page uses. Shaped like the landing page's own modal.
 *
 * ⚠️ The entry outlives the announcement: once it is archived, unpublished or deleted it is no longer
 * in the public list, and the modal says so rather than showing a stale copy.
 */

import { useCallback, useEffect, useState } from "react";
import DOMPurify from "dompurify";
import Modal from "@/components/ui/Modal";
import { getPublishedAnnouncement } from "@/lib/announcements";
import type { AnnouncementPublicDto } from "@/types";

type State =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "missing" }
  | { kind: "loaded"; item: AnnouncementPublicDto };

export default function AnnouncementNotificationModal({ id, onClose }: { id: string; onClose: () => void }) {
  const [state, setState] = useState<State>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const item = await getPublishedAnnouncement(id);
      setState(item ? { kind: "loaded", item } : { kind: "missing" });
    } catch {
      setState({ kind: "error" });
    }
  }, [id]);

  useEffect(() => { void load(); }, [load]);

  return (
    <Modal title={state.kind === "loaded" ? state.item.title : "Announcement"} size="lg" onClose={onClose}>
      {state.kind === "loading" ? (
        <div aria-busy="true" aria-label="Loading announcement" className="space-y-2">
          <div className="mb-4 h-3 w-32 animate-pulse bg-slate-100" />
          <div className="h-4 w-full animate-pulse bg-slate-100" />
          <div className="h-4 w-11/12 animate-pulse bg-slate-100" />
          <div className="h-4 w-3/4 animate-pulse bg-slate-100" />
        </div>
      ) : state.kind === "error" ? (
        <p role="alert" className="text-sm text-slate-600">
          Could not load the announcement.{" "}
          <button type="button" onClick={() => void load()} className="font-medium text-green-700 hover:underline">Retry</button>
        </p>
      ) : state.kind === "missing" ? (
        <p className="text-sm text-slate-600">This announcement is no longer available.</p>
      ) : (
        <>
          <time className="mb-4 block text-xs text-slate-600" dateTime={state.item.publishedAt}>
            {new Date(state.item.publishedAt).toLocaleDateString("en-US", {
              timeZone: "Asia/Manila", month: "long", day: "numeric", year: "numeric",
            })}
          </time>
          <div
            className="announcement-content text-sm leading-relaxed text-slate-600"
            dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(state.item.content) }}
          />
        </>
      )}
    </Modal>
  );
}
