---
id: PPDO-26
title: '[bug] Intermittent redirect to /login during normal use'
status: Canceled
project: PPDO Portal
labels: 
milestone: Backlog — Unscheduled
created: 2026-07-13
completed: 2026-08-02
parent: (none)
blocked_by: RAL-139
related: RAL-198
url: https://linear.app/ralphoksiprojects/issue/PPDO-26
uuid: 58d12f2f-7960-4792-9676-2b2c3ba1f512
---

# PPDO-26 — [bug] Intermittent redirect to /login during normal use

> 🌿 Working branch: `release/1.4.2` (off `main`) once root-caused. Child of epic [RAL-116](https://linear.app/ralphoksiprojects/issue/RAL-116/wfp-rework-epic-v14).
> 📌 Intermittent — reproduction is the hard part.

## Symptom

Some actions bounce the user to `/login` unexpectedly. It does **not** always happen — intermittent.

## Likely area

Token refresh / auth-guard flow:

* Axios interceptor in `frontend/src/lib/api.ts` (401 → refresh → retry, with `callWithRetry` for network errors).
* Portal auth guard in `frontend/src/app/(portal)/layout.tsx`.
* Access token is in-memory only; refresh token in `localStorage["ppdo_rt"]`. Access token expiry is short (15 min).

## Known contributing factors (from CLAUDE.md history)

* Azure Functions cold start (~5-20s) + Azure SQL Free auto-pause (~1 hr) previously caused refresh calls to fail with a network error and wrongly redirect to `/login` — mitigated in v1.0.1 with `callWithRetry` (2 retries, 3s delay, network-errors-only). Check whether that retry still covers every refresh path, or whether a new path (e.g. one of the WFP endpoints, or a genuine 401 vs network error) slips through.
* A concurrency angle: with several tabs/requests in flight, a refresh-token rotation race could invalidate the stored refresh token for a parallel request → 401 → redirect. Refresh tokens rotate on every use (`AuthService`), so two near-simultaneous refreshes can leave one holding a now-stale token.

## Scope

1. Reproduce (ideally during the 10-user load test) and capture: which request 401'd, whether it was a real 401 or a network error, and the refresh-token state at that moment.
2. Harden the refresh flow so a transient failure or a rotation race does not eject a logged-in user. Consider single-flight refresh (dedupe concurrent refreshes so parallel 401s await one refresh) if the race is confirmed.

## Out of scope

Redesigning the auth model — this is a robustness fix to the existing JWT refresh flow.
