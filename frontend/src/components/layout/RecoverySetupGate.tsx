"use client";

/**
 * Full-screen takeover shown in place of the portal shell while `me.needsRecoverySetup`
 * is true (RAL-266) — every account without a recovery answer, whether brand new or
 * rolled over from before this feature existed. Deliberately not skippable: no Cancel,
 * no link out. A skipped setup is indistinguishable from a user who never logged in, and
 * the admin-reset fallback would silently become the only path.
 */

import RecoveryAnswerForm from "@/components/account/RecoveryAnswerForm";

export default function RecoverySetupGate({ onComplete }: { onComplete: () => void }) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 px-4">
      <div className="w-full max-w-sm bg-white border border-slate-200 shadow-sm p-6">
        <h1 className="text-lg font-bold text-slate-800 mb-1">Set up account recovery</h1>
        <p className="text-sm text-slate-600 mb-5">
          One more step: choose a question and answer only you would know. You&apos;ll use
          this later to reset your own password, without needing an administrator.
        </p>
        <RecoveryAnswerForm onSuccess={onComplete} submitLabel="Save & Continue" />
      </div>
    </div>
  );
}
