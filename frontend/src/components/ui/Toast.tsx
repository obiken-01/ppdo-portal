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
 *   - Each auto-dismisses after 2 seconds.
 *   - User can also close manually via the × button.
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
  { bar: string; iconBg: string; icon: string; titleColor: string }
> = {
  success: {
    bar:       "bg-green-500",
    iconBg:    "bg-green-500",
    icon:      "✓",
    titleColor: "text-slate-800",
  },
  error: {
    bar:       "bg-red-500",
    iconBg:    "bg-red-500",
    icon:      "✕",
    titleColor: "text-slate-800",
  },
  info: {
    bar:       "bg-blue-500",
    iconBg:    "bg-blue-500",
    icon:      "i",
    titleColor: "text-slate-800",
  },
  warning: {
    bar:       "bg-amber-400",
    iconBg:    "bg-amber-400",
    icon:      "!",
    titleColor: "text-slate-800",
  },
};

// ---------------------------------------------------------------------------
// Single toast item component
// ---------------------------------------------------------------------------

const AUTO_DISMISS_MS = 2000;

function ToastCard({
  item,
  onDismiss,
}: {
  item: ToastItem;
  onDismiss: (id: string) => void;
}) {
  const cfg = VARIANT_CONFIG[item.variant];
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    timerRef.current = setTimeout(() => onDismiss(item.id), AUTO_DISMISS_MS);
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [item.id, onDismiss]);

  return (
    <div
      role="alert"
      className="flex items-start gap-3 bg-white rounded-xl shadow-lg border border-slate-100 pr-4 pl-0 py-3.5 min-w-72 max-w-sm w-full overflow-hidden animate-slide-in"
    >
      {/* Left colour bar */}
      <div className={`w-1.5 self-stretch rounded-l-xl shrink-0 ${cfg.bar}`} />

      {/* Icon */}
      <div
        className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 text-white text-sm font-bold mt-0.5 ${cfg.iconBg}`}
      >
        {cfg.icon}
      </div>

      {/* Text */}
      <div className="flex-1 min-w-0">
        <p className={`text-sm font-bold leading-tight ${cfg.titleColor}`}>
          {item.title}
        </p>
        {item.message && (
          <p className="text-xs text-slate-500 mt-0.5 leading-snug">
            {item.message}
          </p>
        )}
      </div>

      {/* Close */}
      <button
        onClick={() => onDismiss(item.id)}
        aria-label="Dismiss"
        className="text-slate-400 hover:text-slate-600 transition-colors text-base leading-none mt-0.5 shrink-0"
      >
        ×
      </button>
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
