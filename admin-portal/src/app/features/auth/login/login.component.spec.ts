import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../shared/services/toast.service';

function open(login: (...a: any[]) => Observable<unknown>, role?: string) {
  const toast = { success: vi.fn(), error: vi.fn() };
  const navigate = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: { login, userContext: () => (role ? { role } : null) } },
      { provide: ToastService, useValue: toast },
      { provide: Router, useValue: { navigate } },
    ],
  });
  const fixture = TestBed.createComponent(LoginComponent);
  fixture.detectChanges();
  return { c: fixture.componentInstance, f: fixture, toast, navigate };
}

describe('LoginComponent', () => {
  it('does not call the server until both fields are filled and the password has 6 characters', () => {
    const login = vi.fn(() => of({}));
    const { c } = open(login);

    c.onSubmit();
    c.loginForm.setValue({ username: 'asha', password: '123' });
    c.onSubmit();

    expect(login).not.toHaveBeenCalled();
    expect(c.isLoading()).toBe(false);
  });

  it('a good sign-in says so and opens the dashboard', () => {
    const login = vi.fn(() => of({}));
    const { c, toast, navigate } = open(login);

    c.loginForm.setValue({ username: 'asha', password: 'secret1' });
    c.onSubmit();

    expect(login).toHaveBeenCalledWith({ username: 'asha', password: 'secret1' });
    expect(toast.success).toHaveBeenCalledWith('Login successful');
    expect(navigate).toHaveBeenCalledWith(['/admin/dashboard']);
  });

  it('store staff start on Today, not the administrators page that would deny them', () => {
    const { c, navigate } = open(() => of({}), 'StoreStaff');

    c.loginForm.setValue({ username: 'asha', password: 'secret1' });
    c.onSubmit();

    expect(navigate).toHaveBeenCalledWith(['/today']);
  });

  it('shows why sign-in was refused on the page itself, where there is no toast area', () => {
    const { c, f } = open(() => throwError(() => new Error('This is a delivery partner account. Sign in with the QuickCart delivery app.')));

    c.loginForm.setValue({ username: 'asha', password: 'secret1' });
    c.onSubmit();
    f.detectChanges();

    expect((f.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('delivery app');
  });

  it('a failed sign-in shows the server message and lets the user try again', () => {
    const { c, toast, navigate } = open(() => throwError(() => ({ message: 'Wrong password' })));

    c.loginForm.setValue({ username: 'asha', password: 'secret1' });
    c.onSubmit();

    expect(toast.error).toHaveBeenCalledWith('Wrong password');
    expect(navigate).not.toHaveBeenCalled();
    expect(c.isLoading()).toBe(false);
  });

  it('a failed sign-in without a message says Login failed', () => {
    const { c, toast } = open(() => throwError(() => ({})));
    c.loginForm.setValue({ username: 'asha', password: 'secret1' });
    c.onSubmit();
    expect(toast.error).toHaveBeenCalledWith('Login failed');
  });

  it('field errors appear only after the field was touched', () => {
    const { c } = open(() => of({}));

    expect(c.getFieldError('username')).toBeUndefined();
    c.loginForm.get('username')!.markAsTouched();
    expect(c.getFieldError('username')).toBe('This field is required');

    c.loginForm.get('password')!.setValue('123');
    c.loginForm.get('password')!.markAsTouched();
    expect(c.getFieldError('password')).toBe('Password must be at least 6 characters');

    c.loginForm.get('password')!.setValue('123456');
    expect(c.getFieldError('password')).toBeUndefined();
  });
});
