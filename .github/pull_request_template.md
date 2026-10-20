<!--
PR description standard: docs/PR_DESCRIPTION_STANDARD.md
Full form for any behaviour change. Docs/config/CI/one-liners: delete everything below
"Short form" markers and use the short form at the bottom.
No secrets, no personal data, no screenshots (the repo is public; send screenshots in chat).
-->

**TL;DR:** <what was wrong, what is fixed, deploy needs: "No migration, no settings" / "⚠️ …">

Closes PPDO-

## The issue / aim
<what a user saw or needed, in their terms; where it came from>

## Purpose
<who is affected, what it costs them today, what happens if we don't>

## The fix, at a glance
-
**Alternatives considered:**
- <option> — not chosen because …

## Before → after
| Role / screen | Before | After |
|---|---|---|
|  |  |  |
<!-- mark intended behaviour changes with ↩️ -->

## Code walkthrough
<!-- ordered, in run order: file:line, short excerpt where the logic matters, one sentence each; call out guards (scope, permissions, validation) -->
1.

## Automated tests
- [ ] `<TestName>`: proves …
- [ ] Red-tested: <guard removed> → <tests that failed>
- [ ] `dotnet test`: N passed (was M), 0 failed
- [ ] Frontend: tsc / lint / vitest
- [ ] CI green

## Live checks
<!-- from the spec's acceptance checklist; mark each live / unit-tested only / not checked, and as which user -->
- [ ]

## Risk & rollback
- <risk> → rollback: <exact undo>
<!-- ⚠️ line if this touches auth, PermissionService, office/division scope, migrations or components/ui/ -->

## Deploy notes
None.

## How to review this yourself
- **Read first:**
- **Try it:** <user, screen, action>

## Confidence: NN%
Rests on: …
Unverified: …
Would raise it: …

<!-- ===== Short form (docs / config / CI / one-line fixes) =====
**TL;DR:** … Closes PPDO-

## What changed
-

## Checks
- [ ]

## Deploy notes
None.

## Confidence: NN%
…
-->
