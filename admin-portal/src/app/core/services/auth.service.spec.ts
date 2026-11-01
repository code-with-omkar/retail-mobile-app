import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { Role } from '../models/domain.model';

const loginBody = (roles: string[]) => ({
  success: true,
  data: {
    accessToken: 'access-1',
    refreshToken: 'refresh-1',
    expiresIn: 900,
    tokenType: 'Bearer',
    user: { id: 'u1', displayName: 'Admin User', organizationId: 'o1', roles, permissions: ['orders.read'] },
  },
});

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  TestBed.inject(ConfigService).setBaseUrl('http://api.test/api');
  return { auth: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController) };
}

describe('AuthService', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.spyOn(console, 'log').mockImplementation(() => {});
  });

  it('starts signed out', () => {
    const { auth } = setup();
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.getToken()).toBeNull();
  });

  it('login stores the tokens and signs the user in', () => {
    const { auth, http } = setup();
    let done = false;
    auth.login({ username: 'admin', password: 'x' } as never).subscribe(() => (done = true));

    const req = http.expectOne('http://api.test/api/auth/login');
    expect(req.request.method).toBe('POST');
    req.flush(loginBody([Role.Admin]));

    expect(done).toBe(true);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.getToken()).toBe('access-1');
    expect(auth.getRefreshToken()).toBe('refresh-1');
    expect(auth.isAdmin()).toBe(true);
    expect(auth.getUserId()).toBe('u1');
    expect(auth.isLoading()).toBe(false);
  });

  it('ApplicationAdmin wins when a user has several roles, and counts as an admin', () => {
    const { auth, http } = setup();
    auth.login({} as never).subscribe();
    http.expectOne('http://api.test/api/auth/login').flush(loginBody([Role.StoreStaff, Role.ApplicationAdmin]));

    expect(auth.userContext()?.role).toBe(Role.ApplicationAdmin);
    expect(auth.hasRole(Role.Admin)).toBe(true);
    expect(auth.isStaff()).toBe(false);
  });

  it('a failed login shows the server message and stays signed out', () => {
    const { auth, http } = setup();
    auth.login({} as never).subscribe({ error: () => {} });
    http.expectOne('http://api.test/api/auth/login').flush({ message: 'Wrong password' }, { status: 401, statusText: 'Unauthorized' });

    expect(auth.error()).toBe('Wrong password');
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.isLoading()).toBe(false);
  });

  it('restores the session from storage on start', () => {
    localStorage.setItem('app:token', 't');
    localStorage.setItem('app:user_context', JSON.stringify({ userId: 'u9', role: Role.StoreStaff, displayName: 'S', organizationId: 'o' }));
    const { auth } = setup();

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.isStaff()).toBe(true);
  });

  it('logout tells the server and clears the session', () => {
    const { auth, http } = setup();
    auth.login({} as never).subscribe();
    http.expectOne('http://api.test/api/auth/login').flush(loginBody([Role.Admin]));

    auth.logout();

    const req = http.expectOne('http://api.test/api/auth/logout');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.getToken()).toBeNull();
  });

  it('refresh replaces the stored tokens', () => {
    const { auth, http } = setup();
    auth.login({} as never).subscribe();
    http.expectOne('http://api.test/api/auth/login').flush(loginBody([Role.Admin]));

    auth.refresh().subscribe();
    const req = http.expectOne('http://api.test/api/auth/refresh');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    const next = loginBody([Role.Admin]);
    next.data.accessToken = 'access-2';
    next.data.refreshToken = 'refresh-2';
    req.flush(next);

    expect([auth.getToken(), auth.getRefreshToken()]).toEqual(['access-2', 'refresh-2']);
  });

  it('hasAnyRole is false when signed out', () => {
    const { auth } = setup();
    expect(auth.hasAnyRole(Role.Admin, Role.StoreStaff)).toBe(false);
  });
});
