---
status: draft
version: v1.9.0
tickets: PPDO-216 (T1 backend), PPDO-217 (T2 frontend)
supersedes: IAipCeilingService remarks item 1 ("General Fund ONLY"), office-ceilings D4 (GF tab only)
---

# v1.9.0 — AIP Ceiling Check on Other Fund Sources

> Source: Demo 3 / meeting, 2026-10-13 ("Other fund source ceiling check. TF for PhilHealth will need
> to have ceiling … configurable in the fund source config page"). Drafted and settled 2026-10-14.

## 1. Goal

Some funds besides the General Fund carry a ceiling the province enforces, starting with the
PhilHealth trust fund. PPDO finance turns the ceiling on **per fund source**, sets each office's
ceiling for that fund on the existing Office Ceilings page, and an office cannot send its AIP to PPDO
while it is over any enforced fund's ceiling, the same way the General Fund works today.

## 2. Decisions (settled)

1. **Opt-in per fund source.** A new flag on the fund source, **"Enforce AIP ceiling"**, default off.
   The General Fund is always enforced and its flag is shown on and locked. Every other fund is
   unaffected until someone turns it on. This is how the previous flip-flop is avoided: DECISION H
   ("ceilings on every fund") was adopted and withdrawn within a day (2026-08-26, `Phase_Plan.md`
   §12.3); this does not bring H back, it lets finance choose fund by fund.
2. **Same arithmetic as the General Fund**, per enforced fund, independently:
   Σ `roundUpToThousand(MOOE) + roundUpToThousand(CO)` over the office's lines **on that fund**,
   PS excluded, base figures (the +30% uplift never counts), all of the office's groups summed.
   *(PS exemption: follow-up A, settled.)*
3. **Unset ceiling = zero**, as for the General Fund. An office that encodes nothing on an enforced
   fund is unaffected. An office that encodes on it with no ceiling set is blocked, with a message that
   says the ceiling is missing (PPDO's to fix), never that the office is over.
4. **Same gate as the General Fund**: checked at **send to PPDO** (`CanSubmitToPpdo`), and shown as a
   warning on the encoder's submit, exactly as PPDO-146 does for the General Fund. Never during entry
   (DECISION C). Remaining is never clamped at zero.
5. **One message per fund that fails**, all of them at once, so an office over on two funds does not
   discover the second after fixing the first.
6. **Ceilings are set where they already live**: the Office Ceilings page and `PUT /allocation/ceiling`,
   which have been per fund since v1.4.3. The page's fund tabs (hidden by D4 since 2026-09-19) show
   the General Fund **plus every enforced fund**, nothing else.
7. **Who turns the flag on**: holders of `CanManageOfficeCeilings` (PPDO finance) and SuperAdmin.
   A department head who manages their own office's fund sources (PPDO-109) sees the flag read-only:
   it decides what their office is held to, so it is not theirs to change.
8. **Out of the check, deliberately**: PPDO reviewer edits (PPDO-212 already skips ceilings), the
   WFP ceiling (`IWfpCeilingService`, unchanged), and division-level allocation of non-GF funds.

### Follow-ups — all settled (Ralph, 2026-10-14; the proposed defaults)

- ✅ **A. PS on a trust fund.** The General Fund exempts PS. Does the PhilHealth fund's ceiling also
  exclude PS, or count it? **Settled: exclude, same as the General Fund**, so one rule explains every
  fund.
- ✅ **B. Office-owned funds** (PPDO-109). Can an office's *own* fund be enforced, or only province-wide
  funds? **Settled: either.** The flag lives on the fund row; the ceiling is per office anyway.
- ✅ **C. Turning a flag off mid-cycle.** **Settled: allowed, takes effect at the next check.** Offices
  already at PPDO are not re-checked or returned.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | TF-PhilHealth enforced; office X ceiling ₱500,000; X encodes ₱450,000 MOOE on it | Department head sends to PPDO | Allowed (as far as this check goes) |
| 2 | Same, X encodes ₱520,000 | Send to PPDO | 400: "TF-PhilHealth MOOE + CO totals ₱520,000.00, which is ₱20,000.00 over the ceiling of ₱500,000.00. Reduce the encoded amounts or ask PPDO to raise the ceiling." |
| 3 | TF-PhilHealth enforced, no ceiling for X; X encodes ₱10,000 on it | Send to PPDO | 400: "There is no TF-PhilHealth ceiling set for this office for the fiscal year …" (the General Fund wording, fund-named) |
| 4 | TF-PhilHealth enforced, X encodes nothing on it, no ceiling | Send to PPDO | Not affected |
| 5 | X over on the General Fund **and** TF-PhilHealth | Send to PPDO | One 400 naming both, General Fund first |
| 6 | TF-PhilHealth **not** enforced, X encodes ₱9,000,000 on it | Send to PPDO | Not affected (today's behaviour) |
| 7 | X over on an enforced fund | Encoder submits for department review | Allowed, with the warning naming the fund (PPDO-146 behaviour) |
| 8 | PS-only lines on an enforced fund | Any submit | Not counted (follow-up A) |
| 9 | A finance user | Turns the flag on for TF-PhilHealth | Saved; the fund gets a tab on Office Ceilings |
| 10 | A department head managing their office's own fund | Opens that fund | Flag shown read-only; a direct API attempt to change it returns 403 |
| 11 | Anyone | Turns the flag off on the General Fund | 400 "The General Fund ceiling is always enforced." |
| 12 | A PPDO reviewer edit takes X over TF-PhilHealth | Save | Saved (PPDO-212: reviewer edits are not ceiling-checked) |

## 4. API contract

### Fund source config: `GET/PUT/POST /api/config/funding-sources…` (existing)
- Response gains `enforceAipCeiling: boolean` (true for the General Fund, always).
- Create/update accept `enforceAipCeiling?: boolean`. **Omitted = unchanged**, so the existing
  department-head edits that do not send it cannot be refused for it. Sent with a changed value by a
  caller without `CanManageOfficeCeilings` → 403. `false` on the General Fund → 400.

### Readiness / ceiling status (existing)
- `AipCeilingStatusDto` keeps its General Fund fields unchanged (no client breaks) and gains
  `otherFunds: [{ fundingSourceId, code, name, ceilingSet, ceiling, encodedBaseRounded, remaining, withinCeiling }]`,
  one per enforced non-GF fund **the office has encoded on or has a ceiling for**, ordered by code.
- `CanSubmitToPpdo` is false when the General Fund **or** any entry in `otherFunds` is not within ceiling.

No new routes.

## 5. Data model changes

### `funding_sources` (snake_case) — one additive column

| Column | Type | Null | Notes |
|---|---|---|---|
| `enforce_aip_ceiling` | `bit` | no, default `0` | The migration sets it to `1` on the shared `GF` row, so the stored value tells the truth even though the code never reads the GF's flag |

⚠️ **MIGRATION** `AddFundingSourceEnforceAipCeiling`. Additive; run before the code.

## 6. UI states

### Configuration → Funding Sources
- A checkbox **"Enforce AIP ceiling"** with help text: "Offices cannot send their AIP to PPDO while
  over this fund's ceiling. Set each office's ceiling on Office Ceilings."
- General Fund: checked and disabled, with "Always enforced".
- Read-only (disabled, with the reason as a tooltip) for a caller without the ceiling grant.
- A badge in the list ("Ceiling") on enforced funds.

### Office Ceilings
- ⚠️ **Funds that do not need a ceiling are hidden** (Ralph, 2026-10-14). Only the General Fund and
  the funds flagged "Enforce AIP ceiling" ever get a tab; every other fund stays off this page, and
  off the AIP Entry ceiling strip, entirely.
- Tabs: General Fund, then each enforced fund by code.
- ↩️ **An office's own fund lists only that office** (found live-testing PPDO-217): no other office
  can encode on it, so their rows could only collect a ceiling that means nothing. A province-wide
  fund lists every office, as the General Fund does. D4's hidden tab strip, now driven by the flag
  instead of the constant. Unchanged when only the General Fund is enforced (no tab strip).
- "Not set" for a missing ceiling, never ₱0.00 (existing rule).

### AIP Entry, submit card (ceiling strip)
- The General Fund row as today, plus one row per entry in `otherFunds`: fund code, ceiling,
  encoded, remaining (negative shown in red, never clamped).
- Over on any → the submit card's existing warning lists each fund.

## 7. Non-goals

- No ceilings on funds nobody enforced: never "every fund" (that was DECISION H).
- No division-level allocation of non-GF funds in AIP, and no change to the WFP ceiling.
- No ceiling check during entry, or on PPDO reviewer edits.
- No re-check or automatic return of offices already at PPDO when a flag changes.
- No new permission flag: `CanManageOfficeCeilings` covers it.

## 8. Deployment notes

- One additive migration, before the code. Add it to `Pre_Deployment_Checklist_v1.9.0.md`.
- After deploy, finance turns the flag on for TF-PhilHealth and sets ceilings **before** offices send
  to PPDO; an office with PhilHealth lines and no ceiling would otherwise be blocked (decision 3).
  Tell finance this before the flag is turned on.
- Update `IAipCeilingService`'s remarks (item 1) and the office-ceilings page header (D4).

## 9. Ticket split

| Ticket | Scope | Depends on |
|---|---|---|
| T1 Backend | Migration + entity/config; fund config DTO + the permission and GF rules; `AipCeilingService` per enforced fund; readiness/submit messages; tests (red-tested) | — |
| T2 Frontend | Fund Sources checkbox + badge; Office Ceilings tabs from the flag; AIP Entry ceiling strip rows + warning | T1 |

## 10. Acceptance checklist

- [ ] As finance, tick "Enforce AIP ceiling" on TF-PhilHealth; it shows "Ceiling" in the list.
- [ ] Office Ceilings now shows a TF-PhilHealth tab; set office X to ₱500,000.
- [ ] X with ₱520,000 MOOE on TF-PhilHealth: the department head's send to PPDO is refused naming TF-PhilHealth and the ₱20,000 overage.
- [ ] Reduce to ₱450,000: the send goes through.
- [ ] Another office with PhilHealth lines and no ceiling: refused with the "no ceiling set" message.
- [ ] An office with no PhilHealth lines is unaffected.
- [ ] As a department head on their own office's fund: the checkbox is read-only.
- [ ] The General Fund's checkbox is checked and disabled.
- [ ] Untick TF-PhilHealth: its tab disappears and sends are no longer checked against it.

## 11. Test focus

- `AipCeilingService`: per-fund sums (only that fund's lines, PS excluded, rounded, all groups), unset =
  zero, nothing encoded = unaffected, unflagged = ignored, GF + other fund failing together.
- Fund config: flag change without the grant → 403, GF off → 400, omitted flag → unchanged.
- Red-test: the fund filter, the flag filter, the PS exclusion, and the "nothing encoded" skip.
