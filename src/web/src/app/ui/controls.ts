import { ChangeDetectionStrategy, Component, Directive, computed, inject, input, model } from '@angular/core';
import { text } from '../core/text';

let nextId = 0;

/** A unique id for linking a label, a control and its messages. */
export function uniqueId(prefix: string): string {
  return `${prefix}-${++nextId}`;
}

/** Dresses a native button or link: <button uiButton variant="primary" size="touch">. Styles in styles/_controls.scss. */
@Directive({
  selector: 'button[uiButton], a[uiButton]',
  host: {
    class: 'button',
    '[class.primary]': "variant() === 'primary'",
    '[class.quiet]': "variant() === 'quiet'",
    '[class.danger]': "variant() === 'danger'",
    '[class.small]': "size() === 'small'",
    '[class.touch]': "size() === 'touch'",
    '[class.icon-only]': 'iconOnly()',
    '[attr.type]': "type() ?? 'button'",
  },
})
export class Button {
  readonly variant = input<'default' | 'primary' | 'quiet' | 'danger'>('default');
  readonly size = input<'medium' | 'small' | 'touch'>('medium');
  /** A square button with only an icon; give it an aria-label. */
  readonly iconOnly = input(false);
  /** A plain button unless told otherwise, so it never submits a form by accident. */
  readonly type = input<'button' | 'submit' | 'reset' | null>(null);
}

/**
 * A labelled form row: the label, the control (projected), a hint and an error. A control inside picks up the field's id, so the label
 * points at it and screen readers read the hint and the error with it.
 */
@Component({
  selector: 'ui-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (label()) {
      <label class="label" [attr.for]="id" [id]="id + '-label'">
        {{ label() }}@if (required()) { <span class="required" [attr.title]="text.ui.required">*</span> }
      </label>
    }
    <ng-content />
    @if (error()) {
      <p class="error" [id]="id + '-error'">{{ error() }}</p>
    } @else if (hint()) {
      <p class="hint" [id]="id + '-hint'">{{ hint() }}</p>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-1); align-content: start; min-width: 0; }
    .label { color: var(--ink-2); font-size: var(--text-s); font-weight: 500; }
    .required { margin-left: 2px; color: var(--critical); }
    .hint, .error { margin: 0; font-size: var(--text-xs); }
    .hint { color: var(--muted); }
    .error { color: var(--critical); }
  `,
})
export class Field {
  protected readonly text = text;
  readonly label = input<string | null>(null);
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
  readonly required = input(false);
  readonly id = uniqueId('field');
  /** What a control reads out besides its label: the error when there is one, else the hint. */
  readonly describedBy = computed(() => (this.error() ? `${this.id}-error` : this.hint() ? `${this.id}-hint` : null));
}

/** Dresses a native input, text area or native select; inside a ui-field it takes the field's id, messages and error state. */
@Directive({
  selector: 'input[uiInput], textarea[uiInput]',
  host: {
    class: 'control',
    '[class.number]': 'numeric()',
    '[attr.id]': 'field?.id ?? null',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': "field?.error() ? 'true' : null",
    '[attr.aria-required]': "field?.required() ? 'true' : null",
  },
})
export class Input {
  protected readonly field = inject(Field, { optional: true });
  /** Right-aligned figures, for amounts and quantities. */
  readonly numeric = input(false);
}

/** A checkbox with its label (projected). */
@Component({
  selector: 'ui-checkbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label>
      <input type="checkbox" [checked]="checked()" [indeterminate]="indeterminate()" [disabled]="disabled()"
        (change)="checked.set($any($event.target).checked)" [attr.aria-label]="ariaLabel()" />
      <span class="box" aria-hidden="true"></span>
      <span class="text"><ng-content /></span>
    </label>
  `,
  styles: `
    :host { display: inline-flex; }
    label { display: inline-flex; align-items: center; gap: var(--space-2); cursor: pointer; }
    input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .box {
      position: relative; flex: none; width: 16px; height: 16px; border: 1px solid var(--line-strong); border-radius: var(--radius-xs);
      background: var(--surface);
    }
    input:checked + .box, input:indeterminate + .box { border-color: var(--primary); background: var(--primary); }
    input:checked + .box::after {
      content: ''; position: absolute; left: 5px; top: 1px; width: 4px; height: 9px; border: solid var(--primary-ink);
      border-width: 0 2px 2px 0; transform: rotate(45deg);
    }
    input:indeterminate + .box::after {
      content: ''; position: absolute; left: 3px; top: 6.5px; width: 8px; height: 2px; background: var(--primary-ink);
    }
    input:focus-visible + .box { outline: 2px solid var(--focus); outline-offset: 2px; }
    input:disabled + .box, input:disabled ~ .text { opacity: 0.5; }
    .text:empty { display: none; }
  `,
})
export class Checkbox {
  readonly checked = model(false);
  readonly indeterminate = input(false);
  readonly disabled = input(false);
  /** For a checkbox with no visible text (a table row). */
  readonly ariaLabel = input<string | null>(null);
}
