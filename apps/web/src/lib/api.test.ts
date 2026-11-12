import { describe, expect, it, vi } from 'vitest';
import { createApiClient } from './api';

const BASE = 'http://api.test';

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/** A fake API: /accounts/me needs "Bearer good"; /auth/refresh answers with the given status. */
function fakeApi(refreshStatus = 200) {
  const calls: Array<{ url: string; auth: string | null; client: string | null; credentials?: RequestCredentials }> = [];
  const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(input, init);
    const path = new URL(request.url).pathname;
    calls.push({ url: path, auth: request.headers.get('Authorization'), client: request.headers.get('X-GameVerse-Client'), credentials: init?.credentials ?? request.credentials });
    if (path === '/auth/refresh') return refreshStatus === 200 ? json(200, { accessToken: 'good' }) : json(refreshStatus, { title: 'Sign-in failed' });
    if (path === '/accounts/me') {
      return request.headers.get('Authorization') === 'Bearer good' ? json(200, { userId: 'u1', isGuest: true }) : json(401, { title: 'Unauthorized' });
    }
    return json(404, {});
  });
  return { calls, fetchImpl };
}

describe('api client', () => {
  it('identifies as the web client and sends the access token', async () => {
    const { calls, fetchImpl } = fakeApi();
    const api = createApiClient({ baseUrl: BASE, fetch: fetchImpl });
    api.setAccessToken('good');

    const { data } = await api.GET('/accounts/me');

    expect(data?.userId).toBe('u1');
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ client: 'web', auth: 'Bearer good' });
  });

  it('refreshes once on a 401 and retries the request with the new token', async () => {
    const { calls, fetchImpl } = fakeApi();
    const api = createApiClient({ baseUrl: BASE, fetch: fetchImpl });
    api.setAccessToken('expired');

    const { data, response } = await api.GET('/accounts/me');

    expect(response.status).toBe(200);
    expect(data?.userId).toBe('u1');
    expect(calls.map((c) => c.url)).toEqual(['/accounts/me', '/auth/refresh', '/accounts/me']);
    expect(calls[1]).toMatchObject({ client: 'web', credentials: 'include' }); // the refresh cookie must travel
    expect(api.accessToken).toBe('good');
  });

  it('shares one refresh between parallel requests', async () => {
    const { calls, fetchImpl } = fakeApi();
    const api = createApiClient({ baseUrl: BASE, fetch: fetchImpl });
    api.setAccessToken('expired');

    const results = await Promise.all([api.GET('/accounts/me'), api.GET('/accounts/me'), api.GET('/accounts/me')]);

    expect(results.every((r) => r.response.status === 200)).toBe(true);
    expect(calls.filter((c) => c.url === '/auth/refresh')).toHaveLength(1);
  });

  it('signs out when the refresh is rejected and returns the original 401', async () => {
    const { calls, fetchImpl } = fakeApi(401);
    const onSignedOut = vi.fn();
    const api = createApiClient({ baseUrl: BASE, fetch: fetchImpl, onSignedOut });
    api.setAccessToken('expired');

    const { response, error } = await api.GET('/accounts/me');

    expect(response.status).toBe(401);
    expect(error).toBeDefined();
    expect(onSignedOut).toHaveBeenCalledTimes(1);
    expect(api.accessToken).toBeUndefined();
    expect(calls.map((c) => c.url)).toEqual(['/accounts/me', '/auth/refresh']);
  });

  it('never tries to refresh around an auth call', async () => {
    const { calls, fetchImpl } = fakeApi();
    const api = createApiClient({ baseUrl: BASE, fetch: fetchImpl });

    const { response } = await api.POST('/auth/guest/login', { body: { guestId: 'x', deviceSecret: 'y' } });

    expect(response.status).toBe(404);
    expect(calls.map((c) => c.url)).toEqual(['/auth/guest/login']);
  });
});
