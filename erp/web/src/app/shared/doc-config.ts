import { WRITE_ROLES } from '../core/models';

export type DocKind = 'order' | 'receipt' | 'invoice' | 'payment';

/** Named option sources the page resolves from cached lookups. */
export type OptionSource = 'suppliers' | 'warehouses' | 'currencies' | 'orders' | 'methods';

export interface DocField {
  key: string;
  label: string;
  type: 'date' | 'text' | 'number' | 'select';
  required?: boolean;
  step?: string;
  maxlength?: number;
  nullable?: boolean;
  options?: OptionSource;
  /** Extra reasons to disable the field besides the document not being a draft. */
  disabledWhen?: 'localCurrency' | 'orderLinked';
}

export interface LineCol {
  key: string;
  label: string;
  kind: 'item' | 'qty' | 'price' | 'readonly';
  /** Shown only once the document is no longer a draft (e.g. received / invoiced progress). */
  afterDraft?: boolean;
}

export type ListColKind = 'text' | 'date' | 'supplier' | 'warehouse' | 'currency' | 'amount' | 'status' | 'receiptState' | 'invoiceState' | 'method';

export interface ListCol {
  key: string;
  label: string;
  kind: ListColKind;
}

export interface DocConfig {
  kind: DocKind;
  title: string;
  newLabel: string;
  endpoint: string;
  writeRoles: readonly string[];
  fields: DocField[];
  /** Absent for single-record documents such as payments. */
  lineCols?: LineCol[];
  listCols: ListCol[];
  /** The document carries a currency + exchange rate. */
  hasRate: boolean;
  /** Receipts and invoices can be created against a confirmed purchase order. */
  hasOrderLink?: boolean;
  filters: ('supplier' | 'pending')[];
}

const supplier: DocField = { key: 'supplierId', label: 'f.supplier', type: 'select', options: 'suppliers', required: true, disabledWhen: 'orderLinked' };
const currency: DocField = { key: 'currencyId', label: 'f.currency', type: 'select', options: 'currencies', required: true, disabledWhen: 'orderLinked' };
const rate: DocField = { key: 'exchangeRate', label: 'pur.rate', type: 'number', step: '0.0001', disabledWhen: 'localCurrency' };
const date: DocField = { key: 'date', label: 'f.date', type: 'date', required: true };
const notes: DocField = { key: 'notes', label: 'inv.notes', type: 'text', maxlength: 500 };
const order: DocField = { key: 'purchaseOrderId', label: 'pur.order', type: 'select', options: 'orders', nullable: true };

export const DOC_CONFIGS: Record<DocKind, DocConfig> = {
  order: {
    kind: 'order', title: 'nav.purchaseOrders', newLabel: 'pur.newOrder', endpoint: 'purchaseorders', writeRoles: WRITE_ROLES.purchasing,
    hasRate: true, filters: ['supplier', 'pending'],
    fields: [date, { key: 'expectedDate', label: 'pur.expectedDate', type: 'date' }, supplier, currency, rate, notes],
    lineCols: [
      { key: 'itemId', label: 'f.item', kind: 'item' },
      { key: 'quantity', label: 'inv.quantity', kind: 'qty' },
      { key: 'unitPrice', label: 'pur.unitPrice', kind: 'price' },
      { key: 'receivedQuantity', label: 'pur.received', kind: 'readonly', afterDraft: true },
      { key: 'invoicedQuantity', label: 'pur.invoiced', kind: 'readonly', afterDraft: true },
    ],
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'receiptState', label: 'pur.receiptState', kind: 'receiptState' },
      { key: 'invoiceState', label: 'pur.invoiceState', kind: 'invoiceState' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  receipt: {
    kind: 'receipt', title: 'nav.goodsReceipts', newLabel: 'pur.newReceipt', endpoint: 'goodsreceipts', writeRoles: WRITE_ROLES.goodsReceipts,
    hasRate: true, hasOrderLink: true, filters: ['supplier'],
    fields: [date, order, supplier, { key: 'warehouseId', label: 'f.warehouse', type: 'select', options: 'warehouses', required: true }, currency, rate, notes],
    lineCols: [
      { key: 'itemId', label: 'f.item', kind: 'item' },
      { key: 'quantity', label: 'inv.quantity', kind: 'qty' },
      { key: 'unitPrice', label: 'pur.unitPrice', kind: 'price' },
    ],
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'warehouseId', label: 'f.warehouse', kind: 'warehouse' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  invoice: {
    kind: 'invoice', title: 'nav.purchaseInvoices', newLabel: 'pur.newInvoice', endpoint: 'purchaseinvoices', writeRoles: WRITE_ROLES.purchasing,
    hasRate: true, hasOrderLink: true, filters: ['supplier'],
    fields: [
      date, { key: 'dueDate', label: 'pur.dueDate', type: 'date' }, order, supplier,
      { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', type: 'text', maxlength: 50 }, currency, rate, notes,
    ],
    lineCols: [
      { key: 'itemId', label: 'f.item', kind: 'item' },
      { key: 'quantity', label: 'inv.quantity', kind: 'qty' },
      { key: 'unitPrice', label: 'pur.unitPrice', kind: 'price' },
    ],
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', kind: 'text' },
      { key: 'date', label: 'f.date', kind: 'date' }, { key: 'dueDate', label: 'pur.dueDate', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  payment: {
    kind: 'payment', title: 'nav.supplierPayments', newLabel: 'pur.newPayment', endpoint: 'supplierpayments', writeRoles: WRITE_ROLES.supplierPayments,
    hasRate: true, filters: ['supplier'],
    fields: [
      date, supplier, { key: 'amount', label: 'pur.amount', type: 'number', step: '0.0001', required: true }, currency, rate,
      { key: 'method', label: 'pur.method', type: 'select', options: 'methods', required: true },
      { key: 'reference', label: 'pur.reference', type: 'text', maxlength: 100 }, notes,
    ],
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'amount', label: 'pur.amount', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'method', label: 'pur.method', kind: 'method' },
      { key: 'reference', label: 'pur.reference', kind: 'text' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
};
