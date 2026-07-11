"use client";

/**
 * Modal — generic content modal with a custom footer (v1.1 shared base).
 *
 * The base building block for all v1.1 popups (config Add/Edit forms, CSV
 * upload preview, WFP expenditure entry, AIP manual entry). ConfirmDialog and
 * MessageDialog are thin wrappers over this. Flat design — no rounded corners.
 *
 * Usage:
 *   {open && (
 *     <Modal
 *       title="Add Account"
 *       size="md"
 *       onClose={() => setOpen(false)}
 *       footer={
 *         <>
 *           <Modal.SecondaryButton onClick={() => setOpen(false)}>Cancel</Modal.SecondaryButton>
 *           <Modal.PrimaryButton onClick={save} loading={saving}>Save</Modal.PrimaryButton>
 *         </>
 *       }
 *     >
 *       <form>…</form>
 *     </Modal>
 *   )}
 *
 * Behaviour:
 *   - Backdrop click → onClose
 *   - Escape key     → onClose
 *   - × button       → onClose
 *   - Body scrolls when content exceeds the viewport; header/footer stay fixed.
 *   - `footer` is optional — omit it for a chromeless content modal.
 */

import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";

export type ModalSize = "sm" | "md" | "lg" | "xl" | "2xl";

export interface ModalProps {
  title: React.ReactNode;
  children: React.ReactNode;
  /** Footer buttons; caller supplies them. Omit for no footer. */
  footer?: React.ReactNode;
  size?: ModalSize;
  /** Lock the panel to a fixed 90vh height instead of auto-sizing up to 90vh. */
  fixedHeight?: boolean;
  onClose: () => void;
}

const SIZE_CLASS: Record<ModalSize, string> = {
  sm: "max-w-sm",
  md: "max-w-lg",
  lg: "max-w-2xl",
  xl: "max-w-5xl",
  "2xl": "max-w-7xl",
};

export default function Modal({ title, children, footer, size = "md", fixedHeight, onClose }: ModalProps) {
  const backdropRef = useRef<HTMLDivElement>(null);

  // Close on Escape
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  function handleBackdrop(e: React.MouseEvent) {
    if (e.target === backdropRef.current) onClose();
  }

  const content = (
    <div
      ref={backdropRef}
      onClick={handleBackdrop}
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-sm p-4"
      aria-modal="true"
      role="dialog"
      aria-label={typeof title === "string" ? title : undefined}
    >
      <div className={`w-full ${SIZE_CLASS[size]} bg-white shadow-xl border border-slate-200 flex flex-col ${fixedHeight ? "h-[90vh]" : "max-h-[90vh]"}`}>
        {/* Top accent bar — flat green */}
        <div className="h-1 w-full bg-green-600 shrink-0" />

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 shrink-0">
          <h2 className="text-base font-semibold text-slate-800">{title}</h2>
          <button
            onClick={onClose}
            className="text-slate-600 hover:text-slate-600 transition-colors text-xl leading-none"
            aria-label="Close"
          >
            ×
          </button>
        </div>

        {/* Body — scrollable */}
        <div className="overflow-y-auto flex-1 px-6 py-5">{children}</div>

        {/* Footer */}
        {footer && (
          <div className="flex items-center justify-end gap-2 px-6 py-4 border-t border-slate-200 shrink-0">
            {footer}
          </div>
        )}
      </div>
    </div>
  );

  if (typeof document === "undefined") return null;
  return createPortal(content, document.body);
}

// ---------------------------------------------------------------------------
// Footer button helpers — flat, shared styling for Modal footers
// ---------------------------------------------------------------------------

interface ButtonProps {
  onClick: () => void;
  children: React.ReactNode;
  disabled?: boolean;
  loading?: boolean;
  type?: "button" | "submit";
}

function PrimaryButton({ onClick, children, disabled, loading, type = "button" }: ButtonProps) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled || loading}
      className="px-5 py-2 text-sm bg-green-600 text-white font-medium border border-transparent hover:bg-green-500 transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2 focus:outline-none focus:ring-2 focus:ring-green-400"
    >
      {loading && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
      {children}
    </button>
  );
}

function SecondaryButton({ onClick, children, disabled }: ButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="px-4 py-2 text-sm border border-slate-200 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-slate-300"
    >
      {children}
    </button>
  );
}

Modal.PrimaryButton = PrimaryButton;
Modal.SecondaryButton = SecondaryButton;
