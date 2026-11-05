import { SkinApi } from '../../core/skins';
import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild, effect, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { DocumentApi, DocumentForm, HeldLot } from '../../core/documents';
import { EntityApi, LookupOption, REGISTERS, errorsOf } from '../../core/entities';
import { PosApi, PosBarcode, PosPlace, PosSettings, SaleMenu, UnpaidBill, PosShift, PosShiftSummary } from '../../core/pos';
import { SessionService } from '../../core/session';
import { PosEvents } from './pos-events';
import { PosStays } from './pos-stays';
import { Button } from '../../ui/controls';
import { formatNumber, formatDate } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';
import {
  OrderLine, POSITION_X, POSITION_Y, ProductTerms, SETTING_NAMES, addToOrder, billOf, cashMoveOf, changeDue, changeQuantity, hallsOf, keypad,
  layoutTables, menuCategories, orderTotal, scanned, scopesOf, withVat,
} from './pos-logic';

type View = 'floor' | 'order' | 'shop' | 'events' | 'stays' | 'pay' | 'unpaid' | 'cash' | 'shift';

const HOME: Readonly<Record<string, View | undefined>> = { shop: 'shop', events: 'events', stays: 'stays' };

/** A document type the screen writes, with its form and the category it is filed in. */
interface Writes {
  typeId: string;
  categoryId: string;
  form: DocumentForm;
}

/**
 * The touch point of sale. A shop (the founder, 2026-10-31) scans barcodes or taps the buttons of its sale menus, and is paid at once; a bar or
 * restaurant has the floor plan of the place the user works at with each
 * table's open bills and waiters; taking an order from the menus open now and sending it as a posted bill; paying a table's or chosen
 * bills in cash or otherwise with the change shown; the bills not yet paid; money into and out of the cash desk. What it writes, where the
 * money goes and to whom a bill is written all come from the point of sale's settings (B13).
 */
@Component({
  selector: 'app-pos',
  imports: [TranslatePipe, Button, Icon, NgTemplateOutlet, PosEvents, PosStays, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pos.html',
  styleUrl: './pos.scss',
})
export class Pos {
  private readonly api = inject(PosApi);
  private readonly documents = inject(DocumentApi);
  private readonly entities = inject(EntityApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  protected readonly session = inject(SessionService);
  protected readonly n = formatNumber;
  protected readonly keys = ['7', '8', '9', '4', '5', '6', '1', '2', '3', '.', '0', 'back'];
  protected readonly settingNames = SETTING_NAMES;

  /** Where the user signed in: a point of sale, or a till below one (the founder, 2026-11-04: a cashier is connected to the till they sign in at). */
  protected readonly signedIn = this.session.place;
  /** The shop a till works as, from its settings; null when the user signed in at the point of sale itself. */
  private readonly shopOf = signal<{ id: string; name: string | null } | null>(null);
  /** The point of sale: its settings, menus, places and stock. */
  protected readonly pointOfSale = computed<{ id: string; name: string | null } | null>(() => this.shopOf() ?? this.signedIn());
  /** The till the user signed in at: it owns the shift and is recorded on each bill. */
  protected readonly till = computed(() => (this.shopOf() ? this.signedIn() : null));
  protected readonly me = computed(() => this.session.me());
  protected readonly view = signal<View>('floor');
  /** The screen it comes back to: its kind's (B2). */
  protected readonly home = computed<View>(() => HOME[this.settings()?.screen ?? ''] ?? 'floor');
  protected readonly date = formatDate;
  protected readonly shift = signal<PosShift | null>(null);
  protected readonly shiftSummary = signal<PosShiftSummary | null>(null);
  /** The shift just closed: its expected cash and difference, shown once the count is in. */
  protected readonly closedShift = signal<PosShiftSummary | null>(null);
  protected readonly openingCash = signal('');
  protected readonly countedCash = signal('');
  protected readonly shiftNote = signal('');
  protected readonly barcodes = signal<PosBarcode[]>([]);
  private readonly lotProducts = signal<ReadonlySet<string>>(new Set());
  protected readonly lotChoice = signal<{ product: { productId: string; name: string | null; price: number | null }; lots: HeldLot[] } | null>(null);
  private readonly scanBox = viewChild<ElementRef<HTMLInputElement>>('scanBox');
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);

  protected readonly settings = signal<PosSettings | null>(null);
  protected readonly places = signal<PosPlace[]>([]);
  protected readonly menus = signal<SaleMenu[]>([]);
  /** The document types the screen writes: the bill, money in, money out (from the settings). */
  private readonly writes = signal<{ bill: Writes | null; in: Writes | null; out: Writes | null; order: Writes | null }>({ bill: null, in: null, out: null, order: null });
  private readonly terms = new Map<string, ProductTerms>();

  // Floor plan
  protected readonly halls = computed(() => hallsOf(this.places()));
  protected readonly hallId = signal<string | null | undefined>(undefined);
  protected readonly hall = computed(() => this.halls().find(h => h.id === this.hallId()) ?? this.halls().at(0) ?? null);
  protected readonly onlyMine = signal(false);
  protected readonly arranging = signal(false);
  protected readonly moved = signal<Record<string, { x: number; y: number }>>({});
  protected readonly tables = computed(() => {
    const me = this.me()?.id;
    const moved = this.moved();
    return layoutTables(this.hall()?.tables ?? []).filter(t => !this.onlyMine() || t.place.waiters.some(w => w.id === me))
      .map(t => ({ ...t, ...(moved[t.place.id] ?? {}) }));
  });
  protected readonly planHeight = computed(() => Math.max(480, ...this.tables().map(t => t.y + 160)));
  protected readonly openTotal = computed(() => this.places().reduce((sum, p) => sum + p.open, 0));
  private drag: { id: string; startX: number; startY: number; x: number; y: number } | null = null;

  // Order
  protected readonly table = signal<PosPlace | null>(null);
  protected readonly categories = computed(() => menuCategories(this.menus()));
  protected readonly categoryId = signal<string | null>(null);
  protected readonly category = computed(() => this.categories().find(c => c.categoryId === this.categoryId()) ?? this.categories().at(0) ?? null);
  protected readonly order = signal<OrderLine[]>([]);
  /** The VAT a bill line carries: the bill type's first VAT group, as a new line in the document editor (sale prices are without VAT). */
  protected readonly vatRate = computed(() => this.writes().bill?.form.vatGroups.at(0)?.rate ?? 0);
  /** Each product's own VAT rate, from its line proposal, learned when it is first added; the bill type's first rate until then. */
  private readonly rates = signal<ReadonlyMap<string, number>>(new Map());
  /** The tiles on screen learn their products' own VAT rates at once, so a tile's price is right before it is first added. */
  private readonly tileRates = effect(() => {
    const products = this.category()?.products ?? [];
    untracked(() => products.forEach(p => void this.learnRate(p.productId)));
  });
  protected readonly orderTotal = computed(() => orderTotal(this.order(), l => this.rateOf(l.productId)));
  protected readonly tableBills = signal<UnpaidBill[]>([]);
  protected readonly tableOrders = signal<UnpaidBill[]>([]);
  protected readonly tableOpen = computed(() => [...this.tableBills(), ...this.tableOrders()].reduce((sum, b) => sum + b.open, 0));

  // Pay
  protected readonly payBills = signal<UnpaidBill[]>([]);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly due = computed(() => this.payBills().filter(b => this.chosen().has(b.id)).reduce((sum, b) => sum + Math.round(b.open * 100), 0) / 100);
  protected readonly methodId = signal<string | null>(null);
  protected readonly received = signal('');
  protected readonly change = computed(() => (this.received() === '' ? null : changeDue(Number(this.received()), this.due())));
  /** The methods the point of sale offers: only those linked to it (all when it has no link). */
  private readonly allowedMethods = signal<string[] | null>(null);
  protected readonly methods = computed(() => {
    const allowed = this.allowedMethods();
    return (this.writes().in?.form.paymentMethods ?? []).filter(m => !allowed || allowed.includes(m.id));
  });
  private readonly loadAllowedMethods = effect(() => {
    const place = this.pointOfSale()?.id;
    untracked(() => {
      this.allowedMethods.set(null);
      if (place) {
        void this.api.methodsAt(place).then(ids => this.allowedMethods.set(ids)).catch(() => undefined);
      }
    });
  });

  // Unpaid bills
  protected readonly unpaid = signal<UnpaidBill[]>([]);
  protected readonly unpaidMine = signal(true);
  protected readonly unpaidShown = computed(() => this.unpaid().filter(b => !this.unpaidMine() || b.createdBy === this.me()?.id));
  protected readonly waiterNames = computed(() => {
    const names = new Map<string, string>();
    for (const w of this.places().flatMap(p => p.waiters)) {
      names.set(w.id, w.name ?? '');
    }
    const me = this.me();
    if (me) {
      names.set(me.id, me.name ?? me.username ?? '');
    }
    return names;
  });

  // Money in / out
  protected readonly cashKind = signal<'in' | 'out'>('out');
  protected readonly parties = signal<LookupOption[]>([]);
  protected readonly partyId = signal<string | null>(null);
  protected readonly amount = signal('');
  protected readonly reason = signal('');

  private readonly skins = inject(SkinApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    // The company's point-of-sale skin, on this screen's own element: it wins over the built-in point-of-sale values (app-pos in _tokens.scss).
    void this.skins.applyInUse('pos', this.host.nativeElement);
    void this.documents.trackedLots().then(ids => this.lotProducts.set(new Set(ids)))
      .catch(error => this.message.set(t('The products that track lots could not be read, so no lot is asked for: {0}', errorsOf(error).message)));
    void this.load();
  }

  protected async load(): Promise<void> {
    const signedIn = this.signedIn();
    if (!signedIn) {
      this.loaded.set(true);
      return;
    }
    this.message.set(null);
    try {
      // Signed in at a till: the settings come through it from its shop, and the screen works as the shop.
      const settings = await this.api.settings(signedIn.id);
      this.shopOf.set(settings.tillId && settings.pointOfSaleId ? { id: settings.pointOfSaleId, name: settings.pointOfSale ?? '' } : null);
      const place = this.pointOfSale()!;
      const [places, menus] = await Promise.all([this.api.places(place.id), this.api.menus(place.id)]);
      // A repair shop works on the repair board.
      if (settings.screen === 'repairs') {
        await this.router.navigate(['/repairs']);
        return;
      }
      this.settings.set(settings);
      this.shift.set(await this.api.currentShift(signedIn.id));
      this.places.set(places);
      this.menus.set(menus);
      const types = await this.documents.types();
      const writes = async (typeId: string | null): Promise<Writes | null> => {
        const type = types.find(t => t.id === typeId);
        return type && typeId ? { typeId, categoryId: type.categoryIds[0] ?? '', form: await this.documents.form(typeId) } : null;
      };
      const [bill, moneyIn, moneyOut, order] = await Promise.all([writes(settings.billTypeId), writes(settings.moneyInTypeId), writes(settings.moneyOutTypeId), writes(settings.orderTypeId)]);
      this.writes.set({ bill, in: moneyIn, out: moneyOut, order });
      if (settings.screen === 'shop') {
        this.barcodes.set(await this.api.barcodes());
      }
      if (this.home() !== 'floor') {
        this.show(this.home());
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loaded.set(true);
    }
  }

  private async refresh(): Promise<void> {
    const place = this.pointOfSale();
    if (place) {
      this.places.set(await this.api.places(place.id));
    }
  }

  protected show(view: View): void {
    this.message.set(null);
    this.view.set(view);
    if (view === 'unpaid') {
      void this.loadUnpaid();
    }
    if (view === 'cash') {
      void this.loadParties();
    }
    if (view === 'shift') {
      void this.loadShift();
    }
    if (view === 'floor') {
      this.table.set(null);
      void this.refresh();
    }
    if (view === 'shop') {
      this.order.set([]);
      this.focusScan();
    }
  }

  // ---------- Shift ----------

  private async loadShift(): Promise<void> {
    const shift = this.shift();
    this.shiftSummary.set(null);
    if (shift) {
      try {
        this.shiftSummary.set(await this.api.shiftSummary(shift.id));
      } catch (error) {
        this.message.set(errorsOf(error).message);
      }
    }
  }

  protected async openShift(): Promise<void> {
    const place = this.signedIn();
    if (!place || this.openingCash() === '') {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      this.shift.set(await this.api.openShift(place.id, Number(this.openingCash())));
      this.closedShift.set(null);
      this.openingCash.set('');
      this.show(this.home());
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** The count goes in first: only the closed shift says what was expected. */
  protected async closeShift(): Promise<void> {
    const shift = this.shift();
    if (!shift || this.countedCash() === '') {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      this.closedShift.set(await this.api.closeShift(shift.id, Number(this.countedCash()), this.shiftNote()));
      this.shift.set(null);
      this.shiftSummary.set(null);
      this.countedCash.set('');
      this.shiftNote.set('');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  // ---------- Shop ----------

  /** The scanner types into this box, so it keeps the focus after every line. */
  private focusScan(): void {
    setTimeout(() => this.scanBox()?.nativeElement.focus());
  }

  /** A scanner sends the barcode and Enter: its product joins the basket, at its price. */
  protected scan(box: HTMLInputElement): void {
    const typed = box.value;
    box.value = '';
    if (!typed.trim()) {
      return;
    }
    const found = scanned(this.barcodes(), typed);
    if (!found) {
      this.message.set(t('No product has the barcode {0}.', typed.trim()));
    } else if (found.price === null) {
      this.message.set(t('{0} has no sale price.', found.name));
    } else {
      this.message.set(null);
      this.add(found);
    }
    this.focusScan();
  }

  /** The basket is paid at once: its bill is posted (stock leaves the shop), then paid. */
  protected async charge(): Promise<void> {
    const posted = await this.postBill(null);
    if (posted) {
      await this.payBill(posted);
    }
  }

  /** On to paying a bill just made: a basket, a booking, a check-out. */
  protected async payBill(id: string): Promise<void> {
    const type = this.settings()?.billTypeId;
    if (type) {
      await this.refresh();
      this.startPay((await this.api.unpaid(type, null, null, this.pointOfSale()?.id ?? null).catch(() => [])).filter(b => b.id === id));
    }
  }

  // ---------- Floor plan ----------

  protected openTable(place: PosPlace): void {
    if (this.arranging() || this.drag) {
      return;
    }
    this.table.set(place);
    this.order.set([]);
    this.tableBills.set([]);
    this.view.set('order');
    void this.loadTableBills();
  }

  private async loadTableBills(): Promise<void> {
    const table = this.table();
    const type = this.settings()?.billTypeId;
    const orderType = this.settings()?.orderTypeId;
    if (table && type) {
      this.tableBills.set(await this.api.unpaid(type, table.id).catch(() => []));
    }
    this.tableOrders.set(table && orderType ? await this.api.openOrders(orderType, table.id).catch(() => []) : []);
  }

  /** A cancelled order simply stops: what was already produced stays in stock. */
  protected async cancelOrder(order: UnpaidBill): Promise<void> {
    this.message.set(null);
    try {
      await this.api.cancelOrder(order.id);
      await this.loadTableBills();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected grab(event: PointerEvent, id: string, x: number, y: number): void {
    if (!this.arranging()) {
      return;
    }
    (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
    this.drag = { id, startX: event.clientX, startY: event.clientY, x, y };
  }

  protected move(event: PointerEvent): void {
    const d = this.drag;
    if (d) {
      const x = Math.max(0, Math.round(d.x + event.clientX - d.startX));
      const y = Math.max(0, Math.round(d.y + event.clientY - d.startY));
      this.moved.update(m => ({ ...m, [d.id]: { x, y } }));
    }
  }

  protected drop(): void {
    this.drag = null;
  }

  /** Saves where the moved tables now stand, as their Position X / Y properties (the cost-centre register). */
  protected async saveArrangement(): Promise<void> {
    const moved = this.moved();
    const register = REGISTERS['cost-centers'];
    this.busy.set(true);
    try {
      for (const [id, at] of Object.entries(moved)) {
        const place = this.places().find(p => p.id === id);
        const x = place?.properties.find(p => p.name === POSITION_X)?.id;
        const y = place?.properties.find(p => p.name === POSITION_Y)?.id;
        const element = await this.entities.element(register, id);
        const type = (await this.entities.types(register)).find(t => t.id === element.typeId);
        const xId = x ?? type?.properties.find(p => p.name['en'] === POSITION_X)?.id;
        const yId = y ?? type?.properties.find(p => p.name['en'] === POSITION_Y)?.id;
        if (!xId || !yId) {
          throw new Error(`${place?.name ?? 'This place'} has no Position X / Y properties.`);
        }
        await this.entities.saveElement(register, { ...element, values: { ...element.values, [xId]: at.x, [yId]: at.y } });
      }
      this.toasts.good(t('The floor plan is saved.'));
      this.arranging.set(false);
      await this.refresh();
      this.moved.set({});
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected cancelArrangement(): void {
    this.moved.set({});
    this.arranging.set(false);
  }

  // ---------- Order ----------

  protected gross(amount: number, productId: string | null = null): number {
    return withVat(amount, productId ? this.rateOf(productId) : this.vatRate());
  }

  private rateOf(productId: string): number {
    return this.rates().get(productId) ?? this.vatRate();
  }

  /** The product's own VAT rate, as a bill line of it would have it. */
  private async learnRate(productId: string): Promise<void> {
    const [settings, bill] = [this.settings(), this.writes().bill];
    if (!settings || !bill || this.rates().has(productId)) {
      return;
    }
    const proposal = await this.documents.lineProposal(bill.typeId, productId, settings.walkInClientId, null, null).catch(() => null);
    const rate = bill.form.vatGroups.find(v => v.id === proposal?.vatGroupId)?.rate;
    if (rate !== undefined) {
      this.rates.update(map => new Map(map).set(productId, rate));
    }
  }

  protected async add(product: { productId: string; name: string | null; price: number | null }): Promise<void> {
    void this.learnRate(product.productId);
    const place = this.pointOfSale();
    if (!this.lotProducts().has(product.productId) || !place) {
      this.order.update(lines => addToOrder(lines, product));
      return;
    }
    try {
      const lots = await this.documents.heldLots(product.productId, place.id, null);
      if (lots.length) {
        this.lotChoice.set({ product, lots });
      } else {
        this.message.set(t('No lot of {0} is held here.', product.name));
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  /** The cashier picks the lot of a product that tracks lots; nothing is chosen for them. */
  protected pickLot(lot: HeldLot): void {
    const choice = this.lotChoice();
    if (choice) {
      this.order.update(lines => addToOrder(lines, { ...choice.product, lot: lot.lot }));
    }
    this.lotChoice.set(null);
  }

  protected step(productId: string, by: number, lot: string | null = null): void {
    this.order.update(lines => changeQuantity(lines, productId, by, lot));
  }

  /** Sends the order: one posted bill at the table (stock leaves the point of sale). */
  protected async send(): Promise<void> {
    const table = this.table();
    if (table && await this.postBill(table, !!this.settings()?.orderTypeId)) {
      this.show('floor');
    }
  }

  /** The order as one posted bill, at a table or none; or sent as an order, which moves nothing until it is billed at payment. Its id. */
  private async postBill(table: PosPlace | null, asOrder = false): Promise<string | null> {
    const [settings, place, bill] = [this.settings(), this.pointOfSale(), asOrder ? this.writes().order : this.writes().bill];
    const scopes = bill ? scopesOf(bill.form, 'tbl_product') : [];
    if (!settings || !place || !bill || scopes.length === 0 || this.order().length === 0) {
      return null;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      await Promise.all(this.order().filter(l => !this.terms.has(l.productId)).map(async l => {
        const proposal = await this.documents.lineProposal(bill.typeId, l.productId, settings.walkInClientId, null, null);
        // The line whose scope holds the product (B5): goods move stock, services do not.
        const fits = scopes.find(s => proposal.scopeIds.includes(s.scope.confId));
        if (!fits) {
          throw new Error(`${l.name} fits none of the lines of ${bill.form.name}: check its category in the products register.`);
        }
        // As a new line in the document editor: the proposal's VAT group, else the bill type's first.
        this.terms.set(l.productId, { lineTypeId: fits.kind.transactionTypeId, typeConfId: fits.scope.confId, unitId: proposal.unitId,
          vatGroupId: proposal.vatGroupId ?? bill.form.vatGroups.at(0)?.id ?? null });
      }));
      const document = billOf(this.order(), { typeId: bill.typeId, categoryId: bill.categoryId }, settings, { id: place.id, name: place.name }, table?.id ?? this.till()?.id ?? null,
        this.terms);
      if ('problem' in document) {
        this.message.set(document.problem);
        return null;
      }
      const saved = await this.documents.save(document);
      if (asOrder) {
        this.toasts.good(t('Order {0} sent for {1}.', saved.code ?? '', table?.name ?? 'the table'));
        return saved.id!;
      }
      const posted = await this.documents.post(saved.id!);
      if (table) {
        this.toasts.good(t('Bill {0} sent for {1}.', posted.code ?? '', table.name ?? 'the table'));
      }
      return posted.id!;
    } catch (error) {
      this.message.set(errorsOf(error).message);
      return null;
    } finally {
      this.busy.set(false);
    }
  }

  // ---------- Pay ----------

  /** The table's open orders become one bill first; then it is paid like any other. */
  protected async payTable(): Promise<void> {
    const [settings, table] = [this.settings(), this.table()];
    if (settings?.billTypeId && settings.orderTypeId && table && this.tableOrders().length) {
      this.busy.set(true);
      this.message.set(null);
      try {
        await this.api.billOrders(settings.billTypeId, settings.orderTypeId, table.id);
        await this.loadTableBills();
      } catch (error) {
        this.message.set(errorsOf(error).message);
        return;
      } finally {
        this.busy.set(false);
      }
    }
    this.startPay(this.tableBills());
  }

  protected startPay(bills: UnpaidBill[]): void {
    this.payBills.set(bills);
    this.chosen.set(new Set(bills.map(b => b.id)));
    this.methodId.set(this.settings()?.paymentMethodId ?? null);
    this.received.set('');
    this.view.set('pay');
  }

  protected toggle(id: string): void {
    this.chosen.update(set => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  protected press(key: string, target: 'received' | 'amount'): void {
    const value = target === 'received' ? this.received : this.amount;
    value.set(keypad(value(), key === 'back' ? 'back' : key));
  }

  protected exact(): void {
    this.received.set(this.due().toFixed(2));
  }

  /** Pays the chosen bills: one money document per client into the cash desk, linked to the bills (POST /pos/bills/pay). */
  protected async pay(): Promise<void> {
    const settings = this.settings();
    const ids = this.payBills().filter(b => this.chosen().has(b.id)).map(b => b.id);
    if (!settings?.billTypeId || !settings.moneyInTypeId || !settings.cashDeskId || ids.length === 0) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.pay({ billTypeId: settings.billTypeId, paymentTypeId: settings.moneyInTypeId, cashDeskId: settings.cashDeskId, billIds: ids,
        paymentMethodId: this.methodId(), pointOfSaleId: this.pointOfSale()?.id ?? null });
      const change = this.change();
      this.toasts.good(`Paid ${this.n(this.due())}${change ? `; change ${this.n(change)}` : ''}.`);
      this.show(this.home());
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  // ---------- Unpaid bills ----------

  private async loadUnpaid(): Promise<void> {
    const type = this.settings()?.billTypeId;
    if (type) {
      this.unpaid.set(await this.api.unpaid(type, null, null, this.pointOfSale()?.id ?? null).catch(() => []));
    }
  }

  // ---------- Money in / out ----------

  protected readonly cashType = computed(() => this.writes()[this.cashKind()]);

  protected chooseCashKind(kind: 'in' | 'out'): void {
    this.cashKind.set(kind);
    void this.loadParties();
  }

  /** Who may bring or take the money: the choices of the side the money type names (e.g. staff for money out, clients for money in). */
  protected async loadParties(search = ''): Promise<void> {
    const type = this.cashType();
    const side = type?.form.from ?? type?.form.to ?? null;
    this.partyId.set(null);
    this.parties.set([]);
    if (!side) {
      return;
    }
    const found = (await this.entities.lookup(side.scopeId, search, 30).catch(() => null))?.items ?? [];
    this.parties.set(found);
    const walkIn = this.settings()?.walkInClientId;
    this.partyId.set(found.find(p => p.id === walkIn)?.id ?? (found.length === 1 ? found[0].id : null));
  }

  protected async recordCash(): Promise<void> {
    const [type, settings] = [this.cashType(), this.settings()];
    const party = this.parties().find(p => p.id === this.partyId());
    const amount = Number(this.amount());
    if (!type || !settings?.cashDeskId || !party || !(amount > 0)) {
      return;
    }
    const document = cashMoveOf(type, { id: party.id, name: party.name || party.code || '' }, { id: settings.cashDeskId, name: settings.cashDesk }, amount, this.reason());
    if ('problem' in document) {
      this.message.set(document.problem);
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      const saved = await this.documents.save(document);
      const posted = await this.documents.post(saved.id!);
      this.toasts.good(`${type.form.name} ${posted.code ?? ''}: ${this.n(amount)}.`);
      this.amount.set('');
      this.reason.set('');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** When a bill was written, in the local time (the API sends UTC). */
  protected time(at: string): string {
    return new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  protected leave(): void {
    void this.router.navigateByUrl('/');
  }
}
