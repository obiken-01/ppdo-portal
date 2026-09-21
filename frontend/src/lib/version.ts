/**
 * Single source of truth for the displayed application version.
 *
 * Previously this string was hardcoded in three separate places — the portal
 * sidebar, the login page, and the public footer — and `CLAUDE.md` carried a
 * standing warning that all three must be updated together because they had
 * already drifted out of sync. They now all read this constant, so a version
 * bump is a one-line change here.
 *
 * Version scheme (`CLAUDE.md`): patch releases use `vX.Y.Z`, feature
 * milestones use `vX.Y`. Bump this at the START of a version's development —
 * as the first commit on its `release/X.Y.Z` branch — so the displayed version
 * always reflects what is actually running, including on preview and local builds.
 */
export const APP_VERSION = "v1.8.0";
