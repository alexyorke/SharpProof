# SharpProof

SharpProof 1.0.0-preview.1 is a soundness-first Roslyn analyzer and bounded
out-of-process verifier for C#. It deliberately supports a narrow,
compiler-bound subset.

SharpProof has three semantic outcomes:

- `Proven`: the goal follows from exact lowering and accountable evidence.
- `Refuted`: an executable counterexample or effect trace was replayed.
- `Unknown`: the language, model, evidence, or resource budget was insufficient.

The default `advisory` profile is quiet for unannotated code. Unsupported code
that is explicitly selected by a SharpProof contract or annotation produces
SP0047 instead of disappearing. Diagnostic silence is still not a proof.

## What works today

| Surface | Current capability | User-visible result |
|---|---|---|
| Effect analyzer | Checks `[EnforcePure]`, `[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`, and `[AllowedExceptions]` over the admitted source subset | Advisory "not proven" diagnostics on selected code |
| Contract analyzer | Replays definitely executed, compiler-bound `Contract.Requires(...)` clauses with exact call inputs | SP0027 only when the precondition concretely evaluates to false |
| Worker | Builds an accountable claim manifest and verifies bounded `Contract.Ensures(...)` obligations over acyclic Boolean bodies, admitted integer comparisons, checked `long` arithmetic, and a few exact API-result facts | One `Proven`, replay-validated `Refuted`, or typed `Unknown` result for every manifest claim |

The analyzer does not run SMT or load Z3. General source-callee
assume/guarantee verification, loops in the worker, mutable-heap
postconditions, points-to analysis, and broad reference or sequence reasoning
are not implemented.

## Install and enable

The portable analyzer and generator require a compiler host with Roslyn 4.14
or newer. For command-line builds, use .NET SDK 9.0.300 or newer. The
`SharpProof.Attributes` contract API alone remains a `netstandard2.0` library,
and `SharpProofProfile=off` omits analyzer/generator loading on an older host.
Visual Studio and Rider qualification with Roslyn 4.14 or newer remains a
release gate.

The coordinates below are the intended preview packages, but no SharpProof
package has been promoted to the public NuGet feed yet. Until the first
preview publication, use the repository's package-backed sample matrix; it
packs the exact three-package graph into an isolated local feed before restore.

Library projects that publish SharpProof annotations should reference the
contract API normally:

```xml
<PackageReference Include="SharpProof.Attributes"
                  Version="1.0.0-preview.1" />
```

Add the portable analyzer and generator as a development-only dependency:

```xml
<PackageReference Include="SharpProof" Version="1.0.0-preview.1"
                  PrivateAssets="all" />
```

`SharpProof` depends on the exact matching Attributes version. It contains no
worker, launcher, Z3, or native payload. It defaults to advisory analysis of
both implemented feature groups:

```xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofFeatures>all</SharpProofFeatures>
</PropertyGroup>
```

`SharpProofProfile` values are:

- `advisory` (default): analyze selected code and keep unannotated code quiet.
- `strict`: use the same analyzer features, enable the verifier, default its
  result policy to `require-proven`, and reject user/trusted evidence by
  default.
- `off`: omit the analyzer and generator from compiler analyzer items and skip
  verification.

`SharpProofFeatures` values are `effects`, `contracts`, and `all` (the
default). The effective selection is sealed into the schema-3 compiler
artifact and filters its manifest: `contracts` excludes effect-only
annotations, `effects` excludes postcondition claims and contract assumptions,
and `all` selects both surfaces. The current worker has no effect-proof claims,
so an effect-selected callable remains visible with incomplete callable
coverage and SP0047 instead of being treated as proven. A custom analyzer host
can use the compilation-global
`sharpproof_profile` and `sharpproof_features` analyzer-config keys. Tree-local
values are invalid because selection is compilation-global.

Most feature and incomplete-proof diagnostics are enabled `Info` diagnostics
by default. Concrete SP0027 precondition refutations are `Warning`; malformed
contracts and compiler-artifact failures are `Error`. Use normal Roslyn
configuration to promote, demote, or suppress the IDs:

```ini
[*.cs]
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0016.severity = suggestion
dotnet_diagnostic.SP0027.severity = suggestion
dotnet_diagnostic.SP0045.severity = suggestion
dotnet_diagnostic.SP0046.severity = suggestion
```

SP0024, for malformed supported control/effect arguments, is an Error.
SP0025, for invalid analyzer configuration, is a Warning. SP0013,
SP0015, and SP0030 are reserved until concrete effect-trace replay exists; the
current may-effect analyzer does not emit them.

`SharpProofMode` and `sharpproof_mode` remain preview-only compatibility
aliases with values `off`, `effects`, `contracts`, and `all-experimental`.
They are deprecated, cannot be combined with the replacement profile/feature
settings, and are planned for removal before RC.

For strict CI:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof.Verifier.Win-x64"
                    Version="1.0.0-preview.1"
                    PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <SharpProofProfile>strict</SharpProofProfile>
</PropertyGroup>
```

The verifier package depends on the exact matching `SharpProof` package, so a
CI-only verifier reference also supplies the analyzer and contract API.
`strict` or explicit `SharpProofVerify=true` without the verifier package fails
with an installation error. Installing the verifier while verification remains
disabled is harmless on unsupported hosts.

This implies `SharpProofVerify=true`,
`SharpProofVerifyPolicy=require-proven`, and
`SharpProofAssumptionPolicy=error` unless explicitly overridden. Strict mode
rejects `SharpProofVerify=false`; use `advisory` when worker execution must
remain optional.

## Effect contracts

This test-backed example is accepted without a feature diagnostic when
`SharpProofFeatures` is `effects` or `all`:

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

Each contract consumes only its relevant evidence facet: purity uses observable
read/write regions and capabilities, zero-allocation uses allocation,
capability contracts use capabilities, and exception contracts use escaping
exceptions. An unrelated unknown facet therefore no longer blocks a result.
Not-proven messages identify the missing facet with a stable reason prefix.

Observable purity permits fresh allocation and writes confined to fresh owned
state; `[ZeroAllocations]` does not. Implicit exceptions from dereferences,
indexing, division, casts, checked arithmetic, and similar operations count
toward exception contracts.

Exception contracts cover modeled synchronous managed exceptions. Ambient
catastrophic runtime failures, such as memory or stack exhaustion, are outside
the exception universe unless source or an exact boundary explicitly
throws or declares them.

An external metadata call is modeled only when an exact built-in `ApiSpec`
resolves, or when the boundary has both:

```csharp
using System.Runtime.InteropServices;
using SharpProof.Attributes;

public static class ExternalBoundary {
    [DllImport("reviewed-native-library")]
    [SharpProofTrusted("Reviewed against the external implementation.")]
    [EffectContract(
        SharpProofEffect.ReadsAmbientState,
        Complete = true,
        IsDeterministic = true)]
    public static extern int ReadExternalState();
}
```

`EffectContractAttribute` defaults to no declared capability or escaping
exception, `Complete=false`, and `IsDeterministic=false`. A reviewed boundary
must explicitly opt into every stronger fact, and its effects describe the
whole observable call boundary. Trust without an explicit complete contract
proves nothing. A
`[SharpProofSuppress("reason")]` changes reporting only; it does not add facts.

The analyzer admits a checked subset of ordinary methods, explicit
constructors, static constructors, and accessors. It covers common primitive
expressions, locals, assignments, direct calls, object and array creation,
`if`, ordinary `for`/`while`/`do` loops, constant `switch`, exception handling,
`using`, `lock`, conditional access, and ordinary interpolation.

Async, iterators, `foreach`, closures, local functions, delegates, dynamic
binding, ref parameters or locals, ref returns, ref-like and pointer shapes,
open generic shapes, patterns, queries, ranges, collection expressions, and
unknown future Roslyn operation kinds abstain. Explicitly annotated unsupported
methods report SP0047; unsupported unannotated methods remain silent. A closed
constructed generic API call is admitted only when its exact specification
resolves.

## Concrete call-site preconditions

Contracts are normal C# expressions bound by compiler symbol identity:

```csharp
using SharpProof.Attributes;

public static class Preconditions {
    public static void Positive(int value) {
        Contract.Requires(value > 0);
    }

    public static void BadCall() {
        Positive(-1); // SP0027 when contract features are enabled
    }
}
```

SP0027 covers ordinary invocations and object creation. It is emitted only when
the call is definitely executed, all normally evaluated receiver and argument
expressions lower exactly, the prefix is known not to throw, and the
instantiated precondition replays to `false`. Unknown arguments, conditional
execution, unsupported expressions, and potentially throwing prefixes are
silent.

Replayable top-level forms include a direct expression statement, return or
throw expression, single local initializer, simple assignment whose target is
definitely non-throwing, expression-bodied member, and constructor initializer.

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

`SharpProofVerify` remains optional in `advisory`; the `strict` profile
requires it. It runs after compilation, outside design-time builds. Each
invocation uses isolated compiler-artifact, request, and result paths. After
protocol validation, a cross-process mutex serializes publication. The stable
result is removed first; the manifest and request are atomically replaced, and
the validated result is written last as the commit marker. An interrupted
publication therefore leaves no successful result that can be mistaken for a
current manifest/request/result set. The default result is published under:

```text
obj/<Configuration>/<TargetFramework>/SharpProof/result.json
```

Worker protocol version 6 separates the project run from semantic claim
outcomes. The compiler artifact records the effective `SharpProofFeatures`
selection before manifest construction. A compiler-symbol-based manifest
selects callables and assigns stable `spc1:` semantic IDs to direct clauses,
companion clauses, and return attributes. The selected-claim manifest uses
manifest schema version 2. The response must contain exactly one result for
every manifest claim: no missing, duplicate, invented, or clause-zero
placeholder records are valid. Callable coverage is separately `Complete` or
`Incomplete`, and the run is `Complete`, `TimedOut`, `Canceled`, or `Failed`.

Each manifest claim receives:

- `Proven` carries its canonical proof core, which can be empty for a hygienic
  tautology.
- `Refuted` has a replay-validated concrete model and fails the build with
  worker exit code 5.
- `Unknown` has a closed reason such as `UnsupportedBody`,
  `DeepPostcondition`, `ResourceLimit`, or `MethodTimeout`.

`SharpProofVerifyPolicy` controls a valid incomplete result:

- `advisory` (default) emits SP0047 as information;
- `warn-on-unknown` emits SP0047 as a warning;
- `require-proven` emits SP0047 as an error and fails unless all selected
  claims are proven.

`SharpProofAssumptionPolicy` is `allow`, `warn`, or `error`. SP0048 reports
declared `Contract.Assume` and trusted-boundary evidence at the matching
severity. The advisory default is `allow`; strict defaults to `error`.

Malformed input, compiler errors, protocol/backend/replay errors, containment
failure, infrastructure failure, and a hard worker timeout fail the build
under every policy. The worker uses deterministic query, method, project,
expression-depth, memory, process, and parallelism limits. Its
content-addressed cache defaults to
`obj/<Configuration>/<TargetFramework>/SharpProof/cache` in the MSBuild
integration. Cache schema version 7 stores only a semantically complete
payload whose manifest hash and exact claim set validate against the current
request and whose outcomes are all `Proven` or replay-validated `Refuted`.
Timeout, cancellation, `Unknown`, malformed, infrastructure, and failed-replay
responses are not reusable. `require-proven` runs bypass this local semantic
cache so strict CI never relies on untrusted cached proof output.

The result includes deterministic JSON counts by outcome and reason, assumption
counts, cache status, versions, budgets, and elapsed time. SARIF projection is
opt-in: set `SharpProofVerifySarifFile` to emit deterministic SARIF 2.1.0 from
the already validated response. It preserves `Proven`, `Refuted`, `Unknown`,
SP0047, SP0048, and typed run-failure information with policy-matched levels.
The SARIF file is published atomically under the same lock as the JSON result
and cannot make an unsuccessful verifier run succeed.

The current body executor is capped at 64 reachable blocks, 64 return paths,
and 4,096 execution states. Its exact scalar subset includes Boolean logic,
equality and ordering over bounded integer types through `uint`, and checked
`long` arithmetic. Arithmetic over narrower or unsigned types, `ulong`,
native integers, floating-point, `decimal`, enum equality, unchecked
wrapping, loops, arbitrary source calls, loads/stores, mutable heap state,
unsupported conversions, excessive expression depth, and exceeded bounds
produce `Unknown`.

Manifest discovery also accounts for postconditions in local functions,
lambdas, anonymous methods, and top-level statements exactly once. Those
callable forms are not executable by the current verifier and therefore receive
visible `UnsupportedCallable` results instead of being silently omitted.

The worker also has narrow, spec-justified support for:

- `Math.Abs(int)` normal-return non-negativity;
- `string.Concat(string, string)` result non-nullness;
- `Array.Empty<T>()` result non-nullness and zero array length.

It does not treat `Enumerable.Empty<T>()` as array-backed sequence state.
During counterexample replay, an executed modeled call cannot be independently
reproduced, so the candidate becomes `Unknown` with
`CounterexampleNotReplayable`. An `Ensures` expression that can throw for a
candidate input is `Unknown` with `PostconditionMayBeUndefined`. A call on a
CFG path that the concrete model does not select does not block replay. A
genuine discrepancy while replaying an otherwise executable counterexample
remains a fatal `CounterexampleReplayFailed`.

For a SAT answer, the proof kernel first requires the backend assignments to
close exactly over the requested Boolean/integer model variables, then
re-evaluates every lowered assumption as true and the lowered goal as false.
The worker separately seeds and executes the compiler-produced whole-body
program along the model-selected CFG path, reconstructs the post-state, and
requires the original `Ensures` condition to evaluate to false. Contract-only
ordinary `void` methods use an exact zero-step replay. Constructor
postconditions are currently `UnsupportedBody` because base construction and
field initializers are not yet in the lowered body. Only a candidate that
passes both layers is emitted as `Refuted`; its JSON model exposes only
canonical user-model variables.

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

Contract clauses are not merged across sources. If a target member declares
any direct clause, all of its clauses come from that member; otherwise they may
come from its matching companion. Closed parameter and return attributes still
apply in either case.

Direct `Contract.Requires`, `Ensures`, and `Assume` clauses used by the worker
must be direct expression statements in one contiguous method-body prologue.
`Result<T>` is valid only inside `Ensures`; `Old(...)` is valid only inside
`Ensures` and cannot be nested.

The currently consumed closed value attributes are:

- `[NotNull]`
- `[Positive]`
- `[InRange(minimum, maximum)]`

On parameters they become preconditions. On method return values they become
postconditions. The attribute declarations are restricted to parameter and
return targets. The inactive `[Pure]` attribute has been removed; use
`[EnforcePure]` for the implemented effect contract.

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

The Attributes and portable SharpProof packages target `netstandard2.0` and
contain no verifier, Z3, or native solver payload.
`SharpProof.Verifier.Win-x64` carries `SharpProof.Worker` as a `net9.0` tool,
the launcher, and one Windows x64 native Z3 payload with mandatory Windows Job
Object containment.
`SharpProofVerify=true` on an unsupported host fails with an explicit
unsupported-host build error; portable analyzer features remain available.

The full acceptance workflow runs on `windows-latest`. A separate
package-consumer workflow restores the exact same three-package artifact graph
and exercises portable analyzer consumers on Windows x64, Linux x64, macOS x64,
and macOS ARM64. Only Windows x64 executes the packaged worker; every other
matrix host asserts the explicit unsupported-host rejection. Real Visual
Studio, Rider, and Windows ARM64 validation remain outstanding release gates.

Every package build runs SDK package validation and emits a matching `.snupkg`
with portable PDBs. Package tests require the main packages to remain PDB-free,
require one PDB for every shipped SharpProof assembly, parse every PDB as
portable metadata, and verify SourceLink against the exact repository commit.
The package workflow also emits a deterministic SPDX 2.3 JSON SBOM,
`SHA256SUMS`, and `SharpProof.release.json` for the six NuGet artifacts. The
SBOM binds each main package to its SHA-256 hash, inventories bundled
third-party components, and checks component versions against restored assets.
Pull-request packaging has read-only repository permission. A separate
canonical-push job attaches SLSA build-provenance and SBOM attestations;
workflow actions are pinned to immutable commits.

Release-tag promotion is allowlisted to the approved sequence. Each tag must
match the checked-in package version, identify a commit contained in `master`,
and descend from the preceding release tag. Tag `v1.0.0-preview.1` uses the
owner-protected `nuget.private-preview`
environment and its configured private source; `v1.0.0-preview.2`,
`v1.0.0-rc.1`, and `v1.0.0` use the owner-protected `nuget.org` environment
and temporary OIDC credentials. Both paths download, revalidate, and promote
the exact package bytes that passed the cross-platform consumer matrix.
Before any write, the publisher validates `SharpProof.release.json` and every
artifact hash, then queries the feed's V3 `PackageBaseAddress` for all three
IDs. An existing package is reusable only when its exact ZIP entry names and
uncompressed payloads match the tested local package; only the repository
signature entry `.signature.p7s` may differ. The publisher then sends main and
symbol packages separately in dependency order: Attributes, SharpProof, then
the verifier. `--skip-duplicate` is used only after that remote main-package
match, so an interrupted main/symbol publication can resume without accepting
unknown remote bytes. A mismatched payload or an unverified publication race
fails closed before it can be skipped.
Repository owners must configure `NUGET_PRIVATE_SOURCE` and
`NUGET_PRIVATE_API_KEY` in the private-preview environment, and `NUGET_USER`
plus a matching NuGet trusted-publishing policy in the public environment.
The private source must be an HTTPS NuGet V3 service index, and its API key
must permit V3 package reads plus package and symbol publication. NuGet V3 has
no corresponding symbol-package download resource, so retry safety anchors on
the exact downloadable main package and resubmits the tested `.snupkg`
separately. The workflow contains no feed credential values. An offline plan
and local remote-payload simulation are available through:

```powershell
.\scripts\Publish-SharpProofRelease.ps1 `
  -PackageSource nupkgs `
  -PlanOnly
```

## Closed compiler artifact and remaining release gaps

The build-only collector now emits compiler artifact schema version 3 from the
final post-generator Roslyn `Compilation`. It seals the feature-selected claim
manifest and, for each selected callable, either a typed lowering failure or
portable whole-body CFG/IR with bound contract clauses, canonical variables,
body-entry state, parameter mappings, and exact API-spec witness metadata. The
artifact also records compiler error diagnostics with mapped locations,
handwritten and generated tree hashes and parse settings, a bounded
proof-relevant compilation-option set, assembly/target identity, and
compiler/reference provenance. It contains no source text.

The artifact is the worker's sole compilation input. The worker verifies its
digest and canonical shape, requires its compiler-visible maximum expression
depth to equal the request budget, and validates exact manifest/callable/claim,
assumption, and portable-graph relationships before cache lookup or backend
creation. It hydrates the lowered IR without constructing a Roslyn compilation,
reparsing source, or rereading reference files. Compiler versions and MVIDs,
and reference paths, hashes, identities, kinds, and aliases, are provenance and
cache identity rather than a runtime compiler-compatibility gate.

Generated contracts and bodies are therefore closed into the same artifact.
`AdditionalFiles` are sealed as canonical paths and content hashes; their raw
contents are not embedded. Analyzer configuration is represented through its
observable effect on the final compilation and effective SharpProof options.
Resolver-dependent `#r` or `#load`, missing-assembly resolver mode, reference
supersession, custom assembly-identity comparers, and non-file or unreadable
references fail artifact collection as SP0049.

The compiler-to-worker reconstruction cutover, independent whole-body
counterexample-replay gate, three-package split, symbols, package validation,
hash manifest, SBOM, build attestations, immutable tagged-byte validation,
trusted-publishing workflow, public API IntelliSense coverage, and
package-backed samples are complete for this bounded subset, but SharpProof is
not production-ready. Owner configuration of protected tags and the two NuGet
publishing environments, the first private/public publications, pilot-library
evidence, and the remaining independent release reviews are still
outstanding.

## Package-backed examples

The [sample matrix](samples/README.md) demonstrates effects, preconditions,
`ContractFor`, a reviewed external boundary, library authoring, strict CI,
expected diagnostics, and exact `Proven`/`Refuted`/`Unknown` worker records.
Every sample restores packed NuGet artifacts from an isolated feed; none uses
a repository project reference.

Run the complete host-aware matrix with:

```powershell
.\scripts\Test-SharpProofSamples.ps1
```

The supported consumer surface and its XML-documentation guarantee are listed
in [Supported public API](docs/public-api.md).

## Build and validate this repository

Run long-lived .NET commands through the repository wrapper:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 restore SharpProof.sln
.\scripts\Generate-Readme.ps1 -Verify
.\eng\acceptance\Verify.ps1 -Configuration Release
```

The acceptance gate enforces the dependency graph and proof-kernel-only size
ratchet,
builds the solution, runs architecture and banned-API checks, lattice and
finite-CFG laws, runtime and differential oracles, worker/package integration,
cache/concurrency/cancellation tests, the pinned corpus, a fixed-seed
1,000-case fuzz run, and performance budgets.

## Documentation

- [Documentation index](https://github.com/alexyorke/SharpProof/blob/master/docs/README.md)
  is the complete maintained-doc map.
- [SEMANTICS.md](https://github.com/alexyorke/SharpProof/blob/master/SEMANTICS.md)
  is the normative soundness boundary and wins if another document conflicts
  with it.
- [Architecture](https://github.com/alexyorke/SharpProof/blob/master/docs/architecture.md)
  describes the production dependency graph and proof boundary.
- [Coverage and limits](https://github.com/alexyorke/SharpProof/blob/master/docs/coverage-and-limits.md)
  summarizes admitted and rejected product areas.
- [Analysis limits](https://github.com/alexyorke/SharpProof/blob/master/docs/analysis-limits.md)
  lists shipping worker and performance budgets.
- [Diagnostics](https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md)
  documents analyzer and `ContractFor` generator IDs.
- [Supported public API](https://github.com/alexyorke/SharpProof/blob/master/docs/public-api.md)
  defines the contract API compatibility boundary.
- [Package-backed samples](https://github.com/alexyorke/SharpProof/blob/master/samples/README.md)
  exercise passing, diagnostic, and mixed worker outcomes.
- [Typed Unknown reasons](https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md)
  explains fail-closed abstentions.
- [Native SMT packaging](https://github.com/alexyorke/SharpProof/blob/master/docs/native-smt-packaging.md)
  describes analyzer and worker payload separation.
- [SMT lifecycle](https://github.com/alexyorke/SharpProof/blob/master/docs/smt-lifecycle.md)
  describes solver ownership and disposal.
- [API result domains](https://github.com/alexyorke/SharpProof/blob/master/docs/soundness-notes/2026-07-25-api-spec-result-domains.md)
  records the bounded nullness/cardinality integration.
- [Hardening audit](https://github.com/alexyorke/SharpProof/blob/master/docs/soundness-notes/2026-07-25-hardening.md)
  records validation evidence and outstanding checkpoints.
- [Product bug sweep](https://github.com/alexyorke/SharpProof/blob/master/docs/soundness-notes/2026-07-27-product-sweep.md)
  records the latest analyzer, contract, effect, and worker adversarial review.
- [Acceptance contract](https://github.com/alexyorke/SharpProof/blob/master/eng/acceptance/README.md)
  describes the active release gate.

## Project policies

- [MIT License](https://github.com/alexyorke/SharpProof/blob/master/LICENSE)
- [Security policy](https://github.com/alexyorke/SharpProof/blob/master/SECURITY.md)
- [Contributing guide](https://github.com/alexyorke/SharpProof/blob/master/CONTRIBUTING.md)
- [Changelog](https://github.com/alexyorke/SharpProof/blob/master/CHANGELOG.md)
