/**
 * Pure helpers for the nested AIP record tree: office -> program -> project -> activity.
 *
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change and no React
 * here -- every function below takes a record and returns a new one, which is what lets the
 * page update a node in place after an inline edit without refetching.
 *
 * ⚠️ These are immutable updates. Each returns a NEW record; mutating the argument instead
 * would leave React with an unchanged reference and the row would not re-render.
 */

import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
} from "@/types";

export function sumActivities(
  office: AipOfficeDetail,
  field: keyof Pick<AipActivityDetail, "ps" | "mooe" | "co" | "total">
): number {
  return office.programs
    .flatMap((p) => p.projects)
    .flatMap((p) => p.activities)
    .reduce((s, a) => s + (a[field] ?? 0), 0);
}

// RAL-179 — immutably replaces one activity in the nested tree after a successful inline edit.
export function replaceActivity(record: AipRecordDetail, updated: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== updated.projectId ? j : {
            ...j,
            activities: j.activities.map((a) => (a.id === updated.id ? updated : a)),
          }
        ),
      })),
    })),
  };
}

// ── Detail-page CRUD follow-up to RAL-179 — generic immutable tree updates for the other three
// levels. Every DTO already carries its immediate parent's id (officeId/programId/projectId), so
// these locate the right spot the same way replaceActivity does above — no need to thread parent
// ids through component props separately.

export function removeActivityFromTree(record: AipRecordDetail, projectId: number, activityId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== projectId ? j : { ...j, activities: j.activities.filter((a) => a.id !== activityId) }
        ),
      })),
    })),
  };
}

export function addActivityToTree(record: AipRecordDetail, projectId: number, newActivity: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== projectId ? j : { ...j, activities: [...j.activities, newActivity] }
        ),
      })),
    })),
  };
}

export function replaceProject(record: AipRecordDetail, updated: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== updated.programId ? p : { ...p, projects: p.projects.map((j) => (j.id === updated.id ? updated : j)) }
      ),
    })),
  };
}

export function removeProjectFromTree(record: AipRecordDetail, programId: number, projectId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== programId ? p : { ...p, projects: p.projects.filter((j) => j.id !== projectId) }
      ),
    })),
  };
}

export function addProjectToTree(record: AipRecordDetail, programId: number, newProject: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== programId ? p : { ...p, projects: [...p.projects, newProject] }
      ),
    })),
  };
}

export function replaceProgram(record: AipRecordDetail, updated: AipProgramDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== updated.officeId ? o : { ...o, programs: o.programs.map((p) => (p.id === updated.id ? updated : p)) }
    ),
  };
}

export function removeProgramFromTree(record: AipRecordDetail, officeId: number, programId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== officeId ? o : { ...o, programs: o.programs.filter((p) => p.id !== programId) }
    ),
  };
}

export function addProgramToTree(record: AipRecordDetail, officeId: number, newProgram: AipProgramDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== officeId ? o : { ...o, programs: [...o.programs, newProgram] }
    ),
  };
}

export function replaceOffice(record: AipRecordDetail, updated: AipOfficeDetail): AipRecordDetail {
  return { ...record, offices: record.offices.map((o) => (o.id === updated.id ? updated : o)) };
}

export function removeOfficeFromTree(record: AipRecordDetail, officeId: number): AipRecordDetail {
  return { ...record, offices: record.offices.filter((o) => o.id !== officeId) };
}

export function addOfficeToTree(record: AipRecordDetail, newOffice: AipOfficeDetail): AipRecordDetail {
  return { ...record, offices: [...record.offices, newOffice] };
}

// ── Sector grouping ────────────────────────────────────────────────────────────

/** Sector print order on the AIP form. Module-private: only groupBySector reads it. */
const SECTOR_ORDER = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"];

export function groupBySector(offices: AipOfficeDetail[]): [string, AipOfficeDetail[]][] {
  const map = new Map<string, AipOfficeDetail[]>();
  for (const o of offices) {
    const s = (o.sector ?? "OTHERS").toUpperCase();
    if (!map.has(s)) map.set(s, []);
    map.get(s)!.push(o);
  }
  const result: [string, AipOfficeDetail[]][] = [];
  for (const s of SECTOR_ORDER)           if (map.has(s)) result.push([s, map.get(s)!]);
  for (const [s, list] of Array.from(map.entries()))  if (!SECTOR_ORDER.includes(s)) result.push([s, list]);
  return result;
}

export function toggleSet<T>(prev: Set<T>, key: T): Set<T> {
  const next = new Set(prev);
  if (next.has(key)) next.delete(key); else next.add(key);
  return next;
}

export function allCollapsed(offices: AipOfficeDetail[]) {
  return {
    offices:  new Set(offices.map((o) => o.id)),
    programs: new Set(offices.flatMap((o) => o.programs).map((p) => p.id)),
    projects: new Set(offices.flatMap((o) => o.programs).flatMap((p) => p.projects).map((p) => p.id)),
  };
}
