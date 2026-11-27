import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { WRITE_ROLES } from '../core/models';

interface NavItem {
  path: string;
  label: string;
  roles?: readonly string[];
}

interface NavGroup {
  key: string;
  label: string;
  items: NavItem[];
  visible: boolean;
}

const OPEN_KEY = 'erp.navOpen';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  template: `
    <div class="shell" [class.nav-open]="navOpen()">
      <aside class="sidebar">
        <div class="brand"><img src="shannana-logo.png" alt="" width="53" height="40" (error)="$any($event.target).remove()" />{{ i18n.t('app.name') }}</div>
        <div class="nav-search">
          <input class="input" type="search" name="navSearch" [placeholder]="i18n.t('nav.find')" [ngModel]="navQuery()" (ngModelChange)="navQuery.set($event ?? '')" />
        </div>
        <nav>
          @if (!navQuery().trim()) {
            <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" (click)="navOpen.set(false)">{{ i18n.t('nav.dashboard') }}</a>
          }
          @for (g of groupsView(); track g.key) {
            <button class="nav-group" type="button" [attr.aria-expanded]="g.open" (click)="toggle(g.key)">
              <span>{{ i18n.t(g.label) }}</span>
              <span class="chev" [class.open]="g.open" aria-hidden="true">▾</span>
            </button>
            @if (g.open) {
              @for (n of g.items; track n.path) {
                <a [routerLink]="n.path" routerLinkActive="active" (click)="navOpen.set(false)">{{ i18n.t(n.label) }}</a>
              }
            }
          } @empty {
            <p class="nav-empty">{{ i18n.t('common.empty') }}</p>
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
  private readonly router = inject(Router);
  readonly navQuery = signal('');
  /** Groups the user has expanded (remembered between visits); the group of the current screen always opens. */
  readonly openGroups = signal<string[]>(this.loadOpen());
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
    { path: '/purchase-returns', label: 'nav.purchaseReturns' },
    { path: '/supplier-payments', label: 'nav.supplierPayments' },
    { path: '/expense-invoices', label: 'nav.expenseInvoices' },
    { path: '/supplier-ageing', label: 'nav.supplierAgeing' },
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
    { path: '/customer-ageing', label: 'nav.customerAgeing' },
    { path: '/customer-statement', label: 'nav.customerStatement' },
    { path: '/customer-balances', label: 'nav.customerBalances' },
  ];

  readonly canViewAccounting = this.auth.hasAnyRole(WRITE_ROLES.accounting);

  readonly accounting: NavItem[] = [
    { path: '/accounts', label: 'nav.accounts' },
    { path: '/journal', label: 'nav.journal' },
    { path: '/expense-vouchers', label: 'nav.expenseVouchers' },
    { path: '/revaluation', label: 'nav.revaluation' },
    { path: '/ledger', label: 'nav.ledger' },
    { path: '/trial-balance', label: 'nav.trialBalance' },
    { path: '/income-statement', label: 'nav.incomeStatement' },
    { path: '/balance-sheet', label: 'nav.balanceSheet' },
    { path: '/reconciliation', label: 'nav.reconciliation' },
    { path: '/accounting-settings', label: 'nav.accountingSettings' },
  ];

  private readonly admin: NavItem[] = [{ path: '/users', label: 'nav.users' }];

  private get groups(): NavGroup[] {
    return [
      { key: 'master', label: 'nav.masterData', items: this.master, visible: true },
      { key: 'inventory', label: 'nav.inventory', items: this.inventory, visible: true },
      { key: 'purchasing', label: 'nav.purchasing', items: this.purchasing, visible: this.canViewPurchasing },
      { key: 'sales', label: 'nav.sales', items: this.sales, visible: this.canViewSales },
      { key: 'accounting', label: 'nav.accounting', items: this.accounting, visible: this.canViewAccounting },
      { key: 'admin', label: 'nav.admin', items: this.admin, visible: this.auth.hasAnyRole(['Admin']) },
    ];
  }

  constructor() {
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd), takeUntilDestroyed()).subscribe((e) => this.revealCurrent(e.urlAfterRedirects));
    this.revealCurrent(this.router.url);
  }

  private loadOpen(): string[] {
    try {
      const v = JSON.parse(localStorage.getItem(OPEN_KEY) ?? '[]');
      return Array.isArray(v) ? v : [];
    } catch {
      return [];
    }
  }

  private saveOpen(v: string[]): void {
    this.openGroups.set(v);
    localStorage.setItem(OPEN_KEY, JSON.stringify(v));
  }

  /** Makes sure the group holding the current screen is open, so the active link is never hidden. */
  private revealCurrent(url: string): void {
    const path = url.split('?')[0];
    const g = this.groups.find((x) => x.items.some((i) => path === i.path || path.startsWith(i.path + '/')));
    if (g && !this.openGroups().includes(g.key)) this.saveOpen([...this.openGroups(), g.key]);
  }

  toggle(key: string): void {
    const cur = this.openGroups();
    this.saveOpen(cur.includes(key) ? cur.filter((k) => k !== key) : [...cur, key]);
  }

  /** Groups to draw: only those the role may see; while searching, only matching links and every matching group open. */
  groupsView(): (NavGroup & { open: boolean })[] {
    const q = this.navQuery().trim().toLowerCase();
    return this.groups
      .filter((g) => g.visible)
      .map((g) => {
        const items = q ? g.items.filter((i) => this.i18n.t(i.label).toLowerCase().includes(q)) : g.items;
        return { ...g, items, open: q ? true : this.openGroups().includes(g.key) };
      })
      .filter((g) => g.items.length > 0);
  }

  toggleTheme(): void {
    const next = this.dark() ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = next;
    localStorage.setItem('erp.theme', next);
    this.dark.set(next === 'dark');
  }
}
