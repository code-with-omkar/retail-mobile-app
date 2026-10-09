import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../shared/services/toast.service';

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
        </form>

        <div class="login-footer">
          <p class="demo-notice">
            Development admin: admin / QuickCartAdmin123!
          </p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .login-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(145deg, #2A1BFF 0%, #6A11E8 50%, #B00699 100%);
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
      font-family: 'Space Grotesk', sans-serif;
      box-shadow: 0 8px 24px rgba(255,212,0,0.35);
    }

    h1 {
      margin: 0 0 8px;
      font-size: 22px;
      font-family: 'Space Grotesk', sans-serif;
      font-weight: 700;
      color: #fff;
      letter-spacing: -0.5px;
    }

    h1 span {
      color: #FFD400;
    }

    .login-header p {
      margin: 0;
      color: #E9E4FF;
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 1.5px;
      font-family: 'Inter', sans-serif;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 16px;
      margin-bottom: 24px;
    }

    .login-footer {
      text-align: center;
      padding-top: 24px;
      border-top: 1px solid rgba(255,255,255,0.25);
    }

    .demo-notice {
      margin: 0;
      font-size: 10px;
      color: #E9E4FF;
      line-height: 1.5;
      font-family: 'Inter', sans-serif;
    }
  `],
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private toast = inject(ToastService);
  private router = inject(Router);

  isLoading = signal(false);
  loginForm: FormGroup;

  constructor() {
    this.loginForm = this.fb.group({
      username: ['', Validators.required],
      password: ['', [Validators.required, Validators.minLength(6)]],
    });
  }

  onSubmit(): void {
    if (this.loginForm.invalid) {
      return;
    }

    this.isLoading.set(true);
    this.auth.login(this.loginForm.value).subscribe({
      next: () => {
        this.toast.success('Login successful');
        this.router.navigate(['/admin/dashboard']);
      },
      error: (error: any) => {
        this.toast.error(error.message || 'Login failed');
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
