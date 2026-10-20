// PPDO-235 — without a DOM (server render) DOMPurify would return its input unchanged, so
// RichTextView must render nothing rather than unsanitized HTML. Runs in the default node environment.
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { expect, it } from "vitest";
import { RichTextView } from "./RichTextView";

it("fails closed when there is no DOM", () => {
  const out = renderToStaticMarkup(createElement(RichTextView, { html: "<p>x</p><script>alert(1)</script>" }));
  expect(out).toBe('<div class="tiptap-editor text-sm text-slate-800 leading-relaxed"></div>');
});
