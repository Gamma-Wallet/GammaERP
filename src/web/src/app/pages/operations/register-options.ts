import { EntityApi, LookupScope } from '../../core/entities';
import { SelectOption } from '../../ui/select';

/** The choices of a whole register (products, units, cost centres…), through its lookup scope; none when the register has no scope. */
export async function registerOptions(entities: EntityApi, scopes: LookupScope[], table: string, search = ''): Promise<SelectOption[]> {
  const scope = scopes.find(s => s.table === table && !s.categoryId && !s.typeId);
  if (!scope) {
    return [];
  }
  const page = await entities.lookup(scope.id, search, 500).catch(() => ({ items: [] }));
  return page.items.map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined }));
}

/** Today as an input date. */
export function today(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
