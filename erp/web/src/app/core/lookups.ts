import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from './api';
import { I18n } from './i18n';
import { LookupCurrency, LookupItem, LookupSupplier, LookupWarehouse } from './models';

/** Cached reference data, used to label ids and fill selects on inventory and purchasing screens. */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);

  readonly items = signal<LookupItem[]>([]);
  readonly warehouses = signal<LookupWarehouse[]>([]);
  readonly suppliers = signal<LookupSupplier[]>([]);
  readonly currencies = signal<LookupCurrency[]>([]);
  readonly activeItems = computed(() => this.items().filter((i) => i.isActive));
  readonly activeWarehouses = computed(() => this.warehouses().filter((w) => w.isActive));
  readonly activeSuppliers = computed(() => this.suppliers().filter((s) => s.isActive));
  readonly activeCurrencies = computed(() => this.currencies().filter((c) => c.isActive));
  private loaded = false;

  /** Loads once; call `refresh()` to force. (Limit: the first 500 of each.) */
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

  currencyLabel(id: number | null | undefined): string {
    return this.currencies().find((x) => x.id === id)?.code ?? (id ? String(id) : '');
  }

  isLocalCurrency(id: number | null | undefined): boolean {
    return this.currencies().find((x) => x.id === id)?.isLocal ?? false;
  }
}
