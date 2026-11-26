import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

const typeName = (type: string) => type.split('/')[1].toUpperCase().replace('JPEG', 'JPG');

/** Why a file cannot be used, or null when it can. */
export function fileProblem(file: { type: string; size: number }, types: string[], maxBytes: number): string | null {
  if (types.length && !types.includes(file.type)) return 'Choose a ' + types.map(typeName).join(', ') + ' file.';
  if (file.size > maxBytes) return `The file is too large. The limit is ${Math.round(maxBytes / (1024 * 1024) * 10) / 10} MB.`;
  return file.size === 0 ? 'The file is empty.' : null;
}

export const sizeText = (bytes: number) => (bytes >= 1024 * 1024 ? (bytes / (1024 * 1024)).toFixed(1) + ' MB' : Math.max(1, Math.round(bytes / 1024)) + ' KB');

/**
 * The upload card used wherever a picture or file is added: drop or choose it, see it, replace or remove it. The card checks type and size
 * and hands a good file to the screen, which uploads it and passes the saved picture back as `previewUrl`.
 */
@Component({
  selector: 'app-file-upload',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="card">
      <div class="head" *ngIf="label"><strong>{{ label }}</strong><small *ngIf="hint">{{ hint }}</small></div>
      <div class="body" [class.has]="previewUrl">
        <figure *ngIf="previewUrl">
          <img [src]="previewUrl" [alt]="label || 'Picture'" />
          <button type="button" class="remove" *ngIf="removable" [disabled]="busy" (click)="removed.emit()" aria-label="Remove picture" title="Remove"><app-icon name="close" [size]="14"></app-icon></button>
        </figure>
        <label class="drop" [class.over]="over()" [class.busy]="busy" (dragover)="drag($event, true)" (dragleave)="drag($event, false)" (drop)="dropped($event)">
          <span class="bubble" [class.spin]="busy"><app-icon name="upload" [size]="22"></app-icon></span>
          <span class="title">{{ busy ? 'Uploading…' : previewUrl ? 'Drop a new file to replace' : 'Drag and drop here' }}</span>
          <span class="choose">{{ previewUrl ? 'Replace file' : 'Choose file' }}</span>
          <span class="chips"><span *ngFor="let t of typeNames">{{ t }}</span><span>up to {{ limit }}</span></span>
          <input type="file" [attr.accept]="accept ?? types.join(',')" [disabled]="busy" (change)="picked($event)" />
        </label>
      </div>
      <p class="chosen" *ngIf="chosen() as c"><app-icon name="check" [size]="15"></app-icon>{{ c.name }} <small>{{ c.size }}</small></p>
      <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
    </div>
  `,
  styles: [`
    .card{display:grid;gap:10px;color:var(--ink)}
    .head{display:grid;gap:2px;font-size:12px}
    small{color:var(--muted);font-size:11px}
    .body{display:grid;gap:12px}
    .body.has{grid-template-columns:150px 1fr}
    figure{margin:0;position:relative;border:1px solid var(--line);border-radius:16px;background:#fff;display:grid;place-items:center;min-height:130px;overflow:hidden}
    figure img{max-width:100%;max-height:160px;object-fit:contain}
    .remove{position:absolute;top:6px;right:6px;width:26px;height:26px;border-radius:50%;border:0;background:#fff;color:var(--danger);box-shadow:0 2px 8px rgba(20,17,24,.18);display:grid;place-items:center;cursor:pointer}
    .drop{position:relative;display:grid;justify-items:center;align-content:center;gap:8px;min-height:130px;padding:18px 14px;border:2px dashed #C9C0F0;border-radius:18px;background:linear-gradient(160deg,#F7F5FF 0%,#FFF9E6 100%);cursor:pointer;text-align:center;transition:border-color .15s ease,transform .15s ease,box-shadow .15s ease}
    .drop:hover,.drop.over{border-color:var(--yellow);box-shadow:0 10px 26px rgba(40,24,90,.12)}
    .drop.over{transform:scale(1.015)}
    .drop.busy{cursor:progress;opacity:.7}
    .drop:focus-within{outline:2px solid var(--ink);outline-offset:2px}
    .bubble{width:46px;height:46px;border-radius:50%;background:var(--yellow);display:grid;place-items:center;box-shadow:0 8px 18px rgba(255,212,0,.45)}
    .bubble.spin{animation:bob .8s ease-in-out infinite alternate}
    @keyframes bob{from{transform:translateY(2px)}to{transform:translateY(-4px)}}
    .title{font-size:13px;font-weight:800}
    .choose{border:1px solid var(--ink);border-radius:999px;padding:6px 16px;font-size:11px;font-weight:800;background:#fff}
    .chips{display:flex;gap:6px;flex-wrap:wrap;justify-content:center}
    .chips span{background:#fff;border:1px solid var(--line);border-radius:999px;padding:2px 9px;font-size:10px;font-weight:700;color:var(--muted)}
    input{position:absolute;opacity:0;width:1px;height:1px}
    .chosen{display:flex;gap:8px;align-items:center;margin:0;font-size:12px;font-weight:700;color:var(--success);word-break:break-all}
    .error{margin:0;color:var(--danger);font-weight:700;font-size:12px}
    @media(max-width:520px){.body.has{grid-template-columns:1fr}}
    @media(prefers-reduced-motion:reduce){.drop,.bubble.spin{transition:none;animation:none}}
  `],
})
export class FileUploadComponent {
  @Input() label = 'Picture';
  @Input() hint = '';
  @Input() types: string[] = IMAGE_TYPES;
  /** What the file picker offers, when it differs from the checked types (a spreadsheet has several type names). */
  @Input() accept: string | null = null;
  @Input() maxBytes = 2 * 1024 * 1024;
  @Input() previewUrl: string | null = null;
  @Input() busy = false;
  @Input() removable = true;
  @Output() fileSelected = new EventEmitter<File>();
  @Output() removed = new EventEmitter<void>();

  readonly over = signal(false);
  readonly error = signal<string | null>(null);
  readonly chosen = signal<{ name: string; size: string } | null>(null);

  get typeNames() { return this.types.map(typeName); }
  get limit() { return sizeText(this.maxBytes).replace('.0', ''); }

  picked(event: Event) {
    const input = event.target as HTMLInputElement;
    this.take(input.files?.[0]);
    input.value = ''; // so the same file can be chosen again
  }

  drag(event: Event, over: boolean) { event.preventDefault(); this.over.set(over); }

  dropped(event: DragEvent) {
    this.drag(event, false);
    if (!this.busy) this.take(event.dataTransfer?.files?.[0]);
  }

  take(file: File | undefined) {
    if (!file) return;
    const problem = fileProblem(file, this.types, this.maxBytes);
    this.error.set(problem);
    this.chosen.set(problem ? null : { name: file.name, size: sizeText(file.size) });
    if (!problem) this.fileSelected.emit(file);
  }
}
