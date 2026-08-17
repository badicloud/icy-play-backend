# Backend Testing Standards

## Purpose

This document is the required testing standard for the IcyPlay backend. New tests and material changes to existing tests must follow these conventions so the suite remains readable, deterministic, and aligned with the backend architecture.

## Test Projects

| Project | Scope | External dependencies |
| --- | --- | --- |
| `IcyPlay.UnitTests` | Domain rules, validators, application services, calculations, and isolated orchestration | None; use mocks/fakes at boundaries |
| `IcyPlay.IntegrationTests` | API pipeline, EF Core mappings and migrations, SQL behavior, Dapper queries, authentication/authorization, and external adapter contracts | Real or test-hosted dependency where behavior matters |

Do not call a test a unit test if it opens a network connection, accesses a real database, reads machine-specific state, or depends on execution order.

## Mandatory Test Structure: AAA

Every test must visibly use Arrange, Act, Assert in that order. Add the comments even when a section is one line; consistency is more valuable than saving three lines.

```csharp
[Fact]
public void Should_Lock_User_When_Maximum_Failed_Login_Attempts_Are_Reached()
{
    // Arrange
    var now = TestTimes.UtcNow;
    var user = new UserBuilder().Build();

    // Act
    for (var attempt = 0; attempt < 5; attempt++)
    {
        user.RecordFailedLogin(5, TimeSpan.FromMinutes(15), now);
    }

    // Assert
    user.FailedLoginAttempts.Should().Be(5);
    user.LockoutEnd.Should().Be(now.AddMinutes(15));
}
```

Rules:

- Arrange only the state needed for the behavior.
- Act should normally contain one operation.
- Assert observable outcomes, not private implementation details.
- Do not put branching or loops in Assert.
- One test should describe one behavior. Multiple assertions are allowed when they verify one outcome.

## Naming Convention

All unit test method names must use Pascal-style words separated by underscores in this order:

```text
Should_Expected_Behavior_When_Condition
```

Examples:

- `Should_Normalize_Email_When_Email_Has_Whitespace_And_Mixed_Case`
- `Should_Return_Duplicate_Email_Failure_When_Email_Already_Exists`
- `Should_Return_Forbidden_When_Actor_Does_Not_Own_Court`

Naming rules:

- Begin every test method with `Should_`.
- Describe the observable expected behavior immediately after `Should_`.
- Use `_When_` before the condition or scenario.
- Capitalize every word and separate words with underscores.
- Do not include implementation details unless they are part of the public behavior.

Integration tests use the conventional PascalCase format `Member_Scenario_ExpectedBehavior`; the `Should_Expected_Behavior_When_Condition` convention applies only to `IcyPlay.UnitTests`.

Test class names should identify the subject, such as `UserTests`, `AuthServiceTests`, or `RegisterCustomerRequestValidatorTests`. Avoid broad classes such as `HelpersTests` or `IdentityTests` as a feature grows.

## Test Data Builders

Use builders from `IcyPlay.UnitTests/TestData` for domain entities and requests with several constructor values. Builders must:

- Provide valid defaults so each test overrides only relevant values.
- Return a new object from every `Build()` call.
- Use fluent `With...` methods.
- Contain no assertions and perform no I/O.
- Avoid hiding the behavior being tested. Construct directly when only one or two obvious values are required.

Example:

```csharp
var request = new RegisterCustomerRequestBuilder()
    .WithPassword("weak")
    .Build();
```

Add a focused builder when a type becomes common. Do not create one universal object mother or a reflection-based generic builder.

## Deterministic IDs and Time

Never place random `Guid.NewGuid()`, arbitrary parsed GUIDs, `DateTimeOffset.UtcNow`, or `DateTime.Now` directly in tests.

- Use `TestIds.For("scope", sequence)` for stable IDs.
- Use named IDs such as `TestIds.CustomerUserId` when the role is reused.
- Use `TestTimes.UtcNow` and offsets from it for deterministic timestamps.
- When production behavior depends on current time, inject a clock abstraction rather than mocking static time.

Deterministic values make failures repeatable and keep test relationships clear.

## Mocking Architecture

Use Moq only for an actual architectural boundary. Typical mock candidates are:

- Application service interfaces used by controllers.
- Clock, current-user, file storage, email, CAPTCHA, payment, and notification abstractions.
- Feature-specific repositories or query services when such an interface exists.
- Dapper connection/query abstractions for isolated orchestration tests.

Do not mock:

- Domain entities or value objects.
- FluentValidation itself.
- `IQueryable`, `DbSet<T>`, or EF Core LINQ behavior.
- Concrete classes merely to avoid constructing them.
- Methods belonging to the same class under test.

The project intentionally does not use a generic repository or generic unit-of-work wrapper. `AppDbContext` already fills those roles. Use a real test database for EF Core query, mapping, constraint, transaction, and migration behavior. Introduce a feature-specific repository only when it represents a meaningful domain/data boundary—not just to make mocking easier.

### Mock Setup Rules

- Default to `MockBehavior.Strict` for service orchestration tests.
- Name mocks by role: `captchaVerifier`, `emailSender`, `bookingRepository`.
- Set up only calls required by the scenario.
- Verify important commands and side effects explicitly.
- Use `Times.Never` to prove unsafe downstream work did not occur after failure.
- Do not verify every getter or incidental call.
- Never return production secrets or real personal information from mocks.

```csharp
var captchaVerifier = new Mock<IRecaptchaVerifier>(MockBehavior.Strict);
captchaVerifier
    .Setup(service => service.VerifyAsync("valid-token", "register", null, cancellationToken))
    .ReturnsAsync(true);

// Act
var result = await sut.RegisterAsync(request, cancellationToken);

// Assert
result.Succeeded.Should().BeTrue();
captchaVerifier.VerifyAll();
```

## Layer-Specific Guidance

### Domain

Construct real entities and test public behavior, invariants, state transitions, and emitted domain events. No mocks should normally be needed.

### Validators

Use `FluentValidation.TestHelper`. Keep one scenario per test and use a valid request builder as the baseline.

### Application Services

Mock external or persistence boundaries, call the real service, verify its result and meaningful side effects. Do not test private methods separately.

### API Controllers and Middleware

Prefer integration tests through the ASP.NET Core test host for routing, serialization, filters, authentication, middleware ordering, and status codes. A direct unit test is acceptable for isolated response mapping when the HTTP pipeline adds no material behavior.

### EF Core and Dapper

Use integration tests with SQL Server-compatible behavior. Do not use mocked `DbSet<T>` to claim a query works. Complex Dapper SQL must be tested against a real test schema.

## Assertions

- Prefer FluentAssertions for domain/application outcomes.
- Prefer FluentValidation TestHelper for validators.
- Use an explicit braced `using (new AssertionScope()) { ... }` block whenever a test has two or more related assertions. This reports all failures from the Assert phase in one run instead of stopping at the first failure.
- Keep the `AssertionScope` block inside the Assert section and place all related assertions inside its braces. A single assertion does not need a scope.
- Assert exact status/error codes for API failures.
- Assert collections with equivalence or containment rather than manual loops.
- Avoid assertions on log message wording unless logging itself is the contract.
- Never assert internal framework implementation details.

## Async and Cancellation

- Async methods must have async tests and be awaited.
- Pass a named `CancellationToken` through service tests.
- Never use `.Result`, `.Wait()`, sleeps, or timing races.
- Test cancellation when a workflow performs meaningful I/O or long-running work.

## Test Isolation

- Tests must run independently and in any order.
- Do not share mutable static state.
- Builders and fixtures must return fresh mutable objects.
- Clean database state between integration tests.
- Never depend on developer machine paths, ports, credentials, or current culture unless explicitly under test.

## Required Coverage

Add tests for:

- New or changed business rules.
- Authorization and ownership boundaries.
- Validation success and failure paths.
- Important service success and failure branches.
- Every production bug fix as a regression test.
- Database constraints, mappings, transactions, and report SQL through integration tests.

Coverage percentage is a signal, not the goal. Meaningful behavior and risk coverage take priority.

## Pull Request/Test Review Checklist

- [ ] Unit test name follows `Should_Expected_Behavior_When_Condition`.
- [ ] Arrange, Act, Assert sections are explicit.
- [ ] Multiple related assertions are wrapped in an `AssertionScope`.
- [ ] Test is deterministic and isolated.
- [ ] Builders provide valid defaults and only relevant fields are overridden.
- [ ] IDs and time come from `TestIds` and `TestTimes`.
- [ ] Mocks represent real boundaries and use strict behavior where appropriate.
- [ ] EF Core/Dapper behavior is covered by integration tests, not mocked queries.
- [ ] Success, validation, authorization, and important failure paths are covered.
- [ ] No secrets, tokens, passwords, or personal production data appear in test output.
- [ ] `dotnet test` and `dotnet format --verify-no-changes` pass.
