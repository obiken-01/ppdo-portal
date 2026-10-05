/**
 * Placeholder for the lazy-loaded RichTextEditor (PPDO-188 / O14): same border, toolbar strip and
 * body height as the loaded editor, so the page does not shift when TipTap arrives.
 */
const TOOLBAR_PX = 43; // "basic" toolbar incl. its bottom border, measured

export default function RichTextEditorSkeleton({ minHeight = 120 }: { minHeight?: number }) {
  return (
    <div className="border border-slate-300 bg-white" aria-hidden="true">
      <div className="border-b border-slate-200 bg-slate-50 animate-pulse" style={{ height: TOOLBAR_PX }} />
      {/* EditorContent body: the editor sets min-height on its padded (border-box) element */}
      <div style={{ minHeight }} />
    </div>
  );
}
