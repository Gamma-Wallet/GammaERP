import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { DocumentApi, DocumentFilter, DocumentListItem } from '../../core/documents';
import { save } from '../../core/operations';
import { ElementType, EntityApi, errorsOf } from '../../core/entities';
import { itemAt } from '../../core/menu';
import { SessionService } from '../../core/session';
import { Chip, Chips } from '../../ui/chips';
import { Button, Field, Input } from '../../ui/controls';
import { textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Status } from '../../ui/status';
import { CellTemplate, Column, Sort, Table } from '../../ui/table';

/** The list's columns by the API's sort keys (DocumentQuery.SortColumns); the others are not sortable. */
export const SORT_KEYS: Record<string, string> = { code: 'code', postingDate: 'postingDate', fromName: 'from', toName: 'to', externalNumber: 'externalNumber', total: 'total' };

/** The API's sort for a column sort of the list: by its key, or the newest first. */
export function apiSort(sort: Sort | null): { sort: string | null; descending: boolean } {
  return sort && SORT_KEYS[sort.key] ? { sort: SORT_KEYS[sort.key], descending: sort.direction === 'desc' } : { sort: null, descending: false };
}

type Posted = 'draft' | 'posted';

/**
 * The documents of a category: its types as chips, filters on the columns, the API's sort and pages,
 * and a new document of one of the types the user was given.
 */
@Component({
  selector: 'app-document-list',
  imports: [TranslatePipe, Button, CdkMenu, CdkMenuItem, CdkMenuTrigger, CellTemplate, Chips, Field, Icon, Input, Select, Status, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ title() }}</h1>
      <button uiButton variant="quiet" [disabled]="!lastQuery()" (click)="excel()"><ui-icon name="file-text" />{{ 'Excel' | t }}</button>
      @if (types().length === 1) {
        <button uiButton variant="primary" (click)="create(types()[0])"><ui-icon name="plus" />{{ 'New {0}' | t: (nameOf(types()[0])) }}</button>
      } @else if (types().length > 1) {
        <button uiButton variant="primary" [cdkMenuTriggerFor]="newMenu"><ui-icon name="plus" />{{ 'New' | t }}<ui-icon name="chevron-down" [size]="14" /></button>
        <ng-template #newMenu>
          <div class="menu-panel" cdkMenu>
            @for (t of types(); track t.id) {
              <button class="menu-item" cdkMenuItem (cdkMenuItemTriggered)="create(t)"><span class="code">{{ t.code }}</span>{{ nameOf(t) }}</button>
            }
          </div>
        </ng-template>
      }
    </div>

    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }

    <div class="filters">
      @if (typeChips().length > 1) {
        <ui-chips [chips]="typeChips()" [multiple]="false" [showAll]="true" [chosen]="typeFilter()" (chosenChange)="chooseType($event)" [ariaLabel]="'Type' | t" />
      }
      <ui-chips [chips]="postedChips" [multiple]="false" [showAll]="true" [chosen]="postedFilter()" (chosenChange)="choosePosted($event)" [ariaLabel]="'State' | t" />
      <button uiButton variant="quiet" size="small" [attr.aria-expanded]="more()" (click)="toggleMore()">{{ more() ? ('Fewer filters' | t) : moreLabel() }}</button>
    </div>
    <div class="filters fields">
      <ui-field [label]="'Search' | t"><input uiInput type="search" [placeholder]="'Number or name' | t" [value]="query().search ?? ''" (input)="typed({ search: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'From' | t"><input uiInput [value]="query().from ?? ''" (input)="typed({ from: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'To' | t"><input uiInput [value]="query().to ?? ''" (input)="typed({ to: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Posted from' | t"><input uiInput type="date" [value]="query().postedFrom ?? ''" (change)="set({ postedFrom: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Posted to' | t"><input uiInput type="date" [value]="query().postedTo ?? ''" (change)="set({ postedTo: $any($event.target).value })" /></ui-field>
    </div>
    <div class="filters fields" [style.display]="more() ? null : 'none'">
      <ui-field [label]="'Number' | t"><input uiInput [value]="query().code ?? ''" (input)="typed({ code: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Their number' | t"><input uiInput [value]="query().externalNumber ?? ''" (input)="typed({ externalNumber: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Dated from' | t"><input uiInput type="date" [value]="query().datedFrom ?? ''" (change)="set({ datedFrom: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Dated to' | t"><input uiInput type="date" [value]="query().datedTo ?? ''" (change)="set({ datedTo: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Total from' | t"><input uiInput type="number" [value]="query().totalFrom ?? ''" (input)="typed({ totalFrom: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Total to' | t"><input uiInput type="number" [value]="query().totalTo ?? ''" (input)="typed({ totalTo: $any($event.target).value })" /></ui-field>
      <ui-field [label]="'Written by' | t"><ui-select [options]="users()" [searchable]="true" [value]="query().createdBy ?? null" (valueChange)="set({ createdBy: $event })" /></ui-field>
    </div>

    <ui-table class="table" [columns]="columns" [rows]="rows()" [total]="total()" [page]="query().page" (pageChange)="set({ page: $event }, false)"
      [pageSize]="query().pageSize" [sortLocally]="false" [sort]="sort()" (sortChange)="sortBy($event)" [loading]="loading()" [ariaLabel]="title()"
      [emptyText]="'No documents match.' | t" (rowClick)="open($event)">
      <ng-template uiCell="state" let-row>
        @if ($any(row).reversedDocumentCode) {
          <ui-status kind="good">{{ 'Reversal of {0}' | t: ($any(row).reversedDocumentCode) }}</ui-status>
        } @else if ($any(row).posted) {
          <ui-status kind="good">{{ 'Posted' | t }}</ui-status>
        } @else {
          <ui-status kind="info">{{ 'Draft' | t }}</ui-status>
        }
      </ng-template>
    </ui-table>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--space-3); height: 100%; min-height: 0; padding: var(--space-5) var(--space-6); }
    .head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
    h1 { font-size: var(--title-l); }
    .message { display: flex; align-items: center; gap: var(--space-2); margin: 0; padding: var(--space-3); border-radius: var(--radius);
      background: var(--critical-bg); color: var(--critical); }
    .filters { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-3); }
    ui-chips + ui-chips { margin-left: var(--space-3); }
    .fields ui-field { width: 180px; }
    .table { flex: 1; }
    .code { min-width: 48px; color: var(--muted); font-size: var(--text-s); }
  `,
})
export class DocumentList {
  private readonly api = inject(DocumentApi);
  private readonly entities = inject(EntityApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  /** The category's id (documents/:category). */
  readonly category = input.required<string>();
  /** The document type the menu's tree opened (query parameter type): the list shows only it. */
  readonly type = input<string>();
  protected readonly more = signal(false);
  /** The closed panel says how many of its filters still apply. */
  protected readonly moreLabel = computed(() => {
    const query = this.query();
    const applied = [query.code, query.externalNumber, query.datedFrom, query.datedTo, query.totalFrom, query.totalTo, query.createdBy].filter(Boolean).length;
    return applied ? t('More filters ({0})', applied) : t('More filters');
  });
  protected readonly users = signal<SelectOption[]>([]);

  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)), {
    initialValue: this.router.url,
  });
  protected readonly title = computed(() => itemAt(this.session.menu(), this.url())?.name ?? 'Documents');

  protected readonly types = signal<ElementType[]>([]);
  protected readonly rows = signal<DocumentListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly query = signal<DocumentFilter>({ page: 1, pageSize: 50 });
  protected readonly sort = signal<Sort | null>(null);
  protected readonly typeFilter = signal<string[]>([]);
  protected readonly postedFilter = signal<Posted[]>([]);
  protected readonly postedChips: Chip<Posted>[] = [{ value: 'draft', label: t('Drafts') }, { value: 'posted', label: t('Posted') }];
  protected readonly typeChips = computed<Chip[]>(() => this.types().map(t => ({ value: t.id!, label: t.code ?? textIn(t.name) })));
  protected readonly columns: Column<DocumentListItem>[] = [
    { key: 'code', label: t('Number'), width: '120px', sortable: true },
    { key: 'postingDate', label: t('Posting date'), format: 'date', width: '120px', sortable: true },
    { key: 'fromName', label: t('From'), sortable: true },
    { key: 'toName', label: t('To'), sortable: true },
    { key: 'externalNumber', label: t('Their number'), width: '130px', sortable: true },
    { key: 'currencyCode', label: t('Currency'), width: '90px' },
    { key: 'exchangeRate', label: t('Rate'), format: 'number', decimals: 4, width: '100px' },
    { key: 'value', label: t('Net'), format: 'number', width: '120px' },
    { key: 'vat', label: t('VAT'), format: 'number', width: '110px' },
    { key: 'total', label: t('Total'), format: 'number', width: '130px', sortable: true },
    { key: 'state', label: t('State'), width: '170px' },
  ];

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const category = this.category();
      const type = this.type();
      untracked(() => {
        this.query.set({ page: 1, pageSize: 50, ...(type ? { type } : {}) });
        this.typeFilter.set(type ? [type] : []);
        this.postedFilter.set([]);
        this.sort.set(null);
        void this.loadTypes(category);
      });
    });
    effect(() => {
      const category = this.category();
      const query = this.query();
      untracked(() => void this.load(category, query));
    });
  }

  private async loadTypes(category: string): Promise<void> {
    try {
      // The top-level types, as the menu offers them (document types do not use is_element).
      this.types.set((await this.api.allowedTypes(category)).filter(t => !t.idSup));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  /** The list as last shown, for its Excel sheet. */
  protected readonly lastQuery = signal<{ category: string; query: DocumentFilter } | null>(null);

  protected async excel(): Promise<void> {
    const last = this.lastQuery();
    if (!last) {
      return;
    }
    try {
      save(await this.api.excel(last.category, last.query), `${this.title() || 'documents'}.xlsx`);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async load(category: string, query: DocumentFilter): Promise<void> {
    this.lastQuery.set({ category, query });
    this.loading.set(true);
    try {
      const page = await this.api.list(category, query);
      this.rows.set(page.items);
      this.total.set(page.total);
      this.message.set(null);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  /** The further filters, and the users they choose from (read the first time they open). */
  protected toggleMore(): void {
    this.more.update(open => !open);
    if (this.more() && !this.users().length) {
      void this.loadUsers();
    }
  }

  private async loadUsers(): Promise<void> {
    try {
      const scope = (await this.entities.lookupScopes()).find(s => s.table === 'tbl_user' && !s.categoryId && !s.typeId);
      if (scope) {
        this.users.set((await this.entities.lookup(scope.id, '', 500)).items.map(o => ({ value: o.id, label: o.name || o.code || o.id })));
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  /** A filter changed: back to the first page, unless only the page changed. */
  protected set(change: Partial<DocumentFilter>, firstPage = true): void {
    this.query.update(q => ({ ...q, ...change, ...(firstPage ? { page: 1 } : {}) }));
  }

  protected typed(change: Partial<DocumentFilter>): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.set(change), 300);
  }

  protected chooseType(chosen: string[]): void {
    this.typeFilter.set(chosen);
    this.set({ type: chosen.at(0) ?? null });
  }

  protected choosePosted(chosen: Posted[]): void {
    this.postedFilter.set(chosen);
    this.set({ posted: chosen[0] === 'posted' ? true : chosen[0] === 'draft' ? false : null });
  }

  protected sortBy(sort: Sort | null): void {
    this.sort.set(sort);
    this.set(apiSort(sort));
  }

  protected nameOf(type: ElementType): string {
    return textIn(type.name);
  }

  protected create(type: ElementType): void {
    void this.router.navigate(['/documents', this.category(), 'new', type.id]);
  }

  protected open(row: DocumentListItem): void {
    void this.router.navigate(['/documents', this.category(), row.id]);
  }
}
