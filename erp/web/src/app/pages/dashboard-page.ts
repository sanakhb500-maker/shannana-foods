import { Component, inject } from '@angular/core';
import { Auth } from '../core/auth';
import { I18n } from '../core/i18n';

@Component({
  selector: 'app-dashboard-page',
  template: `
    <header class="page-head">
      <h1>{{ i18n.t('dash.welcome') }}، {{ auth.session()?.fullName }}</h1>
    </header>
    <div class="card pad">
      <p>{{ i18n.t('dash.intro') }}</p>
      <ul class="roadmap">
        @for (p of phases; track p) {
          <li>
            <span>{{ i18n.t(p) }}</span>
            <span class="badge muted">{{ i18n.t('dash.soon') }}</span>
          </li>
        }
      </ul>
    </div>
  `,
})
export class DashboardPage {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly phases = ['dash.phase4', 'dash.phase5'];
}
