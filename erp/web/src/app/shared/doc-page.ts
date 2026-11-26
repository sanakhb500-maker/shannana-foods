import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { Paged, WRITE_ROLES } from '../core/models';
import { DOC_CONFIGS, DocConfig, DocField, ListCol } from './doc-config';

type Doc = Record<string, any> & { lines?: Record<string, any>[] };
type Row = Record<string, any> & { id: number };
type Option = { value: unknown; label: string };

const today = () => new Date().toISOString().slice(0, 10);

/**
 * One list + editor screen for any purchasing document (order, receipt, invoice, payment),
 * driven by a {@link DocConfig}. Draft -> Save / Confirm / Cancel; confirmed documents are read-only.
 */
@Component({
  selector: 'app-doc-page',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './doc-page.html',
})
export class DocPage implements OnInit {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly cfg: DocConfig = DOC_CONFIGS[this.route.snapshot.data['kind'] as keyof typeof DOC_CONFIGS];
  readonly canWrite = this.auth.hasAnyRole(this.cfg.writeRoles);
  readonly canCreateReceipt = this.auth.hasAnyRole(WRITE_ROLES.goodsReceipts);
  readonly canCreateInvoice = this.auth.hasAnyRole(WRITE_ROLES.purchasing);
  readonly statuses = ['Draft', 'Confirmed', 'Cancelled'];

  readonly rows = signal<Row[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / 20)));
  readonly loading = signal(false);
  readonly message = signal('');
  readonly doc = signal<Doc | null>(null);
  readonly busy = signal(false);
  readonly orderOptions = signal<Row[]>([]);
  status = '';
  supplierFilter: number | null = null;
  pendingOnly = false;

  readonly editable = computed(() => this.canWrite && (this.doc()?.['status'] ?? 'Draft') === 'Draft');
  readonly visibleLineCols = computed(() =>
    (this.cfg.lineCols ?? []).filter((c) => !c.afterDraft || (this.doc()?.['status'] ?? 'Draft') !== 'Draft'),
  );
  /** A method, not a computed: line values are edited in place, which signals cannot see. */
  docTotal(): number {
    return (this.doc()?.lines ?? []).reduce((s, l) => s + Number(l['quantity'] ?? 0) * Number(l['unitPrice'] ?? 0), 0);
  }

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
    const fromPo = Number(this.route.snapshot.queryParamMap.get('fromPo'));
    if (fromPo && this.cfg.hasOrderLink && this.canWrite) this.newFromOrder(fromPo);
  }

  // ---------- list ----------
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
    const qs = [`page=${this.page()}`, 'pageSize=20'];
    if (this.status) qs.push(`status=${this.status}`);
    if (this.supplierFilter) qs.push(`supplierId=${this.supplierFilter}`);
    if (this.pendingOnly) qs.push('pendingOnly=true');
    this.api.get<Paged<Row>>(`${this.cfg.endpoint}?${qs.join('&')}`).subscribe({
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

  cell(row: Row, c: ListCol): string {
    const v = row[c.key];
    switch (c.kind) {
      case 'date': return v ?? '';
      case 'supplier': return this.lookups.supplierLabel(v);
      case 'warehouse': return this.lookups.warehouseLabel(v);
      case 'currency': return this.lookups.currencyLabel(v);
      case 'receiptState':
      case 'invoiceState': return this.i18n.t('prog.' + v);
      case 'method': return this.i18n.t('pm.' + v);
      default: return v === null || v === undefined ? '' : String(v);
    }
  }

  // ---------- header field helpers ----------
  optionsFor(f: DocField, selected: unknown): Option[] {
    switch (f.options) {
      case 'suppliers': return this.lookups.suppliers().filter((s) => s.isActive || s.id === selected).map((s) => ({ value: s.id, label: this.lookups.supplierLabel(s.id) }));
      case 'warehouses': return this.lookups.warehouses().filter((w) => w.isActive || w.id === selected).map((w) => ({ value: w.id, label: this.lookups.warehouseLabel(w.id) }));
      case 'currencies': return this.lookups.currencies().filter((c) => c.isActive || c.id === selected).map((c) => ({ value: c.id, label: `${c.code} — ${this.i18n.name(c)}` }));
      case 'orders': return this.orderOptions().map((o) => ({ value: o['id'], label: `${o['number']} · ${o['date']}` }));
      case 'methods': return ['Cash', 'BankTransfer', 'Cheque'].map((m) => ({ value: m, label: this.i18n.t('pm.' + m) }));
      default: return [];
    }
  }

  fieldDisabled(f: DocField, d: Doc): boolean {
    if (!this.editable()) return true;
    if (f.disabledWhen === 'localCurrency') return this.lookups.isLocalCurrency(d['currencyId']);
    if (f.disabledWhen === 'orderLinked') return !!d['purchaseOrderId'];
    return false;
  }

  itemChoices(selected: number) {
    return this.lookups.items().filter((i) => i.isActive || i.id === selected);
  }

  /** Reacts to header edits: keeps the exchange rate and order options in step. */
  changed(key: string): void {
    const d = this.doc();
    if (!d) return;
    if (this.cfg.hasRate && (key === 'currencyId' || key === 'date')) this.refreshRate(d);
    if (key === 'supplierId') {
      if (this.cfg.hasOrderLink) {
        d['purchaseOrderId'] = null;
        this.loadOrderOptions(d['supplierId']);
      }
      const sup = this.lookups.suppliers().find((s) => s.id === d['supplierId']);
      if (sup?.currencyId && !d['id'] && !d['purchaseOrderId']) {
        d['currencyId'] = sup.currencyId;
        this.refreshRate(d);
      }
    }
    if (key === 'purchaseOrderId') {
      if (d['purchaseOrderId']) this.applyOrder(d['purchaseOrderId']);
      else this.unlinkOrder(d);
    }
  }

  private refreshRate(d: Doc): void {
    if (this.lookups.isLocalCurrency(d['currencyId'])) {
      d['exchangeRate'] = 1;
      return;
    }
    if (!d['currencyId']) return;
    this.api.list<Row>('exchangerates', { currencyId: d['currencyId'], pageSize: 200, activeOnly: true }).subscribe((r) => {
      const latest = r.items
        .filter((x) => (x['date'] as string) <= (d['date'] ?? today()))
        .sort((a, b) => (b['date'] as string).localeCompare(a['date'] as string))[0];
      d['exchangeRate'] = latest ? latest['rate'] : null; // empty = the user must enter one
    });
  }

  private loadOrderOptions(supplierId: number | null): void {
    if (!supplierId) {
      this.orderOptions.set([]);
      return;
    }
    this.api.get<Paged<Row>>(`purchaseorders?status=Confirmed&supplierId=${supplierId}&pageSize=200`).subscribe((r) => this.orderOptions.set(r.items));
  }

  /** Fills supplier, currency, rate and the not-yet-received (or not-yet-invoiced) lines from a purchase order. */
  private applyOrder(orderId: number): void {
    this.api.get<Doc & { id: number; supplierId: number; currencyId: number; exchangeRate: number; lines: Row[] }>(`purchaseorders/${orderId}`).subscribe({
      next: (po) => {
        const d = this.doc();
        if (!d) return;
        d['supplierId'] = po.supplierId;
        d['currencyId'] = po.currencyId;
        d['exchangeRate'] = po.exchangeRate;
        const lines = po.lines
          .map((l) => {
            const left = this.cfg.kind === 'receipt' ? l['quantity'] - l['receivedQuantity'] : l['receivedQuantity'] - l['invoicedQuantity'];
            return { purchaseOrderLineId: l['id'], itemId: l['itemId'], quantity: left, unitPrice: l['unitPrice'] };
          })
          .filter((l) => l.quantity > 0);
        d.lines = lines;
        this.message.set(lines.length ? '' : this.i18n.t('pur.nothingLeft'));
        this.loadOrderOptions(po.supplierId);
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  private unlinkOrder(d: Doc): void {
    for (const l of d.lines ?? []) l['purchaseOrderLineId'] = null;
  }

  // ---------- editor ----------
  private blankLine(): Record<string, any> {
    return this.cfg.kind === 'order'
      ? { itemId: 0, quantity: null, unitPrice: null }
      : { purchaseOrderLineId: null, itemId: 0, quantity: null, unitPrice: null };
  }

  private blank(): Doc {
    const local = this.lookups.currencies().find((c) => c.isLocal)?.id ?? null;
    const d: Doc = { date: today(), supplierId: null, currencyId: local, exchangeRate: 1, notes: null, status: 'Draft' };
    if (this.cfg.lineCols) d.lines = [this.blankLine()];
    if (this.cfg.kind === 'order') d['expectedDate'] = null;
    if (this.cfg.kind === 'receipt') Object.assign(d, { warehouseId: null, purchaseOrderId: null });
    if (this.cfg.kind === 'invoice') Object.assign(d, { dueDate: null, supplierInvoiceNumber: null, purchaseOrderId: null });
    if (this.cfg.kind === 'payment') Object.assign(d, { amount: null, method: 'Cash', reference: null });
    return d;
  }

  openNew(): void {
    this.message.set('');
    this.orderOptions.set([]);
    this.doc.set(this.blank());
  }

  /** Starts a receipt/invoice pre-filled from a confirmed purchase order (used by the order screen's shortcuts). */
  newFromOrder(orderId: number): void {
    this.message.set('');
    const d = this.blank();
    d['purchaseOrderId'] = orderId;
    this.doc.set(d);
    this.applyOrder(orderId);
  }

  open(id: number): void {
    this.message.set('');
    this.api.get<Doc>(`${this.cfg.endpoint}/${id}`).subscribe({
      next: (d) => {
        this.doc.set(d);
        this.loadOrderOptions(d['supplierId']);
        // an order that is no longer "Confirmed" would be missing from the options: make sure the linked one shows
        if (d['purchaseOrderId']) {
          this.api.get<Row>(`purchaseorders/${d['purchaseOrderId']}`).subscribe((po) => {
            if (!this.orderOptions().some((o) => o['id'] === po['id'])) this.orderOptions.update((o) => [...o, po]);
          });
        }
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  close(): void {
    this.doc.set(null);
    if (this.route.snapshot.queryParamMap.has('fromPo')) void this.router.navigate([], { queryParams: {} });
  }

  addLine(): void {
    this.doc()?.lines?.push(this.blankLine());
  }

  removeLine(i: number): void {
    this.doc()?.lines?.splice(i, 1);
  }

  /** From a confirmed order: jump to a new receipt/invoice pre-filled from it. */
  createFromOrder(target: 'goods-receipts' | 'purchase-invoices'): void {
    const id = this.doc()?.['id'];
    if (id) void this.router.navigate(['/' + target], { queryParams: { fromPo: id } });
  }

  private num(v: unknown): number | null {
    return v === null || v === undefined || v === '' ? null : Number(v);
  }

  private body(): Record<string, unknown> {
    const d = this.doc()!;
    const b: Record<string, unknown> = {
      date: d['date'],
      supplierId: d['supplierId'] ?? 0,
      currencyId: d['currencyId'] ?? 0,
      exchangeRate: this.num(d['exchangeRate']),
      notes: (d['notes'] as string | null)?.trim() || null,
    };
    switch (this.cfg.kind) {
      case 'order': b['expectedDate'] = d['expectedDate'] || null; break;
      case 'receipt': Object.assign(b, { warehouseId: d['warehouseId'] ?? 0, purchaseOrderId: d['purchaseOrderId'] ?? null }); break;
      case 'invoice':
        Object.assign(b, { dueDate: d['dueDate'] || null, supplierInvoiceNumber: (d['supplierInvoiceNumber'] as string | null)?.trim() || null, purchaseOrderId: d['purchaseOrderId'] ?? null });
        break;
      case 'payment': Object.assign(b, { amount: Number(d['amount'] ?? 0), method: d['method'], reference: (d['reference'] as string | null)?.trim() || null }); break;
    }
    if (this.cfg.lineCols) {
      b['lines'] = (d.lines ?? []).map((l) => ({
        ...(this.cfg.kind === 'order' ? {} : { purchaseOrderLineId: l['purchaseOrderLineId'] ?? null }),
        itemId: l['itemId'],
        quantity: Number(l['quantity'] ?? 0),
        unitPrice: Number(l['unitPrice'] ?? 0),
      }));
    }
    return b;
  }

  private errorText(e: unknown): string {
    const p = parseApiError(e);
    const detail = Object.values(p.fields).flat().join(' ');
    return detail ? `${p.message} ${detail}` : p.message;
  }

  private run(call: () => Observable<Doc>): void {
    this.busy.set(true);
    this.message.set('');
    call().subscribe({
      next: (d) => {
        this.busy.set(false);
        this.doc.set(d);
        this.load();
      },
      error: (e) => {
        this.message.set(this.errorText(e));
        this.busy.set(false);
      },
    });
  }

  private saveCall(): Observable<Doc> {
    const d = this.doc()!;
    return d['id'] ? this.api.update<Doc>(this.cfg.endpoint, d['id'], this.body()) : this.api.create<Doc>(this.cfg.endpoint, this.body());
  }

  save(): void {
    if (this.editable()) this.run(() => this.saveCall());
  }

  /** Saves the draft first, then confirms it. */
  confirm(): void {
    this.busy.set(true);
    this.message.set('');
    this.saveCall().subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.doc.set(saved);
        this.run(() => this.api.post<Doc>(`${this.cfg.endpoint}/${saved['id']}/confirm`));
      },
      error: (e) => {
        this.message.set(this.errorText(e));
        this.busy.set(false);
      },
    });
  }

  cancelDoc(): void {
    const d = this.doc()!;
    if (!d['id']) {
      this.close();
      return;
    }
    if (!confirm(this.i18n.t(d['status'] === 'Confirmed' ? 'pur.confirmReverse' : 'inv.confirmCancel'))) return;
    this.run(() => this.api.post<Doc>(`${this.cfg.endpoint}/${d['id']}/cancel`));
  }
}
