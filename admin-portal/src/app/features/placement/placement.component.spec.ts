import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { PlacementComponent } from './placement.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../core/models/domain.model';

const bid = (over: Record<string, unknown> = {}) => ({ id: 'b1', status: 'Leading', paymentStatus: 2, ...over });
const rules = { enabled: true, minBidPaise: 50000, minStepPaise: 10000, maxWeeksAhead: 4 };
const week = { weekStart: '2026-11-02', minNextBidPaise: 60000 };

type Routes = Record<string, unknown>;
type Call = (...a: any[]) => Observable<any>;

function apiWith(routes: { get?: Routes; post?: Routes; put?: Routes }) {
  const reply = (table: Routes = {}, url: string) => {
    const hit = table[url];
    if (hit instanceof Error) return throwError(() => ({ message: hit.message }));
    return of({ success: true, data: hit ?? null });
  };
  return {
    get: vi.fn(((url: string) => reply(routes.get, url)) as Call),
    post: vi.fn(((url: string) => reply(routes.post, url)) as Call),
    put: vi.fn(((url: string) => reply(routes.put, url)) as Call),
  };
}

function open(role: Role, api: ReturnType<typeof apiWith>) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
    ],
  });
  const fixture = TestBed.createComponent(PlacementComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance };
}

const baseGet = (over: Routes = {}): Routes => ({
  '/placement/bids': [bid()],
  '/placement/rules': rules,
  '/stores': [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Old', isActive: false }],
  '/placement/market': { weeks: [week] },
  ...over,
});

describe('PlacementComponent', () => {
  beforeEach(() => vi.useFakeTimers()); // the payment screen opens on a timer after a bid; never let it run
  afterEach(() => vi.useRealTimers());

  it('loads bids and rules; the rules fill the settings in rupees', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet() }));

    expect(c.bids()).toHaveLength(1);
    expect(c.minBidRupees).toBe('500');
    expect(c.minStepRupees).toBe('100');
    expect(c.weeksAhead).toBe(4);
    expect(c.loading()).toBe(false);
  });

  it('only an administrator loads the active stores', () => {
    const staffApi = apiWith({ get: baseGet() });
    open(Role.StoreStaff, staffApi);
    expect(staffApi.get).not.toHaveBeenCalledWith('/stores');

    TestBed.resetTestingModule();
    const { c } = open(Role.Admin, apiWith({ get: baseGet() }));
    expect(c.stores().map(s => s.name)).toEqual(['Kharghar']);
  });

  it('shows the message when the bids cannot be loaded', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet({ '/placement/bids': new Error('Offline') }) }));
    expect(c.error()).toBe('Offline');
    expect(c.loading()).toBe(false);
  });

  it('describes each bid state, with the refund state of lost or cancelled bids', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet() }));
    const label = (over: Record<string, unknown>) => c.label(bid(over) as never);

    expect(label({ status: 'AwaitingPayment' })).toBe('Awaiting payment');
    expect(label({ status: 'Leading' })).toBe('Leading');
    expect(label({ status: 'Outbid', paymentStatus: 5 })).toBe('Outbid (refunded)');
    expect(label({ status: 'Outbid', paymentStatus: 4 })).toBe('Outbid (refund on its way)');
    expect(label({ status: 'Cancelled', paymentStatus: 6 })).toBe('Cancelled (refund failed)');
    expect(label({ status: 'Cancelled', paymentStatus: 2 })).toBe('Cancelled');
  });

  it('save rules sends paise and the weeks as numbers, and says so', () => {
    const api = apiWith({ get: baseGet(), put: { '/placement/rules': { ...rules, minBidPaise: 75000 } } });
    const { c } = open(Role.Admin, api);

    c.minBidRupees = '750';
    c.minStepRupees = '100';
    c.weeksAhead = 6;
    c.saveRules();

    expect(api.put).toHaveBeenCalledWith('/placement/rules', { enabled: true, minBidPaise: 75000, minStepPaise: 10000, maxWeeksAhead: 6 });
    expect(c.notice()).toBe('Rules saved.');
    expect(c.minBidRupees).toBe('750');
  });

  it('the weeks of a pincode are loaded with that pincode; a failure clears them and shows the message', () => {
    const api = apiWith({ get: baseGet() });
    const { c } = open(Role.StoreStaff, api);

    c.pincode = '410210';
    c.loadMarket();
    expect(api.get).toHaveBeenLastCalledWith('/placement/market', { params: { pincode: '410210' } });
    expect(c.market()).not.toBeNull();

    TestBed.resetTestingModule();
    const bad = open(Role.StoreStaff, apiWith({ get: baseGet({ '/placement/market': new Error('Unknown pincode') }) })).c;
    bad.loadMarket();
    expect(bad.market()).toBeNull();
    expect(bad.error()).toBe('Unknown pincode');
  });

  it('bidding without typing an amount offers the minimum next bid; staff never send a store, administrators do', () => {
    const post = { '/placement/bids': bid({ id: 'b2' }) };
    const staffApi = apiWith({ get: baseGet(), post });
    const staff = open(Role.StoreStaff, staffApi).c;
    staff.pincode = '410210';
    staff.bid(week as never);
    expect(staffApi.post).toHaveBeenCalledWith('/placement/bids', { pincode: '410210', weekStart: '2026-11-02', amountPaise: 60000, storeId: null });
    expect(staff.notice()).toContain('Bid placed.');

    TestBed.resetTestingModule();
    const adminApi = apiWith({ get: baseGet(), post });
    const admin = open(Role.Admin, adminApi).c;
    admin.pincode = '410210';
    admin.storeId = 's1';
    admin.setAmount('2026-11-02', '725.5');
    admin.bid(week as never);
    expect(adminApi.post).toHaveBeenCalledWith('/placement/bids', { pincode: '410210', weekStart: '2026-11-02', amountPaise: 72550, storeId: 's1' });
  });

  it('a refused bid shows the message, frees the button and reads the weeks again', () => {
    const api = apiWith({ get: baseGet(), post: { '/placement/bids': new Error('Bid too low') } });
    const { c } = open(Role.StoreStaff, api);

    c.bid(week as never);

    expect(c.error()).toBe('Bid too low');
    expect(c.busy()).toBe(false);
    expect(api.get).toHaveBeenCalledWith('/placement/market', expect.anything());
  });

  it('cancelling a bid posts to its url and reads the bids again', () => {
    const api = apiWith({ get: baseGet(), post: { '/placement/bids/b1/cancel': {} } });
    const { c } = open(Role.StoreStaff, api);

    c.cancel(bid() as never);

    expect(api.post).toHaveBeenCalledWith('/placement/bids/b1/cancel');
    expect(c.notice()).toBe('Bid cancelled.');
    expect(api.get.mock.calls.filter(x => x[0] === '/placement/bids')).toHaveLength(2);
  });

  it('a failed payment start shows the message', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet(), post: { '/placement/bids/b1/payment': new Error('Payments off') } }));
    c.pay(bid() as never);
    expect(c.error()).toBe('Payments off');
    expect(c.busy()).toBe(false);
  });

  it('formats rupees from paise', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet() }));
    expect(c.rupees(50000)).toBe('₹500');
    expect(c.rupees(50050)).toBe('₹500.50');
  });
});
