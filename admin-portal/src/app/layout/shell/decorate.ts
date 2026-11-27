import { Directive, ElementRef, OnDestroy, OnInit, inject } from '@angular/core';

/** Outline icons (24 x 24) drawn through a CSS mask, so they take the button's own text colour. */
const PATHS: Record<string, string> = {
  check: 'M20 6 9 17l-5-5',
  checks: 'M18 6 7 17l-5-5M22 10l-7.5 7.5L13 16',
  cross: 'M18 6 6 18M6 6l12 12',
  circleCheck: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM8 12l3 3 5-6',
  circleCross: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM15 9l-6 6M9 9l6 6',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 6v6l4 2',
  box: 'M21 8 12 3 3 8v8l9 5 9-5zM3 8l9 5 9-5M12 13v8',
  bag: 'M6 7h12l1 14H5zM9 10V6a3 3 0 0 1 6 0v4',
  truck: 'M2 6h11v11H2zM13 10h5l3 3v4h-8M6 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM17 20a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  edit: 'M4 20h4L19 9l-4-4L4 16zM13.5 6.5l4 4',
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  key: 'M8 20a5 5 0 1 0 0-10 5 5 0 0 0 0 10zM11.5 11.5 20 3M16 7l3 3M14 9l2 2',
  power: 'M12 3v9M6.3 7.3a8 8 0 1 0 11.4 0',
  trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
  stop: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM9 9h6v6H9z',
  ban: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM5 5l14 14',
  rupee: 'M7 4h11M7 9h11M7 4c7 0 7 10 0 10l8 7',
  image: 'M3 4h18v16H3zM8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM21 16l-5-5L5 20',
  undo: 'M9 14 4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3',
  save: 'M5 3h11l3 3v15H5zM8 3v6h7M8 21v-7h8v7',
  pin: 'M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11zM12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  external: 'M14 4h6v6M20 4l-9 9M19 14v6H4V5h6',
  mail: 'M3 5h18v14H3zM3 7l9 6 9-6',
  cash: 'M2 6h20v12H2zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM6 12h.01M18 12h.01',
  shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.500-1-8-4-8-9V6z',
  gavel: 'M14 4l6 6M12 6l6 6M13 9l-9 9 2 2 9-9M4 22h12',
  send: 'M22 2 11 13M22 2l-7 20-4-9-9-4z',
  hourglass: 'M6 3h12M6 21h12M8 3c0 5 8 6 8 9s-8 4-8 9M16 3c0 5-8 6-8 9s8 4 8 9',
  pause: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM10 9v6M14 9v6',
  up: 'M12 19V5M5 12l7-7 7 7',
  down: 'M12 5v14M5 12l7 7 7-7',
  plus: 'M12 5v14M5 12h14',
  minus: 'M5 12h14',
  expand: 'M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5',
  shrink: 'M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5',
};

export const iconUrl = (name: string): string =>
  `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='black' stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round'><path d='${PATHS[name]}'/></svg>`)}")`;

/** What a button says, to its icon. The first rule that matches wins. */
const ACTIONS: [RegExp, string][] = [
  [/^accept|^approve/, 'check'], [/^reject|^refuse/, 'cross'], [/^start packing/, 'box'], [/^mark ready/, 'bag'], [/^complete/, 'checks'],
  [/^change items/, 'edit'], [/^cancel/, 'ban'], [/^give refund/, 'rupee'], [/^(give|change)$/, 'truck'], [/^change card/, 'image'],
  [/^(edit|set own)/, 'edit'], [/^view/, 'eye'], [/^decide/, 'gavel'], [/^password/, 'key'], [/^(deactivate|activate|switch o)/, 'power'],
  [/^delete/, 'trash'], [/^end discount/, 'stop'], [/^pay/, 'rupee'], [/^cash paid/, 'cash'], [/^use default/, 'undo'], [/^save/, 'save'],
  [/^open map/, 'pin'], [/^open /, 'external'], [/^mark read/, 'mail'], [/^permissions/, 'shield'], [/^place bid/, 'gavel'], [/^offer to customer/, 'send'],
  [/^add\b/, 'plus'], [/^remove/, 'minus'], [/^replace/, 'undo'],
];

/** A status word, to its icon and colour. */
const STATUSES: Record<string, [string, string]> = {
  new: ['clock', 'wait'], pending: ['clock', 'wait'], open: ['clock', 'wait'], scheduled: ['clock', 'wait'], 'awaiting payment': ['rupee', 'wait'],
  accepted: ['check', 'go'], packing: ['box', 'go'], preparing: ['box', 'go'], ready: ['bag', 'go'], confirmed: ['check', 'go'], 'out for delivery': ['truck', 'go'], outfordelivery: ['truck', 'go'],
  delivered: ['circleCheck', 'done'], completed: ['checks', 'done'], approved: ['circleCheck', 'done'], active: ['circleCheck', 'done'], sent: ['send', 'done'], leading: ['up', 'done'],
  rejected: ['circleCross', 'bad'], cancelled: ['circleCross', 'bad'], refused: ['circleCross', 'bad'], 'used up': ['ban', 'bad'],
  inactive: ['pause', 'off'], off: ['pause', 'off'], 'switched off': ['pause', 'off'], expired: ['hourglass', 'off'], ended: ['hourglass', 'off'], outbid: ['down', 'off'],
};

const clean = (text: string) => text.replace(/[←→＋+]/g, ' ').replace(/\s+/g, ' ').trim().toLowerCase();

export const actionIcon = (text: string): string | null => ACTIONS.find(([pattern]) => pattern.test(clean(text)))?.[1] ?? null;
export const statusIcon = (text: string): [string, string] | null => STATUSES[clean(text).replace(/ \(.*\)$/, '')] ?? null;

const BUTTONS = ['app-data-table tbody button', 'app-data-table tbody a.act', '.table .row button', '.table .vrow button', 'app-stores article button', 'app-stores article a.act', 'app-categories .product button'].join(',');
const BADGES = ['app-data-table tbody .badge', 'app-data-table tbody .status', 'app-data-table tbody .state', 'app-data-table tbody .chip', '.table .row .badge', 'app-stores article .pill'].join(',');
/** The cards that can be minimised to their heading, or maximised over the screen. */
const CARDS = ['.card', 'section.box', 'app-stores article', 'app-data-table .frame', '.detail'].join(',');

function mark(element: HTMLElement, icon: string, label: string, tone?: string) {
  element.dataset['ic'] = icon;
  element.style.setProperty('--ic', iconUrl(icon));
  element.title = label;
  element.setAttribute('aria-label', label);
  if (tone) element.dataset['tone'] = tone;
}

function unmark(element: HTMLElement) {
  if (!element.dataset['ic']) return;
  delete element.dataset['ic'];
  delete element.dataset['tone'];
  element.removeAttribute('title');
  element.removeAttribute('aria-label');
}

function toolButton(kind: 'min' | 'max', card: HTMLElement): HTMLButtonElement {
  const button = card.ownerDocument.createElement('button');
  button.type = 'button';
  button.className = 'card-tool';
  const show = () => {
    const on = card.classList.contains('card-' + kind);
    mark(button, kind === 'min' ? (on ? 'plus' : 'minus') : on ? 'shrink' : 'expand', kind === 'min' ? (on ? 'Show card' : 'Minimize card') : on ? 'Restore card' : 'Maximize card');
  };
  button.addEventListener('click', event => {
    event.stopPropagation();
    const on = card.classList.toggle('card-' + kind);
    if (on) card.classList.remove(kind === 'min' ? 'card-max' : 'card-min');
    card.querySelectorAll<HTMLButtonElement>(':scope > .card-tools > .card-tool').forEach(tool => tool.dispatchEvent(new Event('refresh')));
  });
  button.addEventListener('refresh', show);
  show();
  return button;
}

/** Gives the buttons and statuses of lists their icons (the words stay as the tooltip), and cards their minimise and maximise buttons. */
export function decorate(root: HTMLElement) {
  root.querySelectorAll<HTMLElement>(BUTTONS).forEach(button => {
    if (button.classList.contains('card-tool')) return; // they keep their own
    const text = button.textContent?.trim() ?? '';
    const icon = actionIcon(text);
    if (icon) mark(button, icon, text.replace(/\s+/g, ' '));
    else unmark(button);
  });
  root.querySelectorAll<HTMLElement>(BADGES).forEach(badge => {
    const text = badge.textContent?.trim() ?? '';
    const found = statusIcon(text);
    if (found) mark(badge, found[0], text, found[1]);
    else unmark(badge);
  });
  root.querySelectorAll<HTMLElement>(CARDS).forEach(card => {
    if (card.closest('app-side-panel, app-phone-preview') || card.querySelector(':scope > .card-tools')) return;
    const tools = card.ownerDocument.createElement('span');
    tools.className = 'card-tools';
    card.classList.add('has-tools');
    tools.append(toolButton('min', card), toolButton('max', card));
    card.append(tools);
  });
}

/** Keeps the screen under it decorated as it changes. */
@Directive({ selector: '[appDecorate]', standalone: true })
export class DecorateDirective implements OnInit, OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private observer?: MutationObserver;
  private waiting = false;
  private readonly escape = (event: KeyboardEvent) => {
    if (event.key !== 'Escape') return;
    this.host.querySelectorAll<HTMLElement>('.card-max').forEach(card => card.querySelector<HTMLButtonElement>(':scope > .card-tools > .card-tool:last-child')?.click());
  };

  ngOnInit() {
    const run = () => { this.waiting = false; decorate(this.host); };
    this.observer = new MutationObserver(() => { if (!this.waiting) { this.waiting = true; queueMicrotask(run); } });
    // Attributes are not watched, so marking an element does not call this again.
    this.observer.observe(this.host, { childList: true, subtree: true, characterData: true });
    this.host.ownerDocument.addEventListener('keydown', this.escape);
    run();
  }

  ngOnDestroy() {
    this.observer?.disconnect();
    this.host.ownerDocument.removeEventListener('keydown', this.escape);
  }
}
