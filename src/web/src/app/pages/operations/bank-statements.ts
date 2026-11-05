import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Dialogs } from '../../ui/dialog';
import { Toasts } from '../../ui/toast';

interface Option {
  id: string;
  name: string | null;
}

interface Statement {
  id: string;
  bank: string | null;
  fileName: string | null;
  importedOn: string;
  lines: number;
  open: number;
}

interface Line {
  id: string;
  date: string;
  text: string | null;
  reference: string | null;
  amount: number;
  documentId: string | null;
  documentCategoryId: string | null;
  documentCode: string | null;
  documentReversed: boolean;
  ignored: boolean;
}

interface Match {
  id: string;
  code: string | null;
  partner: string | null;
  date: string | null;
  amount: number;
}

interface NewDocument {
  typeId: string;
  categoryId: string;
  name: string | null;
  bankIsFrom: boolean;
}

interface Choices {
  bankId: string;
  bank: string | null;
  matches: Match[];
  types: NewDocument[];
}

/** Bank statements, decided line by line by hand: match a line to a posted document of the bank, write a new one, or ignore it. */
@Component({
  selector: 'app-bank-statements',
  imports: [TranslatePipe, Button, Field, Icon, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Bank statements' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (statement(); as id) {
      <div class="actions"><button uiButton (click)="open(null)">{{ 'Back to statements' | t }}</button></div>
      <table>
        <thead><tr><th>{{ 'Date' | t }}</th><th>{{ 'Text' | t }}</th><th>{{ 'Reference' | t }}</th><th class="num">{{ 'Amount' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (l of lines(); track l.id) {
            <tr>
              <td>{{ date(l.date) }}</td><td>{{ l.text }}</td><td>{{ l.reference }}</td><td class="num">{{ n(l.amount) }}</td>
              <td>
                @if (l.documentId) {
                  <button uiButton (click)="show(l)">{{ l.documentCode }}</button>
                  @if (l.documentReversed) { {{ 'Reversed' | t }} }
                  <button uiButton [disabled]="busy()" (click)="clear(l)">{{ 'Undo' | t }}</button>
                } @else if (l.ignored) {
                  {{ 'Ignored' | t }} <button uiButton [disabled]="busy()" (click)="clear(l)">{{ 'Undo' | t }}</button>
                } @else {
                  <button uiButton [disabled]="busy()" (click)="choose(l)">{{ 'Match' | t }}</button>
                  <button uiButton [disabled]="busy()" (click)="ignore(l)">{{ 'Ignore' | t }}</button>
                }
              </td>
            </tr>
            @if (choosing() === l.id && choices(); as c) {
              <tr>
                <td colspan="5">
                  @for (m of c.matches; track m.id) {
                    <p><button uiButton [disabled]="busy()" (click)="match(l, m)">{{ 'Match' | t }}</button> {{ m.code }} · {{ m.partner }} · {{ m.date ? date(m.date) : '' }} · {{ n(m.amount) }}</p>
                  } @empty {
                    <p class="muted">{{ 'No posted document of this bank has this amount.' | t }}</p>
                  }
                  @for (w of c.types; track w.typeId) {
                    <button uiButton (click)="write(l, c, w)">{{ 'Write a new document' | t }}: {{ w.name }}</button>
                  }
                </td>
              </tr>
            }
          }
        </tbody>
      </table>
    } @else {
      @if (!busy() && !banks().length) { <p class="muted">{{ 'Add a cost centre of type Bank first.' | t }}</p> }
      <div class="form">
        <ui-field [label]="'Bank' | t" [required]="true"><ui-select [options]="banks()" [value]="bank()" (valueChange)="bank.set($event)" /></ui-field>
        <ui-field [label]="'File format' | t" [required]="true"><ui-select [options]="formats()" [value]="format()" (valueChange)="format.set($event)" /></ui-field>
        <ui-field [label]="'Upload statement' | t"><input class="control" type="file" accept=".csv,.txt,text/csv" [disabled]="busy()" (change)="upload($any($event.target))" /></ui-field>
      </div>
      @if (statements().length) {
        <table>
          <thead><tr><th>{{ 'Bank' | t }}</th><th>{{ 'File' | t }}</th><th>{{ 'Uploaded' | t }}</th><th class="num">{{ 'Lines' | t }}</th><th class="num">{{ 'To decide' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (s of statements(); track s.id) {
              <tr>
                <td>{{ s.bank }}</td><td>{{ s.fileName }}</td><td>{{ date(s.importedOn) }}</td><td class="num">{{ s.lines }}</td><td class="num">{{ s.open }}</td>
                <td><span class="row-actions"><button uiButton size="small" (click)="open(s.id)">{{ 'Lines' | t }}</button><button uiButton variant="danger" size="small" [disabled]="busy()" (click)="remove(s)">{{ 'Remove' | t }}</button></span></td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">{{ 'No statements yet.' | t }}</p>
      }
    }
  `,
  styleUrl: './operations.scss',
})
export class BankStatements {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private readonly dialogs = inject(Dialogs);
  private readonly base = '/api/v1/bank-statements';

  /** The statement open (query parameter), so going back from a document returns to it. */
  readonly statement = input<string | null>(null);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly banks = signal<SelectOption[]>([]);
  protected readonly formats = signal<SelectOption[]>([]);
  protected readonly bank = signal<string | null>(null);
  protected readonly format = signal<string | null>(null);
  protected readonly statements = signal<Statement[]>([]);
  protected readonly lines = signal<Line[]>([]);
  protected readonly choosing = signal<string | null>(null);
  protected readonly choices = signal<Choices | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    void this.run(async () => {
      const [banks, formats] = await Promise.all([this.get<Option[]>('/banks'), this.get<Option[]>('/formats')]);
      this.banks.set(banks.map(b => ({ value: b.id, label: b.name ?? '' })));
      this.formats.set(formats.map(f => ({ value: f.id, label: f.name ?? '' })));
      this.bank.set(banks.length === 1 ? banks[0].id : null);
      this.format.set(formats.length === 1 ? formats[0].id : null);
      await this.load();
    });
  }

  private get<T>(path: string): Promise<T> {
    return firstValueFrom(this.http.get<T>(this.base + path));
  }

  private async run(work: () => Promise<void>): Promise<void> {
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

  private async load(): Promise<void> {
    const id = this.statement();
    this.choosing.set(null);
    this.choices.set(null);
    if (id) {
      this.lines.set(await this.get<Line[]>(`/${id}/lines`));
    } else {
      this.statements.set(await this.get<Statement[]>('/'));
    }
  }

  protected async open(id: string | null): Promise<void> {
    await this.router.navigate([], { queryParams: { statement: id } });
    await this.run(() => this.load());
  }

  protected upload(picker: HTMLInputElement): Promise<void> {
    const file = picker.files?.[0];
    const [bank, format] = [this.bank(), this.format()];
    picker.value = '';
    if (!file) {
      return Promise.resolve();
    }
    if (!bank || !format) {
      this.message.set(t('Choose a bank and a file format first.'));
      return Promise.resolve();
    }
    return this.run(async () => {
      const form = new FormData();
      form.append('file', file);
      const result = await firstValueFrom(this.http.post<{ added: number; skipped: number }>(`${this.base}/import`, form, { params: { bankId: bank, formatId: format } }));
      this.toasts.good(t('{0} lines added, {1} already held.', result.added, result.skipped));
      await this.load();
    });
  }

  protected async remove(statement: Statement): Promise<void> {
    const yes = await this.dialogs.confirm({ title: t('Remove this statement?'), message: t('It and its lines leave the list. Nothing is deleted: their history stays.'),
      confirmLabel: t('Remove'), danger: true });
    if (!yes) {
      return;
    }
    return this.run(async () => {
      await firstValueFrom(this.http.delete(`${this.base}/${statement.id}`));
      await this.load();
    });
  }

  protected choose(line: Line): Promise<void> {
    return this.run(async () => {
      this.choices.set(await this.get<Choices>(`/lines/${line.id}/choices`));
      this.choosing.set(line.id);
    });
  }

  private act(line: Line, action: string): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.http.post(`${this.base}/lines/${line.id}/${action}`, null));
      await this.load();
    });
  }

  protected match(line: Line, document: Match): Promise<void> {
    return this.act(line, `match/${document.id}`);
  }

  protected ignore(line: Line): Promise<void> {
    return this.act(line, 'ignore');
  }

  protected clear(line: Line): Promise<void> {
    return this.act(line, 'clear');
  }

  protected show(line: Line): Promise<boolean> {
    return this.router.navigate(['/documents', line.documentCategoryId, line.documentId]);
  }

  /** The normal document form with the bank and date filled in: the person picks the client or supplier and types the invoice numbers there. */
  protected write(line: Line, choices: Choices, type: NewDocument): Promise<boolean> {
    const side = type.bankIsFrom ? { fromId: choices.bankId, fromName: choices.bank } : { toId: choices.bankId, toName: choices.bank };
    return this.router.navigate(['/documents', type.categoryId, 'new', type.typeId], { queryParams: { ...side, bankDate: line.date, bankLineId: line.id, bankLine: `${formatNumber(line.amount)} · ${line.text ?? ''}` } });
  }
}
