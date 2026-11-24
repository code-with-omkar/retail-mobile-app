import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { AD_PLACEMENTS, AdsComponent, placementLabel } from './ads.component';
import { ApiService } from '../../core/services/api.service';
import { ConfigService } from '../../core/services/config.service';

const ad = (over: Record<string, unknown> = {}) => ({
  id: 'a1', placement: 'home', title: 'Diwali sale', imageUrl: '/media/offers/a.png', targetType: 'Url', target: 'https://example.com/sale', storeId: null,
  startsAt: '2026-11-01T00:00:00Z', endsAt: null, isActive: true, sortOrder: 0, views: 200, taps: 14, status: 'Live', ...over,
});

type Call = (...a: any[]) => Observable<any>;

function open(opts: { list?: unknown; post?: Call; put?: Call; del?: Call } = {}) {
  const api = {
    get: vi.fn(((url: string) => {
      if (url === '/admin/ads') return opts.list instanceof Error ? throwError(() => ({ message: opts.list instanceof Error ? opts.list.message : '' })) : of({ success: true, data: opts.list ?? [ad()] });
      if (url === '/stores') return of({ success: true, data: [{ id: 's1', name: 'Harbor Point', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] });
      if (url === '/categories') return of({ success: true, data: [{ id: 'c1', name: 'Dairy' }] });
      if (url === '/admin/campaigns') return of({ success: true, data: [{ id: 'o1', titleEn: 'Diwali offer', status: 'Sent' }, { id: 'o2', titleEn: 'Old', status: 'Cancelled' }] });
      return of({ success: true, data: [{ id: 'p1', name: 'Basmati Rice' }] });
    }) as Call),
    post: vi.fn(opts.post ?? (() => of({ success: true, data: ad() }))),
    put: vi.fn(opts.put ?? (() => of({ success: true, data: ad() }))),
    delete: vi.fn(opts.del ?? (() => of({ success: true, data: true }))),
  };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }, { provide: ConfigService, useValue: { getBaseUrl: () => 'http://host/api' } }] });
  const fixture = TestBed.createComponent(AdsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('AdsComponent', () => {
  it('has the seven places the API accepts, each with a label and a size hint', () => {
    expect(AD_PLACEMENTS.map(p => p.key)).toEqual(['home', 'home_middle', 'categories', 'search', 'cart', 'order', 'product']);
    for (const p of AD_PLACEMENTS) { expect(p.label.length).toBeGreaterThan(3); expect(p.hint).toContain('1080'); }
    expect(placementLabel('cart')).toBe('Cart');
    expect(placementLabel('x')).toBe('x');
  });

  it('lists the ads with where they show, what they open, the store, views, taps and the share of views that were tapped', () => {
    const { f } = open();
    expect(text(f)).toContain('Home: top banner');
    expect(text(f)).toContain('https://example.com/sale');
    expect(text(f)).toContain('All stores');
    expect(text(f)).toContain('200');
    expect(text(f)).toContain('14');
    expect(text(f)).toContain('7% of views');
    expect(text(f)).toContain('Live');
  });

  it('says so when there are none, and shows the message when loading fails', () => {
    expect(text(open({ list: [] }).f)).toContain('No ads yet.');
    TestBed.resetTestingModule();
    expect(open({ list: new Error('Offline') }).c.error()).toBe('Offline');
  });

  it('an ad needs a picture: uploading keeps its path and the form can then be saved', () => {
    const { c, api } = open({ post: (url: string) => (url === '/admin/campaigns/image' ? of({ success: true, data: { imageUrl: '/media/offers/new.png' } }) : of({ success: true, data: ad() })) });
    expect(c.imageUrl()).toBeNull();
    c.save();
    expect(api.post).not.toHaveBeenCalled();

    c.pickImage({ target: { files: [new File(['x'], 'a.png')] } } as unknown as Event);
    c.uploadImage();
    expect(api.post.mock.calls[0][0]).toBe('/admin/campaigns/image');
    expect(c.imageUrl()).toBe('/media/offers/new.png');
  });

  it('creating sends the place, picture, title, target, store, dates and order, then clears the form and reads the list again', () => {
    const { c, api } = open();
    c.imageUrl.set('/media/offers/new.png');
    c.placement = 'cart';
    c.title = ' Free delivery ';
    c.targetType = 'Category';
    c.target = 'c1';
    c.storeId = 's1';
    c.sortOrder = 2;
    c.startsAt = '2026-11-20T10:00';

    c.save();

    const [url, body] = api.post.mock.calls[0];
    expect(url).toBe('/admin/ads');
    expect(body).toMatchObject({ placement: 'cart', imageUrl: '/media/offers/new.png', title: 'Free delivery', targetType: 'Category', target: 'c1', storeId: 's1', sortOrder: 2, isActive: true, endsAt: null });
    expect(body.startsAt).toMatch(/^2026-11-20T/);
    expect(c.notice()).toBe('Ad created.');
    expect(c.imageUrl()).toBeNull();
    expect(c.placement).toBe('home');
    expect(api.get.mock.calls.filter(call => call[0] === '/admin/ads').length).toBe(2);
  });

  it('a target of Nothing sends no target, and changing the kind clears the old target', () => {
    const { c, api } = open();
    c.imageUrl.set('/media/offers/new.png');
    c.targetType = 'Url';
    c.target = 'https://example.com';
    c.targetType = 'None';
    c.save();
    expect(api.post.mock.calls[0][1]).toMatchObject({ targetType: 'None', target: null });
  });

  it('a product is found by name and chosen; stores that are closed and cancelled offers are not offered', () => {
    const { c } = open();
    c.productSearch = 'rice';
    c.searchProducts();
    expect(c.productResults().map(p => p.name)).toEqual(['Basmati Rice']);
    c.pickProduct(c.productResults()[0]);
    expect(c.target).toBe('p1');
    expect(c.pickedProductName()).toBe('Basmati Rice');
    expect(c.stores().map(s => s.name)).toEqual(['Harbor Point']);
    expect(c.offers().map(o => o.name)).toEqual(['Diwali offer']);
  });

  it('editing fills the form with the ad, saving sends a change to its own address', () => {
    const { c, api } = open();
    c.edit(ad({ placement: 'product', title: 'Rice', targetType: 'None', target: null, storeId: 's1', sortOrder: 4 }) as never);
    expect(c.placement).toBe('product');
    expect(c.storeId).toBe('s1');
    expect(c.imageUrl()).toBe('/media/offers/a.png');

    c.title = 'Rice deal';
    c.save();
    expect(api.put.mock.calls[0][0]).toBe('/admin/ads/a1');
    expect(api.put.mock.calls[0][1]).toMatchObject({ placement: 'product', title: 'Rice deal', sortOrder: 4, storeId: 's1' });
    expect(c.notice()).toBe('Ad saved.');
  });

  it('switching off sends the ad as it is with only the flag changed, and deleting asks for the ad by id', () => {
    const { c, api } = open();
    c.toggle(ad() as never);
    expect(api.put.mock.calls[0][0]).toBe('/admin/ads/a1');
    expect(api.put.mock.calls[0][1]).toMatchObject({ isActive: false, placement: 'home', targetType: 'Url', target: 'https://example.com/sale' });
    expect(c.notice()).toBe('Ad switched off.');

    c.remove(ad() as never);
    expect(api.delete).toHaveBeenCalledWith('/admin/ads/a1');
    expect(c.notice()).toBe('Ad deleted.');
  });

  it('shows the server message when saving fails and keeps the form', () => {
    const { c } = open({ post: () => throwError(() => ({ message: 'The web link must start with https://.' })) });
    c.imageUrl.set('/media/offers/new.png');
    c.targetType = 'Url';
    c.target = 'http://x';
    c.save();
    expect(c.error()).toBe('The web link must start with https://.');
    expect(c.busy()).toBe(false);
    expect(c.imageUrl()).toBe('/media/offers/new.png');
  });
});
