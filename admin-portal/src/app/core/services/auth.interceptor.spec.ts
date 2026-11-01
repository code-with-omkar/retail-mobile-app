import { TestBed } from '@angular/core/testing';
import { HTTP_INTERCEPTORS, HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AuthInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { ConfigService } from './config.service';

const API = 'http://api.test/api';
const unauthorized = { status: 401, statusText: 'Unauthorized' };

function setup(opts: { token?: string | null; refreshToken?: string | null; refresh?: () => unknown } = {}) {
  let token = opts.token === undefined ? 'old' : opts.token;
  const auth = {
    getToken: () => token,
    getRefreshToken: () => (opts.refreshToken === undefined ? 'r1' : opts.refreshToken),
    refresh: vi.fn(opts.refresh ?? (() => { token = 'new'; return of({}); })),
    clearSession: vi.fn(),
  };
  const router = { navigate: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptorsFromDi()),
      provideHttpClientTesting(),
      { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
    ],
  });
  TestBed.inject(ConfigService).setBaseUrl(API);
  return { auth, router, http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController) };
}

describe('AuthInterceptor', () => {
  it('adds the bearer token to calls to the API', () => {
    const { http, ctl } = setup();
    http.get(`${API}/orders`).subscribe();
    expect(ctl.expectOne(`${API}/orders`).request.headers.get('Authorization')).toBe('Bearer old');
  });

  it('adds nothing when there is no token', () => {
    const { http, ctl } = setup({ token: null });
    http.get(`${API}/orders`).subscribe();
    expect(ctl.expectOne(`${API}/orders`).request.headers.has('Authorization')).toBe(false);
  });

  it('leaves other hosts and the login and refresh calls alone', () => {
    const { http, ctl } = setup();
    http.get('http://elsewhere.test/x').subscribe();
    http.post(`${API}/auth/login`, {}).subscribe();
    http.post(`${API}/auth/refresh`, {}).subscribe();
    expect(ctl.expectOne('http://elsewhere.test/x').request.headers.has('Authorization')).toBe(false);
    expect(ctl.expectOne(`${API}/auth/login`).request.headers.has('Authorization')).toBe(false);
    expect(ctl.expectOne(`${API}/auth/refresh`).request.headers.has('Authorization')).toBe(false);
  });

  it('on a 401 refreshes the token once and retries with the new one', () => {
    const { auth, http, ctl } = setup();
    let result: unknown;
    http.get(`${API}/orders`).subscribe(r => (result = r));

    ctl.expectOne(`${API}/orders`).flush({}, unauthorized);
    expect(auth.refresh).toHaveBeenCalledTimes(1);

    const retry = ctl.expectOne(`${API}/orders`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: true });
    expect(result).toEqual({ ok: true });
  });

  it('two calls failing at once share a single refresh', () => {
    // The refresh stays open until the test finishes it, as a real network call would.
    const refreshing = new Subject<object>();
    const { auth, http, ctl } = setup({ refresh: () => refreshing });
    http.get(`${API}/a`).subscribe();
    http.get(`${API}/b`).subscribe();

    ctl.expectOne(`${API}/a`).flush({}, unauthorized);
    ctl.expectOne(`${API}/b`).flush({}, unauthorized);
    refreshing.next({});
    refreshing.complete();

    expect(auth.refresh).toHaveBeenCalledTimes(1);
    ctl.expectOne(`${API}/a`).flush({});
    ctl.expectOne(`${API}/b`).flush({});
  });

  it('a retry that fails again with 401 is passed on, not refreshed again', () => {
    const { auth, http, ctl } = setup();
    let status = 0;
    http.get(`${API}/orders`).subscribe({ error: e => (status = e.status) });

    ctl.expectOne(`${API}/orders`).flush({}, unauthorized);
    ctl.expectOne(`${API}/orders`).flush({}, unauthorized);

    expect(status).toBe(401);
    expect(auth.refresh).toHaveBeenCalledTimes(1);
  });

  it('without a refresh token a 401 is passed on', () => {
    const { auth, http, ctl } = setup({ refreshToken: null });
    let status = 0;
    http.get(`${API}/orders`).subscribe({ error: e => (status = e.status) });
    ctl.expectOne(`${API}/orders`).flush({}, unauthorized);
    expect(status).toBe(401);
    expect(auth.refresh).not.toHaveBeenCalled();
  });

  it('other errors are passed on untouched', () => {
    const { auth, http, ctl } = setup();
    let status = 0;
    http.get(`${API}/orders`).subscribe({ error: e => (status = e.status) });
    ctl.expectOne(`${API}/orders`).flush({}, { status: 500, statusText: 'Server Error' });
    expect(status).toBe(500);
    expect(auth.refresh).not.toHaveBeenCalled();
  });

  it('when the refresh fails the session is cleared and the user goes to login', () => {
    const { auth, router, http, ctl } = setup({ refresh: () => throwError(() => new Error('expired')) });
    let failed = false;
    http.get(`${API}/orders`).subscribe({ error: () => (failed = true) });
    ctl.expectOne(`${API}/orders`).flush({}, unauthorized);

    expect(failed).toBe(true);
    expect(auth.clearSession).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });
});
