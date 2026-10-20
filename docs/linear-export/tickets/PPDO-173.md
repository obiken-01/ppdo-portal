---
id: PPDO-173
title: 'Demo 2.15 T1b — Align the proposal model and spec with the province template (⚠️ migration)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-10-01
completed: 2026-10-01
parent: PPDO-153
blocked_by: (none)
related: PPDO-154
url: https://linear.app/ralphoksiprojects/issue/PPDO-173
uuid: 6445b507-e45d-4b6c-af45-00135f2dd997
---

# PPDO-173 — Demo 2.15 T1b — Align the proposal model and spec with the province template (⚠️ migration)

**Follow-up to** [PPDO-154](https://linear.app/ralphoksiprojects/issue/PPDO-154/demo-215-t1-data-model-investment-proposal-tables-signatory-defaults) (PR [obiken-01/ppdo-portal#410](https://linear.app/ralphoksiprojects/review/featinvestment-proposal-data-model-and-addinvestmentproposals-69cb962bd2ef), merged). The PGOM template and nine real 2027 proposals were reviewed on 2026-10-01 (files kept outside the public repo). Ralph's decisions:

* The title stays **PGOM INVESTMENT PROPOSAL**, and the **Program Title** row stays. Both are PPDC additions to the form.
* **Project Type** prints blank for now.
* **Signatories: 4 slots** with editable labels. Empty slots are not printed.

**Schema changes**, in a new migration `AlignInvestmentProposalsWithTemplate`. The merged migration is not edited.

* `investment_proposals` gains a fourth signatory slot (`signatory4_label`, `signatory4_name`, `signatory4_position`).
* `investment_proposal_team_members.required_training`. The samples' capacity table lists every member, with one training often merged down all of them, so the training belongs on the member. `investment_proposal_capacity_trainings` is dropped. It is empty everywhere: the table exists only in local databases.
* A CHECK on `investment_proposal_benefits.sector` for the five template sectors: Social, Economic, Environmental, Institutional, Infrastructure/Land Use.

**Spec changes**

* Headings and labels follow the template word for word, except the PPDC additions listed above.
* H-1 columns are **MOOE | PS | CO**.
* H-1 has one row per activity, with the computation lines inside the money cell.
* Source of Fund prints the fund **name**.
* G rows are numbered straight through, ignoring groups.
* The E, K and L labels match the template.
* Wireframes for the picker and for Sections G, H-1 and I are added.

Not a hand-code candidate: it contains an EF migration.
