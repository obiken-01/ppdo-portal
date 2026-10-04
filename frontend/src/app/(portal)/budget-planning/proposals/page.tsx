"use client";

/**
 * Investment Proposals — the list (v1.8.0 Demo 2.15 — PPDO-159, `Investment_Proposal_Spec.md` §6.1).
 * Route: /budget-planning/proposals
 *
 * Lists **AIP projects**, not proposals: a project without one is a row too, with Create, because
 * this is where a reader finds a project to start from. The server scopes the rows to what AIP Entry
 * shows the reader (office, then division), so a guest office sees only its own.
 *
 * ⚠️ **FY 2027 is offered on purpose**, and answers with a sentence instead of a table. It is the
 * year an office is most likely to look for its old proposal in; an empty picker would read as
 * "lost", the sentence says "not here, and why".
 *
 * ⚠️ **The cross-office reviewer reads and exports but never creates** (`ReviewerWriteGuard`). Create
 * is left out for them rather than shown and refused.
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import {
  budgetPlanningFallback, canOpenInvestmentProposals, isCommentOnlyReviewer,
} from "@/lib/budget-planning-access";
import {
  ENTERED_FISCAL_YEAR_OPTIONS, FIRST_ENTERED_FISCAL_YEAR, resolveEnteredFiscalYear,
} from "@/lib/aip-fiscal-years";
import { useDefaultFiscalYear } from "@/lib/default-fiscal-year";
import { listOffices } from "@/lib/config";
import {
  createProposal, exportProposal, getProposalList, proposalEditorHref, proposalErrorMessage,
  proposalFileName,
} from "@/lib/investment-proposals";
import { formatMoney } from "@/lib/money";
import DataTable, { type Column } from "@/components/ui/DataTable";
import TableSkeleton from "@/components/ui/TableSkeleton";
import RowActions, { type RowAction } from "@/components/ui/RowActions";
import OfficeSelect from "@/components/ui/OfficeSelect";
import ProposalStatusPill from "@/components/proposals/ProposalStatusPill";
import type { OfficeResponse, ProposalListItem, ProposalListPage } from "@/types";

/** The last uploaded year, then the entered years. Derived, so the break year is written once. */
const YEARS = [FIRST_ENTERED_FISCAL_YEAR - 1, ...ENTERED_FISCAL_YEAR_OPTIONS];
const PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 300;

/** "1 Oct 2026, 2:05 PM" in Manila time. */
function formatUpdated(iso: string | null): string {
  if (!iso) return "";
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit",
  });
}

export default function InvestmentProposalsPage() {
  const me = useMe(canOpenInvestmentProposals, budgetPlanningFallback);
  const router = useRouter();
  const searchParams = useSearchParams();
  const readOnly = me != null && isCommentOnlyReviewer(me);
  const showOffice = me?.isHostOffice === true;

  // PPDO-145 — the URL's year wins; else the admin default when this page offers it; else the
  // break year. Nothing loads until the year is settled, so no request runs against the fallback.
  const requestedYear = Number(searchParams.get("fiscalYear"));
  const { ready: defaultReady, defaultFiscalYear } = useDefaultFiscalYear();
  const [pickedYear, setPickedYear] = useState<number | null>(YEARS.includes(requestedYear) ? requestedYear : null);
  const yearSettled = pickedYear != null || defaultReady;
  const fiscalYear = pickedYear ?? resolveEnteredFiscalYear(null, defaultFiscalYear, YEARS);
  const beforeEntry = fiscalYear < FIRST_ENTERED_FISCAL_YEAR;

  const [officeId, setOfficeId] = useState<number | null>(null);
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);

  const [data, setData] = useState<ProposalListPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  /** The row whose Create or Export is in flight, so only that button spins. */
  const [busy, setBusy] = useState<{ projectId: number; action: "create" | "export" } | null>(null);

  // Only host-office readers choose an office, so only they need the list.
  useEffect(() => {
    if (!showOffice) return;
    void listOffices({ active: "true" }).then(setOffices).catch(() => setOffices([]));
  }, [showOffice]);

  useEffect(() => {
    const t = setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [searchInput]);

  const load = useCallback(async () => {
    if (!yearSettled || beforeEntry || me == null) return;
    setLoading(true);
    setError(null);
    try {
      setData(await getProposalList({ fiscalYear, officeId, search, page, pageSize: PAGE_SIZE }));
    } catch {
      setData(null);
      setError("Couldn't load projects.");
    } finally {
      setLoading(false);
    }
  }, [yearSettled, beforeEntry, me, fiscalYear, officeId, search, page]);

  useEffect(() => { void load(); }, [load]);

  async function create(row: ProposalListItem) {
    setActionError(null);
    setBusy({ projectId: row.aipProjectId, action: "create" });
    try {
      const outcome = await createProposal(row.aipProjectId);
      // A 409 means someone created it since the list loaded: open theirs rather than failing.
      router.push(proposalEditorHref(outcome.kind === "created" ? outcome.proposal.id : outcome.proposalId));
    } catch (e) {
      setActionError(proposalErrorMessage(e, "Couldn't create the proposal."));
      setBusy(null);
    }
  }

  async function download(row: ProposalListItem) {
    if (row.proposalId == null) return;
    setActionError(null);
    setBusy({ projectId: row.aipProjectId, action: "export" });
    try {
      await exportProposal(row.proposalId, proposalFileName(row.projectRefCode, row.projectName));
    } catch (e) {
      setActionError(proposalErrorMessage(e, "Couldn't export the proposal."));
    } finally {
      setBusy(null);
    }
  }

  function actionsFor(row: ProposalListItem): RowAction[] {
    const isBusy = (action: "create" | "export") => busy?.projectId === row.aipProjectId && busy.action === action;
    if (row.proposalId == null) {
      return readOnly ? [] : [{
        key: "create", label: "Create", variant: "primary", onClick: () => void create(row),
        loading: isBusy("create"), disabled: busy != null,
      }];
    }
    return [
      { key: "open", label: "Open", href: proposalEditorHref(row.proposalId) },
      {
        key: "export", label: "Export", onClick: () => void download(row),
        loading: isBusy("export"), disabled: busy != null, title: "Download as Word",
      },
    ];
  }

  const columns: Column<ProposalListItem>[] = [
    { key: "projectRefCode", header: "Ref code", className: "whitespace-nowrap font-mono text-xs" },
    {
      key: "projectName", header: "Project",
      render: (r) => (
        <div className="min-w-0">
          <p className="font-medium text-slate-800">{r.projectName}</p>
          <p className="text-xs text-slate-600">{r.programName}</p>
        </div>
      ),
    },
    ...(showOffice ? [{ key: "officeName", header: "Office" } as Column<ProposalListItem>] : []),
    {
      key: "projectCost", header: "Cost", align: "right", className: "whitespace-nowrap tabular-nums",
      render: (r) => `₱${formatMoney(r.projectCost)}`,
    },
    { key: "status", header: "Status", render: (r) => <ProposalStatusPill status={r.status} /> },
    {
      key: "updatedAt", header: "Updated", className: "whitespace-nowrap text-xs",
      render: (r) => r.updatedAt ? (
        <span title={r.updatedByName ? `by ${r.updatedByName}` : undefined}>{formatUpdated(r.updatedAt)}</span>
      ) : "",
    },
    { key: "actions", header: "", align: "right", render: (r) => <RowActions actions={actionsFor(r)} /> },
  ];

  const skeletonHeaders = columns.map((c) => c.header);
  const rows = data?.items ?? [];

  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4">
        <h1 className="text-xl font-semibold text-slate-800">Investment Proposals</h1>
        <p className="mt-0.5 text-sm text-slate-600">
          One PGOM Investment Proposal per AIP project, filled from the AIP and exported to Word.
        </p>
      </div>

      {/* Filters render at once and stay usable through loading and errors (spec §6.1). */}
      <div className="mb-4 flex flex-wrap items-end gap-3 border border-slate-200 bg-white px-4 py-3">
        <div>
          <label htmlFor="proposal-year" className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            Fiscal year
          </label>
          <select
            id="proposal-year"
            value={yearSettled ? fiscalYear : ""}
            disabled={!yearSettled}
            onChange={(e) => { setPickedYear(Number(e.target.value)); setPage(1); }}
            className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            {!yearSettled && <option value="">FY …</option>}
            {YEARS.map((y) => <option key={y} value={y}>FY {y}</option>)}
          </select>
        </div>
        {showOffice && (
          <div className="w-72 max-w-full">
            <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">Office</label>
            <OfficeSelect
              offices={offices}
              value={officeId}
              onChange={(id) => { setOfficeId(id); setPage(1); }}
              allOptionLabel="All offices"
            />
          </div>
        )}
        <div className="min-w-[14rem] flex-1">
          <label htmlFor="proposal-search" className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            Search
          </label>
          <input
            id="proposal-search"
            type="search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Ref code, project or program"
            className="w-full border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
          />
        </div>
      </div>

      {actionError && (
        <p role="alert" className="mb-4 border border-danger-500/30 bg-danger-100 px-4 py-3 text-sm text-danger-500">
          {actionError}
        </p>
      )}

      {beforeEntry ? (
        <div className="border border-slate-200 bg-white px-4 py-10 text-center text-sm text-slate-600">
          Investment proposals start with FY {FIRST_ENTERED_FISCAL_YEAR}.
        </div>
      ) : (loading || !yearSettled) ? (
        <div className="overflow-x-auto border border-slate-200 bg-white">
          <TableSkeleton columns={skeletonHeaders} rowCount={8} />
        </div>
      ) : error ? (
        <div className="flex flex-col items-center gap-3 border border-slate-200 bg-white py-12">
          <p className="text-sm text-danger-500">{error}</p>
          <button type="button" onClick={() => void load()} className="text-sm font-medium text-green-700 hover:underline">
            Retry
          </button>
        </div>
      ) : rows.length === 0 ? (
        <div className="border border-slate-200 bg-white px-4 py-10 text-center text-sm text-slate-600">
          {search ? (
            <>
              <p>No projects match &ldquo;{search}&rdquo;.</p>
              <button
                type="button"
                onClick={() => { setSearchInput(""); setSearch(""); setPage(1); }}
                className="mt-2 font-medium text-green-700 hover:underline"
              >
                Clear search
              </button>
            </>
          ) : (
            <>
              <p>
                No FY {fiscalYear} projects in the AIP yet. Proposals are created from AIP projects, so add
                projects in AIP Entry first.
              </p>
              <Link
                href={`/budget-planning/aip/entry?fiscalYear=${fiscalYear}`}
                className="mt-2 inline-block font-medium text-green-700 hover:underline"
              >
                Go to AIP Entry →
              </Link>
            </>
          )}
        </div>
      ) : (
        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(r) => r.aipProjectId}
          rowNoun={["project", "projects"]}
          minWidth={showOffice ? 1000 : 820}
          serverPagination={{
            page: data?.page ?? page,
            pageSize: data?.pageSize ?? PAGE_SIZE,
            totalCount: data?.totalCount ?? 0,
            onPageChange: setPage,
          }}
        />
      )}
    </div>
  );
}
