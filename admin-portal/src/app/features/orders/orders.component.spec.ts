import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { OrdersComponent } from './orders.component';
import { ApiService } from '../../core/services/api.service';

const order = (over: Record<string, unknown> = {}) => ({
  id: 'o1', orderNumber: 'ORD-1', customer: 'Asha', store: 'Kharghar', storeId: 's1', createdAt: '2026-11-01T10:00:00Z', totalAmount: 120, status: 0, ...over,
});

type Call = (...a: any[]) => Observable<any>;

/** Answers by url so a screen that makes several calls gets the right one each time. */
function apiWith(routes: { get?: Record<string, unknown>; post?: Record<string, unknown> }) {
  const reply = (table: Record<string, unknown> = {}, url: string) => {
    const hit = table[url];
    if (hit instanceof Error) return throwError(() => ({ message: hit.message }));
    // tests give the list as an array; the server sends it as a page
    const data = url === '/orders/paged' && Array.isArray(hit) ? { items: hit, totalCount: hit.length, page: 1, pageSize: 20, stores: [{ id: 's1', name: 'Kharghar' }, { id: 's2', name: 'Panvel' }] } : hit;
    return of({ success: true, data: data ?? null });
  };
  return {
    get: vi.fn(((url: string) => reply(routes.get, url.split('?')[0])) as Call),
    post: vi.fn(((url: string) => reply(routes.post, url)) as Call),
  };
}

async function open(api: ReturnType<typeof apiWith>) {
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture: ComponentFixture<OrdersComponent> = TestBed.createComponent(OrdersComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('OrdersComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('lists the orders and turns the status number into its name', async () => {
    const f = await open(apiWith({ get: { '/orders/paged': [order(), order({ id: 'o2', orderNumber: 'ORD-2', status: 2 })] } }));

    expect(f.componentInstance.orders().map(o => o.status)).toEqual(['Pending', 'Preparing']);
    expect(text(f)).toContain('ORD-1');
    expect(text(f)).toContain('NEW'); // only the pending one
    expect(text(f)).toContain('Packing'); // Preparing is shown as Packing
    expect(Array.from((f.nativeElement as HTMLElement).querySelectorAll('tbody td.index')).map(td => td.textContent)).toEqual(['1', '2']);
  });

  it('says so when there are no orders, and shows the message when loading fails', async () => {
    expect(text(await open(apiWith({ get: { '/orders/paged': [] } })))).toContain('No orders found.');
    TestBed.resetTestingModule();
    expect(text(await open(apiWith({ get: { '/orders/paged': new Error('Boom') } })))).toContain('Boom');
  });

  it('shows the customer note and what to do when an item is out of stock', async () => {
    const f = await open(apiWith({ get: { '/orders/paged': [order({ note: 'Ring twice', outOfStockChoice: 'Replace' })] } }));

    expect(text(f)).toContain('Note: Ring twice');
    expect(text(f)).toContain('replace with a similar item');
    expect(f.componentInstance.choiceLabel('Remove')).toBe('remove it');
    expect(f.componentInstance.choiceLabel('Call')).toBe('call the customer');
  });

  it('offers the next step for each status and no Complete once a partner has the order', async () => {
    const f = await open(apiWith({ get: { '/orders/paged': [] } }));
    const c = f.componentInstance;

    expect(c.stepsFor(order({ status: 'Pending' }) as never).map(s => s.label)).toEqual(['Accept', 'Reject']);
    expect(c.stepsFor(order({ status: 'Accepted' }) as never).map(s => s.label)).toEqual(['Start packing']);
    expect(c.stepsFor(order({ status: 'Ready' }) as never).map(s => s.label)).toEqual(['Complete']);
    expect(c.stepsFor(order({ status: 'Ready', deliveryPartnerId: 'd1' }) as never)).toEqual([]);
    expect(c.stepsFor(order({ status: 'Delivered' }) as never)).toEqual([]);
  });

  it('a step posts the status code, says it is done and reads the list again', async () => {
    const api = apiWith({ get: { '/orders/paged': [order()] }, post: { '/operations/orders/o1/status': {} } });
    const f = await open(api);

    f.componentInstance.change(f.componentInstance.orders()[0], { label: 'Accept', code: 1 });

    expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/status', { status: 1 });
    expect(f.componentInstance.notice()).toBe('ORD-1: Accept done.');
    expect(api.get.mock.calls.filter(c => c[0] === '/orders/paged')).toHaveLength(2);
  });

  it('Reject asks for a reason first, and sends it with the remark; Other needs a remark', async () => {
    const api = apiWith({ get: { '/orders/paged': [order()] }, post: { '/operations/orders/o1/status': {} } });
    const f = await open(api);
    const c = f.componentInstance;

    c.change(c.orders()[0], { label: 'Reject', code: 5 });
    expect(api.post).not.toHaveBeenCalled();
    expect(c.rejectReason()).toBe('OutOfStock');

    c.rejectReason.set('Other');
    c.reject(c.rejecting()!);
    expect(api.post).not.toHaveBeenCalled();

    c.rejectNote.set(' Power cut ');
    c.reject(c.rejecting()!);
    expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/status', { status: 5, reason: 'Other', note: 'Power cut' });
    expect(c.rejecting()).toBeNull();
    expect(c.notice()).toBe('ORD-1: rejected.');
  });

  it('a failed step shows the server message', async () => {
    const api = apiWith({ get: { '/orders/paged': [order()] }, post: { '/operations/orders/o1/status': new Error('Already accepted') } });
    const f = await open(api);

    f.componentInstance.change(f.componentInstance.orders()[0], { label: 'Accept', code: 1 });

    expect(f.componentInstance.error()).toBe('Already accepted');
    expect(f.componentInstance.busyId()).toBeNull();
  });

  it('for a Ready order it loads that store\'s delivery partners, and Give needs one chosen', async () => {
    const api = apiWith({ get: { '/orders/paged': [order({ status: 3 })], '/delivery/partners': [{ id: 'd1', name: 'Rina', activeDeliveries: 1 }] }, post: { '/operations/orders/o1/assign': {} } });
    const f = await open(api);
    const c = f.componentInstance;

    expect(api.get).toHaveBeenCalledWith('/delivery/partners', { params: { storeId: 's1' } });
    expect(c.partnersOf(c.orders()[0]).map(p => p.name)).toEqual(['Rina']);

    c.assign(c.orders()[0]); // nobody picked yet
    expect(api.post).not.toHaveBeenCalled();

    c.pick('o1', 'd1');
    c.assign(c.orders()[0]);
    expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/assign', { partnerId: 'd1' });
    expect(c.notice()).toBe('ORD-1: given to the delivery partner.');
  });

  describe('changing the items of an order being packed', () => {
    const items = [
      { productId: 'p1', variantId: 'v1', productNameSnapshot: 'Milk', variantLabel: '1 l', unitPrice: 34, quantity: 3 },
      { productId: 'p2', variantId: null, productNameSnapshot: 'Bread', unitPrice: 40, quantity: 1 },
    ];
    const opened = async (extraPost: Record<string, unknown> = {}) => {
      const api = apiWith({
        get: { '/orders/paged': [order({ status: 2 })], '/orders/o1': { items }, '/operations/orders/o1/replacements': [] },
        post: { '/operations/orders/o1/change': { newTotal: 86, change: { refundAmount: 68 } }, ...extraPost },
      });
      const f = await open(api);
      f.componentInstance.openChange(f.componentInstance.orders()[0]);
      return { api, f, c: f.componentInstance };
    };

    it('opening loads the lines, and opening again closes the panel', async () => {
      const { c } = await opened();
      expect(c.editing()).toBe('o1');
      expect(c.lines()).toHaveLength(2);
      expect(c.changed()).toBe(false); // nothing reduced yet

      c.openChange(c.orders()[0]);
      expect(c.editing()).toBeNull();
    });

    it('Apply sends only the lines that were reduced, with the reason, and reports the refund', async () => {
      const { api, c } = await opened();

      c.setQty(items[0], 1); // Milk 3 to 1; Bread untouched
      expect(c.changed()).toBe(true);
      c.reason.set('  Out of stock ');
      c.applyChange(c.orders()[0]);

      expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/change', { changes: [{ productId: 'p1', variantId: 'v1', quantity: 1 }], reason: 'Out of stock' });
      expect(c.notice()).toBe('ORD-1: changed. New total ₹86; ₹68 is being refunded to the customer.');
      expect(c.editing()).toBeNull();
    });

    it('removing an item entirely is sent as quantity 0, and an empty reason is sent as null', async () => {
      const { api, c } = await opened();

      c.setQty(items[1], 0);
      c.applyChange(c.orders()[0]);

      expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/change', { changes: [{ productId: 'p2', variantId: null, quantity: 0 }], reason: null });
    });

    it('Apply does nothing until something is reduced', async () => {
      const { api, c } = await opened();
      c.applyChange(c.orders()[0]);
      expect(api.post).not.toHaveBeenCalled();
    });

    it('offering a replacement posts both products and the quantity, and shows it as waiting', async () => {
      const proposal = { id: 'r1', newName: 'Oat Milk', newLabel: '1 l', newQuantity: 3, newUnitPrice: 38, expiresAt: '2026-11-01T10:05:00Z' };
      const { api, c } = await opened({ '/operations/orders/o1/replacements': proposal });

      c.startReplace(items[0]);
      expect(c.repQty()).toBe(3);
      c.repProductId.set('p9');
      c.repVariantId.set('v9');
      c.offerReplacement(c.orders()[0], items[0]);

      expect(api.post).toHaveBeenCalledWith('/operations/orders/o1/replacements', { productId: 'p1', variantId: 'v1', replacementProductId: 'p9', replacementVariantId: 'v9', quantity: 3 });
      expect(c.waiting()).toEqual(proposal);
      expect(c.replacing()).toBeNull();
      expect(c.notice()).toContain('The customer has 5 minutes.');
    });

    it('a replacement cannot be offered before a size is chosen', async () => {
      const { api, c } = await opened();
      c.startReplace(items[0]);
      c.offerReplacement(c.orders()[0], items[0]);
      expect(api.post).not.toHaveBeenCalled();
    });
  });

  it('asks the server for one page, and starts at page 1 again when the search or a filter changes', async () => {
    const api = apiWith({ get: { '/orders/paged': [order()] } });
    const f = await open(api);
    const c = f.componentInstance;
    const asked = () => api.get.mock.calls.filter(call => call[0] === '/orders/paged').at(-1)![1];

    expect(asked()).toEqual({ params: { page: 1, pageSize: 20 } });
    c.goTo(3);
    expect(asked()).toEqual({ params: { page: 3, pageSize: 20 } });
    c.search.set('  asha '); c.status.set('0'); c.storeFilter.set('s2'); c.filter();
    expect(asked()).toEqual({ params: { page: 1, pageSize: 20, search: 'asha', status: '0', storeId: 's2' } });
  });

  it('the order acted on stays in its row with its new status even when the filter would drop it, and goes on a new search', async () => {
    const first = [order({ id: 'o1', orderNumber: 'ORD-1' }), order({ id: 'o2', orderNumber: 'ORD-2' })];
    const routes: { get: Record<string, unknown>; post: Record<string, unknown> } = { get: { '/orders/paged': first }, post: { '/operations/orders/o1/status': {} } };
    const f = await open(apiWith(routes));
    const c = f.componentInstance;

    routes.get['/orders/paged'] = [first[1]]; // filtered to new orders: the accepted one is no longer sent
    c.change(c.orders()[0], { label: 'Accept', code: 1 });
    f.detectChanges();

    expect(c.orders().map(o => [o.id, o.status])).toEqual([['o1', 'Accepted'], ['o2', 'Pending']]);
    expect((f.nativeElement as HTMLElement).querySelector('tbody tr.active')?.textContent).toContain('ORD-1');

    c.filter();
    expect(c.orders().map(o => o.id)).toEqual(['o2']);
    expect(c.changedId()).toBeNull();
  });

  it('a scheduled order shows its window in India time, and an order for now shows none', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-02T04:30:00Z')); // 10:00 in India
    const f = await open(apiWith({ get: { '/orders/paged': [
      order({ id: 'o1', orderNumber: 'ORD-1', deliverySlotStart: '2026-11-02T08:30:00Z', deliverySlotEnd: '2026-11-02T09:30:00Z' }),
      order({ id: 'o2', orderNumber: 'ORD-2', deliverySlotStart: '2026-11-03T03:30:00Z', deliverySlotEnd: '2026-11-03T04:30:00Z' }),
      order({ id: 'o3', orderNumber: 'ORD-3' }),
    ] } }));
    const [a, b, c] = f.componentInstance.orders();

    expect(f.componentInstance.windowLabel(a)).toBe('Today, 2 pm - 3 pm');
    expect(f.componentInstance.windowLabel(b)).toBe('Tomorrow, 9 am - 10 am');
    expect(f.componentInstance.windowLabel(c)).toBe('');
    expect(text(f)).toContain('Deliver Today, 2 pm - 3 pm');
    expect((f.nativeElement as HTMLElement).querySelectorAll('.slot')).toHaveLength(2);
  });

  it('the list is read again every 15 seconds while the page is in front, and stops when it is closed', async () => {
    vi.useFakeTimers();
    const api = apiWith({ get: { '/orders/paged': [] } });
    const f = await open(api);
    const reads = () => api.get.mock.calls.filter(c => c[0] === '/orders/paged').length;

    expect(reads()).toBe(1);
    vi.advanceTimersByTime(15000);
    expect(reads()).toBe(2);

    f.destroy();
    vi.advanceTimersByTime(60000);
    expect(reads()).toBe(2);
  });
});
