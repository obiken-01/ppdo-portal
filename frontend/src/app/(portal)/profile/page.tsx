import { redirect } from "next/navigation";

/**
 * /profile — permanent redirect to /account (RAL-252).
 *
 * This route used to be a "coming soon" stub written before the real page existed. The account
 * page (RAL-88, shipped v1.1.1) has carried profile editing and password changes since, so the
 * stub was a second, emptier door to the same room.
 *
 * The route is kept as a redirect rather than deleted: /profile has been linked and bookmarked
 * for several releases, and the portal's office-user gate still lists it as an allowed path.
 * Deleting it outright would turn those into 404s.
 *
 * Redirecting on the server means the stub never renders — there is no flash of an empty shell
 * before the client decides where to go.
 */
export default function ProfilePage() {
  redirect("/account");
}
