/**
 * Readable text for a failed API call, whatever shape the error body has (PPDO-295…298).
 *
 * The backend answers errors in two shapes:
 *   • plain text — Inventory, Users and the other older endpoints;
 *   • JSON `{ error }` (the `ApiResponse` envelope) — Budget Planning, Config, and **every** 500,
 *     because the exception handler always writes JSON whichever endpoint threw.
 * A page that assumed only one shape put the object itself into a toast or error line, showing
 * "[object Object]" or crashing the render. This reads either, plus `{ message }`, and falls
 * back to `fallback` for anything else: no body, a blank one, a network error, a non-Axios throw.
 *
 * `announcementErrorMessage` in `announcements.ts` follows the same rule for its own pages.
 */
export function apiErrorText(err: unknown, fallback: string): string {
  const body: unknown = (err as { response?: { data?: unknown } } | null | undefined)?.response?.data;

  if (typeof body === "string") return body.trim() || fallback;

  if (typeof body === "object" && body !== null) {
    const { error, message } = body as { error?: unknown; message?: unknown };
    if (typeof error === "string" && error.trim()) return error;
    if (typeof message === "string" && message.trim()) return message;
  }

  return fallback;
}
