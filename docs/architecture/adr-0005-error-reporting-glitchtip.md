# ADR-0005: Error reporting with the Sentry SDKs and self-hosted GlitchTip

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** Wisdom (owner), Claude Code (tech lead)
- **Amends:** CLAUDE.md section 3 ("Sentry (env-gated)")

## Context

During the Prompt 0.7 phone tests, a dropped connection left the `/spike` page hanging with no signal to anyone. An audit then found that only the backend had logs (Serilog JSON to stdout). It had no global exception handler, and the web app and the Unity client reported nothing when they failed on a player's device. CLAUDE.md already called for Sentry (env-gated); this ADR decides where reports go and the rules every client follows. The work became Prompt 0.8, ahead of Phase 1, so auth and Ludo start with error reporting in place.

## Decision

1. **The official Sentry SDKs, reporting to self-hosted GlitchTip:**
   - Sentry.AspNetCore and Sentry.Serilog 6.12.1 on the API;
   - `@sentry/react` on the web;
   - the Sentry Unity SDK on mobile and Web builds.

   GlitchTip (MIT) accepts the Sentry protocol, runs as one container plus its own Postgres, and keeps player error data on our servers. That matches the owner's preference for open-source, self-hosted services. Moving to Sentry SaaS later is a DSN change.
2. **Off unless configured.** Each client reports only when its DSN is set (`GameVerse__ErrorReporting__Dsn`, `VITE_ERROR_DSN`, `UNITY_ERROR_DSN`). A malformed API DSN fails startup, so a typo can't silently switch reporting off.
3. **Privacy rules, enforced in code and tests:**
   - `SendDefaultPii = false`.
   - The API strips request headers, cookies, bodies and query strings in `BeforeSend`. WebSocket tickets travel as `?ticket=` (section 4.5).
   - No display names or emails in events. Once auth exists, the only user field is the internal player UUID.
   - Integration tests send a ticket, a cookie and a custom header and assert none of them reaches the event.
4. **One event per failure.** Unhandled API exceptions become RFC 7807 `application/problem+json` with a `traceId` and no exception text, in every environment. The exception is logged once at Error by the exception handler, and that log line is the reported event. Access-log lines for 5xx are Warning, so they never become a second event (a test asserts exactly one).
5. **Development setup:**
   - `make errors-up` starts GlitchTip 6.2.6 (compose profile `errors`, `127.0.0.1:8000`, Postgres-only, all-in-one).
   - `make errors-setup` creates the admin user, the `gameverse` org and the `api` / `web` / `unity` projects, and prints the DSNs.
   - GlitchTip 7.0.0 (released 2026-11-09) is adopted once it has settled.

## Consequences

- **Production follow-up (before launch):**
  - GlitchTip behind Traefik on its own host, with its own secret key and database in the secret store.
  - The Sentry `tunnel` option through our API (`/api/errors`), so browser ad-blockers don't drop reports and players' browsers only talk to our origin.
  - Retention and alert rules.
- **DSN handling:** client DSNs (web, Unity) contain only a public key and are safe to ship. The API DSN lives in the environment like other settings.
- **Not covered here:** OpenTelemetry tracing stays a separate TODO (CLAUDE.md section 3); error reporting is not tracing (`TracesSampleRate = 0`).
- **Verification:** an opt-in integration test (`Reaches_a_real_glitchtip_when_configured`) sends an event to a running GlitchTip and finds it through GlitchTip's API. It ran green on 2026-11-12 (issue API-1).
