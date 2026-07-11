"use client";

/**
 * ConfirmDialog — reusable confirmation modal using PPDO design tokens.
 *
 * Usage:
 *   const [dialog, setDialog] = useState<ConfirmDialogProps | null>(null);
 *
 *   // Open:
 *   setDialog({
 *     title:       "Mark as Completed?",
 *     message:     "This will lock the PR from receiving further deliveries.",
 *     confirmLabel: "Mark Completed",
 *     variant:      "primary",           // "primary" | "warning" | "danger"
 *     onConfirm:   () => doTheAction(),
 *     onClose:     () => setDialog(null),
 *   });
 *
 *   // Render:
 *   {dialog && <ConfirmDialog {...dialog} />}
 *
 * Behaviour:
 *   - Backdrop click → closes (calls onClose)
 *   - Escape key    → closes (calls onClose)
 *   - Confirm button → calls onConfirm then onClose
 *   - Focus is trapped inside the modal while open
 */

import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type ConfirmDialogVariant = "primary" | "warning" | "danger";

export interface ConfirmDialogProps {
  title:        string;
  message:      string;
  confirmLabel?: string;
  cancelLabel?:  string;
  variant?:      ConfirmDialogVariant;
  onConfirm:    () => void;
  onClose:      () => void;
}

// ---------------------------------------------------------------------------
// Variant styles
// ---------------------------------------------------------------------------

const CONFIRM_BTN: Record<ConfirmDialogVariant, string> = {
  primary: "bg-green-600 hover:bg-green-500 text-white border-transparent",
  warning: "bg-amber-500 hover:bg-amber-400 text-white border-transparent",
  danger:  "bg-danger-500 hover:bg-red-500   text-white border-transparent",
};

const ICON: Record<ConfirmDialogVariant, string> = {
  primary: "✓",
  warning: "↩",
  danger:  "⚠",
};

const ICON_BG: Record<ConfirmDialogVariant, string> = {
  primary: "bg-green-100 text-green-600",
  warning: "bg-amber-100 text-amber-600",
  danger:  "bg-danger-100 text-danger-500",
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export default function ConfirmDialog({
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel  = "Cancel",
  variant      = "primary",
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  const cancelRef  = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);

  // Focus cancel button on open (safe default — prevents accidental confirm)
  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

  // Close on Escape
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Trap Tab focus within the two buttons
  useEffect(() => {
    function onTab(e: KeyboardEvent) {
      if (e.key !== "Tab") return;
      const els = [cancelRef.current, confirmRef.current].filter(Boolean) as HTMLButtonElement[];
      if (els.length < 2) return;
      const first = els[0];
      const last  = els[els.length - 1];
      if (e.shiftKey) {
        if (document.activeElement === first) { e.preventDefault(); last.focus(); }
      } else {
        if (document.activeElement === last)  { e.preventDefault(); first.focus(); }
      }
    }
    window.addEventListener("keydown", onTab);
    return () => window.removeEventListener("keydown", onTab);
  }, []);

  function handleConfirm() {
    onConfirm();
    onClose();
  }

  const content = (
    /* Backdrop */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      aria-modal="true"
      role="dialog"
      aria-labelledby="confirm-title"
      aria-describedby="confirm-message"
    >
      {/* Dimmed overlay — click to close */}
      <div
        className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm"
        onClick={onClose}
      />

      {/* Panel */}
      <div className="relative w-full max-w-sm bg-white shadow-xl border border-slate-200 overflow-hidden">

        {/* Top accent bar */}
        <div className={`h-1 w-full ${
          variant === "primary" ? "bg-green-600" :
          variant === "warning" ? "bg-amber-500" :
          "bg-danger-500"
        }`} />

        <div className="px-6 py-5 space-y-4">

          {/* Icon + Title */}
          <div className="flex items-start gap-3">
            <span className={`flex-shrink-0 w-9 h-9 flex items-center justify-center text-base font-bold ${ICON_BG[variant]}`}>
              {ICON[variant]}
            </span>
            <div>
              <h2 id="confirm-title" className="text-sm font-semibold text-slate-800 leading-snug">
                {title}
              </h2>
              <p id="confirm-message" className="mt-1 text-sm text-slate-600 leading-relaxed">
                {message}
              </p>
            </div>
          </div>

          {/* Actions */}
          <div className="flex justify-end gap-2 pt-1">
            <button
              ref={confirmRef}
              onClick={handleConfirm}
              className={`px-4 py-2 text-sm border font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-offset-1 ${CONFIRM_BTN[variant]} ${
                variant === "primary" ? "focus:ring-green-400" :
                variant === "warning" ? "focus:ring-amber-400" :
                "focus:ring-red-400"
              }`}
            >
              {confirmLabel}
            </button>
            <button
              ref={cancelRef}
              onClick={onClose}
              className="px-4 py-2 text-sm border border-slate-200 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium focus:outline-none focus:ring-2 focus:ring-slate-300"
            >
              {cancelLabel}
            </button>
          </div>

        </div>
      </div>
    </div>
  );

  if (typeof document === "undefined") return null;
  return createPortal(content, document.body);
}
