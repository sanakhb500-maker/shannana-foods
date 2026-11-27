import { WRITE_ROLES } from '../core/models';

export type DocKind =
  | 'order' | 'receipt' | 'invoice' | 'payment' | 'purchaseReturn'
  | 'salesOrder' | 'delivery' | 'salesInvoice' | 'salesReturn' | 'customerReceipt'
  | 'expenseInvoice' | 'expenseVoucher';

/** Named option sources the page resolves from cached lookups. */
export type OptionSource = 'suppliers' | 'customers' | 'warehouses' | 'currencies' | 'orders' | 'methods';

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
  kind: 'item' | 'qty' | 'price' | 'readonly' | 'account' | 'text' | 'amount';
  /** Shown only once the document is no longer a draft (e.g. received / invoiced progress). */
  afterDraft?: boolean;
}

export type ListColKind = 'text' | 'date' | 'supplier' | 'customer' | 'warehouse' | 'currency' | 'amount' | 'status' | 'progress' | 'method';

export interface ListCol {
  key: string;
  label: string;
  kind: ListColKind;
}

/** How a receipt/invoice/delivery links back to the order it fulfils. */
export interface OrderLinkConfig {
  endpoint: 'purchaseorders' | 'salesorders' | 'purchaseinvoices' | 'salesinvoices';
  idKey: 'purchaseOrderId' | 'salesOrderId' | 'purchaseInvoiceId' | 'salesInvoiceId';
  /** Absent when the link is only a reference (returns): the lines are then free to edit and carry no per-line link. */
  lineKey?: 'purchaseOrderLineId' | 'salesOrderLineId';
  /** Quantity still to process on an order line, for pre-filling a new document from the order. */
  remaining: (orderLine: Record<string, any>) => number;
}

/** A button on a confirmed order that starts a follow-on document pre-filled from it. */
export interface Shortcut {
  label: string;
  route: string;
  roles: readonly string[];
}

export interface DocConfig {
  kind: DocKind;
  title: string;
  newLabel: string;
  endpoint: string;
  writeRoles: readonly string[];
  /** The counterparty: supplier or customer (absent for documents with no counterparty, e.g. expense vouchers). */
  partyKey?: 'supplierId' | 'customerId';
  /** Lines are expense lines (account + amount) instead of item lines (item + quantity + price). */
  lineKind?: 'item' | 'expense';
  /** The document can be allocated to open invoices of its supplier / customer. */
  allocations?: 'supplier' | 'customer';
  /** i18n key of a note shown under the header. */
  hint?: string;
  /** Label of the "only open orders" filter. */
  pendingLabel?: string;
  fields: DocField[];
  /** Absent for single-record documents such as payments. */
  lineCols?: LineCol[];
  listCols: ListCol[];
  order?: OrderLinkConfig;
  shortcuts?: Shortcut[];
  /** Selling documents default the line price from the item's sale price. */
  priceFromItem?: boolean;
  /** Document-specific initial values and request fields. */
  blankExtras?: () => Record<string, unknown>;
  bodyExtras?: (doc: Record<string, any>) => Record<string, unknown>;
  filters: ('party' | 'pending')[];
}

const supplier: DocField = { key: 'supplierId', label: 'f.supplier', type: 'select', options: 'suppliers', required: true, disabledWhen: 'orderLinked' };
const customer: DocField = { key: 'customerId', label: 'f.customer', type: 'select', options: 'customers', required: true, disabledWhen: 'orderLinked' };
const currency: DocField = { key: 'currencyId', label: 'f.currency', type: 'select', options: 'currencies', required: true, disabledWhen: 'orderLinked' };
const rate: DocField = { key: 'exchangeRate', label: 'pur.rate', type: 'number', step: '0.0001', disabledWhen: 'localCurrency' };
const date: DocField = { key: 'date', label: 'f.date', type: 'date', required: true };
const notes: DocField = { key: 'notes', label: 'inv.notes', type: 'text', maxlength: 500 };
const purchaseOrder: DocField = { key: 'purchaseOrderId', label: 'pur.order', type: 'select', options: 'orders', nullable: true };
const salesOrder: DocField = { key: 'salesOrderId', label: 'sal.order', type: 'select', options: 'orders', nullable: true };

const purchaseLink: OrderLinkConfig = {
  endpoint: 'purchaseorders', idKey: 'purchaseOrderId', lineKey: 'purchaseOrderLineId', remaining: () => 0,
};
const salesLink: OrderLinkConfig = {
  endpoint: 'salesorders', idKey: 'salesOrderId', lineKey: 'salesOrderLineId', remaining: () => 0,
};

const purchaseInvoiceLink: OrderLinkConfig = { endpoint: 'purchaseinvoices', idKey: 'purchaseInvoiceId', remaining: (l) => l['quantity'] };
const salesInvoiceLink: OrderLinkConfig = { endpoint: 'salesinvoices', idKey: 'salesInvoiceId', remaining: (l) => l['quantity'] };
const purchaseInvoiceField: DocField = { key: 'purchaseInvoiceId', label: 'pur.linkedInvoice', type: 'select', options: 'orders', nullable: true };
const salesInvoiceField: DocField = { key: 'salesInvoiceId', label: 'sal.linkedInvoice', type: 'select', options: 'orders', nullable: true };

const itemQtyPrice: LineCol[] = [
  { key: 'itemId', label: 'f.item', kind: 'item' },
  { key: 'quantity', label: 'inv.quantity', kind: 'qty' },
  { key: 'unitPrice', label: 'pur.unitPrice', kind: 'price' },
];

const txt = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : null);

export const DOC_CONFIGS: Record<DocKind, DocConfig> = {
  // ---------------- purchasing ----------------
  order: {
    kind: 'order', title: 'nav.purchaseOrders', newLabel: 'pur.newOrder', endpoint: 'purchaseorders', writeRoles: WRITE_ROLES.purchasing,
    partyKey: 'supplierId', pendingLabel: 'pur.awaitingReceipt', filters: ['party', 'pending'],
    fields: [date, { key: 'expectedDate', label: 'pur.expectedDate', type: 'date' }, supplier, currency, rate, notes],
    lineCols: [
      ...itemQtyPrice,
      { key: 'receivedQuantity', label: 'pur.received', kind: 'readonly', afterDraft: true },
      { key: 'invoicedQuantity', label: 'pur.invoiced', kind: 'readonly', afterDraft: true },
    ],
    shortcuts: [
      { label: 'pur.createReceipt', route: '/goods-receipts', roles: WRITE_ROLES.goodsReceipts },
      { label: 'pur.createInvoice', route: '/purchase-invoices', roles: WRITE_ROLES.purchasing },
    ],
    blankExtras: () => ({ expectedDate: null }),
    bodyExtras: (d) => ({ expectedDate: d['expectedDate'] || null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'receiptState', label: 'pur.receiptState', kind: 'progress' },
      { key: 'invoiceState', label: 'pur.invoiceState', kind: 'progress' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  receipt: {
    kind: 'receipt', title: 'nav.goodsReceipts', newLabel: 'pur.newReceipt', endpoint: 'goodsreceipts', writeRoles: WRITE_ROLES.goodsReceipts,
    partyKey: 'supplierId', filters: ['party'],
    order: { ...purchaseLink, remaining: (l) => l['quantity'] - l['receivedQuantity'] },
    fields: [date, purchaseOrder, supplier, { key: 'warehouseId', label: 'f.warehouse', type: 'select', options: 'warehouses', required: true }, currency, rate, notes],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ warehouseId: null, purchaseOrderId: null }),
    bodyExtras: (d) => ({ warehouseId: d['warehouseId'] ?? 0, purchaseOrderId: d['purchaseOrderId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'warehouseId', label: 'f.warehouse', kind: 'warehouse' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  invoice: {
    kind: 'invoice', title: 'nav.purchaseInvoices', newLabel: 'pur.newInvoice', endpoint: 'purchaseinvoices', writeRoles: WRITE_ROLES.purchasing,
    partyKey: 'supplierId', filters: ['party'], hint: 'pur.varianceHint',
    shortcuts: [{ label: 'pur.createReturn', route: '/purchase-returns', roles: WRITE_ROLES.purchasing }],
    order: { ...purchaseLink, remaining: (l) => l['receivedQuantity'] - l['invoicedQuantity'] },
    fields: [
      date, { key: 'dueDate', label: 'pur.dueDate', type: 'date' }, purchaseOrder, supplier,
      { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', type: 'text', maxlength: 50 }, currency, rate, notes,
    ],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ dueDate: null, supplierInvoiceNumber: null, purchaseOrderId: null }),
    bodyExtras: (d) => ({ dueDate: d['dueDate'] || null, supplierInvoiceNumber: txt(d['supplierInvoiceNumber']), purchaseOrderId: d['purchaseOrderId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', kind: 'text' },
      { key: 'date', label: 'f.date', kind: 'date' }, { key: 'dueDate', label: 'pur.dueDate', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  purchaseReturn: {
    kind: 'purchaseReturn', title: 'nav.purchaseReturns', newLabel: 'pur.newReturn', endpoint: 'purchasereturns', writeRoles: WRITE_ROLES.purchasing,
    partyKey: 'supplierId', filters: ['party'], order: purchaseInvoiceLink,
    fields: [date, purchaseInvoiceField, supplier, { key: 'warehouseId', label: 'f.warehouse', type: 'select', options: 'warehouses', required: true }, currency, rate,
      { key: 'notes', label: 'sal.returnReason', type: 'text', maxlength: 500 }],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ warehouseId: null, purchaseInvoiceId: null }),
    bodyExtras: (d) => ({ warehouseId: d['warehouseId'] ?? 0, purchaseInvoiceId: d['purchaseInvoiceId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'warehouseId', label: 'f.warehouse', kind: 'warehouse' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  payment: {
    kind: 'payment', title: 'nav.supplierPayments', newLabel: 'pur.newPayment', endpoint: 'supplierpayments', writeRoles: WRITE_ROLES.supplierPayments,
    partyKey: 'supplierId', filters: ['party'], allocations: 'supplier',
    fields: [
      date, supplier, { key: 'amount', label: 'pur.amount', type: 'number', step: '0.0001', required: true }, currency, rate,
      { key: 'method', label: 'pur.method', type: 'select', options: 'methods', required: true },
      { key: 'reference', label: 'pur.reference', type: 'text', maxlength: 100 }, notes,
    ],
    blankExtras: () => ({ amount: null, method: 'Cash', reference: null }),
    bodyExtras: (d) => ({ amount: Number(d['amount'] ?? 0), method: d['method'], reference: txt(d['reference']) }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'amount', label: 'pur.amount', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'method', label: 'pur.method', kind: 'method' },
      { key: 'reference', label: 'pur.reference', kind: 'text' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },

  // ---------------- sales ----------------
  salesOrder: {
    kind: 'salesOrder', title: 'nav.salesOrders', newLabel: 'sal.newOrder', endpoint: 'salesorders', writeRoles: WRITE_ROLES.sales,
    partyKey: 'customerId', pendingLabel: 'sal.awaitingDelivery', filters: ['party', 'pending'], priceFromItem: true,
    fields: [date, { key: 'requiredDate', label: 'sal.requiredDate', type: 'date' }, customer, currency, rate, notes],
    lineCols: [
      ...itemQtyPrice,
      { key: 'deliveredQuantity', label: 'sal.delivered', kind: 'readonly', afterDraft: true },
      { key: 'invoicedQuantity', label: 'pur.invoiced', kind: 'readonly', afterDraft: true },
    ],
    shortcuts: [
      { label: 'sal.createDelivery', route: '/deliveries', roles: WRITE_ROLES.deliveries },
      { label: 'pur.createInvoice', route: '/sales-invoices', roles: WRITE_ROLES.sales },
    ],
    blankExtras: () => ({ requiredDate: null }),
    bodyExtras: (d) => ({ requiredDate: d['requiredDate'] || null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'customerId', label: 'f.customer', kind: 'customer' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'deliveryState', label: 'sal.deliveryState', kind: 'progress' },
      { key: 'invoiceState', label: 'pur.invoiceState', kind: 'progress' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  delivery: {
    kind: 'delivery', title: 'nav.deliveries', newLabel: 'sal.newDelivery', endpoint: 'deliverynotes', writeRoles: WRITE_ROLES.deliveries,
    partyKey: 'customerId', filters: ['party'], priceFromItem: true,
    order: { ...salesLink, remaining: (l) => l['quantity'] - l['deliveredQuantity'] },
    fields: [date, salesOrder, customer, { key: 'warehouseId', label: 'f.warehouse', type: 'select', options: 'warehouses', required: true }, currency, rate, notes],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ warehouseId: null, salesOrderId: null }),
    bodyExtras: (d) => ({ warehouseId: d['warehouseId'] ?? 0, salesOrderId: d['salesOrderId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'customerId', label: 'f.customer', kind: 'customer' }, { key: 'warehouseId', label: 'f.warehouse', kind: 'warehouse' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'tripNumber', label: 'sal.trip', kind: 'text' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  salesInvoice: {
    kind: 'salesInvoice', title: 'nav.salesInvoices', newLabel: 'sal.newInvoice', endpoint: 'salesinvoices', writeRoles: WRITE_ROLES.sales,
    partyKey: 'customerId', filters: ['party'], priceFromItem: true,
    shortcuts: [{ label: 'sal.createReturn', route: '/sales-returns', roles: WRITE_ROLES.deliveries }],
    order: { ...salesLink, remaining: (l) => l['deliveredQuantity'] - l['invoicedQuantity'] },
    fields: [date, { key: 'dueDate', label: 'pur.dueDate', type: 'date' }, salesOrder, customer, currency, rate, notes],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ dueDate: null, salesOrderId: null }),
    bodyExtras: (d) => ({ dueDate: d['dueDate'] || null, salesOrderId: d['salesOrderId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' }, { key: 'dueDate', label: 'pur.dueDate', kind: 'date' },
      { key: 'customerId', label: 'f.customer', kind: 'customer' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  salesReturn: {
    kind: 'salesReturn', title: 'nav.salesReturns', newLabel: 'sal.newReturn', endpoint: 'salesreturns', writeRoles: WRITE_ROLES.deliveries,
    partyKey: 'customerId', filters: ['party'], priceFromItem: true, order: salesInvoiceLink,
    fields: [date, salesInvoiceField, customer, { key: 'warehouseId', label: 'f.warehouse', type: 'select', options: 'warehouses', required: true }, currency, rate,
      { key: 'notes', label: 'sal.returnReason', type: 'text', maxlength: 500 }],
    lineCols: itemQtyPrice,
    blankExtras: () => ({ warehouseId: null, salesInvoiceId: null }),
    bodyExtras: (d) => ({ warehouseId: d['warehouseId'] ?? 0, salesInvoiceId: d['salesInvoiceId'] ?? null }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'customerId', label: 'f.customer', kind: 'customer' }, { key: 'warehouseId', label: 'f.warehouse', kind: 'warehouse' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  customerReceipt: {
    kind: 'customerReceipt', title: 'nav.customerReceipts', newLabel: 'sal.newReceipt', endpoint: 'customerreceipts', writeRoles: WRITE_ROLES.customerReceipts,
    partyKey: 'customerId', filters: ['party'], allocations: 'customer',
    fields: [
      date, customer, { key: 'amount', label: 'pur.amount', type: 'number', step: '0.0001', required: true }, currency, rate,
      { key: 'method', label: 'pur.method', type: 'select', options: 'methods', required: true },
      { key: 'reference', label: 'pur.reference', type: 'text', maxlength: 100 }, notes,
    ],
    blankExtras: () => ({ amount: null, method: 'Cash', reference: null }),
    bodyExtras: (d) => ({ amount: Number(d['amount'] ?? 0), method: d['method'], reference: txt(d['reference']) }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' },
      { key: 'customerId', label: 'f.customer', kind: 'customer' }, { key: 'amount', label: 'pur.amount', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'method', label: 'pur.method', kind: 'method' },
      { key: 'reference', label: 'pur.reference', kind: 'text' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },

  // ---------------- expenses ----------------
  expenseInvoice: {
    kind: 'expenseInvoice', title: 'nav.expenseInvoices', newLabel: 'exp.newInvoice', endpoint: 'expenseinvoices', writeRoles: WRITE_ROLES.expenseInvoices,
    partyKey: 'supplierId', lineKind: 'expense', filters: ['party'],
    fields: [date, { key: 'dueDate', label: 'pur.dueDate', type: 'date' }, supplier, { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', type: 'text', maxlength: 50 }, currency, rate, notes],
    lineCols: [
      { key: 'accountId', label: 'acc.account', kind: 'account' },
      { key: 'description', label: 'f.description', kind: 'text' },
      { key: 'amount', label: 'pur.amount', kind: 'amount' },
    ],
    blankExtras: () => ({ dueDate: null, supplierInvoiceNumber: null }),
    bodyExtras: (d) => ({ dueDate: d['dueDate'] || null, supplierInvoiceNumber: txt(d['supplierInvoiceNumber']) }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'supplierInvoiceNumber', label: 'pur.supplierInvoiceNo', kind: 'text' },
      { key: 'date', label: 'f.date', kind: 'date' }, { key: 'dueDate', label: 'pur.dueDate', kind: 'date' },
      { key: 'supplierId', label: 'f.supplier', kind: 'supplier' }, { key: 'totalAmount', label: 'pur.total', kind: 'amount' },
      { key: 'currencyId', label: 'f.currency', kind: 'currency' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
  expenseVoucher: {
    kind: 'expenseVoucher', title: 'nav.expenseVouchers', newLabel: 'exp.newVoucher', endpoint: 'expensevouchers', writeRoles: WRITE_ROLES.accounting,
    lineKind: 'expense', filters: [],
    fields: [
      date, currency, rate, { key: 'method', label: 'pur.method', type: 'select', options: 'methods', required: true },
      { key: 'payee', label: 'exp.payee', type: 'text', maxlength: 200 }, notes,
    ],
    lineCols: [
      { key: 'accountId', label: 'acc.account', kind: 'account' },
      { key: 'description', label: 'f.description', kind: 'text' },
      { key: 'amount', label: 'pur.amount', kind: 'amount' },
    ],
    blankExtras: () => ({ method: 'Cash', payee: null }),
    bodyExtras: (d) => ({ method: d['method'], payee: txt(d['payee']) }),
    listCols: [
      { key: 'number', label: 'inv.number', kind: 'text' }, { key: 'date', label: 'f.date', kind: 'date' }, { key: 'payee', label: 'exp.payee', kind: 'text' },
      { key: 'totalAmount', label: 'pur.total', kind: 'amount' }, { key: 'currencyId', label: 'f.currency', kind: 'currency' },
      { key: 'method', label: 'pur.method', kind: 'method' }, { key: 'status', label: 'common.status', kind: 'status' },
    ],
  },
};
