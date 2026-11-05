import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RightsService } from './entities';
import { language, languageChoices, setCompanyLanguage, switchLanguage } from './i18n';
import { MenuNode } from './menu';
import { NodeStore } from './node-store';
import { SkinApi } from './skins';

export interface MeCostCenter {
  id: string;
  code: string | null;
  name: string | null;
  companyId: string | null;
  companyName: string | null;
  /** The place above it in the entry page's tree (null at the top). */
  parentId?: string | null;
  /** Linked to the user (otherwise a workplace below a linked one). */
  linked?: boolean;
  /** The language of its company. */
  language?: string;
  /** The group above the companies, which keeps its own books too. */
  isGroup?: boolean;
}

export interface Me {
  id: string;
  username: string | null;
  name: string | null;
  isAdministrator: boolean;
  roles: { id: string; code: string | null; name: string | null }[];
  costCenters: MeCostCenter[];
  /** The language chosen at the top of the screens. */
  language?: string | null;
}

/** Who is signed in (GET /account/me), the cost centre they work at now, and their menu there (GET /api/v1/menu). */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly nodes = inject(NodeStore);
  private readonly rights = inject(RightsService);
  private readonly skins = inject(SkinApi);

  readonly me = signal<Me | null>(null);
  readonly menu = signal<MenuNode[]>([]);
  readonly menuLoaded = signal(false);
  readonly node = this.nodes.node;
  readonly place = computed(() => this.me()?.costCenters.find(c => c.id === this.node()) ?? null);
  /** The company's two languages, for the top of the screens. */
  readonly languages = computed(() => languageChoices(this.place()?.language ?? 'en'));

  /** Loads the user and picks their cost centre: the one remembered here if still theirs, else the only one; else none (they choose). */
  async load(): Promise<void> {
    const me = await firstValueFrom(this.http.get<Me>('/account/me'));
    this.me.set(me);
    const remembered = this.nodes.remembered(me.id);
    const pick = me.costCenters.find(c => c.id === remembered) ?? (me.costCenters.length === 1 ? me.costCenters[0] : null);
    if (pick) {
      this.nodes.set(me.id, pick.id);
      this.followLanguage();
    }
  }

  /** The user's choice where the company has it, else English, the default; the screens start again when that is not the language they are in. */
  private followLanguage(): void {
    setCompanyLanguage(this.place()?.language ?? 'en');
    const chosen = this.me()?.language;
    const wanted = chosen && this.languages().includes(chosen) ? chosen : 'en';
    if (wanted !== language()) {
      switchLanguage(wanted);
    }
  }

  /** The language chosen at the top of the screens: kept on the user, and the screens start again in it. */
  async chooseLanguage(code: string): Promise<void> {
    await firstValueFrom(this.http.put('/api/v1/account/language', { language: code }));
    switchLanguage(code);
  }

  /** Works at another cost centre: its menu replaces the current one. */
  async choose(node: string): Promise<void> {
    const me = this.me();
    if (!me?.costCenters.some(c => c.id === node)) {
      throw new Error('That cost centre is not one of yours.');
    }
    this.nodes.set(me.id, node);
    this.followLanguage();
    // Rights depend on the cost centre: read them again there.
    this.rights.reset();
    this.menuLoaded.set(false);
    await this.loadMenu();
  }

  async loadMenu(): Promise<void> {
    this.menu.set(await firstValueFrom(this.http.get<MenuNode[]>('/api/v1/menu')));
    this.menuLoaded.set(true);
    // The company's skin for the ERP screens (its tokens over the built-in look); the point of sale applies its own.
    void this.skins.applyInUse('erp');
  }
}
