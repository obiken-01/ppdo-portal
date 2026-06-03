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

/** Mirrors PRResponseDto */
export interface PRResponse {
  id: string;
  prNo: string;
  prDate: string;
  dateCreated: string;
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
  totalAmount: number;
  status: string;
  createdById: string;
  createdAt: string;
  updatedAt: string;
  items: PRItemResponse[];
}
