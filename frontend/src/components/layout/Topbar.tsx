"use client";

import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import type { MeResponse } from "@/types";

interface TopbarProps {
  me: MeResponse | null;
  title: string;
}

export default function Topbar({ me, title }: TopbarProps) {
  const router = useRouter();

  async function handleLogout() {
    try {
      await api.post("/auth/logout");
    } catch {
      // ignore — clear local tokens regardless
    }
    auth.logout();
    router.replace("/login");
  }

  return (
    <header className="h-14 bg-white border-b border-slate-200 flex items-center justify-between px-6 shrink-0 shadow-sm">
      <h1 className="text-sm font-semibold text-slate-700">{title}</h1>

      <div className="flex items-center gap-4">
        {me && (
          <span className="text-sm text-slate-600 hidden sm:block">
            {me.fullName}
            <span className="ml-1.5 text-xs text-slate-400">({me.division})</span>
          </span>
        )}
        <button
          onClick={handleLogout}
          className="text-xs text-slate-500 hover:text-danger-500 transition-colors border border-slate-200 hover:border-danger-500 rounded-lg px-3 py-1.5"
        >
          Log out
        </button>
      </div>
    </header>
  );
}
