import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, timeout } from 'rxjs/operators';
import { ConfigService } from './config.service';
import { ApiResponse } from '../models/api.model';

/**
 * Centralized API service for all HTTP requests.
 * All feature services should use this for API calls.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(
    private http: HttpClient,
    private config: ConfigService
  ) {}

  /**
   * Generic GET request.
   */
  get<T>(endpoint: string, options?: { params?: any }): Observable<ApiResponse<T>> {
    let httpParams = new HttpParams();
    if (options?.params) {
      Object.keys(options.params).forEach(key => {
        if (options.params[key] !== null && options.params[key] !== undefined) {
          httpParams = httpParams.set(key, options.params[key]);
        }
      });
    }

    return this.http.get<ApiResponse<T>>(
      this.config.getUrl(endpoint),
      { params: httpParams }
    ).pipe(
      timeout(this.config.getTimeout()),
      catchError(error => this.handleError(error))
    );
  }

  /**
   * Generic POST request.
   */
  post<T>(endpoint: string, body: any = null): Observable<ApiResponse<T>> {
    return this.http.post<ApiResponse<T>>(
      this.config.getUrl(endpoint),
      body
    ).pipe(
      timeout(this.config.getTimeout()),
      catchError(error => this.handleError(error))
    );
  }

  /**
   * Generic PUT request.
   */
  put<T>(endpoint: string, body: any = null): Observable<ApiResponse<T>> {
    return this.http.put<ApiResponse<T>>(
      this.config.getUrl(endpoint),
      body
    ).pipe(
      timeout(this.config.getTimeout()),
      catchError(error => this.handleError(error))
    );
  }

  /**
   * Generic PATCH request.
   */
  patch<T>(endpoint: string, body: any = null): Observable<ApiResponse<T>> {
    return this.http.patch<ApiResponse<T>>(
      this.config.getUrl(endpoint),
      body
    ).pipe(
      timeout(this.config.getTimeout()),
      catchError(error => this.handleError(error))
    );
  }

  /**
   * Generic DELETE request.
   */
  delete<T>(endpoint: string): Observable<ApiResponse<T>> {
    return this.http.delete<ApiResponse<T>>(
      this.config.getUrl(endpoint)
    ).pipe(
      timeout(this.config.getTimeout()),
      catchError(error => this.handleError(error))
    );
  }

  /**
   * Handle API errors consistently.
   */
  private handleError(error: any) {
    let errorMessage = 'An unexpected error occurred';

    if (error.error instanceof ErrorEvent) {
      // Client-side error
      errorMessage = error.error.message;
    } else if (error.status) {
      // Server-side error
      if (error.error?.message) {
        errorMessage = error.error.message;
      } else if (error.error?.errors?.length) {
        errorMessage = error.error.errors.join(', ');
      } else {
        errorMessage = `Error: ${error.status} ${error.statusText}`;
      }
    } else if (error.name === 'TimeoutError') {
      errorMessage = 'Request timeout';
    }

    return throwError(() => ({
      status: error.status,
      message: errorMessage,
      details: error.error,
    }));
  }
}
