import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Api, parseApiError } from '../core/api';
import { TableFilter } from '../shared/list-search';
import { I18n } from '../core/i18n';
import { AgeingRow } from '../core/models';

/** Who owes how much and for how long (suppliers: what we owe; customers: what is owed to us). Local currency, by days past due. */
@Component({
  selector: 'app-ageing-page',
  imports: [DecimalPipe, TableFilter],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t(side === 'supplier' ? 'nav.supplierAgeing' : 'nav.customerAgeing') }}</h1>
      <div class="page-tools"><app-table-filter /></div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t(side === 'supplier' ? 'f.supplier' : 'f.customer') }}</th>
            <th class="num">{{ i18n.t('age.current') }}</th>
            <th class="num">{{ i18n.t('age.d1_30') }}</th>
            <th class="num">{{ i18n.t('age.d31_60') }}</th>
            <th class="num">{{ i18n.t('age.d61_90') }}</th>
            <th class="num">{{ i18n.t('age.over90') }}</th>
            <th class="num">{{ i18n.t('common.total') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.partyId) {
            <tr class="clickable" (click)="open(r.partyId)">
              <td>{{ r.code }} — {{ i18n.lang() === 'ar' ? r.nameAr || r.nameEn : r.nameEn || r.nameAr }}</td>
              <td class="num">{{ r.current | number: '1.2-2' }}</td>
              <td class="num">{{ r.days1To30 | number: '1.2-2' }}</td>
              <td class="num">{{ r.days31To60 | number: '1.2-2' }}</td>
              <td class="num" [class.neg]="r.days61To90 > 0">{{ r.days61To90 | number: '1.2-2' }}</td>
              <td class="num" [class.neg]="r.over90 > 0">{{ r.over90 | number: '1.2-2' }}</td>
              <td class="num"><strong>{{ r.total | number: '1.2-2' }}</strong></td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="7">{{ i18n.t(loading() ? 'common.loading' : 'age.none') }}</td></tr>
          }
        </tbody>
        @if (rows().length) {
          <tfoot>
            <tr>
              <th>{{ i18n.t('common.total') }}</th>
              <th class="num">{{ sum('current') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('days1To30') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('days31To60') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('days61To90') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('over90') | number: '1.2-2' }}</th>
              <th class="num">{{ sum('total') | number: '1.2-2' }}</th>
            </tr>
          </tfoot>
        }
      </table>
    </div>
    <p class="hint-text">{{ i18n.t('age.hint') }}</p>
  `,
})
export class AgeingPage implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly i18n = inject(I18n);
  readonly side: 'supplier' | 'customer' = inject(ActivatedRoute).snapshot.data['side'];
  readonly rows = signal<AgeingRow[]>([]);
  readonly loading = signal(true);
  readonly message = signal('');

  ngOnInit(): void {
    this.api.get<AgeingRow[]>(this.side === 'supplier' ? 'purchasing/ageing' : 'sales/ageing').subscribe({
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

  sum(k: 'current' | 'days1To30' | 'days31To60' | 'days61To90' | 'over90' | 'total'): number {
    return this.rows().reduce((s, r) => s + r[k], 0);
  }

  open(partyId: number): void {
    void this.router.navigate([this.side === 'supplier' ? '/supplier-statement' : '/customer-statement'], {
      queryParams: this.side === 'supplier' ? { supplierId: partyId } : { customerId: partyId },
    });
  }
}
