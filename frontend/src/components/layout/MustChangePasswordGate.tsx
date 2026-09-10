"use client";

/**
 * Full-screen takeover shown in place of the portal shell while `me.mustChangePassword`
 * is true (RAL-266). No Sidebar/Topbar render at all, so there is nothing to navigate to —
 * the guard is structural, not a route check that could be bypassed by typing a URL.
 */

import ChangePasswordForm from "@/components/account/ChangePasswordForm";

export default function MustChangePasswordGate({ onComplete }: { onComplete: () => void }) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 px-4">
      <div className="w-full max-w-sm bg-white border border-slate-200 shadow-sm p-6">
        <h1 className="text-lg font-bold text-slate-800 mb-1">Set your password</h1>
        <p className="text-sm text-slate-600 mb-5">
          You&apos;re signing in with a temporary password. Set your own below to continue —
          you won&apos;t be able to reach the rest of the portal until you do.
        </p>
        <ChangePasswordForm onSuccess={onComplete} submitLabel="Set Password" />
      </div>
    </div>
  );
}
