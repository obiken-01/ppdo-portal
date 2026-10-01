"use client";

/**
 * AIP Entry's "Investment proposal" strip, in the project panel between the project details and its
 * activities (Demo 2.15 — PPDO-159, `Investment_Proposal_Spec.md` §6.4, decision 29; wireframe
 * artboard 4).
 *
 * ⚠️ **Fed by the page, one call for the whole office** (`GET /proposals/projects`), never one call
 * per project. The page passes this project's option, or nothing while that call is in flight or
 * after it failed — and nothing renders then: the panel works without the strip, and the
 * Investment Proposals list is the way in when it is missing (§6.4 "Loading / error").
 *
 * ⚠️ **Read-only callers never see Create**: the cross-office reviewer (`ReviewerWriteGuard`) and a
 * Staff user with no division in a divisioned office. Both would only get a 403. They still see the
 * status, and "View proposal →" when one exists.
 */

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { createProposal, proposalEditorHref, proposalErrorMessage } from "@/lib/investment-proposals";
import ProposalStatusPill from "./ProposalStatusPill";
import type { ProposalProjectOption } from "@/types";

function manila(iso: string, withTime: boolean): string {
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", day: "numeric", month: "short", year: "numeric",
    ...(withTime ? { hour: "numeric", minute: "2-digit" } : {}),
  });
}

export default function ProposalStrip({
  option, readOnly,
}: {
  /** This project's row from the page's one `getProposalProjects` call; undefined hides the strip. */
  option: ProposalProjectOption | undefined;
  readOnly: boolean;
}) {
  const router = useRouter();
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!option) return null;

  async function create(projectId: number) {
    setError(null);
    setCreating(true);
    try {
      const outcome = await createProposal(projectId);
      // A 409 means a colleague created it since the strip loaded: open theirs.
      router.push(proposalEditorHref(outcome.kind === "created" ? outcome.proposal.id : outcome.proposalId));
    } catch (e) {
      setError(proposalErrorMessage(e, "Couldn't create the proposal."));
      setCreating(false);
    }
  }

  const linkCls = "text-sm font-medium text-green-700 hover:underline whitespace-nowrap";

  return (
    <section aria-label="Investment proposal" className="border-t border-slate-200 bg-slate-50 px-3 py-2.5">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
          <span className="text-xs font-semibold uppercase tracking-wide text-slate-600">Investment proposal</span>
          <ProposalStatusPill status={option.status} />
          <span className="text-xs text-slate-600">
            {option.status === "None"
              ? readOnly
                ? "No proposal yet."
                : "Creates it with the description, objective and activity names already filled in."
              : option.status === "Final"
                ? option.updatedAt ? `Finalized ${manila(option.updatedAt, false)}` : "Finalized"
                : option.updatedAt
                  ? `Last saved ${manila(option.updatedAt, true)}${option.updatedByName ? ` by ${option.updatedByName}` : ""}`
                  : null}
          </span>
        </div>

        {option.proposalId != null ? (
          <Link href={proposalEditorHref(option.proposalId)} className={linkCls}>
            {readOnly ? "View proposal →" : "Open proposal →"}
          </Link>
        ) : !readOnly ? (
          <button
            type="button"
            onClick={() => void create(option.aipProjectId)}
            disabled={creating}
            className="border border-green-300 bg-green-50 px-3 py-1 text-xs font-medium text-green-700 hover:bg-green-100 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {creating ? "Creating…" : "Create proposal"}
          </button>
        ) : null}
      </div>
      {error && <p role="alert" className="mt-1.5 text-xs text-danger-500">{error}</p>}
    </section>
  );
}
