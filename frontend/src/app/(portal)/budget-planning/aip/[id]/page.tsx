// Server component shell — required by Next.js static export (output: 'export').
// AIP record IDs are resolved client-side at runtime via the Azure Functions API.
export function generateStaticParams() {
  return [{ id: "__placeholder__" }];
}

export { default } from "./AipDetailClient";
