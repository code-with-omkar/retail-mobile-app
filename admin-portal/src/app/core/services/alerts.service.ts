import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';

export interface PortalAlert { id: string; kind: 'Coupon' | 'Offer' | 'Ad'; itemId: string; name: string; endsAt: string; ended: boolean; stores: string[]; }

const READ_KEY = 'qc-read-alerts';

/**
 * Coupons, offers and ads that end soon or have just ended. What was read is remembered in this browser only; an alert becomes new again
 * when the thing ends or its end date changes.
 */
@Injectable({ providedIn: 'root' })
export class AlertsService {
  private api = inject(ApiService);

  readonly alerts = signal<PortalAlert[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  private readonly read = signal<ReadonlySet<string>>(stored());
  readonly unread = computed(() => this.alerts().filter(a => !this.read().has(a.id)).length);

  isRead(id: string) { return this.read().has(id); }

  refresh() {
    this.loading.set(true);
    this.api.get<PortalAlert[]>('/admin/alerts').subscribe({
      next: r => { this.alerts.set(r.data ?? []); this.loading.set(false); this.error.set(null); },
      error: e => { this.loading.set(false); this.error.set(e?.message ?? 'Could not load notifications.'); },
    });
  }

  markRead(ids: string[]) {
    // Only what is still listed is kept, so the list in the browser does not grow for ever.
    const listed = new Set(this.alerts().map(a => a.id));
    const next = new Set([...this.read(), ...ids].filter(id => listed.has(id)));
    this.read.set(next);
    try { localStorage.setItem(READ_KEY, JSON.stringify([...next])); } catch { /* a private window: read marks last until reload */ }
  }
}

function stored(): ReadonlySet<string> {
  try { return new Set<string>(JSON.parse(localStorage.getItem(READ_KEY) ?? '[]')); } catch { return new Set<string>(); }
}
