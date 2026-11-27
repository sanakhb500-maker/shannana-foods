import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { SearchSelect } from '../shared/search-select';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { Adjustment, AdjustmentReason, AdjustmentRow, DocStatus, Paged, WRITE_ROLES } from '../core/models';

const today = () => new Date().toISOString().slice(0, 10);

/**
 * Stock counts, opening balances and write-offs. Each line states the COUNTED quantity;
 * confirming posts the difference against the system quantity at that moment.
 */
@Component({
  selector: 'app-adjustments-page',
  imports: [FormsModule, DecimalPipe, SearchSelect],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.adjustments') }}</h1>
      <div class="page-tools">
        <select class="input" [(ngModel)]="status" (ngModelChange)="reload()">
          <option value="">{{ i18n.t('inv.allStatuses') }}</option>
          @for (s of statuses; track s) {
            <option [value]="s">{{ i18n.t('status.' + s) }}</option>
          }
        </select>
        @if (canWrite) {
          <button class="btn primary" type="button" (click)="openNew()">+ {{ i18n.t('inv.newAdjustment') }}</button>
        } @else {
          <span class="badge muted">{{ i18n.t('common.readOnly') }}</span>
        }
      </div>
    </header>

    @if (message() && !doc()) {
      <div class="alert error">{{ message() }}</div>
    }

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('inv.number') }}</th>
            <th>{{ i18n.t('f.date') }}</th>
            <th>{{ i18n.t('f.warehouse') }}</th>
            <th>{{ i18n.t('inv.reason') }}</th>
            <th class="num">{{ i18n.t('inv.lines') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.id) {
            <tr class="clickable" (click)="open(r.id)">
              <td>{{ r.number ?? '—' }}</td>
              <td>{{ r.date }}</td>
              <td>{{ lookups.warehouseLabel(r.warehouseId) }}</td>
              <td>{{ i18n.t('reason.' + r.reason) }}</td>
              <td class="num">{{ r.lineCount }}</td>
              <td><span class="badge" [class.ok]="r.status === 'Confirmed'" [class.muted]="r.status === 'Cancelled'">{{ i18n.t('status.' + r.status) }}</span></td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="6">{{ i18n.t(loading() ? 'common.loading' : 'common.empty') }}</td></tr>
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

    @if (doc(); as d) {
      <div class="modal-backdrop" (click)="close()">
        <form class="modal wide-modal card" (click)="$event.stopPropagation()" (ngSubmit)="save()" novalidate>
          <h2>
            {{ i18n.t('nav.adjustments') }} {{ d.number ?? '' }}
            <span class="badge" [class.ok]="d.status === 'Confirmed'" [class.muted]="d.status === 'Cancelled'">{{ i18n.t('status.' + d.status) }}</span>
          </h2>

          @if (message()) {
            <div class="alert error">{{ message() }}</div>
          }

          <div class="form-grid">
            <div class="field">
              <label for="a-date">{{ i18n.t('f.date') }} <span class="req">*</span></label>
              <input id="a-date" class="input" type="date" name="date" [(ngModel)]="d.date" [disabled]="!editable()" />
            </div>
            <div class="field">
              <label for="a-wh">{{ i18n.t('f.warehouse') }} <span class="req">*</span></label>
              <app-search-select kind="warehouses" name="warehouse" [(ngModel)]="d.warehouseId" [disabled]="!editable()" />
            </div>
            <div class="field">
              <label for="a-reason">{{ i18n.t('inv.reason') }} <span class="req">*</span></label>
              <select id="a-reason" class="input" name="reason" [(ngModel)]="d.reason" [disabled]="!editable()">
                @for (r of reasons; track r) {
                  <option [ngValue]="r">{{ i18n.t('reason.' + r) }}</option>
                }
              </select>
            </div>
            <div class="field">
              <label for="a-notes">{{ i18n.t('inv.notes') }}</label>
              <input id="a-notes" class="input" name="notes" [(ngModel)]="d.notes" maxlength="500" [disabled]="!editable()" />
            </div>
          </div>

          <p class="hint-text">{{ i18n.t('inv.adjustHint') }}</p>

          <h3 class="sub">{{ i18n.t('inv.lines') }}</h3>
          <div class="table-wrap lines">
            <table class="table">
              <thead>
                <tr>
                  <th>{{ i18n.t('f.item') }}</th>
                  <th class="num">{{ i18n.t('inv.counted') }}</th>
                  <th class="num">{{ i18n.t('inv.surplusCost') }}</th>
                  @if (d.status !== 'Draft') {
                    <th class="num">{{ i18n.t('inv.systemQty') }}</th>
                    <th class="num">{{ i18n.t('inv.difference') }}</th>
                  }
                  @if (editable()) { <th></th> }
                </tr>
              </thead>
              <tbody>
                @for (l of d.lines; track $index; let i = $index) {
                  <tr>
                    <td>
                      <app-search-select kind="items" [name]="'item' + i" [(ngModel)]="l.itemId" [disabled]="!editable()" />
                    </td>
                    <td class="num"><input class="input qty" type="number" step="0.0001" min="0" [name]="'cnt' + i" [(ngModel)]="l.countedQuantity" [disabled]="!editable()" /></td>
                    <td class="num"><input class="input qty" type="number" step="0.0001" min="0" [name]="'cost' + i" [(ngModel)]="l.unitCost" [disabled]="!editable()" /></td>
                    @if (d.status !== 'Draft') {
                      <td class="num">{{ l.systemQuantity ?? '' | number: '1.0-4' }}</td>
                      <td class="num" [class.neg]="(l.difference ?? 0) < 0" [class.pos]="(l.difference ?? 0) > 0">{{ l.difference ?? '' | number: '1.0-4' }}</td>
                    }
                    @if (editable()) {
                      <td class="actions"><button class="btn sm danger" type="button" (click)="removeLine(i)" [attr.aria-label]="i18n.t('common.remove')">✕</button></td>
                    }
                  </tr>
                } @empty {
                  <tr><td class="empty" colspan="6">{{ i18n.t('inv.noLines') }}</td></tr>
                }
              </tbody>
            </table>
          </div>
          @if (editable()) {
            <button class="btn sm" type="button" (click)="addLine()">+ {{ i18n.t('inv.addLine') }}</button>
          }

          <div class="modal-actions">
            <button class="btn" type="button" (click)="close()">{{ i18n.t('common.close') }}</button>
            @if (canWrite && d.status === 'Draft') {
              <button class="btn" type="button" [disabled]="busy()" (click)="cancelDoc()">{{ i18n.t('inv.cancelDoc') }}</button>
              <button class="btn" type="submit" [disabled]="busy()">{{ i18n.t('common.save') }}</button>
              <button class="btn primary" type="button" [disabled]="busy()" (click)="confirm()">{{ i18n.t('inv.confirm') }}</button>
            }
            @if (canWrite && d.status === 'Confirmed') {
              <button class="btn danger" type="button" [disabled]="busy()" (click)="cancelDoc()">{{ i18n.t('inv.reverse') }}</button>
            }
          </div>
        </form>
      </div>
    }
  `,
})
export class AdjustmentsPage implements OnInit {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly canWrite = this.auth.hasAnyRole(WRITE_ROLES.inventory);
  readonly statuses: DocStatus[] = ['Draft', 'Confirmed', 'Cancelled'];
  readonly reasons: AdjustmentReason[] = ['Count', 'Opening', 'Damage', 'Other'];
  readonly rows = signal<AdjustmentRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / 20)));
  readonly loading = signal(false);
  readonly message = signal('');
  readonly doc = signal<Adjustment | null>(null);
  readonly busy = signal(false);
  status = '';

  readonly editable = computed(() => this.canWrite && this.doc()?.status === 'Draft');

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
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
    this.api.get<Paged<AdjustmentRow>>(`stockadjustments?page=${this.page()}&pageSize=20${this.status ? '&status=' + this.status : ''}`).subscribe({
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

  openNew(): void {
    this.message.set('');
    this.doc.set({ date: today(), warehouseId: null, reason: 'Count', status: 'Draft', notes: null, lines: [{ itemId: 0, countedQuantity: null, unitCost: null }] });
  }

  open(id: number): void {
    this.message.set('');
    this.api.get<Adjustment>(`stockadjustments/${id}`).subscribe({
      next: (a) => this.doc.set(a),
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  close(): void {
    this.doc.set(null);
  }

  addLine(): void {
    this.doc()?.lines.push({ itemId: 0, countedQuantity: null, unitCost: null });
  }

  removeLine(i: number): void {
    this.doc()?.lines.splice(i, 1);
  }

  private body() {
    const d = this.doc()!;
    return {
      date: d.date,
      warehouseId: d.warehouseId ?? 0,
      reason: d.reason,
      notes: d.notes?.trim() || null,
      lines: d.lines.map((l) => ({
        itemId: l.itemId,
        countedQuantity: Number(l.countedQuantity ?? 0),
        unitCost: l.unitCost === null || (l.unitCost as unknown) === '' ? null : Number(l.unitCost),
      })),
    };
  }

  private run(call: () => Observable<Adjustment>): void {
    this.busy.set(true);
    this.message.set('');
    call().subscribe({
      next: (a) => {
        this.busy.set(false);
        this.doc.set(a);
        this.load();
      },
      error: (e) => {
        const p = parseApiError(e);
        const detail = Object.values(p.fields).flat().join(' ');
        this.message.set(detail ? `${p.message} ${detail}` : p.message);
        this.busy.set(false);
      },
    });
  }

  save(): void {
    if (!this.editable()) return;
    const d = this.doc()!;
    this.run(() => (d.id ? this.api.update<Adjustment>('stockadjustments', d.id, this.body()) : this.api.create<Adjustment>('stockadjustments', this.body())));
  }

  /** Saves the draft first, then posts it to stock. */
  confirm(): void {
    const d = this.doc()!;
    this.busy.set(true);
    this.message.set('');
    const save$ = d.id ? this.api.update<Adjustment>('stockadjustments', d.id, this.body()) : this.api.create<Adjustment>('stockadjustments', this.body());
    save$.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.doc.set(saved);
        this.run(() => this.api.post<Adjustment>(`stockadjustments/${saved.id}/confirm`));
      },
      error: (e) => {
        const p = parseApiError(e);
        const detail = Object.values(p.fields).flat().join(' ');
        this.message.set(detail ? `${p.message} ${detail}` : p.message);
        this.busy.set(false);
      },
    });
  }

  cancelDoc(): void {
    const d = this.doc()!;
    if (!d.id) {
      this.close();
      return;
    }
    const q = d.status === 'Confirmed' ? 'inv.confirmReverse' : 'inv.confirmCancel';
    if (!confirm(this.i18n.t(q))) return;
    this.run(() => this.api.post<Adjustment>(`stockadjustments/${d.id}/cancel`));
  }
}
