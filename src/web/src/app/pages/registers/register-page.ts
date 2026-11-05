import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { MigrationFile } from '../operations/migration-file';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import {
  Category, Element, ElementType, EntityApi, LookupScope, REGISTERS, Register, RightsService, TreeRow, buildTree, errorsOf,
} from '../../core/entities';
import { itemAt } from '../../core/menu';
import { SessionService } from '../../core/session';
import { Chip, Chips } from '../../ui/chips';
import { Button, Input } from '../../ui/controls';
import { textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Column, Sort, Table, TreeTable, parentIds } from '../../ui/table';
import { ElementPanel } from './element-panel';
import { TREE_LIMIT, TreeListNode, TreeListRow, elementTree, nests } from './element-tree';
import { RegisterSetup } from './register-setup';

/** The categories a person sees in the tree: those they may read, and the folders above them. */
export function readableTree(tree: TreeRow<Category>[], canRead: (id: string) => boolean): TreeRow<Category>[] {
  return tree.flatMap(node => {
    const children = readableTree(node.children, canRead);
    return canRead(node.id) || children.length ? [{ ...node, children }] : [];
  });
}

interface ListRow {
  id: string;
  code: string;
  name: string;
  type: string;
}

const PAGE_SIZE = 50;
const TREE_PAGE = 500;

/**
 * One screen for every register (partners, products, cost centres, charts, units, employees, users, transactions): the categories on the
 * left, the category's rows in the middle (by type, searched, a page at a time from the API), the open row on the right; and a set-up view
 * for the categories, types and their properties.
 */
@Component({
  selector: 'app-register-page',
  imports: [TranslatePipe, Button, Chips, ElementPanel, Icon, Input, MigrationFile, NgTemplateOutlet, RegisterSetup, Table, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register-page.html',
  styleUrl: './register-page.scss',
})
export class RegisterPage {
  private readonly api = inject(EntityApi);
  protected readonly rights = inject(RightsService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  /** The route's entity key (entities/:entity). */
  readonly entity = input.required<string>();
  /** Opens on the register's categories and types (route setup/:entity). */
  readonly setupFirst = input(false);
  /** A row to open at once (?element=, as the pipeline links an opportunity). */
  readonly element = input<string | null>(null);
  protected readonly register = computed<Register | null>(() => REGISTERS[this.entity()] ?? null);

  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)), {
    initialValue: this.router.url,
  });
  protected readonly title = computed(() => itemAt(this.session.menu(), this.url())?.name ?? this.entity());

  protected readonly categories = signal<Category[]>([]);
  protected readonly types = signal<ElementType[]>([]);
  protected readonly scopes = signal<LookupScope[]>([]);
  protected readonly ready = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly setup = signal(false);

  protected readonly categoryId = signal<string | null>(null);
  protected readonly typeIds = signal<string[]>([]);
  protected readonly firstType = computed(() => this.typeIds().at(0) ?? null);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly sort = signal<Sort | null>(null);
  protected readonly rows = signal<ListRow[]>([]);
  /** The category's rows loaded whole when they nest (a tree grid, filtered on the screen); null: a list a page at a time. */
  protected readonly treeRows = signal<TreeListRow[] | null>(null);
  private treeCategory: string | null = null;
  protected readonly rowTree = computed(() => {
    const rows = this.treeRows();
    return rows ? elementTree(rows, this.search(), this.typeIds()) : null;
  });
  protected readonly expanded = signal<string[]>([]);
  protected readonly total = signal(0);
  protected readonly loadingRows = signal(false);
  /** The open row: its id, 'new', or null when the panel is closed. */
  protected readonly open = signal<string | null>(null);
  /** Rows in no category (a user who lost every role): administrators can open them and give them one. */
  protected readonly orphans = signal<Element[]>([]);

  protected readonly tree = computed(() => {
    const register = this.register();
    return register ? readableTree(buildTree(this.categories()), id => this.rights.canRead(register.table, id)) : [];
  });
  protected readonly category = computed(() => this.categories().find(c => c.id === this.categoryId()) ?? null);
  protected readonly canWrite = computed(() => {
    const register = this.register();
    const category = this.categoryId();
    return !!register && !!category && this.rights.canWrite(register.table, category);
  });
  protected readonly canSetUp = computed(() => {
    const register = this.register();
    return !!register && this.rights.canSetUp(register.table);
  });
  protected readonly typeChips = computed<Chip[]>(() => {
    const category = this.categoryId();
    return this.types().filter(t => t.isElement && (!t.categoryIds.length || (category && t.categoryIds.includes(category))))
      .map(t => ({ value: t.id!, label: textIn(t.name) }));
  });
  protected readonly columns: Column<ListRow>[] = [
    { key: 'code', label: t('Code'), width: '140px', sortable: true },
    { key: 'name', label: t('Name'), sortable: true },
    { key: 'type', label: t('Type'), width: '220px', sortable: true },
  ];

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const register = this.register();
      untracked(() => void this.load(register));
    });
    // A new tree or filter opens the top level, or the parents of what was found.
    effect(() => {
      const tree = this.rowTree();
      untracked(() => this.expanded.set(tree?.open ?? []));
    });
    // The list follows the category, the type chips, the search and the page.
    effect(() => {
      const register = this.register();
      const category = this.categoryId();
      const types = this.typeIds();
      const search = this.search();
      const page = this.page();
      if (register?.hasElements && category && this.ready()) {
        untracked(() => void this.loadRows(register, category, types, search, page));
      }
    });
  }

  private async load(register: Register | null): Promise<void> {
    void this.loadOrphans(register);
    this.ready.set(false);
    this.open.set(null);
    this.setup.set(this.setupFirst() || !register?.hasElements);
    this.categoryId.set(null);
    this.treeCategory = null;
    this.treeRows.set(null);
    this.message.set(null);
    if (!register) {
      return;
    }
    try {
      await this.rights.load();
      const [categories, types, scopes] = await Promise.all([this.api.categories(register), this.api.types(register),
        this.api.lookupScopes().catch(() => [] as LookupScope[])]);
      this.categories.set(categories);
      this.types.set(types);
      this.scopes.set(scopes);
      this.categoryId.set(firstHolding(this.tree())?.id ?? null);
      this.ready.set(true);
      const asked = this.element();
      if (asked) {
        const row = await this.api.element(register, asked).catch(() => null);
        if (row?.id) {
          this.categoryId.set(row.categoryIds[0] ?? this.categoryId());
          this.open.set(row.id);
        }
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async loadRows(register: Register, category: string, types: string[], search: string, page: number): Promise<void> {
    this.loadingRows.set(true);
    try {
      if (this.treeCategory !== category) {
        this.treeRows.set(await this.wholeTree(register, category));
        this.treeCategory = category;
      }
      if (this.treeRows()) {
        return;
      }
      // One type chosen: the API filters by it; several: each is asked for and the pages are joined (rare, and small).
      const pages = types.length > 1
        ? await Promise.all(types.map(t => this.api.elements(register, category, t, search, page, PAGE_SIZE)))
        : [await this.api.elements(register, category, types[0] ?? null, search, page, PAGE_SIZE)];
      const typeName = new Map(this.types().map(t => [t.id, textIn(t.name)]));
      this.rows.set(pages.flatMap(p => p.items).map(e => ({ id: e.id!, code: e.code ?? '', name: textIn(e.name), type: typeName.get(e.typeId) ?? '' })));
      this.total.set(pages.reduce((sum, p) => sum + p.total, 0));
    } catch (error) {
      this.message.set(errorsOf(error).message);
      this.rows.set([]);
      this.total.set(0);
    } finally {
      this.loadingRows.set(false);
    }
  }

  /** The category's rows when they nest and are few enough to load whole (the founder, 2026-10-31: cost centres as a tree grid); else null. */
  private async wholeTree(register: Register, category: string): Promise<TreeListRow[] | null> {
    const first = await this.api.elements(register, category, null, '', 1, TREE_PAGE);
    if (first.total > TREE_LIMIT) {
      return null;
    }
    const rest = await Promise.all(Array.from({ length: Math.ceil(first.total / TREE_PAGE) - 1 }, (_, i) =>
      this.api.elements(register, category, null, '', i + 2, TREE_PAGE)));
    const typeName = new Map(this.types().map(t => [t.id, textIn(t.name)]));
    const rows = [first, ...rest].flatMap(p => p.items).map(e => ({ id: e.id!, idSup: e.idSup ?? null, typeId: e.typeId, code: e.code ?? '', name: textIn(e.name),
      type: typeName.get(e.typeId) ?? '' }));
    return nests(rows) ? rows : null;
  }

  protected expandAll(open: boolean): void {
    const roots = this.rowTree()?.roots ?? [];
    this.expanded.set(open ? parentIds<TreeListNode>(roots, r => r.id, r => r.children) : []);
  }

  /** Opens a row in no category under the first category that holds rows, to give it one there. */
  protected openOrphan(row: Element): void {
    const first = this.categories().find(c => c.isElement && c.id);
    if (first?.id && row.id) {
      this.categoryId.set(first.id);
      this.open.set(row.id);
    }
  }

  protected orphanName(row: Element): string {
    return textIn(row.name) || row.code || '';
  }

  private async loadOrphans(register: Register | null): Promise<void> {
    this.orphans.set(register?.hasElements && this.session.me()?.isAdministrator ? await this.api.withoutCategory(register).catch(() => []) : []);
  }

  protected chooseCategory(node: TreeRow<Category>): void {
    if (!node.row.isElement) {
      return;
    }
    this.categoryId.set(node.id);
    this.typeIds.set([]);
    this.page.set(1);
    this.open.set(null);
  }

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
    }, 300);
  }

  protected chooseTypes(types: string[]): void {
    this.typeIds.set(types);
    this.page.set(1);
  }

  protected onSaved(element: Element): void {
    const register = this.register();
    const category = this.categoryId();
    if (register && category) {
      // A saved row may have moved in the tree: load it whole again.
      this.treeCategory = null;
      void this.loadRows(register, category, this.typeIds(), this.search(), this.page());
    }
    void this.loadOrphans(register);
    if (this.open() === 'new' && element.id) {
      this.open.set(element.id);
    }
  }

  /** After the set-up view changed categories or types. */
  protected async reloadSetup(): Promise<void> {
    const register = this.register();
    if (!register) {
      return;
    }
    const [categories, types] = await Promise.all([this.api.categories(register), this.api.types(register)]);
    this.categories.set(categories);
    this.types.set(types);
  }

  protected nameOf(category: Category): string {
    return textIn(category.name) || category.code || '';
  }
}

/** The first category that holds rows, depth first. */
function firstHolding(tree: TreeRow<Category>[]): TreeRow<Category> | null {
  for (const node of tree) {
    if (node.row.isElement) {
      return node;
    }
    const below = firstHolding(node.children);
    if (below) {
      return below;
    }
  }
  return null;
}
