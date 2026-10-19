# Resource analysis: full implementation roadmap

The user expanded this branch beyond milestone one on 2026-10-18. This document
tracks the full requested scope. The completed first milestone remains documented
in [its checkpoint](resource-bounds-progress.md); completing that milestone does
not complete this roadmap. The merge request stays draft while required phases
remain incomplete.

Every feature must have a stated cost model, compiler-bound admission rules,
sound upper bounds, explicit Unknown outcomes at unsupported boundaries, tests
against independently observed executions, and end-to-end diagnostic/reporting
coverage. Neither samples nor diagnostic silence establish a universal proof.

## A. Loop and expression bounds

- [x] Canonical increasing unit-step loops, independent nesting, cumulative arrays.
- [ ] Constant positive non-unit strides with explicit overflow checks.
- [ ] Descending loops with checked progress and lower bounds.
- [ ] Multiplicative/divisive progress and logarithmic iteration bounds.
- [ ] Dependent nested loop bounds and conservative counter-size environments.
- [ ] Broader ranking-function synthesis with checked certificates.
- [ ] Regression tests for boundary integers, nontermination, early exit,
  mutation, exceptional exits, and independently counted iteration/allocation costs.

## B. Calls and recursive costs

- [ ] Source-call summaries with argument substitution, dispatch admission,
  receiver initialization rules, and shared analysis budgets.
- [ ] Reviewed metadata cost summaries with assumption provenance.
- [ ] Recursive progress proofs and recurrence bounds for admitted families.
- [ ] Explicit Unknown for unproved progress, opaque targets and unsupported
  recurrences; verify actual caller/callee workflows.

## C. Quantitative memory and stack

- [ ] Object, string, closure, boxing, and array allocation costs.
- [ ] Byte bounds under an explicit, versioned runtime layout model.
- [ ] Alias/escape and lifetime reasoning; distinguish cumulative allocation,
  peak live auxiliary memory, and retained/returned output.
- [ ] Stack frame and recursive stack bounds under a declared model.
- [ ] Independent allocation/liveness controls, mixed-size objects, aliases,
  escaping values, and runtime-layout incompatibility tests.

## D. Contracts and native verification

- [ ] Resource contract API and compiler-bound validation/selection.
- [ ] Compiler-neutral proof/cost representation and collector artifact encoding.
- [ ] Native-worker checking of resource proofs and numeric contract violations.
- [ ] Protocol, manifest, schema and cache-identity integration.
- [ ] SARIF/result projection with independent claim outcomes and Unknown reasons.
- [ ] Reject malformed/unowned proof records; verify packaged consumer behavior.

## E. Asymptotic and aggregate analysis

- [ ] Upper/lower asymptotic expressions and tightness only with both bounds.
- [ ] Amortized costs through checked potential/accounting rules.
- [ ] Average-case bounds conditional on an explicit validated input model;
  never invent a probability distribution from source structure.
- [ ] Adversarial controls that separate loose upper bounds from tight/expected
  results, and report all assumptions in consumer evidence.

## F. Async, concurrency and performance measurements

- [ ] Async/state-machine costs and allocations for an admitted execution model.
- [ ] Concurrent/fork-join total work, allocation and live-space composition;
  document scheduler/overlap assumptions and return Unknown beyond them.
- [ ] End-to-end runtime/allocation benchmark workflow, with input scales,
  units, environmental identity and comparable baselines. Measurements must
  remain distinguishable from proofs and must not establish Big-O by themselves.
- [ ] Cancellation, exceptional completion, shared-state and reentrancy controls.

## Delivery and verification

- [x] Preserve the original dirty checkout using an isolated branch/worktree.
- [x] First milestone: 1184 analyzer tests plus docs/layout/container gates passed.
- [ ] Open and attach the draft merge request for this branch.
- [ ] Keep this document current after each verified implementation phase.
- [ ] Run focused checks before the relevant broader gate; record exact pushed
  commit and CI status after each delivery checkpoint.
- [ ] Final requirement-by-requirement audit of phases A-F and the actual
  consumer workflow, with no unchecked required items, before marking complete.

## Current checkpoint

Branch: `codex/resource-bounds-milestone-one`; initial master: `cd3363c63`.
Milestone-one implementation: `cd7901b72`; planning document: `488f4300e`.
Next action: complete loop generalization and its independent execution tests,
then implement source-call summary composition. Native-worker and general memory
features are not implemented yet and must not be reported as complete.
