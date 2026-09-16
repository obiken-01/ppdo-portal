"use client";

/**
 * Redirect stub — the Consolidated AIP moved onto the Report page (PPDO-92).
 *
 * ⚠️ **Kept rather than deleted, and it is not ceremony.** This URL is in the AIP Review header link,
 * in bookmarks, and in the ticket and spec history. A 404 would read as the feature having been
 * removed, which is exactly the wrong conclusion: it is a report type now.
 *
 * The fiscal year and sector are carried across, so a link to one sector's sheet still lands on it.
 */

import { Suspense, useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";

function ConsolidatedRedirect() {
  const router = useRouter();
  const searchParams = useSearchParams();

  useEffect(() => {
    const params = new URLSearchParams({ type: "AIP" });
    const fiscalYear = searchParams.get("fiscalYear");
    const sector = searchParams.get("sector");
    if (fiscalYear) params.set("fiscalYear", fiscalYear);
    if (sector) params.set("sector", sector);
    // `replace`, not `push` — Back should return where the reader came from, not to a URL that
    // immediately bounces them forward again.
    router.replace(`/budget-planning/report?${params.toString()}`);
  }, [router, searchParams]);

  return (
    <div className="p-6 text-sm text-slate-600">
      The Consolidated AIP is now a report type. Taking you to the Report page…
    </div>
  );
}

export default function AipConsolidatedRedirectPage() {
  return (
    <Suspense fallback={<div className="p-6 text-sm text-slate-600">Loading…</div>}>
      <ConsolidatedRedirect />
    </Suspense>
  );
}
