"use client";

/**
 * Registers the service worker and surfaces updates (PWA Phase 1).
 *
 * Mounted once in the root layout so it covers both the public site and the
 * portal. Renders nothing until an update is actually waiting.
 *
 * Update flow — why it asks instead of just reloading:
 *   public/sw.js deliberately does NOT call skipWaiting() on install, because
 *   activating a new worker swaps the assets under whatever the user is doing.
 *   In this app that could be a half-finished purchase request or a WFP draft.
 *   So a new worker waits, this component notices and offers a reload, and only
 *   on the user's click do we post SKIP_WAITING and reload.
 *
 * Not registered in development: the SW caches aggressively and would serve
 * stale chunks over the top of a hot-reloading dev server.
 */

import { useEffect, useState } from "react";

export default function ServiceWorkerRegistrar() {
  const [waitingWorker, setWaitingWorker] = useState<ServiceWorker | null>(null);

  useEffect(() => {
    if (process.env.NODE_ENV !== "production") return;
    if (typeof navigator === "undefined" || !("serviceWorker" in navigator)) return;

    let registration: ServiceWorkerRegistration | null = null;

    function trackInstalling(reg: ServiceWorkerRegistration) {
      const installing = reg.installing;
      if (!installing) return;
      installing.addEventListener("statechange", () => {
        // "installed" with an existing controller means this is an UPDATE
        // waiting to take over, not the very first install. On a first install
        // there is nothing to interrupt, so there is nothing to prompt about.
        if (installing.state === "installed" && navigator.serviceWorker.controller) {
          setWaitingWorker(installing);
        }
      });
    }

    navigator.serviceWorker
      .register("/sw.js")
      .then((reg) => {
        registration = reg;
        if (reg.waiting && navigator.serviceWorker.controller) setWaitingWorker(reg.waiting);
        reg.addEventListener("updatefound", () => trackInstalling(reg));
      })
      .catch(() => {
        // A failed registration must never break the page — the app works
        // exactly as it did before the service worker existed.
      });

    // The browser only checks for a new worker on navigation, which a
    // long-lived single-page session may not do for hours. Nudge it when the
    // tab is brought back to the foreground.
    function checkForUpdate() {
      if (document.visibilityState === "visible" && registration) {
        registration.update().catch(() => {});
      }
    }
    document.addEventListener("visibilitychange", checkForUpdate);
    return () => document.removeEventListener("visibilitychange", checkForUpdate);
  }, []);

  function handleReload() {
    if (!waitingWorker) return;
    // Reload once the new worker has actually taken control, rather than
    // racing it — otherwise the reload can be served by the outgoing worker.
    navigator.serviceWorker.addEventListener("controllerchange", () => window.location.reload(), {
      once: true,
    });
    waitingWorker.postMessage({ type: "SKIP_WAITING" });
  }

  if (!waitingWorker) return null;

  return (
    <div
      role="status"
      className="fixed bottom-4 left-1/2 -translate-x-1/2 z-50 w-[calc(100%-2rem)] max-w-md
                 bg-slate-800 text-white shadow-lg px-4 py-3 flex items-center gap-3 font-sans"
    >
      <p className="text-sm flex-1">
        A new version of the PPDO Portal is available.
      </p>
      <button
        type="button"
        onClick={handleReload}
        className="shrink-0 bg-green-600 text-white text-sm font-semibold px-3 py-1.5
                   hover:bg-green-500 active:bg-green-700 transition-colors
                   focus:outline-none focus:ring-2 focus:ring-green-400"
      >
        Reload
      </button>
      <button
        type="button"
        onClick={() => setWaitingWorker(null)}
        aria-label="Dismiss"
        className="shrink-0 text-slate-400 hover:text-white transition-colors px-1"
      >
        ✕
      </button>
    </div>
  );
}
