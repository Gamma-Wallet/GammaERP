import { ChangeDetectionStrategy, Component, computed, input, model, output, signal } from '@angular/core';
import { text } from '../core/text';
import { Button, Checkbox, Field, Input } from './controls';
import { textIn } from './format';
import { Icon } from './icon';
import {
  Location, Property, PropertyKind, PropertyValues, Translated, shownProperties, toLocalInput, validateProperties, valuesToSave,
} from './property-form.logic';
import { Select, SelectOption } from './select';

export interface PropertyFile {
  property: Property;
  file: File;
}

export interface LookupSearch {
  property: Property;
  query: string;
}

/**
 * The form of a type's properties (BRAND, MODEL…), drawn from the properties themselves: one control per kind, in their order, the
 * dependants of an activating list shown only when its choice is picked. [(values)] holds the element's values (property id → JSON);
 * call validate() before saving and send valuesToSave() with toSaveJson. Lookups take their options from [lookupOptions] (entity id →
 * options) and report what is typed in (lookupSearch); photos report the file chosen (fileChosen) for the screen to upload.
 */
@Component({
  selector: 'ui-property-form',
  imports: [Button, Checkbox, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './property-form.html',
  styles: `
    :host { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--space-4) var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .languages { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: var(--space-2); }
    .language { display: flex; align-items: center; gap: var(--space-2); }
    .language span { flex: none; width: 24px; color: var(--muted); font-size: var(--text-xs); font-weight: 600; text-transform: uppercase; }
    .pair, .row { display: flex; gap: var(--space-2); }
    .list { display: grid; gap: var(--space-2); }
    .checks { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-4); min-height: var(--control-h); align-items: center; }
    .photo { display: flex; align-items: center; gap: var(--space-3); }
    .photo .path { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); }
    input[type='file'] { display: none; }
  `,
})
export class PropertyForm {
  protected readonly text = text;
  protected readonly Kind = PropertyKind;
  protected readonly toLocalInput = toLocalInput;

  readonly properties = input<readonly Property[]>([]);
  /** The languages a text is written in: the company's, its first one shown first. */
  readonly languages = input<readonly string[]>(['en']);
  /** The language the names of properties and choices are shown in. */
  readonly language = input('en');
  readonly lookupOptions = input<Record<string, SelectOption[]>>({});
  readonly disabled = input(false);
  readonly values = model<PropertyValues>({});
  readonly fileChosen = output<PropertyFile>();
  readonly lookupSearch = output<LookupSearch>();

  /** Errors show once the person has left a control or tried to save, never on an untouched form. */
  private readonly touched = signal<ReadonlySet<string>>(new Set());
  private readonly submitted = signal(false);

  protected readonly shown = computed(() => shownProperties(this.properties(), this.values()));
  private readonly errors = computed(() => validateProperties(this.properties(), this.values()));

  protected nameOf(p: { name: Translated }): string {
    return textIn(p.name, this.language());
  }

  protected hintOf(p: Property): string | null {
    return p.description ? textIn(p.description, this.language()) || null : null;
  }

  protected errorOf(p: Property): string | null {
    return this.submitted() || this.touched().has(p.id) ? (this.errors()[p.id] ?? null) : null;
  }

  protected choicesOf(p: Property): SelectOption[] {
    return p.choices.filter(c => c.active !== false).map(c => ({ value: c.id, label: this.nameOf(c) }));
  }

  protected wide(p: Property): boolean {
    return (p.kind === PropertyKind.Text && this.languages().length > 1) || p.kind === PropertyKind.MultiText
      || p.kind === PropertyKind.ActivatingList;
  }

  private value<T>(p: Property): T | undefined {
    return this.values()[p.id] as T | undefined;
  }

  /** The value as an input shows it: a number, a date, a photo's link. */
  protected raw(p: Property): string {
    const value = this.values()[p.id];
    return value === null || value === undefined ? '' : String(value);
  }

  protected textOf(p: Property, language: string): string {
    return this.value<Translated>(p)?.[language] ?? '';
  }

  protected one(p: Property): string | null {
    return this.value<string>(p) ?? null;
  }

  protected many(p: Property): string[] {
    const value = this.value<string[] | string>(p);
    return Array.isArray(value) ? value : value ? [value] : [];
  }

  protected lookupOf(p: Property): SelectOption[] {
    return (p.elementsId && this.lookupOptions()[p.elementsId]) || [];
  }

  protected itemText(item: Translated): string {
    return item[this.languages()[0] ?? 'en'] ?? '';
  }

  protected part(p: Property, part: 'lat' | 'lng'): string {
    const value = this.value<Location>(p)?.[part];
    return value === null || value === undefined ? '' : String(value);
  }

  protected set(p: Property, value: unknown): void {
    this.values.update(values => ({ ...values, [p.id]: value }));
  }

  protected touch(p: Property): void {
    this.touched.update(set => new Set(set).add(p.id));
  }

  protected setText(p: Property, language: string, typed: string): void {
    this.set(p, { ...(this.value<Translated>(p) ?? {}), [language]: typed });
  }

  protected setLocation(p: Property, part: 'lat' | 'lng', typed: string): void {
    this.set(p, { ...({ lat: null, lng: null, ...this.value<Location>(p) }), [part]: typed });
  }

  protected texts(p: Property): Translated[] {
    const list = this.value<Translated[]>(p);
    return list?.length ? list : [{}];
  }

  protected setTextAt(p: Property, index: number, typed: string): void {
    const list = [...this.texts(p)];
    list[index] = { ...list[index], [this.languages()[0] ?? 'en']: typed };
    this.set(p, list);
  }

  protected addText(p: Property): void {
    this.set(p, [...this.texts(p), {}]);
  }

  protected removeText(p: Property, index: number): void {
    this.set(p, this.texts(p).filter((_, i) => i !== index));
  }

  protected isPicked(p: Property, choiceId: string): boolean {
    return this.many(p).includes(choiceId);
  }

  protected pick(p: Property, choiceId: string, picked: boolean): void {
    const list = this.many(p);
    this.set(p, picked ? [...list.filter(x => x !== choiceId), choiceId] : list.filter(x => x !== choiceId));
    this.touch(p);
  }

  protected chooseFile(p: Property, input: HTMLInputElement): void {
    const file = input.files?.[0];
    if (file) {
      this.fileChosen.emit({ property: p, file });
    }
    input.value = '';
  }

  /** Shows every error and says whether the form can be saved. */
  validate(): boolean {
    this.submitted.set(true);
    return Object.keys(this.errors()).length === 0;
  }

  /** The values for the API (only the shown properties); send them with toSaveJson so numbers stay exact. */
  toSave(): Record<string, unknown> {
    return valuesToSave(this.properties(), this.values());
  }
}
