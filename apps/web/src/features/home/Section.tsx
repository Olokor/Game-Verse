import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { cn } from '../../lib/cn';

/** Section with a display-type heading and an optional "See all" style link. */
export function Section({ title, aside, action, className, children, id }: {
  title: string;
  aside?: ReactNode;
  action?: { label: string; to: string };
  className?: string;
  children: ReactNode;
  id?: string;
}) {
  const headingId = id ? `${id}-heading` : undefined;
  return (
    <section className={cn('flex min-w-0 flex-col gap-16', className)} aria-labelledby={headingId}>
      <div className="flex items-end justify-between gap-12">
        <div className="flex min-w-0 flex-col gap-4">
          <h2 id={headingId} className="type-display text-title font-bold">
            {title}
          </h2>
          {aside}
        </div>
        {action && (
          <Link
            to={action.to}
            className="pressable -mr-8 flex min-h-touch-target shrink-0 items-center rounded-sm px-8 text-body-sm font-bold text-text-secondary hover:text-text-primary"
          >
            {action.label}
          </Link>
        )}
      </div>
      {children}
    </section>
  );
}

/** Designed empty or error state inside a section. */
export function SectionMessage({ icon, title, body, action }: { icon: ReactNode; title: string; body: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-12 rounded-lg border border-dashed border-border-strong bg-bg-surface p-24">
      <span className="flex size-40 items-center justify-center rounded-md bg-bg-elevated text-text-secondary" aria-hidden="true">
        {icon}
      </span>
      <div className="flex flex-col gap-4">
        <p className="text-body font-bold text-text-primary">{title}</p>
        <p className="text-body-sm text-text-secondary">{body}</p>
      </div>
      {action}
    </div>
  );
}
