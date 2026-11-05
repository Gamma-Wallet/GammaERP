import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { SessionService } from '../../core/session';
import { Button } from '../../ui/controls';
import { Toasts } from '../../ui/toast';

interface FileError { row: number; column: string | null; message: string }
interface FileResult { rows: number; added: number; updated: number; saved: boolean; errors: FileError[] }

interface MenuTree { screen: string | null; children: readonly MenuTree[] }

/** Whether a menu holds a screen, at any depth. */
export function hasScreen(nodes: readonly MenuTree[], screen: string): boolean {
  return nodes.some(n => n.screen === screen || hasScreen(n.children, screen));
}

/**
 * A Migration step's Export, Template and Import on the list's own screen, for whoever has Migration in their menu: the server asks the same. An import is
 * checked first and saved on Confirm, every row or none, as on the Migration screen.
 */
@Component({
  selector: 'app-migration-file',
  imports: [TranslatePipe, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (allowed()) {
      <div class="buttons">
        <button uiButton variant="quiet" (click)="download('export')">{{ 'Export' | t }}</button>
        <button uiButton variant="quiet" (click)="download('template')">{{ 'Template' | t }}</button>
        <button uiButton variant="quiet" [disabled]="busy()" (click)="picker.value = ''; picker.click()">{{ 'Import' | t }}</button>
        <input #picker type="file" hidden accept=".xlsx" (change)="chosen(picker)" />
      </div>
      @if (message() || result()) {
        <div class="result" aria-live="polite">
          @if (message()) { <p role="alert">{{ message() }}</p> }
          @if (result(); as r) {
            @if (r.errors.length) {
              <p>{{ '{0} {1} to fix. Nothing was saved.' | t: (r.errors.length) : (r.errors.length === 1 ? 'problem' : 'problems') }}</p>
              <ul>@for (e of r.errors.slice(0, 20); track $index) { <li>{{ e.row ? ('Row ' | t) + e.row + ': ' : '' }}{{ e.column ? e.column + ': ' : '' }}{{ e.message }}</li> }</ul>
            } @else {
              <p>{{ '{0} rows checked: {1} to add, {2} to update.' | t: (r.rows) : (r.added) : (r.updated) }}</p>
              <div class="buttons"><button uiButton variant="primary" [disabled]="busy()" (click)="send(true)">{{ 'Confirm' | t }}</button><button uiButton (click)="clear()">{{ 'Cancel' | t }}</button></div>
            }
          }
        </div>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .buttons { display: flex; gap: var(--space-2); }
    .result { margin-top: var(--space-2); padding: var(--space-3); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); font-size: var(--text-s); }
    .result p { margin: 0 0 var(--space-2); }
    ul { margin: 0; padding-left: var(--space-4); }
  `,
})
export class MigrationFile {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly toasts = inject(Toasts);

  /** The step's code. */
  readonly step = input.required<string>();
  /** A file went in. */
  readonly imported = output<void>();

  protected readonly allowed = computed(() => hasScreen(this.session.menu(), 'migration'));
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly result = signal<FileResult | null>(null);
  private file: File | null = null;

  protected async download(kind: 'export' | 'template'): Promise<void> {
    this.message.set(null);
    try {
      const body = await firstValueFrom(this.http.get(`/api/v1/migration/${this.step()}/${kind}`, { responseType: 'blob' }));
      const link = document.createElement('a');
      link.href = URL.createObjectURL(body);
      link.download = `${this.step()}${kind === 'template' ? '-template' : ''}.xlsx`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async chosen(picker: HTMLInputElement): Promise<void> {
    this.file = picker.files?.[0] ?? null;
    if (this.file) {
      await this.send(false);
    }
  }

  protected async send(confirm: boolean): Promise<void> {
    if (!this.file) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      const form = new FormData();
      form.append('file', this.file);
      const result = await firstValueFrom(this.http.post<FileResult>(`/api/v1/migration/${this.step()}/import`, form, { params: { confirm } }));
      this.result.set(result.saved ? null : result);
      if (result.saved) {
        this.toasts.good(t('{0} added, {1} updated.', result.added, result.updated));
        this.imported.emit();
      }
    } catch (error) {
      this.result.set(null);
      this.message.set(errorsOf(error).message);
    }
    this.busy.set(false);
  }

  protected clear(): void {
    this.result.set(null);
    this.file = null;
  }
}
