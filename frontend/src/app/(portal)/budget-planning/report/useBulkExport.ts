"use client";

/**
 * State of the bulk initial WFP export for one fiscal year (PPDO-230).
 *
 * Restores the year's latest job on load, queues a new one, downloads the ZIP, and polls the job
 * every 3 s **only** while it is Queued or Running and the tab is visible (spec §6). No push — the
 * poll is the whole mechanism.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import {
  downloadBulkExport, getBulkExport, getLatestBulkExport, isActiveExport, isExpiredDownloadError,
  requestBulkExport,
} from "@/lib/wfp-bulk-export";
import { wfpErrorMessage } from "@/lib/wfp";
import type { InitialWfpExportJobDto } from "@/types";

const POLL_MS = 3000;

export interface BulkExportState {
  /** The year's latest job; `null` when it has none (or until the first load answers). */
  job: InitialWfpExportJobDto | null;
  /** True until the first GET for this year answers. */
  loading: boolean;
  requesting: boolean;
  downloading: boolean;
  /** The last refused request or download, in the API's own words. */
  error: string | null;
  request: () => Promise<void>;
  download: () => Promise<void>;
  dismissError: () => void;
}

export function useBulkExport(fiscalYear: number | null, enabled: boolean): BulkExportState {
  const [job, setJob] = useState<InitialWfpExportJobDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [requesting, setRequesting] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // The year a response was asked for — a slow answer for a year the reader has since left is dropped.
  const yearRef = useRef<number | null>(null);
  yearRef.current = enabled ? fiscalYear : null;

  // ── Restore the latest job when the year (or eligibility) changes ─────────
  useEffect(() => {
    setJob(null);
    setError(null);
    if (!enabled || fiscalYear == null) { setLoading(false); return; }
    let cancelled = false;
    setLoading(true);
    getLatestBulkExport(fiscalYear)
      .then((latest) => { if (!cancelled) setJob(latest); })
      .catch((err) => {
        if (!cancelled) setError(wfpErrorMessage(err, "Could not load the bulk export status."));
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [enabled, fiscalYear]);

  // ── Poll while Queued/Running and the tab is visible ──────────────────────
  const activeId = isActiveExport(job) ? job?.id ?? null : null;
  useEffect(() => {
    if (activeId == null) return;
    let cancelled = false;
    let inFlight = false;

    async function poll(id: number) {
      if (cancelled || inFlight || document.visibilityState !== "visible") return;
      inFlight = true;
      try {
        const next = await getBulkExport(id);
        if (!cancelled && next.fiscalYear === yearRef.current) setJob(next);
      } catch {
        // A blip (cold start, refresh in flight) — the next tick tries again.
      } finally {
        inFlight = false;
      }
    }

    const timer = setInterval(() => void poll(activeId), POLL_MS);
    // Coming back to the tab catches up at once rather than waiting out the interval.
    const onVisible = () => { if (document.visibilityState === "visible") void poll(activeId); };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      cancelled = true;
      clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [activeId]);

  const request = useCallback(async () => {
    if (fiscalYear == null) return;
    setRequesting(true);
    setError(null);
    try {
      const next = await requestBulkExport(fiscalYear);
      if (yearRef.current === fiscalYear) setJob(next);
    } catch (err) {
      setError(wfpErrorMessage(err, "Could not start the export."));
    } finally {
      setRequesting(false);
    }
  }, [fiscalYear]);

  const download = useCallback(async () => {
    if (job == null) return;
    setDownloading(true);
    setError(null);
    try {
      await downloadBulkExport(job);
    } catch (err) {
      if (isExpiredDownloadError(err)) {
        // 410 — the file is gone; show the Expired state rather than an error.
        setJob({ ...job, status: "Expired" });
      } else {
        setError(wfpErrorMessage(err, "The ZIP could not be downloaded."));
      }
    } finally {
      setDownloading(false);
    }
  }, [job]);

  const dismissError = useCallback(() => setError(null), []);

  return { job, loading, requesting, downloading, error, request, download, dismissError };
}
