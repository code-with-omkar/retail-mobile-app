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
    .select-wrapper {
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

    .select-field {
      padding: 10px 11px;
      border: 1px solid #e2e8e3;
      border-radius: 6px;
      font-size: 11px;
      background: #fff;
      color: #172523;
      cursor: pointer;
      transition: border-color 0.2s;
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='8' viewBox='0 0 12 8'%3E%3Cpath fill='%23172523' d='M1 1l5 5 5-5'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 10px center;
      padding-right: 32px;
    }

    .select-field:focus {
      outline: none;
      border-color: #6c9d1e;
      box-shadow: 0 0 0 2px rgba(108, 157, 30, 0.1);
    }

    .select-field:disabled {
      background-color: #f6f8f6;
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
