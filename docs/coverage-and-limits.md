# SharpProof Coverage And Limits

SharpProof is beta software. It is useful for enforcing and inspecting bounded
static contracts, but it is not a whole-program execution engine.

## What Coverage Means

Coverage is evidence-backed and member-level. SharpProof does not infer SDK
coverage from namespace or member-name catalogs.

The analyzer combines:

- Roslyn operation and control-flow analysis
- symbolic IR facts
- lazy metadata/IL effect analysis and exact effect contracts
- bounded SMT/Z3 proof queries
- explicit unknown results for unsupported behavior

## Current Limits

- Unsupported C# and library shapes can remain unknown or unproven.
- Unknown results distinguish unsupported syntax, unsupported operations,
  missing library models, dynamic, external, and recursive boundaries, solver
  budgets, timeouts, native failures, cancellation, and invalid
  contract input through the [stable unknown-reason taxonomy](unknown-reasons.md).
- Regex and string reasoning are partial.
- Solver assignments are examples, not exhaustive input sets. Regex domains,
  opaque non-null references, disjunctions, and alternative-path merges are
  explicitly approximate; unsupported model shapes remain visible.
- Ownership and mutation reasoning are local; there is no full Rust-style borrow
  checker yet.
- Nullable-flow facts combine Roslyn flow state with the supported CodeAnalysis
  contracts through one [shared fact model](nullable-flow-facts.md). Attributes
  are trusted contracts; they do not verify that an annotated implementation
  keeps its promise.
- Runtime hazards are source-visible and bounded, not a guarantee that every
  possible runtime exception is modeled.
- Fact collection, structural null-state inspection, and control-flow/state
  merges have configurable positive caps. Exceeded caps emit stable evidence
  instead of disappearing; see [bounded analysis limits](analysis-limits.md).
- Complexity is asymptotic CPU-work classification for supported method shapes,
  not wall-clock timing, allocation complexity, or JIT/cache behavior.
- External calls, dynamic dispatch, reflection-heavy flows, native interop, and
  hidden framework behavior can force conservative results.
- Transient Z3 failures retry with a recycled context by default; permanent
  native availability failures and explicit thread-context maintenance remain
  visible through [SMT lifecycle health](smt-lifecycle.md).
- The public packages bundle Z3 native assets for Windows x64 and macOS x64.
  Linux, arm64, and other unsupported RIDs retain a permanent conservative
  fallback instead of crashing the analyzer or query host. See
  [native SMT packaging and platform support](native-smt-packaging.md).

## Common Runtime-Hazard Shapes

The bounded model includes direct `Count` guards for count-backed indexers and
for empty `Queue<T>`, `Stack<T>`, and `PriorityQueue<TElement, TPriority>`
`Peek`/`Pop`/`Dequeue` operations. A guard that proves the collection non-empty
prunes the candidate; a path that proves `Count == 0` reports
`InvalidCollectionCardinality` and `InvalidOperationException` evidence.

Nullable result facts flow through `Nullable<T>.HasValue`, `.Value`, explicit
casts, coalescing, and conditional access. They also flow through known
completed async shapes: `Task.FromResult`, `ValueTask.FromResult`, the
`ValueTask<T>(T)` constructor, `await`, `.Result`, and
`GetAwaiter().GetResult()`. These models expose the wrapped value; they do not
claim to predict arbitrary task scheduling, cancellation, faults, or custom
awaiters.

Known bounded-integral `System.Math` calls also lower to typed symbolic IR.
`Math.Min` and `Math.Max` preserve their bound relationships, `Math.Abs`
preserves non-negativity while exposing signed-minimum `OverflowException`
hazards, and `Math.Clamp` preserves ordered constant, equal, or type-extremum
bounds. Floating-point overloads and clamp bounds whose order is not
intrinsically proven stay on the conservative compatibility path.

Runtime-hazard trigger facts are carried as typed
`SymbolicExceptionPreconditionAtom` IR. Formula-shaped compatibility inputs are
projected when the candidate is created. An input that cannot be represented in
typed IR becomes an `Unsupported` fact, returns `Unknown` with
`unsupported_typed_projection`, and renders source-like evidence as
`unknown(...)`. Formula provenance is metadata only; it is not proof control
flow.

Runtime hazards remain structured facts in the unified analysis result. They
are consumed by contract proofs and the CLI rather than emitted as standalone
analyzer diagnostics.

## Soundness Rule

When SharpProof cannot justify a proof, it must not silently upgrade the result
to proven. Unsupported, timed-out, canceled, native-load-failed, or over-budget
proof obligations should remain conservative and should surface an unknown or
unsupported reason where the public surface supports it.
