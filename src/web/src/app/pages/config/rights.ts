import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfigApi } from '../../core/config';
import { DocumentApi } from '../../core/documents';
import { Category, EntityApi, REGISTERS, Right, RightsService, errorsOf } from '../../core/entities';
import { Chip, Chips } from '../../ui/chips';
import { Button, Checkbox, Field } from '../../ui/controls';
import { textIn } from '../../ui/format';
import { Select, SelectOption } from '../../ui/select';
import { CellTemplate, Column, TreeTable } from '../../ui/table';
import { Toasts } from '../../ui/toast';

type Kind = 'users' | 'roles';
type View = 'registers' | 'menu';

/** A node of the whole menu, as an administrator gives it. */
export interface MenuRight { id: string; parentId: string | null; code: string | null; name: string | null; icon: string | null; isElement: boolean; given: boolean }

/** The menu as a tree. */
export function menuTree<T extends Pick<MenuRight, 'id' | 'parentId'>>(rows: readonly T[]): (T & { children: unknown[] })[] {
  const nodes = rows.map(r => ({ ...r, children: [] as unknown[] }));
  const byId = new Map(nodes.map(n => [n.id, n]));
  const top: typeof nodes = [];
  for (const node of nodes) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined;
    if (parent) {
      parent.children.push(node);
    } else {
      top.push(node);
    }
  }
  return top;
}

/** Whether a node comes with a node above it: giving a folder gives everything under it. */
export function comesWith(rows: readonly Pick<MenuRight, 'id' | 'parentId'>[], id: string, given: ReadonlySet<string>): boolean {
  const byId = new Map(rows.map(r => [r.id, r]));
  for (let up = byId.get(id)?.parentId ?? null; up; up = byId.get(up)?.parentId ?? null) {
    if (given.has(up)) {
      return true;
    }
  }
  return false;
}

/** A register's name when its elements row has none to show. */
const REGISTER_NAMES: Record<string, string> = {
  tbl_partner: t('Partners'), tbl_product: t('Products and services'), tbl_cost_center: t('Cost centres'), tbl_chart: t('Accounts'), tbl_unit: t('Units of measure'),
  tbl_employee: t('Employees'), tbl_user: t('Users'), tbl_transaction: t('Transaction types'), tbl_document: t('Documents'),
};
type Flag = 'readAll' | 'readOwn' | 'write' | 'forward';

/** One row of the grid: an entity's category and the four rights on it. */
export interface RightRow {
  table: string;
  entity: string;
  categoryId: string;
  category: string;
  readAll: boolean;
  readOwn: boolean;
  write: boolean;
  forward: boolean;
}

/** The grid's rows for the categories of every entity, ticked as the saved rights say. */
export function rowsOf(categories: readonly { table: string; entity: string; categories: Category[] }[], rights: readonly Right[]): RightRow[] {
  return categories.flatMap(e => e.categories.filter(c => c.isElement).map(c => {
    const r = rights.find(x => x.table === e.table && x.categoryId === c.id);
    return { table: e.table, entity: e.entity, categoryId: c.id!, category: textIn(c.name), readAll: !!r?.readAll, readOwn: !!r?.readOwn, write: !!r?.write,
      forward: !!r?.forward };
  }));
}

/** What is saved: only the rows with at least one right. */
export function rightsOf(rows: readonly RightRow[]): Right[] {
  return rows.filter(r => r.readAll || r.readOwn || r.write || r.forward)
    .map(r => ({ table: r.table, categoryId: r.categoryId, readAll: r.readAll, readOwn: r.readOwn, write: r.write, forward: r.forward }));
}

/**
 * Who may do what: for one user or one role, every category of every entity rights are given on (the entities
 * with an elements row of their own), with read all, read own, write and forward; and the whole menu, with what they may see of it. Only an
 * administrator changes them, and only an administrator sees the whole menu: anyone else sees only what they were given.
 */
@Component({
  selector: 'app-rights',
  imports: [TranslatePipe, Button, CellTemplate, Checkbox, Chips, Field, Select, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Rights' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert">{{ message() }}</p> }
    <div class="pick">
      <ui-chips [chips]="kinds" [multiple]="false" [chosen]="[kind()]" (chosenChange)="chooseKind($event[0])" [ariaLabel]="'Rights of' | t" />
      <ui-field [label]="kind() === 'users' ? 'User' : 'Role'">
        <ui-select [options]="kind() === 'users' ? users() : roles()" [searchable]="true" [value]="who()" (valueChange)="choose($event)" />
      </ui-field>
      @if (who() && admin()) { <button uiButton variant="primary" [disabled]="saving()" (click)="save()">{{ 'Save' | t }}</button> }
    </div>
    @if (who()) {
      <ui-chips [chips]="views" [multiple]="false" [chosen]="[view()]" (chosenChange)="$event[0] && view.set($event[0])" [ariaLabel]="'Rights on' | t" />
    }
    @if (who() && view() === 'menu') {
      <ui-tree-table [columns]="menuColumns" [rows]="tree()" [(expanded)]="expanded" [rowId]="menuId" [ariaLabel]="'Menu' | t" [emptyText]="'The company has no menu.' | t">
        <ng-template uiCell="given" let-row>
          <ui-checkbox [checked]="given().has(row.id) || covered(row.id)" [disabled]="covered(row.id)" [ariaLabel]="row.name ?? ''" (checkedChange)="give(row.id, $event)" />
        </ng-template>
      </ui-tree-table>
    } @else if (who()) {
      <div class="wrap">
        <table [attr.aria-label]="'Rights' | t">
          <thead><tr><th>{{ 'Register' | t }}</th><th>{{ 'Category' | t }}</th><th class="c">{{ 'Read all' | t }}</th><th class="c">{{ 'Read own' | t }}</th><th class="c">{{ 'Write' | t }}</th><th class="c">{{ 'Forward' | t }}</th></tr></thead>
          <tbody>
            @for (row of rows(); track row.table + row.categoryId; let i = $index) {
              <tr>
                <td class="entity">{{ i === 0 || rows()[i - 1].table !== row.table ? row.entity : '' }}</td>
                <td>{{ row.category }}</td>
                @for (flag of flags; track flag) {
                  <td class="c"><ui-checkbox [checked]="row[flag]" [disabled]="!admin()" [ariaLabel]="row.category + ': ' + flag" (checkedChange)="tick(i, flag, $event)" /></td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    :host { display: grid; grid-template-columns: minmax(0, 1fr); align-content: start; gap: var(--space-4); padding: var(--space-5) var(--space-6); }
    h1 { font-size: var(--title-l); }
    .message { margin: 0; padding: var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .pick { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-4); }
    .pick ui-field { width: 300px; }
    .wrap { border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); overflow: auto; }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-s); }
    th { height: 34px; padding: 0 var(--space-3); background: var(--surface-2); color: var(--muted); font-size: var(--text-xs); text-align: left; }
    td { height: 34px; padding: 0 var(--space-3); border-top: 1px solid var(--line); }
    .c { width: 100px; text-align: center; }
    .entity { font-weight: 500; }
  `,
})
export class Rights {
  private readonly config = inject(ConfigApi);
  private readonly entities = inject(EntityApi);
  private readonly documents = inject(DocumentApi);
  private readonly rights = inject(RightsService);
  private readonly toasts = inject(Toasts);

  protected readonly admin = this.rights.isAdministrator;
  protected readonly flags: Flag[] = ['readAll', 'readOwn', 'write', 'forward'];
  protected readonly kinds: Chip<Kind>[] = [{ value: 'roles', label: t('Roles') }, { value: 'users', label: t('Users') }];
  protected readonly kind = signal<Kind>('roles');
  protected readonly who = signal<string | null>(null);
  protected readonly users = signal<SelectOption[]>([]);
  protected readonly roles = signal<SelectOption[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly saving = signal(false);
  private readonly catalogue = signal<{ table: string; entity: string; categories: Category[] }[]>([]);
  protected readonly rows = signal<RightRow[]>([]);
  protected readonly views: Chip<View>[] = [{ value: 'registers', label: t('Registers') }, { value: 'menu', label: t('Menu') }];
  protected readonly view = signal<View>('registers');
  protected readonly menu = signal<MenuRight[]>([]);
  protected readonly given = signal<ReadonlySet<string>>(new Set());
  protected readonly tree = computed(() => menuTree(this.menu()));
  protected readonly expanded = signal<string[]>([]);
  protected readonly menuId = (row: { id: string }) => row.id;
  protected readonly menuColumns: Column<MenuRight>[] = [{ key: 'name', label: t('Menu') }, { key: 'given', label: t('May see'), width: '110px', align: 'center' }];

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      await this.rights.load();
      // Rights are given on every register through its own elements row (each pack has one per register); the names come from those rows
      // where the lookups list them (documents have no lookup), else from the register.
      const scopes = await this.entities.lookupScopes().catch(() => []);
      const named = new Map(scopes.filter(s => !s.categoryId && !s.typeId && s.name).map(s => [s.table, s.name]));
      const registers = [...Object.values(REGISTERS).map(r => ({ table: r.table, load: () => this.entities.categories(r) })),
        { table: 'tbl_document', load: () => this.documents.categories() }];
      const catalogue = await Promise.all(registers.map(async r => ({ table: r.table, entity: named.get(r.table) ?? REGISTER_NAMES[r.table] ?? r.table,
        categories: await r.load().catch(() => []) })));
      this.catalogue.set(catalogue.filter(e => e.categories.length));
      const roles = catalogue.length ? await this.entities.categories(REGISTERS['users']) : [];
      this.roles.set(roles.filter(r => r.isElement).map(r => ({ value: r.id!, label: textIn(r.name) })));
      this.users.set((await this.entities.users()).map(u => ({ value: u.id, label: u.name, hint: u.code ?? undefined })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected chooseKind(kind: Kind | undefined): void {
    if (kind) {
      this.kind.set(kind);
      this.who.set(null);
    }
  }

  protected async choose(id: string | null): Promise<void> {
    this.who.set(id);
    if (!id) {
      return;
    }
    try {
      this.rows.set(rowsOf(this.catalogue(), await this.config.rights(this.kind(), id)));
      await this.loadMenu(id);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async loadMenu(id: string): Promise<void> {
    const menu = await this.config.menuRights<MenuRight>(this.kind(), id);
    this.menu.set(menu);
    this.given.set(new Set(menu.filter(m => m.given).map(m => m.id)));
    this.expanded.set(menu.filter(m => !m.isElement).map(m => m.id));
  }

  protected covered(id: string): boolean {
    return comesWith(this.menu(), id, this.given());
  }

  protected give(id: string, value: boolean): void {
    this.given.update(all => {
      const next = new Set(all);
      if (value) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected tick(i: number, flag: Flag, value: boolean): void {
    this.rows.update(rows => rows.map((r, n) => (n === i ? { ...r, [flag]: value } : r)));
  }

  protected async save(): Promise<void> {
    const id = this.who();
    if (!id) {
      return;
    }
    this.saving.set(true);
    try {
      this.rows.set(rowsOf(this.catalogue(), await this.config.saveRights(this.kind(), id, rightsOf(this.rows()))));
      await this.config.saveMenuRights(this.kind(), id, [...this.given()]);
      await this.loadMenu(id);
      // The signed-in user's own rights may have changed.
      this.rights.reset();
      this.toasts.good(t('Rights saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.saving.set(false);
    }
  }
}
