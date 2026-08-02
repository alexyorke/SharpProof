# Analysis limits

SharpProof has two kinds of limits:

- shipping defaults passed by the NuGet build-transitive integration; and
- acceptance-only thresholds used to decide whether the repository is
  releasable.

They have different sources.
The portable `SharpProof.props` and `SharpProof.targets` define analyzer paths,
profile/feature defaults, and verifier-package requirements.
`SharpProof.Verifier.Win-x64.props` and
`SharpProof.Verifier.Win-x64.targets` define worker budgets, policy defaults,
compiler-manifest properties, paths, invocation, and host enforcement.
`SharpProof.Worker.Protocol/ProtocolModel.schema.json` is the authoritative
model, and checked-in `ProtocolModel.generated.cs` defines the matching runtime
defaults and validation bounds. The release gate mirrors selected values in
`eng/acceptance/contract.json` and verifies that they agree.
`SharpProof.Frontend/CSharpScalarSemantics.json` is the corresponding review
source for admitted integer widths and ranges, value-preserving conversions,
checked behavior, Roslyn-to-IR and inverse mappings, comparison relations, and
the ordered IR type/operator vocabulary and canonical metadata; CI verifies
both generated C# projections before building.

## Package and worker defaults

| MSBuild property | Default | Purpose | Authoritative source |
|---|---:|---|---|
| `SharpProofProfile` | `advisory` | Analyzer/build posture: `advisory`, `strict`, or `off` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofFeatures` | `all` | Analyzer and worker-manifest features: `effects`, `contracts`, or `all` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofVerifyPolicy` | `advisory`; strict defaults to `require-proven` | Incomplete selected-analysis policy: `advisory`, `warn-on-unknown`, or `require-proven` | verifier targets; mirrored by `contract.json` |
| `SharpProofAssumptionPolicy` | `allow`; strict defaults to `error` | User/trusted evidence policy: `allow`, `warn`, or `error` | verifier targets; mirrored by `contract.json` |
| `SharpProofMode` | unset | Deprecated preview alias for profile/features | `SharpProof.targets` |
| `SharpProofVerify` | `false`; strict requires `true` | Optional advisory worker execution; mandatory in strict | `SharpProof.targets` |
| `SharpProofVerifyQueryRlimit` | `3000000` | Z3 resource limit for one query | verifier props and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodRlimit` | `20000000` | Aggregate resource allowance for one method | verifier props and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodWallTimeMilliseconds` | `10000` | Outer method wall boundary | verifier props and `WorkerBudgets`; mirrored as 10 seconds by `contract.json` |
| `SharpProofVerifyProjectWallTimeMilliseconds` | `300000` | Outer project wall boundary | verifier props and `WorkerBudgets`; mirrored as 300 seconds by `contract.json` |
| `SharpProofVerifyMaxParallelism` | `4` | Maximum concurrent worker method verification | verifier props and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMaximumExpressionDepth` | `64` | Compiler-visible proof-obligation term depth sealed into the artifact; worker request must match | verifier props, `FinalCompilationCollector`, and `WorkerBudgets`; not present in `contract.json` |
| `SharpProofVerifyProcessMemoryLimitBytes` | `2147483648` | Windows Job Object memory limit | verifier props and `WorkerBudgets`; mirrored as 2048 MiB by `contract.json` |
| `SharpProofVerifyMaxWorkerProcesses` | `4` | Windows Job Object active-process limit | verifier props and `WorkerBudgets`; not present in `contract.json` |
| `SharpProofVerifyTerminationGraceMilliseconds` | `1000` | Grace added to the project boundary before forced termination | verifier props and `WorkerLauncherDefaults`; mirrored by `contract.json` |
| `SharpProofVerifyCacheEnabled` | `true` | Enables the content-addressed disk cache | verifier props and `WorkerCacheOptions`; not present in `contract.json` |
| `SharpProofVerifyCacheMaximumBytes` | `536870912` | Maximum cache size, 512 MiB | verifier props and `WorkerCacheOptions`; mirrored by `contract.json` |
| `SharpProofVerifySarifFile` | unset | Opt-in deterministic SARIF 2.1.0 output path | verifier targets |
| `SharpProofDotNetHost` | `dotnet` | Host used to start the launcher | verifier props |

`SharpProofVerifyCacheDirectory` is initialized by the verifier targets beneath
the project's intermediate output, normally
`obj/<Configuration>/<TargetFramework>/SharpProof/cache`.
`SharpProofVerifyRequestFile` and `SharpProofVerifyResultFile` are initialized
beside it. Concurrent builds use isolated invocation paths beneath
`SharpProof/runs`. Validated writers are serialized by a cross-process mutex.
The stable result is removed first, the compiler manifest and request are
atomically replaced, and the validated result is written last as the commit
marker. Interrupted publication therefore leaves no successful result for a
partly updated evidence set.

When `SharpProofVerifySarifFile` is nonblank, the launcher projects only the
validated response and atomically writes SARIF under the same publication
mutex before committing the JSON result. Claim outcomes, SP0047 incomplete
coverage, SP0048 assumption evidence, and run failures retain policy-matched
levels; SARIF generation cannot change verifier exit behavior.

Windows verification also initializes an internal, compiler-visible
`_SharpProofCompilerManifestPath` beneath the isolated invocation directory.
The final analyzer compilation atomically writes the manifest there. The
launcher snapshots that file by absolute path and SHA-256 before starting the
worker, and a successful invocation publishes the same artifact to
`SharpProofCompilerManifestFile`, normally
`obj/<Configuration>/<TargetFramework>/SharpProof/compiler-manifest.json`.
Missing compiler evidence fails the build as SP0049 before worker launch.

`SharpProofMode` values `off`, `effects`, `contracts`, and `all-experimental`
remain deprecated compatibility inputs during preview. Do not combine the
alias with `SharpProofProfile` or `SharpProofFeatures`.
`SharpProofVerify=true` invokes the packaged worker target only for
non-design-time builds on the supported Windows x64 host. Every unsupported
host receives an explicit build error; portable analyzer features remain
available. Strict rejects `SharpProofVerify=false`.

## Protocol validation bounds

The worker rejects malformed budgets before analysis:

- query and method rlimits must be positive, and query rlimit cannot exceed
  method rlimit;
- method and project wall times must be positive, and method time cannot exceed
  project time;
- parallelism and the Job Object process limit must each be from 1 through 4;
- expression depth must be from 1 through 256;
- process memory must be positive;
- cache size must be from 1 byte through 512 MiB.

The configured method and project wall values are fail-closed outer boundaries,
not proof facts. Z3 queries use deterministic resource limits. The launcher
uses the project wall limit plus the termination grace to enforce a final
process boundary.

`SharpProofVerifyMaximumExpressionDepth` is also a compiler-visible property.
The collector parses it, enforces the 1-through-256 range, and seals it into the
schema-9 compiler artifact. The launcher supplies the same property as the
worker request budget. A mismatch is `CompilerManifestMismatch` and stops
before cache lookup or backend creation; neither side may silently use a
different depth.

Every budget and every artifact byte participates in worker input and cache
identity. The artifact contains portable lowered callables plus a bounded
proof-relevant compiler snapshot; it does not claim to serialize every Roslyn
diagnostic or host option. The compilation hash covers handwritten and
generated tree hashes and parse settings, bounded compilation options,
assembly/target identity, compiler provenance, and reference provenance. The
worker does not read the trees or references again. Raw analyzer inputs are not
retained, but a change that affects final generated trees, selected claims, or
lowered IR changes the artifact identity. Changing a limit or captured compiler
input cannot reuse an answer produced under a different identity.
Verification and assumption policy are reporting/build policies, not semantic
proof inputs, so they do not alter the semantic cache payload.

Before launch, runtime-closure identity is also bounded and streamed. The
closure permits at most 64 logical components and 64 MiB in total. A component
identity is limited to 256 characters; an ordinary component is limited to
32 MiB, the worker dependency manifest to 1 MiB, and the runtime configuration
to 64 KiB. Dependency JSON depth is limited to 32. The launcher opens each
component read-only without write sharing while hashing it, and any missing,
oversized, escaping, or malformed closure fails before worker execution.

`SharpProofFeatures` is a semantic compiler-artifact input. `contracts` excludes
effect-only annotations from the manifest; `effects` excludes postcondition
claims and contract assumptions; `all` includes both.

## Fixed portable analyzer bounds

The live analyzer's compilation-scoped managed CFG pass accepts at most 256
Roslyn CFG blocks and 4,096 descendant operations per callable. Crossing either
budget produces typed incomplete evidence; incomplete flow cannot discharge a
call-site precondition. Reachable cycles are retained in effect summaries as
`MayDiverge` termination evidence, so a cycle alone does not erase modeled
effects or turn an otherwise accountable effect claim into SP0047. Unsupported
shapes and budget failures still fail closed. These bounds are deterministic
and the analyzer never loads Z3.

## Fixed worker body bounds

The compiler lowerer and acyclic predicate executor have three fixed,
non-configurable bounds for one admitted body:

| Bound | Limit |
|---|---:|
| Reachable CFG blocks | 64 |
| Lowered body instructions | 4,096 |
| Symbolic operations | 65,536 |

Crossing the reachable-block or lowered-instruction bound returns `Unknown`
with `UnsupportedBody`; exhausting the symbolic-operation budget returns
`Unknown` with `ResourceLimit`. Neither path produces a partial proof. The
executor merges predecessor states with symbolic path predicates instead of
enumerating a fixed number of paths or states. Loops are rejected by the
acyclic-body check before symbolic execution.

The manifest discovers local functions, lambdas, anonymous methods, and the
top-level entry point, including their directly owned postconditions. These
forms currently remain outside worker execution and produce
`UnsupportedCallable`; nested clauses are assigned only to their owning
callable.

The portable call-site precondition pass is narrower than worker execution but
does traverse executable local-function, lambda, and anonymous-method child
CFGs. It analyzes each nested body once with its own scalar flow state and
keeps its outcome separate from the containing method. Captured entry values
that cannot be established remain unknown. Quoted expression-tree lambdas are
not treated as executing delegates.

## Acceptance-only thresholds

These values come from `eng/acceptance/contract.json`. They are repository
release gates, not end-user MSBuild defaults.

| Gate | Current threshold |
|---|---:|
| Pull-request fuzz cases | 1,000 |
| Nightly fuzz cases | 10,000 |
| Fuzz parallelism | At most 4 |
| Cancellation p95 | At most 250 ms |
| Forced termination | At most 1,000 ms |
| Performance warmups | 5 |
| Performance samples | 30 |
| Unannotated advisory order-balanced median ratio | At most 1.10 |
| Unannotated advisory paired p95 ratio | At most 1.20 |
| Retained-memory ratio | At most 1.05 |
| Retained-memory absolute increase | At most 32 MiB |
| Enabled analyzer retained compilations | 0 |
| Enabled analyzer retained-memory increase | At most 32 MiB |

Nightly campaign evidence parses every runner JSON result and requires the
exact schema, seed, configured parallelism, passing coverage, empty failure
set, and full `agreements + abstentions` case accounting. Its published total
is the observed runner total rather than the requested budget.
| Simulated IDE edits | 200 |
| IDE edit p95 | At most 100 ms |
| IDE edit maximum | At most 250 ms |

The active contract also fixes protocol version 9, cache schema version 11,
claim-manifest schema version 4, and compiler artifact schema version 9, along
with exact proof-kernel and component TCB path inventories, formatting-neutral
Roslyn complexity ratchets, and the reference surfaces `netstandard2.0`,
`net8.0`, and `net472`.

Unknown rate is reported by the corpus as explicit, silent, and total metrics;
it is not a release threshold.

## Outcome behavior at a limit

No timeout, resource exhaustion, unsupported encoding, malformed result,
backend failure, or exceeded expression depth is promoted to `Proven` or
`Refuted`. A method-level semantic boundary becomes a typed claim `Unknown`.
Project timeout and caller cancellation use separate `TimedOut` and `Canceled`
run statuses. Malformed output, backend/replay failure, containment failure,
and infrastructure failure make the run `Failed` and fail the build under
every policy.

Only exact-manifest, complete, postcondition-only project responses whose
claims are all replay-validated `Refuted` can enter the semantic cache. Every
cache hit reconstructs its scalar models and repeats whole-body replay. See
[Typed abstention reasons](unknown-reasons.md) for exact reason values.

Postcondition replay validation has two layers: exact backend-model and
lowered-term checks in the proof kernel, followed by independent execution of
the compiler-produced whole-body CFG in the worker. Executed spec calls become
typed `CounterexampleNotReplayable`; other unsupported or inconsistent replay
state fails the run as `CounterexampleReplayFailed`. Instructions on
unselected paths do not block a concrete replay.

Effect replay uses a separate compiler-neutral event interpreter rather than
SMT or user-code execution. It admits only an unconditional definite managed
object/array allocation with completed operands and no unmodeled static
initialization. Other definite effect candidates become
`CounterexampleNotReplayable`; may-only conflicts remain
`EffectContractNotEstablished`. Structural artifact tamper is a
`CompilerManifestMismatch`, while semantic replay disagreement is the fatal
`CounterexampleReplayFailed`. Effect results are not cacheable.
