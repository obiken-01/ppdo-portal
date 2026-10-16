"use client";

/**
 * The bell's unread count (v1.9.0 — PPDO-222, spec decision 7 and §3.2).
 *
 * Read on mount, then every 60 s **only while the tab is visible**, and once more the moment the tab
 * becomes visible again. ⚠️ **A failed read is silent**: the badge keeps its last value and the next
 * tick tries again — a flaky connection must not turn the top bar into an error banner.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { POLL_INTERVAL_MS, getUnreadCount, onNotificationsChanged, shouldPoll } from "@/lib/notifications";

export function useUnreadCount(): { count: number; setCount: (n: number) => void; refresh: () => Promise<void> } {
  const [count, setCount] = useState(0);
  const alive = useRef(true);

  const refresh = useCallback(async () => {
    try {
      const n = await getUnreadCount();
      if (alive.current) setCount(n);
    } catch {
      // Keep the last value; the next tick retries.
    }
  }, []);

  useEffect(() => {
    alive.current = true;
    void refresh();

    const tick = () => { if (shouldPoll(document.visibilityState)) void refresh(); };
    const timer = window.setInterval(tick, POLL_INTERVAL_MS);
    document.addEventListener("visibilitychange", tick);
    const off = onNotificationsChanged(() => void refresh());

    return () => {
      alive.current = false;
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", tick);
      off();
    };
  }, [refresh]);

  return { count, setCount, refresh };
}
