# Analysis limits

SharpProof has two kinds of limits:

- shipping defaults passed by the NuGet build-transitive integration; and
- acceptance-only thresholds used to decide whether the repository is
  releasable.

They have different sources. `SharpProof.Package/buildTransitive/SharpProof.props`
defines package defaults. `SharpProof.Worker.Protocol/ProtocolModel.cs` defines
matching protocol defaults and validation bounds. The release gate mirrors
selected values in `eng/acceptance/contract.json` and verifies that they
agree.

## Package and worker defaults

| MSBuild property | Default | Purpose | Authoritative source |
|---|---:|---|---|
| `SharpProofMode` | `off` | Analyzer activation mode | `SharpProof.props`; mirrored by `contract.json` |
| `SharpProofVerify` | `false` | Opt-in worker execution | `SharpProof.props` |
| `SharpProofVerifyQueryRlimit` | `3000000` | Z3 resource limit for one query | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodRlimit` | `20000000` | Aggregate resource allowance for one method | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMethodWallTimeMilliseconds` | `10000` | Outer method wall boundary | `SharpProof.props` and `WorkerBudgets`; mirrored as 10 seconds by `contract.json` |
| `SharpProofVerifyProjectWallTimeMilliseconds` | `300000` | Outer project wall boundary | `SharpProof.props` and `WorkerBudgets`; mirrored as 300 seconds by `contract.json` |
| `SharpProofVerifyMaxParallelism` | `4` | Maximum concurrent worker method verification | `SharpProof.props` and `WorkerBudgets`; mirrored by `contract.json` |
| `SharpProofVerifyMaximumExpressionDepth` | `64` | Maximum proof-obligation term depth | `SharpProof.props` and `WorkerBudgets`; not present in `contract.json` |
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
beside it.

The package accepts `SharpProofMode` values `off`, `effects`, `contracts`, and
`all-experimental`. `SharpProofVerify=true` invokes the packaged worker target
only for non-design-time Windows builds; the shipped native payload is
supported on Windows x64. Non-Windows hosts receive an explicit
unsupported-host build error; analyzer modes remain available.

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

Every effective compilation option and budget participates in worker input and
cache identity. Changing a limit cannot reuse an answer produced under a
different limit.

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
| Default-off median ratio | At most 1.10 |
| Default-off p95 ratio | At most 1.20 |
| Retained-memory ratio | At most 1.05 |
| Retained-memory absolute increase | At most 32 MiB |
| Enabled analyzer retained compilations | 0 |
| Enabled analyzer retained-memory increase | At most 32 MiB |
| Simulated IDE edits | 200 |
| IDE edit p95 | At most 100 ms |
| IDE edit maximum | At most 250 ms |

The active contract also fixes protocol version 2, cache schema version 2, the
trusted-kernel path/LOC boundary, and the reference surfaces
`netstandard2.0`, `net8.0`, and `net472`.

Unknown rate is reported by the corpus as explicit, silent, and total metrics;
it is not a release threshold.

## Outcome behavior at a limit

No timeout, resource exhaustion, unsupported encoding, malformed result,
backend failure, or exceeded expression depth is promoted to `Proven` or
`Refuted`. A method-level boundary becomes a typed worker `Unknown`; a
project-level boundary marks unfinished records with `ProjectTimeout`.
Caller cancellation remains cancellation.

Only complete hygienic `Proven` and replay-validated `Refuted` project
responses can enter the semantic cache. See
[Typed abstention reasons](unknown-reasons.md) for exact reason values.
