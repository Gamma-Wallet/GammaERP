import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { text } from '../core/text';
import { formatNumber } from './format';
import { Icon } from './icon';

export interface Chip<T = string> {
  value: T;
  label: string;
  /** How many rows it holds, shown beside the label. */
  count?: number;
}

/** The chips picked after a tap: one (or none) when single, else the chip added or taken away. */
export function toggleChip<T>(chosen: readonly T[], value: T, multiple: boolean): T[] {
  if (chosen.includes(value)) {
    return chosen.filter(v => v !== value);
  }
  return multiple ? [...chosen, value] : [value];
}

/**
 * The filter chips above a table, in one row: [(chosen)] holds the values picked; none picked means no filter ("All"). [multiple]="false"
 * lets one at a time.
 */
@Component({
  selector: 'ui-chips',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chips" role="group" [attr.aria-label]="ariaLabel()">
      @if (showAll()) {
        <button type="button" class="chip" [attr.aria-pressed]="!chosen().length" (click)="chosen.set([])">{{ text.ui.all }}</button>
      }
      @for (chip of chips(); track $index) {
        <button type="button" class="chip" [attr.aria-pressed]="chosen().includes(chip.value)" (click)="tap(chip.value)">
          @if (chosen().includes(chip.value)) { <ui-icon name="check" [size]="14" /> }
          {{ chip.label }}
          @if (chip.count !== undefined) { <span class="count figures">{{ format(chip.count) }}</span> }
        </button>
      }
      @if (clearable()) {
        <button type="button" class="clear" (click)="chosen.set([])">{{ text.ui.clear }}</button>
      }
    </div>
  `,
  styles: `
    .chips { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2); }
    .chip {
      display: inline-flex; align-items: center; gap: var(--space-1); height: 30px; padding: 0 var(--space-3);
      border: 1px solid var(--line-strong); border-radius: var(--radius-pill); background: var(--surface); color: var(--ink-2);
      font-size: var(--text-s); cursor: pointer;
    }
    .chip:hover { background: var(--hover); }
    .chip[aria-pressed='true'] { border-color: var(--ink-2); background: var(--selected); color: var(--ink); font-weight: 500; }
    .count { color: var(--muted); }
    .chip[aria-pressed='true'] .count { color: var(--ink-2); }
    .clear { border: 0; background: none; color: var(--link); font-size: var(--text-s); cursor: pointer; }
  `,
})
export class Chips<T = string> {
  protected readonly text = text;
  readonly chips = input<readonly Chip<T>[]>([]);
  readonly chosen = model<T[]>([]);
  readonly multiple = input(true);
  /** A first "All" chip that clears the filter. */
  readonly showAll = input(false);
  readonly ariaLabel = input<string | null>(null);
  protected readonly clearable = computed(() => !this.showAll() && this.multiple() && this.chosen().length > 1);

  protected tap(value: T): void {
    this.chosen.set(toggleChip(this.chosen(), value, this.multiple()));
  }

  protected format(count: number): string {
    return formatNumber(count, 0);
  }
}
