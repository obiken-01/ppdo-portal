/**
 * Centered spinner + message, for content areas that have nothing meaningful to skeleton yet
 * (e.g. before any selector data has loaded). Matches the spinner style already used by
 * app/(portal)/loading.tsx (the route-transition fallback) so the two feel like one system.
 *
 * NOT a general replacement for section/page skeletons — once a page has a known final shape
 * (a table, a form with a fixed number of fields), prefer a layout-preserving skeleton instead
 * (see the WFP entry/Allocation/Report pages) so swapping loading -> loaded content doesn't
 * shift the page (CLS). Give this a `minHeightClassName` matching the content it replaces so
 * that swap doesn't jump either.
 */
export default function LoadingState({
  message,
  minHeightClassName = "min-h-24",
  className = "",
}: {
  message: string;
  /** Tailwind min-height class for the container, so swapping to loaded content doesn't shift the page. */
  minHeightClassName?: string;
  className?: string;
}) {
  return (
    <div className={`flex flex-col items-center justify-center gap-3 ${minHeightClassName} ${className}`}>
      <div className="w-8 h-8 border-[3px] border-green-600 border-t-transparent rounded-full animate-spin" />
      <p className="text-sm text-slate-600">{message}</p>
    </div>
  );
}
