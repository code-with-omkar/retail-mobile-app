import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { AdminCategory, CategoriesComponent, groupCategories } from './categories.component';
import { ApiService } from '../../core/services/api.service';

const cat = (id: string, name: string, parent: string | null = null, over: Partial<AdminCategory> = {}): AdminCategory => ({ id, name, parentCategoryId: parent, isActive: true, productCount: 2, ...over });
const tree = [cat('d', 'Dairy'), cat('m', 'Milk', 'd'), cat('c', 'Curd', 'd'), cat('s', 'Snacks')];
const products = [
  { id: 'p1', name: 'Paneer', categoryId: 'd', unitOfMeasure: '200 g' },
  { id: 'p2', name: 'Chips', categoryId: 's' },
  { id: 'p3', name: 'Full cream milk', categoryId: 'm' },
];

type Call = (...a: any[]) => Observable<any>;

function open(opts: { post?: Call; put?: Call; fail?: boolean; items?: typeof products; total?: number } = {}) {
  const get = vi.fn((url: string, o?: any) => {
    if (opts.fail && url === '/admin/catalog/categories') return throwError(() => ({ message: 'down' }));
    if (url === '/stores') return of({ success: true, data: [{ id: 's2', name: 'Panvel', isActive: true }, { id: 's1', name: 'Belapur', isActive: true }] });
    if (url === '/catalog/products') {
      const all = opts.items ?? products, size = opts.total ? 2 : all.length;
      return of({ success: true, data: { items: all.slice((o.params.page - 1) * size, o.params.page * size), totalCount: all.length } });
    }
    if (url.startsWith('/admin/catalog/products/')) return of({ success: true, data: { name: 'Paneer', description: 'Fresh', unitOfMeasure: '200 g', isActive: true } });
    return of({ success: true, data: tree });
  });
  const api = {
    get,
    post: vi.fn(opts.post ?? (() => of({ success: true, data: cat('n', 'New') }))),
    put: vi.fn(opts.put ?? (() => of({ success: true, data: {} }))),
  };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture = TestBed.createComponent(CategoriesComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

describe('CategoriesComponent', () => {
  it('groups sub-categories under their main category, A to Z, and treats an orphan as a main category', () => {
    const groups = groupCategories([...tree, cat('x', 'Lost', 'gone')]);
    expect(groups.map(g => g.category.name)).toEqual(['Dairy', 'Lost', 'Snacks']);
    expect(groups[0].subs.map(s => s.name)).toEqual(['Curd', 'Milk']);
  });

  it('starts on the first category and the first store, with the store products split into in and not in', () => {
    const { c, f } = open();

    expect([c.selectedId(), c.storeId()]).toEqual(['d', 's1']);
    expect(c.choices().map(x => x.label)).toEqual(['Dairy', 'Dairy › Curd', 'Dairy › Milk', 'Snacks']);
    expect(c.inside().map(p => p.id)).toEqual(['p1']);
    expect(c.outside().map(p => p.id)).toEqual(['p2', 'p3']);
    expect((f.nativeElement as HTMLElement).querySelectorAll('.box')).toHaveLength(2);

    c.choose('s');
    expect(c.inside().map(p => p.id)).toEqual(['p2']);
    c.outSearch.set('milk');
    expect(c.outside().map(p => p.id)).toEqual(['p3']);
  });

  it('reads every page of a large store', () => {
    const { c, api } = open({ total: 3 });
    expect(api.get.mock.calls.filter(x => x[0] === '/catalog/products')).toHaveLength(2);
    expect(c.products()).toHaveLength(3);
  });

  it('adds the ticked products to the category, keeping their other details, and marks them', () => {
    const { c, api } = open();
    c.tickAll('out');
    expect(c.ticked(c.outside())).toHaveLength(2);

    c.addTicked();

    expect(api.put).toHaveBeenCalledTimes(2);
    expect(api.put).toHaveBeenNthCalledWith(1, '/admin/catalog/products/p2', { name: 'Paneer', description: 'Fresh', categoryId: 'd', unitOfMeasure: '200 g', isActive: true });
    expect(c.inside().map(p => p.id)).toEqual(['p1', 'p2', 'p3']);
    expect([...c.changed()]).toEqual(['p2', 'p3']);
    expect(c.picked().size).toBe(0);
    expect(c.notice()).toBe('2 products are now in Dairy.');
  });

  it('removing moves a product to the category chosen for that, which is the parent for a sub-category', () => {
    const { c, api } = open();
    expect(c.removeTo()).toBe('c'); // the first other category
    c.choose('m');
    expect(c.removeTo()).toBe('d'); // its parent

    c.move(c.inside(), c.removeTo());

    expect(api.put).toHaveBeenCalledWith('/admin/catalog/products/p3', expect.objectContaining({ categoryId: 'd' }));
    expect(c.inside()).toEqual([]);
    expect(c.notice()).toBe('Full cream milk is now in Dairy.');
  });

  it('a product can be dropped only on the box it is not in, and a ticked set moves together', () => {
    const { c, api } = open();
    const event = { preventDefault: () => {} } as DragEvent;
    c.dragging.set(c.inside()[0]);
    expect([c.accepts('in'), c.accepts('out')]).toEqual([false, true]);
    c.dragging.set(c.outside()[0]);
    expect([c.accepts('in'), c.accepts('out')]).toEqual([true, false]);

    c.tick('p2'); c.tick('p3');
    c.drop(event, 'in');

    expect(api.put).toHaveBeenCalledTimes(2);
  });

  it('reports a product the server refuses and still moves the others', () => {
    const put = (url: string) => (url.endsWith('p2') ? throwError(() => ({ message: 'Inactive category' })) : of({ success: true }));
    const { c } = open({ put });
    c.tickAll('out');

    c.addTicked();

    expect(c.inside().map(p => p.id)).toEqual(['p1', 'p3']);
    expect(c.error()).toBe('Not moved. Chips: Inactive category');
    expect(c.busy()).toBe(false);
  });

  it('only a main category is offered as the parent, never the one being edited, and none for one with sub-categories', () => {
    const { c } = open();
    expect(c.parentChoices().map(p => p.name)).toEqual(['Dairy', 'Snacks']);
    c.edit(tree[3]);
    expect(c.parentChoices().map(p => p.name)).toEqual(['Dairy']);
    c.edit(tree[0]);
    expect(c.parentChoices()).toEqual([]);
  });

  it('adds a sub-category, and edits or hides one', () => {
    const { c, api } = open();
    c.startNew('d');
    c.name = '  Cheese ';
    c.save();
    expect(api.post).toHaveBeenCalledWith('/admin/catalog/categories', { name: 'Cheese', parentCategoryId: 'd', isActive: true });
    expect(c.notice()).toBe('Category added.');
    expect(c.panelOpen()).toBe(false);

    c.edit(tree[3]);
    c.isActive = false;
    c.save();
    expect(api.put).toHaveBeenCalledWith('/admin/catalog/categories/s', { name: 'Snacks', parentCategoryId: null, isActive: false });
  });

  it('shows the server message when saving fails, and does not save a blank name', () => {
    const { c, api } = open({ post: () => throwError(() => ({ message: 'A category with this name already exists here.' })) });
    c.name = '   ';
    c.save();
    expect(api.post).not.toHaveBeenCalled();
    c.name = 'Milk';
    c.save();
    expect(c.error()).toBe('A category with this name already exists here.');
    expect(c.busy()).toBe(false);
  });

  it('says so when the categories cannot be loaded', () => {
    const { c } = open({ fail: true });
    expect(c.error()).toBe('down');
    expect(c.loading()).toBe(false);
  });
});
