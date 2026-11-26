import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Auth } from './core/auth';
import { authGuard, guestGuard } from './core/http';
import { Shell } from './layout/shell';
import { DashboardPage } from './pages/dashboard-page';
import { LoginPage } from './pages/login-page';
import { MASTER_CONFIGS } from './pages/master-configs';
import { AdjustmentsPage } from './pages/adjustments-page';
import { LowStockPage, StockBalancesPage, StockMovementsPage } from './pages/stock-reports';
import { TransfersPage } from './pages/transfers-page';
import { UsersPage } from './pages/users-page';
import { CrudPage } from './shared/crud-page';

const adminGuard: CanActivateFn = () => (inject(Auth).hasAnyRole(['Admin']) ? true : inject(Router).parseUrl('/'));

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
      { path: 'users', component: UsersPage, canActivate: [adminGuard] },
    ],
  },
  { path: '**', redirectTo: '' },
];
