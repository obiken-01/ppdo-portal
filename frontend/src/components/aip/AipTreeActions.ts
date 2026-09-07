/**
 * The callback contract every AIP tree row consumes.
 *
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). Type-only, no behaviour.
 *
 * ⚠️ Kept as a plain prop, deliberately. Converting it to context would change how every row
 * gets its callbacks, which is a redesign and not this extraction's business.
 */

import type { ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type {
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
  FundingSourceResponse,
} from "@/types";

// ── Detail-page CRUD follow-up to RAL-179 — Office/Program/Project add+edit+delete ──
// Bundles every tree-mutation callback into one object so ProjectRow/ProgramRow/OfficeRow don't
// each need a dozen individual props; ActivityRow (above) keeps its own explicit prop list
// unchanged since it predates this and already works.

export interface TreeActions {
  aipRecordId: number;
  canEdit: boolean;
  /**
   * V18-41 — why programs cannot be typed in for this record's fiscal year, or null when
   * they can. Computed once from the record rather than per row: every office in a record
   * shares its year, so asking per row would be the same answer N times.
   */
  programsLdipOnly: string | null;
  fundingSources: FundingSourceResponse[];
  onRequestConfirm: (props: ConfirmDialogProps) => void;
  onOfficeUpdated: (updated: AipOfficeDetail) => void;
  onOfficeDeleted: (officeId: number) => void;
  onProgramAdded: (officeId: number, newProgram: AipProgramDetail) => void;
  onProgramUpdated: (updated: AipProgramDetail) => void;
  onProgramDeleted: (officeId: number, programId: number) => void;
  onProjectAdded: (programId: number, newProject: AipProjectDetail) => void;
  onProjectUpdated: (updated: AipProjectDetail) => void;
  onProjectDeleted: (programId: number, projectId: number) => void;
  onActivityAdded: (projectId: number, newActivity: AipActivityDetail) => void;
  onActivitySaved: (updated: AipActivityDetail) => void;
  onActivityDeleted: (projectId: number, activityId: number) => void;
}
