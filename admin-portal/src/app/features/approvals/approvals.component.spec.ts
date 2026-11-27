import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ApprovalsComponent } from './approvals.component';
import { ApprovalService } from '../../core/services/approval.service';
import { AuthService } from '../../core/services/auth.service';
import { ApiService } from '../../core/services/api.service';
import { StaffCategory } from '../../core/models/domain.model';

const request = (over: Record<string, unknown> = {}) => ({
  id: 'a1', entityType: 'Product', approvalType: 'PriceChange', storeName: 'Kharghar', requestedBy: 'Ravi', status: 'Pending', createdAt: '2026-11-01T10:00:00Z', ...over,
});

type Call = (...a: any[]) => Observable<any>;

function open(category: StaffCategory, service: Partial<Record<'pending' | 'mine' | 'approve' | 'reject' | 'create', Call>>) {
  const get = vi.fn(() => of({ success: true, data: [{ id: 's1', name: 'Kharghar' }] }));
  TestBed.configureTestingModule({
    providers: [
      { provide: ApprovalService, useValue: service },
      { provide: AuthService, useValue: { userContext: () => ({ staffCategory: category }), getStoreId: () => 's1' } },
      { provide: ApiService, useValue: { get } },
    ],
  });
  const fixture = TestBed.createComponent(ApprovalsComponent);
  fixture.detectChanges();
  fixture.detectChanges();
  return fixture;
}

const list = (data: unknown[]) => vi.fn(() => of({ success: true, data }));
const text = (f: { nativeElement: unknown }) => (f.nativeElement as HTMLElement).textContent ?? '';

describe('ApprovalsComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('a manager sees the pending requests of the stores, with counts per status', () => {
    const pending = list([request(), request({ id: 'a2', status: 'Approved' }), request({ id: 'a3', status: 'Rejected', rejectionReason: 'Too high' })]);
    const mine = list([]);
    const f = open(StaffCategory.StoreManager, { pending, mine });

    expect(pending).toHaveBeenCalled();
    expect(mine).not.toHaveBeenCalled();
    expect(text(f)).toContain('Approval Requests');
    expect(f.componentInstance.count('Pending')).toBe(1);
    expect(f.componentInstance.count('Approved')).toBe(1);
    expect(text(f)).toContain('Too high');
    expect((f.nativeElement as HTMLElement).querySelectorAll('.approve')).toHaveLength(1); // only the pending one has buttons
  });

  it('an employee sees their own requests and the new-request form, but no Approve buttons', () => {
    const mine = list([request()]);
    const f = open(StaffCategory.StoreEmployee, { mine, pending: list([]) });
    // the new-request form is in the side panel
    f.componentInstance.creating.set(true);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;

    expect(text(f)).toContain('My Approval Requests');
    expect(el.querySelector('form')).not.toBeNull();
    expect(el.querySelector('.approve')).toBeNull();
    expect(f.componentInstance.storeId).toBe('s1');
  });

  it('says so when there are no requests, and shows the message when loading fails', () => {
    expect(text(open(StaffCategory.StoreManager, { pending: list([]) }))).toContain('No approval requests.');
    TestBed.resetTestingModule();
    expect(text(open(StaffCategory.StoreManager, { pending: () => throwError(() => ({ message: 'Denied' })) }))).toContain('Denied');
  });

  it('Approve sends the id and loads the list again', () => {
    const approve = vi.fn(() => of({ success: true }));
    const pending = list([request()]);
    const f = open(StaffCategory.StoreManager, { pending, approve });

    f.componentInstance.decide(f.componentInstance.requests()[0] as never, true);

    expect(approve).toHaveBeenCalledWith('a1');
    expect(pending).toHaveBeenCalledTimes(2);
  });

  it('Reject asks for a reason and does nothing without one', () => {
    const reject = vi.fn(() => of({ success: true }));
    const f = open(StaffCategory.StoreManager, { pending: list([request()]), reject });
    const r = f.componentInstance.requests()[0] as never;

    vi.spyOn(window, 'prompt').mockReturnValueOnce('   ');
    f.componentInstance.decide(r, false);
    expect(reject).not.toHaveBeenCalled();

    vi.spyOn(window, 'prompt').mockReturnValueOnce(' Too high ');
    f.componentInstance.decide(r, false);
    expect(reject).toHaveBeenCalledWith('a1', 'Too high');
  });

  it('a failed decision shows the server message', () => {
    const f = open(StaffCategory.StoreManager, { pending: list([request()]), approve: () => throwError(() => ({ message: 'Already decided' })) });
    f.componentInstance.decide(f.componentInstance.requests()[0] as never, true);
    expect(f.componentInstance.error()).toBe('Already decided');
  });

  it('submitting needs every field; a valid request is created trimmed and the form is cleared', () => {
    const create = vi.fn(() => of({ success: true }));
    const mine = list([]);
    const f = open(StaffCategory.StoreEmployee, { mine, create });
    const c = f.componentInstance;

    c.submit();
    expect(c.error()).toBe('Complete all request fields.');
    expect(create).not.toHaveBeenCalled();

    c.entityType = ' Product '; c.entityId = ' p1 '; c.approvalType = ' PriceChange ';
    c.submit();

    expect(create).toHaveBeenCalledWith({ entityType: 'Product', entityId: 'p1', approvalType: 'PriceChange', storeId: 's1' });
    expect(c.entityType).toBe('');
    expect(c.submitting()).toBe(false);
    expect(mine).toHaveBeenCalledTimes(2);
  });

  it('a failed submit shows the message and frees the button', () => {
    const f = open(StaffCategory.StoreEmployee, { mine: list([]), create: () => throwError(() => ({ message: 'Duplicate' })) });
    const c = f.componentInstance;
    c.entityType = 'a'; c.entityId = 'b'; c.approvalType = 'c';

    c.submit();

    expect(c.error()).toBe('Duplicate');
    expect(c.submitting()).toBe(false);
  });
});
