import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from '@gameverse/api-schema';

/**
 * Typed client for GameVerse.Api, generated from its OpenAPI document (`make schema`). ADR-0006:
 * - identifies as the web client, so the refresh token travels only as an httpOnly cookie (scripts never see it);
 * - keeps the access token in memory only (never localStorage), and sends it as a Bearer token;
 * - on a 401, refreshes once (one shared refresh for parallel requests) and retries the request; if the refresh
 *   fails the player is signed out.
 */

export const apiBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:8080';

const CLIENT_HEADER = 'X-GameVerse-Client';

export interface ApiClientOptions {
  baseUrl?: string;
  fetch?: typeof globalThis.fetch;
  /** Called when the session can no longer be refreshed (expired, revoked or reused refresh token). */
  onSignedOut?: () => void;
}

export function createApiClient({ baseUrl = apiBaseUrl, fetch: fetchImpl = (input, init) => globalThis.fetch(input, init), onSignedOut }: ApiClientOptions = {}) {
  let accessToken: string | undefined;
  let refreshing: Promise<boolean> | undefined;
  const retryCopies = new WeakMap<Request, Request>();

  const refresh = (): Promise<boolean> => {
    refreshing ??= fetchImpl(`${baseUrl}/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', [CLIENT_HEADER]: 'web' },
      body: '{}',
    })
      .then(async (response) => {
        if (!response.ok) return false;
        const body = (await response.json()) as { accessToken?: string };
        accessToken = body.accessToken;
        return Boolean(accessToken);
      })
      .catch(() => false)
      .then((ok) => {
        if (!ok) {
          accessToken = undefined;
          onSignedOut?.();
        }
        return ok;
      })
      .finally(() => {
        refreshing = undefined;
      });
    return refreshing;
  };

  const auth: Middleware = {
    onRequest({ request }) {
      request.headers.set(CLIENT_HEADER, 'web');
      if (accessToken) request.headers.set('Authorization', `Bearer ${accessToken}`);
      retryCopies.set(request, request.clone()); // a body can only be read once; keep a copy for the retry
      return request;
    },
    async onResponse({ request, response }) {
      const isAuthCall = new URL(request.url).pathname.startsWith('/auth/');
      if (response.status !== 401 || isAuthCall) return response;
      const copy = retryCopies.get(request);
      if (!copy || !(await refresh())) return response;
      copy.headers.set('Authorization', `Bearer ${accessToken}`);
      return fetchImpl(copy);
    },
  };

  const client = createClient<paths>({ baseUrl, credentials: 'include', fetch: fetchImpl });
  client.use(auth);

  return {
    ...client,
    /** Stores the access token from a sign-in response (guest, login, upgrade). */
    setAccessToken(token: string | undefined) {
      accessToken = token;
    },
    get accessToken() {
      return accessToken;
    },
    refresh,
  };
}

/** The app-wide client. */
export const api = createApiClient();
