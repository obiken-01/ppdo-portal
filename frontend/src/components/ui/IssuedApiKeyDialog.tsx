"use client";

/**
 * IssuedApiKeyDialog — shows a newly issued partner API key exactly once
 * (v1.8.0 — PPDO-86, build spec §6.1 "Key issued").
 *
 * Unlike every other modal in the app, this one cannot be dismissed by backdrop click or
 * Escape — losing the key means issuing a new one, so a stray click or keypress must not be
 * able to close it before the admin has copied it. Sibling of IssuedPasswordDialog, which
 * the same "shown once, not recoverable" pattern was built for (RAL-254).
 */

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export interface IssuedApiKeyDialogProps {
  /** Who the key belongs to — shown so the admin can confirm it's for the right partner. */
  partnerName: string;
  plaintextKey: string;
  onClose: () => void;
}

export default function IssuedApiKeyDialog({
  partnerName,
  plaintextKey,
  onClose,
}: IssuedApiKeyDialogProps) {
  const [copied, setCopied] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  async function copy() {
    try {
      await navigator.clipboard.writeText(plaintextKey);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard blocked — the key stays on screen and selectable.
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="issued-api-key-title"
        className="w-full max-w-md bg-white shadow-lg"
      >
        <div className="border-b border-slate-200 px-5 py-3">
          <h2 id="issued-api-key-title" className="text-base font-semibold text-slate-800">
            API key issued
          </h2>
        </div>

        <div className="px-5 py-4">
          <p className="text-sm text-slate-600">
            New key for <span className="font-medium text-slate-800">{partnerName}</span>.
          </p>

          <div className="mt-4 flex items-stretch border border-slate-300">
            <code className="flex-1 select-all break-all px-3 py-2.5 font-mono text-sm tracking-wide text-slate-800">
              {plaintextKey}
            </code>
            <button
              type="button"
              onClick={() => void copy()}
              className="shrink-0 border-l border-slate-300 bg-white px-3 text-sm font-medium text-green-700 hover:bg-slate-50"
            >
              {copied ? "Copied" : "Copy"}
            </button>
          </div>

          <p className="mt-3 border border-amber-300 bg-amber-100 px-3 py-2 text-xs text-amber-800">
            Copy this key now. It will not be shown again — if it is lost, issue a new one.
          </p>
        </div>

        <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3">
          <button
            ref={closeRef}
            type="button"
            onClick={onClose}
            className="bg-green-700 px-4 py-2 text-sm font-medium text-white hover:bg-green-800"
          >
            I have copied it
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
