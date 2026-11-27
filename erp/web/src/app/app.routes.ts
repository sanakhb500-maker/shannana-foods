import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Auth } from './core/auth';
import { authGuard, guestGuard } from './core/http';
import { WRITE_ROLES } from './core/models';
import { Shell } from './layout/shell';
import { MASTER_CONFIGS } from './pages/master-configs';

/** Redirects to the home page unless the user has one of the roles. The API enforces the same rules. */
const rolesGuard = (roles: readonly string[]): CanActivateFn => () =>
  inject(Auth).hasAnyRole(roles) ? true : inject(Router).parseUrl('/');

const adminGuard = rolesGuard(['Admin']);
const purchasingGuard = rolesGuard(WRITE_ROLES.viewPurchasing); // cost prices live here
const salesGuard = rolesGuard(WRITE_ROLES.viewSales);
const accountingGuard = rolesGuard(WRITE_ROLES.accounting);

const doc = (path: string, kind: string, canActivate: CanActivateFn[]) => ({
  path,
  loadComponent: () => import('./shared/doc-page').then((m) => m.DocPage),
  data: { kind },
  canActivate,
});

const masterRoutes: Routes = Object.entries(MASTER_CONFIGS).map(([path, config]) => ({
  path,
  loadComponent: () => import('./shared/crud-page').then((m) => m.CrudPage),
  data: { config },
}));

// Every screen except the shell is loaded on demand, so the first download stays small.
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login-page').then((m) => m.LoginPage), canActivate: [guestGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', loadComponent: () => import('./pages/dashboard-page').then((m) => m.DashboardPage) },
      ...masterRoutes,

      // inventory
      { path: 'stock', loadComponent: () => import('./pages/stock-reports').then((m) => m.StockBalancesPage) },
      { path: 'stock-movements', loadComponent: () => import('./pages/stock-reports').then((m) => m.StockMovementsPage) },
      { path: 'low-stock', loadComponent: () => import('./pages/stock-reports').then((m) => m.LowStockPage) },
      { path: 'transfers', loadComponent: () => import('./pages/transfers-page').then((m) => m.TransfersPage) },
      { path: 'adjustments', loadComponent: () => import('./pages/adjustments-page').then((m) => m.AdjustmentsPage) },

      // purchasing
      doc('purchase-orders', 'order', [purchasingGuard]),
      doc('goods-receipts', 'receipt', [purchasingGuard]),
      doc('purchase-invoices', 'invoice', [purchasingGuard]),
      doc('supplier-payments', 'payment', [purchasingGuard]),
      doc('expense-invoices', 'expenseInvoice', [purchasingGuard]),
      { path: 'supplier-ageing', loadComponent: () => import('./pages/ageing-page').then((m) => m.AgeingPage), data: { side: 'supplier' }, canActivate: [purchasingGuard] },
      { path: 'supplier-statement', loadComponent: () => import('./pages/purchasing-reports').then((m) => m.SupplierStatementPage), canActivate: [purchasingGuard] },
      { path: 'supplier-balances', loadComponent: () => import('./pages/purchasing-reports').then((m) => m.SupplierBalancesPage), canActivate: [purchasingGuard] },

      // sales and distribution
      doc('sales-orders', 'salesOrder', [salesGuard]),
      doc('deliveries', 'delivery', [salesGuard]),
      { path: 'trips', loadComponent: () => import('./pages/trips-page').then((m) => m.TripsPage), canActivate: [salesGuard] },
      doc('sales-invoices', 'salesInvoice', [salesGuard]),
      doc('sales-returns', 'salesReturn', [salesGuard]),
      doc('customer-receipts', 'customerReceipt', [salesGuard]),
      { path: 'customer-ageing', loadComponent: () => import('./pages/ageing-page').then((m) => m.AgeingPage), data: { side: 'customer' }, canActivate: [salesGuard] },
      { path: 'customer-statement', loadComponent: () => import('./pages/sales-reports').then((m) => m.CustomerStatementPage), canActivate: [salesGuard] },
      { path: 'customer-balances', loadComponent: () => import('./pages/sales-reports').then((m) => m.CustomerBalancesPage), canActivate: [salesGuard] },

      // accounting
      { path: 'journal', loadComponent: () => import('./pages/journal-page').then((m) => m.JournalPage), canActivate: [accountingGuard] },
      doc('expense-vouchers', 'expenseVoucher', [accountingGuard]),
      { path: 'revaluation', loadComponent: () => import('./pages/revaluation-page').then((m) => m.RevaluationPage), canActivate: [accountingGuard] },
      { path: 'ledger', loadComponent: () => import('./pages/accounting-reports').then((m) => m.LedgerPage), canActivate: [accountingGuard] },
      { path: 'trial-balance', loadComponent: () => import('./pages/accounting-reports').then((m) => m.TrialBalancePage), canActivate: [accountingGuard] },
      { path: 'income-statement', loadComponent: () => import('./pages/accounting-reports').then((m) => m.IncomeStatementPage), canActivate: [accountingGuard] },
      { path: 'balance-sheet', loadComponent: () => import('./pages/accounting-reports').then((m) => m.BalanceSheetPage), canActivate: [accountingGuard] },
      { path: 'reconciliation', loadComponent: () => import('./pages/accounting-reports').then((m) => m.ReconciliationPage), canActivate: [accountingGuard] },
      { path: 'accounting-settings', loadComponent: () => import('./pages/accounting-reports').then((m) => m.AccountingSettingsPage), canActivate: [accountingGuard] },

      { path: 'users', loadComponent: () => import('./pages/users-page').then((m) => m.UsersPage), canActivate: [adminGuard] },
    ],
  },
  { path: '**', redirectTo: '' },
];
