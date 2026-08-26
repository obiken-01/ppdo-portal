"use client";

/**
 * Change-password form fields + submit logic (RAL-88), extracted so both the
 * voluntary Security tab (/account) and the forced MustChangePasswordGate
 * (RAL-266) share one copy of the password policy instead of two.
 *
 * Renders only the fields and button — no card chrome or heading — so each
 * caller supplies its own wrapper and copy.
 */

import { useState } from "react";
import { changePassword } from "@/lib/account";

export default function ChangePasswordForm({
  onSuccess,
  submitLabel = "Save Changes",
}: {
  onSuccess: () => void;
  submitLabel?: string;
}) {
  const [current, setCurrent] = useState("");
  const [newPw, setNewPw]     = useState("");
  const [confirm, setConfirm] = useState("");
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const canSubmit = current.trim() !== "" && newPw !== "" && confirm !== "";

  // Client-side policy preview
  function validateLocal(): string | null {
    if (newPw.length < 8) return "New password must be at least 8 characters.";
    if (!/[A-Z]/.test(newPw)) return "New password must contain at least one uppercase letter.";
    if (!/\d/.test(newPw)) return "New password must contain at least one digit.";
    if (newPw !== confirm) return "Passwords do not match.";
    return null;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const localErr = validateLocal();
    if (localErr) { setError(localErr); return; }

    setSaving(true);
    setError(null);
    try {
      await changePassword({ currentPassword: current, newPassword: newPw, confirmPassword: confirm });
      setCurrent("");
      setNewPw("");
      setConfirm("");
      onSuccess();
    } catch (err) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Failed to change password.";
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  const inputCls =
    "w-full px-3 py-2 text-sm border border-slate-200 bg-white text-slate-800 focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent";

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          Current Password<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <input
          type="password"
          className={inputCls}
          value={current}
          onChange={(e) => { setCurrent(e.target.value); setError(null); }}
          autoComplete="current-password"
        />
      </div>

      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          New Password<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <input
          type="password"
          className={inputCls}
          value={newPw}
          onChange={(e) => { setNewPw(e.target.value); setError(null); }}
          autoComplete="new-password"
        />
        <p className="text-xs text-slate-600 mt-1">
          Min. 8 characters · at least 1 uppercase letter · at least 1 digit
        </p>
      </div>

      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          Confirm New Password<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <input
          type="password"
          className={inputCls}
          value={confirm}
          onChange={(e) => { setConfirm(e.target.value); setError(null); }}
          autoComplete="new-password"
        />
      </div>

      {error && <p className="text-sm text-danger-500">{error}</p>}

      <div className="flex justify-end pt-2">
        <button
          type="submit"
          disabled={!canSubmit || saving}
          className="px-5 py-2 text-sm font-medium text-white bg-green-600 hover:bg-green-500 disabled:opacity-60 transition-colors flex items-center gap-2"
        >
          {saving && (
            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          {saving ? "Saving…" : submitLabel}
        </button>
      </div>
    </form>
  );
}
