# RAL-68 — WFP Page Implementation Guide

**Branch:** `feature/v1.1-ral-68-wfp-page` (off `release/1.1.0`)
**Design source:** `docs/v1.1/Budget_Planning_Pages_Design_Spec.md` §6–7 (boards 18–19)
**Layout source:** `docs/v1.1/WFP_Report_Spec.md`
**Backend:** Fully implemented (no changes needed). See `WfpService`, `WfpFunctions`, `WfpDtos`.

---

## What to Build

A single page at `/budget-planning/wfp` (query-param navigation, no dynamic segments) that lets
an office plan expenditures against an AIP:

1. AIP + Office selectors in the header
2. Full AIP hierarchy grid (Sector → Office → Program → Project → Activity)
3. Activity rows show aggregated PS / MOOE / CO / Total / Q1–Q4 from saved expenditure lines
4. **Edit** button per activity → opens the Expenditure Popup modal
5. Always-visible **Save** button → POST to server
6. **Finalize** button → locks the WFP (Final status, read-only)
7. `localStorage` draft buffer — auto-save on every popup close; restore prompt on page load

---

## Files to Create / Modify

### 1. `frontend/src/types/budget-planning.ts` — append WFP detail types

```typescript
// ── WFP Detail ───────────────────────────────────────────────────────────────

export interface WfpExpenditureLine {
  id: number;
  wfpActivityId: number;
  expenditureType: "PS" | "MOOE" | "CO";
  resourcesNeeded: string | null;
  responsibleUnit: string | null;
  successIndicator: string | null;
  meansOfVerification: string | null;
  accountId: number | null;
  accountNumberSnapshot: string | null;
  accountTitleSnapshot: string | null;
  totalAppropriation: number | null;
  applyReserve: boolean;
  reserveAmount: number | null;
  netAppropriation: number | null;
  q1: number | null;
  q2: number | null;
  q3: number | null;
  q4: number | null;
  quarterlyTotal: number | null;
  fundingSourceId: number | null;
  fundingSourceSnapshot: string | null;
  sortOrder: number;
}

export interface WfpActivity {
  id: number;
  wfpId: number;
  aipActivityId: number;
  lines: WfpExpenditureLine[];
}

export interface WfpRecord {
  id: number;
  aipRecordId: number;
  officeId: number;
  fiscalYear: number;
  status: "Draft" | "Final";
  createdById: string;
  createdAt: string;
  updatedAt: string;
  finalizedAt: string | null;
  sourceId: number | null;
}

export interface WfpRecordDetail extends WfpRecord {
  activities: WfpActivity[];
}

// ── WFP Save ──────────────────────────────────────────────────────────────────

export interface SaveWfpLine {
  expenditureType: "PS" | "MOOE" | "CO";
  resourcesNeeded: string | null;
  responsibleUnit: string | null;
  successIndicator: string | null;
  meansOfVerification: string | null;
  accountId: number | null;
  totalAppropriation: number | null;
  applyReserve: boolean;
  q1: number | null;
  q2: number | null;
  q3: number | null;
  q4: number | null;
  fundingSourceId: number | null;
  sortOrder: number;
}

export interface SaveWfpActivityRequest {
  aipActivityId: number;
  lines: SaveWfpLine[];
}

export interface SaveWfpRequest {
  aipRecordId: number;
  officeId: number;
  fiscalYear: number;
  activities: SaveWfpActivityRequest[];
}
```

---

### 2. `frontend/src/lib/wfp.ts` — new file (mirror of `lib/aip.ts`)

```typescript
import api from "./api";
import type { WfpRecord, WfpRecordDetail, SaveWfpRequest, ApiResponse } from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

export function wfpErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// GET /api/budget-planning/wfp?aipRecordId=&officeId=
export async function listWfp(params: { aipRecordId?: number; officeId?: number } = {}): Promise<WfpRecord[]>

// GET /api/budget-planning/wfp/{id}
export async function getWfpById(id: number): Promise<WfpRecordDetail>

// POST /api/budget-planning/wfp
export async function saveWfp(body: SaveWfpRequest): Promise<WfpRecord>

// POST /api/budget-planning/wfp/{id}/finalize
export async function finalizeWfp(id: number): Promise<WfpRecord>

// POST /api/budget-planning/wfp/{id}/unlock
export async function unlockWfp(id: number): Promise<WfpRecord>
```

---

### 3. `frontend/src/components/layout/Sidebar.tsx`

Inside the Budget Planning collapsible group, after the AIP link, add:

```tsx
<Link href="/budget-planning/wfp" className={childLinkCls(isActive("/budget-planning/wfp"))}>
  <span className="text-xs">•</span>
  <span className="truncate">WFP</span>
</Link>
```

---

### 4. `frontend/src/app/(portal)/budget-planning/wfp/page.tsx` — main page

**URL pattern:** `/budget-planning/wfp?aipId=5&officeId=3` (query params, safe for `output: 'export'`)

#### Page layout
```
┌─ Header ──────────────────────────────────────────────────────────┐
│ Work and Financial Plan            [Draft badge]   [Finalize btn] │
│ AIP: [AIP FY2027 ▾]   Office: [PPDO ▾]                          │
└───────────────────────────────────────────────────────────────────┘
┌─ Grid (scrollable) ───────────────────────────────────────────────┐
│ ── AIP REF CODE ── DESCRIPTION ── PS ── MOOE ── CO ── TOT ──    │
│                                   Q1 ── Q2 ── Q3 ── Q4 ── Edit  │
│ SECTOR HEADER (gray, bold)                                        │
│   [Office row — light gray bg]                                    │
│     [Program row — light blue bg]                                 │
│       [Project row — light green bg]                              │
│         [Activity row — white]  480  1,200  0  1,680  [Edit]     │
└───────────────────────────────────────────────────────────────────┘
┌─ Footer (sticky) ─────────────────────────────────────────────────┐
│ ● Unsaved changes                                     [Save]      │
└───────────────────────────────────────────────────────────────────┘
```

#### State
```typescript
const [aipList, setAipList]           // AipRecordResponse[] — for AIP selector
const [officeList, setOfficeList]     // OfficeResponse[]    — for office selector
const [selectedAipId, setSelectedAipId]   // number | null
const [selectedOfficeId, setSelectedOfficeId] // number | null
const [aipDetail, setAipDetail]       // AipRecordDetail | null — full hierarchy
const [wfp, setWfp]                   // WfpRecord | null
const [wfpDetail, setWfpDetail]       // WfpRecordDetail | null

// Draft edit state — Map<aipActivityId, SaveWfpLine[]>
const [draftLines, setDraftLines]     // Record<number, SaveWfpLine[]>

const [popupActivityId, setPopupActivityId] // number | null — which activity Edit was clicked
const [saving, setSaving]
const [hasUnsaved, setHasUnsaved]
const [accounts, setAccounts]         // AccountResponse[] — loaded once for popup dropdowns
const [fundingSources, setFundingSources] // FundingSourceResponse[]
```

#### Data loading
1. On mount: `listAip()` + `listOffices({active:'true'})` in parallel (populate selectors)
2. Pre-fill selectors from `?aipId=` / `?officeId=` query params (via `useSearchParams`)
3. When both `selectedAipId` + `selectedOfficeId` set:
   a. `getAipById(selectedAipId)` → `aipDetail`
   b. `listWfp({ aipRecordId: selectedAipId, officeId: selectedOfficeId })` → if found, `getWfpById(result[0].id)` → `wfpDetail`
   c. Init `draftLines` from `wfpDetail.activities` (map `aipActivityId → lines`)
   d. Check `localStorage` key `wfp_draft_${selectedAipId}_${selectedOfficeId}`:
      - If localStorage entry exists AND is newer than `wfpDetail?.updatedAt`, show "Restore unsaved draft?" banner
4. Also load `listAccounts({active:'true'})` + `listFundingSources({active:'true'})` once on mount for popup dropdowns

#### localStorage draft
- Key: `wfp_draft_${aipId}_${officeId}`
- Value: `{ updatedAt: ISO-string, lines: Record<number, SaveWfpLine[]> }`
- Write via `useEffect` any time `draftLines` changes (debounce 300ms)
- On successful server Save: delete the localStorage key

#### Grid rendering
- SECTOR_ORDER = `["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"]`
- Iterate `aipDetail.offices` grouped by `office.sector` (matching SECTOR_ORDER)
- Render a sector header row before each group
- Row colors:
  - Office row: `bg-slate-100` (light gray)
  - Program row: `bg-blue-50` (light blue)
  - Project row: `bg-green-50` (light green)
  - Activity row: `bg-white` (white — fund-source color deferred)
- Activity row columns: `refCode | name | PS | MOOE | CO | TOTAL | Q1 | Q2 | Q3 | Q4 | Edit`
- Per-activity aggregates computed from `draftLines[activity.id]`:
  - `PS = sum(lines.filter(l=>l.expenditureType==="PS").map(l=>l.netAppropriation ?? 0))`
  - Same for MOOE, CO
  - `TOTAL = PS + MOOE + CO`
  - `Q1–Q4 = sum(all lines' q1–q4)`
- Show `—` when no lines for that activity

#### Save flow
1. Build `SaveWfpRequest`: include only activities where `draftLines[id]?.length > 0`
2. Client-side: validate each line `q1+q2+q3+q4 ≤ netAppropriation`
3. `saveWfp(request)` → on success: update `wfp`, delete localStorage draft, clear `hasUnsaved`

#### Finalize
- Button enabled only when `wfp` exists (must have saved at least once)
- `finalizeWfp(wfp.id)` → update `wfp.status = "Final"`
- When Final: hide Edit buttons, disable Save, show "Final — locked" badge
- PPDO admin can unlock via `unlockWfp(wfp.id)` — show Unlock button if `me.canManageConfig`

---

### 5. Expenditure Popup — `ExpenditurePopup` component

Keep it in the same file (`wfp/page.tsx`) or extract to `components/wfp/ExpenditurePopup.tsx`.
Use `components/ui/Modal.tsx` as the shell (size `"xl"`).

#### Layout
```
┌─ Expenditure Lines — {activity.name} ───────────────────────────┐
│ [PS tab] [MOOE tab] [CO tab]                                     │
│                                                                  │
│ ACCT CODE │ OBJECT OF EXPENDITURE │ TOTAL │ RESERVE │ NET       │
│           │ Q1 │ Q2 │ Q3 │ Q4 │ LINE TOTAL │ SOURCE │ [Del]    │
│ 5-02-03   │ Drugs & Medicines     │ 800k  │ ☑       │ 720k     │
│           │ 180k│ 180k│ 180k│ 180k│ 720k     │ GAD    │ [✕]    │
│ [+ Add Line]                                                     │
│                                          [Save Changes] [Cancel] │
└──────────────────────────────────────────────────────────────────┘
```

#### Per-tab state (local to popup, initialized from existing lines)
```typescript
interface PopupLine {
  key: string;                  // uuid for React key
  expenditureType: "PS"|"MOOE"|"CO";
  resourcesNeeded: string;
  responsibleUnit: string;
  successIndicator: string;
  meansOfVerification: string;
  accountId: number | null;     // for lookup; snapshot auto-set on select
  totalAppropriation: string;   // raw input string
  applyReserve: boolean;
  q1: string; q2: string; q3: string; q4: string;
  fundingSourceId: number | null;
  sortOrder: number;
}
```

#### Computed values (derived, not stored in line state)
- `net = applyReserve ? parseFloat(total) * 0.9 : parseFloat(total)`
- `reserve = applyReserve ? parseFloat(total) * 0.1 : 0`
- `lineTotal = (q1+q2+q3+q4)` shown in the LINE TOTAL column

#### Account selector
- Searchable `<select>` or filtered `<datalist>` over `accounts` list
- Filter to accounts matching the current tab's expenditure type:
  - PS tab: `account.accountType === "PS"` (prefix 5-01-)
  - MOOE tab: `account.accountType === "MOOE"` (prefix 5-02-)
  - CO tab: `account.accountType === "CO"` (prefix 5-03-)
- On select: fill account code display; `accountId` stored in line state

#### Validation on "Save Changes"
- Each line: `parseFloat(q1)+…+parseFloat(q4) ≤ net` — show inline error row if violated
- Blank lines (all fields empty) are silently dropped
- Convert string inputs to `number | null` for `SaveWfpLine`

#### On popup save
- Caller receives `(lines: SaveWfpLine[])` callback
- Caller updates `draftLines[activityId] = lines`; sets `hasUnsaved = true`
- Popup is unmounted

---

## API Endpoints (all exist — no backend changes)

| Endpoint | Used for |
|----------|---------|
| `GET /api/budget-planning/aip` | AIP selector |
| `GET /api/budget-planning/aip/{id}` | AIP hierarchy |
| `GET /api/budget-planning/wfp?aipRecordId=&officeId=` | Find existing WFP |
| `GET /api/budget-planning/wfp/{id}` | WFP detail (activities + lines) |
| `POST /api/budget-planning/wfp` | Save/upsert |
| `POST /api/budget-planning/wfp/{id}/finalize` | Finalize |
| `POST /api/budget-planning/wfp/{id}/unlock` | Unlock (admin) |
| `GET /api/config/offices?active=true` | Office selector |
| `GET /api/config/accounts?active=true` | Account dropdown in popup |
| `GET /api/config/funding-sources?active=true` | Fund-source dropdown |

All use `{ data, error, message }` envelope — unwrap same pattern as `lib/aip.ts`.

---

## Existing Utilities to Reuse

| File | Used for |
|------|---------|
| `components/ui/Modal.tsx` | Popup shell (size `"xl"`) |
| `lib/config.ts` — `listAccounts()`, `listFundingSources()`, `listOffices()` | Dropdowns |
| `lib/aip.ts` — `listAip()`, `getAipById()` | AIP selectors + hierarchy |
| `types/config.ts` — `AccountResponse`, `FundingSourceResponse`, `OfficeResponse` | Dropdown types |

---

## Answered Design Questions

- **Activity row color:** white for now — fund-source color mapping deferred (Q1 still open)
- **Cols C–F (Resources Needed, Responsible Unit, etc.):** vary per expenditure line (not shared across lines)
- **10% Reserve:** always show the column; leave blank when `applyReserve = false`
- **Legal-size export:** for RAL-79 (deferred); not in scope for this ticket
- **LBP Form No. 4:** out of scope

---

## Verification

1. `tsc --noEmit` — no type errors
2. `npm run build` — clean build (no static-export errors)
3. Manual test steps:
   a. Navigate to `/budget-planning/wfp` → AIP + Office selectors visible
   b. Select AIP FY2027 + PPDO office → hierarchy loads with sector groups
   c. Click Edit on any activity → popup opens with PS/MOOE/CO tabs
   d. Add PS line: pick account, enter total 100000, check Reserve → NET shows 90000, Q1–Q4 inputs
   e. Fill Q1–Q4 (e.g. 22500 each) → LINE TOTAL = 90000 ≤ 90000 → no error
   f. Save popup → activity row shows PS = 90000, TOTAL = 90000
   g. Refresh page → "Restore unsaved draft?" banner appears
   h. Restore → amounts still there; click Save → POST to server → toast "WFP saved"
   i. Click Finalize → status badge changes to Final, Edit buttons hidden, Save disabled
   j. Office user login → redirected to `/budget-planning/wfp`; only their office visible in selector
