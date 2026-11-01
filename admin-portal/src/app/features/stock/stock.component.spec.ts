import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { StockComponent } from './stock.component';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../core/models/domain.model';

const row = (over: Record<string, unknown> = {}) => ({
  productId: 'p1', productName: 'Milk', variantId: 'v1', variantLabel: '1 l', carried: true, available: 10, reorderThreshold: 2, priceOverride: null, basePrice: 40, mrp: null, ...over,
});

type Api = Partial<Record<'get' | 'put', (...a: any[]) => Observable<any>>>;

async function open(api: Api, role: Role = Role.StoreStaff) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiService, useValue: api },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
    ],
  });
  const fixture: ComponentFixture<StockComponent> = TestBed.createComponent(StockComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('StockComponent', () => {
  it('store staff see their own store stock straight away, without choosing a store', async () => {
    const get = vi.fn(() => of({ success: true, data: [row()] }));
    const f = await open({ get });

    expect(get).toHaveBeenCalledWith('/stock', { params: { storeId: null } });
    expect(text(f)).toContain('Milk');
    expect(f.componentInstance.isAdmin()).toBe(false);
  });

  it('an administrator loads the stores first and sees no stock until one is chosen', async () => {
    const get = vi.fn((url: string) => of({ success: true, data: url === '/stores' ? [{ id: 's1', name: 'Kharghar', isActive: true }, { id: 's2', name: 'Closed', isActive: false }] : [row()] }));
    const f = await open({ get }, Role.Admin);

    expect(get).toHaveBeenCalledTimes(1);
    expect(f.componentInstance.stores().map(s => s.name)).toEqual(['Kharghar']); // inactive stores are left out
    expect(f.componentInstance.rows()).toEqual([]);

    f.componentInstance.storeId = 's1';
    f.componentInstance.load();
    expect(get).toHaveBeenLastCalledWith('/stock', { params: { storeId: 's1' } });
    expect(f.componentInstance.rows()).toHaveLength(1);
  });

  it('search and "only what we sell" narrow the list', async () => {
    const f = await open({ get: () => of({ success: true, data: [row(), row({ variantId: 'v2', productName: 'Bread', carried: false })] }) });

    expect(f.componentInstance.shown()).toHaveLength(2);
    f.componentInstance.query = 'brea';
    expect(f.componentInstance.shown().map(r => r.productName)).toEqual(['Bread']);
    f.componentInstance.query = '';
    f.componentInstance.onlyCarried = true;
    expect(f.componentInstance.shown().map(r => r.productName)).toEqual(['Milk']);
  });

  it('shows the server message when stock cannot be loaded', async () => {
    const f = await open({ get: () => throwError(() => ({ message: 'No store assigned' })) });
    expect(text(f)).toContain('No store assigned');
  });

  it('discountPercent compares the store price with the shared price, or the MRP when that is higher', async () => {
    const f = await open({ get: () => of({ success: true, data: [] }) });
    const c = f.componentInstance;

    expect(c.discountPercent(row({ priceOverride: 30, basePrice: 40 }) as never)).toBe(25);
    expect(c.discountPercent(row({ priceOverride: 30, basePrice: 40, mrp: 60 }) as never)).toBe(50);
    expect(c.discountPercent(row({ priceOverride: null }) as never)).toBe(0);
  });

  it('save sends the numbers, and a store price only while the item is sold here', async () => {
    const put = vi.fn((_url: string, body: any) => of({ success: true, data: row({ available: body.available, priceOverride: body.priceOverride }) }));
    const f = await open({ get: () => of({ success: true, data: [row({ priceOverride: 35 })] }), put });

    f.componentInstance.save(f.componentInstance.rows()[0]);
    expect(put).toHaveBeenLastCalledWith('/stock/v1', { carried: true, available: 10, reorderThreshold: 2, priceOverride: 35 });
    expect(f.componentInstance.notice()).toBe('Milk 1 l: saved.');

    const notSold = row({ carried: false, priceOverride: 35 }) as never;
    f.componentInstance.save(notSold);
    expect(put).toHaveBeenLastCalledWith('/stock/v1', { carried: false, available: 10, reorderThreshold: 2, priceOverride: null });
  });

  it('an administrator save names the store in the url', async () => {
    const put = vi.fn(() => of({ success: true, data: row() }));
    const get = vi.fn((url: string) => of({ success: true, data: url === '/stores' ? [] : [row()] }));
    const f = await open({ get, put }, Role.ApplicationAdmin);

    f.componentInstance.storeId = 's1';
    f.componentInstance.save(row() as never);
    expect(put).toHaveBeenCalledWith('/stock/v1?storeId=s1', expect.anything());
  });

  it('a failed save shows the message and frees the Save buttons', async () => {
    const f = await open({ get: () => of({ success: true, data: [row()] }), put: () => throwError(() => ({ message: 'Price too high' })) });

    f.componentInstance.save(f.componentInstance.rows()[0]);
    f.detectChanges();

    expect(text(f)).toContain('Price too high');
    expect(f.componentInstance.busyId()).toBeNull();
  });
});
