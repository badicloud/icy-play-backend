# Backend Agent Instructions

These instructions apply to the entire `icy-play-backend` repository.

Before creating or changing backend tests, read and follow `docs/testing-standards.md`.

Mandatory testing rules:

- Use explicit Arrange, Act, Assert sections.
- Name unit test methods with the Pascal-style underscore convention `Should_Expected_Behavior_When_Condition`. This convention does not apply to integration tests.
- Wrap two or more related assertions in an explicit braced FluentAssertions `using (new AssertionScope()) { ... }` block.
- Use the reusable builders and deterministic values in `backend/IcyPlay.UnitTests/TestData`.
- Add focused builders when a commonly constructed test object does not have one.
- Mock only true architectural boundaries; do not mock EF Core `DbSet<T>` or introduce generic repositories solely for tests.
- Use integration tests for EF Core, Dapper, migrations, and API pipeline behavior.
- Add a regression test for every production bug fix.
- Run `dotnet test` and `dotnet format --verify-no-changes` before completing backend test work.

Backend interface organization:

- Every interface must be declared in its own file named after the interface, for example `IAuthService.cs`.
- Do not declare interfaces inside consumer, implementation, controller, service, or DTO/contract files.
- Place abstractions in the owning application/domain namespace and implementations in Infrastructure or API as appropriate.
