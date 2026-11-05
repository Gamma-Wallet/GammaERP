import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import {
  ArrowDown, ArrowUp, ArrowUpDown, BookOpen, Boxes, Building2, Calendar, ChartColumn, ChartPie, Check, ChefHat, ChevronDown, ChevronLeft,
  ChevronRight, ChevronUp, Circle, CircleAlert, CircleCheck, CircleHelp, CircleUser, Coins, CreditCard, Database, Ellipsis, Eye, Factory, FileCog,
  FileText, Files, Folder, GitBranch, Grid3x3, Handshake, IdCard, Image, Info, Key, Landmark, List, LogOut, MapPin, Menu, Minus, OctagonAlert,
  Package, Percent, Plus, Printer, Receipt, Repeat, Route, Rows3, Ruler, Scale, Search, Settings, Shield, ShoppingCart, Store, Table, Tag,
  Trash2, TrendingDown, TrendingUp, TriangleAlert, Truck, Undo2, Upload, User, Users, Wallet, X, Zap, Palette,
  type IconNode,
} from 'lucide';

/** The icons by the names the menu (tbl_menu.icon) and the screens use; lucide's drawings (ISC licence). */
const ICONS: Record<string, IconNode> = {
  'file-text': FileText, receipt: Receipt, cart: ShoppingCart, truck: Truck, factory: Factory, wallet: Wallet, bank: Landmark, tag: Tag,
  handshake: Handshake, book: BookOpen, 'book-open': BookOpen, repeat: Repeat, undo: Undo2, zap: Zap, shop: Store, scale: Scale,
  'trending-down': TrendingDown, 'trending-up': TrendingUp, database: Database, users: Users, package: Package, building: Building2,
  list: List, 'id-card': IdCard, ruler: Ruler, coins: Coins, 'bar-chart': ChartColumn, 'pie-chart': ChartPie, percent: Percent,
  boxes: Boxes, route: Route, table: Table, settings: Settings, 'file-cog': FileCog, rows: Rows3, 'credit-card': CreditCard,
  'git-branch': GitBranch, menu: Menu, 'chef-hat': ChefHat, grid: Grid3x3, printer: Printer, shield: Shield, user: User, key: Key,
  eye: Eye, folder: Folder, files: Files, help: CircleHelp, account: CircleUser, 'chevron-down': ChevronDown,
  'chevron-right': ChevronRight, 'log-out': LogOut,
  // The component kit's own
  check: Check, close: X, plus: Plus, minus: Minus, search: Search, 'chevron-up': ChevronUp, 'chevron-left': ChevronLeft,
  'arrow-up': ArrowUp, 'arrow-down': ArrowDown, sort: ArrowUpDown, good: CircleCheck, warning: TriangleAlert, serious: CircleAlert,
  critical: OctagonAlert, info: Info, circle: Circle, trash: Trash2, calendar: Calendar, 'map-pin': MapPin, image: Image, more: Ellipsis, upload: Upload, palette: Palette,
};

/** An icon drawn inline, in the current text colour; an unknown name draws nothing. Decorative unless given a label. */
@Component({
  selector: 'ui-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"
      stroke-linecap="round" stroke-linejoin="round" [attr.width]="size()" [attr.height]="size()"
      [attr.aria-hidden]="label() ? null : 'true'" [attr.aria-label]="label()" [attr.role]="label() ? 'img' : null">
      @for (part of parts(); track $index) {
        @switch (part[0]) {
          @case ('path') { <path [attr.d]="part[1]['d']" /> }
          @case ('circle') { <circle [attr.cx]="part[1]['cx']" [attr.cy]="part[1]['cy']" [attr.r]="part[1]['r']" /> }
          @case ('rect') {
            <rect [attr.x]="part[1]['x']" [attr.y]="part[1]['y']" [attr.width]="part[1]['width']" [attr.height]="part[1]['height']"
              [attr.rx]="part[1]['rx']" [attr.ry]="part[1]['ry']" />
          }
          @case ('line') { <line [attr.x1]="part[1]['x1']" [attr.y1]="part[1]['y1']" [attr.x2]="part[1]['x2']" [attr.y2]="part[1]['y2']" /> }
          @case ('polyline') { <polyline [attr.points]="part[1]['points']" /> }
          @case ('polygon') { <polygon [attr.points]="part[1]['points']" /> }
          @case ('ellipse') { <ellipse [attr.cx]="part[1]['cx']" [attr.cy]="part[1]['cy']" [attr.rx]="part[1]['rx']" [attr.ry]="part[1]['ry']" /> }
        }
      }
    </svg>
  `,
  styles: `:host { display: inline-flex; flex: none; } svg { display: block; }`,
})
export class Icon {
  readonly name = input.required<string | null>();
  readonly size = input(16);
  readonly label = input<string | null>(null);
  protected readonly parts = computed(() => (ICONS[this.name() ?? ''] ?? []) as unknown as [string, Record<string, string>][]);
}
