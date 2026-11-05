import { TranslatePipe } from '../../core/t.pipe';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { localToday } from '../../core/documents';
import { errorsOf } from '../../core/entities';
import { PosApi, PosPlace, PosStay } from '../../core/pos';
import { Button } from '../../ui/controls';
import { formatDate } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { addDays, dayRange, roomsOf, stayOn } from './pos-reserve-logic';

const DAYS = 14;

/**
 * A hotel (the founder, 2026-10-31): the rooms by the days to come. A free night books the room, for as many nights as chosen; a stay stays open
 * until check-out, which bills the nights slept and goes on to be paid.
 */
@Component({
  selector: 'app-pos-stays',
  imports: [TranslatePipe, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="18" />{{ message() }}</p> }
    <div class="head">
      <h1>{{ 'Rooms' | t }}</h1><span class="spacer"></span>
      <button type="button" class="step" [attr.aria-label]="'Earlier days' | t" (click)="move(-7)"><ui-icon name="chevron-left" [size]="22" /></button>
      <span>{{ date(from()) }}</span>
      <button type="button" class="step" [attr.aria-label]="'Later days' | t" (click)="move(7)"><ui-icon name="chevron-right" [size]="22" /></button>
    </div>
    <div class="grid-wrap">
      <table>
        <thead><tr><th>{{ 'Room' | t }}</th>@for (d of days(); track d) { <th>{{ d.slice(8) }}</th> }</tr></thead>
        <tbody>
          @for (room of rooms(); track room.id) {
            <tr>
              <th>{{ room.name ?? room.code }}</th>
              @for (d of days(); track d) {
                @if (stayAt(room.id, d); as s) {
                  <td><button type="button" class="night held" [class.done]="s.billId" [attr.aria-label]="(room.name ?? room.code) + ', ' + (s.guest ?? 'taken')" (click)="choose(s)">{{ s.guest?.slice(0, 2) ?? '•' }}</button></td>
                } @else {
                  <td><button type="button" class="night" [attr.aria-label]="(room.name ?? room.code) + ', ' + d + ', free'" (click)="start(room, d)"></button></td>
                }
              }
            </tr>
          } @empty {
            <tr><td class="muted" [attr.colspan]="days().length + 1">{{ 'No rooms yet: add its floors and rooms in the point of sale\\'s configuration.' | t }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    @if (stay(); as s) {
      <div class="foot">
        <strong>{{ s.guest ?? ('Guest' | t) }}</strong><span>{{ date(s.fromDate) }} – {{ date(s.toDate) }}</span>
        @if (s.checkedInOn) { <span class="muted">{{ 'Checked in {0}' | t : date(s.checkedInOn) }}</span> }<span class="spacer"></span>
        <button uiButton size="touch" (click)="stay.set(null)">{{ 'Close' | t }}</button>
        @if (!s.billId) {
          <button uiButton size="touch" variant="danger" [disabled]="busy()" (click)="cancel(s)">{{ 'Cancel the stay' | t }}</button>
          @if (!s.checkedInOn) { <button uiButton size="touch" [disabled]="busy()" (click)="checkIn(s)">{{ 'Check in' | t }}</button> }
          <input class="text" type="date" [attr.aria-label]="'Extend to' | t" [min]="s.toDate" [value]="extendTo()" (input)="extendTo.set($any($event.target).value)" />
          <button uiButton size="touch" [disabled]="busy() || !extendTo()" (click)="extend(s)">{{ 'Extend' | t }}</button>
          <button uiButton size="touch" variant="primary" [disabled]="busy()" (click)="checkOut(s)">{{ 'Check out' | t }}</button>
        }
      </div>
    }
    @if (room(); as r) {
      <div class="foot">
        <strong>{{ r.name ?? r.code }}</strong><span>{{ 'from {0}' | t: (date(day())) }}</span>
        <label>{{ 'Nights' | t }} <input class="nights" type="number" min="1" [value]="nights()" (input)="nights.set(+$any($event.target).value)" /></label>
        <input class="text" type="text" [attr.aria-label]="'Guest' | t" [placeholder]="'Guest' | t" [value]="guest()" (input)="guest.set($any($event.target).value)" />
        <input class="text" type="number" min="0" [attr.aria-label]="'Price per night' | t" [placeholder]="'Price per night' | t" [value]="price()" (input)="price.set($any($event.target).value)" />
        <span class="spacer"></span>
        <button uiButton size="touch" (click)="room.set(null)">{{ 'Close' | t }}</button>
        <button uiButton size="touch" variant="primary" [disabled]="nights() < 1 || busy()" (click)="book(r)">{{ 'Book the stay' | t }}</button>
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
    .step { display: grid; place-items: center; width: var(--control-h); height: var(--control-h); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); cursor: pointer; }
    .grid-wrap { overflow-x: auto; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface); }
    table { border-collapse: collapse; width: 100%; }
    th { padding: var(--space-2); font-weight: 500; text-align: left; white-space: nowrap; }
    td { padding: 2px; }
    .night { width: 100%; min-width: 40px; height: var(--control-h); border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface-2); font: inherit; cursor: pointer; }
    .night.held { border-color: var(--primary); background: var(--primary); color: var(--primary-ink); }
    .night.held.done { background: var(--line); color: var(--muted); }
    .nights { width: 72px; min-height: var(--control-h); padding: 0 var(--space-2); border: 1px solid var(--line-strong); border-radius: var(--radius); background: var(--surface); color: var(--ink); font: inherit; }
    .text { min-height: var(--control-h); min-width: 160px; padding: 0 var(--space-3); border: 1px solid var(--line-strong); border-radius: var(--radius); background: var(--surface); color: var(--ink); font: inherit; }
  `,
})
export class PosStays {
  private readonly api = inject(PosApi);

  readonly pointOfSale = input.required<string>();
  readonly places = input<PosPlace[]>([]);
  /** A check-out's bill, to be paid at once. */
  readonly billed = output<string>();

  protected readonly date = formatDate;
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly from = signal(localToday());
  protected readonly stays = signal<PosStay[]>([]);
  protected readonly stay = signal<PosStay | null>(null);
  protected readonly room = signal<PosPlace | null>(null);
  protected readonly day = signal('');
  protected readonly nights = signal(1);
  /** The later end day a stay is extended to. */
  protected readonly extendTo = signal('');
  protected readonly guest = signal('');
  protected readonly price = signal('');
  protected readonly rooms = computed(() => roomsOf(this.places()));
  protected readonly days = computed(() => dayRange(this.from(), DAYS));

  constructor() {
    queueMicrotask(() => void this.load());
  }

  protected stayAt(room: string, day: string): PosStay | null {
    return stayOn(this.stays(), room, day);
  }

  private async load(): Promise<void> {
    try {
      this.stays.set(await this.api.stays(this.pointOfSale(), this.from(), addDays(this.from(), DAYS)));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected move(days: number): void {
    this.from.update(d => addDays(d, days));
    void this.load();
  }

  protected choose(stay: PosStay): void {
    this.room.set(null);
    this.stay.set(stay);
  }

  protected start(room: PosPlace, day: string): void {
    this.stay.set(null);
    this.room.set(room);
    this.day.set(day);
    this.nights.set(1);
    this.guest.set('');
    this.price.set('');
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await action();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected book(room: PosPlace): Promise<void> {
    return this.run(async () => {
      await this.api.bookStay(this.pointOfSale(), { roomId: room.id, fromDate: this.day(), toDate: addDays(this.day(), this.nights()), guest: this.guest() || null,
        price: this.price() === '' ? null : Number(this.price()) });
      this.room.set(null);
      await this.load();
    });
  }

  protected cancel(stay: PosStay): Promise<void> {
    return this.run(async () => {
      await this.api.cancelStay(stay.id);
      this.stay.set(null);
      await this.load();
    });
  }

  protected checkIn(stay: PosStay): Promise<void> {
    return this.run(async () => {
      this.stay.set(await this.api.checkIn(stay.id));
      await this.load();
    });
  }

  protected extend(stay: PosStay): Promise<void> {
    return this.run(async () => {
      this.stay.set(await this.api.extendStay(stay.id, this.extendTo()));
      this.extendTo.set('');
      await this.load();
    });
  }

  protected checkOut(stay: PosStay): Promise<void> {
    return this.run(async () => this.billed.emit(await this.api.checkOut(stay.id)));
  }
}
