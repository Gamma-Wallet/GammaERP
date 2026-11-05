import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';

interface Waiting {
  id: string;
  code: string | null;
  type: string | null;
  partner: string | null;
  total: number;
  writtenBy: string | null;
  date: string | null;
  canApprove: boolean;
}

/** The documents over their writer's limit: a person whose limit covers one approves it, and only then can it become the next document. */
@Component({
  selector: 'app-approvals',
  imports: [TranslatePipe, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Approvals' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (rows().length) {
      <table>
        <thead><tr><th>{{ 'Document' | t }}</th><th>{{ 'Document type' | t }}</th><th>{{ 'Client or supplier' | t }}</th><th>{{ 'Date' | t }}</th><th>{{ 'Written by' | t }}</th><th class="num">{{ 'Total' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (d of rows(); track d.id) {
            <tr>
              <td>{{ d.code }}</td><td>{{ d.type }}</td><td>{{ d.partner }}</td><td>{{ d.date ? date(d.date) : '' }}</td><td>{{ d.writtenBy }}</td><td class="num">{{ n(d.total) }}</td>
              <td><button uiButton [disabled]="busy() || !d.canApprove" [title]="d.canApprove ? '' : ('Over your approval limit' | t)" (click)="approve(d)">{{ 'Approve' | t }}</button></td>
            </tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="muted">{{ 'Nothing waits for approval.' | t }}</p>
    }
  `,
  styleUrl: './operations.scss',
})
export class Approvals {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly rows = signal<Waiting[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.rows.set(await firstValueFrom(this.http.get<Waiting[]>('/api/v1/approvals')));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async approve(document: Waiting): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await firstValueFrom(this.http.post(`/api/v1/approvals/${document.id}`, null));
      this.toasts.good(t('{0} approved.', document.code));
      await this.load();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
