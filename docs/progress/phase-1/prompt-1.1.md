# Prompt 1.1: Accounts and auth

- **Status:** done.
- **Commits:** `f9aa605`, `060f52e`, `27b051b`, plus `4c3b008` (infra fix).
- **ADR:** [ADR-0006](../../architecture/adr-0006-accounts-and-auth.md)

## What was built
- **Guest accounts:** one tap, bound to the device by a secret.
- **Tokens:** ES256 access tokens (15 minutes) and rotating refresh tokens, stored hashed.
- **Email upgrade:** the guest keeps the same account id. Verification codes and password reset are mailed over SMTP (Mailpit in development).
- **Profiles:** a display name and an avatar.
- **Account deletion** inside the app.
- **Web:** a typed API client generated from OpenAPI.

## Owner decisions
- Gmail SMTP for now. The app password lives only in `.env`, and the owner was advised to rotate it.
- Birth year is collected at upgrade.
- Account deletion is included.

## Verification
- `make test` green.
- Live checks against `make up`, with Mailpit receiving the codes.

## Open items
- **Deploy:** the production signing key, the trusted proxy IPs, an email provider, and the CORS origin.
- **Unity secure storage:** done in Prompt 1.6, part A.
