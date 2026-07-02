"use client";

/**
 * LDIP edit page (RAL-61) — loads the record's full hierarchy, then renders the
 * shared LdipForm pre-populated. Final/Archived records render read-only
 * (admins get Unlock).
 */

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getLdipById, ldipErrorMessage } from "@/lib/ldip";
import LdipForm from "../../LdipForm";
import type { LdipRecordDetail } from "@/types";

export default function LdipEditClient() {
  const params = useParams();
  const rawId = Array.isArray(params.id) ? params.id[0] : (params.id as string);

  const [record, setRecord] = useState<LdipRecordDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (!rawId) return;
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
