import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonComponent } from '../button/button.component';

@Component({
  selector: 'app-dialog',
  standalone: true,
  imports: [CommonModule, ButtonComponent],
  template: `
    <div *ngIf="isOpen" class="dialog-overlay" (click)="closeOnBackdrop()">
      <div class="dialog" (click)="$event.stopPropagation()">
        <div class="dialog-header">
          <h2>{{ title }}</h2>
          <button class="close-btn" (click)="onClose.emit()">×</button>
        </div>
        <div class="dialog-content">
          <ng-content></ng-content>
        </div>
        <div class="dialog-actions" *ngIf="showFooter">
          <app-button
            label="Cancel"
            variant="secondary"
            size="sm"
            (onClick)="onClose.emit()"
          ></app-button>
          <app-button
            [label]="actionLabel"
            [variant]="actionVariant"
            size="sm"
            [loading]="actionLoading"
            (onClick)="onAction.emit()"
          ></app-button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .dialog-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .dialog {
      background: #fff;
      border-radius: 10px;
      box-shadow: 0 10px 40px rgba(0, 0, 0, 0.1);
      width: 90%;
      max-width: 500px;
      max-height: 90vh;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
    }

    .dialog-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px;
      border-bottom: 1px solid #e3eae5;
    }

    .dialog-header h2 {
      font-size: 16px;
      font-weight: 700;
      margin: 0;
      color: #172523;
    }

    .close-btn {
      background: none;
      border: none;
      font-size: 24px;
      color: #82908b;
      cursor: pointer;
      padding: 0;
      width: 32px;
      height: 32px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .close-btn:hover {
      color: #172523;
    }

    .dialog-content {
      padding: 20px;
      flex: 1;
    }

    .dialog-actions {
      display: flex;
      gap: 12px;
      justify-content: flex-end;
      padding: 20px;
      border-top: 1px solid #e3eae5;
      background: #f6f8f6;
    }
  `],
})
export class DialogComponent {
  @Input() isOpen = false;
  @Input() title = 'Dialog';
  @Input() showFooter = true;
  @Input() actionLabel = 'Confirm';
  @Input() actionVariant: 'primary' | 'danger' = 'primary';
  @Input() actionLoading = false;
  @Input() closeOnBackdrop = true;
  @Output() onClose = new EventEmitter<void>();
  @Output() onAction = new EventEmitter<void>();

  closeOnBackdrop(): void {
    if (this.closeOnBackdrop) {
      this.onClose.emit();
    }
  }
}
