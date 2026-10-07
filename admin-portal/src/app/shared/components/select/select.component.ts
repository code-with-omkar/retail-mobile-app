import { Component, Input, Output, EventEmitter, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface SelectOption {
  value: any;
  label: string;
  disabled?: boolean;
}

@Component({
  selector: 'app-select',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="select-wrapper">
      <label *ngIf="label" [for]="id">{{ label }}</label>
      <select
        [id]="id"
        [value]="value"
        [disabled]="disabled"
        [attr.aria-label]="ariaLabel"
        (change)="onSelectChange($event)"
        (blur)="onBlur()"
        class="select-field"
      >
        <option *ngIf="placeholder" value="">{{ placeholder }}</option>
        <option *ngFor="let opt of options" [value]="opt.value" [disabled]="opt.disabled">
          {{ opt.label }}
        </option>
      </select>
      <small *ngIf="error" class="error-text">{{ error }}</small>
    </div>
  `,
  styles: [`
    .select-wrapper{display:flex;flex-direction:column;gap:6px;font-family:'Inter',sans-serif}
    label{font-size:11px;font-weight:700;color:#E9E4FF;text-transform:uppercase;letter-spacing:.5px}
    .select-field{padding:12px 36px 12px 14px;border:2px solid #FFD400;border-radius:14px;font-size:12px;font-family:'Inter',sans-serif;background-color:#EAE4FF;color:#141118;cursor:pointer;transition:box-shadow .2s,border-color .2s;appearance:none;
      background-image:url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='8' viewBox='0 0 12 8'%3E%3Cpath fill='%23141118' d='M1 1l5 5 5-5'/%3E%3C/svg%3E");
      background-repeat:no-repeat;background-position:right 14px center}
    .select-field option{background:#EAE4FF;color:#141118}
    .select-field:focus{outline:none;box-shadow:0 0 0 3px rgba(255,212,0,.3)}
    .select-field:disabled{background-color:#D9D3F5;border-color:#C9C2F0;color:#5E5A66;cursor:not-allowed}
    .error-text{color:#FB1A8E;font-size:10px;font-weight:700}
  `],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => SelectComponent),
      multi: true,
    },
  ],
})
export class SelectComponent implements ControlValueAccessor {
  @Input() id = `select-${Math.random().toString(36).substr(2, 9)}`;
  @Input() label?: string;
  @Input() options: SelectOption[] = [];
  @Input() placeholder = '';
  @Input() disabled = false;
  @Input() error?: string;
  @Input() ariaLabel?: string;
  @Output() onChange = new EventEmitter<any>();

  @Input() value: any = '';
  private onChange$: (val: any) => void = () => {};
  private onTouched$ = () => {};

  onSelectChange(event: Event): void {
    const target = event.target as HTMLSelectElement;
    this.value = target.value;
    this.onChange$(this.value);
    this.onChange.emit(this.value);
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
