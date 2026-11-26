import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CampaignsComponent } from './campaigns.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role } from '../../core/models/domain.model';

const hour = 3_600_000;
const campaign = (over: Record<string, unknown> = {}) => ({
  id: 'c1', titleEn: 'Diwali', bodyEn: 'Sale', notify: true, showOnLoad: false, imageUrl: null, discountPercent: 0, endsAt: null, startsAt: new Date(Date.now() - hour).toISOString(),
  status: 'Scheduled', allStores: true, stores: [], products: [], recipientCount: 0, feePaise: 0, ...over,
});
const fees = { defaultPaise: 5000, loadPaise: 2000, stores: [{ storeId: 's1', name: 'Kharghar', overridePaise: null }, { storeId: 's2', name: 'Panvel', overridePaise: 3000 }] };

type Routes = Record<string, unknown>;
type Call = (...a: any[]) => Observable<any>;

/** Answers by url; an Error value makes that call fail with its message. */
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
      { provide: ConfigService, useValue: { getBaseUrl: () => 'http://host/api' } },
    ],
  });
  const fixture = TestBed.createComponent(CampaignsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance };
}

const baseGet = (over: Routes = {}): Routes => ({
  '/admin/campaigns': [campaign()],
  '/admin/offer-fees/mine': { feePaise: 0, paymentsAvailable: true, loadPaise: 0 },
  '/admin/offer-fees': fees,
  '/stores': [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Old', isActive: false }],
  ...over,
});
const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('CampaignsComponent', () => {
  it('lists the offers; an administrator also gets the fee settings and the active stores', () => {
    const { f, c } = open(Role.Admin, apiWith({ get: baseGet() }));

    expect(text(f)).toContain('Diwali');
    expect(c.stores().map(s => s.name)).toEqual(['Kharghar']);
    expect(c.defaultRupees).toBe('50');
    expect(c.loadRupees).toBe('20');
    expect(c.storeRupees()).toEqual({ s1: '', s2: '30' });
  });

  it('store fees stay folded, and can be searched and filtered when there are many', () => {
    const { c } = open(Role.Admin, apiWith({ get: baseGet() }));

    expect(c.showStoreFees()).toBe(false);
    expect(c.ownFeeCount()).toBe(1);
    expect(c.shownFeeStores().map(s => s.name)).toEqual(['Kharghar', 'Panvel']);
    c.feeFilter.set('own');
    expect(c.shownFeeStores().map(s => s.name)).toEqual(['Panvel']);
    c.feeFilter.set('all');
    c.feeSearch.set('khar');
    expect(c.shownFeeStores().map(s => s.name)).toEqual(['Kharghar']);
  });

  it('store staff do not load fees or stores', () => {
    const api = apiWith({ get: baseGet() });
    const { c } = open(Role.StoreStaff, api);

    expect(api.get).not.toHaveBeenCalledWith('/admin/offer-fees');
    expect(api.get).not.toHaveBeenCalledWith('/stores');
    expect(c.isAdmin()).toBe(false);
  });

  it('says so when there are no offers and shows the message when loading fails', () => {
    expect(text(open(Role.Admin, apiWith({ get: baseGet({ '/admin/campaigns': [] }) })).f)).toContain('No offers yet.');
    TestBed.resetTestingModule();
    expect(open(Role.Admin, apiWith({ get: baseGet({ '/admin/campaigns': new Error('Offline') }) })).c.error()).toBe('Offline');
  });

  it('formats rupees from paise and picture paths', () => {
    const { c } = open(Role.Admin, apiWith({ get: baseGet() }));
    expect(c.rupees(5000)).toBe('₹50');
    expect(c.rupees(5050)).toBe('₹50.50');
    expect(c.img('/media/a.png')).toBe('http://host/media/a.png');
    expect(c.img(null)).toBe('');
  });

  it('who can cancel: an administrator any offer, store staff only an offer for their own store alone', () => {
    const one = campaign({ stores: [{ id: 's1', name: 'Kharghar' }], allStores: false }) as never;
    const many = campaign({ stores: [{ id: 's1' }, { id: 's2' }] }) as never;

    const staff = open(Role.StoreStaff, apiWith({ get: baseGet() })).c;
    expect(staff.canCancel(one)).toBe(true);
    expect(staff.canCancel(many)).toBe(false);
    TestBed.resetTestingModule();
    expect(open(Role.Admin, apiWith({ get: baseGet() })).c.canCancel(many)).toBe(true);
  });

  it('the pop-up fee is added to the price only for store staff', () => {
    const get = baseGet({ '/admin/offer-fees/mine': { feePaise: 5000, paymentsAvailable: true, loadPaise: 2000 } });
    const staff = open(Role.StoreStaff, apiWith({ get })).c;

    expect(staff.totalFee()).toBe(5000);
    staff.showOnLoad = true;
    expect(staff.totalFee()).toBe(7000);

    TestBed.resetTestingModule();
    const admin = open(Role.Admin, apiWith({ get })).c;
    admin.showOnLoad = true;
    expect(admin.totalFee()).toBe(5000);
  });

  it('a discount is "running" only between its start and end and not when cancelled', () => {
    const { c } = open(Role.Admin, apiWith({ get: baseGet() }));
    const live = { discountPercent: 10, endsAt: new Date(Date.now() + hour).toISOString() };

    expect(c.isRunning(campaign(live) as never)).toBe(true);
    expect(c.isRunning(campaign({ ...live, status: 'Cancelled' }) as never)).toBe(false);
    expect(c.isRunning(campaign({ ...live, endsAt: new Date(Date.now() - 1000).toISOString() }) as never)).toBe(false);
    expect(c.isRunning(campaign({ ...live, startsAt: new Date(Date.now() + hour).toISOString() }) as never)).toBe(false);
    expect(c.isRunning(campaign() as never)).toBe(false);
  });

  it('fees: save sends paise, "Use default" sends null, and bad numbers are refused', () => {
    const api = apiWith({ get: baseGet(), put: { '/admin/offer-fees/default': fees, '/admin/offer-fees/stores/s2': fees } });
    const { c } = open(Role.Admin, api);

    c.defaultRupees = '75.5';
    c.saveDefault();
    expect(api.put).toHaveBeenCalledWith('/admin/offer-fees/default', { amountPaise: 7550 });
    expect(c.notice()).toBe('Fee saved.');

    c.clearStore('s2');
    expect(api.put).toHaveBeenLastCalledWith('/admin/offer-fees/stores/s2', { amountPaise: null });

    api.put.mockClear();
    c.defaultRupees = '-5';
    c.saveDefault();
    expect(api.put).not.toHaveBeenCalled();
    expect(c.error()).toBe('Give the fee as a number of rupees, 0 or more.');
  });

  it('only an administrator sees the card design choice, with Plain, the eight festivals and the six everyday designs', () => {
    const admin = open(Role.Admin, apiWith({ get: baseGet() })).f.nativeElement as HTMLElement;
    const names = Array.from(admin.querySelectorAll('.design-tile')).map(b => b.textContent?.trim());
    expect(names).toEqual(['Plain', 'Diwali', 'Ganeshotsav', 'Navratri', 'Holi', 'Sankranti', 'Gudi Padwa', 'Eid', 'Christmas', 'Fresh picks', 'Weekend sale', 'Big saver', 'Combo deal', 'Free delivery', 'New in store']);

    TestBed.resetTestingModule();
    expect((open(Role.StoreStaff, apiWith({ get: baseGet() })).f.nativeElement as HTMLElement).querySelector('.design-tile')).toBeNull();
  });

  it('choosing a design shows its card preview with the offer title, and Plain takes it away', () => {
    const { f, c } = open(Role.Admin, apiWith({ get: baseGet() }));
    const el = f.nativeElement as HTMLElement;
    c.titleEn = 'Sweets sale';

    c.cardDesign = 'diwali';
    f.detectChanges();
    expect(el.querySelector('app-offer-card')).not.toBeNull();
    expect(el.querySelector('app-offer-card')?.textContent).toContain('Sweets sale');
    expect(el.querySelector('.design-tile.on')?.textContent).toContain('Diwali');

    c.cardDesign = '';
    f.detectChanges();
    expect(el.querySelector('app-offer-card')).toBeNull();
  });

  it('an administrator sends the chosen design with the offer, and the form goes back to Plain afterwards', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns': campaign() } });
    const { c } = open(Role.Admin, api);

    c.titleEn = 'Diwali sale';
    c.cardDesign = 'diwali';
    c.create();

    expect(api.post).toHaveBeenCalledWith('/admin/campaigns', expect.objectContaining({ cardDesign: 'diwali' }));
    expect(c.cardDesign).toBe('');
  });

  it('Plain sends no design, and store staff never send one even if the field were set', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns': campaign() } });
    const admin = open(Role.Admin, api).c;
    admin.titleEn = 'Plain';
    admin.create();
    expect(api.post.mock.calls[0][1].cardDesign).toBeNull();

    TestBed.resetTestingModule();
    const staffApi = apiWith({ get: baseGet(), post: { '/admin/campaigns': campaign() } });
    const staff = open(Role.StoreStaff, staffApi).c;
    staff.titleEn = 'Staff offer';
    staff.cardDesign = 'diwali';
    staff.create();
    expect(staffApi.post.mock.calls[0][1].cardDesign).toBeNull();
  });

  it('an uploaded card picture shows on the preview card in place of the artwork', () => {
    const { f, c } = open(Role.Admin, apiWith({ get: baseGet() }));
    c.titleEn = 'Holi';
    c.cardDesign = 'holi';
    c.imageUrl.set('/media/offers/h.jpg');
    f.detectChanges();

    expect((f.nativeElement as HTMLElement).querySelector('app-offer-card img.art')?.getAttribute('src')).toBe('http://host/media/offers/h.jpg');
  });

  it('the offers list names the card design of an offer', () => {
    const { f } = open(Role.Admin, apiWith({ get: baseGet({ '/admin/campaigns': [campaign({ cardDesign: 'padwa' })] }) }));
    expect((f.nativeElement as HTMLElement).textContent).toContain('Card design: Gudi Padwa');
  });

  it('creating sends trimmed text; the store picks only for an administrator; the form is cleared and the list read again', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns': campaign() } });
    const { c } = open(Role.Admin, api);

    c.titleEn = '  Diwali  ';
    c.bodyEn = ' ';
    c.toggle('s1');
    c.create();

    expect(api.post).toHaveBeenCalledWith('/admin/campaigns', expect.objectContaining({ titleEn: 'Diwali', bodyEn: null, notify: true, discountPercent: 0, productIds: [], storeIds: ['s1'], startsAt: null, endsAt: null }));
    expect(c.notice()).toBe('Offer created. It is sent within a minute.');
    expect(c.titleEn).toBe('');
    expect(c.picked().size).toBe(0);
    expect(api.get.mock.calls.filter(x => x[0] === '/admin/campaigns')).toHaveLength(2);
  });

  it('a discount offer carries its chosen products and end time; store staff never send stores', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns': campaign() } });
    const { c } = open(Role.StoreStaff, api);

    c.titleEn = 'Half price';
    c.discountPercent = 50;
    c.endsAt = '2030-01-01T10:00';
    c.addProduct({ id: 'p1', name: 'Milk' } as never);
    c.toggle('s1');
    c.create();

    const body = api.post.mock.calls[0][1];
    expect(body.discountPercent).toBe(50);
    expect(body.productIds).toEqual(['p1']);
    expect(body.endsAt).toBe(new Date('2030-01-01T10:00').toISOString());
    expect(body.storeIds).toEqual([]);
  });

  it('products can be searched, added and removed', () => {
    const api = apiWith({ get: baseGet({ '/admin/campaigns/products?search=mil': [{ id: 'p1', name: 'Milk', price: 40 }] }) });
    const { c } = open(Role.Admin, api);

    c.productSearch = ' mil ';
    c.searchProducts();
    expect(c.productResults().map(p => p.name)).toEqual(['Milk']);

    c.addProduct(c.productResults()[0]);
    expect(c.pickedProductList()).toEqual([{ id: 'p1', name: 'Milk' }]);
    c.removeProduct('p1');
    expect(c.pickedProductList()).toEqual([]);
  });

  it('a failed create shows the message and frees the button', () => {
    const { c } = open(Role.Admin, apiWith({ get: baseGet(), post: { '/admin/campaigns': new Error('Title needed') } }));
    c.create();
    expect(c.error()).toBe('Title needed');
    expect(c.busy()).toBe(false);
  });

  it('cancel and end-discount post to their own urls, say so and read the list again', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns/c1/cancel': {}, '/admin/campaigns/c1/end-discount': {} } });
    const { c } = open(Role.Admin, api);
    const offer = campaign() as never;

    c.cancel(offer);
    expect(api.post).toHaveBeenLastCalledWith('/admin/campaigns/c1/cancel');
    expect(c.notice()).toBe('Offer cancelled.');

    c.endDiscount(offer);
    expect(api.post).toHaveBeenLastCalledWith('/admin/campaigns/c1/end-discount');
    expect(c.notice()).toBe('Discount ended.');
  });

  it('an administrator changes the card of an existing offer; the picture can be kept or dropped', () => {
    const api = apiWith({ get: baseGet(), put: { '/admin/campaigns/c1/card': {} } });
    const { c } = open(Role.Admin, api);
    const offer = { ...(campaign() as object), imageUrl: '/media/o.jpg', cardDesign: null } as never;

    c.startEdit(offer);
    expect(c.editDesign).toBe('');
    c.editDesign = 'holi';
    c.saveCard();
    expect(api.put).toHaveBeenLastCalledWith('/admin/campaigns/c1/card', { cardDesign: 'holi', imageUrl: '/media/o.jpg', ribbon: null, animation: null });
    expect(c.notice()).toBe('Card changed.');
    expect(c.editing()).toBeNull();

    c.startEdit(offer);
    c.editDesign = '';
    c.editKeepPicture = false;
    c.saveCard();
    expect(api.put).toHaveBeenLastCalledWith('/admin/campaigns/c1/card', { cardDesign: null, imageUrl: null, ribbon: null, animation: null });
  });

  it('a failed card change shows the error and keeps the editor open', () => {
    const api = apiWith({ get: baseGet(), put: { '/admin/campaigns/c1/card': new Error('Only an administrator can change a card design') } });
    const { c } = open(Role.Admin, api);
    c.startEdit(campaign() as never);
    c.saveCard();
    expect(c.error()).toBe('Only an administrator can change a card design');
    expect(c.editing()).not.toBeNull();
  });

  it('a picture is uploaded once chosen, and its path is kept for the offer', () => {
    const api = apiWith({ get: baseGet(), post: { '/admin/campaigns/image': { imageUrl: '/media/o.jpg' } } });
    const { c } = open(Role.Admin, api);

    c.uploadImage();
    expect(api.post).not.toHaveBeenCalled();

    c.pickImage({ target: { files: [new File(['x'], 'o.jpg')] } } as unknown as Event);
    c.uploadImage();
    expect(c.imageUrl()).toBe('/media/o.jpg');
    expect(c.uploadFile).toBeNull();
  });

  it('a failed payment start shows the message', () => {
    const { c } = open(Role.StoreStaff, apiWith({ get: baseGet(), post: { '/admin/campaigns/c1/payment': new Error('Payments off') } }));
    c.pay(campaign({ status: 'AwaitingPayment' }) as never);
    expect(c.error()).toBe('Payments off');
    expect(c.busy()).toBe(false);
  });
});
