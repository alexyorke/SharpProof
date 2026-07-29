# Semantic vacuity evidence - 2026-07-29

## Problem

The worker previously recognized contradictory preconditions only when their
lowered conjunction was the literal Boolean `false`. A semantic contradiction
such as `value > 0` together with `value < 0` could therefore produce an
ordinary `Proven` result or be mislabeled as `NoModeledNormalReturn`.
Likewise, `NoModeledNormalReturn` was limited to a literal-false completion
predicate plus an `Ensures(false)` proxy. That did not establish whether a
nonliteral normal-completion predicate was satisfiable.

## Soundness decision

For nonliteral `Requires` clauses in the supported scalar proof domain, the
worker submits a separate, budgeted proof-kernel query whose goal is `false`
and whose assumptions are exactly those preconditions.

- `Proven(false)` establishes that the preconditions are unsatisfiable and
  permits `ContradictoryPreconditions`.
- A replay-validated `Refuted(false)` establishes a satisfying precondition
  model and permits ordinary claim verification.
- `Unknown` or malformed backend evidence never becomes vacuity evidence. It
  downgrades an otherwise-proven postcondition to the same typed `Unknown`.
- A replay-validated postcondition refutation remains valid even if the earlier
  satisfiability probe was unknown, because its model independently satisfies
  the same preconditions.

The probe excludes user assumptions, body facts, API specifications, and the
normal-completion predicate. Consequently, contradictory preconditions remain
distinct from a method that has satisfiable entry conditions but no modeled
normal return. The probe uses the method resource budget and the existing
isolated solver lane.

After the precondition probe, the worker independently classifies normal
completion:

- A literal `true` completion predicate is an executable witness and needs no
  solver query; a literal `false` predicate establishes
  `NoModeledNormalReturn`.
- A nonliteral completion predicate is checked with a second budgeted
  false-goal query under preconditions, source-domain facts, API
  specifications, and the body's completion evidence.
- `Proven(false)` establishes `NoModeledNormalReturn`; a replay-validated
  `Refuted(false)` establishes a modeled normal return.
- `Unknown` prevents an otherwise-proven postcondition from being reported as
  `Proven`. A replay-validated postcondition refutation still wins because it
  independently witnesses normal execution.
- User `Assume` evidence is excluded from this classification. Even when its
  predicate duplicates normal completion, the body contributes its own
  lowered evidence.

## Executable evidence

`WorkerTcbEdgeCaseTests` covers a semantic contradiction, a satisfiable
precondition with an ordinary proof, a satisfiable precondition with
`Ensures(false)` and a replayed refutation, and an infrastructure-unknown
precondition probe followed by an otherwise-proven claim. It also covers
nonliteral reachable and unreachable completion, an unknown completion probe,
and a duplicate user assumption that cannot supply completion evidence.

This trusted-boundary change still requires the independent human reviews
specified by the acceptance contract.
