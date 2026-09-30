# SharpProof 1.0 preview architecture

SharpProof 1.0 is an effect-first, soundness-first preview. The supported
product is the effect cluster, compiler-bound call-site preconditions, and the
bounded out-of-process postcondition verifier. Unsupported code is an
abstention, not an invitation to guess.

## Dependency direction

The active production graph is a checked DAG:

```text
Attributes
Ir
Dataflow
Specs                 -> Ir
Frontend              -> Attributes (build-only payload identity), Ir
Contracts             -> Frontend, Ir
Effects               -> Dataflow, Frontend, Specs
Verify                -> Ir, Specs
Smt                   -> Ir, Verify
Summaries             -> Ir
CompilerArtifact      -> Ir, Worker.Protocol
Analyzer.Core         -> Contracts, Effects, Frontend, Ir, Specs
Analyzer              -> Analyzer.Core
ContractForGenerator  -> Analyzer.Core, Contracts
CompilerCollector     -> Analyzer.Core, CompilerArtifact, Contracts, Effects,
                         Frontend, Ir, Specs, Summaries, Worker.Protocol
BuildTasks            -> Host
Host
Worker.Protocol
Worker                -> CompilerArtifact, Dataflow, Host, Ir, Smt, Specs,
                         Verify, Worker.Protocol
```

Frontend's
Attributes edge has `ReferenceOutputAssembly=false`; it establishes build
order so the exact Attributes assembly identity can be embedded without adding
a runtime assembly dependency. The architecture suite compares every direct
project reference against this graph. The ordinary live analyzer has no static dependency on the
compiler-artifact model or worker protocol; those
dependencies belong only to the build-only compiler collector.

The Roslyn analyzer has no verifier, SMT, Z3, or native dependency. Z3 is
allowed only in `SharpProof.Smt`, which is packaged below
`tools/net9` for the worker. `Ir`, `Dataflow`, and `Specs` contain no C# syntax
types. Production semantic-model acquisition passes through the single audited
`SharpProof.Frontend.Host.CompilationModelProvider`.

<a id="mechanized-boundaries"></a>
## Mechanized boundaries

The `SPMETA001`-`SPMETA011` repository analyzers turn selected
soundness-critical construction, cancellation, cache, and semantic-identity
boundaries into build errors. Architecture and package tests complement those
rules with exact project-reference and payload checks. These checks define
mechanical enforcement boundaries; they do not expand the admitted language or
turn an unsupported result into proof.

The exact path inventories in `eng/acceptance/contract.json` cover
proof-outcome construction and each declared trusted boundary: discovery,
lowering, execution, obligation construction, SMT encoding, API specification
hand-maintained catalog tables, effect analysis, replay, policy, result assembly,
and cache validation. Compiler-input identity, typed canonical hash encoding,
and protocol validation have their own non-overlapping inventories rather than
 being hidden inside the cache component. API-spec content identity is likewise
 separate from resolution and instantiation. The declarative API-spec catalog,
 its matcher/instantiator source are one audited
 `apiSpecificationCatalog` component. The contract API vocabulary has its own
 `contractApiCatalog` component; its generated output is limited to descriptors
 and ordered tables, while lookup behavior remains handwritten. The C# scalar type, conversion, checked
arithmetic, IR enum vocabulary, and operator rules likewise live in the
`CSharpScalarSemantics.generated.cs` and `IrOperatorCatalog.generated.cs`
tables. The portable IR wire-enum and slot tables and wire projection
adapters are declarative, while the codec keeps
 indexing, depth/cycle, canonicality, and malformed-input validation handwritten.
The effect-contract mapping catalog similarly owns finite capability, region,
direct-event, and reference-family mappings; effect analysis and fail-closed
validation remain handwritten.
The operation-support catalog similarly owns the finite Roslyn operation lists
for contract-expression lowering and effect discovery; support queries,
lowering, shape checks, and fail-closed behavior remain handwritten.
Finite output, result-label, policy, operation-stage, and effect-wiring
projections live in the per-project `*Projections.generated.cs` tables;
replay, validation, and analysis algorithms remain handwritten.

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

Compiler-facing stages share a closed operation-support catalog, with separate
decisions for contract-expression lowering and effect discovery. Stage-owned
shape and type validation remains independent, so sharing the inventory cannot
widen a stage's semantics. Unknown future Roslyn operation kinds fail closed.

`SharpProof.Dataflow` supplies deterministic fixpoint evaluation, partial
orders, joins, widening, havoc, and interval/congruence, sequence-cardinality,
and nullness domains. Source method effects are solved by stable SCC order.
Its canonical-domain opt-in stores validated strictly growing transfer results
directly; ordinary custom domains retain the normalizing join path. Managed
effect-flow analysis remains on that generic path until its representation
contract has independent regression coverage.
The out-of-process worker also projects validated `ApiSpec` result nullness and
array cardinality into spec-justified Boolean and integer proxies. This is a
bounded call-result integration; it does not use roslyn-analyzers entities,
points-to state, or general CFG transfer.

External effect analysis uses a symbol-resolved `ApiSpecTable` or an explicitly
trusted, complete effect contract. Compiler-side callable lowering binds only
eligible resolved `ApiSpec` rows into exact witness metadata; the worker
revalidates those witnesses against its matching table. Unmodeled or untrusted
metadata effect behavior is unknown; relational postcondition summaries do not
act as effect summaries. The separate build-time implementation-IL relation
decoder is bounded to exact static scalar bodies and is not a general IL
interpreter.

Importing a source or metadata effect summary is also conditional on the
callee's entry contract. Analyzer and compiler-artifact runs use the exact
contract binder plus call-site abstract facts to establish every `Requires`
and closed parameter precondition. Standalone effect analysis conservatively
detects direct clauses, closed attributes, and companion intent and marks the
summary incomplete when it cannot prove the obligation. Invalidly placed
clauses cannot refine a call into a complete effect proof. Both policies and
their summary-incompleteness propagation are declared parts of the
`effectAnalysis` trusted computing base.

## Contracts and modular verification

`Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old` bind as normal C#
operations. `Old` is pre-state substitution. `[ContractFor]` companions are
validated by the analyzer's compilation-end action after all generators have
contributed their syntax trees. The package's incremental generator is an
empty loading hook; validation uses exact symbol identity, including generics,
constraints, ref/scoped kinds, nullability, defaults, and return shape.

The worker composes quantifier-free callee relations inferred from exact
acyclic source bodies, exact implementation PE bodies, or explicitly enabled
audited specification packs. Each origin produces the same typed IR relation;
Z3 proves the resulting caller obligation, while summary-schema-2 evidence records the
complete transitive dependency closure. This remains a direct static scalar
boundary, not general recursive, virtual, or heap-aware source-callee modular
verification. The analyzer
combines exact compiler-bound replay with a managed CFG abstract interpreter
over Boolean, nullness, integer-interval, sequence-cardinality, and effect
facts. Comparison edges refine both scalar operands and retain only joined facts
valid on every incoming path. It reports a `Requires` violation only at a definitely executed call
whose receiver/argument prefix completes normally and whose instantiated
condition is definitely false. The worker checks only the bounded `Ensures`
subset supported by its admitted acyclic CFG executor; deep or otherwise
unsupported postconditions abstain.

Proof evidence is type-safe. Approximations cannot construct an assumption.
`Proven` is created only by the proof kernel after unsat-core hygiene checks.
For SAT, the proof kernel first requires exact assignment closure over the
requested Boolean/integer model variables and re-evaluates every lowered
assumption as true and the lowered goal as false. The worker then independently
executes the compiler-produced whole-body program along the concrete CFG path,
reconstructs its post-state, and evaluates the original `Ensures`. `Refuted` is
created only when both layers observe the violation. Contract-only ordinary
`void` methods have an exact zero-step whole-body replay. Constructor
postconditions abstain as `UnsupportedBody` until base-constructor and field-
initializer semantics are lowered.

## Manifest, protocol, determinism, and cache

IR identity is structural and factory-scoped. Formula construction, proof
cores, diagnostics, and serialized responses are stably ordered.

Protocol version 13 carries the compiler-artifact path/digest, reporting
policies, budgets, and cache controls. Source-generated System.Text.Json
metadata requires every wire property and rejects unknown properties.
Object property order is irrelevant. Semantic validation checks enum values,
nulls, claim ownership, dense ordinals, assumptions, and allowed result payloads.
Counts are derived from the callable and claim arrays rather than stored again.

The compiler produces a schema-21 closed artifact containing selected claims,
portable typed IR, relational/spec call bindings, effect constraints and replay
events, diagnostics, and mapped locations. One SHA-256 covers the full canonical
artifact bytes, including effect-only callables without a graph. Source and
reference inventories and duplicated provenance authorities are absent from the
wire. Producer reporting IDs remain stable opaque labels.

Schema 21 requires an explicit execution mode, havoc origin, and nullable
UTF-16 source span for each operation. The temporary Total mode uses signed
32-bit default integers, deterministic bitvector division and remainder, and
explicit Throw/ExceptionalExit edges. Legacy remains authoritative during
migration, and the existing SMT backend rejects Total queries. The candidate
CallableSolverSession encodes total Boolean and fixed-width integer terms in
one native solver per callable. Each query activates its own assumption subset
and negated goal; models contain only that query's requested variables. It
shares native scheduling and resource accounting with the legacy backend, while
active cancellation or recognized infrastructure failure retires only the
candidate session. An explicit candidate frontend shares typed scalar operation
rules between body evaluation and guarded clauses. Body operands are captured
in source order; local fault guards lead to Throw/ExceptionalExit. Clauses keep
safety separate from value, including through Old and lazy expressions. A shared
context gives entry inputs, mutable parameter storage, Old snapshots, and Result
distinct identities. Validated specification calls are omitted from runtime
body evaluation. Partial/generic contexts, ref locals, heap operations, ordinary
calls, cycles, and checked binary or unary arithmetic currently abstain.

The candidate region route supports scalar faults, throw-null, ordered canonical
runtime catches, and nested rethrow. Each lexical catch snapshots both the
original exception kind and throwing operation, so an inner handled fault cannot
replace an outer rethrow's provenance. One finally per callable is lowered once
with a continuation selector. Return values are captured before finally mutates
storage; exceptional continuations resume the original throw explicitly after
mixed normal and exceptional joins. Construction is bounded and cancelable, and
the final graph must remain acyclic. Filters, multiple or nested finally regions,
exception-object locals, object construction, calls, and heap effects remain
incomplete. A shared finally whose dispatch creates a syntactic cycle remains
incomplete even if individual concrete executions terminate.
Owned scalar local storage receives typed initial values at the live body entry,
after the complete contract prologue. Valid C# definite assignment makes those
values unobservable; they preserve normal-only local assignments across a shared
finally join. Canonical parameter and Old identities retain their existing rules.

The standalone passive VC builder owns one immutable Total scalar candidate.
It derives SSA writes, guarded joins, block and edge reachability, and normal
return obligations from the original acyclic program. Unchanged incoming
versions are forwarded. Entry feasibility activates guarded Requires and the
intrinsic typed domains; body Assume filters point reachability and retains its
own core provenance. Construction is bounded and cancelable. Each callable
uses one solver session with the existing method resource meter. Refutations
must validate every SSA assignment and replay the original body and guarded
postcondition; displayed models contain canonical entry parameters only. The
source test adapter rejects mixed contexts and incomplete lowering. Legal
prologue Assume clauses carry their static Safe-and-Value filter in the original
Total program, with used UserAssume IDs retained for conditional and vacuous
proofs. Their elided arguments do not emit runtime evaluations or throws.
Late or conditional placement remains unsupported. Decoding binds each filter
to its owned clause and requires reachable prologue placement before body
execution. The optional Total artifact and shadow comparison reuse decoded
preparations; authoritative worker responses and cache claims remain legacy.

The artifact is trusted build output. ArtifactValidator checks its digest,
validates semantic shape and claim/type/IR bindings, and decodes each graph once.
The prepared snapshot passes through the launcher to the in-process worker.
Before cache access the worker binds it to the current artifact digest, budgets,
and runtime key. Compiler diagnostics fail as CompilationFailure; malformed IR
or an expression-depth mismatch fails as CompilerManifestMismatch. The worker
has no Roslyn dependency and does not reconstruct a compilation or reread source
and reference files.

ProofKernel alone constructs proof outcomes. For callable counterexamples it
checks the backend model, assumptions, and transformed goal, executes the
concrete IR path, reconstructs result and prestate values, checks source integer
domains, and evaluates the original Ensures clause before creating Refuted.
Unsupported instructions on other paths do not block replay. Executed calls
without a concrete registered host become CounterexampleNotReplayable;
inconsistent replay becomes CounterexampleReplayFailed.

Modeled replay tracks approximation values only when they are actually read,
including reads in the original postcondition and its explicit Total-mode
guard. Overwritten values and untaken lazy operands do not count as reads.
Input havoc binds to the original entry model; generic spec-result havoc
cannot authorize Refuted. Total replay without a successful clause guard
abstains, so deterministic completion values cannot hide an undefined clause.

Effect replay remains an interpreter of compiler-produced unconditional events.
It derives effects, capabilities, and exact exception hierarchy, evaluates the
selected constraint, and matches the witness. It does not execute user code or
invoke SMT. Conditional and may-only conflicts remain typed Unknown.

Cache schema 15 reuses every valid complete response, including effects,
semantic Unknown, and empty claim sets. The key combines the artifact digest,
worker/Z3/API-spec identities, and every semantic budget. Cancellation, timeouts,
backend failures, and infrastructure failures are not cached. Reporting policy
does not change the semantic cache payload.

RunVerifier starts one worker process with a hard deadline and kills its process
tree on timeout or cancellation. The worker's project budget includes launcher
artifact preparation and uses elapsed-time checks as well as cancellation.
The child prepares manifest, request, optional SARIF, and result in its private
invocation directory. RunVerifier holds sorted leases for the canonical stable
members through promotion and validation against that private invocation. Each
file is replaced atomically, with the result written last. Standalone launcher
publication, invalidation, and reset use the same leases. Their lock sidecars stay
in place to keep cooperating owners on the same lock. Before rewriting child
destinations, publishers reject outputs inside the declared worker runtime,
including existing leaf symlinks into it. Invalidation uses the same path check.
Input and publication failures retain the launcher's exit codes and messages.
Docker owns CPU and memory isolation.

## Activation and release gates

`SharpProofProfile` accepts `advisory`, `strict`, and `off`; the package
default is `advisory`. `SharpProofFeatures` accepts `effects`, `contracts`, and
`all`; the default is `all`. A custom host can provide the equivalent
compilation-global `sharpproof_profile` and `sharpproof_features` keys. In a
package build, `SharpProofProfile` remains an MSBuild setting because it
controls verifier activation and analyzer/generator inclusion; a global
`sharpproof_profile` must match it. The `off` profile omits analyzer/generator
items and constructs no analysis session. Unsupported unannotated analyzer
callables are silent, while
unsupported explicitly selected callables report SP0047.

The advisory analyzer has a conservative compilation-start fast path for
contract-free, unselected source. It retains configuration validation and
`SHARPPROOF_CONTRACTS` rejection while skipping semantic-session construction
and per-method callbacks. Final compiler-artifact collection is a separate
build-only analyzer and is not loaded by ordinary advisory builds. Strict mode
never takes the fast path. The activation probe is part of the declared
discovery trusted computing base; new selection or implicit-call syntax must
extend the probe and its regressions in the same change.

The advisory activation probe distinguishes contract/attribute candidates
from ordinary call-bearing code. A candidate compilation retains method
attribute, clause-placement, intrinsic, rejection, suppression, subset, and
effect processing. For otherwise contract-free source, the probe reads
portable-executable custom-attribute metadata without populating Roslyn symbol
caches. It registers operation-block precondition screening only when a
referenced assembly contains a closed SharpProof parameter or return contract;
compilation references receive the equivalent symbol check. Thus external
closed preconditions remain visible to unannotated callers, while ordinary
source and BCL calls create no semantic session. Contract inventories,
companion resolution, binders, API specifications, and effect analysis are
independently lazy and are created only on first demand.

Before allocating a precondition CFG, a sound negative screen walks calls
owned by that callable and uses the same cached binder as full analysis. It
skips the CFG only when every target binds successfully with zero entry
clauses. Operation-root or binding failure, a possible entry clause, and
relevant static initialization all retain full fail-closed analysis. The
activation probe, lazy compilation model, pipeline, screen, and binder-owning
session are part of the declared effect-analysis trusted computing base.
When the containing operation tree has a relevant nested owner, the pass
creates the root CFG once and follows Roslyn local-function and anonymous-
function child graphs recursively. Each callable is deduplicated by compiler
symbol, analyzed under its own flow state, and records its own outcome.
Expression-tree lambdas are treated as quoted code and remain unknown rather
than producing an execution diagnostic.

The verifier is optional in advisory builds and mandatory in strict builds;
explicitly setting `SharpProofVerify=false` with `strict` is a configuration
error. `SharpProofVerifyPolicy` controls incomplete selected analysis;
`SharpProofAssumptionPolicy` controls SP0048 reporting for user assumptions and
trusted evidence. A refutation, malformed response, backend/replay failure,
containment failure, or other infrastructure failure is fatal regardless of
policy. The preview interface rejects the removed `SharpProofMode` and
`all-experimental` compatibility inputs.

The current gate includes:

- exhaustive Roslyn operation-kind and architecture checks;
- compiler-enforced banned APIs;
- lattice laws and finite-CFG checks;
- executable witnesses and mutation probes for every claim-bearing API-spec
  facet and postcondition;
- IR/C# and IR/SMT differential oracles;
- replay and unsat-core checks;
- snapshot-corpus and metamorphic invariance;
- cache/concurrency/cancellation determinism;
- worker/package consumer smoke checks;
- fixed-seed fuzzing and a five-minute corpus wall-time budget.

Unannotated advisory latency samples alternate real compiler-only and
SharpProof-imported MSBuild rebuilds under the repository-selected SDK. The
fixture contains ordinary source and BCL calls, so it exercises the
no-precondition callable screen rather than only the compilation-start fast
path. The package policy separately proves that `SharpProofProfile=off` omits
analyzer items and verifier invocation, while retained-memory checks exercise
the call-free unannotated advisory analyzer driver and its no-session fast
path. The worker is isolated in
`SharpProof.Verifier`; the portable `SharpProof` package contains only
analyzer/generator assets and depends exactly on `SharpProof.Attributes`. Each
package has a portable-PDB symbol package with SourceLink, and the package
workflow records exact package identities. The corpus reports explicit, silent, and total
semantic Unknown rates. A `Supported` case producing `Unknown` or
`SilentUnknown` fails with zero tolerance. The supported-case and supported
OSS-method floors cannot decrease, while total and per-reason Unknown counts
for `IntentionallyUnsupported` cases cannot exceed the checked-in ratchet.

The active contract is `eng/acceptance`.
