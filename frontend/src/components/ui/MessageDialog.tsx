"use client";

/**
 * MessageDialog — simple informational popup with a single OK/Close button.
 *
 * A thin wrapper over Modal for notices that need no Yes/No choice — e.g. a CSV
 * import summary ("12 added, 4 updated, 1 skipped") or a blocking validation
 * summary. For Yes/No confirmations use ConfirmDialog; for arbitrary forms use
 * Modal directly.
 *
 * Usage:
 *   {result && (
 *     <MessageDialog
 *       title="Import complete"
 *       variant="success"
 *       onClose={() => setResult(null)}
 *     >
 *       <p>{result.new} added, {result.updated} updated, {result.skipped} skipped.</p>
 *     </MessageDialog>
 *   )}
 */

import Modal, { type ModalSize } from "./Modal";

export type MessageVariant = "info" | "success" | "warning" | "error";

export interface MessageDialogProps {
  title: string;
  children: React.ReactNode;
  variant?: MessageVariant;
  okLabel?: string;
  size?: ModalSize;
  onClose: () => void;
}

const ICON: Record<MessageVariant, string> = {
  info: "i",
  success: "✓",
  warning: "!",
  error: "✕",
};

const ICON_BG: Record<MessageVariant, string> = {
  info: "bg-info-100 text-info-500",
  success: "bg-green-100 text-green-600",
  warning: "bg-amber-100 text-amber-500",
  error: "bg-danger-100 text-danger-500",
};

export default function MessageDialog({
  title,
  children,
  variant = "info",
  okLabel = "OK",
  size = "sm",
  onClose,
}: MessageDialogProps) {
  return (
    <Modal
      title={title}
      size={size}
      onClose={onClose}
      footer={<Modal.PrimaryButton onClick={onClose}>{okLabel}</Modal.PrimaryButton>}
    >
      <div className="flex items-start gap-3">
        <span className={`flex-shrink-0 w-9 h-9 flex items-center justify-center text-base font-bold ${ICON_BG[variant]}`}>
          {ICON[variant]}
        </span>
        <div className="text-sm text-slate-600 leading-relaxed pt-1">{children}</div>
      </div>
    </Modal>
  );
}
