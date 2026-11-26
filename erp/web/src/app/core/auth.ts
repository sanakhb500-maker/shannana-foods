import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, firstValueFrom, map, of, tap } from 'rxjs';
import { ApiEnvelope, Session } from './models';

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly session = signal<Session | null>(null);
  readonly loaded = signal(false);
  readonly isLoggedIn = computed(() => this.session() !== null);

  /** Restores the session from the HttpOnly cookie (called once at startup). */
  async restore(): Promise<void> {
    await firstValueFrom(
      this.http.get<ApiEnvelope<Session>>('/api/v1/auth/me').pipe(
        map((r) => r.data),
        catchError(() => of(null)),
        tap((s) => this.session.set(s)),
      ),
    );
    this.loaded.set(true);
  }

  login(email: string, password: string) {
    return this.http
      .post<ApiEnvelope<Session>>('/api/v1/auth/login', { email, password })
      .pipe(tap((r) => this.session.set(r.data)));
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/auth/logout', {}).pipe(catchError(() => of(null))));
    this.session.set(null);
    await this.router.navigateByUrl('/login');
  }

  hasAnyRole(roles: readonly string[]): boolean {
    const mine = this.session()?.roles ?? [];
    return roles.some((r) => mine.includes(r));
  }
}
