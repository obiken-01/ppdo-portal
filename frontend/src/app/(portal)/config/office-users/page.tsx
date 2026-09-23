"use client";

/**
 * Office Staff Divisions — PPDO-135.
 *
 * A department head holding `CanManageOfficeSetup` assigns their own office's Staff to one of
 * the divisions they configure on Config → Divisions. Deliberately NOT a cut-down User
 * Management: no create, no role change, no password reset, no permission overrides — those all
 * require `CanManageUsers`, which this grant is not (see docs/v1.8/Permission_Matrix.md). The one
 * control here is the division select per row.
 *
 * Endpoints (UserFunctions.cs, PLAIN JSON — not the config `{ data, error, message }` envelope):
 *   GET /api/office/users
 *   PUT /api/office/users/{id}/division   { divisionId: number | null }
 *
 * Both are scoped server-side to the caller's own office — there is no office picker here, and
 * there could not safely be one (see the ticket's warning about CanManageUsers-by-another-name).
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import { listDivisions } from "@/lib/config";
import api from "@/lib/api";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import { useToast } from "@/components/ui/Toast";
import type { DivisionResponse, OfficeUserResponse } from "@/types";

function officeUserErrorMessage(err: unknown, fallback: string): string {
  const data = (err as { response?: { data?: unknown } })?.response?.data;
  if (typeof data === "string" && data.trim()) return data;
  const msg = (data as { message?: string } | undefined)?.message;
  return msg ?? fallback;
}

export default function OfficeUserDivisionsPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [authChecked, setAuthChecked] = useState(false);
  const [officeName, setOfficeName] = useState<string | null>(null);

  const [users, setUsers] = useState<OfficeUserResponse[]>([]);
  const [divisions, setDivisions] = useState<DivisionResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Per-row "saving" state — a row is disabled while its own PUT is in flight, everything else
  // stays interactive. Keyed by user id since several rows could be edited in quick succession.
  const [savingIds, setSavingIds] = useState<Set<string>>(new Set());

  // ── Auth check ──────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canManageOfficeSetup) {
          router.replace(!data.isHostOffice ? "/budget-planning" : "/dashboard");
          return;
        }
        setOfficeName(data.officeName ?? data.officeCode ?? null);
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load ──────────────────────────────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      // Both calls are scoped to the caller's own office server-side — no office param to pass.
      const [usersRes, divisionsList] = await Promise.all([
        api.get<OfficeUserResponse[]>("/office/users"),
        listDivisions({ active: "true" }),
      ]);
      setUsers(usersRes.data);
      setDivisions(divisionsList);
    } catch (err) {
      setFetchError(officeUserErrorMessage(err, "Failed to load your office's staff. Please try again."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) void load();
  }, [authChecked, load]);

  // ── Set division ──────────────────────────────────────────────────────────────

  async function setDivision(user: OfficeUserResponse, divisionId: number | null) {
    const previous = user.divisionId;
    // Optimistic — the row's own select already shows the new value the instant it's picked;
    // this keeps the rest of the table (nothing else) in sync and gives the toast a name to use.
    setUsers((rows) => rows.map((r) => (r.id === user.id ? { ...r, divisionId } : r)));
    setSavingIds((ids) => new Set(ids).add(user.id));
    try {
      const { data } = await api.put<OfficeUserResponse>(`/office/users/${user.id}/division`, { divisionId });
      setUsers((rows) => rows.map((r) => (r.id === user.id ? data : r)));
    } catch (err) {
      // Revert on failure — a silent partial success (UI says X, server kept Y) is worse than
      // visibly snapping back.
      setUsers((rows) => rows.map((r) => (r.id === user.id ? { ...r, divisionId: previous } : r)));
      toast.error("Could not update division", officeUserErrorMessage(err, "Please try again."));
    } finally {
      setSavingIds((ids) => {
        const next = new Set(ids);
        next.delete(user.id);
        return next;
      });
    }
  }

  // ── Columns ───────────────────────────────────────────────────────────────────

  const columns: Column<OfficeUserResponse>[] = [
    {
      key: "fullName",
      header: "Name",
      sortable: true,
      render: (u) => (
        <div>
          <span className="font-medium text-slate-800">{u.fullName}</span>
          {!u.isActive && (
            <span className="ml-2 inline-flex items-center px-1.5 py-0.5 text-[10px] font-medium bg-danger-100 text-danger-500">
              Inactive
            </span>
          )}
        </div>
      ),
    },
    {
      key: "username",
      header: "Username",
      render: (u) => <span className="font-mono text-xs text-slate-600">{u.username}</span>,
    },
    {
      key: "position",
      header: "Position",
      render: (u) => <span className="text-sm text-slate-600">{u.position ?? "—"}</span>,
    },
    {
      key: "divisionId",
      header: "Division",
      render: (u) => (
        <select
          value={u.divisionId ?? ""}
          disabled={savingIds.has(u.id)}
          onChange={(e) => void setDivision(u, e.target.value ? Number(e.target.value) : null)}
          className="w-full max-w-[220px] px-2 py-1.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
        >
          <option value="">Unassigned</option>
          {divisions.map((d) => (
            <option key={d.id} value={d.id}>{d.name}</option>
          ))}
        </select>
      ),
    },
  ];

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-4xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        <ConfigPageHeader
          title="Staff Divisions"
          description={`Assign ${officeName ?? "your office"}'s Staff to a division. Divisions are set up on Divisions — this only says who belongs to which.`}
        />

        {divisions.length === 0 && !loading && (
          <div className="bg-amber-100 border border-slate-200 px-4 py-3">
            <p className="text-sm text-amber-900">
              This office has no divisions yet. Set one up on{" "}
              <a href="/config/divisions" className="font-medium underline">Divisions</a> first.
            </p>
          </div>
        )}

        <DataTable
          columns={columns}
          rows={users}
          rowKey={(u) => u.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage="No staff in this office yet."
          pageSize={25}
          rowNoun={["staff member", "staff members"]}
          minWidth={700}
        />
      </div>
    </div>
  );
}
