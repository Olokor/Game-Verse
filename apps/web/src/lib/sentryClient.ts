/**
 * The only parts of the Sentry SDK the app uses, as named imports so the bundler can drop the rest. errorReporting.ts
 * loads this module lazily: importing '@sentry/react' itself dynamically would ship the whole SDK (measured: 152 KB
 * gzipped instead of about 32 KB).
 */
export { captureException, init } from '@sentry/react';
