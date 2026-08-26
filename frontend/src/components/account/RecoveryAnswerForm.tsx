"use client";

/**
 * Recovery-question setup/change form (RAL-266). Shared by the mandatory one-time
 * RecoverySetupGate and the voluntary "Account Recovery" section on /account —
 * `SetRecoveryAnswerAsync` overwrites whatever was there before, so re-running this
 * later is exactly how a user changes their answer, not a separate operation.
 *
 * The answer fields are masked by default, like a password — a recovery answer is a
 * credential (RAL-253's own framing), not profile text, so it shouldn't sit in plain
 * view on screen. Held down, the "Show" button reveals it for as long as it's held,
 * so a user can check what they typed without leaving it exposed afterward.
 */

import { useEffect, useState } from "react";
import { getRecoveryQuestionOptions, setRecoveryAnswer } from "@/lib/account";
import type { RecoveryQuestionOption } from "@/types";

function RevealableAnswerInput({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  const [revealed, setRevealed] = useState(false);

  return (
    <div className="relative">
      <input
        type={revealed ? "text" : "password"}
        className="w-full px-3 py-2 pr-16 text-sm border border-slate-200 bg-white text-slate-800
                   focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoComplete="off"
      />
      <button
        type="button"
        tabIndex={-1}
        onMouseDown={() => setRevealed(true)}
        onMouseUp={() => setRevealed(false)}
        onMouseLeave={() => setRevealed(false)}
        onTouchStart={() => setRevealed(true)}
        onTouchEnd={() => setRevealed(false)}
        className="absolute right-1 top-1/2 -translate-y-1/2 px-2 py-1 text-xs font-medium
                   text-slate-500 hover:text-slate-700 select-none"
      >
        {revealed ? "Hide" : "Show"}
      </button>
    </div>
  );
}

export default function RecoveryAnswerForm({
  onSuccess,
  submitLabel = "Save Changes",
}: {
  onSuccess: () => void;
  submitLabel?: string;
}) {
  const [options, setOptions] = useState<RecoveryQuestionOption[]>([]);
  const [loadingOptions, setLoadingOptions] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [questionKey, setQuestionKey] = useState("");
  const [answer, setAnswer] = useState("");
  const [confirmAnswer, setConfirmAnswer] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getRecoveryQuestionOptions()
      .then((opts) => {
        setOptions(opts);
        if (opts.length > 0) setQuestionKey(opts[0].key);
      })
      .catch(() => setLoadError(true))
      .finally(() => setLoadingOptions(false));
  }, []);

  const canSubmit = questionKey !== "" && answer.trim() !== "" && confirmAnswer.trim() !== "";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (answer.trim() !== confirmAnswer.trim()) {
      setError("Answers do not match.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await setRecoveryAnswer({ questionKey, answer: answer.trim() });
      setAnswer("");
      setConfirmAnswer("");
      onSuccess();
    } catch (err) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Failed to save your recovery answer.";
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  if (loadingOptions) {
    return <p className="text-sm text-slate-600">Loading…</p>;
  }

  if (loadError) {
    return (
      <p className="text-sm text-danger-500">
        Couldn&apos;t load the question list. Please refresh the page.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          Security Question<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <select
          className="w-full px-3 py-2 text-sm border border-slate-200 bg-white text-slate-800
                     focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent"
          value={questionKey}
          onChange={(e) => { setQuestionKey(e.target.value); setError(null); }}
        >
          {options.map((o) => (
            <option key={o.key} value={o.key}>{o.text}</option>
          ))}
        </select>
      </div>

      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          Answer<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <RevealableAnswerInput value={answer} onChange={(v) => { setAnswer(v); setError(null); }} />
      </div>

      <div>
        <label className="block text-xs font-medium text-slate-600 mb-1">
          Confirm Answer<span className="text-danger-500 ml-0.5">*</span>
        </label>
        <RevealableAnswerInput
          value={confirmAnswer}
          onChange={(v) => { setConfirmAnswer(v); setError(null); }}
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
