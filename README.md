# SharpProof

SharpProof 0.2.0-preview.1 is a soundness-first Roslyn analyzer and bounded
out-of-process verifier for C#. It deliberately supports a narrow,
compiler-bound subset.

SharpProof has three semantic outcomes:

- `Proven`: the goal follows from exact lowering and accountable evidence.
- `Refuted`: an executable counterexample or effect trace was replayed.
- `Unknown`: the language, model, evidence, or resource budget was insufficient.

Unsupported analyzer code normally produces no feature diagnostic. That silence
is an abstention, not a proof.

## What works today

| Surface | Current capability | User-visible result |
|---|---|---|
| Effect analyzer | Checks `[EnforcePure]`, `[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`, and `[AllowedExceptions]` over the admitted source subset | Opt-in "not proven" diagnostics |
| Contract analyzer | Replays definitely executed, compiler-bound `Contract.Requires(...)` clauses with exact call inputs | SP0027 only when the precondition concretely evaluates to false |
| Worker | Verifies bounded `Contract.Ensures(...)` obligations over acyclic Boolean/integer bodies and a few exact API-result facts | `Proven`, replay-validated `Refuted`, or typed `Unknown` records |

The analyzer does not run SMT or load Z3. General source-callee
assume/guarantee verification, loops in the worker, mutable-heap
postconditions, points-to analysis, and broad reference or sequence reasoning
are not implemented.

## Install and enable

Reference the preview package:

```xml
<PackageReference Include="SharpProof" Version="0.2.0-preview.1"
                  PrivateAssets="all" />
```

The package defaults to `off` and does not add its analyzer assemblies to the
compiler. Enable one compilation-global mode:

```xml
<PropertyGroup>
  <SharpProofMode>all-experimental</SharpProofMode>
</PropertyGroup>
```

Valid values are:

- `off`: no analyzer feature pipeline; this is the default.
- `effects`: effect contracts only.
- `contracts`: concrete call-site `Requires` checking only.
- `all-experimental`: both analyzer groups.

A custom analyzer host may instead provide the compilation-global
`sharpproof_mode` analyzer-config key. A tree-scoped `.editorconfig` value is
invalid because the mode must be compilation-global.

Mode selection and diagnostic selection are separate opt-ins. Feature
diagnostics are Info and disabled by default, so enable the IDs you want in
`.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0016.severity = suggestion
dotnet_diagnostic.SP0027.severity = suggestion
dotnet_diagnostic.SP0045.severity = suggestion
dotnet_diagnostic.SP0046.severity = suggestion
```

SP0024, for malformed supported control/effect arguments, is an enabled Error.
SP0025, for invalid analyzer configuration, is an enabled Warning. SP0013,
SP0015, and SP0030 are reserved until concrete effect-trace replay exists; the
current may-effect analyzer does not emit them.

## Effect contracts

This test-backed example is accepted without a feature diagnostic in `effects`
or `all-experimental` mode:

```csharp
using SharpProof.Attributes;

public static class Arithmetic {
    [EnforcePure]
    [ZeroAllocations]
    [DoesNotThrow]
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Identity(int value) => value;
}
```

Effect analysis is a path-insensitive may analysis. A possible allocation,
observable state access, disallowed capability, escaping exception, or
incomplete boundary prevents proof. It does not become a definitive violation
without a replayable effect trace.

Observable purity permits fresh allocation and writes confined to fresh owned
state; `[ZeroAllocations]` does not. Implicit exceptions from dereferences,
indexing, division, casts, checked arithmetic, and similar operations count
toward exception contracts.

An external metadata call is modeled only when an exact built-in `ApiSpec`
resolves, or when the boundary has both:

```csharp
[SharpProofTrusted("Reviewed against the external implementation.")]
[EffectContract(
    SharpProofEffect.ReadsAmbientState,
    Complete = true,
    IsDeterministic = true)]
public static extern int ReadExternalState();
```

Trust without an explicit complete contract proves nothing. A
`[SharpProofSuppress("reason")]` changes reporting only; it does not add facts.

The analyzer admits a checked subset of ordinary methods, explicit
constructors, static constructors, and accessors. It covers common primitive
expressions, locals, assignments, direct calls, object and array creation,
`if`, ordinary `for`/`while`/`do` loops, constant `switch`, exception handling,
`using`, `lock`, conditional access, and ordinary interpolation.

Async, iterators, `foreach`, closures, local functions, delegates, dynamic
binding, ref parameters or locals, ref returns, ref-like and pointer shapes,
open generic shapes, patterns, queries, ranges, collection expressions, and
unknown future Roslyn operation kinds abstain silently. A closed constructed
generic API call is admitted only when its exact specification resolves.

## Concrete call-site preconditions

Contracts are normal C# expressions bound by compiler symbol identity:

```csharp
using SharpProof.Attributes;

public static class Preconditions {
    public static void Positive(int value) {
        Contract.Requires(value > 0);
    }

    public static void BadCall() {
        Positive(-1); // SP0027 when contracts mode and SP0027 are enabled
    }
}
```

SP0027 is emitted only when the call is definitely executed, all normally
evaluated receiver and argument expressions lower exactly, the prefix is known
not to throw, and the instantiated precondition replays to `false`. Unknown
arguments, conditional execution, unsupported expressions, and potentially
throwing prefixes are silent.

`Contract.Requires`, `Contract.Ensures`, and `Contract.Assume` carry
`[Conditional("SHARPPROOF_CONTRACTS")]`. Normal builds therefore erase the
calls and do not evaluate their arguments. They are static-analysis contracts,
not runtime guards.

Do not define `SHARPPROOF_CONTRACTS` in an ordinary application or test build.
Doing so emits the contract calls and evaluates their arguments.
`Contract.Result<T>()` and `Contract.Old(...)` throw
`InvalidOperationException` when directly executed.

`Contract.Assume(...)` is explicit user-supplied proof evidence. It can affect a
worker proof and is reported as an assumption in the proof core; use it only
when the assumption itself is justified.

## Bounded worker postconditions

The worker can verify small acyclic bodies with locals, reassignment, branches,
and multiple returns. This example is covered end to end:

```csharp
using SharpProof.Attributes;

public static class Choices {
    public static bool Choose(bool chooseLeft, bool left, bool right) {
        Contract.Ensures(
            Contract.Result<bool>() == (chooseLeft ? left : right));

        if (chooseLeft) {
            return left;
        }

        return right;
    }
}
```

Opt into worker execution on Windows x64:

```powershell
dotnet build /p:SharpProofVerify=true
```

`SharpProofVerify` is independent of `SharpProofMode` and editor diagnostic
settings. It runs after compilation, outside design-time builds. The default
result is written under:

```text
obj/<Configuration>/<TargetFramework>/SharpProof/result.json
```

Each `Ensures` clause receives a versioned record:

- `Proven` carries its canonical proof core, which can be empty for a hygienic
  tautology.
- `Refuted` has a replay-validated concrete model and fails the build with
  worker exit code 5.
- `Unknown` has a closed reason such as `UnsupportedBody`, `DeepEnsures`,
  `ResourceLimit`, or `MethodTimeout`. A valid `Unknown` record does not by
  itself fail the build.

Malformed input, compiler errors, protocol errors, containment failure, and a
hard worker timeout fail the build. The worker uses deterministic query,
method, project, expression-depth, memory, process, and parallelism limits. Its
content-addressed cache defaults to
`obj/<Configuration>/<TargetFramework>/SharpProof/cache` in the MSBuild
integration; only complete terminal `Proven` and replay-validated `Refuted`
responses are cacheable.

The current body executor is capped at 64 reachable blocks, 64 return paths,
and 4,096 execution states. It supports a Boolean/integer SMT proof domain.
Loops, arbitrary source calls, loads/stores, mutable heap state, unsupported
conversions, excessive expression depth, and exceeded bounds produce
`Unknown`.

The worker also has narrow, spec-justified support for:

- `Math.Abs(int)` normal-return non-negativity;
- `string.Concat(string, string)` result non-nullness;
- `Array.Empty<T>()` result non-nullness and zero array length.

It does not treat `Enumerable.Empty<T>()` as array-backed sequence state, and a
counterexample involving a spec-modeled call result is withheld when concrete
replay cannot validate it.

Within a worker target, `Requires` clauses are entry assumptions; the worker
does not prove that callers satisfy them. `Assume` clauses are explicit
user-supplied assumptions. Only `Ensures` clauses produce verification records.

## Compiler-bound companions and closed attributes

`[ContractFor(typeof(TargetType))]` can place compiler-bound contracts in a
source companion for a target type. The companion must be one static class with
the same generic arity and constraints as the target. Each companion member
must be an ordinary static method with an exact compiler-symbol match,
including receiver placement for instance targets, generic constraints, ref
kinds, nullability, and return type. SPCF0001-SPCF0008 are enabled Error
diagnostics for invalid companion declarations when the package analyzer
payload is loaded.

Direct `Contract.Requires`, `Ensures`, and `Assume` clauses used by the worker
must be direct expression statements in one contiguous method-body prologue.
`Result<T>` is valid only inside `Ensures`; `Old(...)` is valid only inside
`Ensures` and cannot be nested.

The currently consumed closed value attributes are:

- `[NotNull]`
- `[Positive]`
- `[InRange(minimum, maximum)]`

On parameters they become preconditions. On method return values they become
postconditions. Property and field declarations are not a general active
closed-contract proof surface, even though the attribute types permit those
targets. `[Pure]` exists in the attributes package but is not the effect
enforcement attribute; use `[EnforcePure]` for current analyzer behavior.

## Exact built-in API specifications

The default table contains these seven BCL rows:

| API | Current modeled facts |
|---|---|
| `Array.Empty<T>()` | Effects and allocation unknown across type initialization; does not throw; non-null empty array result |
| `object` constructor | Call boundary has no effects or allocation and does not throw; `new object()` still allocates the object |
| `string.Length` | Reads receiver state; no allocation; does not throw inside the resolved call boundary |
| `string.Concat(string, string)` | No side effects; may allocate; does not throw; non-null result |
| `List<T>.Add(T)` | Writes receiver state; may allocate; throw behavior unknown |
| `Math.Abs(int)` | No side effects or allocation; may throw `OverflowException`; normal result is non-negative |
| `Enumerable.Empty<T>()` | Effects and allocation unknown across type initialization; does not throw; non-null empty enumerable fact |

Missing, ambiguous, or target-framework-inapplicable rows fail closed.

## Target frameworks and hosts

The checked-in acceptance contract declares these consumer target frameworks:

- `netstandard2.0`
- `net8.0`
- `net472`

The analyzer and attributes are `netstandard2.0` and contain no verifier, Z3,
or native solver payload. The packaged `SharpProof.Worker` is a `net8.0` tool with a
Windows x64 native Z3 payload and mandatory Windows Job Object containment.
`SharpProofVerify=true` on a non-Windows host fails with an explicit
unsupported-host build error; analyzer modes remain available.

The full acceptance workflow runs on `windows-latest`. A separate
package-consumer workflow restores and exercises analyzer consumers on Windows
x64, Linux x64, and macOS Intel; only Windows x64 enables packaged worker
verification. Real Visual Studio, Rider, and Windows ARM64 validation remain
outstanding release gates.

## Build and validate this repository

Run long-lived .NET commands through the repository wrapper:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 restore SharpProof.sln
.\scripts\Generate-Readme.ps1 -Verify
.\eng\acceptance\Verify.ps1 -Configuration Release
```

The acceptance gate enforces the dependency graph and trusted-kernel size,
builds the solution, runs architecture and banned-API checks, lattice and
finite-CFG laws, runtime and differential oracles, worker/package integration,
cache/concurrency/cancellation tests, the pinned corpus, a fixed-seed
1,000-case fuzz run, and performance budgets.

## Documentation

- [Documentation index](https://github.com/alexyorke/SharpProof/blob/main/docs/README.md)
  is the complete maintained-doc map.
- [SEMANTICS.md](https://github.com/alexyorke/SharpProof/blob/main/SEMANTICS.md)
  is the normative soundness boundary and wins if another document conflicts
  with it.
- [Architecture](https://github.com/alexyorke/SharpProof/blob/main/docs/architecture.md)
  describes the production dependency graph and proof boundary.
- [Coverage and limits](https://github.com/alexyorke/SharpProof/blob/main/docs/coverage-and-limits.md)
  summarizes admitted and rejected product areas.
- [Analysis limits](https://github.com/alexyorke/SharpProof/blob/main/docs/analysis-limits.md)
  lists shipping worker and performance budgets.
- [Diagnostics](https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md)
  documents analyzer and `ContractFor` generator IDs.
- [Typed Unknown reasons](https://github.com/alexyorke/SharpProof/blob/main/docs/unknown-reasons.md)
  explains fail-closed abstentions.
- [Native SMT packaging](https://github.com/alexyorke/SharpProof/blob/main/docs/native-smt-packaging.md)
  describes analyzer and worker payload separation.
- [SMT lifecycle](https://github.com/alexyorke/SharpProof/blob/main/docs/smt-lifecycle.md)
  describes solver ownership and disposal.
- [API result domains](https://github.com/alexyorke/SharpProof/blob/main/docs/soundness-notes/2026-07-25-api-spec-result-domains.md)
  records the bounded nullness/cardinality integration.
- [Hardening audit](https://github.com/alexyorke/SharpProof/blob/main/docs/soundness-notes/2026-07-25-hardening.md)
  records validation evidence and outstanding checkpoints.
- [Acceptance contract](https://github.com/alexyorke/SharpProof/blob/main/eng/acceptance/README.md)
  describes the active release gate.
