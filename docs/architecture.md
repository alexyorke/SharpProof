# Architecture

SharpProof has a portable compiler analysis surface and a native build-verification surface. Both use compiler-bound contract identities; only the worker loads native Z3.

## Components and ownership

| Component | Responsibility |
| --- | --- |
| `SharpProof.Attributes` | Public contract types, intrinsics, flags, trust/reporting controls |
| `SharpProof.Contracts` | Clause inventory, effective source selection, intrinsic validation and binding |
| `SharpProof.Specs` | Reviewed API catalog and resolved specification evidence |
| `SharpProof.Frontend` | Roslyn expression/program lowering to typed IR |
| `SharpProof.Ir` | Owned types, values, instructions, blocks, source provenance, evaluation |
| `SharpProof.Dataflow` | Analysis primitives over the modeled program |
| `SharpProof.Effects` | Effect values, mappings, and conservative summaries |
| `SharpProof.Compiler.Semantics` | Shared compilation-bound configuration validation, callable discovery, effect declarations, and Total body lowering |
| `SharpProof.Analyzer.Core` / `SharpProof.Analyzer` | Portable diagnostics, activation, advisory analysis |
| `SharpProof.ContractForGenerator` | Generator loading hook; companion validation runs after generators in analysis |
| `SharpProof.CompilerCollector` | Final-compilation manifest discovery and closed artifact production |
| `SharpProof.CompilerArtifact` | Versioned compiler evidence, codecs, validation |
| `SharpProof.Worker.Protocol` | Request/response models, budgets, manifests, tuple/accountability checks |
| `SharpProof.Smt` | Native solver abstraction, typed encoding and result evidence |
| `SharpProof.Worker` | Artifact preparation, native claims, VC checks, replay, response cache |
| `SharpProof.Host` | Container/native loading and host infrastructure |
| `SharpProof.BuildTasks` / `SharpProof.Verify` | MSBuild integration and verifier orchestration |
| `SharpProof.Package` | Metadata-only standard package facade with exact verifier dependency |
| `SharpProof.Verifier` | Analyzer/collector, MSBuild integration, managed worker and native solver package assets |
| `SharpProof.Tool` | Optional separately packaged doctor and stored-evidence explanation commands |

The solution lists production and test projects in [SharpProof.slnx](../SharpProof.slnx). Package identity and implementation assembly visibility do not make these internal components supported consumer APIs.

The analyzer and compiler collector both depend on `SharpProof.Compiler.Semantics`. The collector does not load analyzer policy or advisory execution. Semantic claim declarations contain no diagnostic descriptors; the analyzer projects them into diagnostics and the collector projects them into artifact evidence.

The worker consumes compiler-neutral artifact and verification libraries. It does not reference or compile source owned by the compiler-facing projects. Shared guards, hashing helpers, and framework polyfills live under `eng/shared`; linked IR identifier aliases remain owned by `SharpProof.Ir`. Architecture tests inspect evaluated MSBuild items as well as the declared project graph, and compiled assembly tests check the collector runtime closure.

## Compiler boundary

The collector operates on the compiler's final compilation, including generator output. Selection creates callable and claim identities before verification. The artifact includes compiler-bound contract evidence, typed programs, source positions, resolved calls/specifications, and runtime type evidence needed by verification and replay.

The worker consumes this closed artifact rather than reparsing a guessed source project with a potentially different compilation. It validates schema, content hash, manifest alignment, and evidence before constructing obligations. Unknown bodies stay visible in callable coverage and claim results.

The build integration also checks that the artifact belongs to the current compiler capture. The collector publishes a completion token after writing the artifact, while MSBuild validates the matching invocation before verification. A stable capture-request path avoids adding a new incremental compiler input for every build. Request, compiler manifest, and response publication then bind the same invocation; reusing an unrelated result file cannot satisfy that check. See [collector publication](../src/SharpProof.CompilerCollector/FinalCompilationCollector.cs), [verifier targets](../src/SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets), and [capture/cache regression tests](../src/SharpProof.Worker.Tests/Pr275ActualCompilerCaptureCacheTests.cs).

The public contract assembly is checked by exact supported identity and payload evidence. Attributes is fully strong-named with a deliberately public, stable open-source identity key; that provides Framework reference compatibility, not publisher authentication. Package/repository signatures and release attestations have separate verification scopes described in the [release process](../eng/release/README.md). Lookalikes do not create assumptions.

## Native verification

Typed Total IR is the worker's current program representation. Passive VC construction derives guarded scalar SSA and heap constraints from owned instructions. Normal returns, exception paths, effect sites, call preconditions, and checkpoints are explicit.

Loops use checked invariants in the proof encoding and separate bounded witness search. Heap stores retain order and reach; opaque writes forget information conservatively. Unknown calls carry unknown results and may-effects. No general C# support follows from merely accepting a declaration.

The native effect pipeline handles all worker effect contract kinds through `NativeEffectClaims`, `NativeExceptionEffectVerifier`, and `NativeEffectSiteVerifier`. Proof and witness evidence must survive projection and protocol validation. See [semantics](../SEMANTICS.md).

## Solver and replay boundary

Z3 establishes unsatisfiability or supplies a candidate model under bounded resources. Satisfiable is not synonymous with Refuted. Replay checks the candidate against the original typed program and entry domain. Approximation values and impossible sealed-type heap aliases cannot stand in for concrete executions.

Solver contexts belong to bounded verification work. Native payload loading, cancellation, and resource accounting are described in [SMT lifecycle](smt-lifecycle.md). Native code stays out of the portable analyzer process.

## Run and protocol boundary

A request binds the compiler artifact, budgets, cache options, policy, and manifest identity. A response distinguishes run status, callable coverage, and claim outcomes. Validation checks complete accountability, locations, assumptions, effect certainty, witness/vacuity consistency, and enum domains.

The current worker protocol is 43, manifest schema 6, cache schema 63, and compiler artifact schema 55. [Release constants](release-constants.md) identifies the owning files. Changes to compatibility require coordinated updates, not prose-only version bumps.

The launcher bounds wall time and termination grace and validates worker output. MSBuild projects worker results according to policy; infrastructure failure remains a failure. Publication paths and artifacts are protected by the [filesystem support boundary](preview-support.md).

[Reading verification evidence](verification-results.md) describes the published request/manifest/result set, independent callable and claim records, assumption provenance, effect witnesses, cache status, and optional SARIF projection. [Configuration](configuration.md) lists the consumer properties controlling this pipeline.

## Cache

`src/SharpProof.Worker/VerificationCache.cs` stores eligible complete responses by content-addressed input identity. Keys bind artifact bytes, worker binary, API specifications, protocol, and budgets. Reads revalidate schema and manifest. Atomic writes and bounded eviction manage storage; cache failures become misses.

A cache hit does not introduce a new proof rule. Transient timeouts, cancellation, unavailable backends, infrastructure failures, and malformed results are not durable semantic evidence.

## Build and package boundary

The standard consumer authors one `SharpProof` reference. Its exact dependency graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`: the facade has no assembly, Attributes supplies the transitive application API, and Verifier owns the analyzer/collector, MSBuild tasks, managed worker and pinned Z3 assets. Strict full verification activates automatically with SDK 10 on supported Windows, macOS and glibc Linux x64/ARM64 hosts; Verifier remains a compatibility entrypoint. Explicit advisory/off profiles retain their documented host and activation boundaries. See the [support matrix](preview-support.md).

Tooling assemblies carry embedded portable PDBs and compile sources; Attributes retains portable PDBs. Symbol packaging and coverage extract matching tooling PDBs from those same built assemblies rather than rebuilding them. The assembly-free facade has no symbol package. See [NuGet debugging](nuget-debugging.md) for exact symbol/source checks.

`SharpProof.Tool` is an optional separate .NET tool, outside the standard application dependency graph and core package feed. Its doctor inspects bounded host prerequisites; explain interprets stored worker evidence rather than producing a new proof. Its archive, consumer qualification and release provenance remain separate from the core three-package/two-symbol graph. The repository PowerShell wrapper is a maintainer/container entrypoint, not an additional consumer installation requirement.

Profiles select activation; worker policy decides what results the build accepts. Consumer examples live under [samples](../src/samples/README.md). Pinned inputs live in [eng/container/toolchain.json](../eng/container/toolchain.json). The tag workflow checks packed consumers before publication; see [release process](../eng/release/README.md).

## Mechanized checks

The trusted kernel is a subset of the full trusted computing base (TCB). [The acceptance contract](../eng/acceptance/contract.json) separately owns kernel paths, component path classification, and the production pipeline projects whose evaluated `Compile` items must all be classified. [Get-SharpProofTcbPaths.ps1](../eng/scripts/Get-SharpProofTcbPaths.ps1) rejects duplicate/noncanonical paths, unclassified pipeline sources, and sources outside the evaluated production inventory, with the explicit release-validator exception recorded in that function. Splitting a partial type or renaming a file does not remove its proof-pipeline responsibility.

Coverage is another independent measurement. [The coverage baseline](../eng/coverage/baseline.json) owns aggregate, per-project, and changed-TCB line floors plus the explicit declaration-only classification. [The coverage gate](../eng/scripts/Test-SharpProofCoverage.ps1) reconciles the measured universe with production inventory and comparison-ref changes. A small kernel, complete TCB inventory, passing coverage floor, and a proof of user code are four different claims. None implies the others. See [development workflows](development-workflows.md) for coverage evidence and commands.

Tests cover frontend, IR, contracts, specs, SMT, effects, analyzer, worker, protocol behavior, architecture, and packaged consumers. [Golden fixtures](../tests/golden/README.md) freeze stable stage output. [Corpus gates](../src/SharpProof.Gates/README.md) compare independently classified cases, outcomes, and canonical diagnostics. [Production inventory](../eng/scripts/Get-SharpProofProductionInventory.ps1) derives the coverage universe from solution projects and evaluated `SharpProofProductionProject`/`Compile` items. The acceptance contract classifies trusted pipeline projects/paths; the coverage baseline owns line floors and declaration-only coverage classification.

Run the relevant target first, then the smallest broader [container gate](container-development.md). These checks establish the exercised cases and boundaries, not universal language correctness.
