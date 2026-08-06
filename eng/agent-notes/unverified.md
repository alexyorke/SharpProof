# Unverified / could not demonstrate

## `ManagedContractFacts.cs:158` — reversed-refinement branch

The line is `Evaluate(binary.Left, values)` inside the
`IrBinaryTerm { Right: IrVariableTerm }` arm of `Assume`.

**Status: could not write a test that depends on it.** Two attempts, both
passed for the wrong reason.

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
