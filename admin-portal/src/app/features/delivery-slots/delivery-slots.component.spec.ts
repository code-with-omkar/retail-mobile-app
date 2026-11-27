import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { DeliverySlotsComponent } from './delivery-slots.component';
import { ApiService } from '../../core/services/api.service';

const on = (over: Record<string, unknown> = {}) => ({ enabled: true, open: '09:00', close: '21:00', windowMinutes: 60, capacity: 10, cutoffMinutes: 60, daysAhead: 2, asapAllowed: true, ...over });
const off = on({ enabled: false });
const rules = (over: Record<string, unknown> = {}) => ({
  default: off,
  stores: [
    { storeId: 's1', name: 'Kharghar', override: null, effective: off },
    { storeId: 's2', name: 'Panvel', override: on({ capacity: 4 }), effective: on({ capacity: 4 }) },
  ],
  ...over,
});

type Call = (...a: any[]) => Observable<any>;

function open(opts: { get?: Call; put?: Call; delete?: Call } = {}) {
  const api = {
    get: vi.fn(opts.get ?? (() => of({ success: true, data: rules() }))),
    put: vi.fn(opts.put ?? (() => of({ success: true, data: rules() }))),
    delete: vi.fn(opts.delete ?? (() => of({ success: true, data: rules() }))),
  };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture = TestBed.createComponent(DeliverySlotsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

beforeEach(() => localStorage.clear());

describe('DeliverySlotsComponent', () => {
  it('shows the default and each store, saying which have their own settings', () => {
    const { f } = open();

    expect(text(f)).toContain('Kharghar');
    expect(text(f)).toContain('uses the default');
    expect(text(f)).toContain('Panvel');
    expect(text(f)).toContain('own settings');
    expect(text(f)).toContain('As soon as possible only'); // the default is off
    expect(text(f)).toContain('4 orders each');
  });

  it('shows the message when loading fails', () => {
    expect(open({ get: () => throwError(() => ({ message: 'Offline' })) }).c.error()).toBe('Offline');
  });

  it('describes off, scheduled-only and scheduled-or-asap settings', () => {
    const { c } = open();
    expect(c.describe(off as never)).toBe('As soon as possible only');
    expect(c.describe(on({ enabled: false, asapAllowed: false }) as never)).toBe('Off');
    expect(c.describe(on() as never)).toBe('09:00 to 21:00, 60-minute windows, 10 orders each, 2 days, cut-off 60 min, or as soon as possible');
    expect(c.describe(on({ asapAllowed: false, daysAhead: 1 }) as never)).toBe('09:00 to 21:00, 60-minute windows, 10 orders each, 1 day, cut-off 60 min');
  });

  it('editing the default opens its settings and saving puts them to the default route', () => {
    const { c, api } = open();

    c.edit('default');
    expect(c.editing()).toBe('default');
    c.form.enabled = true;
    c.form.capacity = '7' as never; // a number input can give text
    c.save();

    expect(api.put).toHaveBeenCalledWith('/admin/delivery-slots/default', expect.objectContaining({ enabled: true, capacity: 7, open: '09:00', windowMinutes: 60 }));
    expect(c.editing()).toBeNull();
    expect(c.notice()).toBe('Saved. New orders use it.');
  });

  it('editing a store starts from what it uses now and saves to its own route', () => {
    const { c, api } = open();

    c.edit('s2');
    expect(c.form.capacity).toBe(4);
    c.form.capacity = 6;
    c.save();

    expect(api.put).toHaveBeenCalledWith('/admin/delivery-slots/stores/s2', expect.objectContaining({ capacity: 6 }));
  });

  it('Use default removes the store\'s own settings', () => {
    const { c, api } = open();

    c.useDefault('s2');

    expect(api.delete).toHaveBeenCalledWith('/admin/delivery-slots/stores/s2');
    expect(c.notice()).toBe('Panvel uses the default again.');
  });

  it('a refused save keeps the form open and shows the server message', () => {
    const { c } = open({ put: () => throwError(() => ({ message: 'A window can be 30, 45, 60, 90, 120 or 180 minutes long.' })) });
    c.edit('default');

    c.save();

    expect(c.error()).toBe('A window can be 30, 45, 60, 90, 120 or 180 minutes long.');
    expect(c.editing()).toBe('default');
    expect(c.busy()).toBe(false);
  });

  it('a second click while saving is ignored', () => {
    const { c, api } = open();
    c.edit('default');
    c.busy.set(true);
    c.save();
    c.useDefault('s2');
    expect(api.put).not.toHaveBeenCalled();
    expect(api.delete).not.toHaveBeenCalled();
  });
});
