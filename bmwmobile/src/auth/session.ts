import type { AuthSession, AuthTokens } from '../types/auth';

/**
 * The current sign-in session, outside React so the API client can read it.
 * AuthContext owns it: it sets the session and provides the refresh / sign-out handlers.
 */
let current: AuthSession | null = null;
let refreshing: Promise<AuthTokens | null> | null = null;
let refreshHandler: ((refreshToken: string) => Promise<AuthTokens | null>) | null = null;
let expiredHandler: (() => void) | null = null;

/** Offline demo data marks the viewer's own items with this name (see services/api.ts). */
export const DEMO_OWNER = 'you';

export const getSession = () => current;

export const setSession = (session: AuthSession | null) => {
  current = session;
};

/** The signed-in username, or the demo owner for guests. */
export const currentUsername = () => current?.user.username ?? DEMO_OWNER;

/** True when an auction / bid / purchase belongs to whoever is using the app. */
export const isCurrentUser = (name?: string | null) => !!name && name === currentUsername();

export const setTokenHandlers = (handlers: {
  refresh: ((refreshToken: string) => Promise<AuthTokens | null>) | null;
  expired: (() => void) | null;
}) => {
  refreshHandler = handlers.refresh;
  expiredHandler = handlers.expired;
};

/**
 * A usable access token, refreshing it first when it is about to expire.
 * Returns null for guests, or when the session can no longer be refreshed (the user is then signed out).
 */
export async function getValidAccessToken(forceRefresh = false): Promise<string | null> {
  if (!current) return null;
  const fresh = current.accessTokenExpiresAt - Date.now() > 60_000;
  if (fresh && !forceRefresh) return current.accessToken;
  if (!current.refreshToken || !refreshHandler) {
    expiredHandler?.();
    return null;
  }

  // One refresh at a time: refresh tokens are single-use.
  if (!refreshing) {
    const handler = refreshHandler;
    const token = current.refreshToken;
    refreshing = handler(token).finally(() => {
      refreshing = null;
    });
  }
  const tokens = await refreshing;
  if (!tokens) {
    expiredHandler?.();
    return null;
  }
  return tokens.accessToken;
}
