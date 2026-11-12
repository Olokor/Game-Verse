import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import { QueryClient } from '@tanstack/react-query';
import { Providers } from '../app/providers';
import { routes } from '../app/routes';

export function testQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
}

/** Renders the real app routes at a path. */
export function renderApp(path = '/', queryClient = testQueryClient()) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return { ...render(<Providers queryClient={queryClient}><RouterProvider router={router} /></Providers>), router, queryClient };
}

/** Renders a single element inside providers and a router (for components that use Link). */
export function renderUi(ui: ReactElement) {
  const route: RouteObject[] = [{ path: '*', element: ui }];
  const router = createMemoryRouter(route, { initialEntries: ['/'] });
  return render(<Providers queryClient={testQueryClient()}><RouterProvider router={router} /></Providers>);
}
