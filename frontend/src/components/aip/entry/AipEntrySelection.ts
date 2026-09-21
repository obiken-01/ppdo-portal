/**
 * Which program, project and activity AIP Entry is working on (PPDO-89).
 *
 * ⚠️ **Pure, and deliberately kept out of the page.** The selection arrives from the URL, where it
 * can name a row that has since been deleted, was never this office's, or belongs to a different
 * fiscal year than the one on screen. Resolving that against the loaded tree is the one piece of
 * this page's logic with real branching, and it is the piece a reader most needs to be able to
 * check without reading a 700-line component (spec §11 — "the fallback is pure; extract it").
 *
 * ⚠️ **A stale id falls back to the deepest ancestor that still resolves, never to nothing.** An
 * encoder who reloads onto a deleted activity is one keystroke from the work they were doing; one
 * dropped to an empty picker has to find their way back down three levels. The notice says which
 * level went.
 */

import type {
  AipActivityDetail, AipOfficeDetail, AipProgramDetail, AipProjectDetail,
} from "@/types";

/**
 * A program together with the sub-office group it sits in.
 *
 * ⚠️ The group travels with the program rather than being picked separately (decision 2). Most
 * offices have exactly one group, and a fourth lookup would add a step for all of them to
 * disambiguate for the few; the group is shown as a subtitle on the option instead.
 */
export interface AipProgramOption {
  program: AipProgramDetail;
  group: AipOfficeDetail;
}

/** What the URL carries. Any of the three may be absent or stale. */
export interface AipSelectionIds {
  programId: number | null;
  projectId: number | null;
  activityId: number | null;
}

export interface AipResolvedSelection {
  group: AipOfficeDetail | null;
  program: AipProgramDetail | null;
  project: AipProjectDetail | null;
  activity: AipActivityDetail | null;
  /** The ids that actually resolved — what the URL should be rewritten to. */
  ids: AipSelectionIds;
  /** Set only when an id was dropped, so the page can say so rather than silently moving. */
  notice: string | null;
}

export const EMPTY_SELECTION_IDS: AipSelectionIds = {
  programId: null, projectId: null, activityId: null,
};

/** Every program the reader may work on, flattened across their office's groups. */
export function listAipProgramOptions(groups: readonly AipOfficeDetail[]): AipProgramOption[] {
  return groups.flatMap((group) => group.programs.map((program) => ({ program, group })));
}

/**
 * The last dash-segment of a ref code — `…-001-002-003` → `003`.
 *
 * ⚠️ For CHILD lists and lookup options only, never for a panel header. Siblings share their whole
 * prefix, so repeating it down a list is 20 characters of noise per row that the eye has to strip
 * to compare the one segment that differs (RAL-141, the same call the WFP picker makes). The panel
 * header prints the full code, because that is the one an encoder reads aloud against the form.
 */
export function aipRefSegment(refCode: string): string {
  const parts = refCode.split("-");
  return parts[parts.length - 1] || refCode;
}

/**
 * Resolves URL ids against the loaded tree, falling back down the ancestors.
 *
 * `ids` that resolve are returned unchanged with no notice — the common case, which must not
 * produce a rewrite of the URL or a message.
 */
export function resolveAipSelection(
  groups: readonly AipOfficeDetail[],
  ids: AipSelectionIds
): AipResolvedSelection {
  const none: AipResolvedSelection = {
    group: null, program: null, project: null, activity: null,
    ids: EMPTY_SELECTION_IDS, notice: null,
  };

  if (ids.programId == null) return none;

  const option = listAipProgramOptions(groups).find((o) => o.program.id === ids.programId) ?? null;
  if (!option) {
    // ⚠️ "Is not in" rather than "was deleted": a program the reader cannot see is far more often
    // one belonging to another division or another fiscal year than one that has gone (programs
    // are the LDIP's and are not deleted here).
    return { ...none, notice: "That program is not in this fiscal year’s AIP for your office." };
  }

  const base = {
    group: option.group,
    program: option.program,
    project: null,
    activity: null,
    ids: { programId: option.program.id, projectId: null, activityId: null },
  } satisfies Omit<AipResolvedSelection, "notice">;

  if (ids.projectId == null) return { ...base, notice: null };

  const project = option.program.projects.find((p) => p.id === ids.projectId) ?? null;
  if (!project) return { ...base, notice: "That project no longer exists." };

  const withProject = {
    ...base,
    project,
    ids: { programId: option.program.id, projectId: project.id, activityId: null },
  };

  if (ids.activityId == null) return { ...withProject, notice: null };

  const activity = project.activities.find((a) => a.id === ids.activityId) ?? null;
  if (!activity) return { ...withProject, notice: "That activity no longer exists." };

  return {
    ...withProject,
    activity,
    ids: { programId: option.program.id, projectId: project.id, activityId: activity.id },
    notice: null,
  };
}

/**
 * The ids that select one node, given the tree — used by everything that names a node without
 * knowing where it sits: a checklist issue (activity id only), an unresolved comment (node type
 * and id), a newly created row.
 *
 * Returns null when the node is not in the loaded tree, which the caller renders as a notice
 * rather than as a selection that silently does nothing.
 */
export function idsForAipNode(
  groups: readonly AipOfficeDetail[],
  nodeType: "Program" | "Project" | "Activity",
  nodeId: number
): AipSelectionIds | null {
  for (const group of groups) {
    for (const program of group.programs) {
      if (nodeType === "Program" && program.id === nodeId) {
        return { programId: program.id, projectId: null, activityId: null };
      }
      for (const project of program.projects) {
        if (nodeType === "Project" && project.id === nodeId) {
          return { programId: program.id, projectId: project.id, activityId: null };
        }
        if (nodeType === "Activity") {
          const activity = project.activities.find((a) => a.id === nodeId);
          if (activity) {
            return { programId: program.id, projectId: project.id, activityId: activity.id };
          }
        }
      }
    }
  }
  return null;
}
