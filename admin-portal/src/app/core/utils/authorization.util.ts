import { Injectable } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { Role } from '../models/domain.model';

/**
 * Utility service for permission and authorization checks used in templates and components.
 */
@Injectable({ providedIn: 'root' })
export class AuthorizationService {
  constructor(private auth: AuthService) {}

  /**
   * Check if user has a specific role.
   */
  hasRole(role: Role): boolean {
    return this.auth.hasRole(role);
  }

  /**
   * Check if user has any of the provided roles.
   */
  hasAnyRole(...roles: Role[]): boolean {
    return this.auth.hasAnyRole(...roles);
  }

  /**
   * Check if user has all of the provided roles.
   */
  hasAllRoles(...roles: Role[]): boolean {
    const userRole = this.auth.userContext()?.role;
    return userRole ? roles.includes(userRole) : false;
  }

  /**
   * Check if user is authenticated.
   */
  isAuthenticated(): boolean {
    return this.auth.isAuthenticated();
  }

  /**
   * Check if user is admin.
   */
  isAdmin(): boolean {
    return this.auth.isAdmin();
  }

  /**
   * Check if user is store staff.
   */
  isStaff(): boolean {
    return this.auth.isStaff();
  }

  /**
   * Check if user is customer.
   */
  isCustomer(): boolean {
    return this.auth.isCustomer();
  }
}
