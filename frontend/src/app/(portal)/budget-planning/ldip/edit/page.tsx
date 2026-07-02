"use client";

/**
 * LDIP edit page (RAL-61) — /budget-planning/ldip/edit?id=<recordId>
 *
 * Query-param route (the AIP-detail pattern): the app uses `output: "export"`,
 * so a dynamic [id] segment can't be statically exported and 500s in dev for
 * any id not listed in generateStaticParams. Loads the record's full hierarchy,
 * then renders the shared LdipForm pre-populated. Final/Archived records render
 * read-only (admins get Unlock).
 */

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { getLdipById, ldipErrorMessage } from "@/lib/ldip";
import LdipForm from "../LdipForm";
import type { LdipRecordDetail } from "@/types";

function LdipEditInner() {
  const searchParams = useSearchParams();
  const rawId = searchParams.get("id");

  const [record, setRecord] = useState<LdipRecordDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (!rawId) {
      setLoadError("No LDIP record id supplied.");
      return;
    }
    getLdipById(Number(rawId))
      .then(setRecord)
      .catch((err) => setLoadError(ldipErrorMessage(err, "Failed to load LDIP record.")));
  }, [rawId]);

  if (loadError) {
    return (
      <div className="p-6">
        <div className="border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {loadError}
        </div>
      </div>
    );
  }

  if (!record) {
    return (
      <div className="p-6 flex items-center gap-2 text-slate-500 text-sm">
        <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
        Loading…
      </div>
    );
  }

  return <LdipForm record={record} />;
}

// useSearchParams requires a Suspense boundary during prerender (Next.js app router).
export default function LdipEditPage() {
  return (
    <Suspense fallback={null}>
      <LdipEditInner />
    </Suspense>
  );
}
