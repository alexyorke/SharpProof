# SharpProof Coverage And Limits

SharpProof is beta software. It is useful for enforcing and inspecting bounded
static contracts, but it is not a whole-program execution engine.

## What Coverage Means

Coverage is evidence-backed and member-level. SharpProof should not claim a
single percent of the .NET SDK as covered until the effect-summary and BCL
classification data can report that number directly.

The analyzer combines:

- Roslyn operation and control-flow analysis
- symbolic IR facts
- generated build-time effect summaries
- bounded SMT/Z3 proof queries
- conservative fallback for unsupported behavior

## Current Limits

- Unsupported C# and library shapes can remain unknown or unproven.
- Unknown results distinguish unsupported syntax, unsupported operations,
  missing library models, dynamic, external, and recursive boundaries, solver
  disablement, budgets, timeouts, native failures, cancellation, and invalid
  contract input through the [stable unknown-reason taxonomy](unknown-reasons.md).
- Regex and string reasoning are partial.
- Solver assignments are examples, not exhaustive input sets. Regex domains,
  opaque non-null references, disjunctions, and alternative-path merges are
  explicitly approximate; unsupported model shapes remain visible.
- Ownership and mutation reasoning are local; there is no full Rust-style borrow
  checker yet.
- Runtime hazards are source-visible and bounded, not a guarantee that every
  possible runtime exception is modeled.
- Known runtime-hazard coverage gaps have explicit acceptance criteria and
  executable current-behavior regressions in
  [the runtime-hazard backlog](runtime-hazard-backlog.md).
- Complexity is asymptotic CPU-work classification for supported method shapes,
  not wall-clock timing, allocation complexity, or JIT/cache behavior.
- External calls, dynamic dispatch, reflection-heavy flows, native interop, and
  hidden framework behavior can force conservative results.

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

Unknown candidates stay opt-in in analyzer builds. Set
`sharpproof_runtime_hazard_mode = unknowns` for informational `SP0033` only,
`sites-and-unknowns` for warning-level proven sites plus informational unknowns,
or `all-and-unknowns` to add method summaries. SP0033 has its own diagnostic ID,
structured proof and trigger properties, explain metadata, and exact baseline
evidence, so it can be suppressed with normal `.editorconfig`, pragma, or
`SharpProof.Baseline.json` controls without hiding proven SP0011 hazards.

## Soundness Rule

When SharpProof cannot justify a proof, it must not silently upgrade the result
to proven. Unsupported, timed-out, canceled, native-load-failed, or over-budget
proof obligations should remain conservative and should surface an unknown or
unsupported reason where the public surface supports it.
