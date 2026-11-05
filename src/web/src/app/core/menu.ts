// The signed-in user's menu (GET /api/v1/menu) and where each item leads: an item's screen key and parameter pick the route, so a new item
// needs no change here unless it brings a new kind of screen.

export interface MenuDocumentType {
  id: string;
  code: string | null;
  name: string | null;
}

export interface MenuNode {
  id: string;
  code: string | null;
  name: string | null;
  isElement: boolean;
  screen: string | null;
  parameter: string | null;
  documentCategoryId: string | null;
  icon: string | null;
  documentTypes: MenuDocumentType[];
  children: MenuNode[];
}

/** The route of an item, or null for a folder. */
export function routeOf(node: MenuNode): string | null {
  if (!node.isElement || !node.screen) {
    return null;
  }
  switch (node.screen) {
    case 'documents':
      return node.documentCategoryId ? `/documents/${node.documentCategoryId}` : null;
    case 'entities':
      return `/entities/${node.parameter}`;
    case 'config':
      return `/config/${node.parameter}`;
    case 'report':
      return `/reports/${node.parameter}`;
    case 'pos':
      return `/pos/${node.parameter ?? 'sme'}`;
    default:
      return node.parameter ? `/${node.screen}/${node.parameter}` : `/${node.screen}`;
  }
}

/** Every item of the tree, depth first. */
export function itemsOf(nodes: readonly MenuNode[]): MenuNode[] {
  return nodes.flatMap(n => (n.isElement ? [n] : itemsOf(n.children)));
}

/** The item whose route the URL is (or starts with), for the page title. */
export function itemAt(nodes: readonly MenuNode[], url: string): MenuNode | null {
  const path = url.split(/[?#]/)[0];
  return itemsOf(nodes).find(n => {
    const route = routeOf(n);
    return route !== null && (path === route || path.startsWith(`${route}/`));
  }) ?? null;
}
