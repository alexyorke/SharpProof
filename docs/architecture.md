# SharpProof 0.2 architecture

SharpProof 0.2 is an effect-first, soundness-first preview. The supported
product is the effect cluster plus compiler-bound call-site preconditions.
Unsupported code is an abstention, not an invitation to guess.

## Dependency direction

The active production graph is a checked DAG:

```text
Attributes
Ir
Dataflow
Specs                 -> Ir
Frontend              -> Ir, Roslyn
Contracts             -> Attributes, Frontend, Ir, Specs
Effects               -> Attributes, Dataflow, Frontend, Specs
Verify                -> Ir, Specs, ISmtBackend
Smt                   -> Ir, Verify, Z3
ContractForGenerator  -> Frontend
Analyzer              -> Attributes, Contracts, Effects, Frontend, Ir, Specs
Worker.Protocol
Worker                -> Attributes, Contracts, Dataflow, Frontend, Ir, Smt,
                         Specs, Verify, Worker.Protocol
Worker.Launcher       -> Worker.Protocol
```

Build-only references to `SharpProof.Meta.Analyzers` are omitted. The
architecture suite compares every direct project reference against this graph.

The Roslyn analyzer has no verifier, SMT, Z3, or native dependency. Z3 is
allowed only in `SharpProof.Smt`, which is packaged below
`tools/net8` for the worker. `Ir`, `Dataflow`, and `Specs` contain no C# syntax
types. Production semantic-model acquisition passes through the single audited
`SharpProof.Frontend.Host.CompilationModelProvider`.

## Mechanized boundaries

The `SPMETA001`-`SPMETA011` repository analyzers turn selected
soundness-critical construction, cancellation, cache, and semantic-identity
boundaries into build errors. Architecture and package tests complement those
rules with exact project-reference and payload checks. These checks define
mechanical enforcement boundaries; they do not expand the admitted language or
turn an unsupported result into proof.

## Semantic core

`SharpProof.Ir` owns the authoritative representation:

- hash-consed, factory-scoped typed terms;
- scoped variable, operation, block, instruction, and location identities;
- capture-free substitution and structural old-state substitution;
- deterministic printing;
- a concrete expression and program interpreter;
- compact assign/load/store/call/assume/assert/havoc/control-flow
  instructions.

Frontend expression and CFG lowering is total. A supported operation lowers to
exact IR; every unsupported case returns a typed opaque term or program
abstention. There are no `TryLower(..., out ...)` branches, syntax reparsing,
speculative semantic models, or display-string identity.

`SharpProof.Dataflow` supplies deterministic fixpoint evaluation, partial
orders, joins, widening, havoc, and interval/congruence, sequence-cardinality,
and nullness domains. Source method effects are solved by stable SCC order.
The out-of-process worker also projects validated `ApiSpec` result nullness and
array cardinality into spec-justified Boolean and integer proxies. This is a
bounded call-result integration; it does not use roslyn-analyzers entities,
points-to state, or general CFG transfer.

External effect analysis uses a symbol-resolved `ApiSpecTable` or an explicitly
trusted, complete effect contract. The worker consumes only eligible resolved
`ApiSpec` rows. Unmodeled or untrusted metadata is unknown; there is no IL
interpreter.

## Contracts and modular verification

`Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old` bind as normal C#
operations. `Old` is pre-state substitution. `[ContractFor]` companions are
validated by an incremental, no-source generator using exact symbol identity,
including generics, constraints, ref/scoped kinds, nullability, defaults, and
return shape.

At inlining depth zero, the current worker consumes only facts from resolved
`ApiSpec` rows within its admitted call boundary. This is not general
source-callee modular assume/guarantee verification. The analyzer performs
cheap effect projections and reports a compiler-bound `Requires` violation
only when concrete replay evaluates the precondition to false. The worker
checks only the bounded `Ensures` subset supported by its admitted acyclic CFG
executor; deep or otherwise unsupported postconditions abstain.

Proof evidence is type-safe. Approximations cannot construct an assumption.
`Proven` is created only by the proof kernel after unsat-core hygiene checks.
`Refuted` is created only after the executable IR replays a SAT model and
observes the goal fail.

## Manifest, protocol, determinism, and cache

IR identity is structural and factory-scoped. Solver names are canonical
indices. Formula construction, worklists, specs, proof cores, diagnostics, and
serialized responses are stably ordered. Z3 uses resource limits; wall time is
an outer process kill boundary.

Protocol version 3 first builds a manifest from compiler symbols. Stable
semantic IDs identify selected callables, postcondition claims, and
user/trusted evidence independently of formatting. The protocol separates run
status, callable coverage, and per-claim outcome. Central validation requires
the response to match the sealed manifest exactly, including dense ordinals,
claim ownership, summary counts, evidence summaries, and allowed payloads.

The request carries real source and reference paths plus explicit compilation
options, feature selection, policies, and budgets. `WorkerFeatureSet` applies
the same `effects`/`contracts`/`all` selection before manifest discovery:
contract-only requests exclude effect annotations and effect-only requests
exclude postcondition claims. On the supported Windows x64 worker host, the
launcher creates a startup barrier, assigns the worker to a Job Object with
process and memory limits, and only then releases verification work. Concurrent
builds use isolated request/result paths. After validating a response, a
cross-process mutex serializes publication; each stable file is atomically
replaced, and the request replacement is rolled back if result publication
fails. Completed writers leave a consistent pair, though readers can observe
the narrow interval between the two replacements. The
content-addressed cache includes semantic, protocol, tool, compilation,
reference, option, target-framework, and spec identity. Cache schema version 3
stores only the validated semantic payload. A hit is accepted only when its
manifest hash and complete result set match the current manifest. Only
complete callables whose claims are hygienic `Proven` or replay-validated
`Refuted` are cacheable.

One important production boundary is not complete: MSBuild still sends
source/reference lists and the worker reconstructs a compilation. It does not
yet observe the final post-generator Roslyn `Compilation`, generated-tree
checksums, `AdditionalFiles`, or a closed compiler artifact. That collector and
artifact are future work; documentation does not treat generated claims as
accounted for today. Deterministic JSON is emitted, but SARIF projection is
also future work. Counterexample replay currently re-evaluates the lowered
obligation-path IR; an independent whole-body interpreter over the exact CFG is
also still required before 1.0.

## Activation and release gates

`SharpProofProfile` accepts `advisory`, `strict`, and `off`; the package
default is `advisory`. `SharpProofFeatures` accepts `effects`, `contracts`, and
`all`; the default is `all`. A custom host can provide the equivalent
compilation-global `sharpproof_profile` and `sharpproof_features` keys. The
`off` profile omits analyzer/generator items and constructs no analysis
session. Unsupported unannotated analyzer callables are silent, while
unsupported explicitly selected callables report SP0047.

The verifier is optional in advisory builds and mandatory in strict builds;
explicitly setting `SharpProofVerify=false` with `strict` is a configuration
error. `SharpProofVerifyPolicy` controls incomplete selected analysis;
`SharpProofAssumptionPolicy` controls SP0048 reporting for user assumptions and
trusted evidence. A refutation, malformed response, backend/replay failure,
containment failure, or other infrastructure failure is fatal regardless of
policy. `SharpProofMode` and `all-experimental` remain deprecated preview
compatibility inputs only.

The current gate includes:

- exhaustive Roslyn operation-kind and architecture checks;
- compiler-enforced banned APIs and repository meta-analyzers;
- lattice laws and finite-CFG checks;
- executable witnesses and mutation probes for every claim-bearing API-spec
  facet and postcondition;
- IR/C# and IR/SMT differential oracles;
- replay and unsat-core checks;
- snapshot-corpus and metamorphic invariance;
- cache/concurrency/cancellation determinism;
- worker/package consumer smoke checks;
- fixed-seed fuzzing and performance budgets.

Off-profile latency samples alternate real baseline and SharpProof-imported
MSBuild rebuilds. A separate loaded-but-off analyzer canary covers session
creation and retained state, while the package policy proves that
`SharpProofProfile=off` omits analyzer items and verifier invocation. The worker
remains in the current preview package's opt-in tools payload. The corpus
reports explicit, silent, and total semantic Unknown rates as metrics; none is
a release gate.

The active contract is `eng/acceptance`.
