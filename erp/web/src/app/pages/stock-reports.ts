import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, parseApiError } from '../core/api';
import { SearchSelect } from '../shared/search-select';
import { ListSearch, TableFilter } from '../shared/list-search';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { BalanceRow, BalancesResult, LowStockRow, MovementRow, Paged } from '../core/models';

/** Stock on hand per item and warehouse, with the total stock value (quantity x moving-average cost). */
@Component({
  selector: 'app-stock-balances-page',
  imports: [FormsModule, DecimalPipe, SearchSelect],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.stockBalances') }}</h1>
      <div class="page-tools">
        <app-search-select name="warehouse" kind="warehouses" [clearable]="true" [placeholder]="i18n.t('inv.allWarehouses')" [(ngModel)]="warehouseId" (ngModelChange)="reload()" />
        <input class="input" type="search" [placeholder]="i18n.t('common.search')" [(ngModel)]="q" (input)="onSearch()" />
        <label class="check"><input type="checkbox" [(ngModel)]="includeZero" (change)="reload()" /> {{ i18n.t('inv.includeZero') }}</label>
      </div>
    </header>

    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }

    <div class="stat-row">
      <div class="card stat"><span class="muted-text">{{ i18n.t('inv.totalValue') }}</span><strong>{{ result()?.totalValue ?? 0 | number: '1.2-2' }}</strong></div>
      <div class="card stat"><span class="muted-text">{{ i18n.t('common.total') }}</span><strong>{{ result()?.total ?? 0 }}</strong></div>
    </div>

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('f.item') }}</th>
            <th>{{ i18n.t('f.warehouse') }}</th>
            <th class="num">{{ i18n.t('inv.quantity') }}</th>
            <th class="num">{{ i18n.t('inv.avgCost') }}</th>
            <th class="num">{{ i18n.t('inv.value') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.itemId + '-' + r.warehouseId) {
            <tr>
              <td>{{ r.itemCode }} — {{ name(r.itemNameAr, r.itemNameEn) }}</td>
              <td>{{ r.warehouseCode }} — {{ name(r.warehouseNameAr, r.warehouseNameEn) }}</td>
              <td class="num" [class.neg]="r.quantity < 0">{{ r.quantity | number: '1.0-4' }}</td>
              <td class="num">{{ r.averageCost | number: '1.2-4' }}</td>
              <td class="num">{{ r.value | number: '1.2-2' }}</td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="5">{{ i18n.t(loading() ? 'common.loading' : 'common.empty') }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    <footer class="pager">
      <span class="spacer"></span>
      <button class="btn sm" type="button" [disabled]="page() <= 1" (click)="go(-1)">{{ i18n.t('common.prev') }}</button>
      <span>{{ page() }} / {{ pages() }}</span>
      <button class="btn sm" type="button" [disabled]="page() >= pages()" (click)="go(1)">{{ i18n.t('common.next') }}</button>
    </footer>
  `,
})
export class StockBalancesPage implements OnInit {
  private readonly api = inject(Api);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly result = signal<BalancesResult | null>(null);
  readonly rows = computed<BalanceRow[]>(() => this.result()?.items ?? []);
  readonly page = signal(1);
  readonly pages = computed(() => Math.max(1, Math.ceil((this.result()?.total ?? 0) / 50)));
  readonly loading = signal(false);
  readonly message = signal('');
  warehouseId: number | null = null;
  q = '';
  includeZero = false;
  private timer?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
  }

  name(ar: string, en: string): string {
    return this.i18n.lang() === 'ar' ? ar || en : en || ar;
  }

  reload(): void {
    this.page.set(1);
    this.load();
  }

  onSearch(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.reload(), 300);
  }

  go(d: number): void {
    this.page.update((p) => Math.min(this.pages(), Math.max(1, p + d)));
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api
      .get<BalancesResult>(
        `stock/balances?page=${this.page()}&pageSize=50&nonZeroOnly=${!this.includeZero}` +
          (this.warehouseId ? `&warehouseId=${this.warehouseId}` : '') +
          (this.q.trim() ? `&q=${encodeURIComponent(this.q.trim())}` : ''),
      )
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.loading.set(false);
        },
        error: (e) => {
          this.message.set(parseApiError(e).message);
          this.loading.set(false);
        },
      });
  }
}

/** The stock ledger: every movement, newest first. */
@Component({
  selector: 'app-stock-movements-page',
  imports: [FormsModule, DecimalPipe, SearchSelect, ListSearch],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.stockMovements') }}</h1>
      <div class="page-tools">
        <app-list-search (search)="onQuery($event)" />
        <app-search-select name="item" kind="items" [clearable]="true" [activeOnly]="false" [placeholder]="i18n.t('inv.allItems')" [(ngModel)]="itemId" (ngModelChange)="reload()" />
        <app-search-select name="warehouse" kind="warehouses" [clearable]="true" [activeOnly]="false" [placeholder]="i18n.t('inv.allWarehouses')" [(ngModel)]="warehouseId" (ngModelChange)="reload()" />
        <input class="input date" type="date" [(ngModel)]="from" (ngModelChange)="reload()" [title]="i18n.t('inv.from')" />
        <input class="input date" type="date" [(ngModel)]="to" (ngModelChange)="reload()" [title]="i18n.t('inv.to')" />
      </div>
    </header>

    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('f.date') }}</th>
            <th>{{ i18n.t('inv.document') }}</th>
            <th>{{ i18n.t('f.type') }}</th>
            <th>{{ i18n.t('f.item') }}</th>
            <th>{{ i18n.t('f.warehouse') }}</th>
            <th class="num">{{ i18n.t('inv.quantity') }}</th>
            <th class="num">{{ i18n.t('inv.unitCost') }}</th>
            <th class="num">{{ i18n.t('inv.value') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (m of rows(); track m.id) {
            <tr>
              <td>{{ m.date }}</td>
              <td>{{ m.documentNumber }}</td>
              <td>{{ i18n.t('mt.' + m.type) }}</td>
              <td>{{ m.itemCode }} — {{ name(m.itemNameAr, m.itemNameEn) }}</td>
              <td>{{ m.warehouseCode }}</td>
              <td class="num" [class.neg]="m.quantity < 0" [class.pos]="m.quantity > 0">{{ m.quantity | number: '1.0-4' }}</td>
              <td class="num">{{ m.unitCost | number: '1.2-4' }}</td>
              <td class="num">{{ m.value | number: '1.2-2' }}</td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="8">{{ i18n.t(loading() ? 'common.loading' : 'common.empty') }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    <footer class="pager">
      <span>{{ i18n.t('common.total') }}: {{ total() }}</span>
      <span class="spacer"></span>
      <button class="btn sm" type="button" [disabled]="page() <= 1" (click)="go(-1)">{{ i18n.t('common.prev') }}</button>
      <span>{{ page() }} / {{ pages() }}</span>
      <button class="btn sm" type="button" [disabled]="page() >= pages()" (click)="go(1)">{{ i18n.t('common.next') }}</button>
    </footer>
  `,
})
export class StockMovementsPage implements OnInit {
  private readonly api = inject(Api);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly rows = signal<MovementRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / 50)));
  readonly loading = signal(false);
  readonly message = signal('');
  itemId: number | null = null;
  warehouseId: number | null = null;
  q = '';

  onQuery(v: string): void {
    this.q = v;
    this.reload();
  }
  from = '';
  to = '';

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
  }

  name(ar: string, en: string): string {
    return this.i18n.lang() === 'ar' ? ar || en : en || ar;
  }

  reload(): void {
    this.page.set(1);
    this.load();
  }

  go(d: number): void {
    this.page.update((p) => Math.min(this.pages(), Math.max(1, p + d)));
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const qs = [`page=${this.page()}`, 'pageSize=50'];
    if (this.itemId) qs.push(`itemId=${this.itemId}`);
    if (this.warehouseId) qs.push(`warehouseId=${this.warehouseId}`);
    if (this.from) qs.push(`from=${this.from}`);
    if (this.to) qs.push(`to=${this.to}`);
    if (this.q) qs.push(`q=${encodeURIComponent(this.q)}`);
    this.api.get<Paged<MovementRow>>(`stock/movements?${qs.join('&')}`).subscribe({
      next: (r) => {
        this.rows.set(r.items);
        this.total.set(r.total);
        this.loading.set(false);
      },
      error: (e) => {
        this.message.set(parseApiError(e).message);
        this.loading.set(false);
      },
    });
  }
}

/** Items whose total stock is at or below their reorder level. */
@Component({
  selector: 'app-low-stock-page',
  imports: [DecimalPipe, TableFilter],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.lowStock') }}</h1>
      <div class="page-tools"><app-table-filter /></div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('f.item') }}</th>
            <th class="num">{{ i18n.t('inv.onHand') }}</th>
            <th class="num">{{ i18n.t('f.reorderLevel') }}</th>
            <th class="num">{{ i18n.t('inv.shortfall') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.itemId) {
            <tr>
              <td>{{ r.itemCode }} — {{ i18n.lang() === 'ar' ? r.itemNameAr || r.itemNameEn : r.itemNameEn || r.itemNameAr }}</td>
              <td class="num neg">{{ r.totalQuantity | number: '1.0-4' }}</td>
              <td class="num">{{ r.reorderLevel | number: '1.0-4' }}</td>
              <td class="num">{{ r.reorderLevel - r.totalQuantity | number: '1.0-4' }}</td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="4">{{ i18n.t(loading() ? 'common.loading' : 'inv.noLowStock') }}</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class LowStockPage implements OnInit {
  private readonly api = inject(Api);
  readonly i18n = inject(I18n);
  readonly rows = signal<LowStockRow[]>([]);
  readonly loading = signal(true);
  readonly message = signal('');

  ngOnInit(): void {
    this.api.get<LowStockRow[]>('stock/low-stock').subscribe({
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
}
