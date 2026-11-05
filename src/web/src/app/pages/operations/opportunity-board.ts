import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { TranslatePipe } from '../../core/t.pipe';
import { RouterLink } from '@angular/router';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';

interface Stage {
  id: string;
  name: string | null;
  chance: number;
  outcome: number;
}

interface Card {
  id: string;
  code: string | null;
  name: string | null;
  partner: string | null;
  amount: number;
  currency: string | null;
  expectedClose: string | null;
  stageId: string;
  /** How many documents are linked to it, and its posted invoices less reversals, in its currency. */
  documents: number;
  invoiced: number;
}

/** The pipeline: one column per stage. A person moves each opportunity by hand; a lost stage asks for the reason. */
@Component({
  selector: 'app-opportunity-board',
  imports: [TranslatePipe, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Pipeline' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <div class="board">
      @for (column of columns(); track column.stage.id) {
        <section class="column">
          <h2>{{ column.stage.name }} <span class="muted">{{ column.stage.chance }}%</span></h2>
          @for (card of column.cards; track card.id) {
            <article class="card">
              <strong>{{ card.name }}</strong>
              <span>{{ card.partner }}</span>
              <span class="figures">{{ n(card.amount) }} {{ card.currency }}</span>
              @if (card.expectedClose) { <span class="muted">{{ date(card.expectedClose) }}</span> }
              <a class="docs" [routerLink]="['/entities', 'opportunities']" [queryParams]="{ element: card.id }">{{ '{0} documents' | t: card.documents }}@if (card.invoiced) { · {{ 'invoiced {0}' | t: n(card.invoiced) }} }</a>
              <select class="control stage" [attr.aria-label]="'Move to' | t" [value]="card.stageId" (change)="move(card, $any($event.target))">
                @for (s of stages(); track s.id) { <option [value]="s.id" [selected]="s.id === card.stageId">{{ s.name }}</option> }
              </select>
              @if (asking() === card.id) {
                <input [attr.aria-label]="'Reason it was lost' | t" [placeholder]="'Reason it was lost' | t" (change)="lost(card, $any($event.target).value)" />
              }
            </article>
          } @empty {
            <p class="muted">{{ 'Nothing here.' | t }}</p>
          }
        </section>
      }
    </div>
  `,
  styles: `
    :host { display: block; padding: var(--space-5) var(--space-6); }
    .docs { font-size: var(--text-xs); color: var(--muted); }
    .board { display: flex; gap: var(--space-3); overflow-x: auto; align-items: flex-start; }
    .column { flex: 0 0 240px; display: grid; gap: var(--space-2); padding: var(--space-3); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    h2 { margin: 0; font: 500 var(--title-s) var(--font-title); }
    h2 .muted { font: 400 var(--text-s) var(--font-text); }
    .control.stage { height: var(--control-h-s); padding: 0 var(--space-2); font-size: var(--text-s); }
    .card { display: grid; gap: var(--space-1); padding: var(--space-2); border: 1px solid var(--line); border-radius: var(--radius-s); }
    .muted { color: var(--muted); }
    .message { color: var(--critical); }
  `,
})
export class OpportunityBoard {
  private readonly http = inject(HttpClient);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly stages = signal<Stage[]>([]);
  private readonly cards = signal<Card[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly asking = signal<string | null>(null);
  private target: string | null = null;
  protected readonly columns = computed(() => this.stages().map(stage => ({ stage, cards: this.cards().filter(c => c.stageId === stage.id) })));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const board = await firstValueFrom(this.http.get<{ stages: Stage[]; cards: Card[] }>('/api/v1/opportunities/board'));
      this.stages.set(board.stages);
      this.cards.set(board.cards);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async move(card: Card, select: HTMLSelectElement): Promise<void> {
    if (this.stages().find(s => s.id === select.value)?.outcome === 2) {
      this.target = select.value;
      this.asking.set(card.id);
      return;
    }
    await this.send(card, select.value, null);
  }

  protected async lost(card: Card, reason: string): Promise<void> {
    if (this.target && reason.trim()) {
      await this.send(card, this.target, reason);
    }
  }

  private async send(card: Card, stageId: string, lostReason: string | null): Promise<void> {
    this.message.set(null);
    this.asking.set(null);
    try {
      await firstValueFrom(this.http.put(`/api/v1/opportunities/${card.id}/stage`, { stageId, lostReason }));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
    await this.load();
  }
}
