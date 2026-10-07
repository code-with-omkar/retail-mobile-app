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
        class="input-field" [class.invalid]="!!error"
      />
      <small *ngIf="error" class="error-text">{{ error }}</small>
    </div>
  `,
  styles: [`
    .input-wrapper{display:flex;flex-direction:column;gap:6px;font-family:'Inter',sans-serif}
    label{font-size:11px;font-weight:700;color:#E9E4FF;text-transform:uppercase;letter-spacing:.5px}
    .input-field{padding:12px 14px;border:2px solid #FFD400;border-radius:14px;font-size:12px;font-family:'Inter',sans-serif;background:#EAE4FF;color:#141118;transition:box-shadow .2s,border-color .2s}
    .input-field::placeholder{color:#5E5A66}
    .input-field:focus{outline:none;box-shadow:0 0 0 3px rgba(255,212,0,.3)}
    .input-field.invalid{border-color:#FB1A8E}
    .input-field.invalid:focus{box-shadow:0 0 0 3px rgba(251,26,142,.3)}
    .input-field:disabled{background:#D9D3F5;border-color:#C9C2F0;color:#5E5A66;cursor:not-allowed}
    .error-text{color:#FB1A8E;font-size:10px;font-weight:700}
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
