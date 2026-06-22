"use client";

import type { Editor } from "@tiptap/react";

const FONT_FAMILIES = [
  { label: "Arial",       value: "Arial, sans-serif" },
  { label: "Georgia",     value: "Georgia, serif" },
  { label: "Courier New", value: "'Courier New', monospace" },
  { label: "Trebuchet",   value: "'Trebuchet MS', sans-serif" },
  { label: "Verdana",     value: "Verdana, sans-serif" },
];

const TEXT_COLORS = [
  "#111827",  // near-black
  "#dc2626",  // red
  "#d97706",  // amber
  "#16a34a",  // green
  "#2563eb",  // blue
  "#9333ea",  // purple
  "#db2777",  // pink
  "#6b7280",  // slate
];

interface RichTextToolbarProps {
  editor: Editor;
}

export default function RichTextToolbar({ editor }: RichTextToolbarProps) {
  function btnCls(active: boolean) {
    return `px-2 py-1 text-xs font-medium border transition-colors ${
      active
        ? "bg-green-700 text-white border-green-700"
        : "bg-white text-slate-700 border-slate-200 hover:bg-slate-50"
    }`;
  }

  return (
    <div className="flex flex-wrap items-center gap-1 p-2 border-b border-slate-200 bg-slate-50">
      {/* Bold / Italic / Underline */}
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleBold().run()}
        className={btnCls(editor.isActive("bold"))}
        title="Bold"
      >
        <strong>B</strong>
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleItalic().run()}
        className={btnCls(editor.isActive("italic"))}
        title="Italic"
      >
        <em>I</em>
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleUnderline().run()}
        className={btnCls(editor.isActive("underline"))}
        title="Underline"
      >
        <span className="underline">U</span>
      </button>

      <span className="w-px h-5 bg-slate-200 mx-0.5" />

      {/* Headings */}
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
        className={btnCls(editor.isActive("heading", { level: 2 }))}
        title="Heading 2"
      >
        H2
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
        className={btnCls(editor.isActive("heading", { level: 3 }))}
        title="Heading 3"
      >
        H3
      </button>

      <span className="w-px h-5 bg-slate-200 mx-0.5" />

      {/* Lists */}
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleBulletList().run()}
        className={btnCls(editor.isActive("bulletList"))}
        title="Bullet list"
      >
        • List
      </button>
      <button
        type="button"
        onClick={() => editor.chain().focus().toggleOrderedList().run()}
        className={btnCls(editor.isActive("orderedList"))}
        title="Ordered list"
      >
        1. List
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
          className="ml-0.5 px-1.5 py-0.5 text-xs border border-slate-200 text-slate-400 hover:bg-slate-50"
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
        className="text-xs border border-slate-200 px-1.5 py-1 bg-white text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-500"
        title="Font family"
      >
        <option value="">Default font</option>
        {FONT_FAMILIES.map((f) => (
          <option key={f.value} value={f.value}>
            {f.label}
          </option>
        ))}
      </select>

      <span className="w-px h-5 bg-slate-200 mx-0.5" />

      {/* Clear formatting */}
      <button
        type="button"
        onClick={() =>
          editor.chain().focus().clearNodes().unsetAllMarks().run()
        }
        className="px-2 py-1 text-xs border border-slate-200 bg-white text-slate-500 hover:bg-slate-50"
        title="Clear formatting"
      >
        Clear
      </button>
    </div>
  );
}
