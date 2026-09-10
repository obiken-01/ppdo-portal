"use client";

import Link from "next/link";

/**
 * ActionCard — "what do I have to do next, and who am I waiting on?" (PPDO-20).
 *
 * The dashboard's action band. One card, stating the single next thing this person can do, with
 * the control to go do it. The page it replaced answered neither question — it reported four
 * readiness panels and left the reader to work out which one was theirs.
 *
 * Three tones, and the difference is not decoration:
 *
 *   `action`  — you can act now. Primary button.
 *   `waiting` — the next move belongs to somebody else, who is named. No control, because there
 *               is nothing for this person to press; a disabled button would just invite clicking.
 *   `blocked` — something is wrong and it needs attention. Danger tone.
 *
 * `disabledReason` is the one case where a control IS rendered disabled: the account MAY do this
 * (so hiding it would be wrong — see the read-only/forbidden rule in the spec's §6.1) but the
 * state does not yet allow it, e.g. Submit before the ceiling is published. Permission-based
 * unavailability is HIDDEN, not disabled; state-based unavailability is disabled with a reason.
 */

export type ActionTone = "action" | "waiting" | "blocked";

const TONE: Record<ActionTone, { border: string; bg: string; icon: string }> = {
  action: { border: "border-green-200", bg: "bg-green-25", icon: "→" },
  waiting: { border: "border-slate-200", bg: "bg-white", icon: "⏳" },
  blocked: { border: "border-danger-500/30", bg: "bg-danger-100/40", icon: "⚠" },
};

export default function ActionCard({
  tone = "action",
  title,
  description,
  actionLabel,
  href,
  onClick,
  disabledReason,
}: {
  tone?: ActionTone;
  /** The next thing to do, as a short imperative. "Enter your AIP", not "AIP entry". */
  title: string;
  /** One sentence of context, including who is being waited on when tone is "waiting". */
  description: string;
  actionLabel?: string;
  href?: string;
  onClick?: () => void;
  /** Renders the control disabled with this as its reason. State-based only — see the doc comment. */
  disabledReason?: string;
}) {
  const { border, bg, icon } = TONE[tone];
  const showControl = actionLabel != null && (href != null || onClick != null || disabledReason != null);

  const primary =
    "px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed";

  return (
    <div className={`${bg} border ${border} p-4 flex flex-col sm:flex-row sm:items-center gap-3`}>
      <span aria-hidden className="text-lg leading-none shrink-0">
        {icon}
      </span>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-slate-800">{title}</p>
        <p className="text-sm text-slate-600 mt-0.5">{description}</p>
      </div>

      {showControl && (
        <div className="shrink-0">
          {disabledReason ? (
            <>
              <button type="button" className={primary} disabled>
                {actionLabel}
              </button>
              <p className="text-xs text-slate-500 mt-1 text-right">{disabledReason}</p>
            </>
          ) : href ? (
            <Link href={href} className={`${primary} inline-block`}>
              {actionLabel}
            </Link>
          ) : (
            <button type="button" className={primary} onClick={onClick}>
              {actionLabel}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
