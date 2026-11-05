import { describe, expect, it } from 'vitest';
import { challengeOf, randomString } from './pkce';

describe('PKCE', () => {
  it('gives the S256 challenge of RFC 7636, appendix B', async () => {
    expect(await challengeOf('dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk')).toBe('E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM');
  });

  it('makes verifiers of 64 base64url characters, each new', () => {
    const a = randomString();
    expect(a).toMatch(/^[A-Za-z0-9_-]{64}$/);
    expect(randomString()).not.toBe(a);
  });
});
