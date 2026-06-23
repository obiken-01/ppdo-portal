import { Extension } from "@tiptap/core";

declare module "@tiptap/core" {
  interface Commands<ReturnType> {
    indent: {
      increaseIndent: () => ReturnType;
      decreaseIndent: () => ReturnType;
    };
  }
}

const BLOCK_TYPES = ["paragraph", "heading", "blockquote"];
const MAX_INDENT  = 7;
const INDENT_REM  = 2;

export const Indent = Extension.create({
  name: "indent",

  addGlobalAttributes() {
    return [
      {
        types: BLOCK_TYPES,
        attributes: {
          indent: {
            default: 0,
            parseHTML: (el) => {
              const val = el.getAttribute("data-indent");
              return val ? Math.max(0, Math.min(MAX_INDENT, parseInt(val, 10))) : 0;
            },
            renderHTML: (attrs) => {
              if (!attrs.indent) return {};
              return {
                "data-indent": attrs.indent,
                style: `margin-left: ${(attrs.indent as number) * INDENT_REM}rem`,
              };
            },
          },
        },
      },
    ];
  },

  addCommands() {
    return {
      increaseIndent:
        () =>
        ({ tr, state, dispatch }) => {
          const { from, to } = state.selection;
          state.doc.nodesBetween(from, to, (node, pos) => {
            if (BLOCK_TYPES.includes(node.type.name)) {
              const current = (node.attrs.indent as number) ?? 0;
              if (current < MAX_INDENT) {
                tr.setNodeMarkup(pos, undefined, { ...node.attrs, indent: current + 1 });
              }
            }
          });
          if (dispatch) dispatch(tr);
          return true;
        },

      decreaseIndent:
        () =>
        ({ tr, state, dispatch }) => {
          const { from, to } = state.selection;
          state.doc.nodesBetween(from, to, (node, pos) => {
            if (BLOCK_TYPES.includes(node.type.name)) {
              const current = (node.attrs.indent as number) ?? 0;
              if (current > 0) {
                tr.setNodeMarkup(pos, undefined, { ...node.attrs, indent: current - 1 });
              }
            }
          });
          if (dispatch) dispatch(tr);
          return true;
        },
    };
  },

  addKeyboardShortcuts() {
    return {
      Tab:       () => this.editor.commands.increaseIndent(),
      "Shift-Tab": () => this.editor.commands.decreaseIndent(),
    };
  },
});
