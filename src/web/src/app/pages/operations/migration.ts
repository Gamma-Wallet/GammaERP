import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { Button } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Status } from '../../ui/status';
import { CellTemplate, Column, Table, TreeTable } from '../../ui/table';
import { Toasts } from '../../ui/toast';

interface MigrationRun { at: string; user: string | null; fileName: string | null; added: number; updated: number }
export interface MigrationStep { id: string; code: string; name: string | null; sortOrder: number; waits: string[]; rows: number; last: MigrationRun | null; level: number; needs: string[]; parent: string | null }
interface MigrationError { row: number; column: string | null; message: string }
export interface MigrationResult { rows: number; added: number; updated: number; saved: boolean; errors: MigrationError[]; openingBalance: number | null }
interface MigrationOpening { account: string | null; name: string | null; balance: number }

/** Where the opening account stands: at zero the detail agrees with the chart. */
export function openingText(opening: MigrationOpening): string {
  const name = `${opening.account} ${opening.name ?? ''}`.trim();
  return opening.balance === 0 ? `${name} stands at zero.` : `${name} stands at ${formatNumber(opening.balance)}. It must end at zero.`;
}

/** The steps as a tree, each under the step it shows under, numbered in the order the tree reads. */
export function stepTree<T extends Pick<MigrationStep, 'code' | 'parent'>>(steps: readonly T[]): (T & { number: number; children: unknown[] })[] {
  const rows = steps.map((s, i) => ({ ...s, number: i + 1, children: [] as unknown[] }));
  const byCode = new Map(rows.map(r => [r.code, r]));
  const top: typeof rows = [];
  for (const row of rows) {
    const parent = row.parent ? byCode.get(row.parent) : undefined;
    if (parent) {
      parent.children.push(row);
    } else {
      top.push(row);
    }
  }
  return top;
}

/** What a checked file will do, in words. */
export function resultText(result: Pick<MigrationResult, 'rows' | 'added' | 'updated' | 'errors'>): string {
  if (result.errors.length) {
    return `${result.errors.length} ${result.errors.length === 1 ? 'problem' : 'problems'} to fix. Nothing was saved.`;
  }
  return `${result.rows} rows checked: ${result.added} to add, ${result.updated} to update.`;
}

/** The last import of a step, in words. */
export function lastText(run: MigrationRun | null): string {
  return run ? `${formatDate(run.at, true)}, ${run.user ?? t('System')}: ${t('{0} added, {1} updated', run.added, run.updated)}` : '';
}

/**
 * The Migration menu: the lists a company brings in, in the order they depend on each other. Each exports what is here, gives its template and imports
 * a file. An import is checked first and saved only on Confirm, every row or none. A step stays closed until the steps it needs hold data.
 */
@Component({
  selector: 'app-migration',
  imports: [TranslatePipe, Button, CellTemplate, Icon, Status, Table, TreeTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Migration' | t }}</h1><p>{{ 'Each step sits under what it needs and opens once everything above it holds data. Export shows what is here, Template gives an empty file, and Import checks a file before anything is saved.' | t }}</p></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (opening(); as o) { <p class="opening"><ui-status [kind]="o.balance === 0 ? 'good' : 'warning'">{{ openingLine() }}</ui-status></p> }
    <input #picker type="file" hidden accept=".xlsx" (change)="chosen(picker)" />
    <ui-tree-table [columns]="columns" [rows]="tree()" [(expanded)]="expanded" [rowId]="stepId" [ariaLabel]="'Migration steps' | t" [emptyText]="'No step is configured.' | t" [activeId]="step()?.id ?? null">
      <ng-template uiCell="label" let-row><span class="wrap">{{ row.number }}. {{ row.name }}</span></ng-template>
      <ng-template uiCell="needsText" let-row><span class="wrap small">{{ row.needsText }}</span></ng-template>
      <ng-template uiCell="lastRun" let-row><span class="wrap small">{{ row.lastRun }}</span></ng-template>
      <ng-template uiCell="state" let-row>
        @if (row.waits.length) { <ui-status kind="warning">{{ 'Waiting' | t }}</ui-status> }
        @else if (row.last) { <ui-status kind="good">{{ 'Imported' | t }}</ui-status> }
        @else { <ui-status>{{ 'Ready' | t }}</ui-status> }
      </ng-template>
      <ng-template uiCell="actions" let-row>
        <div class="actions">
          <button uiButton size="small" (click)="download(row, 'export')">{{ 'Export' | t }}</button>
          <button uiButton size="small" (click)="download(row, 'template')">{{ 'Template' | t }}</button>
          <button uiButton size="small" [disabled]="row.waits.length > 0 || busy()" (click)="pick(row, picker)">{{ 'Import' | t }}</button>
        </div>
      </ng-template>
    </ui-tree-table>
    @if (result(); as r) {
      <section class="result" aria-live="polite">
        <h2>{{ step()?.name }}: {{ file()?.name }}</h2>
        <p>{{ summary() }}</p>
        @if (r.openingBalance !== null && !r.errors.length) { <p>{{ 'After this file the opening account stands at {0}.' | t: (amount(r.openingBalance)) }}</p> }
        @if (r.errors.length) {
          <ui-table [columns]="errorColumns" [rows]="errorRows()" [pageSize]="50" [ariaLabel]="'Problems' | t" />
        } @else {
          <div class="actions"><button uiButton variant="primary" [disabled]="busy()" (click)="confirm()">{{ 'Confirm' | t }}</button><button uiButton (click)="clear()">{{ 'Cancel' | t }}</button></div>
        }
      </section>
    }
  `,
  styles: `
    :host { display: block; padding: var(--space-5) var(--space-6); }
    .head { margin-bottom: var(--space-4); }
    h1 { font-size: var(--title-l); }
    h2 { margin: 0 0 var(--space-2); font-size: var(--title-s); }
    .head p, .result p { margin: var(--space-2) 0 var(--space-3); color: var(--muted); }
    .wrap { white-space: normal; }
    .small { color: var(--muted); font-size: var(--text-s); }
    .opening { margin: 0 0 var(--space-3); }
    .message { display: flex; align-items: center; gap: var(--space-2); color: var(--critical); }
    .actions { display: flex; gap: var(--space-2); }
    .result { margin-top: var(--space-4); padding: var(--space-4); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    @media (max-width: 720px) { :host { padding: var(--space-4); } }
  `,
})
export class Migration {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);
  private readonly base = '/api/v1/migration';

  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly steps = signal<MigrationStep[]>([]);
  protected readonly step = signal<MigrationStep | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly result = signal<MigrationResult | null>(null);
  protected readonly opening = signal<MigrationOpening | null>(null);
  protected readonly openingLine = computed(() => { const o = this.opening(); return o ? openingText(o) : ''; });
  protected readonly summary = computed(() => { const r = this.result(); return r ? resultText(r) : ''; });
  protected readonly tree = computed(() => stepTree(this.steps().map(s => ({ ...s, lastRun: lastText(s.last), needsText: s.needs.join(', ') }))));
  protected readonly expanded = signal<string[]>([]);
  protected readonly stepId = (row: { id: string }) => row.id;
  protected readonly errorRows = computed(() => (this.result()?.errors ?? []).map((e, i) => ({ ...e, id: String(i), place: e.row ? String(e.row) : '', columnName: e.column ?? '' })));
  protected readonly columns: Column<MigrationStep & { lastRun: string; needsText: string }>[] = [
    { key: 'label', label: t('Step'), width: '36%' }, { key: 'needsText', label: t('Needs'), width: '20%' }, { key: 'rows', label: t('Rows'), format: 'number', decimals: 0, width: '64px' },
    { key: 'state', label: t('State'), width: '104px' }, { key: 'lastRun', label: t('Last import'), width: '13%' }, { key: 'actions', label: '', width: '262px' },
  ];
  protected readonly errorColumns: Column<MigrationError & { id: string; place: string; columnName: string }>[] = [
    { key: 'place', label: t('Row'), width: '80px' }, { key: 'columnName', label: t('Column'), width: '200px' }, { key: 'message', label: t('Problem') },
  ];

  constructor() {
    void this.act(() => this.load());
  }

  private async load(): Promise<void> {
    const steps = await firstValueFrom(this.http.get<MigrationStep[]>(this.base));
    this.steps.set(steps);
    this.expanded.set(steps.map(s => s.id));
    this.opening.set(await firstValueFrom(this.http.get<MigrationOpening | null>(`${this.base}/opening`)));
  }

  protected async download(step: MigrationStep, kind: 'export' | 'template'): Promise<void> {
    await this.act(async () => {
      const body = await firstValueFrom(this.http.get(`${this.base}/${step.code}/${kind}`, { responseType: 'blob' }));
      const link = document.createElement('a');
      link.href = URL.createObjectURL(body);
      link.download = `${step.code}${kind === 'template' ? '-template' : ''}.xlsx`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
    });
  }

  protected pick(step: MigrationStep, picker: HTMLInputElement): void {
    this.step.set(step);
    this.result.set(null);
    picker.value = '';
    picker.click();
  }

  protected async chosen(picker: HTMLInputElement): Promise<void> {
    const file = picker.files?.[0];
    if (file) {
      this.file.set(file);
      await this.send(false);
    }
  }

  protected async confirm(): Promise<void> {
    await this.send(true);
  }

  protected amount(value: number): string {
    return formatNumber(value);
  }

  protected clear(): void {
    this.result.set(null);
    this.file.set(null);
    this.step.set(null);
  }

  private async send(confirm: boolean): Promise<void> {
    const [step, file] = [this.step(), this.file()];
    if (!step || !file) {
      return;
    }
    this.busy.set(true);
    await this.act(async () => {
      const form = new FormData();
      form.append('file', file);
      const result = await firstValueFrom(this.http.post<MigrationResult>(`${this.base}/${step.code}/import`, form, { params: { confirm } }));
      this.result.set(result);
      if (result.saved) {
        this.toasts.good(t('{0} added, {1} updated.', result.added, result.updated));
        this.clear();
        await this.load();
      }
    });
    this.busy.set(false);
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
