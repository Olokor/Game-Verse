import { afterEach, describe, expect, it, vi } from 'vitest';
import * as Sentry from './sentryClient';
import { initErrorReporting, isErrorReportingEnabled, reportError, scrubBreadcrumb, scrubEvent, stripQuery } from './errorReporting';

vi.mock('./sentryClient', () => ({ init: vi.fn(), captureException: vi.fn(() => 'evt-1234567890') }));

afterEach(async () => {
  vi.clearAllMocks();
  await initErrorReporting({ environment: 'test' }); // back to "off"
});

describe('initErrorReporting', () => {
  it('stays off without a DSN and never loads the SDK', async () => {
    expect(await initErrorReporting({ environment: 'test' })).toBe(false);
    expect(isErrorReportingEnabled()).toBe(false);
    expect(Sentry.init).not.toHaveBeenCalled();
  });

  it('initialises with a DSN and turns every personal-data category off', async () => {
    expect(await initErrorReporting({ dsn: 'http://key@127.0.0.1:8000/2', environment: 'test', release: 'abc123' })).toBe(true);
    expect(Sentry.init).toHaveBeenCalledWith(
      expect.objectContaining({
        dsn: 'http://key@127.0.0.1:8000/2',
        environment: 'test',
        release: 'abc123',
        tracesSampleRate: 0,
        dataCollection: { userInfo: false, cookies: false, httpHeaders: false, httpBodies: [], urlQueryParams: false },
      }),
    );
  });
});

describe('reportError', () => {
  it('logs to the console when reporting is off, so errors are never silently lost', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(reportError(new Error('x'))).toBeUndefined();
    expect(consoleError).toHaveBeenCalled();
    expect(Sentry.captureException).not.toHaveBeenCalled();
    consoleError.mockRestore();
  });

  it('captures through the SDK and returns the event id when reporting is on', async () => {
    await initErrorReporting({ dsn: 'http://key@127.0.0.1:8000/2', environment: 'test' });
    const error = new Error('boom');
    expect(reportError(error, { source: 'test' })).toBe('evt-1234567890');
    expect(Sentry.captureException).toHaveBeenCalledWith(error, { tags: { source: 'test' } });
  });
});

describe('lazy loading', () => {
  it('queues errors reported while the SDK loads and sends them once it is ready', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    const starting = initErrorReporting({ dsn: 'http://key@127.0.0.1:8000/2', environment: 'test' });
    const early = new Error('early');
    expect(reportError(early, { source: 'test' })).toBeUndefined(); // still loading: queued, not logged as lost
    expect(consoleError).not.toHaveBeenCalled();
    await starting;
    expect(Sentry.captureException).toHaveBeenCalledWith(early, { tags: { source: 'test' } });
    consoleError.mockRestore();
  });

  it('captures uncaught window errors that happen before the SDK is ready', async () => {
    const starting = initErrorReporting({ dsn: 'http://key@127.0.0.1:8000/2', environment: 'test' });
    const uncaught = new Error('uncaught-early');
    window.dispatchEvent(new ErrorEvent('error', { error: uncaught, message: 'uncaught-early' }));
    await starting;
    expect(Sentry.captureException).toHaveBeenCalledWith(uncaught, { tags: { source: 'window-early' } });
  });
});

describe('scrubbing', () => {
  it('strips query strings and fragments', () => {
    expect(stripQuery('https://gameverse.app/ws/play/s1?ticket=secret#x')).toBe('https://gameverse.app/ws/play/s1');
    expect(stripQuery('/games/ludo')).toBe('/games/ludo');
  });

  it('removes headers, cookies, query strings and user from events', () => {
    const event = scrubEvent({
      type: undefined,
      request: { url: 'https://gameverse.app/r/ABC123?ticket=tkt-secret', headers: { 'User-Agent': 'x' }, cookies: { s: '1' }, query_string: 'ticket=tkt-secret' },
      user: { id: 'u1', ip_address: '1.2.3.4' },
      breadcrumbs: [{ category: 'navigation', data: { from: '/a?ticket=1', to: '/b?code=2' } }],
    });
    const json = JSON.stringify(event);
    expect(json).not.toContain('tkt-secret');
    expect(json).not.toContain('User-Agent');
    expect(json).not.toContain('1.2.3.4');
    expect(event.request?.url).toBe('https://gameverse.app/r/ABC123');
    expect(event.breadcrumbs?.[0]?.data).toEqual({ from: '/a', to: '/b' });
  });

  it('cleans fetch breadcrumb URLs', () => {
    expect(scrubBreadcrumb({ category: 'fetch', data: { url: '/api/x?token=1', method: 'GET' } }).data).toEqual({ url: '/api/x', method: 'GET' });
  });
});
