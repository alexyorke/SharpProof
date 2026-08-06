# Unverified / could not demonstrate

## `ManagedContractFacts.cs:158` — reversed-refinement branch

The line is `Evaluate(binary.Left, values)` inside the
`IrBinaryTerm { Right: IrVariableTerm }` arm of `Assume`.

**Status: reachable — do NOT delete. Still not covered by a test that depends
on it.** Two test attempts, both passed for the wrong reason.

### Deletion is ruled out (checked 2026-08-06)

The earlier note speculated the arm might be unreachable because operands could
be normalised upstream into `value == 3`. **They are not.** `IrFactory.Binary`
(`IrFactory.cs:400-422`) interns on `children: [left.Id.Value, right.Id.Value]`
— positional — and `IrTermServices.FoldBinary` only folds constant operands, it
never swaps them. So `3 == value` keeps `Left = IrIntegerTerm(3)`,
`Right = IrVariableTerm(value)`, the `Left: IrVariableTerm` arm cannot match,
and the `Right:` arm is the only one that can. The code is live.

That makes the open question narrower and more interesting: attempt 2's SP0027
survived deleting the arm, so **that diagnostic was not caused by the
refinement at all**. Find out what actually produced it before writing a third
test — print `diagnostic.Location` and the message, and check whether it is
reported against `Narrow(value)` or against the caller's own clause.

### What was tried

1. `Contract.Requires(0 < value)` on the **callee**, with a separate caller.
   Never runs the branch: `ApplyRequires` seeds the *caller's* entry state from
   the *caller's own* clauses (`RequiresCallSiteDiscovery.cs:79`), and the
   caller had no clauses, so `ApplyRequires` returned at the null check.

2. `Contract.Requires(3 == value)` on the **caller**, calling
   `Narrow(long value)` whose clause is `value > 5`. Intent: the reversed
   refinement pins `value` to `[3,3]`, making `3 > 5` concretely false and
   raising SP0027. The test passes — **but it also passes with the entire
   `Right: IrVariableTerm` arm deleted from the production switch**, which was
   confirmed by removing it and re-running. So the SP0027 originates somewhere
   other than this refinement, and the test proves nothing about this line.

### What to do next

Find out where that SP0027 actually comes from before writing a third test.
Likely candidates: the concrete-replay path in `RequiresCallSiteAnalyzer`, or
`ManagedAbstractFlow.Refine` being reached through the `Left: IrVariableTerm`
arm after some normalization of `3 == value` into `value == 3`. If the operand
order is normalized upstream, the `Right:` arm may be **unreachable for
comparisons entirely** and only live for asymmetric operators — in which case
the honest fix is to delete it, not to test it.

Do not raise, lower, or delete a coverage baseline to make this pass.

### Method note

This is the fourth test this session that passed for the wrong reason. The
other three: two had their input constant-folded away by the IR factory or by
Roslyn, and one asserted SP0027 for a change that only affects the abstract
proving path. Every one was caught by deleting the code under test and
re-running. That check should be mandatory here, not optional.


## `ManagedAbstractFlow.cs:195` — `IsBottom` early return in `Transfer`

**Status: mutation check failed. The guard may be a pure optimisation.**

### Diagnosis (traced, and believed correct)

`TransferMany` calls `Transfer` *before* it checks `IsBottom`, and `ApplyRequires`
returns `ManagedFlowState.Bottom` when a caller's own `Contract.Requires` is
concretely false. That bottom becomes the entry state, `TransferBlock` hands it
to `TransferMany`, and `Transfer` therefore receives it. The call path is real.

### What was tried

`Analyze(method, graph, ManagedFlowState.Bottom, default)` asserting
`Result.IsReachable(root)` is false. Passes.

**But it also passes with the entire `if (state.IsBottom) return state;` block
deleted from `Transfer`.** Verified by removing it and re-running.

### What that probably means

The early return is likely a **short-circuit optimisation, not a semantic
requirement**: `TransferCore` operating on a bottom state appears to keep
producing bottom, so nothing observable changes when the guard is removed. If
that holds, no black-box test can ever cover this line meaningfully, and the
options are:

1. Prove it is pure optimisation and delete it (then the coverage gap vanishes).
2. Find a state operation that does *not* preserve bottom, which would make the
   guard load-bearing and give the test something to assert.

Investigate `ManagedFlowState`'s operations on the bottom instance before doing
either. Do not add a test that asserts something true either way.

## The changed-TCB line numbers are not stable across gate invocations

**Do not chase individual line numbers without a matched, fresh coverage
collection.** This invalidates part of the queue as written.

Observed 2026-08-06. Two runs of
`Test-SharpProofCoverage.ps1 -CoverageRoot artifacts/coverage -ComparisonRef master`
against the **same** coverage artifacts and **identical production code**
reported different uncovered sets:

| Run | Uncovered changed-TCB lines |
|---|---|
| At `a842a27d0` | `ManagedContractFacts.cs:158`, `ManagedAbstractFlow.cs:195`, `ManagedAbstractFlow.cs:1811` |
| At `59e7be1d2` | `ManagedContractFacts.cs:158`, `ManagedAbstractFlow.cs:137`, `:141`, `:142` |

The only change between them was two documentation-only commits that do not
touch `ManagedAbstractFlow.cs`. Lines 137/141/142 are the
`catch (DataflowConvergenceException)` block, which **is** covered by the
committed test `NonConvergentAnalysisDegradesToAnIncompleteSummary`.

Likely cause: the gate compares line numbers recorded in the Cobertura data,
produced by whatever build was instrumented, against line numbers computed from
a `git diff <ref>...HEAD` of the current tree. When HEAD moves, the diff side
shifts but the coverage side does not, so the two are matched against a stale
mapping. `git diff master...HEAD -- SharpProof.Effects/ManagedAbstractFlow.cs`
has no hunk anywhere near line 1811, which is consistent with that line having
been a mis-attribution rather than a real gap.

### Consequence

`ManagedAbstractFlow.cs:195` and `:1811` may never have been genuine gaps. The
mutation result for `:195` — the test passing with the `IsBottom` guard deleted
— is still a real observation and worth resolving, but it is no longer evidence
of a coverage problem.

### Next step

Re-collect coverage from a clean build at the current HEAD and take that list as
authoritative before queuing any further line. Also worth reporting upstream:
the gate should either recollect or refuse to run against artifacts whose commit
does not match HEAD.

## RESOLVED diagnosis: why `ManagedContractFacts.cs:158` resists mutation testing

Probed 2026-08-06 by printing the diagnostic from the attempt-2 fixture. It is
real and correctly targeted:

```
SP0027@line10: Call to 'Narrow' violates precondition '(v5 > 5)'
```

So the refinement genuinely happens — `value` is pinned to `[3,3]` and `3 > 5`
is false. The reason it survived deleting the arm is that **two independent
mechanisms apply the same clause**:

1. `ManagedContractFacts.ApplyRequires` seeds the caller's *entry state* from its
   bound clauses, working on **IR terms** (`RequiresCallSiteDiscovery.cs:79`).
   This is the path containing line 158.
2. `ManagedAbstractFlow.Transfer` case `IInvocationOperation`
   (`ManagedAbstractFlow.cs:254`) calls its own operation-level `Assume` when it
   transfers the `Contract.Requires(...)` invocation **in the body**.

For an explicit `Contract.Requires` the body transfer re-derives the same fact,
so removing path 1 changes nothing observable. Line 158 is *executed* but its
effect is masked.

### Consequences

- The line is **coverable** — any test whose caller has a reversed-operand
  clause (`3 == value`) will execute it. It is currently uncovered only because
  no existing test has one.
- It is **not mutation-discriminating through the analyzer**, by construction.
  Do not keep trying; a black-box test can cover it but can never fail without
  it.
- The discriminating test would have to drive `ManagedContractFacts` directly at
  unit level. `ApplyRequires` is `internal` and `SharpProof.Analyzer.Test` has
  access, but it takes a `BoundMethodContracts`, so the fixture needs a bound
  contract built from a real compilation rather than hand-constructed IR.

### Recommendation

Either add the covering test with an explicit comment that it cannot
discriminate and explain why, or build the direct unit fixture. Do not delete
the arm — it is live (see the operand-order note above), and path 1 is what
seeds entry state before the body is walked, which matters for callers whose
clauses come from closed-contract attributes rather than explicit invocations.
