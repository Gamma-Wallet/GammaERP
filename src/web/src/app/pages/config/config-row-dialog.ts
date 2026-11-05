import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfigApi, ConfigColumn, ConfigRow, ConfigSchema } from '../../core/config';
import { errorsOf } from '../../core/entities';
import { Button, Checkbox, Field, Input } from '../../ui/controls';
import { DialogFrame, Dialogs } from '../../ui/dialog';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';
import { TranslatedInput } from '../../ui/translated-input';
import { CODED, MODEL_NAMES, columnLabel } from './labels';

export interface ConfigRowData {
  model: string;
  schema: ConfigSchema;
  /** Null: a new row. */
  row: ConfigRow | null;
  /** Columns the row keeps as given (e.g. its document type on a document type's tab): not shown. */
  fixed: Record<string, string>;
  readOnly: boolean;
}

/** The columns a row's form shows: every editable one but those fixed by where the row is opened. */
export function formColumns(schema: ConfigSchema, fixed: Record<string, string>): ConfigColumn[] {
  return schema.columns.filter(c => !(c.name in fixed) && c.kind !== 'json');
}

/** A typed number as the row keeps it: null when empty, the number otherwise (NaN when it is not one, so the API names the column). */
export function numberOf(typed: string): number | null {
  return typed.trim() === '' ? null : Number(typed.replace(',', '.'));
}

/** A configuration row's form, built from its model's schema (B6). Closes with true when the row was saved or switched off. */
/** Rows that read the same mean the same, so one of them is offered: the one the row already holds, else the first. A row with nothing to read by only if it is held. */
export function distinctChoices<T extends { value: string; label: string; hint?: string }>(options: readonly T[], current: unknown): T[] {
  const seen = new Map<string, T>();
  for (const option of options) {
    if (option.label === '—' && option.value !== current) {
      continue;
    }
    const key = `${option.label}\u0000${option.hint ?? ''}`;
    if (!seen.has(key) || option.value === current) {
      seen.set(key, option);
    }
  }
  return [...seen.values()];
}

@Component({
  selector: 'app-config-row-dialog',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Field, Input, Select, TranslatedInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="(data.row ? '' : 'New: ') + modelName" width="640px">
      <div class="grid">
        @if (message()) { <p class="message" role="alert">{{ message() }}</p> }
        @for (column of columns; track column.name) {
          <ui-field [class.wide]="column.kind === 'translated'" [label]="label(column)" [required]="column.required" [error]="errors()[column.name] ?? null">
            @if (coded(column); as options) {
              <ui-select [options]="options" [value]="$any(value(column))" [disabled]="data.readOnly" (valueChange)="set(column, $event)" />
            } @else {
              @switch (column.kind) {
                @case ('translated') {
                  <ui-translated-input [label]="label(column)" [value]="$any(value(column))" [disabled]="data.readOnly" (valueChange)="set(column, $event)" />
                }
                @case ('boolean') {
                  <ui-checkbox [checked]="!!value(column)" [disabled]="data.readOnly" (checkedChange)="set(column, $event)">{{ label(column) }}</ui-checkbox>
                }
                @case ('number') {
                  <input uiInput [numeric]="true" inputmode="decimal" [value]="text(column)" [disabled]="data.readOnly" (input)="set(column, numberOf($any($event.target).value))" />
                }
                @case ('integer') {
                  <input uiInput [numeric]="true" inputmode="numeric" [value]="text(column)" [disabled]="data.readOnly" (input)="set(column, numberOf($any($event.target).value))" />
                }
                @case ('date') {
                  <input uiInput type="date" [value]="text(column)" [disabled]="data.readOnly" (input)="set(column, $any($event.target).value || null)" />
                }
                @case ('time') {
                  <input uiInput type="time" [value]="text(column).slice(0, 5)" [disabled]="data.readOnly" (input)="set(column, $any($event.target).value || null)" />
                }
                @case ('id') {
                  @if (column.isLink) {
                    <ui-select [options]="choices()[column.name] ?? []" [searchable]="true" [value]="$any(value(column))" [disabled]="data.readOnly"
                      (search)="loadChoices(column, $event)" (valueChange)="set(column, $event)" />
                  } @else {
                    <input uiInput [value]="text(column)" [disabled]="data.readOnly" (input)="set(column, $any($event.target).value || null)" />
                  }
                }
                @default {
                  <input uiInput [value]="text(column)" [disabled]="data.readOnly" (input)="set(column, $any($event.target).value || null)" />
                }
              }
            }
          </ui-field>
        }
      </div>
      <ng-container dialog-footer>
        @if (data.row?.id && !data.readOnly) { <button uiButton variant="danger" (click)="switchOff()">{{ 'Switch off…' | t }}</button> }
        <span class="spacer"></span>
        <button uiButton variant="quiet" (click)="ref.close(false)">{{ data.readOnly ? ('Close' | t) : ('Cancel' | t) }}</button>
        @if (!data.readOnly) { <button uiButton variant="primary" [disabled]="saving()" (click)="save()">{{ 'Save' | t }}</button> }
      </ng-container>
    </ui-dialog>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); }
    .wide, .message { grid-column: 1 / -1; }
    .message { margin: 0; padding: var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .spacer { flex: 1; }
  `,
})
export class ConfigRowDialog {
  protected readonly data = inject<ConfigRowData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  private readonly api = inject(ConfigApi);
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);
  protected readonly numberOf = numberOf;

  protected readonly modelName = MODEL_NAMES[this.data.model] ?? columnLabel(this.data.model);
  protected readonly columns = formColumns(this.data.schema, this.data.fixed);
  protected readonly draft = signal<ConfigRow>({ ...(this.data.row ?? {}), ...this.data.fixed });
  protected readonly errors = signal<Record<string, string | undefined>>({});
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly choices = signal<Record<string, SelectOption[] | undefined>>({});
  private readonly coding = computed(() => CODED[this.data.model] ?? {});

  constructor() {
    for (const column of this.columns.filter(c => c.isLink)) {
      void this.loadChoices(column, '');
    }
  }

  protected label(column: ConfigColumn): string {
    return columnLabel(column.name, this.data.model);
  }

  protected coded(column: ConfigColumn): SelectOption<number | null>[] | null {
    return this.coding()[column.name] ?? null;
  }

  protected value(column: ConfigColumn): unknown {
    return this.draft()[column.name] ?? null;
  }

  protected text(column: ConfigColumn): string {
    const value = this.value(column);
    return value === null || value === undefined ? '' : String(value);
  }

  protected set(column: ConfigColumn, value: unknown): void {
    this.draft.update(d => ({ ...d, [column.name]: value }));
  }

  async loadChoices(column: ConfigColumn, search: string): Promise<void> {
    try {
      const found = await this.api.choices(this.data.model, column.name, search);
      const current = this.value(column);
      const options = distinctChoices(found.map(c => ({ value: c.id, label: c.name, hint: c.code ?? undefined })), current);
      // The row's own choice stays shown while searching.
      const kept = typeof current === 'string' && !options.some(o => o.value === current) ? (this.choices()[column.name] ?? []).filter(o => o.value === current) : [];
      this.choices.update(all => ({ ...all, [column.name]: [...kept, ...options] }));
    } catch {
      this.choices.update(all => ({ ...all, [column.name]: [] }));
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.message.set(null);
    try {
      await this.api.save(this.data.model, this.draft());
      this.toasts.good(t('{0}: saved.', this.modelName));
      this.ref.close(true);
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(fields);
      this.message.set(message);
    } finally {
      this.saving.set(false);
    }
  }

  protected async switchOff(): Promise<void> {
    const id = this.data.row?.id;
    if (!id) {
      return;
    }
    const yes = await this.dialogs.confirm({ title: t('Switch off this row?'), message: t('The engine stops reading it. Nothing is deleted: its history stays.'),
      confirmLabel: t('Switch off'), danger: true });
    if (!yes) {
      return;
    }
    try {
      await this.api.switchOff(this.data.model, id);
      this.toasts.good(t('{0}: switched off.', this.modelName));
      this.ref.close(true);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
