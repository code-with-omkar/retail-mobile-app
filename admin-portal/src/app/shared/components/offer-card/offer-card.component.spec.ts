import { TestBed } from '@angular/core/testing';
import { OfferCardComponent } from './offer-card.component';
import { OFFER_CARD_DESIGNS, OFFER_CARD_KEYS, chipText, designOf } from './offer-card-designs';

function open(inputs: Partial<Pick<OfferCardComponent, 'title' | 'imageUrl' | 'endsAt'>> & { designKey?: string | null } = {}) {
  const fixture = TestBed.createComponent(OfferCardComponent);
  Object.assign(fixture.componentInstance, inputs);
  fixture.detectChanges();
  return fixture;
}

const text = (f: { nativeElement: unknown }) => ((f.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');

describe('offer card designs', () => {
  it('has the fourteen designs the API accepts, in the same order: eight festivals, then six everyday ones', () => {
    expect(OFFER_CARD_KEYS).toEqual(['diwali', 'ganesh', 'navratri', 'holi', 'sankranti', 'padwa', 'eid', 'christmas', 'fresh', 'weekend', 'bigsaver', 'combo', 'freeship', 'newin']);
    expect(OFFER_CARD_DESIGNS.filter(d => d.festive).length).toBe(8);
  });

  it('every design has an English and a Marathi name, a button text and colours', () => {
    for (const d of OFFER_CARD_DESIGNS) {
      expect(d.name.length).toBeGreaterThan(1);
      expect(/[ऀ-ॿ]/.test(d.mr)).toBe(true);
      expect(d.cta.length).toBeGreaterThan(2);
      for (const colour of [d.bg, d.ink, d.muted, d.ctaBg, d.ctaInk, d.m1, d.m2, d.m3]) expect(colour).toMatch(/^#[0-9A-F]{6}$/i);
    }
  });

  it('text and button colours pass WCAG AA (4.5:1) on their background', () => {
    const lum = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(c => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    const ratio = (a: string, b: string) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
    for (const d of OFFER_CARD_DESIGNS) {
      expect(ratio(d.ink, d.bg), d.key + ' name').toBeGreaterThanOrEqual(4.5);
      expect(ratio(d.muted, d.bg), d.key + ' line').toBeGreaterThanOrEqual(4.5);
      expect(ratio(d.ctaInk, d.ctaBg), d.key + ' button').toBeGreaterThanOrEqual(4.5);
    }
  });

  it('finds a design by key, and nothing for an unknown or empty key', () => {
    expect(designOf('holi')?.name).toBe('Holi');
    expect(designOf('onam')).toBeNull();
    expect(designOf('')).toBeNull();
    expect(designOf(null)).toBeNull();
  });

  it('words the countdown chip by days left', () => {
    const now = new Date('2026-11-05T10:00:00Z');
    expect(chipText(null, now)).toBe('FESTIVE OFFER');
    expect(chipText('2026-11-08T09:00:00Z', now)).toBe('FESTIVE OFFER · ENDS IN 3 DAYS');
    expect(chipText('2026-11-05T20:00:00Z', now)).toBe('FESTIVE OFFER · ENDS IN 1 DAY');
    expect(chipText('2026-11-05T09:00:00Z', now)).toBe('FESTIVE OFFER · ENDS TODAY');
    expect(chipText('not a date', now)).toBe('FESTIVE OFFER');
  });
});

describe('OfferCardComponent', () => {
  it('draws the festival, its Marathi name, the offer line and the button', () => {
    const f = open({ designKey: 'diwali', title: 'Up to 20% off sweets' });

    expect(text(f)).toContain('Diwali');
    expect(text(f)).toContain('दिवाळी');
    expect(text(f)).toContain('Up to 20% off sweets');
    expect(text(f)).toContain('Shop Diwali');
    expect(text(f)).toContain('FESTIVE OFFER');
    expect((f.nativeElement as HTMLElement).querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('an everyday design says SPECIAL OFFER, with its own name and button', () => {
    const f = open({ designKey: 'bigsaver', title: 'Up to 20% off staples' });
    expect(text(f)).toContain('SPECIAL OFFER');
    expect(text(f)).not.toContain('FESTIVE OFFER');
    expect(text(f)).toContain('Big saver');
    expect(text(f)).toContain('मोठी बचत');
    expect(text(f)).toContain('Save now');
    expect(chipText(null, new Date(), false)).toBe('SPECIAL OFFER');
    expect(chipText('2026-11-05T09:00:00Z', new Date('2026-11-05T10:00:00Z'), false)).toBe('SPECIAL OFFER · ENDS TODAY');
  });

  it('every one of the fourteen draws its own motif', () => {
    const shapes = new Set<string>();
    for (const key of OFFER_CARD_KEYS) {
      TestBed.resetTestingModule();
      const f = open({ designKey: key });
      const svg = (f.nativeElement as HTMLElement).querySelector('svg');
      expect(svg, key).not.toBeNull();
      expect(svg!.children.length, key).toBeGreaterThan(0);
      shapes.add(svg!.innerHTML);
    }
    expect(shapes.size).toBe(14);
  });

  it('shows a placeholder line until the offer has a title', () => {
    expect(text(open({ designKey: 'holi' }))).toContain('Your offer line appears here');
  });

  it('an uploaded picture replaces the artwork, name and line, leaving only the chip and the button', () => {
    const f = open({ designKey: 'ganesh', title: 'Modak special', imageUrl: 'http://host/media/offers/a.jpg', endsAt: '2099-01-01T00:00:00Z' });
    const el = f.nativeElement as HTMLElement;

    expect(el.querySelector('img.art')?.getAttribute('src')).toBe('http://host/media/offers/a.jpg');
    expect(el.querySelector('svg')).toBeNull();
    expect(text(f)).not.toContain('Ganeshotsav');
    expect(text(f)).not.toContain('Modak special');
    expect(text(f)).toContain('Shop puja');
    expect(text(f)).toContain('ENDS IN');
  });

  it('draws nothing for an unknown design', () => {
    expect((open({ designKey: 'onam' }).nativeElement as HTMLElement).querySelector('.card')).toBeNull();
  });
});
