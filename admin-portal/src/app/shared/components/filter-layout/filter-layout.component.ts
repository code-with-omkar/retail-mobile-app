import { Component, EventEmitter, HostBinding, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FilterGroup, Selection } from './filter-state';

const MINIMISED_KEY = 'qc-filters-minimised';
/** A group longer than this gets its own search box. */
export const SEARCH_FROM = 9;

/**
 * The split screen of the list screens: the filters on the left (30%), the screen's body on the right (70%). The panel scrolls on its own,
 * starts closed on every screen, and on a narrow window opens from a Filters button. Put a search box in with the `filterSearch` attribute.
 */
@Component({
  selector: 'app-filter-layout',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button type="button" class="opener" (click)="open.set(!open())" [attr.aria-expanded]="open()">{{ open() ? 'Close filters' : 'Filters' }}<span class="n" *ngIf="chosen()">{{ chosen() }}</span></button>
    <button type="button" class="show" *ngIf="hidden()" (click)="toggleHidden()">Filters<span class="n" *ngIf="chosen()">{{ chosen() }}</span></button>

    <aside class="panel" [class.open]="open()" [class.hidden]="hidden()" aria-label="Filters">
      <div class="top">
        <strong>Filters</strong>
        <button type="button" class="link" (click)="clearAll()">Clear all</button>
        <button type="button" class="link hide" (click)="toggleHidden()">Hide</button>
      </div>
      <div class="search"><ng-content select="[filterSearch]"></ng-content></div>
      <button type="button" class="all-groups" *ngIf="groups.length > 1" (click)="minimiseAll()" [attr.title]="allMinimised() ? 'Expand all' : 'Minimize all'" [attr.aria-label]="allMinimised() ? 'Expand all' : 'Minimize all'">
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="allMinimised() ? 'M7 9l5-5 5 5M7 15l5 5 5-5' : 'M7 4l5 5 5-5M7 20l5-5 5 5'"/></svg>
      </button>

      <fieldset *ngFor="let group of groups; trackBy: byKey">
        <legend><button type="button" class="fold" [attr.aria-expanded]="!isMinimised(group)" (click)="minimise(group)"><span>{{ group.label }}</span><small *ngIf="isMinimised(group)">{{ summary(group) }}</small><i aria-hidden="true">{{ isMinimised(group) ? '+' : '−' }}</i></button></legend>
        <ng-container *ngIf="!isMinimised(group)">
        <input type="search" class="find" *ngIf="group.options.length >= searchFrom" [attr.aria-label]="'Find in ' + group.label" [placeholder]="'Find ' + group.label.toLowerCase()" [value]="finding()[group.key] ?? ''" (input)="find(group.key, $any($event.target).value)" />
        <label class="option" *ngIf="!group.required" [class.on]="!ticked(group).length">
          <input [type]="group.single ? 'radio' : 'checkbox'" [name]="'f-' + group.key" [checked]="!ticked(group).length" (change)="all(group)" />
          <span class="name">All</span>
        </label>
        <label class="option" *ngFor="let o of shownOptions(group); trackBy: byValue" [class.on]="ticked(group).includes(o.value)">
          <input [type]="group.single ? 'radio' : 'checkbox'" [name]="'f-' + group.key" [value]="o.value" [checked]="ticked(group).includes(o.value)" (change)="pick(group, o.value)" />
          <span class="name">{{ o.label }}</span>
          <span class="count" *ngIf="o.count !== undefined">{{ o.count }}</span>
        </label>
        <p class="none" *ngIf="!shownOptions(group).length">Nothing matches.</p>
        </ng-container>
      </fieldset>
    </aside>

    <div class="body"><ng-content></ng-content></div>
  `,
  styles: [`
    :host{display:grid;grid-template-columns:minmax(0,30fr) minmax(0,70fr);gap:16px;align-items:start;position:relative}
    :host.collapsed{grid-template-columns:minmax(0,1fr)}
    :host.fill{flex:1;min-height:0;grid-template-rows:minmax(0,1fr);align-items:stretch}
    :host.fill.collapsed{grid-template-rows:auto minmax(0,1fr)}
    .panel{background:#fff;border:1px solid var(--line);border-radius:18px;padding:12px;overflow-y:auto;max-height:calc(100vh - 190px);min-height:0;box-sizing:border-box;scrollbar-width:thin;scrollbar-color:#B9B2D9 transparent;display:flex;flex-direction:column;gap:12px;position:sticky;top:0}
    :host.fill .panel{max-height:100%;position:static}
    .panel>*{flex-shrink:0}
    .panel.hidden{display:none}
    .body{min-width:0;min-height:0;display:flex;flex-direction:column;gap:12px}
    .top{display:flex;align-items:center;gap:10px;font-size:13px}
    .top strong{flex:1}
    .link{border:0;background:transparent;font:800 11px inherit;color:#0B7A3E;cursor:pointer;padding:4px}
    .search:empty{display:none}
    ::ng-deep app-filter-layout [filterSearch],.find{width:100%;box-sizing:border-box;border:1.5px solid #D9D7DF;border-radius:12px;padding:8px 10px;font:inherit;font-size:12px;background:#fff;color:var(--ink);min-width:0}
    fieldset{border:0;margin:0;padding:0;display:flex;flex-direction:column;gap:2px;min-width:0}
    legend{width:100%;float:left}
    .fold{display:flex;align-items:center;gap:8px;width:100%;border:0;background:var(--card);border-radius:10px;padding:8px 10px;cursor:pointer;font:inherit;text-transform:inherit;letter-spacing:inherit;color:inherit;text-align:left}
    .fold span{flex:1}
    .fold small{text-transform:none;letter-spacing:0;font-size:11px;max-width:55%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .fold i{font-style:normal;font-size:15px;width:14px;text-align:center}
    .all-groups{align-self:flex-end;width:32px;height:32px;display:grid;place-items:center;border:1px solid var(--line);border-radius:50%;background:#fff;color:var(--ink);cursor:pointer;padding:0}
    .all-groups:hover{background:#FFF6C2}
    legend{padding:0 0 4px;font-size:10px;font-weight:800;letter-spacing:.5px;text-transform:uppercase;color:var(--muted)}
    .find{margin-bottom:4px}
    .option{display:flex;align-items:center;gap:8px;padding:7px 10px;border-radius:12px;font-size:12px;cursor:pointer;color:var(--ink);min-height:20px}
    .option:hover{background:var(--card)}
    .option.on{background:#FFF6C2;font-weight:800}
    .option input{margin:0;accent-color:#0B7A3E;flex-shrink:0}
    .name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .count{font-size:11px;color:var(--muted);font-weight:700}
    .none{margin:0;padding:4px 10px;font-size:11px;color:var(--muted)}
    .opener,.show{border:1px solid var(--line);border-radius:999px;background:#fff;color:var(--ink);font:800 11px inherit;padding:8px 14px;cursor:pointer;justify-self:start;align-self:start}
    .opener{display:none}
    .n{margin-left:6px;background:var(--yellow);border-radius:999px;padding:1px 7px}
    @media(max-width:900px){
      :host,:host.fill,:host.fill.collapsed{grid-template-columns:minmax(0,1fr);grid-template-rows:auto;align-items:start}
      :host.fill{grid-template-rows:auto minmax(0,1fr)}
      :host.fill:has(.panel.open){grid-template-rows:auto auto minmax(0,1fr)}
      .opener{display:inline-block}
      .show,.hide{display:none}
      .panel,.panel.hidden{display:none}
      .panel.open{display:flex;position:static;max-height:50vh}
    }
  `],
})
export class FilterLayoutComponent {
  @Input() groups: FilterGroup[] = [];
  @Input() selected: Selection = {};
  /** Take the rest of a column layout, so the panel and the list each scroll on their own. */
  @Input() @HostBinding('class.fill') fill = false;
  @Output() selectedChange = new EventEmitter<Selection>();
  /** "Clear all" was pressed: the screen goes back to its defaults and empties its search. */
  @Output() cleared = new EventEmitter<void>();

  readonly searchFrom = SEARCH_FROM;
  readonly open = signal(false);
  /** Closed each time a screen opens; opening it here changes this screen only. */
  readonly hidden = signal(true);
  readonly finding = signal<Record<string, string>>({});
  @HostBinding('class.collapsed') get collapsed() { return this.hidden(); }

  byKey = (_: number, g: FilterGroup) => g.key;
  byValue = (_: number, o: { value: string }) => o.value;

  ticked(group: FilterGroup): string[] { return this.selected[group.key] ?? []; }
  /** How many groups narrow the list (a required choice does not count). */
  chosen() { return this.groups.filter(g => !g.required && this.ticked(g).length).length; }

  shownOptions(group: FilterGroup) {
    const text = (this.finding()[group.key] ?? '').trim().toLowerCase();
    return text ? group.options.filter(o => o.label.toLowerCase().includes(text) || this.ticked(group).includes(o.value)) : group.options;
  }
  find(key: string, text: string) { this.finding.set({ ...this.finding(), [key]: text }); }

  all(group: FilterGroup) { this.selectedChange.emit({ ...this.selected, [group.key]: [] }); }

  pick(group: FilterGroup, value: string) {
    const now = this.ticked(group);
    const next = group.single ? [value] : now.includes(value) ? now.filter(v => v !== value) : [...now, value];
    this.selectedChange.emit({ ...this.selected, [group.key]: next });
  }

  clearAll() { this.finding.set({}); this.cleared.emit(); }

  readonly minimised = signal<ReadonlySet<string>>(readMinimised());
  isMinimised(group: FilterGroup) { return this.minimised().has(group.key); }
  allMinimised() { return this.groups.every(g => this.isMinimised(g)); }
  /** What a minimised group is set to. */
  summary(group: FilterGroup) {
    const on = group.options.filter(o => this.ticked(group).includes(o.value));
    return on.length === 0 ? 'All' : on.length === 1 ? on[0].label : on.length + ' chosen';
  }
  minimise(group: FilterGroup) {
    const next = new Set(this.minimised());
    if (!next.delete(group.key)) next.add(group.key);
    this.keepMinimised(next);
  }
  minimiseAll() {
    const next = new Set(this.minimised()), open = this.allMinimised();
    for (const g of this.groups) { if (open) next.delete(g.key); else next.add(g.key); }
    this.keepMinimised(next);
  }
  private keepMinimised(next: Set<string>) {
    this.minimised.set(next);
    try { localStorage.setItem(MINIMISED_KEY, JSON.stringify([...next])); } catch { /* remembered until reload */ }
  }

  toggleHidden() {
    this.hidden.set(!this.hidden());
  }
}

function readMinimised(): ReadonlySet<string> {
  try { return new Set<string>(JSON.parse(localStorage.getItem(MINIMISED_KEY) ?? '[]')); } catch { return new Set<string>(); }
}
