import { Component, Input, Output, EventEmitter, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'app-input',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="input-wrapper">
      <label *ngIf="label" [for]="id">{{ label }}</label>
      <input
        [id]="id"
        [type]="type"
        [placeholder]="placeholder"
        [value]="value"
        [disabled]="disabled"
        [attr.aria-label]="ariaLabel"
        (input)="onInput($event)"
        (blur)="onBlur()"
        (change)="onChange.emit($event)"
        class="input-field"
      />
      <small *ngIf="error" class="error-text">{{ error }}</small>
    </div>
  `,
  styles: [`
    .input-wrapper {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    label {
      font-size: 11px;
      font-weight: 700;
      color: #172523;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .input-field {
      padding: 10px 11px;
      border: 1px solid #e2e8e3;
      border-radius: 6px;
      font-size: 11px;
      background: #fff;
      color: #172523;
      transition: border-color 0.2s;
    }

    .input-field:focus {
      outline: none;
      border-color: #6c9d1e;
      box-shadow: 0 0 0 2px rgba(108, 157, 30, 0.1);
    }

    .input-field:disabled {
      background: #f6f8f6;
      color: #94a19c;
      cursor: not-allowed;
    }

    .error-text {
      color: #cf7c4e;
      font-size: 10px;
    }
  `],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => InputComponent),
      multi: true,
    },
  ],
})
export class InputComponent implements ControlValueAccessor {
  @Input() id = `input-${Math.random().toString(36).substr(2, 9)}`;
  @Input() label?: string;
  @Input() type: 'text' | 'email' | 'password' | 'number' = 'text';
  @Input() placeholder = '';
  @Input() disabled = false;
  @Input() error?: string;
  @Input() ariaLabel?: string;
  @Output() onChange = new EventEmitter<Event>();

  value = '';
  private onChange$: (val: any) => void = () => {};
  private onTouched$ = () => {};

  onInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.value = target.value;
    this.onChange$(this.value);
  }

  onBlur(): void {
    this.onTouched$();
  }

  writeValue(value: any): void {
    this.value = value || '';
  }

  registerOnChange(fn: any): void {
    this.onChange$ = fn;
  }

  registerOnTouched(fn: any): void {
    this.onTouched$ = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }
}
