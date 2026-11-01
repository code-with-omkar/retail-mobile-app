import { TestBed } from '@angular/core/testing';
import { ProfileComponent } from './profile.component';
import { AuthService } from '../../core/services/auth.service';

function open(user: Record<string, unknown> | null) {
  TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: { userContext: () => user } }] });
  const fixture = TestBed.createComponent(ProfileComponent);
  fixture.detectChanges();
  return fixture;
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('ProfileComponent', () => {
  it('shows the name, initials, role, email and ids of the signed-in user', () => {
    const f = open({ displayName: 'Asha Patil', role: 'StoreStaff', email: 'asha@shop.in', organizationId: 'org-1', userId: 'u-1' });

    expect(f.componentInstance.initials()).toBe('AP');
    for (const part of ['Asha Patil', 'StoreStaff', 'asha@shop.in', 'org-1', 'u-1']) expect(text(f)).toContain(part);
  });

  it('falls back to plain labels when nothing is known', () => {
    const f = open(null);

    expect(f.componentInstance.initials()).toBe('?');
    expect(text(f)).toContain('Profile');
    expect(text(f)).toContain('Email unavailable');
  });
});
