import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, map, catchError } from 'rxjs';
import { UserContext, Role, StaffCategory } from '../models/domain.model';
import { ApiResponse, AuthenticationUser, LoginRequest, LoginResponse } from '../models/api.model';
import { StorageService } from './storage.service';
import { ConfigService } from './config.service';

/**
 * Authentication service managing login state and JWT tokens.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private userContextSignal = signal<UserContext | null>(null);
  private isAuthenticatedSignal = signal(false);
  private loadingSignal = signal(false);
  private errorSignal = signal<string | null>(null);

  // Public derived signals
  readonly userContext = this.userContextSignal.asReadonly();
  readonly isAuthenticated = this.isAuthenticatedSignal.asReadonly();
  readonly isLoading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  // Computed permissions for convenience
  readonly isAdmin = computed(() => this.hasAdminRole(this.userContextSignal()?.role));
  readonly isStaff = computed(() => this.userContextSignal()?.role === Role.StoreStaff);
  readonly isCustomer = computed(() => this.userContextSignal()?.role === Role.Customer);

  constructor(
    private http: HttpClient,
    private storage: StorageService,
    private config: ConfigService
  ) {
    this.initializeFromStorage();
  }

  /**
   * Restore authentication from persistent storage.
   */
  private initializeFromStorage(): void {
    const token = this.storage.getToken();
    const userContext = this.storage.getUserContext();
    if (token && userContext) {
      this.userContextSignal.set(userContext);
      this.isAuthenticatedSignal.set(true);
    }
  }

  /**
   * Login with credentials.
   */
  login(request: LoginRequest): Observable<LoginResponse> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    return this.http.post<ApiResponse<LoginResponse>>(
      this.config.getUrl('/auth/login'),
      request
    ).pipe(
      tap(response => {
        if (response.success && response.data) {
          const userContext = this.toUserContext(response.data.user);
          this.storage.setToken(response.data.accessToken);
          this.storage.setRefreshToken(response.data.refreshToken);
          this.storage.setUserContext(userContext);
          this.userContextSignal.set(userContext);
          console.log('User logged in:', userContext);
          console.log('User logged in role:', userContext.role);
          this.isAuthenticatedSignal.set(true);
        }
        this.loadingSignal.set(false);
      }),
      map(response => response.data!),
      catchError(error => {
        this.errorSignal.set(error.error?.message || 'Login failed');
        this.loadingSignal.set(false);
        throw error;
      })
    );
  }

  /**
   * Logout and clear authentication state.
   */
  logout(): void {
    const refreshToken = this.storage.getRefreshToken();
    if (refreshToken) {
      this.http.post(this.config.getUrl('/auth/logout'), { refreshToken }).subscribe();
    }

    this.clearSession();
  }

  refresh(): Observable<LoginResponse> {
    return this.http.post<ApiResponse<LoginResponse>>(
      this.config.getUrl('/auth/refresh'),
      { refreshToken: this.storage.getRefreshToken() }
    ).pipe(
      tap(response => {
        if (response.success && response.data) {
          const userContext = this.toUserContext(response.data.user);
          this.storage.setToken(response.data.accessToken);
          this.storage.setRefreshToken(response.data.refreshToken);
          this.storage.setUserContext(userContext);
          this.userContextSignal.set(userContext);
          this.isAuthenticatedSignal.set(true);
        }
      }),
      map(response => response.data!),
    );
  }

  clearSession(): void {
    this.storage.clear();
    this.userContextSignal.set(null);
    this.isAuthenticatedSignal.set(false);
    this.errorSignal.set(null);
  }

  /**
   * Get the current JWT token.
   */
  getToken(): string | null {
    return this.storage.getToken();
  }

  getRefreshToken(): string | null {
    return this.storage.getRefreshToken();
  }

  /**
   * Check if user has a specific role.
   */
  hasRole(role: Role): boolean {
    return this.userContextSignal()?.role === role || role === Role.Admin && this.userContextSignal()?.role === Role.ApplicationAdmin;
  }

  /**
   * Check if user has any of the given roles.
   */
  hasAnyRole(...roles: Role[]): boolean {
    const currentRole = this.userContextSignal()?.role;
    return currentRole ? roles.includes(currentRole) : false;
  }

  /**
   * Get user's store ID if applicable.
   */
  getStoreId(): string | undefined {
    return this.userContextSignal()?.storeId;
  }

  /**
   * Get user ID.
   */
  getUserId(): string | null {
    return this.userContextSignal()?.userId || null;
  }

  private hasAdminRole(role?: Role): boolean {
    return role === Role.Admin || role === Role.ApplicationAdmin;
  }

  private toUserContext(user: AuthenticationUser): UserContext {
    const role = user.roles.includes(Role.ApplicationAdmin) ? Role.ApplicationAdmin : user.roles[0] as Role;
    return { userId: user.id, displayName: user.displayName, email: user.email, organizationId: user.organizationId, role, roles: user.roles, permissions: user.permissions, staffCategory: user.staffCategory as StaffCategory | undefined };
  }
}
