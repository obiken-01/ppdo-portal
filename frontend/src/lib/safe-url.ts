/**
 * PPDO-299: true only for an absolute http or https URL.
 *
 * Resource links are saved by Staff and clicked by everyone, Admins included. A `javascript:`
 * or `data:` URL in an href would run script in the clicker's session, so anything that fails
 * this check is shown as plain text instead of a link. The API refuses such URLs on save; this
 * guards rows saved before that check existed.
 */
export function isHttpUrl(url: string | null | undefined): boolean {
  if (!url) return false;
  try {
    const { protocol } = new URL(url.trim());
    return protocol === "http:" || protocol === "https:";
  } catch {
    return false;
  }
}
