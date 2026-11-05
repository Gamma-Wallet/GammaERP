import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfigApi, ConfigRow, ConfigSchema } from '../../core/config';
import { RightsService, buildTree, errorsOf } from '../../core/entities';
import { Button, Input } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { formatDate, formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Column, Table, TreeTable } from '../../ui/table';
import { ConfigRowData, ConfigRowDialog } from './config-row-dialog';
import { codedText, columnLabel } from './labels';

/** A row as the table shows it: every column as text, links by the name of what they point at. */
export interface ShownRow {
  id: string;
  row: ConfigRow;
  cells: Record<string, string>;
  children: ShownRow[];
}

/** The columns a table shows: the code and name first, then the others, at most 7; fixed ones are left out. */
export function shownColumns(schema: ConfigSchema, fixed: Record<string, string>): string[] {
  const names = schema.columns.filter(c => !(c.name in fixed) && c.kind !== 'json' && c.name !== 'idSup').map(c => c.name);
  const first = ['code', 'name'].filter(n => names.includes(n));
  return [...first, ...names.filter(n => !first.includes(n))].slice(0, 7);
}

/** A cell's text: a translated text in English, numbers and dates formatted, yes / no, coded values by name, links by their target's name. */
export function cellOf(model: string, schema: ConfigSchema, column: string, value: unknown, names: Record<string, Map<string, string>>): string {
  const kind = schema.columns.find(c => c.name === column)?.kind;
  const coded = codedText(model, column, value);
  if (coded !== null) {
    return coded;
  }
  if (value === null || value === undefined) {
    return '';
  }
  switch (kind) {
    case 'translated':
      return textIn(value as Record<string, string>);
    case 'number':
      return formatNumber(value as number);
    case 'boolean':
      return value ? t('Yes') : t('No');
    case 'date':
      return formatDate(String(value));
    case 'id':
      return names[column]?.get(String(value)) ?? String(value).slice(0, 8);
    default:
      return String(value);
  }
}

/**
 * The rows of a configuration model (all of them, or those of one parent when columns are fixed, e.g. a document type's), as a table or, for a
 * model with parents (the menu), a tree; a row opens its form. Administrators add and change rows, everyone else reads them.
 */
@Component({
  selector: 'app-config-table',
  imports: [TranslatePipe, Button, Icon, Input, Table, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar">
      <div class="search">
        <ui-icon name="search" />
        <input uiInput type="search" [placeholder]="'Search' | t" [attr.aria-label]="'Search' | t" (input)="search.set($any($event.target).value)" />
      </div>
      <span class="muted figures">{{ '{0} rows' | t: (shown().length) }}</span>
      <span class="spacer"></span>
      @if (canEdit() && schema()) {
        <button uiButton variant="primary" size="small" (click)="open(null)"><ui-icon name="plus" [size]="14" />{{ 'Add' | t }}</button>
      }
    </div>
    @if (message()) { <p class="message" role="alert">{{ message() }}</p> }
    @if (isTree()) {
      <ui-tree-table [columns]="columns()" [rows]="tree()" [(expanded)]="expanded" [loading]="loading()" (rowClick)="open($any($event))" />
    } @else {
      <ui-table [columns]="columns()" [rows]="shown()" [pageSize]="100" [loading]="loading()" (rowClick)="open($any($event))" [emptyText]="'No rows yet.' | t" />
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); }
    .bar { display: flex; align-items: center; gap: var(--space-3); }
    .search { position: relative; width: 260px; }
    .search ui-icon { position: absolute; top: 10px; left: 10px; color: var(--muted); }
    .search input { padding-left: 32px; }
    .spacer { flex: 1; }
    .message { margin: 0; padding: var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
  `,
})
export class ConfigTable {
  private readonly api = inject(ConfigApi);
  private readonly dialogs = inject(Dialogs);
  private readonly rights = inject(RightsService);

  readonly model = input.required<string>();
  readonly fixed = input<Record<string, string>>({});

  protected readonly schema = signal<ConfigSchema | null>(null);
  protected readonly rows = signal<ConfigRow[]>([]);
  protected readonly names = signal<Record<string, Map<string, string>>>({});
  protected readonly loading = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly expanded = signal<string[]>([]);
  protected readonly canEdit = this.rights.isAdministrator;

  protected readonly isTree = computed(() => !!this.schema()?.columns.some(c => c.name === 'idSup' && c.linkModel === this.model()));
  protected readonly columns = computed<Column<ShownRow>[]>(() => {
    const schema = this.schema();
    if (!schema) {
      return [];
    }
    return shownColumns(schema, this.fixed()).map(name => {
      const kind = schema.columns.find(c => c.name === name)?.kind;
      return { key: name, label: columnLabel(name, this.model()), value: (r: ShownRow) => r.cells[name], align: kind === 'number' || kind === 'integer' ? 'end' : 'start', sortable: true };
    });
  });
  protected readonly shown = computed<ShownRow[]>(() => {
    const schema = this.schema();
    if (!schema) {
      return [];
    }
    const term = this.search().trim().toLowerCase();
    return this.rows().map(row => ({
      id: String(row.id),
      row,
      cells: Object.fromEntries(schema.columns.map(c => [c.name, cellOf(this.model(), schema, c.name, row[c.name], this.names())])),
      children: [],
    })).filter(r => !term || Object.values(r.cells).some(v => v.toLowerCase().includes(term)));
  });
  protected readonly tree = computed<ShownRow[]>(() => {
    const byId = new Map(this.shown().map(r => [r.id, r]));
    const nodes = buildTree(this.shown().map(r => ({ id: r.id, idSup: (r.row['idSup'] as string | null) ?? null, active: true,
      sortOrder: Number(r.row['sortOrder'] ?? 0) })));
    const toShown = (list: typeof nodes): ShownRow[] => list.map(n => ({ ...byId.get(n.id)!, children: toShown(n.children) }));
    return toShown(nodes);
  });

  constructor() {
    effect(() => {
      const model = this.model();
      const fixed = this.fixed();
      untracked(() => void this.load(model, fixed));
    });
  }

  private async load(model: string, fixed: Record<string, string>): Promise<void> {
    this.loading.set(true);
    this.message.set(null);
    try {
      await this.rights.load();
      const [schema, rows] = await Promise.all([this.api.schema(model), this.api.list(model, fixed)]);
      this.schema.set(schema);
      this.rows.set(rows);
      await this.loadNames(model, schema, rows);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  /** The names of what the shown link columns point at, so a cell shows a name rather than an id. */
  private async loadNames(model: string, schema: ConfigSchema, rows: ConfigRow[]): Promise<void> {
    const shown = new Set(shownColumns(schema, this.fixed()));
    const links = schema.columns.filter(c => c.isLink && shown.has(c.name) && rows.some(r => r[c.name]));
    const names: Record<string, Map<string, string>> = {};
    await Promise.all(links.map(async c => {
      const choices = await this.api.choices(model, c.name).catch(() => []);
      names[c.name] = new Map(choices.map(x => [x.id, x.code ? `${x.code} · ${x.name}` : x.name]));
    }));
    this.names.set(names);
  }

  protected async open(shown: ShownRow | null): Promise<void> {
    const schema = this.schema();
    if (!schema) {
      return;
    }
    const data: ConfigRowData = { model: this.model(), schema, row: shown?.row ?? null, fixed: this.fixed(), readOnly: !this.canEdit() };
    const changed = await firstValueFrom(this.dialogs.open<boolean, ConfigRowData, ConfigRowDialog>(ConfigRowDialog, data).closed);
    if (changed) {
      await this.load(this.model(), this.fixed());
    }
  }
}
