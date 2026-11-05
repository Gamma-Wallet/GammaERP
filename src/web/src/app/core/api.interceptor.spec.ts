import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NODE_HEADER, apiInterceptor } from './api.interceptor';
import { AuthService } from './auth';
import { NodeStore } from './node-store';

describe('apiInterceptor', () => {
  const auth = { accessToken: vi.fn(), signIn: vi.fn(), clear: vi.fn() };
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    vi.resetAllMocks();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([apiInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    TestBed.inject(NodeStore).node.set('node-1');
  });

  const settle = () => new Promise(resolve => setTimeout(resolve));

  it('gives /api calls the token and the cost centre, /account/me the token only, other calls nothing', async () => {
    auth.accessToken.mockResolvedValue('t-1');
    const calls = [firstValueFrom(http.get('/api/v1/menu')), firstValueFrom(http.get('/account/me')), firstValueFrom(http.get('/public.json'))];
    await settle();
    const api = backend.expectOne('/api/v1/menu');
    expect(api.request.headers.get('Authorization')).toBe('Bearer t-1');
    expect(api.request.headers.get(NODE_HEADER)).toBe('node-1');
    const me = backend.expectOne('/account/me');
    expect(me.request.headers.get('Authorization')).toBe('Bearer t-1');
    expect(me.request.headers.has(NODE_HEADER)).toBe(false);
    const other = backend.expectOne('/public.json');
    expect(other.request.headers.has('Authorization')).toBe(false);
    [api, me, other].forEach(r => r.flush([]));
    await Promise.all(calls);
  });

  it('signs in again when there is no token, or the API refuses it', async () => {
    auth.accessToken.mockResolvedValue(null);
    http.get('/api/v1/menu').subscribe();
    await settle();
    backend.expectNone('/api/v1/menu');
    expect(auth.signIn).toHaveBeenCalledTimes(1);

    auth.accessToken.mockResolvedValue('t-old');
    http.get('/api/v1/menu').subscribe();
    await settle();
    backend.expectOne('/api/v1/menu').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(auth.clear).toHaveBeenCalled();
    expect(auth.signIn).toHaveBeenCalledTimes(2);
  });

  it('keeps a cost centre a request names itself', async () => {
    auth.accessToken.mockResolvedValue('t-1');
    const add = firstValueFrom(http.post('/api/v1/companies', {}, { headers: { [NODE_HEADER]: 'corporate' } }));
    await settle();
    const company = backend.expectOne('/api/v1/companies');
    expect(company.request.headers.get(NODE_HEADER)).toBe('corporate');
    company.flush({});
    await add;
  });
});
