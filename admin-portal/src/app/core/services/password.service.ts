import { Injectable, inject } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';

/** The rules the server applies, said before it has to. Null when the new password looks acceptable. */
export function passwordProblem(password: string, again: string): string | null {
  if (password.length < 8) return 'Use at least 8 characters.';
  if (!/[A-Za-z]/.test(password) || !/\d/.test(password)) return 'Use letters and at least one number.';
  return password === again ? null : 'The two passwords are not the same.';
}

@Injectable({ providedIn: 'root' })
export class PasswordService {
  private api = inject(ApiService);
  private auth = inject(AuthService);

  /** Changes the signed-in person's password, then renews the session so it no longer asks for a change. */
  change(currentPassword: string, newPassword: string): Observable<unknown> {
    return this.api.post<unknown>('/auth/change-password', { currentPassword, newPassword, refreshToken: this.auth.getRefreshToken() })
      .pipe(switchMap(() => this.auth.refresh()));
  }

  /** Sends a code to the email address, when there is such an account. The answer is the same either way. */
  forgot(email: string) { return this.api.post<unknown>('/auth/forgot-password', { email }); }

  reset(email: string, code: string, newPassword: string) { return this.api.post<unknown>('/auth/reset-password', { email, code, newPassword }); }
}

export const errorText = (e: any, otherwise: string): string => e?.error?.errors?.[0] ?? e?.error?.message ?? e?.message ?? otherwise;
