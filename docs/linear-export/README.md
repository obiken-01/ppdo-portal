# Linear Export

A local, greppable copy of the PPDO Portal tickets from Linear.

- **[INDEX.md](INDEX.md)** — all 206 tickets in one table
- **[tickets/](tickets/)** — one Markdown file per ticket, `RAL-NNN.md` (zero-padded so they sort)

**Snapshot:** 206 completed tickets, RAL-24 – RAL-273, exported **2026-08-27, before archiving.**

---

## Why this exists

**Not** because archiving loses anything — archived Linear issues stay searchable, restorable, and
readable through the API. This is insurance of a different kind.

Ticket IDs are load-bearing across this repo: **755 commit messages** and **776 references in
tracked docs** point at RAL numbers, spanning **201 distinct tickets** — including the release
history table in `CLAUDE.md`, `docs/v1.8/RETROSPECTIVE.md`, and `docs/TICKET_PROMPT_STANDARD.md`.
If Linear access ever changes — plan, account, workspace — every one of those becomes a dangling
reference. This keeps the trail readable from inside the repo.

It also means ticket context can be read with `grep` instead of an API round-trip.

## Source of truth

**Linear remains authoritative.** This is a point-in-time snapshot, not a mirror. It does not
update itself, and it does **not** include comments, attachments, or sub-issue threads — only the
fields in Linear's CSV export.

## Refreshing it

1. Linear → **Settings → Administration → Import/Export → Export data** (CSV; available to
   everyone on free plans). The emailed download link **expires after 12 hours**.
2. Re-run the converter against the new CSV and overwrite `tickets/` and `INDEX.md`.

Two things the converter has to handle, both learned the hard way:

- **Formula-injection guards.** Linear prefixes an apostrophe to any field starting with `>`, so
  **85 of these 206 descriptions** arrived as `'> …`. That leading `'` must be stripped or every
  blockquote renders wrong.
- **There is no URL column.** Issue URLs are reconstructed as
  `https://linear.app/ralphoksiprojects/issue/<ID>`, which Linear resolves without the title slug.

Titles are written as YAML single-quoted scalars (internal `'` doubled) so colons in titles do not
break the frontmatter.

## Secret scan

The CSV was scanned before committing — connection strings, password assignments, JWT secrets,
Azure storage keys, bearer/GitHub tokens, private keys, and publish-profile credentials.

**No real secrets found.** Four categories matched, all benign:

| Match | Tickets | Assessment |
|---|---|---|
| SQL connection string | RAL-27, RAL-29 | Local dev only — `Server=.\SQLEXPRESS;…;Trusted_Connection=True`, Windows Auth, no password. Already verbatim in `CLAUDE.md`. |
| `Jwt__SecretKey` | RAL-29, RAL-237 | The dev placeholder `dev-secret-key-minimum-32-characters-long`, already in `CLAUDE.md`. RAL-237 names setting keys only, no values. |
| `TamarawUser2026!` | RAL-174, RAL-254 | The documented default seed password, already in `CLAUDE.md`. |
| "password" assignment | RAL-88 | False positive — a TypeScript signature (`changePassword(dto: { currentPassword… })`). |

**Re-run the scan on any future export** rather than assuming it stays clean — descriptions are
exactly where a real credential gets pasted during debugging, and `CLAUDE.md` forbids secrets in
committed files.

> ⚠️ **Worth following up, surfaced by the scan:** RAL-254 documents that `UserService.CreateAsync`
> sets every account to the same hardcoded default password with no `MustChangePassword` flag
> forcing a change. The ticket is marked Done; confirm whether that was the *fix* or only the
> *analysis*.

---

*Exported and converted 2026-08-27. Covers completed tickets only — active tickets stay in Linear.*
