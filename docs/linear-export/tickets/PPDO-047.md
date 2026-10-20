---
id: PPDO-47
title: 'A corrupt or non-workbook AIP upload returns 500 instead of 400'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-09-03
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-41
url: https://linear.app/ralphoksiprojects/issue/PPDO-47
uuid: e592ab74-1d2a-48d5-bc98-77674c51fc3f
---

# PPDO-47 — A corrupt or non-workbook AIP upload returns 500 instead of 400

**In one line: **`AipService.ParsePreviewAsync` catches only `AipParseException`, so a file that is not a readable workbook escapes as an unhandled exception and the caller gets a 500 with no message.

## Found

While live-verifying [PPDO-41](https://linear.app/ralphoksiprojects/issue/PPDO-41/v18-38-freeze-xlsm-aip-upload-to-historical-fiscal-years) (V18-38). Posting bytes that are not a zip container to
`POST /api/budget-planning/aip/upload?fiscalYear=2027` returns **HTTP 500** with an empty body. The host log shows:

```
System.IO.FileFormatException: File contains corrupted data.
   at System.IO.Packaging.ZipPackage..ctor(...)
   at System.IO.Packaging.Package.Open(...)
```

**Pre-existing and unrelated to the freeze** — [PPDO-41](https://linear.app/ralphoksiprojects/issue/PPDO-41/v18-38-freeze-xlsm-aip-upload-to-historical-fiscal-years) only surfaced it, because exercising the FY≤2027 path with a throwaway payload is how you prove the historical path still reaches the parser.

## Why it matters

Every other refusal on this endpoint is a 400 carrying a sentence the uploader can act on — wrong fiscal year, unmatched funding source, bad ref code. This one is the case a real user is most likely to hit by accident (a renamed `.xlsx`, a truncated download, a file still syncing) and it is the only one that gives them nothing. The frontend surfaces it through `aipErrorMessage`'s fallback, so they see the generic "Upload failed. Please check the file and try again."

## Suggested fix

In `ParsePreviewAsync`, widen the catch beside the existing `AipParseException` — `FileFormatException` and `InvalidDataException` are the realistic ones from `XLWorkbook.Load` — and return `BadRequest` with a message naming the likely cause ("This file could not be read as an Excel workbook. It may be corrupted, or saved in a different format with an .xlsm extension.").

⚠️ Do **not** widen to a bare `catch (Exception)`. A genuine bug inside the parser would then be reported to the user as "your file is bad", which is worse than a 500 — a 500 at least gets logged and noticed.

## Done when

* A non-workbook upload returns 400 with a message naming the likely cause, not 500
* A genuinely unexpected parser exception still escapes as a 500 and is still logged
* A test covers both, so the two are not collapsed into one catch later
