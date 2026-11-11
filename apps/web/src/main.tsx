import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { Providers } from './app/providers';
import { routes } from './app/routes';
import { AppErrorBoundary } from './features/errors/AppErrorBoundary';
import { initErrorReporting, rootErrorOptions } from './lib/errorReporting';
import './styles/index.css';

// Starts before anything renders, without blocking it: the SDK loads lazily and queues early errors (ADR-0005).
// No-op without VITE_ERROR_DSN.
void initErrorReporting();

const router = createBrowserRouter(routes);

const root = document.getElementById('root');
if (!root) throw new Error('#root missing from index.html');

createRoot(root, rootErrorOptions).render(
  <StrictMode>
    <AppErrorBoundary>
      <Providers>
        <RouterProvider router={router} />
      </Providers>
    </AppErrorBoundary>
  </StrictMode>,
);
