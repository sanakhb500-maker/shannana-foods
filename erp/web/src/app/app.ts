import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { I18n } from './core/i18n';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {
  // Injecting I18n at the root applies <html lang/dir> before anything renders.
  protected readonly i18n = inject(I18n);
}
