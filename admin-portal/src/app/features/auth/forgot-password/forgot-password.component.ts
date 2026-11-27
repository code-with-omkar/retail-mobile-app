import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PasswordService, errorText, passwordProblem } from '../../../core/services/password.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="box">
      <h1>{{ step() === 'done' ? 'Password saved' : 'Forgot password' }}</h1>
      <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>

      <form *ngIf="step() === 'email'" (ngSubmit)="send()">
        <p>Enter your email address. If it has an account, we send a code to it.</p>
        <label>Email<input type="email" name="email" autocomplete="email" [(ngModel)]="email" /></label>
        <div class="row"><button type="submit" class="primary" [disabled]="busy() || !email.trim()">{{ busy() ? 'Sending…' : 'Send code' }}</button></div>
      </form>

      <form *ngIf="step() === 'code'" (ngSubmit)="save()">
        <p>If {{ email }} has an account, the code is on its way. It works for a short time.</p>
        <label>Code from the email<input name="code" autocomplete="one-time-code" [(ngModel)]="code" /></label>
        <label>New password<input type="password" name="next" autocomplete="new-password" [(ngModel)]="next" /></label>
        <label>New password again<input type="password" name="again" autocomplete="new-password" [(ngModel)]="again" /></label>
        <div class="row">
          <button type="submit" class="primary" [disabled]="busy() || !code.trim() || !next || !again">{{ busy() ? 'Saving…' : 'Save password' }}</button>
          <button type="button" class="ghost" (click)="step.set('email')">Use another email</button>
        </div>
      </form>

      <p *ngIf="step() === 'done'">You can sign in with your new password.</p>
      <a routerLink="/login">Back to sign in</a>
    </div>
  `,
  styles: [`
    :host{display:flex;align-items:center;justify-content:center;min-height:100vh;padding:20px;box-sizing:border-box;color:var(--ink)}
    .box{width:100%;max-width:400px;display:grid;gap:14px}
    h1{margin:0;font:800 26px var(--display)}
    p{margin:0;font-size:13px;color:var(--muted)}
    form{display:grid;gap:12px}
    label{display:grid;gap:4px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.4px}
    input{border:0;border-radius:14px;padding:12px 14px;font:inherit;font-size:14px;text-transform:none;letter-spacing:0}
    .row{display:flex;gap:10px;flex-wrap:nowrap}
    a{color:#FFE27A;font-size:13px;font-weight:700}
    small{font-size:11px;color:var(--muted)}
  `],
})
export class ForgotPasswordComponent {
  private passwords = inject(PasswordService);

  email = '';
  code = '';
  next = '';
  again = '';
  readonly step = signal<'email' | 'code' | 'done'>('email');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  send() {
    if (this.busy() || !this.email.trim()) return;
    this.busy.set(true); this.error.set(null);
    this.passwords.forgot(this.email.trim()).subscribe({
      next: () => { this.busy.set(false); this.step.set('code'); },
      error: e => { this.busy.set(false); this.error.set(errorText(e, 'Could not send the code.')); },
    });
  }

  save() {
    if (this.busy()) return;
    const problem = passwordProblem(this.next, this.again);
    if (problem) { this.error.set(problem); return; }
    this.busy.set(true); this.error.set(null);
    this.passwords.reset(this.email.trim(), this.code.trim(), this.next).subscribe({
      next: () => { this.busy.set(false); this.step.set('done'); this.code = this.next = this.again = ''; },
      error: e => { this.busy.set(false); this.error.set(errorText(e, 'Could not save the password.')); },
    });
  }
}
