/**
 * Portal name and office logo (PPDO-267, PPDO-268).
 *
 * The single place the system's name and the office logo are written. Every visible
 * mention reads from here, so a future rename is an edit to this file rather than a
 * hunt across pages.
 *
 * ⚠️ The portal name is not the office name. "PLANO MO" names the system and survives an
 * office rename; the office (PPDO, becoming OPPDC) is still written in page copy until
 * PPDO-269 moves it onto the host Offices record.
 *
 * ⚠️ Technical identifiers that merely contain "ppdo" are NOT branding and must not follow
 * a rename: the `ppdo_rt` refresh cookie, the JWT audience `ppdo-portal`, cache and
 * storage keys, namespaces and Azure resource names. See PPDO-197 for what each breaks.
 */

/** Short name: sidebar, navbar, tab titles. Keep it ≤ 12 characters (installed-app label). */
export const APP_NAME = "PLANO MO";

/** What the letters stand for, shown under the short name on the login page. */
export const APP_LONG_NAME = "Provincial LDIP–AIP Network of Occidental Mindoro";

/** Short name and long form together, for SEO titles and link previews. */
export const APP_FULL_NAME = `${APP_NAME} — ${APP_LONG_NAME}`;

/**
 * The office logo. Neutral filenames so the next office rename replaces the image
 * without renaming the file. WebP for on-page use (256×256); PNG for link previews and
 * structured data, which some crawlers do not read as WebP (512×512).
 */
export const OFFICE_LOGO_WEBP = "/images/office-logo.webp";
export const OFFICE_LOGO_PNG = "/images/office-logo.png";
