---
id: PPDO-22
title: 'QA: live load test WFP entry with ≥10 concurrent users'
status: Canceled
project: PPDO Portal
labels: 
milestone: Backlog — Unscheduled
created: 2026-07-13
completed: 2026-08-05
parent: (none)
blocked_by: PPDO-26
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-22
uuid: ea8b0f29-0380-4d77-b3ac-ee0129993da9
---

# PPDO-22 — QA: live load test WFP entry with ≥10 concurrent users

> 🌿 QA/testing task (no branch unless fixes fall out). Child of epic [RAL-116](https://linear.app/ralphoksiprojects/issue/RAL-116/wfp-rework-epic-v14).

## Why

Before opening the reworked WFP module to real division users, confirm it holds up under realistic concurrency. The production stack has known cold-start / auto-pause characteristics (Azure Functions Consumption + Azure SQL Free auto-pause) that can bite under simultaneous first-hits.

## Scope

* Exercise the WFP entry flow (login → select AIP/office/division → open activity → add/edit/save expenditures) with **at least 10 concurrent users** against the live environment.
* Watch for: 401/403s (see the allocation-auth fixes in v1.4.1), 500s, `DbContext` concurrency errors, cold-start timeouts, save failures, and the intermittent login-redirect (tracked separately — link that ticket).
* Capture Application Insights traces for any failures and file follow-up bugs.

## Notes

* Related known issues already fixed in v1.4.1: office/division auto-select (`6f6e984`) and allocation reads 403ing for Staff (`5f2ca32`).
* Coordinate with real PPDO users or scripted sessions for the 10 concurrent logins.

## Deliverable

A short results note (pass/fail per scenario + any bugs filed). No code unless the test surfaces a defect.
