import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfigApi, ConfigRow, DocumentTypeSettings } from '../../core/config';
import { DocumentApi } from '../../core/documents';
import { Category, ElementType, EntityApi, REGISTERS, RightsService, errorsOf } from '../../core/entities';
import { Button, Checkbox, Field } from '../../ui/controls';
import { textIn } from '../../ui/format';
import { Select, SelectOption } from '../../ui/select';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { MigrationFile } from '../operations/migration-file';
import { ConfigTable } from './config-table';

interface TypeGroup {
  category: Category;
  types: ElementType[];
}

/** Document types by category, each type in the first category it is in; types in none come last. */
export function groupTypes(categories: readonly Category[], types: readonly ElementType[]): TypeGroup[] {
  const placed = new Set<string>();
  const groups = categories.filter(c => c.isElement).map(category => {
    const inIt = types.filter(t => t.id && !placed.has(t.id) && t.categoryIds.includes(category.id!));
    inIt.forEach(t => placed.add(t.id!));
    return { category, types: inIt };
  }).filter(g => g.types.length);
  const rest = types.filter(t => t.id && !placed.has(t.id));
  return rest.length ? [...groups, { category: { id: 'none', idSup: null, active: true, isElement: true, code: null, name: { en: 'Other' }, description: null,
    sortOrder: 0 }, types: rest }] : groups;
}

/**
 * The document types' configuration: a type's own settings, who may use it, its from / to sides, its kinds of
 * line (with each kind's fill, scope, sides and posting scenarios), its VAT groups and payment methods. Every part is the company's
 * configuration rows, edited by administrators.
 */
@Component({
  selector: 'app-document-types',
  imports: [TranslatePipe, Button, Checkbox, ConfigTable, Field, MigrationFile, Select, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Document types' | t }}</h1></div>
    <div class="files">
      <div><p class="muted">{{ 'Document categories' | t }}</p><app-migration-file step="DOCUMENT_CATEGORIES" /></div>
      <div><p class="muted">{{ 'Document types' | t }}</p><app-migration-file step="DOCUMENT_TYPES" /></div>
    </div>
    @if (message()) { <p class="message" role="alert">{{ message() }}</p> }
    <div class="frame">
      <nav class="types" [attr.aria-label]="'Document types' | t">
        @for (group of groups(); track group.category.id) {
          <p class="group">{{ name(group.category) }}</p>
          @for (type of group.types; track type.id) {
            <button type="button" class="type" [class.current]="type.id === typeId()" [attr.aria-current]="type.id === typeId() ? 'true' : null" (click)="choose(type)">
              <span class="code">{{ type.code }}</span>{{ name(type) }}
            </button>
          }
        }
      </nav>
      <section class="panel">
        @if (type(); as t) {
          <h2><span class="code">{{ t.code }}</span> {{ name(t) }}</h2>
          <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'Document type' | t" />
          @switch (tab()) {
            @case ('general') {
              @if (settings(); as s) {
                <div class="form">
                  <ui-checkbox [checked]="s.neverPosted" [disabled]="!admin()" (checkedChange)="setSettings({ neverPosted: $event })">{{ 'Posting books nothing (offers, contracts, orders)' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.allowsPartialReversal" [disabled]="!admin()" (checkedChange)="setSettings({ allowsPartialReversal: $event })">{{ 'A reversal may take part of the lines' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.sendsToProduction" [disabled]="!admin()" (checkedChange)="setSettings({ sendsToProduction: $event })">{{ 'Its lines go to the production point display' | t }}</ui-checkbox>
                  @if (s.sendsToProduction) {
                    <ui-field [label]="'Production document made' | t"><ui-select [options]="typeOptions()" [value]="s.productionDocumentTypeId" [disabled]="!admin()" (valueChange)="setSettings({ productionDocumentTypeId: $event })" /></ui-field>
                  }
                  <ui-checkbox [checked]="s.sellsProducedProduct" [disabled]="!admin()" (checkedChange)="setSettings({ sellsProducedProduct: $event })">{{ 'A line from a produced order takes the product itself' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.producedOnProductionScreen" [disabled]="!admin()" (checkedChange)="setSettings({ producedOnProductionScreen: $event })">{{ 'Its orders are listed on the Production screen, to be produced' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.holdsOpenItem" [disabled]="!admin()" (checkedChange)="setSettings({ holdsOpenItem: $event })">{{ 'An invoice: it stays open until a payment covers it' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.paysOpenItems" [disabled]="!admin()" (checkedChange)="setSettings({ paysOpenItems: $event })">{{ 'A payment: its lines name the invoices they cover' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.billsByPlan" [disabled]="!admin()" (checkedChange)="setSettings({ billsByPlan: $event })">{{ 'A contract billed by a plan' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.needsApproval" [disabled]="!admin()" (checkedChange)="setSettings({ needsApproval: $event })">{{ 'Over the limit of its writer it waits for approval' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.sellsAssets" [disabled]="!admin()" (checkedChange)="setSettings({ sellsAssets: $event })">{{ 'Its posted documents sell the fixed assets on their lines' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.startsRepair" [disabled]="!admin()" (checkedChange)="setSettings({ startsRepair: $event })">{{ 'A new repair job opens this type' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.canBeAccepted" [disabled]="!admin()" (checkedChange)="setSettings({ canBeAccepted: $event })">{{ 'Can be marked accepted or refused by the client' | t }}</ui-checkbox>
                  <ui-checkbox [checked]="s.isReversal" [disabled]="!admin()" (checkedChange)="setSettings({ isReversal: $event })">{{ 'This type is a reversal' | t }}</ui-checkbox>
                  @if (s.isReversal) {
                    <ui-field [label]="'It reverses' | t">
                      <ui-select [options]="typeOptions()" [value]="s.reversedDocumentTypeId" [disabled]="!admin()" (valueChange)="setSettings({ reversedDocumentTypeId: $event })" />
                    </ui-field>
                  }
                  @if (admin()) { <div><button uiButton variant="primary" (click)="saveSettings()">{{ 'Save' | t }}</button></div> }
                </div>
              }
            }
            @case ('users') {
              <div class="form">
                <p class="muted">{{ 'Only the users and roles chosen here see the type in their menu and lists.' | t }}</p>
                <ui-field [label]="'Users' | t"><ui-select [multiple]="true" [options]="userOptions()" [values]="userIds()" [disabled]="!admin()" (valuesChange)="userIds.set($event)" /></ui-field>
                <ui-field [label]="'Roles' | t"><ui-select [multiple]="true" [options]="roleOptions()" [values]="roleIds()" [disabled]="!admin()" (valuesChange)="roleIds.set($event)" /></ui-field>
                @if (admin()) { <div><button uiButton variant="primary" (click)="saveUsers()">{{ 'Save' | t }}</button></div> }
              </div>
            }
            @case ('sides') {
              <h3>{{ 'From' | t }}</h3>
              <app-config-table model="document-type-from" [fixed]="{ documentTypeId: t.id! }" />
              <h3>{{ 'To' | t }}</h3>
              <app-config-table model="document-type-to" [fixed]="{ documentTypeId: t.id! }" />
            }
            @case ('lines') {
              <app-config-table model="document-type-transaction-types" [fixed]="{ documentTypeId: t.id! }" />
              @if (kinds().length) {
                <ui-field [label]="'Transaction type' | t">
                  <ui-select [options]="kindOptions()" [value]="kindId()" (valueChange)="kindId.set($event)" />
                </ui-field>
              }
              @if (kindId(); as kind) {
                <h3>{{ 'How its lines are filled' | t }}</h3>
                <app-config-table model="transaction-fills" [fixed]="{ documentTypeTransactionTypeId: kind }" />
                <h3>{{ 'Its lines\\' from / to' | t }}</h3>
                <app-config-table model="transaction-type-from" [fixed]="{ documentTypeTransactionTypeId: kind }" />
                <app-config-table model="transaction-type-to" [fixed]="{ documentTypeTransactionTypeId: kind }" />
                <h3>{{ 'Posting scenarios' | t }}</h3>
                <app-config-table model="scenarios" [fixed]="{ documentTypeTransactionTypeId: kind }" />
              }
            }
            @case ('vat') { <app-config-table model="document-type-vat-groups" [fixed]="{ documentTypeId: t.id! }" /> }
            @case ('payments') { <app-config-table model="payment-method-document-types" [fixed]="{ documentTypeId: t.id! }" /> }
          }
        } @else {
          <p class="muted">{{ 'Choose a document type.' | t }}</p>
        }
      </section>
    </div>
  `,
  styles: `
    :host { display: grid; grid-template-rows: auto auto 1fr; height: 100%; min-height: 0; }
    .files { display: flex; flex-wrap: wrap; gap: var(--space-5); padding: 0 var(--space-6) var(--space-3); }
    .files p { margin: 0 0 var(--space-1); }
    .head { padding: var(--space-5) var(--space-6) var(--space-4); }
    h1 { font-size: var(--title-l); }
    h2 { font-size: var(--title-m); } h3 { margin-top: var(--space-3); font-size: var(--title-s); }
    .code { color: var(--muted); font: 500 var(--text-s) var(--font-text); }
    .message { margin: 0 var(--space-6) var(--space-3); padding: var(--space-3); border-radius: var(--radius); background: var(--critical-bg); color: var(--critical); }
    .frame { display: grid; grid-template-columns: 280px minmax(0, 1fr); min-height: 0; border-top: 1px solid var(--line); }
    .types { padding: var(--space-3) var(--space-2); border-right: 1px solid var(--line); background: var(--surface); overflow-y: auto; }
    .group { margin: var(--space-3) var(--space-2) var(--space-1); color: var(--ink-2); font-size: var(--text-xs); font-weight: 600; }
    .type { display: flex; gap: var(--space-2); width: 100%; padding: var(--space-1) var(--space-2); border: 0; border-radius: var(--radius-s); background: none;
      text-align: left; cursor: pointer; }
    .type .code { min-width: 52px; }
    .type:hover { background: var(--hover); }
    .type.current { background: var(--selected); font-weight: 500; }
    .panel { display: grid; align-content: start; gap: var(--space-4); min-width: 0; padding: var(--space-4) var(--space-6); overflow-y: auto; }
    .form { display: grid; gap: var(--space-4); max-width: 560px; }
    .form p { margin: 0; }
  `,
})
export class DocumentTypes {
  private readonly documents = inject(DocumentApi);
  private readonly entities = inject(EntityApi);
  private readonly config = inject(ConfigApi);
  private readonly rights = inject(RightsService);
  private readonly toasts = inject(Toasts);

  protected readonly admin = this.rights.isAdministrator;
  protected readonly message = signal<string | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly types = signal<ElementType[]>([]);
  protected readonly typeId = signal<string | null>(null);
  protected readonly tab = signal<string | null>('general');
  protected readonly settings = signal<DocumentTypeSettings | null>(null);
  protected readonly userIds = signal<string[]>([]);
  protected readonly roleIds = signal<string[]>([]);
  protected readonly userOptions = signal<SelectOption[]>([]);
  protected readonly roleOptions = signal<SelectOption[]>([]);
  protected readonly kinds = signal<ConfigRow[]>([]);
  protected readonly kindNames = signal<Map<string, string>>(new Map());
  protected readonly kindId = signal<string | null>(null);
  protected readonly tabs: Tab[] = [
    { id: 'general', label: t('General') }, { id: 'users', label: t('Users') }, { id: 'sides', label: t('From / To') }, { id: 'lines', label: t('Transaction types') },
    { id: 'vat', label: t('VAT groups') }, { id: 'payments', label: t('Payment methods') },
  ];

  protected readonly groups = computed(() => groupTypes(this.categories(), this.types()));
  protected readonly type = computed(() => this.types().find(t => t.id === this.typeId()) ?? null);
  protected readonly typeOptions = computed<SelectOption<string | null>[]>(() =>
    [{ value: null, label: '—' }, ...this.allTypes().filter(t => t.id !== this.typeId()).map(t => ({ value: t.id as string | null, label: `${t.code} ${textIn(t.name)}` }))]);
  /** Every type, those under another too: the choices on the General tab. */
  private readonly allTypes = signal<ElementType[]>([]);
  protected readonly kindOptions = computed<SelectOption[]>(() =>
    this.kinds().map(k => ({ value: String(k['id']), label: this.kindNames().get(String(k['transactionTypeId'])) ?? String(k['transactionTypeId']) })));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      await this.rights.load();
      const [categories, types] = await Promise.all([this.documents.categories(), this.documents.types()]);
      this.categories.set(categories);
      this.types.set(types.filter(t => !t.idSup));
      this.allTypes.set(types);
      void this.loadPeople();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async loadPeople(): Promise<void> {
    const roles = await this.entities.categories(REGISTERS['users']).catch(() => []);
    this.roleOptions.set(roles.filter(r => r.isElement).map(r => ({ value: r.id!, label: textIn(r.name) })));
    this.userOptions.set((await this.entities.users().catch(() => [])).map(u => ({ value: u.id, label: u.name, hint: u.code ?? undefined })));
    const transactionTypes = await this.entities.types(REGISTERS['transactions']).catch(() => []);
    this.kindNames.set(new Map(transactionTypes.map(t => [t.id!, `${t.code ?? ''} ${textIn(t.name)}`.trim()])));
  }

  protected name(row: { name: Record<string, string> }): string {
    return textIn(row.name);
  }

  protected async choose(type: ElementType): Promise<void> {
    this.typeId.set(type.id);
    this.kindId.set(null);
    this.settings.set(null);
    this.message.set(null);
    try {
      const [settings, users, kinds] = await Promise.all([this.config.documentTypeSettings(type.id!), this.config.documentTypeUsers(type.id!),
        this.config.list('document-type-transaction-types', { documentTypeId: type.id! })]);
      this.settings.set(settings);
      this.userIds.set(users.userIds);
      this.roleIds.set(users.roleIds);
      this.kinds.set(kinds);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected setSettings(change: Partial<DocumentTypeSettings>): void {
    const settings = this.settings();
    if (settings) {
      this.settings.set({ ...settings, ...change });
    }
  }

  protected async saveSettings(): Promise<void> {
    const id = this.typeId();
    const settings = this.settings();
    if (!id || !settings) {
      return;
    }
    try {
      this.settings.set(await this.config.saveDocumentTypeSettings(id, settings));
      this.toasts.good(t('Settings saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async saveUsers(): Promise<void> {
    const id = this.typeId();
    if (!id) {
      return;
    }
    try {
      await this.config.saveDocumentTypeUsers(id, this.userIds(), this.roleIds());
      this.toasts.good(t('Users and roles saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
