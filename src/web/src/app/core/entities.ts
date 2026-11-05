import { t } from './i18n';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Property, Translated, toSaveJson } from '../ui/property-form.logic';

/** The registers (C3 phase 3): the route's entity key → its API and its element table (as the rights name it). */
/** An opportunity's own fields. */
export interface OpportunityFields {
  partnerId?: string | null;
  stageId?: string | null;
  amount?: number | null;
  currencyId?: string | null;
  expectedClose?: string | null;
  ownerUserId?: string | null;
  lostReason?: string | null;
}

export interface Register {
  key: string;
  api: string;
  table: string;
  /** Transactions have categories and types only (their elements are documents' lines). */
  hasElements: boolean;
  /** Users also get a user name and a password. */
  isUsers?: boolean;
}

export const REGISTERS: Record<string, Register> = {
  partners: { key: 'partners', api: '/api/v1/partners', table: 'tbl_partner', hasElements: true },
  products: { key: 'products', api: '/api/v1/products', table: 'tbl_product', hasElements: true },
  'cost-centers': { key: 'cost-centers', api: '/api/v1/cost-centers', table: 'tbl_cost_center', hasElements: true },
  charts: { key: 'charts', api: '/api/v1/charts', table: 'tbl_chart', hasElements: true },
  units: { key: 'units', api: '/api/v1/units', table: 'tbl_unit', hasElements: true },
  opportunities: { key: 'opportunities', api: '/api/v1/opportunities', table: 'tbl_opportunity', hasElements: true },
  employees: { key: 'employees', api: '/api/v1/employees', table: 'tbl_employee', hasElements: true },
  users: { key: 'users', api: '/api/v1/users', table: 'tbl_user', hasElements: true, isUsers: true },
  transactions: { key: 'transactions', api: '/api/v1/transactions', table: 'tbl_transaction', hasElements: false },
};

/** The fields every row carries (RowDto). */
export interface Row {
  id: string | null;
  ownerId?: string | null;
  idSup: string | null;
  active: boolean;
  createdOn?: string | null;
  createdBy?: string | null;
}

export interface Category extends Row {
  isElement: boolean;
  code: string | null;
  name: Translated;
  description: Translated | null;
  sortOrder: number;
}

export interface ElementType extends Row {
  isElement: boolean;
  /** Products: each unit has a serial number. */
  tracksSerial?: boolean;
  tracksLot?: boolean;
  refusesExpired?: boolean;
  takesBankStatements?: boolean;
  isProductionPoint?: boolean;
  /** Products: its products are fixed assets. */
  fixedAsset?: boolean;
  /** Products: the VAT group its products' lines start with. */
  vatGroupId?: string | null;
  code: string | null;
  codeStart: number | null;
  codeEnd: number | null;
  codeCurrent: number | null;
  name: Translated;
  description: Translated | null;
  sortOrder: number;
  categoryIds: string[];
  properties: (Property & Row)[];
}

export interface Element extends Row {
  typeId: string;
  /** Products: the VAT group its lines start with, over its type's. */
  defaultVatGroupId?: string | null;
  paymentDays?: number | null;
  opportunity?: OpportunityFields | null;
  code: string | null;
  name: Translated;
  description: Translated | null;
  isComposite: boolean;
  categoryIds: string[];
  values: Record<string, unknown>;
  accessUserIds: string[];
  accessRoleIds: string[];
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface HistoryEntry {
  id: string;
  createdOn: string;
  createdBy: string | null;
  row: Record<string, unknown>;
}

export interface LookupOption {
  id: string;
  code: string | null;
  name: string;
}

export interface LookupScope {
  id: string;
  table: string;
  categoryId: string | null;
  typeId: string | null;
  name: string;
}

/** A unit of a product or a product type (ProductUnitDto): in its list's order; in use when stock, prices or a document point at it. */
export interface ProductUnit {
  unitId: string;
  code: string | null;
  name: string | null;
  inUse: boolean;
}

/** A product's own units (the first is the default) and its type's, offered when it has none of its own. */
export interface ProductUnits {
  units: ProductUnit[];
  typeUnits: ProductUnit[];
  canWrite: boolean;
}

export interface Right {
  table: string;
  categoryId: string;
  readAll: boolean;
  readOwn: boolean;
  write: boolean;
  forward: boolean;
}

const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

/** The entity API of one register (categories, types, elements), and the lookups and access calls the register screen uses. */
@Injectable({ providedIn: 'root' })
export class EntityApi {
  private readonly http = inject(HttpClient);

  categories(register: Register): Promise<Category[]> {
    return firstValueFrom(this.http.get<Category[]>(`${register.api}/categories`));
  }

  saveCategory(register: Register, category: Partial<Category>): Promise<Category> {
    return firstValueFrom(this.http.post<Category>(`${register.api}/categories`, category));
  }

  types(register: Register): Promise<ElementType[]> {
    return firstValueFrom(this.http.get<ElementType[]>(`${register.api}/types`));
  }

  saveType(register: Register, type: object): Promise<ElementType> {
    return firstValueFrom(this.http.post<ElementType>(`${register.api}/types`, type));
  }

  copyProperties(register: Register, typeId: string, fromTypeId: string): Promise<ElementType> {
    return firstValueFrom(this.http.post<ElementType>(`${register.api}/types/${typeId}/properties/copy`, { fromTypeId }));
  }

  elements(register: Register, category: string, type: string | null, search: string, page: number, pageSize: number): Promise<Page<Element>> {
    const params: Record<string, string> = { category, page: String(page), pageSize: String(pageSize) };
    if (type) {
      params['type'] = type;
    }
    if (search.trim()) {
      params['search'] = search.trim();
    }
    return firstValueFrom(this.http.get<Page<Element>>(register.api, { params }));
  }

  element(register: Register, id: string): Promise<Element> {
    return firstValueFrom(this.http.get<Element>(`${register.api}/${id}`));
  }

  /** Saved as text so property numbers keep their exact digits (toSaveJson). */
  saveElement(register: Register, element: Partial<Element>): Promise<Element> {
    return firstValueFrom(this.http.post<Element>(register.api, toSaveJson(element), { headers: JSON_HEADERS }));
  }

  switchOff(register: Register, id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${register.api}/${id}`));
  }

  history(register: Register, id: string): Promise<HistoryEntry[]> {
    return firstValueFrom(this.http.get<HistoryEntry[]>(`${register.api}/${id}/history`));
  }

  productUnits(productId: string): Promise<ProductUnits> {
    return firstValueFrom(this.http.get<ProductUnits>(`/api/v1/products/${productId}/units`));
  }

  saveProductUnits(productId: string, unitIds: string[]): Promise<ProductUnits> {
    return firstValueFrom(this.http.put<ProductUnits>(`/api/v1/products/${productId}/units`, unitIds));
  }

  typeUnits(typeId: string): Promise<ProductUnit[]> {
    return firstValueFrom(this.http.get<ProductUnit[]>(`/api/v1/products/types/${typeId}/units`));
  }

  saveTypeUnits(typeId: string, unitIds: string[]): Promise<ProductUnit[]> {
    return firstValueFrom(this.http.put<ProductUnit[]>(`/api/v1/products/types/${typeId}/units`, unitIds));
  }

  lookupScopes(): Promise<LookupScope[]> {
    return firstValueFrom(this.http.get<LookupScope[]>('/api/v1/lookups'));
  }

  lookup(scopeId: string, search: string, pageSize = 50): Promise<Page<LookupOption>> {
    const params: Record<string, string> = { pageSize: String(pageSize) };
    if (search.trim()) {
      params['search'] = search.trim();
    }
    return firstValueFrom(this.http.get<Page<LookupOption>>(`/api/v1/lookups/${scopeId}`, { params }));
  }

  /**
   * Every user the request sees, read from the users register by role (category): the packs give users no register-level lookup scope, so
   * the lookups cannot list them.
   */
  async users(): Promise<LookupOption[]> {
    const register = REGISTERS['users'];
    const roles = (await this.categories(register)).filter(c => c.isElement && c.id);
    const pages = await Promise.all(roles.map(r => this.elements(register, r.id!, null, '', 1, 500).catch(() => ({ items: [] as Element[] }))));
    const seen = new Map<string, LookupOption>();
    for (const user of pages.flatMap(p => p.items)) {
      seen.set(user.id!, { id: user.id!, code: user.code, name: Object.values(user.name)[0] ?? user.code ?? user.id! });
    }
    return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name));
  }

  /** A user's user name and e-mail (administrators). */
  signIn(userId: string): Promise<{ username: string | null; email: string | null }> {
    return firstValueFrom(this.http.get<{ username: string | null; email: string | null }>(`/api/v1/users/${userId}/sign-in`));
  }

  /** The rows of a register in no category (administrators): a user who lost every role. */
  withoutCategory(register: Register): Promise<Element[]> {
    return firstValueFrom(this.http.get<Element[]>(`${register.api}/without-category`));
  }

  async email(userId: string): Promise<string> {
    return (await firstValueFrom(this.http.get<{ email: string | null }>(`/api/v1/users/${userId}/email`))).email ?? '';
  }

  setEmail(userId: string, email: string): Promise<unknown> {
    return firstValueFrom(this.http.put(`/api/v1/users/${userId}/email`, { email }));
  }

  setUsername(userId: string, username: string): Promise<unknown> {
    return firstValueFrom(this.http.put(`/api/v1/users/${userId}/username`, { username }));
  }

  resetPassword(userId: string, password: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/v1/users/${userId}/password`, { new: password }));
  }
}

/** The signed-in user's own rights (GET /account/rights), loaded once per cost centre. */
@Injectable({ providedIn: 'root' })
export class RightsService {
  private readonly http = inject(HttpClient);
  readonly isAdministrator = signal(false);
  readonly rights = signal<Right[]>([]);
  private loaded: Promise<void> | null = null;

  load(): Promise<void> {
    this.loaded ??= firstValueFrom(this.http.get<{ isAdministrator: boolean; rights: Right[] }>('/api/v1/account/rights')).then(r => {
      this.isAdministrator.set(r.isAdministrator);
      this.rights.set(r.rights);
    });
    return this.loaded;
  }

  /** Reload after a change of cost centre or of rights. */
  reset(): void {
    this.loaded = null;
  }

  private of(table: string, categoryId: string): Right | undefined {
    return this.rights().find(r => r.table === table && r.categoryId === categoryId);
  }

  canRead(table: string, categoryId: string): boolean {
    const r = this.of(table, categoryId);
    return this.isAdministrator() || !!r && (r.readAll || r.readOwn || r.write || r.forward);
  }

  canWrite(table: string, categoryId: string): boolean {
    return this.isAdministrator() || !!this.of(table, categoryId)?.write;
  }

  /** Changing categories, types and properties: an administrator, or whoever may write some category of the entity. */
  canSetUp(table: string): boolean {
    return this.isAdministrator() || this.rights().some(r => r.table === table && r.write);
  }
}

/** The API's refusal as field → message (a 422 / 403 ValidationProblem), or one general message. */
export function errorsOf(error: unknown): { fields: Record<string, string>; message: string } {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as { title?: string; errors?: Record<string, string[]> } | null;
    if (problem?.errors) {
      const fields = Object.fromEntries(Object.entries(problem.errors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v.join(' ')]));
      return { fields, message: Object.values(fields).join(' ') };
    }
    if (error.status === 0) {
      return { fields: {}, message: t('The API did not answer. Nothing was saved.') };
    }
    if (error.status === 404) {
      return { fields: {}, message: t('This does not exist, or it is not open to you.') };
    }
    return { fields: {}, message: problem?.title ?? `The API refused (${error.status}).` };
  }
  return { fields: {}, message: error instanceof Error ? error.message : String(error) };
}

export interface TreeRow<T> {
  row: T;
  id: string;
  children: TreeRow<T>[];
}

/** Rows linked by idSup as a tree, each level in sort order; a row whose parent is not there sits at the top. */
export function buildTree<T extends Row & { sortOrder?: number }>(rows: readonly T[]): TreeRow<T>[] {
  const nodes = new Map(rows.filter(r => r.id).map(r => [r.id!, { row: r, id: r.id!, children: [] as TreeRow<T>[] }]));
  const top: TreeRow<T>[] = [];
  for (const node of nodes.values()) {
    const parent = node.row.idSup ? nodes.get(node.row.idSup) : undefined;
    (parent ? parent.children : top).push(node);
  }
  const sort = (list: TreeRow<T>[]) => {
    list.sort((a, b) => (a.row.sortOrder ?? 0) - (b.row.sortOrder ?? 0));
    list.forEach(n => sort(n.children));
  };
  sort(top);
  return top;
}

/** The ids of a row and everything under it: a row cannot move under itself. */
export function withDescendants<T extends Row>(rows: readonly T[], id: string): Set<string> {
  const ids = new Set([id]);
  for (let grew = true; grew; ) {
    grew = false;
    for (const r of rows) {
      if (r.id && r.idSup && ids.has(r.idSup) && !ids.has(r.id)) {
        ids.add(r.id);
        grew = true;
      }
    }
  }
  return ids;
}
