"use client";

/**
 * CsvUploadButton — file picker for CSV bulk import (v1.1 shared).
 *
 * Self-contained on file selection only: wraps a hidden <input type="file">,
 * accepts a `.csv`, and hands the chosen File back via `onSelect`. The consuming
 * page owns the rest of the round-trip — a confirm/preview step (compose with
 * the shared Modal), the POST to the upsert endpoint, and the result summary
 * (compose with MessageDialog). This keeps the button reusable while letting
 * each config page present its own preview. Flat design — no rounded corners.
 *
 * Usage:
 *   <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
 */

import { useRef } from "react";

export interface CsvUploadButtonProps {
  onSelect: (file: File) => void;
  label?: string;
  accept?: string;
  disabled?: boolean;
}

export default function CsvUploadButton({
  onSelect,
  label = "Upload CSV",
  accept = ".csv,text/csv",
  disabled,
}: CsvUploadButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    // Reset so selecting the same file again still fires onChange.
    e.target.value = "";
    if (file) onSelect(file);
  }

  return (
    <>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        onChange={handleChange}
        className="hidden"
      />
      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        disabled={disabled}
        className="flex items-center gap-1.5 px-3 py-2.5 text-sm font-medium border border-slate-200 text-slate-600 bg-white hover:bg-slate-50 transition-colors disabled:opacity-60 disabled:cursor-not-allowed shrink-0"
      >
        <span className="text-base leading-none">↑</span>
        {label}
      </button>
    </>
  );
}
