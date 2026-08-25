# Allocation effect refutation replay - 2026-07-30

> Historical evidence: this note records a dated checkpoint. It is not a
> current product guide; see the [documentation map](../README.md) and
> [coverage and limits](../coverage-and-limits.md).

## Problem

The compiler collector could previously identify a definite direct effect
violation, but the worker had no independent effect representation to replay.
Every candidate effect refutation therefore failed closed as
`Unknown(CounterexampleReplayFailed)`. Reporting the compiler candidate itself
as `Refuted` would have trusted effect discovery and result construction as one
unreviewed assertion.

## Admitted replay domain

Compiler artifact schema 9 adds a sealed, compiler-neutral effect replay
artifact. The current producer emits exactly one unconditional event only for:

- a direct managed object allocation whose constructor arguments complete
  harmlessly, whose object initializer is absent, and whose source and base
  type initialization cannot add pre-body behavior; or
- a direct managed array allocation whose dimensions and initializer complete
  harmlessly.

The callable must be in the same supported source subset selected by the live
analyzer. The direct event must be reached after every possible pre-body
operation. Constructors, static constructors, unsupported callable shapes,
metadata type-initialization uncertainty, deep or malformed base chains,
throwing conversions, and conditional or may-only paths do not enter the
replay domain.

The artifact seals the selected constraint, event order and kind, syntax-tree
ordinal and content hash, source span, mapped location, compiler member and
type identities, and the expected violation witness. The manifest validator
requires the event span to fit the exact final-compilation tree snapshot.
These hashes detect accidental or isolated tampering; they do not independently
reconstruct Roslyn source binding. Contract discovery, effect analysis, and
event lowering therefore remain explicit trusted-computing-base components.

## Independent worker decision

A worker-owned interpreter validates the sealed artifact, derives only the
`Allocates` effect from an admitted allocation event, and compares that
observation with the selected claim:

- `ZeroAllocations` is refuted.
- `EffectContract` is refuted only when it excludes `Allocates`.
- `EnforcePure` is not refuted because its observable-state policy permits
  fresh allocation.
- Exception-only and capability-only contracts are not refuted by allocation.

The independently derived witness must exactly equal the compiler-sealed
witness before `Refuted` is emitted. A semantic disagreement becomes the fatal
`CounterexampleReplayFailed`; an unsupported direct event becomes
`CounterexampleNotReplayable`. Effect results remain noncacheable, cancellation
is checked throughout replay, and no user code is executed during a build.

Malformed effect contracts remain `Unknown(UnsupportedContract)` even when
entry preconditions are contradictory. A valid effect claim may be vacuously
proven only from separately established `ContradictoryPreconditions`; lack of a
modeled normal return is never effect-vacuity evidence.

## Executable evidence

The changed boundary is exercised by:

- `EffectAnalysisTests` for direct-witness completion, pre-body execution,
  type/base initialization, conversion, and exact approved `System.Object`
  identity;
- `ClaimManifestBuilderTests` for source-subset parity, sealed allocation
  events, trusted bodyless boundaries, unsupported selected callables, and
  malformed-contract precedence;
- `CompilerEffectReplayArtifactCodecTests` and
  `CompilerManifestArtifactTests` for event, constraint, tree, span, identity,
  witness, and compilation-snapshot tampering;
- `EffectCounterexampleReplayTests` and `WorkerTests` for independent semantic
  replay, cancellation, concurrent recovery, manifest equality, vacuity, and
  cache exclusion; and
- `PackageLayoutSmokeTests` for packed compiler-collector-to-worker behavior.

Trusted mutation probes cover base and metadata initialization, pre-body and
conversion completion, analyzer/collector subset parity, unsupported-candidate
downgrade, event kind, constraint and operation identity, tree binding,
allocation-policy comparison, exact witness equality, and invalid-contract
precedence.

The local acceptance run on this candidate passed every managed suite, the
package-backed consumer tests, 1,000 differential fuzz cases with zero
abstentions, the supported corpus and equivalence gates, and the unchanged
performance limits. This evidence does not replace the two independent human
soundness reviews required for release qualification.
