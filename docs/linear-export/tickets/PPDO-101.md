---
id: PPDO-101
title: 'AIP Entry: Delete program control on the Program panel'
status: Canceled
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-88, PPDO-91, PPDO-95
url: https://linear.app/ralphoksiprojects/issue/PPDO-101
uuid: 5676bc17-d93f-4a09-a6b0-d0a7e16cbdc1
---

# PPDO-101 — AIP Entry: Delete program control on the Program panel

**CANCELLED 2026-09-16 — program-level delete is not wanted.**

The 2026-09-15 demo note read "add delete button to the program and activity", which is why this ticket exists. Ralph confirmed it meant **project**, not program — and project + activity delete shipped in [PPDO-91](https://linear.app/ralphoksiprojects/issue/PPDO-91/aip-entry-delete-project-delete-activity-controls) (PR [obiken-01/ppdo-portal#339](https://linear.app/ralphoksiprojects/review/aip-entry-final-delete-project-delete-activity-controls-ppdo-91-e8ceda2ebf14)). So the ask was already covered.

`docs/v1.8/AIP_Entry_Layout_Spec.md` §7's **"Program delete in the UI" non-goal stands as written.** Do not re-derive this ticket from the meeting note.

## If this is ever revisited

It was built and then discarded. The branch is preserved at tag `archive/feature/v1.8.0-ppdo-101-program-delete` (PR [obiken-01/ppdo-portal#343](https://linear.app/ralphoksiprojects/review/aip-entry-delete-program-on-the-program-panel-ppdo-101-6d8e0df1e0b7), closed unmerged), and contained:

* A `Program` case on `AipDeleteNodeButton` with subtree counting (projects + activities + unresolved comments across all three levels), and no renumber sentence — program ref codes are the LDIP's and are never renumbered.
* A "can be added again from Add programs" sentence, which is true: `GetAddableProgramsAsync` derives `AlreadyAdded` from the group's **live** programs, so deleting one frees it up again. That makes program delete the one recoverable delete on the page.
* ⚠️ **A real latent gap, left unfixed:** `applyAipDeleteToTree` filters deleted projects and activities out of the in-memory tree but **not programs**. Unreachable while nothing can delete a program; the first control that can will render a program it has just deleted until a reload.

The endpoint itself (`DELETE …/aip/programs/{id}`) is live and keeps [PPDO-88](https://linear.app/ralphoksiprojects/issue/PPDO-88/aip-delete-clear-allocation-ledger-comments-audit-snapshot-renumber)'s ledger and comment clearing — only the UI is refused.

---

*Original scope below.*

**In one line:** the Program panel gets the delete control the Project and Activity panels have.
