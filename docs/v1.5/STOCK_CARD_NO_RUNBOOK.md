# Stock Card No. — Production Runbook

> **v1.5 — RAL-183/184.** How to get `price_index_items.stock_card_no` populated in an
> environment that doesn't have it, and how to refresh it when GSO issues a new item export.
>
> Design background: `docs/v1.5/PPMP_Report_Findings.md` §6 (why the field exists, which coding
> scheme it holds, and why the report joins it live). This document is the *operational* half —
> the steps, the scripts, and what to check afterwards.

---

## 1. Why this exists

The PPMP report's **Stock Card No.** column is joined live from the Price Index via
`WfpProcurementItem.PriceIndexItemId`. It is not stored on the WFP. So the column renders blank in
any environment where `price_index_items.stock_card_no` is empty — the report will still generate,
the totals will still be right, and the column will just be empty all the way down.

The codes were curated on the **local development database** (6,279 of 6,398 rows as of
2026-07-28). **Production has none of them.** Deploying v1.5.0 applies the
`AddPriceIndexItemStockCardNo` migration, which adds the *column* — it does not add any *data*.

Two procedures below:

| | When | What it does |
|---|---|---|
| **Procedure A** | The v1.5.0 production deploy | Copies the already-curated codes from one environment into another |
| **Procedure B** | GSO issues a new Items Export | Re-derives codes by matching the catalogue against that export |

Do **A** for the deploy. Reach for **B** only when the source of truth has changed.

---

## 2. Prerequisites

- v1.5.0 deployed to the target environment (the migration must have run — check that
  Config → Price Index shows a **Stock Card No.** column).
- A sign-in on the target with **Manage Config** permission (SuperAdmin, or a user with
  `CanManageConfig`). The CSV import endpoint is gated on it.
- Python 3.9+ with `openpyxl` (`pip install openpyxl`) — Procedure B only.
- The scripts: `scripts/stock_card_no_promote.py` (A) and `scripts/stock_card_no_merge.py` (B).

Neither script connects to a database or an API. They read CSV/XLSX files and write a CSV. Every
change reaches the portal through the normal **Config → Price Index → Upload CSV** path, which is
permission-checked, validated per row, and audited like any other config change.

---

## 3. ⚠️ The one thing to understand before uploading anything

**The Price Index CSV import is a full-row upsert, not a column patch.**

Every uploaded row overwrites `unit_price`, `category`, `is_active` and `days_enabled` on the
matching `(name, unit)` — and creates the row if it doesn't exist. Uploading a *local* export
straight into production would therefore overwrite production's prices with local ones and add
whatever local-only rows exist.

That is why Procedure A starts from **production's own export** and changes only one column.
Never shortcut it by uploading the local file directly.

Two more rules from `PriceIndexService.ImportCsvAsync`, both deliberate:

- A CSV with **no `stock_card_no` column at all** (a pre-v1.5 export) leaves existing codes
  **untouched**. Re-importing an old price list will not wipe them.
- A row that **has** the column but leaves it **blank** **clears** the code — same as `category`.
  Both scripts preserve existing values rather than emitting blanks, but a hand-edited file will
  not.

---

## 4. Procedure A — promote curated codes into production

Use for the v1.5.0 deploy. Result: production's stock card numbers match the curated set;
nothing else in the catalogue changes.

**1. Export the source (local).**
Sign in locally → **Config → Price Index → Export CSV**. Save as `local-price-index.csv`.

**2. Export the target (production).**
Sign in to production → same page → **Export CSV**. Save as `prod-price-index.csv`.
Keep this file — it is your rollback (see §7).

**3. Merge the codes onto production's own rows.**

```bash
python scripts/stock_card_no_promote.py --source local-price-index.csv --target prod-price-index.csv --out prod-price-index-with-codes.csv
```

It prints what it did:

```
Source rows with a code    : 6286
Target rows                : 6398
Codes written              : 6264
Already identical          : 0
Target code kept           : 22  (use --overwrite to replace)
No code in source          : 112
In source but not in target: 0  (not added — this script never creates rows)
```

- *Target code kept* — production already had a code and it differs; left alone. Add
  `--overwrite` only if the source is authoritative and you intend to replace them.
- *In source but not in target* — a `(name, unit)` that exists only locally. **Not added.** If
  production is genuinely missing catalogue items, that's a separate price-list import, not this
  procedure.

**4. Upload to production.**
**Config → Price Index → Upload CSV** → pick `prod-price-index-with-codes.csv`. The result dialog
reports *added / updated / skipped*. **Added should be 0** — this file was built from production's
own export, so a non-zero "added" means the wrong file was picked.

**5. Verify** — §6.

---

## 5. Procedure B — re-derive codes from a new GSO Items Export

Use when GSO issues a fresh export and codes have changed or new items were coded.

**1. Get the export.** A `.xlsx` with the columns `Item Code`, `Description`, `Account Code`,
`Account Name`, `Unit`, `Price` (as of the 2026-07-24 export, sheet `Items Data`). If GSO renames
columns, update the `EXPORT_*` constants at the top of `scripts/stock_card_no_merge.py`.

**2. Export the target environment's price index** (as in A step 2). Keep it as the rollback file.

**3. Match and merge.**

```bash
python scripts/stock_card_no_merge.py --price-index prod-price-index.csv --items-export Items_Export.xlsx --out prod-price-index-with-codes.csv --report unresolved.csv
```

The matching rule (locked in the findings doc §6):

> price-index `name` + `unit` + `category` **==** export `Description` + `Unit` + `Account Name`

`name` + `unit` alone is ambiguous for thousands of rows — GSO codes the same physical item
differently per account (Battery AA is `OSAME-…` as an expense, `OSAMFD-…` for distribution,
`TREX-…` under training). Category, which equals the export's Account Name, resolves nearly all of
it; a name+unit-only match is used only when that pair maps to exactly one code in the whole
export.

**A row is filled only when the match yields exactly one candidate.** Ambiguous and unmatched rows
keep whatever they already had. `--report` lists every one of them with the candidate codes, for
manual entry in the config UI. **Do not loosen the match to close the gap** — a wrong GSO code on
an item is worse than a blank one.

Representative run (6,398-row catalogue, 2026-07-24 export, starting from all-blank):

```
Filled (name+unit+category): 6283
Filled (name+unit only)    : 3
Ambiguous - left as-is     : 70
No match  - left as-is     : 42
```

**4. Upload and verify** — as in A steps 4–5.

### Gotcha the script handles for you

**1,040 codes in the 2026-07-24 export use an EN DASH (`–`) where every other code uses a hyphen**
— `RAM–BAOS-1434419431` vs `RAM-BAOS-1434419431`. They are visually identical and would silently
fail a typed search in the config UI. The script normalizes all dash variants to ASCII hyphen.
Check a fresh export before removing that behaviour.

---

## 6. Verification

**In the UI** (either procedure):

1. Config → Price Index — the **Stock Card No.** column shows codes.
2. Type `OSE-` in the search box — it filters to matching items (the field is searchable).
3. Budget Planning → Report → **PPMP**, pick an office and FY with a WFP, and confirm the Stock
   Card No. column is populated for linked items.

**Expect some blanks, and don't chase them.** A **free-typed** procurement item has
`PriceIndexItemId = null` and therefore no code and no category — those cells are blank by design
(8 of 72 items locally). Never fuzzy-match a name to fill them.

**In SQL**, if you have direct access:

```sql
SELECT COUNT(*) AS total,
       SUM(CASE WHEN stock_card_no IS NOT NULL AND LEN(stock_card_no) > 0 THEN 1 ELSE 0 END) AS with_code
FROM price_index_items;
```

Local reference as of 2026-07-28: **6,398 total, 6,279 with a code.**

---

## 7. Rollback

The export you saved in step 2 is a complete snapshot of the target's price index *before* the
change. Re-uploading it restores every column, including clearing any code the run added — the
upload is an upsert on `(name, unit)`, so it puts each row back as it was.

It will **not** delete rows created by a bad upload. If an upload created rows it shouldn't have
(watch the "added" count in the result dialog), deactivate them in the config UI — the Price Index
is soft-delete only.

---

## 8. Known divergence — worth knowing before you trust a re-run

Procedure B's script implements the documented matching method, but it does **not** reproduce the
current local database byte-for-byte. Compared against the local DB on 2026-07-28: of 6,302 rows
present in both, **6,091 identical, 211 different** — 80 the script fills that the DB leaves blank,
74 the DB has that the script declines to guess, and 57 where the two chose different codes.

The reason is that the original merge was a one-off ad-hoc pass whose tie-breaking was never
written down, and the DB has since absorbed manual edits. The script's rules (normalize dashes,
strip Excel `_x000d_` escapes, collapse whitespace, dedupe identical codes, fill only on a single
candidate) are stated and repeatable; the original's were not.

**Practical consequence:** for the v1.5.0 deploy, use **Procedure A** — it copies the curated
values as they actually are and has no matching logic to disagree about. Save Procedure B for a
genuine refresh, and review `--report` before uploading.

---

*Runbook added 2026-07-28 for the v1.5.0 release. Scripts: `scripts/stock_card_no_promote.py`,
`scripts/stock_card_no_merge.py`.*
