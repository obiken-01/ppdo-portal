"use client";

/**
 * GitHub-style loading bar along the top edge of the window (PPDO-244).
 *
 * Mounted once in the root layout, so every page — portal and public — gets it with no page code.
 * It draws `loadingProgress` (lib/loading-progress.ts), which is busy while a page change is
 * pending or an API request is in flight. This file feeds the page-change half:
 *
 *   start  — a click on an internal link, a back/forward step, or `router.push` / `router.replace`
 *            to a different page (Next.js 14's App Router has no route-change events)
 *   finish — `usePathname()` OR `useSearchParams()` changes. The AIP Entry and Review drill-downs
 *            only change the query string, so the path alone would never finish them.
 *
 * Creeps toward 80%, fills and fades when the last request ends. Fast loads never show it (the
 * tracker waits ~200 ms). With prefers-reduced-motion it shows as a still bar, no creep.
 */

import { Suspense, useEffect, useRef, useState, type CSSProperties } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { isSameDocumentUrl, loadingProgress, type ProgressPhase } from "@/lib/loading-progress";

const CREEP_TO = 0.8;
const CREEP_MS = 8_000;
const FILL_MS = 200;
const FADE_MS = 200;

type RouterMethod = (href: string, options?: { scroll?: boolean }) => void;
type PatchableRouter = { push: RouterMethod; replace: RouterMethod; __ppdoProgress?: boolean };

function startIfNewPage(href: string): void {
  try {
    const target = new URL(href, window.location.href);
    if (target.origin !== window.location.origin) return;
    if (isSameDocumentUrl(target.href, window.location.href)) return;
    loadingProgress.navigationStarted();
  } catch {
    // An href URL() cannot parse is not a page change we can track.
  }
}

/** Watches the page-change inputs. Inside Suspense: `useSearchParams` needs a boundary. */
function NavigationWatcher() {
  const pathname = usePathname();
  const query = useSearchParams().toString();
  const router = useRouter() as unknown as PatchableRouter;
  const committed = useRef("");

  useEffect(() => {
    committed.current = pathname + (query ? `?${query}` : "");
    loadingProgress.navigationFinished();
  }, [pathname, query]);

  useEffect(() => {
    function onClick(e: MouseEvent) {
      if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const anchor = (e.target as Element | null)?.closest?.("a");
      if (!anchor || !anchor.href) return;
      if (anchor.target && anchor.target !== "_self") return;
      if (anchor.hasAttribute("download")) return;
      startIfNewPage(anchor.href);
    }
    function onPopState() {
      // The URL has already moved; Next.js renders the page for it a moment later.
      if (window.location.pathname + window.location.search !== committed.current) {
        loadingProgress.navigationStarted();
      }
    }
    // ⚠️ Bubble phase, and `defaultPrevented` is ignored on purpose. Next's <Link> always calls
    // preventDefault in its own handler, so that flag cannot tell a navigating link from a blocked
    // one. What does block a link is the unsaved-changes guard (components/ui/UnsavedChanges.tsx):
    // it stops the click in the capture phase, so it never reaches this listener, and if the user
    // then leaves anyway it calls router.push, which the wrapper below catches.
    document.addEventListener("click", onClick);
    window.addEventListener("popstate", onPopState);
    return () => {
      document.removeEventListener("click", onClick);
      window.removeEventListener("popstate", onPopState);
    };
  }, []);

  // `router.push` / `router.replace` from page code. useRouter() hands every component the same
  // instance, so wrapping it once here covers them all. The wrapper only starts the bar and calls
  // through; the safety timeout in the tracker ends it if a navigation never lands.
  useEffect(() => {
    if (router.__ppdoProgress) return;
    const push = router.push.bind(router);
    const replace = router.replace.bind(router);
    router.push = (href, options) => { startIfNewPage(href); push(href, options); };
    router.replace = (href, options) => { startIfNewPage(href); replace(href, options); };
    router.__ppdoProgress = true;
  }, [router]);

  return null;
}

function prefersReducedMotion(): boolean {
  return typeof window !== "undefined"
    && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;
}

export default function TopLoadingBar() {
  const [phase, setPhase] = useState<ProgressPhase>("idle");
  const [style, setStyle] = useState<CSSProperties>({ transform: "scaleX(0)", opacity: 0 });
  const previous = useRef<ProgressPhase>("idle");

  useEffect(() => {
    setPhase(loadingProgress.getPhase());
    return loadingProgress.subscribe(setPhase);
  }, []);

  useEffect(() => {
    const from = previous.current;
    previous.current = phase;
    const still = prefersReducedMotion();

    if (phase === "loading") {
      if (still) { setStyle({ transform: "scaleX(1)", opacity: 1 }); return; }
      if (from === "done") {
        // A new request landed during the fade — pick up from where the bar is.
        setStyle({ transform: `scaleX(${CREEP_TO})`, opacity: 1, transition: `transform ${CREEP_MS}ms ease-out, opacity 100ms` });
        return;
      }
      // Paint a small stub first, then creep from it on the next frame.
      setStyle({ transform: "scaleX(0.08)", opacity: 1 });
      const frame = requestAnimationFrame(() => requestAnimationFrame(() => {
        setStyle({
          transform: `scaleX(${CREEP_TO})`,
          opacity: 1,
          transition: `transform ${CREEP_MS}ms cubic-bezier(0.1, 0.6, 0.2, 1)`,
        });
      }));
      return () => cancelAnimationFrame(frame);
    }

    if (phase === "done") {
      setStyle(still
        ? { transform: "scaleX(1)", opacity: 0 }
        : {
            transform: "scaleX(1)",
            opacity: 0,
            transition: `transform ${FILL_MS}ms ease-out, opacity ${FADE_MS}ms ease ${FILL_MS}ms`,
          });
      return;
    }

    setStyle({ transform: "scaleX(0)", opacity: 0 });
  }, [phase]);

  return (
    <>
      <Suspense fallback={null}>
        <NavigationWatcher />
      </Suspense>
      <div
        aria-hidden="true"
        data-testid="top-loading-bar"
        data-phase={phase}
        className="pointer-events-none fixed left-0 right-0 z-[9999] h-[3px] origin-left bg-green-400"
        style={{ top: "env(safe-area-inset-top, 0px)", ...style }}
      />
    </>
  );
}
