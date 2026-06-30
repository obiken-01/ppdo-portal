# OneDrive Correspondence Logging — Findings & Options

> **Status:** Exploration / idea capture. Not yet scheduled to a release.
> **Date:** 2026-06-30

## The idea

Some PPDO officemates receive **communications from outside the office** (letters,
memos, endorsements) as **physical documents**. Today their process is:

1. Scan the physical document (output is an **image or PDF**).
2. Manually upload the scan to a **shared OneDrive folder**.

The proposal: let them do both steps **inside the portal in one action** — fill a short
log entry (sender, date received, subject, etc.) **and** upload the scan at the same time.
Conceptually similar to how an inbox logs received emails. The log becomes searchable in
the portal; the scan file lives in OneDrive.

## The target folder (important constraint)

The shared folder the office plans to use:

```
https://onedrive.live.com/?id=%2Fpersonal%2F23cfd4206689924e%2FDocuments%2FPPDO%2FPPDO%20Communication%202026
```

Plan: **create a dedicated Microsoft account under the office email** (so no individual
personally owns the folder) and share the folder from there. Path:
`/Documents/PPDO/PPDO Communication 2026`.

⚠️ **This is a *consumer* / personal Microsoft account OneDrive** (`onedrive.live.com`,
personal CID `23cfd4206689924e`) — **not** OneDrive for Business / SharePoint
(`*-my.sharepoint.com` or a `*.sharepoint.com` site). This distinction drives everything
below.

## How the upload would actually work

The portal would **not** have the browser push files straight into OneDrive. Instead:

1. Officemate fills the log form + attaches the scan (image/PDF).
2. Frontend posts file + metadata to the **Functions backend**.
3. Backend uploads the file into the shared folder via **Microsoft Graph API**, and gets
   back the file's `webUrl`.
4. Backend saves the **log row in our SQL DB** with that `webUrl` stored as a clickable link.

Result: the structured log is in our DB (searchable, filterable, permission-scoped like the
rest of the portal), and the scan bytes live in the 1 TB OneDrive. The portal surfaces a
link back to the OneDrive file in the log list.

## Authentication — the key decision

How the backend authenticates to Graph depends entirely on the account type:

| Account type | Auth available | Notes |
|---|---|---|
| **Consumer account** (`onedrive.live.com`) — *the current plan* | **Delegated only** — a user signs in; backend stores a **long-lived refresh token** for the dedicated office account and refreshes access tokens from it. | **Client-credentials / app-only is NOT supported for personal Microsoft accounts.** The backend effectively acts *as* the office account. Refresh token must be stored securely (Azure Key Vault / Function App config) and kept alive (re-consent needed if it ever lapses). |
| **OneDrive for Business** (M365 tenant, `*-my.sharepoint.com`) | **App-only (client credentials)** via Entra ID app registration — clean service identity, no user sign-in, no stored refresh token. | Requires the account/folder to live inside the org's M365 tenant and an Entra admin to grant consent once. |
| **SharePoint / Teams document library** (`*.sharepoint.com/sites/...`) | **App-only** with `Sites.Selected` (scoped to one site) — most robust. | Cleanest of all; only works if the folder is a SharePoint library, not a personal drive. |

### Recommendation

**If feasible, create the dedicated office account inside the organization's Microsoft 365
tenant (OneDrive for Business) rather than as a consumer `onedrive.live.com` account.**
That unlocks **app-only** auth: a single Entra app registration with a client secret in
Azure config, no per-user login, no fragile stored refresh token. It is materially simpler
and safer to operate long-term.

If the office must stay on the **consumer** account, the integration is still possible but
relies on a **one-time interactive OAuth** for that account to mint a refresh token, which
the backend then stores and uses headlessly. The operational risk is the refresh token
lapsing (e.g., long inactivity, password change, MFA reset) and needing re-consent.

## Graph upload mechanics (either path)

- Small files (< 4 MB): `PUT /me/drive/root:/PPDO/PPDO Communication 2026/{filename}:/content`
  (delegated) or `PUT /drives/{drive-id}/root:/...:/content` (app-only).
- Larger scans: **upload session** (`createUploadSession`) + chunked `PUT`.
- Read back `webUrl` (and optionally `id`) from the response to persist the link.
- SDK: `Microsoft.Graph` (.NET) with `Azure.Identity` credential types.

## Alternative worth weighing: Azure Blob Storage

Since the app is already on Azure, storing scans in **Blob Storage** instead of OneDrive
removes all of the above auth complexity (no Entra app, no Graph, no stored refresh token),
keeps every file inside infrastructure we control, and makes upload/list/download a
first-party concern. Trade-off: files would **not** also appear in the existing OneDrive
folder the staff already browse. Choose OneDrive only if "the scans must live in that
OneDrive folder" is a hard requirement; otherwise Blob is the lower-friction option.

## Rough data model (illustrative)

A `correspondence_log` table (snake_case, per `docs/NAMING_CONVENTIONS.md`):

| column | type | note |
|---|---|---|
| `id` | int PK | |
| `office_id` / `division_id` | FK | for permission scoping |
| `sender` | nvarchar | who the communication is from |
| `subject` | nvarchar | |
| `date_received` | date | |
| `reference_no` | nvarchar null | optional internal/external ref |
| `notes` | nvarchar null | |
| `file_name` | nvarchar | original scan filename |
| `file_url` | nvarchar | OneDrive `webUrl` (or Blob URL) |
| `logged_by` | Guid FK → Users | |
| `created_at` | datetime2 | |

Plus the usual list/detail pages (reuse `DataTable`, `Modal`, `useToast`) and a single
upload endpoint that does Graph upload → DB insert.

## Open questions before scheduling

1. **Account type:** Will the dedicated account be created in the **M365 tenant** (OneDrive
   for Business — recommended, enables app-only) or as a **consumer** `onedrive.live.com`
   account (delegated + stored refresh token)?
2. **Entra admin access:** Is there an Entra ID admin who can create an app registration and
   grant consent? (Required for the OneDrive-for-Business / app-only path.)
3. **Must files live in OneDrive specifically**, or would **Azure Blob** (simpler) be
   acceptable?
4. **Scope:** Which offices/divisions use this, and is the log office-scoped like Budget
   Planning?
5. **File constraints:** max size, accepted types (PDF + common image formats), virus
   scanning expectations.
