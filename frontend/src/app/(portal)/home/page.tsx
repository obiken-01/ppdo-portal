"use client";

/**
 * /home — neutral landing entry point (RAL-264).
 *
 * Exists because some entry points cannot know who the user is when the URL is decided:
 *
 *   - the PWA manifest's `start_url` is a **single fixed value baked into the installed app**,
 *     shared by every user on every device — it cannot vary per user at all;
 *   - /reconnecting's `?next=` default fires when a session is recovered with no recorded
 *     destination.
 *
 * Both used to point at /dashboard, which office users cannot open — an installed PWA would
 * launch them straight into a page the layout gate immediately ejected them from.
 *
 * This page resolves the real destination once `me` is known and replaces itself. It renders
 * only a brief placeholder: the portal layout already blocks on its own auth guard, so by the
 * time this mounts the session is settled and the redirect happens immediately.
 */

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import { resolveLandingPath } from "@/lib/landing";

export default function HomePage() {
  const router = useRouter();

  useEffect(() => {
    let cancelled = false;

    fetchMe()
      .then((me) => {
        if (!cancelled) router.replace(resolveLandingPath(me));
      })
      .catch(() => {
        // Session could not be established — the portal guard would land here too.
        if (!cancelled) router.replace("/login");
      });

    return () => { cancelled = true; };
  }, [router]);

  return (
    <div className="flex min-h-[50vh] items-center justify-center">
      <div className="flex flex-col items-center gap-3">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-green-600 border-t-transparent" />
        <p className="text-sm text-slate-600">Opening your portal…</p>
      </div>
    </div>
  );
}
