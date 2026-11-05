import { TranslatePipe } from '../core/t.pipe';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from '../core/auth';
import { DocumentApi } from '../core/documents';
import { language, languageName } from '../core/i18n';
import { MenuNode, itemAt, routeOf } from '../core/menu';
import { SessionService } from '../core/session';
import { text } from '../core/text';
import { Icon } from '../ui/icon';
import { AppFooter } from './app-footer';

const OPEN = 'gamma-erp.menu-open';

/**
 * The frame of every screen: the dark top bar (the product, the company, files, help, the account menu), the side menu with the user and
 * the cost centre on top and the user's menu tree below it (one group per top folder, opening and closing), and the page.
 */
import { Dialogs } from '../ui/dialog';
import { PasswordDialog } from './password-dialog';
import { TokensDialog } from './tokens-dialog';

@Component({
  selector: 'app-shell',
  imports: [AppFooter, TranslatePipe, NgTemplateOutlet, RouterOutlet, RouterLink, RouterLinkActive, Icon, CdkMenuTrigger, CdkMenu, CdkMenuItem],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  protected readonly text = text;
  protected readonly session = inject(SessionService);
  protected readonly language = language;
  protected readonly languageName = languageName;
  private readonly auth = inject(AuthService);
  private readonly documents = inject(DocumentApi);
  private readonly router = inject(Router);
  private readonly dialogs = inject(Dialogs);
  protected readonly routeOf = routeOf;
  /** A document type's link is active only on its own list: the same page with its own filter. */
  protected readonly exactLink = { paths: 'exact', queryParams: 'exact', fragment: 'ignored', matrixParams: 'ignored' } as const;

  private readonly url = toSignal(
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url },
  );
  protected readonly menu = this.session.menu;
  protected readonly filesRoute = computed(() => this.findScreen('files'));
  private readonly opened = signal<ReadonlySet<string>>(this.restore());
  /** The folders the user closed on the page shown: a folder holding the page stays closed until the page changes. */
  private readonly closed = signal<{ url: string; folders: ReadonlySet<string> }>({ url: '', folders: new Set() });
  private readonly closedHere = computed(() => (this.closed().url === this.url() ? this.closed().folders : new Set<string>()));
  /** The folders shown open, at any level: those the user opened, and those holding the page shown unless closed on it. */
  protected readonly open = computed(() => {
    const open = new Set(this.opened());
    const current = itemAt(this.menu(), this.url());
    const closedHere = this.closedHere();
    const walk = (nodes: readonly MenuNode[]) => {
      for (const node of nodes) {
        if (current && !closedHere.has(node.id) && (node.id === current.id ? node.documentTypes.length > 1 : this.holds(node, current))) {
          open.add(node.id);
        }
        walk(node.children);
      }
    };
    walk(this.menu());
    return open;
  });

  protected toggle(folder: MenuNode): void {
    const next = new Set(this.opened());
    const closed = new Set(this.closedHere());
    if (this.open().has(folder.id)) {
      next.delete(folder.id);
      closed.add(folder.id);
    } else {
      next.add(folder.id);
      closed.delete(folder.id);
    }
    this.opened.set(next);
    this.closed.set({ url: this.url(), folders: closed });
    try {
      localStorage.setItem(OPEN, JSON.stringify([...next]));
    } catch {
      // Remembering is a convenience only.
    }
  }

  protected tokens(): void {
    this.dialogs.open(TokensDialog, undefined, { width: '600px' });
  }

  protected changePassword(): void {
    this.dialogs.open(PasswordDialog);
  }

  protected chooseLanguage(code: string): void {
    void this.session.chooseLanguage(code);
  }

  protected async signOut(): Promise<void> {
    // The drafts the user holds are let go, so no one waits for a hold to run out.
    await this.documents.releaseAll().catch(() => undefined);
    this.auth.signOut();
  }

  private holds(folder: MenuNode, item: MenuNode): boolean {
    return folder.children.some(c => c.id === item.id || this.holds(c, item));
  }

  private findScreen(screen: string): string | null {
    const walk = (nodes: MenuNode[]): MenuNode | undefined =>
      nodes.map(n => (n.screen === screen ? n : walk(n.children))).find(n => n !== undefined);
    const node = walk(this.menu());
    return node ? routeOf(node) : null;
  }

  private restore(): ReadonlySet<string> {
    try {
      return new Set(JSON.parse(localStorage.getItem(OPEN) ?? '[]') as string[]);
    } catch {
      return new Set();
    }
  }
}
