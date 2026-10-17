# Internal cancellation flow analyzer

`SPINT002`, `SPINT003`, and `SPINT004` run in repository builds through
`Directory.Build.targets` and are errors by default. They are not part of the
shipped SharpProof analyzer. The canonical `tooling` entrypoint builds the
internal analyzer before building repository projects.

`SPINT002` requires a `CancellationToken` parameter on production asynchronous
methods. `SPINT003` checks that a method with a token forwards it when a
cancellable overload exists. Test assemblies are exempt from these two rules
because test method signatures are owned by the test runner.

`SPINT004` rejects optional `CancellationToken` parameters in all repository
projects, including test helpers. Callers must supply a token explicitly.
Generated code is exempt from all three rules. Static `Main` methods are runtime
entry points; they should create or link a token and pass it into their work.

When a method must finish after cancellation, use a narrow
`#pragma warning disable SPINT003` around that call with a specific reason and
restore the warning immediately afterward.
