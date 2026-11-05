import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { DocumentApi } from '../../core/documents';
import { errorsOf } from '../../core/entities';
import { OperationsApi, SerialMove, SerialRow, SerialStock } from '../../core/operations';
import { Button, Field, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Column, Table } from '../../ui/table';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { parseSerials } from '../documents/serial-dialog';

/** How many units of a place's stock still have no serial number. */
export function withoutSerial(row: Pick<SerialStock, 'quantity' | 'serials'>): number {
  return Math.max(row.quantity - row.serials, 0);
}

/**
 * Serial numbers: find one, where it is now and every move it made; and give numbers to stock already held,
 * which stays under its product.
 */
@Component({
  selector: 'app-serials',
  imports: [TranslatePipe, Button, Field, Icon, Input, Table, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Serial numbers' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'Serial numbers' | t" />
    @if (tab() === 'find') {
      <section class="form">
        <ui-field [label]="'Search' | t" [hint]="'A serial or part number' | t"><input uiInput type="search" (input)="typed($any($event.target).value)" /></ui-field>
      </section>
      <ui-table [columns]="columns" [rows]="rows()" [pageSize]="100" [ariaLabel]="'Serial numbers' | t" [emptyText]="'No serial number found.' | t" [activeId]="chosen()?.id ?? null"
        (rowClick)="open($any($event))" />
      @if (chosen(); as s) {
        <h2>{{ '{0}: its moves' | t: (s.serial) }}</h2>
        <ui-table [columns]="moveColumns" [rows]="moveRows()" [pageSize]="100" [ariaLabel]="'Moves' | t" [emptyText]="'Given to stock already held: no move yet.' | t" (rowClick)="openDocument($any($event).documentId)" />
      }
    } @else {
      <ui-table [columns]="stockColumns" [rows]="stockRows()" [pageSize]="100" [ariaLabel]="'Tracked stock' | t" [emptyText]="'No stock of a product that tracks serial numbers.' | t" [activeId]="stockRow()?.id ?? null"
        (rowClick)="stockRow.set($any($event))" />
      @if (stockRow(); as s) {
        <h2>{{ '{0} at {1}: {2} without a number' | t: (s.product) : (s.costCenter) : (left(s)) }}</h2>
        <section class="form">
          <ui-field [label]="'Serial numbers' | t" [hint]="'One per line; a part number after a comma' | t" [required]="true">
            <textarea uiInput rows="6" [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
          </ui-field>
          <div class="actions"><button uiButton variant="primary" [disabled]="!count() || count() > left(s)" (click)="assign(s)">{{ 'Assign {0}' | t: (count()) }}</button></div>
        </section>
      }
    }
  `,
  styleUrl: './operations.scss',
  styles: `textarea { width: 100%; height: auto; padding: var(--space-2); font-family: inherit; }`,
})
export class Serials {
  private readonly api = inject(OperationsApi);
  private readonly documents = inject(DocumentApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private timer?: ReturnType<typeof setTimeout>;

  protected readonly tabs: Tab[] = [{ id: 'find', label: t('Find') }, { id: 'assign', label: t('Assign to stock') }];
  protected readonly tab = signal<string | null>('find');
  protected readonly message = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly serials = signal<SerialRow[]>([]);
  protected readonly chosen = signal<SerialRow | null>(null);
  protected readonly moves = signal<SerialMove[]>([]);
  protected readonly stock = signal<SerialStock[]>([]);
  protected readonly stockRow = signal<(SerialStock & { id: string }) | null>(null);
  protected readonly text = signal('');
  protected readonly count = computed(() => parseSerials(this.text()).length);
  protected readonly rows = computed(() => this.serials().map(s => ({ ...s, productName: s.product ?? s.productCode, holderName: s.holder ?? 'Left the company' })));
  protected readonly columns: Column<SerialRow & { productName: string | null; holderName: string }>[] = [
    { key: 'serial', label: t('Serial number'), sortable: true }, { key: 'partNumber', label: t('Part number'), width: '180px' },
    { key: 'productName', label: t('Product'), sortable: true }, { key: 'holderName', label: t('Held by'), sortable: true },
  ];
  protected readonly moveRows = computed(() => this.moves().map((m, i) => ({ ...m, id: String(i) })));
  protected readonly moveColumns: Column<SerialMove & { id: string }>[] = [
    { key: 'postingDate', label: t('Date'), format: 'date', width: '110px' }, { key: 'document', label: t('Document'), width: '130px' }, { key: 'documentType', label: t('Type') },
    { key: 'from', label: t('From') }, { key: 'to', label: t('To') },
  ];
  protected readonly stockRows = computed(() => this.stock().map(s => ({ ...s, id: `${s.costCenterId}-${s.productId}-${s.unitId}`, without: withoutSerial(s) })));
  protected readonly stockColumns: Column<SerialStock & { id: string; without: number }>[] = [
    { key: 'costCenter', label: t('Place'), sortable: true }, { key: 'product', label: t('Product'), sortable: true }, { key: 'unit', label: t('Unit'), width: '100px' },
    { key: 'quantity', label: t('Quantity'), format: 'number', decimals: 0, width: '110px' }, { key: 'serials', label: t('With a number'), format: 'number', decimals: 0, width: '140px' },
    { key: 'without', label: t('Without'), format: 'number', decimals: 0, width: '110px', sortable: true },
  ];

  constructor() {
    effect(() => {
      const [tab, search] = [this.tab(), this.search()];
      untracked(() => void this.load(tab, search));
    });
  }

  private async load(tab: string | null, search: string): Promise<void> {
    await this.act(async () => {
      if (tab === 'find') {
        this.serials.set(await this.api.serials(null, null, search));
      } else {
        this.stock.set(await this.api.serialStock());
      }
    });
  }

  protected typed(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.chosen.set(null);
      this.search.set(value);
    }, 300);
  }

  protected async open(row: SerialRow): Promise<void> {
    await this.act(async () => {
      this.moves.set(await this.api.serialMoves(row.id));
      this.chosen.set(row);
    });
  }

  protected async openDocument(id: string): Promise<void> {
    await this.act(async () => {
      const document = await this.documents.get(id);
      await this.router.navigate(['/documents', document.categoryIds[0], document.id]);
    });
  }

  protected left(row: SerialStock): number {
    return withoutSerial(row);
  }

  protected async assign(row: SerialStock): Promise<void> {
    await this.act(async () => {
      await this.api.assignSerials({ costCenterId: row.costCenterId, productId: row.productId, unitId: row.unitId, serials: parseSerials(this.text()) });
      this.toasts.good(t('{0} serial numbers assigned.', this.count()));
      this.text.set('');
      this.stockRow.set(null);
      this.stock.set(await this.api.serialStock());
    });
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
