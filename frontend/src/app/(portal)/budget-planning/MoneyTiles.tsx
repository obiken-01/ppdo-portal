"use client";

import { formatMoney } from "@/lib/money";

/**
 * MoneyTiles — the four figures the dashboard leads with (PPDO-20).
 *
 * A tile can be **muted**, which means "this is a real figure, but it is not yours to change" —
 * a division-scoped encoder sees the office ceiling that way. It is deliberately not hidden: the
 * ceiling is the number their own allocation has to fit inside, and hiding it just moves the
 * question to whoever they ask next.
 *
 * An absent figure renders as `—`, never as `₱0.00`. Zero is a decision somebody made; nothing
 * recorded is not, and collapsing the two is how "PBO has not published a ceiling" becomes
 * invisible on this page.
 */

export interface MoneyTile {
  key: string;
  label: string;
  /** Null renders as "—". Pass 0 only when zero is genuinely the recorded value. */
  value: number | null;
  /** Renders as a plain count instead of pesos. */
  count?: boolean;
  /** One short line under the figure. */
  hint?: string;
  /** Read-only for this account — rendered in a muted tone. See the doc comment. */
  muted?: boolean;
  /** Draws the figure in the danger tone, e.g. a negative remainder. */
  alert?: boolean;
}

function TileValue({ tile }: { tile: MoneyTile }) {
  if (tile.value == null) return <span className="text-slate-500">—</span>;
  if (tile.count) return <>{tile.value.toLocaleString("en-PH")}</>;
  return <>₱{formatMoney(tile.value)}</>;
}

export default function MoneyTiles({ tiles }: { tiles: MoneyTile[] }) {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
      {tiles.map((tile) => (
        <div
          key={tile.key}
          className={`border p-4 ${
            tile.muted ? "bg-slate-50 border-slate-200" : "bg-white border-slate-200"
          }`}
        >
          <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
            {tile.label}
          </p>
          <p
            className={`mt-1 text-lg font-bold tabular-nums ${
              tile.alert ? "text-danger-500" : tile.muted ? "text-slate-600" : "text-slate-800"
            }`}
          >
            <TileValue tile={tile} />
          </p>
          {tile.hint && <p className="text-xs text-slate-500 mt-0.5">{tile.hint}</p>}
        </div>
      ))}
    </div>
  );
}

/** Four grey tiles at the real height — first paint must not jump when the figures arrive. */
export function MoneyTilesSkeleton() {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
      {[0, 1, 2, 3].map((i) => (
        <div key={i} className="bg-white border border-slate-200 p-4">
          <div className="h-3 w-20 bg-slate-100 animate-pulse" />
          <div className="h-6 w-28 bg-slate-100 animate-pulse mt-2" />
          <div className="h-3 w-16 bg-slate-100 animate-pulse mt-1.5" />
        </div>
      ))}
    </div>
  );
}
