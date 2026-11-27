import { WRITE_ROLES } from '../core/models';
import { CrudConfig, Field } from '../shared/crud-config';

const code: Field = { key: 'code', label: 'f.code', type: 'text', required: true, maxlength: 30, lockedOnEdit: false };
const nameAr: Field = { key: 'nameAr', label: 'f.nameAr', type: 'text', required: true, maxlength: 200 };
const nameEn: Field = { key: 'nameEn', label: 'f.nameEn', type: 'text', required: true, maxlength: 200 };
const phone: Field = { key: 'phone', label: 'f.phone', type: 'text', maxlength: 30 };
const email: Field = { key: 'email', label: 'f.email', type: 'email', maxlength: 200, noColumn: true };
const address: Field = { key: 'address', label: 'f.address', type: 'textarea', maxlength: 300, noColumn: true };
const taxNumber: Field = { key: 'taxNumber', label: 'f.taxNumber', type: 'text', maxlength: 50, noColumn: true };
const currencyOpt = (nullable = true): Field => ({
  key: 'currencyId', label: 'f.currency', type: 'select', lookup: 'currencies', nullable, noColumn: true,
});

export const MASTER_CONFIGS: Record<string, CrudConfig> = {
  items: {
    title: 'nav.items', endpoint: 'items', writeRoles: WRITE_ROLES.items,
    defaults: { costPrice: 0, salePrice: 0, distributorPrice: null, weightKg: 0, reorderLevel: 0 },
    fields: [
      code, nameAr, nameEn,
      { key: 'unitId', label: 'f.unit', type: 'select', lookup: 'units', required: true },
      { key: 'categoryId', label: 'f.category', type: 'select', lookup: 'itemcategories', nullable: true },
      { key: 'barcode', label: 'f.barcode', type: 'text', maxlength: 50, noColumn: true },
      { key: 'costPrice', label: 'f.costPrice', type: 'number', step: '0.0001' },
      { key: 'salePrice', label: 'f.salePrice', type: 'number', step: '0.0001' },
      { key: 'distributorPrice', label: 'f.distributorPrice', type: 'number', step: '0.0001', nullable: true, noColumn: true },
      { key: 'weightKg', label: 'f.weightKg', type: 'number', step: '0.0001', noColumn: true },
      { key: 'reorderLevel', label: 'f.reorderLevel', type: 'number', step: '0.0001', noColumn: true },
    ],
  },
  categories: {
    title: 'nav.categories', endpoint: 'itemcategories', writeRoles: WRITE_ROLES.items,
    defaults: {}, fields: [nameAr, nameEn],
  },
  units: {
    title: 'nav.units', endpoint: 'units', writeRoles: WRITE_ROLES.items,
    defaults: {}, fields: [code, nameAr, nameEn],
  },
  warehouses: {
    title: 'nav.warehouses', endpoint: 'warehouses', writeRoles: WRITE_ROLES.warehouses,
    defaults: {}, fields: [code, nameAr, nameEn, address],
  },
  suppliers: {
    title: 'nav.suppliers', endpoint: 'suppliers', writeRoles: WRITE_ROLES.suppliers,
    defaults: {}, fields: [code, nameAr, nameEn, phone, email, taxNumber, currencyOpt(), address],
  },
  customers: {
    title: 'nav.customers', endpoint: 'customers', writeRoles: WRITE_ROLES.customers,
    defaults: { type: 'Retail', creditLimit: 0 },
    fields: [
      code, nameAr, nameEn,
      {
        key: 'type', label: 'f.type', type: 'select', required: true,
        options: [{ value: 'Retail', label: 'enum.Retail' }, { value: 'Distributor', label: 'enum.Distributor' }],
      },
      phone, email, taxNumber, currencyOpt(),
      { key: 'creditLimit', label: 'f.creditLimit', type: 'number', step: '0.0001', noColumn: true },
      address,
    ],
  },
  drivers: {
    title: 'nav.drivers', endpoint: 'drivers', writeRoles: WRITE_ROLES.fleet,
    defaults: {},
    fields: [
      { key: 'name', label: 'f.name', type: 'text', required: true, maxlength: 200 },
      phone,
      { key: 'licenseNumber', label: 'f.licenseNumber', type: 'text', maxlength: 50 },
    ],
  },
  vehicles: {
    title: 'nav.vehicles', endpoint: 'vehicles', writeRoles: WRITE_ROLES.fleet,
    defaults: { capacityKg: 0 },
    fields: [
      { key: 'plateNumber', label: 'f.plateNumber', type: 'text', required: true, maxlength: 20 },
      { key: 'description', label: 'f.description', type: 'text', maxlength: 200 },
      { key: 'capacityKg', label: 'f.capacityKg', type: 'number', step: '0.01' },
    ],
  },
  accounts: {
    title: 'nav.accounts', endpoint: 'accounts', writeRoles: WRITE_ROLES.accounting,
    defaults: { type: 'Asset', isPostable: true },
    fields: [
      { key: 'code', label: 'f.code', type: 'text', required: true, maxlength: 20 },
      nameAr, nameEn,
      {
        key: 'type', label: 'f.type', type: 'select', required: true,
        options: [
          { value: 'Asset', label: 'acct.Asset' }, { value: 'Liability', label: 'acct.Liability' }, { value: 'Equity', label: 'acct.Equity' },
          { value: 'Revenue', label: 'acct.Revenue' }, { value: 'Expense', label: 'acct.Expense' },
        ],
      },
      { key: 'parentId', label: 'acc.parent', type: 'select', lookup: 'accounts', nullable: true },
      { key: 'isPostable', label: 'acc.postable', type: 'checkbox' },
    ],
  },
  currencies: {
    title: 'nav.currencies', endpoint: 'currencies', writeRoles: WRITE_ROLES.currencies,
    defaults: { isLocal: false },
    fields: [
      { key: 'code', label: 'f.code', type: 'text', required: true, maxlength: 3, lockedOnEdit: true },
      nameAr, nameEn,
      { key: 'symbol', label: 'f.symbol', type: 'text', required: true, maxlength: 8 },
      { key: 'isLocal', label: 'f.isLocal', type: 'checkbox', lockedOnEdit: true },
    ],
  },
  rates: {
    title: 'nav.rates', endpoint: 'exchangerates', writeRoles: WRITE_ROLES.currencies,
    defaults: { date: new Date().toISOString().slice(0, 10), rate: 1 },
    fields: [
      { key: 'currencyId', label: 'f.currency', type: 'select', lookup: 'currencies', required: true },
      { key: 'date', label: 'f.date', type: 'date', required: true },
      { key: 'rate', label: 'f.rate', type: 'number', step: '0.0001', required: true },
    ],
  },
};
