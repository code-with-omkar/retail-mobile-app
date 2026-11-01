import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CommissionComponent } from './commission.component';
import { ApiService } from '../../core/services/api.service';

const data = (def = 5, override: number | null = null) => ({
  defaultPercent: def,
  stores: [
    { storeId: 's1', name: 'Kharghar', overridePercent: override, effectivePercent: override ?? def },
    { storeId: 's2', name: 'Panvel', overridePercent: null, effectivePercent: def },
  ],
});

async function open(api: Partial<Record<'get' | 'put', (...a: any[]) => Observable<any>>>) {
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const fixture: ComponentFixture<CommissionComponent> = TestBed.createComponent(CommissionComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent ?? '';
const buttons = (f: ComponentFixture<unknown>) => Array.from((f.nativeElement as HTMLElement).querySelectorAll('button'));

describe('CommissionComponent', () => {
  it('lists the default and every store, noting which stores use the default', async () => {
    const f = await open({ get: () => of({ success: true, data: data() }) });

    expect(text(f)).toContain('Default for every store');
    expect(text(f)).toContain('Kharghar');
    expect(text(f)).toContain('Panvel');
    expect(text(f)).toContain('(default 5%)');
  });

  it('shows the message when the list cannot be loaded', async () => {
    const f = await open({ get: () => throwError(() => ({ message: 'Forbidden' })) });
    expect(text(f)).toContain('Forbidden');
  });

  it('saving the default sends the percent and reports it', async () => {
    const put = vi.fn(() => of({ success: true, data: data(7) }));
    const f = await open({ get: () => of({ success: true, data: data() }), put });

    f.componentInstance.defaultPercent = '7';
    f.componentInstance.saveDefault();
    f.detectChanges();

    expect(put).toHaveBeenCalledWith('/admin/commission/default', { percent: 7 });
    expect(text(f)).toContain('Saved. New orders use it.');
    expect(f.componentInstance.defaultPercent).toBe('7');
  });

  it('a percent outside 0 to 100 is refused without calling the server', async () => {
    const put = vi.fn();
    const f = await open({ get: () => of({ success: true, data: data() }), put });

    for (const bad of ['101', '-1', 'abc']) {
      f.componentInstance.defaultPercent = bad;
      f.componentInstance.saveDefault();
      expect(f.componentInstance.error()).toBe('Give a percent from 0 to 100.');
    }
    expect(put).not.toHaveBeenCalled();
  });

  it('saving a store sends its own percent, and Use default clears it', async () => {
    const put = vi.fn(() => of({ success: true, data: data(5, 9) }));
    const f = await open({ get: () => of({ success: true, data: data(5, 9) }), put });

    f.componentInstance.setStore('s1', '12.5');
    f.componentInstance.saveStore('s1');
    expect(put).toHaveBeenLastCalledWith('/admin/commission/stores/s1', { percent: 12.5 });

    f.componentInstance.clearStore('s1');
    expect(put).toHaveBeenLastCalledWith('/admin/commission/stores/s1', { percent: null });
  });

  it('Use default shows only for a store that has its own percent', async () => {
    const f = await open({ get: () => of({ success: true, data: data(5, 9) }) });
    expect(buttons(f).filter(b => b.textContent?.trim() === 'Use default')).toHaveLength(1);
  });

  it('a failed save shows the server message and frees the buttons', async () => {
    const f = await open({ get: () => of({ success: true, data: data() }), put: () => throwError(() => ({ message: 'Not allowed' })) });

    f.componentInstance.saveDefault();
    f.detectChanges();

    expect(text(f)).toContain('Not allowed');
    expect(f.componentInstance.busy()).toBe(false);
  });
});
