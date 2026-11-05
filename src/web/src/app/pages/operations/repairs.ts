import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';

interface Stage {
  id: string;
  name: string | null;
  isFinal: boolean;
}

interface Card {
  id: string;
  intakeId: string;
  intakeCode: string | null;
  intakeCategoryId: string | null;
  client: string | null;
  item: string | null;
  comeBackOn: string | null;
  technicianId: string | null;
  technician: string | null;
  repairStageId: string;
  offerId: string | null;
  offerCode: string | null;
  offerCategoryId: string | null;
  decision: 'open' | 'accepted' | 'refused';
  invoiceId: string | null;
  invoiceCode: string | null;
  invoiceCategoryId: string | null;
}

interface Option {
  id: string;
  name: string | null;
}

interface Board {
  stages: Stage[];
  cards: Card[];
  technicians: Option[];
}

interface Made {
  id: string;
  categoryId: string | null;
}

/** The repair board: one column per stage, every step by hand, from the intake to the invoice when the client comes back. */
@Component({
  selector: 'app-repairs',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Repair board' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="form">
      <ui-field [label]="'Find a client' | t" [hint]="'By name or phone' | t"><input uiInput type="search" [value]="search()" (input)="find($any($event.target).value)" /></ui-field>
      @for (c of clients(); track c.id) { <button uiButton [variant]="client()?.id === c.id ? 'primary' : 'default'" (click)="client.set(c)">{{ c.name }}</button> }
      @if (!client()) {
        <ui-field [label]="'Name' | t"><input uiInput [value]="name()" (input)="name.set($any($event.target).value)" /></ui-field>
        <ui-field [label]="'Phone' | t"><input uiInput [value]="phone()" (input)="phone.set($any($event.target).value)" /></ui-field>
      }
      <ui-field [label]="'Technician' | t"><ui-select [options]="technicians()" [value]="technician()" (valueChange)="technician.set($event)" /></ui-field>
      <div class="actions"><button uiButton variant="primary" [disabled]="busy() || (!client() && !name().trim() && !phone().trim())" (click)="newJob()">{{ 'New job' | t }}</button></div>
    </section>
    <div class="board">
      @for (column of columns(); track column.stage.id) {
        <section class="column">
          <h2>{{ column.stage.name }} <span class="muted">{{ column.cards.length }}</span></h2>
          @for (c of column.cards; track c.id) {
            <article class="card">
              <strong>{{ c.client }}</strong>
              @if (c.item) { <span>{{ c.item }}</span> }
              <button class="link" (click)="open(c.intakeCategoryId, c.intakeId)">{{ c.intakeCode }}</button>
              @if (c.comeBackOn) { <span class="muted">{{ 'Come back on' | t }}: {{ c.comeBackOn }}</span> }
              <ui-select [options]="technicians()" [value]="c.technicianId" [ariaLabel]="'Technician' | t" (valueChange)="assign(c, $event)" />
              @if (c.offerId) {
                <button class="link" (click)="open(c.offerCategoryId, c.offerId)">{{ c.offerCode }}</button>
                @if (c.decision === 'accepted') { <span>{{ 'Accepted' | t }}</span> }
                @if (c.decision === 'refused') { <span>{{ 'Refused' | t }}</span> }
                @if (c.decision !== 'accepted') { <button uiButton [disabled]="busy()" (click)="decide(c, true)">{{ 'Accepted' | t }}</button> }
                @if (c.decision !== 'refused' && !c.invoiceId) { <button uiButton [disabled]="busy()" (click)="decide(c, false)">{{ 'Refused' | t }}</button> }
              } @else {
                <button uiButton [disabled]="busy()" (click)="act(c, 'offer')">{{ 'Make the offer' | t }}</button>
              }
              @if (c.invoiceId) {
                <button class="link" (click)="open(c.invoiceCategoryId, c.invoiceId)">{{ c.invoiceCode }}</button>
              } @else if (c.decision === 'accepted') {
                <button uiButton variant="primary" [disabled]="busy()" (click)="act(c, 'bill')">{{ 'Make the bill' | t }}</button>
              }
              <ui-select [options]="stageOptions()" [value]="c.repairStageId" [ariaLabel]="'Stage' | t" (valueChange)="move(c, $event)" />
            </article>
          }
        </section>
      }
    </div>
  `,
  styleUrl: './operations.scss',
  styles: `
    .board { display: flex; gap: var(--space-3); overflow-x: auto; align-items: flex-start; }
    .column { flex: 0 0 250px; display: grid; gap: var(--space-2); padding: var(--space-3); border: 1px solid var(--line); border-radius: var(--radius-l); }
    .card { display: grid; gap: var(--space-1); padding: var(--space-2); border: 1px solid var(--line); border-radius: var(--radius-s); background: var(--surface); }
    h2 { margin: 0; font: 500 var(--title-s) var(--font-title); }
    h2 .muted { color: var(--muted); font: 400 var(--text-s) var(--font-text); }
    .link { padding: 0; border: 0; background: none; color: var(--link); text-align: left; text-decoration: underline; cursor: pointer; }
  `,
})
export class Repairs {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = '/api/v1/repairs';
  private timer?: ReturnType<typeof setTimeout>;

  private readonly board = signal<Board>({ stages: [], cards: [], technicians: [] });
  protected readonly columns = computed(() => this.board().stages.map(stage => ({ stage, cards: this.board().cards.filter(c => c.repairStageId === stage.id) })));
  protected readonly stageOptions = computed<SelectOption[]>(() => this.board().stages.map(s => ({ value: s.id, label: s.name ?? '' })));
  protected readonly technicians = computed<SelectOption[]>(() => this.board().technicians.map(s => ({ value: s.id, label: s.name ?? '' })));
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly search = signal('');
  protected readonly clients = signal<Option[]>([]);
  protected readonly client = signal<Option | null>(null);
  protected readonly name = signal('');
  protected readonly phone = signal('');
  protected readonly technician = signal<string | null>(null);

  constructor() {
    void this.run(() => this.load());
  }

  private async load(): Promise<void> {
    this.board.set(await firstValueFrom(this.http.get<Board>(`${this.base}/`)));
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

  protected find(text: string): void {
    this.search.set(text);
    this.client.set(null);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.run(async () => {
      this.clients.set(text.trim() ? await firstValueFrom(this.http.get<Option[]>(`${this.base}/clients`, { params: { search: text.trim() } })) : []);
    }), 300);
  }

  protected newJob(): Promise<void> {
    return this.run(async () => {
      const made = await firstValueFrom(this.http.post<Made>(`${this.base}/`, { partnerId: this.client()?.id ?? null, name: this.name(), phone: this.phone(), technicianId: this.technician() }));
      await this.open(made.categoryId, made.id);
    });
  }

  protected open(category: string | null, id: string): Promise<boolean> {
    return this.router.navigate(['/documents', category, id]);
  }

  protected act(card: Card, action: 'offer' | 'bill'): Promise<void> {
    return this.run(async () => {
      const made = await firstValueFrom(this.http.post<Made>(`${this.base}/${card.id}/${action}`, null));
      await this.open(made.categoryId, made.id);
    });
  }

  protected decide(card: Card, accepted: boolean): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.http.post(`${this.base}/${card.id}/decide`, null, { params: { accepted } }));
      await this.load();
    });
  }

  protected move(card: Card, stage: string | null): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.http.post(`${this.base}/${card.id}/move/${stage}`, null));
      await this.load();
    });
  }

  protected assign(card: Card, technician: string | null): Promise<void> {
    return this.run(async () => {
      await firstValueFrom(this.http.post(`${this.base}/${card.id}/technician`, null, { params: technician ? { technician } : {} }));
      await this.load();
    });
  }
}
