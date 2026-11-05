/** A row of a register list, with its parent and type for the tree. */
export interface TreeListRow {
  id: string;
  idSup: string | null;
  typeId: string;
  code: string;
  name: string;
  type: string;
}

export interface TreeListNode extends TreeListRow {
  children: TreeListNode[];
}

/** The most rows a category is loaded whole for its tree; a larger category stays a list a page at a time. */
export const TREE_LIMIT = 2000;

/** Whether the rows nest: some row's parent is another of the rows (a cost centre under the company, an account under its group). */
export function nests(rows: readonly TreeListRow[]): boolean {
  const ids = new Set(rows.map(r => r.id));
  return rows.some(r => r.idSup !== null && ids.has(r.idSup));
}

/**
 * The tree of the rows that match the search (code or name) and the chosen types, with every parent of a match, so a result is seen
 * where it sits; a row whose parent is not among them is at the top. Open: the top level when nothing is searched, otherwise the parents
 * of the matches.
 */
export function elementTree(rows: readonly TreeListRow[], search: string, typeIds: readonly string[]): { roots: TreeListNode[]; open: string[] } {
  const byId = new Map(rows.map(r => [r.id, r]));
  const term = search.trim().toLowerCase();
  const filtered = term !== '' || typeIds.length > 0;
  const matches = rows.filter(r => (!term || r.code.toLowerCase().includes(term) || r.name.toLowerCase().includes(term))
    && (!typeIds.length || typeIds.includes(r.typeId)));
  const kept = new Set<string>();
  const open = new Set<string>();
  for (const match of matches) {
    kept.add(match.id);
    // Every parent up to the top (a loop in the data stops at the first row seen again).
    const seen = new Set<string>([match.id]);
    for (let up = match.idSup; up !== null && byId.has(up) && !seen.has(up); up = byId.get(up)!.idSup) {
      seen.add(up);
      kept.add(up);
      open.add(up);
    }
  }
  const nodes = new Map<string, TreeListNode>();
  for (const row of rows.filter(r => kept.has(r.id))) {
    nodes.set(row.id, { ...row, children: [] });
  }
  const roots: TreeListNode[] = [];
  for (const node of nodes.values()) {
    const parent = node.idSup ? nodes.get(node.idSup) : undefined;
    (parent ? parent.children : roots).push(node);
  }
  return { roots, open: filtered ? [...open] : roots.filter(r => r.children.length).map(r => r.id) };
}
