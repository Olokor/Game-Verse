import type { RouteObject } from 'react-router';
import { Compass, Gamepad2, Play, User, Users } from 'lucide-react';
import { AppShell } from '../components/shell/AppShell';
import { HomeScreen } from '../features/home/HomeScreen';
import { GamesPage } from '../features/games/GamesPage';
import { ComingSoon } from '../features/placeholder/ComingSoon';
import { RouteErrorScreen } from '../features/errors/RouteErrorScreen';

/** The component gallery ships in dev builds, and in builds made with VITE_DEV_ROUTES=true (Playwright screenshots). */
export const devRoutesEnabled = import.meta.env.DEV || import.meta.env.VITE_DEV_ROUTES === 'true';

export const routes: RouteObject[] = [
  {
    element: <AppShell />,
    errorElement: <RouteErrorScreen />,
    children: [
      { index: true, element: <HomeScreen /> },
      // TODO(gameverse): real screens. games/:gameId: lobby (1.7); play: matchmaking (1.5);
      // social: friends (2.3); profile: Phase 2. Player-facing copy says "Coming soon" without plan details.
      { path: 'games', element: <GamesPage /> },
      {
        path: 'games/:gameId',
        element: <ComingSoon icon={Gamepad2} title="Game lobby" body="Modes, house rules and a leaderboard peek for each game." />,
      },
      {
        path: 'play',
        element: <ComingSoon icon={Play} title="Play" body="Quick Match, Private Room, vs AI and Local, one tap away." />,
      },
      {
        path: 'social',
        element: <ComingSoon icon={Users} title="Social" body="Friends, invites and clubs." />,
      },
      {
        path: 'profile',
        element: <ComingSoon icon={User} title="Profile" body="Your level, ranks per game, stats and showcase." />,
      },
      ...(devRoutesEnabled
        ? [
            { path: 'dev/components', lazy: async () => ({ Component: (await import('../features/dev/ComponentsPage')).ComponentsPage }) },
            // Throws on render: shows the error screen and exercises reporting end to end (ADR-0005).
            { path: 'dev/crash', lazy: async () => ({ Component: (await import('../features/dev/CrashPage')).CrashPage }) },
          ]
        : []),
      {
        path: '*',
        element: <ComingSoon icon={Compass} title="Page not found" body="That link doesn't lead anywhere. Let's get you back to the games." label="Error 404" />,
      },
    ],
  },
];
