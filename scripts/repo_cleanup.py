#!/usr/bin/env python3
"""
Tag past releases, then retire the branches those tags make redundant.

WHY TAGS FIRST
  There are no version tags in this repo. Branch names are currently the only
  marker of "this is where v1.4.3 was", which is what makes 252 branches feel
  load-bearing. A tag does that job better and permanently. Once the tags exist,
  a fully-merged branch is genuinely redundant — every one of its commits is
  already in main's history, and deleting it loses nothing but the name.

WHY --merged IS SAFE HERE
  This repo merges PRs with real merge commits ("Merge pull request #N"), so
  `git branch --merged` is accurate. Under SQUASH merging it is not — a squashed
  branch never becomes an ancestor, so --merged reports it as unmerged and, worse,
  the reverse check gives false confidence. If the merge strategy ever changes,
  revisit this script before trusting it.

USAGE  (run from the repo root; every action is a dry run unless --yes is passed)
  python scripts/repo_cleanup.py --report          # categorise every branch, change nothing
  python scripts/repo_cleanup.py --tag-releases    # show the 19 tags that would be created
  python scripts/repo_cleanup.py --tag-releases --yes
  python scripts/repo_cleanup.py --archive-untagged --yes   # archive/* tags for unmerged, untagged
  python scripts/repo_cleanup.py --delete-merged           # show what would be deleted
  python scripts/repo_cleanup.py --delete-merged --yes

ORDER: --report, then --tag-releases, push the tags, then --delete-merged.
Never delete before the tags are pushed to origin.
"""

import argparse
import subprocess
import sys

# The 19 release-branch merges on main, oldest first. Read off first-parent
# history: `git log --first-parent main --merges | grep 'from obiken-01/release/'`.
# Where a release merged more than once (the branch was reopened), the tag goes on
# the LAST merge — that is the commit that actually represents the shipped state.
RELEASES = [
    ("v1.1.0",  "67c2fca"),
    ("v1.2.0",  "66fa150"),
    ("v1.3.0",  "6a1a084"),
    ("v1.4.0",  "4c9f871"),
    ("v1.4.1",  "e73cc8f"),  # merged 3x (#126, #127, #132) — last one
    ("v1.4.2",  "40ff64a"),
    ("v1.4.3",  "eef6622"),
    ("v1.4.4",  "13ca0c5"),
    ("v1.4.5",  "77a9afb"),
    ("v1.4.7",  "d9f1c86"),  # v1.4.6 never merged — see archive/fix/v1.4.6-*
    ("v1.4.8",  "789d94c"),
    ("v1.5.0",  "970f839"),
    ("v1.6.0",  "829463a"),
    ("v1.7.0",  "ef99d0b"),
    ("v1.7.1",  "91c8ae4"),
    ("v1.7.2",  "58b2507"),  # merged 2x (#227, #231) — last one
    ("v1.7.2B", "677193b"),  # merged 2x (#243, #245) — last one
    ("v1.7.3",  "0629c10"),
    ("v1.7.4",  "8f765f9"),
]

# Branches never to touch, whatever the merge state says.
PROTECTED = ("main", "HEAD")


def sh(*args, check=True):
    r = subprocess.run(["git", *args], capture_output=True, text=True)
    if check and r.returncode != 0:
        print(f"ERROR: git {' '.join(args)}\n{r.stderr.strip()}", file=sys.stderr)
        sys.exit(1)
    return r.stdout.strip()


def remote_branches():
    out = sh("branch", "-r", "--format=%(refname:short)")
    return [b for b in out.splitlines() if "HEAD" not in b and b != "origin/main"]


def merged_set():
    out = sh("branch", "-r", "--merged", "origin/main", "--format=%(refname:short)")
    return {b for b in out.splitlines() if "HEAD" not in b}


def tags_pointing_at(ref):
    return [t for t in sh("tag", "--points-at", ref, check=False).splitlines() if t]


def is_live_release_work(name):
    """Branches feeding the current release branch — never archive or delete these."""
    short = name.removeprefix("origin/")
    return "1.8" in short or short.startswith("release/")


def categorise():
    merged = merged_set()
    cats = {"merged": [], "archived": [], "untagged": [], "live": []}
    for b in remote_branches():
        if is_live_release_work(b):
            cats["live"].append(b)
        elif b in merged:
            cats["merged"].append(b)
        elif tags_pointing_at(b):
            cats["archived"].append(b)
        else:
            cats["untagged"].append(b)
    return cats


def cmd_report():
    c = categorise()
    print(f"{sum(len(v) for v in c.values())} remote branches (excluding main)\n")

    print(f"[{len(c['live']):>3}] LIVE — current release work. Never touched by this script.")
    for b in sorted(c["live"])[:6]:
        print(f"        {b}")
    if len(c["live"]) > 6:
        print(f"        ... and {len(c['live']) - 6} more")

    print(f"\n[{len(c['merged']):>3}] MERGED into main — safe to delete once release tags are pushed.")
    print("        Every commit is already in main's history; only the name is lost.")

    print(f"\n[{len(c['archived']):>3}] UNMERGED but already archive/*-tagged — safe to delete, the tag holds them.")
    for b in sorted(c["archived"]):
        print(f"        {b}")

    print(f"\n[{len(c['untagged']):>3}] UNMERGED and UNTAGGED — deleting these WOULD lose work.")
    for b in sorted(c["untagged"]):
        n = sh("rev-list", "--count", f"origin/main..{b}", check=False) or "?"
        print(f"        {b}  ({n} commits not in main)")
        for line in sh("log", "--oneline", "-3", f"origin/main..{b}", check=False).splitlines():
            print(f"            {line}")
    if c["untagged"]:
        print("\n        Decide per branch: --archive-untagged tags them, but if the content is")
        print("        current (planning docs, live scripts) prefer merging it forward instead.")


def cmd_tag_releases(apply):
    print("Release tags\n")
    todo = []
    for tag, sha in RELEASES:
        existing = sh("tag", "-l", tag, check=False)
        if existing:
            print(f"  = {tag:<9} already exists")
            continue
        if sh("cat-file", "-t", sha, check=False) != "commit":
            print(f"  ! {tag:<9} commit {sha} not found — skipped")
            continue
        subject = sh("log", "-1", "--format=%s", sha, check=False)[:62]
        date = sh("log", "-1", "--format=%ad", "--date=short", sha, check=False)
        print(f"  + {tag:<9} {sha}  {date}  {subject}")
        todo.append((tag, sha, date))

    if not todo:
        print("\nNothing to create.")
        return
    if not apply:
        print(f"\nDRY RUN — {len(todo)} tags would be created. Re-run with --yes.")
        return

    for tag, sha, date in todo:
        sh("tag", "-a", tag, sha, "-m", f"Release {tag} — merged to main {date}")
        print(f"  created {tag}")
    print(f"\n{len(todo)} tags created locally. Push them BEFORE deleting anything:")
    print("  git push origin --tags")


def cmd_archive_untagged(apply):
    c = categorise()
    if not c["untagged"]:
        print("No unmerged, untagged branches.")
        return
    print("Archive tags for unmerged, untagged branches\n")
    for b in sorted(c["untagged"]):
        short = b.removeprefix("origin/")
        tag = f"archive/{short}"
        print(f"  + {tag}")
        if apply:
            sh("tag", "-a", tag, b, "-m", f"Archived branch {short}")
    if not apply:
        print(f"\nDRY RUN — {len(c['untagged'])} tags would be created. Re-run with --yes.")
    else:
        print("\nPush them:  git push origin --tags")


def cmd_delete_merged(apply):
    c = categorise()
    targets = sorted(c["merged"] + c["archived"])

    missing = [t for t, _ in RELEASES if not sh("tag", "-l", t, check=False)]
    if missing:
        print(f"REFUSING: {len(missing)} release tags do not exist yet ({', '.join(missing[:4])}...).")
        print("Run --tag-releases --yes and push them first — the tags are what make this safe.")
        sys.exit(1)

    print(f"{len(targets)} branches to delete "
          f"({len(c['merged'])} merged into main, {len(c['archived'])} archive-tagged)\n")
    for b in targets[:10]:
        print(f"  - {b}")
    if len(targets) > 10:
        print(f"  ... and {len(targets) - 10} more")

    if not apply:
        print(f"\nDRY RUN — nothing deleted. Re-run with --yes.")
        print("Confirm 'git push origin --tags' has run first.")
        return

    names = [b.removeprefix("origin/") for b in targets if b.removeprefix("origin/") not in PROTECTED]
    print(f"\nDeleting {len(names)} remote branches in batches...")
    for i in range(0, len(names), 25):
        batch = names[i:i + 25]
        subprocess.run(["git", "push", "origin", "--delete", *batch], check=False)
        print(f"  {min(i + 25, len(names))}/{len(names)}")
    print("\nDone. Prune local refs with:  git remote prune origin")
    print("Then review local branches:    git branch --merged main")


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--report", action="store_true")
    g.add_argument("--tag-releases", action="store_true")
    g.add_argument("--archive-untagged", action="store_true")
    g.add_argument("--delete-merged", action="store_true")
    p.add_argument("--yes", action="store_true", help="actually do it (default is a dry run)")
    a = p.parse_args()

    sh("rev-parse", "--git-dir")  # fail fast outside a repo
    print("Fetching origin...\n")
    sh("fetch", "origin", "--prune", "--tags", check=False)

    if a.report:
        cmd_report()
    elif a.tag_releases:
        cmd_tag_releases(a.yes)
    elif a.archive_untagged:
        cmd_archive_untagged(a.yes)
    elif a.delete_merged:
        cmd_delete_merged(a.yes)


if __name__ == "__main__":
    main()
