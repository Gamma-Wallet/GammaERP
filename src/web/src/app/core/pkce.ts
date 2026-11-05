// The code verifier, its S256 challenge and the state of a sign-in (OAuth 2.0 PKCE, RFC 7636), in the browser's own crypto.

export function base64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** A random string of the given number of bytes, base64url (48 bytes give a 64-character verifier). */
export function randomString(bytes = 48): string {
  return base64Url(crypto.getRandomValues(new Uint8Array(bytes)));
}

/** The S256 challenge of a verifier: base64url(SHA-256(verifier)). */
export async function challengeOf(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  return base64Url(new Uint8Array(digest));
}
