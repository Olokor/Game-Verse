import { TriangleAlert } from 'lucide-react';
import { Logo } from '../../components/shell/Logo';
import { Button } from '../../components/ui/Button';

/**
 * Full-page error state (DESIGN_SYSTEM: every screen has a designed error state). Shown when rendering fails anywhere:
 * the route errorElement and the top-level error boundary both use it. Stands alone, without the app shell, because
 * the shell itself may be what failed. Uses plain links/reloads, not the router, for the same reason.
 */
export function ErrorScreen({ reference }: { reference?: string }) {
  return (
    <main className="flex min-h-dvh flex-col items-center justify-center gap-24 bg-bg-base px-gutter-mobile text-center">
      <Logo />
      <span className="flex size-72 items-center justify-center rounded-xl border border-border-subtle bg-bg-surface text-state-danger" aria-hidden="true">
        <TriangleAlert className="size-32" />
      </span>
      <div className="flex max-w-md flex-col gap-8" role="alert">
        <h1 className="type-display text-headline font-bold">Something went wrong</h1>
        <p className="text-body text-text-secondary">
          The game hit an error it couldn&apos;t recover from. Reloading usually fixes it. Your coins and progress are safe on our servers.
        </p>
        {reference && (
          <p className="type-label text-text-muted">
            Reference <span className="text-text-secondary">{reference.slice(0, 8)}</span>
          </p>
        )}
      </div>
      <div className="flex flex-wrap items-center justify-center gap-12">
        <Button onClick={() => window.location.reload()}>Reload</Button>
        <Button variant="secondary" onClick={() => window.location.assign('/')}>
          Back to Home
        </Button>
      </div>
    </main>
  );
}
