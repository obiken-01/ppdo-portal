"use client";

import { useLayoutEffect, useRef } from "react";

/** Grows a textarea to fit its content up to this height, then it scrolls internally (PPDO-85). */
export const AUTO_GROW_MAX_HEIGHT_PX = 220;

/**
 * Ref for a textarea that grows to fit `value` as it's typed, up to AUTO_GROW_MAX_HEIGHT_PX, then
 * scrolls internally rather than growing further. Re-measures whenever `value` changes, including
 * the initial value on mount, so a pre-filled long name starts already sized to fit — pair with
 * `overflow-y-auto` on the textarea's className instead of `resize-vertical` (auto-grow and a
 * manual drag handle would otherwise fight each other).
 */
export function useAutoGrowTextarea(value: string) {
  const ref = useRef<HTMLTextAreaElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, AUTO_GROW_MAX_HEIGHT_PX)}px`;
  }, [value]);

  return ref;
}
