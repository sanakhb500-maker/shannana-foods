import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiEnvelope, Paged } from './models';

const BASE = '/api/v1';

export interface ListQuery {
  q?: string;
  page?: number;
  pageSize?: number;
  activeOnly?: boolean;
  [extra: string]: string | number | boolean | undefined;
}

/** Thin typed wrapper over the ERP REST API; unwraps the { success, data } envelope. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  list<T>(endpoint: string, query: ListQuery = {}): Observable<Paged<T>> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== '') params = params.set(k, String(v));
    return this.http.get<ApiEnvelope<Paged<T>>>(`${BASE}/${endpoint}`, { params }).pipe(map((r) => r.data!));
  }

  get<T>(path: string): Observable<T> {
    return this.http.get<ApiEnvelope<T>>(`${BASE}/${path}`).pipe(map((r) => r.data!));
  }

  create<T>(endpoint: string, body: unknown): Observable<T> {
    return this.http.post<ApiEnvelope<T>>(`${BASE}/${endpoint}`, body).pipe(map((r) => r.data!));
  }

  update<T>(endpoint: string, id: number, body: unknown): Observable<T> {
    return this.http.put<ApiEnvelope<T>>(`${BASE}/${endpoint}/${id}`, body).pipe(map((r) => r.data!));
  }

  post<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<ApiEnvelope<T>>(`${BASE}/${path}`, body).pipe(map((r) => r.data!));
  }

  deactivate<T>(endpoint: string, id: number): Observable<T> {
    return this.http.delete<ApiEnvelope<T>>(`${BASE}/${endpoint}/${id}`).pipe(map((r) => r.data!));
  }
}

/** Extracts a human message and per-field errors from a failed API call. */
export function parseApiError(err: unknown): { message: string; fields: Record<string, string[]> } {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as Partial<ApiEnvelope<unknown>> | null;
    const fields: Record<string, string[]> = {};
    for (const [k, v] of Object.entries(body?.errors ?? {})) fields[k.charAt(0).toLowerCase() + k.slice(1)] = v;
    return { message: body?.message ?? err.statusText ?? 'Request failed', fields };
  }
  return { message: 'Request failed', fields: {} };
}
