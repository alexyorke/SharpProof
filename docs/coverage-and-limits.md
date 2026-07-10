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
- Regex and string reasoning are partial.
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

## Soundness Rule

When SharpProof cannot justify a proof, it must not silently upgrade the result
to proven. Unsupported, timed-out, canceled, native-load-failed, or over-budget
proof obligations should remain conservative and should surface an unknown or
unsupported reason where the public surface supports it.
