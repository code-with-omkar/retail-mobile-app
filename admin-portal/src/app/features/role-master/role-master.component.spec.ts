import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { RoleMasterComponent } from './role-master.component';
import { UserMasterService } from '../../core/services/user-master.service';

const perm = (id: string, code: string, over: Record<string, unknown> = {}) => ({ id, code, name: code.toUpperCase(), description: '', isActive: true, ...over });
const permissions = [perm('p1', 'orders:read'), perm('p2', 'orders:write'), perm('p3', 'stock:read')];
const role = (id: string, name: string, ids: string[]) => ({ id, name, permissions: permissions.filter(p => ids.includes(p.id)) });

type Call = (...a: any[]) => Observable<any>;

function open(service: Partial<Record<'getRoles' | 'getPermissions' | 'updateRolePermissions', Call>> = {}) {
  const full = {
    getRoles: () => of({ success: true, data: [role('r1', 'Store Manager', ['p1']), role('r2', 'Staff', [])] }),
    getPermissions: () => of({ success: true, data: permissions }),
    updateRolePermissions: vi.fn(),
    ...service,
  };
  TestBed.configureTestingModule({ providers: [{ provide: UserMasterService, useValue: full }] });
  const fixture = TestBed.createComponent(RoleMasterComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return { f: fixture, c: fixture.componentInstance, svc: full };
}

describe('RoleMasterComponent', () => {
  it('opens on the first role with its permissions ticked', () => {
    const { c } = open();

    expect(c.selectedRole()?.name).toBe('Store Manager');
    expect(c.selectedPermissionIds()).toEqual(['p1']);
    expect(c.isSelected('p1')).toBe(true);
    expect(c.percent()).toBe(33); // 1 of 3
  });

  it('shows the message when roles or permissions cannot be loaded', () => {
    expect(open({ getRoles: () => throwError(() => ({ message: 'No roles' })) }).c.error()).toBe('No roles');
    TestBed.resetTestingModule();
    expect(open({ getPermissions: () => throwError(() => ({ message: 'No perms' })) }).c.error()).toBe('No perms');
  });

  it('groups the permissions by their prefix and counts the ticked ones', () => {
    const { c } = open();

    expect(c.groups().map(g => [g.name, g.items.length, g.enabled])).toEqual([['orders', 2, 1], ['stock', 1, 0]]);
  });

  it('search narrows the groups', () => {
    const { c } = open();
    c.query.set('stock');
    expect(c.groups().map(g => g.name)).toEqual(['stock']);
  });

  it('toggle ticks and unticks; a group can be switched on or off as a whole', () => {
    const { c } = open();

    c.toggle('p2');
    expect(c.selectedPermissionIds()).toEqual(['p1', 'p2']);
    c.toggle('p1');
    expect(c.selectedPermissionIds()).toEqual(['p2']);

    const orders = c.groups().find(g => g.name === 'orders')!;
    c.setGroup(orders, true);
    expect([...c.selectedPermissionIds()].sort()).toEqual(['p1', 'p2']);
    c.setGroup(orders, false);
    expect(c.selectedPermissionIds()).toEqual([]);
  });

  it('isDirty is true only when the ticks differ from what is saved', () => {
    const { c } = open();

    expect(c.isDirty()).toBe(false);
    c.toggle('p2');
    expect(c.isDirty()).toBe(true);
    c.toggle('p2');
    expect(c.isDirty()).toBe(false);
  });

  it('choosing another role loads its ticks and clears an old error', () => {
    const { c } = open();
    c.error.set('old');

    c.selectRole(c.roles()[1]);

    expect(c.selectedPermissionIds()).toEqual([]);
    expect(c.error()).toBeNull();
    c.selectRole(undefined); // nothing happens
    expect(c.selectedRole()?.name).toBe('Staff');
  });

  it('save sends the ticked ids and replaces the role with the reply', () => {
    const saved = role('r1', 'Store Manager', ['p1', 'p3']);
    const { c, svc } = open({ updateRolePermissions: vi.fn(() => of({ success: true, data: saved })) });

    c.toggle('p3');
    c.save();

    expect(svc.updateRolePermissions).toHaveBeenCalledWith('r1', ['p1', 'p3']);
    expect(c.roles()[0]).toEqual(saved);
    expect(c.isDirty()).toBe(false);
    expect(c.isSaving()).toBe(false);
  });

  it('a failed save shows the message and frees the button', () => {
    const { c } = open({ updateRolePermissions: () => throwError(() => ({ message: 'Not allowed' })) });
    c.toggle('p3');
    c.save();
    expect(c.error()).toBe('Not allowed');
    expect(c.isSaving()).toBe(false);
  });

  it('shows the initials of a role name', () => {
    expect(open().c.initials('Store Manager')).toBe('SM');
  });
});
