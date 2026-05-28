# PPDO Portal — Project Memory Context
**Last Updated:** 2026-05-25  
**Owner:** ralpharmand.alcaide@gmail.com  
**Project:** PPDO (Provincial Planning and Development Office), Occidental Mindoro, Philippines  
**Claude Project:** PPDO Inventory Monitoring System  

---

## 1. What This Project Is

A **web portal** for PPDO staff. Started as a Google Sheets inventory monitoring system (v0.4), now evolving into a full web application. The Google Sheets version remains active during transition.

**Two parallel tracks:**
1. **Google Sheets** — `PPDO_Inventory_v0.4` — still in active use (Apps Script automation)
2. **Web portal** — prototype stage, frontend being designed in Penpot + Claude

---

## 2. Google Drive Resources

| Resource | File ID |
|---|---|
| PPDO Folder (root) | `1c6-GozlTUmSDnJvEoE9mfBmBA1NW5zhu` |
| InventoryMonitoring Folder | `1WwqX3TLMuKTsQwIQ-UL6jQsr2PKvWRwR` |
| **PPDO_Inventory_v0.4** (active sheet) | `1u1ZfI8zuRrnLOy1xITo6cuQfNniUEd8sObeU-FlXK3A` |
| Apps Script v0.4 (.gs) | `1EF9OW-Lra2dfj_m6uiEpQvtmsCDIehLa` |
| Project Context file | `1EYl4zJVPeZaHcQVhGPhhuNRGhSkBCAu1` |

---

## 3. Web Portal — Current Status

**Version:** v0.5 prototype  
**Stage:** Frontend prototype (Penpot + HTML interactive prototype)  
**Next milestone:** v1.0 production (stack TBD)

### Prototype Files

| File | Location | Description |
|---|---|---|
| `ppdo_portal_v3.html` | Claude outputs | Latest interactive HTML prototype (all screens) |
| `ppdo_portal_excel_ux.html` | Claude outputs | Excel-feel UX version |
| Penpot file | design.penpot.app | Live editable design file (see Section 5) |

---

## 4. Portal — Feature Scope

### Confirmed Features
1. **Public landing page** — PPDO branding, feature overview
2. **Login page** — Email/password, role badges shown
3. **Main dashboard** — Calendar only (office + personal events + holidays)
4. **Inventory monitor** — Full workflow (see Section 6)
5. **Employee profiles** — Staff directory, self-update contact info (future)
6. **User management** — RBAC (see Section 7)
7. **Admin tools** — Items Master Data, Settings

### Planned / Deferred
- PAR/ICS slip generation
- Printed PR form matching official GSO format
- Employee profiles (planned v1.x)
- Excel/Google Sheets live API sync (deferred, noted as future)

---

## 5. Penpot Design File

**URL:** `https://design.penpot.app` (user's account)  
**File:** PPDO Portal v0.5  
**Connection:** Claude Desktop + Penpot MCP (`penpot:execute_code`, `export_shape`, etc.)

### Frames on Canvas (Page 1)

| Frame | x position | Size | Shapes | Notes |
|---|---|---|---|---|
| `02 Login` | x=0 | 1280×800 | ~30 | ✅ Built |
| `01 Landing` | x=1400 | 1280×900 | ~70 | ✅ Built |
| `03 Main Dashboard` | x=2800 | 1280×800 | ~321 | ✅ Rebuilt clean |
| `04 Inventory Dashboard` | x=4200 | 1280×800 | ~250+ | ✅ Built + grouped stat cards |
| `05 Receive Delivery` | x=5600 | 1280×800 | ~255 | ✅ Built |
| `06 Items Master` | x=7000 | 1280×800 | ~311 | ✅ Built |
| `07 PR Report` | x=8400 | 1280×936 | ~576 | ✅ Built (taller for content) |
| `04b Create PR` | x=9800 | 1280×900 | ~415 | ✅ Built |

**Page 2** — `🎨 Color Palette` (created, not yet populated)

### Penpot MCP Notes
- Use `storage.rbox(F, name, rx, ry, w, h, fill, radius, stroke)` — coordinates relative to frame
- Use `storage.rtxt(F, name, content, rx, ry, size, color, bold)` — coordinates relative to frame
- Font weights: only `'400'` and `'700'` supported (Source Sans Pro)
- Always re-init `storage.colors` at start of new session (stored in `storage` object, lost between connections)
- Build large frames in batches to avoid 30s timeout
- Use `penpotUtils.findShape(s => s.name === 'frameName')` to find frames

---

## 6. Inventory Monitor — Screen Inventory

### Screens Built (Penpot + HTML prototype)

| Screen | Tab Label | Key Content |
|---|---|---|
| Inventory Dashboard | 📦 Inv. Dashboard | Grouped stat cards (2 groups), PR status table, alerts, quick actions |
| Items Overview | 🗂️ Items | Full items table with filter row, stock bars |
| Create PR | 📋 Create PR | Section 1 (9+9 fields), Section 2 (items grid) |
| Receive Delivery | 🚚 Receive Delivery | Delivery info form, PR selector, items table with split delivery |
| PR Report | 📋 PR Report | Section 1 (details), Section 2 (line items), Section 3 (distribution) |
| Items Master | 🗃️ Items Master | Full catalog with ★ NEW flags, edit buttons |

### Screens NOT YET in Penpot (exist in HTML prototype only)
- Distribution view
- Item Ledger
- PR Register

---

## 7. User Roles & Permissions (RBAC + Division Scope)

| Role | Access |
|---|---|
| **Super Admin** | Full access, all divisions, user management |
| **Admin** | All inventory actions, all divisions, reports |
| **Staff** | Create PRs for their division, view own PRs |
| **Viewer** | Read-only — reports and inventory |

**Division scope** — data filtered by division for Staff/Viewer. Admin and Super Admin see all divisions.

**Divisions:** Administrative (Admin), Planning, Research Monitoring & Evaluation (RM), Management Information System (MIS), Special Program (SPD)

---

## 8. Design System

### Color Palette

```css
/* Green — Primary Brand (Philippine government green, DICT/DepEd inspired) */
--green-950: #071F12
--green-900: #0F4526
--green-800: #13512D
--green-700: #196638   /* Login header, landing hero */
--green-600: #1F7A45   /* PRIMARY — sidebar, buttons */
--green-500: #2E9958   /* Hover on primary buttons */
--green-400: #3BAD6A   /* Progress bars, dots, badges */
--green-300: #6DC492
--green-200: #A8DABC   /* Borders on green elements */
--green-100: #D4EDDE
--green-50:  #F0FAF4   /* Table hover, icon backgrounds */
--green-25:  #F7FCF9

/* Slate Gray — Neutral */
--slate-800: #343A40   /* Footer */
--slate-600: #6C757D   /* Body text, labels */
--slate-400: #ADB5BD   /* Disabled, muted */
--slate-200: #E9ECEF   /* Input borders */
--slate-100: #F1F3F5   /* Page background */
--slate-50:  #F8F9FA   /* Zebra rows */

/* Status Colors */
--amber-500: #EF9F27   /* Warning, partial delivery */
--amber-100: #FEF3CD   /* Warning pill bg */
--red-500:   #E24B4A   /* Danger, out of stock */
--red-100:   #FDECEA   /* Danger pill bg */
--blue-500:  #378ADD   /* Personal events, open PR */
--blue-100:  #E3F2FD   /* Open PR pill bg */

/* Semantic */
--color-primary:        #1F7A45
--color-bg-page:        #F1F3F5
--color-bg-card:        #FFFFFF
--color-bg-sidebar:     #1F7A45
--color-cell-fill:      #FFFDE7   /* Yellow — user fills in */
--color-cell-auto:      #F1F3F5   /* Gray — auto-fill */
--color-cell-green:     #F0FAF4   /* Green — system-generated */

/* Status Card Backgrounds (grouped stat cards) */
--bg-blue:   #EBF4FF   /* Open PRs */
--bg-amber:  #FEF9EC   /* Partially Delivered */
--bg-green:  #F0FAF4   /* Completed / In Stock */
--bg-red:    #FEF2F2   /* Out of Stock */
--bg-purple: #F3F0FF   /* Unique Items */
```

### UX Principles
- **Excel-familiar UX** — ribbon toolbar, sheet tabs, row numbers, filter rows, formula bar, zebra rows, status bar
- **Yellow = user fills in**, **Gray = auto-fill**, **Green tint = system-generated**
- Font: Segoe UI / Source Sans Pro (Penpot)
- Font weights in Penpot: `400` (normal) and `700` (bold) only

---

## 9. Google Sheets System (v0.4) — Reference

### Sheet Tab Names (with emoji prefixes)
| Sheet | Purpose |
|---|---|
| 📊 Dashboard | Summary stats, PR selector |
| 📋 Create PR | PR submission form |
| 📥 Receive Delivery | Delivery logging form |
| 📋 PR Report | Auto-generated PR report |
| 📊 Inventory | Per-PR-per-item stock levels |
| 📒 Item Ledger | Cross-PR running totals |
| 🔍 PR Register | Backend PR data |
| 📦 PR Items | Backend item rows |
| 🚚 Deliveries | Backend delivery records |
| 📤 Distribution | Backend distribution records |
| 🗃️ Items Master Data | Supply catalog |

### Key Apps Script Functions
| Function | Description |
|---|---|
| `SubmitPR()` | Validates + writes to PR Register, PR Items, Items Master |
| `SubmitDelivery()` | Writes to Deliveries + Distribution, calls updatePRStatus() |
| `ResetPRForm()` | Clears form, restores defaults |
| `ResetDeliveryForm()` | Clears form, generates new DEL ref |
| `updatePRStatus(prNo)` | Open → Partially Delivered → Fully Delivered |
| `LoadPRReport()` | Sets G50 and navigates to PR Report sheet |

### Important Field Notes
- **Program, Project, Activity** fields can be very long (80–120 chars). In the web app these must use `<textarea>` with `min-height: 44px`, `max-height: 88px`, `resize: vertical`.
- PR No. format: `101-1041-GF-YYYY-MM-DD-XXX`
- Delivery Ref format: `DEL-YYYYMMDD-XXXXX`
- Issue Ref format: `ISS-YYYYMMDD-XXXXX-N`

---

## 10. Inventory Dashboard — Grouped Stat Cards

Two groups side by side (each 516px wide, 130px tall):

**Group 1 — 📋 PURCHASE REQUESTS**
| Card | Value | Color |
|---|---|---|
| Total PRs | 3 | Neutral gray |
| Open PRs | 0 | Blue bg |
| Partially Delivered | 1 | Amber bg |
| Completed PRs | 0 | Green bg |

**Group 2 — 📦 INVENTORY ALERTS**
| Card | Value | Color |
|---|---|---|
| In Stock Items | 0 | Green bg |
| Low / Out of Stock | 22 | Red bg |
| Total PR Value | ₱0 | Gray bg (amber text) |
| Unique Items Tracked | 22 | Purple bg |

---

## 11. Test Data (from live Google Sheet)

### Active PRs
| PR No. | Status | Fulfillment |
|---|---|---|
| 101-1041-GF-2026-24-28-759 | Partially Delivered | 23% (19/83 units) |
| 101-1041-GF-2026-04-28-757 | Fully Delivered | 100% |
| 101-1041-GF-2026-04-28-756 | Open | 0% |

### Known Data Issues
- PR Register col J (Total Amount) shows ₱0 for old test PRs
- Distribution sheet has manual fix rows (ISS-fix-1, ISS-fix-2) for Bathroom Tissue missing units
- 9 items in Items Master flagged `★ NEW - review` (no category assigned)

---

## 12. Next Steps / To-Do

### Penpot (Design)
- [ ] Add Distribution screen
- [ ] Add Item Ledger screen  
- [ ] Add PR Register screen
- [ ] Populate Color Palette page (Page 2)
- [ ] Fix Login screen — card header layering (logo/title float above card)
- [ ] Add prototype flow connections (Prototype tab in Penpot)

### HTML Prototype
- [ ] Update `ppdo_portal_v3.html` with grouped stat cards
- [ ] Update with multiline Program/Project/Activity fields in Create PR
- [ ] Add PR Report screen to HTML prototype

### Development (Future)
- [ ] Decide tech stack (suggested: Next.js + Supabase OR Laravel + Vue)
- [ ] Set up Google Sheets API sync (bidirectional, deferred)
- [ ] Implement RBAC middleware
- [ ] Build Employee Profiles feature

---

## 13. Connected Tools & Integrations

| Tool | Status | Notes |
|---|---|---|
| Penpot MCP | ✅ Connected (Claude Desktop) | Custom connector, design.penpot.app |
| Figma MCP | ✅ Connected (view-only) | Starter plan, read-only |
| Google Drive | ✅ Connected | Can read/access PPDO sheets |
| Gmail | ✅ Connected | |
| Google Calendar | ✅ Connected | |
| Linear | ✅ Connected | |

**Important:** Penpot MCP requires Claude Desktop (not claude.ai browser). The MCP plugin must be open and connected inside Penpot before using Penpot tools.

---

## 14. How to Resume This Project

1. Open **Claude Desktop** (not claude.ai browser) or continue in this Claude Project
2. Open `design.penpot.app` → PPDO Portal v0.5 → enable MCP plugin
3. Re-init storage helpers at start of any Penpot session:

```javascript
// Always run this first in a new Penpot session
storage.colors = {
  g900:'#0F4526', g800:'#13512D', g700:'#196638', g600:'#1F7A45',
  g500:'#2E9958', g400:'#3BAD6A', g200:'#A8DABC', g100:'#D4EDDE', g50:'#F0FAF4',
  s800:'#343A40', s600:'#6C757D', s400:'#ADB5BD', s200:'#E9ECEF',
  s100:'#F1F3F5', s50:'#F8F9FA',
  white:'#FFFFFF', border:'#CCCCCC', text:'#1A1A1A',
  yellow:'#FFFDE7', warn:'#EF9F27', danger:'#E24B4A',
  bgBlue:'#EBF4FF', bgAmber:'#FEF9EC', bgGreen:'#F0FAF4',
  bgRed:'#FEF2F2', bgPurple:'#F3F0FF',
  tAmber:'#B45309', tGreen:'#166534', tRed:'#B91C1C',
  tBlue:'#1D4ED8', tPurple:'#5B21B6',
};
storage.rbox = (F, name, rx, ry, w, h, fill, radius, stroke) => {
  const r = penpot.createRectangle();
  r.name = name; r.x = F.x + rx; r.y = F.y + ry; r.resize(w, h);
  r.fills = fill ? [{fillColor:fill, fillOpacity:1}] : [];
  r.strokes = stroke ? [{strokeColor:stroke, strokeOpacity:1, strokeWidth:1, strokeType:'center'}] : [];
  if (radius) r.borderRadius = radius;
  F.appendChild(r); return r;
};
storage.rtxt = (F, name, content, rx, ry, size, color, bold) => {
  const t = penpot.createText(content);
  t.name = name; t.x = F.x + rx; t.y = F.y + ry;
  t.fontSize = size || 12; t.fontWeight = bold ? '700' : '400';
  t.fills = [{fillColor: color || '#1A1A1A', fillOpacity: 1}];
  t.growType = 'auto-width';
  F.appendChild(t); return t;
};
```

4. Reference frame positions (x values) from **Section 5** above
5. Upload `PPDO_PROJECT_CONTEXT.md` to the Claude Project's knowledge base for persistent context

---

## 15. Portal Replaces PPDO Google Site

The PPDO portal replaces the existing internal Google Site at:
`https://sites.google.com/view/ppdo-missionvision/home`

The Google Site currently serves as a central hub of links to Google Sheets, Drive folders, and Docs organized into 4 sections. The portal's **Resource Links** feature replicates this structure natively, with Admin-manageable links.

### Google Site Sections → Portal Resource Links Categories

| Google Site Section | Portal Category | Status |
|---|---|---|
| Supply & Property Management | Supply & Property Management | 🔄 Replaced by Resource Links (+ native Inventory feature) |
| Records Management | Records Management | 🔄 Replaced by Resource Links |
| Human Resource Management | Human Resource Management | 🔄 Replaced by Resource Links (+ Employee Profiles v1.1) |
| Financial Management | Financial Management | 🔄 Replaced by Resource Links |
| Admin Portal / General | General | 🔄 Replaced by Resource Links |

### Migration Strategy (Strangler Fig Pattern)
1. **v1.0** — Resource Links page provides the same links as the Google Site. Staff switch to the portal.
2. **v1.1** — Employee Profiles replaces the HR section's Personnel Profile and 201 Files links.
3. **Future** — Native features replace individual Google Sheet links one by one as they're built.

---

## 16. Landing Page Content Spec

### Image Assets
| File | Description | Location |
|---|---|---|
| `Ph_seal_occidental_mindoro.png` | Province of Occidental Mindoro Official Seal | `frontend/public/images/` |
| `Bagong_Pilipinas_logo.png` | Bagong Pilipinas logo — use transparent PNG version | `frontend/public/images/` |
| `ppdo-logo-placeholder.png` | Placeholder until official PPDO logo is provided | `frontend/public/images/` (generated) |

### Mission (exact text)
> "To be an effective and efficient department in helping the LGU attain its goals and thrust and provide better quality service."

**Styling:** "effective" and "efficient" in red (`text-red-600`) — matching official PPDO slides.

### Vision (exact text)
> "Occidental Mindoro PPDO is an organization handled by competent, people-oriented, committed, proactive and innovative staff equipped with updated capabilities to generate and utilize a vast array of information and technology to propose to stakeholders appropriate socio-economic, physical, cultural and environmental development frameworks and able to work harmoniously with other local and national government functionaries towards the provincial government's mandate."

**Styling:** "PPDO" in red (`text-red-600`) — matching official PPDO slides.

### Card Header Layout (Mission & Vision)
Each card has a 3-column header matching the official slides:
- Left: `Ph_seal_occidental_mindoro.png` (48×48px)
- Center: Title text ("MISSION" or "VISION") — bold, underlined, large
- Right: `Bagong_Pilipinas_logo.png` (48×48px)

### Landing Page Sections (top to bottom)
1. **Hero** — PPDO name, tagline, login CTA button
2. **Mission & Vision** — two side-by-side cards (stacked on mobile)
3. **Announcements** — public posts from Admin, empty state if none
4. **Login CTA** — secondary CTA at bottom

---

*This file was auto-generated on 2026-05-25. Updated 2026-05-28 — added Google Site replacement notes, landing page content spec.*