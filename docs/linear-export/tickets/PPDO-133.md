---
id: PPDO-133
title: 'Demo 2.11 — Show unresolved comment counts on every submit hop'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: PPDO-72, PPDO-130, PPDO-151
url: https://linear.app/ralphoksiprojects/issue/PPDO-133
uuid: 9b39e7f7-197f-467a-8903-6749f8d432a6
---

# PPDO-133 — Demo 2.11 — Show unresolved comment counts on every submit hop

*"Add the number of unresolved comments (if any) to the warning popup when submitting."*

**Already built — but narrower than asked.** This is a scope change, not new work.

`AipSubmitChecklist` already renders a confirmation with the counts, split by source, via `unresolvedWarning()`:

> *"This office still has 2 unresolved comments from PPDO and 1 from your department head. They stay on the record, and PPDO will see them alongside the re-submitted work. You can send it on anyway."*

The counts are already separated by `fromPpdo` / `fromDepartmentHead` on purpose — *"only the side that wrote a comment may clear it, so a single figure would hide which"*.

## What's actually missing

It fires on one hop only:

```ts
const needsUnresolvedWarning =
  stage.kind === "toPpdo" && stage.resubmit && (unresolved?.total ?? 0) > 0;
```

That was a deliberate spec decision ([PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow), decision 10): at the **first** submit to PPDO the only possible unresolved comments are the department head's own, and warning someone about their own notes helps nobody.

So the change is to widen the trigger — and the reasoning above still holds for the first-submit case, so widening it blindly would reintroduce exactly the noise the original decision avoided.

## ⚠️ Worth doing after Demo 2.8

The genuinely missing hop is the **division → department head** submit, which does not exist yet. There, a division head submitting past their department head's unresolved comments is precisely the case worth warning about — and it's the case Ralph most likely had in mind. Doing this first means guessing; doing it after means the trigger has somewhere real to fire.

## Two rules to preserve

* ⚠️ **It stays soft.** The existing comment is emphatic: *"Do not turn this into a disabled button."* An office may legitimately submit with a comment outstanding.
* The wording says the comments **stay** open rather than being lost — nothing is discarded, and saying otherwise pushes offices into resolving comments they have no right to resolve.

## Acceptance

- [ ] Which hops warn is a stated decision, not an accident of the condition
- [ ] Counts still split by source
- [ ] Still soft — no blocked submit
- [ ] No warning when there is nothing unresolved
