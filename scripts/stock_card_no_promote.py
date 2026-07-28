#!/usr/bin/env python3
"""
Copy `stock_card_no` from one Price Index CSV onto another, changing nothing else.

This is the safe way to move curated stock card numbers between environments —
e.g. promoting the codes curated locally into production after a deploy
(docs/v1.5/STOCK_CARD_NO_RUNBOOK.md, Procedure A).

Why a script instead of just uploading the source CSV: the Price Index CSV import
is a **full-row upsert**. Uploading a local export into production would also
overwrite production's unit_price, category, is_active and days_enabled with the
local values, and create any row production doesn't have. This script instead
starts from the TARGET's own export, changes only the stock_card_no column, and
leaves every other cell — and every row that exists in only one of the two files —
exactly as the target had it.

Rows are matched on (name, unit), the Price Index's natural key, normalized for
case and whitespace only.

Usage
-----
    python scripts/stock_card_no_promote.py \
        --source ~/Downloads/local-price-index.csv \
        --target ~/Downloads/prod-price-index.csv \
        --out ~/Downloads/prod-price-index-with-codes.csv

Then upload --out through Config > Price Index > Upload CSV in the TARGET
environment. Nothing here talks to a database or an API.
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from pathlib import Path

PRICE_INDEX_HEADERS = ["name", "unit", "unit_price", "category", "is_active", "days_enabled", "stock_card_no"]

_WHITESPACE = re.compile(r"\s+")


def key(name: str, unit: str) -> tuple[str, str]:
    return (
        _WHITESPACE.sub(" ", name or "").strip().casefold(),
        _WHITESPACE.sub(" ", unit or "").strip().casefold(),
    )


def read_csv(path: Path, label: str) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        if reader.fieldnames is None:
            sys.exit(f"{label} file {path} is empty.")
        if "name" not in reader.fieldnames or "unit" not in reader.fieldnames:
            sys.exit(
                f"{label} file {path.name} does not look like a Price Index export "
                f"(expected headers: {', '.join(PRICE_INDEX_HEADERS)})."
            )
        return [dict(row) for row in reader]


def main() -> int:
    parser = argparse.ArgumentParser(description="Copy stock_card_no from one Price Index CSV onto another.")
    parser.add_argument("--source", required=True, type=Path, help="CSV holding the codes to copy (e.g. local export)")
    parser.add_argument("--target", required=True, type=Path, help="CSV to update (export from the target environment)")
    parser.add_argument("--out", required=True, type=Path, help="CSV to write — upload this to the target")
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace a code the target already has (default: only fill blanks)",
    )
    args = parser.parse_args()

    source_rows = read_csv(args.source, "Source")
    target_rows = read_csv(args.target, "Target")

    codes: dict[tuple[str, str], str] = {}
    for row in source_rows:
        code = (row.get("stock_card_no") or "").strip()
        if code:
            codes[key(row.get("name", ""), row.get("unit", ""))] = code

    filled = kept = missing_in_source = already_matching = 0
    for row in target_rows:
        existing = (row.get("stock_card_no") or "").strip()
        code = codes.get(key(row.get("name", ""), row.get("unit", "")))
        if code is None:
            missing_in_source += 1
            row["stock_card_no"] = existing
            continue
        if existing == code:
            already_matching += 1
        elif existing and not args.overwrite:
            kept += 1
            row["stock_card_no"] = existing
        else:
            row["stock_card_no"] = code
            filled += 1

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=PRICE_INDEX_HEADERS, extrasaction="ignore")
        writer.writeheader()
        for row in target_rows:
            writer.writerow({header: row.get(header, "") for header in PRICE_INDEX_HEADERS})

    only_in_source = len(set(codes) - {key(r.get("name", ""), r.get("unit", "")) for r in target_rows})

    print(f"Source rows with a code    : {len(codes)}")
    print(f"Target rows                : {len(target_rows)}")
    print(f"Codes written              : {filled}")
    print(f"Already identical          : {already_matching}")
    print(f"Target code kept           : {kept}" + ("" if args.overwrite else "  (use --overwrite to replace)"))
    print(f"No code in source          : {missing_in_source}")
    print(f"In source but not in target: {only_in_source}  (not added — this script never creates rows)")
    print(f"\nWrote {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
