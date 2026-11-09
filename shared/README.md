# shared/

The code the server and the Unity client both run. CLAUDE.md section 3 ("Shared libraries") explains why this exists. In short, every rule is written once, and the server validates with exactly the code the client predicts with.

| Project | Target | What lives here |
|---|---|---|
| `GameVerse.Rules` | netstandard2.1, C# 9, **no dependencies** | Rules engines, AI, deterministic maths and physics, seeded PRNG, replay verifiers |
| `GameVerse.Contracts` | netstandard2.1, C# 9 | Wire types: SessionManifest, realtime envelopes, GameEvents |
| `GameVerse.Rules.Tests` | net10.0, xUnit v3, Shouldly | Unit tests, golden fixture runner, determinism guards |
| `rules-fixtures/` | JSON | Golden vectors and the agreed rulebook, one folder per game ([format](rules-fixtures/README.md)) |

## Build, test, deliver to Unity

```
make test     # dotnet test backend/GameVerse.slnx (covers these projects)
make rules    # Release build, copies DLL + XML docs into apps/mobile/Assets/GameVerse/Plugins/Shared/
```

The DLLs in the Unity folder are build output and are gitignored. Run `make rules` after any change here. `link.xml` in the same folder stops IL2CPP from stripping them.

## What's already here

- `Math.Fixed`: Q32.32 fixed-point in a `long`.
  - `+ - * /`, `Sqrt`, `Floor`, `Ceiling`, `Round`, `Clamp`.
  - Overflow throws. Rounding is to nearest, with halves away from zero.
  - `Parse("1.375")` for constants and fixtures. `ToDouble()` is for display only.
- `Random.Xoshiro256StarStar`: the only PRNG rules may use.
  - Seeded with a `ulong`.
  - `GetState`/`FromState` for snapshots.
  - Unbiased `NextInt`, plus `NextFixed` and `Shuffle`.
- `Random.SeedCommitment`: commit-reveal for provably fair dice and shuffles (SHA-256).
- `Engine.IGameEngine<TState, TAction, TConfig>`, with `ApplyResult`, `RuleError`, `GameOutcome` and `SeatInfo`.

## Guard rails (the build or tests fail if broken)

| Rule | Enforced by |
|---|---|
| `LangVersion` 9.0 and `netstandard2.1` for the libraries | `shared/Directory.Build.targets` (build error) |
| No `PackageReference`/`ProjectReference` in Rules | `shared/Directory.Build.targets` (build error) |
| Rules references only `netstandard` | `Guards/DeterminismGuardTests` |
| No `System.Random`, `DateTime.Now`/`UtcNow`, `Environment.TickCount`, `Guid.NewGuid`, `Stopwatch`, threads or tasks in Rules | `Guards/DeterminismGuardTests` (scans the compiled IL) |
| No `float`/`double`/`decimal` fields in Rules | `Guards/DeterminismGuardTests` |

## Writing shared code

- **C# 9 only.** Use block namespaces (`namespace X { }`). Don't use file-scoped namespaces, `record struct`, `required`, primary constructors on classes or collection expressions.
- **No `record` types or `init` setters.** They need an `IsExternalInit` shim on netstandard2.1, and are fragile across the Unity DLL boundary. Use classes with a constructor and get-only properties (Rules), or plain `get; set;` properties (Contracts).
- **Inside `GameVerse.Rules.*`, `Math` means our namespace.** Write `System.Math.Min(...)` for the BCL one.
- **Never iterate a `Dictionary` or `HashSet` where the order affects the result.** Use arrays, lists or sorted collections.
- **Adding a game:**
  1. Create `GameVerse.Rules/<Game>/` implementing `IGameEngine`.
  2. Add fixtures in `rules-fixtures/<game>/`.
  3. Write an `IFixtureAdapter` for it in the test project.
  4. Run the `rules-auditor` agent, then `make rules`.
