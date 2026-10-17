# Internal cancellation flow analyzer

`SPINT002`, `SPINT003`, `SPINT004`, and `SPINT005` run in repository builds through
`Directory.Build.targets` and are errors by default. They are not part of the
shipped SharpProof analyzer. The canonical `tooling` entrypoint builds the
internal analyzer before building repository projects. Direct dotnet build calls
inside the container must build this analyzer project first.

`SPINT002` requires a `CancellationToken` parameter on production asynchronous
methods. `SPINT003` checks that a method with a token forwards it when a
cancellable overload exists. Test assemblies are exempt from these two rules
because test method signatures are owned by the test runner.

`SPINT004` rejects optional `CancellationToken` parameters in all repository
projects, including test helpers. Callers must supply a token explicitly.
`SPINT005` rejects the static uncancellable token in production and test code. Tests should
pass `TestContext.CurrentContext.CancellationToken` so test runner cancellation can
reach the operation. Generated code is exempt from all four rules. Static `Main` methods are runtime
entry points; they should create or link a token and pass it into their work.

When a method must finish after cancellation, use a narrow
`#pragma warning disable SPINT003` around that call with a specific reason and
restore the warning immediately afterward.
