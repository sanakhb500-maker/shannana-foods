import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Auth } from './core/auth';
import { WRITE_ROLES } from './core/models';
import { authGuard, guestGuard } from './core/http';
import { Shell } from './layout/shell';
import { DashboardPage } from './pages/dashboard-page';
import { LoginPage } from './pages/login-page';
import { MASTER_CONFIGS } from './pages/master-configs';
import { AdjustmentsPage } from './pages/adjustments-page';
import { SupplierBalancesPage, SupplierStatementPage } from './pages/purchasing-reports';
import { CustomerBalancesPage, CustomerStatementPage } from './pages/sales-reports';
import { TripsPage } from './pages/trips-page';
import { DocPage } from './shared/doc-page';
import { LowStockPage, StockBalancesPage, StockMovementsPage } from './pages/stock-reports';
import { TransfersPage } from './pages/transfers-page';
import { UsersPage } from './pages/users-page';
import { CrudPage } from './shared/crud-page';

const adminGuard: CanActivateFn = () => (inject(Auth).hasAnyRole(['Admin']) ? true : inject(Router).parseUrl('/'));

// Purchasing data holds cost prices: only these roles may open it (the API enforces the same rule).
const purchasingGuard: CanActivateFn = () =>
  inject(Auth).hasAnyRole(WRITE_ROLES.viewPurchasing) ? true : inject(Router).parseUrl('/');

const salesGuard: CanActivateFn = () =>
  inject(Auth).hasAnyRole(WRITE_ROLES.viewSales) ? true : inject(Router).parseUrl('/');

const masterRoutes: Routes = Object.entries(MASTER_CONFIGS).map(([path, config]) => ({
  path,
  component: CrudPage,
  data: { config },
}));

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [guestGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', component: DashboardPage },
      ...masterRoutes,
      { path: 'stock', component: StockBalancesPage },
      { path: 'stock-movements', component: StockMovementsPage },
      { path: 'low-stock', component: LowStockPage },
      { path: 'transfers', component: TransfersPage },
      { path: 'adjustments', component: AdjustmentsPage },
      { path: 'purchase-orders', component: DocPage, data: { kind: 'order' }, canActivate: [purchasingGuard] },
      { path: 'goods-receipts', component: DocPage, data: { kind: 'receipt' }, canActivate: [purchasingGuard] },
      { path: 'purchase-invoices', component: DocPage, data: { kind: 'invoice' }, canActivate: [purchasingGuard] },
      { path: 'supplier-payments', component: DocPage, data: { kind: 'payment' }, canActivate: [purchasingGuard] },
      { path: 'supplier-statement', component: SupplierStatementPage, canActivate: [purchasingGuard] },
      { path: 'supplier-balances', component: SupplierBalancesPage, canActivate: [purchasingGuard] },
      { path: 'sales-orders', component: DocPage, data: { kind: 'salesOrder' }, canActivate: [salesGuard] },
      { path: 'deliveries', component: DocPage, data: { kind: 'delivery' }, canActivate: [salesGuard] },
      { path: 'trips', component: TripsPage, canActivate: [salesGuard] },
      { path: 'sales-invoices', component: DocPage, data: { kind: 'salesInvoice' }, canActivate: [salesGuard] },
      { path: 'sales-returns', component: DocPage, data: { kind: 'salesReturn' }, canActivate: [salesGuard] },
      { path: 'customer-receipts', component: DocPage, data: { kind: 'customerReceipt' }, canActivate: [salesGuard] },
      { path: 'customer-statement', component: CustomerStatementPage, canActivate: [salesGuard] },
      { path: 'customer-balances', component: CustomerBalancesPage, canActivate: [salesGuard] },
      { path: 'users', component: UsersPage, canActivate: [adminGuard] },
    ],
  },
  { path: '**', redirectTo: '' },
];
