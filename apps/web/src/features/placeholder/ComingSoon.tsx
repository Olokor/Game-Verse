import type { LucideIcon } from 'lucide-react';
import { Link } from 'react-router';
import { buttonClasses } from '../../components/ui/Button';

/**
 * Designed empty state for screens that are not built yet, so navigation never dead-ends.
 * TODO(gameverse): each route gets its real screen in the prompt named on it.
 */
export function ComingSoon({ icon: Icon, title, body, label = 'Coming soon' }: { icon: LucideIcon; title: string; body: string; label?: string }) {
  return (
    <div className="mx-auto flex min-h-[60dvh] max-w-md flex-col items-center justify-center gap-16 text-center">
      <span className="flex size-72 items-center justify-center rounded-xl border border-border-subtle bg-bg-surface text-brand-volt" aria-hidden="true">
        <Icon className="size-32" />
      </span>
      <div className="flex flex-col gap-8">
        <h1 className="type-display text-headline font-bold">{title}</h1>
        <p className="text-body text-text-secondary">{body}</p>
        <p className="type-label text-text-secondary">{label}</p>
      </div>
      <Link to="/" className={buttonClasses({ variant: 'secondary', size: 'sm' })}>
        Back to Home
      </Link>
    </div>
  );
}
