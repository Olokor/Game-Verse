/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** GlitchTip DSN for the web project (`make errors-setup`). Empty = error reporting off. ADR-0005. */
  readonly VITE_ERROR_DSN?: string;
  /** Environment tag on reported errors; defaults to development or production by build mode. */
  readonly VITE_ERROR_ENVIRONMENT?: string;
  /** Base URL of GameVerse.Api (default http://localhost:8080, the `make up` API). ADR-0006. */
  readonly VITE_API_URL?: string;
}

/** Short git commit of the build (vite.config.ts `define`), used as the error-reporting release. Empty outside git. */
declare const __GV_RELEASE__: string;
