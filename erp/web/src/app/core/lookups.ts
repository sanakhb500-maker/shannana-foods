import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, forkJoin, map } from 'rxjs';
import { Api } from './api';
import { I18n } from './i18n';
import { AccountDto, LookupCurrency, LookupCustomer, LookupDriver, LookupItem, LookupSupplier, LookupVehicle, LookupWarehouse } from './models';

export type LookupKind = 'items' | 'suppliers' | 'customers' | 'warehouses';

/** Cached reference data, used to label ids and fill selects on inventory and purchasing screens. */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);

  readonly items = signal<LookupItem[]>([]);
  readonly warehouses = signal<LookupWarehouse[]>([]);
  readonly suppliers = signal<LookupSupplier[]>([]);
  readonly currencies = signal<LookupCurrency[]>([]);
  readonly customers = signal<LookupCustomer[]>([]);
  readonly drivers = signal<LookupDriver[]>([]);
  readonly vehicles = signal<LookupVehicle[]>([]);
  readonly accounts = signal<AccountDto[]>([]);
  readonly activeItems = computed(() => this.items().filter((i) => i.isActive));
  readonly activeWarehouses = computed(() => this.warehouses().filter((w) => w.isActive));
  readonly activeSuppliers = computed(() => this.suppliers().filter((s) => s.isActive));
  readonly activeCurrencies = computed(() => this.currencies().filter((c) => c.isActive));
  private loaded = false;

  private signalOf(kind: LookupKind) {
    return { items: this.items, suppliers: this.suppliers, customers: this.customers, warehouses: this.warehouses }[kind] as unknown as
      import('@angular/core').WritableSignal<{ id: number; code: string; nameAr: string; nameEn: string; isActive: boolean }[]>;
  }

  endpointOf(kind: LookupKind): string {
    return kind;
  }

  /** Adds records the server returned (search results, single fetches) to the cache so their labels resolve even beyond the first 500. */
  remember(kind: LookupKind, rows: { id: number }[]): void {
    const sig = this.signalOf(kind);
    const known = new Set(sig().map((x) => x.id));
    const fresh = (rows as any[]).filter((r) => !known.has(r.id));
    if (fresh.length) sig.update((list) => [...list, ...fresh]);
  }

  labelOf(kind: LookupKind, id: number): string {
    const r = this.signalOf(kind)().find((x) => x.id === id);
    return r ? `${r.code} — ${this.i18n.name(r)}` : '';
  }

  fetchOne(kind: LookupKind, id: number): Observable<string> {
    return this.api.get<{ id: number; code: string; nameAr: string; nameEn: string; isActive: boolean }>(`${this.endpointOf(kind)}/${id}`).pipe(
      map((r) => {
        this.remember(kind, [r]);
        return this.labelOf(kind, id);
      }),
    );
  }

  /** Makes sure the given ids can be labelled (a list page showing a record outside the cached 500). */
  ensureIds(kind: LookupKind, ids: (number | null | undefined)[]): void {
    const known = new Set(this.signalOf(kind)().map((x) => x.id));
    const missing = [...new Set(ids.filter((i): i is number => !!i && !known.has(i)))];
    if (missing.length) forkJoin(missing.map((id) => this.fetchOne(kind, id))).subscribe({ error: () => {} });
  }

  /** Loads once; call `refresh()` to force. The cache only holds the first 500 of each; search selects fetch the rest on demand. */
  ensureLoaded(): void {
    if (this.loaded) return;
    this.refresh();
  }

  refresh(): void {
    this.loaded = true;
    this.api.list<LookupItem>('items', { pageSize: 500 }).subscribe((r) => this.items.set(r.items));
    this.api.list<LookupWarehouse>('warehouses', { pageSize: 500 }).subscribe((r) => this.warehouses.set(r.items));
    this.api.list<LookupSupplier>('suppliers', { pageSize: 500 }).subscribe((r) => this.suppliers.set(r.items));
    this.api.list<LookupCurrency>('currencies', { pageSize: 100 }).subscribe((r) => this.currencies.set(r.items));
    this.api.list<LookupCustomer>('customers', { pageSize: 500 }).subscribe((r) => this.customers.set(r.items));
    this.api.list<LookupDriver>('drivers', { pageSize: 500 }).subscribe((r) => this.drivers.set(r.items));
    this.api.list<LookupVehicle>('vehicles', { pageSize: 500 }).subscribe((r) => this.vehicles.set(r.items));
    this.api.list<AccountDto>('accounts', { pageSize: 500, postableOnly: true }).subscribe((r) => this.accounts.set(r.items));
  }

  itemLabel(id: number | null | undefined): string {
    const i = this.items().find((x) => x.id === id);
    return i ? `${i.code} — ${this.i18n.name(i)}` : id ? String(id) : '';
  }

  warehouseLabel(id: number | null | undefined): string {
    const w = this.warehouses().find((x) => x.id === id);
    return w ? `${w.code} — ${this.i18n.name(w)}` : id ? String(id) : '';
  }

  supplierLabel(id: number | null | undefined): string {
    const s = this.suppliers().find((x) => x.id === id);
    return s ? `${s.code} — ${this.i18n.name(s)}` : id ? String(id) : '';
  }

  customerLabel(id: number | null | undefined): string {
    const c = this.customers().find((x) => x.id === id);
    return c ? `${c.code} — ${this.i18n.name(c)}` : id ? String(id) : '';
  }

  driverLabel(id: number | null | undefined): string {
    return this.drivers().find((x) => x.id === id)?.name ?? (id ? String(id) : '');
  }

  vehicleLabel(id: number | null | undefined): string {
    const v = this.vehicles().find((x) => x.id === id);
    return v ? (v.description ? `${v.plateNumber} — ${v.description}` : v.plateNumber) : id ? String(id) : '';
  }

  currencyLabel(id: number | null | undefined): string {
    return this.currencies().find((x) => x.id === id)?.code ?? (id ? String(id) : '');
  }

  accountLabel(id: number | null | undefined): string {
    const a = this.accounts().find((x) => x.id === id);
    return a ? `${a.code} — ${this.i18n.name(a)}` : id ? String(id) : '';
  }

  isLocalCurrency(id: number | null | undefined): boolean {
    return this.currencies().find((x) => x.id === id)?.isLocal ?? false;
  }
}
