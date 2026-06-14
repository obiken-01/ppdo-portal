/**
 * /config — Configuration section entry point.
 *
 * Interim redirect to the first config page (Accounts, RAL-72). The full
 * Configuration dashboard / landing (RAL-71) will replace this with a hub that
 * links to all config pages (Accounts, Offices, Funding Sources).
 */

import { redirect } from "next/navigation";

export default function ConfigIndexPage() {
  redirect("/config/accounts");
}
