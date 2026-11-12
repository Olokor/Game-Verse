# ADR-0006: Accounts and auth

- **Status:** Accepted (increment 1 of Prompt 1.1; email and profiles sections are added as they land)
- **Date:** 2026-11-12
- **Deciders:** Wisdom (owner: email, age, deletion), Claude Code (tech lead: everything technical below)

## Context

Prompt 1.1 builds accounts for every game. CLAUDE.md section 3 fixes the outline:
- ASP.NET Core Identity;
- 15-minute JWT access tokens;
- rotating refresh tokens stored hashed;
- guest accounts that can be upgraded.

Players are mostly on Android phones with unreliable mobile data, and many will never type an email. So a guest must be a first-class account from the first tap.

## Decisions

### Module layout
- The **Accounts** module (`GameVerse.Platform/Accounts`) owns users, guest devices and sessions. Other modules ask through `IAccountDirectory` (`IsMinorAsync`, `GetSummaryAsync`).
- Request and response types live in `GameVerse.Contracts/Accounts`, so the Unity client uses the same classes.
- Identity's tables live in the `accounts` schema of the shared database.
- `AccountUser` is public only because the shared `IdentityDbContext` needs it. An architecture test stops other modules from using it.

### Guests are device-bound
- `POST /auth/guest` creates the account and returns a **device secret** once: `gvd_` plus 32 random bytes. The server stores only its SHA-256 hash.
- The device signs back in with `POST /auth/guest/login {guestId, deviceSecret}`.
- Unity keeps the secret in secure storage (Prompt 1.6).
- A fast hash is right here, because the secret is random, not a password.

### Access tokens: ES256 JWT, 15 minutes
- Claims: `sub` (account id), `guest`, `jti`.
- Asymmetric, so a later realtime game server can verify tokens with the public key alone.
- The key is an ECDSA P-256 PEM in `GameVerse__Auth__SigningKey`.
- **Development without a key** generates an ephemeral one (tokens die on restart, with a warning). **Every other environment fails at startup** without a key.

### Refresh tokens rotate, and reuse revokes the session
- Each token is `gvr_` plus 32 random bytes, stored as a SHA-256 hash, valid 30 days.
- Tokens from one sign-in form a **family**. Each refresh marks the presented token used and issues the next one.
- A conditional update stops two racing refreshes from both succeeding.
- **Reuse detection:** presenting a token that was already used means a copy exists elsewhere. The whole family is revoked, the request gets 401, and `GV_AUTH refresh_reuse` is logged at Error, which reaches GlitchTip (ADR-0005).
- **Retry grace (15 s):** on a flaky connection the phone may never receive the refresh response and retry with the same token. Inside the window the server retires the unseen successor and issues another, unless that successor was already used, in which case it's reuse.
  - The trade-off: a thief replaying a token within 15 seconds of the real client is not caught by this path, but the next real refresh then is, because the families fork.
  - Strict detection without the grace would regularly log players out on mobile data.

### Token delivery
- Access tokens are always in the JSON body.
- **Web** (`X-GameVerse-Client: web`) gets the refresh token as an `httpOnly; Secure; SameSite=Strict; Path=/auth` cookie, which page scripts can't read. Unity and other clients get it in the body.

### Passwords (for upgraded accounts, increment 2)
- Minimum 10 characters with no composition rules (NIST 800-63B).
- Identity lockout after 5 failures, for 15 minutes.

### Age
- **Owner decision:** collect only the birth year, at upgrade.
- Guests, and anyone without a birth year, count as minors.
- With only a year, someone born in (current year - 18) may still be 17, so they count as minors until the next year (`AgeRule`). The stricter setting wins.

### Abuse limits
Per-IP fixed windows, answering 429 as problem+json:
- sign-in style calls: 10 per minute;
- account creation: 5 per minute;
- email-sending calls: 3 per 15 minutes.

Limits are configurable (`GameVerse__Auth__RateLimits__*`). Behind Traefik and Cloudflare the client IP must come from trusted proxy headers. That's a deploy TODO.

### Errors
- Invalid credentials always produce the same 401 ("Sign-in failed"), whether the account exists or not.
- Malformed requests get 400 `ValidationProblem` with field errors (FluentValidation).

## Consequences

- Access tokens can't be revoked before they expire (15 minutes). Logout and reuse detection revoke refresh tokens, so a stolen access token is useful for at most 15 minutes.
- A future game server only needs the public key.
- Increment 2 adds email upgrade, verification, password reset and the SMTP sender (Mailpit in development, the owner's mailbox optional, a production provider before deploy).
- Increment 3 adds profiles and account deletion.
