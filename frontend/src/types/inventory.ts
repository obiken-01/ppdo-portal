/** Mirrors PPDO.Application/DTOs/Items/ and DTOs/PurchaseRequest/ */

export interface ItemMasterResponse {
  id: string;
  stockNo: string;
  description: string;
  category: string | null;
  unit: string;
  unitCost: number;
  itemType: string | null;
  reorderQty: number;
  remarks: string | null;
  isNewItem: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateItemMasterRequest {
  stockNo: string;
  description: string;
  unit: string;
  unitCost: number;
  category: string | null;
  itemType: string | null;
  reorderQty: number;
  remarks: string | null;
  isNewItem: boolean;
}

export interface UpdateItemMasterRequest {
  stockNo: string | null;
  description: string | null;
  unit: string | null;
  unitCost: number | null;
  category: string | null;
  itemType: string | null;
  reorderQty: number | null;
  remarks: string | null;
  isNewItem: boolean;
}

// ---------------------------------------------------------------------------
// Item lookup (autocomplete)
// ---------------------------------------------------------------------------

/** Mirrors ItemLookupDto — returned by GET /api/items/lookup?term= */
export interface ItemLookupResponse {
  id: string;
  stockNo: string;
  description: string;
  unit: string;
  unitCost: number;
}

// ---------------------------------------------------------------------------
// Purchase Requests
// ---------------------------------------------------------------------------

/** Mirrors CreatePRItemDto */
export interface CreatePRItemRequest {
  stockNo: string | null;
  description: string;
  unit: string;
  quantity: number;
  unitCost: number;
  itemType: string | null;
}

/** Mirrors CreatePRDto */
export interface CreatePRRequest {
  prDate: string;           // "YYYY-MM-DD"
  prNo?: string | null;     // optional — omit or null to let backend auto-generate
  department: string;
  division: string;
  fund: string;
  requestedBy: string;
  position: string;
  approvedBy: string | null;
  approvingPosition: string | null;
  aipCode: string | null;
  accountNo: string | null;
  accountTitle: string | null;
  program: string | null;
  project: string | null;
  activity: string | null;
  saiNo: string | null;
  alobsNo: string | null;
  items: CreatePRItemRequest[];
}

/** Mirrors PRItemDto */
export interface PRItemResponse {
  id: string;
  prId: string;
  itemNo: number;
  stockNo: string | null;
  description: string;
  unit: string;
  quantity: number;
  unitCost: number;
  totalCost: number;
  itemType: string | null;
}

/** Mirrors PRSummaryDto */
export interface PRSummaryResponse {
  id: string;
  prNo: string;
  prDate: string;
  division: string;   // "Admin" | "Planning" | "RM" | "MIS" | "SPD"
  requestedBy: string;
  totalAmount: number;
  status: string;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Deliveries
// ---------------------------------------------------------------------------

/** Mirrors CreateDistributionDto */
export interface CreateDistributionRequest {
  division: string;   // "Admin" | "Planning" | "RM" | "MIS" | "SPD"
  qtyIssued: number;
  dateIssued: string; // "YYYY-MM-DD"
  issuedBy: string;
  remarks: string | null;
}

/** Mirrors CreateDeliveryItemDto */
export interface CreateDeliveryItemRequest {
  prItemId: string;
  qtyDelivered: number;
  distributions: CreateDistributionRequest[];
}

/** Mirrors CreateDeliveryDto */
export interface CreateDeliveryRequest {
  prId: string;
  deliveryDate: string; // "YYYY-MM-DD"
  receivedBy: string;
  supplier: string | null;
  remarks: string | null;
  items: CreateDeliveryItemRequest[];
}

// ---------------------------------------------------------------------------
// PR Report
// ---------------------------------------------------------------------------

/** Mirrors PRReportDistributionDto — Section 3 row */
export interface PRReportDistributionResponse {
  itemNo: number;
  description: string;
  unit: string;
  qtyDelivered: number;
  deliveryRef: string;
  deliveryDate: string;   // "YYYY-MM-DD"
  division: string;       // "Admin" | "Planning" | "RM" | "MIS" | "SPD"
  qtyIssued: number;
  issueRef: string;
  dateIssued: string;     // "YYYY-MM-DD"
  issuedBy: string;
  remarks: string | null;
}

/** Mirrors PRReportDto */
export interface PRReportResponse {
  pr: PRResponse;
  distributions: PRReportDistributionResponse[];
}

/** Mirrors DeliverySummaryDto — lightweight list record with no items */
export interface DeliverySummaryResponse {
  id: string;
  deliveryRef: string;
  prId: string;
  deliveryDate: string;
  receivedBy: string;
  supplier: string | null;
  createdAt: string;
}

/** Mirrors DeliveryResponseDto */
export interface DeliveryResponse {
  id: string;
  deliveryRef: string;
  prId: string;
  deliveryDate: string;
  receivedBy: string;
  supplier: string | null;
  remarks: string | null;
  createdAt: string;
  items: DeliveryItemResponse[];
}

export interface DeliveryItemResponse {
  id: string;
  deliveryId: string;
  prItemId: string;
  qtyDelivered: number;
  distributions: DistributionResponse[];
}

export interface DistributionResponse {
  id: string;
  issueRef: string;
  deliveryItemId: string;
  division: number;  // integer from API
  qtyIssued: number;
  dateIssued: string;
  issuedBy: string;
  remarks: string | null;
}

// ---------------------------------------------------------------------------
// Inventory Dashboard Stats
// ---------------------------------------------------------------------------

/** Mirrors PRStatsGroupDto — Group 1: Purchase Requests */
export interface PRStatsGroupResponse {
  total: number;
  open: number;
  partiallyDelivered: number;
  fullyDeliveredOrCompleted: number;
}

/** Mirrors AlertsGroupDto — Group 2: Inventory Alerts */
export interface AlertsGroupResponse {
  inStock: number;
  lowOrOutOfStock: number;
  totalPRValue: number;
  uniqueItemsTracked: number;
}

/** Mirrors InventoryStatsDto */
export interface InventoryStatsResponse {
  purchaseRequests: PRStatsGroupResponse;
  inventoryAlerts: AlertsGroupResponse;
}

// ---------------------------------------------------------------------------
// Item Ledger
// ---------------------------------------------------------------------------

/** Mirrors ItemLedgerRowDto */
export interface ItemLedgerRowResponse {
  stockNo: string;
  description: string;
  unit: string;
  totalOrdered: number;
  totalDelivered: number;
  totalDistributed: number;
  onHand: number;
  reorderQty: number;
  isLowStock: boolean;
  isOutOfStock: boolean;
}

/** Mirrors PRResponseDto — division is now a string name e.g. "Admin" */
export interface PRResponse {
  id: string;
  prNo: string;
  prDate: string;
  dateCreated: string;
  department: string;
  division: string;   // "Admin" | "Planning" | "RM" | "MIS" | "SPD"
  fund: string;
  requestedBy: string;
  position: string;
  approvedBy: string | null;
  approvingPosition: string | null;
  aipCode: string | null;
  accountNo: string | null;
  accountTitle: string | null;
  program: string | null;
  project: string | null;
  activity: string | null;
  saiNo: string | null;
  alobsNo: string | null;
  totalAmount: number;
  status: string;
  createdById: string;
  createdAt: string;
  updatedAt: string;
  items: PRItemResponse[];
}
