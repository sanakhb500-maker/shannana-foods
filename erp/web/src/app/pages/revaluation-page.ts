import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { RevaluationPreview, RevaluationRun, WRITE_ROLES } from '../core/models';

/**
 * Period-end revaluation of open foreign-currency invoices at the closing rate. The result is posted on the as-of date
 * and reversed automatically the next day; the realised difference is booked when the invoice is actually paid.
 */
@Component({
  selector: 'app-revaluation-page',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.revaluation') }}</h1>
      <div class="page-tools">
        <input class="input date" type="date" [(ngModel)]="asOf" [title]="i18n.t('acc.asOf')" />
        <button class="btn" type="button" [disabled]="!asOf" (click)="preview()">{{ i18n.t('reval.preview') }}</button>
        @if (canWrite) {
          <button class="btn primary" type="button" [disabled]="!asOf || !pv() || pv()!.lines.length === 0 || busy()" (click)="run()">{{ i18n.t('reval.post') }}</button>
        }
      </div>
    </header>

    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }

    @if (pv(); as p) {
      <div class="stat-row">
        <div class="card stat"><span class="muted-text">{{ i18n.t('reval.receivables') }}</span><strong [class.pos]="p.receivablesDifference > 0" [class.neg]="p.receivablesDifference < 0">{{ p.receivablesDifference | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('reval.payables') }}</span><strong [class.neg]="p.payablesDifference > 0" [class.pos]="p.payablesDifference < 0">{{ p.payablesDifference | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('reval.net') }}</span><strong [class.pos]="p.netResult > 0" [class.neg]="p.netResult < 0">{{ p.netResult | number: '1.2-2' }}</strong></div>
      </div>
      <div class="card table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>{{ i18n.t('alloc.invoice') }}</th>
              <th>{{ i18n.t('f.type') }}</th>
              <th class="num">{{ i18n.t('alloc.outstanding') }}</th>
              <th class="num">{{ i18n.t('reval.booked') }}</th>
              <th class="num">{{ i18n.t('reval.closing') }}</th>
              <th class="num">{{ i18n.t('acc.difference') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (l of p.lines; track l.kind + l.invoiceId) {
              <tr>
                <td>{{ l.number }}</td>
                <td>{{ i18n.t(l.isReceivable ? 'reval.receivable' : 'reval.payable') }} · {{ lookups.currencyLabel(l.currencyId) }}</td>
                <td class="num">{{ l.outstanding | number: '1.2-4' }}</td>
                <td class="num">{{ l.bookedRate | number: '1.0-4' }}</td>
                <td class="num">{{ l.closingRate | number: '1.0-4' }}</td>
                <td class="num">{{ l.difference | number: '1.2-2' }}</td>
              </tr>
            } @empty {
              <tr><td class="empty" colspan="6">{{ i18n.t('reval.nothing') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }

    <h3 class="sub">{{ i18n.t('reval.history') }}</h3>
    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('acc.asOf') }}</th>
            <th>{{ i18n.t('reval.entry') }}</th>
            <th>{{ i18n.t('reval.reversal') }}</th>
            <th class="num">{{ i18n.t('reval.net') }}</th>
            <th>{{ i18n.t('reval.by') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of runs(); track r.id) {
            <tr>
              <td>{{ r.asOf }}</td>
              <td>{{ r.entryNumber }}</td>
              <td>{{ r.reversalNumber }}</td>
              <td class="num">{{ r.netResult | number: '1.2-2' }}</td>
              <td>{{ r.createdBy }}</td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="5">{{ i18n.t('common.empty') }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    <p class="hint-text">{{ i18n.t('reval.hint') }}</p>
  `,
})
export class RevaluationPage implements OnInit {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);
  readonly canWrite = this.auth.hasAnyRole(WRITE_ROLES.accounting);
  readonly pv = signal<RevaluationPreview | null>(null);
  readonly runs = signal<RevaluationRun[]>([]);
  readonly message = signal('');
  readonly busy = signal(false);
  asOf = new Date().toISOString().slice(0, 10);

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.loadRuns();
  }

  private loadRuns(): void {
    this.api.get<RevaluationRun[]>('accounting/revaluation/runs').subscribe((r) => this.runs.set(r));
  }

  preview(): void {
    this.message.set('');
    this.api.get<RevaluationPreview>(`accounting/revaluation/preview?asOf=${this.asOf}`).subscribe({
      next: (p) => this.pv.set(p),
      error: (e) => {
        this.pv.set(null);
        this.message.set(parseApiError(e).message);
      },
    });
  }

  run(): void {
    if (!confirm(this.i18n.t('reval.confirm'))) return;
    this.busy.set(true);
    this.message.set('');
    this.api.post<RevaluationRun>('accounting/revaluation', { asOf: this.asOf }).subscribe({
      next: () => {
        this.busy.set(false);
        this.pv.set(null);
        this.loadRuns();
      },
      error: (e) => {
        this.busy.set(false);
        this.message.set(parseApiError(e).message);
      },
    });
  }
}
