import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { TableFilter } from '../shared/list-search';
import { AccountDto, BalanceSheet, IncomeStatement, Ledger, ReconciliationRow, TrialBalance, WRITE_ROLES } from '../core/models';

/** Shared by the report pages: a localized account name and a "1,234.00 Dr/Cr" formatter for net debit amounts. */
abstract class ReportBase {
  protected readonly api = inject(Api);
  readonly i18n = inject(I18n);
  readonly message = signal('');

  name(r: { nameAr: string; nameEn: string }): string {
    return this.i18n.name(r);
  }

  dc(v: number): string {
    if (Math.abs(v) < 0.00005) return '0.00';
    const text = Math.abs(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });
    return `${text} ${this.i18n.t(v > 0 ? 'acc.dr' : 'acc.cr')}`;
  }

  protected fail(e: unknown): void {
    this.message.set(parseApiError(e).message);
  }
}

@Component({
  selector: 'app-trial-balance-page',
  imports: [FormsModule, DecimalPipe, TableFilter],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.trialBalance') }}</h1>
      <div class="page-tools">
        <app-table-filter />
        <input class="input date" type="date" [(ngModel)]="from" (ngModelChange)="load()" [title]="i18n.t('inv.from')" />
        <input class="input date" type="date" [(ngModel)]="to" (ngModelChange)="load()" [title]="i18n.t('inv.to')" />
      </div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    @if (tb(); as t) {
      <div class="stat-row">
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.debit') }}</span><strong>{{ t.totalDebit | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.credit') }}</span><strong>{{ t.totalCredit | number: '1.2-2' }}</strong></div>
        <div class="card stat">
          <span class="muted-text">{{ i18n.t('acc.check') }}</span>
          <strong [class.neg]="!t.isBalanced" [class.pos]="t.isBalanced">{{ i18n.t(t.isBalanced ? 'acc.balanced' : 'acc.unbalanced') }}</strong>
        </div>
      </div>
      <div class="card table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>{{ i18n.t('f.code') }}</th>
              <th>{{ i18n.t('acc.account') }}</th>
              <th class="num">{{ i18n.t('pur.opening') }}</th>
              <th class="num">{{ i18n.t('pur.debit') }}</th>
              <th class="num">{{ i18n.t('pur.credit') }}</th>
              <th class="num">{{ i18n.t('pur.closing') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of t.rows; track r.accountId) {
              <tr>
                <td>{{ r.code }}</td>
                <td>{{ name(r) }}</td>
                <td class="num">{{ dc(r.opening) }}</td>
                <td class="num">{{ r.debit | number: '1.2-2' }}</td>
                <td class="num">{{ r.credit | number: '1.2-2' }}</td>
                <td class="num">{{ dc(r.closing) }}</td>
              </tr>
            } @empty {
              <tr><td class="empty" colspan="6">{{ i18n.t('common.empty') }}</td></tr>
            }
          </tbody>
          <tfoot>
            <tr>
              <th colspan="3">{{ i18n.t('common.total') }}</th>
              <th class="num">{{ t.totalDebit | number: '1.2-2' }}</th>
              <th class="num">{{ t.totalCredit | number: '1.2-2' }}</th>
              <th class="num">{{ t.closingDebit | number: '1.2-2' }} {{ i18n.t('acc.dr') }} / {{ t.closingCredit | number: '1.2-2' }} {{ i18n.t('acc.cr') }}</th>
            </tr>
          </tfoot>
        </table>
      </div>
    }
  `,
})
export class TrialBalancePage extends ReportBase implements OnInit {
  readonly tb = signal<TrialBalance | null>(null);
  from = '';
  to = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.message.set('');
    const qs = [this.from && `from=${this.from}`, this.to && `to=${this.to}`].filter(Boolean).join('&');
    this.api.get<TrialBalance>(`accounting/trial-balance${qs ? '?' + qs : ''}`).subscribe({ next: (t) => this.tb.set(t), error: (e) => this.fail(e) });
  }
}

@Component({
  selector: 'app-ledger-page',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.ledger') }}</h1>
      <div class="page-tools">
        <select class="input" [(ngModel)]="accountId" (ngModelChange)="load()">
          <option [ngValue]="null" disabled>{{ i18n.t('common.select') }}</option>
          @for (a of accounts(); track a.id) {
            <option [ngValue]="a.id">{{ a.code }} — {{ name(a) }}</option>
          }
        </select>
        <input class="input date" type="date" [(ngModel)]="from" (ngModelChange)="load()" [title]="i18n.t('inv.from')" />
        <input class="input date" type="date" [(ngModel)]="to" (ngModelChange)="load()" [title]="i18n.t('inv.to')" />
      </div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    @if (ledger(); as l) {
      <div class="stat-row">
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.opening') }}</span><strong>{{ dc(l.opening) }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.debit') }}</span><strong>{{ l.totalDebit | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.credit') }}</span><strong>{{ l.totalCredit | number: '1.2-2' }}</strong></div>
        <div class="card stat"><span class="muted-text">{{ i18n.t('pur.closing') }}</span><strong>{{ dc(l.closing) }}</strong></div>
      </div>
      <div class="card table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>{{ i18n.t('f.date') }}</th>
              <th>{{ i18n.t('inv.number') }}</th>
              <th>{{ i18n.t('f.description') }}</th>
              <th>{{ i18n.t('acc.source') }}</th>
              <th class="num">{{ i18n.t('pur.debit') }}</th>
              <th class="num">{{ i18n.t('pur.credit') }}</th>
              <th class="num">{{ i18n.t('inv.value') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of l.rows; track $index) {
              <tr>
                <td>{{ r.date }}</td>
                <td>{{ r.entryNumber }}</td>
                <td>{{ r.description }}</td>
                <td>{{ i18n.t('src.' + r.sourceType) }}</td>
                <td class="num">{{ r.debit ? (r.debit | number: '1.2-2') : '' }}</td>
                <td class="num">{{ r.credit ? (r.credit | number: '1.2-2') : '' }}</td>
                <td class="num">{{ dc(r.balance) }}</td>
              </tr>
            } @empty {
              <tr><td class="empty" colspan="7">{{ i18n.t('common.empty') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    } @else if (!message()) {
      <p class="hint-text">{{ i18n.t('acc.pickAccount') }}</p>
    }
  `,
})
export class LedgerPage extends ReportBase implements OnInit {
  private readonly route = inject(ActivatedRoute);
  readonly ledger = signal<Ledger | null>(null);
  readonly accounts = signal<AccountDto[]>([]);
  accountId: number | null = null;
  from = '';
  to = '';

  ngOnInit(): void {
    this.api.list<AccountDto>('accounts', { pageSize: 500, postableOnly: true }).subscribe((r) => this.accounts.set(r.items));
    const id = Number(this.route.snapshot.queryParamMap.get('accountId'));
    if (id) {
      this.accountId = id;
      this.load();
    }
  }

  load(): void {
    if (!this.accountId) return;
    this.message.set('');
    const qs = [`accountId=${this.accountId}`, this.from && `from=${this.from}`, this.to && `to=${this.to}`].filter(Boolean).join('&');
    this.api.get<Ledger>(`accounting/ledger?${qs}`).subscribe({ next: (l) => this.ledger.set(l), error: (e) => { this.ledger.set(null); this.fail(e); } });
  }
}

@Component({
  selector: 'app-income-statement-page',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.incomeStatement') }}</h1>
      <div class="page-tools">
        <input class="input date" type="date" [(ngModel)]="from" (ngModelChange)="load()" [title]="i18n.t('inv.from')" />
        <input class="input date" type="date" [(ngModel)]="to" (ngModelChange)="load()" [title]="i18n.t('inv.to')" />
      </div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    @if (s(); as st) {
      <div class="card pad statement">
        <h3>{{ i18n.t('acc.revenue') }}</h3>
        @for (l of st.revenue; track l.accountId) {
          <div class="srow"><span>{{ l.code }} — {{ name(l) }}</span><span>{{ l.amount | number: '1.2-2' }}</span></div>
        } @empty { <div class="srow muted-text">—</div> }
        <div class="srow total"><span>{{ i18n.t('acc.totalRevenue') }}</span><span>{{ st.totalRevenue | number: '1.2-2' }}</span></div>

        <h3>{{ i18n.t('acc.expenses') }}</h3>
        @for (l of st.expenses; track l.accountId) {
          <div class="srow"><span>{{ l.code }} — {{ name(l) }}</span><span>{{ l.amount | number: '1.2-2' }}</span></div>
        } @empty { <div class="srow muted-text">—</div> }
        <div class="srow total"><span>{{ i18n.t('acc.totalExpenses') }}</span><span>{{ st.totalExpenses | number: '1.2-2' }}</span></div>

        <div class="srow grand" [class.neg]="st.netProfit < 0" [class.pos]="st.netProfit > 0"><span>{{ i18n.t('acc.netProfit') }}</span><span>{{ st.netProfit | number: '1.2-2' }}</span></div>
      </div>
    }
  `,
})
export class IncomeStatementPage extends ReportBase implements OnInit {
  readonly s = signal<IncomeStatement | null>(null);
  from = '';
  to = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.message.set('');
    const qs = [this.from && `from=${this.from}`, this.to && `to=${this.to}`].filter(Boolean).join('&');
    this.api.get<IncomeStatement>(`accounting/income-statement${qs ? '?' + qs : ''}`).subscribe({ next: (x) => this.s.set(x), error: (e) => this.fail(e) });
  }
}

@Component({
  selector: 'app-balance-sheet-page',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.balanceSheet') }}</h1>
      <div class="page-tools">
        <input class="input date" type="date" [(ngModel)]="asOf" (ngModelChange)="load()" [title]="i18n.t('acc.asOf')" />
      </div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    @if (s(); as b) {
      <div class="two-col">
        <div class="card pad statement">
          <h3>{{ i18n.t('acc.assets') }}</h3>
          @for (l of b.assets; track l.accountId) {
            <div class="srow"><span>{{ l.code }} — {{ name(l) }}</span><span>{{ l.amount | number: '1.2-2' }}</span></div>
          } @empty { <div class="srow muted-text">—</div> }
          <div class="srow grand"><span>{{ i18n.t('acc.totalAssets') }}</span><span>{{ b.totalAssets | number: '1.2-2' }}</span></div>
        </div>
        <div class="card pad statement">
          <h3>{{ i18n.t('acc.liabilities') }}</h3>
          @for (l of b.liabilities; track l.accountId) {
            <div class="srow"><span>{{ l.code }} — {{ name(l) }}</span><span>{{ l.amount | number: '1.2-2' }}</span></div>
          } @empty { <div class="srow muted-text">—</div> }
          <div class="srow total"><span>{{ i18n.t('acc.totalLiabilities') }}</span><span>{{ b.totalLiabilities | number: '1.2-2' }}</span></div>

          <h3>{{ i18n.t('acc.equity') }}</h3>
          @for (l of b.equity; track l.accountId) {
            <div class="srow"><span>{{ l.code }} — {{ name(l) }}</span><span>{{ l.amount | number: '1.2-2' }}</span></div>
          }
          <div class="srow"><span>{{ i18n.t('acc.currentEarnings') }}</span><span>{{ b.currentEarnings | number: '1.2-2' }}</span></div>
          <div class="srow total"><span>{{ i18n.t('acc.totalEquity') }}</span><span>{{ b.totalEquity | number: '1.2-2' }}</span></div>

          <div class="srow grand"><span>{{ i18n.t('acc.liabEquity') }}</span><span>{{ b.totalLiabilities + b.totalEquity | number: '1.2-2' }}</span></div>
        </div>
      </div>
      <p class="hint-text" [class.neg]="!b.isBalanced" [class.pos]="b.isBalanced">{{ i18n.t(b.isBalanced ? 'acc.balanced' : 'acc.unbalanced') }}</p>
    }
  `,
})
export class BalanceSheetPage extends ReportBase implements OnInit {
  readonly s = signal<BalanceSheet | null>(null);
  asOf = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.message.set('');
    this.api.get<BalanceSheet>(`accounting/balance-sheet${this.asOf ? '?asOf=' + this.asOf : ''}`).subscribe({ next: (x) => this.s.set(x), error: (e) => this.fail(e) });
  }
}

/** Control accounts in the general ledger against what the sub-ledgers (stock, suppliers, customers) say. */
@Component({
  selector: 'app-reconciliation-page',
  imports: [DecimalPipe, TableFilter],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.reconciliation') }}</h1>
      <div class="page-tools"><app-table-filter /></div>
    </header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('acc.control') }}</th>
            <th class="num">{{ i18n.t('acc.generalLedger') }}</th>
            <th class="num">{{ i18n.t('acc.subLedger') }}</th>
            <th class="num">{{ i18n.t('acc.difference') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.key) {
            <tr>
              <td>{{ i18n.t('rec.' + r.key) }}</td>
              <td class="num">{{ r.generalLedger | number: '1.2-4' }}</td>
              <td class="num">{{ r.subLedger | number: '1.2-4' }}</td>
              <td class="num" [class.neg]="!r.reconciled">{{ r.difference | number: '1.2-4' }}</td>
              <td><span class="badge" [class.ok]="r.reconciled">{{ i18n.t(r.reconciled ? 'acc.reconciled' : 'acc.differs') }}</span></td>
            </tr>
          }
        </tbody>
      </table>
    </div>
    <p class="hint-text">{{ i18n.t('acc.reconHint') }}</p>
  `,
})
export class ReconciliationPage extends ReportBase implements OnInit {
  readonly rows = signal<ReconciliationRow[]>([]);

  ngOnInit(): void {
    this.api.get<ReconciliationRow[]>('accounting/reconciliation').subscribe({ next: (r) => this.rows.set(r), error: (e) => this.fail(e) });
  }
}

/** Closing a period: nothing can be posted or reversed on or before the lock date. */
@Component({
  selector: 'app-accounting-settings-page',
  imports: [FormsModule],
  template: `
    <header class="page-head"><h1>{{ i18n.t('nav.accountingSettings') }}</h1></header>
    @if (message()) {
      <div class="alert error">{{ message() }}</div>
    }
    @if (saved()) {
      <div class="alert ok">{{ i18n.t('common.saved') }}</div>
    }
    <div class="card pad" style="max-width: 560px">
      <div class="field">
        <label for="lock">{{ i18n.t('acc.lockDate') }}</label>
        <input id="lock" class="input date" type="date" name="lock" [(ngModel)]="lockDate" [disabled]="!canWrite" />
      </div>
      <p class="hint-text">{{ i18n.t('acc.lockHint') }}</p>
      @if (canWrite) {
        <div class="modal-actions">
          <button class="btn" type="button" (click)="lockDate = ''; save()">{{ i18n.t('acc.reopen') }}</button>
          <button class="btn primary" type="button" (click)="save()">{{ i18n.t('common.save') }}</button>
        </div>
      }
    </div>
  `,
})
export class AccountingSettingsPage extends ReportBase implements OnInit {
  private readonly auth = inject(Auth);
  readonly canWrite = this.auth.hasAnyRole(WRITE_ROLES.accounting);
  readonly saved = signal(false);
  lockDate = '';

  ngOnInit(): void {
    this.api.get<{ lockDate: string | null }>('accounting/settings').subscribe({ next: (s) => (this.lockDate = s.lockDate ?? ''), error: (e) => this.fail(e) });
  }

  save(): void {
    this.message.set('');
    this.saved.set(false);
    this.api.put<{ lockDate: string | null }>('accounting/settings', { lockDate: this.lockDate || null }).subscribe({
      next: (r) => {
        this.lockDate = r.lockDate ?? '';
        this.saved.set(true);
      },
      error: (e) => this.fail(e),
    });
  }
}

