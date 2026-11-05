import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { NODE_HEADER } from '../../core/api.interceptor';
import { errorsOf } from '../../core/entities';
import { SessionService } from '../../core/session';
import { Button, Checkbox, Field, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';
import { Activity, ActivityPicker, COUNTRY_NAMES, GROUP_NAMES, GroupsPicker, PackGroup, SetupApi, SetupCountry, pathOf } from './setup-parts';

// No cash desks or banks here: they are cost centres, set up in the cost-centre register (the founder, 2026-10-31).
export const COMPANY_STEPS = ['Country', 'Company', 'Activity', 'Starting configuration', 'Review'] as const;

/** What the new-company steps collect (CompanyRequest). */
export interface CompanyModel {
  country: string | null;
  name: string;
  code: string;
  activity: string;
  fromCatalogue: boolean;
  copyFrom: string | null;
  groups: string[];
}

/** Why a step cannot be left yet; null when it can. */
export function companyProblem(step: number, m: CompanyModel): string | null {
  switch (step) {
    case 0:
      return m.country ? null : t('Choose the country.');
    case 1:
      return m.name.trim() ? null : t('Name the company.');
    case 3:
      return m.groups.length ? null : t('Choose at least one part to load.');
    default:
      return null;
  }
}

export function companyRequestOf(m: CompanyModel): object {
  return {
    name: m.name.trim(), country: m.country, code: m.code.trim() || null, activity: m.activity, fromCatalogue: m.fromCatalogue, groups: m.groups,
    copyFrom: m.copyFrom,
  };
}

/**
 * A new company of the group (the founder, 2026-10-31: started from the entry page; every new company goes through the same steps): its country,
 * name, activity, starting configuration (the country's pack, or copied from a company of the group). Made at the group's
 * corporate, which only an administrator working there may do; a single company has no group to add to.
 */
@Component({
  selector: 'app-new-company',
  imports: [TranslatePipe, ActivityPicker, Button, Checkbox, Field, GroupsPicker, Icon, Input, RouterLink, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './new-company.html',
  styleUrl: './setup.scss',
})
export class NewCompany {
  private readonly api = inject(SetupApi);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  protected readonly session = inject(SessionService);
  protected readonly steps = COMPANY_STEPS;
  protected readonly groupNames = GROUP_NAMES;

  /** The group the user works at, if they do. */
  protected readonly corporate = computed(() => this.session.me()?.costCenters.find(c => c.isGroup) ?? null);
  protected readonly companies = computed<SelectOption<string | null>[]>(() => {
    const seen = new Map<string, string>();
    for (const c of this.session.me()?.costCenters ?? []) {
      // A company to copy from: a group is not one.
      if (c.companyId && !c.isGroup) {
        seen.set(c.companyId, c.companyName ?? c.companyId);
      }
    }
    return [{ value: null, label: t("The country's starter pack") }, ...[...seen].map(([id, name]) => ({ value: id as string | null, label: `Copy of ${name}` }))];
  });

  protected readonly step = signal(0);
  protected readonly model = signal<CompanyModel>({ country: null, name: '', code: '', activity: 'GENERAL', fromCatalogue: true, copyFrom: null, groups: [] });
  protected readonly countries = signal<SetupCountry[]>([]);
  protected readonly catalogue = signal<Activity[]>([]);
  protected readonly packGroups = signal<PackGroup[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly linking = signal(false);

  protected readonly problem = computed(() => companyProblem(this.step(), this.model()));
  protected readonly countryOptions = computed<SelectOption[]>(() => this.countries().map(c => ({ value: c.code, label: COUNTRY_NAMES[c.code] ?? c.code,
    hint: c.currency ?? undefined })).sort((a, b) => a.label.localeCompare(b.label)));
  protected readonly activityPath = computed(() => pathOf(this.catalogue(), this.model().activity).map(a => a.name['en'] ?? a.code).join(' › '));
  protected readonly source = computed(() => this.companies().find(c => c.value === this.model().copyFrom)?.label ?? '');

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [countries, catalogue, groups] = await Promise.all([this.api.countries(), this.api.activities(), this.api.groups()]);
      this.countries.set(countries);
      this.catalogue.set(catalogue);
      this.packGroups.set(groups);
      this.set({ groups: groups.map(g => g.name) });
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected set(change: Partial<CompanyModel>): void {
    this.model.update(m => ({ ...m, ...change }));
  }

  protected next(): void {
    if (!this.problem()) {
      this.message.set(null);
      this.step.update(s => Math.min(s + 1, COMPANY_STEPS.length - 1));
    }
  }

  protected back(): void {
    this.message.set(null);
    this.step.update(s => Math.max(s - 1, 0));
  }

  /** Makes the group and each company each other's partner where they are not yet (made at the group). */
  protected async linkPartners(): Promise<void> {
    const corporate = this.corporate();
    if (!corporate) {
      return;
    }
    this.linking.set(true);
    this.message.set(null);
    try {
      const made = await firstValueFrom(this.http.post<{ rows: number }>('/api/v1/group-books/partners', {}, { headers: new HttpHeaders({ [NODE_HEADER]: corporate.id }) }));
      this.toasts.good(made.rows ? t('{0} partner links made.', made.rows) : t('The group and its companies are already partners of each other.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.linking.set(false);
    }
  }

  protected async create(): Promise<void> {
    const corporate = this.corporate();
    if (!corporate) {
      return;
    }
    this.creating.set(true);
    this.message.set(null);
    try {
      // Made at the group: the request names the corporate as its cost centre.
      const made = await firstValueFrom(this.http.post<{ company: string; rows: number }>('/api/v1/companies', companyRequestOf(this.model()),
        { headers: new HttpHeaders({ [NODE_HEADER]: corporate.id }) }));
      this.toasts.good(t('{0} is set up ({1} configuration rows).', this.model().name.trim(), made.rows));
      await this.router.navigateByUrl('/choose');
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.creating.set(false);
    }
  }
}
