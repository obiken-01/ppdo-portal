// @vitest-environment jsdom
// PPDO-235 — RichTextView's browser-side DOMPurify layer, rendered the way the editor shows it.
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { RichTextView } from "./RichTextView";

const render = (html: string | null) => renderToStaticMarkup(createElement(RichTextView, { html }));
const OPEN = '<div class="tiptap-editor text-sm text-slate-800 leading-relaxed">';

describe("RichTextView", () => {
  it("keeps the allowed tags exactly", () => {
    const html =
      "<p>First <strong>bold</strong> and <em>italic</em><br>next</p>" +
      "<ul><li><p>One</p></li></ul><ol><li><p>Two</p></li></ol>";
    expect(render(html)).toBe(OPEN + html + "</div>");
  });

  it("strips script and event-handler payloads", () => {
    const out = render('<p onclick="x()">ok</p><script>alert(1)</script><img src=x onerror="alert(2)">');
    expect(out).toBe(OPEN + "<p>ok</p></div>");
  });

  it("strips every attribute and disallowed tag, keeping the text", () => {
    const out = render('<p style="color:red" class="c"><a href="javascript:alert(1)">link</a> <span>t</span></p>');
    expect(out).toBe(OPEN + "<p>link t</p></div>");
  });

  it("strips svg and math payloads", () => {
    const out = render('<svg onload="alert(1)"><script>alert(2)</script></svg><math><mi>x</mi></math><p>ok</p>');
    expect(out).not.toMatch(/<svg|<math|<script|onload/i);
    expect(out).toContain("<p>ok</p>");
  });

  it("renders an em dash when empty", () => {
    expect(render(null)).toBe('<p class="text-sm text-slate-600">—</p>');
  });
});
