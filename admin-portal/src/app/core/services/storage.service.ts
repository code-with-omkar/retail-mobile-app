import { Injectable } from '@angular/core';

/**
 * Persistent storage service for tokens and user data.
 */
@Injectable({ providedIn: 'root' })
export class StorageService {
  private readonly TOKEN_KEY = 'app:token';
  private readonly REFRESH_TOKEN_KEY = 'app:refresh_token';
  private readonly USER_CONTEXT_KEY = 'app:user_context';

  setToken(token: string): void {
    if (typeof window !== 'undefined') {
      localStorage.setItem(this.TOKEN_KEY, token);
    }
  }

  getToken(): string | null {
    return typeof window !== 'undefined' ? localStorage.getItem(this.TOKEN_KEY) : null;
  }

  removeToken(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.TOKEN_KEY);
    }
  }

  setRefreshToken(token: string): void {
    if (typeof window !== 'undefined') {
      localStorage.setItem(this.REFRESH_TOKEN_KEY, token);
    }
  }

  getRefreshToken(): string | null {
    return typeof window !== 'undefined' ? localStorage.getItem(this.REFRESH_TOKEN_KEY) : null;
  }

  removeRefreshToken(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    }
  }

  setUserContext(context: any): void {
    if (typeof window !== 'undefined') {
      localStorage.setItem(this.USER_CONTEXT_KEY, JSON.stringify(context));
    }
  }

  getUserContext(): any {
    if (typeof window === 'undefined') return null;
    const stored = localStorage.getItem(this.USER_CONTEXT_KEY);
    return stored ? JSON.parse(stored) : null;
  }

  removeUserContext(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.USER_CONTEXT_KEY);
    }
  }

  clear(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.TOKEN_KEY);
      localStorage.removeItem(this.REFRESH_TOKEN_KEY);
      localStorage.removeItem(this.USER_CONTEXT_KEY);
    }
  }
}
