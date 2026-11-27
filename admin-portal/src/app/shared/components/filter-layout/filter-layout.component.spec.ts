import { TestBed } from '@angular/core/testing';
import { FilterLayoutComponent } from './filter-layout.component';
import { FilterGroup, Selection, facets, filterState } from './filter-state';

const groups: FilterGroup[] = [
  { key: 'status', label: 'Status', options: [{ value: 'new', label: 'New', count: 3 }, { value: 'done', label: 'Done', count: 1 }] },
  { key: 'sort', label: 'Sort by', single: true, required: true, options: [{ value: 'name', label: 'Name' }] },
];

describe('filter layout', () => {
  beforeEach(() => localStorage.clear());

  it('ticks several values, shows their counts, and All unticks the group', () => {
    const f = TestBed.createComponent(FilterLayoutComponent);
    const asked: Selection[] = [];
    f.componentInstance.selectedChange.subscribe(next => { asked.push(next); f.componentRef.setInput('selected', next); });
    f.componentRef.setInput('groups', groups);
    f.detectChanges();
    // hidden until asked for
    expect(f.componentInstance.hidden()).toBe(true);
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('New');
    expect(el.textContent).toContain('3');
    // a required choice has no All
    expect(el.querySelectorAll('input[name="f-sort"]').length).toBe(1);

    const [all, first, second] = Array.from(el.querySelectorAll<HTMLInputElement>('input[name="f-status"]'));
    first.click(); f.detectChanges();
    second.click(); f.detectChanges();
    expect(asked.at(-1)!['status']).toEqual(['new', 'done']);
    expect(f.componentInstance.chosen()).toBe(1);
    all.click(); f.detectChanges();
    expect(asked.at(-1)!['status']).toEqual([]);
  });

  it('starts hidden, and opening it on one screen leaves the others closed', () => {
    const f = TestBed.createComponent(FilterLayoutComponent);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).classList).toContain('collapsed');
    f.componentInstance.toggleHidden();
    expect(f.componentInstance.hidden()).toBe(false);
    expect(TestBed.createComponent(FilterLayoutComponent).componentInstance.hidden()).toBe(true);
  });

  it('minimises a group to a line that says what is chosen', () => {
    const f = TestBed.createComponent(FilterLayoutComponent);
    f.componentRef.setInput('groups', groups);
    f.componentRef.setInput('selected', { status: ['new'] });
    f.detectChanges();
    f.componentInstance.minimise(groups[0]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelectorAll('input[name="f-status"]').length).toBe(0);
    expect(el.querySelector('.fold small')?.textContent).toBe('New');
    f.componentInstance.minimiseAll();
    expect(f.componentInstance.allMinimised()).toBe(true);
    f.componentInstance.minimiseAll();
    expect(f.componentInstance.isMinimised(groups[0])).toBe(false);
  });

  it('remembers the choice per screen, clears back to the defaults and ignores junk', () => {
    const first = filterState('x', { sort: ['name'] });
    first.set({ sort: ['email'], status: ['new'] });
    expect(filterState('x', { sort: ['name'] }).of('sort')).toEqual(['email']);
    first.clear();
    expect(filterState('x').selected()).toEqual({ sort: ['name'] });
    localStorage.setItem('qc-filters:y', '{"a":5}');
    expect(filterState('y').selected()).toEqual({});
  });

  it('counts each value under the other groups', () => {
    const rows = [{ a: 'x', b: '1' }, { a: 'y', b: '1' }, { a: 'x', b: '2' }];
    const found = facets(rows, { a: ['x'], b: ['1'] }, { a: r => r.a, b: r => r.b });
    expect(found.shown).toEqual([rows[0]]);
    expect([found.count('a', 'y'), found.count('b', '2')]).toEqual([1, 1]);
  });
});
