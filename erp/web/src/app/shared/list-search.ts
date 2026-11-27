import { Component, ElementRef, OnDestroy, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { I18n } from '../core/i18n';

/** Search box for server-paged lists: emits the (debounced) text; the page reloads with `q=`. */
@Component({
  selector: 'app-list-search',
  imports: [FormsModule],
  template: `<input class="input search" type="search" name="listSearch" [placeholder]="i18n.t('common.search')" [ngModel]="text()" (ngModelChange)="type($event)" />`,
  styles: [`:host { display: contents; } .search { min-width: 200px; }`],
})
export class ListSearch implements OnDestroy {
  readonly i18n = inject(I18n);
  readonly search = output<string>();
  readonly text = signal('');
  private timer?: ReturnType<typeof setTimeout>;

  type(v: string): void {
    this.text.set(v);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.search.emit((v ?? '').trim()), 300);
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }
}

/**
 * Search box for screens that already hold all their rows (reports, small lists): hides the table rows that do not contain
 * the text. Works on every table of the current page, so it needs no wiring to the page's data.
 */
@Component({
  selector: 'app-table-filter',
  imports: [FormsModule],
  template: `<input class="input search" type="search" name="tableFilter" [placeholder]="i18n.t('common.search')" [ngModel]="text()" (ngModelChange)="type($event)" />`,
  styles: [`:host { display: contents; } .search { min-width: 200px; }`],
})
export class TableFilter implements OnDestroy {
  readonly i18n = inject(I18n);
  readonly text = signal('');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private observer?: MutationObserver;

  constructor() {
    // rows arrive after the data loads: re-apply the filter whenever the table body changes
    queueMicrotask(() => {
      const root = this.root();
      if (!root) return;
      this.observer = new MutationObserver(() => this.apply());
      this.observer.observe(root, { childList: true, subtree: true });
    });
  }

  private root(): HTMLElement | null {
    return this.host.nativeElement.closest('.content');
  }

  type(v: string): void {
    this.text.set(v ?? '');
    this.apply();
  }

  private apply(): void {
    const needle = this.text().trim().toLowerCase();
    this.root()?.querySelectorAll<HTMLTableRowElement>('table tbody tr').forEach((tr) => {
      const keep = !needle || tr.querySelector('td.empty') !== null || (tr.textContent ?? '').toLowerCase().includes(needle);
      tr.hidden = !keep;
    });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
