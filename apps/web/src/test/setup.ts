import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { catalogFixture } from './catalogFixture';

afterEach(() => cleanup());

// The app talks to the API through fetch. By default tests see the launch catalog; anything else is a 404, so a test
// that forgets to stub a call fails loudly instead of reaching the network. Tests override with vi.stubGlobal.
beforeEach(() => {
  vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
    const url = new URL(input instanceof Request ? input.url : String(input));
    if (url.pathname === '/catalog/games') {
      return new Response(JSON.stringify(catalogFixture), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(JSON.stringify({ title: 'Not stubbed in this test' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } });
  });
});

// jsdom has no matchMedia; Motion's reduced-motion hook and responsive code query it.
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList;
}
