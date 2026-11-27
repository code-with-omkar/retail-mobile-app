import { ErrorHandler, Provider } from '@angular/core';
import * as Sentry from '@sentry/angular';

/**
 * Crash reporting with Sentry. It is off until the page sets window.__SENTRY_DSN__, and it never sends who the person is:
 * no user, no request headers, cookies or query string, no typed values.
 */
export function scrubEvent<T extends Sentry.ErrorEvent>(event: T): T {
  event.user = {};
  if (event.request) {
    delete event.request.headers;
    delete event.request.cookies;
    delete event.request.data;
    delete event.request.query_string;
  }
  // Clicks and navigation are kept as a trail, but not what was typed or the address (it can carry ids).
  event.breadcrumbs = (event.breadcrumbs ?? []).filter(item => item.category !== 'ui.input').map(item => ({ ...item, data: undefined }));
  return event;
}

export function crashReportingDsn(win: unknown): string | null {
  const dsn = (win as { __SENTRY_DSN__?: unknown } | null | undefined)?.__SENTRY_DSN__;
  return typeof dsn === 'string' && dsn.trim() ? dsn.trim() : null;
}

/** Providers to add to the application: nothing when no key is set, otherwise Sentry starts and takes over unhandled errors. */
export function crashReportingProviders(win: unknown = typeof window !== 'undefined' ? window : null): Provider[] {
  const dsn = crashReportingDsn(win);
  if (!dsn) return [];
  Sentry.init({ dsn, sendDefaultPii: false, tracesSampleRate: 0, beforeSend: scrubEvent });
  return [{ provide: ErrorHandler, useValue: Sentry.createErrorHandler({ showDialog: false }) }];
}
