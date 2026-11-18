import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DocumentData } from './documents';

/** The settings a point of sale works with (B13, PosSettingsDto): its own row over the company's default, and what is still not set. */
export interface PosSettings {
  pointOfSaleId: string | null;
  billTypeId: string | null;
  billType: string | null;
  moneyInTypeId: string | null;
  moneyInType: string | null;
  moneyOutTypeId: string | null;
  moneyOutType: string | null;
  cashDeskId: string | null;
  cashDesk: string | null;
  walkInClientId: string | null;
  walkInClient: string | null;
  paymentMethodId: string | null;
  paymentMethod: string | null;
  missing: string[];
  /** The screen it opens (from its kind): tables, shop, events or stays. */
  screen?: string | null;
  /** The type a waiter sends as an order; empty: the bill itself is sent. */
  orderTypeId: string | null;
  orderType?: string | null;
  /** Bills need an open shift. */
  requiresShift: boolean;
  /** What a night is sold as (its name). */
  product?: string | null;
  /** The point of sale's name: the shop, when the user signed in at one of its tills. */
  pointOfSale?: string | null;
  /** The till the user signed in at, when it is below its point of sale. */
  tillId?: string | null;
}

export interface PosShift {
  id: string;
  pointOfSaleId: string;
  pointOfSale: string | null;
  cashDesk: string | null;
  openedBy: string | null;
  openedOn: string;
  openingCash: number;
  closedBy: string | null;
  closedOn: string | null;
  /** Empty until the count is in. */
  expectedCash: number | null;
  countedCash: number | null;
  difference: number | null;
  note: string | null;
}

export interface PosShiftSummary {
  shift: PosShift;
  bills: number;
  sales: number;
  payments: { methodId: string | null; method: string | null; countsAsCash: boolean; amount: number }[];
  moneyIn: number;
  moneyOut: number;
}

export interface PosProperty {
  id: string;
  name: string | null;
  value: unknown;
}

/** A place of the floor plan (B14, PosPlaceDto): a hall (it holds places) or a table / counter, with its open bills. */
export interface PosPlace {
  id: string;
  parentId: string | null;
  code: string | null;
  name: string | null;
  holdsPlaces: boolean;
  properties: PosProperty[];
  openBills: number;
  open: number;
  waiters: { id: string; name: string | null }[];
  typeId?: string | null;
}

export interface MenuProduct {
  productId: string;
  code: string | null;
  name: string | null;
  categoryId: string | null;
  price: number | null;
  currencyId: string | null;
}

/** A product by its barcode, with its price (PosBarcodeDto). */
export interface PosBarcode {
  barcode: string;
  productId: string;
  code: string | null;
  name: string | null;
  price: number | null;
  currencyId: string | null;
}

/** A show with how full it is (PosShowDto). */
export interface PosShow {
  id: string;
  name: string | null;
  day: string;
  startsAt: string | null;
  auditoriumId: string;
  auditorium: string | null;
  productId: string;
  price: number | null;
  seats: number;
  taken: number;
}

export interface TakenSeat {
  seatId: string;
  billId: string | null;
  guest: string | null;
}

/** A room held from a day to the day it is left; its bill once checked out (PosStayDto). */
export interface PosStay {
  id: string;
  roomId: string;
  guest: string | null;
  fromDate: string;
  toDate: string;
  price: number | null;
  billId: string | null;
  checkedInOn?: string | null;
}

export interface MenuCategory {
  categoryId: string;
  name: string | null;
  products: MenuProduct[];
}

export interface SaleMenu {
  id: string;
  code: string | null;
  name: string | null;
  startsAt: string | null;
  endsAt: string | null;
  categories: MenuCategory[];
}

export interface UnpaidBill {
  id: string;
  code: string | null;
  tableId: string | null;
  table: string | null;
  clientId: string | null;
  client: string | null;
  createdBy: string | null;
  createdOn: string;
  total: number;
  paid: number;
  reversed: number;
  open: number;
}

export interface PayBills {
  billTypeId: string;
  paymentTypeId: string;
  cashDeskId: string;
  billIds: string[];
  paymentMethodId: string | null;
  /** The point of sale paid at: its link for the method may send the money to a bank. */
  pointOfSaleId?: string | null;
}

/** The payment QR code the till shows for a method marked "shows a bank QR" (EPC069-12, euro). */
export interface BankQr {
  bankId: string;
  bank: string | null;
  iban: string;
  bic: string | null;
  holder: string;
  amount: number;
  currency: string;
  reference: string;
  payload: string;
  svg: string;
  /** The bank account is connected: Check bank can read it. */
  canCheck: boolean;
  /** The provider the method pays through by the payment link: BankOfCyprus or Jcc (cards); null for the plain bank QR. */
  provider?: string | null;
}

/** A bill declared to the tax authority after its payment was confirmed: Confirmed (code + fiscal QR), Refused or Declared (Send again). */
export interface FiscalReceipt {
  id: string;
  documentId: string;
  bill: string | null;
  status: 'Declared' | 'Confirmed' | 'Refused';
  number: number | null;
  authorityCode: string | null;
  qrText: string | null;
  qrSvg: string | null;
  errors: string | null;
  attempts: number;
}

/** A till payment's bill at Gamma Wallet: Waiting (the customer can claim), Claimed, Held (waiting for the tax authority) or Failed. */
export interface WalletBill {
  id: string;
  documentId: string;
  reference: string;
  total: number;
  status: 'Held' | 'Waiting' | 'Claimed' | 'Failed';
  link: string | null;
  qrSvg: string | null;
  errors: string | null;
  attempts: number;
}

/** A money-in line from the bank that may be the payment. */
export interface BankPaymentLine {
  externalId: string | null;
  date: string;
  amount: number;
  text: string | null;
  reference: string | null;
  counterpartyName: string | null;
  counterpartyIban: string | null;
  booked: boolean;
  mentionsReference: boolean;
  /** bank, or card (a card gateway's notice). */
  kind?: string;
}

/** The point-of-sale API: its settings and floor plan, the menus open now, the bills not yet paid and paying them. */
@Injectable({ providedIn: 'root' })
export class PosApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/pos';

  openOrders(type: string, table: string): Promise<UnpaidBill[]> {
    return firstValueFrom(this.http.get<UnpaidBill[]>(`${this.base}/orders/open`, { params: { type, table } }));
  }

  billOrders(billTypeId: string, orderTypeId: string, tableId: string): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`${this.base}/orders/bill`, { billTypeId, orderTypeId, tableId }));
  }

  cancelOrder(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/orders/${id}`));
  }

  currentShift(pointOfSale: string): Promise<PosShift | null> {
    return firstValueFrom(this.http.get<PosShift | null>(`${this.base}/shift/current`, { params: { pointOfSale } }));
  }

  openShift(pointOfSaleId: string, openingCash: number): Promise<PosShift> {
    return firstValueFrom(this.http.post<PosShift>(`${this.base}/shift/open`, { pointOfSaleId, openingCash }));
  }

  shiftSummary(id: string): Promise<PosShiftSummary> {
    return firstValueFrom(this.http.get<PosShiftSummary>(`${this.base}/shift/${id}`));
  }

  closeShift(id: string, countedCash: number, note: string): Promise<PosShiftSummary> {
    return firstValueFrom(this.http.post<PosShiftSummary>(`${this.base}/shift/${id}/close`, { countedCash, note }));
  }

  shifts(): Promise<PosShift[]> {
    return firstValueFrom(this.http.get<PosShift[]>('/api/v1/pos-shifts'));
  }

  shiftDetail(id: string): Promise<PosShiftSummary> {
    return firstValueFrom(this.http.get<PosShiftSummary>(`/api/v1/pos-shifts/${id}`));
  }

  settings(pointOfSale: string | null): Promise<PosSettings> {
    return firstValueFrom(this.http.get<PosSettings>(`${this.base}/settings`, { params: pointOfSale ? { pointOfSale } : {} }));
  }

  shows(pointOfSale: string): Promise<PosShow[]> {
    return firstValueFrom(this.http.get<PosShow[]>(`${this.base}/shows`, { params: { pointOfSale } }));
  }

  taken(show: string): Promise<TakenSeat[]> {
    return firstValueFrom(this.http.get<TakenSeat[]>(`${this.base}/shows/${show}/taken`));
  }

  /** Books seats; the id of the bill to pay. */
  bookSeats(pointOfSale: string, book: { showId: string; seatIds: string[]; guest: string | null }): Promise<string> {
    return firstValueFrom(this.http.post<string>(`${this.base}/shows/book`, book, { params: { pointOfSale } }));
  }

  cancelSeats(bill: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/shows/cancel/${bill}`, null));
  }

  stays(pointOfSale: string, from: string, to: string): Promise<PosStay[]> {
    return firstValueFrom(this.http.get<PosStay[]>(`${this.base}/stays`, { params: { pointOfSale, from, to } }));
  }

  bookStay(pointOfSale: string, book: { roomId: string; fromDate: string; toDate: string; guest: string | null; price: number | null }): Promise<PosStay> {
    return firstValueFrom(this.http.post<PosStay>(`${this.base}/stays`, book, { params: { pointOfSale } }));
  }

  /** Checks a stay out; the id of the bill to pay. */
  checkIn(stay: string): Promise<PosStay> {
    return firstValueFrom(this.http.post<PosStay>(`${this.base}/stays/${stay}/check-in`, null));
  }

  extendStay(stay: string, toDate: string): Promise<PosStay> {
    return firstValueFrom(this.http.post<PosStay>(`${this.base}/stays/${stay}/extend`, null, { params: { toDate } }));
  }

  checkOut(stay: string): Promise<string> {
    return firstValueFrom(this.http.post<string>(`${this.base}/stays/${stay}/check-out`, null));
  }

  cancelStay(stay: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/stays/${stay}`));
  }

  barcodes(): Promise<PosBarcode[]> {
    return firstValueFrom(this.http.get<PosBarcode[]>(`${this.base}/barcodes`));
  }

  places(pointOfSale: string): Promise<PosPlace[]> {
    return firstValueFrom(this.http.get<PosPlace[]>(`${this.base}/places`, { params: { pointOfSale } }));
  }

  menus(pointOfSale: string): Promise<SaleMenu[]> {
    return firstValueFrom(this.http.get<SaleMenu[]>(`${this.base}/menus/current`, { params: { pointOfSale } }));
  }

  unpaid(billTypeId: string, table: string | null = null, user: string | null = null, pointOfSale: string | null = null): Promise<UnpaidBill[]> {
    const params: Record<string, string> = { type: billTypeId };
    if (pointOfSale) {
      params['pointOfSale'] = pointOfSale;
    }
    if (table) {
      params['table'] = table;
    }
    if (user) {
      params['user'] = user;
    }
    return firstValueFrom(this.http.get<UnpaidBill[]>(`${this.base}/bills/unpaid`, { params }));
  }

  /** The payment methods a cost centre offers (null: every method). */
  /** The point of sale a place works as (a till works as its shop); null when none. */
  async pointOfSaleOf(place: string): Promise<string | null> {
    return (await firstValueFrom(this.http.get<{ pointOfSaleId: string | null }>('/api/v1/pos/point-of-sale-of', { params: { place } }))).pointOfSaleId;
  }

  async methodsAt(costCenter: string): Promise<string[] | null> {
    return (await firstValueFrom(this.http.get<{ ids: string[] | null }>(`${this.base}/payment-methods`, { params: { costCenter } }))).ids;
  }

  pay(request: PayBills): Promise<DocumentData[]> {
    return firstValueFrom(this.http.post<DocumentData[]>(`${this.base}/bills/pay`, request));
  }

  /** The QR code for paying an amount by bank; null when the method shows none. */
  async bankQr(pointOfSale: string, paymentMethod: string, amount: number, reference: string): Promise<BankQr | null> {
    const params = { pointOfSale, paymentMethod, amount: amount.toFixed(2), reference };
    return (await firstValueFrom(this.http.get<BankQr | null>(`${this.base}/bank-qr`, { params }))) ?? null;
  }

  /** The bank's own "payment received" messages for this amount and reference, not used yet (no read of the bank). */
  bankNotices(pointOfSale: string, paymentMethod: string, amount: number, reference: string): Promise<BankPaymentLine[]> {
    const params = { pointOfSale, paymentMethod, amount: amount.toFixed(2), reference };
    return firstValueFrom(this.http.get<BankPaymentLine[]>(`${this.base}/bank-notices`, { params }));
  }

  /** The payments' Gamma bills. */
  walletBills(paymentIds: string[]): Promise<WalletBill[]> {
    return firstValueFrom(this.http.get<WalletBill[]>(`${this.base}/wallet-bills`, { params: { payment: paymentIds } }));
  }

  walletSendAgain(id: string): Promise<WalletBill> {
    return firstValueFrom(this.http.post<WalletBill>(`${this.base}/wallet-bills/${id}/send-again`, {}));
  }

  walletCheckClaim(id: string): Promise<WalletBill> {
    return firstValueFrom(this.http.post<WalletBill>(`${this.base}/wallet-bills/${id}/check-claim`, {}));
  }

  /** The bills' fiscal receipts. */
  fiscalReceipts(billIds: string[]): Promise<FiscalReceipt[]> {
    return firstValueFrom(this.http.get<FiscalReceipt[]>(`${this.base}/fiscal-receipts`, { params: { bill: billIds } }));
  }

  /** Send again: declares a refused or unanswered receipt again. */
  sendFiscalAgain(id: string): Promise<FiscalReceipt> {
    return firstValueFrom(this.http.post<FiscalReceipt>(`${this.base}/fiscal-receipts/${id}/send-again`, {}));
  }

  /** Check card: reads the card gateway for this payment, then answers its notices. */
  cardCheck(pointOfSaleId: string, paymentMethodId: string, amount: number, reference: string): Promise<BankPaymentLine[]> {
    return firstValueFrom(this.http.post<BankPaymentLine[]>(`${this.base}/card-check`, { pointOfSaleId, paymentMethodId, amount, reference }));
  }

  /** Reads the bank once for the cashier: money-in lines of this amount not yet held, those mentioning the reference first. */
  bankCheck(pointOfSaleId: string, paymentMethodId: string, amount: number, reference: string): Promise<BankPaymentLine[]> {
    return firstValueFrom(this.http.post<BankPaymentLine[]>(`${this.base}/bank-check`, { pointOfSaleId, paymentMethodId, amount, reference }));
  }

  /** Pays with the bank line the cashier chose: the receipt is posted and that line matched to it in the same press. */
  payByBank(pay: PayBills, bankLine: BankPaymentLine): Promise<DocumentData[]> {
    return firstValueFrom(this.http.post<DocumentData[]>(`${this.base}/bills/pay-by-bank`, { pay, bankLine }));
  }
}
