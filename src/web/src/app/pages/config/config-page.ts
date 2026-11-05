import { MigrationFile } from '../operations/migration-file';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ConfigTable } from './config-table';
import { MODEL_NAMES, columnLabel } from './labels';

/** A configuration model's screen (/config/:model): its rows, edited by administrators. */
/** The lists that are a Migration step too, by their configuration model. */
const MIGRATION_STEPS: Record<string, string> = {
  scenarios: 'POSTING_GROUPS', 'vat-groups': 'VAT_GROUPS', 'document-type-vat-groups': 'DOCUMENT_VAT_GROUPS', 'vat-return-boxes': 'VAT_RETURN_BOXES', 'vat-return-box-rules': 'VAT_RETURN_BOX_RULES',
};

@Component({
  selector: 'app-config-page',
  imports: [ConfigTable, MigrationFile],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ title() }}</h1></div>
    @if (step(); as code) { <app-migration-file class="files" [step]="code" /> }
    <app-config-table [model]="model()" />
  `,
  styles: `
    :host { display: grid; grid-template-columns: minmax(0, 1fr); align-content: start; gap: var(--space-4); padding: var(--space-5) var(--space-6); }
    h1 { font-size: var(--title-l); }
  `,
})
export class ConfigPage {
  /** The model's key in the API (config/:model). */
  readonly model = input.required<string>();
  protected readonly step = computed(() => MIGRATION_STEPS[this.model()] ?? null);
  protected readonly title = computed(() => MODEL_NAMES[this.model()] ?? columnLabel(this.model()));
}
