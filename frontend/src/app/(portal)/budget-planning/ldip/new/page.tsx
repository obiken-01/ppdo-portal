"use client";

/**
 * LDIP create page (RAL-61) — thin wrapper around the shared LdipForm.
 * On first Save Draft the form creates the record and routes to its edit page.
 */

import LdipForm from "../LdipForm";

export default function LdipNewPage() {
  return <LdipForm />;
}
