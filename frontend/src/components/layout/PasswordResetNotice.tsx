"use client";

/**
 * Dismissible banner shown once, at the top of the portal shell, when the account was
 * just reset — self-service (RAL-265) or admin-initiated (RAL-254) (RAL-267).
 *
 * This is the detective control that makes a colleague-guessed recovery answer
 * noticeable: the real user sees this the next time they log in even if they never
 * requested the reset.
 */

import { useState } from "react";
import { acknowledgePasswordReset } from "@/lib/account";

export default function PasswordResetNotice({
  resetAt,
  onDismissed,
}: {
  /** ISO timestamp from MeResponse.unacknowledgedPasswordResetAt. */
  resetAt: string;
  onDismissed: () => void;
}) {
  const [dismissing, setDismissing] = useState(false);

  async function handleDismiss() {
    setDismissing(true);
    try {
      await acknowledgePasswordReset();
    } catch {
      // Non-critical — worst case the banner reappears next load; still dismiss locally
      // so a flaky request doesn't trap the user behind a banner they already read.
    } finally {
      onDismissed();
    }
  }

  const formatted = new Date(resetAt).toLocaleString("en-PH", {
    dateStyle: "medium",
    timeStyle: "short",
  });

  return (
    <div className="flex items-center justify-between gap-4 bg-amber-50 border-b border-amber-200 px-4 py-2">
      <p className="text-sm text-amber-800">
        Your password was reset on {formatted}. If this wasn&apos;t you, contact your administrator.
      </p>
      <button
        type="button"
        onClick={handleDismiss}
        disabled={dismissing}
        className="text-xs font-medium text-amber-800 hover:text-amber-900 underline whitespace-nowrap disabled:opacity-60"
      >
        Dismiss
      </button>
    </div>
  );
}
