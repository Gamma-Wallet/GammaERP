import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DocumentApi } from '../../core/documents';
import { errorsOf } from '../../core/entities';
import { Button, Field, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Column, Table } from '../../ui/table';
import { Tab, Tabs } from '../../ui/tabs';

interface AuditUser { id: string; name: string | null }
interface SignInRow { id: string; at: string; userId: string; user: string | null; event: string }
interface ActivityTable { table: string; label: string; added: number; changed: number; deleted: number }
interface ActivityRow { id: string; at: string; user: string; action: string; record: string | null; documentId: string | null; document: string | null }
interface ActivityField { field: string; before: string | null; after: string | null }

/** The last seven days up to a day, the screen's first period. */
export function lastWeek(today: Date): { from: string; to: string } {
  const day = (d: Date) => d.toISOString().slice(0, 10);
  return { from: day(new Date(today.getTime() - 6 * 86_400_000)), to: day(today) };
}

/** How one version of a row reads in the heading of its fields. */
export function changeTitle(row: Pick<ActivityRow, 'action' | 'record' | 'document'>): string {
  return `${row.action}: ${row.record ?? row.document ?? 'this row'}`;
}

/**
 * User activity: who signed in, and what each user added, changed and deleted, table by table down to the fields of one
 * change. Who sees it is decided by Menu access, on the server too.
 */
@Component({
  selector: 'app-user-activity',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select, Table, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'User activity' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="form">
      <ui-field [label]="'From' | t"><input uiInput type="date" [value]="from()" (change)="from.set($any($event.target).value)" /></ui-field>
      <ui-field [label]="'Up to' | t"><input uiInput type="date" [value]="to()" (change)="to.set($any($event.target).value)" /></ui-field>
      <ui-field [label]="'User' | t"><ui-select [options]="userOptions()" [(value)]="user" /></ui-field>
    </section>
    <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'User activity' | t" />
    @if (tab() === 'activity') {
      <ui-table [columns]="tableColumns" [rows]="tableRows()" [pageSize]="50" [ariaLabel]="'Tables' | t" [emptyText]="'Nothing was done in this period.' | t" [activeId]="chosen()?.table ?? null"
        (rowClick)="openTable($any($event))" />
      @if (chosen(); as t) {
        <h2>{{ t.label }}</h2>
        @if (rows().length >= maxRows) { <p class="who">{{ 'The latest {0} rows. Choose fewer days or one user to see the others.' | t: (maxRows) }}</p> }
        <ui-table [columns]="rowColumns" [rows]="rows()" [pageSize]="50" [ariaLabel]="'Rows' | t" [emptyText]="'No row.' | t" [activeId]="row()?.id ?? null" (rowClick)="openRow($any($event))" />
      }
      @if (row(); as r) {
        <h2>{{ title() }}</h2>
        <p class="who">{{ r.user }}</p>
        <ui-table [columns]="fieldColumns" [rows]="fieldRows()" [pageSize]="100" [ariaLabel]="'Fields' | t" [emptyText]="'No field to show.' | t" />
        @if (r.documentId) { <div class="actions"><button uiButton (click)="openDocument(r.documentId)">{{ 'Open {0}' | t: (r.document ?? 'the document') }}</button></div> }
      }
    } @else {
      <ui-table [columns]="signInColumns" [rows]="signIns()" [pageSize]="50" [ariaLabel]="'Sign-ins' | t" [emptyText]="'Nobody signed in during this period.' | t" />
    }
  `,
  styleUrl: './operations.scss',
  styles: `.who { margin: 0 0 var(--space-2); color: var(--muted); }`,
})
export class UserActivity {
  private readonly http = inject(HttpClient);
  private readonly documents = inject(DocumentApi);
  private readonly router = inject(Router);
  private readonly base = '/api/v1/audit';
  protected readonly maxRows = 500;

  protected readonly tabs: Tab[] = [{ id: 'sign-ins', label: t('Sign-ins') }, { id: 'activity', label: t('Activity') }];
  protected readonly tab = signal<string | null>('sign-ins');
  protected readonly message = signal<string | null>(null);
  protected readonly from = signal(lastWeek(new Date()).from);
  protected readonly to = signal(lastWeek(new Date()).to);
  protected readonly user = signal<string | null>('');
  protected readonly users = signal<AuditUser[]>([]);
  protected readonly userOptions = computed<SelectOption[]>(() => [{ value: '', label: t('All users') }, ...this.users().map(u => ({ value: u.id, label: u.name ?? '' }))]);
  protected readonly signIns = signal<SignInRow[]>([]);
  protected readonly tables = signal<ActivityTable[]>([]);
  protected readonly tableRows = computed(() => this.tables().map(t => ({ ...t, id: t.table })));
  protected readonly chosen = signal<ActivityTable | null>(null);
  protected readonly rows = signal<ActivityRow[]>([]);
  protected readonly row = signal<ActivityRow | null>(null);
  protected readonly fields = signal<ActivityField[]>([]);
  protected readonly fieldRows = computed(() => this.fields().map(f => ({ ...f, id: f.field })));
  protected readonly title = computed(() => { const r = this.row(); return r ? changeTitle(r) : ''; });

  protected readonly signInColumns: Column<SignInRow>[] = [
    { key: 'at', label: t('When'), format: 'datetime', width: '180px', sortable: true }, { key: 'user', label: t('User'), sortable: true }, { key: 'event', label: t('Event'), sortable: true },
  ];
  protected readonly tableColumns: Column<ActivityTable & { id: string }>[] = [
    { key: 'label', label: t('Table'), sortable: true }, { key: 'added', label: t('Added'), format: 'number', decimals: 0, width: '110px', sortable: true },
    { key: 'changed', label: t('Changed'), format: 'number', decimals: 0, width: '110px', sortable: true }, { key: 'deleted', label: t('Deleted'), format: 'number', decimals: 0, width: '110px', sortable: true },
  ];
  protected readonly rowColumns: Column<ActivityRow>[] = [
    { key: 'at', label: t('When'), format: 'datetime', width: '180px', sortable: true }, { key: 'user', label: t('User'), width: '160px', sortable: true },
    { key: 'action', label: t('Action'), width: '110px', sortable: true }, { key: 'record', label: t('Row'), sortable: true }, { key: 'document', label: t('Document'), width: '150px', sortable: true },
  ];
  protected readonly fieldColumns: Column<ActivityField & { id: string }>[] = [
    { key: 'field', label: t('Field'), width: '220px' }, { key: 'before', label: t('Before') }, { key: 'after', label: t('After') },
  ];

  constructor() {
    void this.act(async () => this.users.set(await firstValueFrom(this.http.get<AuditUser[]>(`${this.base}/users`))));
    effect(() => {
      const [tab, params] = [this.tab(), this.params()];
      untracked(() => void this.load(tab, params));
    });
  }

  private readonly params = computed(() => {
    const user = this.user();
    return { from: this.from(), to: this.to(), ...(user ? { user } : {}) };
  });

  private async load(tab: string | null, params: Record<string, string>): Promise<void> {
    this.chosen.set(null);
    this.row.set(null);
    if (!params['from'] || !params['to']) {
      return;
    }

    await this.act(async () => {
      if (tab === 'activity') {
        this.tables.set(await firstValueFrom(this.http.get<ActivityTable[]>(`${this.base}/activity`, { params })));
      } else {
        this.signIns.set(await firstValueFrom(this.http.get<SignInRow[]>(`${this.base}/sign-ins`, { params })));
      }
    });
  }

  protected async openTable(table: ActivityTable): Promise<void> {
    await this.act(async () => {
      this.rows.set(await firstValueFrom(this.http.get<ActivityRow[]>(`${this.base}/activity/${table.table}`, { params: this.params() })));
      this.chosen.set(table);
      this.row.set(null);
    });
  }

  protected async openRow(row: ActivityRow): Promise<void> {
    const table = this.chosen();
    await this.act(async () => {
      this.fields.set(await firstValueFrom(this.http.get<ActivityField[]>(`${this.base}/activity/${table?.table}/${row.id}`)));
      this.row.set(row);
    });
  }

  /** A document where it lives: a journal in its sheet, under the document's own category. */
  protected async openDocument(id: string): Promise<void> {
    await this.act(async () => {
      const document = await this.documents.get(id);
      const shown = document.idSup ? await this.documents.get(document.idSup) : document;
      await this.router.navigate(['/documents', shown.categoryIds[0], shown.id]);
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
