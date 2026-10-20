#!/usr/bin/env python3
"""
Convert a Linear CSV export into docs/linear-export/tickets/<ID>.md + INDEX.md
and one hub per version under versions/ (so the Obsidian graph groups tickets by version).

The ticket files double as the archive list for scripts/linear_archive.py, which
archives every file under tickets/ that carries a `uuid:`. So the filter below IS
the archive scope: a ticket written here gets archived on the next --all run.

USAGE  (run from the repo root)
  python scripts/linear_export_convert.py <export.csv> --dry-run
  python scripts/linear_export_convert.py <export.csv> --exclude-milestone "v1.9.0"

  --team               team name to keep (default: PPDO-Dev)
  --exclude-milestone  substring; closed tickets in a matching milestone are skipped
                       (repeatable). Use it for a release that has not shipped yet.
  --dry-run            print what would be written, touch nothing

Only closed tickets are exported (Done, Canceled, Duplicate). Existing files are
overwritten; files for tickets not in this export (e.g. the 2026-08-27 RAL-* set)
are kept, and INDEX.md is rebuilt from everything under tickets/.

Two quirks of Linear's export, both learned the hard way (see README.md):
  - any field starting with `>` arrives as `'>` (a formula-injection guard);
    the leading apostrophe is stripped.
  - there is no URL column; URLs are rebuilt from the identifier.
"""

import argparse
import csv
import re
import sys
from datetime import date
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "docs" / "linear-export"
TICKETS = OUT / "tickets"
VERSIONS = OUT / "versions"
URL = "https://linear.app/ralphoksiprojects/issue/{}"
CLOSED = {"done", "canceled", "cancelled", "duplicate"}

csv.field_size_limit(sys.maxsize if sys.maxsize < 2**31 else 2**31 - 1)


def col(row, *names):
    """First non-empty value among candidate column names (Linear renames them)."""
    for n in names:
        v = row.get(n)
        if v not in (None, ""):
            return v.strip()
    return ""


def unguard(text):
    """Strip Linear's leading-apostrophe guard on `>`-prefixed fields."""
    return text[1:] if text.startswith("'>") else text


def day(ts):
    m = re.match(r"(\d{4}-\d{2}-\d{2})", ts or "")
    if m:
        return m.group(1)
    # Linear sometimes exports "Thu Oct 16 2026 ..." style dates
    m = re.match(r"\w{3} (\w{3}) (\d{1,2}) (\d{4})", ts or "")
    if m:
        months = "Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec".split()
        return f"{m.group(3)}-{months.index(m.group(1)) + 1:02d}-{int(m.group(2)):02d}"
    return ""


def yaml_str(s):
    return "'" + s.replace("'", "''") + "'"


def filename(ident):
    prefix, num = ident.rsplit("-", 1)
    return f"{prefix}-{int(num):03d}.md"


def none_if_blank(s):
    return s if s else "(none)"


def render(row):
    ident = col(row, "ID", "Identifier")
    title = col(row, "Title")
    status = col(row, "Status")
    completed = day(col(row, "Completed", "Completed At")) or day(col(row, "Canceled", "Canceled At"))
    desc = unguard(col(row, "Description")).replace("\r\n", "\n")
    fm = [
        "---",
        f"id: {ident}",
        f"title: {yaml_str(title)}",
        f"status: {status}",
        f"project: {col(row, 'Project')}",
        f"labels: {col(row, 'Labels')}",
        f"milestone: {col(row, 'Project Milestone', 'Milestone')}",
        f"created: {day(col(row, 'Created', 'Created At'))}",
        f"completed: {completed}",
        f"parent: {none_if_blank(col(row, 'Parent issue', 'Parent Issue', 'Parent'))}",
        f"blocked_by: {none_if_blank(col(row, 'Blocked by', 'Blocked By'))}",
        f"related: {none_if_blank(col(row, 'Related to', 'Related', 'Related Issues'))}",
        f"url: {URL.format(ident)}",
        f"uuid: {col(row, 'UUID', 'Uuid', 'Issue ID')}",
        "---",
        "",
        f"# {ident} — {title}",
        "",
        desc if desc else "*(no description)*",
        "",
    ]
    return ident, "\n".join(fm)


def read_frontmatter(path):
    head = path.read_text(encoding="utf-8").split("---", 2)
    meta = {}
    if len(head) >= 3:
        for ln in head[1].splitlines():
            if ":" in ln:
                k, v = ln.split(":", 1)
                meta[k.strip()] = v.strip()
    t = meta.get("title", "")
    if t.startswith("'") and t.endswith("'"):
        meta["title"] = t[1:-1].replace("''", "'")
    return meta


def version_key(milestone):
    """(slug, title) of the version hub a milestone belongs to."""
    m = re.match(r"(v\d+(?:\.\d+)*)", milestone)
    if m:
        return m.group(1), m.group(1)
    if milestone.lower().startswith("demo 2"):
        return "demo-2", "Demo 2"
    if milestone.lower() == "techdebt":
        return "techdebt", "techdebt"
    return "unscheduled", "Unscheduled"


def collect_rows():
    rows = []
    for f in TICKETS.glob("*.md"):
        m = read_frontmatter(f)
        ident = m.get("id", f.stem)
        prefix, num = ident.rsplit("-", 1)
        rows.append(((0 if prefix == "RAL" else 1, int(num)), ident, f.name, m))
    rows.sort()
    return rows


def group_rows(rows):
    """Tickets grouped by version, oldest version first, non-version groups last."""
    groups = {}
    for row in rows:
        ms = row[3].get("milestone", "").replace("&amp;", "&")
        ms = "" if ms in ("(none)", "None") else ms
        slug, title = version_key(ms)
        groups.setdefault(slug, {"title": title, "rows": []})["rows"].append((ms, row))

    def order(slug):
        m = re.match(r"v(\d+)\.(\d+)(?:\.(\d+))?$", slug)
        return (0, int(m.group(1)), int(m.group(2)), int(m.group(3) or 0)) if m else (1, 0, 0, 0)

    return {k: groups[k] for k in sorted(groups, key=lambda k: (order(k), k))}


def build_version_hubs():
    """versions/<slug>.md: one hub per version, linking its ticket files. This is what
    makes the Obsidian graph show tickets grouped under their version."""
    today = date.today().isoformat()
    hubs = {}
    for slug, g in group_rows(collect_rows()).items():
        lines = ["---", "type: hub"]
        if re.match(r"v\d+\.\d+(\.\d+)?$", slug):
            lines.append("version: " + (slug if slug.count(".") == 2 else slug + ".0"))
        lines += [
            "status: shipped", "tickets: []", f"updated: {today}", "---", "",
            f"# Linear export: {g['title']}",
            "",
            "Up: [Linear export index](../INDEX.md)",
            "",
            f"**{len(g['rows'])} closed tickets.**",
            "",
            "| Ticket | Title | Status | Milestone | Labels | Closed |",
            "|---|---|---|---|---|---|",
        ]
        for ms, (_, ident, fname, m) in g["rows"]:
            title = m.get("title", "").replace("|", "\\|")
            lines.append(
                f"| [{ident}](../tickets/{fname}) | {title} | {m.get('status', '')} | {ms} | {m.get('labels', '')} | {m.get('completed', '')} |"
            )
        hubs[slug] = "\n".join(lines) + "\n"
    return hubs


def build_index():
    rows = collect_rows()
    lines = [
        "---", "type: hub", "status: current", "tickets: []", f"updated: {date.today().isoformat()}", "---", "",
        "# Linear Export — Closed PPDO Portal Tickets",
        "",
        "Up: [Standards and reference](../STANDARDS.md)",
        "",
        f"**{len(rows)} tickets.** RAL-* exported 2026-08-27; PPDO-* exported 2026-10-19 — both before archiving.",
        "",
        "See [README.md](README.md) for what this is and how to refresh it.",
        "",
        "Tickets are grouped by version. Each version hub lists its tickets, with links to the ticket files.",
        "",
        "| Version | Tickets |",
        "|---|---|",
    ]
    for slug, g in group_rows(rows).items():
        lines.append(f"| [{g['title']}](versions/{slug}.md) | {len(g['rows'])} |")
    return "\n".join(lines) + "\n"


def write_index_and_hubs():
    (OUT / "INDEX.md").write_text(build_index(), encoding="utf-8", newline="\n")
    VERSIONS.mkdir(parents=True, exist_ok=True)
    hubs = build_version_hubs()
    for old in VERSIONS.glob("*.md"):
        if old.stem not in hubs:
            old.unlink()
    for slug, text in hubs.items():
        (VERSIONS / f"{slug}.md").write_text(text, encoding="utf-8", newline="\n")
    return len(hubs)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("csv")
    ap.add_argument("--team", default="PPDO-Dev")
    ap.add_argument("--exclude-milestone", action="append", default=[])
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    with open(a.csv, encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        headers = reader.fieldnames or []
        rows = list(reader)
    if "UUID" not in headers and "Uuid" not in headers:
        print(f"WARNING: no UUID column. Headers: {headers}", file=sys.stderr)

    kept, skipped = [], {"open": 0, "other team": 0, "excluded milestone": 0}
    for r in rows:
        if col(r, "Team") != a.team:
            skipped["other team"] += 1
            continue
        if col(r, "Status").lower() not in CLOSED:
            skipped["open"] += 1
            continue
        ms = col(r, "Project Milestone", "Milestone")
        if any(x.lower() in ms.lower() for x in a.exclude_milestone):
            skipped["excluded milestone"] += 1
            continue
        kept.append(r)

    print(f"{len(rows)} rows; writing {len(kept)}; skipped {skipped}")
    missing_uuid = [col(r, "ID") for r in kept if not col(r, "UUID", "Uuid", "Issue ID")]
    if missing_uuid:
        print(f"WARNING: {len(missing_uuid)} without a UUID (archive script will skip them): {missing_uuid[:10]}")
    if a.dry_run:
        for r in kept[:15]:
            print("  ", col(r, "ID"), "|", col(r, "Status"), "|", col(r, "Project Milestone", "Milestone"))
        return

    TICKETS.mkdir(parents=True, exist_ok=True)
    for r in kept:
        ident, text = render(r)
        (TICKETS / filename(ident)).write_text(text, encoding="utf-8", newline="\n")
    (OUT / "INDEX.md").write_text(build_index(), encoding="utf-8", newline="\n")
    print(f"wrote {len(kept)} ticket files and INDEX.md")


if __name__ == "__main__":
    main()
