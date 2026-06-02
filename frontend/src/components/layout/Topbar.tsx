"use client";

import type { MeResponse } from "@/types";

interface TopbarProps {
  me: MeResponse | null;
  title: string;
}

export default function Topbar({ me, title }: TopbarProps) {
  return (
    <header className="h-14 bg-white border-b border-slate-200 flex items-center justify-between px-6 shrink-0 shadow-sm">
      <h1 className="text-sm font-semibold text-slate-700">{title}</h1>

      {me && (
        <span className="text-sm text-slate-500 hidden sm:block">
          {me.fullName}
          <span className="ml-1.5 text-xs text-slate-400">({me.role})</span>
        </span>
      )}
    </header>
  );
}
