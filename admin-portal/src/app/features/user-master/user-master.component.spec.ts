import { AuthService } from '../../core/services/auth.service';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { UserMasterComponent } from './user-master.component';
import { UserMasterService } from '../../core/services/user-master.service';
import { Role, StaffCategory, UserStatus } from '../../core/models/domain.model';

const user = (over: Record<string, unknown> = {}) => ({
  id: 'u1', firstName: 'Asha', lastName: 'Patil', email: 'asha@shop.in', organizationId: 'o1', storeId: 's1', role: Role.StoreStaff, staffCategory: StaffCategory.StoreManager,
  status: UserStatus.Active, permissions: [{ id: 'p1', code: 'orders:read' }], ...over,
});
const users = [
  user(),
  user({ id: 'u2', firstName: 'Ravi', lastName: 'Kale', email: 'ravi@shop.in', role: Role.Customer, status: UserStatus.Inactive, organizationId: 'o2', staffCategory: null }),
  user({ id: 'u3', firstName: 'Bina', lastName: 'Shah', email: 'bina@shop.in', role: Role.Customer, organizationId: 'o1' }),
];

type Call = (...a: any[]) => Observable<any>;
type Svc = Partial<Record<'getUsersPage' | 'getOrganizations' | 'getStores' | 'createUser' | 'updateUser' | 'toggleUserStatus' | 'getRoles' | 'getPermissions', Call>>;
const ok = (data: unknown) => () => of({ success: true, data });
const page = (items: unknown[], over: Record<string, unknown> = {}) => ({ items, totalCount: items.length, page: 1, pageSize: 20, allCount: 3, activeCount: 2, roleCount: 2, ...over });

function open(over: Svc = {}) {
  const svc = {
    getUsersPage: vi.fn(ok(page(users))),
    getOrganizations: ok([{ id: 'o1', name: 'QuickCart' }, { id: 'o2', name: 'Other' }]),
    getStores: ok([{ id: 's1', name: 'Kharghar' }]),
    getRoles: ok([{ id: 'r1', code: Role.StoreStaff, name: 'Store Staff', isActive: true, permissions: [{ id: 'p1', code: 'orders:read' }] }]),
    getPermissions: ok([]),
    setTemporaryPassword: vi.fn(() => of({ success: true, data: { temporaryPassword: 'Temp-pass-1234' } })),
    createUser: vi.fn(ok(user())),
    updateUser: vi.fn(ok(user())),
    toggleUserStatus: vi.fn(ok(user())),
    permissions: signal([]),
    ...over,
  };
  TestBed.configureTestingModule({ providers: [{ provide: UserMasterService, useValue: svc }, { provide: AuthService, useValue: { getUserId: () => 'me' } }] });
  const fixture = TestBed.createComponent(UserMasterComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, svc };
}

const validForm = (c: UserMasterComponent) => c.form.patchValue({ firstName: 'Meena', lastName: 'Joshi', email: 'meena@shop.in', organizationId: 'o1', role: Role.Customer });

describe('UserMasterComponent', () => {
  beforeEach(() => localStorage.clear());
  it('loads a page of users with their full names and the totals, plus organisations and roles', () => {
    const { c } = open();

    expect(c.users().map(u => u.fullName)).toEqual(['Asha Patil', 'Ravi Kale', 'Bina Shah']);
    expect(c.organizations()).toHaveLength(2);
    expect(c.roleOptions()).toEqual([{ value: Role.StoreStaff, label: 'Store Staff' }]);
    expect(c.groups().map(g => g.key)).toEqual(['role', 'status', 'sort']);
    expect([c.total(), c.allCount(), c.activeCount(), c.roleCount()]).toEqual([3, 3, 2, 2]);
    expect(c.isLoading()).toBe(false);
  });

  it('shows the server message when users cannot be loaded, or the reply says no', () => {
    const failed = open({ getUsersPage: () => throwError(() => ({ message: 'Denied' })) }).c;
    expect(failed.apiError()).toBe('Denied');
    expect(failed.isLoading()).toBe(false);
    TestBed.resetTestingModule();
    expect(open({ getUsersPage: () => of({ success: false, message: 'Not yours' }) }).c.apiError()).toBe('Not yours');
  });

  it('asks the server for the filtered page, starting again at page 1 when a filter changes', () => {
    const { c, svc } = open();
    const asked = () => (svc.getUsersPage as ReturnType<typeof vi.fn>).mock.calls.at(-1)![0];

    expect(asked()).toEqual({ search: '', roles: [], statuses: [], sort: 'name', page: 1, pageSize: 20 });
    c.goTo(3);
    expect(asked().page).toBe(3);
    c.searchTerm.set('  ravi ');
    c.onFilters({ ...c.filters.selected(), status: [UserStatus.Inactive] });
    c.onFilters({ ...c.filters.selected(), role: [Role.Customer] });
    expect(asked()).toEqual({ search: 'ravi', roles: [Role.Customer], statuses: [UserStatus.Inactive], sort: 'name', page: 1, pageSize: 20 });
  });

  it('numbers the rows and marks the user being edited', () => {
    const { f, c } = open();
    c.openEditForm(c.users()[1]);
    f.detectChanges();
    const rows = (f.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows).toHaveLength(3);
    expect(rows[1].classList.contains('active')).toBe(true);
    expect(rows[0].querySelector('.index')?.textContent).toBe('1');
  });

  it('keeps an administrator role selectable so saving does not change it', () => {
    const { c } = open();
    c.openEditForm(user({ role: Role.Admin, staffCategory: null }) as never);

    expect(c.roleOptions().at(-1)).toEqual({ value: Role.Admin, label: 'Administrator' });
  });

  it('creating: the form starts empty as a Customer, and an invalid form is not sent', () => {
    const { c, svc } = open();

    c.openCreateForm();
    expect(c.mode()).toBe('create');
    expect(c.isFormVisible()).toBe(true);
    expect(c.form.controls.role.value).toBe(Role.Customer);

    c.saveUser();
    expect(svc.createUser).not.toHaveBeenCalled();
    expect(c.getFieldError('email')).toBe('This field is required.');
  });

  it('field errors name the problem: required, email, too short', () => {
    const { c } = open();
    c.openCreateForm();
    c.form.patchValue({ email: 'nope', firstName: 'A' });
    c.form.markAllAsTouched();

    expect(c.getFieldError('email')).toBe('Enter a valid email address.');
    expect(c.getFieldError('firstName')).toBe('Value is too short.');
    expect(c.getFieldError('role')).toBeUndefined();
  });

  it('a valid new user is sent trimmed, the form closes and the list is read again', () => {
    const getUsersPage = vi.fn(ok(page(users)));
    const { c, svc } = open({ getUsersPage });
    c.openCreateForm();
    validForm(c);
    c.form.patchValue({ firstName: '  Meena ' });

    c.saveUser();

    expect(svc.createUser).toHaveBeenCalledWith(expect.objectContaining({ firstName: 'Meena', email: 'meena@shop.in', storeId: undefined, staffCategory: undefined, role: Role.Customer }));
    expect(c.isFormVisible()).toBe(false);
    expect(getUsersPage).toHaveBeenCalledTimes(2);
    expect(c.isLoading()).toBe(false);
  });

  it('a staff category is sent only for store staff, and is required for them', () => {
    const { c, svc } = open();
    c.openCreateForm();
    validForm(c);
    c.form.patchValue({ role: Role.StoreStaff });

    c.saveUser(); // staff category missing
    expect(svc.createUser).not.toHaveBeenCalled();

    c.form.patchValue({ staffCategory: StaffCategory.StoreEmployee });
    c.saveUser();
    expect(svc.createUser).toHaveBeenCalledWith(expect.objectContaining({ staffCategory: StaffCategory.StoreEmployee }));
  });

  it('a refused save keeps the form open and shows the server message', () => {
    const { c } = open({ createUser: () => of({ success: false, message: 'Email taken' }) });
    c.openCreateForm();
    validForm(c);

    c.saveUser();

    expect(c.apiError()).toBe('Email taken');
    expect(c.isFormVisible()).toBe(true);
    expect(c.isLoading()).toBe(false);
  });

  it('editing fills the form from the user, loads that organisation\'s stores and saves with the id', () => {
    const { c, svc } = open();
    const asha = c.users()[0];

    c.openEditForm(asha);
    expect(c.mode()).toBe('edit');
    expect(c.form.controls.email.value).toBe('asha@shop.in');
    expect(c.form.controls.storeIds.value).toEqual(['s1']); // falls back to the single store
    expect(c.form.controls.permissions.value).toEqual(['orders:read']);
    expect(c.stores().map(s => s.name)).toEqual(['Kharghar']);

    c.saveUser();
    expect(svc.updateUser).toHaveBeenCalledWith('u1', expect.objectContaining({ id: 'u1', email: 'asha@shop.in' }));
  });

  it('view mode fills the form without loading stores', () => {
    const getStores = vi.fn(ok([]));
    const { c } = open({ getStores });
    c.openViewForm(c.users()[0]);

    expect(c.mode()).toBe('view');
    expect(c.isFormVisible()).toBe(true);
    expect(getStores).not.toHaveBeenCalled();
    c.closeForm();
    expect(c.isFormVisible()).toBe(false);
    expect(c.selectedUser()).toBeNull();
  });

  it('stores: ticking sets the first one as the main store; changing the organisation clears them', () => {
    const { c } = open();
    c.openCreateForm();

    c.toggleStore('s2');
    c.toggleStore('s1');
    expect(c.form.controls.storeIds.value).toEqual(['s2', 's1']);
    expect(c.form.controls.storeId.value).toBe('s2');
    expect(c.isStoreSelected('s1')).toBe(true);

    c.toggleStore('s2');
    expect(c.form.controls.storeId.value).toBe('s1');

    c.onOrganizationSelected('o2');
    expect(c.form.controls.storeIds.value).toEqual([]);
    expect(c.form.controls.storeId.value).toBe('');
  });

  it('permissions can be ticked and unticked', () => {
    const { c } = open();
    c.openCreateForm();

    c.togglePermission('orders:read');
    expect(c.isPermissionSelected('orders:read')).toBe(true);
    c.togglePermission('orders:read');
    expect(c.isPermissionSelected('orders:read')).toBe(false);
  });

  it('switching a user on or off sends the opposite status and reads the list again', () => {
    const getUsersPage = vi.fn(ok(page(users)));
    const { c, svc } = open({ getUsersPage });

    c.toggleUserStatus(c.users()[0]);
    expect(svc.toggleUserStatus).toHaveBeenCalledWith('u1', UserStatus.Inactive);
    c.toggleUserStatus(c.users()[1]);
    expect(svc.toggleUserStatus).toHaveBeenLastCalledWith('u2', UserStatus.Active);
    expect(getUsersPage).toHaveBeenCalledTimes(3);
  });

  it('a failed status change shows the message', () => {
    const { c } = open({ toggleUserStatus: () => throwError(() => ({ message: 'Cannot disable yourself' })) });
    c.toggleUserStatus(c.users()[0]);
    expect(c.apiError()).toBe('Cannot disable yourself');
    expect(c.isLoading()).toBe(false);
  });

  it('small helpers: initials, role label, organisation and store names', () => {
    const { c } = open();
    c.openEditForm(c.users()[0]); // loads the stores

    expect(c.initials(c.users()[0])).toBe('AP');
    expect(c.roleLabel(Role.StoreStaff)).toBe('Store Staff');
    expect(c.roleLabel('Mystery')).toBe('Mystery');
    expect(c.getOrganizationName('o2')).toBe('Other');
    expect(c.getOrganizationName('zz')).toBe('—');
    expect(c.getStoreName('s1')).toBe('Kharghar');
    expect(c.getStoreName(undefined)).toBe('—');
  });
});
