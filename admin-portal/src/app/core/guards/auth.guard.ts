import { Injectable } from '@angular/core';
import {
  CanActivate,
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  Router,
} from '@angular/router';
import { AuthService } from '../services/auth.service';
import { Role } from '../models/domain.model';

/**
 * Guard to protect routes that require authentication.
 */
@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  canActivate(
    route: ActivatedRouteSnapshot,
    state: RouterStateSnapshot
  ): boolean {
    if (this.auth.isAuthenticated()) {
      return true;
    }

    this.router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }
}

/**
 * Guard to protect routes by required roles.
 * Usage: canActivate: [RoleGuard], data: { roles: [Role.Admin, Role.StoreStaff] }
 */
@Injectable({ providedIn: 'root' })
export class RoleGuard implements CanActivate {
  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  canActivate(route: ActivatedRouteSnapshot): boolean {
    if (!this.auth.isAuthenticated()) {
      this.router.navigate(['/login']);
      return false;
    }

    const requiredRoles: Role[] = route.data['roles'];
    if (!requiredRoles || requiredRoles.length === 0) {
      return true;
    }

    if (this.auth.hasAnyRole(...requiredRoles)) {
      return true;
    }

    this.router.navigate(['/unauthorized']);
    return false;
  }
}

/**
 * Guard to prevent authenticated users from accessing login page.
 */
@Injectable({ providedIn: 'root' })
export class NoAuthGuard implements CanActivate {
  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  canActivate(): boolean {
    if (this.auth.isAuthenticated()) {
      this.router.navigate(['/']);
      return false;
    }
    return true;
  }
}
