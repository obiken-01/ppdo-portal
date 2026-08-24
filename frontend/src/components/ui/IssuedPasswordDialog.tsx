"use client";

/**
 * IssuedPasswordDialog — shows the sign-in credentials after an account is created
 * or an admin resets a password (RAL-254).
 *
 * The portal used to hand every account the same documented default password, so
 * nothing had to be relayed. Passwords are now random and per-account: this dialog
 * is the only place the plaintext ever appears. It is not recoverable afterwards —
 * losing it means resetting again.
 *
 * Username is shown alongside it (and separately copyable) because the admin has to
 * relay both, and usernames now keep whatever casing they were typed in.
 *
 * Deliberately not a toast: a toast auto-dismisses, and the admin needs time to copy
 * the values and hand them over.
 */

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export interface IssuedPasswordDialogProps {
  /** Who the credentials belong to — shown so the admin relays them to the right person. */
  fullName: string;
  username: string;
  password: string;
  /** Distinguishes "account created" wording from "password reset" wording. */
  context: "created" | "reset";
  onClose: () => void;
}

type CopyField = "username" | "password";

/** One labelled, selectable, copyable credential row. */
function CredentialField({
  label,
  value,
  copied,
  onCopy,
}: {
  label: string;
  value: string;
  copied: boolean;
  onCopy: () => void;
}) {
  return (
    <div className="flex items-stretch border border-slate-300">
      <span className="flex w-24 shrink-0 items-center border-r border-slate-300 bg-slate-50 px-3 text-xs font-medium text-slate-600">
        {label}
      </span>
      <code className="flex-1 select-all break-all px-3 py-2.5 font-mono text-base tracking-wide text-slate-800">
        {value}
      </code>
      <button
        type="button"
        onClick={onCopy}
        className="shrink-0 border-l border-slate-300 bg-white px-3 text-sm font-medium text-green-700 hover:bg-slate-50"
      >
        {copied ? "Copied" : "Copy"}
      </button>
    </div>
  );
}

export default function IssuedPasswordDialog({
  fullName,
  username,
  password,
  context,
  onClose,
}: IssuedPasswordDialogProps) {
  const [copied, setCopied] = useState<CopyField | null>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  async function copy(field: CopyField, value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(field);
      window.setTimeout(() => setCopied((c) => (c === field ? null : c)), 2000);
    } catch {
      // Clipboard blocked (insecure origin or denied permission) — the value is on
      // screen and selectable, so this is a convenience failure, not a blocker.
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
                Give these sign-in details to{" "}
                <span className="font-medium text-slate-800">{fullName}</span>.
              </>
            ) : (
              <>
                A new password has been issued for{" "}
                <span className="font-medium text-slate-800">{fullName}</span>. Any active
                session has been signed out.
              </>
            )}
          </p>

          <div className="mt-4 space-y-2">
            <CredentialField
              label="Username"
              value={username}
              copied={copied === "username"}
              onCopy={() => copy("username", username)}
            />
            <CredentialField
              label="Password"
              value={password}
              copied={copied === "password"}
              onCopy={() => copy("password", password)}
            />
          </div>

          <p className="mt-3 border border-amber-300 bg-amber-100 px-3 py-2 text-xs text-amber-800">
            The password is shown once and cannot be retrieved later. Copy it now and give it
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
