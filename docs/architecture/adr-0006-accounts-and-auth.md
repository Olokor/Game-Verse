# ADR-0006: Accounts and auth

- **Status:** Accepted (Prompt 1.1 complete)
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

### Upgrading a guest to an email account
- `POST /auth/upgrade {email, password, birthYear}`, for a signed-in guest only, converts **the same account id**. Anything a guest earned stays attached by construction.
- It runs in one transaction: password, email, birth year and the guest flag change together.
- Afterwards:
  - the device secret is deleted;
  - the guest sessions are revoked;
  - a new session is returned;
  - a verification code is mailed.
- Email addresses are unique case-insensitively, enforced by a filtered unique index on `normalized_email`, so racing upgrades can't share one.
- A taken address returns 409, and a non-guest returns 403.

### Email codes
- Verification and password reset use **6-digit codes**, not links: easier to type on a phone, and they need no web page.
- Each code is stored as a salted SHA-256 hash, valid **15 minutes**, and burned after **5 wrong attempts**. A new code replaces the old one.
- Reset:
  - `POST /auth/password/forgot` always answers 202, so the response never reveals whether an address has an account;
  - `POST /auth/password/reset` sets the password, clears lockout, marks the email verified (the player proved they read it), and **revokes every session**.
- Email sign-in (`POST /auth/login`) gives the same 401 for an unknown email, a guest, a wrong password and a lockout.

### Sending email
- **Owner decision:** SMTP now. Development uses the owner's own mailbox if wanted; a transactional provider is chosen before deploy.
- The sender is `IEmailSender` (Platform), implemented with MailKit (MIT) in Infrastructure and configured by `GameVerse__Email__{Host,Port,Security,Username,Password,From}`.
- **Development default:** Mailpit (MIT), which catches every message (UI at http://127.0.0.1:8025). The integration tests run a Mailpit container and read the codes from its API.
- **Personal Gmail:** `smtp.gmail.com:587`, StartTls, with an app password (`.env.example`).
- **No host configured:** messages are dropped with a warning, and request bodies are never logged (they carry codes).
- **A failed send never fails the request** (the player can ask again). It is logged at Error, which reaches GlitchTip.
- Emails are plain text plus minimal HTML, with no tracking and no external content.

### Profiles
- The **Profiles** module owns the display name and avatar. Accounts creates the default profile when a guest is created: "Player" plus 4 digits, with `avatar_01`.
- Accounts talks to Profiles only through `IProfileService`, and the profile table has no foreign key into `accounts`. Modules don't couple through each other's tables.
- `GET /profiles/me` and `PATCH /profiles/me`. Fields left null stay unchanged.
- **Display names:** 3 to 20 letters, digits, single spaces or underscores, with no leading or trailing space. A profanity filter comes with the Moderation module.
- **Avatar ids:** a fixed list in `GameVerse.Contracts` (`ProfileAvatars.All`), so Unity and the web offer the same choices.

### Account deletion
- **Owner decision:** in-app deletion, a Google Play requirement.
- `POST /accounts/me/delete {password?}`. A POST, because Unity's `DELETE` requests can't carry a body. Email accounts confirm with their password; guests just need their session.
- **The account row stays, anonymised**, so ledger entries and match history keep their references (CLAUDE.md section 7):
  - email, username, password, birth year and phone are cleared;
  - device secrets and email codes are deleted;
  - every session is revoked;
  - the profile becomes "Deleted player".
- The email address is free for a new account straight away.

### Web client and CORS
- The browser app calls the API from its own origin with credentials, so the API allows exactly the origins in `GameVerse__CorsOrigins` (never `*`): `http://localhost:5173` and `:4173` in development.
- In production, the web app and API on the same site (`gameverse.app` and `api.gameverse.app`) keep the `SameSite=Strict` refresh cookie working.
- `apps/web/src/lib/api.ts` is a typed `openapi-fetch` client generated from the OpenAPI document (`make schema`). It:
  - sends `X-GameVerse-Client: web`;
  - keeps the access token in memory only;
  - on a 401, refreshes once (one shared refresh for parallel requests) and retries;
  - signs the player out if the refresh fails.

### Passwords
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
- **Before deploy:** choose a transactional email provider (cost, deliverability). Gmail suits development only, because of its daily send limits.
- **Deploy TODOs:** a production signing key in the secret store; trusted-proxy client IPs for rate limits; the transactional email provider; `GameVerse__CorsOrigins` set to the production web origin.
