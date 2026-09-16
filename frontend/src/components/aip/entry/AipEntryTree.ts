/**
 * Immutable edits to the loaded AIP record (PPDO-89 — lifted out of `aip/entry/page.tsx`).
 *
 * ⚠️ **These exist so a write never has to reload the record.** `load()` clears `record`, which
 * flashes the skeleton and tears down the panel the encoder is working inside — right after an
 * action they took in it. It was reported as "the page reloads when a user creates a new activity
 * or project". Every write endpoint on this page returns the node it changed precisely so the tree
 * can absorb it here instead.
 */

import type {
  AipActivityDetail, AipExpenditureWriteResult, AipProjectDetail, AipRecordDetail,
} from "@/types";

/**
 * Replaces one activity in the tree, immutably, merging `patch` over it.
 *
 * ⚠️ Exists so a save does not have to reload the record — reloading tears down the panel the
 * encoder is working in and rebuilds it under them. Found by live-testing.
 */
export function patchActivity(
  record: AipRecordDetail, activityId: number, patch: Partial<AipActivityDetail>
): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) => ({
        ...program,
        projects: program.projects.map((project) => ({
          ...project,
          activities: project.activities.map((activity) =>
            activity.id === activityId ? { ...activity, ...patch } : activity
          ),
        })),
      })),
    })),
  };
}

/**
 * Appends a newly created project to its program, immutably.
 *
 * ⚠️ `addAipProject` returns the created node carrying its own `programId`, so the tree can absorb
 * it directly — calling `load()` instead flashes the skeleton and reads as the page reloading,
 * which is what it was reported as.
 */
export function addProjectToTree(record: AipRecordDetail, project: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) =>
        program.id === project.programId
          ? { ...program, projects: [...program.projects, project] }
          : program
      ),
    })),
  };
}

/**
 * Appends a newly created activity to its project, immutably.
 *
 * ℹ️ Appended, not inserted by ref code. A new node always takes the next code in its parent's
 * sequence (`RefCodeAllocator`), so the end of the list is its sorted position — the same order a
 * reload would produce.
 */
export function addActivityToTree(record: AipRecordDetail, activity: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) => ({
        ...program,
        projects: program.projects.map((project) =>
          project.id === activity.projectId
            ? { ...project, activities: [...project.activities, activity] }
            : project
        ),
      })),
    })),
  };
}

/** The totals half of the above — what an expenditure write hands back. */
export function applyActivityTotals(
  record: AipRecordDetail, r: AipExpenditureWriteResult
): AipRecordDetail {
  return patchActivity(record, r.activityId, {
    ps: r.activityPs, mooe: r.activityMooe, co: r.activityCo, total: r.activityTotal,
    // ⚠️ Patched with the totals, not separately. Adding a line can introduce a fund and deleting
    // one can remove the last line naming a fund — neither is visible from the amounts, and the
    // tree is never reloaded, so leaving this out strands the row's fund pill on a stale value.
    fundCodes: r.activityFundCodes,
  });
}
