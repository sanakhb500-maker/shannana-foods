import { Component, forwardRef, inject, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { Api } from '../core/api';
import { I18n } from '../core/i18n';
import { Lookups, LookupKind } from '../core/lookups';

interface Hit { id: number; code?: string; nameAr?: string; nameEn?: string }

/**
 * A select backed by the server: type to search (code / name), the list shows the best 20 matches.
 * Works with ngModel (value = id). It does not depend on the first-500 lookup cache, so it keeps working with any number of records.
 */
@Component({
  selector: 'app-search-select',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SearchSelect), multi: true }],
  template: `
    <div class="ss" (focusout)="onBlur($event)">
      <input class="input" type="text" autocomplete="off" [disabled]="disabled()" [value]="open() ? term() : label()"
             [placeholder]="label() || placeholder() || i18n.t('common.search')" (focus)="openList()" (input)="type($any($event.target).value)" />
      @if (clearable() && value && !disabled()) {
        <button type="button" class="ss-clear" (mousedown)="clear($event)" aria-label="clear">✕</button>
      }
      @if (open()) {
        <ul class="ss-list" role="listbox">
          @for (h of hits(); track h.id) {
            <li role="option" tabindex="-1" (mousedown)="pick(h)" [class.sel]="h.id === value">{{ text(h) }}</li>
          } @empty {
            <li class="muted">{{ i18n.t(loading() ? 'common.loading' : 'common.empty') }}</li>
          }
        </ul>
      }
    </div>
  `,
  styles: [`
    .ss { position: relative; }
    .ss-clear { position: absolute; inset-inline-end: .4rem; top: 50%; transform: translateY(-50%); border: 0; background: none; cursor: pointer; color: var(--muted, #888); }
    .ss-list { position: absolute; z-index: 60; inset-inline: 0; margin: .15rem 0 0; padding: .2rem 0; list-style: none; max-height: 14rem; overflow-y: auto;
      background: var(--surface, #fff); border: 1px solid var(--border, #ccc); border-radius: 8px; box-shadow: 0 6px 18px rgb(0 0 0 / 15%); }
    .ss-list li { padding: .4rem .7rem; cursor: pointer; }
    .ss-list li:hover, .ss-list li.sel { background: color-mix(in srgb, var(--primary, #7a1f2b) 10%, transparent); }
    .ss-list li.muted { cursor: default; color: var(--muted, #888); }
  `],
})
export class SearchSelect implements ControlValueAccessor {
  private readonly api = inject(Api);
  private readonly lookups = inject(Lookups);
  readonly i18n = inject(I18n);

  readonly kind = input.required<LookupKind>();
  /** Only active records can be newly chosen (the current value always displays). */
  readonly activeOnly = input(true);
  /** Shows a ✕ to go back to "no selection" (filters). */
  readonly clearable = input(false);
  /** Text shown when nothing is selected. */
  readonly placeholder = input('');

  value: number | null = null;
  readonly label = signal('');
  readonly term = signal('');
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly hits = signal<Hit[]>([]);
  readonly disabled = signal(false);

  private readonly queries = new Subject<string>();
  private onChange: (v: number | null) => void = () => {};
  private onTouched: () => void = () => {};

  constructor() {
    this.queries
      .pipe(
        debounceTime(200),
        distinctUntilChanged(),
        switchMap((q) => {
          this.loading.set(true);
          return this.api.list<Hit>(this.lookups.endpointOf(this.kind()), { q, pageSize: 20, activeOnly: this.activeOnly() });
        }),
      )
      .subscribe({
        next: (r) => {
          this.lookups.remember(this.kind(), r.items);
          this.hits.set(r.items);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  text(h: Hit): string {
    return `${h.code ?? ''} — ${this.i18n.name(h as { nameAr: string; nameEn: string })}`;
  }

  openList(): void {
    this.term.set('');
    this.open.set(true);
    this.queries.next('');
    // distinctUntilChanged would swallow a repeated empty query: refresh directly when reopened
    if (!this.hits().length) this.loading.set(true);
  }

  type(q: string): void {
    this.term.set(q);
    this.queries.next(q.trim());
  }

  pick(h: Hit): void {
    this.lookups.remember(this.kind(), [h]);
    this.value = h.id;
    this.label.set(this.text(h));
    this.open.set(false);
    this.onChange(h.id);
  }

  clear(e: Event): void {
    e.preventDefault();
    this.value = null;
    this.label.set('');
    this.open.set(false);
    this.onChange(null);
  }

  onBlur(e: FocusEvent): void {
    if ((e.currentTarget as HTMLElement).contains(e.relatedTarget as Node)) return;
    this.open.set(false);
    this.onTouched();
  }

  // ---------- ControlValueAccessor ----------
  writeValue(v: number | null): void {
    this.value = v ?? null;
    if (!v) {
      this.label.set('');
      return;
    }
    const known = this.lookups.labelOf(this.kind(), v);
    if (known) this.label.set(known);
    else this.lookups.fetchOne(this.kind(), v).subscribe((l) => this.label.set(l));
  }

  registerOnChange(fn: (v: number | null) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(d: boolean): void { this.disabled.set(d); }
}
