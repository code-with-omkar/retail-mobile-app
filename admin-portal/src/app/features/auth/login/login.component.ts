import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../shared/services/toast.service';
import { Role } from '../../../core/models/domain.model';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonComponent, InputComponent],
  template: `
    <div class="login-container">
      <div class="login-card">
        <div class="login-header">
          <div class="logo">
            <span class="logo-mark">Q</span>
          </div>
          <h1>quickcart<span>.</span> admin</h1>
          <p>Operations Console</p>
        </div>

        <form [formGroup]="loginForm" (ngSubmit)="onSubmit()" class="login-form">
          <p class="login-error" role="alert" *ngIf="errorMessage()">{{ errorMessage() }}</p>

          <app-input
            label="Username or email"
            type="text"
            placeholder="admin"
            formControlName="username"
            [error]="getFieldError('username')"
          ></app-input>

          <app-input
            label="Password"
            type="password"
            placeholder="••••••••"
            formControlName="password"
            [error]="getFieldError('password')"
          ></app-input>

          <app-button
            type="submit"
            label="Sign In"
            [loading]="isLoading()"
            [disabled]="loginForm.invalid || isLoading()"
            variant="primary"
          ></app-button>
          <button type="button" class="forgot" (click)="forgot()">Forgot password?</button>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .forgot { color: #FFE27A; font: 700 13px inherit; justify-self: start; border: 0; background: none; padding: 0; cursor: pointer; text-decoration: underline; }

    .login-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: transparent;
      padding: 20px;
    }

    .login-card {
      background: transparent;
      padding: 24px 8px;
      width: 100%;
      max-width: 400px;
    }

    .login-header {
      text-align: center;
      margin-bottom: 36px;
    }

    .logo {
      display: grid;
      place-items: center;
      width: 52px;
      height: 52px;
      border-radius: 14px;
      background: #FFD400;
      color: #141118;
      margin: 0 auto 18px;
      font-weight: 800;
      font-size: 26px;
      font-family: 'Bricolage Grotesque', sans-serif;
      box-shadow: 0 8px 24px rgba(255,212,0,0.35);
    }

    h1 {
      margin: 0 0 8px;
      font-size: 22px;
      font-family: 'Bricolage Grotesque', sans-serif;
      font-weight: 700;
      color:var(--ink);
      letter-spacing: -0.5px;
    }

    h1 span {
      color: var(--ink);
    }

    .login-header p {
      margin: 0;
      color:var(--muted);
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 1.5px;
      font-family: 'Plus Jakarta Sans', sans-serif;
    }

    .login-error {
      margin: 0;
      padding: 12px 14px;
      border-radius: 14px;
      background: #FDECEA;
      color: #9B2414;
      font: 700 12px/1.4 'Plus Jakarta Sans', sans-serif;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 16px;
      margin-bottom: 24px;
    }
  `],
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private toast = inject(ToastService);
  private router = inject(Router);

  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  loginForm: FormGroup;

  constructor() {
    this.loginForm = this.fb.group({
      username: ['', Validators.required],
      password: ['', [Validators.required, Validators.minLength(6)]],
    });
  }

  forgot(): void { this.router.navigate(['/forgot-password']); }

  onSubmit(): void {
    if (this.loginForm.invalid) {
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.auth.login(this.loginForm.value).subscribe({
      next: () => {
        this.toast.success('Login successful');
        // The dashboard is the administrators' (the server refuses store staff), so staff start on Today.
        const staff = this.auth.userContext?.()?.role === Role.StoreStaff;
        const temporary = !!this.auth.userContext?.()?.mustChangePassword;
        this.router.navigate([temporary ? '/change-password' : staff ? '/today' : '/admin/dashboard']);
      },
      error: (error: any) => {
        // The server's own words when it sent some (wrong password), otherwise the reason found here.
        const message = error.error?.message || error.message || 'Login failed';
        this.errorMessage.set(message);
        this.toast.error(message);
        this.isLoading.set(false);
      },
    });
  }

  getFieldError(field: string): string | undefined {
    const control = this.loginForm.get(field);
    if (!control?.touched || !control?.errors) {
      return undefined;
    }

    if (control.errors['required']) {
      return 'This field is required';
    }
    if (control.errors['minlength']) {
      return 'Password must be at least 6 characters';
    }

    return 'Invalid input';
  }
}
