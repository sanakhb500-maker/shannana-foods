import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, parseApiError } from '../core/api';
import { Auth } from '../core/auth';
import { TableFilter } from '../shared/list-search';
import { I18n } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { ROLES, UserDto } from '../core/models';

@Component({
  selector: 'app-users-page',
  imports: [FormsModule, TableFilter],
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('nav.users') }}</h1>
      <div class="page-tools">
        <app-table-filter />
        <button class="btn primary" type="button" (click)="openNew()">+ {{ i18n.t('common.add') }}</button>
      </div>
    </header>

    @if (message() && !editing()) {
      <div class="alert error">{{ message() }}</div>
    }

    <div class="card table-wrap">
      <table class="table">
        <thead>
          <tr>
            <th>{{ i18n.t('f.fullName') }}</th>
            <th>{{ i18n.t('f.email') }}</th>
            <th>{{ i18n.t('f.roles') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
            <th class="actions">{{ i18n.t('common.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (u of users(); track u.id) {
            <tr [class.inactive]="!u.isActive">
              <td>{{ u.fullName }}</td>
              <td dir="ltr">{{ u.email }}</td>
              <td>
                @for (r of u.roles; track r) {
                  <span class="badge">{{ i18n.t('role.' + r) }}</span>
                }
              </td>
              <td>
                <span class="badge" [class.ok]="u.isActive" [class.muted]="!u.isActive">
                  {{ i18n.t(u.isActive ? 'common.active' : 'common.inactive') }}
                </span>
              </td>
              <td class="actions">
                <button class="btn sm" type="button" (click)="openEdit(u)">{{ i18n.t('common.edit') }}</button>
                <button class="btn sm" type="button" (click)="openReset(u)">{{ i18n.t('users.reset') }}</button>
              </td>
            </tr>
          } @empty {
            <tr><td class="empty" colspan="5">{{ i18n.t('common.empty') }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (editing()) {
      <div class="modal-backdrop" (click)="close()">
        <form class="modal card" (click)="$event.stopPropagation()" (ngSubmit)="save()" novalidate>
          <h2>{{ i18n.t(mode === 'new' ? 'common.add' : mode === 'edit' ? 'common.edit' : 'users.reset') }} — {{ i18n.t('nav.users') }}</h2>
          @if (message()) {
            <div class="alert error">{{ message() }}</div>
          }

          <div class="form-grid">
            @if (mode !== 'reset') {
              <div class="field">
                <label for="u-name">{{ i18n.t('f.fullName') }} <span class="req">*</span></label>
                <input id="u-name" class="input" name="fullName" [(ngModel)]="form.fullName" required />
              </div>
              <div class="field">
                <label for="u-email">{{ i18n.t('f.email') }} <span class="req">*</span></label>
                <input id="u-email" class="input" dir="ltr" type="email" name="email" [(ngModel)]="form.email" [disabled]="mode === 'edit'" required />
              </div>
            }
            @if (mode !== 'edit') {
              <div class="field">
                <label for="u-pass">{{ i18n.t(mode === 'reset' ? 'f.newPassword' : 'f.password') }} <span class="req">*</span></label>
                <input id="u-pass" class="input" dir="ltr" type="password" name="password" [(ngModel)]="form.password" autocomplete="new-password" required />
              </div>
            }
            @if (mode !== 'reset') {
              <div class="field wide">
                <label>{{ i18n.t('f.roles') }} <span class="req">*</span></label>
                <div class="chips">
                  @for (r of allRoles; track r) {
                    <label class="chip" [class.on]="form.roles.includes(r)">
                      <input type="checkbox" [checked]="form.roles.includes(r)" (change)="toggleRole(r)" />
                      {{ i18n.t('role.' + r) }}
                    </label>
                  }
                </div>
                <small class="hint">{{ i18n.t('users.rolesHint') }}</small>
              </div>
              @if (form.roles.includes('Driver')) {
                <div class="field wide">
                  <label for="u-driver">{{ i18n.t('users.driver') }} <span class="req">*</span></label>
                  <select id="u-driver" class="input" name="driverId" [(ngModel)]="form.driverId">
                    <option [ngValue]="null" disabled>{{ i18n.t('common.select') }}</option>
                    @for (d of lookups.drivers(); track d.id) {
                      @if (d.isActive) { <option [ngValue]="d.id">{{ d.name }}</option> }
                    }
                  </select>
                  <small class="hint">{{ i18n.t('users.driverHint') }}</small>
                </div>
              }
              @if (mode === 'edit') {
                <div class="field">
                  <label class="check"><input type="checkbox" name="isActive" [(ngModel)]="form.isActive" /> {{ i18n.t('common.active') }}</label>
                </div>
              }
            }
          </div>

          <div class="modal-actions">
            <button class="btn" type="button" (click)="close()">{{ i18n.t('common.cancel') }}</button>
            <button class="btn primary" type="submit" [disabled]="saving()">{{ i18n.t('common.save') }}</button>
          </div>
        </form>
      </div>
    }
  `,
})
export class UsersPage implements OnInit {
  private readonly api = inject(Api);
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly lookups = inject(Lookups);

  readonly allRoles = ROLES as readonly string[];
  readonly users = signal<UserDto[]>([]);
  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly message = signal('');
  mode: 'new' | 'edit' | 'reset' = 'new';
  form = this.blank();
  private targetId = 0;

  ngOnInit(): void {
    this.lookups.ensureLoaded();
    this.load();
  }

  private blank() {
    return { fullName: '', email: '', password: '', roles: [] as string[], isActive: true, driverId: null as number | null };
  }

  load(): void {
    this.api.get<UserDto[]>('users').subscribe({
      next: (u) => this.users.set(u),
      error: (e) => this.message.set(parseApiError(e).message),
    });
  }

  openNew(): void {
    this.mode = 'new';
    this.form = this.blank();
    this.open();
  }

  openEdit(u: UserDto): void {
    this.mode = 'edit';
    this.targetId = u.id;
    this.form = { fullName: u.fullName, email: u.email, password: '', roles: [...u.roles], isActive: u.isActive, driverId: u.driverId ?? null };
    this.open();
  }

  openReset(u: UserDto): void {
    this.mode = 'reset';
    this.targetId = u.id;
    this.form = { ...this.blank(), email: u.email };
    this.open();
  }

  private open(): void {
    this.message.set('');
    this.editing.set(true);
  }

  close(): void {
    this.editing.set(false);
  }

  toggleRole(r: string): void {
    const has = this.form.roles.includes(r);
    this.form.roles = has ? this.form.roles.filter((x) => x !== r) : [...this.form.roles, r];
  }

  save(): void {
    this.saving.set(true);
    this.message.set('');
    const f = this.form;
    const call =
      this.mode === 'new'
        ? this.api.create('users', { email: f.email, fullName: f.fullName, password: f.password, roles: f.roles, driverId: f.roles.includes('Driver') ? f.driverId : null })
        : this.mode === 'edit'
          ? this.api.update('users', this.targetId, { fullName: f.fullName, isActive: f.isActive, roles: f.roles, driverId: f.roles.includes('Driver') ? f.driverId : null })
          : this.api.post(`users/${this.targetId}/reset-password`, { newPassword: f.password });
    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.load();
      },
      error: (e) => {
        const p = parseApiError(e);
        const detail = Object.values(p.fields).flat().join(' ');
        this.message.set(detail ? `${p.message} ${detail}` : p.message);
        this.saving.set(false);
      },
    });
  }
}
