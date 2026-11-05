import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';

interface Item {
  lineId: string;
  name: string | null;
  quantity: number;
  unit: string | null;
}

interface Card {
  orderId: string;
  code: string | null;
  table: string | null;
  waiter: string | null;
  sentOn: string;
  items: Item[];
}

interface ProducedItem {
  lineId: string;
  name: string | null;
  quantity: number;
  code: string | null;
  table: string | null;
  producedOn: string;
  productionDocument: string | null;
  canUndo: boolean;
}

interface Board {
  warningMinutes: number;
  cards: Card[];
  produced: ProducedItem[];
}

/** How often the open screen reads the board again; it only reads. */
const REFRESH_MS = 15000;

/**
 * The production point display: what a kitchen or a bar has to make, oldest first. The person says each item was produced: with a recipe that makes the production document, so the
 * ingredients leave and the product comes in for the bill to take.
 */
@Component({
  selector: 'app-production-display',
  imports: [TranslatePipe, Button, Field, Icon, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Production point display' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="form">
      <ui-field [label]="'Production point' | t" [required]="true"><ui-select [options]="points()" [value]="point()" (valueChange)="choose($event)" /></ui-field>
      <div class="actions"><button uiButton (click)="fullScreen()">{{ 'Full screen' | t }}</button></div>
    </section>
    @if (!points().length && loaded()) { <p class="muted">{{ 'Add a cost centre of type Production point first.' | t }}</p> }
    @if (board(); as b) {
      <div class="cards">
        @for (c of b.cards; track c.orderId) {
          <article class="ticket" [class.late]="minutes(c) >= b.warningMinutes">
            <header><strong>{{ c.table ?? c.code }}</strong><span>{{ minutes(c) }} min</span></header>
            <p class="muted">{{ c.code }} · {{ c.waiter }}</p>
            <ul>
              @for (i of c.items; track i.lineId) {
                <li><span>{{ i.quantity }} {{ i.name }}</span><button uiButton [disabled]="busy()" (click)="produced([i.lineId])">{{ 'Produced' | t }}</button></li>
              }
            </ul>
            <button uiButton variant="primary" [disabled]="busy()" (click)="produced(lines(c))">{{ 'All produced' | t }}</button>
          </article>
        } @empty {
          <p class="muted">{{ 'Nothing to produce.' | t }}</p>
        }
      </div>
      @if (b.produced.length) {
        <h2>{{ 'Produced' | t }}</h2>
        <table>
          <tbody>
            @for (p of b.produced; track p.lineId) {
              <tr>
                <td>{{ p.quantity }} {{ p.name }}</td><td>{{ p.table ?? p.code }}</td><td>{{ p.productionDocument }}</td>
                <td>@if (p.canUndo) { <button uiButton [disabled]="busy()" (click)="undo(p)">{{ 'Undo' | t }}</button> }</td>
              </tr>
            }
          </tbody>
        </table>
      }
      @if (notRouted().length) {
        <h2>{{ 'Ordered with no production point' | t }}</h2>
        <p class="muted">{{ notRoutedText() }}</p>
      }
    }
  `,
  styleUrl: './operations.scss',
  styles: `
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: var(--space-3); }
    .ticket { display: grid; gap: var(--space-2); padding: var(--space-3); border: 1px solid var(--line); border-radius: 8px; background: var(--surface); }
    .ticket.late { border-color: var(--warning, var(--critical)); border-width: 2px; }
    .ticket header { display: flex; justify-content: space-between; font-size: var(--title-s); }
    .ticket p { margin: 0; }
    .ticket ul { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
    .ticket li { display: flex; justify-content: space-between; align-items: center; gap: var(--space-2); }
  `,
})
export class ProductionDisplay {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/production-display';

  protected readonly points = signal<SelectOption[]>([]);
  protected readonly point = signal<string | null>(null);
  protected readonly board = signal<Board | null>(null);
  protected readonly notRouted = signal<string[]>([]);
  protected readonly notRoutedText = computed(() => this.notRouted().join(', '));
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  private readonly now = signal(Date.now());

  constructor() {
    const timer = setInterval(() => void this.load(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    void this.start();
  }

  private async start(): Promise<void> {
    try {
      const points = await firstValueFrom(this.http.get<{ id: string; name: string | null }[]>(`${this.base}/points`));
      this.points.set(points.map(p => ({ value: p.id, label: p.name ?? '' })));
      this.point.set(points.length === 1 ? points[0].id : null);
      this.notRouted.set(await firstValueFrom(this.http.get<string[]>(`${this.base}/not-routed`)));
      await this.load();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
    this.loaded.set(true);
  }

  private async load(): Promise<void> {
    const point = this.point();
    this.now.set(Date.now());
    if (!point) {
      return;
    }
    try {
      this.board.set(await firstValueFrom(this.http.get<Board>(`${this.base}/${point}`)));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected choose(point: string | null): void {
    this.point.set(point);
    this.board.set(null);
    void this.load();
  }

  protected minutes(card: Card): number {
    return Math.max(0, Math.floor((this.now() - new Date(card.sentOn).getTime()) / 60000));
  }

  protected lines(card: Card): string[] {
    return card.items.map(i => i.lineId);
  }

  private async send(action: string, lineIds: string[]): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await firstValueFrom(this.http.post(`${this.base}/${this.point()}/${action}`, { lineIds }));
      await this.load();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected produced(lineIds: string[]): Promise<void> {
    return this.send('produced', lineIds);
  }

  protected undo(item: ProducedItem): Promise<void> {
    return this.send('undo', [item.lineId]);
  }

  protected fullScreen(): void {
    void document.documentElement.requestFullscreen().catch(() => undefined);
  }
}
