# Accounts module

Users, guest accounts (device-bound), upgrade to full accounts, JWT access and rotating refresh tokens.

Status: not started. See docs/PROMPTS.md for the prompt that builds it.

Module rules (CLAUDE.md 4.1): expose a small public interface (e.g. `IAccountsService`), keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
