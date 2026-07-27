# Analysis limits

SharpProof has two kinds of limits:

- shipping defaults passed by the NuGet build-transitive integration; and
- acceptance-only thresholds used to decide whether the repository is
  releasable.

They have different sources.
`SharpProof.Package/buildTransitive/SharpProof.props` defines fixed worker
defaults and compiler-visible properties; `SharpProof.targets` resolves profile,
feature, and policy defaults. `SharpProof.Worker.Protocol/ProtocolModel.cs`
defines matching protocol defaults and validation bounds. The release gate
mirrors selected values in `eng/acceptance/contract.json` and verifies that
they agree.

## Package and worker defaults

| MSBuild property | Default | Purpose | Authoritative source |
|---|---:|---|---|
| `SharpProofProfile` | `advisory` | Analyzer/build posture: `advisory`, `strict`, or `off` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofFeatures` | `all` | Analyzer and worker-manifest features: `effects`, `contracts`, or `all` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofVerifyPolicy` | `advisory`; strict defaults to `require-proven` | Incomplete selected-analysis policy: `advisory`, `warn-on-unknown`, or `require-proven` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofAssumptionPolicy` | `allow`; strict defaults to `error` | User/trusted evidence policy: `allow`, `warn`, or `error` | `SharpProof.targets`; mirrored by `contract.json` |
| `SharpProofMode` | unset | Deprecated preview alias for profile/features | `SharpProof.targets` |
| `SharpProofVerify` | `false`; strict requires `true` | Optional advisory worker execution; mandatory in strict | `SharpProof.targets` |
| `SharpProofVerifyQueryRlimit` | `3000000` | Z3 resource limit for one query | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodRlimit` | `20000000` | Aggregate resource allowance for one method | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodWallTimeMilliseconds` | `10000` | Outer method wall boundary | `SharpProof.props` and `WorkerBudgets`; mirrored as 10 seconds by `contract.json` |
| `SharpProofVerifyProjectWallTimeMilliseconds` | `300000` | Outer project wall boundary | `SharpProof.props` and `WorkerBudgets`; mirrored as 300 seconds by `contract.json` |
| `SharpProofVerifyMaxParallelism` | `4` | Maximum concurrent worker method verification | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMaximumExpressionDepth` | `64` | Compiler-visible proof-obligation term depth sealed into the artifact; worker request must match | `SharpProof.props`, `FinalCompilationCollector`, and `WorkerBudgets`; not present in `contract.json` |
| `SharpProofVerifyProcessMemoryLimitBytes` | `2147483648` | Windows Job Object memory limit | `SharpProof.props` and `WorkerBudgets`; mirrored as 2048 MiB by `contract.json` |
| `SharpProofVerifyMaxWorkerProcesses` | `4` | Windows Job Object active-process limit | `SharpProof.props` and `WorkerBudgets`; not present in `contract.json` |
| `SharpProofVerifyTerminationGraceMilliseconds` | `1000` | Grace added to the project boundary before forced termination | `SharpProof.props` and `WorkerLauncherDefaults`; mirrored by `contract.json` |
| `SharpProofVerifyCacheEnabled` | `true` | Enables the content-addressed disk cache | `SharpProof.props` and `WorkerCacheOptions`; not present in `contract.json` |
| `SharpProofVerifyCacheMaximumBytes` | `536870912` | Maximum cache size, 512 MiB | `SharpProof.props` and `WorkerCacheOptions`; mirrored by `contract.json` |
| `SharpProofDotNetHost` | `dotnet` | Host used to start the launcher | `SharpProof.props` |

`SharpProofVerifyCacheDirectory` is initialized by `SharpProof.targets` beneath
the project's intermediate output, normally
`obj/<Configuration>/<TargetFramework>/SharpProof/cache`.
`SharpProofVerifyRequestFile` and `SharpProofVerifyResultFile` are initialized
beside it. Concurrent builds use isolated invocation paths beneath
`SharpProof/runs`. Validated writers are serialized by a cross-process mutex.
The stable result is removed first, the compiler manifest and request are
atomically replaced, and the validated result is written last as the commit
marker. Interrupted publication therefore leaves no successful result for a
partly updated evidence set.

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
non-design-time Windows builds; the shipped native payload is supported on
Windows x64. Non-Windows hosts receive an explicit unsupported-host build
error; portable analyzer features remain available. Strict rejects
`SharpProofVerify=false`.

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
schema-3 artifact. The launcher supplies the same property as the worker request
budget. A mismatch is `CompilerManifestMismatch` and stops before cache lookup
or backend creation; neither side may silently use a different depth.

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

`SharpProofFeatures` is a semantic compiler-artifact input. `contracts` excludes
effect-only annotations from the manifest; `effects` excludes postcondition
claims and contract assumptions; `all` includes both.

## Fixed worker body bounds

`SharpProof.Worker/CallableVerifier.cs` also has three fixed, non-configurable
bounds for one admitted acyclic body:

| Bound | Limit |
|---|---:|
| Reachable CFG blocks | 64 |
| Normal-return paths | 64 |
| Symbolic execution states | 4,096 |

Crossing one of these bounds returns `Unknown` with `UnsupportedBody`; it does
not produce a partial proof. Loops are rejected by the acyclic-body check
before symbolic execution.

The manifest discovers local functions, lambdas, anonymous methods, and the
top-level entry point, including their directly owned postconditions. These
forms currently remain outside worker execution and produce
`UnsupportedCallable`; nested clauses are assigned only to their owning
callable.

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
| Off-profile median ratio | At most 1.10 |
| Off-profile p95 ratio | At most 1.20 |
| Retained-memory ratio | At most 1.05 |
| Retained-memory absolute increase | At most 32 MiB |
| Enabled analyzer retained compilations | 0 |
| Enabled analyzer retained-memory increase | At most 32 MiB |
| Simulated IDE edits | 200 |
| IDE edit p95 | At most 100 ms |
| IDE edit maximum | At most 250 ms |

The active contract also fixes protocol version 5, cache schema version 6,
claim-manifest schema version 2, and compiler artifact schema version 3, along
with the narrow proof-kernel and component TCB path/LOC ratchets and the
reference surfaces `netstandard2.0`, `net8.0`, and `net472`.

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

Only exact-manifest, complete hygienic `Proven` and replay-validated `Refuted`
project responses can enter the semantic cache. See
[Typed abstention reasons](unknown-reasons.md) for exact reason values.

Replay validation has two layers: exact backend-model and lowered-term checks
in the proof kernel, followed by independent execution of the compiler-produced
whole-body CFG in the worker. Executed spec calls or unsupported operations
fail closed to `CounterexampleReplayFailed`; instructions on unselected paths
do not block a concrete replay.
