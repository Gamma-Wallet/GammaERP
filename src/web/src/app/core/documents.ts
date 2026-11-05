import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toSaveJson } from '../ui/property-form.logic';
import { Category, Element, ElementType, HistoryEntry, Page } from './entities';

/** A row of a document list (DocumentListItemDto): totals in the company's currency. */
/** A serial number on a line. */
export interface LineSerial {
  serial: string;
  partNumber?: string | null;
}

export interface DocumentListItem {
  id: string;
  code: string | null;
  typeId: string;
  externalNumber: string | null;
  dueDate?: string | null;
  opportunityId?: string | null;
  fromId: string | null;
  fromName: string | null;
  toId: string | null;
  toName: string | null;
  posted: boolean;
  postingDate: string | null;
  documentDate: string | null;
  currencyId: string | null;
  exchangeRate: number | null;
  currencyCode?: string | null;
  discount: number;
  value: number;
  vat: number;
  total: number;
  createdBy: string | null;
  createdOn: string;
  reversedDocumentCode: string | null;
}

/** A document line (TransactionDto). Amounts the server works out come back filled in. */
export interface Line {
  id?: string | null;
  /** On a payment: the internal code of the document it covers. */
  referenceNumber?: string | null;
  lot?: string | null;
  lotExpiryDate?: string | null;
  tracksLot?: boolean;
  idSup?: string | null;
  typeId: string;
  typeConfId?: string | null;
  name?: Record<string, string>;
  fromId?: string | null;
  fromName?: string | null;
  toId?: string | null;
  toName?: string | null;
  elementId?: string | null;
  elementName?: string | null;
  serials?: LineSerial[];
  tracksSerial?: boolean;
  currencyId?: string | null;
  vatGroupId?: string | null;
  unitId?: string | null;
  unitName?: string | null;
  price?: number | null;
  discount?: number | null;
  quantity?: number | null;
  value?: number | null;
  vat?: number | null;
  valueVat?: number | null;
  exchangeRate?: number | null;
  valueLcu?: number | null;
  valueVatLcu?: number | null;
  debit?: number | null;
  credit?: number | null;
  description?: Record<string, string> | null;
}

export interface DocumentTotal {
  currencyId: string | null;
  discount: number | null;
  value: number | null;
  vat: number | null;
  valueVat: number | null;
  valueLcu: number | null;
  vatLcu: number | null;
  valueVatLcu: number | null;
}

export interface DocumentData extends Omit<Element, 'values'> {
  values: Record<string, unknown>;
  companyId?: string;
  postingDate: string | null;
  documentDate: string | null;
  fromId: string | null;
  fromName: string | null;
  toId: string | null;
  toName: string | null;
  posted: boolean;
  externalNumber: string | null;
  dueDate?: string | null;
  opportunityId?: string | null;
  paymentMethodId: string | null;
  tableId?: string | null;
  transactions: Line[];
  createdFromIds: string[];
  totals: DocumentTotal[];
  reversedDocumentId: string | null;
  reversedDocumentCode: string | null;
}

export interface Option {
  id: string;
  code: string | null;
  name: string;
}

export interface Side {
  scopeId: string;
  table: string | null;
  name: string;
}

export interface LineScope {
  confId: string;
  scopeId: string | null;
  table: string | null;
  name: string;
  stockDirection: number;
}

/** How a line's field is filled for the document type: 0 typed, 1 chosen from the database, 2 not needed. */
export interface LineFill {
  vatGroup: number | null;
  element: number | null;
  unit: number | null;
  location: number | null;
  unitPrice: number | null;
  discount: number | null;
  quantity: number | null;
  value: number | null;
  vat: number | null;
  defaultQuantity: number | null;
  defaultDiscount: number | null;
}

export interface LineKind {
  transactionTypeId: string;
  code: string | null;
  name: string;
  scopes: LineScope[];
  fill: LineFill | null;
  from: Side | null;
  to: Side | null;
}

export interface DocumentForm {
  typeId: string;
  code: string | null;
  name: string;
  neverPosted: boolean;
  /** A type exists that reverses this one. */
  hasReversal?: boolean;
  allowsPartialReversal: boolean;
  isReversal: boolean;
  holdsOpenItem: boolean;
  paysOpenItems: boolean;
  billsByPlan: boolean;
  from: Side | null;
  to: Side | null;
  paymentMethods: Option[];
  vatGroups: (Option & { rate: number })[];
  currencies: (Option & { isBase: boolean })[];
  lineKinds: LineKind[];
}

export interface LineProposal {
  units: Option[];
  unitId: string | null;
  price: number | null;
  currencyId: string | null;
  vatGroupId: string | null;
  source: string | null;
  /** The type's line scopes (confId) that hold the product: goods and services go on different lines. */
  scopeIds: string[];
  tracksSerial?: boolean;
  tracksLot?: boolean;
}

/** A lot a holder has, to pick on a line. */
export interface HeldLot {
  id: string;
  lot: string;
  expiryDate: string | null;
  quantity: number;
}

export interface PostingRow {
  line: string | null;
  chartId: string;
  account: string | null;
  accountName: string | null;
  costCenterId: string | null;
  costCenter: string | null;
  debit: number;
  credit: number;
}

/** A side's balance in one currency, and in the company's (SideBalanceDto, PartnerCurrencyBalanceDto). */
export interface SideBalance {
  currencyId: string | null;
  debit: number;
  credit: number;
  debitLcu: number;
  creditLcu: number;
}

/** A document type a document may make; refused says why not now. */
export interface DocumentFlow {
  typeId: string;
  code: string | null;
  name: string | null;
  categoryId: string | null;
  refused: string | null;
}

export interface MadeDocument {
  id: string;
  code: string | null;
  type: string | null;
  categoryId: string | null;
  posted: boolean;
}

export interface DocumentFilter {
  type?: string | null;
  search?: string;
  code?: string;
  from?: string;
  to?: string;
  externalNumber?: string;
  posted?: boolean | null;
  postedFrom?: string;
  postedTo?: string;
  datedFrom?: string;
  datedTo?: string;
  totalFrom?: string;
  totalTo?: string;
  createdBy?: string | null;
  sort?: string | null;
  descending?: boolean;
  page: number;
  pageSize: number;
}

const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

/** The day a document's rate is read for: as the API takes it. */
export function dayOf(document: { postingDate?: string | null; documentDate?: string | null }, today: string): string {
  return document.postingDate ?? document.documentDate ?? today;
}

/** The report behind a side's balance: its ledger, this year so far (what came before is its opening line); a side without a ledger has none. */
export function ledgerOf(table: string, id: string, today: string): { report: string; params: Record<string, string> } | null {
  const reports: Record<string, [string, string]> = { tbl_partner: ['partner-ledger', 'partner'], tbl_cost_center: ['cost-center-ledger', 'costCenter'], tbl_employee: ['staff-ledger', 'employee'] };
  const report = reports[table];
  return report ? { report: report[0], params: { [report[1]]: id, from: `${today.slice(0, 4)}-01-01`, to: today } } : null;
}

/** Today in the browser's own time zone (YYYY-MM-DD). */
export function localToday(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** A new document of a type, in the category the list shows, dated today. */
export function blankDocument(typeId: string, categoryId: string): DocumentData {
  return {
    id: null, idSup: null, active: true, typeId, code: null, name: {}, description: null, isComposite: false, categoryIds: [categoryId], values: {},
    accessUserIds: [], accessRoleIds: [], postingDate: localToday(), documentDate: localToday(), fromId: null, fromName: null, toId: null, toName: null,
    posted: false, externalNumber: null, paymentMethodId: null, transactions: [], createdFromIds: [], totals: [], reversedDocumentId: null,
    reversedDocumentCode: null,
  };
}

/** The document API (C3 phase 4): lists, the document and its actions, the type's form, line proposals, side balances. */
@Injectable({ providedIn: 'root' })
export class DocumentApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/documents';

  categories(): Promise<Category[]> {
    return firstValueFrom(this.http.get<Category[]>(`${this.base}/categories`));
  }

  types(): Promise<ElementType[]> {
    return firstValueFrom(this.http.get<ElementType[]>(`${this.base}/types`));
  }

  allowedTypes(category: string): Promise<ElementType[]> {
    return firstValueFrom(this.http.get<ElementType[]>(`${this.base}/types/allowed`, { params: { category } }));
  }

  list(category: string, filter: DocumentFilter): Promise<Page<DocumentListItem>> {
    return firstValueFrom(this.http.get<Page<DocumentListItem>>(this.base, { params: this.listParams(category, filter) }));
  }

  /** The payment methods a cost centre offers (null: every method). */
  async methodsAt(costCenter: string): Promise<string[] | null> {
    return (await firstValueFrom(this.http.get<{ ids: string[] | null }>(`${this.base}/payment-methods`, { params: { costCenter } }))).ids;
  }

  /** The list as it is filtered, as an Excel sheet. */
  excel(category: string, filter: DocumentFilter): Promise<Blob> {
    const params = this.listParams(category, filter);
    delete params['page'];
    delete params['pageSize'];
    return firstValueFrom(this.http.get(`${this.base}/xlsx`, { params, responseType: 'blob' }));
  }

  private listParams(category: string, filter: DocumentFilter): Record<string, string> {
    const params: Record<string, string> = { category, page: String(filter.page), pageSize: String(filter.pageSize) };
    for (const key of ['type', 'search', 'code', 'from', 'to', 'externalNumber', 'postedFrom', 'postedTo', 'datedFrom', 'datedTo', 'totalFrom', 'totalTo', 'createdBy', 'sort'] as const) {
      const value = filter[key];
      if (value) {
        params[key] = String(value).trim();
      }
    }
    if (filter.posted !== null && filter.posted !== undefined) {
      params['posted'] = String(filter.posted);
    }
    if (filter.sort) {
      params['descending'] = String(!!filter.descending);
    }
    return params;
  }

  get(id: string): Promise<DocumentData> {
    return firstValueFrom(this.http.get<DocumentData>(`${this.base}/${id}`));
  }

  form(type: string): Promise<DocumentForm> {
    return firstValueFrom(this.http.get<DocumentForm>(`${this.base}/types/${type}/form`));
  }

  matchBankLine(line: string, document: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/v1/bank-statements/lines/${line}/match/${document}`, null));
  }

  heldLots(product: string, holder: string, unit: string | null): Promise<HeldLot[]> {
    const params: Record<string, string> = { product, holder };
    if (unit) {
      params['unit'] = unit;
    }
    return firstValueFrom(this.http.get<HeldLot[]>('/api/v1/lots/held', { params }));
  }

  trackedLots(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/v1/lots/tracked'));
  }

  lineProposal(documentType: string, product: string, partner: string | null, unit: string | null, currency: string | null, date: string | null = null): Promise<LineProposal> {
    const params: Record<string, string> = { documentType, product };
    if (date) {
      params['date'] = date;
    }
    if (partner) {
      params['partner'] = partner;
    }
    if (unit) {
      params['unit'] = unit;
    }
    if (currency) {
      params['currency'] = currency;
    }
    return firstValueFrom(this.http.get<LineProposal>(`${this.base}/line-proposal`, { params }));
  }

  save(document: Partial<DocumentData>): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(this.base, toSaveJson(document), { headers: JSON_HEADERS }));
  }

  /** The types a document may make, those it may not make now with the reason. */
  flows(id: string): Promise<DocumentFlow[]> {
    return firstValueFrom(this.http.get<DocumentFlow[]>(`${this.base}/${id}/flows`));
  }

  /** The documents made from one. */
  made(id: string): Promise<MadeDocument[]> {
    return firstValueFrom(this.http.get<MadeDocument[]>(`${this.base}/${id}/made`));
  }

  /** Makes a document of a type from another: an unposted copy of its lines. */
  make(id: string, type: string): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`${this.base}/${id}/make/${type}`, null));
  }

  /** The journals under a sheet. */
  journals(id: string): Promise<DocumentData[]> {
    return firstValueFrom(this.http.get<DocumentData[]>(`${this.base}/${id}/journals`));
  }

  /** Saves a sheet with its rows, each one journal under it. */
  saveSheet(sheet: Partial<DocumentData>, journals: Partial<DocumentData>[]): Promise<{ sheet: DocumentData; journals: DocumentData[] }> {
    return firstValueFrom(this.http.post<{ sheet: DocumentData; journals: DocumentData[] }>(`${this.base}/sheet`, toSaveJson({ sheet, journals }), { headers: JSON_HEADERS }));
  }

  post(id: string): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`${this.base}/${id}/post`, null));
  }

  /** What is left to reverse of each line: its quantity and the serial numbers not yet back. */
  reversible(id: string): Promise<{ id: string; left: number; serials: string[] }[]> {
    return firstValueFrom(this.http.get<{ id: string; left: number; serials: string[] }[]>(`${this.base}/${id}/reversible`));
  }

  reverse(id: string, postingDate: string, quantities: Record<string, number> | null, serials: Record<string, string[]> | null = null): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`${this.base}/${id}/reverse`, { postingDate, quantities, serials }));
  }

  hold(id: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/${id}/hold`, null));
  }

  release(id: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/${id}/release`, null));
  }

  /** Lets go of every draft the user holds (on sign-out). */
  releaseAll(): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/release-all`, null));
  }

  holder(id: string): Promise<{ holder: string | null }> {
    return firstValueFrom(this.http.get<{ holder: string | null }>(`${this.base}/${id}/holder`));
  }

  switchOff(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/${id}`));
  }

  neighbours(id: string): Promise<{ previous: string | null; next: string | null }> {
    return firstValueFrom(this.http.get<{ previous: string | null; next: string | null }>(`${this.base}/${id}/neighbours`));
  }

  postingPreview(id: string): Promise<PostingRow[]> {
    return firstValueFrom(this.http.get<PostingRow[]>(`${this.base}/${id}/posting-preview`));
  }

  history(id: string): Promise<HistoryEntry[]> {
    return firstValueFrom(this.http.get<HistoryEntry[]>(`${this.base}/${id}/history`));
  }

  pdf(id: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.base}/${id}/pdf`, { responseType: 'blob' }));
  }

  email(id: string, to: string[], subject: string | null, message: string | null): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/${id}/email`, { to, subject, message }));
  }

  repeat(id: string, date: string, post: boolean): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`${this.base}/${id}/repeat`, { date, post }));
  }

  /** The live balance of a document's side, by its table: a cost centre's, a partner's; null when the table has none. */
  /** The rate a document of that day takes in a currency, null when there is none yet. */
  async dayRate(currency: string, date: string): Promise<number | null> {
    return (await firstValueFrom(this.http.get<{ rate: number | null }>('/api/v1/exchange-rates/rate', { params: { currency, date } }))).rate;
  }

  async balance(table: string | null, id: string): Promise<SideBalance[] | null> {
    if (table === 'tbl_cost_center') {
      return firstValueFrom(this.http.get<SideBalance[]>(`/api/v1/cost-centers/${id}/balance`));
    }
    if (table === 'tbl_partner') {
      const statistics = await firstValueFrom(this.http.get<{ currencies: SideBalance[] }>(`/api/v1/partners/${id}/statistics`));
      return statistics.currencies;
    }
    return null;
  }
}

/** The balance in the company's currency (debit − credit over every currency). */
export function balanceLcu(balances: readonly SideBalance[]): number {
  return balances.reduce((sum, b) => sum + b.debitLcu - b.creditLcu, 0);
}
