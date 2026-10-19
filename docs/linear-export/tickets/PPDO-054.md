---
id: PPDO-54
title: 'V18-80 — AIP expenditure procurement lines from the Price Index'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-08
parent: PPDO-48
blocked_by: PPDO-52
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-54
uuid: d37e9b2c-9bd8-4ecc-96a7-89a1666a0482
---

# PPDO-54 — V18-80 — AIP expenditure procurement lines from the Price Index

**In one line:** some AIP expenditures carry procurement items drawn from the Price Index config, exactly as WFP expenditures do — in both the entry tab and the review tab.

**Plan:** `docs/v1.8/Phase_Plan.md` §12.7 · **Spec:** `docs/v1.8/AIP_Entry_Spec.md` §2 decision 15 · Tracker **W13** · **Size:** ↩️ **L** (was M — P3-a chose the larger option) · **Blocked by:** V18-42 ([PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity))

## ⚠️ This is scope that was nearly missed

It came out of reading the PPDC meeting photos — *"delete item / change cost → Price Index"* — which looked like a maintenance note. It is not. Ralph confirmed it means AIP activities carry expenditures with accounts, **and some carry procurement items sourced from the Price Index**, the same as WFP.

**Nothing in the original Phase 3 plan covers this.** V18-42 says only "enter expenditures", and V18-65 lists the price index purely as offline cached reference data. Asking about the four photo items was worth it for this one alone — the other three closed at no cost.

## Scope

Procurement lines on an AIP expenditure, sourced from the Price Index config, present in **both** the entry tab (V18-42) and the review tab (Phase 4).

WFP already does this. Read how it does it before designing anything — the WFP procurement line-item entry with price-index search shipped in v1.4.0, and the Procurement Presets config and duplicate-item warning followed in v1.4.2. **Stock Card No. on the price index** came with the PPMP report in v1.5.0.

## ✅ ANSWERED 2026-09-07 — P3-a / W13-b: the FULL WFP treatment

↩️ **This overrides the spec's own default**, which was item-selection-plus-cost on the reasoning that the AIP is a *plan* and the arithmetic exists in WFP because it is a *schedule*. Parity was chosen instead: encoders already know the WFP table, and two procurement UIs that behave differently is its own cost. **Size grows M → L accordingly.**

### Copy, from `frontend/src/components/wfp/WfpProcurementItemTable.tsx`

| What | Where it came from | Note |
| -- | -- | -- |
| Price-index picker | [RAL-231](https://linear.app/ralphoksiprojects/issue/RAL-231/wfp-entry-158-mb-price-index-fetch-blocks-the-aipoffice-selectors-on) | Searchable over the ~6,400-row catalogue, fetched **off the critical path** with its own `priceIndexLoading` state — without it the picker just looks like an empty catalogue |
| Line arithmetic | v1.4.0 | See the open question below |
| Presets | [RAL-119](https://linear.app/ralphoksiprojects/issue/RAL-119/config-procurement-presets-reusable-line-item-templates-per-account) | Account-scoped, lazily loaded. “Load” copies an editable **snapshot**, not a live link |
| Duplicate-item warning | [RAL-153](https://linear.app/ralphoksiprojects/issue/RAL-153/wfp-warn-when-a-procurement-item-is-duplicated-within-the-same) | ⚠️ Re-derive it — see below |

### ⚠️ Do NOT copy the period dimension — this is the whole difficulty of the ticket

`periodNo`, `frequency`, `annualQuarterChoice`, the reserve fields (`applyReserve` / `reserveAmount` / `reserveRate`), `computeWfpRollUpPreview` and `mergeWfpPeriodAndItemAmounts` are **schedule** concepts. An AIP activity carries **one annual figure**.

Copying the component wholesale imports a scheduling model the AIP does not have — and it would reach the printed form. **Lift the item-level machinery; leave the period machinery behind.**

### ⚠️ The duplicate rule does not survive that removal unchanged

[RAL-153](https://linear.app/ralphoksiprojects/issue/RAL-153/wfp-warn-when-a-procurement-item-is-duplicated-within-the-same) scopes the warning to the **active period**, deliberately: the same item recurring across periods is normal (a monthly office-supplies purchase). Remove periods and that scoping is meaningless. The AIP's scope is **the activity's own expenditure lines**. Re-derive the rule; do not transliterate it.

### ✅ `numberOfDays` — KEPT (settled 2026-09-07)

WFP computes `qty × unitPrice × numberOfDays`. This was left open because the days factor is a scheduling notion and everything else scheduling-shaped is being stripped.

**Ralph: PPDO employees asked for it.** So it is a **requirement in its own right**, not a copied artefact — a stronger reason than parity, and a different one.

⚠️ **This is the one field the “no schedule concepts” rule above does NOT reach.** While stripping `periodNo`, `frequency`, `annualQuarterChoice` and the reserve fields, `numberOfDays` looks exactly like more of the same. It stays.

## Watch out

⚠️ The AIP↔WFP relationship here is easy to over-build. An AIP procurement line is a *plan*; the WFP one is a *commitment*. They are not the same row and this ticket does not link them — the netting mechanism is deferred (§12.1, with V18-45).

## Done when

* An AIP expenditure can carry procurement items chosen from the Price Index
* The same lines are visible in the review tab
* Picker, presets, arithmetic and duplicate warning all work, and **no period/frequency/reserve concept appears anywhere in the AIP UI or DTOs**
* The duplicate warning fires on repeats within **one activity**, and its test says so
* `numberOfDays` is present and multiplies into the line total, and a test pins it
* Existing WFP procurement behaviour is unchanged

## Out of scope

Linking AIP procurement lines to WFP ones · the netting mechanism · offline caching of the price index (V18-65) · changes to the Price Index config itself.
