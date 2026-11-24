import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { AdminCategory, CategoriesComponent, groupCategories } from './categories.component';
import { ApiService } from '../../core/services/api.service';

const cat = (id: string, name: string, parent: string | null = null, over: Partial<AdminCategory> = {}): AdminCategory => ({ id, name, parentCategoryId: parent, isActive: true, productCount: 2, ...over });
const tree = [cat('d', 'Dairy'), cat('m', 'Milk', 'd'), cat('c', 'Curd', 'd'), cat('s', 'Snacks')];

type Call = (...a: any[]) => Observable<any>;

function open(opts: { list?: AdminCategory[]; post?: Call; put?: Call; fail?: boolean } = {}) {
  const api = {
    get: vi.fn((() => (opts.fail ? throwError(() => ({ message: 'down' })) : of({ success: true, data: opts.list ?? tree }))) as Call),
    post: vi.fn(opts.post ?? (() => of({ success: true, data: cat('n', 'New') }))),
    put: vi.fn(opts.put ?? (() => of({ success: true, data: cat('n', 'New') }))),
  };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture = TestBed.createComponent(CategoriesComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, api };
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('CategoriesComponent', () => {
  it('groups sub-categories under their main category, A to Z, and treats an orphan as a main category', () => {
    const groups = groupCategories([...tree, cat('x', 'Lost', 'gone')]);
    expect(groups.map(g => g.category.name)).toEqual(['Dairy', 'Lost', 'Snacks']);
    expect(groups[0].subs.map(s => s.name)).toEqual(['Curd', 'Milk']);
  });

  it('shows the tree with product counts and sub-category counts', () => {
    const { f } = open();
    expect(text(f)).toContain('Dairy');
    expect(text(f)).toContain('2 sub-categories');
    expect(text(f)).toContain('↳ Milk');
  });

  it('only a main category is offered as the parent, never the one being edited', () => {
    const { c } = open();
    expect(c.parentChoices().map(p => p.name)).toEqual(['Dairy', 'Snacks']);
    c.edit(tree[3]);
    expect(c.parentChoices().map(p => p.name)).toEqual(['Dairy']);
  });

  it('a main category that has sub-categories cannot be put under another', () => {
    const { c } = open();
    c.edit(tree[0]);
    expect(c.parentChoices()).toEqual([]);
  });

  it('adds a sub-category with its parent and reloads', () => {
    const { c, api } = open();
    c.name = '  Cheese ';
    c.parentId = 'd';
    c.save();
    expect(api.post).toHaveBeenCalledWith('/admin/catalog/categories', { name: 'Cheese', parentCategoryId: 'd', isActive: true });
    expect(c.notice()).toBe('Category added.');
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(c.name).toBe('');
  });

  it('edits a category: moves it under another or back to the top, and can switch it off', () => {
    const { c, api } = open();
    c.edit(tree[3]);
    c.parentId = 'd';
    c.isActive = false;
    c.save();
    expect(api.put).toHaveBeenCalledWith('/admin/catalog/categories/s', { name: 'Snacks', parentCategoryId: 'd', isActive: false });
    expect(c.notice()).toBe('Category saved.');
  });

  it('shows the server message when saving fails, and does not save a blank name', () => {
    const { c, api } = open({ post: () => throwError(() => ({ message: 'A category with this name already exists here.' })) });
    c.name = '   ';
    c.save();
    expect(api.post).not.toHaveBeenCalled();
    c.name = 'Milk';
    c.parentId = 'd';
    c.save();
    expect(c.error()).toBe('A category with this name already exists here.');
    expect(c.busy()).toBe(false);
  });

  it('says so when the list cannot be loaded', () => {
    const { c } = open({ fail: true });
    expect(c.error()).toBe('down');
    expect(c.loading()).toBe(false);
  });
});
