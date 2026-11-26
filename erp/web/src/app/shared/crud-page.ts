import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { CrudConfig, Field } from './crud-config';

type Row = Record<string, any> & { id: number; isActive: boolean };
type LookupRow = { id: number; nameAr?: string; nameEn?: string; name?: string; code?: string; plateNumber?: string };

/** One list + form screen, driven entirely by a {@link CrudConfig}; reused by every master-data page. */
@Component({
  selector: 'app-crud-page',
  imports: [FormsModule],
  templateUrl: './crud-page.html',
})
export class CrudPage implements OnInit {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  readonly i18n = inject(I18n);

  readonly cfg: CrudConfig = inject(ActivatedRoute).snapshot.data['config'];
  readonly canWrite = this.auth.hasAnyRole(this.cfg.writeRoles);
  readonly columns = this.cfg.fields.filter((f) => !f.noColumn && f.type !== 'textarea');

  readonly rows = signal<Row[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = 20;
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  readonly loading = signal(false);
  readonly lookups = signal<Record<string, LookupRow[]>>({});

  search = '';
  showInactive = false;
  private searchTimer?: ReturnType<typeof setTimeout>;

  // form state
  readonly editing = signal(false);
  readonly saving = signal(false);
  isNew = true;
  model: Record<string, any> = {};
  readonly message = signal('');
  readonly fieldErrors = signal<Record<string, string[]>>({});

  ngOnInit(): void {
    for (const f of this.cfg.fields) if (f.lookup) this.loadLookup(f.lookup);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .list<Row>(this.cfg.endpoint, {
        q: this.search.trim(),
        page: this.page(),
        pageSize: this.pageSize,
        activeOnly: !this.showInactive,
      })
      .subscribe({
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

  private loadLookup(endpoint: string): void {
    this.api.list<LookupRow>(endpoint, { pageSize: 500, activeOnly: true }).subscribe((r) =>
      this.lookups.update((l) => ({ ...l, [endpoint]: r.items })),
    );
  }

  onSearch(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.page.set(1);
      this.load();
    }, 300);
  }

  toggleInactive(): void {
    this.page.set(1);
    this.load();
  }

  go(delta: number): void {
    this.page.update((p) => Math.min(this.pages(), Math.max(1, p + delta)));
    this.load();
  }

  // ---- display helpers ----
  lookupLabel(f: Field, id: unknown): string {
    if (id === null || id === undefined) return '';
    const row = this.lookups()[f.lookup!]?.find((x) => x.id === id);
    if (!row) return String(id);
    const n = this.i18n.name(row);
    return n || row.code || row.plateNumber || String(id);
  }

  optionLabel(f: Field, value: unknown): string {
    const o = f.options?.find((x) => x.value === value);
    return o ? this.i18n.t(o.label) : String(value ?? '');
  }

  cell(row: Row, f: Field): string {
    const v = row[f.key];
    if (v === null || v === undefined || v === '') return '';
    if (f.type === 'checkbox') return v ? '✓' : '';
    if (f.lookup) return this.lookupLabel(f, v);
    if (f.options) return this.optionLabel(f, v);
    return String(v);
  }

  fieldError(key: string): string {
    return this.fieldErrors()[key]?.join(' ') ?? '';
  }

  lookupOptions(f: Field): LookupRow[] {
    return this.lookups()[f.lookup!] ?? [];
  }

  // ---- form ----
  openNew(): void {
    this.isNew = true;
    this.model = { ...this.cfg.defaults };
    this.resetFormState();
    this.editing.set(true);
  }

  openEdit(row: Row): void {
    this.isNew = false;
    this.model = { ...row };
    this.resetFormState();
    this.editing.set(true);
  }

  private resetFormState(): void {
    this.message.set('');
    this.fieldErrors.set({});
  }

  close(): void {
    this.editing.set(false);
  }

  save(): void {
    this.saving.set(true);
    this.resetFormState();
    const body = this.buildBody();
    const call = this.isNew
      ? this.api.create<Row>(this.cfg.endpoint, body)
      : this.api.update<Row>(this.cfg.endpoint, this.model['id'], body);
    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        // a new/changed lookup source (e.g. currencies) must be visible to dependent forms
        for (const f of this.cfg.fields) if (f.lookup) this.loadLookup(f.lookup);
        this.load();
      },
      error: (e) => {
        const p = parseApiError(e);
        this.message.set(p.message);
        this.fieldErrors.set(p.fields);
        this.saving.set(false);
      },
    });
  }

  private buildBody(): Record<string, unknown> {
    const body: Record<string, unknown> = { ...this.model };
    for (const f of this.cfg.fields) {
      let v = body[f.key];
      if (v === '' || v === undefined) v = f.type === 'checkbox' ? false : null;
      if (f.type === 'select' && f.nullable && (v === 0 || v === '0')) v = null;
      body[f.key] = v;
      // optional strings: null is fine for the API, required ones are validated server-side
      if (f.type === 'text' && v === null && f.required) body[f.key] = '';
    }
    return body;
  }

  setActive(row: Row, active: boolean): void {
    if (!active && !confirm(this.i18n.t('common.confirmDeactivate'))) return;
    const done = () => this.load();
    const fail = (e: unknown) => this.message.set(parseApiError(e).message);
    if (active) this.api.update<Row>(this.cfg.endpoint, row.id, { ...row, isActive: true }).subscribe({ next: done, error: fail });
    else this.api.deactivate<Row>(this.cfg.endpoint, row.id).subscribe({ next: done, error: fail });
  }
}
