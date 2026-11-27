import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CouponsComponent } from './coupons.component';
import { ApiService } from '../../core/services/api.service';

const coupon = (over: Record<string, unknown> = {}) => ({
  id: 'c1', code: 'WELCOME', description: null, kind: 'Percent', value: 10, maxDiscount: 100, minSubtotal: 200, startsAt: '2026-11-01T00:00:00Z', endsAt: null,
  totalLimit: null, perCustomerLimit: 1, firstOrderOnly: true, isActive: true, usedCount: 3, discountGiven: 150, status: 'Active', ...over,
});

type Call = (...a: any[]) => Observable<any>;

function open(opts: { list?: unknown; post?: Call; put?: Call } = {}) {
  const api = {
    get: vi.fn(((url: string) => (url === '/admin/coupons' && opts.list instanceof Error ? throwError(() => ({ message: opts.list instanceof Error ? opts.list.message : '' })) : of({ success: true, data: opts.list ?? [coupon()] }))) as Call),
    post: vi.fn(opts.post ?? (() => of({ success: true, data: coupon() }))),
    put: vi.fn(opts.put ?? (() => of({ success: true, data: coupon() }))),
  };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture = TestBed.createComponent(CouponsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('CouponsComponent', () => {
  it('lists the coupons with what they do, how often they were used and what they cost', () => {
    const { f } = open();

    expect(text(f)).toContain('WELCOME');
    expect(text(f)).toContain('10% off, up to ₹100');
    expect(text(f)).toContain('Minimum order ₹200');
    expect(text(f)).toContain('First order only');
    expect(text(f)).toContain('₹150');
  });

  it('says so when there are none, and shows the message when loading fails', () => {
    expect(text(open({ list: [] }).f)).toContain('No coupons yet.');
    TestBed.resetTestingModule();
    expect(open({ list: new Error('Offline') }).c.error()).toBe('Offline');
  });

  it('describes each kind', () => {
    const { c } = open();
    expect(c.describe(coupon({ kind: 'Flat', value: 50 }) as never)).toBe('₹50 off');
    expect(c.describe(coupon({ kind: 'FreeDelivery', value: 0 }) as never)).toBe('Free delivery');
    expect(c.describe(coupon({ maxDiscount: null }) as never)).toBe('10% off');
  });

  it('creating sends the code, kind, amounts and limits, then clears the form and reads the list again', () => {
    const { c, api } = open();
    c.code = ' save10 ';
    c.kind = 'Percent';
    c.value = 10;
    c.cap = 50;
    c.minSubtotal = 100;
    c.totalLimit = 200;
    c.firstOrderOnly = true;
    c.endsAt = '2030-01-01T10:00';

    c.save();

    expect(api.post).toHaveBeenCalledWith('/admin/coupons', expect.objectContaining({
      code: 'save10', kind: 'Percent', value: 10, maxDiscount: 50, minSubtotal: 100, totalLimit: 200, perCustomerLimit: null, firstOrderOnly: true, isActive: true,
      startsAt: null, endsAt: new Date('2030-01-01T10:00').toISOString(),
    }));
    expect(c.notice()).toBe('Coupon created.');
    expect(c.code).toBe('');
    expect(api.get.mock.calls.filter(x => x[0] === '/admin/coupons')).toHaveLength(2);
  });

  it('free delivery sends no amount and no cap; flat sends no cap', () => {
    const { c, api } = open();
    c.code = 'FREEDEL'; c.kind = 'FreeDelivery'; c.value = 99; c.cap = 10;
    c.save();
    expect(api.post).toHaveBeenLastCalledWith('/admin/coupons', expect.objectContaining({ value: 0, maxDiscount: null }));

    c.code = 'FLAT50'; c.kind = 'Flat'; c.value = 50; c.cap = 10;
    c.save();
    expect(api.post).toHaveBeenLastCalledWith('/admin/coupons', expect.objectContaining({ value: 50, maxDiscount: null }));
  });

  it('editing fills the form and saves with the id; Cancel puts the form back to new', () => {
    const { c, api } = open();

    c.edit(coupon() as never);
    expect(c.editing()?.id).toBe('c1');
    expect(c.code).toBe('WELCOME');
    expect(c.value).toBe(10);
    expect(c.cap).toBe(100);
    expect(c.perCustomerLimit).toBe(1);

    c.save();
    expect(api.put).toHaveBeenCalledWith('/admin/coupons/c1', expect.objectContaining({ code: 'WELCOME', isActive: true }));
    expect(c.notice()).toBe('Coupon saved. New orders use it.');
    expect(c.editing()).toBeNull();
  });

  it('a refused save shows the server message and frees the button', () => {
    const { c } = open({ post: () => throwError(() => ({ message: 'That code already exists.' })) });
    c.code = 'WELCOME'; c.value = 5; c.kind = 'Flat';

    c.save();

    expect(c.error()).toBe('That code already exists.');
    expect(c.busy()).toBe(false);
  });

  it('Switch off sends the coupon as it is with only the flag changed; Switch on does the opposite', () => {
    const { c, api } = open();

    c.toggle(coupon() as never);
    expect(api.put).toHaveBeenLastCalledWith('/admin/coupons/c1', expect.objectContaining({ code: 'WELCOME', value: 10, maxDiscount: 100, minSubtotal: 200, isActive: false, firstOrderOnly: true }));
    expect(c.notice()).toBe('WELCOME switched off.');

    c.toggle(coupon({ isActive: false }) as never);
    expect(api.put).toHaveBeenLastCalledWith('/admin/coupons/c1', expect.objectContaining({ isActive: true }));
  });

  it('a second click while saving is ignored', () => {
    const { c, api } = open();
    c.busy.set(true);
    c.save();
    c.toggle(coupon() as never);
    expect(api.post).not.toHaveBeenCalled();
    expect(api.put).not.toHaveBeenCalled();
  });

  it('sends the look: design, picture, ribbon and animation; a pop-up is shown in the cart too, and switching off keeps it all', () => {
    const { c, api } = open();
    c.code = 'FEST'; c.kind = 'Flat'; c.value = 20;
    c.design = 'diwali'; c.imageUrl.set('/media/offers/a.png'); c.ribbon = ' NEW '; c.showAsPopup = true; c.animation = 'confetti';
    c.save();
    expect(api.post).toHaveBeenLastCalledWith('/admin/coupons', expect.objectContaining({
      design: 'diwali', imageUrl: '/media/offers/a.png', ribbon: 'NEW', showInApp: true, showAsPopup: true, animation: 'confetti',
    }));
    expect(c.design).toBe('');

    c.toggle(coupon({ design: 'holi', showInApp: true, animation: 'gift' }) as never);
    expect(api.put).toHaveBeenLastCalledWith('/admin/coupons/c1', expect.objectContaining({ design: 'holi', showInApp: true, animation: 'gift' }));
  });
});
