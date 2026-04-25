# Contribution Instructions

## TDD Workflow

- Always use Test-Driven Development (TDD): write tests first, then follow the red-green-refactor cycle.
- Write a failing test (red), implement the minimum code to make it pass (green), then refactor.
- Write additional failing tests to cover edge cases and error conditions, and repeat the cycle until you have comprehensive test coverage for the feature or bug fix you're working on.
- For every unit test written, if possible, write the equivalent integration test, testing the same functionality from the entry point.

## Bug Fixing

- Always fix all bugs you find along the way, even if they are outside the immediate scope of the current task.
- When fixing a bug, identify missing test coverage in and around the affected area and create that coverage — again following the TDD red-green-refactor cycle.
- Fix any additional bugs discovered during that expanded test coverage work.

## Reflection Policy

- **Do not use reflection as a first resort.** Explore all public API options before considering reflection.
- Reflection on internal/private members of external libraries (e.g., SDK backing fields) is fragile — it can break silently on library updates with no compile-time warning.
- If reflection is genuinely the only viable approach after exhausting alternatives, it may be used — but:
  - **The PR description must explicitly state in bold that reflection is used**, what it targets, and why no public API alternative exists.
  - Add a code comment at the reflection site explaining the dependency and what would break if the internal member is renamed or removed.
  - Prefer a graceful fallback (e.g., leave the value as null) over a hard failure if the reflected member is missing.

## Behavioral Source Requirements

Every piece of behavioral logic in the source code — status codes, validation rules, error conditions, side-effect semantics — **must** be backed by a verified source. This prevents accidental divergence from real Google Cloud Spanner behavior.

### Rules

1. **Before implementing any behavioral logic**, find and verify the expected behavior from one of the approved sources listed below.
2. **Add a code comment** at the implementation site citing the source (a short URL or description is sufficient). Example:
   ```csharp
   // Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#executesqlrequest
   //   "A single SQL query string."
   ```
3. **If sources conflict** (e.g., the Go emulator behaves differently from the documentation), prefer the official documentation over observed emulator behavior. Document the discrepancy in a code comment and mark the relevant integration test with `[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]`.
4. **If no source can be found**, do not guess. Ask for guidance or raise a discussion in the PR.

### Approved Behavioral Sources (in priority order)

| Priority | Source | URL / Location |
|----------|--------|----------------|
| 1 | Google Cloud Spanner gRPC API reference | https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1 |
| 2 | Google Cloud Spanner Admin API reference (Database) | https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1 |
| 3 | Google Cloud Spanner Admin API reference (Instance) | https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1 |
| 4 | Google Cloud Spanner .NET SDK API reference | https://cloud.google.com/dotnet/docs/reference/Google.Cloud.Spanner.V1/latest |
| 5 | Google Cloud Spanner "How-to" guides | https://cloud.google.com/spanner/docs/how-to |
| 6 | Google Cloud Spanner conceptual docs | https://cloud.google.com/spanner/docs/concepts |
| 7 | Google Cloud Spanner .NET SDK source code | https://github.com/googleapis/google-cloud-dotnet |
| 8 | Observed behavior on the Go Spanner Emulator | (local testing via `gcloud emulators spanner start`) |

> **Note:** Source 8 (Go emulator observation) is the weakest evidence. The Go emulator has known gaps. Always cross-reference with sources 1–7 when possible.

## Versioning & Release

- After every session of bug fixes is complete and the full test suite has passed, increment the patch version in `src/Directory.Build.props` (the single `<Version>` property shared by all packages).
- **On `main`:** Commit, create a git tag (`v{version}`), and push both the commit and the tag to origin.
- **On any other branch:** Commit and push the code changes and version bump only. Do not create or push a tag.

## Test Classification Rules

Tests are split into two projects. When creating or moving tests, follow these rules:

### Tests.Integration
- Uses `TestFixtureFactory.Create(session)` / `ITestDatabaseFixture` to obtain a database, where `session` is an injected `EmulatorSession` (xUnit collection fixture — decorate the test class with `[Collection(IntegrationCollection.Name)]`)
- Goes through the real SpannerClient SDK gRPC pipeline via the in-process gRPC TestServer
- Must **not** use `new InMemorySpannerStore()`, `new FaultInjector()`, or any `internal` API
- Can run against in-memory, Go emulator, or real GCP Spanner via `SPANNER_TEST_TARGET`
- **This is the primary test project** — every test should be an integration test unless it requires internal API access

### Tests.Unit
- Uses `new InMemorySpannerStore()`, `new InMemorySpannerClient()`, or any `internal` API directly
- Tests that use the gRPC service but also touch internal APIs (e.g., fault injection internals) belong here
- Only runs in-memory — never against a real emulator

### Tests.Shared
- Class library (not a test project) — shared infrastructure, fixtures, traits, and models
- Referenced by both Unit and Integration projects

### Key constraint
The Integration project does **not** have `InternalsVisibleTo` access. If a test needs internal APIs, it belongs in Unit.

## Documentation

After any changes are made that might affect the public API or functionality, documentation must be updated to reflect those changes. The documentation should be clear and comprehensive, covering all new features, changes to existing features, and any deprecations or removals. This includes updating the README file (if relevant), but mainly the wiki which can be found in a sister folder to the main repository — `../Spanner.InMemoryEmulator.wiki`.
