import type { AuthUser, UserRole } from '../types/auth';

const ROLES: UserRole[] = ['admin', 'seller', 'buyer'];

/** Older saved sessions had role "user" (or none): they are buyers until the server says otherwise. */
export function normalizeRole(role?: string | null): UserRole {
  const r = (role ?? '').toLowerCase();
  return (ROLES as string[]).includes(r) ? (r as UserRole) : 'buyer';
}

/** Sell, My Listings, Sales and accepting bids are for sellers only (the backend enforces the same). */
export const isSeller = (user?: Pick<AuthUser, 'role'> | null) => !!user && normalizeRole(user.role) === 'seller';

export const roleLabel = (role?: string | null) => ({ buyer: 'Buyer', seller: 'Seller', admin: 'Admin' })[normalizeRole(role)];

const B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

/** base64url → text (no atob dependency; ASCII / UTF-8 JSON payloads). */
function decodeBase64Url(input: string): string {
  const s = input.replace(/-/g, '+').replace(/_/g, '/');
  const bytes: number[] = [];
  let buffer = 0;
  let bits = 0;
  for (const ch of s) {
    if (ch === '=') break;
    const v = B64.indexOf(ch);
    if (v < 0) continue;
    // Plain arithmetic instead of bit shifts: 6 bits in, whole bytes out.
    buffer = buffer * 64 + v;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      const unit = 2 ** bits;
      bytes.push(Math.floor(buffer / unit) % 256);
      buffer %= unit;
    }
  }
  try {
    return decodeURIComponent(bytes.map(b => `%${b.toString(16).padStart(2, '0')}`).join(''));
  } catch {
    return String.fromCharCode(...bytes);
  }
}

/** The "role" claim inside an access token (no verification needed: the app only uses it to choose screens). */
export function roleFromAccessToken(accessToken?: string | null): UserRole | null {
  const payload = accessToken?.split('.')[1];
  if (!payload) return null;
  try {
    const claims = JSON.parse(decodeBase64Url(payload)) as { role?: string | string[] };
    const role = Array.isArray(claims.role) ? claims.role[0] : claims.role;
    return role ? normalizeRole(role) : null;
  } catch {
    return null;
  }
}
