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
- [x] Constant positive non-unit strides with explicit overflow checks.
- [x] Descending loops with checked progress and lower bounds.
- [x] Multiplicative/divisive progress and logarithmic iteration bounds.
- [x] Dependent nested loop bounds and conservative counter-size environments.
- [ ] Broader ranking-function synthesis with checked certificates.
- [x] Regression tests for boundary integers, loops that do not terminate, early exit,
  mutation, exceptional exits, and independently counted iteration/allocation costs.

## B. Calls and recursive costs

- [x] Source-call summaries with argument substitution, dispatch admission,
  receiver initialization rules, and shared analysis budgets.
- [x] Reviewed metadata cost summaries with assumption provenance (Int32
  Math.Min/Max, .NET 8.0.32, immutable source commit and catalog digest).
- [x] Recursive progress proofs and recurrence bounds for admitted families
  (positively guarded, decrement/division, static void tail recursion).
- [x] Explicit Unknown for unproved progress, opaque targets and unsupported
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
- [ ] Cancellation, exceptional completion, shared-state and repeated-entry controls.

## Delivery and verification

- [x] Preserve the original dirty checkout using an isolated branch/worktree.
- [x] First milestone: 1184 analyzer tests plus docs/layout/container gates passed.
- [x] Open and attach the draft merge request for this branch.
- [ ] Keep this document current after each verified implementation phase.
- [ ] Run focused checks before the relevant broader gate; record exact pushed
  commit and CI status after each delivery checkpoint.
- [ ] Final requirement-by-requirement audit of phases A-F and the actual
  consumer workflow, with no unchecked required items, before marking complete.

## Current checkpoint

Branch: `codex/resource-bounds-milestone-one`; initial master: `cd3363c63`.
Milestone-one implementation: `cd7901b72`; planning document: `488f4300e`.
Draft MR: [#334](https://github.com/alexyorke/SharpProof/pull/334), initially
published at `6118749b5`. CI was still running at the first inspection.

Loop generalization and source-call size substitution passed all 83 focused
resource tests with zero build warnings/errors. This includes independent small
executions, checked/unchecked overflow controls, dependent allocations, symbol
substitution, argument-evaluation allocation costs, type-initializer abstention,
and a shared call-expansion budget. The initial loop-only run's invalid C# case
was replaced with a valid Int64 overflow control before this passing run.

Decreasing-while ranking rules and guarded tail-recursive allocation bounds passed
101 focused tests, then the analyzer suite passed 1238 tests after consumer
projection and final-expression-budget controls were added. Independent controls
include negative/positive inputs, 138 combinations of loop sizes and strides,
and 19 recursive executions.

The metadata catalog and assumption projection then passed all 108 focused
resource tests. Source evidence is now pinned to runtime commit
`b9a49e4a6d3a444a3685e318c74a7892d01873f7` (.NET 8.0.32). The final full analyzer
run for this checkpoint passed all 1243 tests with zero build warnings/errors,
including 108 resource cases and the immutable source pin.

CI for initial MR head `6118749b5` exposed two issues: spelling in new prose and
the corpus host enabling SP0060 while normalizing every diagnostic to warnings.
The host now preserves opt-in disabled defaults. The corrected full Gates suite
passed all 471 tests, and the canonical lint/quality gate passed. No assertions
were weakened and no existing corpus snapshots were rewritten to hide the extra
diagnostic. Updated docs and evaluated layout also passed with the metadata
files: 2616 source files, 59 evaluated projects, 151 pinned common files.

Checkpoint commands use `eng/scripts/Invoke-SharpProof.ps1` with the canonical
container environment recorded in the milestone-one document:

| Arguments | Verified result |
| --- | --- |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj -TestFilter FullyQualifiedName~ResourceBoundsTests` | 108 passed |
| `test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj` | 1243 passed |
| `test -Target src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj` | 471 passed, including the CI regressions |
| `lint` | All quality checks and negative fixtures passed |
| `docs` | Drift, links and automation lifecycle checks passed |
| `layout` | 2616 source files, 59 evaluated projects, 151 pinned common files |

The full goal is not complete: the unchecked phase A certificate requirement and
all phase C-F requirements still need implementation and verification. The MR
remains draft. These passing checks establish this checkpoint, not completion of
the roadmap.

## Next implementation dependencies

The native proof representation (phase D) must precede quantitative memory proof
composition (phase C); both phases remain required. Introduce compiler-neutral
resource expression and proof nodes with validated input identities, sizes,
arithmetic, budgets and serialization. The current presentation strings must
never become unchecked worker assumptions. Wire that representation into the
existing portable analysis before the collector and independent worker checker.

Memory evidence must distinguish cumulative allocation, logically reachable
live space, retained output and stack frames. Byte bounds require an explicit
versioned layout assumption; source-level reasoning alone must not claim an
exact JIT stack layout or physical GC high-water mark. Native claim ownership,
runtime compatibility, contract validation, protocol/cache identity and SARIF
projection remain open. All asymptotic/aggregate and async/concurrent/measurement
requirements in phases E-F also remain open.

## Native expression foundation (in progress)

Resource arithmetic now has a compiler-neutral tree in `SharpProof.Ir`, using
nonnegative mathematical integers rather than parsed display text. Input identity
consists of parameter ordinal, scalar/array/string size kind and physical domain;
renaming a display label does not change identity. Addition, multiplication,
maximum, positive difference, ceiling division, integer digit/log counts and
alignment have explicit semantics. Construction, evaluation and serialization
have bounded depth, node count, numeric representation and text size. Cancellation
propagates rather than becoming an Unknown proof or malformed-input verdict.

The portable analysis uses these nodes internally, and proven SP0060 results
publish expression model version 1. Its shared implementation now lives in
`SharpProof.Compiler.Semantics`, accessible to both the analyzer and collector
without adding a compiler-artifact dependency to the analyzer core. The resource
cost model remains v3 because its measured units and admitted source behavior
have not changed.

The strict artifact codec rejects unknown or duplicate fields, wrong versions,
invalid operators/arity/domains and payload budgets before constant folding.
Its owned-callable entry point additionally checks the callable ID, concrete
body, parameter map, input ordinal/type and exact physical domain. Display names
provide no ownership evidence. The preparation must already belong to the
validated manifest envelope; matching a caller-provided label cannot establish
external source ownership.

This is an arithmetic and input-binding foundation, not a native resource proof.
It does not yet emit resource claims from the collector or verify their cost
composition in the worker. The phase D proof representation checkbox remains
open until those parts and adversarial consumer tests are implemented. Memory,
asymptotic, amortized, distribution, async, concurrency and measurement phases
remain required.

The first expression checkpoint passed 15 focused IR tests and 126 focused
analyzer/resource-codec tests, including the source-analysis-to-codec bridge.
The same 126 tests passed again after the shared semantic layer move. With
ownership and numeric-budget controls added, all 132 focused resource/codec/
ownership tests, all 497 IR tests, and all 10 architecture tests passed with zero
build warnings/errors. The full analyzer suite passed all 1267 tests with zero
build warnings/errors. Evaluated layout passed with 2622 source files, 59 projects
and 151 pinned common files; documentation drift/lifecycle checks also passed.
All quality checks except markdown passed on the first run. After correcting one
heading's blank-line spacing, the pinned markdown engine checked both changed
documents with zero findings. Package compatibility and negative gate fixtures
passed. The published predecessor `bf966074c` still had four CI checks pending
at the latest inspection (25 successful, four skipped, no failures).
