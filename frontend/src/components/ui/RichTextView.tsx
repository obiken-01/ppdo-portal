"use client";

// Split out of RichTextEditor.tsx (PPDO-188 / O14): read-only rendering must not pull TipTap into the
// bundle of every page that merely shows saved rich text.

/** Saved rich text, read-only. Empty renders an em dash, as the other read-only fields do. */
export function RichTextView({ html }: { html: string | null }) {
  if (!html) return <p className="text-sm text-slate-600">—</p>;
  return (
    <div
      className="tiptap-editor text-sm text-slate-800 leading-relaxed"
      // Server-sanitized to p/strong/em/ul/ol/li/br (ProposalRichText); never raw input.
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}
