# Contributing to Spanner.InMemoryEmulator

Thank you for your interest in contributing!

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a feature branch: `git checkout -b my-feature`
4. Make your changes following the guidelines below
5. Push and open a Pull Request

## Development Requirements

- .NET 8.0 SDK (or later)
- PowerShell (for test scripts)
- Docker Desktop (optional — only needed for Tier 2 emulator parity tests)

## Building

```bash
dotnet build Spanner.InMemoryEmulator.sln
```

## Running Tests

### Run tests (in-memory only — no Docker required)

```powershell
$env:SPANNER_TEST_TARGET = "InMemory"
dotnet test tests/Spanner.InMemoryEmulator.Tests.Unit/
dotnet test tests/Spanner.InMemoryEmulator.Tests.Integration/
```

### Run tests against Docker emulator

```powershell
# Start the emulator
pwsh scripts/start-emulator.ps1
# Run integration tests against it
$env:SPANNER_TEST_TARGET = "Emulator"
dotnet test tests/Spanner.InMemoryEmulator.Tests.Integration/ --filter "Target!=InMemoryOnly"
# Stop when done
pwsh scripts/stop-emulator.ps1
```

### Run a single test category

```powershell
dotnet test --filter "Category=DDL"
```

### Skip Docker entirely

You can develop and run ALL unit tests and ALL InMemory integration tests with zero Docker dependency.
Docker is only needed for Tier 2 parity verification.

## Guidelines

- **TDD**: Write a failing test first, then implement the minimum code to make it pass, then refactor. See [AGENTS.md](AGENTS.md) for full workflow details.
- **Behavioral Sources**: Every piece of behavioral logic must be backed by a verified source. See [AGENTS.md](AGENTS.md) for the approved source table and citation format.
- **Test Classification**: Unit tests go in `Tests.Unit`, integration tests in `Tests.Integration`. See [AGENTS.md](AGENTS.md) for classification rules.
- **No breaking changes** without discussion in an issue first.
- **Keep PRs focused** — one feature or fix per PR.
- **Code style**: Follow existing conventions, no unused imports, XML docs on public API.

## Reporting Issues

- Use GitHub Issues for bug reports and feature requests.
- Include steps to reproduce, expected behavior, and actual behavior for bugs.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
