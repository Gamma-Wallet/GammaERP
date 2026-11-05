import { TranslatePipe } from '../../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { errorsOf } from '../../core/entities';
import { PosApi, PosPlace, PosShow, TakenSeat } from '../../core/pos';
import { Button } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { seatRows } from './pos-reserve-logic';

/**
 * A cinema or theatre (the founder, 2026-10-31): the shows to come, each show's seats, and the seats chosen booked on one bill that is paid at
 * once. A seat taken shows who holds it and can be cancelled, with the others of its bill.
 */
@Component({
  selector: 'app-pos-events',
  imports: [TranslatePipe, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="18" />{{ message() }}</p> }
    @if (show(); as s) {
      <div class="head">
        <button type="button" class="back" [attr.aria-label]="'Back to the shows' | t" (click)="close()"><ui-icon name="chevron-left" [size]="22" /></button>
        <h1>{{ s.name }}</h1>
        <span class="muted">{{ date(s.day) }} {{ s.startsAt?.slice(0, 5) }} · {{ s.auditorium }}@if (s.price !== null) { · {{ n(s.price) }} }</span>
      </div>
      <div class="plan">
        <p class="screen">{{ 'Screen / stage' | t }}</p>
        @for (r of rows(); track r.id) {
          <div class="row">
            <span class="name">{{ r.name }}</span>
            @for (seat of r.seats; track seat.id) {
              <button type="button" class="seat" [class.taken]="taken().has(seat.id)" [class.chosen]="chosen().has(seat.id)" [attr.aria-pressed]="chosen().has(seat.id)"
                [attr.aria-label]="'Seat ' + (seat.name ?? seat.code)" (click)="tap(seat)">{{ seat.name ?? seat.code }}</button>
            }
          </div>
        } @empty {
          <p class="muted">{{ 'This auditorium has no seats yet: add its rows and seats in the point of sale\\'s configuration.' | t }}</p>
        }
      </div>
      @if (held(); as h) {
        <div class="foot">
          <span>{{ 'Taken{0}' | t: (h.guest ? ' by ' + h.guest : '') }}</span><span class="spacer"></span>
          <button uiButton size="touch" (click)="held.set(null)">{{ 'Keep' | t }}</button>
          <button uiButton size="touch" variant="danger" [disabled]="busy() || !h.billId" (click)="cancel(h.billId!)">{{ 'Cancel the booking' | t }}</button>
        </div>
      } @else {
        <div class="foot">
          <input class="guest" type="text" [attr.aria-label]="'Guest' | t" [placeholder]="'Guest (optional)' | t" [value]="guest()" (input)="guest.set($any($event.target).value)" />
          <span>{{ '{0} seat(s)' | t: (chosen().size) }}</span><span class="spacer"></span>
          <button uiButton size="touch" variant="primary" [disabled]="!chosen().size || busy()" (click)="book()">{{ busy() ? ('Booking…' | t) : ('Book and pay' | t) }}</button>
        </div>
      }
    } @else {
      <h1>{{ 'Shows' | t }}</h1>
      <div class="shows">
        @for (s of shows(); track s.id) {
          <button type="button" class="show" (click)="open(s)">
            <strong>{{ s.name }}</strong>
            <span>{{ date(s.day) }} {{ s.startsAt?.slice(0, 5) }}</span>
            <span class="muted">{{ '{0} · {1} of {2} free' | t: (s.auditorium) : (s.seats - s.taken) : (s.seats) }}</span>
          </button>
        } @empty {
          <p class="muted">{{ 'No shows to come. They are set in Point of sale › Shows.' | t }}</p>
        }
      </div>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--space-3); grid-column: 1 / -1; }
    h1 { margin: 0; font-size: 1.5rem; }
    .muted { color: var(--muted); }
    .message { display: flex; align-items: center; gap: var(--space-2); margin: 0; padding: var(--space-2) var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .head, .foot { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); }
    .foot { padding: var(--space-3); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); }
    .spacer { flex: 1; }
    .back { display: grid; place-items: center; width: 44px; height: 44px; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); cursor: pointer; }
    .guest { min-height: 44px; min-width: 200px; padding: 0 var(--space-3); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); font: inherit; }
    .shows { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--space-3); }
    .show { display: grid; gap: var(--space-1); padding: var(--space-4); text-align: left; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); font: inherit; cursor: pointer; }
    .plan { display: grid; gap: var(--space-2); padding: var(--space-4); overflow-x: auto; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); }
    .screen { margin: 0 0 var(--space-3); padding: var(--space-1); text-align: center; color: var(--muted); border-bottom: 3px solid var(--line); }
    .row { display: flex; align-items: center; gap: var(--space-2); }
    .row .name { width: 48px; color: var(--muted); }
    .seat { min-width: var(--control-h); min-height: var(--control-h); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface-2); font: inherit; cursor: pointer; }
    .seat.chosen { border-color: var(--primary); background: var(--primary); color: var(--primary-ink); }
    .seat.taken { background: var(--line); color: var(--muted); }
  `,
})
export class PosEvents {
  private readonly api = inject(PosApi);

  readonly pointOfSale = input.required<string>();
  readonly places = input<PosPlace[]>([]);
  /** A booking's bill, to be paid at once. */
  readonly billed = output<string>();

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly shows = signal<PosShow[]>([]);
  protected readonly show = signal<PosShow | null>(null);
  protected readonly taken = signal<ReadonlyMap<string, TakenSeat>>(new Map());
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly held = signal<TakenSeat | null>(null);
  protected readonly guest = signal('');
  protected readonly rows = computed(() => {
    const show = this.show();
    return show ? seatRows(this.places(), show.auditoriumId) : [];
  });

  constructor() {
    queueMicrotask(() => void this.loadShows());
  }

  private async loadShows(): Promise<void> {
    try {
      this.shows.set(await this.api.shows(this.pointOfSale()));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async open(show: PosShow): Promise<void> {
    this.show.set(show);
    this.chosen.set(new Set());
    this.held.set(null);
    this.guest.set('');
    await this.loadTaken(show.id);
  }

  protected close(): void {
    this.show.set(null);
    void this.loadShows();
  }

  private async loadTaken(show: string): Promise<void> {
    this.taken.set(new Map((await this.api.taken(show)).map(t => [t.seatId, t])));
  }

  protected tap(seat: PosPlace): void {
    const held = this.taken().get(seat.id);
    if (held) {
      this.held.set(held);
      return;
    }
    this.held.set(null);
    this.chosen.update(set => {
      const next = new Set(set);
      if (!next.delete(seat.id)) {
        next.add(seat.id);
      }
      return next;
    });
  }

  protected async book(): Promise<void> {
    const show = this.show();
    if (!show) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      this.billed.emit(await this.api.bookSeats(this.pointOfSale(), { showId: show.id, seatIds: [...this.chosen()], guest: this.guest() || null }));
    } catch (error) {
      this.message.set(errorsOf(error).message);
      this.chosen.set(new Set());
      await this.loadTaken(show.id);
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(bill: string): Promise<void> {
    const show = this.show();
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.cancelSeats(bill);
      this.held.set(null);
      if (show) {
        await this.loadTaken(show.id);
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
