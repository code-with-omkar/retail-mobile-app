import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { NotificationsComponent, whenText } from './notifications.component';
import { AlertsService } from '../../core/services/alerts.service';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';

const alert = (over: Record<string, unknown> = {}) => ({ id: 'Coupon:1:ending:1', kind: 'Coupon', itemId: '1', name: 'SOON', endsAt: new Date(Date.now() + 2 * 86_400_000).toISOString(), ended: false, stores: [], ...over });

function open(admin: boolean, list: unknown[]) {
  localStorage.clear();
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ApiService, useValue: { get: () => of({ success: true, data: list }) } }, { provide: AuthService, useValue: { isAdmin: () => admin } }] });
  const f = TestBed.createComponent(NotificationsComponent);
  f.detectChanges();
  f.detectChanges();
  return { f, c: f.componentInstance, service: TestBed.inject(AlertsService), text: () => (f.nativeElement as HTMLElement).textContent ?? '' };
}

describe('NotificationsComponent', () => {
  it('lists what is ending, marks it new until read, and filters by kind', () => {
    const { f, c, service, text } = open(true, [alert(), alert({ id: 'Ad:2:ended:2', kind: 'Ad', name: 'Fresh deals', ended: true, stores: ['Harbor'] })]);

    expect(text()).toContain('SOON');
    expect(text()).toContain('Ends in 2 days');
    expect(text()).toContain('Harbor');
    expect(service.unread()).toBe(2);

    c.markAll();
    f.detectChanges();
    expect(service.unread()).toBe(0);
    expect(text()).not.toContain('NEW');

    c.onFilters({ kind: ['Ad'] });
    f.detectChanges();
    expect(c.shown().map(a => a.name)).toEqual(['Fresh deals']);
  });

  it('a store manager can open offers, not the administrator screens', () => {
    const { c, text } = open(false, []);
    expect(text()).toContain('Nothing is ending soon.');
    expect(c.canOpen(alert() as never)).toBe(false);
    expect(c.canOpen(alert({ kind: 'Offer' }) as never)).toBe(true);
  });

  it('says how long is left or how long ago it ended', () => {
    const now = new Date('2026-11-26T10:00:00Z');
    expect(whenText('2026-11-26T15:00:00Z', now)).toBe('Ends in 5 hours');
    expect(whenText('2026-11-25T10:00:00Z', now)).toBe('Ended 1 day ago');
  });
});
