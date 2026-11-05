import { ChangeDetectionStrategy, Component, ElementRef, input, model, viewChildren } from '@angular/core';
import { uniqueId } from './controls';

export interface Tab {
  id: string;
  label: string;
  /** A count beside the label (lines, errors). */
  count?: number;
}

/**
 * Tabs: [(active)] is the id of the tab shown. The screen shows the content itself (@switch on active), inside an element with
 * role="tabpanel" and aria-labelledby="tabsId + '-' + id" when it wants the link for screen readers.
 */
@Component({
  selector: 'ui-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tabs" role="tablist" [attr.aria-label]="ariaLabel()">
      @for (tab of tabs(); track tab.id; let i = $index) {
        <button #tabButton type="button" role="tab" class="tab" [id]="tabsId + '-' + tab.id" [attr.aria-selected]="tab.id === active()"
          [tabIndex]="tab.id === active() ? 0 : -1" (click)="active.set(tab.id)" (keydown)="onKey($event, i)">
          {{ tab.label }}
          @if (tab.count !== undefined) { <span class="count">{{ tab.count }}</span> }
        </button>
      }
    </div>
  `,
  styles: `
    .tabs { display: flex; gap: var(--space-4); border-bottom: 1px solid var(--line); overflow-x: auto; }
    .tab {
      display: inline-flex; align-items: center; gap: var(--space-2); height: 40px; margin-bottom: -1px; padding: 0 var(--space-1);
      border: 0; border-bottom: 2px solid transparent; background: none; color: var(--muted); font-weight: 500; white-space: nowrap;
      cursor: pointer;
    }
    .tab:hover { color: var(--ink); }
    .tab[aria-selected='true'] { border-bottom-color: var(--ink); color: var(--ink); }
    .count { padding: 0 6px; border-radius: var(--radius-pill); background: var(--selected); font-size: var(--text-xs); }
  `,
})
export class Tabs {
  readonly tabs = input<readonly Tab[]>([]);
  readonly active = model<string | null>(null);
  readonly ariaLabel = input<string | null>(null);
  readonly tabsId = uniqueId('tabs');
  private readonly buttons = viewChildren<ElementRef<HTMLButtonElement>>('tabButton');

  /** Arrow keys move between tabs and show the one reached, as the WAI-ARIA tabs pattern does. */
  protected onKey(event: KeyboardEvent, index: number): void {
    const count = this.tabs().length;
    const next =
      event.key === 'ArrowRight' ? (index + 1) % count
      : event.key === 'ArrowLeft' ? (index - 1 + count) % count
      : event.key === 'Home' ? 0
      : event.key === 'End' ? count - 1
      : -1;
    if (next < 0) {
      return;
    }
    event.preventDefault();
    this.active.set(this.tabs()[next].id);
    this.buttons()[next]?.nativeElement.focus();
  }
}
