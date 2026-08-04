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

/** Mirrors ItemMasterSearchResultDto — GET /api/items/master/search response. */
export interface ItemMasterSearchResult {
  items: ItemMasterResponse[];
  totalCount: number;
  totalNewItemCount: number;
  page: number;
  pageSize: number;
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
  // Filter fields
  fund: string;
  aipCode: string | null;
  accountNo: string | null;
  accountTitle: string | null;
  program: string | null;
  project: string | null;
  activity: string | null;
}

/** Mirrors PRStatusCountsDto — per-status counts within a filtered PR search result. */
export interface PRStatusCounts {
  open: number;
  partiallyDelivered: number;
  fullyDelivered: number;
  completed: number;
}

/** Mirrors PRSearchResultDto — GET /api/purchase-requests/search response. */
export interface PRSearchResult {
  items: PRSummaryResponse[];
  totalCount: number;
  page: number;
  pageSize: number;
  statusCounts: PRStatusCounts;
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

/** Mirrors PRReportDeliveryItemDto — one row per DeliveryItem, no distributions required */
export interface PRReportDeliveryItemResponse {
  itemNo: number;
  deliveryRef: string;
  deliveryDate: string;   // "YYYY-MM-DD"
  qtyDelivered: number;
}

/** Mirrors PRReportDto */
export interface PRReportResponse {
  pr: PRResponse;
  distributions: PRReportDistributionResponse[];
  /** Delivery items — source of truth for qty delivered, independent of distributions */
  deliveryItems: PRReportDeliveryItemResponse[];
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
  category: string | null;
  unit: string;
  unitCost: number;
  itemType: string | null;
  qtyOrdered: number;
  qtyDelivered: number;
  qtyDistributed: number;
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

/** Mirrors GsoPRImportItemDto. */
export interface GsoPRImportItemResponse {
  stockNo: string | null;
  description: string;
  unit: string;
  quantity: number;
  unitCost: number;
  isUnknownStock: boolean;
}

/**
 * Mirrors GsoPRImportPreviewDto — POST /api/purchase-requests/import/gso-preview response
 * (RAL-196/RAL-197). Prefill data only; every field except `items` may be null since neither
 * source export format's parsed data carries all of these. Division and the signatory fields
 * are never in either format.
 */
export interface GsoPRImportPreviewResponse {
  prNo: string | null;
  fund: string | null;
  prDate: string | null; // "YYYY-MM-DD"
  purpose: string | null;
  aipCode: string | null;
  accountNo: string | null;
  accountTitle: string | null;
  program: string | null;
  project: string | null;
  activity: string | null;
  items: GsoPRImportItemResponse[];
}

// ---------------------------------------------------------------------------
// Distribution
// ---------------------------------------------------------------------------

/** Mirrors ExistingDistributionDto */
export interface ExistingDistributionResponse {
  id: string;
  issueRef: string;
  division: string;
  qtyIssued: number;
  dateIssued: string;
  issuedBy: string;
  remarks: string | null;
}

/** Mirrors DeliveryItemBreakdownDto */
export interface DeliveryItemBreakdownResponse {
  deliveryItemId: string;
  deliveryRef: string;
  deliveryDate: string;
  prId: string;
  prNo: string;
  qtyDelivered: number;
  qtyDistributed: number;
  qtyAvailable: number;
  distributions: ExistingDistributionResponse[];
}

/** Mirrors ItemDistributionSummaryDto — returned by GET /api/distributions/item/{stockNo} */
export interface ItemDistributionSummaryResponse {
  stockNo: string;
  description: string;
  category: string | null;
  unit: string;
  totalOrdered: number;
  totalDelivered: number;
  totalDistributed: number;
  onHand: number;
  deliveryItems: DeliveryItemBreakdownResponse[];
}

/** Mirrors DistributionSplitDto — one division's share of an item-level distribution request */
export interface DistributionSplitRequest {
  division: string;
  qtyIssued: number;
  dateIssued: string;   // "YYYY-MM-DD"
  issuedBy: string;
  remarks: string | null;
}

/**
 * Mirrors CreateItemDistributionDto — POST /api/distributions/item/{stockNo}/allocate.
 * The backend FIFO-allocates each split across the item's available delivery batches
 * (oldest DeliveryDate first) and returns one DistributionCreatedResponse per batch drawn from.
 */
export interface CreateItemDistributionRequest {
  splits: DistributionSplitRequest[];
}

/** Mirrors DistributionCreatedDto */
export interface DistributionCreatedResponse {
  id: string;
  issueRef: string;
  deliveryItemId: string;
  deliveryRef: string;
  prNo: string;
  stockNo: string;
  description: string;
  division: string;
  qtyIssued: number;
  dateIssued: string;
  issuedBy: string;
  remarks: string | null;
}

// ---------------------------------------------------------------------------
// Warehouse stock input — physical-count ledger (RAL-193)
// ---------------------------------------------------------------------------

/** Mirrors StockBalanceDto */
export interface StockBalanceResponse {
  id: string;
  stockNo: string;
  countedQty: number;
  systemOnHandAtEntry: number;
  varianceQty: number;
  effectiveDate: string; // "YYYY-MM-DD"
  reason: string | null;
  recordedByUserId: string;
  recordedByName: string | null;
  createdAt: string;
  updatedAt: string;
  /** True when this save also auto-created a new Items Master row (IsNewItem = true,
   * pending admin review) because the StockNo wasn't cataloged yet. */
  itemWasAutoCreated: boolean;
}

/**
 * Mirrors CreateStockBalanceDto. Description/unit/unitCost/itemType are only used when
 * stockNo isn't already in Items Master — the backend auto-creates the catalog entry
 * (IsNewItem = true, pending admin review) from these, mirroring Create PR's unknown-stock
 * handling. Ignored (the catalog's own values win) when stockNo is already known.
 */
export interface CreateStockBalanceRequest {
  stockNo: string;
  countedQty: number;
  effectiveDate: string; // "YYYY-MM-DD"
  reason: string | null;
  description: string | null;
  unit: string | null;
  unitCost: number | null;
  itemType: string | null;
}

/** Mirrors UpdateStockBalanceDto */
export interface UpdateStockBalanceRequest {
  countedQty: number | null;
  effectiveDate: string | null;
  reason: string | null;
}

/** Mirrors StockBalanceImportRowDto */
export interface StockBalanceImportRowResponse {
  rowNumber: number;
  stockNo: string | null;
  countedQty: number | null;
  effectiveDate: string | null;
  reason: string | null;
  description: string | null;
  unit: string | null;
  unitCost: number | null;
  itemType: string | null;
  error: string | null;
}

/** Mirrors StockBalanceImportPreviewDto — POST /api/inventory/stock-balances/import/preview */
export interface StockBalanceImportPreviewResponse {
  rows: StockBalanceImportRowResponse[];
}

/** Mirrors StockBalanceImportResultDto — POST /api/inventory/stock-balances/import/commit */
export interface StockBalanceImportResultResponse {
  inserted: number;
  updated: number;
  entries: StockBalanceResponse[];
}

/** Mirrors SystemOnHandDto — GET /api/inventory/stock-balances/system-on-hand?stockNo= */
export interface SystemOnHandResponse {
  stockNo: string;
  onHand: number;
}
