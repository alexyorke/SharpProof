# Reading verification evidence

SharpProof publishes structured evidence for the selected compilation. Use it to distinguish a checked claim, an unsupported body, a replayed violation, and a failed worker run. Start with [configuration](configuration.md) to enable the worker and [semantics](../SEMANTICS.md) for the meaning of a proof.

## Find and retain the output

The verifier normally publishes `request.json`, `result.json`, and `compiler-manifest.json` under `$(IntermediateOutputPath)SharpProof`, typically `obj/<Configuration>/<TargetFramework>/SharpProof`. The build prints its effective result path. `SharpProofVerifySarifFile` enables an additional SARIF report; it is empty by default. A custom output filename in a multitargeted project is placed in a target-framework subdirectory to keep compilations separate.

Finite repository tooling tasks use disposable private source copies. Retain exported CI artifacts, or inspect a persistent canonical container workspace, rather than assume a finite task's intermediate files remain on the host. See [development workflows](development-workflows.md).

The three JSON files form one evidence set. The request binds the compiler artifact path and hash; the response binds the request and input identities and embeds its selected manifest. A result copied from another compilation, worker, or budget is insufficient. Stable output is published after invocation validation; an old file's existence does not establish that the latest build succeeded.

Source: [verifier targets](../src/SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets), [publication](../src/SharpProof.BuildTasks/VerificationPublication.cs), and [published-result validation](../src/SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs). Regression evidence includes [actual result-file admission](../src/SharpProof.Worker.Tests/ActualLauncherResultFileAdmissionTests.cs) and [compiler capture/cache workflows](../src/SharpProof.Worker.Tests/Pr275ActualCompilerCaptureCacheTests.cs).

## Read results in this order

After validating a worker response, the build prints a readable summary: run/failure status, selected callables and claims, Proven/Refuted/Unknown counts, incomplete callables, vacuous claims, distinct declared user/trusted assumptions and used count, elapsed milliseconds, and cache status. After successful publication it prints the evidence paths. Individual Unknown diagnostics include source locations and a typed next action where available. Use this summary to find the relevant evidence; it does not replace the authoritative JSON or establish that an old report belongs to the current build.

| Layer | Fields | Interpretation |
| --- | --- | --- |
| Run | `RunStatus`, `FailureReason`, `Errors` | Whether the worker completed, timed out, was canceled, or failed; a completed run can still contain Unknown claims |
| Selection | `Manifest.Callables`, `Manifest.Claims` | What this compilation selected, where, why, and with which contract evidence |
| Callable | `CallableResults`: `Coverage`, `Reason`, `Assumptions` | Whether selected analysis was accounted for completely; incomplete analysis is visible even when no ordinary claim diagnostic would explain it |
| Claim | `ClaimResults`: `Outcome`, `Reason` | Proven, Refuted, or Unknown for one selected postcondition or effect contract |
| Evidence | `Vacuity`, `ProofCore`, `Model`, `EffectCertainty`, `EffectWitness`, `Assumptions` | Conditions, dependencies, counterexample inputs, and source sites needed to interpret that outcome |
| Execution | `Summary`: `Versions`, `Budgets`, `ElapsedMilliseconds`, `CacheStatus`, `CacheHit` | The worker/specification identity, configured limits, timing, and reuse status |

The table uses C# protocol member names; JSON properties use camel case (`runStatus`, `claimResults`, `summary.cacheStatus`) and enum values use names such as `Complete` and `Proven`. See the [protocol model](../src/SharpProof.Worker.Protocol/ProtocolModel.cs) and [serializer configuration](../src/SharpProof.Worker.Protocol/ProtocolJsonSupport.cs). JSON is a versioned protocol, not a hand-written receipt format. IDs connect manifest entries and results. Missing, duplicate, foreign, or inconsistent results fail validation; counting successful diagnostics is not an accountability check.

[Protocol tests](../src/SharpProof.Worker.Tests/ProtocolJsonTests.cs), [expanded manifest checks](../src/SharpProof.Worker.Tests/ProtocolExpandedManifestAuthorityTests.cs), and [result projection tests](../src/SharpProof.Worker.Tests/TotalClaimResultTests.cs) exercise these boundaries.

## Selection and individual claims

A callable can be selected for effects, contracts, or both. Selection reasons include explicit annotations and discovered postconditions. Claim evidence distinguishes direct clauses, companion clauses, return attributes, and attributes. One callable can have multiple independently reported claims. A Proven postcondition therefore does not establish its allocation, exception, purity, or capability contracts.

`Requires` restricts entry and produces obligations at modeled calls. It does not automatically create a selected postcondition. Direct clauses take precedence over valid companion clauses for the member. The selected manifest records the effective evidence, including assumptions; inspect it when a companion or annotation appears to have no effect.

See [public API](public-api.md), [companion tutorial](tutorials/07-companion.md), [claim discovery](../src/SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs), and [manifest regression tests](../src/SharpProof.Worker.Tests/ClaimManifestBuilderTests.cs).

## Proofs, models, and replay

`Proven` establishes the modeled obligation under the recorded entry domain and assumptions. `ProofCore` is evidence produced by the verifier; it is not a source-level proof script or a guarantee of termination. A core entry beginning `il-summary:` identifies an implementation-IL dependency. The report explains that this assumes the compile-time referenced binary is the runtime binary.

An SMT model is only a candidate counterexample. SharpProof checks its entry bindings, path, operation semantics, postcondition definedness, and violation against the owned typed program before publishing `Refuted`. A `Model` supplies named typed values; do not assume unconstrained internal values can be supplied as executable inputs. Strings and reference identities need their encoded meaning preserved.

Approximated heap reads, opaque call results, impossible aliases, or unsupported replay operations can prevent replay. The result then remains Unknown with a typed reason such as `CounterexampleNotReplayable`, `CounterexampleReplayFailed`, or `PostconditionMayBeUndefined`. Increasing a solver budget does not add a missing concrete evaluator.

An effect refutation additionally identifies a concrete `EffectWitness` with kind, detail, effects, capabilities, exception hierarchy where relevant, and source location. A metadata effect without a source site cannot supply a definite emitted violation. Evidence certainty distinguishes a complete body summary, a trusted complete external boundary, a definite violation, incomplete/unavailable summaries, and vacuous entry.

Read [Unknown reasons](unknown-reasons.md) for the full reason list. Source and behavioral evidence: [proof kernel](../src/SharpProof.Smt/Verify/ProofKernel.cs), [replay validator](../src/SharpProof.Smt/Verify/CallableReplayValidator.cs), [field replay tests](../src/SharpProof.Worker.Tests/NativeFieldRefutationReplayTests.cs), [effect witness matrix](../src/SharpProof.Worker.Tests/Wave8ProtocolEffectWitnessContractMatrixTests.cs), and [string/model projection tests](../src/SharpProof.Worker.Tests/TotalClaimResultTests.cs).

## Vacuity and assumptions

| Evidence | What to check |
| --- | --- |
| `ContradictoryPreconditions` | The entry domain is infeasible; review the preconditions rather than interpret the proof as successful reachable behavior |
| `NoModeledNormalReturn` | A normal-return postcondition has no modeled normal return; this proves neither termination nor exception freedom |
| `Precondition` | A declared condition restricting entry or a modeled call's contract |
| `UserAssume` | Explicit user evidence that SharpProof has not independently proven |
| `TrustedBoundary` | A reviewed boundary declaration whose provenance remains an assumption |
| `ApiSpecification` | A resolved specification under its exact matching/domain evidence |
| `SourceDomain` | Compiler/runtime input-domain facts admitted by the model |
| `NormalCompletion` | The normal-completion condition relevant to the obligation |

Each assumption has an ID, kind, and `Used` flag. A declared assumption and a used proof dependency answer different questions. The assumption policy applies to declared user assumptions and trusted boundaries; `error` does not reject every precondition, source-domain fact, or API specification. A contradictory entry must not mark an unrelated point assumption used.

The SARIF projection marks a vacuous proof for review. See [source-assumption tests](../src/SharpProof.Worker.Tests/WorkerVcSourceAssumeTests.cs), [assumption provenance tests](../src/SharpProof.Worker.Tests/NativeCallAssumptionProvenanceTests.cs), and [policy projection](../src/SharpProof.Worker/Launcher/SarifProjection.cs).

## Policy and SARIF

Policy projects validated evidence into build diagnostics and exit status. It does not change a claim's outcome.

| Claim | SARIF presentation |
| --- | --- |
| Proven, nonvacuous | `pass`, level `none` |
| Proven, vacuous | `review`, level `none` |
| Refuted | `fail`, level `error` |
| Unknown, `advisory` | `review`, level `none` |
| Unknown, `warn-on-unknown` | `fail`, level `warning` |
| Unknown, `require-proven` | `fail`, level `error` |

SARIF includes rule IDs such as `SharpProof.Proven`, `SharpProof.Refuted`, and `SharpProof.Unknown`, claim identities, locations, and structured claim/result properties. Incomplete selected callables and declared policy assumptions have their own records. The optional report helps code-review tools navigate evidence; retain the JSON result for full protocol/accountability inspection.

The worker launcher has internal failure exit codes as well as semantic policy failures; use the recorded status/reason and build diagnostic to distinguish them. A zero ordinary compiler exit code or suppressed analyzer diagnostic is insufficient evidence of verification.

Source: [SARIF projection](../src/SharpProof.Worker/Launcher/SarifProjection.cs), [launcher presentation](../src/SharpProof.Worker/Launcher/LauncherPresentation.cs), [launcher program](../src/SharpProof.Worker/Launcher/LauncherProgram.cs). [SARIF policy tests](../src/SharpProof.Worker.Tests/WorkerSarifPolicyCoverageTests.cs) check that reporting preserves evidence.

## Cache reuse and resource failures

The default cache is enabled, bounded to 512 MiB, and located under the intermediate SharpProof directory. See [configuration](configuration.md) for overrides and [analysis limits](analysis-limits.md) for query, method, project, expression-depth, and parallelism limits.

`CacheStatus` distinguishes `Disabled`, `Miss`, `Hit`, `Written`, `Rejected`, and `Unavailable`. A reusable entry must be a validated complete response with no run failure/errors and the expected input/manifest identity. Stable semantic Unknown outcomes may be reused. Timed-out, canceled, and failed runs cannot be reused as complete evidence. Corruption, unreadable storage, and write/eviction failures degrade reuse rather than change verdicts.

Cache keys bind the compiler artifact, worker binary, specification identity, protocol, and budgets. Changing source or limits is therefore different from retrying identical inputs. A `Hit` does not mean the current invocation solved the obligation again; its summary still identifies the evidence being reused.

First classify an Unknown as a model limitation, resource limit, replay issue, or infrastructure failure. Adjust a budget only for evidence of exhaustion. Preserve the original inputs and compare verdicts and end-to-end time if evaluating a change. [SMT lifecycle](smt-lifecycle.md) explains cancellation and accounting.

Source: [cache eligibility](../src/SharpProof.Worker/VerificationCache.cs). Regression evidence: [cache input identity](../src/SharpProof.Worker.Tests/WorkerCacheHashInputCoverageTests.cs), [unavailable cache](../src/SharpProof.Worker.Tests/WorkerCacheAvailabilityCoverageTests.cs), and [eviction failures](../src/SharpProof.Worker.Tests/WorkerCacheEvictionAvailabilityCoverageTests.cs).

## Troubleshooting sequence

1. Check the profile, feature selection, verifier package reference, native/container host, and design-time build status in [configuration](configuration.md).
2. Confirm the manifest contains the expected callable and claims. Compare direct and companion binding and invalid-usage diagnostics.
3. Check run status and errors before individual outcomes. Preserve the complete evidence set and the exact commit/package/target-framework identity.
4. Inspect callable completeness, claim reasons, vacuity, assumption provenance, and replay evidence.
5. Use the relevant [language boundary](language-reference.md) or [Unknown reason](unknown-reasons.md) to choose a supported expression, isolate a regression case, or investigate resource exhaustion.
6. Reproduce through the canonical container workflow or CI with the same inputs. Diagnostic suppression or relaxed policy does not repair missing evidence.
