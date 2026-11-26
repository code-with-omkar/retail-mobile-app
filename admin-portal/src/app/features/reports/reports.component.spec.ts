import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { ReportsComponent } from './reports.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role } from '../../core/models/domain.model';

const totals = { orders: 10, delivered: 6, inProgress: 1, cancelled: 2, rejected: 1, revenue: 5000, averageOrder: 500, cashRevenue: 2000, onlineRevenue: 3000, newCustomers: 2, refunds: 0, refundedAmount: 0 };
const report = (over: Record<string, unknown> = {}) => ({
  from: '2026-10-26', to: '2026-11-01', storeId: null, storeName: null, totals,
  days: [{ date: '2026-10-31', orders: 4, delivered: 3, revenue: 1000 }, { date: '2026-11-01', orders: 6, delivered: 3, revenue: 4000 }],
  hours: [{ hour: 10, orders: 3 }, { hour: 11, orders: 6 }], topProducts: [], stores: [], lowStock: [], income: null, ...over,
});
const ratings = { summary: { count: 4, averageStore: 4.5, averageDelivery: null, stars: [0, 0, 0, 1, 3], partners: [] }, recent: [] };

type Call = (...a: any[]) => Observable<any>;

function open(role: Role, get: Call, http: Partial<Record<'get', Call>> = {}) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiService, useValue: { get } },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
      { provide: ConfigService, useValue: { getUrl: (p: string) => 'http://api' + p } },
      { provide: HttpClient, useValue: http },
    ],
  });
  const fixture = TestBed.createComponent(ReportsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const answer = (url: string) => of({ success: true, data: url === '/reports' ? report() : url === '/reports/ratings' ? ratings : [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] });

describe('ReportsComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('loads the last 7 days on opening: the report and the ratings', () => {
    const get = vi.fn(answer);
    const f = open(Role.StoreStaff, get);
    const c = f.componentInstance;

    expect(get).toHaveBeenCalledWith('/reports', { params: { from: c.from, to: c.to, storeId: 's1' } });
    expect(get).toHaveBeenCalledWith('/reports/ratings', expect.anything());
    expect(c.report()?.totals.orders).toBe(10);
    expect(c.ratings()?.summary.count).toBe(4);
    expect(c.loading()).toBe(false);
  });

  it('the range for 7 days is 7 calendar days long, for today it is one day, for 30 days it is 30', () => {
    const c = open(Role.StoreStaff, answer).componentInstance;
    const days = () => Math.round((Date.parse(c.to) - Date.parse(c.from)) / 86_400_000) + 1;

    expect(days()).toBe(7);
    c.choose('today'); expect(days()).toBe(1);
    c.choose('30'); expect(days()).toBe(30);
  });

  it('choosing a preset loads again, but "Choose dates" waits for the user', () => {
    const get = vi.fn(answer);
    const c = open(Role.StoreStaff, get).componentInstance;
    get.mockClear();

    c.choose('custom');
    expect(get).not.toHaveBeenCalled();
    c.choose('today');
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('both dates are needed', () => {
    const get = vi.fn(answer);
    const c = open(Role.StoreStaff, get).componentInstance;
    get.mockClear();

    c.from = '';
    c.load();

    expect(c.error()).toBe('Choose both dates.');
    expect(get).not.toHaveBeenCalled();
  });

  it('starts on the first store, an administrator can go back to all stores, and staff choose only among what the server gave them', () => {
    const get = vi.fn(answer);
    const c = open(Role.Admin, get).componentInstance;

    expect(c.stores().map(s => s.name)).toEqual(['Kharghar']);
    expect(c.storeId).toBe('s1');
    c.storeId = '';
    c.load();
    expect(get).toHaveBeenLastCalledWith('/reports/ratings', { params: expect.objectContaining({ storeId: null }) });

    const staff = vi.fn(answer);
    TestBed.resetTestingModule();
    const s = open(Role.StoreStaff, staff).componentInstance;
    expect(staff).toHaveBeenCalledWith('/stores');
    expect(s.canChooseStore()).toBe(false); // one store: nothing to choose
  });

  it('a failed load clears the report and shows the message; failing ratings alone stay quiet', () => {
    const c = open(Role.StoreStaff, () => throwError(() => ({ message: 'Server down' }))).componentInstance;
    expect(c.error()).toBe('Server down');
    expect(c.report()).toBeNull();

    TestBed.resetTestingModule();
    const d = open(Role.StoreStaff, (url: string) => (url === '/reports/ratings' ? throwError(() => ({})) : answer(url))).componentInstance;
    expect(d.error()).toBeNull();
    expect(d.ratings()).toBeNull();
    expect(d.report()).not.toBeNull();
  });

  it('works out stars, shares, lost orders, bar heights and chart labels', () => {
    const c = open(Role.StoreStaff, answer).componentInstance;
    const r = c.report()!;

    expect(c.stars(4.4)).toBe('★★★★☆');
    expect(c.stars(9)).toBe('★★★★★');
    expect(c.stars(-1)).toBe('☆☆☆☆☆');
    expect(c.share(c.ratings()!.summary, 5)).toBe(75);
    expect(c.share({ ...c.ratings()!.summary, count: 0 }, 5)).toBe(0);
    expect(c.lostPercent(r)).toBe(30); // 2 cancelled + 1 rejected of 10
    expect(c.lostPercent({ ...r, totals: { ...r.totals, orders: 0 } })).toBe(0);
    expect(c.maxRevenue(r)).toBe(4000);
    expect(c.maxHour(r)).toBe(6);
    expect(c.height(4000, 4000)).toBe(100);
    expect(c.height(1, 4000)).toBe(2); // never invisible
    expect(c.height(5, 0)).toBe(0);
    expect(c.showLabel(r, r.days[1])).toBe(true); // short range: every day is labelled
  });

  it('chart labels are thinned out on long ranges', () => {
    const c = open(Role.StoreStaff, answer).componentInstance;
    const days = Array.from({ length: 30 }, (_, i) => ({ date: `d${i}`, orders: 0, delivered: 0, revenue: 0 }));
    const r = { ...c.report()!, days };

    expect(days.filter(d => c.showLabel(r, d))).toHaveLength(10); // every 3rd day
  });

  it('sums sales and commission over the earnings rows', () => {
    const c = open(Role.Admin, answer).componentInstance;
    const rows = [{ sales: 100, commission: 10 }, { sales: 50, commission: 5 }] as never;
    expect(c.sumOf(rows, 'sales')).toBe(150);
    expect(c.sumOf(rows, 'commission')).toBe(15);
  });

  it('download fetches the csv with the token-bearing client and names the file by the dates', () => {
    const get = vi.fn(() => of(new Blob(['a,b'])));
    const c = open(Role.StoreStaff, answer, { get }).componentInstance;
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    c.download();

    expect(get).toHaveBeenCalledWith('http://api/reports/orders.csv', expect.objectContaining({ responseType: 'blob' }));
    expect(click).toHaveBeenCalled();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:x');
  });

  it('a failed download shows a message', () => {
    const c = open(Role.StoreStaff, answer, { get: () => throwError(() => ({})) }).componentInstance;
    c.download();
    expect(c.error()).toBe('Could not download the orders.');
  });
});
