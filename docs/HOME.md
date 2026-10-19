---
type: hub
status: current
tickets: [PPDO-233]
updated: 2026-10-19
---

# PPDO Portal docs

This folder is an Obsidian vault and also ordinary markdown on GitHub. Every link is a standard relative markdown link, so it resolves in both. Each document carries a properties block (type, version, status, tickets, supersedes, updated) so you can tell from the file itself whether it is current.

## Start here

- **Standards** (what to read before touching a query, a page, a table or a spec): [Standards and reference](STANDARDS.md)
- **Current release:** [v1.9 hub](v1.9/README.md) (its pre-deployment checklist is listed there)
- **The biggest design record:** [v1.8 hub](v1.8/README.md) (start with the Phase Plan listed there)
- **Release timeline:** [Releases canvas](Releases.canvas)
- **Tables over the docs and tickets:** [Docs.base](Docs.base) (specs by version, current standards, superseded, checklists and runbooks, docs to verify) and [Tickets.base](Tickets.base) (the Linear export by milestone, status, label)
- **Linear ticket archive:** [linear-export](linear-export/README.md)

### Root documents outside the vault

These live at the repo root, not in `docs/`, so they are named here rather than linked: `CLAUDE.md` (rules for Claude sessions; wins on any conflict), `README.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md` (full spec) and `PPDO_PROJECT_CONTEXT.md` (design tokens and business logic).

## Version hubs

- [v1.1 — Budget Planning foundation](v1.1/README.md)
- [v1.1.1 — Small features and fixes](v1.1.1/README.md)
- [v1.2 — Allocation, divisions and the permission model](v1.2/README.md)
- [v1.3 — LDIP entry and upload](v1.3/README.md)
- [v1.4 — WFP rework](v1.4/README.md)
- [v1.4.3 — Other fund source ceiling and allocation](v1.4.3/README.md)
- [v1.4.4 — WFP Excel export and dashboard scoping](v1.4.4/README.md)
- [v1.5 — PPMP report](v1.5/README.md)
- [v1.7 — Inventory](v1.7/README.md)
- [v1.8 — AIP redesign](v1.8/README.md)
- [v1.9 — Reviewer edits, comments, notifications and WFP export](v1.9/README.md)

There is no docs folder for v1.0 or v1.6.0; their record is in the root `CLAUDE.md` release history.

## Status values

| `status` | Meaning |
|---|---|
| `current` | In force now: a standard, or a spec, plan or checklist still being worked from |
| `shipped` | Built and merged for its release. Read it as the record of what was built; the code is the source of truth |
| `superseded` | Replaced. `superseded_by` names the replacement, and `superseded_scope` says whether only part was replaced |
| `draft` | Not yet accepted |
| `reference` | Background (findings, studies, audits, retrospectives), or a document whose status could not be established from the evidence. Verify against the code before relying on it |

A document with `superseded_by` but a `current` or `shipped` status is replaced only in part; the `superseded_scope` line says which part.

Where a document already had its own properties, they are kept: the old value of `status`, `version`, `tickets` or `supersedes` moved to `status_note`, `version_note`, `tickets_note` or `supersedes_note`.

## Document types

`spec`, `standard`, `checklist`, `runbook`, `audit`, `findings`, `guide`, `wireframe`, `retro`, `plan`, `hub`.

## Rules for this vault

- Relative markdown links only, never wikilinks. A relative path names exactly one file; GitHub does not render wikilinks.
- The repository is public: nothing personal goes in `docs/`.
- Do not move or rename a document; code and commits refer to these paths.
- New document: add the properties block, an `Up:` line under the title, and add it to its version hub.
- Per-machine vault state (`workspace.json`, `graph.json`, `appearance.json`, plugins, themes) is not committed.

## Graph colour groups

`docs/.obsidian/graph.json` is git-ignored, so the colours do not travel with the repo. To apply them on another machine, open Graph view, then Filters and Groups, or paste this `colorGroups` block into that file with Obsidian closed (keep any other keys already in the file):

```json
{
  "colorGroups": [
    {
      "query": "path:\"v1.1/\"",
      "color": {
        "a": 1,
        "rgb": 8038082
      }
    },
    {
      "query": "path:\"v1.1.1/\"",
      "color": {
        "a": 1,
        "rgb": 6000560
      }
    },
    {
      "query": "path:\"v1.2/\"",
      "color": {
        "a": 1,
        "rgb": 7321504
      }
    },
    {
      "query": "path:\"v1.3/\"",
      "color": {
        "a": 1,
        "rgb": 9421434
      }
    },
    {
      "query": "path:\"v1.4/\"",
      "color": {
        "a": 1,
        "rgb": 13223018
      }
    },
    {
      "query": "path:\"v1.4.3/\"",
      "color": {
        "a": 1,
        "rgb": 14264410
      }
    },
    {
      "query": "path:\"v1.4.4/\"",
      "color": {
        "a": 1,
        "rgb": 13601364
      }
    },
    {
      "query": "path:\"v1.5/\"",
      "color": {
        "a": 1,
        "rgb": 13201263
      }
    },
    {
      "query": "path:\"v1.7/\"",
      "color": {
        "a": 1,
        "rgb": 11759539
      }
    },
    {
      "query": "path:\"v1.8/\"",
      "color": {
        "a": 1,
        "rgb": 8355798
      }
    },
    {
      "query": "path:\"v1.9/\"",
      "color": {
        "a": 1,
        "rgb": 4171683
      }
    },
    {
      "query": "[\"type\":standard]",
      "color": {
        "a": 1,
        "rgb": 14727226
      }
    },
    {
      "query": "path:\"database/\" OR path:\"external-api/\"",
      "color": {
        "a": 1,
        "rgb": 9079434
      }
    },
    {
      "query": "path:\"linear-export/tickets/\"",
      "color": {
        "a": 1,
        "rgb": 13158600
      }
    }
  ]
}
```
