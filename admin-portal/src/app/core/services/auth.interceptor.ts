import { Injectable } from '@angular/core';
import {
  HttpInterceptor,
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpContextToken,
} from '@angular/common/http';
import { Observable, catchError, finalize, map, shareReplay, switchMap, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { ConfigService } from './config.service';

/**
 * HTTP interceptor to automatically add JWT tokens to requests.
 * Also handles authorization errors.
 */
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(
    private auth: AuthService,
    private config: ConfigService,
    private router: Router
  ) {}

  private refreshRequest$: Observable<string> | null = null;

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const apiBaseUrl = this.config.getBaseUrl();
    const isApiRequest = request.url.startsWith(apiBaseUrl);
    const isAuthenticationRequest = request.url.endsWith('/auth/login') || request.url.endsWith('/auth/refresh');
    const isRetry = request.context.get(AUTH_RETRY);
    if (!isApiRequest || isAuthenticationRequest) {
      return next.handle(request);
    }

    const token = this.auth.getToken();

    if (token) {
      request = request.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
        },
      });
    }

    return next.handle(request).pipe(
      catchError(error => {
        if (error.status !== 401 || isAuthenticationRequest || isRetry || !this.auth.getRefreshToken()) {
          return throwError(() => error);
        }

        return this.refreshAndRetry(request, next);
      })
    );
  }

  private refreshAndRetry(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (!this.refreshRequest$) {
      this.refreshRequest$ = this.auth.refresh().pipe(
        map(() => this.auth.getToken()),
        switchMap(token => token ? [token] : throwError(() => new Error('Refresh did not return an access token.'))),
        catchError(error => {
          this.auth.clearSession();
          void this.router.navigate(['/login']);
          return throwError(() => error);
        }),
        finalize(() => this.refreshRequest$ = null),
        shareReplay({ bufferSize: 1, refCount: false })
      );
    }

    return this.refreshRequest$.pipe(
      switchMap(token => next.handle(this.withToken(request, token))),
    );
  }

  private withToken(request: HttpRequest<any>, token: string | null): HttpRequest<any> {
    return token
      ? request.clone({
          context: request.context.set(AUTH_RETRY, true),
          setHeaders: { Authorization: `Bearer ${token}` },
        })
      : request;
  }
}

const AUTH_RETRY = new HttpContextToken<boolean>(() => false);
