"use client";

/**
 * The Program level of AIP Entry's drill-down (PPDO-89, spec decision 3).
 *
 * Header, then a compact list of the program's projects, then + Add project. **Names and totals
 * only — never fields.** The child list exists so an encoder can find the row they want without a
 * search; the moment it starts carrying editable data it is the tree again.
 */

import { useMemo } from "react";
import type {
  AipCommentNodeType, AipProgramDetail, AipProjectDetail,
} from "@/types";
import { addAipProject } from "@/lib/aip";
import { AipCommentAnchor } from "./AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "./AipHierarchy";
import { AipFigureStrip, sumActivityAmounts } from "./AipRowFigures";
import {
  AipChildList, AipChildRow, AipInlineAdd, AipPanel,
} from "./AipEntryPanelParts";

export default function AipProgramPanel({
  program, canEdit, lockedReason, unresolvedCount, onSelectProject, onProjectAdded,
}: {
  program: AipProgramDetail;
  canEdit: boolean;
  /** Who holds the work, when this office cannot edit. Shown in place of the add control. */
  lockedReason: string;
  unresolvedCount: (nodeType: AipCommentNodeType, nodeId: number) => number;
  onSelectProject: (projectId: number) => void;
  onProjectAdded: (project: AipProjectDetail) => void;
}) {
  // ℹ️ The figures here are navigation: they tell an encoder whether the program they just picked is
  // the one carrying the money they are looking for, before they open a project.
  //
  // ↩️ This said "a program prints BLANK in the form's money columns" — true until PPDO-98 put
  // subtotals on the program and project rows. The strip is still not the printed figure, though:
  // this sums amounts **as encoded**, and the sheet rounds up and uplifts MOOE and CO.
  const amounts = useMemo(
    () => sumActivityAmounts(program.projects.flatMap((p) => p.activities)),
    [program]
  );

  return (
    <AipPanel>
      <div className={`px-3 py-2 ${aipHeaderRow("program")}`}>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <AipLevelChip level="program" />
              {/* The FULL code at the header — this is the one the encoder reads aloud against
                  the printed form. The child rows below print only their own segment. */}
              <AipRefCode code={program.refCode} />
            </div>
            <p className="mt-0.5 text-sm font-semibold text-slate-800">{program.name}</p>
          </div>
          <AipFigureStrip amounts={amounts} />
        </div>
        <AipCommentAnchor nodeType="Program" nodeId={program.id} />
      </div>

      <AipChildList
        title="Projects"
        count={program.projects.length}
        emptyText="No projects yet."
        footer={
          <AipInlineAdd
            label="+ Add project"
            placeholder="Project name"
            disabled={!canEdit}
            disabledReason={`With ${lockedReason} — projects cannot be added here.`}
            onAdd={async (name) => onProjectAdded(await addAipProject(program.id, { name }))}
          />
        }
      >
        {program.projects.map((project) => (
          <AipChildRow
            key={project.id}
            refCode={project.refCode}
            name={project.name}
            total={sumActivityAmounts(project.activities).total}
            unresolved={unresolvedCount("Project", project.id)}
            onSelect={() => onSelectProject(project.id)}
          />
        ))}
      </AipChildList>
    </AipPanel>
  );
}
