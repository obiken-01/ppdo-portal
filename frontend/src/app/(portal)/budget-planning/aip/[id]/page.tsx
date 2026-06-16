// Server component shell — required by Next.js static export (output: 'export').
// generateStaticParams must live in a Server Component (no "use client").
// The actual UI is in AipDetailClient.tsx (client component).
export function generateStaticParams() {
  return [{ id: "__placeholder__" }];
}

import AipDetailClient from "./AipDetailClient";

export default function AipDetailPage({ params }: { params: { id: string } }) {
  return <AipDetailClient params={params} />;
}
