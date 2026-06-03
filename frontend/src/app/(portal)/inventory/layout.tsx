/**
 * Inventory nested layout — wraps every route under /inventory.
 * Breadcrumb is rendered inside the Topbar (Topbar.tsx), not here.
 */
export default function InventoryLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
