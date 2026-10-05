"use client";

/**
 * Rich text, shared (Demo 2.15 — PPDO-160). Two exports:
 *
 * - `RichTextToolbar` — the Announcements toolbar, moved here unchanged as the **"full"** variant, plus
 *   a **"basic"** variant with only bold, italic and the two lists.
 * - `RichTextEditor` (default) — a controlled TipTap field restricted to the investment proposal's
 *   allow-list (`Investment_Proposal_Spec.md` decision 20: p, strong, em, ul, ol, li, br).
 *
 * ⚠️ **The restriction is in the schema, not only the toolbar.** StarterKit's other nodes and marks
 * (headings, quotes, code, strike, underline, links, rules) are switched off, so a paste carrying a
 * table or a heading arrives as plain paragraphs instead of markup the toolbar merely cannot make.
 * The server sanitizes to the same list anyway (`ProposalRichText`); this keeps the screen honest
 * about what will be kept.
 *
 * `RichTextView` renders saved HTML read-only with the same list styles. The HTML is the server's
 * sanitized output, never raw input.
 */

import { useEffect } from "react";
import { EditorContent, useEditor, useEditorState, type Editor } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";

const FONT_FAMILIES = [
  { label: "Arial",       value: "Arial, sans-serif" },
  { label: "Georgia",     value: "Georgia, serif" },
  { label: "Courier New", value: "'Courier New', monospace" },
  { label: "Trebuchet",   value: "'Trebuchet MS', sans-serif" },
  { label: "Verdana",     value: "Verdana, sans-serif" },
];

const TEXT_COLORS = [
  "#111827",
  "#dc2626",
  "#d97706",
  "#16a34a",
  "#2563eb",
  "#9333ea",
  "#db2777",
  "#6b7280",
];

interface RichTextToolbarProps {
  editor: Editor;
  /** "full" is the Announcements toolbar; "basic" is bold, italic and lists only. */
  variant?: "full" | "basic";
}

export function RichTextToolbar({ editor, variant = "full" }: RichTextToolbarProps) {
  // TipTap 3 does not re-render on every transaction. Toggling bold on an empty selection only
  // changes the stored marks, not the document, so without subscribing here the button would not
  // light up until the next keystroke.
  const active = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e.isActive("bold"),
      italic: e.isActive("italic"),
      underline: e.isActive("underline"),
      bulletList: e.isActive("bulletList"),
      orderedList: e.isActive("orderedList"),
    }),
  });

  function btnCls(active: boolean) {
    return `px-2 py-1 text-xs font-medium border transition-colors ${
      active
        ? "bg-green-700 text-white border-green-700"
        : "bg-white text-slate-800 border-slate-200 hover:bg-slate-50"
    }`;
  }

  const full = variant === "full";

  return (
    <div className="flex flex-wrap items-center gap-1 p-2 border-b border-slate-200 bg-slate-50">

      {/* Bold / Italic / Underline */}
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleBold().run()}
        className={btnCls(active.bold)}
        title="Bold (Ctrl+B)"
      >
        <strong>B</strong>
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleItalic().run()}
        className={btnCls(active.italic)}
        title="Italic (Ctrl+I)"
      >
        <em>I</em>
      </button>
      {full && (
        <button
          type="button"
          onClick={() => editor.chain().focus().toggleUnderline().run()}
          className={btnCls(active.underline)}
          title="Underline (Ctrl+U)"
        >
          <span className="underline">U</span>
        </button>
      )}

      <span className="w-px h-5 bg-slate-200 mx-0.5" />

      {/* Lists */}
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleBulletList().run()}
        className={btnCls(active.bulletList)}
        title="Bullet list"
      >
        • List
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleOrderedList().run()}
        className={btnCls(active.orderedList)}
        title="Ordered list"
      >
        1. List
      </button>

      {full && (
        <>
          <span className="w-px h-5 bg-slate-200 mx-0.5" />

          {/* Indentation */}
          <button
            type="button"
            onClick={() => editor.chain().focus().increaseIndent().run()}
            className={btnCls(false)}
            title="Increase indent (Tab)"
          >
            →
          </button>
          <button
            type="button"
            onClick={() => editor.chain().focus().decreaseIndent().run()}
            className={btnCls(false)}
            title="Decrease indent (Shift+Tab)"
          >
            ←
          </button>

          <span className="w-px h-5 bg-slate-200 mx-0.5" />

          {/* Text color swatches */}
          <div className="flex items-center gap-0.5">
            {TEXT_COLORS.map((color) => (
              <button
                key={color}
                type="button"
                onClick={() => editor.chain().focus().setColor(color).run()}
                className="w-5 h-5 border border-slate-300 hover:scale-110 transition-transform flex-shrink-0"
                style={{ backgroundColor: color }}
                title={`Color ${color}`}
              />
            ))}
            <button
              type="button"
              onClick={() => editor.chain().focus().unsetColor().run()}
              className="ml-0.5 px-1.5 py-0.5 text-xs border border-slate-200 text-slate-600 hover:bg-slate-50"
              title="Remove color"
            >
              ✕
            </button>
          </div>

          <span className="w-px h-5 bg-slate-200 mx-0.5" />

          {/* Font family */}
          <select
            onChange={(e) => {
              if (e.target.value) {
                editor.chain().focus().setFontFamily(e.target.value).run();
              } else {
                editor.chain().focus().unsetFontFamily().run();
              }
            }}
            defaultValue=""
            className="text-xs border border-slate-200 px-1.5 py-1 bg-white text-slate-600 focus:outline-none focus:ring-1 focus:ring-green-500"
            title="Font family"
          >
            <option value="">Default font</option>
            {FONT_FAMILIES.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
        </>
      )}

      <span className="w-px h-5 bg-slate-200 mx-0.5" />

      {/* Clear formatting */}
      <button
        type="button"
        onClick={() => editor.chain().focus().clearNodes().unsetAllMarks().run()}
        className="px-2 py-1 text-xs border border-slate-200 bg-white text-slate-600 hover:bg-slate-50"
        title="Clear formatting"
      >
        Clear
      </button>
    </div>
  );
}

/** StarterKit with everything outside decision 20's allow-list switched off. */
const BASIC_EXTENSIONS = [
  StarterKit.configure({
    heading: false,
    blockquote: false,
    code: false,
    codeBlock: false,
    horizontalRule: false,
    strike: false,
    underline: false,
    link: false,
  }),
];

/** TipTap's empty document, which the server stores as null. */
function normalise(html: string): string | null {
  return html === "<p></p>" ? null : html;
}

export default function RichTextEditor({
  value, onChange, id, minHeight = 120, ariaLabel,
}: {
  value: string | null;
  onChange: (html: string | null) => void;
  id?: string;
  minHeight?: number;
  ariaLabel?: string;
}) {
  const editor = useEditor({
    extensions: BASIC_EXTENSIONS,
    content: value ?? "",
    // A static export renders on the client only; this avoids a hydration mismatch warning.
    immediatelyRender: false,
    editorProps: {
      attributes: {
        class: "tiptap-editor p-3 focus:outline-none text-sm text-slate-800 leading-relaxed",
        style: `min-height:${minHeight}px`,
        ...(id ? { id } : {}),
        ...(ariaLabel ? { "aria-label": ariaLabel } : {}),
      },
    },
    onUpdate: ({ editor: e }) => onChange(normalise(e.getHTML())),
  });

  // Discard / reload replace the value from outside; follow it without a loop on our own edits.
  useEffect(() => {
    if (!editor) return;
    const current = normalise(editor.getHTML());
    if ((value ?? null) !== current) editor.commands.setContent(value ?? "", { emitUpdate: false });
  }, [editor, value]);

  return (
    <div className="border border-slate-300 bg-white focus-within:ring-1 focus-within:ring-green-600">
      {editor && <RichTextToolbar editor={editor} variant="basic" />}
      <EditorContent editor={editor} />
    </div>
  );
}
