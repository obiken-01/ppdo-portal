/** Shared AIP field constants — used by both Manual Entry (RAL-62) and inline edit (RAL-179). */

export const AIP_MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

export const AIP_ESRE_OPTIONS = [
  { value: "SS", label: "SS — Social Services" },
  { value: "ES", label: "ES — Economic Services" },
  { value: "ID", label: "ID — Infrastructure Development" },
  { value: "EN", label: "EN — Environment" },
];

export const AIP_SECTOR_OPTIONS = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"] as const;

// Numeric prefix each sector contributes to an office-level (5-segment) AIP ref code —
// {prefix}-000-1-{Office.OfficeRefCode}. Client-side mirror of AipSector.Prefixes on the
// backend, used only for live ref-code previews; the server computes the real value.
export const AIP_SECTOR_PREFIX: Record<string, string> = {
  GENERAL: "1000", SOCIAL: "3000", ECONOMIC: "8000", OTHERS: "9000",
};

export const AIP_FUNCTION_BANDS = ["CORE", "STRATEGIC", "SUPPORT"];
