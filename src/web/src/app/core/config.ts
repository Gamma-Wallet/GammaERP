import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toSaveJson } from '../ui/property-form.logic';
import { Right } from './entities';

/** A column of a configuration model (ConfigColumnDto, B6). */
export interface ConfigColumn {
  name: string;
  kind: 'text' | 'translated' | 'number' | 'integer' | 'boolean' | 'date' | 'time' | 'datetime' | 'id' | 'json';
  required: boolean;
  isLink: boolean;
  linkModel: string | null;
}

export interface ConfigSchema {
  model: string;
  columns: ConfigColumn[];
}

export interface ConfigChoice {
  id: string;
  code: string | null;
  name: string;
}

/** A configuration row as the API sends it: its columns by name, its id, owner and history fields. */
export type ConfigRow = Record<string, unknown> & { id?: string };

export interface DocumentTypeSettings {
  neverPosted: boolean;
  allowsPartialReversal: boolean;
  sendsToProduction: boolean;
  productionDocumentTypeId: string | null;
  sellsProducedProduct: boolean;
  producedOnProductionScreen: boolean;
  startsRepair: boolean;
  canBeAccepted: boolean;
  holdsOpenItem: boolean;
  paysOpenItems: boolean;
  billsByPlan: boolean;
  needsApproval: boolean;
  sellsAssets: boolean;
  isReversal: boolean;
  reversedDocumentTypeId: string | null;
}

const JSON_HEADERS = new HttpHeaders({ 'Content-Type': 'application/json' });

/** The configuration and administration API (C3 phase 5). */
@Injectable({ providedIn: 'root' })
export class ConfigApi {
  private readonly http = inject(HttpClient);
  private readonly schemas = new Map<string, Promise<ConfigSchema>>();

  schema(model: string): Promise<ConfigSchema> {
    let schema = this.schemas.get(model);
    if (!schema) {
      schema = firstValueFrom(this.http.get<ConfigSchema>(`/api/v1/config/${model}/schema`));
      this.schemas.set(model, schema);
      schema.catch(() => this.schemas.delete(model));
    }
    return schema;
  }

  list(model: string, filters: Record<string, string> = {}): Promise<ConfigRow[]> {
    return firstValueFrom(this.http.get<ConfigRow[]>(`/api/v1/config/${model}`, { params: filters }));
  }

  save(model: string, row: ConfigRow): Promise<ConfigRow> {
    return firstValueFrom(this.http.post<ConfigRow>(`/api/v1/config/${model}`, toSaveJson(row), { headers: JSON_HEADERS }));
  }

  switchOff(model: string, id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/v1/config/${model}/${id}`));
  }

  choices(model: string, column: string, search = ''): Promise<ConfigChoice[]> {
    const params: Record<string, string> = search.trim() ? { search: search.trim() } : {};
    return firstValueFrom(this.http.get<ConfigChoice[]>(`/api/v1/config/${model}/choices/${column}`, { params }));
  }

  documentTypeSettings(id: string): Promise<DocumentTypeSettings> {
    return firstValueFrom(this.http.get<DocumentTypeSettings>(`/api/v1/documents/types/${id}/settings`));
  }

  saveDocumentTypeSettings(id: string, settings: DocumentTypeSettings): Promise<DocumentTypeSettings> {
    return firstValueFrom(this.http.put<DocumentTypeSettings>(`/api/v1/documents/types/${id}/settings`, settings));
  }

  documentTypeUsers(id: string): Promise<{ userIds: string[]; roleIds: string[] }> {
    return firstValueFrom(this.http.get<{ userIds: string[]; roleIds: string[] }>(`/api/v1/documents/types/${id}/users`));
  }

  saveDocumentTypeUsers(id: string, userIds: string[], roleIds: string[]): Promise<unknown> {
    return firstValueFrom(this.http.put(`/api/v1/documents/types/${id}/users`, { userIds, roleIds }));
  }

  rights(of: 'users' | 'roles', id: string): Promise<Right[]> {
    return firstValueFrom(this.http.get<Right[]>(`/api/v1/${of}/${id}/rights`));
  }

  saveRights(of: 'users' | 'roles', id: string, rights: Right[]): Promise<Right[]> {
    return firstValueFrom(this.http.put<Right[]>(`/api/v1/${of}/${id}/rights`, rights));
  }

  menuRights<T>(of: 'users' | 'roles', id: string): Promise<T[]> {
    return firstValueFrom(this.http.get<T[]>(`/api/v1/${of}/${id}/menu`));
  }

  saveMenuRights(of: 'users' | 'roles', id: string, menu: string[]): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/v1/${of}/${id}/menu`, menu));
  }

  costCenters(userId: string): Promise<string[]> {
    return firstValueFrom(this.http.get<string[]>(`/api/v1/users/${userId}/cost-centers`));
  }

  saveCostCenters(userId: string, costCenters: string[]): Promise<string[]> {
    return firstValueFrom(this.http.put<string[]>(`/api/v1/users/${userId}/cost-centers`, costCenters));
  }

  changePassword(current: string, next: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/v1/account/password', { current, new: next }));
  }
}
