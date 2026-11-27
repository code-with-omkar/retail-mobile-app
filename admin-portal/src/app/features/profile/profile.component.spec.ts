import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { TestBed } from '@angular/core/testing';
import { ProfileComponent } from './profile.component';
import { AuthService } from '../../core/services/auth.service';

const get = vi.fn(() => of({ success: true, data: [{ id: 'org-1', name: 'Fresh Foods' }] }));

function open(user: Record<string, unknown> | null) {
  get.mockClear();
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: ApiService, useValue: { get } }, { provide: AuthService, useValue: { userContext: () => user } }] });
  const fixture = TestBed.createComponent(ProfileComponent);
  fixture.detectChanges();
  return fixture;
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('ProfileComponent', () => {
  it('shows the name, initials, role, email and ids of the signed-in user', () => {
    const f = open({ displayName: 'Asha Patil', role: 'StoreStaff', email: 'asha@shop.in', organizationId: 'org-1', userId: 'u-1' });

    expect(f.componentInstance.initials()).toBe('AP');
    for (const part of ['Asha Patil', 'StoreStaff', 'asha@shop.in', 'u-1']) expect(text(f)).toContain(part);
    expect(text(f)).not.toContain('org-1');
    expect(get).not.toHaveBeenCalled();
  });

  it('shows an administrator the organization by name, never by number', () => {
    const f = open({ displayName: 'Admin', role: 'Admin', organizationId: 'org-1', userId: 'u-2' });

    expect(text(f)).toContain('Fresh Foods');
    expect(text(f)).not.toContain('org-1');
  });

  it('falls back to plain labels when nothing is known', () => {
    const f = open(null);

    expect(f.componentInstance.initials()).toBe('?');
    expect(text(f)).toContain('Profile');
    expect(text(f)).toContain('Email unavailable');
  });
});
