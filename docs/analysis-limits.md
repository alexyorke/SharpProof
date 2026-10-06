# Analysis limits

Shipping worker budgets, fixed construction bounds, and repository acceptance floors serve different purposes. Raising a timeout cannot make unsupported semantics sound; lowering an acceptance floor cannot establish coverage.

## Package and worker defaults

The package profile is `advisory`, `strict`, or `off`; features are `effects`, `contracts`, or `all`. Advisory defaults to all features and optional verification. Strict requires verification and defaults to `require-proven` with assumption policy `error`.

Worker defaults are owned by [ProtocolModel.generated.cs](../SharpProof.Worker.Protocol/ProtocolModel.generated.cs) and mirrored in [SharpProof.Verifier.defaults.props](../SharpProof.Verifier/buildTransitive/SharpProof.Verifier.defaults.props).

| Property | Default | Unit/purpose |
| --- | ---: | --- |
| `SharpProofVerifyQueryRlimit` | 3000000 | Z3 resource units per query |
| `SharpProofVerifyMethodRlimit` | 20000000 | Accumulated method resource units |
| `SharpProofVerifyMethodWallTimeMilliseconds` | 10000 | Milliseconds per method |
| `SharpProofVerifyProjectWallTimeMilliseconds` | 300000 | Milliseconds per project |
| `SharpProofVerifyMaxParallelism` | 4 | Maximum concurrent method lanes |
| `SharpProofVerifyMaximumExpressionDepth` | 64 | Maximum expression depth |
| `SharpProofVerifyTerminationGraceMilliseconds` | 1000 | Launcher termination grace in milliseconds |
| `SharpProofVerifyCacheEnabled` | true | Enable the response cache |
| `SharpProofVerifyCacheMaximumBytes` | 536870912 | Bytes; 512 MiB aggregate active cache limit |

Resource units are solver accounting, not elapsed milliseconds. Container CPU/memory quotas are separate outer limits.

`SharpProofSpecificationPacks` defaults to unset. A nonempty value selects embedded reviewed packs by semicolon-delimited ID. The current catalog contains `dotnet.scalar`; unknown, duplicate, or empty list entries are rejected. This is a semantic artifact input, not a solver budget.

## Validation bounds

Budget validation requires positive query/method rlimits, with query no larger than method, and positive method/project times, with method no larger than project. Parallelism must be 1 through 4. Expression depth must be 1 through 256. Cache maximum size must be positive and no larger than 512 MiB. Termination grace accepts 1 through 300000 milliseconds.

[ProtocolJson.cs](../SharpProof.Worker.Protocol/ProtocolJson.cs) caps JSON input at 16 MiB with maximum JSON depth 32 and validates UTF-8 and shape. This per-file cap differs from aggregate cache eviction. Oversized or malformed cache envelopes become misses; invalid requests or responses fail closed.

## Fixed construction bounds

The typed frontend's `MaximumRegionSteps` is 4096, enforced for region traversal, CFG/instruction work, and related construction. The passive VC builder also limits steps to 4096 and bounds construction work. These are internal bounds, not user-configurable proof budgets.

The portable analysis surface has its own bounded CFG and expression work. Its admission is separate from native worker admission; a bound in one pipeline does not define the other pipeline's supported language surface.

Natural loops can be admitted with checked invariants. Bounded witness search does not prove arbitrary loops. Exhausted construction produces abstention rather than a partial proof.

## Output paths and concurrency

Verifier targets place default compiler manifest, request, result, and cache paths beneath the project's intermediate `SharpProof` directory. Invocation paths under `runs` isolate active builds. Configured publication paths are normalized and validated; multitarget builds project them into target-framework-specific paths.

`SharpProofVerifySarifFile` opts into SARIF output. The launcher projects validated results under the same publication boundary; SARIF cannot change semantic outcomes or build policy. Configure dedicated cache directories, and do not reuse arbitrary application output paths.

See [preview support](preview-support.md) for local filesystem requirements and concurrent publication behavior.

## Acceptance-only thresholds

[eng/acceptance/contract.json](../eng/acceptance/contract.json) owns production inventory, trusted/declaration-only classifications, and coverage floors. The [corpus ratchet](../SharpProof.Gates/Corpus/unknown-reason-ratchet.json) owns supported-case floors and Unknown caps. These are qualification criteria, not user-configurable worker limits.

Use the [container commands](container-development.md) to run the corresponding gates. Coverage needs a valid changed-code comparison ref. PR testing does not implicitly run every nightly, corpus, coverage, or security gate.

## Outcome behavior at a limit

Method/project timeout, cancellation, resource exhaustion, and backend failure remain explicit run or claim evidence. Unsupported syntax remains Unknown. None of these establishes the contract or a definite violation.

Incomplete runs are not eligible complete cache evidence. The cache accepts only validated complete responses with no run failure or protocol errors; stable semantic Unknown results may be reused. See [Unknown reasons](unknown-reasons.md).
