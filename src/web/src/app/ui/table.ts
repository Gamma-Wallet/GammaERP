import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy, Component, Directive, TemplateRef, computed, contentChildren, effect, inject, input, model, output,
} from '@angular/core';
import { text } from '../core/text';
import { Checkbox } from './controls';
import { formatDate, formatNumber } from './format';
import { Icon } from './icon';

/** How a column shows its value: amounts with 2 decimals, quantities with the column's decimals, dates in the screens' format. */
export type ColumnFormat = 'text' | 'number' | 'date' | 'datetime';

export interface Column<T> {
  key: string;
  label: string;
  /** The value shown and sorted by; default row[key]. */
  value?: (row: T) => unknown;
  format?: ColumnFormat;
  /** Decimals for a number column (default 2). */
  decimals?: number;
  /** Numbers line up on the right by default; everything else on the left. */
  align?: 'start' | 'end' | 'center';
  /** A CSS width: "120px", "20%". */
  width?: string;
  sortable?: boolean;
}

export interface Sort {
  key: string;
  direction: 'asc' | 'desc';
}

export function cellValue<T>(row: T, column: Column<T>): unknown {
  return column.value ? column.value(row) : (row as Record<string, unknown>)[column.key];
}

export function cellText<T>(row: T, column: Column<T>): string {
  const value = cellValue(row, column);
  switch (column.format) {
    case 'number':
      return formatNumber(value as number | string | null, column.decimals ?? 2);
    case 'date':
      return formatDate(value as string | null);
    case 'datetime':
      return formatDate(value as string | null, true);
    default:
      return value === null || value === undefined ? '' : String(value);
  }
}

export function alignOf<T>(column: Column<T>): 'start' | 'end' | 'center' {
  return column.align ?? (column.format === 'number' ? 'end' : 'start');
}

const collator = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** The rows in the sort's order: numbers by value, texts as people read them (A2 before A10), empty values always last. */
export function sortRows<T>(rows: readonly T[], columns: readonly Column<T>[], sort: Sort | null): T[] {
  const column = sort && columns.find(c => c.key === sort.key);
  if (!sort || !column) {
    return [...rows];
  }
  const sign = sort.direction === 'asc' ? 1 : -1;
  const numeric = column.format === 'number';
  return rows
    .map((row, index) => ({ row, index, value: cellValue(row, column) }))
    .sort((a, b) => {
      const empty = (v: unknown) => v === null || v === undefined || v === '';
      if (empty(a.value) || empty(b.value)) {
        return empty(a.value) === empty(b.value) ? a.index - b.index : empty(a.value) ? 1 : -1;
      }
      const order = numeric ? Number(a.value) - Number(b.value) : collator.compare(String(a.value), String(b.value));
      return order * sign || a.index - b.index;
    })
    .map(x => x.row);
}

/** The next sort when a column's heading is clicked: ascending, then descending, then the rows' own order. */
export function nextSort(sort: Sort | null, key: string): Sort | null {
  if (sort?.key !== key) {
    return { key, direction: 'asc' };
  }
  return sort.direction === 'asc' ? { key, direction: 'desc' } : null;
}

/** A cell drawn by the screen: <ng-template uiCell="amount" let-row let-value="value">…</ng-template> inside a table. */
@Directive({ selector: 'ng-template[uiCell]' })
export class CellTemplate {
  readonly key = input.required<string>({ alias: 'uiCell' });
  readonly template = inject<TemplateRef<{ $implicit: unknown; value: unknown }>>(TemplateRef);
}

const DEFAULT_ROW_ID = (row: unknown) => String((row as { id?: unknown }).id ?? '');

/**
 * A dense data table. Sorting by a heading click ([(sort)]): the table sorts the rows itself, or with [sortLocally]="false" only reports the
 * sort so the API can. Paging: with [total] the rows are one page from the API and [(page)] tells it which; without, the table pages them.
 * [selectable] adds row checkboxes ([(selected)] holds the row ids); [footer] is a totals row (column key → text).
 */
@Component({
  selector: 'ui-table',
  imports: [NgTemplateOutlet, Checkbox, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './table.html',
  styleUrl: './table.scss',
})
export class Table<T> {
  protected readonly text = text;
  protected readonly cellText = cellText;
  protected readonly alignOf = alignOf;

  readonly columns = input.required<readonly Column<T>[]>();
  readonly rows = input<readonly T[]>([]);
  readonly rowId = input<(row: T) => string>(DEFAULT_ROW_ID);
  readonly sort = model<Sort | null>(null);
  readonly sortLocally = input(true);
  readonly selectable = input(false);
  readonly selected = model<string[]>([]);
  readonly page = model(1);
  readonly pageSize = input(50);
  /** All the rows there are, when the rows given are one page from the API. */
  readonly total = input<number | null>(null);
  readonly loading = input(false);
  readonly emptyText = input<string | null>(null);
  readonly footer = input<Record<string, string | undefined> | null>(null);
  /** The row shown as current (the one open beside the list). */
  readonly activeId = input<string | null>(null);
  readonly ariaLabel = input<string | null>(null);
  readonly rowClick = output<T>();

  private readonly cells = contentChildren(CellTemplate);
  protected readonly templates = computed(() => new Map(this.cells().map(c => [c.key(), c.template])));

  private readonly sorted = computed(() => (this.sortLocally() ? sortRows(this.rows(), this.columns(), this.sort()) : [...this.rows()]));
  protected readonly count = computed(() => this.total() ?? this.rows().length);
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.count() / this.pageSize())));
  protected readonly shownRows = computed(() => {
    if (this.total() !== null) {
      return this.sorted();
    }
    const start = (this.page() - 1) * this.pageSize();
    return this.sorted().slice(start, start + this.pageSize());
  });
  protected readonly range = computed(() => {
    const from = this.count() ? (this.page() - 1) * this.pageSize() + 1 : 0;
    return text.ui.rowsOf(from, Math.min(this.page() * this.pageSize(), this.count()), this.count());
  });
  protected readonly allChosen = computed(() => {
    const ids = this.shownRows().map(this.rowId());
    return ids.length > 0 && ids.every(id => this.selected().includes(id));
  });
  protected readonly someChosen = computed(() => !this.allChosen() && this.shownRows().some(r => this.selected().includes(this.rowId()(r))));

  constructor() {
    // Rows that shrink (a new filter) never leave the table on a page past the end.
    effect(() => {
      if (this.page() > this.pages()) {
        this.page.set(this.pages());
      }
    });
  }

  protected sortBy(column: Column<T>): void {
    if (!column.sortable) {
      return;
    }
    this.sort.set(nextSort(this.sort(), column.key));
    if (this.total() === null) {
      this.page.set(1);
    }
  }

  protected ariaSort(column: Column<T>): string | null {
    if (!column.sortable) {
      return null;
    }
    const sort = this.sort();
    return sort?.key === column.key ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none';
  }

  protected isChosen(row: T): boolean {
    return this.selected().includes(this.rowId()(row));
  }

  protected choose(row: T, chosen: boolean): void {
    const id = this.rowId()(row);
    const rest = this.selected().filter(x => x !== id);
    this.selected.set(chosen ? [...rest, id] : rest);
  }

  protected chooseAll(chosen: boolean): void {
    const ids = this.shownRows().map(this.rowId());
    const rest = this.selected().filter(id => !ids.includes(id));
    this.selected.set(chosen ? [...rest, ...ids] : rest);
  }

  protected goTo(page: number): void {
    this.page.set(Math.min(Math.max(page, 1), this.pages()));
  }
}

/** One row of a tree as shown: its depth and whether it has children. */
export interface TreeLine<T> {
  row: T;
  id: string;
  depth: number;
  hasChildren: boolean;
  expanded: boolean;
}

/** The rows a tree shows: every top row, and the children of each expanded row under it, depth first. */
export function flattenTree<T>(rows: readonly T[], idOf: (row: T) => string, childrenOf: (row: T) => readonly T[] | null | undefined,
  expanded: ReadonlySet<string>, depth = 0): TreeLine<T>[] {
  const lines: TreeLine<T>[] = [];
  for (const row of rows) {
    const id = idOf(row);
    const children = childrenOf(row) ?? [];
    const open = expanded.has(id);
    lines.push({ row, id, depth, hasChildren: children.length > 0, expanded: open });
    if (open && children.length) {
      lines.push(...flattenTree(children, idOf, childrenOf, expanded, depth + 1));
    }
  }
  return lines;
}

/** The ids of every row with children: to expand a whole tree. */
export function parentIds<T>(rows: readonly T[], idOf: (row: T) => string, childrenOf: (row: T) => readonly T[] | null | undefined): string[] {
  return rows.flatMap(row => {
    const children = childrenOf(row) ?? [];
    return children.length ? [idOf(row), ...parentIds(children, idOf, childrenOf)] : [];
  });
}

const DEFAULT_CHILDREN = (row: unknown) => (row as { children?: unknown[] }).children ?? [];

/**
 * A table of a tree (a chart of accounts, categories): the first column is indented by depth with a toggle; [(expanded)] holds the open
 * rows' ids. Same columns, cell templates and footer as ui-table; it keeps the tree's own order.
 */
@Component({
  selector: 'ui-tree-table',
  imports: [NgTemplateOutlet, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap">
      <table role="treegrid" [attr.aria-label]="ariaLabel()">
        <thead>
          <tr>
            @for (column of columns(); track column.key) {
              <th scope="col" [style.width]="column.width" [class]="alignOf(column)">{{ column.label }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (line of lines(); track line.id) {
            <tr [attr.aria-level]="line.depth + 1" [attr.aria-expanded]="line.hasChildren ? line.expanded : null"
              [class.active]="line.id === activeId()" [class.parent]="line.hasChildren" (click)="rowClick.emit(line.row)">
              @for (column of columns(); track column.key; let first = $first) {
                <td [class]="alignOf(column)">
                  @if (first) {
                    <span class="tree-cell" [style.padding-left.px]="line.depth * 20">
                      @if (line.hasChildren) {
                        <button type="button" class="toggle" [attr.aria-label]="line.expanded ? text.ui.collapse : text.ui.expand"
                          (click)="$event.stopPropagation(); toggle(line.id)">
                          <ui-icon [name]="line.expanded ? 'chevron-down' : 'chevron-right'" [size]="14" />
                        </button>
                      } @else if (hasTree()) {
                        <span class="toggle-space"></span>
                      }
                      <ng-container *ngTemplateOutlet="cell; context: { line, column }" />
                    </span>
                  } @else {
                    <ng-container *ngTemplateOutlet="cell; context: { line, column }" />
                  }
                </td>
              }
            </tr>
          } @empty {
            <tr><td class="empty" [attr.colspan]="columns().length">{{ loading() ? text.ui.loading : (emptyText() ?? text.ui.noRows) }}</td></tr>
          }
        </tbody>
        @if (footer(); as footer) {
          <tfoot>
            <tr>
              @for (column of columns(); track column.key) { <td [class]="alignOf(column)">{{ footer[column.key] ?? '' }}</td> }
            </tr>
          </tfoot>
        }
      </table>
    </div>
    <ng-template #cell let-line="line" let-column="column">
      @if (templates().get(column.key); as template) {
        <ng-container *ngTemplateOutlet="template; context: { $implicit: line.row, value: cellText(line.row, column) }" />
      } @else {
        {{ cellText(line.row, column) }}
      }
    </ng-template>
  `,
  styleUrl: './table.scss',
})
export class TreeTable<T> {
  protected readonly text = text;
  protected readonly cellText = cellText;
  protected readonly alignOf = alignOf;

  readonly columns = input.required<readonly Column<T>[]>();
  readonly rows = input<readonly T[]>([]);
  readonly rowId = input<(row: T) => string>(DEFAULT_ROW_ID);
  readonly childrenOf = input<(row: T) => readonly T[] | null | undefined>(DEFAULT_CHILDREN as (row: T) => readonly T[]);
  readonly expanded = model<string[]>([]);
  readonly loading = input(false);
  readonly emptyText = input<string | null>(null);
  readonly footer = input<Record<string, string | undefined> | null>(null);
  readonly activeId = input<string | null>(null);
  readonly ariaLabel = input<string | null>(null);
  readonly rowClick = output<T>();

  private readonly cells = contentChildren(CellTemplate);
  protected readonly templates = computed(() => new Map(this.cells().map(c => [c.key(), c.template])));
  protected readonly lines = computed(() => flattenTree(this.rows(), this.rowId(), this.childrenOf(), new Set(this.expanded())));
  /** Whether any row has children: a flat list keeps no indent for a toggle. */
  protected readonly hasTree = computed(() => this.rows().some(row => (this.childrenOf()(row)?.length ?? 0) > 0));

  protected toggle(id: string): void {
    const open = this.expanded();
    this.expanded.set(open.includes(id) ? open.filter(x => x !== id) : [...open, id]);
  }

  /** Opens every row, or closes them all. */
  expandAll(open: boolean): void {
    this.expanded.set(open ? parentIds(this.rows(), this.rowId(), this.childrenOf()) : []);
  }
}
