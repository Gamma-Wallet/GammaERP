import { Dialogs } from '../../ui/dialog';
import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { errorsOf } from '../../core/entities';
import { OperationsApi, StoredFile, save } from '../../core/operations';
import { Button, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { CellTemplate, Column, Table } from '../../ui/table';
import { Toasts } from '../../ui/toast';

/** A size people read: bytes, KB, MB. */
export function sizeText(bytes: number): string {
  return bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** The company's files: searched by name, uploaded, downloaded. */
@Component({
  selector: 'app-files',
  imports: [TranslatePipe, Button, CellTemplate, Icon, Input, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ 'Files' | t }}</h1>
    </div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="form">
      <input uiInput type="search" [placeholder]="'Search by name' | t" [attr.aria-label]="'Search' | t" (input)="typed($any($event.target).value)" />
      <div class="actions">
        <button uiButton variant="primary" [disabled]="busy()" (click)="picker.click()"><ui-icon name="plus" />{{ 'Upload' | t }}</button>
        <input #picker type="file" hidden (change)="upload(picker)" />
      </div>
    </section>
    <ui-table [columns]="columns" [rows]="rows()" [total]="total()" [(page)]="page" [pageSize]="50" [sortLocally]="true" [ariaLabel]="'Files' | t"
      [emptyText]="'No files yet.' | t" [loading]="busy()">
      <ng-template uiCell="download" let-row>
        <button uiButton size="small" (click)="download($any(row))">{{ 'Download' | t }}</button>
        <button uiButton size="small" variant="danger" [disabled]="busy()" (click)="remove($any(row))">{{ 'Remove' | t }}</button>
      </ng-template>
    </ui-table>
  `,
  styleUrl: './operations.scss',
})
export class Files {
  private readonly api = inject(OperationsApi);
  private readonly toasts = inject(Toasts);
  private readonly dialogs = inject(Dialogs);

  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly rows = signal<(StoredFile & { sizeText: string })[]>([]);
  protected readonly total = signal(0);
  protected readonly columns: Column<StoredFile & { sizeText: string }>[] = [
    { key: 'fileName', label: t('Name'), sortable: true }, { key: 'contentType', label: t('Type'), width: '220px' },
    { key: 'sizeText', label: t('Size'), width: '110px', align: 'end' }, { key: 'createdOn', label: t('Uploaded'), format: 'datetime', width: '170px', sortable: true },
    { key: 'download', label: '', width: '200px' },
  ];
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const [search, page] = [this.search(), this.page()];
      untracked(() => void this.load(search, page));
    });
  }

  private async load(search: string, page: number): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.api.files(search, page, 50);
      this.rows.set(result.items.map(f => ({ ...f, sizeText: sizeText(f.size) })));
      this.total.set(result.total);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected typed(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
    }, 300);
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      const stored = await this.api.upload(file);
      this.toasts.good(t('{0} uploaded.', stored.fileName));
      await this.load(this.search(), 1);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Takes the file away after a confirmation; refused while a property still uses it. */
  protected async remove(file: StoredFile): Promise<void> {
    if (!(await this.dialogs.confirm({ title: t('Remove {0}?', file.fileName), message: t('The file is switched off; a property that still uses it keeps it.'), confirmLabel: t('Remove') }))) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.removeFile(file.id);
      this.toasts.good(t('{0} removed.', file.fileName));
      await this.load(this.search(), this.page());
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async download(file: StoredFile): Promise<void> {
    try {
      save(await this.api.download(file.link), file.fileName);
    } catch (error) {
      this.toasts.critical(errorsOf(error).message);
    }
  }
}
