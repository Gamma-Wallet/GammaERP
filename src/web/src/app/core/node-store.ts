import { Injectable, signal } from '@angular/core';

const KEY = 'gamma-erp.node.';

/**
 * The cost centre the user works at now (the API's X-Gamma-Node), remembered per user in this browser. Kept apart from the session so the
 * API interceptor can read it without depending on HttpClient.
 */
@Injectable({ providedIn: 'root' })
export class NodeStore {
  readonly node = signal<string | null>(null);

  set(userId: string, node: string): void {
    this.node.set(node);
    try {
      localStorage.setItem(KEY + userId, node);
    } catch {
      // Remembering is a convenience only.
    }
  }

  remembered(userId: string): string | null {
    try {
      return localStorage.getItem(KEY + userId);
    } catch {
      return null;
    }
  }
}
