---
id: PPDO-207
title: 'Stop Zod''s eval probe on the login page (CSP report noise)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-10-14
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: PPDO-199
url: https://linear.app/ralphoksiprojects/issue/PPDO-207
uuid: acb81c3c-af66-444b-8af6-80b56f6b8b2c
---

# PPDO-207 — Stop Zod's eval probe on the login page (CSP report noise)

Found in the [PPDO-199](https://linear.app/ralphoksiprojects/issue/PPDO-199/security-hardening-lower-risk-findings-from-the-2026-10-08-review) UAT check (2026-10-14).

**What happens:** on the login page, Zod 4 (`zod@4.4.3`, the only Zod user is `src/app/(public)/login/page.tsx`) checks whether it may compile validators by running `Function("")` in a try/catch. The CSP added in [PPDO-199](https://linear.app/ralphoksiprojects/issue/PPDO-199/security-hardening-lower-risk-findings-from-the-2026-10-08-review) (`script-src` without `'unsafe-eval'`) blocks it, so every login page load files a `script-src eval` CSP violation report (chunk 8578).

**Impact:** none on behaviour. Zod catches the error and validates in its non-eval mode. The cost is noise: a real CSP violation would sit among these reports.

**Fix:** `z.config({ jitless: true })` before the schema is used. Zod then skips the probe and always uses the non-eval mode it already falls back to. Same validation, no report.

**Verify:** load `/login` on UAT, read buffered `ReportingObserver` csp-violation reports: expect none. Sign in still works, and an empty password still shows the validation message.
