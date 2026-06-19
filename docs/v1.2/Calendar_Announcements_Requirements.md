# PPDO Portal — Calendar Event Approval & Public Announcements
_Date: June 18, 2026 | Target: PPDO Portal v1.1.1_

> **Working branch:** `release/1.1.1` — all feature branches created off this and PRs target it.
> See combined spec: `docs/v1.1.1/v1.1.1_Requirements.md` §2 (Calendar) and §3 (Announcements).

---

## 1. Feature Overview

### 1A. Calendar Event Approval Workflow

The existing dashboard calendar allows any authenticated user to create Office (shared) or Personal (private) events directly. This feature adds an approval step for non-admin Office event submissions.

**New rules:**
| Creator role | Event type | Behavior |
|---|---|---|
| Admin / SuperAdmin | Office | Immediately `Approved` — visible to all |
| Admin / SuperAdmin | Personal | Immediately visible (private) — no change |
| Staff / Observer / Office user | Office | Created as `Pending` — not visible to others until reviewed |
| Staff / Observer / Office user | Personal | Immediately visible (private) — no approval needed |

**Admin review:**
- Admin/SuperAdmin see a pending-count badge on the Dashboard.
- Clicking opens the `CalendarApprovalPanel` modal with a DataTable of pending events.
- Each row has Approve (green) and Reject (red) buttons.
- Rejection requires a reason string (max 500 characters).

**Creator visibility of own events:**
- Pending Office events are visible only to the creator (orange indicator) and to Admin/SuperAdmin (in the approval panel).
- Rejected Office events remain visible to the creator in red with the rejection reason accessible on click.
- Approved events turn green and become visible to all users.

**Deletion:** Creator can delete their own event. Admin/SuperAdmin can delete any event.

---

### 1B. Public Announcements

Admin/SuperAdmin author rich-text announcements in the portal. Published announcements appear on the public landing page. The landing page already has a placeholder section (empty state card).

**Access:** Admin and SuperAdmin only. No new permission flag — simple role check.

**Status workflow:**
```
Draft → Published → Archived
         ↑
    (can un-publish back to Draft)
```
- Draft announcements are not visible on the public site.
- Published announcements are displayed publicly, ordered by `PublishedAt DESC`.
- Archived announcements are hidden from the public site but remain in the admin list.
- Deleting a Published announcement is blocked — must archive first.
- Hard delete is allowed for Draft and Archived announcements.

**Rich text:** TipTap editor (React, MIT) with toolbar: Bold · Italic · Underline · Heading (H2/H3) · Bullet list · Ordered list · Text color · Font family · Clear formatting.

**XSS protection (mandatory):**
- Server-side: `Ganss.Xss` HtmlSanitizer (NuGet) strips disallowed tags/attributes before saving to DB.
- Client-side: `DOMPurify.sanitize()` before all `dangerouslySetInnerHTML` renders.

---

## 2. Database Schema Changes

### 2A. `calendar_events` — ALTER (migration: `AddCalendarEventApproval`)

Add four nullable columns to the existing table. Default `status = 1` (Approved) so all existing rows remain visible.

```sql
ALTER TABLE calendar_events ADD
  status           INT           NOT NULL DEFAULT 1,   -- 0=Pending, 1=Approved, 2=Rejected
  reviewed_by_id   UNIQUEIDENTIFIER NULL,              -- FK → Users (Restrict)
  reviewed_at      DATETIME2     NULL,
  rejection_reason NVARCHAR(500) NULL;

ALTER TABLE calendar_events
  ADD CONSTRAINT FK_CalendarEvents_ReviewedBy
    FOREIGN KEY (reviewed_by_id) REFERENCES users (id)
    ON DELETE RESTRICT;

CREATE INDEX IX_CalendarEvents_Status ON calendar_events (status);
```

### 2B. `announcements` — NEW (migration: `AddAnnouncements`)

```sql
CREATE TABLE announcements (
  id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
  title           NVARCHAR(200)    NOT NULL,
  content         NVARCHAR(MAX)    NOT NULL,   -- sanitized HTML
  status          INT              NOT NULL DEFAULT 0,  -- 0=Draft, 1=Published, 2=Archived
  published_at    DATETIME2        NULL,       -- set once on first Publish; never updated
  created_by_id   UNIQUEIDENTIFIER NOT NULL,
  created_at      DATETIME2        NOT NULL,
  updated_at      DATETIME2        NOT NULL,

  CONSTRAINT FK_Announcements_CreatedBy
    FOREIGN KEY (created_by_id) REFERENCES users (id)
    ON DELETE RESTRICT
);

CREATE INDEX IX_Announcements_Status       ON announcements (status);
CREATE INDEX IX_Announcements_PublishedAt  ON announcements (published_at DESC);
```

---

## 3. Domain Model

### 3A. `CalendarEventStatus` enum (new, PPDO.Domain/Enums/)
```csharp
public enum CalendarEventStatus
{
    Pending  = 0,
    Approved = 1,
    Rejected = 2,
}
```

### 3B. `CalendarEvent` entity changes (PPDO.Domain/Entities/)
Add properties:
- `CalendarEventStatus Status { get; set; } = CalendarEventStatus.Approved;`
- `Guid? ReviewedById { get; set; }`
- `DateTime? ReviewedAt { get; set; }`
- `string? RejectionReason { get; set; }`
- `User? ReviewedBy { get; set; }` (navigation)

### 3C. `Announcement` entity (new, PPDO.Domain/Entities/)
```csharp
public sealed class Announcement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;  // max 200
    public string Content { get; set; } = string.Empty; // sanitized HTML
    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User? CreatedBy { get; set; }
}
```

### 3D. `AnnouncementStatus` enum (new, PPDO.Domain/Enums/)
```csharp
public enum AnnouncementStatus
{
    Draft     = 0,
    Published = 1,
    Archived  = 2,
}
```

---

## 4. Application Layer

### 4A. IDashboardService / DashboardService — changes

New / changed method signatures:

```csharp
// CHANGED: now returns caller's own Pending/Rejected events in addition to Approved
Task<List<CalendarEventDto>> GetEventsAsync(int year, int month, User caller, CancellationToken ct);

// CHANGED: Admin/SuperAdmin → Approved; others creating Office event → Pending
Task<ServiceResult<CalendarEventDto>> CreateEventAsync(User caller, CreateCalendarEventDto dto, CancellationToken ct);

// NEW: Admin/SuperAdmin only — list all Pending Office events
Task<ServiceResult<List<PendingCalendarEventDto>>> GetPendingEventsAsync(User caller, CancellationToken ct);

// NEW: Admin/SuperAdmin only — approve or reject a pending event
Task<ServiceResult<CalendarEventDto>> ReviewEventAsync(User caller, Guid id, ReviewCalendarEventDto dto, CancellationToken ct);

// NEW: creator or Admin/SuperAdmin
Task<ServiceResult<bool>> DeleteEventAsync(User caller, Guid id, CancellationToken ct);
```

### 4B. DTOs (PPDO.Application/DTOs/Dashboard/)

**`CalendarEventDto`** — add fields:
- `CalendarEventStatus Status`
- `string? RejectionReason`

**`ReviewCalendarEventDto`** (new):
```csharp
public sealed record ReviewCalendarEventDto(bool Approved, string? RejectionReason);
```

**`PendingCalendarEventDto`** (new):
```csharp
public sealed record PendingCalendarEventDto(
    Guid Id, string Title, string? Description,
    DateTime StartDate, DateTime? EndDate, bool IsAllDay,
    string CreatedByName, DateTime CreatedAt);
```

### 4C. IAnnouncementService / AnnouncementService (new)

```csharp
public interface IAnnouncementService
{
    Task<List<AnnouncementPublicDto>> GetPublishedAsync(CancellationToken ct);
    Task<ServiceResult<List<AnnouncementDto>>> GetAllAsync(User caller, CancellationToken ct);
    Task<ServiceResult<AnnouncementDto>> CreateAsync(User caller, CreateAnnouncementDto dto, CancellationToken ct);
    Task<ServiceResult<AnnouncementDto>> UpdateAsync(User caller, Guid id, UpdateAnnouncementDto dto, CancellationToken ct);
    Task<ServiceResult<AnnouncementDto>> PublishAsync(User caller, Guid id, CancellationToken ct);
    Task<ServiceResult<AnnouncementDto>> UnpublishAsync(User caller, Guid id, CancellationToken ct);
    Task<ServiceResult<AnnouncementDto>> ArchiveAsync(User caller, Guid id, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(User caller, Guid id, CancellationToken ct);
}
```

**Permission check (all write methods):**
```csharp
if (caller.Role is not (UserRole.Admin or UserRole.SuperAdmin))
    return ServiceResult<T>.Forbidden("Announcements can only be managed by Admin or SuperAdmin.");
```

**HtmlSanitizer usage (CreateAsync / UpdateAsync):**
```csharp
using Ganss.Xss;
var sanitizer = new HtmlSanitizer();
dto = dto with { Content = sanitizer.Sanitize(dto.Content) };
```

### 4D. Announcement DTOs (new, PPDO.Application/DTOs/Announcements/)

```csharp
public sealed record AnnouncementPublicDto(
    Guid Id, string Title, string Content, DateTime? PublishedAt);

public sealed record AnnouncementDto(
    Guid Id, string Title, string Content, AnnouncementStatus Status,
    DateTime? PublishedAt, DateTime CreatedAt, DateTime UpdatedAt, string CreatedByName);

public sealed record CreateAnnouncementDto(string Title, string Content);
public sealed record UpdateAnnouncementDto(string Title, string Content);
```

---

## 5. API Endpoints

### 5A. Dashboard (existing file: `DashboardFunctions.cs`)

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/dashboard/events/pending` | JWT, Admin/SuperAdmin | List all pending Office events |
| PUT | `/api/dashboard/events/{id}/review` | JWT, Admin/SuperAdmin | Approve or reject a pending event |
| DELETE | `/api/dashboard/events/{id}` | JWT, creator or Admin/SuperAdmin | Delete an event |

### 5B. Announcements (new file: `AnnouncementFunctions.cs`)

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/announcements` | **Public (no JWT)** | Published announcements only |
| GET | `/api/announcements/manage` | JWT, Admin/SuperAdmin | All announcements with status |
| POST | `/api/announcements` | JWT, Admin/SuperAdmin | Create (Draft) |
| PUT | `/api/announcements/{id}` | JWT, Admin/SuperAdmin | Update title/content |
| PUT | `/api/announcements/{id}/publish` | JWT, Admin/SuperAdmin | Draft/Archived → Published |
| PUT | `/api/announcements/{id}/unpublish` | JWT, Admin/SuperAdmin | Published → Draft |
| PUT | `/api/announcements/{id}/archive` | JWT, Admin/SuperAdmin | Published → Archived |
| DELETE | `/api/announcements/{id}` | JWT, Admin/SuperAdmin | Hard delete (Draft/Archived only) |

> `GET /api/announcements` was already declared as a public endpoint in `CLAUDE.md`.

---

## 6. Frontend

### 6A. Dashboard calendar changes (`DashboardCalendar.tsx`)

**Event color scheme (updated):**
| Type / Status | Color |
|---|---|
| Office — Approved | `bg-green-600` (unchanged) |
| Office — Pending (own) | `bg-orange-500` |
| Office — Rejected (own) | `bg-red-500` |
| Personal | `bg-blue-500` (unchanged) |
| Holiday (PH) | `bg-amber-500` (unchanged) |

**Create event modal (non-admin path):**
- Button label: "Submit for Approval" (was "Create Event")
- Post-submit message: "Your event has been submitted for admin approval."

**Click on own Pending event:** modal shows title/date + "Awaiting admin approval" badge.
**Click on own Rejected event:** modal shows rejection reason in a red notice box.

### 6B. Dashboard page changes (`(portal)/dashboard/page.tsx`)

- **Non-admins:** shows a chip "N event(s) pending approval" below calendar header. Click → expands a compact list of their pending events.
- **Admin/SuperAdmin:** shows a chip "N event(s) awaiting review". Click → opens `CalendarApprovalPanel`.

### 6C. New component: `CalendarApprovalPanel.tsx`

Modal/drawer (Admin/SuperAdmin only):
- DataTable: Event title · Creator · Start date · Actions
- Approve button (green) — single click, no confirmation needed
- Reject button (red) — opens inline reason input (required, max 500 chars), then confirm

### 6D. New portal page: `(portal)/announcements/page.tsx`

- Sidebar item: megaphone icon, label "Announcements", visible to Admin/SuperAdmin only
- Placed between Dashboard and Inventory in sidebar menu
- DataTable columns: Title · Status badge · Published Date · Last Updated · Actions
- Actions: Edit · Publish/Unpublish toggle · Archive · Delete (ConfirmDialog, danger variant)
- Top-right: "New Announcement" button (green)

### 6E. New component: `AnnouncementEditorModal.tsx`

- Standard `Modal` wrapper
- TipTap `EditorContent` with custom `Toolbar` component
- Toolbar buttons: Bold · Italic · Underline · H2 · H3 · Bullet list · Numbered list · Text color (color picker) · Font family (dropdown) · Clear formatting
- Footer: "Save as Draft" (secondary) · "Publish Now" (primary)
- Edit mode: loads existing content, shows "Save Changes" + current status badge

### 6F. Public landing page (`(public)/page.tsx`)

- Extract the announcements placeholder into `AnnouncementsSection.tsx` (client component)
- Fetch `GET /api/announcements` on mount (no auth header)
- Show skeleton loader while fetching
- Render each announcement as a white card: Title (bold), published date (slate-500 text-sm), rich text via `dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(item.content) }}`
- Keep the existing "No announcements yet" empty state when list is empty

### 6G. New files

| Path | Purpose |
|---|---|
| `src/lib/announcements.ts` | Axios API helpers — CRUD + status actions |
| `src/types/announcements.ts` | TypeScript types mirroring DTOs |
| `src/components/dashboard/CalendarApprovalPanel.tsx` | Admin approval modal |
| `src/components/landing/AnnouncementsSection.tsx` | Public landing announcements display |

### 6H. New NPM packages

```
@tiptap/react
@tiptap/starter-kit
@tiptap/extension-text-style
@tiptap/extension-color
@tiptap/extension-font-family
@tiptap/extension-underline
dompurify
@types/dompurify
```

### 6I. New NuGet package

```
Ganss.Xss   (add to PPDO.Application)
```

---

## 7. Implementation Sequence (Linear Tickets)

> 🌿 **Working branch:** `release/1.3.0` — all feature branches off this, PRs target `release/1.3.0`.

| # | Ticket | Title | Depends on |
|---|---|---|---|
| 1 | RAL-82 | DB: add approval fields to CalendarEvents | — |
| 2 | RAL-83 | Backend: calendar event approval workflow | RAL-82 |
| 3 | RAL-84 | Frontend: calendar event approval UX | RAL-83 |
| 4 | RAL-85 | DB + API: Announcements entity, CRUD, HTML sanitization | — |
| 5 | RAL-86 | Admin portal: Announcements management page (TipTap) | RAL-85 |
| 6 | RAL-87 | Public landing page: display published announcements | RAL-85 |

Chains RAL-82→83→84 and RAL-85→86→87 are independent and can run in parallel.

---

## 8. Verification Checklist

- [ ] Apply migration, verify `calendar_events` gains `status`, `reviewed_by_id`, `reviewed_at`, `rejection_reason` columns
- [ ] Staff creates Office event → visible to creator as orange (pending); invisible to other non-admin users
- [ ] Admin sees pending count badge; opens panel; approves → event turns green on all calendars
- [ ] Admin rejects with reason → creator sees event in red; click shows reason text
- [ ] Staff creates Personal event → immediately visible to creator only (no approval needed)
- [ ] Existing Office events are unaffected (already `Approved` by default)
- [ ] Create announcement as Admin (Draft) → public site shows empty state
- [ ] Publish → announcement appears on public landing page
- [ ] Archive → disappears from public site; still in admin list as Archived
- [ ] Attempt delete on Published → error response; archive first required
- [ ] Paste `<script>alert('xss')</script>` into TipTap → sanitized on server save (Ganss.Xss) and on render (DOMPurify)
- [ ] `GET /api/announcements` returns 200 without any Authorization header
- [ ] All existing tests still pass; new TDD tests added per convention (Application layer ≥ 80% coverage)

---

*PPDO Portal · v1.2 doc · June 18, 2026 · Ralph Armand Alcaide*
