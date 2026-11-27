import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Api, parseApiError } from '../core/api';
import { SearchSelect } from '../shared/search-select';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { CustomerBalanceRow } from '../core/models';

interface CustomerStatementRow { date: string; type: 'Invoice' | 'Return' | 'Receipt'; number: string | null; reference: string | null; currencyId: number; amount: number; exchangeRate: number; debit: number; credit: number; balance: number }
interface CustomerStatement { customerId: number; openingBalance: number; rows: CustomerStatementRow[]; totalInvoiced: number; totalCredited: number; closingBalance: number; creditLimit: number }

/** What each customer owes (local currency): confirmed invoices minus confirmed returns and receipts. */
@Component({
  selector: 'app-customer-balances-page',
  imports: [DecimalPipe],
  template: `
    <header class="page-head"><h1>{{ i18n.t('nav.customerBalances') }}</h1></header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('f.customer') }}</th>
            <th class="num">{{ i18n.t('pur.invoicedTotal') }}</th>
            <th class="num">{{ i18n.t('sal.returnedTotal') }}</th>
            <th class="num">{{ i18n.t('sal.receivedTotal') }}</th>
            <th class="num">{{ i18n.t('sal.balanceDue') }}</th>
            <th class="num">{{ i18n.t('f.creditLimit') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.customerId) {
            <tr class="clickable" (click)="open(r.customerId)">
              <td>{{ r.code }} — {{ i18n.lang() === 'ar' ? r.nameAr || r.nameEn : r.nameEn || r.nameAr }}</td>
              <td class="num">{{ r.invoiced | number: '1.2-2' }}</td>
              <td class="num">{{ r.returned | number: '1.2-2' }}</td>
              <td class="num">{{ r.received | number: '1.2-2' }}</td>
              <td class="num" [class.neg]="r.balance > 0 && r.creditLimit > 0 && r.balance > r.creditLimit">{{ r.balance | number: '1.2-2' }}</td>
              <td class="num">{{ r.creditLimit ? (r.creditLimit | number: '1.2-2') : '—' }}</td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="6">{{ i18n.t(loading() ? 'common.loading' : 'common.empty') }}</td></tr>
          }
        </tbody>
        @if (rows().length) {
          <tfoot>
            <tr>
              <th>{{ i18n.t('common.total') }}</th>
              <th class="num">{{ sum('invoiced') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('returned') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('received') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('balance') | number: '1.2-2' }}</th>
              <th></th>
            </tr>
          </tfoot>
        }
      </table>
    </div>
  `,
})
export class CustomerBalancesPage implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly i18n = inject(I18n);
  readonly rows = signal<CustomerBalanceRow[]>([]);
  readonly loading = signal(true);
  readonly message = signal('');

  ngOnInit(): void {
    this.api.get<CustomerBalanceRow[]>('sales/customer-balances').subscribe({
      next: (r) => {
        this.rows.set(r);
        this.loading.set(false);
      },
      error: (e) => {
        this.message.set(parseApiError(e).message);
        this.loading.set(false);
      },
    });
  }

  sum(k: 'invoiced' | 'returned' | 'received' | 'balance'): number {
    return this.rows().reduce((s, r) => s + r[k], 0);
  }

  open(customerId: number): void {
    void this.router.navigate(['/customer-statement'], { queryParams: { customerId } });
  }
}

/** One customer's account: invoices raise what they owe; returns and receipts lower it. */
@Component({
  selector: 'app-customer-statement-page',
  imports: [FormsModule, DecimalPipe, SearchSelect],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.customerStatement') }}</h1>
      <div class="page-tools">
        <app-search-select name="customer" kind="customers" [activeOnly]="false" [(ngModel)]="customerId" (ngModelChange)="load()" />
        <input class="input date" type="date" [(ngModel)]="from" (ngModelChange)="load()" [title]="i18n.t('inv.from')" />
        <input class="input date" type="date" [(ngModel)]="to" (ngModelChange)="load()" [title]="i18n.t('inv.to')" />
      </div>
    </header>

    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }

    @if (stmt(); as s) {
      <div class="stat-row">
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.opening') }}</span><strong>{{ s.openingBalance | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.invoicedTotal') }}</span><strong>{{ s.totalInvoiced | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('sal.creditedTotal') }}</span><strong>{{ s.totalCredited | number: '1.2-2' }}</strong></div>
        <div class="card stat">
          <span class="muted-text">{{ i18n.t('pur.closing') }}</span>
          <strong [class.neg]="s.closingBalance > 0">{{ s.closingBalance | number: '1.2-2' }}</strong>
          @if (s.creditLimit > 0) {
            <span class="muted-text">{{ i18n.t('f.creditLimit') }}: {{ s.creditLimit | number: '1.2-2' }}</span>
          }
        </div>
      </div>

      <div class="card table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>{{ i18n.t('f.date') }}</th>
              <th>{{ i18n.t('f.type') }}</th>
              <th>{{ i18n.t('inv.number') }}</th>
              <th>{{ i18n.t('pur.reference') }}</th>
              <th class="num">{{ i18n.t('pur.amount') }}</th>
              <th class="num">{{ i18n.t('pur.rate') }}</th>
              <th class="num">{{ i18n.t('pur.debit') }}</th>
              <th class="num">{{ i18n.t('pur.credit') }}</th>
              <th class="num">{{ i18n.t('inv.value') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of s.rows; track $index) {
              <tr>
                <td>{{ r.date }}</td>
                <td>{{ i18n.t('stmt.' + r.type) }}</td>
                <td>{{ r.number }}</td>
                <td>{{ r.reference }}</td>
                <td class="num">{{ r.amount | number: '1.2-4' }} {{ lookups.currencyLabel(r.currencyId) }}</td>
                <td class="num">{{ r.exchangeRate | number: '1.0-4' }}</td>
                <td class="num">{{ r.debit ? (r.debit | number: '1.2-2') : '' }}</td>
                <td class="num">{{ r.credit ? (r.credit | number: '1.2-2') : '' }}</td>
                <td class="num">{{ r.balance | number: '1.2-2' }}</td>
              </tr>
            } @empty {
              <tr><td class="empty" colspan="9">{{ i18n.t('common.empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <p class="hint-text">{{ i18n.t('sal.statementHint') }}</p>
    } @else if (!message()) {
      <p class="hint-text">{{ i18n.t('sal.pickCustomer') }}</p>
    }
  `,
})
export class CustomerStatementPage implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);
  readonly stmt = signal<CustomerStatement | null>(null);
  readonly message = signal('');
  customerId: number | null = null;
  from = '';
  to = '';

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    const id = Number(this.route.snapshot.queryParamMap.get('customerId'));
    if (id) {
      this.customerId = id;
      this.load();
    }
  }

  load(): void {
    if (!this.customerId) return;
    this.message.set('');
    const qs = [`customerId=${this.customerId}`];
    if (this.from) qs.push(`from=${this.from}`);
    if (this.to) qs.push(`to=${this.to}`);
    this.api.get<CustomerStatement>(`sales/customer-statement?${qs.join('&')}`).subscribe({
      next: (s) => this.stmt.set(s),
      error: (e) => {
        this.stmt.set(null);
        this.message.set(parseApiError(e).message);
      },
    });
  }
}
