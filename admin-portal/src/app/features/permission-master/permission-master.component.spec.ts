import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PermissionMasterComponent } from './permission-master.component';
import { UserMasterService } from '../../core/services/user-master.service';

const perm = (id: string, code: string, over: Record<string, unknown> = {}) => ({ id, code, name: code.toUpperCase(), description: 'Does ' + code, isActive: true, ...over });

function open(getPermissions: () => unknown) {
  TestBed.configureTestingModule({ providers: [{ provide: UserMasterService, useValue: { getPermissions } }] });
  const fixture = TestBed.createComponent(PermissionMasterComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const list = [perm('p1', 'orders:read'), perm('p2', 'orders:write', { isActive: false }), perm('p3', 'stock.read')];
const loaded = () => open(() => of({ success: true, data: list }));

describe('PermissionMasterComponent', () => {
  it('lists the permissions and counts the active ones', () => {
    const f = loaded();
    const c = f.componentInstance;

    expect(c.permissions()).toHaveLength(3);
    expect(c.activeCount()).toBe(2);
    expect((f.nativeElement as HTMLElement).textContent).toContain('ORDERS:READ');
  });

  it('makes one chip per group with its size; ":" "." and "_" all split the prefix', () => {
    const c = loaded().componentInstance;
    expect(c.chips().map(x => [x.name, x.count])).toEqual([['orders', 2], ['stock', 1]]);
    expect(c.groupOf(list[2] as never)).toBe('stock');
  });

  it('filters by group and by text in name, code or description', () => {
    const c = loaded().componentInstance;

    c.group.set('stock');
    expect(c.filtered().map(p => p.id)).toEqual(['p3']);

    c.group.set('all');
    c.query.set('  DOES ORDERS:W ');
    expect(c.filtered().map(p => p.id)).toEqual(['p2']);

    c.query.set('nothing like this');
    expect(c.filtered()).toEqual([]);
  });

  it('shows the message when permissions cannot be loaded', () => {
    expect(open(() => throwError(() => ({ message: 'Denied' }))).componentInstance.error()).toBe('Denied');
  });
});
