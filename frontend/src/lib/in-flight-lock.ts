/**
 * Double-click guard for save and submit actions (PPDO-244).
 *
 * A `saving` React state disables the button only after the next render. Any `await` before
 * `setSaving(true)` (a confirm, a validation read) leaves a window where a second click starts a
 * second request. The lock is set synchronously, so the second call is dropped no matter when the
 * render lands. Keep the `saving` state for the UI; wrap the handler body in `run`.
 */

import { useRef } from "react";

export interface InFlightLock {
  /** Runs `fn` unless a previous call is still pending; a dropped call resolves to `undefined`. */
  run<T>(fn: () => Promise<T>): Promise<T | undefined>;
  isBusy(): boolean;
}

export function createInFlightLock(): InFlightLock {
  let busy = false;
  return {
    async run<T>(fn: () => Promise<T>): Promise<T | undefined> {
      if (busy) return undefined;
      busy = true;
      try {
        return await fn();
      } finally {
        busy = false;
      }
    },
    isBusy: () => busy,
  };
}

/** One lock per component instance, stable across renders. */
export function useInFlightLock(): InFlightLock {
  const ref = useRef<InFlightLock | null>(null);
  if (ref.current === null) ref.current = createInFlightLock();
  return ref.current;
}
