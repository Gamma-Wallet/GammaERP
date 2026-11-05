import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type SkinKind = 'erp' | 'pos';

export interface Skin {
  id: string;
  name: string | null;
  kind: SkinKind;
  dark: boolean;
  inUse: boolean;
  tokens: number;
}

/** A skin file: what is uploaded and downloaded. */
export interface SkinFile {
  name: string | null;
  kind: SkinKind;
  dark: boolean;
  tokens: Record<string, string>;
}

export interface SkinInUse {
  id: string | null;
  name: string | null;
  dark: boolean;
  tokens: Record<string, string>;
}

/**
 * The company's skins (the founder, 2026-11-04): the values of the design tokens, one skin in use for the ERP and one for the point of sale. A skin is
 * applied as inline custom properties on an element (the page root for the ERP, the point of sale's host for the POS); a token it leaves out keeps the
 * built-in value from _tokens.scss.
 */
@Injectable({ providedIn: 'root' })
export class SkinApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/skins';
  /** The token names set on each element, so the next skin removes them first. */
  private readonly applied = new WeakMap<HTMLElement, string[]>();

  inUse(kind: SkinKind): Promise<SkinInUse> {
    return firstValueFrom(this.http.get<SkinInUse>(`${this.base}/in-use`, { params: { kind } }));
  }

  list(): Promise<Skin[]> {
    return firstValueFrom(this.http.get<Skin[]>(this.base));
  }

  file(id: string): Promise<SkinFile> {
    return firstValueFrom(this.http.get<SkinFile>(`${this.base}/${id}/file`));
  }

  upload(file: SkinFile): Promise<{ skin: Skin; warnings: string[] }> {
    return firstValueFrom(this.http.post<{ skin: Skin; warnings: string[] }>(this.base, file));
  }

  use(id: string): Promise<Skin> {
    return firstValueFrom(this.http.post<Skin>(`${this.base}/${id}/use`, {}));
  }

  useBuiltIn(kind: SkinKind): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/built-in`, {}, { params: { kind } }));
  }

  remove(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/${id}`));
  }

  /** Reads the skin in use for the kind and applies it on the element; a failure keeps the built-in look. */
  async applyInUse(kind: SkinKind, element: HTMLElement = document.documentElement): Promise<void> {
    const skin = await this.inUse(kind).catch(() => null);
    this.apply(element, skin?.tokens ?? {}, skin?.dark ?? false);
  }

  /** Sets the tokens on the element (and only those), with its colour scheme for native controls and scrollbars. */
  apply(element: HTMLElement, tokens: Record<string, string>, dark: boolean): void {
    for (const name of this.applied.get(element) ?? []) {
      element.style.removeProperty(`--${name}`);
    }
    const names = Object.keys(tokens);
    for (const name of names) {
      element.style.setProperty(`--${name}`, tokens[name]);
    }
    this.applied.set(element, names);
    element.style.colorScheme = dark ? 'dark' : '';
    element.classList.toggle('skin-dark', dark);
  }
}
