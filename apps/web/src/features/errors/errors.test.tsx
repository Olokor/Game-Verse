import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { AppErrorBoundary } from './AppErrorBoundary';
import { RouteErrorScreen } from './RouteErrorScreen';
import { reportError } from '../../lib/errorReporting';

vi.mock('../../lib/errorReporting', () => ({ reportError: vi.fn(() => 'abcdef1234567890') }));

function Bomb(): never {
  throw new Error('kaboom');
}

describe('error handling UI', () => {
  it('the app boundary shows the error screen with a reference and reports once', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {}); // React logs caught errors
    render(
      <AppErrorBoundary>
        <Bomb />
      </AppErrorBoundary>,
    );
    expect(await screen.findByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument();
    expect(await screen.findByText('abcdef12')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Back to Home' })).toBeEnabled();
    expect(reportError).toHaveBeenCalledTimes(1);
    expect(vi.mocked(reportError).mock.calls[0]?.[1]).toMatchObject({ source: 'app-boundary' });
  });

  it('a route render error shows the error screen and reports it', async () => {
    vi.mocked(reportError).mockClear();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const router = createMemoryRouter([{ path: '/', element: <Bomb />, errorElement: <RouteErrorScreen /> }]);
    render(<RouterProvider router={router} />);
    expect(await screen.findByRole('heading', { name: 'Something went wrong' })).toBeInTheDocument();
    expect(reportError).toHaveBeenCalledTimes(1);
    expect(vi.mocked(reportError).mock.calls[0]?.[1]).toEqual({ source: 'route' });
  });
});
