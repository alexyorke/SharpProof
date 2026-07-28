# SharpProof product bug sweep - 2026-07-27

> Dated evidence: this note records the implementation and validation state
> reviewed on 2026-07-27. Current behavior remains defined by
> [SEMANTICS.md](../../SEMANTICS.md) and the maintained
> [coverage inventory](../coverage-and-limits.md).

This sweep concentrated on product semantics rather than packaging appearance:
call-site precondition replay, clause-source consistency, effect summaries,
claim visibility, proof-domain boundaries, and counterexample replay.

## Defects closed

- Expression-bodied properties and indexers now construct a usable compiler
  control-flow graph, so direct concrete precondition violations are no longer
  skipped.
- Base and `this` constructor initializers now participate in call-site replay.
  An implicit `this` receiver is treated as non-null without requiring a
  fabricated concrete receiver value.
- Precondition replay now abstains when a preceding static call, the target
  static method, or the target constructor can trigger source type
  initialization that is not proven to complete. A throwing type initializer
  can no longer yield a diagnostic for a call that is never entered.
- Direct member clauses and `ContractFor` clauses now have one consistent
  precedence rule in full binding, requires-only binding, analyzer replay, and
  manifest construction. Any direct clause makes the target member the source
  for all clauses; clauses are never merged across the two sources.
- Value-type object creation no longer reports a managed heap allocation.
  Reference-type creation remains allocating, and constructor effects are
  still analyzed separately.
- `nameof` and constant interpolated strings are exact effect-free compile-time
  operations. Runtime interpolation remains incomplete because implicit
  formatting can invoke user behavior; it is not promoted to a no-throw or
  zero-allocation fact.

## Adversarial regressions

The new tests cover constructor initialization, expression-bodied property and
indexer calls, implicit receivers, non-completing prefixes and target type
initialization, mixed direct/companion contracts, value-type construction,
compile-time string operations, and manifest ownership.

An additional worker regression uses two non-null arrays with equal contents
but distinct identities. The worker returns typed `Unknown` rather than
treating reference equality as structural sequence equality. This confirms
that the current lack of a general heap, alias, and sequence-element model is
fail-closed.

## Validation evidence

The exact changed tree passed:

- 44 contract tests, 45 effect tests, 61 analyzer tests, and 161 worker tests in
  focused Debug runs;
- maintained-document verification and the production-size ratchet;
- the complete Release acceptance contract with zero build warnings or errors,
  649 passing tests and one expected unsupported-host skip;
- 1,000 of 1,000 frontend/IR/SMT differential fuzz agreements;
- the 480-case corpus, cache/concurrency/cancellation, package, architecture,
  containment, and performance gates.

The acceptance corpus reported 299 explicit and 10 silent `Unknown` cases, a
total rate of 64.375 percent. That is primarily a breadth measurement for the
deliberately bounded preview, not a correctness failure; unsupported behavior
is required to remain visible or silent according to selection policy rather
than guessed.

## Remaining product limits

- Concrete analyzer replay intentionally handles only calls whose receiver,
  arguments, reachability, and required evaluation prefix are exact and
  non-throwing. Nested expressions and benign but unmodeled type initialization
  can therefore abstain.
- Runtime interpolation, unresolved calls, reference/array identity, sequence
  elements, mutable heap state, aliasing, loops, recursion, async/iterators,
  and broad managed-language lowering remain outside the proof subset.
- Spec-modeled calls can establish normal-return facts, but a candidate
  counterexample that requires executing such a call remains `Unknown` when
  independent replay cannot execute it.
- The project remains a bounded preview rather than an arbitrary-C# verifier.
  Real Visual Studio/Rider qualification, pilot-library cycles, protected
  release promotion, first private/public publication, and independent human
  soundness reviews remain release gates.
