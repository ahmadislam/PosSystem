import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse } from '../models';
import { CartStore } from '../cart/cart.store';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly cart = inject(CartStore);
  private readonly base = `${environment.apiUrl}/api/auth`;

  private readonly token = signal<string | null>(null);
  readonly username = signal('');
  readonly role = signal('');
  readonly isDemo = signal(false);
  readonly ready = signal(false);

  readonly isSignedIn = computed(() => !!this.token());
  readonly accessToken = this.token.asReadonly();
  readonly isStaff = computed(() => this.role() === 'Admin' || this.role() === 'Manager');

  private refreshInFlight: Observable<AuthResponse> | null = null;

  hasRole(...roles: string[]): boolean {
    return roles.includes(this.role());
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.base}/login`, { username, password }, { withCredentials: true })
      .pipe(tap((r) => {
        this.cart.clear();
        this.apply(r);
      }));
  }

  loginDemo(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.base}/demo`, {}, { withCredentials: true })
      .pipe(tap((response) => {
        this.cart.clear();
        this.apply(response);
      }));
  }

  logout(): Observable<unknown> {
    return this.http.post(`${this.base}/logout`, {}, { withCredentials: true }).pipe(
      catchError(() => of(null)),
      tap(() => this.clear()),
      finalize(() => this.clear()),
    );
  }

  /** Replay session from the HttpOnly refresh cookie after a full page load. */
  restore(): Observable<AuthResponse | null> {
    return this.refresh().pipe(
      catchError(() => {
        this.clear();
        return of(null);
      }),
      tap(() => this.ready.set(true)),
    );
  }

  refresh(): Observable<AuthResponse> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.http
        .post<AuthResponse>(`${this.base}/refresh`, {}, { withCredentials: true })
        .pipe(
          tap((r) => this.apply(r)),
          finalize(() => {
            this.refreshInFlight = null;
          }),
          shareReplay({ bufferSize: 1, refCount: true }),
        );
    }
    return this.refreshInFlight;
  }

  clear(): void {
    this.cart.clear();
    this.token.set(null);
    this.username.set('');
    this.role.set('');
    this.isDemo.set(false);
  }

  private apply(r: AuthResponse): void {
    if (this.username() && this.username() !== r.username) this.cart.clear();
    this.token.set(r.accessToken);
    this.username.set(r.username);
    this.role.set(r.role);
    this.isDemo.set(r.isDemo);
  }
}
