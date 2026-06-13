"use client";

/**
 * Budget Planning — placeholder (RAL-81).
 *
 * RAL-81 introduces budget-planning access control and the office-user redirect
 * target. The full dashboard (PPDO vs office-user views, LDIP/AIP/WFP) is RAL-80.
 * This page exists so the post-login redirect for office users lands somewhere real;
 * it guards on canAccessBudgetPlanning and otherwise shows a "coming soon" state.
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import type { MeResponse } from "@/types";

export default function BudgetPlanningPage() {
  const router = useRouter();
  const [checked, setChecked] = useState(false);

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessBudgetPlanning) router.replace("/dashboard");
        else setChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  if (!checked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-4xl mx-auto px-6 py-16">
        <div className="bg-white border border-slate-200 rounded-xl shadow-sm px-8 py-12 text-center">
          <div className="text-4xl mb-3">💰</div>
          <h1 className="text-xl font-bold text-slate-800 mb-2">Budget Planning</h1>
          <p className="text-sm text-slate-500 max-w-md mx-auto">
            LDIP, AIP, and WFP budget planning is being set up. The dashboard and entry
            screens will appear here soon.
          </p>
        </div>
      </div>
    </div>
  );
}
