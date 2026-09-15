"use client";

/**
 * Character counter + soft warning for an AIP activity name being typed (PPDO-85). Sits under the
 * name textarea on every surface that edits it. Never blocks saving — see
 * `lib/aipActivityNameLimits.ts` for how the threshold is derived from the Annex B Excel export.
 */

import { aipActivityNamePastPrintableHeight } from "@/lib/aipActivityNameLimits";

export default function AipActivityNameCounter({ name }: { name: string }) {
  const overThreshold = aipActivityNamePastPrintableHeight(name);

  return (
    <p className={`mt-1 text-[11px] ${overThreshold ? "text-amber-700" : "text-slate-600"}`}>
      {name.length.toLocaleString()} character{name.length === 1 ? "" : "s"}
      {overThreshold && (
        <span className="ml-1">
          — this name is long enough that it may be cut off on the printed Annex B form. It will
          still save.
        </span>
      )}
    </p>
  );
}
