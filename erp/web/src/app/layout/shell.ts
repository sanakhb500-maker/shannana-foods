import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { WRITE_ROLES } from '../core/models';

interface NavItem {
  path: string;
  label: string;
  roles?: readonly string[];
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell" [class.nav-open]="navOpen()">
      <aside class="sidebar">
        <div class="brand">{{ i18n.t('app.name') }}</div>
        <nav>
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" (click)="navOpen.set(false)">{{ i18n.t('nav.dashboard') }}</a>

          <div class="nav-group">{{ i18n.t('nav.masterData') }}</div>
          @for (n of master; track n.path) {
            <a [routerLink]="n.path" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t(n.label) }}</a>
          }

          <div class="nav-group">{{ i18n.t('nav.inventory') }}</div>
          @for (n of inventory; track n.path) {
            <a [routerLink]="n.path" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t(n.label) }}</a>
          }

          @if (canViewPurchasing) {
            <div class="nav-group">{{ i18n.t('nav.purchasing') }}</div>
            @for (n of purchasing; track n.path) {
              <a [routerLink]="n.path" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t(n.label) }}</a>
            }
          }

          @if (canViewSales) {
            <div class="nav-group">{{ i18n.t('nav.sales') }}</div>
            @for (n of sales; track n.path) {
              <a [routerLink]="n.path" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t(n.label) }}</a>
            }
          }

          @if (auth.hasAnyRole(['Admin'])) {
            <div class="nav-group">{{ i18n.t('nav.admin') }}</div>
            <a routerLink="/users" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t('nav.users') }}</a>
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <button class="btn sm menu-btn" type="button" (click)="navOpen.set(!navOpen())" aria-label="menu">☰</button>
          <span class="spacer"></span>
          <button class="btn sm" type="button" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'العربية' }}</button>
          <button class="btn sm" type="button" (click)="toggleTheme()" [attr.aria-label]="i18n.t('common.theme')" [title]="i18n.t('common.theme')">{{ dark() ? '☀' : '☾' }}</button>
          <span class="user">{{ auth.session()?.fullName }}</span>
          <button class="btn sm" type="button" (click)="auth.logout()">{{ i18n.t('nav.logout') }}</button>
        </header>
        <main class="content"><router-outlet /></main>
      </div>
      <div class="scrim" (click)="navOpen.set(false)"></div>
    </div>
  `,
})
export class Shell {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly navOpen = signal(false);
  readonly dark = signal(document.documentElement.dataset['theme'] === 'dark');

  readonly master: NavItem[] = [
    { path: '/items', label: 'nav.items' },
    { path: '/categories', label: 'nav.categories' },
    { path: '/units', label: 'nav.units' },
    { path: '/warehouses', label: 'nav.warehouses' },
    { path: '/suppliers', label: 'nav.suppliers' },
    { path: '/customers', label: 'nav.customers' },
    { path: '/drivers', label: 'nav.drivers' },
    { path: '/vehicles', label: 'nav.vehicles' },
    { path: '/currencies', label: 'nav.currencies' },
    { path: '/rates', label: 'nav.rates' },
  ];

  readonly inventory: NavItem[] = [
    { path: '/stock', label: 'nav.stockBalances' },
    { path: '/stock-movements', label: 'nav.stockMovements' },
    { path: '/low-stock', label: 'nav.lowStock' },
    { path: '/transfers', label: 'nav.transfers' },
    { path: '/adjustments', label: 'nav.adjustments' },
  ];

  readonly canViewPurchasing = this.auth.hasAnyRole(WRITE_ROLES.viewPurchasing);

  readonly purchasing: NavItem[] = [
    { path: '/purchase-orders', label: 'nav.purchaseOrders' },
    { path: '/goods-receipts', label: 'nav.goodsReceipts' },
    { path: '/purchase-invoices', label: 'nav.purchaseInvoices' },
    { path: '/supplier-payments', label: 'nav.supplierPayments' },
    { path: '/supplier-statement', label: 'nav.supplierStatement' },
    { path: '/supplier-balances', label: 'nav.supplierBalances' },
  ];

  readonly canViewSales = this.auth.hasAnyRole(WRITE_ROLES.viewSales);

  readonly sales: NavItem[] = [
    { path: '/sales-orders', label: 'nav.salesOrders' },
    { path: '/deliveries', label: 'nav.deliveries' },
    { path: '/trips', label: 'nav.trips' },
    { path: '/sales-invoices', label: 'nav.salesInvoices' },
    { path: '/sales-returns', label: 'nav.salesReturns' },
    { path: '/customer-receipts', label: 'nav.customerReceipts' },
    { path: '/customer-statement', label: 'nav.customerStatement' },
    { path: '/customer-balances', label: 'nav.customerBalances' },
  ];

  toggleTheme(): void {
    const next = this.dark() ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = next;
    localStorage.setItem('erp.theme', next);
    this.dark.set(next === 'dark');
  }
}
