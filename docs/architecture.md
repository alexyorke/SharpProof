# SharpProof 1.0 preview architecture

SharpProof 1.0 is an effect-first, soundness-first preview. The supported
product is the effect cluster plus compiler-bound call-site preconditions.
Unsupported code is an abstention, not an invitation to guess.

## Dependency direction

The active production graph is a checked DAG:

```text
Attributes
Ir
Dataflow
Specs                 -> Ir
Frontend              -> Ir
Contracts             -> Frontend, Ir, Specs
Effects               -> Dataflow, Frontend, Specs
Verify                -> Ir, Specs
Smt                   -> Ir, Verify
CompilerArtifact      -> Contracts, Frontend, Ir, Specs, Worker.Protocol
ContractForGenerator  -> Contracts
Analyzer              -> CompilerArtifact, Contracts, Effects, Frontend, Ir,
                         Specs
Worker.Protocol
Worker                -> CompilerArtifact, Dataflow, Ir, Smt, Specs, Verify,
                         Worker.Protocol
Worker.Launcher       -> CompilerArtifact, Ir, Specs, Worker.Protocol
```

Build-only references to `SharpProof.Meta.Analyzers` are omitted. The
architecture suite compares every direct project reference against this graph.

The Roslyn analyzer has no verifier, SMT, Z3, or native dependency. Z3 is
allowed only in `SharpProof.Smt`, which is packaged below
`tools/net9` for the worker. `Ir`, `Dataflow`, and `Specs` contain no C# syntax
types. Production semantic-model acquisition passes through the single audited
`SharpProof.Frontend.Host.CompilationModelProvider`.

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
code and catalog generation, effect analysis, replay, policy, result assembly,
and cache validation. Compiler-input identity, typed canonical hash encoding,
and protocol validation have their own non-overlapping inventories rather than
being hidden inside the cache component. API-spec content identity is likewise
separate from resolution and instantiation. The declarative API catalog, its
generator, and the generated matcher/instantiator source are one audited
`apiSpecificationCatalog` component. The C# scalar type, conversion, checked
arithmetic, and IR-operator rules likewise come from the versioned
`SharpProof.Frontend/CSharpScalarSemantics.json` catalog and its verified
generated source. Launcher containment and publication remain separately
checked by architecture, package, and integration tests.

Source complexity is measured independently of formatting. Repository,
coordinator, algorithm-file, and member ratchets count Roslyn expression nodes,
decision points, and declarations while excluding whitespace, comments, line
wrapping, and optional block braces. Physical and nonblank line totals are
reported only as information. This replaced the historical physical/nonblank
LOC and "10% smaller" gates, which rewarded brace removal and line collapsing
rather than architectural decomposition.

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
trusted, complete effect contract. Compiler-side callable lowering binds only
eligible resolved `ApiSpec` rows into exact witness metadata; the worker
revalidates those witnesses against its matching table. Unmodeled or untrusted
metadata is unknown; there is no IL interpreter.

## Contracts and modular verification

`Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old` bind as normal C#
operations. `Old` is pre-state substitution. `[ContractFor]` companions are
validated by an incremental, no-source generator using exact symbol identity,
including generics, constraints, ref/scoped kinds, nullability, defaults, and
return shape.

At inlining depth zero, the current verifier consumes only facts from
compiler-bound `ApiSpec` rows within its admitted call boundary. This is not
general source-callee modular assume/guarantee verification. The analyzer
combines exact compiler-bound replay with a managed CFG abstract interpreter
over Boolean, nullness, integer-interval, sequence-cardinality, and effect
facts. It reports a `Requires` violation only at a definitely executed call
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

IR identity is structural and factory-scoped. Solver names are canonical
indices. Formula construction, worklists, specs, proof cores, diagnostics, and
serialized responses are stably ordered. Z3 uses resource limits; wall time is
an outer process kill boundary.

Protocol version 8 binds each request to a compiler-produced closed artifact.
Stable semantic IDs identify selected callables, postcondition/effect claims, and
user/trusted evidence independently of formatting. The protocol separates run
status, callable coverage, and per-claim outcome. Central validation requires
the response to match the sealed manifest exactly, including dense ordinals,
claim ownership, summary counts, assumption summaries, and allowed payloads.

The request carries only the compiler-artifact path/digest, policies, budgets,
and cache controls. The artifact carries `WorkerFeatureSet` and applies the same
`effects`/`contracts`/`all` selection before manifest discovery: contract-only
artifacts exclude effect annotations and effect-only artifacts exclude
postcondition claims. On the supported Windows x64 worker host, the
launcher creates a startup barrier, assigns the worker to a Job Object with
process and memory limits, and only then releases verification work. Concurrent
builds use isolated artifact/request/result paths. After validating a response,
a cross-process mutex serializes publication. The stable result is deleted
first, the manifest and request are atomically replaced, and the result is
written last as the commit marker. A failed publication therefore cannot leave
a stale successful result associated with a partly updated evidence set. The
content-addressed cache includes semantic, protocol, tool, compilation,
reference, option, target-framework, canonical packaged worker runtime-closure,
and spec-content identity.
Cache schema version 9
stores only the validated semantic payload. A hit is accepted only when its
manifest hash and complete result set match the current manifest. Only
complete callables whose claims are hygienic `Proven` or replay-validated
`Refuted` are cacheable.

During Windows verification, the production analyzer observes the final
post-generator Roslyn `Compilation` and atomically emits compiler artifact
schema version 5. The compiler owns selection, contract/spec binding, effect
evaluation, and body
lowering. Every selected callable has either a typed failure record or a
portable graph containing its bound clauses, canonical variables, whole-body
CFG/IR, body start, initial environment, parameter mappings, and exact
API-spec witness metadata. Every selected effect-attribute occurrence also has
one compiler-sealed `Proven`, `Refuted`, or typed `Unknown` evidence record.
Repeated attributes retain distinct claim IDs while sharing their effective
combined constraint/evidence. A `Refuted` effect record requires a structured
unconditional direct witness that the worker independently validates against
the sealed constraint. Callable IDs, claim ownership, and user-assumption IDs
remain tied to the sealed manifest.

The artifact also contains compiler error diagnostics with mapped locations,
handwritten and generated tree hashes and parse settings, the bounded
proof-relevant compilation-option set, assembly and target identity, and
compiler/reference provenance. It intentionally contains no source text.
Readable file-backed references are required while the compiler records their
path, image hash, identity, kind, embed flag, and aliases. Resolver-dependent
`#r`/`#load`, missing-assembly resolver mode, reference supersession, and custom
assembly-identity comparers fail artifact collection as SP0049.

The launcher binds the artifact bytes and request identity into the response
and publishes the request, response, and manifest under one lock with the
result written last. The artifact is the worker's sole compilation input. The
worker validates its digest and canonical shape, requires the embedded maximum
expression depth to equal the request budget, and decodes the portable graph.
Exact manifest/lowered-callable equality, claim lists, assumption declarations,
and graph indices are checked before cache lookup or backend creation.
Compiler diagnostics fail as `CompilationFailure`; malformed lowered evidence
or option mismatch fails as `CompilerManifestMismatch`.

The worker project contains no direct Roslyn dependency and performs no
compiler reconstruction or source parsing. It does not reread reference files.
Compiler versions and MVIDs and reference paths/hashes/identities/aliases are
provenance and cache-key evidence, not a runtime compatibility gate.
`AdditionalFiles` are sealed by canonical path and content hash without
embedding their raw contents. Analyzer configuration is represented by its
observable effects on the final compilation and effective SharpProof options;
generated output is covered by its tree hashes, manifest entries, and lowered
callables.

This closes both the compiler-to-worker lowered-artifact cutover and the
independent whole-body replay gate for the bounded verifier subset. Replay
executes only the concrete CFG path selected by the model, so unsupported
operations or spec-modeled calls on other paths do not block a refutation. If
a modeled call is executed, the candidate becomes `Unknown` with
`CounterexampleNotReplayable`; other unsupported or inconsistent replay state
is a fatal `CounterexampleReplayFailed`. Result JSON includes only canonical
user-model variables, not temporary lowered variables. Optional deterministic
SARIF 2.1.0 projects the validated response under the same atomic publication
boundary and does not participate in semantic verification.

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
`SharpProofProfile=off` omits analyzer items and verifier invocation. The
worker is isolated in `SharpProof.Verifier.Win-x64`; the portable `SharpProof`
package contains only analyzer/generator assets and depends exactly on
`SharpProof.Attributes`. Each package has a portable-PDB symbol package with
SourceLink, and the package workflow records exact SHA-256 hashes, an SPDX
SBOM, and GitHub provenance/SBOM attestations. The corpus reports explicit,
silent, and total semantic Unknown rates as metrics; none is a release gate.

The active contract is `eng/acceptance`.
