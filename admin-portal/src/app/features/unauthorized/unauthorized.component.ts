import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ButtonComponent } from '../../shared/components/button/button.component';

@Component({
  selector: 'app-unauthorized',
  standalone: true,
  imports: [CommonModule, ButtonComponent],
  template: `
    <div class="error-container">
      <div class="error-content">
        <div class="error-icon">⛔</div>
        <h1>Access Denied</h1>
        <p>You don't have permission to access this resource.</p>
        <app-button
          label="Go to Dashboard"
          variant="primary"
          (onClick)="goHome()"
        ></app-button>
      </div>
    </div>
  `,
  styles: [`
    .error-container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
      background: #f6f8f5;
    }

    .error-content {
      text-align: center;
      background: #fff;
      border: 1px solid #e5ebe6;
      border-radius: 12px;
      padding: 60px 40px;
      max-width: 400px;
    }

    .error-icon {
      font-size: 64px;
      margin-bottom: 20px;
    }

    h1 {
      margin: 0 0 12px;
      font-size: 28px;
      font-family: Manrope, sans-serif;
      color: #172523;
    }

    p {
      margin: 0 0 24px;
      color: #82908b;
      font-size: 14px;
    }
  `],
})
export class UnauthorizedComponent {
  private router = inject(Router);

  goHome(): void {
    this.router.navigate(['/dashboard']);
  }
}
