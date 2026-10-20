---
id: PPDO-130
title: 'Demo 2.8 — Division head submits to department head, who can return that division''s work'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-29
parent: PPDO-122
blocked_by: (none)
related: PPDO-70, PPDO-72, PPDO-131, PPDO-133, PPDO-134, PPDO-135, PPDO-138, PPDO-140, PPDO-153, PPDO-165, PPDO-166, PPDO-167, PPDO-168, PPDO-169, PPDO-170, PPDO-171, PPDO-172
url: https://linear.app/ralphoksiprojects/issue/PPDO-130
uuid: ba980718-89c7-4222-8f4a-ade4bb5e94bf
---

# PPDO-130 — Demo 2.8 — Division head submits to department head, who can return that division's work

*"Establish the flow where when division head user (encoder user) submits to department head. When submitted they won't be able to edit their entries. Dept head can return that division's work. Note: this maybe big?"*

Yes — big. **Needs a spec before any code** (CLAUDE.md: no implementation of a feature that adds a table, endpoint, permission or screen until the spec exists).

## Why it is big

The submit workflow is **office-grained** everywhere today. This adds a division-grained hop *below* it:

```
division → department head → PPDO
```

That touches the state machine, the locking rule ([PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return) locks at `SubmittedToPpdo`), the return/re-submit flow ([PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow)), the comment scoping, the readiness board and the notification counts — all of which currently reason about an office as the unit.

## ⚠️ The blocking question

**Which activities are a given division's work?** Attribution runs through `ProgramDivision`, and a program spanning several divisions is unresolved — `AipCeilingService` attributes the whole reservation to the lowest division id, with the comment:

> *splitting one activity's money across divisions is a modelling question nobody has answered, and guessing a split here would be inventing policy.*

If a program belongs to three divisions, and one division submits, what locks? Nothing here can be designed until that is answered. **Same question blocks nothing else** — Demo 2.5 turned out to be allocation-only — so this ticket owns it.

Also unresolved today: guest offices write **no** ledger rows at all (`ResolveAttributionAsync` returns before `ProgramDivision` is queried for them), so a guest office currently has no division attribution of any kind.

## Spec must cover

* The activity→division rule, including multi-division programs
* New states, and what "locked" means at division grain — can a department head edit a locked division's entries themselves?
* Return: does returning one division reopen only that division?
* What the office-level submit to PPDO requires — all divisions submitted, or not?
* Per-role behaviour incl. a division head viewing another division
* UI states: locked, returned, partially-submitted office
* Whether existing offices without divisions keep the current single-hop flow

Related: Demo 2.11 (unresolved comment counts) becomes more useful once this hop exists.
