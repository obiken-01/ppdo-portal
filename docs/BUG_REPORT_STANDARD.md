# Bug Report Standard

## Ralph's Personal Development Standard (RPDS) — Phase 6: Maintenance

> **Purpose:** A consistent bug reporting standard ensures bugs are actionable, traceable, and resolved efficiently. Follow this standard when logging bugs in any project management tool.

---

## Bug Severity Levels

Every bug must be assigned a severity level when reported.

|Severity|Definition|Examples|Response Time|
|---|---|---|---|
|🔴 **Critical**|System is down or completely unusable. Data loss or security breach.|Login broken for all users, data being deleted incorrectly, security vulnerability exposed|Immediately — drop everything|
|🟠 **High**|Major feature is broken. Significant impact on users. No workaround available.|Filter returns wrong results, export fails, idle logout happens unexpectedly|Same day|
|🟡 **Medium**|Feature partially works. Workaround exists. Impacts user experience but not core function.|Date shows UTC instead of local time, button spacing looks wrong on mobile|Within current version|
|🟢 **Low**|Minor cosmetic or UX issue. Does not affect functionality.|Typo in label, minor alignment issue, tooltip missing|Next version or backlog|

---

## Bug Lifecycle

```
New → Open → In Progress → For Review → Fixed → Done
```

|Status|Meaning|
|---|---|
|**New**|Bug has been reported but not yet reviewed|
|**Open**|Bug confirmed and accepted — ready to be worked on|
|**In Progress**|Developer is actively working on the fix|
|**For Review**|Fix is implemented — waiting for verification|
|**Fixed**|Fix verified on staging or production|
|**Done**|Bug closed — verified, deployed, and confirmed resolved|

> **Note:** A bug can be moved back to **Open** if the fix is rejected during review. A bug can be marked **Duplicate** or **Won't Fix** and closed without going through the full lifecycle — always add a comment explaining why.

---

## Bug Report Template

Use this template when logging a bug in your project management tool.

---

### Title

```
[Component/Feature]: Short description of the issue

Examples:
Timekeeping: Date filter returns 400 Bad Request
Auth: Idle session logs out with "Invalid refresh token" error
TopMenu: Navigation links overlap on mobile screens
```

### Bug Report Fields

**Severity:** 🔴 Critical / 🟠 High / 🟡 Medium / 🟢 Low

**Where (Location):**

> The page, feature, or component where the bug occurs. Example: Netlify tools site → Timekeeping → Filter Logs section

**Environment:**

- [ ] Production
- [ ] Staging
- [ ] Local development
- [ ] All environments

**Description:**

> A clear explanation of what the bug is. What is happening that should not be happening?

**Steps to Replicate:**

> Numbered steps that reliably reproduce the bug. Be specific.

```
1. Log in to the timekeeping app
2. Navigate to the Filter Logs section
3. Select a From date of 2026-05-04
4. Select a To date of 2026-05-04
5. Click Apply
6. Observe: "Failed to load time logs" error appears
```

**Expected Result:**

> What should happen when following the steps above? Example: Logs from May 4, 2026 should be displayed in the table.

**Actual Result:**

> What actually happens? Example: A "Failed to load time logs" error is displayed. Network tab shows 400 Bad Request.

**Root Cause (if known):**

> If you already know why it's happening, document it here. Example: DateOnly values sent without UTC suffix — PostgreSQL rejects unspecified timezone.

**Fix (if known):**

> If you already know the fix, document it here with affected files. Example: Append `Z` to date strings before sending. Affected file: `TimeLogPage.jsx`

**Screenshots / Logs:**

> Attach screenshots, network request details, or log output if available.

---

## Labeling Convention

Use consistent labels in your project management tool:

|Label|When to use|
|---|---|
|`bug`|Confirmed bug — something is broken|
|`ux`|User experience issue — not broken but needs improvement|
|`security`|Security-related issue|
|`performance`|Slow or inefficient behavior|
|`regression`|Bug introduced by a recent change that broke previously working functionality|
|`wont-fix`|Acknowledged but intentionally not fixing|
|`duplicate`|Same as an existing issue|

---

## Linking Bugs to Versions

Always link a bug to the version/milestone it will be fixed in:

- Use the project management tool's milestone or version field
- If the bug is critical — fix it in the current version
- If medium or low — add it to the next planned version backlog
- Always note which version introduced the bug (if known)

---

## Example Bug Reports

### Good bug report ✅

```
Title: Timekeeping: Date filter returns 400 Bad Request

Severity: 🟠 High
Where: Netlify tools site → Timekeeping → Filter Logs
Environment: Production

Description:
Applying From/To date filters results in a "Failed to load time logs" error.
Logs display correctly when no filters are applied.

Steps to Replicate:
1. Log in to timekeeping
2. In Filter Logs, set From = 05/04/2026 and To = 05/04/2026
3. Click Apply

Expected: Logs from May 4 are displayed
Actual: "Failed to load time logs" error. Network shows 400 Bad Request.

Root Cause: DateOnly sent as plain date string — PostgreSQL rejects without UTC info.
Fix: Use DateOnly on backend + specify DateTimeKind.Utc in repository query.
Affected files: TimeLogQueryDto.cs, TimeLogRepository.cs
```

### Bad bug report ❌

```
Title: Filter not working

Description: The filter doesn't work when I try to use it.
```

---

_Part of Ralph's Personal Development Standard (RPDS) v1.0 — Phase 6: Maintenance_