# Coding Standard for ComparisonTool

This document defines the coding conventions, style guidelines, and recommended tooling for the ComparisonTool solution. The goal is consistent, readable, and maintainable C#/.NET code across the repository.

## Table of contents
- Purpose
- GENERAL GUIDELINES
- Formatting
- Naming
- Files & Project Layout
- Language features & patterns
- Asynchronous code
- Error handling & logging
- Testing conventions
- Pull requests & code review
- Tooling & enforcement
- Appendix: Quick commands

## Purpose
Keep code consistent and easy to read across contributors, reduce churn in code reviews, and enable automatic enforcement using editor config and analyzers.

## GENERAL GUIDELINES
- Follow idiomatic modern C# (target frameworks in the repo, e.g., .NET 10). Prefer clarity over cleverness.
- Keep methods small and focused. If a method exceeds ~100 lines or has more than one responsibility, consider refactoring.
- Use dependency injection for services. Avoid static/global mutable state.
- Write XML documentation comments for public APIs and library methods.

## Formatting
- Indentation: 4 spaces. Do not use tabs.
- Line endings: CRLF (Windows). LF may be tolerated in cloned repos but will be normalized.
- Maximum line length: 120 (soft wrap / guideline, not enforced hard).
- Brace style: Allman (new line). Example:

  public class Foo
  {
      public void Bar()
      {
          // ...
      }
  }

- `using` directives: placed OUTSIDE the namespace at the very top of the file.
- Namespaces: Prefer FILE-SCOPED namespaces where practical (C# 10+) for library and model classes. Use block namespaces only when multiple type declarations require region separation.
- Remove trailing whitespace; ensure a final newline.
- Always use `var` for local variables (consistency + focus on the right-hand side intent). Avoid `dynamic` unless absolutely required.

## Naming
- Types (classes, structs, enums, interfaces): PascalCase. Interfaces start with `I` (e.g., `IComparisonService`).
- Methods and properties: PascalCase. Async methods end with `Async`.
- Local variables and method parameters: camelCase.
- Private fields: camelCase (NO leading underscore). Example: `fileSystemService`. If a name conflicts with a parameter, prefer suffixing the field or using `this.` qualifier, do not add an underscore.
- Constants and readonly static members: PascalCase.
- Boolean properties: affirmative names (`IsValid`, `HasChanges`).

## Files & Project Layout
- One top-level public type per file (file name matches the type name) is preferred.
- Keep small helper classes grouped logically in the same folder.
- Tests mirror the main code namespace and structure under the `ComparisonTool.Tests` project.

## Language features & patterns
- Prefer pattern matching and expression-bodied members only when they remain obviously readable (avoid nested pattern complexity that obscures intent).
- Use `var` ALWAYS for local variables (uniformity). Exception: Use explicit types for constants, fields, and public API signatures.
- Avoid premature optimization — profile first.

## Asynchronous code
- Prefer asynchronous APIs for I/O or long-running operations.
- Name async methods with `Async` suffix.
- Avoid `async void` except for top-level event handlers; prefer `Task`/`Task<T>` for testable/awaitable code.

## Error handling & logging
- Use exceptions for exceptional conditions; validate inputs at public boundaries.
- Catch only the exceptions you can handle meaningfully; otherwise let them bubble up.
- Use structured logging (e.g., Microsoft.Extensions.Logging) and avoid string concatenation for log messages. Use message templates and pass variables separately.

## Testing conventions
- Framework: MSTest only (no xUnit / NUnit). Existing xUnit tests will be migrated incrementally.
- Additional libraries: Moq + AutoFixture (+ AutoFixture.AutoMoq). FluentAssertions may remain temporarily but new tests should prefer native MSTest + Assert patterns unless expressiveness needed.
- Test naming pattern: `MethodUnderTest_StateUnderTest_ExpectedResult`.
- Project structure: Use separate test project names with suffixes: `.UnitTests`, `.IntegrationTests`, `.EndToEndTests` as needed. Current `ComparisonTool.Tests` will be refactored into multiple projects if scope grows.
- Unit tests: small, fast, deterministic.
- Integration / end-to-end tests: isolated environment setup; limit external dependencies.
- Each test should arrange-act-assert with clear section separation (blank lines or comments) and minimize logic inside assertions.

### Migration Note
Current tests use xUnit attributes (`[Fact]`). Migration plan:
1. Introduce MSTest packages alongside xUnit (transitional phase).
2. Convert attributes: `[Fact]` -> `[TestMethod]`, remove xUnit-specific features.
3. Replace `Assert` patterns / FluentAssertions chains where simple MSTest assertions suffice.
4. Remove xUnit packages once conversion complete.

## Pull requests & code review
- Keep PRs focused and small where possible.
- Provide a short description of the change and the reason behind it.
- Resolve static analysis warnings or justify why they are suppressed.

## Tooling & enforcement
- `.editorconfig` enforces indentation, brace style, using placement, namespace style, `var` usage, and naming patterns.
- `dotnet format` (verify in CI) ensures style compliance.
- StyleCop.Analyzers applied to ALL projects (as PrivateAssets) — warnings should be addressed; suppressions require justification.
- Roslyn IDE analyzers enabled by default (implicit in SDK). Consider raising severity for key rules over time.
- Optional future: pre-commit hook running `dotnet format --verify-no-changes` + `dotnet test` (deferred).

### Recommended commands
- Install/update formatter:

```
dotnet tool install -g dotnet-format
dotnet format
```

- Verify formatting (CI style):

```
dotnet format --verify-no-changes
```

- Run all tests:

```
dotnet test
```

- Run only unit tests (after project split):

```
dotnet test ComparisonTool.UnitTests.csproj
```

## Appendix: Editor formatting rules (high level)
- Indent size: 4
- Indent style: spaces
- End of line: CRLF
- Charset: UTF-8

---

If you want, I can extend this document with: DTO naming, test migration checklist, analyzer suppression template, or add a `STYLEGUIDE.md` for contributor-facing guidance.
