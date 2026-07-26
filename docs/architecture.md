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

## Determinism, budgets, and cache

IR identity is structural and factory-scoped. Solver names are canonical
indices. Formula construction, worklists, specs, proof cores, diagnostics, and
serialized responses are stably ordered. Z3 uses resource limits; wall time is
an outer process kill boundary.

The versioned worker protocol carries real source and reference paths plus
explicit budgets. On the supported Windows x64 worker host, the launcher
applies a Job Object with process and memory limits. Its content-addressed cache
includes semantic, protocol, tool, compilation, reference, option,
target-framework, and spec identity. Only complete hygienic `Proven` and
replay-validated `Refuted` results are cacheable.

## Activation and release gates

`SharpProofMode` accepts `off`, `effects`, `contracts`, and
`all-experimental`; the package default is `off` and conditionally omits its
analyzers from the compiler. A custom host that loads the analyzer can use the
equivalent compilation-global `sharpproof_mode` key; `off` constructs no
session. Feature diagnostics are Info and disabled by default. Unsupported
analyzer callables are silent.

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

Default-off latency samples alternate real baseline and SharpProof-imported
MSBuild rebuilds. A separate loaded-but-off analyzer canary covers session
creation and retained state, while the package policy proves that the shipped
default omits analyzer items and verifier invocation. The worker remains in the
package's opt-in tools payload. The corpus reports explicit, silent, and total
semantic Unknown rates as metrics; none is a release gate.

The active contract is `eng/acceptance`.
