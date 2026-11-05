import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Category, EntityApi, Register, errorsOf, withDescendants } from '../../core/entities';
import { Button, Checkbox, Field, Input } from '../../ui/controls';
import { DialogFrame, Dialogs } from '../../ui/dialog';
import { textIn } from '../../ui/format';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';
import { TranslatedInput } from '../../ui/translated-input';

export interface CategoryEditorData {
  register: Register;
  categories: Category[];
  /** Null: a new category. */
  category: Category | null;
}

/** A category's name, code, place in the tree and whether it holds rows; switched off rather than deleted. Closes with true when saved. */
@Component({
  selector: 'app-category-editor',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Field, Input, Select, TranslatedInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="data.category ? ('Category {0}' | t: (name() || '')) : ('New category' | t)" width="560px">
      <div class="grid">
        @if (message()) { <p class="message" role="alert">{{ message() }}</p> }
        <ui-field [label]="'Name' | t" [required]="true" [error]="errors()['name'] ?? null">
          <ui-translated-input [label]="'Name' | t" [value]="draft().name" (valueChange)="set({ name: $event ?? {} })" />
        </ui-field>
        <ui-field [label]="'Code' | t" [error]="errors()['code'] ?? null">
          <input uiInput [value]="draft().code ?? ''" (input)="set({ code: $any($event.target).value || null })" />
        </ui-field>
        <ui-field [label]="'Under' | t" [hint]="'Empty: at the top' | t" [error]="errors()['idSup'] ?? null">
          <ui-select [options]="parents()" [value]="draft().idSup" (valueChange)="set({ idSup: $event })" />
        </ui-field>
        <ui-checkbox [checked]="draft().isElement" (checkedChange)="set({ isElement: $event })">{{ 'Holds rows (otherwise a folder of categories)' | t }}</ui-checkbox>
        @if (statutory() !== null) {
          <ui-checkbox [checked]="statutory()!" (checkedChange)="statutory.set($event)">{{ 'Statutory chart: the one the reports read and the account mapping maps into' | t }}</ui-checkbox>
        }
      </div>
      <ng-container dialog-footer>
        @if (data.category) { <button uiButton variant="danger" (click)="switchOff()">{{ 'Switch off…' | t }}</button> }
        <span class="spacer"></span>
        <button uiButton variant="quiet" (click)="ref.close(false)">{{ 'Cancel' | t }}</button>
        <button uiButton variant="primary" [disabled]="saving()" (click)="save()">{{ 'Save' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `
    .grid { display: grid; gap: var(--space-4); }
    .message { margin: 0; padding: var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .spacer { flex: 1; }
  `,
})
export class CategoryEditor {
  protected readonly data = inject<CategoryEditorData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  private readonly api = inject(EntityApi);
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);

  protected readonly draft = signal<Partial<Category> & { name: Record<string, string>; isElement: boolean; idSup: string | null }>(
    this.data.category ? { ...this.data.category } : { id: null, idSup: null, active: true, isElement: true, code: null, name: {}, description: null, sortOrder: 0 });
  protected readonly errors = signal<Record<string, string | undefined>>({});
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  /** Chart categories only, for administrators: whether this one is the statutory chart; null hides the box. */
  protected readonly statutory = signal<boolean | null>(null);
  private statutoryWas: boolean | null = null;
  private readonly http = inject(HttpClient);

  constructor() {
    const id = this.data.category?.id;
    if (this.data.register.key === 'charts' && id) {
      void firstValueFrom(this.http.get<{ id: string | null }>('/api/v1/charts/categories/statutory'))
        .then(s => { this.statutoryWas = s.id === id; this.statutory.set(this.statutoryWas); }).catch(() => undefined);
    }
  }
  protected readonly name = computed(() => textIn(this.draft().name));
  /** Any other category, but never this one or one below it. */
  protected readonly parents = computed<SelectOption<string | null>[]>(() => {
    const own = this.data.category?.id ? withDescendants(this.data.categories, this.data.category.id) : new Set<string>();
    return [{ value: null, label: '— At the top —' },
      ...this.data.categories.filter(c => c.id && !own.has(c.id)).map(c => ({ value: c.id as string | null, label: textIn(c.name), hint: c.code ?? undefined }))];
  });

  protected set(change: Partial<Category>): void {
    this.draft.update(d => ({ ...d, ...change }));
  }

  protected async save(): Promise<boolean> {
    this.saving.set(true);
    try {
      await this.api.saveCategory(this.data.register, this.draft());
      const statutory = this.statutory();
      if (statutory !== null && statutory !== this.statutoryWas) {
        await firstValueFrom(this.http.post(`/api/v1/charts/categories/${this.data.category!.id}/statutory`, null, { params: { statutory } }));
      }
      this.toasts.good(t('Category {0} saved.', this.name()));
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

  protected async switchOff(): Promise<void> {
    const yes = await this.dialogs.confirm({ title: `Switch off ${this.name()}?`, message: t('It leaves the tree. Nothing is deleted: its history stays.'),
      confirmLabel: t('Switch off'), danger: true });
    if (yes) {
      this.set({ active: false });
      if (!(await this.save())) {
        this.set({ active: true });
      }
    }
  }
}
