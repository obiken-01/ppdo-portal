#!/usr/bin/env python3
"""
Archive completed Linear issues via the GraphQL API.

Linear's UI has no manual archive ("Archiving happens automatically with no option
to manually archive items") and auto-archive will not fire while a project is still
active — but the GraphQL API exposes `issueArchive` directly. That is the only way
to free the free plan's 250 non-archived-issue cap without DELETING issues, which
purges them permanently after 30 days.

Issue UUIDs are read from the committed snapshot in docs/linear-export/tickets/,
so this never needs the CSV again.

SETUP
  1. Linear -> Settings -> Security & access -> Personal API keys -> create one.
  2. Put it in your environment. NEVER paste it into a chat or commit it.

       PowerShell:  $env:LINEAR_API_KEY = "lin_api_..."
       bash:        export LINEAR_API_KEY="lin_api_..."

USAGE  (run from the repo root)
  python scripts/linear_archive.py --status      # read-only: what would be archived
  python scripts/linear_archive.py --verify      # archive exactly ONE, then stop
  python scripts/linear_archive.py --all         # archive the rest
  python scripts/linear_archive.py --restore-one <RAL-ID>   # undo, to prove it is reversible

Progress is appended to scripts/.linear_archive_log.tsv, so --all is resumable and
will not re-archive anything already done.
"""

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.linear.app/graphql"
REPO = Path(__file__).resolve().parent.parent
TICKETS = REPO / "docs" / "linear-export" / "tickets"
LOG = Path(__file__).resolve().parent / ".linear_archive_log.tsv"


def die(msg, code=1):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(code)


def api_key():
    k = os.environ.get("LINEAR_API_KEY", "").strip()
    if not k:
        die(
            "LINEAR_API_KEY is not set.\n"
            '  PowerShell:  $env:LINEAR_API_KEY = "lin_api_..."\n'
            '  bash:        export LINEAR_API_KEY="lin_api_..."'
        )
    return k


def gql(query, variables=None):
    """POST a GraphQL request. Returns (data, errors). Never logs the key."""
    body = json.dumps({"query": query, "variables": variables or {}}).encode()
    req = urllib.request.Request(
        API,
        data=body,
        headers={
            "Content-Type": "application/json",
            "Authorization": api_key(),  # Linear personal keys go raw, no "Bearer"
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            payload = json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        detail = e.read().decode()[:400]
        if e.code in (401, 403):
            die(f"HTTP {e.code} — the API key was rejected or lacks permission.\n{detail}")
        return None, [{"message": f"HTTP {e.code}: {detail}"}]
    except urllib.error.URLError as e:
        return None, [{"message": f"network error: {e.reason}"}]
    return payload.get("data"), payload.get("errors")


def load_tickets():
    """[(RAL-id, uuid)] from the committed export, sorted by issue number."""
    if not TICKETS.is_dir():
        die(f"{TICKETS} not found — run this from the repo root.")
    out = []
    for f in TICKETS.glob("*.md"):
        head = f.read_text(encoding="utf-8")[:1200]
        rid = re.search(r"^id:\s*(\S+)", head, re.M)
        uid = re.search(r"^uuid:\s*([0-9a-fA-F-]{36})", head, re.M)
        if rid and uid:
            out.append((rid.group(1), uid.group(1)))
    out.sort(key=lambda t: int(re.search(r"(\d+)$", t[0]).group(1)))
    return out


def done_ids():
    if not LOG.exists():
        return set()
    return {
        ln.split("\t")[0]
        for ln in LOG.read_text(encoding="utf-8").splitlines()
        if ln.strip() and ln.split("\t")[-1] == "ok"
    }


def record(rid, uid, status):
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(f"{rid}\t{uid}\t{time.strftime('%Y-%m-%dT%H:%M:%S')}\t{status}\n")


def check_state(uid):
    """Returns (identifier, archivedAt) or None."""
    data, errs = gql(
        "query($id:String!){ issue(id:$id){ identifier archivedAt } }", {"id": uid}
    )
    if errs or not data or not data.get("issue"):
        return None
    return data["issue"]["identifier"], data["issue"].get("archivedAt")


def archive(uid):
    data, errs = gql(
        "mutation($id:String!){ issueArchive(id:$id){ success } }", {"id": uid}
    )
    if errs:
        return False, "; ".join(e.get("message", "?") for e in errs)
    if data and data.get("issueArchive", {}).get("success"):
        return True, "ok"
    return False, f"unexpected response: {json.dumps(data)[:200]}"


def unarchive(uid):
    data, errs = gql(
        "mutation($id:String!){ issueUnarchive(id:$id){ success } }", {"id": uid}
    )
    if errs:
        return False, "; ".join(e.get("message", "?") for e in errs)
    return bool(data and data.get("issueUnarchive", {}).get("success")), "ok"


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--status", action="store_true", help="read-only; mutates nothing")
    g.add_argument("--verify", action="store_true", help="archive exactly ONE issue, then stop")
    g.add_argument("--all", action="store_true", help="archive every remaining issue")
    g.add_argument("--restore-one", metavar="RAL-ID", help="unarchive one issue, to prove reversibility")
    p.add_argument("--delay", type=float, default=0.25, help="seconds between calls (default 0.25)")
    a = p.parse_args()

    tickets = load_tickets()
    already = done_ids()
    print(f"{len(tickets)} tickets in the local export; {len(already)} already logged as archived.\n")

    if a.status:
        rid, uid = tickets[0]
        print("Connectivity check against the oldest ticket...")
        st = check_state(uid)
        if not st:
            die("Could not read that issue. Check the API key and that it has read access.")
        print(f"  {st[0]}: archivedAt = {st[1] or 'null (not archived)'}")
        todo = [t for t in tickets if t[0] not in already]
        print(f"\nWould archive {len(todo)} issues: {todo[0][0]} ... {todo[-1][0]}")
        print("Nothing was modified. Run --verify next.")
        return

    if a.restore_one:
        match = [t for t in tickets if t[0].upper() == a.restore_one.upper()]
        if not match:
            die(f"{a.restore_one} is not in the local export.")
        rid, uid = match[0]
        ok, msg = unarchive(uid)
        print(f"{rid}: {'RESTORED' if ok else 'FAILED — ' + msg}")
        if ok:
            st = check_state(uid)
            print(f"  archivedAt is now: {st[1] or 'null'}  <- reversibility confirmed")
        return

    todo = [t for t in tickets if t[0] not in already]
    if not todo:
        print("Nothing left to archive.")
        return

    if a.verify:
        todo = todo[:1]
        print("VERIFY MODE — archiving one issue only.\n")
    else:
        print(f"Archiving {len(todo)} issues. Ctrl+C is safe; progress is logged and resumable.\n")

    ok_n = fail_n = 0
    for i, (rid, uid) in enumerate(todo, 1):
        good, msg = archive(uid)
        record(rid, uid, "ok" if good else f"failed: {msg}")
        if good:
            ok_n += 1
            print(f"  [{i}/{len(todo)}] {rid} archived")
        else:
            fail_n += 1
            print(f"  [{i}/{len(todo)}] {rid} FAILED — {msg}")
            if fail_n >= 3 and ok_n == 0:
                die("First 3 calls all failed — stopping before doing damage. See the message above.")
        time.sleep(a.delay)

    print(f"\nDone. archived={ok_n} failed={fail_n}. Log: {LOG}")
    if a.verify and ok_n:
        rid, uid = todo[0]
        st = check_state(uid)
        print(f"\nConfirmation — {rid} archivedAt = {st[1] if st else '?'}")
        print("The issue is archived, NOT deleted: still searchable and restorable in Linear.")
        print(f"Undo it with:  python scripts/linear_archive.py --restore-one {rid}")
        print("If that all looks right, run --all for the remaining issues.")


if __name__ == "__main__":
    main()
