"use client";

/**
 * IssuedPasswordDialog — shows a one-time password after an account is created
 * or an admin resets one (RAL-254).
 *
 * The portal used to hand every account the same documented default password, so
 * nothing had to be relayed. Passwords are now random and per-account: this dialog
 * is the only place the plaintext ever appears. It is not recoverable afterwards —
 * losing it means resetting again.
 *
 * Deliberately not a toast: a toast auto-dismisses, and the admin needs time to
 * copy the value and hand it over.
 */

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export interface IssuedPasswordDialogProps {
  /** Who the password belongs to — shown so the admin relays it to the right person. */
  fullName: string;
  username: string;
  password: string;
  /** Distinguishes "account created" wording from "password reset" wording. */
  context: "created" | "reset";
  onClose: () => void;
}

export default function IssuedPasswordDialog({
  fullName,
  username,
  password,
  context,
  onClose,
}: IssuedPasswordDialogProps) {
  const [copied, setCopied] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(password);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard blocked (insecure origin or denied permission) — the password is
      // on screen and selectable, so this is a convenience failure, not a blocker.
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="issued-password-title"
        className="w-full max-w-md bg-white shadow-lg"
      >
        <div className="border-b border-slate-200 px-5 py-3">
          <h2 id="issued-password-title" className="text-base font-semibold text-slate-800">
            {context === "created" ? "Account created" : "Password reset"}
          </h2>
        </div>

        <div className="px-5 py-4">
          <p className="text-sm text-slate-600">
            {context === "created" ? (
              <>
                <span className="font-medium text-slate-800">{fullName}</span> can now sign in
                as <span className="font-mono">{username}</span> with this password.
              </>
            ) : (
              <>
                A new password has been issued for{" "}
                <span className="font-medium text-slate-800">{fullName}</span>{" "}
                (<span className="font-mono">{username}</span>). Any active session has been
                signed out.
              </>
            )}
          </p>

          <div className="mt-4 flex items-stretch border border-slate-300">
            <code className="flex-1 select-all bg-slate-50 px-3 py-2.5 font-mono text-base tracking-wide text-slate-800 break-all">
              {password}
            </code>
            <button
              type="button"
              onClick={copy}
              className="shrink-0 border-l border-slate-300 bg-white px-3 text-sm font-medium text-green-700 hover:bg-slate-50"
            >
              {copied ? "Copied" : "Copy"}
            </button>
          </div>

          <p className="mt-3 text-xs text-amber-800 bg-amber-100 border border-amber-300 px-3 py-2">
            This password is shown once and cannot be retrieved later. Copy it now and give it
            to the user directly. If you lose it, reset the password again.
          </p>
        </div>

        <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3">
          <button
            ref={closeRef}
            type="button"
            onClick={onClose}
            className="bg-green-700 px-4 py-2 text-sm font-medium text-white hover:bg-green-800"
          >
            Done
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
