"use client";

/**
 * A proposal's status (spec §6.1): None grey, Draft amber, Final green.
 *
 * ⚠️ Its own pill rather than `StatusPill`: that one is the dashboard's four-stage vocabulary
 * (Todo / In progress / Review / Done), and folding a document's Draft/Final into it would make
 * "Final" read as "Done", the wrong word for a proposal that can be reopened. Same shape and tokens.
 */

import type { ProposalStatus } from "@/types";

const CLS: Record<ProposalStatus, string> = {
  None:  "bg-slate-100 text-slate-600",
  Draft: "bg-amber-100 text-amber-500",
  Final: "bg-green-100 text-green-700",
};

export default function ProposalStatusPill({ status }: { status: ProposalStatus }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap ${CLS[status]}`}
      title={status === "None" ? "No proposal yet" : status}
    >
      {status}
    </span>
  );
}
