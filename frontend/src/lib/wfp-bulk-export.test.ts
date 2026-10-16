import { describe, expect, it } from "vitest";
import { FIRST_ENTERED_FISCAL_YEAR } from "./aip-fiscal-years";
import { fileNameFromDisposition, isActiveExport, isExpiredDownloadError } from "./wfp-bulk-export";
import type { InitialWfpExportJobDto, InitialWfpExportStatus } from "@/types";

function job(status: InitialWfpExportStatus): InitialWfpExportJobDto {
  return {
    id: 1, fiscalYear: FIRST_ENTERED_FISCAL_YEAR, status, done: 0, total: 3, problems: [], requestedByName: "x",
    createdAt: "2026-10-16T00:00:00Z",
  };
}

describe("isActiveExport", () => {
  it("is true only while the job still changes on its own", () => {
    expect(isActiveExport(job("Queued"))).toBe(true);
    expect(isActiveExport(job("Running"))).toBe(true);
    for (const s of ["Done", "DoneWithProblems", "Failed", "Expired"] as const) {
      expect(isActiveExport(job(s))).toBe(false);
    }
    expect(isActiveExport(null)).toBe(false);
  });
});

describe("fileNameFromDisposition", () => {
  it("reads a quoted filename", () => {
    expect(fileNameFromDisposition('attachment; filename="Initial WFP FY2028 - 2026-10-15 1430.zip"'))
      .toBe("Initial WFP FY2028 - 2026-10-15 1430.zip");
  });
  it("returns null when the header is absent", () => {
    expect(fileNameFromDisposition(undefined)).toBeNull();
    expect(fileNameFromDisposition("attachment")).toBeNull();
  });
});

describe("isExpiredDownloadError", () => {
  it("matches only a 410", () => {
    expect(isExpiredDownloadError({ response: { status: 410 } })).toBe(true);
    expect(isExpiredDownloadError({ response: { status: 409 } })).toBe(false);
    expect(isExpiredDownloadError(new Error("x"))).toBe(false);
  });
});
