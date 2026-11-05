import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { text } from '../core/text';
import { formatChange } from './format';
import { Icon } from './icon';

export type StatusKind = 'good' | 'warning' | 'serious' | 'critical' | 'info' | 'neutral';

/** A state shown as an icon and a word, never by colour alone: "Posted", "On hold", "Reversed". The word is projected. */
@Component({
  selector: 'ui-status',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'kind()' },
  template: `<ui-icon [name]="kind() === 'neutral' ? 'circle' : kind()" [size]="14" /><span><ng-content /></span>`,
  styles: `
    :host {
      display: inline-flex; align-items: center; gap: var(--space-1); height: 22px; padding: 0 var(--space-2);
      border-radius: var(--radius-pill); background: var(--surface-2); color: var(--ink-2); font-size: var(--text-xs); font-weight: 500;
      white-space: nowrap;
    }
    :host(.good) { color: var(--good); background: var(--good-bg); }
    :host(.warning) { color: var(--warning); background: var(--warning-bg); }
    :host(.serious) { color: var(--serious); background: var(--serious-bg); }
    :host(.critical) { color: var(--critical); background: var(--critical-bg); }
    :host(.info) { color: var(--link); background: var(--info-bg); }
  `,
})
export class Status {
  readonly kind = input<StatusKind>('neutral');
}

/**
 * One headline figure (a KPI): a label, the value already formatted, and how it changed against an earlier period. Whether a rise is good
 * is the screen's to say ([upIsGood]="false" for costs); the change carries an arrow and its sign, so the colour is never the only cue.
 */
@Component({
  selector: 'ui-kpi',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="label">{{ label() }}</p>
    <p class="value">{{ value() }}@if (unit()) { <span class="unit">{{ unit() }}</span> }</p>
    @if (change() !== null) {
      <p class="change" [class]="tone()">
        <ui-icon [name]="change()! > 0 ? 'arrow-up' : change()! < 0 ? 'arrow-down' : 'minus'" [size]="14" />
        <span class="figures">{{ changeText() }}</span>
        @if (comparedTo()) { <span class="muted">{{ text.ui.comparedTo }} {{ comparedTo() }}</span> }
      </p>
    } @else if (caption()) {
      <p class="change muted">{{ caption() }}</p>
    } @else {
      <p class="change" aria-hidden="true"></p>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-1); padding: var(--space-4); border: 1px solid var(--line); border-radius: var(--radius-l);
      background: var(--surface); }
    p { margin: 0; }
    .label { color: var(--muted); font-size: var(--text-s); }
    .value { font: 500 var(--kpi-value) / 1.15 var(--font-text); letter-spacing: -0.015em; font-variant-numeric: tabular-nums; }
    .unit { margin-left: var(--space-1); color: var(--muted); font-size: var(--text-m); font-weight: 400; }
    .change { display: flex; align-items: center; gap: var(--space-1); min-height: calc(var(--text-s) * 1.45); font-size: var(--text-s); }
    .change.up { color: var(--good); }
    .change.down { color: var(--critical); }
    .change.flat { color: var(--muted); }
  `,
})
export class Kpi {
  protected readonly text = text;
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly unit = input<string | null>(null);
  /** The change in percent; null: no comparison. */
  readonly change = input<number | null>(null);
  readonly comparedTo = input<string | null>(null);
  readonly upIsGood = input(true);
  readonly caption = input<string | null>(null);
  protected readonly changeText = computed(() => formatChange(this.change() ?? 0));
  /** "up" means good here, whatever the direction, so costs that fall show as good. */
  protected readonly tone = computed(() => {
    const change = this.change() ?? 0;
    if (change === 0) {
      return 'flat';
    }
    return change > 0 === this.upIsGood() ? 'up' : 'down';
  });
}
