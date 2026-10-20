---
id: PPDO-200
title: 'Session does not survive a page reload in browsers that block third-party cookies'
status: Done
project: PPDO Portal
labels: devops, backend, Bug
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-11
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-200
uuid: 543107b6-559b-4aba-a35a-2da62730702f
---

# PPDO-200 — Session does not survive a page reload in browsers that block third-party cookies

Found in the [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10) UAT run (2026-10-11, v1.8.2). Applies to production too: same architecture.

## What happens

Signed in to UAT in an **Edge InPrivate** window, then pasted a portal URL (or reloaded): redirected to `/login` although signed in. Clicking through the app without a full page load works.

## Why

* The access token is in memory only, so any full page load needs a silent `POST /api/auth/refresh`.
* The refresh cookie is set by the **API host** (`…azurewebsites.net`) while the page is on `…azurestaticapps.net`: a different site, so the browser treats it as a **third-party cookie**. That is why it is `SameSite=None` (`AuthFunctions.cs:37`).
* Edge InPrivate blocks third-party cookies by default; so do Safari (default), Firefox strict mode, any private window, and Chrome as it phases them out. The refresh carries no cookie → 401 → login.

Evidence: App Insights shows 3× `Refresh` 401 during the run (02:00–03:25 UTC), matching the InPrivate attempts.

## Expected

A signed-in user stays signed in across reloads and pasted links in any mainstream browser.

## Fix options (to decide)

1. **Same-site API**: custom domain covering both (e.g. `portal.<domain>` + `api.<domain>`, same registrable domain → first-party cookie).
2. **SWA linked backend** (`/api` on the SWA origin): needs SWA Standard tier (cost).
3. Interim: show a clear message on the login page when a refresh fails with no cookie ("your browser blocks the cookie this portal needs").

Relevant to the MIS-hosting decision: hosting both under one MIS domain fixes this for free.

## Workaround for testers

Normal (non-private) window, or allow third-party cookies for the site.
