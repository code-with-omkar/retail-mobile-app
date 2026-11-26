import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CellDirective, DataTableComponent } from './data-table/data-table.component';
import { FileUploadComponent, fileProblem, IMAGE_TYPES } from './file-upload/file-upload.component';
import { PaginationComponent, pageCount, pageNumbers } from './pagination/pagination.component';
import { SidePanelComponent } from './side-panel/side-panel.component';
import { MENU, menuFor } from '../../layout/shell/menu';
import { ICONS } from './icon/icon.component';
import { Role, StaffCategory, UserContext } from '../../core/models/domain.model';

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('pagination', () => {
  it('counts pages and offers the first, last and neighbouring numbers', () => {
    expect([pageCount(0, 20), pageCount(20, 20), pageCount(21, 20)]).toEqual([1, 1, 2]);
    expect(pageNumbers(1, 3)).toEqual([1, 2, 3]);
    expect(pageNumbers(10, 20)).toEqual([1, 0, 8, 9, 10, 11, 12, 0, 20]);
  });

  it('says which rows are shown, moves within range only, and goes to page 1 when the size changes', () => {
    const f = TestBed.createComponent(PaginationComponent);
    f.componentRef.setInput('total', 45);
    f.componentRef.setInput('page', 3);
    f.detectChanges();
    const pages: number[] = [];
    f.componentInstance.pageChange.subscribe(p => pages.push(p));

    expect(text(f)).toContain('Showing 41–45 of 45');
    f.componentInstance.go(4);
    f.componentInstance.go(2);
    f.componentInstance.resize(50);

    expect(pages).toEqual([2, 1]);
  });
});

@Component({
  standalone: true,
  imports: [DataTableComponent, CellDirective],
  template: `<app-data-table [columns]="columns" [rows]="rows" [startIndex]="20" activeId="b"><ng-template appCell="state" let-row><b>{{ row.state }}!</b></ng-template></app-data-table>`,
})
class Host {
  columns = [{ key: 'name', label: 'Name' }, { key: 'state', label: 'State' }];
  rows = [{ id: 'a', name: 'First', state: 'on' }, { id: 'b', name: 'Second', state: 'off' }];
}

describe('data table', () => {
  it('numbers rows across pages, uses a cell template and marks the active row', () => {
    const f = TestBed.createComponent(Host);
    f.detectChanges();
    f.detectChanges();
    const rows = (f.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows[0].querySelector('.index')?.textContent).toBe('21');
    expect(rows[1].classList.contains('active')).toBe(true);
    expect(rows[0].classList.contains('active')).toBe(false);
    expect(rows[1].textContent).toContain('off!');
    expect(rows[0].textContent).toContain('First');
  });
});

describe('file upload card', () => {
  it('refuses the wrong type, a file over the limit and an empty file', () => {
    expect(fileProblem({ type: 'application/pdf', size: 10 }, IMAGE_TYPES, 100)).toContain('JPG, PNG, WEBP');
    expect(fileProblem({ type: 'image/png', size: 3 * 1024 * 1024 }, IMAGE_TYPES, 2 * 1024 * 1024)).toContain('2 MB');
    expect(fileProblem({ type: 'image/png', size: 0 }, IMAGE_TYPES, 100)).toContain('empty');
    expect(fileProblem({ type: 'image/png', size: 50 }, IMAGE_TYPES, 100)).toBeNull();
  });

  it('hands on a good file only', () => {
    const f = TestBed.createComponent(FileUploadComponent);
    f.detectChanges();
    const got: File[] = [];
    f.componentInstance.fileSelected.subscribe(file => got.push(file));

    f.componentInstance.take(new File(['x'], 'a.txt', { type: 'text/plain' }));
    f.componentInstance.take(new File(['x'], 'a.png', { type: 'image/png' }));

    expect(got.map(file => file.name)).toEqual(['a.png']);
  });
});

describe('side panel', () => {
  it('is drawn only while open and asks to close on Escape', () => {
    const f = TestBed.createComponent(SidePanelComponent);
    f.componentRef.setInput('title', 'New thing');
    f.detectChanges();
    expect(text(f)).not.toContain('New thing');
    let closed = 0;
    f.componentInstance.closed.subscribe(() => closed++);
    f.componentInstance.onEscape();

    f.componentRef.setInput('open', true);
    f.detectChanges();
    f.componentInstance.onEscape();

    expect(text(f)).toContain('New thing');
    expect(closed).toBe(1);
  });
});

const who = (role: Role, over: Partial<UserContext> = {}) => ({ userId: 'u', displayName: 'U', organizationId: 'o', role, roles: [role], permissions: ['orders:read'], ...over }) as UserContext;
const labels = (context: UserContext) => menuFor(context).map(item => item.label);

describe('the menu', () => {
  it('shows a platform administrator every screen except the staff approval requests', () => {
    expect(labels(who(Role.ApplicationAdmin, { permissions: [] }))).toEqual(MENU.filter(item => item.route !== '/approvals').map(item => item.label));
  });

  it('shows store staff their screens only, with approval requests named by what they do', () => {
    const manager = labels(who(Role.StoreStaff, { staffCategory: StaffCategory.StoreManager }));
    expect(manager).toContain('Stores');
    expect(manager).toContain('Approval requests');
    for (const hidden of ['Overview', 'Users', 'Roles', 'Categories', 'Ads', 'Coupons', 'Commission']) expect(manager).not.toContain(hidden);
    expect(labels(who(Role.StoreStaff, { staffCategory: StaffCategory.StoreEmployee }))).toContain('My approval requests');
    expect(labels(who(Role.StoreStaff, { permissions: [] }))).not.toContain('Orders');
  });

  it('shows nothing to other accounts, and every entry has an icon', () => {
    expect(labels(who(Role.Customer))).toEqual([]);
    for (const item of MENU) expect(ICONS[item.icon]).toBeDefined();
  });
});
