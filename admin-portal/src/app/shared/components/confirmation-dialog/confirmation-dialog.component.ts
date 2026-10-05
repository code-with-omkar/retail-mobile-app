import { Component, Input, Output, EventEmitter } from '@angular/core';
import { DialogComponent } from '../dialog/dialog.component';

@Component({
  selector: 'app-confirmation-dialog',
  standalone: true,
  imports: [DialogComponent],
  template: `
    <app-dialog
      [isOpen]="isOpen"
      [title]="title"
      [showFooter]="true"
      [actionLabel]="confirmLabel"
      [actionVariant]="variant"
      [actionLoading]="isConfirming"
      (onClose)="onCancel.emit()"
      (onAction)="onConfirm.emit()"
    >
      <p class="dialog-message">{{ message }}</p>
    </app-dialog>
  `,
  styles: [`
    .dialog-message {
      margin: 0;
      color: #82908b;
      font-size: 12px;
      line-height: 1.6;
    }
  `],
})
export class ConfirmationDialogComponent {
  @Input() isOpen = false;
  @Input() title = 'Confirm';
  @Input() message = 'Are you sure?';
  @Input() confirmLabel = 'Confirm';
  @Input() variant: 'primary' | 'danger' = 'primary';
  @Input() isConfirming = false;
  @Output() onConfirm = new EventEmitter<void>();
  @Output() onCancel = new EventEmitter<void>();
}
