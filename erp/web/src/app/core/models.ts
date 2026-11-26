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
  purchasing: ['Admin', 'Purchasing'],
  goodsReceipts: ['Admin', 'Purchasing', 'StoreKeeper'],
  supplierPayments: ['Admin', 'Accountant'],
  viewPurchasing: ['Admin', 'Accountant', 'Purchasing', 'StoreKeeper'],
  sales: ['Admin', 'Sales'],
  deliveries: ['Admin', 'Sales', 'StoreKeeper'],
  trips: ['Admin', 'Dispatcher'],
  customerReceipts: ['Admin', 'Accountant'],
  viewSales: ['Admin', 'Accountant', 'Sales', 'Dispatcher', 'StoreKeeper'],
  accounting: ['Admin', 'Accountant'],
} as const satisfies Record<string, readonly string[]>;

// ---------- inventory ----------
export type DocStatus = 'Draft' | 'Confirmed' | 'Cancelled';
export type AdjustmentReason = 'Count' | 'Opening' | 'Damage' | 'Other';

export interface LookupItem { id: number; code: string; nameAr: string; nameEn: string; isActive: boolean; reorderLevel?: number; salePrice?: number }
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

// ---------- purchasing ----------
export interface LookupSupplier { id: number; code: string; nameAr: string; nameEn: string; isActive: boolean; currencyId?: number | null }
export interface LookupCurrency { id: number; code: string; nameAr: string; nameEn: string; symbol: string; isLocal: boolean; isActive: boolean }
export type ProgressState = 'None' | 'Partial' | 'Full';

export interface SupplierBalanceRow { supplierId: number; code: string; nameAr: string; nameEn: string; invoiced: number; paid: number; balance: number }
export interface StatementRow { date: string; type: 'Invoice' | 'Payment'; number: string | null; reference: string | null; currencyId: number; amount: number; exchangeRate: number; debit: number; credit: number; balance: number }
export interface SupplierStatement { supplierId: number; from: string | null; to: string | null; openingBalance: number; rows: StatementRow[]; totalInvoiced: number; totalPaid: number; closingBalance: number }

// ---------- sales and distribution ----------
export interface LookupCustomer { id: number; code: string; nameAr: string; nameEn: string; isActive: boolean; currencyId?: number | null; creditLimit?: number }
export interface LookupDriver { id: number; name: string; isActive: boolean }
export interface LookupVehicle { id: number; plateNumber: string; description?: string | null; isActive: boolean }

export type TripStatus = 'Planned' | 'InTransit' | 'Completed' | 'Cancelled';
export type StopStatus = 'Pending' | 'Delivered' | 'Failed';

export interface TripRow { id: number; number: string | null; date: string; driverId: number; vehicleId: number; status: TripStatus; stopCount: number; deliveredCount: number }
export interface TripStop { id?: number; sequence: number; status: StopStatus; notes: string | null; deliveryNoteId: number; deliveryNumber: string | null; customerId: number; totalAmount: number; currencyId: number; deliveryStatus: DocStatus }
export interface Trip { id?: number; number?: string | null; date: string; driverId: number | null; vehicleId: number | null; status: TripStatus; notes: string | null; startedAt?: string | null; completedAt?: string | null; stops: TripStop[] }
export interface DeliveryRow { id: number; number: string | null; customerId: number; totalAmount: number; currencyId: number; status: DocStatus; tripNumber: string | null }

export interface CustomerBalanceRow { customerId: number; code: string; nameAr: string; nameEn: string; invoiced: number; returned: number; received: number; balance: number; creditLimit: number }

// ---------- accounting ----------
export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Revenue' | 'Expense';
export type JournalStatus = 'Draft' | 'Posted' | 'Reversed';

export interface AccountDto { id: number; code: string; nameAr: string; nameEn: string; type: AccountType; parentId: number | null; isPostable: boolean; systemRole: string | null; isActive: boolean }

export interface JournalRow { id: number; number: string; date: string; description: string; sourceType: string; sourceId: number | null; status: JournalStatus; total: number }
export interface JournalLine { id?: number; accountId: number; debit: number | null; credit: number | null; description: string | null; partyType?: string; partyId?: number | null }
export interface JournalEntry { id?: number; number?: string; date: string; description: string; sourceType?: string; sourceId?: number | null; status: JournalStatus; reversalOfId?: number | null; lines: JournalLine[] }

export interface TrialBalanceRow { accountId: number; code: string; nameAr: string; nameEn: string; type: AccountType; opening: number; debit: number; credit: number; closing: number }
export interface TrialBalance { rows: TrialBalanceRow[]; totalDebit: number; totalCredit: number; closingDebit: number; closingCredit: number; isBalanced: boolean }

export interface LedgerRow { date: string; entryId: number; entryNumber: string; description: string; sourceType: string; debit: number; credit: number; balance: number }
export interface Ledger { accountId: number; code: string; nameAr: string; nameEn: string; type: AccountType; opening: number; rows: LedgerRow[]; totalDebit: number; totalCredit: number; closing: number }

export interface StatementLine { accountId: number; code: string; nameAr: string; nameEn: string; amount: number }
export interface IncomeStatement { revenue: StatementLine[]; totalRevenue: number; expenses: StatementLine[]; totalExpenses: number; netProfit: number }
export interface BalanceSheet { assets: StatementLine[]; totalAssets: number; liabilities: StatementLine[]; totalLiabilities: number; equity: StatementLine[]; currentEarnings: number; totalEquity: number; isBalanced: boolean }

export interface ReconciliationRow { key: string; generalLedger: number; subLedger: number; difference: number; reconciled: boolean }
export interface MonthlyFigure { month: string; revenue: number; costOfGoodsSold: number; grossProfit: number }
export interface AccountingSummary { cashAndBank: number; receivables: number; payables: number; inventoryValue: number; monthRevenue: number; monthCostOfGoodsSold: number; monthGrossProfit: number; months: MonthlyFigure[]; lockDate: string | null }
