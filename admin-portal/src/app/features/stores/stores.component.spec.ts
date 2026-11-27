import { AuthService } from '../../core/services/auth.service';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StoresComponent } from './stores.component';
import { ApiService } from '../../core/services/api.service';

const store = (over: Record<string, unknown> = {}) => ({ id: 's1', name: 'kharghar', address: 'Sector 7', isActive: true, ...over });

function open(get: (...a: any[]) => any) {
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { get } }, { provide: AuthService, useValue: { isAdmin: () => false } }] });
  const fixture = TestBed.createComponent(StoresComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

beforeEach(() => localStorage.clear());

describe('StoresComponent', () => {
  it('lists each store with its address, first letter and Active / Inactive', () => {
    const f = open(() => of({ success: true, data: [store(), store({ id: 's2', name: 'Panvel', address: 'Station Rd', isActive: false })] }));

    expect(text(f)).toContain('kharghar');
    expect(text(f)).toContain('Sector 7');
    expect(text(f)).toContain('Active');
    expect(text(f)).toContain('Inactive');
    expect(Array.from((f.nativeElement as HTMLElement).querySelectorAll('.avatar')).map(a => a.textContent?.trim())).toEqual(['K', 'P']);
  });

  it('says so when there are no stores', () => {
    expect(text(open(() => of({ success: true, data: [] })))).toContain('No stores found.');
  });

  it('shows the message when loading fails', () => {
    const f = open(() => throwError(() => ({ message: 'Offline' })));
    expect(text(f)).toContain('Offline');
    expect(f.componentInstance.loading()).toBe(false);
  });

  it('a new store is checked, saved with its code in capitals, and the list is read again', async () => {
    const api = { get: vi.fn(() => of({ success: true, data: [] })), post: vi.fn(() => of({ success: true, data: {} })), put: vi.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }, { provide: AuthService, useValue: { isAdmin: () => true } }] });
    const f = TestBed.createComponent(StoresComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.edit(null);
    c.save();
    expect(api.post).not.toHaveBeenCalled();
    c.form = { ...c.form, name: ' Vashi ', address: '1 Road', latitude: 19.07, longitude: 73.0, code: 'vsh' };
    c.save();
    expect(api.post).toHaveBeenCalledWith('/admin/stores', expect.objectContaining({ name: 'Vashi', code: 'VSH', serviceRadiusKm: 6, phoneNumber: null }));
    expect(c.panelOpen()).toBe(false);
    expect(api.get).toHaveBeenCalledTimes(2);
  });
});
