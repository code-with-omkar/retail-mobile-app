import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ComplaintsComponent } from './complaints.component';
import { ApiService } from '../../core/services/api.service';

const complaint = (over: Record<string, unknown> = {}) => ({ id: 'c1', orderNumber: 'ORD-1', storeId: 's1', store: 'Kharghar', customer: 'Asha', kind: 'Damaged', note: 'Torn', status: 'Open', claimedAmount: 90, approvedAmount: null, decisionNote: null, refundMethod: null, cashPaidAt: null, createdAt: '2026-11-26T10:00:00Z', canDecide: true, items: [{ name: 'Rice', label: '1 kg', quantity: 1, amount: 90 }], ...over });

function open(list: unknown[]) {
  localStorage.clear();
  const api = { get: vi.fn(() => of({ success: true, data: list })), post: vi.fn(() => of({ success: true, data: {} })) };
  TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
  const f = TestBed.createComponent(ComplaintsComponent);
  f.detectChanges();
  f.detectChanges();
  return { f, c: f.componentInstance, api, text: () => (f.nativeElement as HTMLElement).textContent ?? '' };
}

describe('ComplaintsComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('lists the complaints with what is claimed', () => {
    const { text } = open([complaint()]);
    expect(text()).toContain('ORD-1');
    expect(text()).toContain('1 × Rice');
    expect(text()).toContain('₹90.00');
  });

  it('refusing needs a message; approving sends the amount after a confirmation', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const { c, api } = open([complaint()]);
    c.view(complaint() as never);
    c.decide(false);
    expect(api.post).not.toHaveBeenCalled();
    c.amount.set(40);
    c.decide(true);
    expect(api.post).toHaveBeenCalledWith('/issues/c1/decide', { approve: true, amount: 40, note: null });
    expect(c.open()).toBeNull();
  });

  it('a cash refund waits to be marked paid', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const waiting = complaint({ status: 'Approved', approvedAmount: 40, refundMethod: 'Cash', canDecide: false });
    const { c, api, text } = open([waiting]);
    expect(text()).toContain('Cash to be paid');
    c.cashPaid(waiting as never);
    expect(api.post).toHaveBeenCalledWith('/issues/c1/cash-paid', {});
  });

  it('a 404 shows no records rather than an error', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: { get: () => throwError(() => ({ status: 404, message: 'Error: 404 Not Found' })) } }] });
    const f = TestBed.createComponent(ComplaintsComponent);
    f.detectChanges();
    f.detectChanges();
    expect(f.componentInstance.error()).toBeNull();
    expect((f.nativeElement as HTMLElement).textContent).toContain('No records found.');
  });
});
