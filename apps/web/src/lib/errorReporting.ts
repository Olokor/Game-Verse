import type { Breadcrumb, BrowserOptions } from '@sentry/react';

/**
 * Error reporting (ADR-0005): the Sentry SDK, sending to self-hosted GlitchTip. The only module that touches Sentry;
 * everything else calls reportError(). Off unless VITE_ERROR_DSN is set; while off, reportError() logs to the console
 * instead. No personal data: no user info or IPs, no request headers or cookies, no query strings (WebSocket tickets
 * travel as ?ticket=..., CLAUDE.md section 4.5).
 *
 * The SDK (about 32 KB gzipped) is loaded lazily and only when a DSN is set, so it never sits on the first-load path.
 * Errors that happen before it arrives, including uncaught ones, are queued and sent once it is ready.
 */

export interface ErrorReportingConfig {
  dsn?: string;
  environment: string;
  release?: string;
}

type Sdk = typeof import('./sentryClient');
type Pending = { error: unknown; context?: Record<string, string> };

let configured = false;
let sdk: Sdk | undefined;
let pending: Pending[] = [];

export function configFromEnv(): ErrorReportingConfig {
  return {
    dsn: import.meta.env.VITE_ERROR_DSN || undefined,
    environment: import.meta.env.VITE_ERROR_ENVIRONMENT || (import.meta.env.DEV ? 'development' : 'production'),
    release: __GV_RELEASE__ || undefined,
  };
}

/** Uncaught errors before the SDK loads would otherwise be missed (its own handlers attach on init). */
function listenEarly() {
  const onError = (e: ErrorEvent) => pending.push({ error: e.error ?? e.message, context: { source: 'window-early' } });
  const onRejection = (e: PromiseRejectionEvent) => pending.push({ error: e.reason, context: { source: 'unhandledrejection-early' } });
  window.addEventListener('error', onError);
  window.addEventListener('unhandledrejection', onRejection);
  return () => {
    window.removeEventListener('error', onError);
    window.removeEventListener('unhandledrejection', onRejection);
  };
}

/**
 * Starts reporting when a DSN is configured; resolves to whether reporting is on. Call once at startup without
 * awaiting it: rendering does not wait for the SDK.
 */
export async function initErrorReporting(config: ErrorReportingConfig = configFromEnv()): Promise<boolean> {
  configured = Boolean(config.dsn);
  sdk = undefined;
  if (!config.dsn) {
    pending = [];
    return false;
  }

  const detachEarlyListeners = listenEarly();
  try {
    const loaded = await import('./sentryClient');
    const options: BrowserOptions = {
      dsn: config.dsn,
      environment: config.environment,
      release: config.release,
      // Collect nothing personal (SDK v11 replaced sendDefaultPii with per-category switches). scrubEvent is the
      // second layer in case an integration attaches data anyway.
      dataCollection: { userInfo: false, cookies: false, httpHeaders: false, httpBodies: [], urlQueryParams: false },
      tracesSampleRate: 0,
      beforeSend: (event) => scrubEvent(event),
      beforeBreadcrumb: (breadcrumb) => scrubBreadcrumb(breadcrumb),
    };
    loaded.init(options);
    sdk = loaded;
  } catch (error) {
    // Reporting must never break the app (an ad-blocker or a flaky network can block the chunk).
    configured = false;
    console.warn('[GameVerse] error reporting unavailable', error);
  } finally {
    detachEarlyListeners();
  }

  const queued = pending;
  pending = [];
  for (const { error, context } of queued) reportError(error, context);
  return sdk !== undefined;
}

export function isErrorReportingEnabled() {
  return sdk !== undefined;
}

/**
 * Reports an error. Returns the event id (shown to the player as a reference code) once the SDK is ready. While it is
 * loading the error is queued; when reporting is off it goes to the console, so an error is never silently lost.
 */
export function reportError(error: unknown, context?: Record<string, string>): string | undefined {
  if (sdk) return sdk.captureException(error, context ? { tags: context } : undefined);
  if (configured) {
    pending.push({ error, context });
    return undefined;
  }
  console.error('[GameVerse] unreported error', error, context ?? '');
  return undefined;
}

/** Root options for createRoot: errors no boundary caught (the boundaries report their own). */
export const rootErrorOptions = {
  onUncaughtError: (error: unknown) => {
    reportError(error, { source: 'react-uncaught' });
  },
  onRecoverableError: (error: unknown) => {
    console.warn('[GameVerse] recoverable React error', error);
  },
};

/** Removes the query string and fragment from a URL. Exported for tests. */
export function stripQuery(url: string): string {
  const cut = url.search(/[?#]/);
  return cut === -1 ? url : url.slice(0, cut);
}

type ScrubbableEvent = Parameters<NonNullable<BrowserOptions['beforeSend']>>[0];

/** Last line of defence before an event leaves the browser. Exported for tests. */
export function scrubEvent<T extends ScrubbableEvent>(event: T): T {
  if (event.request) {
    delete event.request.headers;
    delete event.request.cookies;
    delete event.request.query_string;
    if (event.request.url) event.request.url = stripQuery(event.request.url);
  }
  delete event.user;
  event.breadcrumbs = event.breadcrumbs?.map(scrubBreadcrumb);
  return event;
}

/** Navigation and fetch breadcrumbs keep the path, never the query string. Exported for tests. */
export function scrubBreadcrumb(breadcrumb: Breadcrumb): Breadcrumb {
  const data = breadcrumb.data;
  if (!data) return breadcrumb;
  const clean = { ...data };
  for (const key of ['url', 'from', 'to']) {
    if (typeof clean[key] === 'string') clean[key] = stripQuery(clean[key] as string);
  }
  return { ...breadcrumb, data: clean };
}
