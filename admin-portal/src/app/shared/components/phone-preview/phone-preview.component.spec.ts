import { TestBed } from '@angular/core/testing';
import { PhonePreviewComponent, couponSaving } from './phone-preview.component';

const coupon = (over: Record<string, unknown> = {}) => ({ code: 'SAVE', kind: 'Percent' as const, value: 20, cap: 0, minSubtotal: 0, ...over });
const offer = (over: Record<string, unknown> = {}) => ({ titleEn: 'Diwali sale', titleMr: 'दिवाळी सेल', bodyEn: 'Sweets are here', bodyMr: '', discountPercent: 0, design: '', imageUrl: null, endsAt: null, popup: false, ...over });

function show(inputs: Record<string, unknown>) {
  const f = TestBed.createComponent(PhonePreviewComponent);
  for (const [key, value] of Object.entries(inputs)) f.componentRef.setInput(key, value);
  f.detectChanges();
  return { f, c: f.componentInstance, text: () => (f.nativeElement as HTMLElement).textContent ?? '' };
}

describe('phone preview', () => {
  it('works out what a coupon takes off', () => {
    expect(couponSaving(coupon(), 300, 25)).toBe(60);
    expect(couponSaving(coupon({ cap: 40 }), 300, 25)).toBe(40);
    expect(couponSaving(coupon({ kind: 'Flat', value: 500 }), 300, 25)).toBe(300);
    expect(couponSaving(coupon({ kind: 'FreeDelivery' }), 300, 25)).toBe(25);
    expect(couponSaving(coupon({ minSubtotal: 400 }), 300, 25)).toBe(0);
  });

  it('shows a coupon in the cart, or how much more is needed', () => {
    expect(show({ coupon: coupon() }).text()).toContain('you save ₹60');
    expect(show({ coupon: coupon({ minSubtotal: 400 }) }).text()).toContain('Add ₹100 more');
  });

  it('shows an offer in the chosen language, with the pop-up when asked', () => {
    const { f, c, text } = show({ offer: offer({ popup: true }) });
    expect(text()).toContain('Diwali sale');
    expect((f.nativeElement as HTMLElement).querySelector('.popup')).not.toBeNull();
    c.marathi.set(true);
    f.detectChanges();
    expect(text()).toContain('दिवाळी सेल');
    expect(c.offerLine(offer({ discountPercent: 10 }))).toBe('10% सूट');
  });

  it('shows an ad at its place with the Ad mark', () => {
    const { text } = show({ ad: { placement: 'cart', title: 'Fresh deals', imageUrl: null } });
    expect(text()).toContain('Your items');
    expect(text()).toContain('Fresh deals');
    expect(text()).toContain('Ad');
  });

  it('shows a coupon pop-up that can be closed, with its ribbon', () => {
    const { f, c, text } = show({ coupon: coupon({ popup: true, ribbon: 'NEW', design: 'diwali', animation: 'confetti' }) });
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.popup')).not.toBeNull();
    expect(text()).toContain('NEW');
    expect(text()).toContain('Play confetti');
    (el.querySelector('.popup .x') as HTMLButtonElement).click();
    f.detectChanges();
    expect(el.querySelector('.popup')).toBeNull();
    expect(c.closed()).toBe(true);
  });
});
