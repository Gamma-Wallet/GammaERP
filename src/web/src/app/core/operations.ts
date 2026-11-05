import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DocumentData } from './documents';
import { Page } from './entities';

/** An ingredient of a production: how much is used and wasted, and what the place holds (ProductionLineDto). */
/** An ingredient that comes back when a product is taken apart: what comes back, what is lost (waste), what the place holds. */
export interface DisassemblyLine {
  productId: string;
  name: string | null;
  unitId: string;
  recovered: number;
  lost: number;
  held: number | null;
}

export interface ProductionLine {
  productId: string;
  name: string | null;
  unitId: string;
  quantity: number;
  wasteQuantity: number;
  held: number | null;
}

export interface OrderToProduce {
  id: string;
  code: string | null;
  date: string | null;
  client: string | null;
  products: ProductionLine[];
}

export interface DepreciationBook {
  id: string;
  name: string;
  methodId: string | null;
  method: string | null;
  chartCategoryId: string | null;
  posts: boolean;
}

export interface DepreciationLine {
  assetValueId: string;
  productId: string;
  productName: string | null;
  costCenterId: string | null;
  from: string;
  to: string;
  days: number;
  amount: number;
  bookValueBefore: number;
  bookValueAfter: number;
}

export interface Currency {
  id: string;
  code: string | null;
  name: string | null;
  isBase: boolean;
}

export interface ExchangeRate {
  id: string;
  currencyId: string;
  rateDate: string;
  rate: number;
}

export interface StoredFile {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  sha256: string;
  link: string;
  createdOn: string;
}

export interface SerialRow {
  id: string;
  productId: string;
  productCode: string | null;
  product: string | null;
  serial: string;
  partNumber: string | null;
  holderId: string | null;
  holder: string | null;
}

export interface SerialMove {
  postingDate: string | null;
  documentId: string;
  document: string | null;
  documentType: string | null;
  from: string | null;
  to: string | null;
}

export interface LotRow {
  id: string;
  lotId: string;
  productId: string;
  productCode: string | null;
  product: string | null;
  lot: string;
  expiryDate: string | null;
  holderId: string;
  holder: string | null;
  unitId: string;
  unit: string | null;
  quantity: number;
}

export interface LotStock {
  costCenterId: string;
  costCenter: string | null;
  productId: string;
  productCode: string | null;
  product: string | null;
  unitId: string;
  unit: string | null;
  quantity: number;
  inLots: number;
}

export interface SerialStock {
  costCenterId: string;
  costCenter: string | null;
  productId: string;
  productCode: string | null;
  product: string | null;
  unitId: string;
  unit: string | null;
  quantity: number;
  serials: number;
}

export interface AssetRow {
  assetValueId: string;
  productId: string;
  code: string | null;
  product: string | null;
  startDate: string | null;
  endDate: string | null;
  method: string | null;
  initialValue: number;
  depreciated: number;
  bookValue: number;
  lastRun: string | null;
  disposed: boolean;
  sold?: boolean;
}

export interface AssetRunRow {
  from: string | null;
  to: string | null;
  productId: string | null;
  product: string | null;
  amount: number;
  bookValue: number;
  documentId: string | null;
  document: string | null;
  posted: boolean;
}

export interface DepreciationRun {
  date: string;
  assets: number;
  amount: number;
  documentId: string | null;
  document: string | null;
  posted: boolean;
  latest: boolean;
}

/** The operations API (C3 phase 7): production, stock conversion, depreciation, currencies and files. */
@Injectable({ providedIn: 'root' })
export class OperationsApi {
  private readonly http = inject(HttpClient);

  calculate(product: string, unit: string, quantity: number, costCenter: string | null): Promise<ProductionLine[]> {
    let params = new HttpParams().set('product', product).set('unit', unit).set('quantity', String(quantity));
    if (costCenter) {
      params = params.set('costCenter', costCenter);
    }
    return firstValueFrom(this.http.get<ProductionLine[]>('/api/v1/production/calculate', { params }));
  }

  produce(request: { costCenterId: string; productId: string; unitId: string; quantity: number; date: string | null; lines: ProductionLine[] | null;
    toCostCenterId?: string | null; typeId?: string | null }): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>('/api/v1/production', request));
  }

  /** The company's document types of a kind, to choose one when there are several. */
  stockTypes(kind: 'production' | 'disassembly' | 'unit-split'): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>(kind === 'unit-split' ? '/api/v1/stock/convert/types' : '/api/v1/production/types', { params: kind === 'unit-split' ? {} : { kind } }));
  }

  /** What a place holds of a product in a unit. */
  held(costCenter: string, product: string, unit: string): Promise<number> {
    return firstValueFrom(this.http.get<number>('/api/v1/production/held', { params: { costCenter, product, unit } }));
  }

  /** The document types an order to produce can be (never posted, top level). */
  calculateDisassembly(product: string, unit: string, quantity: number, costCenter: string | null): Promise<DisassemblyLine[]> {
    let params = new HttpParams().set('product', product).set('unit', unit).set('quantity', String(quantity));
    if (costCenter) {
      params = params.set('costCenter', costCenter);
    }
    return firstValueFrom(this.http.get<DisassemblyLine[]>('/api/v1/production/disassemble/calculate', { params }));
  }

  /** Takes the product apart: a posted disassembly document (not reversed). */
  disassemble(request: { costCenterId: string; productId: string; unitId: string; quantity: number; date: string | null; lines: DisassemblyLine[] | null; typeId?: string | null }): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>('/api/v1/production/disassemble', request));
  }

  orderTypes(): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>('/api/v1/production/order-types'));
  }

  ordersToProduce(type: string): Promise<OrderToProduce[]> {
    return firstValueFrom(this.http.get<OrderToProduce[]>('/api/v1/production/orders', { params: { type } }));
  }

  produceOrder(order: string, costCenter: string, date: string | null): Promise<DocumentData[]> {
    let params = new HttpParams().set('costCenter', costCenter);
    if (date) {
      params = params.set('date', date);
    }
    return firstValueFrom(this.http.post<DocumentData[]>(`/api/v1/production/orders/${order}`, null, { params }));
  }

  /** How many of the "to" unit one "from" unit makes (for the product, else in general); null when the units do not convert. */
  async factor(from: string, to: string, product: string | null): Promise<number | null> {
    let params = new HttpParams().set('from', from).set('to', to);
    if (product) {
      params = params.set('product', product);
    }
    try {
      return await firstValueFrom(this.http.get<number>('/api/v1/stock/factor', { params }));
    } catch {
      return null;
    }
  }

  convert(request: { costCenterId: string; productId: string; fromUnitId: string; toUnitId: string; quantity: number; date: string | null; typeId?: string | null }): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>('/api/v1/stock/convert', request));
  }

  /** What an asset is still worth in a book. */
  async remainingValue(product: string, unit: string, book: string): Promise<number> {
    const answer = await firstValueFrom(this.http.get<{ remaining: number }>('/api/v1/depreciation/remaining', { params: { product, unit, book } }));
    return answer.remaining;
  }

  /** The products of the fixed-asset types, as choices. */
  async assetProducts(): Promise<{ value: string; label: string; hint?: string }[]> {
    const rows = await firstValueFrom(this.http.get<{ id: string; code: string | null; name: string }[]>('/api/v1/depreciation/asset-products'));
    return rows.map(r => ({ value: r.id, label: r.name || r.code || r.id, hint: r.code ?? undefined }));
  }

  books(): Promise<DepreciationBook[]> {
    return firstValueFrom(this.http.get<DepreciationBook[]>('/api/v1/depreciation/books'));
  }

  setDisposed(assetValueId: string, disposed: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/depreciation/assets/${assetValueId}/disposed`, null, { params: { disposed } }));
  }

  registerAsset(asset: { productId: string; unitId: string; bookId: string; initialValue: number; startDate: string; months: number | null; defaultCostCenterId: string | null }): Promise<string[]> {
    return firstValueFrom(this.http.post<string[]>('/api/v1/depreciation/assets', asset));
  }

  serials(product: string | null, holder: string | null, search = ''): Promise<SerialRow[]> {
    const params: Record<string, string> = {};
    if (product) {
      params['product'] = product;
    }
    if (holder) {
      params['holder'] = holder;
    }
    if (search) {
      params['search'] = search;
    }
    return firstValueFrom(this.http.get<SerialRow[]>('/api/v1/serials', { params }));
  }

  serialMoves(id: string): Promise<SerialMove[]> {
    return firstValueFrom(this.http.get<SerialMove[]>(`/api/v1/serials/${id}/moves`));
  }

  lots(search: string, expiresBy: string | null): Promise<LotRow[]> {
    const params: Record<string, string> = {};
    if (search.trim()) {
      params['search'] = search.trim();
    }
    if (expiresBy) {
      params['expiresBy'] = expiresBy;
    }
    return firstValueFrom(this.http.get<LotRow[]>('/api/v1/lots', { params }));
  }

  lotStock(): Promise<LotStock[]> {
    return firstValueFrom(this.http.get<LotStock[]>('/api/v1/lots/stock'));
  }

  assignLot(body: { costCenterId: string; productId: string; unitId: string; lot: string; expiryDate: string | null; quantity: number }): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/lots/assign', body));
  }

  serialStock(): Promise<SerialStock[]> {
    return firstValueFrom(this.http.get<SerialStock[]>('/api/v1/serials/stock'));
  }

  assignSerials(assign: { costCenterId: string; productId: string; unitId: string; serials: { serial: string; partNumber?: string | null }[] }): Promise<SerialRow[]> {
    return firstValueFrom(this.http.post<SerialRow[]>('/api/v1/serials/assign', assign));
  }

  assets(book: string): Promise<AssetRow[]> {
    return firstValueFrom(this.http.get<AssetRow[]>(`/api/v1/depreciation/books/${book}/assets`));
  }

  assetHistory(id: string): Promise<AssetRunRow[]> {
    return firstValueFrom(this.http.get<AssetRunRow[]>(`/api/v1/depreciation/assets/${id}/history`));
  }

  depreciationRuns(book: string): Promise<DepreciationRun[]> {
    return firstValueFrom(this.http.get<DepreciationRun[]>(`/api/v1/depreciation/books/${book}/runs`));
  }

  depreciationRunLines(book: string, date: string): Promise<AssetRunRow[]> {
    return firstValueFrom(this.http.get<AssetRunRow[]>(`/api/v1/depreciation/books/${book}/runs/${date}`));
  }

  undoDepreciationRun(book: string, date: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/v1/depreciation/books/${book}/runs/${date}`));
  }

  calculateDepreciation(book: string, date: string): Promise<DepreciationLine[]> {
    return firstValueFrom(this.http.get<DepreciationLine[]>(`/api/v1/depreciation/books/${book}/calculate`, { params: { date } }));
  }

  runDepreciation(book: string, date: string): Promise<DepreciationLine[]> {
    return firstValueFrom(this.http.post<DepreciationLine[]>(`/api/v1/depreciation/books/${book}/runs`, null, { params: { date } }));
  }

  depreciationJournal(book: string, date: string, documentTypeId: string, categoryId: string): Promise<DocumentData> {
    return firstValueFrom(this.http.post<DocumentData>(`/api/v1/depreciation/books/${book}/journals`, { date, documentTypeId, categoryId }));
  }

  saveCurrency(currency: { id: string | null; code: string | null; name: string }): Promise<Currency> {
    return firstValueFrom(this.http.post<Currency>('/api/v1/currencies', currency));
  }

  switchOffCurrency(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/v1/currencies/${id}`));
  }

  currencies(): Promise<Currency[]> {
    return firstValueFrom(this.http.get<Currency[]>('/api/v1/currencies'));
  }

  rates(currency: string, from: string, to: string): Promise<ExchangeRate[]> {
    return firstValueFrom(this.http.get<ExchangeRate[]>('/api/v1/exchange-rates', { params: { currency, from, to } }));
  }

  setRate(currencyId: string, rateDate: string, rate: number): Promise<ExchangeRate> {
    return firstValueFrom(this.http.post<ExchangeRate>('/api/v1/exchange-rates', { currencyId, rateDate, rate }));
  }

  files(search: string, page: number, pageSize: number): Promise<Page<StoredFile>> {
    const params: Record<string, string> = { page: String(page), pageSize: String(pageSize) };
    if (search.trim()) {
      params['search'] = search.trim();
    }
    return firstValueFrom(this.http.get<Page<StoredFile>>('/api/v1/files', { params }));
  }

  upload(file: File): Promise<StoredFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<StoredFile>('/api/v1/files', form));
  }

  removeFile(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/v1/files/${id}`));
  }

  download(link: string): Promise<Blob> {
    return firstValueFrom(this.http.get(link, { responseType: 'blob' }));
  }
}

/** Saves a downloaded file under its name. */
export function save(blob: Blob, name: string): void {
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
}
