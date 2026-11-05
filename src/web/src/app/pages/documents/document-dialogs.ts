import { TranslatePipe } from '../../core/t.pipe';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Line } from '../../core/documents';
import { Button, Checkbox, Field, Input } from '../../ui/controls';
import { DialogFrame } from '../../ui/dialog';
import { formatNumber } from '../../ui/format';

const today = () => new Date().toISOString().slice(0, 10);

/** A line in the reverse dialog: quantity and serial numbers are what is left; original is the line's own quantity. */
export type ReverseLine = Line & { original?: number | null };

export interface ReverseData {
  code: string;
  lines: ReverseLine[];
  partial: boolean;
}

/** What a reversal is: its posting date, and (where the type allows it) how much of each line. */
export interface ReverseResult {
  postingDate: string;
  quantities: Record<string, number> | null;
  /** Which serial numbers come back, for a line only partly reversed. */
  serials: Record<string, string[]> | null;
}

/** A line reversed by its serial numbers: how many are chosen is its quantity; a line with all of them chosen is reversed whole. */
export function serialsChosen(lines: readonly Line[], chosen: Record<string, string[] | undefined>): { quantities: Record<string, number>; serials: Record<string, string[]> } {
  const partly = lines.filter(l => l.id && l.serials?.length && chosen[l.id] && chosen[l.id]!.length !== l.serials.length);
  return { quantities: Object.fromEntries(partly.map(l => [l.id!, chosen[l.id!]!.length])), serials: Object.fromEntries(partly.map(l => [l.id!, chosen[l.id!]!])) };
}

/** The quantities to reverse, only those changed from the whole line; null when every line is reversed whole. */
export function changedQuantities(lines: readonly Line[], typed: Record<string, number | undefined>): Record<string, number> | null {
  const changed = Object.fromEntries(lines.filter(l => l.id && typed[l.id] !== undefined && typed[l.id] !== l.quantity).map(l => [l.id!, typed[l.id!]!]));
  return Object.keys(changed).length ? changed : null;
}

@Component({
  selector: 'app-reverse-dialog',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'Reverse ' + data.code" width="560px">
      <div class="grid">
        <p>{{ 'A reversal document is made and posted on the date below. {0} stays as it is, marked as reversed.' | t: (data.code) }}</p>
        <ui-field [label]="'Posting date' | t" [required]="true"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
        @if (data.partial) {
          <table>
            <thead><tr><th>{{ 'Line' | t }}</th><th class="num">{{ 'Quantity' | t }}</th><th class="num">{{ 'Left' | t }}</th><th class="num">{{ 'Reverse' | t }}</th></tr></thead>
            <tbody>
              @for (line of data.lines; track line.id) {
                <tr>
                  <td>{{ line.elementName || line.name?.['en'] }}</td>
                  <td class="num figures">{{ format(line.original ?? line.quantity) }}</td>
                  <td class="num figures">{{ format(line.quantity) }}</td>
                  @if (line.serials?.length) {
                    <td class="num figures">{{ chosenOf(line).length }}</td>
                  } @else {
                    <td class="num"><input class="control number" inputmode="decimal" [attr.aria-label]="'Reverse quantity of ' + (line.elementName ?? '')"
                      [value]="typed()[line.id!] ?? line.quantity" (input)="type(line.id!, $any($event.target).value)" /></td>
                  }
                </tr>
                @if (line.serials?.length) {
                  <tr><td colspan="4"><div class="serials">
                    @for (s of line.serials; track s.serial) {
                      <ui-checkbox [checked]="chosenOf(line).includes(s.serial)" (checkedChange)="toggle(line, s.serial, $event)">{{ s.serial }}</ui-checkbox>
                    }
                  </div></td></tr>
                }
              } @empty {
                <tr><td colspan="4" class="muted">{{ 'Nothing is left to reverse.' | t }}</td></tr>
              }
            </tbody>
          </table>
        }
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ 'Cancel' | t }}</button>
        <button uiButton variant="danger" [disabled]="!date()" (click)="done()">{{ 'Reverse' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `
    .grid { display: grid; gap: var(--space-4); } p { margin: 0; color: var(--ink-2); }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-s); }
    th, td { padding: 4px var(--space-2); border-bottom: 1px solid var(--line); text-align: left; vertical-align: middle; }
    th { height: var(--row-head-h); background: var(--surface-2); color: var(--ink-2); font-size: var(--text-xs); font-weight: 500;
      letter-spacing: 0.01em; }
    .num { text-align: right; } td.num { width: 120px; }
    .serials { display: flex; flex-wrap: wrap; gap: var(--space-3); }
  `,
})
export class ReverseDialog {
  protected readonly data = inject<ReverseData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<ReverseResult>>(DialogRef);
  protected readonly date = signal(today());
  protected readonly typed = signal<Record<string, number | undefined>>({});
  protected readonly chosen = signal<Record<string, string[] | undefined>>({});

  protected format(value: number | null | undefined): string {
    return formatNumber(value ?? 0, 3);
  }

  protected chosenOf(line: Line): string[] {
    return this.chosen()[line.id!] ?? (line.serials ?? []).map(s => s.serial);
  }

  protected toggle(line: Line, serial: string, on: boolean): void {
    const now = this.chosenOf(line);
    this.chosen.update(c => ({ ...c, [line.id!]: on ? [...now, serial] : now.filter(s => s !== serial) }));
  }

  protected type(id: string, value: string): void {
    const n = Number(value.replace(',', '.'));
    if (!Number.isNaN(n)) {
      this.typed.update(t => ({ ...t, [id]: n }));
    }
  }

  protected done(): void {
    const typed = this.data.partial ? changedQuantities(this.data.lines, this.typed()) : null;
    const { quantities, serials } = serialsChosen(this.data.lines, this.chosen());
    const some = this.data.partial && Object.keys(quantities).length > 0;
    this.ref.close({ postingDate: this.date(), quantities: some || typed ? { ...typed, ...(some ? quantities : {}) } : null, serials: some ? serials : null });
  }
}

export interface EmailResult {
  to: string[];
  subject: string | null;
  message: string | null;
}

/** The addresses typed: separated by commas, semicolons or spaces. */
export function addressesOf(typed: string): string[] {
  return typed.split(/[\s,;]+/).map(a => a.trim()).filter(Boolean);
}

@Component({
  selector: 'app-email-dialog',
  imports: [TranslatePipe, Button, DialogFrame, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'E-mail ' + data" width="520px">
      <div class="grid">
        <ui-field [label]="'Recipient' | t" [required]="true" [hint]="'Several addresses: separate them with commas' | t">
          <input uiInput type="email" multiple [value]="to()" (input)="to.set($any($event.target).value)" />
        </ui-field>
        <ui-field [label]="'Subject' | t"><input uiInput [value]="subject()" (input)="subject.set($any($event.target).value)" /></ui-field>
        <ui-field [label]="'Message' | t"><textarea uiInput [value]="message()" (input)="message.set($any($event.target).value)"></textarea></ui-field>
        <p class="muted">{{ 'The document goes as a PDF attachment.' | t }}</p>
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ 'Cancel' | t }}</button>
        <button uiButton variant="primary" [disabled]="!addresses().length" (click)="send()">{{ 'Send' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `.grid { display: grid; gap: var(--space-4); } p { margin: 0; }`,
})
export class EmailDialog {
  protected readonly data = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<EmailResult>>(DialogRef);
  protected readonly to = signal('');
  protected readonly subject = signal('');
  protected readonly message = signal('');

  protected addresses(): string[] {
    return addressesOf(this.to());
  }

  protected send(): void {
    this.ref.close({ to: this.addresses(), subject: this.subject() || null, message: this.message() || null });
  }
}

export interface RepeatResult {
  date: string;
  post: boolean;
}

@Component({
  selector: 'app-repeat-dialog',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'Repeat ' + data" width="440px">
      <div class="grid">
        <p>{{ 'A copy of {0} is made for the date below, with the same lines.' | t: (data) }}</p>
        <ui-field [label]="'Date' | t" [required]="true"><input uiInput type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></ui-field>
        <ui-checkbox [(checked)]="post">{{ 'Post it straight away' | t }}</ui-checkbox>
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ 'Cancel' | t }}</button>
        <button uiButton variant="primary" [disabled]="!date()" (click)="ref.close({ date: date(), post: post() })">{{ 'Repeat' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `.grid { display: grid; gap: var(--space-4); } p { margin: 0; color: var(--ink-2); }`,
})
export class RepeatDialog {
  protected readonly data = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<RepeatResult>>(DialogRef);
  protected readonly date = signal(today());
  protected readonly post = signal(false);
}
