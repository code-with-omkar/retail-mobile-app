import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../core/services/api.service';

const snapshot = (over: Record<string, unknown> = {}) => ({
  totalOrders: 12, totalProducts: 40, totalCustomers: 7, activeStores: 3, todaysOrders: 2, activeOrders: 1, completedOrders: 9, pendingOrders: 1, cancelledOrders: 1, totalSales: 5400,
  activeOrdersList: [{ orderNumber: 'ORD-9', customer: 'Asha', store: 'Kharghar', status: 'Packing', orderTime: '2026-11-01T10:00:00Z', amount: 120, deliveryPartner: null }],
  ...over,
});

function open(get: (...a: any[]) => any) {
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { get } }] });
  const fixture = TestBed.createComponent(DashboardComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('DashboardComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('shows the totals and the active orders, with Unassigned when no partner has it', () => {
    const f = open(() => of({ success: true, data: snapshot() }));

    expect(text(f)).toContain('Total orders');
    expect(text(f)).toContain('12');
    expect(text(f)).toContain('₹5,400');
    expect(text(f)).toContain('ORD-9');
    expect(text(f)).toContain('Unassigned');
    expect(text(f)).toContain('1 active');
  });

  it('says so when no order is active', () => {
    const f = open(() => of({ success: true, data: snapshot({ activeOrders: 0, activeOrdersList: [] }) }));
    expect(text(f)).toContain('No active orders');
  });

  it('shows the server message when the call fails, and Retry loads again', () => {
    let fail = true;
    const get = vi.fn(() => (fail ? throwError(() => ({ message: 'Offline' })) : of({ success: true, data: snapshot() })));
    const f = open(get);

    expect(f.componentInstance.error()).toBe('Offline');
    expect(text(f)).toContain('Unable to load dashboard');

    fail = false;
    f.componentInstance.loadData();
    expect(f.componentInstance.error()).toBeNull();
    expect(f.componentInstance.dashboard()?.totalOrders).toBe(12);
  });

  it('a reply without data is shown as an error', () => {
    const f = open(() => of({ success: false, message: 'No access' }));
    expect(f.componentInstance.error()).toBe('No access');
  });

  it('refreshes every 30 seconds without the loading state, and stops when closed', () => {
    vi.useFakeTimers();
    const get = vi.fn(() => of({ success: true, data: snapshot() }));
    const f = open(get);

    expect(get).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(30000);
    expect(get).toHaveBeenCalledTimes(2);
    expect(f.componentInstance.isLoading()).toBe(false);

    f.destroy();
    vi.advanceTimersByTime(90000);
    expect(get).toHaveBeenCalledTimes(2);
  });
});
