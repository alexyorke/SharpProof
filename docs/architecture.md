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
| `SharpProof.Analyzer.Core` / `SharpProof.Analyzer` | Portable diagnostics, activation, advisory analysis |
| `SharpProof.ContractForGenerator` | Generator loading hook; companion validation runs after generators in analysis |
| `SharpProof.CompilerCollector` | Final-compilation manifest discovery and closed artifact production |
| `SharpProof.CompilerArtifact` | Versioned compiler evidence, codecs, validation |
| `SharpProof.Worker.Protocol` | Request/response models, budgets, manifests, tuple/accountability checks |
| `SharpProof.Smt` | Native solver abstraction, typed encoding and result evidence |
| `SharpProof.Worker` | Artifact preparation, native claims, VC checks, replay, response cache |
| `SharpProof.Host` | Container/native loading and host infrastructure |
| `SharpProof.BuildTasks` / `SharpProof.Verify` | MSBuild integration and verifier orchestration |
| `SharpProof.Package` / `SharpProof.Verifier` | Portable and verifier package assembly |

The solution lists production and test projects in [SharpProof.slnx](../SharpProof.slnx). Package identity and implementation assembly visibility do not make these internal components supported consumer APIs.

## Compiler boundary

The collector operates on the compiler's final compilation, including generator output. Selection creates callable and claim identities before verification. The artifact includes compiler-bound contract evidence, typed programs, source positions, resolved calls/specifications, and runtime type evidence needed by verification and replay.

The worker consumes this closed artifact rather than reparsing a guessed source project with a potentially different compilation. It validates schema, content hash, manifest alignment, and evidence before constructing obligations. Unknown bodies stay visible in callable coverage and claim results.

The public contract assembly is checked by exact supported identity and payload evidence. The unsigned package does not supply public-key authentication. Lookalikes do not create assumptions.

## Native verification

Typed Total IR is the worker's current program representation. Passive VC construction derives guarded scalar SSA and heap constraints from owned instructions. Normal returns, exception paths, effect sites, call preconditions, and checkpoints are explicit.

Loops use checked invariants in the proof encoding and separate bounded witness search. Heap stores retain order and reach; opaque writes forget information conservatively. Unknown calls carry unknown results and may-effects. No general C# support follows from merely accepting a declaration.

The native effect pipeline handles all worker effect contract kinds through `NativeEffectClaims`, `NativeExceptionEffectVerifier`, and `NativeEffectSiteVerifier`. Proof and witness evidence must survive projection and protocol validation. See [semantics](../SEMANTICS.md).

## Solver and replay boundary

Z3 establishes unsatisfiability or supplies a candidate model under bounded resources. Satisfiable is not synonymous with Refuted. Replay checks the candidate against the original typed program and entry domain. Approximation values and impossible sealed-type heap aliases cannot stand in for concrete executions.

Solver contexts belong to bounded verification work. Native payload loading, cancellation, and resource accounting are described in [SMT lifecycle](smt-lifecycle.md). Native code stays out of the portable analyzer process.

## Run and protocol boundary

A request binds the compiler artifact, budgets, cache options, policy, and manifest identity. A response distinguishes run status, callable coverage, and claim outcomes. Validation checks complete accountability, locations, assumptions, effect certainty, witness/vacuity consistency, and enum domains.

The current worker protocol is 13, manifest schema 5, cache schema 15, and compiler artifact schema 31. [Release constants](release-constants.md) identifies the owning files. Changes to compatibility require coordinated updates, not prose-only version bumps.

The launcher bounds wall time and termination grace and validates worker output. MSBuild projects worker results according to policy; infrastructure failure remains a failure. Publication paths and artifacts are protected by the [filesystem support boundary](preview-support.md).

## Cache

`SharpProof.Worker/VerificationCache.cs` stores eligible complete responses by content-addressed input identity. Keys bind artifact bytes, worker binary, API specifications, protocol, and budgets. Reads revalidate schema and manifest. Atomic writes and bounded eviction manage storage; cache failures become misses.

A cache hit does not introduce a new proof rule. Transient timeouts, cancellation, unavailable backends, infrastructure failures, and malformed results are not durable semantic evidence.

## Build and package boundary

The portable package supplies analyzers and compiler tooling without application compile assets. Attributes supplies the application API. Verifier supplies build tooling and Linux amd64 worker dependencies, including pinned managed and native Z3.

Profiles select activation; worker policy decides what results the build accepts. Consumer examples live under [samples](../samples/README.md). Pinned inputs live in [eng/container/toolchain.json](../eng/container/toolchain.json). The tag workflow checks packed consumers before publication; see [release process](../eng/release/README.md).

## Mechanized checks

Tests cover frontend, IR, contracts, specs, SMT, effects, analyzer, worker, protocol behavior, architecture, and packaged consumers. [Golden fixtures](../tests/golden/README.md) freeze stable stage output. [Corpus gates](../SharpProof.Gates/README.md) compare independently classified cases, outcomes, and canonical diagnostics. Coverage inventories and floors live in [eng/acceptance/contract.json](../eng/acceptance/contract.json).

Run the relevant target first, then the smallest broader [container gate](container-development.md). These checks establish the exercised cases and boundaries, not universal language correctness.
