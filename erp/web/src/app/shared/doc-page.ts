import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { OpenInvoice, Paged } from '../core/models';
import { DOC_CONFIGS, DocConfig, DocField, ListCol } from './doc-config';
import { SearchSelect } from './search-select';
import { ListSearch } from './list-search';
import { LookupKind } from '../core/lookups';

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
  imports: [FormsModule, DecimalPipe, SearchSelect, ListSearch],
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
  /** Open invoices of the document's party in the document's currency: what a payment / receipt can be allocated to. */
  readonly openInvoices = signal<OpenInvoice[]>([]);
  allocMap: Record<string, number | null> = {};
  status = '';
  partyFilter: number | null = null;
  pendingOnly = false;
  q = '';

  onQuery(v: string): void {
    this.q = v;
    this.reload();
  }

  readonly editable = computed(() => this.canWrite && (this.doc()?.['status'] ?? 'Draft') === 'Draft');
  readonly visibleLineCols = computed(() =>
    (this.cfg.lineCols ?? []).filter((c) => !c.afterDraft || (this.doc()?.['status'] ?? 'Draft') !== 'Draft'),
  );
  /** Shortcuts the current user may use (e.g. "create delivery" on a confirmed order). */
  readonly shortcuts = (this.cfg.shortcuts ?? []).filter((s) => this.auth.hasAnyRole(s.roles));

  /** A method, not a computed: line values are edited in place, which signals cannot see. */
  docTotal(): number {
    const expense = this.cfg.lineKind === 'expense';
    const lines = (this.doc()?.lines ?? []).reduce((s, l) => s + (expense ? Number(l['amount'] ?? 0) : this.lineTotal(l)), 0);
    return this.cfg.headerDiscount ? lines - Number(this.doc()?.['discountAmount'] ?? 0) : lines;
  }

  /** Quantity x price less the line discount. */
  lineTotal(l: Record<string, any>): number {
    const gross = Number(l['quantity'] ?? 0) * Number(l['unitPrice'] ?? 0);
    return this.cfg.lineDiscount ? gross * (1 - Number(l['discountPercent'] ?? 0) / 100) : gross;
  }

  /** The party column key, or '' for documents with none. */
  private get pk(): string {
    return this.cfg.partyKey ?? '';
  }

  expenseAccounts(selected: number) {
    return this.lookups.accounts().filter((a) => (a.type === 'Expense' && a.isActive) || a.id === selected);
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
    if (this.q) qs.push(`q=${encodeURIComponent(this.q)}`);
    this.api.get<Paged<Row>>(`${this.cfg.endpoint}?${qs.join('&')}`).subscribe({
      next: (r) => {
        this.rows.set(r.items);
        // labels for records beyond the cached first 500
        for (const c of this.cfg.listCols) {
          const kind = c.kind === 'supplier' ? 'suppliers' : c.kind === 'customer' ? 'customers' : c.kind === 'warehouse' ? 'warehouses' : null;
          if (kind) this.lookups.ensureIds(kind, r.items.map((x) => x[c.key]));
        }
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

  // ---------- header field helpers ----------
  /** Which header selects use the server-side search (large lists) instead of a plain dropdown. */
  searchKind(f: DocField): LookupKind | null {
    return f.options === 'suppliers' || f.options === 'customers' || f.options === 'warehouses' ? f.options : null;
  }

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

  /** Key of the per-line link to an order line; absent for documents that only reference a header (returns). */
  private get lineKey(): string | undefined {
    return this.cfg.order?.lineKey;
  }

  /** Lines come from a linked order and cannot be added or re-targeted. */
  linesLocked(d: Doc): boolean {
    return !!this.lineKey && !!this.orderId(d);
  }

  lineLinked(l: Record<string, any>): boolean {
    const k = this.lineKey;
    return !!k && !!l[k];
  }

  private orderId(d: Doc): number | null {
    return this.cfg.order ? (d[this.cfg.order.idKey] ?? null) : null;
  }

  /** Selling documents: suggest the item's sale price (converted to the document currency) when the price is still empty. */
  itemChanged(line: Record<string, any>): void {
    if (!this.cfg.priceFromItem || line['unitPrice'] !== null && line['unitPrice'] !== undefined && line['unitPrice'] !== '') return;
    const item = this.lookups.items().find((i) => i.id === line['itemId']);
    const d = this.doc();
    if (!item || !d) return;
    // two price lists: distributors get the distributor price when the item has one
    const distributor = this.lookups.customers().find((c) => c.id === d['customerId'])?.type === 'Distributor';
    const price = distributor && item.distributorPrice ? item.distributorPrice : item.salePrice;
    if (!price) return;
    const rate = Number(d['exchangeRate']) > 0 ? Number(d['exchangeRate']) : 1;
    line['unitPrice'] = Math.round((price / rate) * 10000) / 10000;
  }

  /** Reacts to header edits: keeps the exchange rate and order options in step. */
  changed(key: string): void {
    const d = this.doc();
    if (!d) return;
    if (key === 'currencyId' || key === 'date') this.refreshRate(d);
    if (this.cfg.allocations && (key === this.cfg.partyKey || key === 'currencyId')) this.resetAllocations();
    if (key === this.cfg.partyKey) {
      if (this.cfg.order) {
        d[this.cfg.order.idKey] = null;
        this.loadOrderOptions(d[this.pk]);
      }
      const party = (this.cfg.partyKey === 'supplierId' ? this.lookups.suppliers() : this.lookups.customers()).find((p) => p.id === d[this.pk]);
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
        d[this.pk] = order[this.pk];
        d['currencyId'] = order['currencyId'];
        d['exchangeRate'] = order['exchangeRate'];
        const lines = order.lines
          .map((l) => ({ ...(link.lineKey ? { [link.lineKey]: l['id'] } : {}), itemId: l['itemId'], quantity: link.remaining(l), unitPrice: link.price ? link.price(l) : l['unitPrice'],
            ...(this.cfg.lineDiscount ? { discountPercent: l['discountPercent'] ?? 0 } : {}) }))
          .filter((l) => (l['quantity'] as number) > 0);
        d.lines = lines;
        this.message.set(lines.length ? '' : this.i18n.t('pur.nothingLeft'));
        this.loadOrderOptions(order[this.pk]);
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  private unlinkOrder(d: Doc): void {
    const k = this.lineKey;
    if (k) for (const l of d.lines ?? []) l[k] = null;
  }

  // ---------- editor ----------
  private blankLine(): Record<string, any> {
    if (this.cfg.lineKind === 'expense') return { accountId: 0, description: null, amount: null };
    const extra = this.cfg.lineDiscount ? { discountPercent: 0 } : {};
    return this.lineKey
      ? { [this.lineKey]: null, itemId: 0, quantity: null, unitPrice: null, ...extra }
      : { itemId: 0, quantity: null, unitPrice: null, ...extra };
  }

  private blank(): Doc {
    const local = this.lookups.currencies().find((c) => c.isLocal)?.id ?? null;
    const d: Doc = { date: today(), ...(this.cfg.partyKey ? { [this.cfg.partyKey]: null } : {}), currencyId: local, exchangeRate: 1, notes: null, status: 'Draft', ...this.cfg.blankExtras?.() };
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
        this.loadOrderOptions(d[this.pk]);
        this.initAllocations(d);
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
      ...(this.cfg.partyKey ? { [this.cfg.partyKey]: d[this.cfg.partyKey] ?? 0 } : {}),
      currencyId: d['currencyId'] ?? 0,
      exchangeRate: this.num(d['exchangeRate']),
      notes: (d['notes'] as string | null)?.trim() || null,
      ...this.cfg.bodyExtras?.(d),
    };
    if (this.cfg.allocations) b['allocations'] = this.allocationBody();
    if (this.cfg.lineCols && this.cfg.lineKind === 'expense') {
      b['lines'] = (d.lines ?? []).map((l) => ({ accountId: l['accountId'], description: (l['description'] as string | null)?.trim() || null, amount: Number(l['amount'] ?? 0) }));
    } else if (this.cfg.lineCols) {
      const lineKey = this.lineKey;
      b['lines'] = (d.lines ?? []).map((l) => ({
        ...(lineKey ? { [lineKey]: l[lineKey] ?? null } : {}),
        itemId: l['itemId'],
        quantity: Number(l['quantity'] ?? 0),
        unitPrice: Number(l['unitPrice'] ?? 0),
        ...(this.cfg.lineDiscount ? { discountPercent: Number(l['discountPercent'] ?? 0) } : {}),
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

  // ---------- allocation of a payment / receipt to open invoices ----------
  private allocKey(kind: string, id: number): string {
    return `${kind}:${id}`;
  }

  /** Loads the allocation map from a saved document and the open invoices it can still be allocated to. */
  private initAllocations(d: Doc): void {
    this.allocMap = {};
    if (!this.cfg.allocations) return;
    for (const a of (d['allocations'] ?? []) as Record<string, any>[]) {
      const kind = a['purchaseInvoiceId'] ? 'PurchaseInvoice' : a['expenseInvoiceId'] ? 'ExpenseInvoice' : 'SalesInvoice';
      this.allocMap[this.allocKey(kind, a['purchaseInvoiceId'] ?? a['expenseInvoiceId'] ?? a['salesInvoiceId'])] = a['amount'];
    }
    this.loadOpenInvoices();
  }

  loadOpenInvoices(): void {
    const d = this.doc();
    if (!this.cfg.allocations || !d || d['status'] !== 'Draft' || !d[this.pk]) {
      this.openInvoices.set([]);
      return;
    }
    const url = this.cfg.allocations === 'supplier' ? `purchasing/open-invoices?supplierId=${d[this.pk]}` : `sales/open-invoices?customerId=${d[this.pk]}`;
    this.api.get<OpenInvoice[]>(url).subscribe((rows) => this.openInvoices.set(rows.filter((r) => r.currencyId === d['currencyId'])));
  }

  /** The party or currency changed: what was assigned no longer applies. */
  private resetAllocations(): void {
    this.allocMap = {};
    this.loadOpenInvoices();
  }

  allocKeyOf(r: OpenInvoice): string {
    return this.allocKey(r.kind, r.invoiceId);
  }

  allocatedTotal(): number {
    return Object.values(this.allocMap).reduce<number>((s, v) => s + Number(v ?? 0), 0);
  }

  /** Oldest first, up to the payment amount. */
  autoAllocate(): void {
    let left = Number(this.doc()?.['amount'] ?? 0);
    this.allocMap = {};
    for (const r of this.openInvoices()) {
      const take = Math.min(left, r.outstanding);
      if (take > 0) this.allocMap[this.allocKeyOf(r)] = Math.round(take * 10000) / 10000;
      left -= take;
    }
  }

  clearAllocations(): void {
    this.allocMap = {};
  }

  private allocationBody(): { kind: string; invoiceId: number; amount: number }[] {
    return Object.entries(this.allocMap)
      .filter(([, v]) => Number(v ?? 0) > 0)
      .map(([k, v]) => {
        const [kind, id] = k.split(':');
        return { kind, invoiceId: Number(id), amount: Number(v) };
      });
  }

  savedAllocations(): { label: string; amount: number }[] {
    return ((this.doc()?.['allocations'] ?? []) as Record<string, any>[]).map((a) => ({
      label: a['purchaseInvoiceId'] ? `PINV #${a['purchaseInvoiceId']}` : a['expenseInvoiceId'] ? `EXP #${a['expenseInvoiceId']}` : `SINV #${a['salesInvoiceId']}`,
      amount: a['amount'],
    }));
  }
}
