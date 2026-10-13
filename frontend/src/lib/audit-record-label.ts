/**
 * PPDO-110 — what the Audit Log's Record column shows. An AIP program, project or activity reads as
 * its ref code (`1000-000-1-01-010-001-001`), with its name and id on hover; a database id means
 * nothing to a reader. Every other table keeps `#id`, users keep the first GUID segment.
 *
 * The server resolves the code (`AuditRecordCodeResolver`): the live row first, then the audit row's
 * own snapshot. No code → `#id`, which is the behaviour before PPDO-110, so never blank.
 */
export interface AuditRecordRef {
  recordId: number | null;
  recordGuid: string | null;
  recordCode?: string | null;
  recordName?: string | null;
}

export function auditRecordLabel(entry: AuditRecordRef): { text: string; title: string | undefined } {
  const id = entry.recordId != null ? `#${entry.recordId}` : null;
  if (entry.recordCode) {
    return { text: entry.recordCode, title: entry.recordName ? `${entry.recordName}${id ? ` (${id})` : ""}` : id ?? undefined };
  }
  if (id) return { text: id, title: undefined };
  if (entry.recordGuid != null) return { text: `#${entry.recordGuid.split("-")[0]}`, title: undefined };
  return { text: "—", title: undefined };
}
