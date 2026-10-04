#!/usr/bin/env python3
"""
Generate the database data dictionary and Mermaid ERDs from the EF Core model snapshot.

Source of truth: backend/PPDO.Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs
(what the migrations build), plus the XML <summary> comments on backend/PPDO.Domain/Entities/*.cs
for the descriptions. Nothing here connects to a database.

Usage (from the repo root):
    python scripts/generate_db_docs.py            # writes docs/database/*.md
    python scripts/generate_db_docs.py --check    # exits 1 if the committed docs are stale

Re-run it whenever a migration is added. The module grouping lives in MODULES below; a table
that is not listed there fails the run, so a new table cannot silently drop out of the docs.
"""

from __future__ import annotations

import argparse
import html
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SNAPSHOT = ROOT / "backend/PPDO.Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs"
ENTITIES = ROOT / "backend/PPDO.Domain/Entities"
MIGRATIONS = ROOT / "backend/PPDO.Infrastructure/Data/Migrations"
OUT = ROOT / "docs/database"

# ── Module grouping ──────────────────────────────────────────────────────────
# (key, title, blurb, tables). Order here is the order in the docs.
MODULES: list[tuple[str, str, str, list[str]]] = [
    ("access", "Identity, Organization & Access",
     "Users, the configurable office and division structure that scopes every query, the audit "
     "trail, and the partner keys that guard the external AIP API.",
     ["Users", "offices", "divisions", "audit_log",
      "partner_api_keys", "partner_api_key_offices", "partner_api_requests"]),
    ("portal", "Portal Content",
     "Public announcements, the calendar of activities, and the shared resource links.",
     ["announcements", "CalendarEvents", "ResourceLinks"]),
    ("inventory", "Inventory & Procurement Monitoring",
     "Items master, purchase requests, deliveries, distributions and the physical-count ledger "
     "(v1.0 → v1.7). Most of these are the legacy PascalCase tables.",
     ["ItemMasters", "PurchaseRequests", "PRItems", "Deliveries", "DeliveryItems",
      "Distributions", "stock_balances"]),
    ("reference", "Budget Reference Data",
     "Chart of accounts, funding sources, price index, procurement presets, ESRE codes and "
     "climate-change typologies — shared lookups used by WFP and AIP entry.",
     ["accounts", "funding_sources", "price_index_items", "procurement_presets",
      "procurement_preset_items", "esre_codes", "climate_change_typologies"]),
    ("allocation", "Ceilings & Allocation",
     "Budget ceilings per fiscal year and funding source, their split across divisions, and the "
     "program-to-division assignment.",
     ["budget_ceilings", "division_allocations", "program_divisions"]),
    ("ldip", "LDIP (Local Development Investment Program)",
     "The multi-year LDIP, per office and program.",
     ["ldip_records", "ldip_offices", "ldip_programs"]),
    ("aip", "AIP (Annual Investment Program) — v1.8.0 redesign",
     "One record per fiscal year → offices → programs → projects → activities. FY2028+ activities "
     "carry expenditure lines and procurement items; FY2027 and earlier keep money on the activity "
     "(clean fiscal-year break, no data migration).",
     ["aip_records", "aip_offices", "aip_programs", "aip_projects", "aip_activities",
      "aip_expenditures", "aip_procurement_items", "aip_review_comments",
      "aip_division_submissions", "aip_division_allocation_ledger"]),
    ("proposal", "Investment Proposals — v1.8.0",
     "Project proposals that feed the AIP, with their logframe, work plan, team, risks and "
     "monitoring plan, plus the investment-planning settings per fiscal year.",
     ["investment_planning_settings", "investment_proposal_groups", "investment_proposals",
      "investment_proposal_beneficiaries", "investment_proposal_benefits",
      "investment_proposal_logframe", "investment_proposal_monitoring",
      "investment_proposal_risks", "investment_proposal_team_members",
      "investment_proposal_work_plan_rows"]),
    ("wfp", "WFP (Work and Financial Plan)",
     "Per-division WFP: activities, expenditures by frequency period, line items, procurement "
     "items, and the allocation ledger that tracks consumption against division allocations.",
     ["wfp_records", "wfp_activities", "wfp_expenditures", "wfp_expenditure_periods",
      "wfp_expenditure_lines", "wfp_procurement_items", "wfp_division_allocation_ledger"]),
]

# ── Model ────────────────────────────────────────────────────────────────────
@dataclass
class Column:
    prop: str
    name: str
    clr: str
    sql: str = ""
    required: bool = False
    nullable: bool = True
    identity: bool = False
    default: str = ""
    max_len: str = ""
    concurrency: bool = False
    precision: str = ""


@dataclass
class Index:
    props: list[str]
    unique: bool = False
    name: str = ""
    filter: str = ""


@dataclass
class ForeignKey:
    props: list[str]
    target: str            # entity name
    on_delete: str = ""
    required: bool = False
    one_to_one: bool = False
    name: str = ""


@dataclass
class Entity:
    name: str
    table: str = ""
    columns: dict[str, Column] = field(default_factory=dict)
    key: list[str] = field(default_factory=list)
    indexes: list[Index] = field(default_factory=list)
    checks: list[tuple[str, str]] = field(default_factory=list)
    fks: list[ForeignKey] = field(default_factory=list)
    seed_rows: int = 0
    summary: str = ""
    prop_docs: dict[str, str] = field(default_factory=dict)


def blocks(src: str):
    """Yield (entity, body) for every modelBuilder.Entity("...", b => { body })."""
    for m in re.finditer(r'modelBuilder\.Entity\("PPDO\.Domain\.Entities\.(\w+)", b =>\s*\{', src):
        depth, i = 1, m.end()
        while depth:
            c = src[i]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
            i += 1
        yield m.group(1), src[m.end():i - 1]


def statements(body: str):
    """Split a block body into top-level `b.` statements (ending at `;` at depth 0)."""
    out, depth, start, in_str = [], 0, None, False
    i = 0
    while i < len(body):
        c = body[i]
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c in "({":
            depth += 1
        elif c in ")}":
            depth -= 1
        elif c == ";" and depth == 0:
            if start is not None:
                out.append(body[start:i].strip())
            start = None
        if start is None and not in_str and body.startswith("b.", i) and depth == 0:
            start = i
        elif start is None and body.startswith("SqlServer", i) and depth == 0:
            start = i
        i += 1
    return out


def strargs(s: str) -> list[str]:
    return re.findall(r'"((?:[^"\\]|\\.)*)"', s)


def first_call_args(stmt: str, method: str) -> str | None:
    m = re.search(r"\." + method + r"\(", stmt)
    if not m:
        return None
    depth, i = 1, m.end()
    while depth:
        if stmt[i] == "(":
            depth += 1
        elif stmt[i] == ")":
            depth -= 1
        i += 1
    return stmt[m.end():i - 1]


def parse_snapshot(src: str) -> dict[str, Entity]:
    ents: dict[str, Entity] = {}
    for name, body in blocks(src):
        e = ents.setdefault(name, Entity(name))
        for st in statements(body):
            if st.startswith("SqlServer") and "UseIdentityColumn" in st:
                prop = re.search(r'Property<[^>]+>\("(\w+)"\)', st).group(1)
                if prop in e.columns:
                    e.columns[prop].identity = True
            elif st.startswith("b.Property<"):
                m = re.match(r'b\.Property<([^>]+)>\("(\w+)"\)', st)
                clr, prop = m.group(1), m.group(2)
                col = Column(prop=prop, name=prop, clr=clr)
                if (a := first_call_args(st, "HasColumnName")) is not None:
                    col.name = strargs(a)[0]
                if (a := first_call_args(st, "HasColumnType")) is not None:
                    col.sql = strargs(a)[0]
                if (a := first_call_args(st, "HasMaxLength")) is not None:
                    col.max_len = a
                if (a := first_call_args(st, "HasPrecision")) is not None:
                    col.precision = a.replace(" ", "")
                col.required = ".IsRequired()" in st
                col.concurrency = ".IsConcurrencyToken()" in st
                if (a := first_call_args(st, "HasDefaultValueSql")) is not None:
                    col.default = strargs(a)[0]
                elif (a := first_call_args(st, "HasDefaultValue")) is not None:
                    col.default = a.strip().strip('"')
                nullable_clr = clr.endswith("?") or clr in ("string", "byte[]")
                col.nullable = nullable_clr and not col.required
                e.columns[prop] = col
            elif st.startswith("b.HasKey("):
                e.key = strargs(st)
            elif st.startswith("b.HasIndex("):
                props = strargs(first_call_args(st, "HasIndex"))
                ix = Index(props=props, unique=".IsUnique()" in st)
                if (a := first_call_args(st, "HasDatabaseName")) is not None:
                    ix.name = strargs(a)[0]
                if (a := first_call_args(st, "HasFilter")) is not None:
                    ix.filter = strargs(a)[0]
                e.indexes.append(ix)
            elif st.startswith("b.ToTable("):
                e.table = strargs(st)[0]
                for cm in re.finditer(r'HasCheckConstraint\("([^"]+)",\s*"((?:[^"\\]|\\.)*)"\)', st):
                    e.checks.append((cm.group(1), cm.group(2).replace('\\"', '"')))
            elif st.startswith("b.HasData("):
                e.seed_rows += len(re.findall(r"\bnew\s*\{", st)) or st.count("new ")
            elif st.startswith("b.HasOne("):
                tgt = re.match(r'b\.HasOne\("PPDO\.Domain\.Entities\.(\w+)"', st).group(1)
                fk_args = first_call_args(st, "HasForeignKey")
                if fk_args is None:
                    continue
                args = strargs(fk_args)
                one_to_one = ".WithOne(" in st
                if one_to_one and args and args[0].startswith("PPDO.Domain.Entities."):
                    dep = args[0].split(".")[-1]
                    props = args[1:]
                    if dep != name:
                        # FK lives on the other side; record it there.
                        fk = ForeignKey(props=props, target=name, one_to_one=True)
                        _fk_opts(fk, st)
                        ents.setdefault(dep, Entity(dep)).fks.append(fk)
                        continue
                else:
                    props = args
                fk = ForeignKey(props=props, target=tgt, one_to_one=one_to_one)
                _fk_opts(fk, st)
                e.fks.append(fk)
    return ents


def _fk_opts(fk: ForeignKey, st: str) -> None:
    if (a := first_call_args(st, "OnDelete")) is not None:
        fk.on_delete = a.replace("DeleteBehavior.", "")
    fk.required = ".IsRequired()" in st
    if (a := first_call_args(st, "HasConstraintName")) is not None:
        fk.name = strargs(a)[0]


# ── Doc comments ─────────────────────────────────────────────────────────────
def clean_doc(text: str) -> str:
    t = re.sub(r"<see\s+cref=\"(?:[A-Z]:)?([^\"]+)\"\s*/>", lambda m: m.group(1).split(".")[-1], text)
    t = re.sub(r"<see\s+langword=\"([^\"]+)\"\s*/>", r"\1", t)
    t = re.sub(r"<paramref\s+name=\"([^\"]+)\"\s*/>", r"\1", t)
    t = re.sub(r"<c>(.*?)</c>", r"`\1`", t, flags=re.S)
    t = re.sub(r"<b>(.*?)</b>", r"\1", t, flags=re.S)
    t = re.sub(r"<[^>]+>", " ", t)
    t = html.unescape(t)
    return re.sub(r"\s+", " ", t).strip()


def first_paragraph(raw: str) -> str:
    para = re.split(r"<para>|\n\s*\n", raw, maxsplit=1)[0]
    return clean_doc(para)


# A full stop after one of these does not end the sentence ("e.g. "aip_activities"" was cut at "e.g.").
ABBREVIATIONS = re.compile(r"(?:\be\.g|\bi\.e|\betc|\bvs|\bNo)\.$")


def first_sentence(text: str, limit: int = 220) -> str:
    s = text
    for m in re.finditer(r"[.!?](?=\s|$)", text):
        candidate = text[:m.end()]
        if not ABBREVIATIONS.search(candidate):
            s = candidate
            break
    if len(s) > limit:
        s = s[:limit].rsplit(" ", 1)[0] + " …"
    return s


def parse_docs(ents: dict[str, Entity]) -> None:
    for path in ENTITIES.rglob("*.cs"):
        src = path.read_text(encoding="utf-8-sig")
        lines = src.splitlines()
        doc: list[str] = []
        for line in lines:
            s = line.strip()
            if s.startswith("///"):
                doc.append(s[3:].strip())
                continue
            if s.startswith("["):        # attributes between doc and declaration
                continue
            raw = "\n".join(doc)
            summary = re.search(r"<summary>(.*?)</summary>", raw, re.S)
            text = summary.group(1) if summary else ""
            if m := re.match(r"public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)", s):
                if m.group(1) in ents and text:
                    ents[m.group(1)].summary = first_sentence(first_paragraph(text), 400)
                current = m.group(1)
            elif m := re.match(r"public\s+(?:required\s+|virtual\s+|override\s+)*[\w<>\[\]?,. ]+?\s+(\w+)\s*\{", s):
                cls = locals().get("current")
                if cls in ents and text:
                    ents[cls].prop_docs[m.group(1)] = first_sentence(first_paragraph(text))
            doc = []


# ── Rendering ────────────────────────────────────────────────────────────────
def sql_type(c: Column) -> str:
    t = c.sql
    if not t:
        t = {"int": "int", "bool": "bit", "decimal": "decimal(18,2)", "DateTime": "datetime2",
             "Guid": "uniqueidentifier", "string": "nvarchar(max)", "long": "bigint",
             "byte[]": "varbinary(max)", "DateOnly": "date"}.get(c.clr.rstrip("?"), c.clr)
    if c.precision and t.startswith("decimal") and "(" not in t:
        t += f"({c.precision})"
    return t


def mermaid_type(c: Column) -> str:
    t = sql_type(c)
    return re.sub(r"[^A-Za-z0-9_]", "", t.split("(")[0]) or "col"


def fk_columns(e: Entity) -> dict[str, ForeignKey]:
    out = {}
    for fk in e.fks:
        for p in fk.props:
            out[p] = fk
    return out


def unique_props(e: Entity) -> set[str]:
    return {ix.props[0] for ix in e.indexes if ix.unique and len(ix.props) == 1}


def ordered_columns(e: Entity) -> list[Column]:
    cols = list(e.columns.values())
    keyset = set(e.key)
    return sorted(cols, key=lambda c: (c.prop not in keyset, e.key.index(c.prop) if c.prop in keyset else 0))


def render_erd(ents: dict[str, Entity], tables: list[str], by_table: dict[str, Entity],
               detailed: bool = True) -> str:
    inside = {by_table[t].name for t in tables}
    lines = ["```mermaid", "erDiagram"]
    stubs: set[str] = set()
    rels: list[str] = []
    for t in tables:
        e = by_table[t]
        for fk in e.fks:
            tgt = ents[fk.target]
            if tgt.name not in inside:
                stubs.add(tgt.name)
            optional = not fk.required and any(e.columns[p].nullable for p in fk.props if p in e.columns)
            parent = "|o" if optional else "||"
            child = "o|" if fk.one_to_one else "o{"
            label = ", ".join(e.columns[p].name if p in e.columns else p for p in fk.props)
            rels.append(f'    {tgt.table} {parent}--{child} {e.table} : "{label}"')
    for t in tables:
        e = by_table[t]
        if not detailed:
            lines.append(f"    {e.table}")
            continue
        fkc, uq = fk_columns(e), unique_props(e)
        lines.append(f"    {e.table} {{")
        for c in ordered_columns(e):
            keys = []
            if c.prop in e.key:
                keys.append("PK")
            if c.prop in fkc:
                keys.append("FK")
            if c.prop in uq and c.prop not in e.key:
                keys.append("UK")
            k = (" " + ",".join(keys)) if keys else ""
            lines.append(f"        {mermaid_type(c)} {c.name}{k}")
        lines.append("    }")
    for s in sorted(stubs, key=lambda n: ents[n].table):
        lines.append(f"    {ents[s].table}")
    lines.extend(sorted(set(rels)))
    lines.append("```")
    return "\n".join(lines)


def render_overview(ents: dict[str, Entity], by_table: dict[str, Entity]) -> str:
    """Table-to-table relationships only, across every module."""
    lines = ["```mermaid", "erDiagram"]
    rels = set()
    for e in by_table.values():
        for fk in e.fks:
            tgt = ents[fk.target]
            child = "o|" if fk.one_to_one else "o{"
            rels.add(f"    {tgt.table} ||--{child} {e.table} : \"\"")
    linked = {r.split()[0] for r in rels} | {r.split()[2] for r in rels}
    for t in sorted(by_table):
        if t not in linked:
            lines.append(f"    {t}")
    lines.extend(sorted(rels))
    lines.append("```")
    return "\n".join(lines)


def md_escape(s: str) -> str:
    return s.replace("|", "\\|")


def render_table_doc(e: Entity, ents: dict[str, Entity], module_of: dict[str, str]) -> str:
    out = [f"### `{e.table}`", ""]
    meta = [f"Entity `{e.name}`"]
    if e.seed_rows:
        meta.append(f"{e.seed_rows} seeded row{'s' if e.seed_rows != 1 else ''}")
    if re.search(r"[A-Z]", e.table):
        meta.append("legacy PascalCase table")
    out.append(" · ".join(meta))
    out.append("")
    if e.summary:
        out.extend([md_escape(e.summary), ""])
    fkc, uq = fk_columns(e), unique_props(e)
    out.append("| Column | Type | Null | Key | Default | Description |")
    out.append("|---|---|---|---|---|---|")
    for c in ordered_columns(e):
        keys = []
        if c.prop in e.key:
            keys.append("PK" + (" (identity)" if c.identity else ""))
        if c.prop in fkc:
            tgt = ents[fkc[c.prop].target]
            keys.append(f"FK → [`{tgt.table}`](#{anchor(tgt.table)})")
        if c.prop in uq and c.prop not in e.key:
            keys.append("Unique")
        if c.concurrency:
            keys.append("Concurrency token")
        desc = md_escape(e.prop_docs.get(c.prop, ""))
        out.append(f"| `{c.name}` | {sql_type(c)} | {'Yes' if c.nullable else 'No'} | "
                   f"{'<br>'.join(keys)} | {('`' + md_escape(c.default) + '`') if c.default else ''} | {desc} |")
    out.append("")
    if e.fks:
        out.append("**Foreign keys**")
        out.append("")
        for fk in e.fks:
            tgt = ents[fk.target]
            cols = ", ".join(f"`{e.columns[p].name if p in e.columns else p}`" for p in fk.props)
            rel = "one-to-one" if fk.one_to_one else "many-to-one"
            out.append(f"- {cols} → `{tgt.table}` ({rel}, on delete **{fk.on_delete or 'ClientSetNull'}**)")
        out.append("")
    extra_ix = [ix for ix in e.indexes if not (len(ix.props) == 1 and ix.props[0] in fkc and not ix.unique)]
    if extra_ix:
        out.append("**Indexes**")
        out.append("")
        for ix in extra_ix:
            cols = ", ".join(f"`{e.columns[p].name if p in e.columns else p}`" for p in ix.props)
            bits = ["unique" if ix.unique else "non-unique"]
            if ix.filter:
                bits.append(f"filtered `{ix.filter}`")
            nm = f"`{ix.name}` " if ix.name else ""
            out.append(f"- {nm}({cols}) — {', '.join(bits)}")
        out.append("")
    if e.checks:
        out.append("**Check constraints**")
        out.append("")
        for n, expr in e.checks:
            out.append(f"- `{n}`: `{md_escape(expr)}`")
        out.append("")
    return "\n".join(out)


def anchor(table: str) -> str:
    return table.lower()


def latest_migration() -> tuple[str, int]:
    names = sorted(p.stem for p in MIGRATIONS.glob("2*.cs")
                   if not p.stem.endswith(".Designer") and "." not in p.stem)
    return names[-1], len(names)


def build(ents: dict[str, Entity]) -> dict[str, str]:
    by_table = {e.table: e for e in ents.values() if e.table}
    listed = [t for _, _, _, ts in MODULES for t in ts]
    missing = sorted(set(by_table) - set(listed))
    unknown = sorted(set(listed) - set(by_table))
    if missing or unknown:
        sys.exit(f"MODULES out of date. Unlisted tables: {missing}. Listed but not in model: {unknown}")
    module_of = {t: key for key, _, _, ts in MODULES for t in ts}
    latest, count = latest_migration()
    n_fk = sum(len(e.fks) for e in by_table.values())
    n_cols = sum(len(e.columns) for e in by_table.values())
    stamp = (f"> Generated by `scripts/generate_db_docs.py` from the EF Core model snapshot — "
             f"**{len(by_table)} tables, {n_cols} columns, {n_fk} foreign keys**, "
             f"{count} migrations, latest `{latest}`.\n"
             f"> Do not edit by hand: change the entity or its configuration, add a migration, and re-run the script.")

    files: dict[str, str] = {}

    # README / overview
    rows = []
    for key, title, blurb, ts in MODULES:
        rows.append(f"| [{title}](DATA_DICTIONARY.md#{key}) | {len(ts)} | {', '.join(f'`{t}`' for t in ts)} |")
    files["README.md"] = "\n".join([
        "# PPDO Portal — Database Documentation (v1.8.0)", "",
        stamp, "",
        "| File | What it is |", "|---|---|",
        "| [DATA_DICTIONARY.md](DATA_DICTIONARY.md) | Every table and column: type, nullability, keys, defaults, description, indexes, check constraints |",
        "| [ERD.md](ERD.md) | Entity-relationship diagrams (Mermaid) — one overview plus one per module |",
        "", "## Platform", "",
        "| Item | Value |", "|---|---|",
        "| Engine | Azure SQL Database (SQL Server), Basic tier, Southeast Asia |",
        "| Schema owner | EF Core 9 code-first migrations in `backend/PPDO.Infrastructure/Data/Migrations/` |",
        "| Access path | Only through the PPDO Functions API (and the external AIP API). No other system reads or writes the database directly |",
        "| Time stamps | Stored in UTC (`datetime2`, `GETUTCDATE()` defaults); displayed in Manila time (UTC+8) |",
        "| Money | `decimal(18,2)`, Philippine pesos (AIP amounts are stored in pesos from v1.8.0, not thousands) |",
        "| Naming | Tables created from v1.1 on are `snake_case`; the original v1.0 tables (`Users`, `PurchaseRequests`, `CalendarEvents`, …) keep their PascalCase names — see `docs/NAMING_CONVENTIONS.md` |",
        "| Deletes | Child rows of a document cascade (record → office → program → …); references to users and configuration use `Restrict`, so config in use cannot be deleted |",
        "", "## Modules", "",
        "| Module | Tables | |", "|---|---|---|",
        *rows, "",
    ])

    # Data dictionary
    dd = ["# PPDO Portal — Data Dictionary (v1.8.0)", "", stamp, "", "## Contents", ""]
    for key, title, _, ts in MODULES:
        dd.append(f"- [{title}](#{key}) — " + ", ".join(f"[`{t}`](#{anchor(t)})" for t in ts))
    dd.append("")
    for key, title, blurb, ts in MODULES:
        dd.extend([f'<a id="{key}"></a>', "", f"## {title}", "", blurb, ""])
        for t in ts:
            dd.extend([f'<a id="{anchor(t)}"></a>', "", render_table_doc(by_table[t], ents, module_of)])
    files["DATA_DICTIONARY.md"] = "\n".join(dd)

    # ERDs
    erd = ["# PPDO Portal — Entity-Relationship Diagrams (v1.8.0)", "", stamp, "",
           "Diagrams are Mermaid and render on GitHub and in VS Code (Markdown Preview Mermaid Support). "
           "Notation: `||` exactly one, `|o` zero or one, `o{` zero or many. Each relationship is labelled "
           "with the foreign-key column on the child table. In a module diagram, tables from other "
           "modules appear as a box with no columns.", "",
           "## Contents", "", "- [Overview — all tables](#overview)"]
    for key, title, _, _ in MODULES:
        erd.append(f"- [{title}](#erd-{key})")
    erd.extend(["", '<a id="overview"></a>', "", "## Overview — all tables", "",
                "Relationships only, no columns. Use the module diagrams below for detail.", "",
                render_overview(ents, by_table), ""])
    for key, title, blurb, ts in MODULES:
        erd.extend([f'<a id="erd-{key}"></a>', "", f"## {title}", "", blurb, "",
                    render_erd(ents, ts, by_table), ""])
    files["ERD.md"] = "\n".join(erd)
    return files


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="fail if docs/database is out of date")
    args = ap.parse_args()
    ents = parse_snapshot(SNAPSHOT.read_text(encoding="utf-8-sig"))
    parse_docs(ents)
    files = build(ents)
    stale = []
    OUT.mkdir(parents=True, exist_ok=True)
    for name, content in files.items():
        path = OUT / name
        content = content.rstrip() + "\n"
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                stale.append(name)
        else:
            path.write_text(content, encoding="utf-8")
            print(f"wrote {path.relative_to(ROOT)} ({len(content.splitlines())} lines)")
    if stale:
        print("stale: " + ", ".join(stale) + " — run python scripts/generate_db_docs.py")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
