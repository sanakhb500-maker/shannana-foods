import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { Paged } from '../core/models';
import { DOC_CONFIGS, DocConfig, DocField, ListCol } from './doc-config';

type Doc = Record<string, any> & { lines?: Record<string, any>[] };
type Row = Record<string, any> & { id: number };
type Option = { value: unknown; label: string };

const today = () => new Date().toISOString().slice(0, 10);

/**
 * One list + editor screen for any purchasing or sales document, driven by a {@link DocConfig}.
 * Draft -> Save / Confirm / Cancel; confirmed documents are read-only (cancelling reverses their effect).
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
  partyFilter: number | null = null;
  pendingOnly = false;

  readonly editable = computed(() => this.canWrite && (this.doc()?.['status'] ?? 'Draft') === 'Draft');
  readonly visibleLineCols = computed(() =>
    (this.cfg.lineCols ?? []).filter((c) => !c.afterDraft || (this.doc()?.['status'] ?? 'Draft') !== 'Draft'),
  );
  /** Shortcuts the current user may use (e.g. "create delivery" on a confirmed order). */
  readonly shortcuts = (this.cfg.shortcuts ?? []).filter((s) => this.auth.hasAnyRole(s.roles));

  /** A method, not a computed: line values are edited in place, which signals cannot see. */
  docTotal(): number {
    return (this.doc()?.lines ?? []).reduce((s, l) => s + Number(l['quantity'] ?? 0) * Number(l['unitPrice'] ?? 0), 0);
  }

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
    const fromOrder = Number(this.route.snapshot.queryParamMap.get('fromOrder'));
    if (fromOrder && this.cfg.order && this.canWrite) this.newFromOrder(fromOrder);
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
    if (this.partyFilter) qs.push(`${this.cfg.partyKey}=${this.partyFilter}`);
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
      case 'customer': return this.lookups.customerLabel(v);
      case 'warehouse': return this.lookups.warehouseLabel(v);
      case 'currency': return this.lookups.currencyLabel(v);
      case 'progress': return this.i18n.t('prog.' + v);
      case 'method': return this.i18n.t('pm.' + v);
      default: return v === null || v === undefined || v === '' ? (c.key === 'number' ? '—' : '') : String(v); // drafts have no number yet
    }
  }

  get partyOptions(): Option[] {
    return this.cfg.partyKey === 'supplierId'
      ? this.lookups.suppliers().map((s) => ({ value: s.id, label: this.lookups.supplierLabel(s.id) }))
      : this.lookups.customers().map((c) => ({ value: c.id, label: this.lookups.customerLabel(c.id) }));
  }

  // ---------- header field helpers ----------
  optionsFor(f: DocField, selected: unknown): Option[] {
    switch (f.options) {
      case 'suppliers': return this.lookups.suppliers().filter((s) => s.isActive || s.id === selected).map((s) => ({ value: s.id, label: this.lookups.supplierLabel(s.id) }));
      case 'customers': return this.lookups.customers().filter((c) => c.isActive || c.id === selected).map((c) => ({ value: c.id, label: this.lookups.customerLabel(c.id) }));
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
    if (f.disabledWhen === 'orderLinked') return !!this.orderId(d);
    return false;
  }

  private orderId(d: Doc): number | null {
    return this.cfg.order ? (d[this.cfg.order.idKey] ?? null) : null;
  }

  itemChoices(selected: number) {
    return this.lookups.items().filter((i) => i.isActive || i.id === selected);
  }

  /** Selling documents: suggest the item's sale price (converted to the document currency) when the price is still empty. */
  itemChanged(line: Record<string, any>): void {
    if (!this.cfg.priceFromItem || line['unitPrice'] !== null && line['unitPrice'] !== undefined && line['unitPrice'] !== '') return;
    const item = this.lookups.items().find((i) => i.id === line['itemId']);
    const d = this.doc();
    if (!item?.salePrice || !d) return;
    const rate = Number(d['exchangeRate']) > 0 ? Number(d['exchangeRate']) : 1;
    line['unitPrice'] = Math.round((item.salePrice / rate) * 10000) / 10000;
  }

  /** Reacts to header edits: keeps the exchange rate and order options in step. */
  changed(key: string): void {
    const d = this.doc();
    if (!d) return;
    if (key === 'currencyId' || key === 'date') this.refreshRate(d);
    if (key === this.cfg.partyKey) {
      if (this.cfg.order) {
        d[this.cfg.order.idKey] = null;
        this.loadOrderOptions(d[this.cfg.partyKey]);
      }
      const party = (this.cfg.partyKey === 'supplierId' ? this.lookups.suppliers() : this.lookups.customers()).find((p) => p.id === d[this.cfg.partyKey]);
      if (party?.currencyId && !d['id'] && !this.orderId(d)) {
        d['currencyId'] = party.currencyId;
        this.refreshRate(d);
      }
    }
    if (this.cfg.order && key === this.cfg.order.idKey) {
      if (d[key]) this.applyOrder(d[key]);
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

  private loadOrderOptions(partyId: number | null): void {
    const link = this.cfg.order;
    if (!link || !partyId) {
      this.orderOptions.set([]);
      return;
    }
    this.api.get<Paged<Row>>(`${link.endpoint}?status=Confirmed&${this.cfg.partyKey}=${partyId}&pageSize=200`).subscribe((r) => this.orderOptions.set(r.items));
  }

  /** Fills party, currency, rate and the lines still open (not yet received / delivered / invoiced) from the order. */
  private applyOrder(orderId: number): void {
    const link = this.cfg.order!;
    this.api.get<Doc & { lines: Row[] }>(`${link.endpoint}/${orderId}`).subscribe({
      next: (order) => {
        const d = this.doc();
        if (!d) return;
        d[this.cfg.partyKey] = order[this.cfg.partyKey];
        d['currencyId'] = order['currencyId'];
        d['exchangeRate'] = order['exchangeRate'];
        const lines = order.lines
          .map((l) => ({ [link.lineKey]: l['id'], itemId: l['itemId'], quantity: link.remaining(l), unitPrice: l['unitPrice'] }))
          .filter((l) => (l['quantity'] as number) > 0);
        d.lines = lines;
        this.message.set(lines.length ? '' : this.i18n.t('pur.nothingLeft'));
        this.loadOrderOptions(order[this.cfg.partyKey]);
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  private unlinkOrder(d: Doc): void {
    for (const l of d.lines ?? []) l[this.cfg.order!.lineKey] = null;
  }

  // ---------- editor ----------
  private blankLine(): Record<string, any> {
    return this.cfg.order
      ? { [this.cfg.order.lineKey]: null, itemId: 0, quantity: null, unitPrice: null }
      : { itemId: 0, quantity: null, unitPrice: null };
  }

  private blank(): Doc {
    const local = this.lookups.currencies().find((c) => c.isLocal)?.id ?? null;
    const d: Doc = { date: today(), [this.cfg.partyKey]: null, currencyId: local, exchangeRate: 1, notes: null, status: 'Draft', ...this.cfg.blankExtras?.() };
    if (this.cfg.lineCols) d.lines = [this.blankLine()];
    return d;
  }

  openNew(): void {
    this.message.set('');
    this.orderOptions.set([]);
    this.doc.set(this.blank());
  }

  /** Starts a document pre-filled from a confirmed order (used by the order screen's shortcuts). */
  newFromOrder(orderId: number): void {
    this.message.set('');
    const d = this.blank();
    d[this.cfg.order!.idKey] = orderId;
    this.doc.set(d);
    this.applyOrder(orderId);
  }

  open(id: number): void {
    this.message.set('');
    this.api.get<Doc>(`${this.cfg.endpoint}/${id}`).subscribe({
      next: (d) => {
        this.doc.set(d);
        this.loadOrderOptions(d[this.cfg.partyKey]);
        // an order that is no longer "Confirmed" would be missing from the options: make sure the linked one shows
        const oid = this.orderId(d);
        if (oid) {
          this.api.get<Row>(`${this.cfg.order!.endpoint}/${oid}`).subscribe((o) => {
            if (!this.orderOptions().some((x) => x['id'] === o['id'])) this.orderOptions.update((list) => [...list, o]);
          });
        }
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  close(): void {
    this.doc.set(null);
    if (this.route.snapshot.queryParamMap.has('fromOrder')) void this.router.navigate([], { queryParams: {} });
  }

  addLine(): void {
    this.doc()?.lines?.push(this.blankLine());
  }

  removeLine(i: number): void {
    this.doc()?.lines?.splice(i, 1);
  }

  /** From a confirmed order: jump to a new follow-on document pre-filled from it. */
  goShortcut(route: string): void {
    const id = this.doc()?.['id'];
    if (id) void this.router.navigate([route], { queryParams: { fromOrder: id } });
  }

  private num(v: unknown): number | null {
    return v === null || v === undefined || v === '' ? null : Number(v);
  }

  private body(): Record<string, unknown> {
    const d = this.doc()!;
    const b: Record<string, unknown> = {
      date: d['date'],
      [this.cfg.partyKey]: d[this.cfg.partyKey] ?? 0,
      currencyId: d['currencyId'] ?? 0,
      exchangeRate: this.num(d['exchangeRate']),
      notes: (d['notes'] as string | null)?.trim() || null,
      ...this.cfg.bodyExtras?.(d),
    };
    if (this.cfg.lineCols) {
      const lineKey = this.cfg.order?.lineKey;
      b['lines'] = (d.lines ?? []).map((l) => ({
        ...(lineKey ? { [lineKey]: l[lineKey] ?? null } : {}),
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
