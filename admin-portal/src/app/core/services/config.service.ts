import { Injectable } from '@angular/core';
import { API_CONFIG } from '../config/api-config';

/**
 * Centralized configuration service.
 * Allows dynamic configuration override for different environments.
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  private baseUrl = API_CONFIG.baseUrl;
  private timeout = API_CONFIG.timeout;

  /**
   * Set API base URL (typically called during app initialization).
   */
  setBaseUrl(url: string): void {
    this.baseUrl = url;
  }

  /**
   * Get the current API base URL.
   */
  getBaseUrl(): string {
    return this.baseUrl;
  }

  /**
   * Get full URL for an endpoint.
   */
  getUrl(endpoint: string): string {
    return `${this.baseUrl}${endpoint}`;
  }

  /**
   * Get request timeout.
   */
  getTimeout(): number {
    return this.timeout;
  }

  /**
   * Set request timeout.
   */
  setTimeout(ms: number): void {
    this.timeout = ms;
  }
}
