"use client";

/**
 * RecordCodeLink — a record's identifier, used as its row's one click target (PPDO-105).
 *
 * The portal rule for list tables (`docs/DESIGN_SYSTEM.md` §5): **the identifier is the link, and
 * nothing else in the row is.** It came from the AIP Review search, where activity names can run to
 * several lines — a long wrapped name is a poor click target, while a reference code is short, the
 * same width on every row, and what people read against the printed form anyway.
 *
 * ⚠️ **Never truncated or wrapped.** An identifier cut to `1000-000-1-01-…` is a different code to the
 * reader. The column grows or the table scrolls; the code stays whole.
 *
 * ⚠️ **One link per row, so the text beside it is plain.** Two links to one destination are noise to a
 * keyboard or screen-reader user. Pass `description` so this link's accessible name says what it
 * opens — the code alone does not.
 *
 * Named generically on purpose: a PR No. or a Stock No. is the same shape as an AIP reference code.
 */

import Link from "next/link";

/** The link colour the Annex B grid already uses for its activity buttons — one look for "opens". */
const LINK_CLS =
  "text-green-800 underline decoration-green-200 underline-offset-2 hover:decoration-green-700 " +
  "focus:outline-none focus-visible:ring-1 focus-visible:ring-green-600";

export default function RecordCodeLink({
  code,
  href,
  onClick,
  description,
  emphasizeLastSegment = true,
  separator = "-",
  className = "",
}: {
  code: string;
  /** Navigate here. Takes precedence over `onClick`. */
  href?: string;
  /** Or run this — e.g. open a modal, as an AIP activity row does. */
  onClick?: () => void;
  /**
   * What the code identifies, for the accessible name — typically the row's name. The name itself
   * is no longer a link, so this is how a screen reader learns what the code opens.
   */
  description?: string;
  /**
   * Bold the last segment — the part that tells siblings apart. On by default because every AIP code
   * is its parent's code plus one segment, and the shared prefix is the part a reader skips.
   */
  emphasizeLastSegment?: boolean;
  separator?: string;
  className?: string;
}) {
  const cut = emphasizeLastSegment ? code.lastIndexOf(separator) : -1;
  const body = cut === -1 ? (
    code
  ) : (
    <>
      {code.slice(0, cut + separator.length)}
      <span className="font-semibold">{code.slice(cut + separator.length)}</span>
    </>
  );

  // ⚠️ Contains the visible code, so what is heard matches what is seen (WCAG 2.5.3, label in name).
  const label = description ? `${code}, ${description}` : undefined;
  const base = `whitespace-nowrap font-mono text-xs ${className}`;

  if (href) {
    return <Link href={href} aria-label={label} className={`${base} ${LINK_CLS}`}>{body}</Link>;
  }

  if (onClick) {
    return (
      <button type="button" onClick={onClick} aria-label={label} className={`${base} text-left ${LINK_CLS}`}>
        {body}
      </button>
    );
  }

  // Nothing to open — e.g. an unmatched legacy row with no owning office. Shown, never hidden: the
  // record exists, and dropping it would read as missing data.
  return <span className={`${base} text-slate-600`}>{body}</span>;
}
