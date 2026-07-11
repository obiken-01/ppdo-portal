"use client";

/**
 * PPDO Portal — Toast notification system.
 *
 * Usage:
 *   1. Wrap your page (or layout) with <ToastProvider>.
 *   2. Call useToast() inside any child component to get { toast }.
 *   3. Call toast.success("Saved!") / toast.error("…") / toast.info("…") / toast.warn("…")
 *
 * Behaviour:
 *   - Toasts stack in the top-right corner.
 *   - Each auto-dismisses after AUTO_DISMISS_MS (5 s).
 *   - A progress bar at the bottom shrinks from full-width to zero over that
 *     duration; its colour matches the variant's accent colour.
 *   - User can also close manually via the × button.
 *   - Rectangular shape, no rounded corners.
 *   - Left accent bar is full-height and thicker (6 px).
 *
 * When to use toasts vs inline errors — see CLAUDE.md / Toast standard:
 *   ✅ Toast  — success confirmations, API errors on completed actions
 *   ❌ Inline — form validation, errors inside open modals
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type ToastVariant = "success" | "error" | "info" | "warning";

interface ToastItem {
  id: string;
  variant: ToastVariant;
  title: string;
  message?: string;
}

interface ToastContextValue {
  toast: {
    success: (title: string, message?: string) => void;
    error:   (title: string, message?: string) => void;
    info:    (title: string, message?: string) => void;
    warn:    (title: string, message?: string) => void;
  };
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const ToastContext = createContext<ToastContextValue | null>(null);

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast must be used inside <ToastProvider>");
  return ctx;
}

// ---------------------------------------------------------------------------
// Visual config per variant
// ---------------------------------------------------------------------------

const VARIANT_CONFIG: Record<
  ToastVariant,
  { bar: string; iconBg: string; icon: string; progress: string }
> = {
  success: {
    bar:      "bg-green-500",
    iconBg:   "bg-green-500",
    icon:     "✓",
    progress: "bg-green-500",
  },
  error: {
    bar:      "bg-red-500",
    iconBg:   "bg-red-500",
    icon:     "✕",
    progress: "bg-red-500",
  },
  info: {
    bar:      "bg-blue-500",
    iconBg:   "bg-blue-500",
    icon:     "i",
    progress: "bg-blue-500",
  },
  warning: {
    bar:      "bg-amber-400",
    iconBg:   "bg-amber-400",
    icon:     "!",
    progress: "bg-amber-400",
  },
};

// ---------------------------------------------------------------------------
// Single toast card
// ---------------------------------------------------------------------------

const AUTO_DISMISS_MS = 5000;

function ToastCard({
  item,
  onDismiss,
}: {
  item: ToastItem;
  onDismiss: (id: string) => void;
}) {
  const cfg = VARIANT_CONFIG[item.variant];

  // Auto-dismiss timer
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    timerRef.current = setTimeout(() => onDismiss(item.id), AUTO_DISMISS_MS);
    return () => { if (timerRef.current) clearTimeout(timerRef.current); };
  }, [item.id, onDismiss]);

  return (
    <div
      role="alert"
      className="flex flex-col bg-white shadow-lg border border-slate-200 min-w-72 max-w-sm w-full overflow-hidden animate-slide-in"
      // No rounded corners — rectangular by design
    >
      {/* ── Main row: accent bar | icon | text | close ── */}
      <div className="flex items-stretch">
        {/* Left accent bar — full-height, 6 px wide */}
        <div className={`w-1.5 shrink-0 ${cfg.bar}`} />

        {/* Icon */}
        <div className={`flex items-center justify-center w-11 shrink-0 ${cfg.iconBg}`}>
          <span className="text-white text-sm font-bold">{cfg.icon}</span>
        </div>

        {/* Text */}
        <div className="flex-1 min-w-0 px-3 py-3">
          <p className="text-sm font-bold leading-tight text-slate-800">
            {item.title}
          </p>
          {item.message && (
            <p className="text-xs text-slate-600 mt-0.5 leading-snug">
              {item.message}
            </p>
          )}
        </div>

        {/* Close button */}
        <button
          onClick={() => onDismiss(item.id)}
          aria-label="Dismiss"
          className="px-3 text-slate-600 hover:text-slate-600 hover:bg-slate-50 transition-colors self-stretch flex items-start pt-2.5 text-base leading-none shrink-0"
        >
          ×
        </button>
      </div>

      {/* ── Progress bar — shrinks left-to-right over AUTO_DISMISS_MS ── */}
      <div className="h-1 w-full bg-slate-100">
        <div
          className={`h-full ${cfg.progress} origin-left`}
          style={{
            animation: `toast-progress ${AUTO_DISMISS_MS}ms linear forwards`,
          }}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  function add(variant: ToastVariant, title: string, message?: string) {
    const id = `${Date.now()}-${Math.random()}`;
    setToasts((prev) => [...prev, { id, variant, title, message }]);
  }

  const toast: ToastContextValue["toast"] = {
    success: (t, m) => add("success", t, m),
    error:   (t, m) => add("error",   t, m),
    info:    (t, m) => add("info",    t, m),
    warn:    (t, m) => add("warning", t, m),
  };

  return (
    <ToastContext.Provider value={{ toast }}>
      {children}

      {/* ── Toast stack — fixed top-right ── */}
      {toasts.length > 0 && (
        <div
          aria-live="polite"
          className="fixed top-4 right-4 z-[9999] flex flex-col gap-2 pointer-events-none"
        >
          {toasts.map((item) => (
            <div key={item.id} className="pointer-events-auto">
              <ToastCard item={item} onDismiss={dismiss} />
            </div>
          ))}
        </div>
      )}
    </ToastContext.Provider>
  );
}
