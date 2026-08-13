# AIP Redesign — Working Notes

> ⚠️ **Incomplete.** This captures Ralph's initial description (2026-08-13) verbatim in substance.
> More detail to follow. Do not ticket the redesign from this document alone — the open questions
> in §4 and in `Office_User_Path_Findings.md` §6.4 are unanswered.

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
4. What happens to the existing Excel upload path? It produces a multi-office record, which is the
   shape being replaced.
5. What happens to existing v1.6 AIP data and the features built on it (RAL-108 synthetic leaves,
   RAL-180 carry-forward, RAL-181 LDIP seeding)? Migration path or clean break?
6. `wfp_activities.aip_activity_id` is an FK-Restrict onto AIP activities (see RAL-178). Any
   restructuring of AIP activities has to account for WFPs already built on them.
