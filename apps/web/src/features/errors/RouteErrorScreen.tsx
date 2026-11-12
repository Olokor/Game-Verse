import { useEffect, useRef, useState } from 'react';
import { useRouteError } from 'react-router';
import { reportError } from '../../lib/errorReporting';
import { ErrorScreen } from './ErrorScreen';

/** Root route errorElement: reports the render or loader error once and shows the error screen. */
export function RouteErrorScreen() {
  const error = useRouteError();
  const [reference, setReference] = useState<string>();
  const reported = useRef<unknown>(undefined);

  useEffect(() => {
    if (reported.current === error) return; // StrictMode runs effects twice in development
    reported.current = error;
    setReference(reportError(error, { source: 'route' }));
  }, [error]);

  return <ErrorScreen reference={reference} />;
}
