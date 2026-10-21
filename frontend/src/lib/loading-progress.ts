/**
 * State behind the GitHub-style top loading bar (PPDO-244).
 *
 * Two inputs keep it busy: page changes (`navigationStarted` / `navigationFinished`, fed by
 * `TopLoadingBar`) and API requests (`requestStarted` / `requestFinished`, fed by the `lib/api.ts`
 * interceptors). No page calls it. A request opts out with `{ skipProgress: true }` — background
 * polls and silent refreshes do, or the bar would flicker every minute.
 *
 * Phases:
 *   idle    → nothing showing
 *   loading → busy for at least SHOW_DELAY_MS (fast loads never flash the bar)
 *   done    → quiet for FINISH_GRACE_MS; the bar fills and fades for DONE_HOLD_MS, then idle
 *
 * The grace keeps back-to-back work as one load: a new page mounts, then fires its first request a
 * few ms later. Without it the bar filled, then shrank back as that request started.
 *
 * A page change that never finishes (a link Next.js handled some other way) is ended after
 * NAV_TIMEOUT_MS so the bar can never stick.
 */

export type ProgressPhase = "idle" | "loading" | "done";

export const SHOW_DELAY_MS = 200;
export const FINISH_GRACE_MS = 150;
export const DONE_HOLD_MS = 400;
export const NAV_TIMEOUT_MS = 15_000;

export interface ProgressTracker {
  requestStarted(): void;
  requestFinished(): void;
  navigationStarted(): void;
  navigationFinished(): void;
  getPhase(): ProgressPhase;
  subscribe(listener: (phase: ProgressPhase) => void): () => void;
}

export function createProgressTracker(): ProgressTracker {
  let inFlight = 0;
  let navigating = false;
  let phase: ProgressPhase = "idle";
  let showTimer: ReturnType<typeof setTimeout> | null = null;
  let finishTimer: ReturnType<typeof setTimeout> | null = null;
  let doneTimer: ReturnType<typeof setTimeout> | null = null;
  let navTimer: ReturnType<typeof setTimeout> | null = null;
  const listeners = new Set<(phase: ProgressPhase) => void>();

  const busy = () => inFlight > 0 || navigating;

  function setPhase(next: ProgressPhase): void {
    if (next === phase) return;
    phase = next;
    listeners.forEach((l) => l(next));
  }

  function update(): void {
    if (busy()) {
      if (finishTimer) { clearTimeout(finishTimer); finishTimer = null; }
      if (doneTimer) { clearTimeout(doneTimer); doneTimer = null; }
      if (phase === "done") { setPhase("loading"); return; }
      if (phase === "idle" && !showTimer) {
        showTimer = setTimeout(() => {
          showTimer = null;
          if (busy()) setPhase("loading");
        }, SHOW_DELAY_MS);
      }
      return;
    }

    if (showTimer) { clearTimeout(showTimer); showTimer = null; }
    if (phase === "loading" && !finishTimer) {
      finishTimer = setTimeout(() => {
        finishTimer = null;
        if (busy()) return;
        setPhase("done");
        doneTimer = setTimeout(() => {
          doneTimer = null;
          setPhase("idle");
        }, DONE_HOLD_MS);
      }, FINISH_GRACE_MS);
    }
  }

  function clearNavTimer(): void {
    if (navTimer) { clearTimeout(navTimer); navTimer = null; }
  }

  return {
    requestStarted() {
      inFlight++;
      update();
    },
    requestFinished() {
      inFlight = Math.max(0, inFlight - 1);
      update();
    },
    navigationStarted() {
      navigating = true;
      clearNavTimer();
      navTimer = setTimeout(() => {
        navTimer = null;
        navigating = false;
        update();
      }, NAV_TIMEOUT_MS);
      update();
    },
    navigationFinished() {
      if (!navigating) return;
      navigating = false;
      clearNavTimer();
      update();
    },
    getPhase: () => phase,
    subscribe(listener) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
  };
}

/** The one tracker the app shares: `lib/api.ts` feeds it, `TopLoadingBar` draws it. */
export const loadingProgress: ProgressTracker = createProgressTracker();

/**
 * True when `href` resolves to the page already showing (same path and query; the hash is
 * ignored). A click on such a link does not change the page, so it must not start the bar —
 * nothing would ever finish it.
 */
export function isSameDocumentUrl(href: string, currentHref: string): boolean {
  const target = new URL(href, currentHref);
  const current = new URL(currentHref);
  return target.origin === current.origin
    && target.pathname === current.pathname
    && target.search === current.search;
}

/** The two config fields the API interceptors read and write. */
export interface ProgressTrackable {
  /** True keeps the request off the bar — background polls and silent refreshes. */
  skipProgress?: boolean;
  /** Set while the request is counted. A 401 retry reuses the same config, so it is not counted twice. */
  _progressTracked?: boolean;
}

export function trackRequestStart(config: ProgressTrackable, tracker: ProgressTracker = loadingProgress): void {
  if (config.skipProgress || config._progressTracked) return;
  config._progressTracked = true;
  tracker.requestStarted();
}

export function trackRequestEnd(config: ProgressTrackable | undefined, tracker: ProgressTracker = loadingProgress): void {
  if (!config?._progressTracked) return;
  config._progressTracked = false;
  tracker.requestFinished();
}
