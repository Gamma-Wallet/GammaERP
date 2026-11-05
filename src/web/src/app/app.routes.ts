import { Routes } from '@angular/router';
import { menuScreenGuard, shellGuard, signedInGuard } from './core/guards';
import { ChooseNode } from './pages/choose-node';
import { Home } from './pages/home';
import { ScreenPage } from './pages/screen-page';
import { SignedIn } from './pages/signed-in';
import { Shell } from './shell/shell';

/** The screens by the menu's screen keys (see core/menu.ts routeOf); a kind of screen not built yet shows its placeholder. */
export const routes: Routes = [
  { path: 'signed-in', component: SignedIn },
  { path: 'choose', component: ChooseNode, canActivate: [signedInGuard] },
  { path: 'new-company', loadComponent: () => import('./pages/setup/new-company').then(m => m.NewCompany), canActivate: [signedInGuard] },
  // The touch point of sale fills the screen: no menu, a waiter's own top bar (still at a chosen cost centre, with its rights).
  { path: 'pos/:kind', loadComponent: () => import('./pages/pos/pos').then(m => m.Pos), canActivate: [shellGuard] },
  {
    path: '',
    component: Shell,
    canActivate: [shellGuard],
    children: [
      { path: '', component: Home },
      // The component kit's reference page (not in the menu): every building block with made-up data.
      { path: 'kit', loadComponent: () => import('./pages/kit').then(m => m.Kit) },
      { path: 'documents/:category', loadComponent: () => import('./pages/documents/document-list').then(m => m.DocumentList) },
      { path: 'documents/:category/new/:type', loadComponent: () => import('./pages/documents/document-editor').then(m => m.DocumentEditor) },
      { path: 'documents/:category/:id', loadComponent: () => import('./pages/documents/document-editor').then(m => m.DocumentEditor) },
      // A register opened on its categories and types, from System settings › Types and categories.
      { path: 'setup/:entity', loadComponent: () => import('./pages/registers/register-page').then(m => m.RegisterPage), data: { setupFirst: true } },
      { path: 'entities/:entity', loadComponent: () => import('./pages/registers/register-page').then(m => m.RegisterPage) },
      { path: 'config/:model', loadComponent: () => import('./pages/config/config-page').then(m => m.ConfigPage) },
      { path: 'skins', loadComponent: () => import('./pages/config/skins').then(m => m.Skins), canActivate: [menuScreenGuard] },
      { path: 'document-types', loadComponent: () => import('./pages/config/document-types').then(m => m.DocumentTypes) },
      { path: 'rights', loadComponent: () => import('./pages/config/rights').then(m => m.Rights) },
      { path: 'production', loadComponent: () => import('./pages/operations/production').then(m => m.Production), canActivate: [menuScreenGuard] },
      { path: 'repairs', loadComponent: () => import('./pages/operations/repairs').then(m => m.Repairs), canActivate: [menuScreenGuard] },
      { path: 'production-display', loadComponent: () => import('./pages/operations/production-display').then(m => m.ProductionDisplay), canActivate: [menuScreenGuard] },
      { path: 'pos-shifts', loadComponent: () => import('./pages/operations/pos-shifts').then(m => m.PosShifts), canActivate: [menuScreenGuard] },
      { path: 'lots', loadComponent: () => import('./pages/operations/lots').then(m => m.Lots), canActivate: [menuScreenGuard] },
      { path: 'serials', loadComponent: () => import('./pages/operations/serials').then(m => m.Serials), canActivate: [menuScreenGuard] },
      { path: 'user-activity', loadComponent: () => import('./pages/operations/user-activity').then(m => m.UserActivity), canActivate: [menuScreenGuard] },
      { path: 'migration', loadComponent: () => import('./pages/operations/migration').then(m => m.Migration), canActivate: [menuScreenGuard] },
      { path: 'dashboard', loadComponent: () => import('./pages/reports/dashboard').then(m => m.Dashboard), canActivate: [menuScreenGuard] },
      { path: 'stock-convert', loadComponent: () => import('./pages/operations/stock-convert').then(m => m.StockConvert), canActivate: [menuScreenGuard] },
      { path: 'depreciation', loadComponent: () => import('./pages/operations/depreciation').then(m => m.Depreciation), canActivate: [menuScreenGuard] },
      { path: 'opportunity-board', loadComponent: () => import('./pages/operations/opportunity-board').then(m => m.OpportunityBoard), canActivate: [menuScreenGuard] },
      { path: 'bank-statements', loadComponent: () => import('./pages/operations/bank-statements').then(m => m.BankStatements), canActivate: [menuScreenGuard] },
      { path: 'approvals', loadComponent: () => import('./pages/operations/approvals').then(m => m.Approvals), canActivate: [menuScreenGuard] },
      { path: 'invoice-run', loadComponent: () => import('./pages/operations/invoice-run').then(m => m.InvoiceRun), canActivate: [menuScreenGuard] },
      { path: 'renewals', loadComponent: () => import('./pages/operations/renewals').then(m => m.Renewals), canActivate: [menuScreenGuard] },
      { path: 'year-closing', loadComponent: () => import('./pages/operations/year-closing').then(m => m.YearClosing), canActivate: [menuScreenGuard] },
      { path: 'asset-value', loadComponent: () => import('./pages/operations/asset-value').then(m => m.AssetValue), canActivate: [menuScreenGuard] },
      { path: 'currencies', loadComponent: () => import('./pages/operations/currencies').then(m => m.Currencies), canActivate: [menuScreenGuard] },
      { path: 'files', loadComponent: () => import('./pages/operations/files').then(m => m.Files), canActivate: [menuScreenGuard] },
      { path: 'reports/:report', loadComponent: () => import('./pages/reports/report-page').then(m => m.ReportPage), canActivate: [menuScreenGuard] },
      { path: ':screen', component: ScreenPage, canActivate: [menuScreenGuard] },
      { path: ':screen/:parameter', component: ScreenPage, canActivate: [menuScreenGuard] },
    ],
  },
];
