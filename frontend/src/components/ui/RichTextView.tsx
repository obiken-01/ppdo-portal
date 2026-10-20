"use client";

// Split out of RichTextEditor.tsx (PPDO-188 / O14): read-only rendering must not pull TipTap into the
// bundle of every page that merely shows saved rich text.

import DOMPurify from "dompurify";

/**
 * The browser-side copy of the server's allow-list (ProposalRichText.AllowedTags, decision 20):
 * six tags plus <br>, no attributes. The server already sanitizes on save (PPDO-235); this is the
 * second layer, so a stored value that ever bypassed it still cannot run script here.
 */
export const RICH_TEXT_PURIFY_CONFIG = {
  ALLOWED_TAGS: ["p", "strong", "em", "ul", "ol", "li", "br"],
  ALLOWED_ATTR: [] as string[],
};

/** Saved rich text, read-only. Empty renders an em dash, as the other read-only fields do. */
export function RichTextView({ html }: { html: string | null }) {
  if (!html) return <p className="text-sm text-slate-600">—</p>;
  // DOMPurify needs a DOM. Without one (server render) it would hand the input back untouched, so
  // fail closed instead. Proposals are fetched client-side, so the server never has HTML to render.
  const clean = DOMPurify.isSupported ? DOMPurify.sanitize(html, RICH_TEXT_PURIFY_CONFIG) : "";
  return (
    <div
      className="tiptap-editor text-sm text-slate-800 leading-relaxed"
      dangerouslySetInnerHTML={{ __html: clean }}
    />
  );
}
