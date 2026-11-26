import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { BehaviourComponent } from './behaviour.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../core/models/domain.model';

const funnel = [
  { name: 'app_open', sessions: 200, percentOfFirst: 100, percentOfPrevious: 100 },
  { name: 'product_view', sessions: 100, percentOfFirst: 50, percentOfPrevious: 50 },
  { name: 'add_to_cart', sessions: 40, percentOfFirst: 20, percentOfPrevious: 40 },
  { name: 'checkout_start', sessions: 20, percentOfFirst: 10, percentOfPrevious: 50 },
  { name: 'order_placed', sessions: 10, percentOfFirst: 5, percentOfPrevious: 50 },
];
const report = (over: Record<string, unknown> = {}) => ({
  from: '2026-10-28', to: '2026-11-03', storeId: null, funnel,
  steps: [{ name: 'app_open', events: 250, sessions: 200 }, { name: 'search', events: 90, sessions: 60 }],
  topSearches: [{ detail: 'tomato', count: 12 }, { detail: 'milk', count: 7 }],
  couponRefusals: [{ detail: 'belowminimum', count: 4 }, { detail: 'expired', count: 1 }],
  dailySessions: [{ day: '2026-11-02', sessions: 30 }, { day: '2026-11-03', sessions: 10 }],
  ...over,
});

type Call = (...a: any[]) => Observable<any>;

function open(role: Role, get: Call) {
  const api = { get: vi.fn(get) };
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
    ],
  });
  const fixture = TestBed.createComponent(BehaviourComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const answer = (data: unknown = report()): Call => (url: string) => of({ success: true, data: url === '/stores' ? [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] : data });
const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('BehaviourComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('store staff see the funnel for the last 7 days at once, with the share of each step', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-03T05:00:00Z'));
    const { f, api } = open(Role.StoreStaff, answer());

    expect(api.get).toHaveBeenCalledWith('/reports/behaviour', { params: { from: '2026-10-28', to: '2026-11-03', storeId: 's1' } });
    expect(text(f)).toContain('Opened the app');
    expect(text(f)).toContain('Placed an order');
    expect(text(f)).toContain('5% of openers');
    expect(text(f)).toContain('50% of the step before');
    expect((f.nativeElement as HTMLElement).querySelectorAll('.bar-row')).toHaveLength(5);
  });

  it('the bars are as wide as the share of the first step', () => {
    const { f } = open(Role.StoreStaff, answer());
    const widths = Array.from((f.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.fill')).map(el => el.style.width);
    expect(widths).toEqual(['100%', '50%', '20%', '10%', '5%']);
  });

  it('an administrator can pick a store or all stores, and the choice goes to the report', () => {
    const { c, api } = open(Role.Admin, answer());
    expect(c.stores().map(s => s.name)).toEqual(['Kharghar']); // inactive stores are left out

    c.pickStore('s1');
    expect(api.get).toHaveBeenLastCalledWith('/reports/behaviour', { params: expect.objectContaining({ storeId: 's1' }) });
    c.pickStore('');
    expect(api.get).toHaveBeenLastCalledWith('/reports/behaviour', { params: expect.objectContaining({ storeId: null }) });
  });

  it('store staff send their own store', () => {
    const { c, api } = open(Role.StoreStaff, answer());
    expect(c.storeId).toBe('s1');
    c.load();
    expect(api.get).toHaveBeenLastCalledWith('/reports/behaviour', { params: expect.objectContaining({ storeId: 's1' }) });
  });

  it('the period choices are 7, 30 and 90 days ending today in India time', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-03T20:00:00Z')); // already 4 Nov in India
    const { c } = open(Role.StoreStaff, answer());

    expect(c.range()).toEqual({ from: '2026-10-29', to: '2026-11-04' });
    c.choose('30');
    expect(c.range()).toEqual({ from: '2026-10-06', to: '2026-11-04' });
    c.choose('90');
    expect(c.range()).toEqual({ from: '2026-08-07', to: '2026-11-04' }); // 90 days, under the server's 92-day limit
  });

  it('lists top searches and coupon refusals in plain words', () => {
    const { f } = open(Role.StoreStaff, answer());
    expect(text(f)).toContain('tomato');
    expect(text(f)).toContain('Order below the minimum');
    expect(text(f)).toContain('Expired');
  });

  it('says so when nothing was recorded', () => {
    const empty = report({ funnel: funnel.map(s => ({ ...s, sessions: 0, percentOfFirst: 0, percentOfPrevious: 0 })), topSearches: [], couponRefusals: [], dailySessions: [] });
    const { f } = open(Role.StoreStaff, answer(empty));
    expect(text(f)).toContain('No sessions were recorded in this period yet.');
    expect(text(f)).toContain('No searches yet.');
    expect(text(f)).toContain('No refusals.');
  });

  it('chart bars scale to the busiest day, with a visible minimum', () => {
    const { c } = open(Role.StoreStaff, answer());
    const r = report() as never;
    expect(c.height(30, r)).toBe(100);
    expect(c.height(10, r)).toBe(33);
    expect(c.height(0, r)).toBe(2);
  });

  it('shows the server message when it cannot load', () => {
    const { c, f } = open(Role.StoreStaff, () => throwError(() => ({ message: 'Customer behaviour is for store staff and administrators.' })));
    expect(c.error()).toBe('Customer behaviour is for store staff and administrators.');
    expect(text(f)).toContain('Customer behaviour is for store staff');
    expect(c.report()).toBeNull();
  });

  it('every step has a plain label, and an unknown one falls back to its name', () => {
    const { c } = open(Role.StoreStaff, answer());
    expect(c.label('out_of_service_area')).toBe('Address outside the service area');
    expect(c.label('something_new')).toBe('something_new');
    expect(c.reason('customerlimitreached')).toBe('Already used by the customer');
    expect(c.reason('weird')).toBe('weird');
  });
});
