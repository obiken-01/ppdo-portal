/**
 * The four AIP levels, made visually distinguishable (PPDO-58).
 *
 * ⚠️ **Why this exists.** Office → Program → Project → Activity were rendered in almost the same
 * type: `text-slate-800`, near-identical weight, and ref codes all `font-mono text-xs
 * text-slate-600`. **Indentation was the only cue**, and two levels of `pl-4` is not enough to tell
 * a Program from a Project when the names are long and the screen is scrolled — which is the
 * normal state of this page. Reported from manual testing, not from a spec.
 *
 * The fix encodes level in **three reinforcing channels** rather than one, so it survives a reader
 * who has scrolled past the parent, a narrow window, and a greyscale print:
 *
 * 1. **A named chip** — the level is written down, so it is never inferred from indentation.
 * 2. **A coloured left rail** that steps down in strength with depth.
 * 3. **A typographic step-down** in the name itself (uppercase → semibold → medium → normal).
 *
 * ⚠️ **Every colour is a documented token** (`docs/DESIGN_SYSTEM.md` §1). In particular the ref-code
 * emphasis below uses **weight, not dimming**: the obvious move is to grey out the inherited
 * prefix, but the only shades faint enough to read as "inherited" are `slate-400`/`slate-500`,
 * which fail AA for content (RAL-133). A ref code is content — an encoder reads it aloud to
 * reconcile against the printed form — so the whole code stays `slate-600` and the new segment is
 * made **bolder** instead of the prefix being made fainter.
 *
 * Kept in `components/` rather than inline on the entry page because **Phase 4's review tab renders
 * the same four levels**. Two copies of this would drift, and a reviewer and an encoder disagreeing
 * about which row is a Project is exactly the confusion this removes.
 */

export type AipLevel = "office" | "program" | "project" | "activity";

/**
 * One descending green ladder, applied to three things at once.
 *
 * ⚠️ **The chip is always a step DARKER than the row it sits on**, never the same tint. Running the
 * row and its chip down the ramp together would make the chip vanish into its own background at
 * exactly the depth where the reader most needs it. So the row steps green-100 → 50 → 25 → white
 * while the chip steps green-600 → 200 → 100 → 50, and every pair keeps its contrast.
 *
 * ⚠️ Four levels, and the ramp only has three faint greens (100 / 50 / 25). The fourth step is
 * **white** — the activity — which is also correct on its own terms: it is the leaf, it sits in its
 * own bordered card, and a tint there would fight the expanded content below it.
 */
const LEVEL_STYLES: Record<AipLevel, { label: string; chip: string; rail: string; row: string }> = {
  office:   { label: "Office",   chip: "bg-green-600 text-white",     rail: "border-green-600", row: "bg-green-100" },
  program:  { label: "Program",  chip: "bg-green-200 text-green-900", rail: "border-green-400", row: "bg-green-50"  },
  project:  { label: "Project",  chip: "bg-green-100 text-green-800", rail: "border-green-200", row: "bg-green-25"  },
  // White, not a fourth tint: the activity is the leaf and carries expanded content beneath it.
  activity: { label: "Activity", chip: "bg-green-50 text-green-800",  rail: "border-slate-200", row: "bg-white"     },
};

/**
 * The level, written down. `rounded-full` is deliberate and allowed: portal pages are flat, but
 * CLAUDE.md exempts pills and badges.
 */
export function AipLevelChip({ level }: { level: AipLevel }) {
  return (
    <span
      className={`shrink-0 rounded-full px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${LEVEL_STYLES[level].chip}`}
    >
      {LEVEL_STYLES[level].label}
    </span>
  );
}

/**
 * The tinted background for a level's header row, plus its rail — the two always travel together,
 * so a caller cannot tint a row one level and rail it another.
 */
export function aipHeaderRow(level: AipLevel): string {
  return `${LEVEL_STYLES[level].row} border-l-2 ${LEVEL_STYLES[level].rail}`;
}

/**
 * A ref code with its **own** segment emphasised.
 *
 * The codes are cumulative — `…-010`, then `…-010-001`, then `…-010-001-001` — so at a glance every
 * level looks like the same long string. Bolding only the segment this node adds makes the depth
 * readable in the identifier itself, which is the one piece of text present at all four levels.
 *
 * ⚠️ Emphasis by weight, never by dimming the prefix — see this file's header.
 */
export function AipRefCode({ code, className = "" }: { code: string; className?: string }) {
  const cut = code.lastIndexOf("-");
  const prefix = cut === -1 ? "" : code.slice(0, cut + 1);
  const own = cut === -1 ? code : code.slice(cut + 1);

  return (
    <span className={`font-mono text-xs text-slate-600 ${className}`}>
      {prefix}
      <span className="font-semibold text-slate-800">{own}</span>
    </span>
  );
}
