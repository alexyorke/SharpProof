# SharpProof agent fix queue

One item per iteration. Both gates must pass before every commit:
`dotnet test SharpProof.Dev.Tests.slnf` and `pwsh eng/acceptance/Verify.ps1`.
A green dev suite alone is not enough — it does not run the production
complexity ratchet, which `ci.yml`, `package-consumers.yml` and `weekly.yml` do.

Branch: `agent/soundness-fixes-abstention-and-bounds`. Never touch master.

## Method

**(A) Diagnose** — trace the call path, state in one sentence why the line is
uncovered. **(B) Test** — write it, confirm it passes. **(C) Mutate** — delete or
invert the code under test, re-run, restore, confirm `git diff` is clean.

- Mutation kills the test → it is real. Commit with the evidence.
- Mutation does *not* kill the test → the code may not be load-bearing.
  Investigate reachability. If provably redundant, **delete the production code**
  and commit that as the fix. If undecided after one attempt, record in
  `unverified.md` and move on.

## Done

- [x] Commit the pending coverage tests — landed as `a842a27d0`. Coverage went
  41 → 3 uncovered changed-TCB lines; every project floor now passes.
- [x] Move loop state from `.scratch/` into `eng/agent-notes/` so it survives a
  clone and is visible to reviewers.

## Ready

- [ ] **`ManagedContractFacts.cs:158`** — the one remaining genuine gap. The
  reversed-refinement arm is confirmed reachable (see `unverified.md`: the IR
  factory preserves operand order, so `3 == value` keeps the literal on the
  left and only the `Right:` arm can match). Two tests passed for the wrong
  reason. Next step is *not* a third test: print `diagnostic.Location` and the
  message from the attempt-2 fixture to find out what actually produces its
  SP0027, since that diagnostic survived deleting the arm.

## Resolved as not-a-gap

- [x] **`ManagedAbstractFlow.cs:195` and `:1811`** — artifacts of stale coverage
  data, not real gaps. A fresh matched collection at HEAD does not report them.
  See the line-number instability note in `unverified.md`.
- [x] **`ManagedAbstractFlow.cs:137/141/142`** — the convergence catch. Genuinely
  uncovered, but because the committed test used a cyclic (`while`) fixture that
  `IsAcyclic` rejects before the solver runs, and asserted `not Complete` which
  `Cyclic` satisfies. Fixed with an acyclic fixture and an exact
  `BudgetExceeded` assertion; mutation-confirmed.

## Backlog — refill from here when Ready empties

1. **Mutation audit.** For each of the nine commits on this branch, identify its
   production change and confirm a test fails when that change is reverted.
   Record each verdict in `mutation-audit.md`, one commit per iteration. This is
   the highest-value remaining work: four tests written during the original
   session passed for the wrong reason, so the other fixes deserve the same
   scrutiny.
2. Re-run the coverage gate and queue any newly uncovered changed-TCB line.
3. Stop and summarise.

## Needs sign-off — do not start

- **Strong-name the product.** `SharpProof.Attributes` is unsigned, so the
  public-key check in `IsTrustedReferenceType` is vacuous and the SHA256 payload
  pin is the only authenticity control. Signing means 18 production projects, 56
  `InternalsVisibleTo` declarations, and a change to shipped package identity.
- **TCB drift tripwire.** Collapsing the manifest removed it by explicit request;
  deleting `SharpProof.Analyzer/SharpProofAnalyzer.cs` from the `discovery`
  component now passes every test. A path-count assertion in
  `ArchitectureTests.TrustedComputingBaseDeclarationNamesEveryRequiredPath`
  would restore it in about three lines, but its removal was a decision.

## Rules learned the hard way

1. A green dev suite does not mean CI is green — run the acceptance gate too.
2. Confirm every new test **fails when the code under test is removed**. Four
   tests in the original session passed for the wrong reason: two had their
   input constant-folded away (by the IR factory, and by Roslyn), one asserted
   SP0027 for a change that only affects the abstract proving path, and one
   targeted a branch that turned out not to be load-bearing.
3. Never derive a pinned assertion from the value it pins. `Verify.ps1` contains
   53 `Assert-Equal` calls whose whole purpose is to fail when the contract moves.
4. Prove a bug by running it. Findings that came from reading code shape rather
   than executing it mostly did not survive contact with the code.
