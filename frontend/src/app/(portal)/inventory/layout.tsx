import InventoryBreadcrumb from "@/components/layout/InventoryBreadcrumb";

/**
 * Inventory nested layout — wraps every route under /inventory.
 * Renders the breadcrumb strip above all inventory page content.
 * Stacks inside the portal shell (sidebar + topbar) from the parent layout.
 */
export default function InventoryLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col h-full">
      <InventoryBreadcrumb />
      <div className="flex-1 overflow-auto">{children}</div>
    </div>
  );
}
