import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { AccountingSummary, WRITE_ROLES } from '../core/models';

/** Home page. People with accounting access also see the key figures straight from the books. */
@Component({
  selector: 'app-dashboard-page',
  imports: [DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('dash.welcome') }}، {{ auth.session()?.fullName }}</h1>
    </header>

    @if (summary(); as s) {
      <div class="kpis">
        <div class="card stat"><span class="muted-text">{{ i18n.t('kpi.cash') }}</span><strong>{{ s.cashAndBank | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('kpi.receivables') }}</span><strong>{{ s.receivables | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('kpi.payables') }}</span><strong>{{ s.payables | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('kpi.inventory') }}</span><strong>{{ s.inventoryValue | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('kpi.revenue') }}</span><strong>{{ s.monthRevenue | number: '1.2-2' }}</strong></div>
        <div class="card stat">
          <span class="muted-text">{{ i18n.t('kpi.grossProfit') }}</span>
          <strong [class.neg]="s.monthGrossProfit < 0" [class.pos]="s.monthGrossProfit > 0">{{ s.monthGrossProfit | number: '1.2-2' }}</strong>
        </div>
      </div>

      <div class="card pad">
        <h3 class="sub">{{ i18n.t('kpi.last6') }}</h3>
        <div class="bars">
          @for (m of s.months; track m.month) {
            <div class="bar-col">
              <div class="bar-pair">
                <div class="bar rev" [style.height.%]="pct(m.revenue)" [title]="i18n.t('kpi.revenue') + ': ' + (m.revenue | number: '1.2-2')"></div>
                <div class="bar cogs" [style.height.%]="pct(m.costOfGoodsSold)" [title]="i18n.t('kpi.cogs') + ': ' + (m.costOfGoodsSold | number: '1.2-2')"></div>
              </div>
              <span class="muted-text">{{ m.month.slice(5) }}</span>
            </div>
          }
        </div>
        <div class="legend"><span class="dot rev"></span> {{ i18n.t('kpi.revenue') }} <span class="dot cogs"></span> {{ i18n.t('kpi.cogs') }}</div>
        @if (s.lockDate) {
          <p class="hint-text">{{ i18n.t('acc.lockedUntil') }}: {{ s.lockDate }}</p>
        }
      </div>
    } @else {
      <div class="card pad">
        <p>{{ i18n.t('dash.intro') }}</p>
      </div>
    }
  `,
})
export class DashboardPage implements OnInit {
  private readonly api = inject(Api);
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly summary = signal<AccountingSummary | null>(null);
  private peak = 1;

  ngOnInit(): void {
    if (!this.auth.hasAnyRole(WRITE_ROLES.accounting)) return;
    this.api.get<AccountingSummary>('accounting/summary').subscribe({
      next: (s) => {
        this.peak = Math.max(1, ...s.months.flatMap((m) => [m.revenue, m.costOfGoodsSold]));
        this.summary.set(s);
      },
      error: () => this.summary.set(null),
    });
  }

  /** Bar height as a share of the busiest month, never below a visible sliver for non-zero values. */
  pct(v: number): number {
    return v <= 0 ? 0 : Math.max(3, (v / this.peak) * 100);
  }
}
