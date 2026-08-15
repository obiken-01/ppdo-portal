# v1.8.0 — Questions for Monday

> Prepared 2026-08-14 for Ralph. Companion to `AIP_Requirements_Review.md`, which has the technical
> reasoning behind each of these; this document is the meeting version.
>
> Grouped by **who can answer**, not by feature — so each group can be asked separately without
> reading the whole thing. Questions are written in the language of the person answering; the
> *"why it matters"* line is there if someone asks why you're asking.
>
> **🔴 = blocking.** Work cannot start on that area until it's answered. Six of them.

---

## Start here — the six blockers

If Monday gets cut short, these are the ones to get through. Everything else can be answered later
without stalling development.

| # | Question | Ask |
|---|---|---|
| 1 | Does the AIP draw from the **same budget ceiling** the WFP already uses, or its own separate one? | PBO / PPDC |
| 2 | For "in thousand pesos", does **1,234,200** show as **1,234** or **1,235**? | PBO |
| 3 | Must the printed figures in a column **add up exactly** to the printed total? | PBO |
| 4 | Can a reviewer **send work back** for correction, and can they leave **comments**? | PPDC |
| 5 | Can an office add a **program that isn't in the LDIP**? | PPDC |
| 6 | What happens to the **2027 AIP** and the Excel upload once the new AIP is live? | Ralph / PPDC |

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

### 🔴 A2. Rounding — does 1,234,200 display as 1,234 or 1,235?

Confirmed already: **1,234,567.89 → 1,235**. But that example doesn't settle the rule, because two
different rules both produce it:

| Amount | If "normal rounding" | If "always round up" |
|---|---|---|
| 1,234,567.89 | **1,235** | **1,235** ← both agree |
| **1,234,200.00** | **1,234** | **1,235** ← this is the one that decides it |
| 1,234,900.00 | 1,235 | 1,235 |

*Why it matters:* affects roughly half of every figure in every AIP report.

### 🔴 A3. Must the printed rows add up to the printed total?

Once figures are shown in thousands, you can have **one** of these, not both:

- **Accurate total** — the total is the true sum, but adding up the printed rows by hand gives a
  slightly different number.
- **Column that adds up** — the printed rows sum exactly to the printed total, but that total is a
  few thousand off the true figure.

*Why it matters:* if the PBO checks the AIP line by line against their own figures, "the column adds
up" usually matters more. This is a finance policy call, not a technical one.

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

### 🔴 D1. The 2027 AIP and the Excel upload

Two related decisions:

- **Amounts.** AIP figures are currently stored **in thousands** (that's how the Excel file is
  written). The new AIP stores **full pesos**. So does the 2027 data get converted to pesos, or do
  we keep 2027 as-is and only apply the new format from 2028? (Converting is cleaner — leaving two
  different units in the same column is a bug waiting to happen.)
- **Excel upload.** Does the `.xlsm` upload path stay for 2028 and beyond, or is it retired now that
  offices encode directly?

⚠️ Related and important: there are three places in the WFP code that multiply AIP amounts by 1,000
to compare them against WFP pesos. When AIP switches to peso storage, those **must** be removed in
the same change — otherwise every WFP budget check silently becomes 1,000× too generous, with no
error anywhere.

### D2. The printable AIP — same layout as the file we import?

You said "same to what I uploaded before". Confirming: do you mean the **same official AIP form
layout that the `.xlsm` upload reads** (the GENERAL / SOCIAL / ECONOMIC / OTHERS sheets)? If so
that's good news — the importer already knows that structure, so the printable version can be
built from it rather than reverse-engineered.

### D3. Offline — whose laptop?

Will offline data entry happen on **office-issued machines**, or on personal / shared laptops?

*Why it matters:* it decides how much of the login session we're willing to keep on the device. On
an office machine, staying signed in is defensible. On a shared laptop, staying signed in means
anyone who opens the browser is that user, with that office's budget data.

### D4. Two encoders in the same office

Is it one encoder per office, or could two people encode the same office's AIP at the same time?
(If two, we need a rule for what happens when they edit the same activity — currently the last save
silently wins.)

---

## E. Already settled — no need to re-ask

Recorded here so nobody re-opens them by accident.

| Decided | Answer |
|---|---|
| Multiple fund sources | One fund source **per expense line**; an activity with several funds simply has several lines |
| Rounding of 1,234,567.89 | **1,235,000** (the draft's 1,234,000 was a typo) — but see A2 for the remaining tie-break |
| Which funds have a ceiling | **General Fund only** |
| "Limit Dept Head, except GAD / 20% DF / PS / LDRRF / Trust Fund" | This is the **ceiling exemption list**, not a permissions rule |
| Printable AIP form | **In scope** |
| Password reset | **Self-service** — user answers their own recovery question, gets a temporary password, no admin involved |

---

*If only part of Monday is available, the six 🔴 questions are the ones that unblock work.
Questions 1, 4 and 6 change how the data is stored — those are expensive to change later.*
