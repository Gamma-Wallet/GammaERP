import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Category, ElementType, LookupScope, Register, TreeRow, buildTree, withDescendants } from '../../core/entities';
import { Button } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Status } from '../../ui/status';
import { CellTemplate, Column, TreeTable, parentIds } from '../../ui/table';
import { CategoryEditor, CategoryEditorData } from './category-editor';
import { TypeEditor, TypeEditorData } from './type-editor';

interface SetupRow {
  id: string;
  code: string;
  name: string;
  holds: boolean;
  extra: string;
  children: SetupRow[];
}

function rowsOf<T extends Category | ElementType>(tree: TreeRow<T>[], extra: (row: T) => string): SetupRow[] {
  return tree.map(n => ({ id: n.id, code: n.row.code ?? '', name: textIn(n.row.name), holds: n.row.isElement, extra: extra(n.row),
    children: rowsOf(n.children, extra) }));
}

/** The types of a category and of the categories under it, with the folders above them so the tree stays whole; no category: all of them. */
export function typesIn(types: readonly ElementType[], categories: readonly Category[], category: string | null): ElementType[] {
  if (!category) {
    return [...types];
  }
  const inside = withDescendants(categories, category);
  const byId = new Map(types.map(t => [t.id, t]));
  const shown = new Set<string | null>();
  for (const type of types.filter(t => t.categoryIds.some(c => inside.has(c)))) {
    for (let t: ElementType | undefined = type; t && !shown.has(t.id); t = t.idSup ? byId.get(t.idSup) : undefined) {
      shown.add(t.id);
    }
  }
  return types.filter(t => shown.has(t.id));
}

/**
 * The set-up of a register: its categories and its types (with their properties), as trees. A category filters the types (again: all of them)
 * and the button on its row opens its editor; a type opens its editor.
 */
@Component({
  selector: 'app-register-setup',
  imports: [TranslatePipe, Button, CellTemplate, Icon, Status, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section>
      <div class="bar">
        <h2>{{ 'Categories' | t }}</h2>
        <button uiButton size="small" (click)="editCategory(null)"><ui-icon name="plus" [size]="14" />{{ 'Category' | t }}</button>
      </div>
      <ui-tree-table [columns]="categoryColumns" [rows]="categoryRows()" [(expanded)]="openCategories" [ariaLabel]="'Categories' | t" [activeId]="selected()"
        (rowClick)="select($any($event).id)">
        <ng-template uiCell="holds" let-row>
          @if ($any(row).holds) { <ui-status kind="good">{{ 'Holds rows' | t }}</ui-status> } @else { <ui-status>{{ 'Folder' | t }}</ui-status> }
        </ng-template>
        <ng-template uiCell="edit" let-row>
          <button uiButton variant="quiet" size="small" (click)="$event.stopPropagation(); editCategory($any(row).id)">{{ 'Edit' | t }}</button>
        </ng-template>
      </ui-tree-table>
    </section>
    <section>
      <div class="bar">
        <h2>{{ 'Types' | t }}</h2>
        <div class="actions">
          @if (selected()) { <button uiButton variant="quiet" size="small" (click)="selected.set(null)">{{ 'All types' | t }}</button> }
          <button uiButton size="small" (click)="editType(null)"><ui-icon name="plus" [size]="14" />{{ 'Type' | t }}</button>
        </div>
      </div>
      <ui-tree-table [columns]="typeColumns" [rows]="typeRows()" [(expanded)]="openTypes" [ariaLabel]="'Types' | t" (rowClick)="editType($any($event).id)">
        <ng-template uiCell="holds" let-row>
          @if ($any(row).holds) { <ui-status kind="good">{{ 'Rows' | t }}</ui-status> } @else { <ui-status>{{ 'Folder' | t }}</ui-status> }
        </ng-template>
      </ui-tree-table>
    </section>
  `,
  styles: `
    :host { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-5); padding: 0 var(--space-6) var(--space-6); }
    @media (max-width: 1200px) { :host { grid-template-columns: minmax(0, 1fr); } }
    section { display: flex; flex-direction: column; gap: var(--space-3); min-width: 0; }
    .bar { display: flex; align-items: center; justify-content: space-between; }
    .actions { display: flex; align-items: center; gap: var(--space-2); }
    h2 { font-size: var(--title-s); }
  `,
})
export class RegisterSetup {
  private readonly dialogs = inject(Dialogs);
  readonly register = input.required<Register>();
  readonly categories = input<Category[]>([]);
  readonly types = input<ElementType[]>([]);
  readonly scopes = input<LookupScope[]>([]);
  readonly changed = output<void>();

  protected readonly categoryRows = computed(() => rowsOf(buildTree(this.categories()), () => ''));
  /** The category the types are filtered by. */
  protected readonly selected = signal<string | null>(null);
  protected readonly typeRows = computed(() => rowsOf(buildTree(typesIn(this.types(), this.categories(), this.selected())), t => String(t.properties.filter(p => p.active !== false).length)));
  protected readonly openCategories = signal<string[]>([]);
  protected readonly openTypes = signal<string[]>([]);
  protected readonly categoryColumns: Column<SetupRow>[] = [
    { key: 'name', label: t('Name') },
    { key: 'code', label: t('Code'), width: '140px' },
    { key: 'holds', label: '', width: '120px' },
    { key: 'edit', label: '', width: '70px' },
  ];
  protected readonly typeColumns: Column<SetupRow>[] = [
    { key: 'name', label: t('Name') },
    { key: 'code', label: t('Code'), width: '120px' },
    { key: 'extra', label: t('Properties'), width: '100px', align: 'end' },
    { key: 'holds', label: '', width: '100px' },
  ];

  constructor() {
    // Every folder open at first: set-up trees are short.
    queueMicrotask(() => {
      this.openCategories.set(parentIds(this.categoryRows(), r => r.id, r => r.children));
      this.openTypes.set(parentIds(this.typeRows(), r => r.id, r => r.children));
    });
  }

  protected select(id: string): void {
    this.selected.update(current => (current === id ? null : id));
  }

  protected async editCategory(id: string | null): Promise<void> {
    const data: CategoryEditorData = { register: this.register(), categories: this.categories(), category: this.categories().find(c => c.id === id) ?? null };
    const ref = this.dialogs.open<boolean, CategoryEditorData, CategoryEditor>(CategoryEditor, data);
    if (await firstValueFrom(ref.closed)) {
      this.changed.emit();
    }
  }

  protected async editType(id: string | null): Promise<void> {
    const data: TypeEditorData = { register: this.register(), types: this.types(), categories: this.categories(), scopes: this.scopes(),
      type: this.types().find(t => t.id === id) ?? null };
    const ref = this.dialogs.open<boolean, TypeEditorData, TypeEditor>(TypeEditor, data);
    if (await firstValueFrom(ref.closed)) {
      this.changed.emit();
    }
  }
}
