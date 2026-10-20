---
id: PPDO-103
title: 'PWA: add mobile-web-app-capable alongside the deprecated Apple meta tag'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-103
uuid: a116c8df-061e-48f6-b64c-f603101b75ec
---

# PPDO-103 — PWA: add mobile-web-app-capable alongside the deprecated Apple meta tag

**In one line:** every page logs a deprecation warning because we emit only `apple-mobile-web-app-capable`.

**Found:** 2026-09-16, in Ralph's Edge console while investigating something else · **Size:** XS

## The warning

```
<meta name="apple-mobile-web-app-capable" content="yes"> is deprecated.
Please include <meta name="mobile-web-app-capable" content="yes">
```

`app/layout.tsx` sets `appleWebApp: { capable: true }`, which is the Next 14 Metadata API's way of emitting the Apple tag. Chromium deprecated that spelling in favour of the standard `mobile-web-app-capable`.

## Scope

Add the standard tag **alongside** the Apple one — Next 14's Metadata API has no first-class field for it, so it goes through `other`:

```ts
other: { "mobile-web-app-capable": "yes" },
```

⚠️ **Keep** `appleWebApp`**.** iOS Safari still reads the Apple spelling; dropping it would break add-to-home-screen on iPhone, which is a real usage path for this portal. This is additive.

## Acceptance

- [ ] The deprecation warning is gone from the console on any portal page
- [ ] Both meta tags are present in the rendered `<head>`
- [ ] Add-to-home-screen still works on iOS
