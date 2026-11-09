import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { m } from 'motion/react';
import { cn } from '../../lib/cn';
import { durationToken, easingToken } from '../../lib/motionTokens';
import { usePlayerStore } from '../../stores/usePlayerStore';
import { CurrencyPill } from '../ui/CurrencyPill';
import { XPBar } from '../ui/XPBar';
import { rankStyle } from '../ui/tokens';
import { Logo } from './Logo';
import { navItems } from './navItems';

/**
 * Platform shell. Desktop (lg+): 72px left rail. Phone and tablet: bottom tab bar with Play in the centre,
 * so the primary action sits in the bottom 40% of the screen.
 */
export function AppShell() {
  const { pathname } = useLocation();

  return (
    <div className="min-h-dvh lg:pl-72">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-8 focus:left-8 focus:z-50 focus:rounded-md focus:bg-bg-elevated focus:p-12"
      >
        Skip to content
      </a>
      <LeftRail />
      <TopBar />
      <main
        id="main"
        className="mx-auto w-full max-w-7xl px-gutter-mobile pt-16 pb-[calc(var(--gv-space-72)+var(--gv-space-32)+env(safe-area-inset-bottom))] sm:px-gutter-tablet lg:px-gutter-desktop lg:pt-24 lg:pb-56"
      >
        <m.div
          key={pathname}
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: durationToken('fast'), ease: easingToken('fast') }}
        >
          <Outlet />
        </m.div>
      </main>
      <BottomBar />
    </div>
  );
}

function TopBar() {
  const { coins, gems, level, xp, xpRequired, displayName, rank } = usePlayerStore();
  return (
    <header className="sticky top-0 z-30 border-b border-border-subtle bg-bg-base/95">
      <div className="mx-auto flex h-56 w-full max-w-7xl items-center gap-16 px-gutter-mobile sm:px-gutter-tablet lg:h-72 lg:px-gutter-desktop">
        <Link to="/" className="pressable flex min-h-touch-target items-center rounded-sm lg:hidden" aria-label="GameVerse home">
          <Logo />
        </Link>
        {/* Visibility lives on wrappers: a component's own display class would override `hidden`. */}
        <div className="hidden w-full max-w-xs lg:block">
          <XPBar level={level} xp={xp} xpRequired={xpRequired} />
        </div>
        <div className="ml-auto flex items-center gap-8">
          {/* TODO(gameverse): pills open the store once Economy lands (Prompt 2.2). */}
          <CurrencyPill currency="coin" amount={coins} />
          <span className="hidden sm:contents">
            <CurrencyPill currency="gem" amount={gems} />
          </span>
          <Link
            to="/profile"
            className="pressable ml-4 flex size-touch-target items-center justify-center rounded-pill"
            aria-label={`Profile, ${displayName}`}
          >
            <span className={`flex size-32 items-center justify-center rounded-pill bg-bg-elevated text-body-sm font-bold text-text-primary ring-2 ring-offset-2 ring-offset-bg-base ${rankStyle[rank].ring}`}>
              {displayName.slice(0, 1)}
            </span>
          </Link>
        </div>
      </div>
    </header>
  );
}

function LeftRail() {
  return (
    <nav
      aria-label="Main"
      className="fixed inset-y-0 left-0 z-40 hidden w-72 flex-col items-center gap-8 border-r border-border-subtle bg-bg-base py-16 lg:flex"
    >
      <Link to="/" className="pressable mb-16 flex size-touch-target items-center justify-center rounded-sm" aria-label="GameVerse home">
        <Logo wordmark={false} />
      </Link>
      {navItems.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end={item.to === '/'}
          className={({ isActive }) =>
            cn(
              'pressable flex w-56 flex-col items-center gap-4 rounded-md py-8 text-label font-bold',
              isActive
                ? 'bg-bg-surface text-brand-volt'
                : item.primary
                  ? 'border border-border-subtle bg-bg-surface text-brand-volt hover:border-border-strong'
                  : 'text-text-secondary hover:bg-bg-surface hover:text-text-primary',
            )
          }
        >
          <item.icon className={cn('size-24', item.primary && 'fill-current')} aria-hidden="true" />
          {item.label}
        </NavLink>
      ))}
    </nav>
  );
}

function BottomBar() {
  return (
    <nav
      aria-label="Main"
      className="fixed inset-x-0 bottom-0 z-40 border-t border-border-subtle bg-bg-surface pb-[env(safe-area-inset-bottom)] lg:hidden"
    >
      <ul className="mx-auto grid h-72 max-w-xl grid-cols-5 items-center px-8">
        {navItems.map((item) => (
          <li key={item.to} className="flex justify-center">
            <NavLink
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                cn(
                  'pressable flex min-h-touch-target min-w-touch-target flex-col items-center justify-center gap-4 text-label font-bold',
                  isActive ? 'text-brand-volt' : item.primary ? 'text-text-primary' : 'text-text-secondary hover:text-text-primary',
                )
              }
            >
              {({ isActive }) => (
                <>
                  {item.primary ? (
                    <span
                      className={cn(
                        '-mt-32 flex size-56 items-center justify-center rounded-pill bg-brand-volt text-brand-volt-ink ring-4',
                        isActive ? 'ring-border-strong' : 'ring-bg-base',
                      )}
                    >
                      <item.icon className="size-24 fill-current" aria-hidden="true" />
                    </span>
                  ) : (
                    <item.icon className="size-24" aria-hidden="true" />
                  )}
                  {item.label}
                </>
              )}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
