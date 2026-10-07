import { describe, expect, it } from "vitest";
import { auditRecordLabel } from "./audit-record-label";

const base = { recordId: null, recordGuid: null, recordCode: null, recordName: null };

describe("auditRecordLabel (PPDO-110)", () => {
  it("shows an AIP row's ref code, with its name for the hover title", () => {
    expect(auditRecordLabel({ ...base, recordId: 5, recordCode: "1000-000-1-01-010-001-001-001", recordName: "Conduct of LDC meetings" }))
      .toEqual({ text: "1000-000-1-01-010-001-001-001", title: "Conduct of LDC meetings (#5)" });
  });

  it("falls back to #id when there is no ref code, exactly as before", () => {
    expect(auditRecordLabel({ ...base, recordId: 36 })).toEqual({ text: "#36", title: undefined });
  });

  it("keeps the id in the title when there is a code but no name", () => {
    expect(auditRecordLabel({ ...base, recordId: 5, recordCode: "X-1" })).toEqual({ text: "X-1", title: "#5" });
  });

  it("users (RecordGuid) keep the first GUID segment", () => {
    expect(auditRecordLabel({ ...base, recordGuid: "0dfc172f-05f2-43ca-ab9c-39eb50c3eb9f" }))
      .toEqual({ text: "#0dfc172f", title: undefined });
  });

  it("never blank: a row with nothing to name it by reads as a dash", () => {
    expect(auditRecordLabel(base)).toEqual({ text: "—", title: undefined });
  });

  it("an older API without the new fields still works", () => {
    expect(auditRecordLabel({ recordId: 7, recordGuid: null })).toEqual({ text: "#7", title: undefined });
  });
});
