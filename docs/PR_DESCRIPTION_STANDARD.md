---
type: standard
status: current
tickets: [PPDO-206]
updated: 2026-10-19
---

# Pull Request Description Standard

Up: [Standards and reference](STANDARDS.md)

How to write a PR description in this repo. The reader is **Ralph**, who reviews every PR and did not
write the code (Claude does, in a dev session). A description is done when he can answer three
questions from it alone, without opening a file: **what was wrong, what we changed, and how sure we are.**

The GitHub template [`.github/pull_request_template.md`](../.github/pull_request_template.md) carries
the headings. This document says what goes under each one.

Adopted 2026-10-19 (Ralph). [PR #515](https://github.com/obiken-01/ppdo-portal/pull/515) (PPDO-206) is
the closest existing example; the gaps it had are noted at the end.

---

## Which form

| Form | Use for |
|---|---|
| **Full** | Anything that changes code behaviour: backend logic, frontend behaviour, queries, auth, scope, migrations, dependencies. |
| **Short** | Docs only, config or CI only, version bumps, a typo or a one-line fix with no behaviour change. |

When in doubt, use the full form. A short form that hides a behaviour change is worse than a long
form for a small one.

---

## Full form (in order)

### 1. TL;DR

Two or three plain sentences: what was wrong, what is fixed, and whether deploy needs anything
(**"No migration, no settings"** or **"⚠️ 1 migration before the code"**). Then `Closes PPDO-NNN`.

### 2. The issue / aim

What a **user saw or needed**, in their terms, not the code's. Use who, where and what happened:
*"An encoder deleted the activity PPDO had commented on, and the comment, the reason for the return,
vanished."* Name the source: the ticket, the UAT run or the meeting. For new work, the aim stated the
same way.

### 3. Purpose

Why it matters: who is affected (by role), what it costs them today, and what happens if we don't do
it. One short paragraph.

### 4. The fix, at a glance

The approach in plain words, a few bullets. Then **alternatives considered**, each with one line on
why it was not chosen. This is where a reviewer learns the design, and it is the part a code diff
can never show.

### 5. Before → after

A small table by role (encoder, department head, PPDO reviewer, finance, SuperAdmin; only the ones
affected) or by screen. **Mark every intended behaviour change with ↩️**, so a change of behaviour
is never mistaken for a side effect.

### 6. Code walkthrough

An **ordered tour**, in the order the code runs: where the request or click enters, then each key
step. For each step, give a `file:line` link, a short excerpt (3–10 lines) when the logic matters, and
one sentence on what it does. Call out **where the guards are**: office and division scope,
permission checks, validation, the concurrency token. Readable, not exhaustive: 4–8 stops for a
typical ticket.

Illustrative shape (names and lines are examples):

```text
1. AipSubmitChecklist.tsx:540 — the Submit click builds the warning list via buildSubmitWarnings()
2. lib/aip-submit-confirm.ts:12 — pure rules: which warnings apply to which role
   ...
```

### 7. Automated tests

A checklist. Each **new or changed test, by name, and what it proves**. Illustrative shape:

```markdown
- [x] `DeleteNode_KeepsComments_AndSnapshotsThem`: a delete no longer removes comments, and the audit row still lists them
- [x] Red-tested: put the `DeleteByIdsAsync` call back → the 3 keep-comments tests fail
- [x] `dotnet test`: 3,679 passed (was 3,671), 0 failed
- [x] Frontend: tsc clean, lint clean, vitest 171/171
- [x] CI green
```

**Red-test every guard**: remove it, watch the test fail, restore it, and say so here. A test that
never failed proves nothing (`DoesNotContain` and `Times.Never` pass vacuously).

### 8. Live checks

What was tried in the running app, **as which user**, and what happened. Copy the lines from the
spec's acceptance checklist (`SPEC_STANDARD.md` §2.10) and mark each one *live*, *unit-tested only* or
*not checked*. Screenshots are **sent in chat, never posted here**: the repo is public and local data
is real.

### 9. Risk & rollback

For each material risk: what could break, how likely it is, and the **exact undo**. "Revert the PR"
is a rollback only when no migration or data change has run. Otherwise give the down-migration or
the restore step.

Add a ⚠️ line whenever the PR touches **auth, `PermissionService`, office or division scope,
migrations, or shared `components/ui/`**, and say what was done to check it.

### 10. Deploy notes

Migrations (name, additive or destructive, run **before** the code), app settings, Azure steps and
their order, and anything a later PR must do (merge forward, a uat-only workflow change). Write
**"None"** when there is nothing. Leaving the section empty reads as "not checked".

### 11. How to review this yourself

- **Read first:** the 2–3 files that carry the change, in reading order.
- **Try it:** the shortest path to see it work: who to log in as (local or UAT), which screen, and
  which action. Never write a password here.

### 12. Confidence %

One number, then what it rests on (tests, red-tested guards, live checks) and **what is unverified**:
an untested path, an unchecked role, a migration not yet run. Add what would raise it. Be honest
rather than high; the number should drop when something was not checked.

```markdown
## Confidence: 88%
Rests on: unit tests for the delete and dialog rules; live run as encoder, head, reviewer, finance.
Unverified: the division-level submit path (no submittable division locally). Raises to ~95% after
one division submit on UAT.
```

---

## Short form

```markdown
**TL;DR:** <what and why, 1–2 sentences>. Closes PPDO-NNN.

## What changed
- ...

## Checks
- [x] <what was run or looked at, e.g. link check, CI, rendered on GitHub>

## Deploy notes
None.

## Confidence: NN%
<what it rests on, what is unverified>
```

---

## Writing rules

- **Plain words over code words in sections 1–5.** Code names belong in the walkthrough.
- **Relative claims need a baseline:** "4 reads → 0", "3,671 → 3,679 tests", not "fewer reads".
- **No secrets, no personal data, no screenshots.** The repo is public.
- **Keep it current.** If review changes the fix, update the description before merge, since it is the
  record of what shipped.
- Dev-session prompts (`TICKET_PROMPT_STANDARD.md`) point here, so every PR follows this without
  being told twice.

---

## What #515 was missing, against this standard

| Section | #515 |
|---|---|
| 2. The issue | Named the UAT run, not what the user hit |
| 3. Purpose | Absent |
| 4. Alternatives | Absent, though the choice (keep comments vs log them in history) was made |
| 6. Walkthrough | Listed changes per item, with no ordered tour or `file:line` stops |
| 7. Tests | Totals and one red-test, not each new test by name |
| 9–11 | Risk, deploy and review guide absent (it did state "No migration") |

The rest (before/after detail, the acceptance checklist marked live or unit-tested, and the confidence
section) is the model to keep.
