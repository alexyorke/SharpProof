# Internal parameter guard analyzer

`SPINT001` runs in repository builds through `Directory.Build.targets`. It is
not part of the shipped SharpProof analyzer. A build fails when an input
parameter of a concrete callable has no explicit rejecting validation.
The canonical `tooling` entrypoint builds the analyzer once before project
builds. Direct `dotnet build` calls in the container must build this project
first.

The analyzer covers methods, constructors (including primary constructors),
operators, local functions, and lambdas. It ignores `out` parameters, which
cannot be read before assignment, declarations without bodies, and generated
code. Nonnullable `bool` and `CancellationToken` inputs are also exempt because
every representable value is valid by itself. A guard can be a conditional path that throws or returns, a `?? throw`,
a throwing switch arm, or a call to a recognized validation helper (`ThrowIf*`,
`Validate*`, `Ensure*`, `Check*`, `Require*`, or `NotNull`) that receives the
parameter. The rule requires a visible guard; it does not prove that the
condition rejects every invalid value.

The rule checks all repository C# source without a baseline. Every reported
parameter must be reviewed and given a meaningful validation, or the analyzer
must be corrected if it mistakes existing validation for an unguarded input.
