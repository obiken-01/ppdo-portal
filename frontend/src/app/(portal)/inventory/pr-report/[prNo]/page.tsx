// TODO RAL-xx: PR Report — matches Penpot frame "07 PR Report"
// Section 1 (header), Section 2 (line items), Section 3 (distribution) + Excel export
// Route param: prNo — the PR number string (e.g. 101-1041-GF-2026-04-28-757)

// Required for Next.js static export with output: 'export'.
// Returns a placeholder so the dynamic segment is recognized at build time.
// Real PR numbers are resolved client-side at runtime via the Azure Functions API.
export function generateStaticParams() {
  return [{ prNo: "__placeholder__" }];
}

export default function PRReportPage() {
  return null;
}
