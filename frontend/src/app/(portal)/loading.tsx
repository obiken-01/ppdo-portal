// Shown inside the portal shell while a page chunk or RSC payload is loading.
// Next.js transitions here immediately on click — so navigation feels instant
// even if the destination chunk hasn't finished prefetching yet.
export default function PortalLoading() {
  return (
    <div className="flex items-center justify-center h-full">
      <div className="w-6 h-6 border-[3px] border-green-600 border-t-transparent rounded-full animate-spin" />
    </div>
  );
}
