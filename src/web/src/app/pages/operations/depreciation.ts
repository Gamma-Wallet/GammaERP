import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { DocumentApi } from '../../core/documents';
import { EntityApi, errorsOf } from '../../core/entities';
import { AssetRow, AssetRunRow, DepreciationBook, DepreciationLine, DepreciationRun, OperationsApi } from '../../core/operations';
import { Button, Field, Input } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { formatDate, formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Kpi } from '../../ui/status';
import { CellTemplate, Column, Table } from '../../ui/table';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { registerOptions, today } from './register-options';

/** The run's total and the book values before and after it. */
export function runTotals(lines: readonly DepreciationLine[]): { amount: number; before: number; after: number } {
  return lines.reduce((t, l) => ({ amount: t.amount + l.amount, before: t.before + l.bookValueBefore, after: t.after + l.bookValueAfter }), { amount: 0, before: 0, after: 0 });
}

/** What undoing a run does, for the question before it: a posted journal is reversed, an unposted one goes, without one the run just goes. */
export function undoMessage(run: Pick<DepreciationRun, 'date' | 'document' | 'posted'>): string {
  const day = formatDate(run.date);
  if (!run.document) {
    return t('The run of {0} is removed and its assets go back to their book value before it.', day);
  }
  return run.posted ? t('Journal {0} is reversed on {1} and the assets go back to their book value before it.', run.document, day)
    : t('Journal {0}, not posted, is removed with the run, and the assets go back to their book value before it.', run.document);
}

/**
 * Depreciation by book: a run up to a date previewed, then recorded,
 * then its journal made as a document of the chosen type (posted when the book posts); the book's assets with their history; its past runs, the
 * latest of which can be undone.
 */
@Component({
  selector: 'app-depreciation',
  imports: [TranslatePipe, Button, CellTemplate, Field, Icon, Input, Kpi, Select, Table, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Depreciation' | t }}</h1></div>
    <section class="form">
      <ui-field [label]="'Book' | t" [required]="true"><ui-select [options]="bookOptions()" [value]="book()" (valueChange)="chooseBook($event)" /></ui-field>
      @if (currentBook(); as b) { <p class="muted">{{ b.method || 'No method' }} · {{ b.posts ? 'journals are posted' : 'journals are not posted' }}</p> }
    </section>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (book()) {
      <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'Depreciation' | t" />
      @switch (tab()) {
        @case ('assets') {
          <ui-table [columns]="assetColumns" [rows]="assetRows()" [pageSize]="100" [ariaLabel]="'Assets' | t" [emptyText]="'No asset on this book yet.' | t" [activeId]="chosenAsset()?.assetValueId ?? null"
            (rowClick)="openAsset($any($event))" />
          @if (chosenAsset(); as a) {
            <h2>{{ '{0}: its depreciation' | t: (a.product) }}</h2>
            @if (!a.sold) {
              <div class="actions"><button uiButton [disabled]="busy()" (click)="setDisposed(a, !a.disposed)">{{ (a.disposed ? 'Back in use' : 'Mark disposed') | t }}</button></div>
            }
            <ui-table [columns]="historyColumns" [rows]="historyRows()" [pageSize]="100" [ariaLabel]="'Asset history' | t" [emptyText]="'Not depreciated yet.' | t" (rowClick)="openJournal($any($event).documentId)" />
          }
          <h2>{{ 'Register an asset' | t }}</h2>
          <section class="form">
            <ui-field [label]="'Asset (product)' | t" [required]="true"><ui-select [options]="products()" [searchable]="true" [value]="asset()" (valueChange)="asset.set($event)" /></ui-field>
            <ui-field [label]="'Unit' | t" [required]="true"><ui-select [options]="units()" [searchable]="true" [value]="unit()" (valueChange)="unit.set($event)" /></ui-field>
            <ui-field [label]="'Initial value' | t" [required]="true"><input uiInput [numeric]="true" inputmode="decimal" [value]="value()" (input)="value.set($any($event.target).value)" /></ui-field>
            <ui-field [label]="'Start date' | t" [required]="true"><input uiInput type="date" [value]="start()" (input)="start.set($any($event.target).value)" /></ui-field>
            <ui-field [label]="'Depreciation cost centre' | t" [hint]="'Empty: the place that holds it. Choose one for assets kept at clients' | t"><ui-select [options]="costCenters()" [searchable]="true" [value]="costCenter()" (valueChange)="costCenter.set($event)" /></ui-field>
            <ui-field [label]="'Months' | t" [hint]="'Empty: the book\\'s rate for the asset\\'s category' | t"><input uiInput [numeric]="true" inputmode="numeric" [value]="months()" (input)="months.set($any($event.target).value)" /></ui-field>
            <div class="actions"><button uiButton variant="primary" [disabled]="!asset() || !unit() || !(+value() > 0) || busy()" (click)="register()">{{ 'Register' | t }}</button></div>
          </section>
        }
        @case ('runs') {
          <ui-table [columns]="runColumns" [rows]="runRows()" [pageSize]="100" [ariaLabel]="'Past runs' | t" [emptyText]="'No run on this book yet.' | t" [activeId]="chosenRun()"
            (rowClick)="openRun($any($event).date)">
            <ng-template uiCell="actions" let-row>
              @if ($any(row).documentId) { <button uiButton variant="quiet" size="small" (click)="$event.stopPropagation(); openJournal($any(row).documentId)">{{ 'Open journal' | t }}</button> }
              @if ($any(row).latest) { <button uiButton variant="quiet" size="small" [disabled]="busy()" (click)="$event.stopPropagation(); undo($any(row))">{{ 'Undo' | t }}</button> }
            </ng-template>
          </ui-table>
          @if (chosenRun()) {
            <ui-table [columns]="runLineColumns" [rows]="runLineRows()" [pageSize]="200" [ariaLabel]="'The run\\'s assets' | t" [emptyText]="'No asset in this run.' | t" />
          }
        }
        @case ('run') {
          <section class="form">
            <ui-field [label]="'Up to' | t" [required]="true"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
            <div class="actions">
              <button uiButton [disabled]="busy()" (click)="calculate()">{{ 'Preview' | t }}</button>
              <button uiButton variant="primary" [disabled]="!lines()?.length || ran() || busy()" (click)="run()">{{ 'Run' | t }}</button>
            </div>
          </section>
          @if (lines(); as list) {
            <section class="kpis">
              <ui-kpi [label]="'Depreciation' | t" [value]="money(totals().amount)" [caption]="ran() ? 'recorded' : 'preview'" />
              <ui-kpi [label]="'Book value before' | t" [value]="money(totals().before)" />
              <ui-kpi [label]="'Book value after' | t" [value]="money(totals().after)" />
            </section>
            <ui-table [columns]="columns" [rows]="rows()" [pageSize]="200" [ariaLabel]="'Depreciation' | t" [emptyText]="'Nothing to depreciate up to this date.' | t" />
          }
          <!-- The journal of the run recorded up to this date: right after running, or for a run recorded before (the API refuses without one). -->
          <section class="form">
            <ui-field [label]="'Journal type' | t" [required]="true"><ui-select [options]="types()" [searchable]="true" [value]="journalType()" (valueChange)="journalType.set($event)" /></ui-field>
            <ui-field [label]="'In category' | t" [required]="true"><ui-select [options]="categories()" [value]="journalCategory()" (valueChange)="journalCategory.set($event)" /></ui-field>
            <div class="actions"><button uiButton variant="primary" [disabled]="!journalType() || !journalCategory() || busy()" (click)="journal()">{{ 'Make the journal' | t }}</button></div>
          </section>
        }
      }
    }
  `,
  styleUrl: './operations.scss',
  styles: `.kpis { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: var(--space-4); }`,
})
export class Depreciation {
  private readonly api = inject(OperationsApi);
  private readonly entities = inject(EntityApi);
  private readonly documents = inject(DocumentApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private readonly dialogs = inject(Dialogs);

  protected readonly tabs: Tab[] = [{ id: 'run', label: t('Run') }, { id: 'assets', label: t('Assets') }, { id: 'runs', label: t('Past runs') }];
  protected readonly tab = signal<string | null>('run');
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly books = signal<DepreciationBook[]>([]);
  protected readonly book = signal<string | null>(null);
  protected readonly products = signal<SelectOption[]>([]);
  protected readonly units = signal<SelectOption[]>([]);
  protected readonly types = signal<SelectOption[]>([]);
  protected readonly categories = signal<SelectOption[]>([]);
  protected readonly asset = signal<string | null>(null);
  protected readonly unit = signal<string | null>(null);
  protected readonly value = signal('');
  protected readonly start = signal(today());
  protected readonly months = signal('');
  protected readonly costCenters = signal<SelectOption[]>([]);
  protected readonly costCenter = signal<string | null>(null);
  protected readonly date = signal(today());
  protected readonly lines = signal<DepreciationLine[] | null>(null);
  protected readonly ran = signal(false);
  protected readonly journalType = signal<string | null>(null);
  protected readonly journalCategory = signal<string | null>(null);
  protected readonly assets = signal<AssetRow[]>([]);
  protected readonly chosenAsset = signal<AssetRow | null>(null);
  protected readonly history = signal<AssetRunRow[]>([]);
  protected readonly runs = signal<DepreciationRun[]>([]);
  protected readonly chosenRun = signal<string | null>(null);
  protected readonly runLines = signal<AssetRunRow[]>([]);

  protected readonly bookOptions = computed<SelectOption[]>(() => this.books().map(b => ({ value: b.id, label: b.name, hint: b.posts ? 'posts' : undefined })));
  protected readonly currentBook = computed(() => this.books().find(b => b.id === this.book()) ?? null);
  protected readonly totals = computed(() => runTotals(this.lines() ?? []));
  protected readonly rows = computed(() => (this.lines() ?? []).map(l => ({ ...l, id: l.assetValueId, period: `${formatDate(l.from)} – ${formatDate(l.to)}` })));
  protected readonly columns: Column<DepreciationLine & { id: string; period: string }>[] = [
    { key: 'productName', label: t('Asset'), sortable: true }, { key: 'period', label: t('Period'), width: '220px' },
    { key: 'days', label: t('Days'), format: 'number', decimals: 0, width: '80px' }, { key: 'bookValueBefore', label: t('Value before'), format: 'number', width: '140px' },
    { key: 'amount', label: t('Depreciation'), format: 'number', width: '140px', sortable: true }, { key: 'bookValueAfter', label: t('Value after'), format: 'number', width: '140px' },
  ];

  protected readonly assetRows = computed(() => this.assets().map(a => ({ ...a, id: a.assetValueId, state: a.disposed ? 'Disposed' : a.bookValue <= 0 ? 'Fully depreciated' : 'In use' })));
  protected readonly assetColumns: Column<AssetRow & { id: string; state: string }>[] = [
    { key: 'code', label: t('Code'), width: '100px', sortable: true }, { key: 'product', label: t('Asset'), sortable: true }, { key: 'method', label: t('Method'), width: '150px' },
    { key: 'startDate', label: t('Start'), format: 'date', width: '110px', sortable: true }, { key: 'endDate', label: t('End'), format: 'date', width: '110px', sortable: true },
    { key: 'initialValue', label: t('Value'), format: 'number', width: '130px', sortable: true }, { key: 'depreciated', label: t('Depreciated'), format: 'number', width: '130px' },
    { key: 'bookValue', label: t('Book value'), format: 'number', width: '130px', sortable: true }, { key: 'state', label: t('State'), width: '140px' },
  ];
  protected readonly historyRows = computed(() => this.history().map((h, i) => ({ ...h, id: String(i) })));
  protected readonly historyColumns: Column<AssetRunRow & { id: string }>[] = [
    { key: 'from', label: t('From'), format: 'date', width: '110px' }, { key: 'to', label: t('Up to'), format: 'date', width: '110px' }, { key: 'amount', label: t('Depreciation'), format: 'number', width: '140px' },
    { key: 'bookValue', label: t('Book value'), format: 'number', width: '140px' }, { key: 'document', label: t('Journal') },
  ];
  protected readonly runRows = computed(() => this.runs().map(r => ({ ...r, id: r.date, journal: r.document ?? 'No journal yet', state: r.posted ? 'Posted' : 'Not posted' })));
  protected readonly runColumns: Column<DepreciationRun & { id: string; journal: string; state: string }>[] = [
    { key: 'date', label: t('Date'), format: 'date', width: '120px' }, { key: 'assets', label: t('Assets'), format: 'number', decimals: 0, width: '90px' },
    { key: 'amount', label: t('Depreciation'), format: 'number', width: '150px' }, { key: 'journal', label: t('Journal') }, { key: 'state', label: t('State'), width: '120px' }, { key: 'actions', label: '', width: '190px' },
  ];
  protected readonly runLineRows = computed(() => this.runLines().map((l, i) => ({ ...l, id: String(i) })));
  protected readonly runLineColumns: Column<AssetRunRow & { id: string }>[] = [
    { key: 'product', label: t('Asset') }, { key: 'from', label: t('From'), format: 'date', width: '110px' }, { key: 'to', label: t('Up to'), format: 'date', width: '110px' },
    { key: 'amount', label: t('Depreciation'), format: 'number', width: '140px' }, { key: 'bookValue', label: t('Book value'), format: 'number', width: '140px' },
  ];

  constructor() {
    void this.load();
    effect(() => {
      const [book, tab] = [this.book(), this.tab()];
      untracked(() => void this.refresh(book, tab));
    });
  }

  /** What the tab shows, fresh each time it opens: a run or an asset may have been added since. */
  private async refresh(book: string | null, tab: string | null): Promise<void> {
    if (!book) {
      return;
    }
    await this.act(async () => {
      this.chosenAsset.set(null);
      this.chosenRun.set(null);
      if (tab === 'assets') {
        this.assets.set(await this.api.assets(book));
      } else if (tab === 'runs') {
        this.runs.set(await this.api.depreciationRuns(book));
      }
    });
  }

  /** Marks the asset disposed, or back in use, by hand; a sold asset is closed by its sale. */
  protected async setDisposed(asset: AssetRow, disposed: boolean): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.setDisposed(asset.assetValueId, disposed);
      this.assets.set(await this.api.assets(this.book()!));
      this.chosenAsset.set(this.assets().find(a => a.assetValueId === asset.assetValueId) ?? null);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async openAsset(asset: AssetRow): Promise<void> {
    await this.act(async () => {
      this.history.set(await this.api.assetHistory(asset.assetValueId));
      this.chosenAsset.set(asset);
    });
  }

  protected async openRun(date: string): Promise<void> {
    await this.act(async () => {
      this.runLines.set(await this.api.depreciationRunLines(this.book()!, date));
      this.chosenRun.set(date);
    });
  }

  /** A journal where it lives: in its sheet, under the document's own category. */
  protected async openJournal(id: string | null): Promise<void> {
    if (id) {
      await this.act(async () => {
        const journal = await this.documents.get(id);
        const shown = journal.idSup ? await this.documents.get(journal.idSup) : journal;
        await this.router.navigate(['/documents', shown.categoryIds[0], shown.id]);
      });
    }
  }

  protected async undo(run: DepreciationRun): Promise<void> {
    if (await this.dialogs.confirm({ title: `Undo the run of ${formatDate(run.date)}?`, message: undoMessage(run), confirmLabel: t('Undo the run'), danger: true })) {
      await this.act(async () => {
        await this.api.undoDepreciationRun(this.book()!, run.date);
        this.toasts.good(t('The run is undone.'));
        this.chosenRun.set(null);
        this.runs.set(await this.api.depreciationRuns(this.book()!));
      });
    }
  }

  private async load(): Promise<void> {
    try {
      const scopes = await this.entities.lookupScopes().catch(() => []);
      this.costCenters.set(await registerOptions(this.entities, scopes, 'tbl_cost_center'));
      const [books, products, units, types, categories] = await Promise.all([this.api.books(), this.api.assetProducts(),
        registerOptions(this.entities, scopes, 'tbl_unit'), this.documents.types(), this.documents.categories()]);
      this.books.set(books);
      this.products.set(products);
      this.units.set(units);
      // Every type, children too: a journal type sits under its sheet (FAA under FA).
      this.types.set(types.map(t => ({ value: t.id!, label: `${t.code ?? ''} ${textIn(t.name)}`.trim() })));
      this.categories.set(categories.filter(c => c.isElement).map(c => ({ value: c.id!, label: textIn(c.name) })));
      if (books.length === 1) {
        this.book.set(books[0].id);
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected chooseBook(id: string | null): void {
    this.book.set(id);
    this.lines.set(null);
    this.ran.set(false);
  }

  protected async register(): Promise<void> {
    await this.act(async () => {
      const ids = await this.api.registerAsset({ productId: this.asset()!, unitId: this.unit()!, bookId: this.book()!, initialValue: Number(this.value()),
        startDate: this.start(), months: this.months() ? Number(this.months()) : null, defaultCostCenterId: this.costCenter() });
      this.toasts.good(t('Asset registered ({0} value{1}).', ids.length, ids.length === 1 ? '' : 's'));
      this.value.set('');
      this.assets.set(await this.api.assets(this.book()!));
    });
  }

  protected async calculate(): Promise<void> {
    await this.act(async () => {
      this.ran.set(false);
      this.lines.set(await this.api.calculateDepreciation(this.book()!, this.date()));
    });
  }

  protected async run(): Promise<void> {
    await this.act(async () => {
      this.lines.set(await this.api.runDepreciation(this.book()!, this.date()));
      this.ran.set(true);
      this.toasts.good(t('The run is recorded.'));
    });
  }

  protected async journal(): Promise<void> {
    await this.act(async () => {
      const document = await this.api.depreciationJournal(this.book()!, this.date(), this.journalType()!, this.journalCategory()!);
      this.toasts.good(t('Journal {0} made.', document.code));
      if (document.id && document.categoryIds[0]) {
        void this.router.navigate(['/documents', document.categoryIds[0], document.id]);
      }
    });
  }

  protected money(value: number): string {
    return formatNumber(value);
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
