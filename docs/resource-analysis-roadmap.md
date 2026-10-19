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
- [x] Broader ranking-function synthesis with checked certificates, including
  protected two-counter phases.
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

- [x] Resource contract API and compiler-bound validation/selection.
- [x] Compiler-neutral proof/cost representation and collector artifact encoding.
- [x] Native-worker derivation and checking of cumulative resource upper bounds.
- [x] Exact-prefix numeric violations for array allocations and allocated elements.
- [x] Exact source-loop-entry numeric violation evidence.
- [x] Exact typed-array byte-prefix refutations with owned layout evidence.
- [x] Bind native metadata cost assumptions and their provenance.
- [x] Protocol, manifest, schema and cache-identity integration.
- [x] SARIF/result projection with independent claim outcomes and Unknown reasons.
- [x] Reject malformed/unowned proof records; verify packaged consumer behavior.

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

## Closed resource programs and native derivation (in progress)

The compiler now emits a resource program alongside its presentation bounds.
Nodes describe zero cost, array allocation, sequence, branch alternatives and
repeated regions. Repeats carry initialization, strict guard, positive update,
counter width and overflow mode rather than a producer-asserted trip count.
Source-call expansion and input substitution apply to this program too. Guarded
tail recurrence uses a separate repeat kind so it does not invent loop entries.

This is an upper-envelope lowering: early exits can shorten a source execution,
and dependent sizes can be replaced by conservative enclosing-counter envelopes.
The compiler lowering remains in the trusted boundary for preserving allocations,
closed operation admission and counter stability. A large modeled upper cost
alone cannot refute a source contract; refutation still requires owned replay
of an actual violating execution.

An independent worker module checks rank domains, monotone progress and overflow,
then derives all three cumulative metrics from the nodes. Its strict program
codec binds program/expression/cost-model versions and rejects malformed fields,
arity, payloads and raw tree budgets before simplification. Native input binding
uses the concrete owned callable's parameter types and domains. Conditional
metadata leaves an explicit boundary with native Unknown until its provenance is
bound, preserving the analyzer's existing conditional results.

The first 38 focused native tests passed, including independent small executions,
checked-prefix versus unchecked-wrap controls, source-to-worker agreement and
malformed transport controls. The expanded run passed all 41 cases, adding
source-call, metadata-boundary and construction-budget controls. The full
analyzer regression suite passed all 1267 tests with zero build warnings/errors.
Evaluated layout passed with 2626 source files, 59 projects and 151 pinned common
files; documentation drift and lifecycle checks passed. All 497 IR tests and
10 architecture tests passed. Both source-level metadata-boundary assertions
passed after they were added. Nine quality categories passed initially; one
prose spelling finding was corrected, then the pinned spelling checker passed
all 71 configured files and markdown checked the changed roadmap with no findings.
The published predecessor `9f7a9769e` still had CI running at the latest
inspection, with no failures. The native resource-program consumer workflow
remains pending as described below.

Collector attachment, resource contracts, protocol/cache/SARIF integration,
native metadata assumptions and replayed numeric violations remain open. This
module verifies a supplied closed program; it is not yet a published native
resource-claim workflow. Broader ranking synthesis and all required phases C-F
remain incomplete.

## Native resource contract integration (in progress)

The public API now declares constant or linear bounds for loop-body entries,
primitive-array allocations and allocated elements. Trusted compiler identity,
parameter binding, independent attribute outcomes and invalid-metadata abstention
use the existing Effects selection path. Resource programs are attached only to
selected resource contracts. The consumer binds their expressions to concrete
callable parameter types and rejects unowned records, mismatched constraints and
compiler-supplied proof or trusted-cost verdicts.

The native checker derives cumulative costs, then applies sufficient nonnegative
arithmetic rules to prove the declared inequality. Failure of that comparison
is Unknown, not a numeric refutation. Compiler-elided clauses and their arguments
are omitted through the repository's existing conditional-call policy. Enabled
conditional calls retain their costs.

Protocol 14, manifest 6, cache 16 and compiler artifact 32 bind the new declaration,
program and result shapes. Resource evidence reaches validated worker responses,
cache hits and SARIF, with metric/model identities and declared-bound digests.
Quantitative memory/stack and later phases remain open. Replayed numeric
violations, native metadata assumptions, broader ranking synthesis, packaged
consumer coverage and final protocol/cache qualification are still required.

The first integrated run verified the full worker/cache/SARIF path and most
focused cases. Tests were corrected to exercise the untrusted consumer decoder
and production parse options; the versioned manifest hash fixture was updated.
The full IR suite now passes 502 tests, including independent controls over 256
linear coefficient/constant combinations. The final focused native/protocol run
passed all 181 tests. The full analyzer suite passed 1271 tests; architecture
passed 10 tests; documentation, evaluated layout and all quality/compatibility
categories passed. Final layout evaluated 2637 source files and 59 projects.

A strict consumer built from the actual NuGet packages passed: all three metrics
produced Proven results and validated JSON/SARIF evidence. The full worker suite
passed 5675 tests and failed only three old schema-number fixtures. Their schema
values were corrected without changing verdict assertions; all 82 targeted
schema/golden tests then passed. Additional arithmetic-domain and malformed-record
boundary controls passed with the full IR suite, now 508 tests. All recorded
builds reported zero warnings/errors. A final real-worker/cache/SARIF control
also passed after null-constraint validation was hardened.

CI for published predecessor `80a15d016` failed the required 100% coverage gate:
91 resource-code lines were uncovered. The exact report has been inspected;
targeted boundary coverage work is in progress. The coverage baseline is
unchanged. Coverage qualification, native numeric refutations and every remaining
unchecked roadmap requirement still block completion; the MR remains draft.

## Boundary coverage and CI hardening (in progress)

The next checkpoint adds explicit malformed arithmetic/program transport controls,
aggregate encoding budgets, detached-record validation, rank-domain/overflow
controls and native claim ownership/Unknown controls. Supported primitive
expression forms and unsupported loop updates also have direct regressions.
Encoding reuses the immutable trees' expanded-node/depth invariants while
retaining aggregate payload limits. Decoding still enforces raw budgets before
folding, and its admitted arithmetic operators are explicitly closed.

The diagnostic resource coverage invocation initially ran static collectors in
parallel and produced compiler-reference identity mismatches. Repeating the same
filter with `SHARPPROOF_TEST_PROJECT_PARALLELISM=1` eliminated those failures:
90 worker resource tests, 151 analyzer resource tests and the actual packaged
resource consumer passed. This filtered run is diagnostic evidence only; it does
not qualify the repository-wide coverage gate. The separate Attributes phase
then exposed missing XML property-value documentation, now corrected; all 21
Attributes tests passed. Coverage floors remain unchanged.

Published head `e3abf12a4` also failed the packaging job's formatting prerequisite
at `ProtocolModel.generated.cs:805`. The exact CI job log was inspected and the
canonical formatter with `--include-generated` applied the missing line break.
Final suite and gate results are recorded below. Coverage qualification is
still in progress. No remaining roadmap phase is marked complete by these
boundary fixes, and numeric violations still require exact owned replay.

The full analyzer suite passed 1301 tests and the full IR suite passed 512 tests.
After adding the exact payload-boundary control, all 170 focused analyzer
resource tests passed. All 75 focused native resource tests and all 32 Attributes
tests passed, including direct constant/linear construction and rejection paths.
Encoding accepts an exactly one-MiB expression envelope and rejects one extra
byte; the writer reserves the envelope's closing byte explicitly. Builds reported
zero warnings/errors. All ten lint/quality/compatibility categories passed, final
changed-source formatting (including generated source) passed, and layout passed
with 2638 source files, 59 projects and 151 pinned common files.

The next inspection of CI for `e3abf12a4` found two exact frontend catalog parity
failures: the attribute roster omitted ResourceUpperBound, and the shared effect
category test assumed every attribute also targeted constructors/properties.
The expectations now include the new attribute and check its declared method-only
target separately while retaining the existing targets for all other effects.
The serialized diagnostic coverage run completed its tests and report export;
its repository-wide floor check failed as expected for a resource-only filter.
That report predates the final native/Attributes controls and is not final
coverage qualification. Documentation drift and automation lifecycle checks passed.

After the catalog expectation fixes, all 2052 frontend tests passed with zero
build warnings/errors.

## Exact numeric array violations (in progress)

The native contract path now attempts exact replay after its universal upper-bound
comparison remains unproved. The first admitted refutations are cumulative
primitive-array allocation counts and allocated elements. Internal IR events fire
only after successful allocation and include the evaluated length and sticky
approximation state. Replay rejects a candidate unless its typed entry values,
receiver constraint, declared disjointness, Requires values and definedness guards
hold. Search uses bounded concrete candidates and native feasibility/allocation
models; neither search exhaustion nor an envelope comparison becomes a proof.

Refuted evidence carries canonical observed-prefix and evaluated-entry-limit
values, a concrete source witness and the replayed model. Protocol 15 and cache
17 bind the new response shape; manifest 6 and compiler artifact 32 are unchanged.
Validation rejects noncanonical values, values that do not exceed the bound,
missing numeric witnesses and incompatible outcome fields. Cache hits and SARIF preserve the
numeric comparison and source location.

The loose-envelope control exposed a missing Requires guard in the initial new
replay path. Direct concrete evaluation of all owned Requires/definedness terms
was added before any observation could be accepted. With that correction, all
84 focused native/program/worker-cache-SARIF controls passed with zero build
warnings/errors. Broader gate results are recorded below. Phase D remains open:
loop-body-entry violations still need exact source-entry instrumentation, native
metadata assumptions remain unbound, and all other unchecked roadmap work is
still required. CI for checkpoint `ac99f280e` was cancelled across its workflows;
those cancelled runs do not qualify the published checkpoint.

Final checkpoint gates passed: 5702 full worker tests, 1302 analyzer tests,
517 IR tests and 10 architecture tests. Independent allocation-event controls
passed within the 70 focused total-execution cases. Both actual NuGet strict
consumers passed: valid bounds build with three Proven metrics, while violated
array count/element bounds fail the build with two Refuted claims and concrete
JSON/SARIF metric evidence. All builds reported zero warnings/errors.

Layout passed with 2639 source files, 59 projects and 151 pinned common files.
Documentation drift/lifecycle and final changed-source formatting passed. The
quality run passed every category except one spelling finding in this document;
after rewording it, the pinned spelling engine checked all 71 configured files
with zero findings. The 100% coverage baseline is unchanged and final coverage
qualification remains required. No full-roadmap completion is claimed.

## Exact source-loop-entry violations (in progress)

Resource-selected total lowering now identifies admitted C# for/while guards by
their owned source syntax and typed strict comparison. A compiler-owned semantic
marker records the true edge's body-entry meaning and original source span. The
shared source-call session preserves these annotations through inlining. Ordinary
lowering keeps its prior operation sites. The worker observes marked true edges
only after condition evaluation and retains the sticky approximation state.
Neither an ordinary conditional, a jump to an earlier block nor a loose upper envelope invents
a source body entry. Guard annotations are trusted lowering semantics, not an
independent progress certificate; the native upper-bound checker still validates
rank/overflow evidence separately.

Numeric loop witnesses use no allocation/effect flags. Resource-specific protocol
validation checks the metric/flag relationship without allowing empty qualitative
effect witnesses. Protocol 16, cache 18 and compiler artifact 33 bind these
semantics; manifest 6 and the v3 cost units remain unchanged. Schema-only golden
expectations were updated without changing verdicts.

The first 46 focused native contract tests passed, followed by 301 native,
protocol, worker/cache/SARIF and golden controls. Independent total-execution
controls passed all 74 cases. Controls include parenthesized/Int64 guards,
non-unit/geometric/descending progress, dependent nesting, inlined callees,
checked-overflow prefixes, early return, approximation and loose envelopes.
Broader gate results are recorded below.

CI aggregate failures for published `0e69f12d0` were traced to cancelled upstream
jobs: the build aggregator received cancelled quality/main/package/Gates/
self-application jobs, and the coverage aggregator received cancelled shards.
These runs contain no completed cohort/coverage qualification and do not establish
a code regression. Final coverage qualification is still required.

Final loop-entry checkpoint gates passed: 2052 frontend tests, 521 IR tests,
1302 analyzer tests, 10 architecture tests, the 301 focused native/protocol/
consumer/golden cases, and all three ordinary-conditional/loose-loop controls.
All three actual NuGet strict consumers passed, covering valid bounds, array
violations and loop-entry violations with numeric JSON/SARIF evidence. Builds
reported zero warnings/errors. Final changed-source formatting and documentation
drift/lifecycle passed; layout evaluated 2640 source files, 59 projects and 151
pinned common files. The quality categories passed after two technical words in
this document/guide were reworded and the pinned spelling engine rechecked all
71 configured files with zero findings.

The native source-loop-entry evidence item is complete for the admitted families.
Native metadata cost assumptions/provenance remain the next phase D item. Every
other unchecked requirement remains required, including final coverage at the
unchanged 100% baseline. The merge request remains draft.

Before publication, the remote branch advanced to `91fe3b184`, the user's merge
of master containing dictionary-state modeling (#331) and CI image caching
(#339). The local loop-entry commit rebased cleanly onto that merge. The shared
source-call session retained both the upstream dictionary changes and the new
resource-tracking flag. Integrated-state rechecks passed 2062 frontend tests,
302 native/protocol/consumer/golden cases and all three actual NuGet consumers,
with zero build warnings/errors. Earlier layout counts above describe the
pre-merge checkpoint; the remote merge is preserved in the published history.

Integrated layout also passed: 2648 source files, 59 projects and 151 pinned
common files.

The completed coverage run for published `62aef3a092` changes the earlier CI
status: all four coverage shards passed, but the aggregate gate failed. Changed
trusted-code coverage was 1597/1660 lines (96.2%) against the unchanged 100%
requirement, leaving 63 uncovered lines. This is an actual coverage gap, unlike
the earlier cancelled aggregate runs. Coverage qualification remains open.

The next implementation phase binds native metadata cost assumptions. Its closed
program representation now carries catalog/method/reference identities; the
compiler manifest declares callable-owned dependencies, and native results
project the reviewed runtime/source and captured reference hash. The compiler
artifact must preserve target-framework and reference-snapshot evidence across
serialization. General compiler references remain excluded; only the subset
used by resource metadata calls crosses the boundary. Protocol admission distinguishes the catalog's established
uppercase digest encoding from lowercase reference-image hashes. Focused native/protocol/cache/SARIF/artifact checks passed 285 cases, the
analyzer/collector checks passed 241 cases, and IR resource checks passed 40
cases. The actual NuGet consumer passed three conditional Proven results,
strict SP0048 rejection and explicitly enabled advisory verification, with
provenance in JSON/SARIF. Exact execution replay still abstains after an
unsupported metadata call; a cost summary does not supply concrete values.

Metadata-phase quality categories, 10 architecture tests, documentation drift
and 722 local links passed. Layout evaluated 2651 source files, 59 projects and
151 pinned common files. Final spelling checked all 71 configured files with
zero findings. A broad worker run caught the general-reference transport
regression; the producer now emits only the resource-used reference subset,
leaving general references excluded. The corrected full worker gate passed all 5809 tests with zero warnings/errors.
All other unchecked roadmap items and final 100% coverage remain required.

Published metadata checkpoint: `2e9ecfb61fc005a4808729e19e86e50fc51567ad`.
Its exact-head documentation maintenance and toolchain image security workflows
passed; container coverage, canonical PR gates, and package/consumer workflows
were still running at inspection. The draft MR description now records the
metadata behavior, strict/advisory policy, transport correction and coverage gap.

Coverage follow-up added four adversarial arithmetic controls: left-hand
constant multiplication is checked against independently evaluated costs, and
three reversed maximum trees must stop at the shared comparison budget. All 12
arithmetic-order tests passed with zero build warnings/errors. This exercises
two previously uncovered rule families; complete coverage must still be measured
and qualified, and no coverage percentage is inferred from these test results.

The next phase extends checked ranking synthesis for top-tested while loops.
Increasing additive/multiplicative ranks require an unchanged, exact nonnegative
constant initializer; a clamped size envelope cannot establish the actual
initial counter value. Decreasing additive ranks can use nonnegative constant
lower bounds. The compiler emits the existing typed rank certificates for native
checking. Mutation, unsupported starts, missing progress and unchecked overflow
remain Unknown. Source, independent execution, native proof and exact-prefix
replay controls passed: 190 focused source-resource tests and 129 native resource
tests, followed by the full 1322-test analyzer gate. Builds reported zero warnings
or errors. The broader ranking checkbox remains open for multi-counter synthesis.

Exact-head coverage for metadata checkpoint `2e9ecfb61` completed all four shards
successfully but failed qualification at 1766/1833 changed trusted-code lines
(96.34%), leaving 67 uncovered lines. The 100% baseline is unchanged. Its package,
consumer and release workflow passed. The local arithmetic follow-up and while
extension were not part of that measurement. The next ranking work is checked
composition of multiple counter phases; unsupported coupled updates will remain
Unknown. Remaining metadata/protocol coverage gaps are also being mapped to
boundary controls rather than excluded from qualification.

Published while-ranking checkpoint: `2848a1d8c237f9f62325bcb0b299746d04a14ce5`.
Its exact-head documentation maintenance and toolchain image security checks
passed; package/consumer, coverage and canonical PR gates were queued at inspection.

The next coverage follow-up passed two source and five native/protocol boundary
tests, with zero warnings/errors. Controls cover instance metadata receivers,
mutating metadata arguments, invalid dependency projection, null or oversized
metadata arrays, null witness identity, and real Unknown responses corrupted with
partial numeric or undefined-outcome evidence. Every mutation is rejected and the
restored response validates. No coverage percentage is inferred until the next
complete measurement; the 100% baseline and remaining roadmap requirements stay
unchanged.

## Composed counter phases

The implementation admits a top-tested loop with two distinct Int32/Int64 locals, initialized from
supported sizes, and this fixed phase selection:

```csharp
public static class PhasedExample
{
    public static void Run(int leftSize, int rightSize)
    {
        int left = leftSize, right = rightSize;
        while (left > 0 || right > 0)
        {
            if (left > 0) { /* phase prefix */ left -= 2; }
            else { /* phase prefix */ right -= 3; }
        }
    }
}
```

Each continuing phase must reach its own checked progress rule. The decrement
family uses ceiling division of the initial nonnegative size; division by a
constant greater than one uses the existing digit bound. Both counters remain
protected from all other writes, aliases and jumps. Earlier mutation, missing
steps, mismatched phase selection and coupled updates produce Unknown.

Compose the two existing typed rank certificates as a sequence of resource
repeats. The native checker derives each phase count and its prefix costs, then
adds the results. This preserves separate phase allocation/nested-loop costs
without treating a mathematical sum as a physical counter domain.

Roslyn can split the logical-or guard into separate branches. Extend source-entry
markers only where a true edge enters this source while body; ordinary body
conditionals must not add entries. Check left-only, right-only, both-positive and
both-inactive executions independently before using numeric violation evidence.

Verification must include independent small executions, constant and same-input
linear contracts, all three resource metrics, exact violating prefixes, and
explicit Unknown controls. Keep the broader ranking checkbox open until the
source, native and relevant broader gates pass for this admitted family.

The composed-phase implementation now emits a sequence of existing typed rank
certificates, with separate prefix costs and protected counter environments.
Logical-or source guard leaves preserve exactly one body-entry event; the phase
selection does not create an event. All 149 focused native resource tests passed,
including repeated inlining, early-return abstention, mixed progress, and costs
whose natural sums exceed Int64. The source run passed 218 cases before exposing
one invalid C# test operand; it was replaced with a valid wrapped negative stride.
The broader analyzer run then passed 1354 cases, with its sole failure in the
roadmap's fragmentary C# fence. That example is now standalone. The full frontend
gate passed all 2062 tests. The native restore crash was terminal and its retry
passed; it did not produce a code failure. The corrected full analyzer gate passed all 1355 tests, and both actual NuGet
consumer cases passed. Strict builds prove all three valid metrics and reject
all three smaller bounds with concrete numeric JSON/SARIF evidence.

The broader ranking item is complete for the documented increasing/decreasing,
geometric, nested and protected two-counter families. Independent controls ran
200 small two-counter executions, with separate array counts and element costs.
The native, analyzer, frontend, packaged-consumer and quality gates passed with
zero build warnings/errors. Progress for unsupported coupled updates stays
Unknown. The full roadmap remains open for quantitative memory/stack, asymptotic,
amortized and average-case models, async/concurrency, measurements and final
coverage qualification.

For published boundary checkpoint `d9760676e`, documentation, toolchain security,
package/consumer/release and canonical PR workflows all passed. All four coverage
shards passed, but qualification failed at 2198/2259 changed trusted-code lines
(97.30%), leaving 61 uncovered lines against the unchanged 100% baseline. Its
changed-line report lists no remaining gaps in the resource metadata catalog,
metadata binder, protocol validation or arithmetic-order kernel. The composed
phase implementation is not part of that measurement and needs its own exact-head
CI results after publication.

## Quantitative memory implementation plan

First introduce compiler-neutral allocation shapes for SZ arrays, ordinary
non-overlapping fields, UTF-16 strings, primitive boxing, delegates, and a fresh
closure frame plus delegate. A versioned CoreCLR 8.0.32 x64 layout model will
derive byte intervals from typed storage slots and natural size expressions.
Object field packing can make an interval loose; an upper byte bound must never
be used as an exact violating cost. Unsupported shapes or incompatible model
contexts produce Unknown with no partial interval.

The layout model is conditional on its declared runtime and architecture. Its
constants and digest bind the reviewed runtime source at commit
`b9a49e4a6d3a444a3685e318c74a7892d01873f7`. Validate it with independent allocation
controls in the canonical .NET 8.0.32 x64 runtime, including mixed fields and
empty/cached results. Instance storage excludes referenced objects unless they
are explicitly part of the allocation bundle.

Then transport typed shapes in the resource program, bind layout assumptions to
compiler-owned target/platform/reference evidence, and expose cumulative byte
contracts through native checking and consumer results. Continue with source
lowering for every category above, alias/escape and lifetime certificates,
separate peak auxiliary and retained output bounds, and declared stack models.
None of the phase C checkboxes is complete until the corresponding compiler,
worker, protocol, consumer and independent controls pass. These steps preserve
the full memory scope; the layout kernel is its initial implementation layer.

Primary layout evidence:
[object and array layout](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/object.h),
[string sizing](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/object.cpp), and
[delegate implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Delegate.CoreCLR.cs).

The first memory layer is implemented: typed allocation shapes cover arrays,
ordinary non-overlapping object fields, strings, primitive boxes, delegates and
fresh closure bundles. Model `coreclr-8.0.32-x64-source-storage-v1` computes byte
intervals, rejects incompatible contexts, propagates cancellation and returns
Unknown on unsupported shapes or arithmetic budgets. Its digest is derived from
the actual model constants, storage widths, supported context and pinned source.
Closed shape transport rejects duplicate/missing/unrelated fields, undefined
storage, noncanonical arithmetic encoding, supplied byte verdicts and runtime
assertions. Shape transport alone establishes neither input ownership nor a
runtime/layout assumption; those remain worker integration requirements.

All 25 initial layout tests and 20 transport tests passed, including 13 independent
allocation controls in the canonical CoreCLR 8.0.32 x64 process. A final digest
regression increased the passing focused layout suite to 26 tests. Loose object
packing remains an interval, and an empty-string factory's cached result is not
mistaken for a fresh allocation. The full IR suite passed 554 tests before that
additional identity test; the full analyzer suite passed 1375. All quality and
compatibility categories passed with zero build warnings/errors. Layout passed
for 2656 source files, 59 projects and 151 pinned common files.

Cumulative byte contracts, compiler shape extraction, native binding/projection,
liveness/escape, retained output and stack models are still unimplemented. Phase
C stays unchecked. The next step is typed shape transport within the owned
resource representation, followed by bound target/platform/reference assumptions
and native byte contracts; continue through every allocation category before
completing the cumulative allocation item.

For published composed-rank checkpoint `45f6419c7`, all non-coverage workflows and
all four coverage shards passed. Qualification failed at 2510/2572 changed trusted
lines (97.59%), leaving 62 gaps against the unchanged 100% baseline. The memory
layer is not included in that measurement and requires its own exact-head CI.

## Owned typed-array program integration

Resource program model 3 now embeds typed allocation shapes. Primitive source
array creation preserves 1-, 2-, 4- and 8-byte element storage, including arrays
inside source calls. The strict program codec rejects unrelated shapes, mismatched
extents and noncanonical encodings. Native program validation checks shape/input
ownership before deriving any established count metric.

An internal native allocation checker independently derives conditional cumulative
byte upper bounds from those facts and the reviewed layout model. Sequences add,
choices take upper maxima, and repeats multiply by native-checked rank envelopes.
It deliberately supplies no cumulative lower bound: a branch maximum or loop
upper envelope is insufficient evidence for one. Untyped arrays, opaque metadata,
foreign inputs, invalid ranks and incompatible layouts return Unknown with no
partial bound. Cancellation propagates. The result is explicitly conditional,
not a published byte-contract verdict or proof of the deployed runtime.

This embedding changes protocol 17 to 18, cache schema 19 to 20, compiler artifact
schema 34 to 35 and resource program model 2 to 3. Manifest schema 6, expression
model 1 and the existing public three-metric cost model remain unchanged.

Initial focused validation passed 194 native resource/protocol tests and 325
source resource/compiler-capture tests. The latter includes all twelve admitted
primitive element types. Expanded compiler-to-native controls additionally cover
mixed widths, source-call substitution, repetition and branch envelopes. Final
broader and quality results are recorded below when complete.

Phase C remains unchecked. Next bind the declared runtime/layout assumption to
compiler target platform, reviewed references and owned manifest evidence; then
expose native byte contracts and projection. Continue with object/string/boxing/
delegate/closure extraction and effect accounting, liveness/escape, retained
output and stack analysis, then every remaining phase E/F requirement. No item
is considered complete merely because this infrastructure exists.

Broader validation passed all 555 IR tests and all 1387 analyzer tests. The first
full analyzer attempt terminated in NuGet SDK resolver migration with a mutex
exception; a clean retry passed with zero build warnings/errors. The worker run
exposed two golden expectations for compiler artifact schema 34; both were updated
to 35, and all 81 golden controls then passed. The final combined native resource,
protocol, compiler-artifact and golden run passed 380 tests, including direct
native-entry rejection of conflicting detached storage facts. The full worker
run is tracked separately until its terminal result is available.
All quality/compatibility categories passed, as did final C# formatting,
documentation drift and 723 local links. Layout evaluated 2658 source files,
59 projects and 151 pinned common files.

Exact published foundation `9628aff14` passed every non-coverage CI workflow and
all four coverage shards. Aggregate qualification failed at 2807/2876 changed
trusted lines (97.60%), with 69 gaps against the unchanged 100% baseline. That
measurement excludes the newer typed-program integration and cannot qualify it.
The exported local report records the complete gap list for later qualification.

## Native byte-contract integration plan

Add AllocatedBytes as public metric 4. Its initial cost model explicitly names
CoreCLR 8.0.32 x64 source-owned fresh-instance storage; it does not represent
process RSS or arbitrary runtime allocations. Existing three-metric contracts
retain their cost model. A byte contract declares a callable-owned trusted layout
dependency using the existing resource assumption identity/evidence machinery.
The strict profile retains its existing rejection policy for such dependencies.

Capture the compiler target platform, preserve the relevant reviewed core-library
reference evidence only for byte-selected compilations, and require net8.0 plus
an explicit x64 target. Bind the owned declaration, actual captured reference
identity/hash, layout digest and immutable source evidence before deriving bytes.
An incompatible/missing target, reference or assumption yields Unknown. A context
string alone cannot establish target compatibility or a deployed runtime.

The native upper-bound verifier will consume the already checked typed array
program, compare its byte envelope with the declared bound, and project the layout
dependency into validated results/SARIF. Numerical refutations require a later
exact byte-event replay; an insufficient envelope alone remains Unknown. Other
allocation families and all remaining phase C/E/F items stay required.

Coordinate protocol/cache/artifact invalidation for the new metric and captured
target evidence. Also correct the evidence program-model constant, which still
reported 2 after the typed-program codec moved to 3; add a cross-layer identity
control to prevent recurrence. Validate missing/conflicting declarations,
platform/framework/reference mismatches, ownership, cached/result tampering,
independent outcomes and actual consumers.

The completed broad worker run for typed-program checkpoint 180d4a95a recorded
5862 passing tests and the two previously identified schema-only golden failures.
All 81 corrected golden controls passed separately, as did the final 380-test
native/protocol/golden run. This is distinct from an entirely green full-suite
rerun; exact-head CI remains the authoritative delivery qualification.

## Layout-bound native byte contracts

Implemented public AllocatedBytes metric 4 with its distinct versioned cost model.
Compiler capture carries target platform and only the core-library reference
needed by selected byte claims. Discovery declares the callable-owned layout
assumption before sealing the manifest. Native binding requires net8.0, an x64
compiler target, a reviewed reference identity with a single consistent captured
module hash, and the exact owned assumption. Missing/conflicting inputs yield
Unknown. The reference identifies compiler inputs; the runtime model remains an
explicit trusted execution assumption, never detected from a context label.

Native checking now compares conditional byte envelopes to constant/linear
contracts and projects the layout digest, runtime/source model and reference
identity/hash into results. Protocol validation requires the layout dependency
for nonvacuous byte proofs, including cached results, and rejects stripped or
changed evidence. Byte and element claims retain independent outcomes. Protocol
19, cache 21 and compiler artifact 36 invalidate earlier evidence; resource
program/evidence model 3 and expression model 1 have cross-layer identity tests.

Initial combined native/protocol/golden/consumer validation passed 397 tests.
An invalid C# fixture mixing incompatible conditional array types was replaced
with separate branches before this passing run. Added symbolic alignment and
repetition cases are undergoing their final run. An actual NuGet consumer also
passed strict/advisory/x64/AnyCPU controls: strict reports SP0048 for the trusted
layout dependency; explicitly enabled advisory verification proves the x64 bound
and publishes JSON/SARIF provenance, while AnyCPU returns Unknown. Earlier
fixture formatting failures were corrected with the pinned formatter.

This completes the first array byte-contract integration, not phase C. Exact
numerical byte-event refutation still requires implementation; a failed envelope
comparison remains Unknown with no invented prefix. Continue with that replay,
then source extraction/effect accounting for every remaining allocation family,
liveness/escape, peak auxiliary and retained output, and stack models. All phase
E/F requirements and unchanged 100% coverage qualification remain open.

Final byte-contract validation passed 401 focused native/protocol/golden/consumer
tests, all 33 public attribute API tests, all 1388 analyzer tests and the actual
NuGet consumer described above. Symbolic controls prove aligned primitive-array
sizes and repeated per-iteration storage under the owned layout assumption.
All quality/compatibility categories passed with zero build warnings/errors.
Generated public API documentation was refreshed; drift and 723 local links
passed. Layout evaluated 2661 source files, 59 projects and 151 pinned common files.

Published typed-program checkpoint 180d4a95a passed every non-coverage workflow
and all four coverage shards. Qualification failed at 2987/3062 changed trusted
lines (97.55%), leaving 75 gaps against the unchanged 100% baseline. This report
excludes the newer byte-contract implementation; its exact-head CI and eventual
full qualification remain required.

## Exact typed-array byte prefixes

Byte refutation now observes successful typed IR array-allocation events. It
computes header-plus-payload alignment under the owned layout model, accumulates
actual execution-prefix costs and requires the prefix to exceed the evaluated
entry bound. Boolean and typed 8/16/32/64-bit integer storage, including char,
are admitted; unknown storage never acquires a guessed width. Unsupported storage
stops later byte evidence from crossing the missing event. The existing entry,
Requires, approximation, source-location and replay-budget guards remain required.

Both Proven and Refuted byte results project the owned layout dependency. Protocol
validation rejects decided byte evidence without that dependency, including
cached results. Protocol 20/cache 22 invalidate the earlier semantics; compiler
artifact 36 and program/evidence model 3 are unchanged because the event typing
and evidence fields already existed. This concerns the declared source storage
model, not physical JIT/GC/process allocation measurements.

Initial targeted validation passed 246 native/protocol cases, then 411 combined
native/resource/protocol/golden/cache/SARIF cases. Added controls cover zero-length
arrays, mixed widths, sequences, repeated allocations, executed branches, Requires,
and loop envelopes whose allocations never execute. Further prefix-boundary and
actual NuGet proof/refutation controls are undergoing final validation.

This advances the array byte path. Continue through object/string/boxing/delegate/
closure extraction and effect accounting, alias/escape/liveness, peak auxiliary
versus retained output, stack models and every remaining phase E/F requirement.
The full roadmap and unchanged 100% qualification remain open.

Final focused replay validation passed 417 native/resource/protocol/golden/cache/
SARIF tests, including infeasible Requires branches, prior exceptions, opaque
metadata and floating/unknown IR storage. Both actual NuGet proof/refutation
consumers passed. An initial test incorrectly expected advisory verification to
succeed for an explicit refuted contract; it now preserves and checks the existing
SP0051 build failure while verifying the concrete byte prefix and layout evidence.

Exact-head CI for c9170a1a0 exposed a separate semantic-cache regression:
CompilerOptionChangesReuseEquivalentSemanticArtifacts failed in canonical-package
and the package coverage shard because target platform was serialized even when
no byte contract used it. Capture now records platform only for byte-selected
compilations. The original cache-reuse integration test and both byte consumers
then passed together (3 tests), and all 417 focused native tests passed after the
fix. Ordinary resource contracts assert that both platform and layout references
remain absent. This preserves the prior cache semantics without weakening the
layout binding for byte claims. A new exact-head run remains required.

After the platform-capture fix, the final full analyzer suite passed all 1388
tests with zero build warnings/errors. All quality/compatibility categories
passed; final C# formatting, documentation drift and 723 local links passed.
Layout remains 2661 source files, 59 projects and 151 pinned common files.
The canonical/package failure on c9170a1a0 and its failed package coverage shard
are not a qualification pass; the fixed pushed commit must receive fresh CI.
The last complete aggregate coverage report remains the earlier 180d4a95a report,
with the unchanged 100% baseline still required for final delivery.

## Managed object and constructor integration

The next memory layer adds a closed non-array managed-allocation leaf to resource
program model 4. It transports typed storage facts, validates extent ownership,
charges fields against the shared program budget and derives byte intervals in
the native layout checker. The existing three count metrics charge zero for the
managed instance itself and continue to charge all constructor/initializer arrays
and loop entries. Protocol 21/cache 23/compiler artifact 37 invalidate model 3
programs; expression model 1 and the reviewed layout formulas remain unchanged.

Source admission covers intrinsic System.Object creation and ordinary source
classes directly derived from System.Object, with primitive/reference fields.
The fundamental object constructor reuses the existing total-lowering intrinsic
rule. User constructor arguments, instance field/property initializers, explicit
constructor bodies and field object-initializers are visited separately. Static
initialization, recursive construction, inheritance, records/generics, COM
activation, cleanup callbacks and events, custom layouts, unsupported value storage and
unmodeled constructor chains remain explicit Unknown boundaries. User constructor
effects are never inferred from instance size alone.

Native replay observes concrete managed-reference allocations as well as arrays.
An ordinary fresh instance has a model lower cost of at least 24 bytes; loose
field-packing intervals cannot become exact costs. Mixed constructor prefixes
are undergoing replay validation before this boundary is considered verified.
The complete source/native tests and final checks are recorded after they pass.
Other allocation families, liveness/escape, retained/peak memory, stack, phase E/F
and final unchanged 100% qualification remain required.

Managed integration passed 438 focused native/resource/protocol/cache/golden
checks, all 555 IR tests and 195 source resource controls. A mixed field-initializer
refutation initially stayed Unknown; a bounded replay trace established that the
existing functional backend treats instance-initializer constructors as opaque,
consuming approximation before allocation. The verifier correctly preserves
Unknown there. Explicit plain constructor bodies replay exact array events and
compose them with managed lower costs. Exact initializer execution is therefore
an explicit remaining backend task, not a waived requirement. Diagnostic scaffolding
was removed after establishing the boundary.

The initial structural layout check used a banned display-string API and was
replaced with typed namespace/name matching. Formatting and test-only guard/import
issues were corrected with the pinned tooling. Existing Unknown expectations for
reference locals now identify the underlying unsupported operation. Broader and
actual NuGet validation is tracked below after completion.

Final managed-object validation passed all 1400 analyzer tests, 438 focused native
resource/protocol/cache/golden tests, all 555 IR tests and 5 actual package cases.
The package cases cover managed/array proof and refutation, strict/advisory policy,
AnyCPU Unknown outcomes and equivalent-artifact cache reuse. Layout passed with
2664 source files, 59 projects and 151 pinned common files. Documentation drift
and 723 local links passed. All non-spelling quality/compatibility categories
passed; targeted spelling/Markdown verification passed after plain-language fixes.
Final source formatting remains part of the delivery checks.

Published checkpoint 457146536 passed every non-coverage workflow and all four
coverage shards. Aggregate qualification failed at 3099/3180 changed trusted lines
(97.45%), leaving 81 gaps against the unchanged 100% baseline. That measurement
excludes this managed-object layer; fresh exact-head CI and final qualification
remain required. No phase C/E/F requirement is removed by this checkpoint.

## Scalar boxing source integration

Scalar boxing to object now reuses the total backend's typed conversion predicate.
Operand effects precede a managed Box storage fact, so source-call arrays, loop
counts and exceptions retain their ordering. Boolean and physical 8/16/32/64-bit
integer storage (including char) use the reviewed 24-byte box model. Existing
array count/element metrics remain zero for the box itself. Floating, decimal,
nullable, interface and user-defined conversions remain explicit Unknown cases.
Protocol 22/cache 24 invalidate older capability results; artifact 37 and program
model 4 already represent managed box facts and are unchanged.

Initial native validation passed 275 contract/protocol tests. Seven actual package
cases then passed across arrays, managed objects and scalar boxes, including
strict/advisory proof/refutation, AnyCPU Unknown and semantic artifact/cache reuse.
Final native operand-ordering and broader checks are recorded after completion.

String construction must preserve the runtime's cached zero-length path and
negative-length exceptions, rather than treating every constructor call as a
fresh instance. The next source/native implementation will follow the pinned
[String constructor implementation](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/String.cs).
Exact initializer replay, remaining string/delegate/closure extraction, alias/
escape/liveness, peak/retained output, stack, phase E/F and final unchanged 100%
qualification remain required. No existing roadmap item is removed here.

Final scalar-boxing validation passed 449 focused native/resource/protocol/cache/
golden tests, all 1405 analyzer tests and 7 actual package cases. Operand-call
arrays precede the box in concrete prefixes; a throwing operand cannot create a
box event. All quality/compatibility categories passed with zero build warnings
or errors. Documentation drift and 723 local links passed. Layout evaluated
2665 source files, 59 projects and 151 pinned common files. Exact-head CI for the
published managed-object checkpoint was still running at this local delivery
check; neither it nor the newer boxing layer is represented by an older coverage
percentage. The unchanged 100% qualification remains required.

## Repeated-character string integration

The next phase C slice admits the metadata `string(char, int)` constructor with a
constant character outside the surrogate range. The count may be a supported
constant or scalar size expression. Argument effects retain source evaluation
order; successful positive counts produce a typed String storage fact. Zero
returns the cached empty value without an allocation event. Negative counts throw
ArgumentOutOfRange before a string instance is created.

The symbolic backend keeps content conservative, retains non-null/length facts,
and never infers unequal string values from fresh storage. Concrete replay fills
the requested character and reports positive successful allocation prefixes under
the owned layout model. Runtime helper work remains outside source loop counts.
Dynamic characters, surrogate characters, other string overloads and opaque
string factories remain Unknown. The well-formed UTF-16 value domain is unchanged.

Protocol 23, cache 25 and artifact 38 invalidate old capability results. The
existing resource program model 4 already represents the typed String shape.
Final validation is recorded below. Remaining phase C/E/F requirements, including exact
initializer replay, delegates/closures, alias/lifetime analysis, peak/retained
space, stack models, asymptotic/amortized/average-case analysis and concurrency,
remain open. Final exact-head CI and unchanged 100% qualification remain required.
String validation passed all 1408 analyzer tests, 562 IR tests (including the
independent pinned-runtime allocation controls), and 9 actual package cases.
The 659-case focused worker run passed 657 cases; the only failures were two
artifact-schema golden numbers, corrected from 37 to 38 and independently rerun.
All 81 golden cases passed after the schema correction. Layout passed with 2665
source files, 59 evaluated projects and 151 pinned common files.
Quality/compatibility categories passed apart from documentation spelling/newline
issues; targeted spelling and Markdown checks passed after correction. Final
source whitespace verification passed. Documentation drift and 723 local links
passed.

Published boxing checkpoint f59fb2fda passed every non-coverage workflow and all
four coverage shards. Aggregate qualification failed at 3219/3308 changed trusted
lines (97.31%), leaving 89 gaps against the unchanged 100% floor. This measurement
excludes the string layer. Fresh exact-head CI and final qualification remain
required; no roadmap item is removed by this checkpoint.

## Constructor initializer replay

The functional/native backend now lowers admitted instance field and simple
auto-property initializers in source declaration order. Caller arguments are
captured first, then the instance is allocated, then initializers execute, then
the explicit body runs. Implicit constructors execute initializers before
returning. Direct field object initializers then execute after construction. Field stores share the fresh instance with body/caller reads, and
exceptions stop later initializers and body effects. Root constructor body-only
admission remains separate; a constructor body CFG is never silently treated as
containing its field initializers.

The exact construction path requires one source type declaration without type
parameters directly derived from System.Object. Static initialization, inheritance,
constructor chains, partial declarations, property-setter/nested object initializers and
unsupported initializer values retain conservative boundaries. Compiler-backed
instance auto-property reads have no hidden getter cost; custom/virtual getters
remain unsupported in resource cost extraction.

Protocol 24, cache 26 and artifact 39 invalidate previous capability evidence;
resource program/evidence model 4 and expression model 1 are unchanged. Tests
cover mixed byte prefixes, initializer value flow, throwing arguments/initializers
and package consumers. Broader phase C/E/F requirements and
unchanged 100% final qualification remain open.
Validation passed 881 broader native/resource/source-call/protocol/cache/golden
cases, 180 final construction cases plus 2 constructor-fault controls, all 2062
frontend tests and all 1409 analyzer
tests. Eleven actual package cases passed across arrays, explicit/implicit
construction, boxing and strings, including strict/advisory handling, AnyCPU
Unknown and equivalent-artifact reuse. All quality and compatibility categories
passed. The 26 pinned-runtime layout tests also passed, independently measuring
64-byte field/auto-property construction and 104-byte field replacement after
construction. Final source formatting passed. Layout passed with 2665 source
files, 59 evaluated projects and 151 pinned common files; documentation drift and
723 local links passed.

Published string checkpoint 518c98d80 passed every non-coverage workflow and all
four coverage shards. Aggregate qualification failed at 3292/3382 changed trusted
lines (97.34%), leaving 90 gaps against the unchanged 100% baseline. This result
excludes initializer replay. Exact-head CI, coverage gap closure and all remaining
phase C/E/F work remain required.

## Master reconciliation

The draft MR became conflicted after master advanced to `deaa0a57c`, so GitHub
could not run pull-request CI for initializer checkpoint `17d028b08`. The branch
now merges that master revision. Its only content conflict was source call
preparation: source binding types must refresh before receiver creation, while
constructed calls must still model their fresh receiver. The resolution preserves
both rules and retains the upstream array range, collection, conversion and
runtime type-identity implementations.

Merged-state verification passed 882 worker resource/source-call and upstream
feature cases, all 577 IR tests, all 2112 frontend tests and all 1409 analyzer
tests. All 11 package cases and all quality/compatibility categories passed.
Documentation generation and 723 local links passed. Layout passed with 2704
source files, 59 evaluated projects and 151 pinned common files. Delegate/closure allocation work
resumes after merge qualification; no remaining phase C/E/F requirement or the
unchanged 100% coverage qualification is removed by this reconciliation.

## Explicit delegate storage

Compiler-bound delegate type certificates now survive portable IR transport and
identify successful fresh delegate events without parsing display names. The
factory/decoder reject built-in, legacy, class/interface-conflicting and foreign
certificates. This uses the same compiler-fact trust boundary as other typed IR
certificates; arbitrary producer assertions are not independent source proofs.

The resource source/native path admits explicit method-bound construction with
source targets, preserving receiver effects and skipping target method bodies.
Each fresh delegate uses the reviewed 64-byte runtime footprint. Erased delegate
construction contributes zero, and null receiver failures cannot create an event.
Repeated construction composes with loop bounds. Managed delegate signatures are
now admitted; body support remains independently checked. Discard assignments
preserve RHS effects, including the delegate-specific erasure rule.

Implicit method-group/lambda conversions, capture-frame placement/sharing,
metadata/extern target binding, target type initialization and unmodeled dispatch
remain Unknown. Closure frames must be placed at their actual compiler allocation
scope; charging a bundle only at a lambda expression would miss frames allocated
before zero-trip loops or exceptional exits. Closure/lifetime/peak/stack and phase
E/F requirements remain open.

Protocol 25, cache 27 and artifact 40 invalidate previous capability evidence;
resource program/evidence model 4, expression model 1 and the runtime layout model
are unchanged. Initial validation passed 77 native/codec cases and 585 IR tests.
Final validation passed 961 focused worker cases, all 1412 analyzer tests, all
2112 frontend tests and 13 actual package cases. All quality/compatibility
categories passed. Independent pinned-runtime controls measured fresh static
delegates at 64 bytes, instance-bound creation including its receiver at 88 bytes,
and erased construction at zero; all 26 runtime layout cases passed. Layout
evaluated 2705 source files, 59 projects and 151 pinned common files. Documentation
generation and 723 local links passed.

Published merge checkpoint 11e3a57ed restored CI eligibility. Canonical CI found
three old target-typed constructor expectations that excluded a valid proof after
field replay became exact. The strengthened regression now checks both the actual
result and false postconditions under an invalid callee precondition. All seven
constructor/callee-precondition controls passed, confirming false callee requires
are not imported as assumptions. The same three expectations blocked the published semantic-worker coverage shard,
so no fresh aggregate percentage is available for that merge. Coverage
qualification and exact-head CI remain required against the unchanged 100% baseline.

## Root parameter closure frames

A shared bounded capture plan now admits synchronous static methods whose sole
return constructs one delegate closing over entry parameters. The plan uses
compiler data-flow capture facts, not textual variable matches; `nameof` does not
invent captures. Nested functions, local capture scopes, broader control flow,
async lambdas and unsupported value storage retain Unknown boundaries.

The native program allocates a typed frame at entry and the delegate at the return
expression. Immutable frame storage certificates survive portable transport and
are incompatible with ordinary class/interface/delegate certificates. Source
bounds charge the same frame separately from delegate creation. A packed lower
bound and conservative field-layout upper bound remain distinct. No lambda body
is executed while computing construction costs.

Independent Debug/Release emission controls confirm frame construction precedes
user branches, zero-trip loops and exceptional exits. The zero-loop example also
contains a delegate-cache field, making its entry frame 32 bytes. It remains
Unknown because a general capture/sharing model is still required; moving all
closure cost to the lambda expression would miss that allocation. The admitted
single-return family has no such cache field.

Protocol 26, cache 28 and artifact 41 invalidate previous capability evidence.
Resource program/evidence model 4, expression model 1 and the runtime layout model
are unchanged. Validation passed 934 focused worker cases plus three packing-bound
controls, all 1415 analyzer tests, all 2112 frontend tests, all 593 IR tests and
15 package cases. The 95 capture/transport controls include independent Debug
and Release compiler observations. Full quality and compatibility checks passed;
layout evaluated 2708 source files, 59 projects and 151 pinned common files.
Documentation generation and 723 local links passed. Published delegate CI also
exposed a stale signature expectation: admitting a delegate parameter does not
supply a complete effect summary, which remains Unknown. The regression now
checks that distinction explicitly; all 88 manifest cases passed. Broader closure scopes,
sharing, lifetime/peak/retained memory, stack and phase E/F remain required, along
with unchanged 100% coverage qualification and exact-head CI.

The closure implementation is committed at 399215b6b. Latest master 9e3b1ade2
(HashSet state and signed callback counts) is merged at c8baf35fb. The one
insertion conflict preserves both repeated-character string construction and
HashSet lowering. Combined-state validation passed 581 focused resource,
delegate, closure, manifest, HashSet and constructor controls, all 2121 frontend
tests, all quality/compatibility categories, documentation generation and 723
local links. Layout evaluated 2713 source files, 59 projects and 151 pinned common
files. Published b3e0d2db6 coverage failed in the semantic-projects shard on the
same stale manifest expectation now corrected; it produced no qualified aggregate.
Fresh exact-head CI and unchanged 100% coverage qualification remain open.

## Logical memory lifetime evaluator

Phase C now has a bounded finite-trace evaluator for fresh source-owned objects,
root aliases and strong-reference field stores. It computes cumulative storage,
peak auxiliary storage and retained output separately. The declared logical model
excludes the final returned object graph from auxiliary storage throughout the
trace; it does not claim GC collection times or physical heap residency. Shared
objects and cycles count once. Replacing a root includes the transient overlap
while the new object is allocated before the old root is overwritten. Removing a
field reference can release a former child from the logical reachable set.

Allocation sizes remain lower/upper intervals supplied by the separately checked
layout model. Unsupported escapes, malformed references, resurrection of objects
that lost every root, invalid output roots and exhausted traversal budgets return
Unknown without partial metrics. The finite event and traversal limits bound work
and snapshot storage; cancellation propagates.

This evaluator is an internal foundation. Compiler-owned trace extraction,
portable ownership binding, static path/repetition composition, native contracts,
and consumer reporting for peak/retained metrics remain to be connected. It does
not yet expand public metric support. All 612 IR tests passed, including 19 new
lifetime cases and replacement traces for 100 sizes.
All quality and compatibility checks passed. Layout evaluated 2715 source files,
59 projects and 151 pinned common files. The phase C checklist remains incomplete.
