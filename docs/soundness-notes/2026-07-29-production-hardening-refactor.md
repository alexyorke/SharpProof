# Production hardening and coordinator refactor

> Historical evidence: this note records a dated checkpoint. It is not a
> current product guide; see the [documentation map](../README.md) and
> [coverage and limits](../coverage-and-limits.md).

Date: 2026-07-29

This tranche preserved the bounded preview semantics while reducing several
compiler-host, protocol, and analyzer maintenance risks.

## Compiler and artifact boundary

- Compiler artifact schema 8 retains closed compiler-option wire enums and
  adds raw plus effective per-tree preprocessor symbols. Roslyn-to-wire
  conversion is exhaustive, unknown future values fail closed, and an
  effective runtime-contract symbol is rejected independently by the worker.
- Exception constraints, evidence text, and exact witness hierarchies use one
  canonical encoder containing the full assembly identity and documentation
  type-reference ID. Independent replay therefore distinguishes aliased
  same-simple-name assemblies.
- The internal `ReferencesSupersedeLowerVersions` option is read through a
  shape-checked Roslyn property rather than a compiler-generated backing-field
  name.
- Contract API metadata names now have one compiler-symbol-based source of
  truth.
- Contract-kind ordinals and analyzer configuration options no longer depend on
  enum or array positions.

## Analysis and architecture

- Contract canonicalization and generic type specialization were extracted from
  clause binding. The checked contract-binder coordinator fell from 1,797 to
  1,080 expression nodes.
- CFG call-site discovery and replayability checks were extracted from
  precondition evaluation. The checked call-site coordinator fell from 1,380 to
  742 expression nodes.
- Protocol JSON syntax, canonical writing, and validation support were separated
  from response policy validation. The checked protocol coordinator fell from
  3,278 to 2,850 expression nodes.
- Scalar comparison edges now refine both operands. Equality intersects both
  integer intervals, while ordered comparisons propagate safe opposing bounds.
- Contract lowering and effect discovery consume a shared closed Roslyn
  operation catalog while retaining separate shape and type checks.

## Validation

- Structural acceptance and generated-file verification pass.
- Analyzer, contracts, effects, frontend, architecture, protocol, worker, and
  package-backed tests pass.
- The broad test run excludes performance tests; the two performance contract
  tests pass when run sequentially in their isolated lane.
- The corpus remains intentionally bounded. The pre-tranche 480-case ratchet
  allows 309 typed `Unknown` outcomes; this tranche improves precision within
  the supported acyclic scalar subset rather than widening loops, heap, async,
  recursion, or general-call semantics.
