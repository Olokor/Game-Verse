import { useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { LazyMotion, MotionConfig } from 'motion/react';

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
    },
  });
}

const loadMotionFeatures = () => import('./motionFeatures').then((m) => m.default);

/**
 * Server state (TanStack Query) and motion settings. reducedMotion="user" follows the OS setting.
 * LazyMotion + `m` components keep Motion's engine out of the initial bundle; `strict` makes a stray
 * `motion.div` (which would pull it back in) throw in development.
 */
export function Providers({ children, queryClient }: { children: ReactNode; queryClient?: QueryClient }) {
  const [client] = useState(() => queryClient ?? createQueryClient());
  return (
    <QueryClientProvider client={client}>
      <LazyMotion features={loadMotionFeatures} strict>
        <MotionConfig reducedMotion="user">{children}</MotionConfig>
      </LazyMotion>
    </QueryClientProvider>
  );
}
