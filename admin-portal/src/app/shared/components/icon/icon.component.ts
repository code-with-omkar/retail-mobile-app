import { Component, Input } from '@angular/core';

/** Rounded line icons drawn in a 24 x 24 box, as in the customer app. Each is one SVG path. */
export const ICONS: Record<string, string> = {
  home: 'M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10',
  today: 'M5 5h14a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V7a2 2 0 012-2zM16 3v4M8 3v4M3 10h18M9 15l2 2 4-4',
  orders: 'M6 3h12v18l-3-2-3 2-3-2-3 2V3zM9 8h6M9 12h6M9 16h3',
  check: 'M12 21a9 9 0 100-18 9 9 0 000 18zM8 12l3 3 5-6',
  bell: 'M6 9a6 6 0 0112 0c0 6 2 7 2 8H4c0-1 2-2 2-8zM10 21h4',
  box: 'M12 3l9 5v8l-9 5-9-5V8l9-5zM3 8l9 5 9-5M12 13v8',
  grid: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
  layers: 'M12 3l9 5-9 5-9-5 9-5zM3 13l9 5 9-5',
  store: 'M4 10v10h16V10M3 10l2-6h14l2 6a3 3 0 01-6 0 3 3 0 01-6 0 3 3 0 01-6 0zM10 20v-5h4v5',
  tag: 'M3 12V4h8l10 10-7 7L3 12zM8 8h.01',
  image: 'M5 4h14a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V6a2 2 0 012-2zM8.5 10a1.5 1.5 0 100-3 1.5 1.5 0 000 3zM21 15l-5-5L5 20',
  ticket: 'M3 7h18v4a2 2 0 000 4v3H3v-3a2 2 0 000-4V7zM14 7v11',
  up: 'M12 20V5M5 12l7-7 7 7',
  chart: 'M4 4v16h16M8 16v-4M13 16V8M18 16v-6',
  pulse: 'M3 12h4l3-8 4 16 3-8h4',
  clock: 'M12 21a9 9 0 100-18 9 9 0 000 18zM12 7v5l3 2',
  percent: 'M19 5L5 19M7.5 10a2.5 2.5 0 100-5 2.5 2.5 0 000 5zM16.5 19a2.5 2.5 0 100-5 2.5 2.5 0 000 5z',
  users: 'M9 11a4 4 0 100-8 4 4 0 000 8zM2 21a7 7 0 0114 0M16 3a4 4 0 010 8M18 14a6 6 0 014 7',
  shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3z',
  key: 'M8 19a4 4 0 100-8 4 4 0 000 8zM11 12l9-9M17 6l3 3M14 9l3 3',
  upload: 'M12 16V4M7 9l5-5 5 5M4 16v4h16v-4',
  close: 'M6 6l12 12M18 6L6 18',
};

@Component({
  selector: 'app-icon',
  standalone: true,
  template: `<svg viewBox="0 0 24 24" [attr.width]="size" [attr.height]="size" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="path" /></svg>`,
  styles: [':host{display:inline-flex;flex-shrink:0}'],
})
export class IconComponent {
  @Input() name = '';
  @Input() size = 18;
  get path(): string { return ICONS[this.name] ?? ICONS['grid']; }
}
