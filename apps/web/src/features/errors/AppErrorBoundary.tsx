import { Component, type ErrorInfo, type ReactNode } from 'react';
import { reportError } from '../../lib/errorReporting';
import { ErrorScreen } from './ErrorScreen';

interface State {
  failed: boolean;
  reference?: string;
}

/**
 * Top-level error boundary, around the router and providers. Route render errors are handled closer by the root
 * route's errorElement (RouteErrorScreen); this catches anything outside it. React only supports error boundaries as
 * class components (no hook equivalent), hence the class.
 */
export class AppErrorBoundary extends Component<{ children: ReactNode }, State> {
  override state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    const reference = reportError(error, { source: 'app-boundary', componentStack: (info.componentStack ?? '').slice(0, 500) });
    this.setState({ reference });
  }

  override render() {
    return this.state.failed ? <ErrorScreen reference={this.state.reference} /> : this.props.children;
  }
}
