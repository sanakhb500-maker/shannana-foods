import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  template: `
    <main class="auth">
      <section class="auth-form">
        <div class="auth-top">
          <img class="auth-logo" src="shannana-logo.png" alt="Shannana Foods" (error)="$any($event.target).remove()" />
          <button class="btn sm" type="button" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'العربية' }}</button>
        </div>

        <form class="auth-card" (ngSubmit)="submit()" novalidate>
          <p class="auth-eyebrow">{{ i18n.t('app.name') }}</p>
          <h1>{{ i18n.t('login.welcome') }}</h1>
          <p class="auth-sub">{{ i18n.t('login.subtitle') }}</p>

          @if (error()) {
            <div class="alert error" role="alert">{{ error() }}</div>
          }

          <div class="field">
            <label for="email">{{ i18n.t('login.email') }}</label>
            <input id="email" class="input lg" dir="ltr" type="email" name="email" [(ngModel)]="email" autocomplete="username" placeholder="name@company.com" required />
          </div>
          <div class="field">
            <label for="password">{{ i18n.t('login.password') }}</label>
            <div class="pw">
              <input id="password" class="input lg" dir="ltr" [type]="showPw() ? 'text' : 'password'" name="password" [(ngModel)]="password" autocomplete="current-password" required />
              <button class="pw-toggle" type="button" (click)="showPw.set(!showPw())" [attr.aria-label]="i18n.t(showPw() ? 'login.hide' : 'login.show')" [attr.aria-pressed]="showPw()">
                {{ i18n.t(showPw() ? 'login.hide' : 'login.show') }}
              </button>
            </div>
          </div>

          <button class="btn primary block lg" type="submit" [disabled]="busy()">{{ i18n.t('login.submit') }}</button>
        </form>

        <p class="auth-foot">© {{ year }} Shannana Foods</p>
      </section>

      <aside class="auth-hero" aria-hidden="true">
        <div class="blob b1"></div>
        <div class="blob b2"></div>
        <div class="hero-copy">
          <span class="hero-badge">Shannana Foods</span>
          <h2>{{ i18n.t('login.heroTitle') }}</h2>
          <p>{{ i18n.t('login.heroText') }}</p>
          <ul class="hero-chips">
            <li>{{ i18n.t('nav.inventory') }}</li>
            <li>{{ i18n.t('nav.purchasing') }}</li>
            <li>{{ i18n.t('nav.sales') }}</li>
            <li>{{ i18n.t('nav.accounting') }}</li>
          </ul>
        </div>
        <div class="hero-shelf">
          <img class="p tomato" src="login/Tomato.webp" alt="" />
          <img class="p pizza" src="login/HeroProduct.webp" alt="" />
          <img class="p paste" src="login/TomatoPaste.webp" alt="" />
          <img class="p hot" src="login/HotSauce.webp" alt="" />
          <img class="p jam" src="login/StrawberryJam.webp" alt="" />
          <img class="p corn" src="login/Corn.webp" alt="" />
        </div>
      </aside>
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
  readonly showPw = signal(false);
  readonly year = new Date().getFullYear();
  readonly error = signal('');

  submit(): void {
    if (!this.email || !this.password) return;
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.email, this.password).subscribe({
      next: (r) => {
        // a driver account belongs to the mobile app: do not let it into this system
        if (r.data?.roles.length === 1 && r.data.roles[0] === 'Driver') {
          void this.auth.logout().then(() => this.error.set(this.i18n.t('login.driverOnly')));
          this.busy.set(false);
          return;
        }
        void this.router.navigateByUrl('/');
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(e.status === 429 ? e.error?.message ?? this.i18n.t('common.error') : this.i18n.t('login.failed'));
      },
    });
  }
}
