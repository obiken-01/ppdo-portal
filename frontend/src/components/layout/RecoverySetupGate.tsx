"use client";

/**
 * Full-screen takeover shown in place of the portal shell while `me.needsRecoverySetup`
 * is true (RAL-266) — every account without a recovery answer, whether brand new or
 * rolled over from before this feature existed. Deliberately not skippable: no Cancel,
 * no link out. A skipped setup is indistinguishable from a user who never logged in, and
 * the admin-reset fallback would silently become the only path.
 *
 * Copy is written for the ROLLOVER audience, not onboarding. `needsRecoverySetup` is derived
 * from account state, not session state, so an existing user restored from a refresh token
 * lands here having asked for nothing — this is the only surface that reaches them (the
 * landing-page announcement does not; they never see the landing page). It must therefore
 * answer, unprompted: why am I seeing this, is my password being changed, will it recur.
 */

import RecoveryAnswerForm from "@/components/account/RecoveryAnswerForm";

export default function RecoverySetupGate({ onComplete }: { onComplete: () => void }) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-100 px-4">
      <div className="w-full max-w-sm bg-white border border-slate-200 shadow-sm p-6">
        <h1 className="text-lg font-bold text-slate-800 mb-2">Set up account recovery</h1>
        <p className="text-sm text-slate-600 mb-3">
          The portal now lets you reset your own password if you forget it. To make that
          possible, every account needs one security question saved &mdash; including accounts
          that already existed, so you&apos;re seeing this once even if nothing about your
          account has changed.
        </p>
        <p className="text-sm text-slate-600 mb-5">
          Choose a question and give an answer only you would know.{" "}
          <span className="font-medium text-slate-800">Your password is not changing.</span>{" "}
          This takes about a minute, and you won&apos;t be asked again.
        </p>
        <RecoveryAnswerForm onSuccess={onComplete} submitLabel="Save & Continue" />
      </div>
    </div>
  );
}
