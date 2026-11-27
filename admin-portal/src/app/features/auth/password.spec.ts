import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ChangePasswordComponent } from './change-password/change-password.component';
import { ForgotPasswordComponent } from './forgot-password/forgot-password.component';
import { PasswordService, passwordProblem } from '../../core/services/password.service';
import { AuthService } from '../../core/services/auth.service';
import { Role } from '../../core/models/domain.model';

function setup(passwords: Record<string, unknown>, mustChangePassword = true) {
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: PasswordService, useValue: passwords }, { provide: AuthService, useValue: { userContext: () => ({ role: Role.StoreStaff, mustChangePassword }), logout: vi.fn() } }] });
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

describe('portal passwords', () => {
  it('says what is wrong with a new password', () => {
    expect(passwordProblem('short1', 'short1')).toContain('8');
    expect(passwordProblem('lettersonly', 'lettersonly')).toContain('number');
    expect(passwordProblem('goodpass1', 'goodpass2')).toContain('not the same');
    expect(passwordProblem('goodpass1', 'goodpass1')).toBeNull();
  });

  it('a forced change saves and goes on to the staff home; a refusal is shown', () => {
    const change = vi.fn(() => of({}));
    const navigate = setup({ change });
    const f = TestBed.createComponent(ChangePasswordComponent);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('temporary password');
    const c = f.componentInstance;
    c.current = 'Temp-1234abcd'; c.next = c.again = 'Temp-1234abcd';
    c.save();
    expect(change).not.toHaveBeenCalled(); // same as before
    c.next = c.again = 'my-own-pass1';
    c.save();
    expect(change).toHaveBeenCalledWith('Temp-1234abcd', 'my-own-pass1');
    expect(navigate).toHaveBeenCalledWith(['/today']);

    change.mockReturnValue(throwError(() => ({ error: { message: 'Your current password is incorrect.' } })) as never);
    c.save();
    expect(c.error()).toBe('Your current password is incorrect.');
  });

  it('forgot password asks for the code, then saves the new one', () => {
    const forgot = vi.fn(() => of({})), reset = vi.fn(() => of({}));
    setup({ forgot, reset }, false);
    const f = TestBed.createComponent(ForgotPasswordComponent);
    const c = f.componentInstance;
    c.email = ' a@shop.in ';
    c.send();
    expect(forgot).toHaveBeenCalledWith('a@shop.in');
    expect(c.step()).toBe('code');
    c.code = '123456'; c.next = c.again = 'my-own-pass1';
    c.save();
    expect(reset).toHaveBeenCalledWith('a@shop.in', '123456', 'my-own-pass1');
    expect(c.step()).toBe('done');
  });
});
