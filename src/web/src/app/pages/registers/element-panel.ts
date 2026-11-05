import { OperationsApi } from '../../core/operations';
import { PropertyFile } from '../../ui/property-form';
import { TranslatePipe } from '../../core/t.pipe';
import { PosApi } from '../../core/pos';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { language, t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import {
  Category, Element, ElementType, EntityApi, HistoryEntry, LookupOption, LookupScope, Register, RightsService, errorsOf,
} from '../../core/entities';
import { ConfigApi } from '../../core/config';
import { Button, Field, Input, Checkbox } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { formatDate, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { LookupSearch, PropertyForm } from '../../ui/property-form';
import { PropertyKind } from '../../ui/property-form.logic';
import { Select, SelectOption } from '../../ui/select';
import { Status } from '../../ui/status';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { TranslatedInput, languagesOf } from '../../ui/translated-input';
import { PosSetup } from './pos-setup';
import { OpportunityFieldsForm } from './opportunity-fields';
import { ProductUnits } from './product-units';

/** What an element looks like before it is saved: in the category the list shows, of the type chosen there. */
export function newElement(categoryId: string, typeId: string | null): Element {
  return {
    id: null, idSup: null, active: true, typeId: typeId ?? '', code: null, name: {}, description: null, isComposite: false, categoryIds: [categoryId],
    values: {}, accessUserIds: [], accessRoleIds: [],
  };
}

/** The types an element in these categories may have: element types linked to one of them, or every element type when none is linked. */
export function typesFor(types: readonly ElementType[], categoryIds: readonly string[]): ElementType[] {
  const usable = types.filter(t => t.isElement && t.active !== false);
  const linked = usable.filter(t => t.categoryIds.some(c => categoryIds.includes(c)));
  return linked.length ? linked : usable;
}

/** A user's Claude token as an administrator sees it: never the token itself. */
export interface UserTokenRow {
  id: string;
  name: string | null;
  prefix: string | null;
  createdOn: string;
  lastUsedOn: string | null;
}

/** A document linked to an opportunity, as its Documents tab lists it. */
export interface LinkedDocument {
  id: string;
  code: string | null;
  type: string | null;
  date: string | null;
  posted: boolean;
  categoryId: string | null;
}

/** One earlier version beside the next one: the fields that changed (Code, Name…), as the history list shows them. */
export interface Version {
  id: string;
  when: string;
  code: string;
  name: string;
  changed: string[];
}

// Not Active: every earlier version is kept as a switched-off copy, so it always differs.
const COMPARED: Record<string, string> = { Code: t('Code'), Name: t('Name'), Description: t('Description'), TypeId: t('Type'), IdSup: t('Parent') };

export function versionsOf(history: readonly HistoryEntry[], current: Element): Version[] {
  // The current state, as the history rows are written (ElementDto with PascalCase names).
  let newer: Record<string, unknown> = { Code: current.code, Name: current.name, Description: current.description, TypeId: current.typeId,
    Active: current.active, IdSup: current.idSup };
  return history.map(entry => {
    const row = entry.row;
    const changed = Object.keys(COMPARED).filter(k => JSON.stringify(row[k] ?? null) !== JSON.stringify(newer[k] ?? null)).map(k => COMPARED[k]);
    newer = row;
    return { id: entry.id, when: formatDate(entry.createdOn, true), code: String(row['Code'] ?? ''), name: textIn(row['Name'] as Record<string, string>), changed };
  });
}

/** The VAT groups as choices, by name, in the language of the screens. */
export function vatOptionsOf(rows: readonly Record<string, unknown>[]): SelectOption<string | null>[] {
  return rows.map(r => ({ value: r['id'] as string, label: textIn(r['name'] as Parameters<typeof textIn>[0]) || String(r['code'] ?? '') }));
}

/**
 * The open row of a register, beside the list: its details (code, name, type, categories, the type's properties), who may see it, its
 * history and, for users, the user name and password. A new one is saved in the category the list shows.
 */
@Component({
  selector: 'app-element-panel',
  imports: [TranslatePipe, RouterLink, OpportunityFieldsForm, Button, Checkbox, Field, Icon, Input, PosSetup, ProductUnits, PropertyForm, Select, Status, Tabs, TranslatedInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './element-panel.html',
  styleUrl: './element-panel.scss',
})
export class ElementPanel {
  private readonly api = inject(EntityApi);
  private readonly config = inject(ConfigApi);
  private readonly rights = inject(RightsService);
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);

  readonly register = input.required<Register>();
  protected readonly vatOptions = signal<SelectOption<string | null>[]>([{ value: null, label: '—' }]);
  /** The element's id, or null for a new one. */
  readonly elementId = input<string | null>(null);
  readonly categoryId = input.required<string>();
  readonly defaultTypeId = input<string | null>(null);
  readonly categories = input<Category[]>([]);
  readonly types = input<ElementType[]>([]);
  readonly scopes = input<LookupScope[]>([]);
  readonly closed = output<void>();
  readonly saved = output<Element>();

  protected readonly element = signal<Element | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly errors = signal<Record<string, string | undefined>>({});
  private readonly files = inject(OperationsApi);
  protected readonly message = signal<string | null>(null);
  protected readonly tab = signal<string | null>('details');
  protected readonly history = signal<HistoryEntry[] | null>(null);
  /** An opportunity's documents, read when its tab opens. */
  protected readonly linked = signal<LinkedDocument[] | null>(null);
  private readonly http = inject(HttpClient);
  protected readonly lookups = signal<Record<string, SelectOption[]>>({});
  /** The rows a row may sit under (the same register and category, never itself): for trees such as cost centres and units. */
  protected readonly parentOptions = signal<SelectOption[]>([]);
  protected readonly userOptions = signal<SelectOption[]>([]);
  protected readonly roleOptions = signal<SelectOption[]>([]);
  protected readonly username = signal('');
  protected readonly email = signal('');
  /** For users: the cost centres they may work at, and the cost centres there are. */
  protected readonly places = signal<string[]>([]);
  protected readonly placeOptions = signal<SelectOption[]>([]);
  protected readonly password = signal('');
  /** The cost-centre types that are kinds of point of sale: those with settings of their own. */
  /** The tab the panel opens next on: the configuration of a point of sale just made. */
  private nextTab = 'details';
  private readonly posKinds = signal<ReadonlySet<unknown>>(new Set());
  protected readonly isPointOfSale = computed(() => this.register().key === 'cost-centers' && this.posKinds().has(this.element()?.typeId));
  /** A place below a point of sale (a shop's till): it gets the point-of-sale tab for its own cash desk. */
  protected readonly isTill = signal(false);
  private readonly pos = inject(PosApi);
  private readonly form = viewChild(PropertyForm);

  protected readonly isNew = computed(() => !this.element()?.id);
  protected readonly canWrite = computed(() => (this.element()?.categoryIds ?? [this.categoryId()]).some(c => this.rights.canWrite(this.register().table, c)));
  protected readonly isAdministrator = this.rights.isAdministrator;
  protected readonly title = computed(() => {
    const element = this.element();
    return element?.id ? textIn(element.name) || element.code || '' : t('New');
  });
  protected readonly tabs = computed<Tab[]>(() => {
    if (this.isNew()) {
      return [{ id: 'details', label: t('Details') }];
    }
    const tabs: Tab[] = [{ id: 'details', label: t('Details') }, { id: 'access', label: t('Access') }, { id: 'history', label: t('History') }];
    if (this.register().key === 'products') {
      // A product's units (the founder, 2026-10-31): it can have more than one, the first the default.
      tabs.splice(1, 0, { id: 'units', label: t('Units') });
    }
    if (this.isPointOfSale() || this.isTill()) {
      tabs.splice(1, 0, { id: 'pos', label: t('Point of sale') });
    }
    if (this.register().key === 'opportunities') {
      // The documents linked to the opportunity (offers, orders, contracts…), those the user sees.
      tabs.splice(1, 0, { id: 'documents', label: t('Documents') });
    }
    if (this.register().isUsers && this.isAdministrator()) {
      tabs.push({ id: 'login', label: t('Sign-in') }, { id: 'places', label: t('Cost centres') });
    }
    return tabs;
  });
  protected readonly typeOptions = computed<SelectOption[]>(() =>
    typesFor(this.types(), this.element()?.categoryIds ?? []).map(t => ({ value: t.id!, label: textIn(t.name), hint: t.code ?? undefined })));
  protected readonly categoryOptions = computed<SelectOption[]>(() =>
    this.categories().filter(c => c.isElement && this.rights.canWrite(this.register().table, c.id!) || this.element()?.categoryIds.includes(c.id!))
      .map(c => ({ value: c.id!, label: textIn(c.name), hint: c.code ?? undefined })));
  protected readonly properties = computed(() => this.types().find(t => t.id === this.element()?.typeId)?.properties.filter(p => p.active !== false) ?? []);
  protected readonly languages = computed(() => {
    const element = this.element();
    const values = Object.values(element?.values ?? {}).filter((v): v is Record<string, string> => !!v && typeof v === 'object' && !Array.isArray(v));
    return [...new Set([...languagesOf(), ...values.flatMap(v => Object.keys(v).filter(k => k.length === 2))])];
  });
  protected day(value: string | null): string {
    return formatDate(value);
  }

  protected readonly versions = computed(() => {
    const element = this.element();
    const history = this.history();
    return element && history ? versionsOf(history, element) : [];
  });

  constructor() {
    effect(() => {
      const id = this.elementId();
      const register = this.register();
      untracked(() => this.open(register, id));
    });
  }

  private async loadVatGroups(): Promise<void> {
    this.vatOptions.set([{ value: null, label: '—' }, ...vatOptionsOf(await this.config.list('vat-groups').catch(() => []))]);
  }

  private async loadPosKinds(): Promise<void> {
    const rows = await this.config.list('point-of-sale-settings').catch(() => []);
    this.posKinds.set(new Set(rows.map(r => r['pointOfSaleTypeId']).filter(Boolean)));
  }

  private async open(register: Register, id: string | null): Promise<void> {
    if (register.key === 'cost-centers' && !this.posKinds().size) {
      void this.loadPosKinds();
    }
    if (register.key === 'products' && this.vatOptions().length === 1) {
      void this.loadVatGroups();
    }
    this.errors.set({});
    this.message.set(null);
    this.history.set(null);
    this.linked.set(null);
    this.tab.set(this.nextTab);
    this.nextTab = 'details';
    this.username.set('');
    this.email.set('');
    this.password.set('');
    if (!id) {
      const types = typesFor(this.types(), [this.categoryId()]);
      const type = types.find(t => t.id === this.defaultTypeId()) ?? types[0];
      this.element.set(newElement(this.categoryId(), type?.id ?? null));
      void this.loadLookups();
      void this.findParents('');
      return;
    }
    this.loading.set(true);
    try {
      this.element.set(await this.api.element(register, id));
      this.isTill.set(false);
      if (register.key === 'cost-centers' && !this.isPointOfSale()) {
        void this.pos.pointOfSaleOf(id).then(at => this.isTill.set(!!at && at !== id)).catch(() => undefined);
      }
      void this.loadLookups();
      void this.findParents('');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  /** The first choices of every lookup property of the type (the rest come as the person types). */
  private async loadLookups(): Promise<void> {
    const scopes = [...new Set(this.properties().filter(p => p.kind === PropertyKind.Lookup && p.elementsId).map(p => p.elementsId!))];
    for (const scope of scopes) {
      await this.search(scope, '');
    }
  }

  private async search(scope: string, query: string): Promise<void> {
    try {
      const page = await this.api.lookup(scope, query, 200);
      this.lookups.update(all => ({ ...all, [scope]: page.items.map(optionOf) }));
    } catch {
      this.lookups.update(all => ({ ...all, [scope]: [] }));
    }
  }

  protected onLookupSearch(event: LookupSearch): void {
    if (event.property.elementsId) {
      void this.search(event.property.elementsId, event.query);
    }
  }

  /** The rows to sit under, as the person types; the current parent stays a choice even when the search does not find it. */
  protected async findParents(query: string): Promise<void> {
    const [register, element] = [this.register(), this.element()];
    if (!element) {
      return;
    }
    try {
      const page = await this.api.elements(register, this.categoryId(), null, query, 1, 50);
      const options = page.items.filter(e => e.id !== element.id).map(e => ({ value: e.id!, label: textIn(e.name) || e.code || e.id!, hint: e.code ?? undefined }));
      if (element.idSup && !options.some(o => o.value === element.idSup)) {
        const parent = await this.api.element(register, element.idSup).catch(() => null);
        options.unshift({ value: element.idSup, label: parent ? textIn(parent.name) || parent.code || element.idSup : element.idSup, hint: parent?.code ?? undefined });
      }
      this.parentOptions.set(options);
    } catch {
      this.parentOptions.set([]);
    }
  }

  /** A photo chosen on the form is stored as a file at once; the property keeps its link, saved with the rest. */
  protected async onFile(chosen: PropertyFile): Promise<void> {
    try {
      const stored = await this.files.upload(chosen.file);
      const current = this.element();
      if (current) {
        this.update({ values: { ...current.values, [chosen.property.id]: stored.link } });
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected update(change: Partial<Element>): void {
    const element = this.element();
    if (element) {
      this.element.set({ ...element, ...change });
    }
  }

  protected changeType(typeId: string | null): void {
    this.update({ typeId: typeId ?? '' });
    void this.loadLookups();
  }

  protected async showTab(tab: string | null): Promise<void> {
    this.tab.set(tab);
    const element = this.element();
    if (!element?.id) {
      return;
    }
    if (tab === 'history' && this.history() === null) {
      try {
        this.history.set(await this.api.history(this.register(), element.id));
      } catch (error) {
        this.message.set(errorsOf(error).message);
      }
    }
    if (tab === 'documents' && this.linked() === null) {
      try {
        this.linked.set(await firstValueFrom(this.http.get<LinkedDocument[]>(`/api/v1/opportunities/${element.id}/documents`, { params: { language: language() } })));
      } catch (error) {
        this.message.set(errorsOf(error).message);
      }
    }
    if (tab === 'access' && !this.roleOptions().length) {
      await this.loadAccessChoices();
    }
    if (tab === 'places') {
      await this.loadPlaces(element.id);
    }
    if (tab === 'login') {
      await this.loadEmail(element.id);
      await this.loadTokens(element.id);
    }
  }

  private async loadAccessChoices(): Promise<void> {
    try {
      const roles = await this.api.categories({ key: 'users', api: '/api/v1/users', table: 'tbl_user', hasElements: true });
      this.roleOptions.set(roles.filter(r => r.isElement).map(r => ({ value: r.id!, label: textIn(r.name) })));
      this.userOptions.set((await this.api.users()).map(optionOf));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async save(): Promise<void> {
    const element = this.element();
    const form = this.form();
    if (!element) {
      return;
    }
    this.message.set(null);
    if (form && !form.validate()) {
      this.message.set(t('Some fields need attention.'));
      return;
    }
    this.saving.set(true);
    const isNew = !element.id;
    try {
      const saved = await this.api.saveElement(this.register(), { ...element, values: form ? form.toSave() : element.values });
      this.errors.set({});
      this.element.set(saved);
      this.history.set(null);
      this.toasts.good(t('{0} saved.', textIn(saved.name) || saved.code));
      // A new point of sale opens on its configuration (the founder, 2026-10-31).
      if (isNew && this.isPointOfSale()) {
        this.nextTab = 'pos';
      }
      this.saved.emit(saved);
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(fields);
      this.message.set(message);
    } finally {
      this.saving.set(false);
    }
  }

  protected async switchOff(): Promise<void> {
    const element = this.element();
    if (!element?.id) {
      return;
    }
    const name = textIn(element.name) || element.code || '';
    const yes = await this.dialogs.confirm({
      title: `Switch off ${name}?`,
      message: t('It leaves the lists and can no longer be chosen. Nothing is deleted: its history stays.'),
      confirmLabel: t('Switch off'),
      danger: true,
    });
    if (!yes) {
      return;
    }
    try {
      await this.api.switchOff(this.register(), element.id);
      this.toasts.good(t('{0} switched off.', name));
      this.saved.emit(element);
      this.closed.emit();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async setUsername(): Promise<void> {
    const id = this.element()?.id;
    if (!id) {
      return;
    }
    try {
      await this.api.setUsername(id, this.username());
      this.errors.update(e => ({ ...e, username: '' }));
      this.toasts.good(t('The user name is now {0}.', this.username().trim().toLowerCase()));
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.update(e => ({ ...e, username: fields['username'] ?? message }));
    }
  }

  /** A user's personal Claude tokens, for an administrator to see and switch off (each user makes their own). */
  protected readonly userTokens = signal<UserTokenRow[]>([]);
  protected readonly tokensNote = t('Tokens this user made for Claude. Switch one off if it may be lost; the user makes new ones in their account menu.');

  private async loadTokens(userId: string): Promise<void> {
    this.userTokens.set(await firstValueFrom(this.http.get<UserTokenRow[]>(`/api/v1/users/${userId}/tokens`)).catch(() => []));
  }

  protected async revokeToken(token: { id: string; name: string | null }): Promise<void> {
    const userId = this.element()?.id;
    if (!userId) {
      return;
    }
    try {
      await firstValueFrom(this.http.delete(`/api/v1/users/${userId}/tokens/${token.id}`));
      this.toasts.good(t('{0} was switched off.', token.name ?? ''));
      await this.loadTokens(userId);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async loadEmail(userId: string): Promise<void> {
    try {
      const signIn = await this.api.signIn(userId);
      this.email.set(signIn.email ?? '');
      this.username.set(signIn.username ?? '');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async setEmail(): Promise<void> {
    const id = this.element()?.id;
    if (!id) {
      return;
    }
    try {
      await this.api.setEmail(id, this.email());
      this.errors.update(e => ({ ...e, email: '' }));
      this.toasts.good(this.email().trim() ? `The e-mail address is now ${this.email().trim()}.` : 'The e-mail address is removed.');
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.update(e => ({ ...e, email: fields['email'] ?? message }));
    }
  }

  private async loadPlaces(userId: string): Promise<void> {
    try {
      const scope = this.scopes().find(s => s.table === 'tbl_cost_center' && !s.categoryId && !s.typeId);
      const [places, options] = await Promise.all([this.config.costCenters(userId),
        scope ? this.api.lookup(scope.id, '', 500).then(p => p.items) : Promise.resolve([])]);
      this.places.set(places);
      this.placeOptions.set(options.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async savePlaces(): Promise<void> {
    const id = this.element()?.id;
    if (!id) {
      return;
    }
    try {
      this.places.set(await this.config.saveCostCenters(id, this.places()));
      this.toasts.good(t('Cost centres saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async setPassword(): Promise<void> {
    const id = this.element()?.id;
    if (!id) {
      return;
    }
    try {
      await this.api.resetPassword(id, this.password());
      this.password.set('');
      this.errors.update(e => ({ ...e, new: '' }));
      this.toasts.good(t('The password was set.'));
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.update(e => ({ ...e, new: fields['new'] ?? message }));
    }
  }
}

function optionOf(option: LookupOption): SelectOption {
  return { value: option.id, label: option.name || option.code || option.id, hint: option.code ?? undefined };
}
