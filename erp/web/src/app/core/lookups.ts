import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from './api';
import { I18n } from './i18n';
import { LookupItem, LookupWarehouse } from './models';

/** Cached items and warehouses, used to label ids and fill selects on inventory screens. */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);

  readonly items = signal<LookupItem[]>([]);
  readonly warehouses = signal<LookupWarehouse[]>([]);
  readonly activeItems = computed(() => this.items().filter((i) => i.isActive));
  readonly activeWarehouses = computed(() => this.warehouses().filter((w) => w.isActive));
  private loaded = false;

  /** Loads once; call `refresh()` to force. (Limit: the first 500 items.) */
  ensureLoaded(): void {
    if (this.loaded) return;
    this.refresh();
  }

  refresh(): void {
    this.loaded = true;
    this.api.list<LookupItem>('items', { pageSize: 500 }).subscribe((r) => this.items.set(r.items));
    this.api.list<LookupWarehouse>('warehouses', { pageSize: 500 }).subscribe((r) => this.warehouses.set(r.items));
  }

  itemLabel(id: number | null | undefined): string {
    const i = this.items().find((x) => x.id === id);
    return i ? `${i.code} — ${this.i18n.name(i)}` : id ? String(id) : '';
  }

  warehouseLabel(id: number | null | undefined): string {
    const w = this.warehouses().find((x) => x.id === id);
    return w ? `${w.code} — ${this.i18n.name(w)}` : id ? String(id) : '';
  }
}
