import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { DeliveryRow, Paged, StopStatus, Trip, TripRow, TripStatus, WRITE_ROLES } from '../core/models';

const today = () => new Date().toISOString().slice(0, 10);

/**
 * Distribution trips: a driver and a vehicle visiting a list of delivery notes in order.
 * Planned (edit) -> In transit (record each stop) -> Completed. Goods leave stock when the delivery notes are confirmed.
 */
@Component({
  selector: 'app-trips-page',
  imports: [FormsModule, DecimalPipe],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.trips') }}</h1>
      <div class="page-tools">
        <select class="input" [(ngModel)]="status" (ngModelChange)="reload()">
          <option value="">{{ i18n.t('inv.allStatuses') }}</option>
          @for (s of statuses; track s) {
            <option [value]="s">{{ i18n.t('trip.' + s) }}</option>
          }
        </select>
        <select class="input" [(ngModel)]="driverFilter" (ngModelChange)="reload()">
          <option [ngValue]="null">{{ i18n.t('trip.allDrivers') }}</option>
          @for (d of lookups.drivers(); track d.id) {
            <option [ngValue]="d.id">{{ d.name }}</option>
          }
        </select>
        @if (canWrite) {
          <button class="btn primary" type="button" (click)="openNew()">+ {{ i18n.t('trip.new') }}</button>
        } @else {
          <span class="badge muted">{{ i18n.t('common.readOnly') }}</span>
        }
      </div>
    </header>

    @if (message() && !trip()) {
      <div class="alert error">{{ message() }}</div>
    }

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('inv.number') }}</th>
            <th>{{ i18n.t('f.date') }}</th>
            <th>{{ i18n.t('trip.driver') }}</th>
            <th>{{ i18n.t('trip.vehicle') }}</th>
            <th class="num">{{ i18n.t('trip.stops') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (r of rows(); track r.id) {
            <tr class="clickable" (click)="open(r.id)">
              <td>{{ r.number ?? '—' }}</td>
              <td>{{ r.date }}</td>
              <td>{{ lookups.driverLabel(r.driverId) }}</td>
              <td>{{ lookups.vehicleLabel(r.vehicleId) }}</td>
              <td class="num">{{ r.deliveredCount }} / {{ r.stopCount }}</td>
              <td><span class="badge" [class.ok]="r.status === 'Completed'" [class.muted]="r.status === 'Cancelled'">{{ i18n.t('trip.' + r.status) }}</span></td>
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

    @if (trip(); as t) {
      <div class="modal-backdrop" (click)="close()">
        <form class="modal wide-modal card" (click)="$event.stopPropagation()" (ngSubmit)="save()" novalidate>
          <h2>
            {{ i18n.t('nav.trips') }} {{ t.number ?? '' }}
            <span class="badge" [class.ok]="t.status === 'Completed'" [class.muted]="t.status === 'Cancelled'">{{ i18n.t('trip.' + t.status) }}</span>
          </h2>

          @if (message()) {
            <div class="alert error">{{ message() }}</div>
          }

          <div class="form-grid">
            <div class="field">
              <label for="t-date">{{ i18n.t('f.date') }} <span class="req">*</span></label>
              <input id="t-date" class="input" type="date" name="date" [(ngModel)]="t.date" [disabled]="!editable()" />
            </div>
            <div class="field">
              <label for="t-notes">{{ i18n.t('inv.notes') }}</label>
              <input id="t-notes" class="input" name="notes" [(ngModel)]="t.notes" maxlength="500" [disabled]="!editable()" />
            </div>
            <div class="field">
              <label for="t-driver">{{ i18n.t('trip.driver') }} <span class="req">*</span></label>
              <select id="t-driver" class="input" name="driver" [(ngModel)]="t.driverId" [disabled]="!editable()">
                <option [ngValue]="null" disabled>{{ i18n.t('common.select') }}</option>
                @for (d of lookups.drivers(); track d.id) {
                  @if (d.isActive || d.id === t.driverId) { <option [ngValue]="d.id">{{ d.name }}</option> }
                }
              </select>
            </div>
            <div class="field">
              <label for="t-vehicle">{{ i18n.t('trip.vehicle') }} <span class="req">*</span></label>
              <select id="t-vehicle" class="input" name="vehicle" [(ngModel)]="t.vehicleId" [disabled]="!editable()">
                <option [ngValue]="null" disabled>{{ i18n.t('common.select') }}</option>
                @for (v of lookups.vehicles(); track v.id) {
                  @if (v.isActive || v.id === t.vehicleId) { <option [ngValue]="v.id">{{ lookups.vehicleLabel(v.id) }}</option> }
                }
              </select>
            </div>
          </div>

          <h3 class="sub">{{ i18n.t('trip.stops') }}</h3>
          <div class="table-wrap lines">
            <table class="table">
              <thead>
                <tr>
                  <th class="num">#</th>
                  <th>{{ i18n.t('inv.document') }}</th>
                  <th>{{ i18n.t('f.customer') }}</th>
                  <th class="num">{{ i18n.t('pur.total') }}</th>
                  @if (t.status !== 'Planned') {
                    <th>{{ i18n.t('trip.result') }}</th>
                    <th>{{ i18n.t('inv.notes') }}</th>
                  }
                  @if (editable()) { <th></th> }
                </tr>
              </thead>
              <tbody>
                @for (s of t.stops; track s.deliveryNoteId; let i = $index) {
                  <tr>
                    <td class="num">{{ i + 1 }}</td>
                    <td>{{ s.deliveryNumber }}
                      @if (s.deliveryStatus !== 'Confirmed') { <span class="badge muted">{{ i18n.t('status.' + s.deliveryStatus) }}</span> }
                    </td>
                    <td>{{ lookups.customerLabel(s.customerId) }}</td>
                    <td class="num">{{ s.totalAmount | number: '1.2-2' }} {{ lookups.currencyLabel(s.currencyId) }}</td>
                    @if (t.status !== 'Planned') {
                      <td>
                        @if (t.status === 'InTransit' && canWrite) {
                          <select class="input" [name]="'st' + i" [ngModel]="s.status" (ngModelChange)="setStop(s, $event)">
                            @for (o of stopStatuses; track o) { <option [value]="o">{{ i18n.t('stop.' + o) }}</option> }
                          </select>
                        } @else {
                          <span class="badge" [class.ok]="s.status === 'Delivered'" [class.muted]="s.status === 'Failed'">{{ i18n.t('stop.' + s.status) }}</span>
                        }
                      </td>
                      <td>
                        @if (t.status === 'InTransit' && canWrite) {
                          <input class="input" [name]="'sn' + i" [ngModel]="s.notes" maxlength="300" (change)="setNote(s, $any($event.target).value)" />
                        } @else {
                          {{ s.notes }}
                        }
                      </td>
                    }
                    @if (editable()) {
                      <td class="actions">
                        <button class="btn sm" type="button" (click)="move(i, -1)" [disabled]="i === 0" aria-label="up">↑</button>
                        <button class="btn sm" type="button" (click)="move(i, 1)" [disabled]="i === t.stops.length - 1" aria-label="down">↓</button>
                        <button class="btn sm danger" type="button" (click)="removeStop(i)" [attr.aria-label]="i18n.t('common.remove')">✕</button>
                      </td>
                    }
                  </tr>
                } @empty {
                  <tr><td class="empty" colspan="7">{{ i18n.t('trip.noStops') }}</td></tr>
                }
              </tbody>
            </table>
          </div>

          @if (editable()) {
            <div class="add-stop">
              <select class="input" name="addStop" [(ngModel)]="addId">
                <option [ngValue]="null">{{ i18n.t('trip.addDelivery') }}</option>
                @for (d of availableDeliveries(); track d.id) {
                  <option [ngValue]="d.id">{{ d.number }} — {{ lookups.customerLabel(d.customerId) }} ({{ d.totalAmount | number: '1.2-2' }} {{ lookups.currencyLabel(d.currencyId) }})</option>
                }
              </select>
              <button class="btn sm" type="button" [disabled]="!addId" (click)="addStop()">+ {{ i18n.t('trip.addStop') }}</button>
            </div>
            <p class="hint-text">{{ i18n.t('trip.hint') }}</p>
          }

          <div class="modal-actions">
            <button class="btn" type="button" (click)="close()">{{ i18n.t('common.close') }}</button>
            @if (canWrite && t.status === 'Planned') {
              <button class="btn" type="submit" [disabled]="busy()">{{ i18n.t('common.save') }}</button>
              <button class="btn primary" type="button" [disabled]="busy()" (click)="start()">{{ i18n.t('trip.start') }}</button>
            }
            @if (canWrite && t.status === 'InTransit') {
              <button class="btn primary" type="button" [disabled]="busy()" (click)="act('complete')">{{ i18n.t('trip.complete') }}</button>
            }
            @if (canWrite && (t.status === 'Planned' || t.status === 'InTransit') && t.id) {
              <button class="btn danger" type="button" [disabled]="busy()" (click)="cancelTrip()">{{ i18n.t('trip.cancel') }}</button>
            }
          </div>
        </form>
      </div>
    }
  `,
})
export class TripsPage implements OnInit {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly canWrite = this.auth.hasAnyRole(WRITE_ROLES.trips);
  readonly statuses: TripStatus[] = ['Planned', 'InTransit', 'Completed', 'Cancelled'];
  readonly stopStatuses: StopStatus[] = ['Pending', 'Delivered', 'Failed'];
  readonly rows = signal<TripRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / 20)));
  readonly loading = signal(false);
  readonly message = signal('');
  readonly trip = signal<Trip | null>(null);
  readonly busy = signal(false);
  /** Deliveries not on any active trip: the pool stops are picked from. */
  readonly pool = signal<DeliveryRow[]>([]);
  status = '';
  driverFilter: number | null = null;
  addId: number | null = null;

  readonly editable = computed(() => this.canWrite && this.trip()?.status === 'Planned');

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
    const qs = [`page=${this.page()}`, 'pageSize=20'];
    if (this.status) qs.push(`status=${this.status}`);
    if (this.driverFilter) qs.push(`driverId=${this.driverFilter}`);
    this.api.get<Paged<TripRow>>(`trips?${qs.join('&')}`).subscribe({
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

  private loadPool(): void {
    this.api.get<Paged<DeliveryRow>>('deliverynotes?unassignedOnly=true&pageSize=200').subscribe((r) => this.pool.set(r.items.filter((d) => d.status !== 'Cancelled')));
  }

  /** Deliveries that can still be added: unassigned and not already a stop of this trip. */
  availableDeliveries(): DeliveryRow[] {
    const used = new Set((this.trip()?.stops ?? []).map((s) => s.deliveryNoteId));
    return this.pool().filter((d) => !used.has(d.id));
  }

  openNew(): void {
    this.message.set('');
    this.addId = null;
    this.loadPool();
    this.trip.set({ date: today(), driverId: null, vehicleId: null, status: 'Planned', notes: null, stops: [] });
  }

  open(id: number): void {
    this.message.set('');
    this.addId = null;
    this.api.get<Trip>(`trips/${id}`).subscribe({
      next: (t) => {
        this.trip.set(t);
        if (t.status === 'Planned') this.loadPool();
      },
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  close(): void {
    this.trip.set(null);
  }

  addStop(): void {
    const t = this.trip();
    const d = this.pool().find((x) => x.id === this.addId);
    if (!t || !d) return;
    t.stops.push({
      sequence: t.stops.length + 1, status: 'Pending', notes: null, deliveryNoteId: d.id, deliveryNumber: d.number,
      customerId: d.customerId, totalAmount: d.totalAmount, currencyId: d.currencyId, deliveryStatus: d.status,
    });
    this.addId = null;
  }

  removeStop(i: number): void {
    this.trip()?.stops.splice(i, 1);
  }

  move(i: number, delta: number): void {
    const stops = this.trip()?.stops;
    if (!stops) return;
    const j = i + delta;
    if (j < 0 || j >= stops.length) return;
    [stops[i], stops[j]] = [stops[j], stops[i]];
  }

  private body() {
    const t = this.trip()!;
    return {
      date: t.date,
      driverId: t.driverId ?? 0,
      vehicleId: t.vehicleId ?? 0,
      notes: t.notes?.trim() || null,
      deliveryNoteIds: t.stops.map((s) => s.deliveryNoteId),
    };
  }

  private errorText(e: unknown): string {
    const p = parseApiError(e);
    const detail = Object.values(p.fields).flat().join(' ');
    return detail ? `${p.message} ${detail}` : p.message;
  }

  private run(call: () => Observable<Trip>): void {
    this.busy.set(true);
    this.message.set('');
    call().subscribe({
      next: (t) => {
        this.busy.set(false);
        this.trip.set(t);
        if (t.status === 'Planned') this.loadPool();
        this.load();
      },
      error: (e) => {
        this.message.set(this.errorText(e));
        this.busy.set(false);
      },
    });
  }

  private saveCall(): Observable<Trip> {
    const t = this.trip()!;
    return t.id ? this.api.update<Trip>('trips', t.id, this.body()) : this.api.create<Trip>('trips', this.body());
  }

  save(): void {
    if (this.editable()) this.run(() => this.saveCall());
  }

  /** Saves the plan first, then sends the trip out. */
  start(): void {
    this.busy.set(true);
    this.message.set('');
    this.saveCall().subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.trip.set(saved);
        this.run(() => this.api.post<Trip>(`trips/${saved.id}/start`));
      },
      error: (e) => {
        this.message.set(this.errorText(e));
        this.busy.set(false);
      },
    });
  }

  act(action: 'complete'): void {
    this.run(() => this.api.post<Trip>(`trips/${this.trip()!.id}/${action}`));
  }

  cancelTrip(): void {
    if (!confirm(this.i18n.t('trip.confirmCancel'))) return;
    this.run(() => this.api.post<Trip>(`trips/${this.trip()!.id}/cancel`));
  }

  setNote(stop: Trip['stops'][number], notes: string): void {
    this.setStop({ ...stop, notes }, stop.status);
  }

  setStop(stop: Trip['stops'][number], status: StopStatus): void {
    const t = this.trip();
    if (!t?.id || !stop.id) return;
    this.run(() => this.api.update<Trip>(`trips/${t.id}/stops`, stop.id!, { status, notes: stop.notes }));
  }
}
