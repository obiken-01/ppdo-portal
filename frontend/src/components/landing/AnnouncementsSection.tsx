"use client";

/**
 * AnnouncementsSection — public landing page feed (RAL-87).
 *
 * Fetches GET /api/announcements with NO Authorization header (public endpoint).
 * Uses a plain axios call — NOT the api.ts instance which auto-attaches Bearer tokens.
 *
 * States:
 *   loading → 3-bar skeleton
 *   empty   → empty-state card (preserved from the old placeholder)
 *   error   → silently shows empty state
 *   loaded  → one flat card per announcement
 */

import { useEffect, useState } from "react";
import axios from "axios";
import DOMPurify from "dompurify";
import Modal from "@/components/ui/Modal";
import type { AnnouncementPublicDto } from "@/types";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

export default function AnnouncementsSection() {
  const [items, setItems]       = useState<AnnouncementPublicDto[]>([]);
  const [loading, setLoading]   = useState(true);
  const [warming, setWarming]   = useState(false);
  const [selected, setSelected] = useState<AnnouncementPublicDto | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    // After 5 s still loading → show "server waking up" hint in skeleton.
    const warmTimer  = setTimeout(() => setWarming(true), 5_000);
    // After 15 s give up entirely → fall through to empty state silently.
    const abortTimer = setTimeout(() => controller.abort(), 15_000);

    axios
      .get<AnnouncementPublicDto[]>(`${API_BASE}/announcements`, {
        signal: controller.signal,
      })
      .then((r) => setItems(r.data))
      .catch(() => {})
      .finally(() => {
        clearTimeout(warmTimer);
        clearTimeout(abortTimer);
        setLoading(false);
      });

    return () => {
      controller.abort();
      clearTimeout(warmTimer);
      clearTimeout(abortTimer);
    };
  }, []);

  return (
    <section id="announcements" className="bg-slate-100">
      <div className="max-w-6xl mx-auto px-6 py-10">
        <h2 className="text-xl font-bold text-slate-800 mb-5">Announcements</h2>

        {loading ? (
          <SkeletonBars warming={warming} />
        ) : items.length === 0 ? (
          <EmptyState />
        ) : (
          <div className="space-y-4">
            {items.map((item) => (
              <AnnouncementCard key={item.id} item={item} onReadMore={setSelected} />
            ))}
          </div>
        )}
      </div>

      {selected && (
        <Modal title={selected.title} size="lg" onClose={() => setSelected(null)}>
          <time className="block text-xs text-slate-400 mb-4" dateTime={selected.publishedAt}>
            {new Date(selected.publishedAt).toLocaleDateString("en-US", {
              month: "long", day: "numeric", year: "numeric",
            })}
          </time>
          <div
            className="text-sm text-slate-600 leading-relaxed announcement-content"
            dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(selected.content) }}
          />
        </Modal>
      )}
    </section>
  );
}

// ---------------------------------------------------------------------------
// Skeleton — 3 animated bars while data loads
// ---------------------------------------------------------------------------

function SkeletonBars({ warming }: { warming: boolean }) {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="Loading announcements">
      {[0, 1, 2].map((i) => (
        <div key={i} className="bg-white border border-slate-200 px-6 py-5 animate-pulse">
          <div className="flex items-center justify-between mb-3">
            <div className="h-5 bg-slate-200 w-1/3" />
            <div className="h-4 bg-slate-100 w-24" />
          </div>
          <div className="space-y-2">
            <div className="h-3 bg-slate-100 w-full" />
            <div className="h-3 bg-slate-100 w-5/6" />
            <div className="h-3 bg-slate-100 w-4/6" />
          </div>
        </div>
      ))}
      {warming && (
        <p className="text-xs text-slate-400 text-center pt-1">
          Server is waking up, this may take a moment…
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Empty state — shown when API returns [] or errors silently
// ---------------------------------------------------------------------------

function EmptyState() {
  return (
    <div className="bg-white border border-slate-200 px-8 py-14 text-center">
      <div className="mx-auto mb-4 w-12 h-12 bg-slate-100 flex items-center justify-center">
        <MegaphoneIcon />
      </div>
      <p className="text-slate-600 font-medium">No announcements yet</p>
      <p className="text-slate-400 text-sm mt-1">
        Check back later for updates from the office.
      </p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Announcement card — flat, no rounded corners
// ---------------------------------------------------------------------------

function AnnouncementCard({
  item,
  onReadMore,
}: {
  item: AnnouncementPublicDto;
  onReadMore: (item: AnnouncementPublicDto) => void;
}) {
  const formattedDate = new Date(item.publishedAt).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });

  return (
    <article className="bg-white border border-slate-200 px-6 py-5">
      <div className="flex items-start justify-between gap-4 mb-3">
        <h3 className="text-lg font-semibold text-slate-800">{item.title}</h3>
        <time className="text-sm text-slate-400 shrink-0" dateTime={item.publishedAt}>
          {formattedDate}
        </time>
      </div>
      <div
        className="text-sm text-slate-600 leading-relaxed announcement-content line-clamp-4 overflow-hidden"
        dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(item.content) }}
      />
      <button
        onClick={() => onReadMore(item)}
        className="mt-3 text-sm text-green-700 hover:text-green-600 font-medium transition-colors"
      >
        Read more →
      </button>
    </article>
  );
}

// ---------------------------------------------------------------------------
// Megaphone icon — used in empty state
// ---------------------------------------------------------------------------

function MegaphoneIcon() {
  return (
    <svg
      className="w-6 h-6 text-slate-400"
      fill="none"
      stroke="currentColor"
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth={1.5}
        d="M11 5.882V19.24a1.76 1.76 0 01-3.417.592l-2.147-6.15M18 13a3 3 0 100-6M5.436 13.683A4.001 4.001 0 017 6h1.832c4.1 0 7.625-1.234 9.168-3v14c-1.543-1.766-5.067-3-9.168-3H7a3.988 3.988 0 01-1.564-.317z"
      />
    </svg>
  );
}
