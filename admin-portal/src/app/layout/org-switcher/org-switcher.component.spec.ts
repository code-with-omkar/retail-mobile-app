import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Role } from '../../core/models/domain.model';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { StorageService } from '../../core/services/storage.service';
import { OrgSwitcherComponent } from './org-switcher.component';

const list = (current = 'o1') => ({
  success: true,
  data: { homeOrganizationId: 'o1', currentOrganizationId: current, organizations: [{ id: 'o1', name: 'Home Retail' }, { id: 'o2', name: 'Other Retail' }] },
});

function open(role: Role, get: ReturnType<typeof vi.fn>) {
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiService, useValue: { get } },
      { provide: AuthService, useValue: { hasRole: (r: Role) => r === role } },
    ],
  });
  const storage = TestBed.inject(StorageService);
  storage.removeOrganization();
  const f = TestBed.createComponent(OrgSwitcherComponent);
  const c = f.componentInstance;
  c.reload = vi.fn();
  f.detectChanges();
  return { f, c, storage };
}
const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('OrgSwitcherComponent', () => {
  it('shows nothing to anyone but a platform administrator, and asks the server nothing', () => {
    const get = vi.fn(() => of(list()));
    const { f } = open(Role.Admin, get);
    expect(get).not.toHaveBeenCalled();
    expect(text(f).trim()).toBe('');
  });

  it('a platform administrator sees the organizations, with their own marked, and no warning at home', () => {
    const { f } = open(Role.ApplicationAdmin, vi.fn(() => of(list())));
    expect(text(f)).toContain('Home Retail (yours)');
    expect(text(f)).toContain('Other Retail');
    expect(text(f)).not.toContain('Read-only');
  });

  it('choosing another organization remembers it, starts over, and says it is read-only', () => {
    const { f, c, storage } = open(Role.ApplicationAdmin, vi.fn(() => of(list())));
    c.choose('o2');
    f.detectChanges();
    expect(storage.getOrganization()).toBe('o2');
    expect(c.reload).toHaveBeenCalled();
    expect(text(f)).toContain('Read-only: you are looking at Other Retail');
  });

  it('choosing the own organization again forgets the choice', () => {
    const { c, storage } = open(Role.ApplicationAdmin, vi.fn(() => of(list('o2'))));
    storage.setOrganization('o2');
    c.choose('o1');
    expect(storage.getOrganization()).toBeNull();
    expect(c.reload).toHaveBeenCalled();
  });

  it('a remembered organization the server refuses is dropped and the error shown', () => {
    const { f, storage } = open(Role.ApplicationAdmin, vi.fn(() => throwError(() => ({ message: 'Only a platform administrator can switch organizations.' }))));
    expect(storage.getOrganization()).toBeNull();
    expect(text(f)).toContain('Only a platform administrator');
  });
});
