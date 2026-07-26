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
Worker                -> Attributes, Contracts, Frontend, Ir, Smt, Specs,
                         Verify, Worker.Protocol
Worker.Launcher       -> Worker.Protocol
```

Build-only references to `SharpProof.Meta.Analyzers` are omitted. The
architecture suite compares every direct project reference against this graph.

The Roslyn analyzer has no verifier, SMT, Z3, native, or retired-engine
dependency. Z3 is allowed only in `SharpProof.Smt`, which is packaged below
`tools/net8` for the worker. `Ir`, `Dataflow`, and `Specs` contain no C# syntax
types. Production semantic-model acquisition passes through the single audited
`SharpProof.Frontend.Host.CompilationModelProvider`.

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
External calls use a symbol-resolved `ApiSpecTable` or an explicitly trusted,
complete effect contract. Unmodeled or untrusted metadata is unknown; there is
no IL interpreter.

## Contracts and modular verification

`Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old` bind as normal C#
operations. `Old` is pre-state substitution. `[ContractFor]` companions are
validated by an incremental, no-source generator using exact symbol identity,
including generics, constraints, ref/scoped kinds, nullability, defaults, and
return shape.

Verification is modular at inlining depth zero: assert a callee precondition
and assume only a verified or explicitly trusted postcondition/specification.
The analyzer performs cheap effect projections and concretely replayed
`Requires` checks. Deep `Ensures` obligations run only in the worker.

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
explicit budgets. On Windows, the launcher applies a Job Object with process
and memory limits. Its content-addressed cache includes semantic, protocol,
tool, compilation, reference, option, target-framework, and spec identity.
Only complete hygienic `Proven` and replay-validated `Refuted` results are
cacheable.

## Activation and release gates

`SharpProofMode` accepts `off`, `effects`, `contracts`, and
`all-experimental`; the package default is `off` and conditionally omits its
analyzers from the compiler. A custom host that loads the analyzer can use the
equivalent compilation-global `sharpproof_mode` key; `off` constructs no
session. Feature diagnostics are Info and disabled by default. Unsupported
analyzer callables are silent.

The v2 gate includes:

- exhaustive Roslyn operation-kind and architecture checks;
- compiler-enforced banned APIs and repository meta-analyzers;
- lattice laws and finite-CFG checks;
- executable spec witnesses and mutation probes;
- IR/C# and IR/SMT differential oracles;
- replay and unsat-core checks;
- snapshot-corpus and metamorphic invariance;
- cache/concurrency/cancellation determinism;
- worker/package consumer smoke checks;
- fixed-seed fuzzing and performance budgets.

The active contract is `eng/acceptance/v2`. `eng/acceptance/v1` is an immutable
historical tree and is not a compatibility constraint for this coordinated
breaking preview.
