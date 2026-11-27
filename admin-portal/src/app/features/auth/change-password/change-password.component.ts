import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { PasswordService, errorText, passwordProblem } from '../../../core/services/password.service';
import { Role } from '../../../core/models/domain.model';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="box">
      <h1>{{ forced() ? 'Choose your password' : 'Change password' }}</h1>
      <p>{{ forced() ? 'You signed in with a temporary password. Choose your own to continue.' : 'Other devices signed in to this account are signed out.' }}</p>
      <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
      <form (ngSubmit)="save()">
        <label>{{ forced() ? 'Temporary password' : 'Current password' }}<input type="password" name="current" autocomplete="current-password" [(ngModel)]="current" /></label>
        <label>New password<input type="password" name="next" autocomplete="new-password" [(ngModel)]="next" /></label>
        <label>New password again<input type="password" name="again" autocomplete="new-password" [(ngModel)]="again" /></label>
        <small>At least 8 characters, with letters and a number.</small>
        <div class="row">
          <button type="submit" class="primary" [disabled]="busy() || !current || !next || !again">{{ busy() ? 'Saving…' : 'Save password' }}</button>
          <button type="button" class="ghost" *ngIf="forced()" (click)="signOut()">Sign out</button>
          <a class="act" *ngIf="!forced()" routerLink="/profile">Cancel</a>
        </div>
      </form>
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
export class ChangePasswordComponent {
  private auth = inject(AuthService);
  private passwords = inject(PasswordService);
  private router = inject(Router);

  current = '';
  next = '';
  again = '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  forced() { return !!this.auth.userContext()?.mustChangePassword; }

  save() {
    if (this.busy()) return;
    const problem = passwordProblem(this.next, this.again) ?? (this.next === this.current ? 'Choose a password different from the current one.' : null);
    if (problem) { this.error.set(problem); return; }
    this.busy.set(true); this.error.set(null);
    this.passwords.change(this.current, this.next).subscribe({
      next: () => { this.busy.set(false); this.router.navigate([this.auth.userContext()?.role === Role.StoreStaff ? '/today' : '/dashboard']); },
      error: e => { this.busy.set(false); this.error.set(errorText(e, 'Could not change the password.')); },
    });
  }

  signOut() { this.auth.logout(); this.router.navigate(['/login']); }
}
