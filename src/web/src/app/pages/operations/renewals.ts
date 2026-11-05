import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';

interface Renewal {
  contractId: string;
  code: string | null;
  partner: string | null;
  endDate: string;
  daysLeft: number;
  total: number;
}

/** The contracts ending soon: a person renews each by hand into a new draft contract, linked to the old one; nothing renews by itself. */
@Component({
  selector: 'app-renewals',
  imports: [TranslatePipe, Button, Field, Icon, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Renewals' | t }}</h1></div>
    <section class="form">
      <ui-field [label]="'Ending within (days)' | t"><input uiInput type="number" min="0" [value]="days()" (change)="choose(+$any($event.target).value)" /></ui-field>
    </section>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (rows().length) {
      <table>
        <thead><tr><th>{{ 'Contract' | t }}</th><th>{{ 'Client or supplier' | t }}</th><th>{{ 'End date' | t }}</th><th class="num">{{ 'Days left' | t }}</th><th class="num">{{ 'Total' | t }}</th><th>{{ 'New end date' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (c of rows(); track c.contractId) {
            <tr>
              <td>{{ c.code }}</td><td>{{ c.partner }}</td><td>{{ date(c.endDate) }}</td><td class="num">{{ c.daysLeft }}</td><td class="num">{{ n(c.total) }}</td>
              <td><input uiInput type="date" [attr.aria-label]="'New end date' | t" [value]="ends()[c.contractId] ?? ''" (change)="setEnd(c.contractId, $any($event.target).value)" /></td>
              <td><button uiButton [disabled]="busy() || !ends()[c.contractId]" (click)="renew(c)">{{ 'Renew' | t }}</button></td>
            </tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="muted">{{ 'No contract is ending.' | t }}</p>
    }
  `,
  styleUrl: './operations.scss',
})
export class Renewals {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly days = signal(60);
  protected readonly rows = signal<Renewal[]>([]);
  protected readonly ends = signal<Record<string, string>>({});
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    void this.load();
  }

  protected choose(days: number): void {
    this.days.set(days);
    void this.load();
  }

  protected setEnd(id: string, day: string): void {
    this.ends.update(all => ({ ...all, [id]: day }));
  }

  private async load(): Promise<void> {
    this.message.set(null);
    try {
      this.rows.set(await firstValueFrom(this.http.get<Renewal[]>('/api/v1/contract-billing/renewals', { params: { days: this.days() } })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async renew(contract: Renewal): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      const made = await firstValueFrom(this.http.post<{ id: string; code: string | null; categoryIds: string[] }>(`/api/v1/contract-billing/${contract.contractId}/renew`, null,
        { params: { endDate: this.ends()[contract.contractId] } }));
      this.toasts.good(t('{0} renewed as {1}.', contract.code, made.code));
      await this.router.navigate(['/documents', made.categoryIds[0], made.id]);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
