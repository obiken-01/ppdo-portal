# AIP Redesign — Working Notes

> ⚠️ **Partly superseded.** This captures Ralph's initial description (2026-08-13) verbatim in
> substance. The §4 and `Office_User_Path_Findings.md` §6.4 questions it warned about were largely
> **answered at the 2026-08-25 PPDC meeting** — see §4d below, and `Phase_Plan.md` §12 for the full
> reading. Still do not ticket from this document alone: the plan, not this file, is the ordering.

---

## 1. Ralph's description (2026-08-13)

> "For PPDO, the AIP creation will be separated by division, same with WFP. But other offices will
> have only access to their office's work regardless of their division."
>
> "It looks like WFP but users first need to create project and activity before they enter their
> expenditures."

Plus, from the reviewer discussion the same day (`Office_User_Path_Findings.md` §6):

- Each office prepares its own AIP; the office's **reviewer** is the sole submitter.
- Submitted office AIPs feed a **consolidated** PPDO-level document.

---

## 2. What this implies structurally

### 2.1 — Two different scoping rules in one feature

| User | Scope |
|---|---|
| PPDO staff | Scoped **by division** — same model as WFP (`WfpRecord.DivisionId`) |
| Office user | Scoped **by office only** — division is explicitly *not* a factor |

This is a genuinely two-axis model, and it is not what either existing feature does:

- **WFP** scopes by `(OfficeId, DivisionId)` — division always participates.
- **LDIP** scopes by `OfficeId` alone — division never participates.

The new AIP needs division to participate **only when the caller is PPDO**. That maps cleanly onto
the `OfficeScope` + `DivisionScope` pair: office users resolve to `OfficeScope.For(id)` with
division ignored; PPDO users resolve to `OfficeScope.All` (or PPDO's own office) *plus* their
`DivisionScope`.

### 2.2 — Entry order is create-then-cost

Current AIP entry (RAL-62, v1.6) walks Office → Program → Project → Activity, each Add persisting
immediately, and amounts live on the Activity (or a synthetic leaf, per RAL-108).

The new flow is described as WFP-like: **create Project and Activity first, then enter
expenditures against them.** That matches WFP's existing two-stage shape
(`WfpActivity` → `WfpExpenditureLine`) rather than AIP's current single-stage
"activity row carries its own amounts".

Open: does an AIP activity gain an expenditure-line child table like WFP's, or do the existing
amount columns stay and only the *UI* becomes two-stage? This determines whether there is a
schema change here at all.

### 2.3 — Ownership requires a new FK

Per `Office_User_Path_Findings.md` §6.1: `AipOffice` currently has **no FK to the `offices` config
table** — office identity is `RefCode` string matching. A per-office prepare-and-submit flow needs
real ownership, so this redesign is where that FK gets added.

### 2.4 — "Based from LDIP"

LDIP already has the office-owned-record shape (`LdipRecord.OfficeId`) that the prepare → submit →
consolidate flow needs. AIP's current shape (one provincial multi-office record per FY) is the
opposite. Basing the new AIP on LDIP's ownership model is therefore structural, not cosmetic —
while the *interface* borrows from WFP.

---

## 3. Relationship to other v1.8.0 work

The `OfficeScope` primitive and the dashboard leak fixes (`Office_User_Path_Findings.md` §5 steps
1-3) are being built first and are **independent** of every open question here. The redesign
consumes `OfficeScope` rather than defining it.

AIP office isolation (§3.1 of the findings doc) is deliberately **not** being retrofitted before
this redesign — it would be work done twice.

**⚠️ Consequence:** the "do not create an office-user account in production" rule extends until
this redesign ships, not just until the leak fixes land. Until AIP has ownership, an office account
would have destructive access to PPDO's AIP (`DELETE /aip/{id}`).

---

## 4. Open questions (beyond findings doc §6.4)

1. Does an AIP activity gain an expenditure-line child table (WFP-style), or do the current amount
   columns stay with only the UI going two-stage? (§2.2)
2. For PPDO, is there one AIP record per division, or one record with division-tagged rows? WFP
   chose one record per `(office, division)`.
3. How does division-separated PPDO entry consolidate — do PPDO's divisions submit to a PPDO
   consolidation the same way offices do, or is PPDO's roll-up implicit?
4. ~~Excel upload path~~ and 5. ~~existing v1.6 data~~ — **answered in §4a below.** Numbering is
   left as-is so the references to "questions 4 and 5" stay meaningful.
6. `wfp_activities.aip_activity_id` is an FK-Restrict onto AIP activities (see RAL-178). Any
   restructuring of AIP activities has to account for WFPs already built on them.

> **State of these after 2026-08-25 (§4d):** Q1 is effectively settled by the entry flow — the
> encoder creates projects and activities, then costs them, so the expenditure-line child table
> (`Phase_Plan.md` V18-33) is the shape being built. **Q2 and Q3 are still open** and are now the
> last structural unknowns in the redesign: whether PPDO holds one AIP record per division or one
> record with division-tagged rows, and whether PPDO's divisions submit through a PPDO
> department-head reviewer the way an office does. Both are folded into `Phase_Plan.md` §12.8 Q8.

### 4a. Decided 2026-08-17 — questions 4 and 5 (clean break by fiscal year)

**Decision: clean break, not migration.** FY2027 and earlier AIP data stays exactly as-is, in the
current v1.6 shape (single provincial multi-office record, no ownership FK, Excel upload). The new
office-owned/reviewer format applies starting **FY2028**. No conversion job, no dual-write.

**Reasoning:**
- No office-user account exists in production and none will until this redesign ships (per
  `Office_User_Path_Findings.md` §5 step 5) — so there is no real office data on FY2027 or earlier
  to migrate. This is the cheapest point at which to draw the line.
- The confirmed reviewer flow (office prepares → reviewer submits → PPDO consolidates) has no
  meaning for old-format data, since old `AipOffice` rows have no ownership FK at all — a reviewer
  can't "own" something that was never office-scoped to begin with. FY2027 simply predates the
  feature, the same way any feature has a first version it didn't exist before.
- Existing FY≤2027 WFP records keep their `wfp_activities.aip_activity_id` FKs pointing at
  unchanged old-schema `AipActivity` rows — zero migration risk to already-built WFPs. RAL-108
  synthetic leaves, RAL-180 carry-forward, and RAL-181 LDIP seeding all keep working for that
  historical data untouched, since nothing about it is restructured.

**Consequence — Excel upload stays, but frozen to historical years.** The current
`AipUpload`/multi-office import path is not ported to the new format; it remains reachable only for
FY≤2027 records (or is hard-gated by fiscal year once the new entry flow exists for FY2028+).
Whether that's a literal `if (fiscalYear <= 2027)` gate on the same endpoints, or the old endpoints
simply never being touched while new ones are added, is an implementation detail for whoever tickets
this — not decided here.

**Consequence — two AIP shapes live in the codebase indefinitely**, not just during a migration
window. The AIP detail page, edit endpoints, and Excel upload for ≤2027 stay as the v1.6 code
path; a parallel office-owned/two-stage path is added for ≥2028. This is the accepted cost of
avoiding migration risk to real historical data — revisit only if that dual-maintenance burden
turns out to be worse in practice than expected.

### 4c. Update 2026-08-25 — the clean break is about SHAPE, not UNITS (DECISION E)

§4a says "FY2027 and earlier AIP data stays exactly as-is." Read literally that would also freeze
the **storage unit**, leaving `aip_activities.total` holding thousands on FY≤2027 rows and pesos on
FY2028+ rows — one column, two units, told apart only by the parent record's fiscal year. §4a never
actually said that, and it is **not** what was decided.

**Decided (Ralph, 2026-08-25): units are migrated for every fiscal year.** AIP totals move from
thousands to pesos across the board (`UPDATE aip_activities SET total = total * 1000`), and the six
×1000 conversion sites are **deleted**, not made fiscal-year-conditional.

**The distinction being drawn is shape vs units.** §4a's reasoning holds for shape and only for
shape: a multi-office record with no ownership FK cannot be retrofitted into an office-owned,
reviewer-driven one, so FY≤2027 keeps the v1.6 structure. Units carry none of that risk — the
migration restructures nothing, is verifiable (sum before × 1000 = sum after), and is reversible.
RAL-108 synthetic leaves, RAL-180 carry-forward and RAL-181 seeding are all indifferent to
magnitude. §4b's caveat is unaffected: multiplying an imperfectly-imported number by 1000 leaves it
exactly as imperfect, no worse.

**What partitioning would have cost.** `AipActivity.Total` would stop being readable without the
fiscal year in hand — permanently, not for a migration window — and the six conversions become six
branches that must stay correct forever. An error in the permissive direction is silent: the WFP
ceiling check simply never trips again, with no exception anywhere. Migrating removes the failure
mode instead of managing it.

**LDIP does not move.** `LdipProgram.Budget` stays in ₱000. The rule is not *one unit everywhere*
but *units may differ only where the value never crosses a boundary* — and LDIP has no such
boundary, since `SeedProgramsFromLdipAsync` copies no amounts. The invariant is now written on the
entity itself. Full reasoning and the site list: `Phase_Plan.md` §4, V18-35 detail.


### 4d. Decided 2026-08-25 — the PPDC meeting (flow, reviewers, and a +30% uplift)

The meeting answered every decision that was blocking Phases 2–5. **`Phase_Plan.md` §12 is the full
record** — including the code checks behind the ceiling change and the ten questions that remain.
What matters at *this* document's level:

**1. The flow, end to end.** Encoder(s) prepare the office's PPAs in a WFP-shaped entry tab →
submit the whole office's work for **department-head review** (where both encoder and reviewer may
still edit) → the department head submits to **PPDO**, which **locks** it → designated **PPDO
reviewers** comment and may send a whole office's work back, which unlocks it for re-submission.
Consolidation is not a new record: it is the **existing multi-office record filled in office by
office as they submit**, visible to reviewers only, and reviewable **partially** before every office
has submitted.

**2. The LFC is out of the system.** The step-4 reviewer on the meeting page is crossed through and
redirected to PPDO. Cross-office review is a **permission flag held by certain PPDO users**, not an
external committee — so §2 of this document's reviewer model now has two flags: the office's own
department-head reviewer (**one per office**), and the PPDO consolidated reviewer.

**3. Programs stay a closed list.** An office cannot add a program outside the LDIP. This also
retires the risk §4 of `Phase_Plan.md` flagged — that offices inventing programs would create the
first LDIP↔AIP amount seam and with it a units boundary.

**4. 🆕 A +30% uplift on MOOE and CO.** Every activity's MOOE and CO, across all fund sources, carry
an additional 30%; PS is shown as entered. This appears in no earlier version of the requirements.

> **✅ Mechanics settled 2026-08-26 (tracker G1–G6): the uplift is presentation-only.** The base
> figure is stored, the rate is a **fixed** 30%, and the 30% is applied **only when a report is
> rendered** — **neither the ceiling check nor the AIP→WFP limit ever sees the uplifted figure**.
> FY2028+ only; the printed Total is `PS + 1.3 × (MOOE + CO)`.
>
> ⚠️ The consequence is deliberate and must not be "fixed": an office encoding exactly to its
> ceiling **passes every check** and then **prints a document 30% over that ceiling**. This departs
> from the principle tracker A2-4 settled, knowingly. `Phase_Plan.md` §12.2 has the full reading.

**5. ↩️ Ceilings apply to every fund source — made 2026-08-25, WITHDRAWN 2026-08-26.**
On 2026-08-25 this reversed the 2026-08-14 "General Fund only" decision. **The next day it was
reversed back** (tracker A1-b and A6-4: "in a later discussion, we will go back to old requirement
where only GF will have ceiling check"), confirmed by Ralph the same day. So:

> **Ceilings are General Fund only**, as settled 2026-08-14. The "except GAD / 20% DF / LDRRF /
> Trust Fund" exemption list stands, and **PS remains exempt** on top of it as an expense class.
>
> ⚠️ One code caveat outlives the withdrawal: `GetDivisionAllocationAsync` resolves a missing
> allocation row to `0m`, so a fund with **no** ceiling row is constrained to **zero**, not left
> unconstrained. Non-GF funds must therefore be **explicitly excluded** from the check rather than
> left blank. `Phase_Plan.md` §12.3.

**6. One pot, drawn down in sequence** — the division allocation constrains the AIP, and the WFP is
constrained by its AIP activity. AIP gets **its own ledger tables**; WFP's are not generalised, and
WFP itself is reworked in a later version. ⚠️ Until that rework, both would draw on the same
allocation — see `Phase_Plan.md` §12.1, which is the single most important caveat to carry into
Phase 2.

### 4b. Update 2026-08-19 — the FY≤2027 importer was fixed, but FY2027 is NOT being re-imported

The Excel import path that §4a freezes to historical years had a real defect, found by analysing
the province's own FY2027 file (`AIP_2027 PGOM.xlsm`) and fixed in **RAL-238 / PR #246**.

`AipXlsmParser` decided each row's level (Office / Program / Project / Activity) from the number of
segments in its AIP reference code. The province does not encode those codes consistently: 82 of
2,887 rows have a segment count that contradicts the description column the row is indented into.
The parser now takes the level from **which description column holds the text** (B/C/D/E), with the
segment count as a fallback only. Four failure modes were fixed — nameless phantom offices,
out-of-range segment counts silently dropped, a blank-ref project row orphaning its children, and
blank/`"None"`-ref rows carrying money being folded into the previous activity's *name*. Net effect
on the real file: **₱80,755,182 of line items recovered** (the parser now captures 100% of the
line-item money, ₱33,437,078,747), plus ₱627.8M that had been filed under nameless offices moved
back to AKAP-HUB / Housing / Local School Board.

**Decided (Ralph, 2026-08-19): FY2027 will NOT be re-imported.** The fix applies to future imports
only. So the FY2027 records in the database still carry the old parser's reading — including the
six nameless SOCIAL offices and the missing line items described above. This is consistent with
§4a: FY2027 predates the fix the same way it predates the redesign, and no WFP records are affected
since `wfp_activities.aip_activity_id` still points at the same unchanged `AipActivity` rows.

**Two things that follow for whoever tickets the redesign:**
- Do not treat the FY2027 data in the database as a faithful representation of the province's FY2027
  AIP. If a report or a carry-forward reads those records, the discrepancies above will show up.
- The printable AIP form (confirmed in scope, see the v1.8.0 requirements review) is derived from
  the same sheet structure this parser reads. Build it against the description-column rule, not the
  ref-code segment count.

**Separate finding, for PBO rather than for code:** the province's file prints a GRAND TOTAL of
₱33,406,410,747, which is **₱30,668,000 below its own line items**, because a number of rows are
missing their Total (column O) formula and so contribute zero to every subtotal that sums that
column. Worth raising alongside the rounding/footing answers — it is the same class of problem.
