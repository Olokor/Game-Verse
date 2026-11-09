import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { cn } from '../../lib/cn';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

interface StyleOptions {
  variant?: ButtonVariant;
  size?: ButtonSize;
  pill?: boolean;
  fullWidth?: boolean;
}

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-brand-volt text-brand-volt-ink hover:brightness-110',
  secondary: 'bg-bg-surface text-text-primary border border-border-subtle hover:bg-bg-elevated hover:border-border-strong',
  ghost: 'bg-transparent text-text-secondary hover:bg-bg-surface hover:text-text-primary',
  danger: 'bg-state-danger text-brand-volt-ink hover:brightness-110',
};

// Every size meets the 44px touch target (DESIGN_SYSTEM.md section 10).
const sizes: Record<ButtonSize, string> = {
  sm: 'h-touch-target px-16 gap-8 text-body-sm',
  md: 'h-56 px-24 gap-8 text-body',
  lg: 'h-72 px-32 gap-12 text-title-sm',
};

// Disabled keeps each variant's shape (ghost stays transparent) and mutes it.
const disabledStyles: Record<ButtonVariant, string> = {
  primary: 'disabled:bg-bg-elevated disabled:text-text-muted disabled:brightness-100',
  secondary: 'disabled:bg-bg-surface disabled:text-text-muted disabled:border-border-subtle',
  ghost: 'disabled:bg-transparent disabled:text-text-muted',
  danger: 'disabled:bg-bg-elevated disabled:text-text-muted disabled:brightness-100',
};

/**
 * Button classes, for elements that look like a button but are not one (e.g. the "Play" label inside a card link,
 * where a nested button would be invalid HTML).
 */
export function buttonClasses({ variant = 'primary', size = 'md', pill = false, fullWidth = false }: StyleOptions = {}) {
  return cn(
    'pressable inline-flex shrink-0 select-none items-center justify-center font-bold whitespace-nowrap',
    pill ? 'rounded-pill' : 'rounded-md',
    variants[variant],
    sizes[size],
    fullWidth && 'w-full',
  );
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement>, StyleOptions {
  /** Shows loading dots, keeps the width, and blocks clicks. */
  loading?: boolean;
  /** Icon before the label. */
  icon?: ReactNode;
}

/**
 * Primary (volt), secondary, ghost and danger. States: default, hover, pressed (scale), focus-visible (volt ring
 * from the base layer), disabled, loading.
 */
export function Button({
  variant = 'primary', size = 'md', pill, fullWidth, loading = false, icon, disabled, className, children, type = 'button', onClick, ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={cn(buttonClasses({ variant, size, pill, fullWidth }), 'disabled:cursor-not-allowed', disabledStyles[variant], loading && 'relative cursor-progress', className)}
      disabled={disabled}
      aria-busy={loading || undefined}
      aria-disabled={loading || undefined}
      onClick={(e) => {
        // Blocks both onClick and form submission while loading (the button stays focusable, unlike `disabled`).
        if (loading) {
          e.preventDefault();
          return;
        }
        onClick?.(e);
      }}
      {...rest}
    >
      <span className={cn('inline-flex items-center gap-8', loading && 'invisible')}>
        {icon}
        {children}
      </span>
      {loading && (
        <span className="absolute inset-0 flex items-center justify-center gap-4" aria-hidden="true">
          <span className="loading-dot size-8 rounded-pill bg-current" />
          <span className="loading-dot size-8 rounded-pill bg-current [animation-delay:calc(var(--gv-motion-instant-duration)*2)]" />
          <span className="loading-dot size-8 rounded-pill bg-current [animation-delay:calc(var(--gv-motion-instant-duration)*4)]" />
        </span>
      )}
    </button>
  );
}
