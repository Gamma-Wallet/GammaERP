import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ConfigApi } from '../../core/config';
import { vatOptionsOf } from './element-panel';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Category, ElementType, EntityApi, LookupScope, Register, RightsService, Row, errorsOf, withDescendants } from '../../core/entities';
import { Button, Checkbox, Field, Input } from '../../ui/controls';
import { DialogFrame, Dialogs } from '../../ui/dialog';
import { textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Property, PropertyChoice, PropertyKind } from '../../ui/property-form.logic';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';
import { TranslatedInput } from '../../ui/translated-input';
import { ProductUnits } from './product-units';

export interface TypeEditorData {
  register: Register;
  types: ElementType[];
  categories: Category[];
  scopes: LookupScope[];
  /** Null: a new type. */
  type: ElementType | null;
}

type PropertyDraft = Property & Partial<Row> & { id: string };
type TypeDraft = Omit<ElementType, 'properties'> & { properties: PropertyDraft[] };

/** The kinds a property can be, as the editor names them. */
export const KINDS: SelectOption<PropertyKind>[] = [
  { value: PropertyKind.Text, label: t('Text') },
  { value: PropertyKind.Number, label: t('Number') },
  { value: PropertyKind.Date, label: t('Date') },
  { value: PropertyKind.DateTime, label: t('Date and time') },
  { value: PropertyKind.List, label: t('List (one choice)') },
  { value: PropertyKind.MultiList, label: t('List (several choices)') },
  { value: PropertyKind.ActivatingList, label: t('Choices that show more fields') },
  { value: PropertyKind.Lookup, label: t('Lookup (a row of a register)') },
  { value: PropertyKind.MultiText, label: t('Several texts') },
  { value: PropertyKind.Location, label: t('Location on a map') },
  { value: PropertyKind.Photo, label: t('Photo') },
];

const HAS_CHOICES = [PropertyKind.List, PropertyKind.MultiList, PropertyKind.ActivatingList];

/** A property not saved yet has a temporary id on the screen only, so it can be tracked; it is sent without one. */
const NEW = 'new:';
let nextNew = 0;

/** The type as the API wants it: new properties and choices without ids, properties numbered in their order on the screen. */
export function typeToSave(draft: TypeDraft): object {
  return {
    ...draft,
    properties: draft.properties.map((p, i) => ({
      ...p,
      id: p.id.startsWith(NEW) ? (null as unknown as string) : p.id,
      sortOrder: i + 1,
      elementsId: p.kind === PropertyKind.Lookup ? (p.elementsId ?? null) : null,
      choices: HAS_CHOICES.includes(p.kind) ? p.choices.filter(c => textIn(c.name).trim()).map(c => ({ ...c, id: c.id.startsWith(NEW) ? (null as unknown as string) : c.id })) : [],
    })),
  };
}

/**
 * A type's name, code and numbering, place in the tree, categories, and its properties: their kind, whether required,
 * their choices, the scope a lookup picks from, and the choice of an activating list that shows them. Closes with true when saved.
 */
@Component({
  selector: 'app-type-editor',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Field, Icon, Input, ProductUnits, Select, TranslatedInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './type-editor.html',
  styleUrl: './type-editor.scss',
})
export class TypeEditor {
  protected readonly data = inject<TypeEditorData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  private readonly api = inject(EntityApi);
  private readonly config = inject(ConfigApi);
  protected readonly isAdministrator = inject(RightsService).isAdministrator;
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);
  protected readonly kinds = KINDS;
  protected readonly Kind = PropertyKind;

  protected readonly draft = signal<TypeDraft>(this.fromType(this.data.type));
  protected readonly errors = signal<Record<string, string | undefined>>({});
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly vatOptions = signal<SelectOption<string | null>[]>([{ value: null, label: '—' }]);
  protected readonly copyFrom = signal<string | null>(null);

  protected readonly name = computed(() => textIn(this.draft().name));
  protected readonly parents = computed<SelectOption<string | null>[]>(() => {
    const own = this.data.type?.id ? withDescendants(this.data.types, this.data.type.id) : new Set<string>();
    return [{ value: null, label: '— At the top —' },
      ...this.data.types.filter(t => t.id && !own.has(t.id)).map(t => ({ value: t.id as string | null, label: textIn(t.name), hint: t.code ?? undefined }))];
  });
  protected readonly categoryOptions = computed<SelectOption[]>(() =>
    this.data.categories.filter(c => c.isElement).map(c => ({ value: c.id!, label: textIn(c.name), hint: c.code ?? undefined })));
  protected readonly scopeOptions = computed<SelectOption[]>(() =>
    this.data.scopes.map(s => ({ value: s.id, label: s.name || s.table, hint: [s.table, s.categoryId ? 'category' : '', s.typeId ? 'type' : ''].filter(Boolean).join(' · ') })));
  protected readonly otherTypes = computed<SelectOption[]>(() =>
    this.data.types.filter(t => t.id !== this.data.type?.id && t.properties.length).map(t => ({ value: t.id!, label: textIn(t.name), hint: `${t.properties.length} properties` })));
  /** The saved choices of the type's activating lists: a property can be shown for one of them (a new choice gets its id when saved). */
  protected readonly showChoices = computed<SelectOption<string | null>[]>(() => [
    { value: null, label: '— Always shown —' },
    ...this.draft().properties.filter(p => p.kind === PropertyKind.ActivatingList && !p.id.startsWith(NEW))
      .flatMap(p => p.choices.filter(c => !c.id.startsWith(NEW)).map(c => ({ value: c.id as string | null, label: `${textIn(p.name)}: ${textIn(c.name)}` }))),
  ]);

  constructor() {
    if (this.data.register.key === 'products') {
      void this.config.list('vat-groups').then(rows => this.vatOptions.set([{ value: null, label: '—' }, ...vatOptionsOf(rows)])).catch(() => undefined);
    }
  }

  private fromType(type: ElementType | null): TypeDraft {
    const base = type ?? { id: null, idSup: null, active: true, isElement: true, code: null, codeStart: null, codeEnd: null, codeCurrent: null, name: {},
      description: null, sortOrder: 0, categoryIds: [], properties: [] };
    return { ...base, properties: base.properties.filter(p => p.active !== false).map(p => ({ ...p, id: p.id!, choices: p.choices.filter(c => c.active !== false) })) };
  }

  protected set(change: Partial<TypeDraft>): void {
    this.draft.update(d => ({ ...d, ...change }));
  }

  protected number(typed: string): number | null {
    return typed.trim() === '' || Number.isNaN(Number(typed)) ? null : Number(typed);
  }

  protected setProperty(index: number, change: Partial<PropertyDraft>): void {
    this.draft.update(d => ({ ...d, properties: d.properties.map((p, i) => (i === index ? { ...p, ...change } : p)) }));
  }

  protected addProperty(): void {
    const property: PropertyDraft = { id: `${NEW}${++nextNew}`, name: {}, required: false, kind: PropertyKind.Text, sortOrder: 0, choices: [], active: true };
    this.draft.update(d => ({ ...d, properties: [...d.properties, property] }));
  }

  protected removeProperty(index: number): void {
    this.draft.update(d => ({ ...d, properties: d.properties.filter((_, i) => i !== index) }));
  }

  protected move(index: number, by: -1 | 1): void {
    this.draft.update(d => {
      const list = [...d.properties];
      const target = index + by;
      if (target < 0 || target >= list.length) {
        return d;
      }
      [list[index], list[target]] = [list[target], list[index]];
      return { ...d, properties: list };
    });
  }

  protected hasChoices(p: PropertyDraft): boolean {
    return HAS_CHOICES.includes(p.kind);
  }

  protected setChoice(index: number, choice: number, name: Record<string, string>): void {
    const p = this.draft().properties[index];
    const choices = p.choices.map((c, i) => (i === choice ? { ...c, name } : c));
    this.setProperty(index, { choices });
  }

  protected addChoice(index: number): void {
    const p = this.draft().properties[index];
    const choice: PropertyChoice = { id: `${NEW}${++nextNew}`, name: {} };
    this.setProperty(index, { choices: [...p.choices, choice] });
  }

  protected removeChoice(index: number, choice: number): void {
    const p = this.draft().properties[index];
    this.setProperty(index, { choices: p.choices.filter((_, i) => i !== choice) });
  }

  protected choiceName(c: PropertyChoice): string {
    return c.name['en'] ?? textIn(c.name);
  }

  protected errorOf(index: number): string | null {
    const prefix = `properties[${index}]`;
    const found = Object.entries(this.errors()).filter(([k]) => k.startsWith(prefix)).map(([, v]) => v);
    return found.length ? found.join(' ') : null;
  }

  protected async save(): Promise<boolean> {
    this.saving.set(true);
    this.message.set(null);
    try {
      await this.api.saveType(this.data.register, typeToSave(this.draft()));
      this.toasts.good(t('Type {0} saved.', this.name()));
      this.ref.close(true);
      return true;
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(fields);
      this.message.set(message);
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  protected async copy(): Promise<void> {
    const from = this.copyFrom();
    const id = this.data.type?.id;
    if (!from || !id) {
      return;
    }
    try {
      const type = await this.api.copyProperties(this.data.register, id, from);
      this.draft.set(this.fromType(type));
      this.copyFrom.set(null);
      this.toasts.good(t('Properties copied. Those the type already had were left as they were.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async switchOff(): Promise<void> {
    const yes = await this.dialogs.confirm({ title: `Switch off ${this.name()}?`, message: t('New rows can no longer have this type. Nothing is deleted.'),
      confirmLabel: t('Switch off'), danger: true });
    if (yes) {
      this.set({ active: false });
      if (!(await this.save())) {
        this.set({ active: true });
      }
    }
  }
}
