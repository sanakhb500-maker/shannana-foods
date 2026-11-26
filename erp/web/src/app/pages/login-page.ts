import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  template: `
    <main class="login-wrap">
      <form class="card login" (ngSubmit)="submit()" novalidate>
        <div class="login-top">
          <h1>{{ i18n.t('app.name') }}</h1>
          <button class="btn sm" type="button" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'العربية' }}</button>
        </div>
        <h2>{{ i18n.t('login.title') }}</h2>

        @if (error()) {
          <div class="alert error">{{ error() }}</div>
        }

        <div class="field">
          <label for="email">{{ i18n.t('login.email') }}</label>
          <input id="email" class="input" dir="ltr" type="email" name="email" [(ngModel)]="email" autocomplete="username" required />
        </div>
        <div class="field">
          <label for="password">{{ i18n.t('login.password') }}</label>
          <input id="password" class="input" dir="ltr" type="password" name="password" [(ngModel)]="password" autocomplete="current-password" required />
        </div>

        <button class="btn primary block" type="submit" [disabled]="busy()">{{ i18n.t('login.submit') }}</button>
      </form>
    </main>
  `,
})
export class LoginPage {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  readonly i18n = inject(I18n);

  email = '';
  password = '';
  readonly busy = signal(false);
  readonly error = signal('');

  submit(): void {
    if (!this.email || !this.password) return;
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.email, this.password).subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (e) => {
        this.busy.set(false);
        this.error.set(e.status === 429 ? e.error?.message ?? this.i18n.t('common.error') : this.i18n.t('login.failed'));
      },
    });
  }
}
