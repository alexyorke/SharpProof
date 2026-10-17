# Internal parameter guard analyzer

`SPINT001` runs in repository builds through `Directory.Build.targets`. The
analyzer is an MSBuild project dependency and is not included in shipped
SharpProof analyzer packages. Run builds through the canonical Linux container.

The rule covers methods, constructors (including primary constructors), operators,
local functions, anonymous methods, and lambdas. It ignores `out` parameters,
unread underscore inputs in anonymous callables, declarations without bodies, and generated code. A used underscore parameter is still checked.

Reference inputs require visible null validation unless their nullable annotation
admits null and no explicit null or range input constraint overrides it. A nullability suppression used as a dereference receiver still requires a runtime guard. A guard
can be a conditional rejection, `?? throw`, a throwing switch arm, or a recognized
validation helper (`ThrowIf*`, `Validate*`, `Ensure*`, `Check*`, `Require*`, or
`NotNull`) receiving the parameter. Null-testing predicates also count. Validation through an unchanged local alias is recognized; reassigned aliases do not count.

For value inputs, explicit validation includes returned predicates, default-array
normalization, nullable `HasValue` checks, and NUnit assertions in test assemblies.
Inputs whose entire domain is valid do not need artificial rejection: Boolean
values, cancellation tokens, tuples, key-value pairs, GUIDs, and safe numeric or
enum pass-through inputs without parameter attributes are exempt. Stated parameter constraints still require validation, including identity methods. Numeric indexes, array sizes, divisors,
checked arithmetic, value extraction, and switch dispatch still need validation.
Unused value inputs with no attributes or ID constraint are also total-domain inputs. Empty marker structs need no artificial rejection. Value identity functions and direct field or compiler-generated auto-property
projections also accept every input value. Scoped IR IDs require guards even in
identity functions because their default value is invalid unless documented as a
sentinel.

Other intentional total-domain inputs need a parameter-local explanation. Put the
comment inside the parameter, before its name, so it stays attached to that
parameter after formatting:

```csharp
string /* SPINT001: all values valid - raw content is validated during preflight */ content
```

The reason must be nonempty. An inferred lambda parameter can put the comment
immediately after its name. The rule checks every repository C# parameter without a baseline;
each finding must be fixed, documented as accepting the full domain, or shown to
be existing validation that the analyzer should recognize. The rule verifies
visible validation, not that a condition rejects every possible invalid input.

`SPINT002` requires a `CancellationToken` parameter on production asynchronous
methods. `SPINT003` checks that a method with a token forwards it when a
cancellable overload exists. Test assemblies are exempt from these two rules
because test method signatures are owned by the test runner.

`SPINT004` rejects optional `CancellationToken` parameters in all repository
projects, including test helpers. Callers must supply a token explicitly.
`SPINT005` rejects the static uncancellable token in production and test code.
Tests should pass `TestContext.CurrentContext.CancellationToken` so test runner
cancellation can reach the operation. Generated code is exempt from all four
rules. Static `Main` methods are runtime entry points; they should create or
link a token and pass it into their work.

When a method must finish after cancellation, use a narrow
`#pragma warning disable SPINT003` around that call with a specific reason and
restore the warning immediately afterward.

Repository validation primitives live in `eng/ParameterGuard.cs` and are linked
only into their consuming assemblies through an assembly-specific alias. They
preserve exception types, parameter names, and messages. Shared contract tests
exercise valid inputs, rejected inputs, and invalid parameter names for the
linked implementations. Guards that narrow nullable struct fields remain explicit
so compiler flow analysis retains the same guarantees.
