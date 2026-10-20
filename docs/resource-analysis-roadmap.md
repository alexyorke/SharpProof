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

- [x] Object, string, closure, boxing, and array allocation costs.
- [x] Byte bounds under an explicit, versioned runtime layout model.
- [ ] Alias/escape and lifetime reasoning; distinguish cumulative allocation,
  peak live auxiliary memory, and retained/returned output.
- [x] Stack frame and recursive stack bounds under a declared model.
- [x] Independent allocation/liveness controls, mixed-size objects, aliases,
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
- [x] End-to-end runtime/allocation benchmark workflow, with input scales,
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

The branch now includes native asymptotic upper evidence, structured protocol
validation and trusted string-length size/contract binding, in addition to the
expression foundation and packaged SP0060 activation repair. Before the latest
master merge, verification passed 828 affected worker tests, 748 IR tests and five
strict native growth/stack/string package workflows. The full analyzer run passed
1429 cases plus the separately repaired protocol-seal assertion. The combined tree
then passed 920 worker, 1430 analyzer, 10 architecture and 11 package/closure cases. Independent lower-bound/tightness evidence, amortized accounting and
validated distributional models remain required.

The previously published stack checkpoint eec823652 passed canonical/package/docs/
security CI, then failed the unchanged 100% changed-trusted-code requirement at
3720/3910 covered lines (95.14%, 190 gaps). Fresh exact-head CI is required for
this checkpoint. The draft MR remains [#334](https://github.com/alexyorke/SharpProof/pull/334).

Remaining required work is the unchecked phase C/E/F scope and final coverage/CI
qualification. The checked phases describe explicitly admitted models with Unknown
outside admission; they do not claim arbitrary C# analysis. Details and evidence
for each extension follow below.

### Historical initial checkpoints

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

## Compiler-owned finite lifetime extraction

The lifetime evaluator now accepts an internally extracted trace from a typed IR
program, rather than caller-provided allocation numbers. The initial admitted
family is a single finite chain of primitive-array allocations, scalar/reference
copies, and normal returns. Array storage widths and lengths come from typed IR
and the separately validated runtime layout. Reference aliases preserve object
identity; last-use root drops distinguish dead temporaries from live aliases.
Literal copies through compiler temporaries retain their values until overwritten;
unknown writes invalidate them. Constant comparison guards select only their proven
edge. Local-write effect markers do not invent heap edges. Unreachable blocks do
not contribute execution costs. Cycles, unproved branches, calls,
unknown scalar effects, unproved symbolic or negative lengths, borrowed references and
unsupported allocation shapes remain Unknown without partial metrics.

The extractor bounds block/instruction/term scanning and emitted event counts.
This is a normal-completion logical model. Exceptional-path composition, field
stores/loads, object graphs, source-call lifetime summaries and symbolic repeated
regions remain required. Native contract transport/reporting and public peak and
retained metrics are still pending. All 635 IR tests passed, including 23 new
extractor cases. Four validated-artifact source workflows passed: direct array
return, returned alias, a dead temporary before the returned allocation, and
borrowed-output rejection. The first three also execute emitted C# and verify the
returned array lengths. Tests check the actual compiler-generated constant-copy
and exception-guard representation rather than assuming source literals remain
literal IR terms. Layout evaluated 2718 source files, 59 projects and 151 pinned
common files. All quality and compatibility categories passed. Documentation
generation and 723 local links passed.

Exact published closure head fcbf7a467 passed all four coverage shards. The
aggregate enforced the unchanged 100% floor and failed at 2665/2785 changed
trusted lines (95.69%, 120 uncovered lines). This measurement predates the
lifetime evaluator/extractor; it does not qualify the newer work. Coverage gap
closure and fresh final exact-head CI remain required.

Published closure head fcbf7a467 also passed every canonical container PR gate,
package/consumer/release workflow, documentation and toolchain security checks.
Its coverage threshold is the remaining failing workflow.

## Native logical lifetime upper-bound contracts

The public contract API now names `PeakAuxiliaryBytes` and `RetainedBytes`.
Compiler collection binds the same reviewed layout dependency and target/reference
evidence as cumulative bytes. A distinct lifetime cost-model identity declares
normal-completion logical reachability; cumulative metrics keep their existing
exceptional-prefix definition. The native worker derives finite primitive-array
lifetime bounds from the owned typed IR, records its model and program digest,
and projects the owned layout dependency only on a decided proof.

Unsupported lifetime paths and unproved comparisons return Unknown without partial
numbers or used assumptions. Numerical counterexamples for these metrics remain
pending independent replay; upper estimates cannot manufacture refutations.
Broader field/object graph extraction, borrowed ownership, exceptional-path
composition, symbolic repetition, calls, stack and phases E/F remain required.

Protocol 27, cache 29 and compiler artifact 42 invalidate older capabilities.
Manifest 6, resource program/evidence 4, expression 1 and the allocation-layout
identity remain unchanged. All 14 focused public contract API tests, all 1416
analyzer tests, all 635 IR tests and 17 actual packaged-consumer cases passed.
Full quality/compatibility categories, layout (2719 source files, 59 projects,
151 pinned common files), documentation generation and 723 local links passed.
The final broader worker/protocol run passed all 549 cases, including the mixed
cumulative/peak/retained and conflicting-cost-model controls. Two initial restore failures were shared NuGet
vulnerability-cache collisions. The affected gates passed with container-private
HTTP caches and auditing left enabled.

Native lifetime implementation 77e0aff40 is merged with latest master 7f6e25f21
(Dictionary capacity construction) at 520600efb. Combined-state checks passed 776
focused worker/protocol/Dictionary cases and all 2122 frontend tests, followed by
all quality/compatibility categories. Final review separated lifetime layout
ownership from cumulative-program availability. Two fixed-local-size source cases
prove peak/retained bounds with a null cumulative program; the expanded affected
worker/protocol/Dictionary run passed all 778 cases. Both added packaged-consumer
controls and all 281 affected analyzer/collector cases passed. Together with the
earlier 17 package cases, 19 distinct package cases now cover this milestone.
Final quality/compatibility categories passed. The added package controls used
the Git-aware wrapper required by Arcade packaging; a terminal restore-process
crash was rerun successfully with the same pinned toolchain.

## Completed-execution lifetime refutations

Native lifetime violations now require a concrete completed execution, independent
of the source-produced upper envelope. A bounded interpreter observer records
successful instructions and produced values. Replay reconstructs primitive-array
identities, byte footprints, aliases, last-use roots and returned ownership from
those observations, then checks the declared lifetime model. Unknown, approximate,
exceptional, truncated, foreign or unsupported observations cannot supply numbers.

Entry constraints, receiver requirements and all Requires definedness/value
conditions are checked before replay. Samples only supply candidates. A separate
solver-entry control requires n == 37, outside the cheap sample set, and validates
that entry concretely. False and contradictory Requires produce no refutation.
Lifetime witnesses identify normal completion; cumulative witnesses retain their
execution-prefix semantics. Owned layout evidence is projected for decided
lifetime refutations without requiring a cumulative resource program.

Protocol 28, cache 30 and compiler artifact 43 invalidate earlier cached Unknown
outcomes. Manifest 6, resource program/evidence 4, expression 1 and the lifetime
model identity remain unchanged. All 781 affected worker/protocol/Dictionary
cases, all 641 IR tests, all 1416 analyzer tests and five selected package cases
passed. The package selection includes three new lifetime refutations and two
independent-path proofs; 22 distinct package cases have now been validated across
the lifetime milestones. Layout evaluated 2721 source files, 59 projects and 151
pinned common files; documentation generation and 723 local links passed. A full
IR restore-process crash was rerun successfully with private CLI/HTTP state.
All quality categories passed. The first external-link audit received a 504 for
an unchanged GitHub URL; its targeted retry passed without source-link changes.
Broader graph, call/repetition/exceptional lifetime composition, stack and phases
E/F remain open, together with unchanged 100% coverage and exact-head CI.

## Owned object field graphs

Compiler-issued complete object-storage certificates now cover every instance
field of admitted direct source classes, including unused and backing fields.
Typed immutable storage survives portable IR and cannot be mixed with interface,
delegate, closure, inherited, legacy or built-in certificates. Layout facts are
compiler facts rather than inferences from whichever fields a body happens to read.

Finite lifetime extraction and independent concrete replay now preserve owned
field graphs. Shared children and cycles count once; overwriting a field edge can
release a former child. Proven non-null facts and safe object casts preserve field
store order. Packed lower and conservative upper object bounds remain separate.
Borrowed child references and unsupported layouts produce Unknown without partial
numbers or used layout dependencies. Field reads and broader lifetime composition
remain required; this does not finish phase C or phases E/F.

Protocol 29, cache 31 and compiler artifact 44 invalidate older object-layout
capabilities. Manifest 6, resource program/evidence 4, expression 1, the allocation
layout and lifetime model identities remain unchanged. Four initial IR graph
controls and 16 focused native/portable cases passed. Final validation passed
574 affected worker/protocol/portable cases, all 645 IR tests, all 2122 frontend
tests, all 1416 analyzer tests and four actual packaged object-graph proof and
refutation cases. Across the lifetime milestones, 26 distinct package cases have
passed. Quality/compatibility, documentation generation and 723 local links passed;
layout evaluated 2724 source files, 59 projects and 151 pinned common files.
Broad regression checks caught an initially excessive certificate restriction on
sealed classes. Sealed and direct-class facts now compose, and both original
suites plus the explicit sealed-object control pass. Packing controls preserve
Unknown between the packed lower and conservative upper bounds.

## Reads of explicitly stored owned references

Finite lifetime extraction now resolves instance field reads by scoped receiver and
member identity after an explicit supported store. Object aliases see the same
field; copying a child reference into a local preserves the child's identity when
the field is later overwritten. Explicitly stored null references are also known.
The bounded reference resolver follows safe casts and nested field reads while
last-use traversal keeps their receiver roots live through evaluation.

Concrete replay derives read results from actual completed instruction values,
independently of the static field map. The existing normal-completion and entry
validation requirements remain in force. Unwritten fields remain Unknown because
the current storage certificate does not bind field members to complete default
storage facts. Scalar field computations and broader lifetime composition are
still required; this checkpoint does not finish phases C, E or F.

Protocol 30, cache 32 and compiler artifact 45 invalidate older capabilities and
cached Unknown results. Manifest 6, resource program/evidence 4, expression 1 and
the reviewed layout/lifetime model identities remain unchanged. Focused checks
passed all five IR graph cases and five native read controls, including proof and
concrete refutation, aliases, overwritten edges, explicit null and an unwritten
field Unknown with no partial numbers or used layout dependencies.

Broader checks passed all 646 IR tests, 578 affected worker/resource/protocol/portable
cases, 352 analyzer/collector cases and four actual packaged field-read cases.
Across lifetime milestones, 30 distinct packaged consumer cases have passed.
Quality/compatibility, formatting, documentation generation and 723 local links
passed. A restore-process crash was retried successfully with fresh container-private
CLI/HTTP state and auditing enabled.

Published coverage CI for object-graph head 143901c17 exposed a missed acceptance
contract cache update: the contract still specified schema 28 while the worker
used 31. The contract now matches schema 32. Exact-head published CI and the
unchanged 100% coverage floor remain required; local checks do not substitute for
that qualification.

The corrected acceptance contract and updated artifact snapshots passed all 122
selected acceptance/golden worker cases. Final documentation spelling and Markdown
checks passed.

## Comparable runtime and allocation measurement workflow

The existing packaged-consumer performance harness now has an explicit resource
workload. It builds the same eight allocation kernels against an Attributes-only
baseline, advisory analysis and strict native verification, rotating variant order
between iterations. Restore, first build, no-op, warm rebuild and small-edit times
stay separate. Every strict build must retain eight nonvacuous Proven resource
claims and complete selected coverage before its measurements can be accepted.

The final edited source is executed at explicit input scales with repeated calls
and ten warmups per scale. A separate process runs each variant. Raw stopwatch
ticks/frequency describe kernel execution; process milliseconds include startup,
warmups and report serialization. GC thread allocation counters describe total
bytes over the measured repetitions, including fixed entry allocation at input
zero. They do not describe peak live or retained storage. Output checksums validate
actual execution independently of selected source proofs.

Report schema 3 records source/harness/package identities, actual runtime and
architecture, OS, GC mode, the tiered compilation environment setting, inputs,
repetitions, raw samples and signed comparisons with each iteration's baseline.
Comparison admission requires matching source, environment and output. Invalid or
duplicate scales are rejected before package loading. The resource sample and final combined controls passed against the packaged
product, including the original contract workload, invalid-scale rejection and
baseline report comparisons.

The [consumer performance guide](consumer-performance.md) documents the command
and interpretation.
Measurements are explicitly separate from asymptotic/tight/expected/amortized
proofs. Async/concurrent models, cancellation and exceptional completion remain
required along with the other unfinished roadmap items.

Final validation passed all four package integration controls. Both complete
benchmark workloads ran concurrently. The initial combined run exposed shared
compiler-server analyzer loading state across separate package caches; a per-run
compiler identity now isolates independent runs while preserving sharing among
that run's variants. The final combined run passed after this correction.

The .NET 8.0.32 x64 resource smoke run used one iteration and three repetitions per
input. Every variant observed 576, 12864 and 98880 allocated bytes for input sizes
0, 16 and 128 respectively, with matching output checksums. These are functional
measurement controls rather than timing conclusions; routine measurements default
to three iterations and 100 repetitions. Raw reports are retained as test evidence.

All quality/compatibility categories passed, followed by the focused PowerShell
check after compiler isolation. Documentation generation and 724 local links passed.
Native protocol 30, cache 32 and compiler artifact 45 remain unchanged because this
phase adds measurement tooling rather than trusted proof capabilities. Benchmark
report schema 3 distinguishes measured process/kernel time and allocation samples
from source-derived resource proofs. The benchmark workflow requirement is now
complete; all other unchecked C/E/F and final qualification requirements remain.

## String lifetimes and guard root uses

Finite typed-IR lifetime extraction and independent completed-execution replay
now admit repeated-character string construction with constant nonnegative sizes.
The reviewed UTF-16 layout charges distinct positive-length instances; equal
contents do not collapse allocation identity. Object widening, saved aliases,
field edges and overwrite preserve ownership. Shared empty construction and empty
literals use a permanent non-null zero-byte anchor, so no new owned bytes are
charged and repeated use cannot resurrect a reclaimed instance.

Guard operands now participate in last-use root tracking in both extraction and
replay. This corrects an earlier omission that could drop two references before
the comparison that reads them. String content equality is explicitly excluded
from reference-identity guard inference. Borrowed/nonempty literal storage,
negative lengths, unproved content branches and broader composition remain Unknown
without numeric evidence or used assumptions.

Protocol 31, cache 33 and compiler artifact 46 invalidate earlier capabilities and
cached outcomes, including the guard-root correction. Acceptance worker/cache
versions and artifact snapshots move together. Manifest 6, resource program/evidence
4, expression 1 and reviewed layout/lifetime model identities remain unchanged.
Initial validation passed 58 memory IR cases and five native string cases.
Compiler-pinned Boolean comparisons initially prevented two new native guard
controls from being decided. Proven comparison values now propagate through
Boolean temporaries, while unproved content/effect conditions remain Unknown.
The expanded 11 native string controls and all 653 IR tests passed after this
correction, along with 352 analyzer/collector cases. The final broad worker/
protocol/acceptance/golden run passed 711 cases, and all four package controls
passed.
Phase C and the other unchecked roadmap requirements remain open.

The four packaged string lifetime controls passed after Boolean guard propagation,
including independently replayed retained and guard-root peak violations. There
are now 34 distinct validated lifetime package cases, alongside the separate
measurement workflow controls. The broad worker retry passed all 711 affected worker/protocol/acceptance/golden
cases. An earlier .NET build-process exit 139 occurred before tests; the serial
retry used fresh private CLI/HTTP state.

Published benchmark-head f6e63144c coverage passed all four shards, then failed the
unchanged 100% changed-trusted-code floor: 3242/3439 lines (94.27%), with 197 gaps.
This is newer than the earlier closure-only measurement but predates string
lifetimes and cannot qualify this implementation. Coverage gap closure and fresh
exact-head CI remain required.

Published benchmark-head canonical CI identified a script formatter failure in
Measure-SharpProofConsumerPerformance.ps1, separate from its passing PowerShell
lint. The formatter changed only script whitespace. The complete canonical
formatting gate passed after that correction, including 87 PowerShell files,
19 shell scripts, script-tool regression checks and complete C# formatting.
The earlier package/consumer/
release, documentation and security workflows passed. Native string changes
remain subject to fresh exact-head CI and unchanged coverage qualification.

## Completed replay path integrity and boundary controls

Coverage review identified missing controls around lifetime replay validation.
A regression reproduced acceptance of omitted and reordered callbacks from an
otherwise completed real execution. In particular, omitting a returned-graph
field store could misclassify the child as auxiliary storage. Instruction ownership
and the final return alone did not establish a complete observation path.

Replay now indexes each owned instruction's block and position. Observations must
start at the entry instruction, remain contiguous inside each block, and follow
the preceding terminator. The interpreter supplies the actual evaluated Boolean
at branch completion; replay requires the next block to match that decision.
Missing stores, missing entry instructions, reordering and missing/contradictory
branch observations produce MemoryReplayTraceIncomplete without numeric evidence.
Foreign or repeated instructions remain unsupported, and approximation/budget
failures remain sticky even when later callbacks arrive.

Additional controls exercise primitive storage widths, incompatible layouts,
reference-element and initialized arrays, uncertified objects, allocations without
a result target,
borrowed copies/returns, graph/observation budgets, legacy semantics, actual
completed repetition and cancellation. These are boundary controls around existing
admission rules; they do not add a repetition or borrowed-ownership model.
All 679 IR tests passed. A test setup initially supplied Total replay options to
the legacy interpreter and was corrected to observe an actual legacy completion.
All 517 selected native/golden/cache-identity cases and 17 packaged lifetime
proof/refutation cases passed.

Protocol 31, cache 33 and compiler artifact 46 remain unchanged: this hardens an
internal callback invariant and adds no wire fields or newly admitted source
capability. Worker binary identity includes every SharpProof companion assembly,
including SharpProof.Ir.dll, so changed interpreter/replay code invalidates caches.
A dedicated control mutates that companion module and checks the digest changes,
while an unrelated module leaves it stable. Fresh exact-head CI and unchanged
100% changed-trusted-code coverage qualification remain required, along with all
other unfinished C/E/F roadmap items.

Final validation passed all 679 IR tests, 517 selected worker/golden/cache-identity
cases and 17 packaged lifetime cases. Repository quality categories passed after
correcting two documentation spelling findings; complete canonical formatting,
including script-tool checks and full C# formatting, passed. Documentation
generation and 724 local links passed. These controls do not establish the final
coverage percentage; the unchanged 100% aggregate floor and the full remaining
roadmap stay open.

The latest completed published coverage aggregate is string head 18d11e69c.
All four shards passed, but 3299/3501 changed trusted lines (94.23%) left 202 gaps
against the unchanged 100% floor. That aggregate predates this replay-path fix and
its new boundary controls; it cannot qualify this checkpoint.

## Compiler field initialization and finite scalar state

Eligible direct source-class construction now materializes CLR zero/null
initialization as scoped typed field stores, after allocation and before source
initializers and constructor execution. This uses the existing instruction model
rather than inferring member defaults from byte storage certificates. It includes
backing fields and preserves reference/owner identity across aliases.

Memory extraction records known integer and Boolean field values, resolves scalar
reads into guards and extents, and clears stale facts after unknown writes. It
supports safe constant conversions and uses mathematical integer values rather
than a signed intermediary, including UInt64.MaxValue. Independent replay continues
to derive actual sizes, values, identities and completed paths from execution.
Truncating conversions without already-folded typed facts remain unsupported.

Initial native controls passed default reference reads, default/updated scalar
extents, Boolean guards, source field initializers, backing fields, aliases and
separate instances. A constructor-body control exposed approximation at new Node()
before allocation; the constructor-receiver follow-up below resolves that
boundary. Broader constructor/call lifetime admission remains required.
The scalar overwrite control prevents an unknown input from reusing default zero.

All 682 IR tests, 2122 frontend tests and 1416 analyzer tests passed, along with
four actual packaged default-field/scalar proof and refutation cases. One lowering
golden gained the reviewed zero stores and instruction renumbering; both original
and reformatted source controls passed in the full frontend run. An initial
truncation test was corrected to prevent the factory from folding its constant
before memory analysis. The final affected native/protocol/artifact/acceptance/
golden gate passed all 723 cases.

Protocol 32, cache 34 and compiler artifact 47 invalidate older capabilities and
cached compiler results. Acceptance worker/cache constants and artifact snapshots
move together. Manifest 6, resource program/evidence 4, expression 1 and declared
layout/lifetime model identities remain unchanged. Phase C and remaining E/F work,
unchanged 100% changed-trusted-code coverage and exact-head CI remain required.

Quality/compatibility categories and complete canonical formatting passed. The
new package cases bring the cumulative validated lifetime package controls to 38.
Published replay-head 5567c1bb0 passed all four coverage shards, then reported
3338/3518 changed trusted lines (94.88%), with 180 gaps against the unchanged
100% floor. That improves on the preceding 202 gaps but predates field-default
analysis and cannot qualify this checkpoint. Fresh exact-head CI remains required.

## Fresh constructor receivers and initialization effects

Direct constructor frames already lowered scalar stores and conditional bodies
exactly. Caller lowering nevertheless emitted an approximation for the receiver
before evaluating arguments, then replaced that receiver with the allocation.
Construction now binds only the fresh allocated receiver, after arguments and
before the constructor body. Ordinary instance calls retain their evaluated
receiver and null/freshness rules.

Native lifetime controls cover constructor scalar stores, guards over CLR
defaults, field initializers followed by constructor updates, and constant
constructor arguments. They check both exact bounds and independently replayed
violations. An unconstrained constructor argument must still produce Unknown
without partial numbers or used metadata assumptions.

The first receiver-fix verification passed 302 native/source-call cases, all
2122 frontend cases, and four actual packaged constructor lifetime proof and
refutation cases. The package controls exercise retained output and auxiliary
memory with strict/advisory diagnostics and result evidence.

Published field-default checkpoint 36c649ad9 did not reach aggregate coverage:
the semantic-worker shard failed six existing purity/synchronization controls.
CLR zero stores had ordinary observable write sites. Zero initialization now
uses the existing compiler-owned fresh-write marker, before any source
initializer or constructor can publish the object. Source initializer and body
writes retain their previous classification. All 40 affected purity, ownership,
constructor, synchronization and independent runtime controls passed after this
correction, including escape/rebind controls. The canonical-main failure at the
published checkpoint contains the same six regressions. Its package/release run
passed. No coverage qualification or full-roadmap completion is claimed.

This correction uses existing receiver assignments, field stores and compiler
fresh-write sites. Wire versions remain unchanged; worker identities include the
frontend and other SharpProof companion assemblies, invalidating prior cached
lowering results. Final canonical formatting, repository quality/compatibility,
documentation generation and all 724 local links passed. The final frontend
regression run again passed all 2122 tests.

The final packaged rerun passed all four constructor cases after the fresh-write
correction, bringing the cumulative validated lifetime package controls to 42.
Final checkpoint Markdown and spelling checks passed as well.

The full final worker suite passed all 6595 cases with no failures. This includes
the native lifetime, purity, synchronization, replay, artifact, protocol and
consumer-result regressions. Fresh exact-head CI remains required after publishing
this checkpoint; the unchanged coverage floor and remaining C/E/F work stay open.

## Static delegate and scalar box lifetimes

Finite lifetime extraction and completed replay now recognize explicit static
delegate allocations through an exact compiler-owned operation marker plus the
existing nominal delegate type certificate. The compiler marker requires an
explicit static method binding, owned source declaration and admitted type
initialization. A delegate type alone, a marker without that type certificate,
or a similar-looking marker cannot establish capture-free storage.

The declared layout supplies the delegate's 64-byte storage; no producer byte
estimate enters the lifetime trace. Existing aliases, reference guards, last-use
roots and returned-graph exclusion compose with these fresh identities. Scalar
boxes already use the supported fresh-object path; new native controls verify
their 24-byte storage and distinct identities for Boolean and integer payloads.

Initial verification passed six IR admission/replay cases and eight native
lifetime cases. Additional controls preserve Unknown for captured instance
delegates, cached conversions, closures, metadata bindings and target type
initialization. Full affected checks and packaged proof/refutation controls are
in progress. Instance-target and closure capture edges, broader repeated and
exceptional lifetimes, stack bounds and remaining E/F requirements remain open.
Wire versions and model identities are unchanged: the operation descriptor and
delegate certificate use existing owned IR transport; companion-module identities
invalidate earlier implementations.

The full IR suite passed 688 tests and the full frontend suite passed 2122.
All 583 affected native/resource, delegate/closure, allocation, protocol,
artifact/golden and cache-identity controls passed. Independent pinned-runtime
allocation controls passed after adding Boolean and 64-bit box payloads alongside
the existing integer-box and static-delegate measurements. Layout passed for
2725 source files, 59 projects and all 151 pinned common files; documentation
generation and 724 local links passed. Quality categories passed apart from one
guide spelling correction; the focused spelling gate then passed with zero
findings. Complete canonical formatting passed as well.

All 11 final native controls passed, including a generic static delegate whose
target throws if invoked. This confirms that construction does not execute the
target body. Six actual packaged static-delegate/box proof and refutation cases
passed with strict/advisory diagnostics and serialized evidence, bringing the
cumulative validated lifetime package controls to 48.

Next, model instance delegate targets as mandatory owned reference edges and
connect returned closure delegates to their entry frames and captured values.
Missing, borrowed or unproved capture edges must prevent numerical lifetime
claims. Preserve field-graph sharing/cycles and independently observe these edges
during completed replay before admitting broader capture families. Stack bounds,
remaining C/E/F requirements and unchanged coverage qualification stay open.

## Owned instance delegate target graphs

Explicit admitted instance bindings now emit a delegate allocation followed by a
scoped synthetic target-field store. The target is evaluated and checked before
allocation; the target body does not execute. The store uses the existing fresh
write classification because it initializes the newly allocated delegate.

Finite extraction and completed replay require the exact instance-allocation
marker, nominal delegate type and immediately following target store. The field
must have its reserved role, type and allocated receiver. Its value must be an
already owned, non-null reference distinct from the new delegate; the edge can
be initialized only once. Missing, unrelated, null, overwritten or self-capture
evidence returns Unknown without partial bounds. Existing object-graph traversal
then includes the target's children and cycles in retained output and excludes
the returned graph from auxiliary storage.

Initial verification passed seven IR capture mutation cases and 11 native
controls covering target retention, child arrays, target cycles, generic methods,
dead auxiliary storage, borrowed targets and unsupported conversions. Broader
checks, self-capture rejection, runtime graph controls and packaged evidence are
in progress. Ambiguous non-direct bindings remain Unknown under the existing
compiler erasure model. Closure frame/capture links, broader lifetimes, stack
bounds and remaining E/F work stay required.

Final verification passed all 697 IR tests, including eight malformed-capture
controls and independent runtime checks of the 120-byte delegate/target/child
bundle and its actual target link. All 847 affected worker tests passed across
resource, purity/effect, ownership, artifact/protocol, golden and cache families.
Six final native cases passed, including binding through an owned field read
while releasing the parent root. Six actual packaged proof/refutation controls
passed, bringing cumulative validated lifetime package controls to 54.

The frontend run passed 2120 cases and identified the two comparisons of the
instance-delegate lowering snapshot. Its reviewed update adds the capture store
and subsequent instruction numbering; both original and reformatted comparisons
then passed. Quality/compatibility, scoped C# formatting, spelling/Markdown,
documentation generation and 724 local links passed. Layout passed for 2726
source files, 59 projects and all 151 pinned common files.

Published static-delegate checkpoint 19b28e12c passed all four coverage shards,
but aggregate validation stopped before percentage enforcement because the new
static helper was missing from the explicit trusted-source inventory. Both
delegate helpers are now classified in the acceptance contract. The authoritative
evaluated production inventory validates all 516 trusted paths with both helpers
included. No coverage exclusion or floor reduction was introduced. The latest
numerical aggregate remains constructor checkpoint f98b6d080: 3395/3577 changed
trusted lines (94.91%, 182 gaps). Fresh exact-head CI and coverage qualification
remain required for this extension.

## Closure frame capture links

Admitted root parameter closures now initialize every captured slot at entry in
certificate order, using scoped synthetic fields with checked storage types.
Their returned delegate records the mandatory target edge to the frame. The
closure plan retains the ordered parameter symbols as well as storage widths,
so initialization uses the actual bound values instead of inferred defaults.

Extraction and completed replay require the frame marker, complete ordered slot
stores and matching receiver/member/storage roles. They reject omissions,
reordering, mismatched widths, overwritten slots and impossible direct frame
self-references. Null captured references remain valid; non-null references must
be owned. Existing graph traversal preserves shared captures and captured child
graphs without counting shared objects twice. Primitive inputs need not have
known values when their storage and construction path are fixed.

The initial build caught a local-name collision in the extractor; it was fixed.
Representative verification then passed 13 IR admission/replay controls and 18
native tests, including existing cumulative closure bounds and packing intervals.
New controls cover primitive frames, inlined source factories, owned reference
captures and borrowed-reference Unknown results. Broader checks, lifetime packing
controls, independent runtime graph measurements and packaged workflows are in
progress. The new helper is included in the explicit trusted-source inventory.
Closure invocation, broader capture scopes and remaining C/E/F requirements stay
open; existing wire transport and model identities remain unchanged.

Final verification passed all 711 IR tests and all 2122 frontend tests. Runtime
controls independently measured 144-byte owned and 152-byte shared closure
bundles and verified their returned target/child behavior. Required parameter
guards were added to those runtime test helpers after the broader build flagged
them. All 864 affected worker controls passed, followed by six final factory
cases including known-null captures, shared children and mixed primitive/reference
slots. Lifetime packing controls preserve Refuted/Unknown/Proven separation.

Six actual packaged primitive/owned/shared closure proof and refutation cases
passed, bringing cumulative validated lifetime package controls to 60. Layout
passed for 2727 source files, 59 projects and all 151 pinned common files.
Authoritative production inventory validates all 517 trusted paths, including the
new closure helper. Quality/compatibility, scoped C# formatting, documentation
generation and 724 local links passed.

Published capture checkpoint f306d8dfb passed canonical/package gates and all four
coverage shards. Its aggregate reached 3479/3659 changed trusted lines (95.08%,
180 gaps), failing the unchanged 100% floor. The inventory repair now reaches
percentage enforcement. This evidence predates closure-frame links; fresh
exact-head CI and gap closure remain required. Broader capture/call lifetimes,
stack models and remaining E/F requirements are still open.

## Logical source activation bounds

The stack requirement now has an explicit source activation model rather than a
JIT-frame estimate. Each activation reserves eight-byte variable slots for its
parameters, declared locals, receiver/result when present, and two bookkeeping
slots. Generated/runtime/native frames, evaluation temporaries and tail-call
elimination are outside this model. These bounds do not establish physical stack
usage or stack-overflow safety.

Compiler resource programs preserve method and constructor activation boundaries,
including calls in field initializers. Typed activation shapes contain storage
facts, not supplied depth/byte verdicts. Native derivation takes maxima across
sequences/choices and loop iterations, accumulates retained recursive activations
only under checked ranking programs, and adds conservative prefix-call storage.
Existing cumulative work/allocation visitors pass through activation boundaries.

SourceActivationDepth and SourceStackBytes contracts select this model. Current
stack claims support Proven upper bounds and explicit Unknown results; completed
stack-violation replay is not implemented, and protocol validation rejects stack
refutations. Unknown frames, unmodeled calls and unsupported recursion cannot
publish partial stack numbers. Strict transport validates shape fields, flags,
arity and unrelated payloads.

Resource program/evidence model 5, protocol 33, cache 35 and compiler artifact 48
invalidate older encodings/capabilities. Manifest 6 and expression model 1 remain
unchanged. Public API entries, owning assertions, acceptance versions and artifact
snapshots move together. Representative compiler/native/transport and independent
executed-recursion controls are being validated before broader gates and packaged
consumer qualification. The full stack milestone is not yet marked complete.

The completed stack checkpoint passed all 1416 analyzer tests, all 711 IR tests,
685 affected native/protocol/artifact controls, and 27 final stack/consumer
controls. Two real packaged strict-profile direct-call/recursive stack-contract
cases passed. Final consumer controls exercise validated cache hits, SARIF model
projection and rejection of forged stack refutations. Two additional IR controls for
oversized block/instruction streams passed with no partial memory bounds.

Phase C has implementations for the declared admitted families documented in the
resource guide: typed allocation costs under the pinned layout, finite owned
normal-completion reachability, and logical source activation storage with checked
recursive ranks. This does not expand admission to arbitrary borrowed graphs,
closure invocation, unbounded lifetime paths or physical JIT/GC peaks; those
boundaries return Unknown. Broader capture/call lifetime composition remains required; the lifetime checkbox
stays open. Unsupported cases are not evidence of a completed proof family.

Quality/compatibility and full repository formatting passed before final fixture
additions; scoped whitespace formatting passed for those additions. Documentation
generation and 724 local links passed. Layout validates 2731 source files,
59 evaluated projects and all 151 pinned common files. The authoritative production
inventory validates all 519 trusted paths, including both stack implementation
files. Final docs and formatting drift checks follow the added validation notes.

Published closure checkpoint 8eb137d67 passed the other 29 checks (six skipped)
but failed changed-trusted-code coverage: 3587/3765 lines, 95.27%, 178 gaps.
This predates stack changes. The 100% floor remains unchanged. Coverage gap
closure and exact-head CI qualification remain mandatory, as do all phase E/F
unchecked requirements. Latest fetched master 8556188d1 adds exception-constructor
and callback-effect semantics; it must be incorporated and its affected workflows
verified before this checkpoint is qualified.

## Stack checkpoint and latest-master integration

Stack implementation and its progress document are committed in bdef19d084.
Latest master 8556188d1 merged cleanly in d5ce7c3ae. On the combined tree, all
2144 frontend tests and 636 selected native resource, exception, callback,
cache/SARIF and artifact controls passed. This directly checks the merged
constructor/callback semantics alongside resource derivation. Repository quality
and negative-fixture checks passed; documentation generation and all 725 local
links passed. Evaluated layout passed for 2740 sources, 59 projects and all
151 unchanged pinned common files. Complete post-merge repository formatting passed.

The earlier stack-specific package cases, full analyzer/IR suites, final stack
boundaries and independent recursive controls remain recorded above. The merge
did not change their resource implementation. Fresh pushed-head CI must verify
the combined state before qualification. Broader capture/call lifetimes, phase E/F implementation and the unchanged
100% coverage gate remain required; this checkpoint does not complete the goal.

## Asymptotic expression foundation

Phase E now has a bounded classifier for the single-input unbounded mathematical
extension of closed resource upper formulas. It handles nonnegative constants,
addition/maxima, polynomial/logarithmic products, constant subtraction/division
and alignment, and logarithms of polynomial growth. It distinguishes zero from
positive constants, validates operator arity/payloads, retains input ordinal/kind
identity across renaming, and propagates cancellation. Multiple independent inputs,
iterated logarithms and malformed/budget-exhausted expressions yield explicit
Unknown results without a partial order.

SP0060 projects the model and upper orders into machine-readable properties and
diagnostic text, including the selected input scale. Numeric proofs remain valid
when the growth classifier abstains. Finite CLR input caps are explicitly separate
from the unbounded formula extension. Upper expressions do not establish lower
source-cost bounds or tightness. All phase E checkboxes therefore remain open
until native evidence, independent lower bounds, checked amortized rules and
validated distributional models are implemented and verified.

Initial focused verification passed 19 expression tests and five analyzer tests.
Additional noncanonical unary-constant controls and full IR/analyzer gates are in
progress. The new classifier is classified in the trusted-source inventory.
No wire/schema/cache change is needed for these advisory diagnostic properties.

Published stack head eec823652 passed canonical, package, documentation and
toolchain-security CI. Coverage attempt one failed before aggregate enforcement
when the native-payload download connection reset during container preparation;
the failed job was rerun on the same exact head. Attempt two reached aggregate
enforcement and failed at 3720/3910 changed trusted lines (95.14%, 190 gaps).
The gap inventory is preserved for targeted tests. The unchanged 100% requirement
remains mandatory.

The classifier's final full IR gate passed 748 tests, including 35 new growth,
constant-folding, malformed-record, cancellation and adversarial budget controls.
The full analyzer gate passed 1422 tests before the packaged activation repair.
Five final visible-message/property controls passed.

The first real package runs exposed a pre-existing SP0060 activation defect:
compiler-owned editorconfig severities were not read from SyntaxTreeOptionsProvider.
Generic analyzer-option tests had masked that path. A separate eight-case
reproduction failed all five enabling severities and passed the three disabled
cases on the unfixed implementation. SP0060 now reads effective per-tree/global
compiler severities before generic-option fallback. The default/off profiles
remain disabled, and the production severity fix is undergoing full analyzer and
real packaged-consumer checks. A transient compiler-flags probe was removed from
the final tests after diagnosis.

Quality/compatibility and scoped C# formatting passed before the final activation
repair. Layout validates 2743 source files, 59 projects and all 151 pinned common
files. Documentation generation and 725 local links passed, and the authoritative
production inventory validates all 520 trusted paths with the classifier included.
Final formatting, documentation notes and the activation workflow are being
checked before committing this foundation. Phase E/F and coverage qualification
remain open.

Final activation verification passed all 1430 analyzer tests, including all eight
compiler-owned severity cases. Both real packaged consumers then passed using
the documented editorconfig opt-in: the quadratic case emits its O(n^2) upper
order and input identity; independent inputs emit Unknown complexity while
retaining numeric upper bounds. Neither consumer reports tightness. The complete
IR gate passed 748 tests, including all 35 classifier controls.

The production activation repair fixes an existing packaged SP0060 gap rather
than relying on test-host severity overrides. The final package tests retain
normal build commands and assertions; temporary verbose compiler probes were
removed. Full quality/compatibility, repository formatting and documentation checks passed.
Native asymptotic evidence, independently justified lower bounds, tightness,
checked amortized accounting, validated average-case models and phase F remain
required. This foundation does not mark any phase E requirement complete.

A requirement audit reopened the phase C lifetime item. RootParameterClosurePlan
still admits only static ordinary factories without type parameters returning one root
anonymous delegate, with no nested closure/local-function bodies. The existing
finite owned graph model is implemented and tested, but the previously named
broader capture/call lifetime composition remains required. The earlier statement
that phase C was fully complete overstated that evidence; admission limits do not
make this remaining requested work optional.

## Native asymptotic upper evidence

Native resource proofs now classify the owned inferred upper expression and
publish structured growth evidence. The record contains the versioned extension
model, canonical upper order, polynomial/logarithmic degrees, zero status and
source input identity. Growth Unknown results keep the numeric proof while
withholding all partial growth data. Numeric Unknown/refuted outcomes do not
publish asymptotic proofs; observed finite lower bounds do not become universal
asymptotic lower bounds.

Protocol 34 and cache 36 reject prior responses. Strict validation requires the
record for non-vacuous native proofs, checks canonical syntax against structured
fields, checks bounded degrees/input shape, and rejects contradictory known/Unknown
records or growth evidence on refutations. Compiler artifact 48, manifest 6,
resource program/evidence 5 and expression model 1 remain unchanged.

The first native build found formatting violations in the expanded classifier
constructor. Canonical whitespace formatting repaired them. The representative
native, cache/SARIF and malformed-record run then passed all 17 tests. Broader
resource/memory/lifetime/protocol regressions, the full IR suite with structured
field assertions, and strict packaged growth/stack workflows are in progress.
This implements native upper evidence; independent lower bounds, tightness,
amortized rules, validated average-case models, broader phase C lifetime
composition and phase F remain required.

The published foundation head 691b72875 has no coverage qualification result:
its coverage workflow was cancelled. The latest numerical gap inventory remains
stack head eec823652 at 3720/3910 changed trusted lines (95.14%, 190 gaps).
The unchanged 100% floor and fresh exact-head CI remain mandatory.

The broader native run passed 819 of 820 cases and found that string-length
inputs were still rejected before growth classification. Array-length input
ownership already passed. The repair uses the existing trusted intrinsic
sequence-length matcher, admits string-length contract sizes, and preserves
source-call/constructor string-size substitution. Fixed repeated-character
strings and literals retain their actual size expressions; bindings are removed
after each call. A focused follow-up passed all 24 native/consumer controls,
including direct/callee/constructor string lengths and repeated calls with
different literal sizes. Added controls require Unknown for fake Length getters,
unsupported aliases and changing non-ranking recursive string arguments.

Four real strict package growth/stack cases passed before adding the new packaged
string-length case. The IR task's first attempt stopped in restore with exit 139;
after confirming its process had terminated, a fresh isolated CLI/cache retry
passed all 748 tests. The final broad worker gate, full analyzer suite and five
strict packaged growth/stack/string workflows are now running. No floor or
coverage exclusion changed.

Final feature verification passed all 828 affected worker tests, all 748 IR tests
and five actual strict package workflows (known growth, independent-input Unknown,
string-length contracts, and both stack metrics). The full analyzer run passed
1429 cases and identified one stale final-compilation protocol assertion expecting
33; it now expects 34 and its isolated check is in progress. Quality/compatibility,
documentation generation and 725 local links passed. Layout validates 2745 source
files, 59 projects and all 151 pinned common files. The authoritative production
inventory validates all 520 trusted paths. Complete repository formatting passed.

Latest fetched master advanced by two commits: 1cdf105b0 adds proven HashSet
ICollection dispatch, and 11a70a2f1 trims verifier references and internalizes
implementation assemblies. They must be merged after this feature checkpoint,
with new implementation types following the revised visibility policy and affected
workflows verified on the combined tree. This checkpoint remains unqualified
until that integration, fresh exact-head CI and unchanged coverage enforcement.

The native feature and progress document are committed in b3be527fb. Latest
master 11a70a2f1 merged in c9f8174ac after resolving the protocol-model visibility
conflict. Native resource types and the internal IR budget exception follow the
new implementation visibility policy. A type-specific CA1064 suppression explains
that the budget exception is an internal signal; it is not a consumer API.

The merged architecture suite passed all 10 cases, including the compiler-neutral
worker closure. Merged compilation initially flagged the internal exception's
design rule; the scoped annotation repaired it. Full merged worker, analyzer and
strict package checks are running. The separately repaired final-compilation
protocol test passed before this merge. Both aggregate and changed-trusted-code
coverage floors remain 100%; upstream declaration-only baseline updates were
preserved, with no resource-source exclusions added.

Merged quality/compatibility and complete repository formatting passed. Layout
validates 2747 sources, 59 projects and all 151 pinned common files; documentation
and 726 local links passed, with 521 trusted paths classified.

The next merged compilation exposed inconsistent partial fixture accessibility:
master internalized WorkerTests and its enum parameter types, while branch-added
resource partial declarations were still public. The 13 affected fixture
declarations now follow the same internal visibility policy; no assertions or
test cases were removed. The worker/resource/HashSet gate is running again on
that adaptation. Strict packages must be rerun after it compiles. The merged
analyzer task stopped in restore with exit 139 before tests; its retry is deferred
until the current worker task finishes to serialize restore-heavy verification.
These integration adaptations are not yet qualified or published.

The repaired combined worker gate passed all 920 selected cases, including the
resource/memory/lifetime/protocol/cache/SARIF families and merged HashSet dispatch.
The serialized analyzer retry passed all 1430 tests. This confirms the visibility
repair and current protocol-seal assertion on the integrated tree. The subsequent
strict package task is running from the same source state. Completion still
requires its result, publication and fresh exact-head CI; all remaining phase C/E/F
requirements and the unchanged coverage floor stay open.

Final merged verification passed all 920 selected worker cases, all 1430 analyzer
tests, all 10 architecture tests and all 11 selected strict package/runtime-closure
cases. Quality/compatibility, full formatting, documentation, 726 local links,
2747-source layout and the 521-path trusted inventory passed. This validates the
internal implementation and partial-fixture adaptations without weakening tests.

The next fetched master commit 101971bba changes only .gitignore for local
development/scratch outputs. It will be incorporated after committing these
verified integration repairs; it changes no production or test code. Fresh
pushed-head CI and unchanged 100% coverage qualification still remain required.

Verified integration repairs are committed in eacbc7951. Ignore-only master
101971bba merged cleanly in be7073b50; no production/test code changed after the
passing combined-tree gates. The next publication includes this complete
checkpoint. Native lower bounds/tightness, broader lifetime composition, amortized
and validated average-case models, phase F and final coverage/CI remain open.

## Resource transport and memory boundary qualification

Targeted gap work now covers allocation envelopes after base64 expansion, malformed
root/integer fields, unknown allocation kinds, resource-program enum/arity/depth
limits, unrelated activation payloads, noncanonical managed-allocation encodings
and maximum arithmetic transport. Allocation-kind admission is centralized in
the existing exhaustive dispatch; removing its redundant early kind check does
not admit any new shape. Invalid storage kinds remain rejected separately.

Detached activation/managed-allocation records must produce no partial native
costs. Memory results must contain either bounds or an Unknown reason, and mixed
storage intervals keep cumulative, auxiliary and retained lower/upper values
distinct. These tests exercise semantic rejection and published projections; they
do not replace the unchanged coverage requirement.

Focused verification passed all 26 allocation-codec cases, all 63 combined
allocation/program transport cases, all 92 native allocation/program cases and
all 23 finite memory-trace cases. Full analyzer and IR suites are running
sequentially. No coverage percentage improvement is claimed until the aggregate
report measures this checkpoint.

For published head 2d58b8593, all four coverage shards passed; its aggregate is
still running. The full phase C/E/F and 100% qualification requirements remain
open. No baseline floor or resource-source exclusion was changed.

Published native-growth head 2d58b8593 passed all four coverage shards and reached
aggregate enforcement at 3926/4119 changed trusted lines (95.31%, 193 gaps), failing
the unchanged 100% floor. Its report and deduplicated gap inventory are preserved.
The largest remaining groups are memory extraction/replay, call lowering and
violation replay. This result predates the current transport/ownership/memory
boundary additions; those additions have no measured aggregate claim yet.

The full analyzer suite for the boundary checkpoint passed all 1450 tests.
The native allocation/program suite passed 92 tests and the finite memory-trace
suite passed 23. Quality/compatibility, generated documentation and 726 local
links passed. The full IR and repository formatting checks are still running.
All remaining feature scope stays unchanged.

Final boundary qualification passed all 1450 analyzer tests and all 752 IR tests,
plus the 92 native allocation/program and 23 finite memory-trace cases recorded
above. Repository quality/API compatibility, full formatting, documentation
generation and 726 local links passed. No structural project change was made.
The checkpoint is ready for publication and fresh exact-head coverage measurement;
it does not claim the 193-gap inventory is closed or any unfinished feature done.
