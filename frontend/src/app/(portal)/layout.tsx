// TODO RAL-xx: Portal layout — Sidebar + auth guard
// All routes inside (portal)/ require a valid JWT. The auth guard
// reads the token from memory (Axios interceptor in lib/api.ts handles
// refresh). Redirect to /login if no valid session.
import type { ReactNode } from "react";

export default function PortalLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
