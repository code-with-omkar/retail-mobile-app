import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { TodayComponent } from './today.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../core/models/domain.model';

const counts = { placed: 12, new: 2, packing: 3, ready: 1, outForDelivery: 4, delivered: 8, cancelled: 1, revenue: 5400 };
const summary = (over: Record<string, unknown> = {}) => ({
  storeId: 's1', storeName: 'Kharghar', asOf: '2026-11-02T04:30:00Z', counts, alerts: [], upcoming: [], ...over,
});
const urgent = { kind: 'PendingTooLong', severity: 'urgent', title: 'ORD-1 has been waiting 12 minutes', detail: 'A new order not yet accepted or rejected.', orderId: 'o1' };
const warning = { kind: 'OutOfStock', severity: 'warning', title: '2 items are out of stock', detail: 'Milk 1 l, Bread. Customers cannot buy these.', orderId: null };

type Call = (...a: any[]) => Observable<any>;

function open(role: Role, get: Call) {
  const api = { get: vi.fn(get) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
    ],
  });
  const fixture = TestBed.createComponent(TodayComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const answer = (data: unknown = summary()): Call => (url: string) => of({ success: true, data: url === '/stores' ? [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] : data });
const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('TodayComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('store staff see their store at once, with the counts', () => {
    const { f, api } = open(Role.StoreStaff, answer());

    expect(api.get).toHaveBeenCalledWith('/store-today', { params: { storeId: 's1' } });
    expect(text(f)).toContain('Today · Kharghar');
    expect(text(f)).toContain('of 12 placed today');
    expect(text(f)).toContain('₹5,400');
  });

  it('an administrator starts on the first store by name and can pick another', () => {
    const get: Call = url => of({ success: true, data: url === '/stores' ? [{ id: 's3', name: 'Panvel', isActive: true }, { id: 's1', name: 'Belapur', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] : summary() });
    const { c, api } = open(Role.Admin, get);

    expect(c.stores().map(s => s.name)).toEqual(['Belapur', 'Panvel']);
    expect(api.get).toHaveBeenCalledWith('/store-today', { params: { storeId: 's1' } });

    c.choose('s3');
    expect(api.get).toHaveBeenLastCalledWith('/store-today', { params: { storeId: 's3' } });
  });

  it('says nothing needs attention when there are no alerts, and quotes the limits', () => {
    const { f } = open(Role.StoreStaff, answer());
    expect(text(f)).toContain('Nothing needs attention right now.');
    expect(text(f)).toContain('5 minutes (urgent at 10)');
    expect(text(f)).toContain('80% full');
  });

  it('lists the alerts, urgent ones marked, with a way to the orders or the stock', () => {
    const { f } = open(Role.StoreStaff, answer(summary({ alerts: [urgent, warning] })));
    const el = f.nativeElement as HTMLElement;

    expect(text(f)).toContain('ORD-1 has been waiting 12 minutes');
    expect(text(f)).toContain('2 items are out of stock');
    expect(el.querySelectorAll('.alert')).toHaveLength(2);
    expect(el.querySelectorAll('.alert.urgent')).toHaveLength(1);
    expect(Array.from(el.querySelectorAll('.go')).map(a => a.textContent?.trim())).toEqual(['Open orders', 'Open stock']);
  });

  it('lists the scheduled orders coming up, or says there are none', () => {
    expect(text(open(Role.StoreStaff, answer()).f)).toContain('No scheduled deliveries are waiting.');
    TestBed.resetTestingModule();
    const { f } = open(Role.StoreStaff, answer(summary({ upcoming: [{ orderId: 'o2', orderNumber: 'ORD-2', startUtc: 'x', endUtc: 'y', label: 'Today, 2 pm - 3 pm', status: 'Accepted' }] })));
    expect(text(f)).toContain('Today, 2 pm - 3 pm');
    expect(text(f)).toContain('ORD-2');
    expect(text(f)).toContain('Accepted');
  });

  it('shows the server message when it cannot load', () => {
    const { c } = open(Role.StoreStaff, () => throwError(() => ({ message: 'Store staff must be assigned to a store.' })));
    expect(c.error()).toBe('Store staff must be assigned to a store.');
    expect(c.loading()).toBe(false);
  });

  it('refreshes every 30 seconds without the loading state, and stops when closed', () => {
    vi.useFakeTimers();
    const { f, c, api } = open(Role.StoreStaff, answer());
    const reads = () => api.get.mock.calls.filter(x => x[0] === '/store-today').length;

    expect(reads()).toBe(1);
    vi.advanceTimersByTime(30000);
    expect(reads()).toBe(2);
    expect(c.loading()).toBe(false);

    f.destroy();
    vi.advanceTimersByTime(120000);
    expect(reads()).toBe(2);
  });

  it('with no store to show, an administrator reads nothing and is not refreshed', () => {
    vi.useFakeTimers();
    const { api } = open(Role.Admin, url => of({ success: true, data: url === '/stores' ? [] : summary() }));
    vi.advanceTimersByTime(60000);
    expect(api.get.mock.calls.filter(x => x[0] === '/store-today')).toHaveLength(0);
  });
});
