/** Mirrors PPDO.Application/DTOs/Items/ */

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
