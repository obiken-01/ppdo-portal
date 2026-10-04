# Investment Planning dashboard — wireframes

The design canvas for the dashboard design pass, drawn 2026-10-04:
**https://claude.ai/artifact/H2UFMjGgMwGfwWzShHzjr1** (private: share it from the page's Share menu before
sending the link to anyone else).

The findings, measurements and suggested tickets are in
[../../Dashboard_Design_Pass.md](../../Dashboard_Design_Pass.md). No code was changed.

## What is here

| File | Artboard |
|---|---|
| `Main.dc.html` | **1 · Department head, guest office.** Status band ("ready for you to send to PPDO"), step track, ceiling meter, Investment proposals band, readable recent activity. No Divisions band: the office has none |
| `Reviewer.dc.html` | **2 · PPDO cross-office reviewer.** "4 offices are waiting for PPDO review", the province bar by stage, the Offices board moved up with a compact Not started column, PPDO's own office as one card |
| `HostFinance.dc.html` | **3 · PPDO finance.** Status band about unallocated money, the six-step host track, the fund card with the division split at full width, empty division rows folded |
| `Mobile.dc.html` | **4 · Encoder on a phone.** The step track as a list, so status, action and progress fit the first screen |
| `States.dc.html` | **5 · States.** The status band's sentence and button for every state and reader, recent activity today vs proposed, empty division rows, the loading skeleton |
| `canvas.json` | Artboard positions and titles |

Sample data is fictional, shaped like the local test data. Colours and spacing are the portal's tokens
(`frontend/tailwind.config.ts`), flat, with no rounded cards.

## Rebuilding the canvas

The `.dc.html` files are the source of truth. Edit them here and republish to the canvas above through the
Design artifact type, the same way the other wireframe sets in this folder are kept.
