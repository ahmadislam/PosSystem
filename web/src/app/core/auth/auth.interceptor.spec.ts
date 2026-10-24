import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { of, tap } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('API authentication interceptor', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let token: ReturnType<typeof signal<string | null>>;
  let refresh: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    token = signal<string | null>('access-token');
    refresh = vi.fn().mockReturnValue(of(null).pipe(tap(() => token.set('refreshed-token'))));
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { accessToken: token, refresh, clear: vi.fn() } },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
    ] });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
  });

  afterEach(() => requests.verify());

  it('attaches the token to protected auth/me requests', () => {
    http.get('/api/auth/me').subscribe();
    const request = requests.expectOne('/api/auth/me');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-token');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ username: 'cashier' });
  });

  it('keeps bearer tokens off the public refresh endpoint', () => {
    http.post('/api/auth/refresh', {}).subscribe();
    const request = requests.expectOne('/api/auth/refresh');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.withCredentials).toBe(true);
    request.flush({});
  });

  it('does not send API credentials to a different origin', () => {
    http.get('https://third-party.example.test/api/products').subscribe();
    const request = requests.expectOne('https://third-party.example.test/api/products');
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.withCredentials).toBe(false);
    request.flush({});
  });

  it('refreshes and retries a protected request with the newly issued token', () => {
    let value: unknown;
    http.get('/api/products').subscribe((response) => value = response);
    requests.expectOne('/api/products').flush({}, { status: 401, statusText: 'Unauthorized' });
    const retry = requests.expectOne('/api/products');
    expect(refresh).toHaveBeenCalledOnce();
    expect(retry.request.headers.get('Authorization')).toBe('Bearer refreshed-token');
    retry.flush({ items: [] });
    expect(value).toEqual({ items: [] });
  });
});
