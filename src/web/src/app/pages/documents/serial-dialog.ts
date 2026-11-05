import { TranslatePipe } from '../../core/t.pipe';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LineSerial } from '../../core/documents';
import { SerialRow, OperationsApi } from '../../core/operations';
import { Button, Checkbox, Input } from '../../ui/controls';
import { DialogFrame } from '../../ui/dialog';

export interface SerialDialogData {
  product: string;
  productId: string;
  quantity: number;
  serials: LineSerial[];
  /** Typed when they come new (a purchase), picked from what the side holds otherwise. */
  mode: 'type' | 'pick';
  holderId: string | null;
  readOnly: boolean;
}

/** Serial numbers as typed: one per line, a part number after a comma, a semicolon or a tab; empty lines left out. */
export function parseSerials(text: string): LineSerial[] {
  return text.split(/\r?\n/).map(l => l.trim()).filter(Boolean).map(l => {
    const [serial, part] = l.split(/[,;\t]/).map(p => p.trim());
    return { serial, partNumber: part || null };
  });
}

/** Serial numbers as the box shows them. */
export function serialsText(serials: readonly LineSerial[]): string {
  return serials.map(s => (s.partNumber ? `${s.serial}, ${s.partNumber}` : s.serial)).join('\n');
}

/** The serial numbers of a line: typed for what comes new, picked from what the side holds for what leaves, one per unit. */
@Component({
  selector: 'app-serial-dialog',
  imports: [TranslatePipe, Button, Checkbox, DialogFrame, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'Serial numbers: ' + data.product" width="520px">
      <div class="grid">
        <p class="count" [class.short]="count() !== data.quantity">{{ '{0} of {1}' | t: (count()) : (data.quantity) }}</p>
        @if (data.readOnly) {
          <ul>@for (s of data.serials; track s.serial) { <li>{{ s.serial }}@if (s.partNumber) { <span class="muted"> · {{ s.partNumber }}</span> }</li> }</ul>
        } @else if (data.mode === 'type') {
          <textarea uiInput rows="10" [attr.aria-label]="'Serial numbers' | t" [placeholder]="'One per line; a part number after a comma' | t" [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
        } @else {
          <input uiInput type="search" [placeholder]="'Search' | t" [attr.aria-label]="'Search serial numbers' | t" (input)="search.set($any($event.target).value)" />
          <div class="list">
            @for (s of shown(); track s.id) {
              <ui-checkbox [checked]="isChosen(s.serial)" (checkedChange)="toggle(s, $event)">{{ s.serial }}@if (s.partNumber) { <span class="muted"> · {{ s.partNumber }}</span> }</ui-checkbox>
            } @empty { <p class="muted">{{ 'No serial number of this product is here.' | t }}</p> }
          </div>
        }
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ data.readOnly ? ('Close' | t) : ('Cancel' | t) }}</button>
        @if (!data.readOnly) { <button uiButton variant="primary" (click)="done()">{{ 'Done' | t }}</button> }
      </ng-container>
    </ui-dialog>
  `,
  styles: `
    .grid { display: grid; gap: var(--space-3); }
    p { margin: 0; } .count { font-weight: 600; } .count.short { color: var(--critical); }
    textarea { width: 100%; height: auto; padding: var(--space-2); font-family: inherit; }
    .list { display: grid; gap: var(--space-2); max-height: 280px; overflow-y: auto; }
    ul { margin: 0; padding-left: var(--space-4); }
  `,
})
export class SerialDialog {
  protected readonly data = inject<SerialDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<LineSerial[]>>(DialogRef);
  private readonly api = inject(OperationsApi);

  protected readonly text = signal(serialsText(this.data.serials));
  protected readonly search = signal('');
  protected readonly held = signal<SerialRow[]>([]);
  protected readonly chosen = signal<LineSerial[]>(this.data.serials);
  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.held().filter(s => !term || s.serial.toLowerCase().includes(term) || (s.partNumber ?? '').toLowerCase().includes(term));
  });
  protected readonly count = computed(() => (this.data.mode === 'type' && !this.data.readOnly ? parseSerials(this.text()).length : this.chosen().length));

  constructor() {
    if (this.data.mode === 'pick' && !this.data.readOnly && this.data.holderId) {
      void this.api.serials(this.data.productId, this.data.holderId).then(rows => this.held.set(rows)).catch(() => this.held.set([]));
    }
  }

  protected isChosen(serial: string): boolean {
    return this.chosen().some(s => s.serial === serial);
  }

  protected toggle(row: SerialRow, on: boolean): void {
    this.chosen.update(all => (on ? [...all, { serial: row.serial, partNumber: row.partNumber }] : all.filter(s => s.serial !== row.serial)));
  }

  protected done(): void {
    this.ref.close(this.data.mode === 'type' ? parseSerials(this.text()) : this.chosen());
  }
}
