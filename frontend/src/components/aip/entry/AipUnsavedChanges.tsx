"use client";

/**
 * AIP Entry's names for the shared unsaved-changes guard (PPDO-166). The guard itself moved to
 * `components/ui/UnsavedChanges.tsx` in PPDO-160 so the investment proposal editor could reuse it;
 * these aliases keep every AIP Entry import as it was. AIP Entry passes no `onSaveAll`, so its dialog
 * is still Discard / Keep editing.
 */

export {
  UnsavedChangesProvider as AipUnsavedChangesProvider,
  useUnsavedChange as useAipUnsavedChange,
  useLeaveGuard as useAipLeaveGuard,
} from "@/components/ui/UnsavedChanges";
