# Investment Proposal — wireframes

The design canvas for Demo 2.15 (PPDO-153), drawn on 2026-09-30/10-01 before any screen was built:
**https://claude.ai/artifact/MU8EjU5diyYuWXqMEMQDiw** (private: share it from the page's Share
menu before sending the link to anyone else).

The PDC asked for the proposal's sections to be separate, collapsible, and to save on their own.
The decisions that came out of it are spec decisions 24 (revised), 28 and 29, §6.2 (revised) and
§6.4 in [../../Investment_Proposal_Spec.md](../../Investment_Proposal_Spec.md).

## What is here

| File | Artboard |
|---|---|
| `FocusMode.dc.html` | **1 · The editor as it opens.** One section at a time (shown on G, the work plan), the Fiscal year → Program → Project selector, Open in AIP Entry, the section rail with status dots, and per-section Save / Discard |
| `Main.dc.html` | **2 · The alternative view.** Every section as a collapsible card with a one-line summary, Expand all / Collapse all, Save all (n) |
| `SectionStates.dc.html` | **3 · Section card states.** Saved, folded with unsaved edits, editing, validation error, from the AIP (no Save), Final, and the save conflict |
| `AipEntryProject.dc.html` | **4 · AIP Entry's project panel.** The Investment proposal strip in each state (None, Draft, Final, reviewer) |
| `canvas.json` | Artboard positions and titles |

Sample data is OPA / FY 2028-shaped and is not real. Colours and spacing are the portal's tokens
(`frontend/tailwind.config.ts`), flat, with no rounded cards.

## Rebuilding the canvas

The `.dc.html` files are the source of truth. Edit them here and republish to the canvas above
through the Design artifact type (the same way the AIP Review wireframes are kept).

## Decided from these (Ralph, 2026-10-01)

1. A Save per section on one saved document (one PUT, one row version).
2. Leaving a section with unsaved edits asks **Save and continue · Discard · Keep editing**.
3. The editor opens **one section at a time**.
4. Sections that read another section show what is on screen; the export uses saved data.
5. Ways in: the list page, the editor's own selector, and the AIP Entry strip.
