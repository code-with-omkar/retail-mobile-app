import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ProductsComponent } from './products.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { Role } from '../../core/models/domain.model';

const variant = (over: Record<string, unknown> = {}) => ({ id: 'v1', sku: 'MLK-1', label: '1 l', price: 40, mrp: 45, sortOrder: 0, isDefault: true, isActive: true, ...over });
const product = (over: Record<string, unknown> = {}) => ({
  id: 'p1', sku: 'MLK', name: 'Milk', description: 'Fresh', categoryId: 'c1', categoryName: 'Dairy', unitOfMeasure: 'l', imageUrl: null, isActive: true, price: 40, mrp: 45,
  variants: [variant()], translations: [{ locale: 'mr', name: 'दूध', description: null }], ...over,
});

type Call = (...a: any[]) => Observable<any>;
type Api = Partial<Record<'get' | 'post' | 'put' | 'delete', Call>>;

function open(role: Role, api: Api) {
  const full = { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), ...api };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: ApiService, useValue: full },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
      { provide: ConfigService, useValue: { getBaseUrl: () => 'http://host/api' } },
    ],
  });
  const fixture = TestBed.createComponent(ProductsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api: full };
}

const page = (items: unknown[], totalCount = items.length) => ({ success: true, data: { items, page: 1, pageSize: 20, totalCount } });
const admin = (extra: Api = {}): Api => ({
  get: vi.fn((url: string) => of(url === '/categories' ? { success: true, data: [{ id: 'c1', name: 'Dairy' }, { id: 'c2', name: 'Old', isActive: false }] } : page([product()]))),
  ...extra,
});
const ok = (data: unknown) => vi.fn(() => of({ success: true, data }));

beforeEach(() => localStorage.clear());

describe('ProductsComponent', () => {
  it('an administrator loads the catalogue page by page and the active categories', () => {
    const { c, api } = open(Role.Admin, admin());

    expect(api.get).toHaveBeenCalledWith('/admin/catalog/products', { params: { search: null, page: 1, pageSize: 20, categoryId: null, active: null } });
    expect(c.items().map(p => p.name)).toEqual(['Milk']);
    expect(c.categories().map(x => x.name)).toEqual(['Dairy']);

    c.more();
    expect(api.get).toHaveBeenLastCalledWith('/admin/catalog/products', { params: { search: null, page: 2, pageSize: 20, categoryId: null, active: null } });
    expect(c.items()).toHaveLength(2); // the next page is added to the list

    c.search = ' bread ';
    c.reload();
    expect(api.get).toHaveBeenLastCalledWith('/admin/catalog/products', { params: { search: 'bread', page: 1, pageSize: 20, categoryId: null, active: null } });
    expect(c.items()).toHaveLength(1);
  });

  it('other staff see a plain read-only list that is searched on the screen', () => {
    const { c, api } = open(Role.StoreStaff, {
      get: vi.fn((url: string) => of({ success: true, data: url === '/products' ? [{ id: 'p1', sku: 'MLK', name: 'Milk' }, { id: 'p2', sku: 'BRD', name: 'Bread' }] : [] })),
    });

    expect(api.get).toHaveBeenCalledWith('/products');
    expect(c.items()).toHaveLength(2);
    c.search = 'bre';
    c.reload();
    expect(c.items().map(p => p.name)).toEqual(['Bread']);
    expect(c.total()).toBe(1);
  });

  it('shows the server message when the catalogue cannot be loaded', () => {
    const { c } = open(Role.Admin, { get: vi.fn((url: string) => (url === '/categories' ? of({ success: true, data: [] }) : throwError(() => ({ message: 'Denied' })))) });
    expect(c.error()).toBe('Denied');
    expect(c.loading()).toBe(false);
  });

  it('picture paths from the API get the host in front; links stay as they are', () => {
    const { c } = open(Role.Admin, admin());
    expect(c.img('/media/a.jpg')).toBe('http://host/media/a.jpg');
    expect(c.img('https://cdn/x.jpg')).toBe('https://cdn/x.jpg');
    expect(c.img(null)).toBe('');
  });

  it('creating sends a missing price as 0, then opens the new product for editing', () => {
    const created = product({ id: 'p9', name: 'Oats' });
    const { c, api } = open(Role.Admin, admin({ post: ok(created) }));

    c.startNew();
    expect(c.creating()).toBe(true);
    c.draft = { sku: 'OAT', name: 'Oats', description: '', categoryId: 'c1', unitOfMeasure: 'kg', price: null, mrp: null };
    c.create();

    expect(api.post).toHaveBeenCalledWith('/admin/catalog/products', { sku: 'OAT', name: 'Oats', description: '', categoryId: 'c1', unitOfMeasure: 'kg', price: 0, mrp: null });
    expect(c.notice()).toBe('Product created.');
    expect(c.creating()).toBe(false);
    expect(c.editing()?.id).toBe('p9');
  });

  it('a picture chosen for a new product is sent as soon as the product exists', () => {
    const post = vi.fn((url: string) => of({ success: true, data: url.endsWith('/image') ? product({ id: 'p9', imageUrl: '/media/o.png' }) : product({ id: 'p9' }) }));
    const { c } = open(Role.Admin, admin({ post }));
    c.startNew();
    c.chooseDraftPicture(new File(['x'], 'o.png', { type: 'image/png' }));
    c.draft = { sku: 'OAT', name: 'Oats', description: '', categoryId: 'c1', unitOfMeasure: 'kg', price: 10, mrp: null };

    c.create();

    expect(post.mock.calls.map(call => call[0])).toEqual(['/admin/catalog/products', '/admin/catalog/products/p9/image']);
    expect(c.draftFile).toBeNull();
    expect(c.editing()?.imageUrl).toBe('/media/o.png');
  });

  it('Marathi text given for a new product is saved after it is created, and a description needs a name', () => {
    const put = vi.fn(() => of({ success: true, data: product({ id: 'p9' }) }));
    const { c, api } = open(Role.Admin, admin({ post: ok(product({ id: 'p9' })), put }));
    c.startNew();
    c.draft = { sku: 'OAT', name: 'Oats', description: '', categoryId: 'c1', unitOfMeasure: 'kg', price: 10, mrp: null };
    c.draftMr = { name: '', description: 'वर्णन' };
    c.create();
    expect(api.post).not.toHaveBeenCalled();

    c.draftMr = { name: 'ओट्स', description: '' };
    c.create();
    expect(put).toHaveBeenCalledWith('/admin/catalog/products/p9/translations/mr', { name: 'ओट्स', description: null });
  });

  it('editing fills the form, the Marathi text and the sizes from the product', () => {
    const { c } = open(Role.Admin, admin());
    c.edit(c.items()[0]);

    expect(c.form).toEqual({ name: 'Milk', description: 'Fresh', categoryId: 'c1', unitOfMeasure: 'l', isActive: true });
    expect(c.mr.name).toBe('दूध');
    expect(c.hasMarathi(c.items()[0])).toBe(true);
    expect(c.sizes).toHaveLength(1);
    expect(c.newSize.sortOrder).toBe(1); // goes after the existing size
  });

  it('saving details puts the form and replaces the product in the list', () => {
    const saved = product({ name: 'Milk 2' });
    const { c, api } = open(Role.Admin, admin({ put: ok(saved) }));
    c.edit(c.items()[0]);

    c.saveDetails();

    expect(api.put).toHaveBeenCalledWith('/admin/catalog/products/p1', { name: 'Milk', description: 'Fresh', categoryId: 'c1', unitOfMeasure: 'l', isActive: true });
    expect(c.items()[0].name).toBe('Milk 2');
    expect(c.notice()).toBe('Details saved.');
  });

  it('Marathi: saved with an empty description as null, and removed', () => {
    const { c, api } = open(Role.Admin, admin({ put: ok(product()), delete: ok(product({ translations: [] })) }));
    c.edit(c.items()[0]);

    c.mr = { name: 'दूध', description: '' };
    c.saveMarathi();
    expect(api.put).toHaveBeenCalledWith('/admin/catalog/products/p1/translations/mr', { name: 'दूध', description: null });

    c.removeMarathi();
    expect(api.delete).toHaveBeenCalledWith('/admin/catalog/products/p1/translations/mr');
    expect(c.hasMarathi(c.items()[0])).toBe(false);
  });

  it('sizes: save sends an empty MRP as null; add and make-default use their own urls', () => {
    const { c, api } = open(Role.Admin, admin({ put: ok(product()), post: ok(product()) }));
    c.edit(c.items()[0]);

    c.saveSize(variant({ mrp: '' }) as never);
    expect(api.put).toHaveBeenCalledWith('/admin/catalog/products/p1/variants/v1', { sku: 'MLK-1', label: '1 l', price: 40, mrp: null, sortOrder: 0, isActive: true });

    c.newSize = { sku: 'MLK-2', label: '2 l', price: null, mrp: null, sortOrder: 1 };
    c.addSize();
    expect(api.post).toHaveBeenLastCalledWith('/admin/catalog/products/p1/variants', { sku: 'MLK-2', label: '2 l', price: 0, mrp: null, sortOrder: 1, isActive: true });

    c.makeDefault(variant({ id: 'v2' }) as never);
    expect(api.post).toHaveBeenLastCalledWith('/admin/catalog/products/p1/variants/v2/default');
  });

  it('upload needs a chosen file; remove picture deletes it', () => {
    const { c, api } = open(Role.Admin, admin({ post: ok(product({ imageUrl: '/media/m.jpg' })), delete: ok(product()) }));
    c.edit(c.items()[0]);

    c.upload();
    expect(api.post).not.toHaveBeenCalled();

    c.picked({ target: { files: [new File(['x'], 'm.jpg')] } } as unknown as Event);
    c.upload();
    expect(api.post).toHaveBeenCalledWith('/admin/catalog/products/p1/image', expect.any(FormData));
    expect(c.file).toBeNull();

    c.removePicture();
    expect(api.delete).toHaveBeenCalledWith('/admin/catalog/products/p1/image');
  });

  it('a failed save shows the message, frees the buttons, and a second click while saving is ignored', () => {
    let sent = 0;
    const put = () => new Observable<never>(s => { sent++; s.error({ message: 'Name taken' }); });
    const { c } = open(Role.Admin, admin({ put }));
    c.edit(c.items()[0]);

    c.saveDetails();
    expect(c.error()).toBe('Name taken');
    expect(c.busy()).toBe(false);

    c.busy.set(true);
    c.saveDetails();
    expect(sent).toBe(1);
  });
});
