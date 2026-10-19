---
id: PPDO-202
title: 'Conflict panel: "Discard mine and reload" shows the activity as "No division"'
status: Done
project: PPDO Portal
labels: frontend, Bug
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-11
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-116, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-202
uuid: d77581c9-e298-4261-ab72-ea1004c6fb06
---

# PPDO-202 — Conflict panel: "Discard mine and reload" shows the activity as "No division"

Found in the [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10) UAT run, T8 (2026-10-11, v1.8.2).

## Steps

1. encoder1 and `pto_head_test` both open Edit details on activity 4666 ("Test — office supplies", tagged CASH).
2. encoder1 saves; head saves → conflict panel (correct).
3. Head clicks **Discard mine and reload**.

## Actual

The reloaded activity header shows a **"No division"** badge instead of the Division picker set to Cash Management Division (test), and "With Draft — cannot delete".

## Expected

After reload, the activity shows its division (CASH), as it does on a fresh page load.

## Notes

Display only: encoder1 and a fresh load as the head both still show CASH, so the stored `division_id` is intact. Likely the reload path replaces the activity from a response/DTO that lacks the division fields. Screenshot in chat, not attached (public-repo rule does not apply to Linear, but it shows UAT data).
