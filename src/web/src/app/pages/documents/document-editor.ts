import { OperationsApi } from '../../core/operations';
import { PropertyFile } from '../../ui/property-form';
import { registerOptions } from '../operations/register-options';
import { TranslatePipe } from '../../core/t.pipe';
import { ContractBillingPlan } from './contract-billing';
import { t } from '../../core/i18n';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { balanceLcu, blankDocument, dayOf, ledgerOf, localToday, DocumentApi, DocumentData, DocumentFlow, DocumentForm, MadeDocument, PostingRow, Side } from '../../core/documents';
import { ElementType, EntityApi, HistoryEntry, errorsOf } from '../../core/entities';
import { SessionService } from '../../core/session';
import { Button, Field, Input } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { LookupSearch, PropertyForm } from '../../ui/property-form';
import { Property, PropertyKind } from '../../ui/property-form.logic';
import { Select, SelectOption } from '../../ui/select';
import { Status } from '../../ui/status';
import { Column, Table } from '../../ui/table';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { EmailDialog, EmailResult, RepeatDialog, RepeatResult, ReverseData, ReverseDialog, ReverseResult } from './document-dialogs';
import { LineDraft, LineGrid, draftOf, totalsOf } from './line-grid';
import { SheetGrid } from './sheet-grid';

/** The document as the API takes it: its lines without the screen's own fields. */
export function documentToSave(document: DocumentData, lines: readonly LineDraft[]): Partial<DocumentData> {
  return { ...document, transactions: lines.map(({ key: _key, units: _units, ...line }) => line) };
}

/**
 * One document: its header (number, dates, from / to with their live balance, their number, payment method, currency), its
 * lines, totals and actions. A draft is held while it is open, so no one else changes it; a posted document is read only, corrected by a
 * reversal.
 */
@Component({
  selector: 'app-document-editor',
  imports: [TranslatePipe, ContractBillingPlan, Button, CdkMenu, CdkMenuItem, CdkMenuTrigger, Field, Icon, Input, LineGrid, PropertyForm, Select, SheetGrid, Status, Table, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-editor.html',
  styleUrl: './document-editor.scss',
  host: { '[class.read-only]': 'readOnly()' },
})
export class DocumentEditor {
  private readonly api = inject(DocumentApi);
  private readonly entities = inject(EntityApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly dialogs = inject(Dialogs);
  private readonly toasts = inject(Toasts);

  readonly category = input.required<string>();
  /** A saved document's id (documents/:category/:id). */
  readonly id = input<string | null>(null);
  /** A new document's type (documents/:category/new/:type). */
  readonly type = input<string | null>(null);
  /** What a new document starts with when a bank statement line opens it (query parameters). */
  readonly fromId = input<string | null>(null);
  readonly fromName = input<string | null>(null);
  readonly toId = input<string | null>(null);
  readonly toName = input<string | null>(null);
  readonly bankDate = input<string | null>(null);
  readonly bankLine = input<string | null>(null);
  /** The bank statement line this new document is written for: it is matched to it once posted. */
  readonly bankLineId = input<string | null>(null);

  protected readonly form = signal<DocumentForm | null>(null);
  protected readonly document = signal<DocumentData | null>(null);
  protected readonly lines = signal<LineDraft[]>([]);
  protected readonly errors = signal<Record<string, string | undefined>>({});
  private readonly files = inject(OperationsApi);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dirty = signal(false);
  protected readonly heldByOther = signal(false);
  private readonly holding = signal<string | null>(null);
  protected readonly neighbours = signal<{ previous: string | null; next: string | null }>({ previous: null, next: null });
  protected readonly tab = signal<string | null>('lines');
  protected readonly preview = signal<PostingRow[] | null>(null);
  protected readonly history = signal<HistoryEntry[] | null>(null);
  protected readonly sideOptions = signal<{ from: SelectOption[]; to: SelectOption[] }>({ from: [], to: [] });
  protected readonly balances = signal<{ from: number | null; to: number | null }>({ from: null, to: null });
  protected readonly currencyId = signal<string | null>(null);
  /** The document's rate: the day's unless someone typed one; only a foreign currency has one. */
  protected readonly rate = signal('');
  protected readonly noRate = signal(false);
  protected readonly foreign = computed(() => !!this.currencyId() && this.form()?.currencies.find(c => c.id === this.currencyId())?.isBase === false);
  private rateTyped = false;
  private readonly grid = viewChild(LineGrid);

  protected readonly readOnly = computed(() => {
    const document = this.document();
    const form = this.form();
    return !document || !form || document.posted || form.isReversal || this.heldByOther();
  });
  protected readonly isNew = computed(() => !this.document()?.id);
  protected readonly title = computed(() => {
    const document = this.document();
    const form = this.form();
    return document?.code ? `${form?.name ?? ''} ${document.code}` : `New ${form?.name ?? 'document'}`;
  });
  protected readonly state = computed(() => {
    const document = this.document();
    // A reversal carries the number of the document it reverses (reversedDocumentCode).
    return !document ? null : document.reversedDocumentCode ? 'reversal' : document.posted ? 'posted' : document.id ? 'draft' : 'new';
  });
  protected readonly partnerId = computed(() => {
    const form = this.form();
    const document = this.document();
    return form?.to?.table === 'tbl_partner' ? (document?.toId ?? null) : form?.from?.table === 'tbl_partner' ? (document?.fromId ?? null) : null;
  });
  protected readonly totals = computed(() => {
    const rates = new Map((this.form()?.vatGroups ?? []).map(v => [v.id, v.rate]));
    return totalsOf(this.lines(), id => (id && rates.get(id)) || 0);
  });
  protected readonly paymentOptions = computed<SelectOption<string | null>[]>(() =>
    [{ value: null, label: '—' }, ...(this.form()?.paymentMethods ?? []).filter(m => !this.allowedMethods() || this.allowedMethods()!.includes(m.id) || m.id === this.document()?.paymentMethodId)
      .map(m => ({ value: m.id as string | null, label: m.name, hint: m.code ?? undefined }))]);
  /** The methods the cost centre the money goes into offers (the To side when it is a cost centre, else the From side); null: every method. */
  private readonly allowedMethods = signal<string[] | null>(null);
  private readonly moneyPlace = computed(() => {
    const [form, document] = [this.form(), this.document()];
    return form?.to?.table === 'tbl_cost_center' && document?.toId ? document.toId : form?.from?.table === 'tbl_cost_center' && document?.fromId ? document.fromId : null;
  });
  private readonly loadAllowedMethods = effect(() => {
    const place = this.moneyPlace();
    untracked(() => {
      this.allowedMethods.set(null);
      if (place && this.form()?.paymentMethods?.length) {
        void this.api.methodsAt(place).then(ids => this.allowedMethods.set(ids)).catch(() => undefined);
      }
    });
  });
  protected readonly currencyOptions = computed<SelectOption[]>(() =>
    (this.form()?.currencies ?? []).map(c => ({ value: c.id, label: c.code ?? c.name, hint: c.isBase ? 'own currency' : undefined })));
  /** The document type's own properties (contract dates, carrier and so on). */
  protected readonly properties = signal<Property[]>([]);
  protected readonly lookups = signal<Record<string, SelectOption[]>>({});
  private readonly propertyForm = viewChild(PropertyForm);
  /** A posted document that is not itself a reversal is reversed; the rest of reversal is unchanged. */
  protected readonly canReverse = computed(() => !!this.document()?.posted && !this.form()?.isReversal && this.form()?.hasReversal !== false);
  /** What this document may make, and what was made from it. */
  protected readonly flows = signal<DocumentFlow[]>([]);
  protected readonly opportunities = signal<SelectOption[]>([]);
  protected readonly made = signal<MadeDocument[]>([]);
  /** The journal types under this document's type: it is a sheet, its rows journals, when it has lines of none of its own. */
  protected readonly journalTypes = signal<ElementType[]>([]);
  /** What posting does on a type that books nothing (offers, contracts, orders). */
  protected readonly bookNothing = t('Posting books nothing: it only makes the document final.');
  /** A contract's billing plan, saved with the contract. */
  private readonly billingPlan = viewChild(ContractBillingPlan);
  protected readonly journals = signal<DocumentData[]>([]);
  protected readonly rowCount = signal(0);
  protected readonly isSheet = computed(() => this.journalTypes().length > 0 && !this.form()?.lineKinds.length);
  private readonly sheetGrid = viewChild(SheetGrid);
  protected readonly tabs = computed<Tab[]>(() => {
    const tabs: Tab[] = [{ id: 'lines', label: t('Lines'), count: this.isSheet() ? this.rowCount() : this.lines().length }];
    if (!this.isNew()) {
      tabs.push({ id: 'posting', label: t('Posting') }, { id: 'history', label: t('History') });
      if (this.document()?.createdFromIds.length || this.document()?.reversedDocumentId || this.made().length) {
        tabs.push({ id: 'links', label: t('Linked documents') });
      }
    }
    return tabs;
  });
  protected readonly previewColumns: Column<PostingRow & { id: string }>[] = [
    { key: 'line', label: t('Line'), width: '70px' },
    { key: 'account', label: t('Account'), width: '110px' },
    { key: 'accountName', label: t('Name') },
    { key: 'costCenter', label: t('Cost centre'), width: '180px' },
    { key: 'debit', label: t('Debit'), format: 'number', width: '130px' },
    { key: 'credit', label: t('Credit'), format: 'number', width: '130px' },
  ];
  protected readonly previewRows = computed(() => (this.preview() ?? []).map((r, i) => ({ ...r, id: String(i) })));
  protected readonly previewFooter = computed(() => {
    const rows = this.preview() ?? [];
    return { accountName: t('Total'), debit: formatNumber(rows.reduce((s, r) => s + r.debit, 0)), credit: formatNumber(rows.reduce((s, r) => s + r.credit, 0)) };
  });

  constructor() {
    void this.entities.lookupScopes().then(scopes => registerOptions(this.entities, scopes, 'tbl_opportunity')).then(options => this.opportunities.set(options)).catch(() => undefined);
    effect(() => {
      const id = this.id();
      const type = this.type();
      untracked(() => void this.load(id, type));
    });
    inject(DestroyRef).onDestroy(() => this.release());
  }

  private async load(id: string | null, type: string | null): Promise<void> {
    this.release();
    this.message.set(null);
    this.errors.set({});
    this.preview.set(null);
    this.history.set(null);
    this.tab.set('lines');
    this.heldByOther.set(false);
    this.dirty.set(false);
    try {
      if (id) {
        const document = await this.api.get(id);
        const form = await this.api.form(document.typeId);
        this.form.set(form);
        this.setDocument(document);
        await this.loadSheet(document.typeId, id, document.categoryIds[0] ?? this.category());
        await this.loadProperties(document.typeId);
        await this.loadFlows(id);
        this.neighbours.set(await this.api.neighbours(id));
        await this.holdIfDraft(document);
      } else if (type) {
        const form = await this.api.form(type);
        this.form.set(form);
        const base = form.currencies.find(c => c.isBase)?.id ?? null;
        this.currencyId.set(base);
        const blank = blankDocument(type, this.category());
        const date = this.bankDate();
        this.document.set({ ...blank, fromId: this.fromId(), fromName: this.fromName(), toId: this.toId(), toName: this.toName(), postingDate: date ?? blank.postingDate, documentDate: date ?? blank.documentDate });
        this.lines.set([]);
        await this.loadSheet(type, null, this.category());
        await this.loadProperties(type);
        this.flows.set([]);
        this.made.set([]);
        this.neighbours.set({ previous: null, next: null });
      }
      void this.loadSides();
      void this.grid()?.preload();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async loadProperties(type: string): Promise<void> {
    const properties = ((await this.api.types()).find(t => t.id === type)?.properties ?? []).filter(p => p.active !== false) as Property[];
    this.properties.set(properties);
    const scopes = [...new Set(properties.filter(p => p.kind === PropertyKind.Lookup && p.elementsId).map(p => p.elementsId!))];
    await Promise.all(scopes.map(scope => this.findLookup(scope, '')));
  }

  private async findLookup(scope: string, query: string): Promise<void> {
    const page = await this.entities.lookup(scope, query, 50).catch(() => ({ items: [] }));
    this.lookups.update(all => ({ ...all, [scope]: page.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })) }));
  }

  protected onLookupSearch(search: LookupSearch): void {
    if (search.property.elementsId) {
      void this.findLookup(search.property.elementsId, search.query);
    }
  }

  private async loadFlows(id: string): Promise<void> {
    const [flows, made] = await Promise.all([this.api.flows(id), this.api.made(id)]);
    this.flows.set(flows);
    this.made.set(made);
  }

  protected openMade(document: MadeDocument): void {
    void this.router.navigate(['/documents', document.categoryId ?? this.category(), document.id]);
  }

  /** Makes the next document from this one and opens it, to check and post. */
  protected async makeDocument(flow: DocumentFlow): Promise<void> {
    const document = this.document();
    if (flow.refused) {
      this.message.set(`${flow.name}: ${flow.refused}`);
      return;
    }
    if (!document?.id || ((this.dirty() || this.isNew()) && !(await this.save()))) {
      return;
    }
    await this.act(async () => {
      const made = await this.api.make(document.id!, flow.typeId);
      this.toasts.good(t('{0} {1} made from {2}.', flow.name, made.code ?? '', document.code));
      await this.router.navigate(['/documents', flow.categoryId ?? this.category(), made.id]);
    });
  }

  /** The document's own category, not the list's: a reversal sheet opened from the journals is filed with its journals in Reversals. */
  private async loadSheet(type: string, id: string | null, category: string): Promise<void> {
    const types = (await this.api.allowedTypes(category)).filter(t => t.idSup === type);
    this.journalTypes.set(types);
    if (!types.length && (await this.api.types().catch(() => [] as ElementType[])).some(t => t.idSup === type)) {
      this.message.set(t('This sheet has row types, but none is given to you: ask an administrator for them.'));
    }
    const journals = types.length && id ? await this.api.journals(id) : [];
    this.journals.set(journals);
    this.rowCount.set(journals.length);
  }

  private setDocument(document: DocumentData): void {
    this.document.set(document);
    this.lines.set(document.transactions.map(draftOf));
    this.currencyId.set(document.transactions[0]?.currencyId ?? this.form()?.currencies.find(c => c.isBase)?.id ?? null);
    void this.loadBalances();
    void this.showRate(document.transactions[0]?.exchangeRate ?? null);
  }

  /** A draft is held while open: if someone else holds it, it opens read only. */
  private async holdIfDraft(document: DocumentData): Promise<void> {
    if (document.posted || !document.id || this.form()?.isReversal) {
      return;
    }
    const { holder } = await this.api.holder(document.id);
    if (holder && holder !== this.session.me()?.id) {
      this.heldByOther.set(true);
      return;
    }
    await this.api.hold(document.id);
    this.holding.set(document.id);
    // Renewed every minute while the document is open, so a hold timeout counts from when the editor was last open.
    const id = document.id;
    this.renewal = setInterval(() => void this.api.hold(id).catch(() => undefined), 60_000);
  }

  private renewal: ReturnType<typeof setInterval> | undefined;

  private release(): void {
    clearInterval(this.renewal);
    this.renewal = undefined;
    const held = this.holding();
    if (held) {
      this.holding.set(null);
      void this.api.release(held).catch(() => undefined);
    }
  }

  private async loadSides(): Promise<void> {
    const form = this.form();
    const document = this.document();
    if (!form || !document) {
      return;
    }
    const load = async (side: Side | null, id: string | null, name: string | null, query = ''): Promise<SelectOption[]> => {
      if (!side) {
        return [];
      }
      const options = (await this.entities.lookup(side.scopeId, query, 50).catch(() => ({ items: [] }))).items
        .map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined }));
      return id && !options.some(o => o.value === id) ? [{ value: id, label: name ?? '' }, ...options] : options;
    };
    const [from, to] = await Promise.all([load(form.from, document.fromId, document.fromName), load(form.to, document.toId, document.toName)]);
    this.sideOptions.set({ from, to });
  }

  protected async searchSide(which: 'from' | 'to', query: string): Promise<void> {
    const side = this.form()?.[which];
    if (!side) {
      return;
    }
    const page = await this.entities.lookup(side.scopeId, query, 50).catch(() => ({ items: [] }));
    const options = page.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined }));
    this.sideOptions.update(all => ({ ...all, [which]: options }));
  }

  private async loadBalances(): Promise<void> {
    const form = this.form();
    const document = this.document();
    if (!form || !document) {
      return;
    }
    const of = async (side: Side | null, id: string | null) => {
      if (!side || !id) {
        return null;
      }
      const balances = await this.api.balance(side.table, id).catch(() => null);
      return balances ? balanceLcu(balances) : null;
    };
    const [from, to] = await Promise.all([of(form.from, document.fromId), of(form.to, document.toId)]);
    this.balances.set({ from, to });
  }

  /** A photo chosen on the form is stored as a file at once; the property keeps its link, saved with the rest. */
  protected async onFile(chosen: PropertyFile): Promise<void> {
    try {
      const stored = await this.files.upload(chosen.file);
      const current = this.document();
      if (current) {
        this.update({ values: { ...current.values, [chosen.property.id]: stored.link } });
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected update(change: Partial<DocumentData>): void {
    const document = this.document();
    if (document) {
      this.document.set({ ...document, ...change });
      this.dirty.set(true);
      if ('postingDate' in change || 'documentDate' in change) {
        void this.loadRate();
      }
    }
  }

  /** A saved document's rate: it counts as typed when it is not the day's, so a new date leaves it alone. */
  private async showRate(saved: number | null): Promise<void> {
    this.rateTyped = false;
    if (saved === null || !this.foreign()) {
      return this.loadRate();
    }
    this.rate.set(String(saved));
    this.noRate.set(false);
    const document = this.document();
    const day = document ? await this.api.dayRate(this.currencyId()!, dayOf(document, localToday())).catch(() => null) : null;
    this.rateTyped = day !== saved;
  }

  /** The day's rate, unless someone typed one. */
  private async loadRate(): Promise<void> {
    const [currency, document] = [this.currencyId(), this.document()];
    if (!this.foreign() || !currency || !document) {
      this.rate.set('');
      this.noRate.set(false);
      this.rateTyped = false;
      return;
    }
    if (!this.rateTyped) {
      const day = await this.api.dayRate(currency, dayOf(document, localToday())).catch(() => null);
      this.rate.set(day === null ? '' : String(day));
      this.noRate.set(day === null);
    }
  }

  protected typeRate(value: string): void {
    this.rate.set(value);
    this.rateTyped = !!value.trim();
    this.dirty.set(true);
    if (!this.rateTyped) {
      void this.loadRate();
    }
  }

  protected pickSide(which: 'from' | 'to', id: string | null): void {
    const name = this.sideOptions()[which].find(o => o.value === id)?.label ?? null;
    this.update(which === 'from' ? { fromId: id, fromName: name } : { toId: id, toName: name });
    void this.loadBalances();
  }

  protected setLines(lines: LineDraft[]): void {
    this.lines.set(lines);
    this.dirty.set(true);
  }

  protected setCurrency(id: string | null): void {
    this.currencyId.set(id);
    this.setLines(this.lines().map(l => ({ ...l, currencyId: id })));
    this.rateTyped = false;
    void this.loadRate();
  }

  /** The ledger behind a side's balance: the work so far saved first, as on leaving for a made document. */
  protected async openLedger(which: 'from' | 'to'): Promise<void> {
    const side = this.form()?.[which];
    const id = which === 'from' ? this.document()?.fromId : this.document()?.toId;
    const link = side?.table && id ? ledgerOf(side.table, id, localToday()) : null;
    if (!link || ((this.dirty() || this.isNew()) && !(await this.save()))) {
      return;
    }
    await this.router.navigate(['/reports', link.report], { queryParams: link.params });
  }

  protected balance(value: number | null): string {
    return value === null ? '' : formatNumber(value);
  }

  protected money(value: number): string {
    return formatNumber(value);
  }

  protected date(value: string | null | undefined): string {
    return formatDate(value ?? null, true);
  }

  /** Saves the document; true when it was saved. */
  protected async save(): Promise<boolean> {
    const document = this.document();
    if (!document || this.readOnly()) {
      return false;
    }
    const properties = this.propertyForm();
    if (properties && !properties.validate()) {
      this.message.set(t('Some fields need attention.'));
      return false;
    }
    const withValues = { ...document, values: properties ? properties.toSave() : document.values };
    this.busy.set(true);
    this.message.set(null);
    try {
      let saved: DocumentData;
      if (this.isSheet()) {
        const sheet = await this.api.saveSheet(documentToSave(withValues, []), this.sheetGrid()?.toSave() ?? this.journals());
        saved = sheet.sheet;
        this.journals.set(sheet.journals);
      } else {
        const rate = Number(this.rate().replace(',', '.'));
        saved = await this.api.save(documentToSave(withValues, this.lines().map(l => ({ ...l, exchangeRate: this.foreign() && rate > 0 ? rate : null }))));
      }
      this.errors.set({});
      await this.billingPlan()?.saveIfChanged();
      this.setDocument(saved);
      this.dirty.set(false);
      this.preview.set(null);
      this.history.set(null);
      this.toasts.good(t('{0} {1} saved.', this.form()?.name ?? 'Document', saved.code));
      if (!document.id && saved.id) {
        await this.router.navigate(['/documents', this.category(), saved.id], { replaceUrl: true });
      }
      return true;
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(fields);
      this.message.set(message);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async post(): Promise<void> {
    if ((this.dirty() || this.isNew()) && !(await this.save())) {
      return;
    }
    const document = this.document();
    if (!document?.id) {
      return;
    }
    const yes = await this.dialogs.confirm({
      title: `Post ${document.code}?`,
      message: t('A posted document is final: its entries are written to the books. Mistakes are corrected by reversing it.'),
      confirmLabel: t('Post'),
    });
    if (!yes) {
      return;
    }
    await this.act(async () => {
      this.release();
      this.setDocument(await this.api.post(document.id!));
      this.preview.set(null);
      this.toasts.good(t('{0} posted.', document.code));
      const line = this.bankLineId();
      if (line) {
        await this.api.matchBankLine(line, document.id!).then(() => this.toasts.good(t('Matched to the bank statement line.')), () => this.toasts.warning(t('Not matched to the bank statement line: match it there.')));
      }
    });
  }

  protected async reverse(): Promise<void> {
    const document = this.document();
    const form = this.form();
    if (!document?.id || !form) {
      return;
    }
    // A type reversed in part shows what earlier reversals left: the quantity and the serial numbers not yet back; lines with nothing left go.
    let lines: ReverseData['lines'] = document.transactions;
    if (form.allowsPartialReversal && document.id) {
      const left = new Map((await this.api.reversible(document.id).catch(() => [])).map(l => [l.id, l]));
      lines = document.transactions.map(l => {
        const open = l.id ? left.get(l.id) : undefined;
        return open ? { ...l, original: l.quantity, quantity: open.left, serials: (l.serials ?? []).filter(s => open.serials.includes(s.serial)) } : l;
      }).filter(l => (l.quantity ?? 0) > 0);
    }
    const data: ReverseData = { code: document.code ?? '', lines, partial: form.allowsPartialReversal };
    const result = await firstValueFrom(this.dialogs.open<ReverseResult, ReverseData, ReverseDialog>(ReverseDialog, data).closed);
    if (!result) {
      return;
    }
    await this.act(async () => {
      const reversal = await this.api.reverse(document.id!, result.postingDate, result.quantities, result.serials);
      this.toasts.good(t('{0} reversed by {1}.', document.code, reversal.code));
      await this.router.navigate(['/documents', this.category(), reversal.id]);
    });
  }

  protected async print(): Promise<void> {
    const id = this.document()?.id;
    if (!id) {
      return;
    }
    await this.act(async () => {
      const pdf = await this.api.pdf(id);
      window.open(URL.createObjectURL(pdf), '_blank', 'noopener');
    });
  }

  protected async email(): Promise<void> {
    const document = this.document();
    if (!document?.id) {
      return;
    }
    const result = await firstValueFrom(this.dialogs.open<EmailResult, string, EmailDialog>(EmailDialog, document.code ?? '').closed);
    if (result) {
      await this.act(async () => {
        await this.api.email(document.id!, result.to, result.subject, result.message);
        this.toasts.good(t('{0} sent to {1}.', document.code, result.to.join(', ')));
      });
    }
  }

  protected async repeat(): Promise<void> {
    const document = this.document();
    if (!document?.id) {
      return;
    }
    const result = await firstValueFrom(this.dialogs.open<RepeatResult, string, RepeatDialog>(RepeatDialog, document.code ?? '').closed);
    if (result) {
      await this.act(async () => {
        const copy = await this.api.repeat(document.id!, result.date, result.post);
        this.toasts.good(t('{0} repeated as {1}.', document.code, copy.code));
        await this.router.navigate(['/documents', this.category(), copy.id]);
      });
    }
  }

  protected async switchOff(): Promise<void> {
    const document = this.document();
    if (!document?.id) {
      return;
    }
    const yes = await this.dialogs.confirm({ title: `Switch off ${document.code}?`, message: t('The draft leaves the list. Nothing is deleted: its history stays.'),
      confirmLabel: t('Switch off'), danger: true });
    if (yes) {
      await this.act(async () => {
        this.release();
        await this.api.switchOff(document.id!);
        this.toasts.good(t('{0} switched off.', document.code));
        await this.router.navigate(['/documents', this.category()]);
      });
    }
  }

  protected go(id: string | null): void {
    if (id) {
      void this.router.navigate(['/documents', this.category(), id]);
    }
  }

  protected back(): void {
    void this.router.navigate(['/documents', this.category()]);
  }

  protected async showTab(tab: string | null): Promise<void> {
    this.tab.set(tab);
    const id = this.document()?.id;
    if (!id) {
      return;
    }
    if (tab === 'posting' && this.preview() === null) {
      await this.act(async () => {
        try {
          this.preview.set(await this.api.postingPreview(id));
        } catch (error) {
          this.preview.set([]); // the message says why; the tab stops loading
          throw error;
        }
      });
    }
    if (tab === 'history' && this.history() === null) {
      await this.act(async () => this.history.set(await this.api.history(id)));
    }
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      const { fields, message } = errorsOf(error);
      this.errors.set(fields);
      this.message.set(message);
    } finally {
      this.busy.set(false);
    }
  }
}
