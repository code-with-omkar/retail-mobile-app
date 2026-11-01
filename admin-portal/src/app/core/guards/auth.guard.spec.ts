import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { AuthGuard, NoAuthGuard, RoleGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';
import { Role } from '../models/domain.model';

function setup(signedIn: boolean, roles: Role[] = []) {
  const router = { navigate: vi.fn() };
  const auth = { isAuthenticated: () => signedIn, hasAnyRole: (...wanted: Role[]) => wanted.some(r => roles.includes(r)) };
  TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: router }, { provide: AuthService, useValue: auth }] });
  return router;
}

const routeWith = (roles?: Role[]) => ({ data: { roles } }) as unknown as ActivatedRouteSnapshot;

describe('AuthGuard', () => {
  it('lets a signed-in user through', () => {
    const router = setup(true);
    expect(TestBed.inject(AuthGuard).canActivate({} as ActivatedRouteSnapshot, { url: '/orders' } as RouterStateSnapshot)).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('sends a signed-out user to login and remembers where they were going', () => {
    const router = setup(false);
    expect(TestBed.inject(AuthGuard).canActivate({} as ActivatedRouteSnapshot, { url: '/orders' } as RouterStateSnapshot)).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/orders' } });
  });
});

describe('RoleGuard', () => {
  it('sends a signed-out user to login', () => {
    const router = setup(false);
    expect(TestBed.inject(RoleGuard).canActivate(routeWith([Role.Admin]))).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('lets through a user who has one of the roles', () => {
    setup(true, [Role.StoreStaff]);
    expect(TestBed.inject(RoleGuard).canActivate(routeWith([Role.Admin, Role.StoreStaff]))).toBe(true);
  });

  it('lets any signed-in user through when the route names no roles', () => {
    setup(true);
    expect(TestBed.inject(RoleGuard).canActivate(routeWith(undefined))).toBe(true);
    expect(TestBed.inject(RoleGuard).canActivate(routeWith([]))).toBe(true);
  });

  it('sends a user without the role to the unauthorized page', () => {
    const router = setup(true, [Role.Customer]);
    expect(TestBed.inject(RoleGuard).canActivate(routeWith([Role.Admin]))).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/unauthorized']);
  });
});

describe('NoAuthGuard', () => {
  it('shows the login page to a signed-out user', () => {
    setup(false);
    expect(TestBed.inject(NoAuthGuard).canActivate()).toBe(true);
  });

  it('sends a signed-in user to the home page', () => {
    const router = setup(true);
    expect(TestBed.inject(NoAuthGuard).canActivate()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/']);
  });
});
