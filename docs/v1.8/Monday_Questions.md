# v1.8.0 — Open Questions for Clarification

> Prepared 2026-08-14 for Ralph, ahead of the Monday meeting. **Refreshed 2026-08-25** and moved
> onto `release/1.8.0` — until now this list and `AIP_Requirements_Review.md` existed only on the
> `claude/progressive-web-apps-9kym78` branch, so anyone opening the release branch to find them
> could not.
>
> Companion to `AIP_Requirements_Review.md`, which has the technical reasoning behind each item;
> this document is the meeting version. Grouped by **who can answer**, not by feature — so each
> group can be asked separately without reading the whole thing. Questions are written in the
> language of the person answering; the *"why it matters"* line is there if someone asks why you're
> asking.
>
> **🔴 = blocking.** Work cannot start on that area until it's answered. Five of them.
>
> ⚠️ **If the 2026-08-17 meeting answered any of these, the answers were never written down** — not
> in this doc, not in `Phase_Plan.md` §9, not in Linear. Anything answered there needs recording
> before the affected phase can be ticketed.

---

## What changed since this list was written

So nobody re-asks something already decided, or re-raises something already fixed.

**Answered:**

- **The 2027 AIP and the Excel upload** (was blocker #5) → **clean break by fiscal year.** FY2027
  and earlier keep the current format and the `.xlsm` upload permanently; the new office-owned
  format starts **FY2028**. No conversion job, no dual-write. FY2027 is *not* being re-imported.
- **Redesign vs. patch** → redesign the AIP rather than retrofit office isolation onto the current
  model.
- **"User Profile" as a landing page** → resolves to `/account`; the `/profile` stub goes.

**Shipped on `release/1.8.0` since:** the landing-page feature (schema, server-side resolution,
one helper for all five redirect sites, PWA `start_url`) and the shared-password fix — every new or
reset account now gets its own random one-time password instead of the documented default.

**Newly raised, and now one of the five blockers:** whether PPDO becomes a real office row
(**F**, §D1 below).

---

## Start here — the five blockers

If time gets cut short, these are the ones to get through. Everything else can be answered later
without stalling development.

| # | Question | Ask |
|---|---|---|
| 1 | Does the AIP draw from the **same budget ceiling** the WFP already uses, or its own separate one? | PBO / PPDC |
| 2 | Must the printed figures **add up exactly** to the printed total — both down a column and across the PS/MOOE/CO row? (A2 ① and A3) | PBO |
| 3 | Can a reviewer **send work back** for correction, and can they leave **comments**? | PPDC |
| 4 | Can an office add a **program that isn't in the LDIP**? | PPDC |
| 5 | Does **PPDO become a real office** in the system, or stay a special case? (D1) | Ralph — internal |

Three more are not on the critical path today but block later phases: **A4/A5** (ceiling
enforcement and division allocations), **D2** (the storage unit), and **D4** (offline device
ownership).

---

## A. For the Budget Office (PBO)

### 🔴 A1. Does the AIP draw from the same ceiling as the WFP, or a separate one?

Right now the system checks a WFP against two things: the division's budget allocation, **and** the
amount already planned in the matching AIP activity. If the AIP now also gets checked against that
same division allocation, the same peso could be counted twice — once when it's planned in the AIP,
again when it's detailed in the WFP.

**Two clean answers:**
- **Same pot** — the allocation limits the AIP, and the WFP is then limited by its AIP activity
  only. (Conceptually tidiest: the AIP is the plan, the WFP details it.)
- **Separate pots** — the AIP gets its own ceiling figure, independent of the WFP's.

*Why it matters:* decides how the budget tracking is built. Getting it wrong silently
double-counts money, with no error message.

### A2. Rounding — the rule is settled, five details aren't

**Settled:** always round up to the nearest thousand on any remainder above zero
(`1,234,200` → `1,235`). Zero stays zero.

Five follow-on details. The first is the one worth raising in the room — it shows up on every
printed line.

**① The PS / MOOE / CO / Total row won't balance across.** Round each column up on its own and the
row stops adding up:

| | PS | MOOE | CO | Total |
|---|---|---|---|---|
| Exact | 100,100 | 200,100 | 300,100 | 600,300 |
| Each rounded up | 101 | 201 | 301 | **601** — but 101 + 201 + 301 = **603** |

Up to 2 thousand out, on every single line. *Recommendation: make the row total the sum of the
rounded components (603). Together with A3 that makes the whole document balance both across and
down, wherever anyone checks.*

**② Anything under ₱1,000 prints as 1.** ₱500 shows as "1", ₱0.01 shows as "1". Consistent with
never understating — just confirming nobody is surprised.

**③ Are there ever negative amounts?** Not in a first AIP, presumably — but in an amendment or
supplemental budget, where a realignment reduces a line? If so, "round up" needs to mean *round the
size up* (−1,234,200 → −1,235), not literally upward, which would shrink the deduction to −1,234.

**④ Should the ceiling be checked against the rounded figures or the exact ones?** They can
disagree: an office at ₱49,999,600 exact is inside a ₱50M ceiling, but its printed AIP — built from
rounded-up rows — could read ₱50,020,000, i.e. over. *Recommendation: enforce on the same rounded
figures the document prints, so the system and the paper can never disagree.*

**⑤ In the Excel export, do the cells hold the rounded thousands or the exact pesos?**
*Recommendation: the rounded thousands as real numbers*, so when PBO re-sums a column in Excel they
get exactly the printed total.

### 🔴 A3. Must the printed rows add up to the printed total?

Once figures show in thousands, you can have **one** of these, not both:

- **Accurate total** — the total is the true sum, but adding up the printed rows by hand gives a
  different number.
- **Column that adds up** — the printed rows sum exactly to the printed total, but that total sits
  slightly above the true figure.

**Because everything now rounds up, this gap is one-directional and grows with the number of
rows.** Every row is overstated by up to 1,000 pesos (about 500 on average) and nothing cancels out:

| Rows in the column | Printed column would exceed the printed total by roughly |
|---|---|
| 10 | ~5 thousand |
| 50 | ~25 thousand |
| 100 | ~50 thousand |
| 500 | ~250 thousand |

On a consolidated AIP across ~20 offices, that's a visible discrepancy a reviewer would query.

**Our recommendation:** round each row first, then add the rounded rows for every subtotal and
total. The printed document is then internally consistent at every level — and it still never
understates, which was the point of rounding up. Exact centavos stay in the database.

*Why it matters:* it's a finance policy call, and it changes every total on every AIP report.

### A4. Should going over the ceiling **block** saving, or just **warn**?

And if it blocks — at the moment the user types it, or only when they submit the whole AIP for
review? (Blocking only at submit is friendlier for a document built over several weeks, and it's
close to necessary if people are working offline.)

### A5. Must the division allocations add up to the office's ceiling, or can they be less?

And what should happen if the PBO **lowers an office's ceiling after** that office has already
allocated to divisions and encoded activities against it?

### A6. Confirmations — please just verify these are right

| We understood | Correct? |
|---|---|
| Only the **General Fund** has a ceiling. GAD, 20% DF, LDRRF and Trust Fund have none | |
| **Personal Services is excluded** from the ceiling even when it's General Fund — so if one expense is part PS and part MOOE, only the MOOE part counts against the ceiling | |
| Amounts are **entered in full pesos** (e.g. 1,234,567.89) and only *displayed* in thousands | |

---

## B. For the PPDC / PPDO leadership — the review workflow

### 🔴 B1. Can a reviewer send work back, and can they leave comments?

The requirements say work is "sent for review" and "once approved" — but not what happens when a
reviewer **isn't** satisfied. The whiteboard says "Review Comments", so presumably yes. Need to know:

- Can a reviewer **return** work to the encoder for correction, or only approve?
- Can they leave **comments**? If yes — on the whole submission, or on a specific
  program / project / activity / expense line?

*Why it matters:* per-item comments are much more useful to the encoder but a fair bit more work to
build. Whole-submission comments are cheap. Worth deciding deliberately rather than by default.

### 🔴 B2. Can an office add a program that isn't in the LDIP?

Programs load from the office's approved LDIP. If an office needs a program that isn't there:
can they add it themselves, does someone approve it, or is the LDIP a closed list?

### B3. Once submitted, is the work locked?

- Locked to the **encoder** only, or to everyone including the reviewer?
- Can the reviewer **edit** the figures, or only approve / return with comments?
- If work is returned and re-submitted, does the reviewer see **what changed**?

### B4. Is PPDO's own internal review a real approval step?

Our understanding of the chain:

```
PPDO division encoder → PPDO reviewer (PPDC?) ─┐
                                                ├→ Consolidated → LFC review
Office encoder → Office reviewer ──────────────┘
```

Is the PPDO reviewer step a genuine gate (PPDO's divisions can't reach the consolidated document
without it), or just a view?

**Related, and needed at the same time** — three questions about what "consolidated" means in
practice, from `Office_User_Path_Findings.md` §6.4:

- Is the consolidated work a **new** provincial record assembled from submissions, or the existing
  multi-office record with offices filled in as they submit?
- Can PPDO consolidate **partially**, before every office has submitted?
- **One reviewer per office, or several?** The design supports many; if exactly one is required,
  that constraint has to be built in deliberately.

### B5. When the LFC reviews, can they return **one** office's work?

Or only approve/return the consolidated document as a whole? And if LFC returns one office —
does that office's reviewer have to submit it again?

### B6. Who are the LFC users?

We need names or at least a count. Note this is the **first permission in the system that spans all
offices** — everyone else only ever sees their own. Worth confirming that's intended: LFC users see
every office's budget figures.

### B7. Is there a submission deadline?

AIP preparation runs to a calendar. Should the system have a **cut-off date per fiscal year** after
which offices can no longer submit — and a page showing which offices have submitted and which
haven't? (Recommended: it's the only way to chase 20 offices without doing it by hand.)

### B8. From the whiteboard — what was the "₱5M" next to Program/Project?

Couldn't tell from the photo whether it's a threshold, a ceiling, or just an example figure.

---

## C. For the offices that will use the data (PBO, PACCO, PTO, GSO)

### C1. What do you need out of the AIP, and in what form?

Best possible answer here is not a description — it's **a filled-in copy of whatever they use
today**. (We learned this the hard way on the WFP export: the "template" we were given turned out
to be a filled sample, and the difference cost real time.)

Ask each of the four:
- What columns do you need?
- Excel file, CSV, or a direct connection into your own system?
- How often — once when approved, or refreshed continuously?

### C2. GSO specifically — is the API we already designed the better fit?

There's an existing read-only AIP data contract designed for GSO
(`docs/External_AIP_API_Contract.md`, currently in the backlog). A live connection would always show
current data, where a file goes stale as soon as it's generated. Worth asking GSO which they prefer
before we build a file export for them.

---

## D. For Ralph — internal, no meeting needed

### 🔴 D1. Does PPDO become a real office in the system?

Today PPDO is identified **two different ways**: users belong to PPDO by having *no* office at all
(`OfficeId == null`), and the Budget Planning dashboard separately looks for a hardcoded office code
`"PPDO"`. Two mechanisms for one idea.

The alternative is to make PPDO an ordinary office row with a flag marking it as the provincial one.
Cleaner, and it removes an oddity where "no office" currently means "sees everything" — the opposite
of what "no division" means elsewhere in the system.

*Why it matters, and why it's on this list:* this is **cheap to change now and expensive later**.
The AIP redesign builds ownership — which office owns which plan — directly on top of whichever
answer this gets. Change it afterwards and it becomes a data migration touching every permission
check in the system. No other open question has that shape.

### D2. Confirm the storage unit for the new AIP

The clean-break decision (FY≤2027 old format, FY2028+ new) implies the answer but never says it
outright: FY2027 amounts stay stored **in thousands** and FY2028+ are stored **in full pesos**,
side by side in the same column, told apart by fiscal year.

⚠️ **Why this needs saying explicitly:** there are three places in the WFP code that multiply AIP
amounts by 1,000 to compare them against WFP pesos. If the two years store different units, those
three places have to become *conditional on the fiscal year* — not deleted, which is what they'd
need if everything moved to pesos. Get it wrong in either direction and WFP budget checks silently
stop validating: 1,000× too generous for one set of years, with no error anywhere.

### D3. The printable AIP — same layout as the file we import?

You said "same to what I uploaded before". Confirming: do you mean the **same official AIP form
layout that the `.xlsm` upload reads** (the GENERAL / SOCIAL / ECONOMIC / OTHERS sheets)? If so
that's good news — the importer already knows that structure, so the printable version can be
built from it rather than reverse-engineered.

### D4. Offline — whose laptop?

Will offline data entry happen on **office-issued machines**, or on personal / shared laptops?

*Why it matters:* it decides how much of the login session we're willing to keep on the device. On
an office machine, staying signed in is defensible. On a shared laptop, staying signed in means
anyone who opens the browser is that user, with that office's budget data.

### D5. Two encoders in the same office

Is it one encoder per office, or could two people encode the same office's AIP at the same time?
(If two, we need a rule for what happens when they edit the same activity — currently the last save
silently wins.)

### D6. Does Phase 1 ship a real Budget Planning dashboard for office users?

Office users currently get the readiness hub only — deliberately, because an office dashboard "belongs
to the redesign". This is where that promise comes due. **RAL-255** is the only Phase 1 ticket still
sitting in Backlog, waiting on this.

### D7. Split v1.8.0 into three versions?

As scoped, v1.8.0 is the largest version in this project's history — 73 work items and six open
decisions inside one milestone. The recommendation in `Phase_Plan.md` §10 is to split it:
**v1.8.0** = Phase 1 (identity, configuration, landing, password reset — already underway),
**v1.9.0** = the AIP redesign, **v1.10.0** = offline entry.

Stated fairly: the split is about release cadence, not about unblocking office accounts. Office
users still can't be created safely in production until AIP has ownership, which is Phase 2 either
way.

### D8. The SuperAdmin seed password

`superadmin@ppdo.gov.ph` / `PPDOAdmin2026!` is still written in `CLAUDE.md` and live in production.
Same class of problem as the shared default password that was just fixed, but it was outside that
ticket's scope and has no ticket of its own yet. Worth one.

---

## E. Already settled — no need to re-ask

Recorded here so nobody re-opens them by accident.

| Decided | Answer |
|---|---|
| Multiple fund sources | One fund source **per expense line**; an activity with several funds simply has several lines |
| Rounding to thousands | **Always round up** on any remainder above zero — 1,234,200 → 1,235, 1,234,567.89 → 1,235. Zero stays zero |
| Which funds have a ceiling | **General Fund only** |
| "Limit Dept Head, except GAD / 20% DF / PS / LDRRF / Trust Fund" | This is the **ceiling exemption list**, not a permissions rule |
| Printable AIP form | **In scope** |
| Password reset | **Self-service** — user answers their own recovery question, gets a temporary password, no admin involved |
| Redesign or patch the AIP? | **Redesign** — no office data exists on the current shape yet, so this is the cheapest point to change it |
| The 2027 AIP and the Excel upload | **Clean break.** FY≤2027 keeps today's format and the `.xlsm` upload permanently; the new format starts FY2028. No conversion, no re-import of FY2027 |
| "User Profile" as a landing page | Resolves to **`/account`** — the `/profile` stub is removed and redirects there |

---

*The five 🔴 questions are the ones that unblock work. Questions 1, 2 and 5 change how the data is
stored or scoped — those are the expensive ones to change later.*
