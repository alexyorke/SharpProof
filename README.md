# SharpProof

SharpProof 1.0.0-preview.1 is a soundness-first Roslyn analyzer and bounded
out-of-process verifier for C#. It deliberately supports a narrow,
compiler-bound subset.

SharpProof has three semantic outcomes:

- `Proven`: the goal follows from exact lowering and accountable evidence.
- `Refuted`: an executable postcondition counterexample or an admitted direct
  allocation-effect violation was independently replayed.
- `Unknown`: the language, model, evidence, or resource budget was insufficient.

The default `advisory` profile is quiet for unannotated code. Unsupported code
that is explicitly selected by a SharpProof contract or annotation produces
SP0047 instead of disappearing. Diagnostic silence is still not a proof.

## What works today

| Surface | Current capability | User-visible result |
|---|---|---|
| Effect analyzer | Checks `[EnforcePure]`, `[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`, and `[AllowedExceptions]` over the admitted source subset | Advisory "not proven" diagnostics on selected code |
| Contract analyzer | Replays definitely executed, compiler-bound `Contract.Requires(...)` clauses with exact call inputs | SP0027 only when the precondition concretely evaluates to false |
| Worker | Builds an accountable claim manifest, verifies bounded `Contract.Ensures(...)` obligations over acyclic scalar bodies, composes bounded relational callee summaries, and independently replays the admitted direct-allocation effect evidence | One `Proven`, replay-validated `Refuted`, or typed `Unknown` result for every manifest claim |

The analyzer does not run SMT or load Z3. The worker can compose inferred,
quantifier-free relations for direct, acyclic, static scalar callees from
current source or an exact implementation PE, plus explicitly enabled audited
specification packs. General modular source-callee verification, loops,
recursion, mutable-heap postconditions, points-to analysis, and broad reference
or sequence reasoning are not implemented.

## Install and enable

The portable analyzer and generator require a compiler host with Roslyn 4.14
or newer. The canonical development image supplies the exact .NET SDK version
pinned in `global.json` (currently 9.0.316; roll-forward is disabled); it is
not a host prerequisite. The package-consumer compatibility lane separately validates the
`netstandard2.0` contract API with its minimum SDK, currently 9.0.300. The
`SharpProof.Attributes` contract API alone remains a `netstandard2.0` library,
and `SharpProofProfile=off` omits analyzer/generator loading on an older host.
The preview verifier is qualified only in the repository's pinned Linux amd64
container using Core MSBuild. Docker Engine or Docker Desktop with Compose v2
is the only host prerequisite. Native host execution, Visual Studio verifier
execution, Rider, and ARM64 verifier containers are not supported for this
preview. The portable analyzer remains separately cross-platform. The exact
host and filesystem boundary is listed in
[Preview support boundary](docs/preview-support.md).
The permanent editor workflow is documented in
[Container development](docs/container-development.md).

For repository development, Docker is the only required tool. Start the
persistent environment once and work inside it so incremental outputs survive:

```text
docker compose build tooling
docker compose up -d dev
docker compose exec dev sharpproof-dev-init
docker compose exec dev bash
```

For a permanent editor environment, open the repository in VS Code and choose
**Dev Containers: Reopen in Container**. The container validates its pinned
contract, clones the configured remote branch with container Git, and performs
a locked restore once. No host initialization command runs. Its terminal runs
as the non-root `sharpproof` user and exposes the same commands without nested
Docker. Source, Git state, build outputs, and artifacts live in a persistent
Compose workspace volume:

```text
sp test-changed
sp check
sp build
sp portable-tests
sp worker-tests
sp package-tests
sp acceptance -Configuration Release
```

`sp test-changed` is the shortest edit-loop check. `sp check` runs one
incremental build plus duration-aware semantic, Worker, package, and performance
smoke shards. Disposable `docker compose run --rm tooling ...` commands remain
the clean qualification path and intentionally discard build outputs.

The default container budget is 16 CPUs and 40 GiB. Test-project concurrency is
derived from the CPUs visible inside the container (one lane per two CPUs), so
changing `SHARPPROOF_CONTAINER_CPU_LIMIT` also changes orchestration without a
second hardcoded worker count. `SHARPPROOF_TEST_PROJECT_PARALLELISM` is an
explicit diagnostic override and cannot exceed the visible CPU count.

Compose derives its default project name from the source directory. Put a
distinct `COMPOSE_PROJECT_NAME` and optional `SHARPPROOF_DEV_REF` in each
checkout's untracked `.env` file. The persistent source volume, NuGet cache,
and .NET home are then private to that Compose project. Finite task commands
clone into a temporary container workspace instead of writing host `bin` or
`obj` trees; only deliberate evidence is copied to the mounted checkout's
`artifacts` folder.

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
default). The effective selection is sealed into the schema-12 compiler
artifact and filters its manifest: `contracts` excludes effect-only
annotations, `effects` excludes postcondition claims and contract assumptions,
and `all` selects both surfaces. Every effective effect contract has one typed
claim backed by sealed compiler evidence, with incomplete summaries reported
as `Unknown` and SP0047. A custom analyzer host
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
SP0015, and SP0030 remain reserved live-analyzer diagnostics; the current
may-effect analyzer does not emit them. The opt-in worker separately replays
the narrow allocation evidence described below.

The retired `SharpProofMode`/`sharpproof_mode` preview alias is rejected.
Use only `SharpProofProfile` and `SharpProofFeatures`; the preview interface is
now frozen on those properties.

For strict CI:

```xml
<ItemGroup>
  <PackageReference Include="SharpProof.Verifier"
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

Effect analysis is a conservative two-phase may analysis. A bounded acyclic
CFG pass first refines scalar reachability and safety facts; the effect pass
then joins `EffectSummary` facts path-insensitively across the remaining
branches. Impossible refined branches do not contribute effects. Exceeding the
block or operation budget, or encountering a reachable cycle, gives every
selected effect contract a typed `Unknown` result and SP0047 evidence. It never
becomes a proof, although an independently replayable direct witness can still
produce `Refuted`.

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
        PreconditionFree = true,
        IsDeterministic = true)]
    public static extern int ReadExternalState();
}
```

`EffectContractAttribute` defaults to no declared capability or escaping
exception, `Complete=false`, `IsDeterministic=false`, and
`PreconditionFree=false`. A reviewed boundary must explicitly certify a
precondition-free metadata envelope before another assembly can consume the
summary, opt into every stronger fact, and describe the whole observable call
boundary. Trust without an explicit complete contract proves nothing. A
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

That subset boundary applies to selected effect and verifier-body analysis.
The lighter call-site precondition pass also follows executable local-function,
lambda, and anonymous-method CFGs so a definite bad nested call still reports
SP0027. It assigns each result to the nested callable, treats captured facts
conservatively, and does not treat quoted expression-tree lambdas as executing
delegates.

## Call-site preconditions

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

SP0027 covers ordinary invocations and object creation. Exact concrete
expressions use compiler-bound IR replay. Other definitely executed calls use
a compilation-scoped CFG analysis that tracks Boolean facts, nullness, integer
intervals, sequence cardinality, and effects through branches and joins. The
analysis refines both scalar operands on comparison edges and propagates caller
`Requires` clauses and parameter attributes, so it
can establish non-null, division, bounds, range, and overflow facts without
running Z3 in the live analyzer.

Executable local functions, lambdas, and anonymous methods are checked through
their Roslyn child CFGs exactly once. Their outcomes are not folded into the
containing method. Captured values that cannot be established at the nested
entry remain `Unknown`; expression-tree lambdas are quoted code and are not
reported as executing call sites.

A violation is emitted only when the receiver and argument prefix is known to
complete normally and the instantiated precondition is definitely false.
Conditional execution, unsupported expressions, potentially throwing
prefixes, and exhausted analysis budgets do not become violations or proofs.

Replayable top-level forms include a direct expression statement, return or
throw expression, single local initializer, simple assignment whose target is
definitely non-throwing, expression-bodied member, and constructor initializer.

`Contract.Requires`, `Contract.Ensures`, and `Contract.Assume` carry
`[Conditional("SHARPPROOF_CONTRACTS")]`. Normal builds therefore erase the
calls and do not evaluate their arguments. They are static-analysis contracts,
not runtime guards.

SharpProof analysis rejects `SHARPPROOF_CONTRACTS` because defining it emits
the ghost contract calls and evaluates their arguments. The portable package
also rejects the symbol in `DefineConstants`; compiler-side validation covers
source-local directives and generated trees. Set `SharpProofProfile=off` only
when intentionally compiling without SharpProof analysis.
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

Opt into worker execution inside the canonical container:

```text
docker compose run --rm tooling dev -lc \
  'dotnet build /p:SharpProofVerify=true'
```

`SharpProofVerify` remains optional in `advisory`; the `strict` profile
requires it. It runs after compilation, outside design-time builds. Each
invocation uses isolated compiler-artifact, request, and result paths. After
protocol validation, one ordered cross-process lock set covers the request,
result, manifest, and optional SARIF paths. Partially overlapping publication
configurations are rejected. The stable result is removed first; the manifest
and request are atomically replaced, and the validated result is written last
as the commit marker. An interrupted publication therefore leaves no
successful result that can be mistaken for a current manifest/request/result
set. The default result is published under:

```text
obj/<Configuration>/<TargetFramework>/SharpProof/result.json
```

Worker protocol version 11 separates the project run from semantic claim
outcomes. The compiler artifact records the effective `SharpProofFeatures`
selection before manifest construction. A compiler-symbol-based manifest
selects callables and assigns stable `spc1:` semantic IDs to direct clauses,
companion clauses, return attributes, and every selected effect-attribute
occurrence. Repeated effect attributes share their effective combined
constraint and evidence, but retain distinct IDs and dense ordinals. The
selected-claim manifest uses manifest schema version 4. Each
`EnforcePure`, `ZeroAllocations`, `AllowedCapabilities`, `DoesNotThrow`,
`AllowedExceptions`, or `EffectContract` occurrence has one typed effect
claim. The response must contain exactly one result for
every manifest claim: no missing, duplicate, invented, or clause-zero
placeholder records are valid. Callable coverage is separately `Complete` or
`Incomplete`, and the run is `Complete`, `TimedOut`, `Canceled`, or `Failed`.

Each manifest claim receives:

- `Proven` carries its canonical proof core, which can be empty for a hygienic
  tautology.
- `Refuted` requires independent replay and fails the build with worker exit
  code 5. It currently covers postconditions with a replay-validated concrete
  model and the narrow direct-allocation effect evidence described below.
- `Unknown` has a closed reason such as `UnsupportedBody`,
  `DeepPostcondition`, `EffectSummaryIncomplete`,
  `EffectContractNotEstablished`, `ResourceLimit`, or `MethodTimeout`.

Effect claims use canonical compiler-produced evidence. They are `Proven` only
when a complete effect summary establishes the selected contract. Compiler
artifact schema 12 retains schema 10's independently replayable,
unconditional direct event for a definite managed object or array allocation.
The worker validates the event's order, source-tree hash and span, semantic
identity, selected constraint, and compiler witness, then derives the
`Allocates` effect itself. A successful replay can refute
`[ZeroAllocations]` or `[EffectContract]` when its allowed-effect set excludes
`Allocates`. `[EnforcePure]` is observable purity and permits fresh allocation,
so allocation evidence does not refute it.

The compiler does not publish other direct candidates as refutations. Definite
explicit throw, field access, `lock`/`Monitor`, static-initialization-sensitive
allocation, and other non-replayable direct candidates become
`Unknown(CounterexampleNotReplayable)`. Conditional, path-dependent, and other
may-only conflicts remain `Unknown(EffectContractNotEstablished)`; incomplete
summaries remain `Unknown(EffectSummaryIncomplete)`. Invalid replay structure
is malformed compiler evidence and fails the run. A semantic disagreement
during an otherwise valid replay becomes the fatal
`Unknown(CounterexampleReplayFailed)`. Effect results remain noncacheable.

Proven postconditions explicitly record `ContradictoryPreconditions` or
`NoModeledNormalReturn` when the proof is vacuous under partial-correctness
semantics. This evidence is preserved in canonical JSON and SARIF; an ordinary
non-vacuous proof records `None`. Proven claims do not enter the disk cache.

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
integration. Cache schema version 13 stores only complete, postcondition-only
responses whose claims are all `Refuted`. Before accepting a hit, the worker
reconstructs every canonical Boolean/integer model against the current lowered
callable, validates entry assumptions and source ranges, and independently
replays the whole body and postcondition. Proven claims, effect claims,
unsupported models, timeout, cancellation, `Unknown`, malformed,
infrastructure, and failed-replay responses are neither written nor reused.
`require-proven` runs bypass this local semantic cache.

The result includes deterministic JSON counts by outcome and reason, assumption
counts, cache status, protocol/tool/spec versions, the canonical packaged
worker runtime-closure digest and API spec-content SHA-256 identity, budgets,
and elapsed time. SARIF projection is
opt-in: set `SharpProofVerifySarifFile` to emit deterministic SARIF 2.1.0 from
the already validated response. It preserves `Proven`, `Refuted`, `Unknown`,
SP0047, SP0048, and typed run-failure information with policy-matched levels.
The SARIF file is published atomically under the same lock as the JSON result
and cannot make an unsuccessful verifier run succeed.

The current body executor is capped at 64 reachable blocks, 4,096 lowered body
instructions, and 65,536 symbolic operations. It merges acyclic predecessor
states with symbolic path predicates rather than enumerating a fixed number of
paths or states. Its exact scalar subset includes Boolean logic,
equality and ordering over bounded integer types through `uint`, and checked
`long` arithmetic. Arithmetic over narrower or unsigned types, `ulong`,
native integers, floating-point, `decimal`, enum equality, unchecked
wrapping, loops, calls outside the admitted relational/specification boundary,
loads/stores, mutable heap state,
unsupported conversions, excessive expression depth, and exceeded bounds
produce `Unknown`.

### Bounded relational callee summaries

For an eligible direct call, the build-time compiler collector derives the
callee relation instead of relying on a method-name-specific verifier branch.
All admitted origins lower to the same typed IR relation and are composed into
the caller's Z3 obligation:

1. a single current-compilation source declaration with an exact acyclic body;
2. an exact file-backed implementation PE whose metadata is byte-equal to the
   Roslyn reference and which is not marked as a reference assembly; or
3. an explicitly enabled, embedded, schema-1 audited specification pack.

The initial boundary is static, non-generic Boolean and supported-integer
parameters/results with no `ref` shape, virtual dispatch, heap access, loops,
or recursion. Implementation IL is decoded only through a bounded scalar
opcode allowlist. Reference assemblies, facades, missing or changed bodies,
unsupported opcodes, recursive dependencies, and exhausted budgets abstain as
typed `Unknown`; they are never treated as implementation proof authority.
Every composed call seals its origin, evidence digest, optional pack identity,
and complete transitive dependency-evidence closure into compiler artifact
schema 12. Relational-summary schema version 1 and specification-pack schema
version 1 govern those evidence records.

Specification packs are off by default. The preview ships one data-driven
pack, `dotnet.scalar@1`, whose current audited relation covers
`System.Math.Max(int, int)`. Enable it explicitly:

```xml
<PropertyGroup>
  <SharpProofSpecificationPacks>dotnet.scalar</SharpProofSpecificationPacks>
</PropertyGroup>
```

Unknown pack IDs fail artifact collection. Pack selection and exact catalog
content are sealed into the artifact; a pack cannot be supplied from an
arbitrary consumer file.

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
any valid direct clause, all of its clauses come from that member; otherwise
they may come from its matching companion. A recognized direct clause with
invalid placement still produces SP0024, but it neither displaces a valid
companion nor contributes executable argument effects: the whole
compiler-elided invocation is omitted. Closed parameter and return attributes
still apply in either case.

Direct `Contract.Requires`, `Ensures`, and `Assume` clauses used by the worker
must be direct expression statements in one contiguous method-body prologue.
`Result<T>` is valid only inside `Ensures`; `Old(...)` is valid only inside
`Ensures` and cannot be nested.

SharpProof accepts these APIs only from the `SharpProof.Attributes` assembly
whose exact name/version identity and payload SHA-256 match the analyzer
payload. The unsigned package makes no public-key authenticity claim. It also
validates the exact `Contract` type and requires one real
`[Conditional("SHARPPROOF_CONTRACTS")]` on each clause method. Source or
project lookalikes, mismatched package identities, and malformed clause APIs
contribute no facts and report SP0047; a rejected `ContractFor` lookalike
reports SPCF0001. The compiler-bound ghost API specifications use this same
identity and shape gate.

The currently consumed closed value attributes are:

- `[NotNull]`
- `[Positive]`
- `[InRange(minimum, maximum)]`

On parameters they become preconditions. On method return values they become
postconditions. The attribute declarations are restricted to parameter and
return targets. The inactive `[Pure]` attribute has been removed; use
`[EnforcePure]` for the implemented effect contract.

## Exact built-in API specifications

The default table contains these eleven BCL rows:

| API | Current modeled facts |
|---|---|
| `Array.Empty<T>()` | Effects and allocation unknown across type initialization; does not throw; non-null empty array result |
| `Exception()` | Writes only the fresh receiver, has no additional allocation, and does not throw |
| `Exception(string)` | Writes only the fresh receiver, has no additional allocation, and does not throw |
| `InvalidOperationException()` | Writes only the fresh receiver, has no additional allocation, and does not throw |
| `InvalidOperationException(string)` | Writes only the fresh receiver, has no additional allocation, and does not throw |
| `object` constructor | Call boundary has no effects or allocation and does not throw; `new object()` still allocates the object |
| `string.Length` | Reads receiver state; no allocation; does not throw inside the resolved call boundary |
| `string.Concat(string, string)` | No side effects; may allocate; does not throw; non-null result |
| `List<T>.Add(T)` | Writes receiver state; may allocate; throw behavior unknown |
| `Math.Abs(int)` | No side effects or allocation; may throw `OverflowException`; normal result is non-negative |
| `Enumerable.Empty<T>()` | Effects and allocation unknown across type initialization; does not throw; non-null empty enumerable fact |

Each row binds to an approved assembly name, public-key token, and reference
family. Reference-pack families additionally require compiler metadata marked
with `ReferenceAssemblyAttribute`; the runtime family rejects that marker.
Missing, ambiguous, spoofed, or target-framework-inapplicable rows fail closed.

## Target frameworks and hosts

The checked-in acceptance contract declares these consumer target frameworks:

- `netstandard2.0`
- `net8.0`
- `net472`

The Attributes and portable SharpProof packages target `netstandard2.0` and
contain no verifier, Z3, or native solver payload.
`SharpProof.Verifier` carries `SharpProof.Worker` as a `net9.0` tool, the
launcher, build tasks, and one pinned Linux x64 native Z3 payload. Docker is the
hard CPU and memory boundary; SharpProof retains semantic and wall-clock
budgets but does not duplicate cgroup enforcement.
`SharpProofVerify=true` outside the canonical Linux amd64 container fails with
an explicit unsupported-host build error; portable analyzer features remain
available.

The full acceptance and package-consumer workflows run in the pinned container.
The portable analyzer remains operating-system-neutral, while all repository
qualification uses the same Linux toolchain. Container qualification covers
percent-containing, Unicode, space-containing, and long local paths, cache,
SARIF, cancellation, and cooperative publication. Native host installs,
Visual Studio verifier execution, Rider, ARM64 verifier containers,
UNC/shared-network publication, and hostile concurrent filesystem mutation are
outside this preview's supported boundary.

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
the exact package bytes that passed the container consumer matrix.
Before any write, the publisher validates `SharpProof.release.json` and every
artifact hash, then queries the feed's V3 `PackageBaseAddress` for all three
IDs and rejects any existing main package. The publisher then sends main and
symbol packages separately in dependency order: Attributes, SharpProof, then
the verifier. Publication is intentionally non-overwriting, and no push uses
`--skip-duplicate`, so an existing symbol package or publication race also
fails the release. An interrupted publication must use a new package version
rather than treating remote bytes as reusable.
Repository owners must configure `NUGET_PRIVATE_SOURCE` and
`NUGET_PRIVATE_API_KEY` in the private-preview environment, and `NUGET_USER`
plus a matching NuGet trusted-publishing policy in the public environment.
The private source must be an HTTPS NuGet V3 service index, and its API key
must permit V3 package reads plus package and symbol publication. NuGet V3 has
no corresponding symbol-package download resource, so symbol-package
nonexistence is enforced by a push without duplicate skipping. The workflow
contains no feed credential values. An offline plan and local remote-presence
simulation are available through:

```text
docker compose run --rm tooling release-plan -PackageSource nupkgs
```

For a real publication, the publisher resolves an absolute `dotnet` host and
requires its SDK version to match the repository's `global.json`; project-local
host shadowing and arbitrary relative overrides are rejected before any push.

## Closed compiler artifact and remaining release gaps

The build-only collector now emits compiler artifact schema version 12 from the
final post-generator Roslyn `Compilation`. It seals the feature-selected claim
manifest and, for each selected callable, either a typed lowering failure or
portable whole-body CFG/IR with bound contract clauses, canonical variables,
body-entry state, parameter mappings, exact API-spec witness metadata, and
canonical relational-summary calls and per-effect outcome/reason/evidence
digests. Summary calls carry their source, exact implementation-IL, or audited
pack identity plus their transitive dependency-evidence closure. For the admitted direct
managed object/array allocations, it also seals the ordered unconditional
event, exact constraint identity, semantic operation identity, and source-tree
span needed for independent worker replay. Worker protocol version 11 and cache
schema version 13 carry this wire break. Relational-summary schema version 1
and specification-pack schema version 1 govern the new evidence. The artifact
also records compiler error
diagnostics with mapped locations,
handwritten and generated tree hashes and parse settings, a bounded
proof-relevant compilation-option set, assembly/target identity, and
compiler/reference provenance. It contains no source text.

The semantic-operation hash is a canonical consistency check over the
compiler-produced event fields; it is not a second source binding. Contract
discovery, effect analysis, and event lowering remain explicit parts of the
trusted computing base.

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
evidence, and the exact-candidate release run are still outstanding. Preview
trusted-boundary changes use the solo executable-evidence gate; they do not
require a reviewer count or time-based freeze. Stable 1.0 governance remains a
separate post-preview policy.

## Package-backed examples

The [sample matrix](samples/README.md) demonstrates effects, preconditions,
`ContractFor`, a reviewed external boundary, library authoring, strict CI,
expected diagnostics, and exact `Proven`/`Refuted`/`Unknown` worker records.
Every sample restores packed NuGet artifacts from an isolated feed; none uses
a repository project reference.

Run the complete package-backed matrix with:

```text
docker compose run --rm tooling samples -Configuration Release
```

The supported consumer surface and its XML-documentation guarantee are listed
in [Supported public API](docs/public-api.md).

## Build and validate this repository

Run repository tooling only in the canonical container:

```text
docker compose up -d dev
docker compose exec dev bash
sp test-changed
sp check
```

Run the clean exact qualification only for a coherent candidate:

```text
docker compose run --rm tooling acceptance -Configuration Release
```

The acceptance gate enforces the dependency graph, exact trusted-boundary path
inventories, and formatting-neutral Roslyn complexity ratchets,
builds the solution, runs architecture and banned-API checks, lattice and
finite-CFG laws, runtime and differential oracles, worker/package integration,
cache/concurrency/cancellation tests, the pinned corpus, a fixed-seed
1,000-case fuzz run, and performance budgets. Corpus cases carry explicit
reviewed support labels independently from their expected verdicts and
snapshots: supported cases have zero `Unknown` tolerance, supported totals
cannot decrease, and intentionally unsupported Unknown buckets are capped by a
checked-in ratchet.
Nightly and release qualification additionally require every deterministic
trusted-boundary mutation, including both replay paths, to be killed and retain
the commit-bound JSON evidence.

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
- [Formatting-neutral source metrics](https://github.com/alexyorke/SharpProof/blob/master/docs/soundness-notes/2026-07-29-formatting-neutral-source-metrics.md)
  records the readable formatting policy and structural complexity gates.
- [Allocation effect replay](https://github.com/alexyorke/SharpProof/blob/master/docs/soundness-notes/2026-07-30-allocation-effect-replay.md)
  records the independently interpreted allocation-refutation boundary.
- [Acceptance contract](https://github.com/alexyorke/SharpProof/blob/master/eng/acceptance/README.md)
  describes the active release gate.

## Project policies

- [MIT License](https://github.com/alexyorke/SharpProof/blob/master/LICENSE)
- [Security policy](https://github.com/alexyorke/SharpProof/blob/master/SECURITY.md)
- [Contributing guide](https://github.com/alexyorke/SharpProof/blob/master/CONTRIBUTING.md)
- [Changelog](https://github.com/alexyorke/SharpProof/blob/master/CHANGELOG.md)
