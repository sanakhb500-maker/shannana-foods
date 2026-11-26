export interface ApiEnvelope<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: Record<string, string[]> | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Session {
  id: number;
  email: string;
  fullName: string;
  roles: string[];
  csrfToken: string;
}

export interface UserDto {
  id: number;
  email: string;
  fullName: string;
  isActive: boolean;
  roles: string[];
}

/** Roles, mirrored from Erp.Domain.Common.Roles. */
export const ROLES = ['Admin', 'Accountant', 'StoreKeeper', 'Purchasing', 'Sales', 'Dispatcher'] as const;
export type Role = (typeof ROLES)[number];

/** Who may write each area — mirrors Erp.Domain.Common.Policies (the API is the real enforcement). */
export const WRITE_ROLES = {
  users: ['Admin'],
  currencies: ['Admin', 'Accountant'],
  items: ['Admin', 'StoreKeeper', 'Purchasing'],
  warehouses: ['Admin', 'StoreKeeper'],
  suppliers: ['Admin', 'Purchasing', 'Accountant'],
  customers: ['Admin', 'Sales', 'Accountant'],
  fleet: ['Admin', 'Dispatcher'],
  inventory: ['Admin', 'StoreKeeper'],
} as const satisfies Record<string, readonly string[]>;

// ---------- inventory ----------
export type DocStatus = 'Draft' | 'Confirmed' | 'Cancelled';
export type AdjustmentReason = 'Count' | 'Opening' | 'Damage' | 'Other';

export interface LookupItem { id: number; code: string; nameAr: string; nameEn: string; isActive: boolean; reorderLevel?: number }
export interface LookupWarehouse { id: number; code: string; nameAr: string; nameEn: string; isActive: boolean }

export interface BalanceRow {
  itemId: number; itemCode: string; itemNameAr: string; itemNameEn: string;
  warehouseId: number; warehouseCode: string; warehouseNameAr: string; warehouseNameEn: string;
  quantity: number; averageCost: number; value: number;
}
export interface BalancesResult { items: BalanceRow[]; total: number; page: number; pageSize: number; totalValue: number }

export interface MovementRow {
  id: number; date: string; itemId: number; itemCode: string; itemNameAr: string; itemNameEn: string;
  warehouseId: number; warehouseCode: string; warehouseNameAr: string; warehouseNameEn: string;
  quantity: number; unitCost: number; value: number; type: string;
  documentType: string; documentId: number; documentNumber: string | null; createdAt: string; createdBy: string | null;
}

export interface LowStockRow { itemId: number; itemCode: string; itemNameAr: string; itemNameEn: string; reorderLevel: number; totalQuantity: number }

export interface TransferRow { id: number; number: string | null; date: string; fromWarehouseId: number; toWarehouseId: number; status: DocStatus; notes: string | null; lineCount: number }
export interface TransferLine { itemId: number; quantity: number | null }
export interface Transfer { id?: number; number?: string | null; date: string; fromWarehouseId: number | null; toWarehouseId: number | null; status: DocStatus; notes: string | null; lines: TransferLine[] }

export interface AdjustmentRow { id: number; number: string | null; date: string; warehouseId: number; reason: AdjustmentReason; status: DocStatus; notes: string | null; lineCount: number }
export interface AdjustmentLine { itemId: number; countedQuantity: number | null; unitCost: number | null; systemQuantity?: number | null; difference?: number | null }
export interface Adjustment { id?: number; number?: string | null; date: string; warehouseId: number | null; reason: AdjustmentReason; status: DocStatus; notes: string | null; lines: AdjustmentLine[] }
