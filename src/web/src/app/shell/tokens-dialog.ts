import { HttpClient } from '@angular/common/http';
import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../core/entities';
import { t } from '../core/i18n';
import { TranslatePipe } from '../core/t.pipe';
import { Button, Field, Input } from '../ui/controls';
import { DialogFrame } from '../ui/dialog';
import { formatDate } from '../ui/format';
import { Icon } from '../ui/icon';
import { Toasts } from '../ui/toast';

interface UserToken {
  id: string;
  name: string | null;
  prefix: string | null;
  createdOn: string;
  lastUsedOn: string | null;
}

/**
 * The signed-in user's personal tokens (the founder, 2026-11-04): Claude signs in with one as this user, with this user's rights. A new token is shown
 * once; the list shows only its first characters and when it was last used. Switching one off ends it at once.
 */
@Component({
  selector: 'app-tokens-dialog',
  imports: [TranslatePipe, Button, DialogFrame, Field, Input, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [title]="'Claude tokens' | t" width="560px">
      <div class="grid">
        <p class="muted">{{ intro }}</p>
        @if (made(); as value) {
          <div class="made" role="status">
            <strong>{{ 'Copy your new token now: it is shown only once.' | t }}</strong>
            <code>{{ value }}</code>
            <div><button uiButton size="small" (click)="copy(value)"><ui-icon name="files" [size]="14" />{{ 'Copy' | t }}</button></div>
          </div>
        }
        <div class="new">
          <ui-field [label]="'Name of a new token' | t" [error]="error()">
            <input uiInput [placeholder]="'e.g. Claude Desktop' | t" [value]="name()" (input)="name.set($any($event.target).value)" />
          </ui-field>
          <button uiButton variant="primary" [disabled]="busy()" (click)="make()"><ui-icon name="plus" [size]="14" />{{ 'Make a token' | t }}</button>
        </div>
        <table [attr.aria-label]="'Your tokens' | t">
          <thead><tr><th>{{ 'Name' | t }}</th><th>{{ 'Starts with' | t }}</th><th>{{ 'Made' | t }}</th><th>{{ 'Last used' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (k of tokens(); track k.id) {
              <tr>
                <td>{{ k.name }}</td><td><code>{{ k.prefix }}…</code></td><td>{{ day(k.createdOn) }}</td><td>{{ k.lastUsedOn ? day(k.lastUsedOn) : ('Never' | t) }}</td>
                <td class="end"><button uiButton variant="quiet" size="small" [disabled]="busy()" (click)="revoke(k)">{{ 'Switch off' | t }}</button></td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="muted">{{ 'No tokens yet.' | t }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <ng-container dialog-footer>
        <button uiButton variant="quiet" (click)="ref.close()">{{ 'Close' | t }}</button>
      </ng-container>
    </ui-dialog>
  `,
  styles: `
    .grid { display: grid; gap: var(--space-4); }
    .muted { color: var(--muted); margin: 0; }
    .new { display: flex; gap: var(--space-3); align-items: flex-end; flex-wrap: wrap; } .new ui-field { flex: 1; min-width: 200px; }
    .made { display: grid; gap: var(--space-2); padding: var(--space-3); border-radius: var(--radius); background: var(--warning-bg); color: var(--ink); }
    code { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: var(--text-s); word-break: break-all; }
    table { width: 100%; border-collapse: collapse; font-size: var(--text-s); }
    th, td { padding: 6px var(--space-2); border-bottom: 1px solid var(--line); text-align: left; }
    th { color: var(--muted); font-size: var(--text-xs); font-weight: 600; } .end { text-align: right; }
  `,
})
export class TokensDialog {
  protected readonly ref = inject(DialogRef);
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);
  protected readonly intro = t("Claude (Code or Desktop) signs in with a token as you, and can do only what your roles allow. Keep a token like a password; switch it off when it's no longer used.");
  protected readonly tokens = signal<UserToken[]>([]);
  protected readonly name = signal('');
  protected readonly made = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly day = formatDate;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.tokens.set(await firstValueFrom(this.http.get<UserToken[]>('/api/v1/account/tokens')).catch(() => []));
  }

  protected async make(): Promise<void> {
    await this.act(async () => {
      const made = await firstValueFrom(this.http.post<{ token: UserToken; value: string }>('/api/v1/account/tokens', { name: this.name().trim() || null }));
      this.made.set(made.value);
      this.name.set('');
      await this.load();
    });
  }

  protected async revoke(token: UserToken): Promise<void> {
    await this.act(async () => {
      await firstValueFrom(this.http.delete(`/api/v1/account/tokens/${token.id}`));
      this.toasts.good(t('{0} was switched off.', token.name ?? ''));
      await this.load();
    });
  }

  protected async copy(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toasts.good(t('Copied.'));
    } catch {
      this.toasts.good(t('Select the token and copy it.'));
    }
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
