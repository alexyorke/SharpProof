# Bug backlog

## Current audit and evidence

Updated on 2026-09-24. The findings below were audited against baseline `1d96799e6` (`Fix contract semantics, worker ownership, and evidence recovery`). In this working tree, the compound-assignment false-proof, managed exception-region false-proof, and completion-analysis recursion-budget findings have been fixed and verified, so they are removed from the active backlog. Proposed fixes for the remaining findings have not been implemented. The active backlog contains **69 findings**: 0 P0, 9 P1, 22 P2, and 38 P3. Former candidate C1 is now B6; no separate candidate remains in this audit. B18 onward come from a fifth pass on 2026-09-22 that ran a real analyzer built from an unchanged `git archive` of HEAD with SDK 9.0.318 outside the container (the pinned 9.0.316 SDK was not installed).
The fifth pass also ran generated fuzz campaigns with execution-checked ground truth, stack-exhaustion and timing runs (B47, B48, B50), and end-to-end false-proof confirmations through the collector and in-process worker. The next paragraph describes the evidence of the earlier waves only.
Evidence is scoped per finding. Probes on unchanged sources observed fuzz
scheduling, canonical hashing, interval precision, frontend IR, module-reference
validation, substitution ownership, cache maintenance, effect summaries, and
analyzer diagnostics. A bounded completion-facts probe traversed 600 helpers;
no stack-exhaustion run was attempted. Isolated pilot-validator probes used
result-file doubles; receipt probes exercised an actual admission switch and a
simulated changing file view. A framework-source helper was tested with empty
and prepared package caches. No full fuzz campaign, physical file race, native
workflow, release CI run, or end-to-end qualification was executed. Worker
eligibility and false-proof consequences of frontend and effects findings
remain untested. The earlier Summaries suite passed 15/15 tests; it was not
rerun in wave four and does not reproduce or disprove these findings. Earlier
coverage from the 2026-09-16 through 2026-09-18 audits is preserved below.

Confidence definitions:

- **Confirmed:** a targeted executable check and controls observed the stated
  boundary defect. This does not confirm downstream consequences beyond that
  check. Earlier analyzer probes used `CompilationWithAnalyzers`; historical
  worker and SMT probes used scratch builds and the bundled backend.
- **High:** the defect follows directly from source inspection, optionally
  strengthened by a partial executable probe; unexecuted consequences are
  identified explicitly.
- **Medium:** a plausible defect whose reachability or downstream behavior still
  requires a targeted check.
- **Low:** a latent hazard that may be unreachable through current callers.

Priority definitions:

- **P0 - Critical:** Can make a false proof or refutation trusted, or break verification/publication integrity.
- **P1 - High:** Can produce a wrong supported-workflow result, hide a mandatory check, or broadly crash, hang, or lose authoritative results.
- **P2 - Medium:** Usually fails closed or causes false positives, incomplete diagnostics, bounded reliability problems, or narrower correctness errors.
- **P3 - Low:** Minor precision, canonicalization, test, documentation, or low-impact operational issue.

### Current coverage ledger

These are the actual five review partitions. Coverage is bounded source review
plus the checks described below, not an exhaustive correctness claim. Proposed
regressions and unexecuted downstream paths remain open.

| Area | Evidence in this audit | Finding or remaining gap |
| --- | --- | --- |
| Contracts, Frontend, ContractForGenerator, plus Analyzer/Core, Meta.Analyzers, and Attributes in wave four | Earlier exact frontend IR probes; real analyzer probes now compare direct/aliased purity attributes and cancellation-filter mutation with controls | B6 frontend, B14 analyzer, and B15 meta-analyzer boundaries observed; wider alias cases and worker consequences remain open |
| Effects, Dataflow, and shared throw facts | Bounded facts traversal reached 600 helpers; public summaries omitted managed receiver writes despite concrete mutation, with unmanaged-copy control; earlier interval probes retained | B1 bypass observed without a crash; B5 and B10 boundaries observed; EnforcePure and worker consequences untested |
| IR, SMT, Summaries, and Verify | Earlier B3/B11 probes and Summaries 15/15; wave four reviewed summary ownership/signatures, substitution, model validation/replay, cancellation, and disposal without new probes or repeated suites | No distinct new finding: foreign actuals rejected by replacement validation, null models rejected before replay, extra mutable views duplicate B11; downstream gaps remain |
| Worker, Protocol, CompilerArtifact, CompilerCollector, and Specs | Earlier B4 validator controls; real cache/filesystem reads now compare absent, malformed, oversized, and held-lock misses | B4 and B7 boundaries observed; no valid-cache-hit control or complete worker request; B3 custom-table and other downstream consequences source-traced |
| Host, BuildTasks, Launcher, Gates, scripts, Tools, and .github | Earlier B2/B8/B9/B12/B13 probes; reviewed workflow receipt producers/dependencies and exercised actual framework-source helper with empty/prepared caches | B16 missing review handoff source-traced; B17 helper boundary observed; no release CI, native workflow, or end-to-end qualification run |

B3 combines the hash-writer and specification-admission evidence into one
deduplicated boundary finding; it is not counted twice. All five fourth-wave
reviews are finished. Unexecuted downstream paths and proposed regressions
remain open for later passes.

### Fifth-pass coverage (2026-09-22)

This pass built the analyzer, meta analyzer, compiler collector, and worker
from an unchanged `git archive` of HEAD (SDK 9.0.318, container guard
satisfied locally) and ran real probes. It produced B18-B72 and added evidence
to B6, B15, and B27. Areas probed without a new finding:

- **Analyzer effects:** 15 implicit-call purity escapes (static constructors,
  user conversions, `ToString`, `Deconstruct`, record copy constructors,
  `Dispose`, `Interlocked`, spans, `ref` locals, `Array.Fill`), 17 aliasing
  escapes, 23 `[DoesNotThrow]` exception sources, 16 `[ZeroAllocations]`
  shapes, 25 ambient BCL capability calls, unsafe/pointer helpers, trusted
  `[EffectContract]` on virtual/interface/abstract dispatch, and
  `[AllowedExceptions]` subtype and malformed-argument handling. All were
  reported or conservatively unknown.
- **Analyzer contracts:** outer `[SharpProofSuppress]` covers local functions
  and lambdas; `SP0027` handled constants, `unchecked` wraparound, string
  length, and negation correctly and produced no false positive on 20
  wraparound/shift/sign cases; analyzer configuration fails closed with
  `SP0025`.
- **Worker:** implementation-IL `div`, exception regions, and admitted types
  (int32/int64/bool only); `ulong` abstention and narrow-domain widening;
  per-operation `checked`/`unchecked` (including `unchecked` blocks in a
  checked project); non-short-circuit `|`/`&`/`^` in postconditions;
  `Contract.Assume(false)`/`Requires(false)` vacuity labels; closed
  `[InRange]`/`[Positive]` attributes; patterns, switches, and `ref` calls
  abstain.
- **Infrastructure:** `IrSmtBackend` encoding and lifecycle, `ProofKernel`,
  relational summary instantiation, dataflow domains, launcher publication
  rollback, worker CLI, `AtomicFile`, `BoundedReadStream`, `LinuxPathIdentity`
  canonicalization and mount parsing, `LinuxWorkerProcess` termination,
  `VerifierProcessSupervisor`, `RunVerifier`/targets exit-code handling,
  published-result invalidation and validation, changed-test selection,
  corpus gate completeness, fuzz-result validation, dependency audit,
  `Publish-SharpProofRelease.ps1`, and the CI/coverage/security/nightly
  workflows (no expression injection found).
- **Fuzz campaigns:** the four historical effect differential fuzzers
  (throw, pure, and alloc modes; 180 new seeds) found no false negatives or
  analyzer crashes; the source-summary, widened-int, and IL-summary worker
  fuzzers (60 new seeds) found no false proofs (the infrastructure failures they
  exposed are B27); a new `SP0027` differential fuzzer ran 9,300 constant-driven
  call sites with checked/unchecked wraparound, shifts, `%`, `/`, and
  conversions, and all 1,034 reports were genuine violations; 20,000
  null/type mutations of a real worker response never escaped
  `WorkerProtocolJson.Validate` with an unexpected exception type; 30,000
  compiler-manifest mutations (raw and resealed) found only B25 and B26.
- **Determinism:** compiler manifests were byte-identical across concurrent and
  sequential collector runs; analyzer diagnostic IDs and locations were
  identical across 23 concurrent runs (message text differs, B29); worker
  outcomes were identical across 1- and 4-lane runs except where B27
  incompleteness intervenes.
- **Other checks:** `Contract.Requires` placement, `[ContractFor]` overload
  matching, closed-attribute typing, annotated code in generated files, unsafe
  and pointer code, `IrInterpreter` versus SMT division semantics, worker
  effect replay on infeasible paths, publication reset ownership, and the
  performance gate's statistics.
- **Later probes without further findings:** SP0027 argument mapping
  (named, default, `params`, extension, indexer, operator, `init`, and
  constructor-chaining calls) and 20 documented replay contexts;
  `[DoesNotThrow]` over decimal arithmetic, catch filters, `finally` and
  rethrow; purity over collection expressions, custom interpolation
  handlers, index/range, `System.Threading.Lock`, `params` spans, `await`,
  primary-constructor captures and switch/pattern effects; `[Conditional]`
  elision; the call-site precondition interpreter on nested loops; the
  verification cache key; `CorpusFileTransaction` recovery; and the default
  API-spec catalog facts. Generated fuzz campaigns: with the compound-assignment shape
  excluded, differential SP0027 (6,000 + 6,000 methods), `[DoesNotThrow]`
  (12,000), `[EnforcePure]` (3,000), array-aliasing `[EnforcePure]` (2,000,
  384 of 808 pure methods verified silently) and nullness (2,000 + 2,000) found no
  unsound silence and no false SP0027 beyond the compound-assignment case and B59. The generators'
  C# evaluator was checked against real execution with zero mismatches.
  Power is limited, though: about 85% of non-throwing methods are
  conservatively flagged, so each 1,000 methods exercise roughly 80-100
  silent verdicts.
  A `checked`-context `[DoesNotThrow]` campaign (4,000) had no power at
  all: every non-throwing method was flagged. Generated values quickly leave
  the ranges the interval domain tracks (see B60).
  Worker postcondition fuzzing with execution-derived ground truth (checked
  `long` code with ternaries, `if`/`else`, reassignment, bounded
  `Requires` parameters and acyclic private helper callees; 8,000 claims)
  produced 2,354 `Proven` and 2,446 `Refuted` verdicts, all correct, with
  no false proof or false refutation. Increments, compound assignments and
  embedded assignments are outside the worker subset (`UnsupportedBody`).
  Twelve concurrent analyzer runs over 600 generated methods produced
  identical diagnostics apart from the `vN`/`#tN` identifiers already
  described in B29. Handler reachability (12 reachable-`catch` purity
  shapes), struct copy versus `ref` purity, array-cardinality index proofs,
  unsigned wraparound indices, and implicit-allocation shapes under
  `[ZeroAllocations]` were all sound. Values passed through `out`/`ref`
  arguments (including `int.TryParse`, `Math.DivRem`, `Interlocked.Exchange`)
  make selected methods abstain, and code after a caught throw is treated as
  reachable. A `try`/`catch`/`finally` statement mode added to the
  `[DoesNotThrow]` fuzzer (2,950 methods) found nothing beyond B61, which
  was found by the manual probes. A later combined campaign (embedded side
  effects plus `try` statements, 4,750 methods, 2,065 throwing) also had no
  unsound silence. `dynamic`, reflective (`MethodInfo.Invoke`) and delegate
  dispatch in callees made `[EnforcePure]` callers unprovable, as they
  should.
  The compiler collector emitted a manifest without SP0049 for all 119
  compiling probe files (every shape used in this audit, except the scaling
  inputs of B47). On 272 token-mutated sources it failed only for files that
  already had compile errors (8 extra SP0049 "artifact is invalid"
  errors on builds that fail anyway).
  The in-process worker completed every one of the 100 resulting non-empty
  manifests (`run=Complete`, no protocol errors).
  Further clean probes: project-wide `checked` arithmetic against explicit
  `unchecked` expressions and blocks; code after definitely-throwing `try`
  bodies stays reachable; callees that swallow their own throws
  (`catch`, `catch when`, nested `finally`) are treated as returning;
  static field initializers of `beforefieldinit` types contribute their
  effects, allocations and exceptions; per-clause claim attribution with
  mixed `Ensures`/return attributes; `[Positive]`/`[InRange]` on `uint`,
  `char` and `byte`; `Contract.Old` versus post-state parameters in the
  worker; unchecked `long` arithmetic in the worker (abstains);
  `[ContractFor]` preconditions at sealed-class call sites; and generic
  calls in effect claims (abstain).
  The launcher's source rebinding (`CompilerSourceRebinding.Validate`, called
  by reflection on real manifests) accepted all 112 corpus manifests and four
  `#line` shapes (plain remap, `#line hidden`, the C# 10 span directive with
  a character offset, and a remap inside the contract prologue and before
  a return attribute). The worker proved every claim in those `#line`
  manifests. Effect attributes on properties check every accessor (`get`,
  `set`, `init`). The worker produced no false effect refutations for
  allocations or throws in constant-false, infeasible or unreachable code,
  while a real allocation was `Refuted`.
  Edge refinement with unknown inputs was fuzzed separately:
  `[DoesNotThrow] M(int x)` with conditions over wrapping transforms of `x`
  (`x * k`, shifts, casts, `%`, `/`, masks, `&&`/`||`) guarding an
  indexed read, with ground truth from executing 3,600 sampled `x` values
  per method (5,600 methods). There was no unsound silence, though power is
  modest (109 of 895 safe methods were proven).
  Further manual soundness probes were clean: `goto`/`switch`/`goto case`
  value flow; `lock`/`using` bodies (implicit `finally`); `Span`/`stackalloc`
  indexing (abstains); nullness refinement through `IsNullOrEmpty`, `==`,
  `?.`, `is`; callee effect summaries imported under unmet or unknown
  `Requires`/`[InRange]`; elided `Debug.Assert` arguments with side effects;
  and modular trust (callers of callees whose `[DoesNotThrow]`,
  `[EffectContract]` or `[AllowedExceptions]` claims are false are never
  proven by the worker).
  On 1,000 generated `[DoesNotThrow]` methods, analyzer silence and worker
  `Proven` agreed exactly (84 both, none on only one side), so the IDE and
  build paths share one verdict. The confirmed false proofs affected
  both.

## P1 - High

### B8. Pilot validation accepts strict refutations and failed responses

**Confidence: Confirmed at the isolated validator boundary.**

- **Location:** `scripts/Test-SharpProofPilotReport.ps1:143` and `:160-179`;
  generator requirements in `scripts/Test-SharpProofPilots.ps1:353-362`;
  receipt consumer `scripts/Write-SharpProofQualificationReceipt.ps1:94-96`.
- **Defect:** the validator matches reported claims but does not enforce the
  actual response's `runStatus` or require every strict-pilot outcome to be
  `Proven`. The generator requires both conditions; the qualification receipt
  consumer trusts validator success.
- **Observed boundary:** an isolated probe of the actual validator, catalog,
  and projects with in-memory result-file doubles returned
  `baselineAccepted=true`, `strictRefutedAccepted=true`, and
  `failedResponseAccepted=true`. These were synthetic response inputs, not an
  actual failed worker run or an issued qualification receipt.
- **Proposed fix:** independently validate actual response completion status
  and strict-pilot outcomes before accepting a report or qualification input.
- **Proposed regression:** reject strict `Refuted` and `Unknown` outcomes and
  `Failed` responses even when reported claims match. Retain a `Complete`
  response with all strict claims `Proven` as a positive control, then exercise
  the receipt path with real publication files.

### B10. Managed struct receiver copies lose observable reachable writes

**Confidence: Confirmed public effect-summary omission and concrete mutation.**

- **Location:** `SharpProof.Effects/OperationEffectScanner.cs:806-813` and
  `:830-850`; `SharpProof.Effects/EffectSummaryOperations.cs:178` and `:199`;
  `SharpProof.Effects/ConversionOwnershipClassifier.cs:92-113` and `:129-134`.
- **Defect:** defensive copies, including `in` parameters and readonly fields,
  map `writeReceiver` to `Empty`, dropping declared receiver writes. By-value
  struct receivers likewise map to `Empty`. Managed copies still share objects
  reachable through reference fields; argument ownership already distinguishes
  those fields at classifier lines 92-113.
- **Observed boundary:** a fresh unchanged-HEAD Effects.Test build in the
  canonical container had zero warnings/errors. A stdin/in-memory fixture in
  an isolated `AssemblyLoadContext` used a trusted external `ManagedValue`
  struct with `int[] Items` and an accurate, complete, precondition-free
  `ReadsReceiverState | WritesReceiverState` contract. `Mutate` writes `1` to
  a nonempty array's first element. The public summaries and concrete witnesses
  were:

  | Case | Summary | Concrete result |
  | --- | --- | --- |
  | Managed `in` receiver | `Complete=true`, `Effects=ReadsArgumentState`, `WritesEmpty=true` | Shared array element became `1` |
  | Managed by-value receiver | `Complete=true`, `Effects=None`, `WritesEmpty=true` | Shared array element became `1` |
  | Unmanaged scalar `in` copy | `Complete=true`, `Effects=None`, `WritesEmpty=true` | Original scalar remained `0` |

  The probe exited successfully. `EnforcePure` analyzer diagnostics and worker
  false-proof paths remain untested; the scalar-copy optimization is valid.
- **Proposed fix:** retain reachable writes for managed receiver copies, or
  conservatively return unknown; preserve unmanaged scalar-copy optimization.
- **Proposed regression:** retain the trusted referenced-assembly fixture and
  all three witnesses, requiring managed writes or abstention. Follow through
  `EnforcePure` analysis separately to establish the diagnostic consequence.

### B12. Qualification admission coerces invalid Boolean and integer evidence

**Confidence: Confirmed admission-switch boundary; no receipt issued.**

- **Location:** `scripts/Write-SharpProofQualificationReceipt.ps1:58-86`,
  particularly `:78` and `:85-86`.
- **Defect:** permissive Boolean and integer casts accept evidence whose types
  do not satisfy the admission schema, potentially turning failure into success.
- **Observed boundary:** a probe of the actual AST switch accepted coverage
  Boolean `true` and rejected Boolean `false`, but accepted string `"false"`.
  Mutation counts `2` and `1` were rejected, while `1.4` and `1.1` were accepted
  after both became `[int]1`. The full receipt writer was not exercised.
- **Proposed fix:** require exact Boolean and integer token/types, validate
  integer ranges, and reject strings, fractions, and nulls before evaluating
  evidence acceptance.
- **Proposed regression:** reject malformed types and out-of-range counts;
  preserve actual Boolean and integral-count positive/negative controls and
  check that invalid evidence cannot issue a receipt.

### B13. Qualification receipts can bind bytes different from validated evidence

**Confidence: Confirmed simulated admission boundary; physical race untested.**

- **Location:** `scripts/Write-SharpProofQualificationReceipt.ps1:31-32`,
  `:58-99`, and `:115-123`; consumer
  `scripts/Invoke-SharpProofReleaseContainer.ps1:209-224`.
- **Defect:** the writer reads/parses evidence, validates it, and separately
  reads its length/hash. A replacement between phases can bind a passing
  receipt to different bytes than those accepted by validation.
- **Observed boundary:** changing an in-memory file view from passed to failed
  between phases yielded `Validated=true`, `ReceiptStatus=passed`,
  `CurrentEvidenceStatus=failed`, `ReceiptBindsCurrentFailedBytes=true`, and
  `ReceiptBindsValidatedBytes=false`. This simulated the admission boundary;
  no physical filesystem race or end-to-end receipt issuance was executed.
- **Source-traced consequence:** the release consumer checks receipt/hash
  agreement without rechecking the evidence's acceptance status. That downstream
  path was not exercised by the simulation.
- **Proposed fix:** parse and hash one immutable byte snapshot; detect later
  replacement where the publication contract requires the file to remain bound.
- **Proposed regression:** inject replacement between validation and binding;
  require rejection or a receipt bound only to the validated bytes. Retain
  unchanged-file controls and exercise the release consumer separately.

### B14. Attribute aliases silently disable advisory analysis

**Confidence: Confirmed analyzer boundary for a local purity-attribute alias.**

- **Location:** `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs:95-105`,
  `:222-226`, and `:586-603`.
- **Defect:** spelling-only `IsSharpProofAttributeCandidate` does not resolve
  aliases, so an otherwise eligible compilation may never activate analysis.
- **Observed boundary:** a fresh unchanged-HEAD Analyzer.Test build completed
  with zero warnings/errors. A real analyzer stdin probe in advisory/effects
  configuration used a class static field and `static void M() { state++; }`.
  Direct `[EnforcePure]` emitted `SP0002`; replacing it with `[Pure]` and
  `using Pure = SharpProof.Attributes.EnforcePureAttribute;` emitted no
  diagnostics when no other syntax activated analysis. The probe exited
  successfully. Only this local alias was executed.
- **Proposed fix:** recognize attributes semantically or conservatively activate
  analysis for aliases that can name SharpProof attributes.
- **Proposed regression:** cover local/global aliases, effects and closed-contract
  attributes, direct spelling, and unrelated-alias controls. The additional
  alias and attribute forms are proposed coverage, not observed failures.

### B16. Release qualification has no human pilot-review handoff

**Confidence: High. Evidence: workflow and script paths source-traced.**

- **Location:** `.github/workflows/package-consumers.yml:202-218`;
  `scripts/Invoke-SharpProofContainer.ps1:607-625`;
  `scripts/Test-SharpProofPilots.ps1:367`;
  `scripts/Invoke-SharpProofReleaseContainer.ps1:202-207`.
- **Defect:** the supported annotated release-tag push runs `tooling pilots`
  then release qualification. Pilot generation writes `Unreviewed`; the
  separate `pilot-review` command produces the receipt required at
  the release-evidence path qualification-receipts/pilots.json. The workflow has no handoff that supplies
  a human review and this receipt before qualification, assuming prior gates pass.
- **Source-traced boundary:** reviewed dependencies, uploads/downloads, caches,
  tracked files, inputs, and docs provide no receipt producer or review ledger.
  The package job supplies only `artifacts/container-packages`; container
  verification supplies package-consumer report/receipt; portable jobs supply
  family receipts. Publishing-environment approval occurs after qualification.
  No CI workflow was executed.
- **Proposed fix:** add an explicit human-review artifact or ledger handoff
  bound to the exact packages and pilot results before resuming qualification.
  Preserve rejection of unreviewed evidence; do not automatically approve it.
- **Proposed regression:** verify each required receipt has a reachable producer
  and dependency, then cover reviewed and unreviewed qualification paths.

### B27. Solver incompleteness on nonlinear arithmetic fails the whole verifier run

**Confidence: Confirmed end to end in the worker; launcher and MSBuild
consequences source-traced.**

- **Location:** `SharpProof.Smt/IrSmtBackend.cs:276-292` (`ClassifyUnknown`,
  whose default branch returns `InfrastructureFailure`);
  `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:280-300` (`Classify`,
  where any claim-level infrastructure failure makes the run `Failed`);
  `SharpProof.Worker.Launcher/Program.cs:531-538` (a non-complete,
  non-timeout run returns exit code 3); and
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:237-238`
  (a nonzero exit without a structured error is a build error).
- **Defect:** when Z3 answers `unknown` because nonlinear integer arithmetic is
  incomplete, `ReasonUnknown` is `(incomplete (theory arithmetic))`.
  `ClassifyUnknown` recognizes only timeout, resource, rlimit, memory, and
  `canceled` reasons, so this ordinary, expected solver outcome becomes
  `BackendFailureReason.InfrastructureFailure`, then
  `WorkerClaimReason.InfrastructureFailure`. `Classify` then marks the whole
  run `Failed/InfrastructureFailure`, although every other claim completed.
- **Observed boundary:** 20 seeds of the IL-summary differential fuzzer
  (random int32 library helpers with multiplication, 40 contract-bearing
  clients each; implementation-IL summaries lower int32 wraparound through
  `%`, so products become nonlinear) were run through the real collector and
  the in-process worker, both built from unchanged HEAD sources. 7 seeds
  (7000, 7002, 7004, 7006, 7007, 7017, 7018) ended `run=Failed
  failure=InfrastructureFailure`, caused by one or two claims each. The
  source-summary fuzzers hit the same outcome on 1 of 20 seeds each. First-chance
  exception logging showed no exception on these paths. A scratch-only
  logging line in `ClassifyUnknown` (outside the repository) printed
  `Z3 UNKNOWN REASON: '(incomplete (theory arithmetic))'` for the failing
  claim of seed 7000 (the other unknowns were `canceled`, correctly classified
  as resource limits). Re-solving the same query on a fresh context returned
  `SATISFIABLE`, so the outcome depends on solver state and is not
  deterministic across queries. Through the production multi-lane path (the
  internal `SharpProofWorker(Func<ISmtBackend>)` constructor that
  `SharpProofWorker.Create` uses, minus its container check), the same seed
  7002 manifest ended `Failed/InfrastructureFailure` in three runs and
  `Complete` in one 4-lane run, with no timeouts in any of them. Seed 7000's
  count of claims with reason `None` also varied between 2 and 3 across
  identical runs. The build verdict for unchanged inputs therefore depends on
  lane scheduling.
- **Impact:** a single hard nonlinear obligation anywhere in a project turns
  an otherwise complete, correct verification into a failed run. The launcher
  returns exit code 3, and the MSBuild target reports "SharpProof verifier
  failed with exit code 3" as a build error under every policy, including
  `advisory`. Published results for all other claims are marked as belonging to
  a failed run, and the build error blames infrastructure rather than proof
  difficulty. No false proof is involved.
- **Proposed fix:** classify solver-reported incompleteness as a semantic
  `Unknown`, not an infrastructure fault. For example, add
  `BackendFailureReason.Incomplete` (mapped to a new
  `AbstentionReason`/`WorkerClaimReason.SolverIncomplete` that
  `SummarizeClaimReasons` does not count as a run failure), and match
  reasons containing `incomplete` in `ClassifyUnknown`. Keep
  `InfrastructureFailure` for exceptions and unexpected native states.
  Consider also retrying once on a fresh lane before abstaining, because the
  same query was satisfiable on a fresh context.
- **Proposed regression:** unit-test `ClassifyUnknown` with
  `(incomplete (theory arithmetic))` and an unknown/empty reason; add a worker
  test whose single nonlinear claim is `Unknown` while the run stays
  `Complete`; retain the `canceled` → `ResourceLimit` and exception →
  `InfrastructureFailure` controls.

### B47. Normal-completion analysis crashes the compiler on deep call chains and is exponential on shared or recursive call graphs

**Confidence: Confirmed by probing the analyzer and profiling it.**

- **Location:** `SharpProof.Effects/ManagedAbstractFlow.cs:2610-2705`
  (`DefiniteOperationFacts.MethodCanCompleteNormally`), reached through
  `:3164-3187` (`InvocationMayCompleteNormally`) and
  `SharpProof.Effects/InvocationEmissionPolicy.cs` (`IsElided`). The
  thread-static active-method sets at `:2424-2436` are the only guard. Results
  are deliberately not cached when they are "cycle affected" (the comment
  before `_methodCompletionCache.TryAdd` at `:2697-2704`).
  The sibling definite query `CompletesNormally(IMethodSymbol)` at
  `:2571-2600`, used by SP0027 prefix replay, has no cache at all.
- **Defect:** there are two problems.
  1. The query recurses into each source callee on the native stack, with
     no depth budget. That differs from the expression walker in the same
     file, which caps recursion at `MaximumWalkDepth = 256` precisely because
     "StackOverflowException is uncatchable and would take the compiler host
     down with it".
  2. Inside a strongly connected component, every result depends on the
     active-method guard, so nothing is cached. Each invocation site
     re-explores its callee's whole subgraph, which enumerates all simple
     call paths through the component: exponential time.
  3. `CompletesNormally(IMethodSymbol)` re-walks each callee's body on every
     query even in an acyclic graph, so a callee reached along k distinct
     call paths is analyzed k times.
- **Observed boundary:** with the real analyzer (profile `advisory`,
  features `effects`) and one `[EnforcePure]` method at the top:
  - A straight chain `C_i(x) => C_{i-1}(x)` of 180 methods completes. At 190
    or more methods, the process dies with `Stack overflow` (exit
    `0xC00000FD`) in every repetition. The stack shows 184 nested
    `MethodCanCompleteNormally` → `MayCompleteNormally` →
    `SequenceMayCompleteNormally` → `InvocationMayCompleteNormally` cycles.
    Frames are larger when each method has a branch and a second call
    (`int y = C_{i-1}(x); if (x > i) y = C_{i-1}(y); return y;`). That
    variant completes at 105 methods and overflows at 110.
  - A parser-like component where `Level_i(x)` calls `Level_{i+1}` twice
    and the last level recurses into `Level0` took 2.0 s, 3.4 s, 5.7 s and
    20.3 s for 8, 10, 12 and 14 levels.
  - A denser component (each `K_i` calling every later `K_j`, with a back
    edge) took 2.2 s at 8 methods, 59.5 s at 12, and more than 300 s at 16.
  - An *acyclic* helper stack (`C_i(x) { int a = C_{i-1}(x); int b = C_{i-1}(a); return b; }`)
    followed by a contracted call (`C_n(1); F(0);` with `F` requiring
    `v > 0`, features `contracts`) took 1.3 s, 2.1 s, 6.2 s, 40.4 s and
    307.8 s for 12, 18, 21, 24 and 27 levels. The expected `SP0027` was
    reported each time.
  - A `dotnet-trace` CPU sample of the 12-method case attributes 99.1% of
    the hot thread's time to `MethodCanCompleteNormally`, entered from
    `InvocationEmissionPolicy.IsElided`.
  - Without a selected method, the advisory fast path skips analysis. The
    unannotated chain of 1,000 was unaffected.
  - The final-compilation collector (`SharpProof.CompilerCollector`, run
    in-process on the same inputs) shows the same behavior: the 200-method
    chain dies with `Stack overflow`, and the 12- and 14-level parser shapes
    take 5.4 s and 16.6 s. A strict verification build therefore fails in
    the collector too.
- **Impact:** code with a selected method that reaches a long helper chain,
  or a recursive-descent parser, evaluator, or visitor, can hang compilation
  for minutes or hours, or crash `csc`/`VBCSCompiler`/the IDE analyzer host
  with an uncatchable stack overflow. The overflow threshold depends on the
  host's thread stack size (about 185 methods on a 1 MB .NET thread-pool
  stack). The strict profile never takes the advisory fast path, so any
  such code in a strict project is exposed as soon as a method is analyzed.
- **Proposed fix:** compute "may complete normally" per strongly connected
  component instead of per DFS path. Build the source call graph for the
  queried method (an explicit worklist, not recursion), run Tarjan's
  algorithm, and solve each component with a monotone fixpoint that starts
  every member at `false` (cannot complete) and iterates until stable. Then
  cache every member's final value; `true` remains the conservative answer.
  Memoize `CompletesNormally(IMethodSymbol)` per method in the same way. A
  `false` computed without any active-cycle dependency is definitive, and
  inside a cycle `false` is already the conservative answer for that query.
  Until that lands, cache cycle-affected results as well. This is sound for
  a may-complete query: a result computed while an active method is assumed
  to complete (`true`) is an over-approximation, and a `false` computed
  under that more permissive assumption is still `false`. Also add a
  method-nesting budget (for example 64) and
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()`, both returning the
  conservative `true` without caching. There is precedent: `ConversionOwnershipClassifier.cs:691`
  and `EffectAnalysisSession.cs:590` already cap their call-graph recursion at
  `EffectCallGraph.MaximumCallGraphDepth`. That cap is 512, deeper than the
  observed overflow depth for these frames, so the new budget must be
  smaller, or the walk must not use the native stack.
- **Proposed regression:** analyzer tests with a 1,000-method chain, a
  30-level acyclic doubly-calling helper stack before a contracted call, and a
  20-level doubly-recursive parser under an `[EnforcePure]` root, each
  finishing within a small time budget, plus a precision control proving
  that a directly self-recursive method with no base case is still
  classified as nonreturning.

### B67. `[SharpProofSuppress]` silently removes claims from strict verification, without an assumption record

**Confidence: Confirmed by running the collector and worker.**

- **Location:** `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs:51-61`
  (`BuildTarget` returns `null`, so the callable and all its claims are
  omitted from the manifest, whenever
  `SharpProofControlAttributePolicy.ValidateAndShouldSuppress` is true for
  the method, its containing types, or the assembly). The contract is stated
  in `docs/public-api.md` ("`SharpProofSuppressAttribute` changes diagnostic
  reporting only"). The assumption reporting that exists for
  `[SharpProofTrusted]` is in `SharpProof.Worker.Launcher/Program.cs:584-607`
  (`ReportAssumptions`, SP0048).
- **Defect:** suppression is documented as a reporting control, but it
  actually deletes the suppressed callables' postcondition and effect claims
  from the compiler manifest. The worker never sees them, so the launcher's
  `require-proven` policy and `SharpProofAssumptionPolicy=error` (both strict
  defaults) have nothing to reject. No SP0047, SP0048 or summary entry
  records that claims were skipped. `[SharpProofTrusted]`, the explicit
  trust mechanism, is audited as an assumption, while suppression bypasses
  it.
- **Observed boundary:** with the collector and in-process worker (checked
  compilation):
  - `[assembly: SharpProofSuppress("whole assembly")]` plus
    `long Wrong(long x) { Contract.Ensures(Contract.Result<long>() == x + 1L); return x; }`
    produced a manifest with **0 claims** and `run=Complete failure=None`.
  - With `[SharpProofSuppress("type")]` on the containing class, that
    class's claim was omitted, while the identical unsuppressed `S3b.Wrong`
    was `Refuted` with a counterexample.
  - A method carrying both `[SharpProofSuppress]` and a false
    `[DoesNotThrow]` produced no worker claim and no analyzer diagnostic.
    Callers of suppressed methods were still correctly not proven.
- **Impact:** a single assembly-level attribute turns a strict,
  `require-proven` build into one that verifies nothing and still succeeds,
  and the SARIF and summary show a clean, complete run. Teams that rely on
  strict mode as a gate can lose it by accident (a suppression added to
  quiet an IDE message) or by design, with no audit trail. This hides a
  mandatory check (P1).
- **Proposed fix:** keep suppressed callables and their claims in the
  manifest, and apply suppression only in presentation (the analyzer's
  reporting and the launcher's diagnostic severity), as documented. If
  skipping verification is intended, make it explicit: emit the callable
  with `Coverage=Incomplete` and a new reason `Suppressed`, report it
  through SP0047/SP0048 with the suppression reason, count it in the run
  summary, and have `require-proven` and `AssumptionPolicy=error` fail on
  it just as they do for `[SharpProofTrusted]` evidence. Update
  `docs/public-api.md` to match.
- **Proposed regression:** worker/launcher tests where assembly-, type- and
  method-level suppression each covers a false `Ensures` and a false
  `[DoesNotThrow]`. Under the strict defaults the launcher must exit
  non-zero and name the suppressed claims; under `advisory` they appear as
  informational suppressed claims, never as absent.

## P2 - Medium

### B2. A rotating-seed collision can omit most retained fuzz cases

**Confidence: Confirmed scheduling defect; full campaign not executed.**

- **Location:** `scripts/Invoke-SharpProofFuzzCampaign.ps1:73-82`, `:189-197`,
  and `:212-215`.
- **Defect:** a retained seed equal to the rotating seed is excluded regardless
  of whether the rotating run covers its retained case budget. The summary
  still records the configured retained budget.
- **Observed boundary:** a canonical Docker stdin probe executing the actual
  scheduling AST assignment with retained seed `23063`, `-RotatingSeed 23063`,
  `-RotatingCases 1`, and `-RetainedCases 1000` scheduled one case for that seed.
  Equal budgets scheduled 1,000; a larger rotating budget scheduled 2,000;
  a different rotating seed preserved the retained run. No full campaign ran.
- **Proposed fix:** schedule the shared seed with the larger budget, or execute
  the missing suffix without duplicating the prefix. Compute campaign limits,
  requested coverage, and summary fields from the effective schedule.
- **Proposed regression:** cover rotating budgets below, equal to, and above
  the retained budget. Place a failure beyond the rotating prefix to verify
  collision deduplication cannot hide it; check execution and coverage counts.

### B3. Malformed UTF-16 witness identifiers collide in canonical hashes

**Confidence: Confirmed hash collision; custom-table consequence source-inferred.**

- **Location:** `SharpProof.Ir/CanonicalHashWriter.cs:13`,
  `SharpProof.Specs/ApiSpecTable.cs:183-186` and `:405-410`, and
  `SharpProof.Specs/ApiSpecContentDigest.cs:13`.
- **Defect:** text validation accepts malformed UTF-16, while canonical hashing
  uses UTF-8 replacement fallback. Witness identifiers expressed in C# as
  `"probe\uD800"` and `"probe\uFFFD"` are distinct lookup keys but encode to the
  same hash input. Source inspection shows that otherwise identical custom
  API-spec declarations can consequently share a content digest despite
  different public witness identities; that table path was not executed.
- **Observed boundary:** unchanged `CanonicalHashWriter` and dependencies were
  compiled in memory in the canonical container. Malformed high-surrogate,
  malformed low-surrogate, and U+FFFD inputs all produced
  `75c18c4934601c4b284d7bfa1ab16e66d070aa831d488c5af9d66f0c591dcc25`.
  Valid ASCII and valid surrogate-pair controls produced distinct hashes.
- **Proposed fix:** reject malformed UTF-16 at the accepted-input boundary, or
  use a lossless canonical encoding. Preserve existing digests for valid text.
- **Proposed regression:** compare lone-surrogate and replacement-character
  witness keys through table construction, lookup, and digest generation;
  include valid Unicode and existing digest controls.
- **Scope:** the bundled worker uses a static default table. No bundled-worker
  false proof, cache exploit, or attacker-controlled table path was demonstrated.

### B4. A null module row escapes typed artifact validation

**Confidence: Confirmed validator boundary; downstream paths source-traced.**

- **Location:** `SharpProof.CompilerArtifact/CompilationFingerprint.cs:522`
  and its `ValidReferenceModules` validation order; `WorkerInputSnapshot.Load`
  exception translation and worker CLI `Program.cs:101-110`.
- **Defect:** module-reference validation reads `Modules[0].Name` before
  checking each entry. A reference with `Kind="Module"`,
  `Identity="module.netmodule"`, `EmbedInteropTypes=false`, `Aliases=[]`, and
  `Modules=[null!]` throws instead of returning the intended shape rejection.
- **Observed boundary:** the unchanged CompilerArtifact project built in the
  canonical container with outputs under `/tmp`, with zero warnings/errors.
  In-memory reflection invoked the actual private `ValidReference`: a valid
  module returned `true`, an assembly reference with a null module row returned
  `false`, and a module reference with a null row threw `NullReferenceException`.
- **Source-traced impact:** the row can reach `Deserialize` semantic validation:
  an empty-callable artifact can reseal `CompilationSha256`, and the feature
  seal excludes references. `WorkerInputSnapshot.Load` translates
  `JsonException`, `InvalidDataException`, and `DecoderFallbackException`,
  excluding this null dereference. Public `VerifyAsync` propagates it; the CLI
  catches the generic exception as `InfrastructureFailure`. Full deserialization,
  public API, and CLI paths were not executed; no unhandled CLI crash is claimed.
- **Proposed fix:** validate entries before reading the first module's name,
  preserving typed artifact rejection and `CompilerManifestMismatch` evidence.
- **Proposed regression:** combine existing `Kind=Module` and `Modules=[null!]`
  mutations in `CompilerManifestArtifactTests`; assert typed rejection and
  structured worker evidence through public API and CLI paths, retaining the
  valid-module and invalid-assembly controls.

### B6. Later ref effects replace earlier argument, receiver, and target values

**Confidence: Confirmed exact frontend IR defect; worker consequences untested.**

- **Location:** `SharpProof.Frontend/RoslynProgramLowerer.cs:283-298`, `:408`,
  `:430`, `:442`, `:485-486`, and `:535-537`;
  `SharpProof.Frontend/RoslynOperationLowerer.cs:141-153`.
- **Defect:** lowering retains raw variables for previously evaluated values,
  then mutates those variables while lowering a later ref call. The eventual
  call or store therefore uses a changed variable instead of the saved value
  required by C# evaluation order. These are one root cause, including former C1.
- **Observed boundary:** a fresh isolated unchanged-HEAD build with Roslyn 4.14
  produced all three cases with `IsExact=True`:

  | Source | Emitted operation sequence |
  | --- | --- |
  | `Use(x, Mutate(ref x), x)` | `Mutate(v0); Havoc(v0); Use(v0, v1, v0)` |
  | `b.Use(Swap(ref b))` | `Swap(v0); Havoc(v0); Use(receiver=v0)` |
  | `a[0] = Replace(ref a)` | `Replace(v0); Havoc(v0); Store(sequence=v0, index=0)` |

  C# preserves the original early argument, receiver, and assignment target,
  respectively. The earlier CFG probe showed parameter/nested-call/parameter
  with no flow captures. Worker eligibility and false-proof consequences were
  not exercised.
- **Fifth-pass worker check (2026-09-22):** the real collector and in-process
  worker (unchanged HEAD, project-wide `checked`) were run over
  `x + Bump(ref x)`, `x += Bump(ref x)`, `Bump(ref x)` followed by a read,
  `i++ + i`, and `a + (a = 2)` with true and false postconditions. Every claim
  was `Unknown`: the `ref` calls abstained with `UnsupportedCallable` and the
  others with `UnsupportedBody`. No worker false proof was reachable through
  these shapes; the frontend IR defect itself remains.
- **Proposed fix:** materialize evaluation-time values before later effects,
  covering arguments, receivers, and assignment locations while preserving
  ref/out aliases and C# evaluation order.
- **Proposed regression:** retain all three examples, compare emitted IR with
  direct execution, and add unchanged-value controls. Separately verify the
  worker's accepted/abstained classification before claiming a worker impact.

### B7. Rejected cache reads skip capacity maintenance

**Confidence: Confirmed cache-read/filesystem boundary.**

- **Location:** `SharpProof.Worker/VerificationCache.cs:65-72`, `:83-91`,
  and `:498-519`.
- **Defect:** malformed or oversized requested entries return after deletion
  or quarantine, before `TryStageCapacity`. Other entries can remain above a
  reduced cap. A noncacheable verification result would cause no later write
  to repair that state; this downstream consequence remains source-traced.
- **Observed boundary:** the current Worker built privately in canonical Linux
  with `--artifacts-path /tmp/b7-current-build`, zero warnings/errors. Real cache
  reads used a 150-byte cap and two retained 100-byte entries:

  | Requested entry | Observed result |
  | --- | --- |
  | Absent | Oldest retained entry evicted; 100 bytes remained |
  | Malformed JSON | Requested entry removed; both retained entries remained, 200 bytes |
  | Oversized, 151 bytes | Requested entry removed; both retained entries remained, 200 bytes |
  | Held-lock control | All entries preserved, 201 bytes; `LastReadUnavailable=true` |

  All reads were misses; the probe exited successfully and private data was
  cleaned. No valid-cache-hit control or complete worker request was executed.
- **Proposed fix:** run common capacity maintenance under the acquired lock
  for rejected misses; lock-acquisition failure must not evict entries.
- **Proposed regression:** retain these cases and add semantic rejection and
  a noncacheable result; assert LRU eviction and final size, plus valid-hit and
  lock-failure/no-eviction controls.

### B9. Pilot validation accepts absent required publication files

**Confidence: Confirmed at the isolated validator boundary.**

- **Location:** `scripts/Test-SharpProofPilotReport.ps1:147-160`; the existing
  `scripts/Test-SharpProofPilotAuthorityFixtures.ps1:130-132` fixture names
  nonexistent publication files.
- **Defect:** the validator checks result-file existence and length but accepts
  missing request, compiler-manifest, and SARIF files named by the report.
- **Observed boundary:** the same isolated actual-validator probe as B8 used
  file doubles only for result paths. All 15 non-result paths across five pilots
  were genuinely absent, and validation accepted the report. No end-to-end
  qualification issuance was exercised.
- **Proposed fix:** validate every required publication file's existence,
  nonempty content, content identity, and binding to the reported run and its
  other artifacts before accepting qualification input.
- **Proposed regression:** start with a complete valid publication, then delete
  and alter each required file independently. Reject missing, empty, stale,
  or mismatched artifacts while retaining the complete-publication control.

### B11. Substitution validates one root enumeration and returns another

**Confidence: Confirmed API-boundary ownership defect.**

- **Location:** `SharpProof.Ir/IrSubstitution.cs:89-93`, `:98`, and `:103-114`;
  replacement-map snapshotting at `:121-124`.
- **Defect:** `SubstituteMany` validates roots, then enumerates the caller-owned
  list again for empty and nonempty replacement maps. A changing
  `IReadOnlyList` can provide valid owned roots first and foreign roots later.
- **Observed boundary:** unchanged IR sources compiled in memory in the
  canonical container. A custom list yielded `factory.Integer(7)` on its first
  enumeration and `otherFactory.Integer(9)` on its second. Both map cases
  returned the foreign term, which the original factory's interpreter rejected.
  A stable-list control preserved the expected result. This is a changing
  caller-owned-view defect; no production-worker false proof is claimed.
- **Proposed fix:** snapshot roots once and validate/process that same array,
  following the replacement-map snapshot pattern.
- **Proposed regression:** cover changing roots for empty and nonempty maps,
  assert owned results or typed rejection, and retain stable-list controls.

### B15. A catch filter can replace cancellation before an accepted rethrow

**Confidence: Confirmed meta-analyzer diagnostic boundary.**

- **Location:** `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:28-34`
  and `:434-461`.
- **Defect:** `RethrowsCancellationImmediately` compares identifier spelling,
  ignoring a filter that changes the caught exception's identity before
  `throw caught`.
- **Observed boundary:** a fresh unchanged-HEAD Meta.Analyzers.Test build had
  zero warnings/errors. A real analyzer probe with zero compiler errors emitted
  no `SPMETA003` for:

  ```csharp
  try { throw new OperationCanceledException(); }
  catch (Exception caught) when ((caught = new Exception()) != null)
  { throw caught; }
  ```

  An empty-catch control emitted `SPMETA003`; a bare-throw control correctly
  emitted none. The probe exited successfully. No production cancellation
  incident is claimed.
- **Related gap (fifth pass):** a 19-case probe of the rebuilt meta analyzer
  flagged every direct, base-type, catch-all, conditional-rethrow, lambda, and
  local-function swallow, but was silent for
  `try { Task.Run(() => Work(t)).Wait(); } catch (AggregateException) { }`,
  which swallows the wrapped `TaskCanceledException`. No production code
  currently catches `AggregateException`.
- **Proposed fix:** require the original exception identity for throw-caught,
  accounting for filter assignments and ref/out escapes; preserve bare throw.
  Treat `AggregateException` handlers as cancellation-capable unless they
  rethrow or inspect `InnerExceptions` for cancellation.
- **Proposed regression:** cover direct and ref/out filter mutation, unchanged
  caught variables, and bare rethrows.

### B17. Native portable qualification assumes a prepared framework package cache

**Confidence: Confirmed helper boundary; native workflow consequence source-traced.**

- **Location:** `.github/workflows/package-consumers.yml:102-114`;
  `scripts/Test-SharpProofPortableConsumer.ps1:33-35`;
  `scripts/Test-SharpProofPackageConsumers.ps1:86-171` and `:314-317`, with
  the missing-file check at `:158-161`.
- **Defect:** the native job checks out the repository and downloads SharpProof
  packages, but `New-FrameworkPackageSource` requires six framework `.nupkg`
  archives before its first restore. Entry/package validation does not prepare
  them, and the job has no framework-package preparation step.
- **Observed boundary:** canonical Docker stdin executed the actual AST helper
  and real package-identity module against a private `NUGET_PACKAGES` cache.
  An empty cache failed for missing
  netstandard.library/2.0.3/netstandard.library.2.0.3.nupkg. Copying six existing
  real framework archives into that private cache made the same helper succeed
  with `OfflineArchives=6`. Only ephemeral `/tmp` fixture data was used; no
  downloads or source/probe files were created.
- **Scope:** this establishes the helper prerequisite on a cold supported host
  with the correct SDK. No full native workflow was executed, and no claim is
  made about current hosted-runner caches or SDK availability.
- **Proposed fix:** bootstrap the pinned framework archives explicitly before
  offline source creation, preserving package identity validation.
- **Proposed regression:** run the supported native entry path from an empty
  package cache and retain a prepared-cache control.

### B18. Nullable value-type receivers are treated as possibly null references

**Confidence: Confirmed analyzer false positive.**

- **Location:** `SharpProof.Effects/OperationEffectScanner.cs:793-803` (the
  receiver check in the invocation scan) calling `PotentialNullCheck` in
  `SharpProof.Effects/OperationEffectScanner.Expressions.cs:18-27`, which relies
  on `IsStaticallyNonNull` in
  `SharpProof.Effects/OperationNullnessEvaluator.cs:155-162`.
- **Defect:** `IsStaticallyNonNull` deliberately returns `false` for
  `Nullable<T>` because a nullable value can be "null" (have no value). The
  invocation scan reuses that answer as "the receiver may be a null
  reference" for every instance call. A `Nullable<T>` receiver is a value type:
  `HasValue`, `GetValueOrDefault()`, and `GetValueOrDefault(T)` can never throw
  `NullReferenceException`. Only `Value` can throw, and its
  `InvalidOperationException` is already modeled by the API spec.
- **Observed boundary:** a real analyzer built from unchanged HEAD sources
  (`features=all`, advisory profile) reported
  `SP0046 ... may-effect summary includes disallowed exceptions:
  System.NullReferenceException` for `[DoesNotThrow] bool Hv(int? v) =>
  v.HasValue;`, `v.GetValueOrDefault()`, `v.GetValueOrDefault(7)`, and a
  `long?` variant. It also reported `SP0045 ... allocation: Managed` for
  `[ZeroAllocations] bool AllocHvParam(int? v) => v.HasValue;`, because the
  spurious exception path is charged as an allocation. Controls: the same
  member on a local `int? v = 3` produced no diagnostic; `v != null` and
  `v ?? 0` produced none; `[EnforcePure]` on `v.HasValue` produced none;
  `string s; s.Length` correctly reported `NullReferenceException`.
- **Documentation conflict:** `docs/coverage-and-limits.md:142-144` lists
  `HasValue`, `GetValueOrDefault()`, and `GetValueOrDefault(T)` as "Does not
  throw" with no allocation; the analyzer reports both for nullable parameters.
- **Impact:** false `SP0046`/`SP0045` diagnostics on common nullable-value code.
  In `RequireProven`/error configurations this fails builds that are correct.
  This fails closed; no false proof is involved.
- **Proposed fix:** in the receiver check at `OperationEffectScanner.cs:793`,
  skip `PotentialNullCheck` when `instance.Type` is a value type (including
  `Nullable<T>`), e.g. `if (instance != null && method.ReducedFrom == null &&
  instance.Type?.IsValueType != true)`. Keep `IsStaticallyNonNull` unchanged,
  because null-value questions (`v == null`, unboxing, `v.Value`) still need
  the `Nullable<T>` exclusion. A boxed receiver (for example `v.GetType()`) is
  a conversion whose type is `object`, so it keeps its null check.
- **Proposed regression:** `[DoesNotThrow]` and `[ZeroAllocations]` over
  `HasValue`/`GetValueOrDefault` on nullable parameters must be silent. Keep
  controls that `((int?)null).Value` still reports `InvalidOperationException`,
  that `v.GetType()` on a nullable parameter still reports
  `NullReferenceException`, and that reference receivers are still checked.

### B19. IL summaries treat `int.MinValue % -1` as normal completion

**Confidence: Confirmed at the worker boundary; fails closed through replay.**

- **Location:**
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs:1270-1320`
  (`Arithmetic`), specifically the shared `Div`/`Rem` branch at `:1302-1320`
  that assumes `InRange(raw, minimum, maximum)` on the result only.
- **Defect:** IL `rem` on int32 is lowered to the 64-bit IR `Remainder`. For
  `int.MinValue % -1` the IR value is `0`, which is in the int32 range, so the
  summary records normal completion with result `0`. The CLR (and the C#
  specification for `%`) throws `OverflowException` for this input. `div` is
  correct only because its IR result, `2^31`, falls outside the int32 range.
  The 64-bit case is already correct because the IR remainder is undefined for
  `long.MinValue % -1`.
- **Observed boundary:** a separately compiled net9.0 library exposed
  `R(a, b) => a % b` and `Dv(a, b) => a / b`. Client methods were collected
  with the real collector and verified by the in-process worker (both built
  from unchanged HEAD sources):

  | Client claim | Outcome |
  | --- | --- |
  | `Requires(b != 0); Ensures(a != int.MinValue \|\| b != -1)` over `Lib.Dv` | `Proven` (`il-summary:M:Lib.Dv...`) |
  | Same claim over `Lib.R` | `Unknown`, `CounterexampleNotReplayable` |
  | `Requires(a == int.MinValue); Ensures(Result != 0)` over `Lib.R(a, -1)` | `Unknown`, `CounterexampleNotReplayable` |

  Both `Lib.R` claims hold on every normal return, because the only
  counterexample input throws. The solver found that input only because the
  summary claims normal completion there. Counterexample replay rejected the
  model, so no false refutation was published.
- **Impact:** true postconditions over library `%` helpers become `Unknown`,
  and the persisted IL summary misstates the callee's normal-completion
  relation. Any consumer that trusts `NormalCompletion` without replay would
  see a behavior the runtime never produces.
- **Proposed fix:** for `ILOpCode.Rem`, also assume that the quotient is
  representable:
  `context.Builder.Assume(context.Block, Operation(instruction.Offset),
  InRange(_factory.Binary(IrBinaryOperator.Divide, left.Term, right.Term),
  minimum, maximum))`, in addition to the existing result-range assumption.
  Division by zero remains excluded by IR undefinedness.
- **Proposed regression:** in the implementation-IL summary tests, assert that
  the int32 `rem` summary excludes `(int.MinValue, -1)` from normal completion,
  and repeat the three client claims above, expecting `Proven` for both
  remainder claims. Keep the `div` control, the long `rem` control, and a
  `(7, -1)` remainder control that must still complete with `0`.

### B33. Reads through `?.`, field-held references, or `?:` receivers become `Unknown`

**Confidence: Confirmed through public effect summaries and analyzer
diagnostics.**

- **Location:** `SharpProof.Effects/ConversionOwnershipClassifier.cs:35-90`.
  `ClassifyRegion` has no case for `IConditionalAccessInstanceOperation`;
  maps every `IFieldReferenceOperation`/`IArrayElementReferenceOperation`
  value to `EffectRegionSet.Unknown` (`:74-75`); and resolves
  `IConditionalOperation`/`ICoalesceOperation` receivers only when
  `aliasSource` is true (`:82-87`), so ordinary reads fall through to
  `Unknown` at `:89`.
- **Defect:** the region of the object a read goes through is lost in three
  idiomatic shapes: the `b` in `b?.V`; the object held in a field, as in
  `n.Next.V` (reachable from parameter `n`); and the chosen receiver in
  `(c ? x : y).V`. Each read is recorded against `Unknown`, which
  `[EnforcePure]` cannot distinguish from mutable static or ambient state.
- **Observed boundary:** `EffectAnalysisSession.Analyze` built from unchanged
  HEAD returned `reads=[Parameter 0]` for `b.V`, `b == null ? 0 : b.V`, and
  `in` parameter reads, but `reads=[Unknown]` for `b?.V` (value and reference
  fields alike), `n.Next == null ? n.V : n.Next.V`, and `(b ? x : y).V`. The
  analyzer reported `SP0002` for `[EnforcePure]` on `b?.V`, `b?.V ?? 0`, `b?.S`,
  the `n.Next.V` chain, and the conditional receiver, while `n.V` and the
  explicit null-check form were silent.
- **Impact:** false `SP0002` (and incomplete `[EffectContract]` coverage) on
  null-safe reads and on any read that traverses an object graph (linked
  lists, trees, nested options); fails closed, but breaks correct builds under
  error severities.
- **Proposed fix:** resolve an `IConditionalAccessInstanceOperation` to its
  owning `IConditionalAccessOperation` (the nearest ancestor whose
  `WhenNotNull` contains it) and classify that operation's `Operation`. For
  reads (not aliasing writes), classify a field or array-element value as
  "reachable from" the region of its instance (a transitive `ReadsArgumentState`
  / `ReadsReceiverState` region) instead of `Unknown`. Apply the existing
  conditional/coalesce union for read receivers as well as alias sources.
- **Proposed regression:** effect-summary and analyzer tests for `b?.V`,
  `b?.S`, `b?.Inner?.V`, `n.Next.V`, `a[0].V`, and `(c ? x : y).V` on
  parameters, locals, and `this`, expecting argument/receiver reads, with a
  control that a read through a static field remains a static read.

### B34. Implicit constructors with field initializers are unmodeled

**Confidence: Confirmed through public effect summaries and analyzer
diagnostics.**

- **Location:** `SharpProof.Effects/EffectMethodNodeBuilder.cs:587-598`
  (`IsProvablyEmptyImplicitConstructorLayer` requires
  `!HasInstanceMemberInitializer(type)`, defined at `:633`) and its caller
  `SharpProof.Effects/EffectCallSiteResolver.cs:113-140`; explicit constructors
  already scan the same initializers through `GetMemberInitializerOperations`
  (`EffectMethodNodeBuilder.cs:424`).
- **Defect:** a compiler-generated constructor of a class with any instance
  field initializer, even `public int V = 5;`, is resolved as an unmodeled
  call with a Top summary. The same class with an explicit empty constructor
  `public C() { }` has identical runtime behavior and is modeled completely.
  Primary constructors with initializers are also `UnsupportedOperation`.
- **Observed boundary:** `EffectAnalysisSession.Analyze` built from unchanged
  HEAD:

  | Allocation | Summary |
  | --- | --- |
  | `new ImplicitScalarInit()` (`int V = 5;`) | `Incomplete`, `DirectCall, UnmodeledCall` |
  | `new ImplicitArrayInit()` / `new ImplicitObjectInit()` | `Incomplete`, `UnmodeledCall` |
  | `new PrimaryCtor(1)` (`int V = v;`) | `Incomplete`, `UnsupportedOperation` |
  | `new ExplicitScalarInit()` / `new ExplicitArrayInit()` | `Complete`, fresh writes only |
  | `new NoInit()` / struct with explicit ctor | `Complete` |

  The analyzer reported `SP0002` for `[EnforcePure] => new ImplicitScalarInit()`
  and `=> new Options().Retries` (a typical options class), and
  `SP0046 ... ExceptionSetUnknown: DirectCall, UnmodeledCall` for
  `[DoesNotThrow]`, while the explicit-constructor twins were silent.
- **Impact:** any method that instantiates an ordinary class with field
  initializers (options, DTOs, collections holders) cannot satisfy
  `[EnforcePure]`, `[DoesNotThrow]`, `[AllowedCapabilities]`, or
  `[EffectContract]`. Fails closed, but produces widespread false positives.
- **Proposed fix:** model an implicit constructor as an explicit empty
  constructor body: scan `GetMemberInitializerOperations(compilation, type,
  staticInitializers: false, ...)` with the receiver as a fresh region, then
  chain to the unique parameterless base constructor, as the explicit path
  already does. Handle primary-constructor parameter captures through the
  existing `PrimaryConstructorParameterOwnership`.
- **Proposed regression:** effect tests comparing implicit and explicit
  constructors for scalar, array, object, and throwing initializers (the
  throwing one must stay non-`DoesNotThrow`), plus a primary-constructor case.

### B41. Declared trusted computing base omits soundness-relevant source files

**Confidence: Confirmed by comparing the contract with the compiled sources.**

- **Location:** `eng/acceptance/contract.json` (`trustedKernel.paths` and
  `trustedComputingBase.components[].paths`);
  `scripts/Get-SharpProofTcbPaths.ps1:67-86` checks only that each declared
  path is a production Compile item, and
  `SharpProof.ArchitectureTest/ArchitectureTests.cs:544-620`
  (`TrustedComputingBaseDeclarationNamesEveryRequiredPath`) checks only
  non-empty components, no duplicates, and mutation targets inside the TCB.
  Nothing checks the reverse direction.
- **Defect:** the TCB is a hand-written allowlist with no completeness rule,
  and many files that decide verdicts are missing from it. Examples at
  baseline: `SharpProof.Effects/OperationEffectScanner.Patterns.cs`,
  `OperationEffectScanner.ExternalExceptions.cs` and
  `OperationEffectScanner.DirectForeach.cs` (partial files of the TCB type
  `OperationEffectScanner`, whose other three files are listed),
  `SharpProof.Effects/OperationNullnessEvaluator.cs`,
  `ConversionEffectClassifier.cs`, `ManagedMutationFacts.cs`,
  `UsingDisposalEffectResolver.cs`, `TryCompletionFacts.cs`,
  `CatchFilterFacts.cs` (27 of 57 Effects files are outside);
  `SharpProof.Ir/IrSubstitution.cs`, `IrTraversal.cs`, `IrTermServices.cs`,
  `IrProgramBuilder.cs`, `IrExceptionKindFacts.cs`, `IrInstructionFacts.cs`;
  `SharpProof.Frontend/RoslynTypeMapper.cs`, `CompilerMethodScopes.cs`,
  `CompilerConstantAdmission.cs`;
  `SharpProof.CompilerArtifact/CompilerSourceIntegerDomain.cs`,
  `CompilerFeatureScopeFingerprint.cs`, `CompilerSourceRebinding.cs`;
  `SharpProof.Contracts/BoundContracts.cs`;
  `SharpProof.Dataflow/SequenceCardinalityDomain.cs`; and
  `SharpProof.Worker/MethodResourceBudget.cs`.
- **Observed boundary:** a script that collected every `SharpProof*.cs` path
  named in `contract.json` and listed each project's non-`obj`/`bin` sources
  found 12 of 24 Ir files, 27 of 57 Effects, 5 of 21 Frontend, 7 of 19
  CompilerArtifact, 3 of 18 Contracts, 4 of 12 Dataflow and 2 of 19 Worker
  files outside the declaration (a few are only `GlobalUsings.cs`). TCB code
  calls the omitted files directly: for example `OperationCompletionEvaluator.cs`
  and `OperationEffectScanner.cs` (both in the TCB) call
  `OperationNullnessEvaluator`, and `IrRelationalSummaryInstantiator.cs` and
  `PostconditionObligationBuilder.cs` call `IrSubstitution`. B11
  (`IrSubstitution.cs`) and the cause of B18 (`OperationNullnessEvaluator.cs`)
  are both in omitted files.
- **Impact:** changes to these files escape every TCB-scoped gate: changed-TCB
  line coverage in `scripts/Test-SharpProofCoverage.ps1`, the release
  authority closure in `scripts/Test-SharpProofReleaseAuthorityClosure.ps1`,
  acceptance in `eng/acceptance/Verify.ps1`, and TCB mutation targeting. A
  soundness regression in, for example, nullness or substitution can ship
  without the evidence the release process claims to require for trusted
  code. Partial files are the sharpest case, because the same type is half
  trusted and half not.
- **Proposed fix:** add the files above to the matching components. Then add
  a completeness rule to `Get-SharpProofTcbPaths.ps1` (when
  `-ProductionInventory` is given) and to the architecture test: every Compile
  item of the pipeline projects (Ir, Smt, Effects, Frontend, CompilerArtifact,
  CompilerCollector, Contracts, Dataflow, Summaries, Worker) must be either in
  the TCB or in a new explicit `trustedComputingBase.excludedPaths` list with a
  reason. Also require that all files declaring any partial of a TCB type are
  in the TCB, which can be found with Roslyn by grouping
  `TypeDeclarationSyntax` by symbol.
- **Proposed regression:** an architecture test that fails when a new `.cs`
  file is added to a pipeline project without being classified, and a
  fixture where one partial file of a TCB type is omitted, which must fail.

### B42. `.globalconfig` profile and features are only partly honored

**Confidence: Confirmed by probing the analyzer; the MSBuild half is confirmed by reading the targets.**

- **Location:** `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs:93-150`
  (`ReadOptionAliases`) and `:255-292` (`IsPackageDefaultProperty`,
  `HasPackageDefaults`, `IsPackageDefaultValue`);
  `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props:3-8`;
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:19` and
  `:32-35`; the claim in `README.md` that "The same choices can be made in a
  `.globalconfig` file".
- **Defect:** there are two problems.
  1. The analyzer ignores a `build_property.SharpProof*` value only when
     *both* MSBuild properties hold their package defaults (`advisory` and
     `all`). If the user sets either one explicitly in MSBuild, the other
     one's package default counts as a user value, and it conflicts with the
     `.globalconfig` key.
  2. Every strict-mode behavior that matters is decided in MSBuild from
     `$(SharpProofProfile)`: `SharpProofVerify=true`,
     `SharpProofVerifyPolicy=require-proven`, `SharpProofAssumptionPolicy=error`,
     and omitting the items for `off`. MSBuild cannot see `.globalconfig`
     keys, so `sharpproof_profile = strict` there only disables the analyzer's
     advisory fast path (`SharpProofAnalyzerEngine.cs:95`). The verifier stays
     off and no proof is required. Likewise, `sharpproof_profile = off` leaves
     the analyzer and generator items loaded.
- **Observed boundary:** with the real analyzer and global options
  `build_property.SharpProofProfile=advisory`,
  `build_property.SharpProofFeatures=effects` and `sharpproof_profile=strict`,
  the result is `SP0025 ... 'sharpproof_profile' has invalid value
  'strict / advisory': configuration aliases disagree`, and analysis is
  disabled (`Profile=Off`). With `build_property.SharpProofProfile=strict`,
  `build_property.SharpProofFeatures=all` and `sharpproof_features=effects`,
  the result is `SP0025 ... 'sharpproof_features' ... 'effects / all'`. The
  control (`SharpProofFeatures=all`, `sharpproof_profile=strict`) is silent,
  as `AnalyzerConfigurationPackageDefaultTests` expects.
- **Impact:** a project that sets `<SharpProofFeatures>effects</SharpProofFeatures>`
  in MSBuild and `sharpproof_profile = strict` in `.globalconfig` fails its
  build on a configuration error it did not cause. Worse, a project that uses
  only `.globalconfig` to pick `strict`, as the README invites, gets an
  advisory build without verification and still believes proofs are
  enforced. This is a silent weakening of the strongest mode. Because the
  analyzer also treats an explicit MSBuild `advisory` as a package default, an
  explicit `<SharpProofProfile>advisory</SharpProofProfile>` plus a
  `.globalconfig` `strict` is resolved silently to analyzer-strict with no
  verifier.
- **Proposed fix:** have `SharpProof.ConsumerContract.props` record whether
  each property was defaulted (for example
  `_SharpProofProfileWasDefaulted=true`, exposed via `CompilerVisibleProperty`).
  Have `IsPackageDefaultProperty` use that per-option flag instead of
  `HasPackageDefaults`. Then either make MSBuild authoritative for the
  profile, by reporting SP0025 when `sharpproof_profile` in `.globalconfig`
  differs from the effective `$(SharpProofProfile)`, or make the analyzer
  report a configuration error when it sees an analyzer-strict profile while
  `build_property.SharpProofVerify` is not `true`. Correct the README
  sentence to say that only MSBuild properties control verification.
- **Proposed regression:** analyzer configuration tests for the two observed
  mixed-source cases (no SP0025 expected once defaults are tracked per
  option), plus a package test in which `.globalconfig` sets
  `sharpproof_profile = strict` with no MSBuild property. That test must
  either run the verifier with `require-proven` or fail with a configuration
  error, and must never produce a passing advisory build.

### B49. A callee that has its own contract blocks relational summaries for every caller

**Confidence: Confirmed by running the collector and worker; the exact rejecting line is inferred from code reading.**

- **Location:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:239-360`
  (`TryBuildSource`). The callee body is lowered with `IsAdmissiblePureCall`
  (`:118-126`), and then every lowered call must itself yield a relational
  summary through `TryGet` (`:332-347`); otherwise the whole summary is
  rejected (`return false`).
- **Defect:** `Contract.Requires`/`Ensures`/`Assume` in the callee are
  ordinary `IInvocationOperation`s in Roslyn's tree, even though the compiler
  elides them from IL (`[Conditional]` unless `SHARPPROOF_CONTRACTS` is
  defined). The default API-spec table treats them as side-effect free
  (`ContractSemantics`), so they are admitted into the lowering. None of them
  has a relational summary, so `TryBuildSource` fails for any callee that
  declares a contract. The caller's obligation then contains an unresolved
  call and becomes `Unknown(UnsupportedBody)`. The callee's own proven
  `Ensures` is not used either.
- **Observed boundary:** with the collector and the in-process worker
  (checked compilation), consider private callees
  `IdReq(x) { Contract.Requires(x >= 0); return x; }`,
  `IdEns(x) { Contract.Ensures(Contract.Result<int>() == x); return x; }`
  and `Id(x) { return x; }`, and callers that each require `x >= 0` and ensure
  `Result >= 0`. The caller of `Id` was `Proven` with core
  `[requires:0, source-summary:M:R4.Id(System.Int32)]`. The callers of
  `IdReq` and `IdEns` were `Unknown(UnsupportedBody)`, while `IdEns`'s own
  postcondition was `Proven`. A 1,000-method chain in which every method has
  `Requires`/`Ensures` and returns the previous one produced 1 `Proven` and
  999 `UnsupportedBody`.
- **Impact:** the moment a helper gains a contract, every contracted caller
  loses verification. That inverts the expected benefit of annotating code.
  `docs/architecture.md` ("Contracts and modular verification") and
  `docs/coverage-and-limits.md` ("Relational callees") describe direct acyclic
  static scalar callees as composable and do not mention this exclusion.
- **Proposed fix:** when lowering a callee body for a source summary, drop
  invocations of the `Contract` API that the compiler elides in the callee's
  tree. Decide elision with the same `[Conditional]`/preprocessor check the
  analyzer uses (`InvocationEmissionPolicy`), so the relation describes the
  emitted IL. Then, at the call site, emit the callee's bound `Requires` as a
  caller obligation (it is already checked by SP0027 and could be reused).
  Optionally, when the callee's `Ensures` is `Proven` in the same run, allow
  it as a justified assumption in place of, or together with, the inferred
  relation.
- **Proposed regression:** worker tests for the three callees above,
  requiring `Proven` for all three callers, plus a control in which a
  callee's `Requires(x > 0)` is not established by the caller. That control
  must not become `Proven` on the strength of the callee body alone, so the
  obligation must be reported.

### B54. SP0027 is silenced by almost any earlier statement (instance or BCL calls, field writes, constant arithmetic)

**Confidence: Confirmed by probing the analyzer; the mechanism is partly inferred. SP0027 has a syntactic replay path and a managed-flow path, and the flow path evidently accepts `x += 2` but not the other prefixes, so both paths need the fix.**

- **Location:** `SharpProof.Effects/ManagedAbstractFlow.cs:2561-2569`
  (`DefiniteOperationFacts.CompletesNormally(IInvocationOperation)`), which
  requires `invocation.Instance == null ||
  invocation.Instance is IInstanceReferenceOperation`. It is consumed by
  `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:595-660`
  (`HasReplayablePrefix`, which requires every earlier statement in the block
  to complete normally).
- **Defect:** the definite-completion query treats every instance call whose
  receiver is not `this` as possibly non-completing, even when the receiver
  is a fresh `new T()` or a local that flow analysis already knows is
  non-null, and the target is a non-virtual source method with a trivially
  completing body. Because SP0027 replay requires a completing prefix, a
  single such statement anywhere earlier in the block disables SP0027 for
  the rest of the block.
- **Observed boundary:** with `St.P(int v)` requiring `v > 0`, `St.P(0)`
  reports SP0027 on its own, after `St.P(0)`, and after a static
  `K5.Z(1)`. It is silent after `new K5().M();` (where `M() => 1`), after
  `new K5().N(1);`, after `new K5().O(1);` (whose own precondition is
  satisfied), and after `var k = new K5(); k.M();`. The same holds for
  closed-attribute preconditions (`[Positive]`) and for calls following
  `new K3().R(1)`.
  Static BCL calls that the default API-spec table marks `DoesNotThrow`
  also block the report: `P(0)` is silent after `Math.Max(1, 2);`,
  `var b = string.IsNullOrEmpty("a");` or `var s = string.Concat("a", "b");`,
  and `int m = Math.Max(1, 2); P(m - 2);` is silent too. Only
  `var o = new object(); P(0);` still reports. `CompletesNormally(IMethodSymbol)`
  (`:2571-2600`) returns `false` for any method without exactly one source
  declaration and never consults the resolved API-spec throw facet.
  The same narrow prefix model silences `P(0)` after each of these
  statements, in both static and instance callers: `a[0] = 1;` on a fresh
  `int[3]`, `_f = 1;`, `b.F = 1;` on a fresh `Box`, `Box.S = 1;` (no static
  constructor), `b.Prop = 1;`, `int x = 10 / 2;` (a compile-time constant),
  `string s = "a" + 1;`, `object o = 1;`, `int i = (int)l;` (unchecked),
  and an empty `for` loop. Of the probed prefixes, only `x += 2;` kept the
  report. `CompletesNormally(IOperation)` (`:2508-2559`) allows
  assignments only to locals, parameters, and discards; rejects every
  division regardless of constant operands; and has no case for
  constant-valued operations, field or array stores, boxing, or loops.
- **Impact:** almost every realistic method calls an instance method on a
  local object before calling a contracted API. In such methods, definite
  precondition violations go unreported, so SP0027 silently loses most of
  its value in object-oriented code. This is a false negative, not a false
  proof.
- **Proposed fix:** accept a receiver when it completes normally *and* is
  definitely non-null: an `IObjectCreationOperation`, `this`, a
  `[NotNull]` parameter, or a local or field access whose managed-flow
  nullness fact at that point is non-null (the analyzer already has that
  fact in `ManagedFlowResult`). Keep rejecting virtual dispatch unless the
  receiver's type is sealed or the method is non-virtual.
  For metadata targets, consult the resolved API spec (`ResolvedApiSpecTable`,
  already held by `ManagedAbstractFlow`). A `DoesNotThrow` +
  `Terminates` facet should count as completing normally.
  In `CompletesNormally(IOperation)`, return `true` for any operation
  with `ConstantValue.HasValue`. Accept simple assignments to fields of
  `this`, of definitely non-null receivers, or static fields of types
  without a static constructor; accept boxing and unchecked numeric
  conversions; and accept `for` loops whose condition is a constant-bounded
  counter only if a termination argument exists (otherwise keep `false`).
- **Proposed regression:** analyzer tests for `new K().M(); P(0);`,
  `var k = new K(); k.M(); P(0);` and `k?.M(); P(0);`. The first two must
  report SP0027. The third remains silent only if the conditional access is
  not definitely completing. A control with a parameter receiver
  `F(K k) { k.M(); P(0); }` must stay silent.

### B55. The advisory fast path skips analysis for SharpProof attributes applied through a using alias

**Confidence: Confirmed by probing the analyzer.**

- **Location:** `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs:586-604`
  (`IsSharpProofAttributeCandidate`), used by `GetAdvisoryActivation`
  (`:209-250`) to decide whether an advisory compilation is analyzed at all
  (`:95-106`).
- **Defect:** the activation probe matches an attribute only by the last
  token of its written name (`EnforcePure`, `EnforcePureAttribute`, ...). A
  C# using alias (`using EP = SharpProof.Attributes.EnforcePureAttribute;`,
  including a `global using` alias in another file) spells the attribute
  with a different identifier. The probe then finds no candidate, returns
  `AdvisoryActivation.None`, and no analysis runs. `docs/architecture.md`
  calls this fast path "conservative" and says "new selection or
  implicit-call syntax must extend the probe", but aliases were never
  covered. The contract-method probe is not affected, because it matches
  the member name (`Requires`) and aliases rename only the type.
- **Observed boundary:** with the default advisory profile, `[EP]` on a
  method that increments a static field and `[DNT]` (alias for
  `DoesNotThrowAttribute`) on a method that throws produced no
  diagnostics. The same file under `build_property.SharpProofProfile=strict`
  reported `SP0002` and `SP0046`, as did the unaliased control file in the
  advisory profile.
  Closed preconditions are affected the same way: with only
  `using Pos = SharpProof.Attributes.PositiveAttribute;`, the definite
  violation `Q(0)` against `Q([Pos] int v)` is silent. A namespace alias
  (`[SPA.EnforcePure]`) is recognized and reports normally.
- **Impact:** in the default profile, every effect contract written through
  an alias is silently unchecked. The build looks clean while
  `[EnforcePure]`/`[DoesNotThrow]` claims go unverified.
- **Proposed fix:** before scanning attributes, collect every using-alias
  name in the compilation (`UsingDirectiveSyntax` with `Alias != null` in any
  tree, including `global using`) whose target's last identifier is a
  SharpProof attribute type name, and treat those alias names as candidates.
  Alternatively, when an attribute's simple name is not a known name but a
  using alias with that name exists in scope, fall back to
  `SemanticModel.GetSymbolInfo(attribute)` for that attribute only.
- **Proposed regression:** advisory analyzer tests for a local alias, a
  `global using` alias declared in a separate tree, and an alias to the
  `SharpProof.Attributes` namespace (`using SPA = SharpProof.Attributes;
  [SPA.EnforcePure]`, which already works and serves as the control), and an
  aliased `[Positive]` at a violating call site, each expecting the
  diagnostic the unaliased form produces.

### B59. SP0027 treats a call after a guard-clause `return` as definitely executed, including dead code

**Confidence: Confirmed by probing the analyzer; root cause located by code reading.**

- **Location:** `SharpProof.Effects/ManagedAbstractFlow.cs:2553`
  (`DefiniteOperationFacts.CompletesNormally(IOperation)` puts
  `IReturnOperation`, and `IConditionalOperation` statements containing one,
  in the "completes if children complete" group). It is consumed by
  `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:595-660`
  (`HasReplayablePrefix`: every earlier statement must "complete normally").
- **Defect:** one predicate is used with two meanings. When it evaluates a
  callee body, "completes normally" means the *method* returns, so a
  `return` completes. When it vets the statements *before* a call site, it
  must mean "control reaches the next statement", which a `return` does
  not. Because the prefix check reuses the callee meaning, any earlier
  `if (...) { return; }` guard counts as a completing prefix, and the later
  call is replayed as if definitely executed. This contradicts
  `docs/diagnostic-examples.md` ("non-definitely-executed calls remain
  silent"; "Conditional branches ... remain silent unless the flow proof
  establishes definite execution").
- **Observed boundary:** with `Pos(int v)` requiring `v > 0`:
  - `if (3 > 0) { return; } Pos(0);` and `if (true) return; Pos(0);` report
    SP0027, although `Pos(0)` is unreachable (the compiler itself warns
    CS0162).
  - `if (x > 0) { return; } Pos(0);`, `if (b) return; Pos(0);` and
    `if (x > 0) { Pos(1); return; } Pos(0);` report SP0027 although the call
    runs only on some paths.
  - Controls: `int a = 7; if (a > 0) { return; } Pos(0);` and a `throw`
    guard (`if (x > 0) throw ...; Pos(0);`) are silent.
  - The flow-based SP0027 differential fuzzer (extended with early
    `return` guards, `switch`, `while` and `do` statements; 3,000 methods)
    reported four false violations. All four are calls after a guard whose
    return is always taken at runtime (for example
    `int v0 = 100; ... if (v0 > 0) { return; } Pos(v0);`).
- **Impact:** SP0027, documented as the strong diagnostic ("emitted only
  after concrete predicate replay evaluates to false" for definitely
  executed calls), fires on dead code and on merely possible paths in
  ordinary guard-clause code. That erodes trust in the one diagnostic that
  is supposed to be definite, and it can break builds that treat SP0027 as
  an error. It is not unsound for proofs.
- **Proposed fix:** give the prefix check its own predicate,
  `ReachesNextStatement(IOperation)`, that returns `false` for
  `IReturnOperation`, `IBranchOperation` (`break`/`continue`/`goto`),
  `IThrowOperation`, and any statement containing one of them on some path
  (an `if` whose branch returns, `switch` sections, `try` bodies with
  `return`), unless the managed flow proves that path infeasible. Keep
  `CompletesNormally` for callee-body evaluation. Alternatively, derive
  definite execution from the CFG: the call's block must post-dominate the
  method entry along all feasible edges.
- **Proposed regression:** analyzer tests for the five reporting shapes
  above (all silent after the fix, except that a flow-proven-infeasible
  guard such as `if (x != x) return;` may still allow a report), plus the
  fuzzer's always-taken-guard cases, with a control where the guard is
  provably never taken (`int a = 0; if (a > 0) return; Pos(0);` must still
  report).

### B65. The Z3 solver binary is pinned only by byte length, from download to package to runtime

**Confidence: Confirmed by code reading of every integrity check in the chain.**

- **Location:**
  - `eng/container/Prepare-NativePayload.ps1:36-56` downloads
    `z3.archiveUrl` (from `eng/container/toolchain.json`) with
    `Invoke-WebRequest`, reuses an existing archive file without checking it,
    extracts it, and compares only `libz3.so` and `Microsoft.Z3.dll` *lengths*
    with `libraryBytes`/`managedAssemblyBytes`. Its own error text calls it
    "The verified Z3 archive".
  - `scripts/Test-SharpProofPackagePayloads.ps1:255-266` validates the
    packaged the package entry tools/native/linux-x64/libz3.so and tools/net9/Microsoft.Z3.dll
    only by length.
  - `SharpProof.Host/ContainerContract.cs:125-150`
    (`ResolveZ3LibraryRequired`) checks only `information.Length` before
    loading the library.
  - `eng/container/toolchain.json` records no digest for the archive or
    either file.
  - The solver that is actually loaded is chosen by the
    `SHARPPROOF_NATIVE_ROOT` environment variable (`SharpProof.Host/ContainerContract.cs:128-141`,
    default `/opt/sharpproof/native`). The verifier package's own copy
    (`_SharpProofPackageNativeZ3Path` in
    `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:8`) is only
    checked for existence by `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:43`
    and is never loaded. Any directory with a same-size `libz3.so` therefore
    supplies the solver (see also B66).
- **Defect:** the SMT solver is the component that turns obligations into
  `Proven` verdicts, but none of the checks binds its contents. Any
  replacement of the same size is accepted at image build, package
  validation and worker start: a substituted release asset, a poisoned
  download cache, or a tampered file in the image or package. The worker's
  runtime-closure hash (`WorkerBinaryIdentity`) covers the managed closure but
  not this native library, so the verification cache key and the published
  provenance do not identify it either.
- **Observed boundary:** source-traced. There is no SHA-256 anywhere in
  `toolchain.json` or `third-party-components.json` for these files, no
  hash comparison in the three scripts or classes above, and the
  `payload.json` written into the image records only `bytes`.
- **Impact:** release and verification integrity rest on an unauthenticated
  native binary. A same-size solver that answers `unsat` would make every
  obligation `Proven`, and the package would still pass
  `Test-SharpProofPackagePayloads.ps1`. This violates the P0-class
  "verification integrity" property only under supply-chain compromise, so
  it is filed as P2.
- **Proposed fix:** add `archiveSha256`, `librarySha256` and
  `managedAssemblySha256` to `eng/container/toolchain.json`. Verify the
  archive hash immediately after download (and before reusing a cached
  file) in `Prepare-NativePayload.ps1`. Verify both file hashes in that
  script, in `Test-SharpProofPackagePayloads.ps1`, and at runtime in
  `ContainerContract.ResolveZ3LibraryRequired`, hashing the opened handle
  that is then passed to the loader. Include the library hash in the
  worker's binary identity, so cache keys and published versions bind it,
  and record it in `third-party-components.json`.
- **Proposed regression:** container-contract and payload tests that replace
  `libz3.so` with a same-length file of different content and expect
  rejection at each of the three checkpoints.

### B66. Worker and launcher inherit the build environment, so `DOTNET_STARTUP_HOOKS` and similar variables bypass the authenticated runtime closure

**Confidence: Confirmed by running the real worker with an injected startup hook.**

- **Location:** `SharpProof.Host/LinuxWorkerProcess.cs:53-70` (the
  `ProcessStartInfo` for the worker copies the launcher's whole environment);
  `SharpProof.Worker.Launcher/Program.cs:270-290` (`RunWorker` starts
  `dotnet <worker.dll>`); `SharpProof.BuildTasks/RunVerifier.cs:171` and
  `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:90` and `:204` (the
  launcher and supervisor also inherit MSBuild's environment). Neither
  `SharpProof.Worker` nor `SharpProof.Worker.Launcher` sets
  `<StartupHookSupport>false</StartupHookSupport>`.
- **Defect:** the launcher authenticates the worker's managed runtime
  closure (it stages it, hashes it, and refuses to run if it "changed before
  launch"), and the input hash and published versions bind that identity.
  The .NET host, however, also honors environment variables that load or
  replace code without touching those files: `DOTNET_STARTUP_HOOKS`,
  `DOTNET_ADDITIONAL_DEPS`, `DOTNET_SHARED_STORE`, `DOTNET_ROOT`/
  `DOTNET_ROLL_FORWARD` (which runtime runs the worker), `LD_PRELOAD`, and
  `LD_LIBRARY_PATH` (which native `libz3` is loaded if resolution falls back).
  None of them is cleared or recorded.
- **Observed boundary:** a trivial `InjectedHook.dll` whose
  `StartupHook.Initialize` writes to stderr, with
  `DOTNET_STARTUP_HOOKS=<path>` set, printed
  `INJECTED CODE RUNNING IN PROCESS ...\dotnet.exe ...\SharpProof.Worker.dll verify
  --request x --result y` before the worker's own usage message, running
  `SharpProof.Worker.dll` built from HEAD (on Windows; the host behavior is
  the same on Linux).
- **Impact:** any environment variable set in the build (by a CI step, a
  shell profile, or a compromised build tool) can run arbitrary code inside
  the worker and rewrite verdicts to `Proven`. The result still carries the
  authenticated worker binary hash and passes launcher validation. This
  sidesteps exactly the integrity property the closure snapshot is meant to
  provide. It is a local-environment trust issue rather than a remote
  attack, so it is filed as P2.
- **Proposed fix:** set `<StartupHookSupport>false</StartupHookSupport>` in
  both executables. Start the worker and launcher with an explicit minimal
  environment: clear `startInfo.Environment`, then copy an allowlist
  (`PATH`, `HOME`, `TMPDIR`, `SHARPPROOF_CONTAINER*`, `SHARPPROOF_NATIVE_ROOT`,
  and `DOTNET_CLI_TELEMETRY_OPTOUT`), and set `DOTNET_ROOT` to the validated
  host directory. Fail closed if `LD_PRELOAD` or `DOTNET_STARTUP_HOOKS` are
  present in the launcher's own process.
- **Proposed regression:** a launcher test that sets `DOTNET_STARTUP_HOOKS`
  and `DOTNET_ADDITIONAL_DEPS` to a marker assembly and asserts that the
  worker does not load it (the marker file is not created) and that the
  verdicts are unchanged, plus a test that the child environment contains
  only allowlisted keys.

### B72. A contract in a top-level program makes the launcher reject the whole project

**Confidence: Confirmed by running the collector and `CompilerSourceRebinding.Validate` (by reflection) on real manifests.**

- **Location:** `SharpProof.CompilerArtifact/CompilerSourceRebinding.cs:57-61`
  (a `Callable` authority must satisfy `IsDeclaration(span) || IsEnsuresInvocation(span)`)
  and `:258-261` (`IsDeclaration` requires the span's *last character* to be
  `}` or `;`), called from `SharpProof.Worker.Launcher/Program.cs:1191`
  before any worker runs. The callable authority for the synthesized
  `<Main>$` entry point is the span of the whole `CompilationUnitSyntax`
  (created in `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs:221-235`
  from the callable's declaration syntax).
- **Defect:** for a top-level program, the owning syntax is the compilation
  unit, whose span ends after the final newline or trailing comment. The
  rebinding shape check therefore sees `\n` (or the end of a comment) as the
  last character and rejects the authority. The launcher maps the resulting
  `InvalidDataException` to "SharpProof launcher input is invalid" (exit
  code 2), so verification of the entire project fails, not just the one
  claim. `docs/analysis-limits.md` and `docs/coverage-and-limits.md`
  explicitly list top-level claims as manifested.
- **Observed boundary:** with the collector probe (console application
  output):
  - `using SharpProof.Attributes; Contract.Ensures(true); return;` followed
    by a newline, the same with an extra statement and trailing comment, and
    one followed by a type declaration all compiled without errors. Each
    produced a `Callable` authority `M:Program.<Main>$(System.String[])`
    spanning the whole file, and `CompilerSourceRebinding.Validate` threw
    `InvalidDataException: A compiler source location is not bound to its
    owner's syntax.`
  - The identical program without a trailing newline passed.
  - The in-process worker, which does not run rebinding, handled the claim
    normally (`Unknown(UnsupportedCallable)`).
- **Impact:** any project that puts a `Contract.Ensures`/`Requires` in
  `Program.cs` top-level statements (the default .NET console template)
  cannot be verified at all. Strict builds fail with an input-validation
  error that points neither at the file nor at the construct.
- **Proposed fix:** record the callable authority for top-level entry points
  as the span of the last `GlobalStatementSyntax` (or of the contract
  prologue), which ends in `;` or `}`. Alternatively, make
  `IsDeclaration` skip trailing whitespace and comments lexically, and accept
  a span that covers exactly `[0, tree.TextLength)` for the `<Main>$` callable.
- **Proposed regression:** a launcher/package test with a console project
  whose `Program.cs` top-level statements begin with `Contract.Ensures(...)`
  and end with a newline and a comment. The run must reach the worker and
  report the claim (currently `UnsupportedCallable`) instead of failing
  input validation.

## P3 - Low

### B5. Equivalent congruence intervals lack one canonical representation

**Confidence: Confirmed domain-precision defect.**

- **Location:** `SharpProof.Dataflow/IntervalDomain.cs:62-83` and `:111-120`.
- **Defect:** endpoint normalization does not consistently identify the first
  or last representable signed-64-bit value satisfying a congruence. Equivalent
  carrier sets can compare unequal and fail an inclusion check.
- **Observed boundary:** exact unchanged interval/domain/guard sources were
  compiled in memory in the canonical container, without emitted files.
  Equivalence was false for both `Create(null, 0, 3, 0)` versus
  `Create(long.MinValue + 2, 0, 3, 0)` and `Create(0, null, 3, 0)` versus
  `Create(0, long.MaxValue - 1, 3, 0)`. A no-op
  `AssumeAtLeast(a, long.MinValue + 1)` also returned an inequivalent result.
  Equal-range equivalence was true, and an outside-carrier singleton was bottom.
  These results establish domain precision, not a downstream false proof.
- **Proposed fix:** normalize endpoints against the first and last congruent
  carrier values, selecting one representation for equivalent open/bounded ends.
- **Proposed regression:** assert equality, equal hashes, and mutual inclusion
  for both pairs and no-op refinements. Retain neighboring-residue, genuinely
  different interval, empty, singleton, and outside-carrier controls.

### B20. SARIF assumption results pair `kind: review` with `level: note`

**Confidence: High. Evidence: source trace against the SARIF 2.1.0 rule.**

- **Location:** `SharpProof.Worker.Launcher/SarifProjection.cs:150-171`
  (`AssumptionResult`), using `LauncherPresentation.Level` in
  `SharpProof.Worker.Launcher/LauncherProjections.generated.cs:64-76`.
- **Defect:** `Level(WorkerAssumptionPolicy.Allow, "note")` returns `"note"`,
  and `AssumptionResult` then emits `kind = "review"` with `level = "note"`.
  SARIF 2.1.0 section 3.27.9 requires `level` to be `"none"` whenever `kind`
  is present with any value other than `"fail"`. `allow` is the default
  assumption policy for the advisory profile
  (`SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets`), so any
  selected callable with `Contract.Assume` or a trusted boundary produces a
  non-conforming result in the default configuration. Claim results already
  follow the rule: advisory `Unknown` uses `review`/`none`
  (`SarifProjectionTests.cs:176`, `LauncherArgumentTests.cs:1625`).
- **Impact:** strict SARIF consumers (for example, GitHub code scanning
  upload validation or the SARIF multitool validator) may reject or
  misclassify the published SARIF file. Verification outcomes are unaffected.
- **Proposed fix:** in `AssumptionResult`, emit `("review", "none")` for the
  `Allow` policy (keeping the note-level intent in a property if needed), and
  `("fail", "warning")` / `("fail", "error")` for `Warn`/`Error`, e.g.
  `var (kind, level) = request.AssumptionPolicy switch { Allow => ("review",
  "none"), Warn => ("fail", "warning"), Error => ("fail", "error") };`.
- **Proposed regression:** add `AssumptionResult` cases for all three
  assumption policies to `SharpProof.Package.Test/SarifProjectionTests.cs`,
  asserting that every result with `kind != "fail"` has `level == "none"`.
  A generic assertion over all emitted results would also cover future
  presentations.

### B21. `LinuxProcessStatParser.TryParse` throws on a stat line ending at `)`

**Confidence: High for the parser; Low for reachability with a real procfs.**

- **Location:** `SharpProof.Host/LinuxProcessStatParser.cs:18-26`; callers
  `SharpProof.Host/LinuxWorkerProcess.cs:394-410` (`TryReadProcessStat`) and
  `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:459-488`
  (`ReadProcessParents`).
- **Defect:** after `var closeName = stat.LastIndexOf(')')`, the parser calls
  `stat.AsSpan(closeName + 2)`. When `)` is the last character (for example a
  truncated `"123 (x)"`), `closeName + 2` exceeds the string length and
  `AsSpan` throws `ArgumentOutOfRangeException` instead of returning `false`.
  Both callers catch only I/O and access exceptions, so the exception escapes
  descendant capture during worker termination and supervisor cleanup.
- **Reachability:** a Linux kernel always writes the fields after the command
  name in one `/proc/<pid>/stat` read, so this requires a truncated or
  nonstandard procfs. It is a latent contract violation of a `Try*` API used
  on the containment path, where an unexpected throw can skip killing
  captured descendants.
- **Proposed fix:** return `false` when `closeName + 2 > stat.Length` (or use
  `stat.AsSpan(closeName + 1).TrimStart()`), before splitting.
- **Proposed regression:** add parser cases for `"123 (x)"`, `"123 (x) "`, and a
  command name containing `)`, asserting `false` or correct fields without
  throwing.

### B22. Attempt-scoped artifact names make "Re-run failed jobs" unusable

**Confidence: High from workflow source and GitHub's `run_attempt` semantics;
no workflow was executed.**

- **Location:** `.github/actions/prepare-qualified-packages/action.yml:17`;
  `.github/workflows/package-consumers.yml:57`, `:81`, `:109`, `:117`, `:143`,
  `:187`, and `:222`.
- **Defect:** every uploaded and downloaded artifact name embeds
  `${{ github.run_attempt }}`. GitHub increments `run_attempt` for the whole
  run on any re-run, but "Re-run failed jobs" does not re-run upstream jobs
  that already succeeded. A re-run of `container-verifier`,
  `release-qualification`, `publish-private-preview`, or `publish` therefore
  downloads `nuget-packages-<sha>-<N+1>` (and
  `package-consumer-qualification-<sha>-<N+1>` / `portable-receipt-*-<N+1>`),
  which only exist if `package` and the other producers were also re-run.
  The download step fails.
- **Impact:** a transient failure in a late job (for example the NuGet
  service-index request in `publish`, before any package is pushed) cannot be
  retried on its own. The operator must re-run every job, which repeats
  packing, pilots, and qualification and, together with B16, needs a new
  human pilot review. Nothing is documented about this constraint. This fails
  closed; it does not publish wrong bytes.
- **Proposed fix:** name artifacts by `github.run_id` and `github.sha` only
  (or pass the producing attempt explicitly through job outputs), and keep
  per-attempt uniqueness only where overwriting within a run is a real risk.
  If attempt scoping is intentional, document that only "Re-run all jobs" is
  supported and fail early with a clear message.
- **Proposed regression:** add a workflow lint that every
  `download-artifact` name is produced by an `upload-artifact` in a job listed
  (transitively) in `needs:` using the same naming inputs that a partial
  re-run would see.

### B23. SPMETA002 misses wrapped mutable statics and most analyzer/verifier layers

**Confidence: Confirmed meta-analyzer boundary; no current product violation
found.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:745-749`
  (`IsForbiddenMutableStaticStorage`), `:777-783` (value types return "not
  mutable" before inspecting fields), `:872-885` (`System.Collections.Immutable`
  and `Frozen` types are immutable regardless of element type), and `:975-982`
  (`IsCriticalStateNamespace`).
- **Defect:** the rule message says mutable static storage is forbidden in
  "analyzer, frontend, and verifier layers", but:
  1. only `SharpProof.Analyzer`, `Frontend`, `Verify`, `Meta.Analyzers`, and
     `ContractForGenerator` namespaces are checked. `SharpProof.Effects`,
     `Contracts`, `Dataflow`, `Ir`, and `Specs` ship inside the analyzer
     package, and `SharpProof.Smt`, `Summaries`, and `Worker` are verifier
     layers, yet none of them is covered;
  2. any value type is treated as immutable, so a `static readonly` tuple,
     `KeyValuePair`, or struct holding a mutable reference passes;
  3. an immutable collection is treated as immutable even when its elements
     are mutable (`ImmutableArray<int[]>`,
     `ImmutableDictionary<string, List<int>>`).
- **Observed boundary:** the meta analyzer built from unchanged HEAD sources
  ran on a probe file. It reported SPMETA002 only for the two controls
  (`static readonly List<int>` and `static int` in `SharpProof.Analyzer.Probe`).
  It was silent for `static readonly (List<int> Items, int Count)`, a
  `static readonly` struct with an `int[]` field, `ImmutableArray<int[]>`,
  `ImmutableDictionary<string, List<int>>`, `KeyValuePair<string, List<int>>`,
  and for `static int` / `static readonly List<int>` fields declared in
  `SharpProof.Effects.Probe` and `SharpProof.Smt.Probe`.
- **Current state:** a search of the uncovered layers found only
  `[ThreadStatic]` guards, `Interlocked` scope counters, and
  `ConditionalWeakTable` caches, which are benign. The gap would let a future
  shared mutable static in these layers (for example a cross-compilation cache
  keyed by symbol text) reach concurrent analysis without a build error.
- **Proposed fix:** extend `IsCriticalStateNamespace` to every assembly
  bundled into the analyzer and worker (`Effects`, `Contracts`, `Dataflow`,
  `Ir`, `Specs`, `Smt`, `Summaries`, `CompilerArtifact`, `CompilerCollector`,
  `Worker`), keeping the existing allowances for `[ThreadStatic]`, `Interlocked`
  counters (for example through an explicit attribute or allowlist), and
  compilation-scoped weak caches. In `IsMutableStorageType`, recurse into
  value-type instance fields and into the type arguments of immutable and
  frozen collections and `KeyValuePair`/`ValueTuple` instead of returning
  `false`.
- **Proposed regression:** add Meta.Analyzers tests for each silent case above,
  plus controls that `ImmutableArray<int>`, `ImmutableDictionary<string, int>`,
  `static readonly (int, string)`, `[ThreadStatic]` fields, and
  `ConditionalWeakTable` caches remain accepted.

### B24. A shared per-compilation `Lazy` captures and caches the first caller's cancellation

**Confidence: Confirmed mechanism by reflection; no standard Roslyn host
path reproduced.**

- **Location:** `SharpProof.Effects/EffectCallPreconditionPolicy.cs:18-21`
  (static `CompanionTypes` table of `Lazy<ImmutableHashSet<INamedTypeSymbol>>`),
  `:58-70` (the `Lazy` factory captures the constructor's `cancellationToken`
  with `LazyThreadSafetyMode.ExecutionAndPublication`), `:123` (evaluation),
  and `FindTypesWithCompanions` at `:209-251`, which calls
  `ThrowIfCancellationRequested` on the captured token.
- **Defect:** the first policy created for a `Compilation` stores a `Lazy` in
  a static table, and its factory uses that first caller's token. Any later
  policy for the same `Compilation` reuses the `Lazy` but not its own token.
  If the first token is cancelled before or during evaluation, the factory
  throws `OperationCanceledException`, and `ExecutionAndPublication` caches
  that exception permanently. Every later query on that `Compilation` then
  rethrows cancellation even though its own token was never cancelled.
- **Observed boundary:** a reflection probe against the analyzer's
  `SharpProof.Effects.dll` built from unchanged HEAD sources created policy A
  with a live token, cancelled it without querying, then created policies B and
  C with `CancellationToken.None` on the same compilation.
  `HasPotentialPreconditions` threw `OperationCanceledException` for both
  `IThing.Get` and an unrelated `Other.Plain`. The control (no earlier
  cancelled policy) returned `True` and `False`.
- **Reachability:** a 201-delay end-to-end loop through
  `CompilationWithAnalyzers` (cancel the first run, re-analyze the same
  `Compilation` object without cancellation) did not reproduce it, because
  `CompilationWithAnalyzers` analyzes a cloned compilation per run; the table
  had no entry for the caller's `Compilation`. Hosts that reuse one analyzed
  compilation across analyzer sessions with different tokens (custom drivers,
  gate or test hosts, future collector reuse) would be affected. Within one
  driver run all sessions share one token.
- **Proposed fix:** do not capture a caller token in shared caches. Compute
  the companion set without cancellation inside the cached factory (or with
  `CancellationToken.None`) and check the caller's own token outside it; or
  use `LazyThreadSafetyMode.PublicationOnly`, or a `ConditionalWeakTable`
  factory, which do not cache exceptions. Audit the other per-compilation
  caches for the same pattern.
- **Proposed regression:** construct a policy with a token, cancel it,
  construct a second policy with `CancellationToken.None` for the same
  compilation, and assert that queries return the control results.

### B25. A null lowered `callableId` escapes typed manifest rejection

**Confidence: Confirmed by mutation fuzzing of the real deserializer.**

- **Location:** `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:656`
  in `HasFeatureScopeParity` (reached from `HasValidFeatureScope` at `:554`),
  translated by `SharpProof.Worker/WorkerInputSnapshot.cs:44-47` and then
  classified by `SharpProof.Worker/SharpProofWorker.cs:144-147`.
- **Defect:** `loweredById.ContainsKey(lowered.CallableId)` runs before any
  check that `CallableId` is non-null, so a lowered callable with
  `"callableId": null` throws `ArgumentNullException`. `WorkerInputSnapshot`
  translates only JSON, invalid-data, and decoder exceptions into
  `ManifestInvalid`. The generic `ArgumentException` handler in
  `SharpProofWorker` then reports `InputUnavailable` /
  `input.unavailable` ("The compiler artifact or a referenced image could not
  be loaded") instead of the typed `CompilerManifestMismatch` /
  `compiler_manifest.invalid` rejection used for every other malformed
  manifest. This is the same validation-order class as B4.
- **Observed boundary:** a reflection fuzzer applied 11,000 one- and
  two-node mutations (null, wrong types, empty containers, extreme integers) to
  a real 159 KB collector manifest and invoked the internal
  `CompilerManifestArtifactJson.Deserialize` built from HEAD sources. No
  mutated manifest was accepted. Every rejection was a `JsonException` or
  `InvalidDataException` except 11 cases, all
  `ArgumentNullException @ HasFeatureScopeParity`, each containing
  `$.callables[i].callableId=null`.
- **Impact:** a corrupted or tampered artifact is reported as unavailable input
  rather than a manifest mismatch, so the failure reason and error code
  consumers rely on are wrong. It still fails closed.
- **Proposed fix:** in the lowered-callable loop, return `false` when
  `lowered.CallableId` is null or blank before using it as a dictionary key
  (`if (lowered?.CallableId is not { Length: > 0 } id || !loweredById.TryAdd(id,
  lowered)) return false;`). Consider also translating `ArgumentException`
  thrown by `Deserialize` into `ManifestInvalid` in `WorkerInputSnapshot`.
- **Proposed regression:** add a `CompilerManifestArtifactTests` case that
  nulls one lowered `callableId` and asserts `JsonException` from
  `Deserialize` and `CompilerManifestMismatch` from the worker.

### B26. Any negative `currentStateVariable` is accepted as "none"

**Confidence: Confirmed by resealed mutation fuzzing.**

- **Location:** `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:538`
  (decoder: `row.CurrentStateVariable < 0 ? null : Variable(...)`); the encoder
  at `:198-199` always writes `-1` for "none".
- **Defect:** the manifest format is meant to have one canonical encoding.
  `Deserialize` rejects non-canonical JSON, and the IR codec re-encodes and
  byte-compares its graphs. The lowered-variable decoder, however, maps every
  negative value (`-2`, `int.MinValue`, ...) to "no current-state variable",
  and the round-trip check only reserializes the same object, so `-2`
  survives. Semantically identical artifacts therefore have distinct accepted
  wire forms, feature-scope seals, compilation input hashes, and cache keys.
- **Observed boundary:** the resealing fuzzer (mutate `callables`, recompute
  `FeatureScopeSha256` with the internal `CompilerFeatureScopeFingerprint`,
  serialize with `SerializeValidated`, then `Deserialize` and
  `DecodeCallables`) accepted `variables[i].currentStateVariable = -2` on
  parameter and receiver rows of three different callables across three seeds.
  The accepted canonical JSON differed from the original while decoding to
  the same model.
- **Impact:** canonicalization and identity only; no proof consequence. The
  seal is unkeyed, so this is reachable only by editing the artifact.
- **Proposed fix:** accept exactly `-1` as the sentinel:
  `IrVarId? current = row.CurrentStateVariable switch { -1 => null, >= 0 =>
  Variable(row.CurrentStateVariable), _ => throw new InvalidDataException(
  "A lowered canonical variable is invalid.") };`. More generally, have
  `Deserialize` compare the wire artifact with the re-encoding of its decoded
  model, as `PortableIrGraphCodec.RequireCanonicalEncoderImage` already does.
- **Proposed regression:** mutate one row to `-2` and to `int.MinValue`,
  reseal, and assert rejection; keep the `-1` control.

### B28. Vacuous proofs are printed and reported as ordinary proofs

**Confidence: Confirmed worker vacuity output; launcher and SARIF
presentation source-traced.**

- **Location:** `SharpProof.Worker.Launcher/Program.cs:476-488` (console claim
  lines) and `SharpProof.Worker.Launcher/SarifProjection.cs:88-127`
  (`ClaimResult` message, `kind`, and `level`). Nothing in
  `SharpProof.Worker.Launcher` reads `WorkerClaimResult.Vacuity`.
- **Defect:** `SEMANTICS.md` and `docs/unknown-reasons.md` state that
  `ContradictoryPreconditions` and `NoModeledNormalReturn` "make
  partial-correctness vacuity visible rather than silently presenting the
  result as an ordinary proof". The worker does record vacuity (for example
  `Contract.Requires(false)` gave `outcome=Proven vacuity=ContradictoryPreconditions`
  and `Contract.Assume(false)` gave `vacuity=NoModeledNormalReturn` in this
  pass). The launcher then prints the same `SharpProof Proven <callable>
  postcondition claim <id>` line as for a real proof. SARIF emits
  `kind: "pass"`, `level: "none"`, and a message without vacuity; only the
  nested `properties.result.vacuity` JSON carries it. `RequireProven` accepts
  vacuous proofs without any diagnostic.
- **Impact:** a method whose preconditions can never hold (for example a
  domain-disjoint `[InRange]` on an `int`, or contradictory `Requires`) passes
  strict verification and looks fully proven in build output and code-scanning
  views. No false proof in the partial-correctness sense, but the promised
  visibility is missing on every user-facing surface except raw JSON.
- **Proposed fix:** append the vacuity kind to the console line (for example
  `SharpProof Proven ... claim c0 [vacuous: ContradictoryPreconditions]`),
  include it in the SARIF message text, and emit SARIF `kind: "review"` (with
  `level: "none"`) for vacuous proofs. Optionally add a policy switch so
  `RequireProven` reports vacuous proofs as warnings.
- **Proposed regression:** launcher and SARIF tests with one
  `ContradictoryPreconditions` and one `NoModeledNormalReturn` claim,
  asserting the vacuity text appears; keep the ordinary-proof control.

### B29. `SP0027` messages embed nondeterministic internal IR identifiers

**Confidence: Confirmed across repeated analyzer runs.**

- **Location:** `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs:615-652`
  (`CompleteEvaluation` passes `FormatCondition(printer,
  evaluation.Condition)` as the second message argument, and `FormatCondition`
  prints the bound IR term with `IrPrinter`).
- **Defect:** the message argument is the IR printer's rendering of the bound
  clause, for example `'(v14 > 0)'` or
  `'(v8 != (("<no-assembly>::System.Int32[]"#t4)null))'`. Variable (`vN`) and
  type (`#tN`) numbers are IR factory allocation ordinals, which depend on
  how many terms earlier analysis created, and therefore on concurrent
  scheduling and on the rest of the compilation. The message also exposes
  internal type identities instead of the user's parameter names.
- **Observed boundary:** a determinism harness combined the fifth-pass probe
  files (one namespace each) into one compilation and ran the real analyzer
  once sequentially and 23 more times with concurrent execution. The diagnostic
  IDs and locations were identical in every run, but the `SP0027` message
  text differed in every concurrent run (for example `v26`/`v17`/`v13` for the
  same call, and `#t4`/`#t7`/`#t12` for the same array type). After replacing
  `vN` with a placeholder, the only remaining difference was the `#tN` type
  ordinal.
- **Impact:** build logs, SARIF, IDE, and CI diagnostics for the same code
  change text between runs and machines. That defeats baseline and
  suppression tooling keyed on message text and makes the message
  meaningless to users (`v14` instead of `value`).
- **Proposed fix:** render the precondition from the source clause, for
  example the `Contract.Requires` argument syntax
  (`clause.Syntax.ArgumentList.Arguments[0].ToString()`) or the closed
  attribute text (`[Positive] value`). Alternatively give `IrPrinter` a
  variable-name map from `BoundContractVariable` to parameter names and print
  types by display name, never by factory ordinal.
- **Proposed regression:** analyze the same compilation twice, once alone and
  once after unrelated analysis has warmed the factory, and assert identical
  `SP0027` messages that mention the parameter name.

### B30. One method timeout relabels every incomplete callable as a project timeout

**Confidence: High. Evidence: source trace; method timeouts observed in
worker runs of this pass.**

- **Location:** `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:294-298`
  and `:316-320` (a single `MethodTimeout` callable sets run status
  `TimedOut`); `SharpProof.Worker.Launcher/Program.cs:472-518`
  (`projectTimedOut` is derived only from `RunStatus == TimedOut`).
- **Defect:** the worker reports a per-method wall-clock timeout as run
  status `TimedOut`, which is the same status as a project timeout. The
  launcher treats every `TimedOut/None` run as a project timeout, so the
  `SP0047` incomplete-analysis diagnostic for *every* incomplete callable
  reads "Project analysis timed out for M (UnsupportedBody)" (or
  `(UnsupportedCallable)`, `(ResourceLimit)`, ...), including callables that
  were never close to a timeout. The worker runs in this pass produced
  exactly this mix (one `MethodTimeout` plus many `UnsupportedBody` callables
  in the same response).
- **Impact:** misleading build output: users are told their project timed out
  and may raise `SharpProofVerifyProjectWallTimeMilliseconds`, when the real
  causes are one slow method plus unrelated unsupported constructs.
- **Proposed fix:** derive `projectTimedOut` from the callable reasons
  (`WorkerCallableCoverageReason.ProjectTimeout` present) rather than run
  status, and print "Selected analysis is incomplete for M (reason)" for
  callables whose own reason is not `ProjectTimeout`. Alternatively give
  method-level timeouts their own run status.
- **Proposed regression:** a launcher test with one `MethodTimeout` callable and
  one `UnsupportedBody` callable must print "timed out" only for the former;
  keep a true project-timeout control.

### B31. Effect refutation replay only works for single-statement bodies

**Confidence: Confirmed end to end in the worker.**

- **Location:** `SharpProof.Effects/OperationEffectScanner.cs:1427-1433`
  (`IsDirectSyntax`) and `:1572-1580` (`GetDirectSyntax` / `SingleStatement`
  return a direct syntax only for a block with exactly one statement);
  documentation in `docs/coverage-and-limits.md:16` and `:202-208`,
  `docs/diagnostic-examples.md:78-82`, and `docs/README.md:138`.
- **Defect:** the docs promise that the worker "independently replays
  unconditional definite managed object/array allocation, exact framework
  explicit-throw, empty-`lock`, and exact-`Monitor` events". The scanner
  records a direct witness only when the method body is a single statement
  and the event is that statement. Any additional statement, even one after
  the event, removes the witness, so the claim stays
  `Unknown(EffectContractNotEstablished)` although the event executes on
  every path.
- **Observed boundary:** real collector plus in-process worker (unchanged
  HEAD, checked and unchecked projects):

  | Method body | Worker outcome |
  | --- | --- |
  | `lock (new object()) { }` (void) | `Refuted` |
  | `lock (typeof(Subject)) { }` (void) | `Refuted` |
  | `lock (new object()) { } return 1;` | `Unknown` |
  | `lock (typeof(Subject)) { } return 1;` | `Unknown` |
  | `lock (new object()) { } int x = 1; x++;` | `Unknown` |
  | `=> new object()` / `=> new int[3]` | `Refuted` |
  | `var o = new object(); return 1;` / `_ = new object();` | `Unknown` |

  The worker's own `DirectWriteAndCapabilityClaimsFailClosedWithoutReplayTraces`
  test uses the single-statement form.
- **Impact:** the replay feature is much narrower than documented, so almost
  all definite violations in realistic methods are reported only as `Unknown`.
  Fail-closed; no false result.
- **Proposed fix:** either document the restriction ("only when the event is
  the method's sole statement or expression body"), or generalize
  `GetUnavoidableDirectOperation` to accept the first statement of a block
  when every preceding statement is absent (the event dominates all exits and
  nothing before it can throw or return), then extend `IsDirectSyntax`
  accordingly.
- **Proposed regression:** worker tests for each two-statement form in the
  table, expecting `Refuted` after the fix (or a documented `Unknown`), with
  a conditional-event control that must stay `Unknown`.

### B32. Event accessors are documented as admitted but can never be admitted

**Confidence: Confirmed analyzer boundary.**

- **Location:** `SharpProof.Analyzer.Core/LanguageSubsetGate.cs:153-162`
  (admits `MethodKind.EventAdd`/`EventRemove`), `:136-146` (rejects any
  parameter whose type `IsUnsupportedType`), and `:302-309`
  (`TypeKind.Delegate` is unsupported); documentation in
  `docs/coverage-and-limits.md:51`.
- **Defect:** C# requires every event to have a delegate type, so the
  implicit `value` parameter of every `add`/`remove` accessor is a delegate.
  The parameter check therefore rejects every event accessor, and the
  `EventAdd`/`EventRemove` admission is dead. The documentation table lists
  "event add/remove accessors" as admitted callable kinds.
- **Observed boundary:** the real analyzer reported `SP0047 ...
  UnsupportedCallable` for `[EnforcePure]` on both accessors of
  `event Action E { add { ... } remove { } }`, including the empty `remove`.
  Property getters and setters, `init` accessors, indexers, instance and
  static constructors in the same file were all analyzed (`SP0002`).
- **Impact:** users annotating event accessors (a documented, admitted shape)
  always get an incomplete-analysis diagnostic; under `RequireProven` this is
  a build failure with no way to satisfy it.
- **Proposed fix:** either remove "event add/remove accessors" from the
  admitted list in the docs and the gate, or exempt the accessor's implicit
  `value` parameter from the delegate-type rejection (treating it as an
  opaque reference that is only stored or combined), with the effect scanner
  modeling `Delegate.Combine`/`Remove` and field-like storage conservatively.
- **Proposed regression:** an analyzer test annotating a custom event accessor,
  asserting the documented outcome (analyzed, or a documented rejection
  reason).

### B35. `nameof(TypeName)` makes a selected method unsupported

**Confidence: Confirmed analyzer boundary.**

- **Location:** `SharpProof.Analyzer.Core/LanguageSubsetGate.cs:59-71` (every
  walked operation must classify as exact) and `:92` (`WalkCallableBoundary`
  descends into `nameof` arguments); documentation in
  `docs/coverage-and-limits.md:54` lists `nameof` as admitted.
- **Defect:** Roslyn represents a type-name operand of `nameof` as a child
  operation of kind `None`. The gate walks into the argument of the
  `INameOfOperation` and rejects that `None` child, although `nameof` is a
  compile-time constant whose argument is never evaluated. (The effect
  scanner already treats `INameOfOperation` as effect-free at
  `SharpProof.Effects/OperationEffectScanner.cs:268`.)
- **Observed boundary:** the real analyzer reported `SP0047 ...
  UnsupportedOperationKind (None)` for `[EnforcePure] string M() =>
  nameof(Plain);` and `nameof(G)`, while `nameof(x)` (a parameter) and
  `nameof(Plain.V)` (a member) were accepted.
- **Impact:** false incomplete-analysis diagnostics for common logging and
  exception-message code (for example `throw new ArgumentException(...,
  nameof(Options))`); under `RequireProven` this fails the build.
- **Proposed fix:** in `WalkCallableBoundary`, yield an `INameOfOperation` but
  do not descend into its children (or treat any operation whose constant
  value is known and whose ancestor is `INameOfOperation` as exact).
- **Proposed regression:** analyzer tests for `nameof` over a type, a generic
  type, a namespace-qualified type, a parameter, and a member, all accepted.

### B36. SPMETA009 misses C# expression text built by formatting APIs

**Confidence: Confirmed meta-analyzer boundary; no current product violation
found.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:112-118`
  (the rule is registered only for binary `+`, compound `+=`, and
  interpolated strings) and `:445-493` (`AppendCSharpExpressionShape` follows
  only `+` and `string.Concat`).
- **Defect:** the rule forbids synthesizing C# expression text such as
  `" == "`, `"=>"`, or `"?."` in soundness-critical layers, but it does not
  inspect `string.Format`, `string.Join`, `StringBuilder.Append`, or
  `string.Replace`, which produce the same text.
- **Observed boundary:** the meta analyzer built from unchanged HEAD reported
  SPMETA009 for `a + " == " + b` and `$"{a} == {b}"` in
  `SharpProof.Frontend.Probe`, but was silent for
  `string.Format("{0} == {1}", a, b)`, `string.Format("{0} => {1}", a, b)`,
  `string.Join(" == ", a, b)`, a `StringBuilder` chain appending `" == "`, and
  `"x == y".Replace("x", a)`. A search of the guarded layers found no current
  use of these APIs to build expression text.
- **Impact:** a future change could build C# expression text through these
  APIs without the guard noticing, reintroducing string-based semantic
  identity. Guard gap only; there is no current violation.
- **Proposed fix:** also analyze invocations of `string.Format`,
  `string.Join`, `StringBuilder.Append`/`AppendFormat`/`Insert`, and
  `string.Replace` whose constant arguments contain a forbidden fragment (for
  example by extending `IsStringConcat` into a small catalog of text-producing
  methods and scanning their constant string arguments).
- **Proposed regression:** add each silent form above to the Meta.Analyzers
  tests, with controls for unrelated formatting (`string.Format("{0}, {1}",
  ...)`).

### B37. SPMETA004 misses comparer, ordinal, and collection comparisons

**Confidence: Confirmed meta-analyzer boundary; no current product violation
found.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:286-305`
  (`==`/`!=` only), `:307-337` (`AnalyzeSemanticStringInvocation` accepts only
  `string` methods named `Contains`/`EndsWith`/`Equals`/`StartsWith` and
  `object.Equals`), and `:339-362` plus `:127-128` (constant patterns and
  `case` labels).
- **Defect:** the rule is meant to stop reason or provenance literals (`ir.*`,
  `ir_*`) from controlling semantic behavior, but equivalent comparisons
  through other APIs are not inspected.
- **Observed boundary:** the meta analyzer built from unchanged HEAD flagged
  only `r == "ir.unsupported"` in `SharpProof.Frontend.Probe`. It was silent for
  `StringComparer.Ordinal.Equals(r, "ir.unsupported")`,
  `string.CompareOrdinal(r, "ir.unsupported") == 0`,
  `string.Compare(r, "ir.unsupported", StringComparison.Ordinal) == 0`,
  `r.IndexOf("ir.", StringComparison.Ordinal) >= 0`,
  `dictionary.ContainsKey("ir.unsupported")`,
  `set.Contains("ir.unsupported")`,
  `r.AsSpan().SequenceEqual("ir.unsupported".AsSpan())`, and
  `EqualityComparer<string>.Default.Equals(r, "ir.unsupported")`. No guarded
  layer currently contains an `ir.`/`ir_` literal.
- **Impact:** a future comparison of semantic `ir.` identifiers through
  these forms would bypass the rule. Guard gap only; there is no current
  violation.
- **Proposed fix:** report any invocation (not only `string` predicates) that
  receives a semantic literal as an argument or receiver and returns `bool` or
  an integer used in a comparison, excluding known non-semantic sinks
  (diagnostic message construction, logging). At minimum add
  `StringComparer`/`EqualityComparer<T>` `Equals`, `string.Compare*`,
  `IndexOf`/`LastIndexOf`, `ContainsKey`/`Contains`/`TryGetValue` on
  collections, and span `SequenceEqual`.
- **Proposed regression:** Meta.Analyzers tests for each silent form above,
  with a control that passing an `ir.*` literal to a diagnostic message
  builder is not reported.

### B38. SPMETA011 misses reflective construction of proof outcomes

**Confidence: Confirmed meta-analyzer boundary; no current product violation
found.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:261-270`
  (SPMETA011 inspects only `IObjectCreationOperation`) and `:75` (the
  forbidden-API table lists only `RuntimeHelpers.GetUninitializedObject`).
- **Defect:** the trusted-kernel rule says `ProvenOutcome`, `RefutedOutcome`,
  and `ValidatedModel` may only be created by `ProofKernel`. Only `new`
  expressions and `RuntimeHelpers.GetUninitializedObject` are detected.
  Reflective creation, which is the only way to reach the `internal`
  constructors (`SharpProof.Verify/DeclarativeModels.generated.cs:64`, `:73`,
  `:82`) from outside `SharpProof.Verify`, is not.
- **Observed boundary:** with stub `SharpProof.Verify` types (as the existing
  meta tests use), the meta analyzer built from unchanged HEAD flagged
  `new ProvenOutcome()` (SPMETA011) and `RuntimeHelpers.GetUninitializedObject`
  (SPMETA001) in `SharpProof.Worker.Probe`, but was silent for
  `Activator.CreateInstance(typeof(ProvenOutcome))`,
  `Activator.CreateInstance<ProvenOutcome>()`,
  `typeof(ProvenOutcome).GetConstructor(...).Invoke(...)`,
  `FormatterServices.GetUninitializedObject(typeof(ProvenOutcome))`, and
  `JsonSerializer.Deserialize<ProvenOutcome>(...)`. No production code uses
  these APIs.
- **Impact:** reflective or deserialization-based construction could forge
  proof outcomes outside `ProofKernel` without a meta diagnostic. Guard gap
  only; there is no current use.
- **Proposed fix:** report any `typeof(X)` or generic type argument naming a
  kernel-only type (`ProvenOutcome`, `RefutedOutcome`, `ValidatedModel`,
  `Assumption`, `EffectSummary`) outside its allowlisted owner when passed to
  `Activator`, `ConstructorInfo.Invoke`, `FormatterServices`, or serializer
  `Deserialize` APIs; also add `FormatterServices.GetUninitializedObject` and
  `Activator.CreateInstance` to the SPMETA001 table for soundness-critical
  layers.
- **Proposed regression:** Meta.Analyzers tests for each silent form above,
  with controls that `ProofKernel` itself and unrelated `Activator` uses stay
  silent.

### B39. SPMETA010 recognizes caches only by type name

**Confidence: Confirmed meta-analyzer boundary; no current product violation
established.**

- **Location:** `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:467-470`
  (`IsCacheType` is `type.Name` containing `"Cache"`), used by
  `IsCacheReceiver` at `:565-600` and `IsCacheAssignmentTarget` at `:439-458`.
- **Defect:** the rule forbids writing `Unknown`, timeout, error, or failure
  answers into a semantic cache, but a cache is recognized only when the
  receiver's type name contains `Cache` (or a local resolves to such a type).
  Memoization through a general-purpose dictionary field is invisible, even
  when the field is named `_cache`.
- **Observed boundary:** the meta analyzer built from unchanged HEAD reported
  SPMETA010 for `VerificationCache.Store(k, ProofOutcomeKind.Unknown)` in
  `SharpProof.Worker.Probe`, but was silent for
  `ConcurrentDictionary<string, ProofOutcomeKind> _cache` written with
  `_cache[k] = Unknown`, `_cache.TryAdd(k, Unknown)`, and
  `_cache.GetOrAdd(k, _ => Unknown)`, and for a `Dictionary` field `_memo`
  written with `Add` and the indexer.
- **Impact:** a new cache that is not named like `VerificationCache` could
  persist `Unknown`/refutation outcomes that SPMETA010 is meant to forbid,
  without a diagnostic. Guard gap only.
- **Proposed fix:** treat a field or property as a cache receiver when its
  type is a mutable dictionary or `ConditionalWeakTable` (or `Lazy`) and it is
  static or an instance field of a long-lived type, or when its name contains
  `cache`/`memo` (case-insensitive); include indexer assignment targets.
  Keep an explicit opt-out attribute for per-request memo tables whose unknown
  answers are intentionally request-scoped.
- **Proposed regression:** Meta.Analyzers tests for each silent form above,
  with a control that storing `Proven`/`Refuted` stays silent.

### B40. Changed-line coverage exempts code wrapped in block comments

**Confidence: Confirmed by executing the actual classifier.**

- **Location:** `scripts/Test-SharpProofCoverage.ps1:29-45`
  (`Test-ClearlyNonSemanticSourceLine`), applied to every changed TCB line at
  `:731-737`.
- **Defect:** a changed line counts as trivia (and needs no coverage) when its
  trimmed text starts with `/*` and ends with `*/`. The check does not require
  the comment to span the whole line, so any statement between a leading and
  a trailing block comment is exempt from the release-delta coverage
  requirement for trusted-computing-base files.
- **Observed boundary:** the function extracted from the script's AST and run
  directly returned `True` (non-semantic) for
  `/* a */ DangerousCall(); /* b */` and `/**/ return proven; /**/`, as it
  correctly did for `// comment`, `}`, and `/* only a comment */`, and `False`
  for plain `DangerousCall();`.
- **Impact:** a TCB line formatted this way is dropped from the changed-line
  denominator and from the published `uncoveredLines` evidence, so it can
  never be shown as uncovered and it inflates the changed-TCB percentage
  (checked against `minimumChangedTcbLinePercent`, 73.32 in
  `eng/coverage/baseline.json`, despite the workflow step being named "Enforce
  full release-delta coverage"). The same heuristic would also treat a line
  that closes one comment and opens another (`*/ code /*`) as trivia. Because
  the gate is percentage-based and the shape is unusual, the practical effect
  is limited.
- **Proposed fix:** classify a line as trivia only if removing all complete
  `/* ... */` spans and a trailing `// ...` leaves nothing but whitespace or a
  lone brace, for example with the Roslyn lexer (`SyntaxFactory.ParseTokens`)
  or a regex such as `^\s*(/\*.*?\*/\s*)*(//.*)?$` applied after brace
  handling.
- **Proposed regression:** add the two exempted forms above, plus
  `*/ code /*` and `/* a */ { /* b */`, to the coverage script fixture tests,
  requiring coverage for the first three and none for the last.

### B43. SPMETA005 descriptor-catalog guard is bypassed by file name or type name

**Confidence: Confirmed by running the meta analyzer.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:220-235`
  (`AnalyzeObjectCreation`, the `DiagnosticDescriptor` branch), and the
  backstop test
  `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:416-456`
  (`DiagnosticDescriptorsComeOnlyFromTheGeneratedCatalog`).
- **Defect:** the analyzer exempts any creation whose syntax-tree file path
  ends with `.generated.cs`, and any creation in a type *named*
  `ContractForDiagnosticDescriptors` in any namespace. The second check comes
  in addition to the symbol comparison against the real
  `KnownType.ContractForDiagnosticDescriptors`. The architecture backstop
  scans only `SharpProof.Analyzer.Core` and `SharpProof.Meta.Analyzers`,
  skips files ending in `DiagnosticDescriptors.generated.cs`, and matches only
  the regex `new\s+DiagnosticDescriptor\s*\(`. That regex misses target-typed
  `new(...)`.
- **Observed boundary:** with the meta analyzer built from HEAD, the field
  `static readonly DiagnosticDescriptor D = new("SP9999", ...)` in namespace
  `SharpProof.Analyzer`, with the path
  /src/SharpProof.Analyzer/RogueDescriptors.generated.cs, produced no
  diagnostic. The same declaration in a class
  `SharpProof.Analyzer.Rogue.ContractForDiagnosticDescriptors` produced no
  diagnostic, while the identical control class in the same file reported
  `SPMETA005`. The first case also passes the architecture regex because it
  uses target-typed `new`.
- **Impact:** a hand-written descriptor can sit outside the generated catalog.
  It can carry an ID, severity, or help link that bypasses the catalog's
  stability and documentation checks, for example a hidden-severity duplicate
  of an error ID. This is a guard gap, not a runtime defect.
- **Proposed fix:** drop the file-path and simple-name exemptions and compare
  only against the resolved generated-catalog symbols. If a generated-file
  exemption is still needed, require the file to be listed in the generator
  inventory (for example, recognize the `// <auto-generated>` header *and*
  the exact catalog path). In the architecture test, match
  `DiagnosticDescriptor` creations semantically, or at least also match
  `DiagnosticDescriptor\s+\w+\s*=\s*new\s*\(`, and scan every production
  project that references Roslyn.
- **Proposed regression:** meta analyzer tests for the two observed shapes,
  plus the target-typed form in a non-generated file, each expecting
  `SPMETA005`.

### B44. SPMETA006 only rejects scalar `string` members in the IR

**Confidence: Confirmed by running the meta analyzer.**

- **Location:** `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:665-676`
  (`AnalyzeField`) and `:693-712` (`AnalyzeProperty`), both of which test
  `Type.SpecialType == SpecialType.System_String`.
- **Defect:** the rule "Semantic identity in the program IR must use scoped
  typed identifiers" is checked only for members whose type is exactly
  `string`. Arrays, `ImmutableArray<string>`, `List<string>`, dictionaries
  keyed by `string`, tuples containing `string`, `KeyValuePair<string, _>`,
  `char[]`, and `object` members holding strings all pass.
- **Observed boundary:** in a `SharpProof.Ir.IrThing` class with fields
  `string Name`, `string[] Names`, `ImmutableArray<string> Keys`,
  `List<string> Parts`, `(string A, int B) Pair`,
  `KeyValuePair<string, int> Kvp`, `object Boxed`, `char[] Chars`, and
  `IReadOnlyDictionary<string, int> Map`, only `Name` reported `SPMETA006`.
- **Impact:** new IR nodes can carry names or keys as untyped strings inside
  a collection, which is the identity ambiguity the rule exists to prevent.
  Today only the intern table in `SharpProof.Ir/IrFactory.cs` legitimately
  holds `List<string>`/`Dictionary<string, IrStringId>`.
- **Proposed fix:** walk the member type recursively (array element types,
  type arguments, and tuple elements) and report when it contains `string`.
  Add `IrFactory`'s private intern fields to the explicit allowlist alongside
  `IrUnsupportedInfo`/`IrExceptionInfo`. Match those types by symbol instead of
  simple name, as B39 recommends for SPMETA010.
- **Proposed regression:** a meta analyzer test with each collection shape
  above expecting `SPMETA006`, plus the `IrFactory` intern table as a silent
  control.

### B45. Source rebinding rejects every project whose sources are not UTF-8 or BOM-marked

**Confidence: Confirmed. The encoding APIs were checked at runtime, and the invalid-UTF-8 path was reproduced end to end through the collector and `CompilerSourceRebinding.Validate`.**

- **Location:** `SharpProof.CompilerArtifact/CompilerSourceRebinding.cs:120-155`
  (`Decode`), called from `SharpProof.Worker.Launcher/Program.cs:1191`. The
  encoding name is recorded at
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:255`
  (`text.Encoding?.WebName`).
- **Defect:** with no byte-order mark, `Decode` handles `utf-8` with a strict
  decoder and passes any other name to `Encoding.GetEncoding(name)`. No
  production code registers `CodePagesEncodingProvider`, so on .NET the
  launcher can decode only the built-in encodings (UTF-8/16/32, ASCII,
  Latin-1). Two ordinary cases fail:
  1. A project compiled with `<CodePage>1252</CodePage>` (or any other code
     page, such as `shift_jis`). The compiler registers the provider itself,
     so the tree records `windows-1252`, and the launcher throws
     `ArgumentException: 'windows-1252' is not a supported encoding name`.
  2. A source file containing bytes that are not valid UTF-8 (for example a
     Latin-1 `é` in a comment), compiled without `CodePage`. On Linux,
     Roslyn's fallback (`EncodedStringText.CreateFallbackEncoding`) resolves
     to the lenient default UTF-8, so the tree records `utf-8` with U+FFFD
     replacement characters. The launcher's strict `UTF8Encoding(false, true)`
     then throws `DecoderFallbackException`.
- **Observed boundary:** on .NET 9 without a registered provider,
  `Encoding.GetEncoding("windows-1252")` and `GetEncoding("shift_jis")` throw
  `ArgumentException`, while `utf-16`, `utf-32`, `us-ascii` and `iso-8859-1`
  resolve. `SourceRebindingAcceptsCompilerByteOrderMarks` in
  `SharpProof.Worker.Test/CompilerSourceLocationAuthorityTests.cs` covers
  only the BOM encodings.
  End to end: a source whose comment contains the single byte `0xE9`
  (Latin-1 `é`), compiled by the collector probe from leniently decoded UTF-8
  text (as Roslyn's fallback does on Linux), produced a manifest recording
  `encoding: utf-8`. `CompilerSourceRebinding.Validate` on that manifest
  then threw `DecoderFallbackException: Unable to translate bytes [E9]`.
- **Impact:** both exceptions are `ArgumentException`s, which the launcher
  maps to "SharpProof launcher input is invalid" with exit code 2, before
  any worker runs. Verification is impossible for such a project, and the
  message points at launcher input rather than source encoding. The
  behavior fails closed and is not unsound, but it blocks legacy-encoded
  codebases, and no documentation states that sources must be UTF-8.
- **Proposed fix:** register `CodePagesEncodingProvider.Instance` in the
  launcher before rebinding (it is part of the shared framework through
  `System.Text.Encoding.CodePages`), and decode with the *same leniency* the
  compiler used: when no BOM is present and the recorded name is `utf-8`,
  first try strict UTF-8, then fall back to `new UTF8Encoding(false, false)`.
  The captured SHA-256 check that follows still guarantees the text is
  identical. Wrap any remaining encoding failures in an
  `InvalidDataException` naming the tree and its encoding.
- **Proposed regression:** extend the test above with `windows-1252` (via
  `CodePage`) and with a UTF-8-declared file containing an invalid byte,
  compiled through the real collector. Both must rebind successfully, and
  appending text must still be rejected.

### B46. Preconditions on local functions are silently ignored at call sites

**Confidence: Confirmed by probing the analyzer; the exact line that drops the target is not isolated.**

- **Location:** `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs:278-360`
  (`AnalyzeCallSite`: `ResolveExactTarget`, then
  `session.BindRequires(contractTarget)`), and
  `SharpProof.Analyzer.Core/AnalyzerSession.cs:156-186` (`BindRequires` and
  `HasPotentialCallPreconditions`), together with call-site discovery for
  local-function invocations. Local functions have
  `MethodKind.LocalFunction`, which `LanguageSubsetGate.cs:262-265` and
  `AnalyzerFeaturePipeline.cs:535` treat as nested callables.
- **Defect:** a `Contract.Requires` inside a local function binds to that
  local function, since postconditions are "directly owned" by it according
  to `docs/analysis-limits.md`. A call to the local function with a definitely
  violating constant is never reported, and nothing tells the user that the
  clause is inert. `docs/coverage-and-limits.md` says call-site preconditions
  are bound "for ordinary calls and object creation" and that the pass
  follows local-function CFGs. It does not say that calls *to* local
  functions are exempt.
- **Observed boundary:** with `SharpProofFeatures=all`, none of these
  produced a diagnostic:
  `int Local(int y) { Contract.Requires(y > 0); return y; } return Local(-1);`,
  the `static` local-function variant, the variant with a local
  `int z = -1; Local(z)`, and a lambda
  `Func<int,int> f = y => { Contract.Requires(y > 0); return y; }; f(-1);`.
  The identical precondition on an ordinary static method reports `SP0027`
  for `Positive(0)`, `Two(b: -1, a: -1)`, and 12 other argument shapes.
- **Impact:** users can write preconditions on local functions that look
  enforced but are neither checked at calls nor flagged as unsupported, while
  local-function *postconditions* do surface as `UnsupportedCallable`. For
  lambdas, the call goes through a delegate, so silence is expected, but
  there is still no diagnostic that the clause has no effect.
- **Proposed fix:** resolve direct invocations of local functions
  (`IInvocationOperation.TargetMethod.MethodKind == MethodKind.LocalFunction`)
  to their declaration and run the same binding and replay as for ordinary
  methods. Captured variables should become unknown inputs, as the docs
  already describe for nested CFGs. For `Contract.Requires` inside a lambda
  or anonymous method, report `SP0024` (placement): the clause can never be
  checked at a call site.
- **Proposed regression:** analyzer tests expecting `SP0027` for
  `Local(-1)`, the `static` variant, and the local-variable variant, no
  diagnostic for `Local(1)`, and `SP0024` for a `Contract.Requires` inside a
  lambda body.

### B48. Call-site precondition discovery is quadratic in a method's statement count

**Confidence: Confirmed by timing and profiling the analyzer.**

- **Location:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:595-660`
  (`HasReplayablePrefix`), called for each candidate at `:179`, `:265` and
  `:758`. The last statement of that method evaluates
  `block.Statements.TakeWhile(...).All(prior => operationFacts.CompletesNormally(semanticModel.GetOperation(prior, ...)))`.
- **Defect:** for every call site in a block, the analyzer re-derives the
  operation of every earlier statement in the block and re-runs
  `DefiniteOperationFacts.CompletesNormally` on it. Nothing caches "the
  first k statements all complete normally", so a block with n call
  statements costs O(n²) completion queries. Each query can itself descend
  into callees; see B47.
- **Observed boundary:** a method of n statements `F(1); F(2); ...`, where
  `F` has `Contract.Requires(x > 0)`, alongside a variant that threads a
  local through the calls, took 3.3 s, 3.8 s, 11.1 s and 40.6 s of analyzer
  time for n = 500, 1,000, 2,000 and 4,000, with no diagnostics beyond the
  expected `SP0047` on the variable variant. A `dotnet-trace` sample of
  n = 2,000 attributes 11.9 of 12.6 analyzer seconds to
  `HasReplayablePrefix` → `DefiniteOperationFacts.CompletesNormally` →
  `InvocationEmissionPolicy.IsElided`.
- **Impact:** long straight-line methods (generated registration code, test
  tables, large `Main` methods) that call contracted APIs slow every build
  and IDE analysis pass quadratically. It is not a correctness problem.
- **Proposed fix:** compute, once per block, the index of the first statement
  that may not complete normally (a single forward pass using
  `CompletesNormally`), store it in a per-block dictionary for the discovery
  run, and answer `HasReplayablePrefix` by comparing the call statement's
  index against it. Also cache `CompletesNormally(IOperation)` per operation
  for the discovery run.
- **Proposed regression:** a performance-style analyzer test with 4,000
  sequential contracted calls that must finish within a small multiple of
  the 500-call time, plus a precision control in which a statement that may
  not complete normally (for example `if (x) throw ...;`) still blocks
  replay of the later calls.

### B50. Exception-handler reachability overflows the stack on nested `try` statements just below the walk-depth cap

**Confidence: Confirmed by probing the analyzer.**

- **Location:** `SharpProof.Effects/ExceptionHandlerReachability.cs:107-140`
  (`GetPotentialExceptions`) and `:2029` (`GetNestedTryExceptions`), called
  from `:820`. These two recurse once per nested `try`, with no depth guard of
  their own. The only nearby guard is `MaximumWalkDepth = 256` in
  `SharpProof.Effects/ManagedAbstractFlow.cs:25` and `:705-719`, which
  belongs to a different walker.
- **Defect:** the managed-flow walker abstains once nesting reaches its
  256-level cap, but exception-handler reachability runs first (from
  `OperationEffectScanner.IsReachable` → `IsReachable(CatchClauseSyntax)` →
  `GetReachability`). Its per-level frames are large enough that a 1 MB
  thread-pool stack runs out before the cap is reached. The result is an
  uncatchable `StackOverflowException` instead of the intended `SP0047`
  abstention.
- **Observed boundary:** a `[DoesNotThrow]` method with n nested
  `try { ... } catch (DivideByZeroException) { ... }` statements around one
  division gave: 200, 210, 220 and 240 completed; 230, 250, 255 and 256
  died with `Stack overflow` (repeated
  `GetPotentialExceptions`/`GetNestedTryExceptions` frames); 300 abstained
  with `SP0047`. Roslyn compiled every variant. Nested `using` statements of
  the same depths abstained cleanly.
- **Impact:** generated or pathological code with 230 or more nested
  `try` blocks crashes the compiler or IDE analyzer host instead of
  receiving the documented abstention. The shape is rare in hand-written
  code; this is the same failure class as B47.
- **Proposed fix:** give `ExceptionHandlerReachability` its own nesting budget
  (for example 64 nested `try` operations), returning the conservative "all
  exceptions possible / handler reachable" answer when exceeded. Also call
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()` at the recursive entry
  points. Alternatively, lower the shared walk cap to a value validated
  against the largest frame chain.
- **Proposed regression:** analyzer tests with 250 and 1,000 nested `try`
  blocks under `[DoesNotThrow]`, both expecting `SP0047` and no crash.

### B51. The README's minimal contract cannot be proven, and is false in the default unchecked context

**Confidence: Confirmed by running the collector and worker on the README snippet.**

- **Location:** `README.md` ("A minimal contract": `Increment(int value)` with
  `Contract.Requires(value >= 0)`, `Contract.Ensures(Contract.Result<int>() > value)`
  and `return value + 1;`). The next README section, "Strict container
  verification", configures `require-proven`.
- **Defect:** two problems.
  1. The worker admits only checked `long` arithmetic (`docs/coverage-and-limits.md`:
     "bounded integer comparisons, checked `long` arithmetic"), so the
     `int` addition makes the body unsupported.
  2. In the default *unchecked* C# context, the postcondition is actually
     false for `value == int.MaxValue`: `value + 1` wraps to
     `int.MinValue`. A counterexample exists even though the precondition
     holds.
- **Observed boundary:** the exact snippet, collected with the compiler
  collector and run through the in-process worker, gave
  `outcome=Unknown reason=UnsupportedBody` in both an unchecked and a
  checked compilation. The same claim shape on `long` with `return value;`
  (the `samples/Outcomes/OutcomeExamples.cs` `Proven` sample) is `Proven`.
- **Impact:** the first example a user copies fails a strict build
  ("requires every selected claim to be proven"). If `int` support is
  added later without fixing the example, an unchecked build would
  correctly *refute* it, which is equally confusing in a README.
- **Proposed fix:** replace the README example with one inside the
  supported subset that is true in the default context. For example, use
  `long` with an upper bound in the precondition
  (`Contract.Requires(value >= 0 && value < long.MaxValue)`) and
  `return checked(value + 1);`, or copy the provable `getting-started`
  example (`return value;` with `Result >= 0`). Add a sentence noting that
  `int` arithmetic is outside the worker subset.
- **Proposed regression:** have `scripts/Test-SharpProofReadme.ps1` (or an
  acceptance test) compile each README C# fence through the collector and
  worker and assert the outcome the surrounding text implies (`Proven` for
  the minimal contract).

### B52. `[NotNull]` is rejected as an error on nullable value types and unconstrained generics

**Confidence: Confirmed by probing the analyzer.**

- **Location:** `SharpProof.Contracts/ClosedContractAttributeValidator.cs:83-85`
  (`ClosedContractAttributeKind.NotNull when !type.IsReferenceType`), and the
  SP0024 bullet in `docs/diagnostic-examples.md` ("`[NotNull]` on a value
  that cannot be null").
- **Defect:** the validator accepts `[NotNull]` only when
  `ITypeSymbol.IsReferenceType` is true. `Nullable<T>` (`int?`) is a value
  type that *can* be null, and an unconstrained type parameter `T` is not
  `IsReferenceType` although it may be instantiated with a reference type. Both are
  rejected with the error SP0024 ("expected a definitely reference-capable
  value"). The documentation promises rejection only for values that
  cannot be null, and `docs/public-api.md` says nothing further.
- **Observed boundary:** `A([NotNull] int x)` and `A2([NotNull] int? x)` both
  reported `SP0024 ... '[NotNull]' has invalid argument ...: expected a
  definitely reference-capable value` (`Int32` and `Nullable`). An
  unconstrained generic `U<T>([NotNull] T x)` also reported SP0024 (`'T'`),
  while `where T : class` and a `[NotNull] string` parameter are accepted.
- **Impact:** a natural annotation (`[NotNull] int? value`) breaks the build
  with an error that contradicts the documentation. Users must drop the
  contract or change the signature. This is not a soundness problem, because
  the attribute is rejected rather than trusted.
- **Proposed fix:** either support `Nullable<T>` by binding `[NotNull]` to
  `HasValue` (an existing API spec, `bcl.nullable.has-value`), or keep the
  restriction and state it precisely in `docs/diagnostic-examples.md` and
  `docs/public-api.md` ("reference types only; `Nullable<T>` and
  unconstrained type parameters are rejected"), with a message that names
  the rule, for example "expected a reference type; use `Contract.Requires(x.HasValue)`
  for nullable value types".
- **Proposed regression:** analyzer tests for `[NotNull]` on `int?`, on an
  unconstrained `T`, and on `T : class`, matching whichever rule is chosen,
  plus a documentation check that the SP0024 bullet names the accepted types.

### B53. SP0027 misses nested violations in expression-bodied members and after literal interpolation text

**Confidence: Confirmed by probing the analyzer.**

- **Location:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:608-615`.
  In `HasReplayablePrefix`, an expression body is accepted only through
  `IsOwnedCallSiteExpression` (`:1119-1129`), which requires the entire body
  to *be* the call. Also `:1084-1117` (`PrecedingExpressionOperationsComplete`),
  which requires `CompletesNormally` for every earlier sibling operation,
  including `IInterpolatedStringTextOperation` literals.
- **Defect:** `docs/diagnostic-examples.md` (SP0027) says nested calls "in
  ordinary arguments, arithmetic, interpolations, returns, ... are replayed
  when the expression prefix is definitely non-throwing". Block bodies are
  handled through `IsReplayableCallExpression`, but expression bodies (`=>`)
  never are, unless the whole body is the call. In interpolated strings, a
  literal text segment before the call prevents replay, although literal
  text cannot throw.
- **Observed boundary:** with `P(int v)` requiring `v > 0`:
  - `int B1() { return 1 + P(0); }` and `int B2() { return Id(P(0)); }`
    report SP0027. The expression-bodied equivalents `=> 1 + P(0)`,
    `=> Id(P(0))`, `=> P(0) + 1` and the property `Prop => 1 + P(0)` are
    silent.
  - `Console.WriteLine($"{P(0)}")` reports, while
    `Console.WriteLine($"a{P(0)}")` and `string s = $"a{P(0)}";` are silent.
  - The whole-body forms `=> P(0)` and `string s = $"{P(0)}";` report.
  - Calls nested in constructor-initializer arguments are skipped:
    `public F3() : this(Fp.P(0)) { }` is silent, while the same call in a
    field initializer, property initializer or local initializer reports.
    `HasReplayablePrefix` (`:618-623`) accepts a `ConstructorInitializerSyntax`
    only when it is itself the call site.
  - Similarly, `using (new R()) { P(0); }` reports, while the C# 8 using
    declaration `using var r = new R(); P(0);` is silent, even though
    the doc lists `using` bodies as replayable.
- **Impact:** definite precondition violations go unreported in idiomatic
  expression-bodied members and log or format strings. This is a
  false-negative precision gap against the documented replay contexts, not
  a false proof.
- **Proposed fix:** in `HasReplayablePrefix`, handle an `ExpressionSyntax`
  body with the same `IsReplayableCallExpression` walk used for return
  statements. In `PrecedingExpressionOperationsComplete` (or
  `DefiniteOperationFacts.CompletesNormally`), treat
  `IInterpolatedStringTextOperation` and constant literals as completing
  normally. Treat a `using` declaration whose initializer completes normally
  as a completing prefix statement.
- **Proposed regression:** analyzer tests for the four expression-bodied
  forms and the two literal-prefixed interpolations above, each expecting
  SP0027, plus controls in which the prefix may throw (for example
  `=> Div(1, x) + P(0)`), which must stay silent.

### B56. Case-insensitive PowerShell deduplication drops distinct paths and preprocessor symbols

**Confidence: Confirmed by executing the PowerShell cmdlets; the end-to-end loop run was not executed.**

- **Location:** `scripts/Invoke-SharpProofLoop.ps1:181`
  (`$sourcePaths = @($sourcePaths | Sort-Object -Unique)`), `:236`
  (`Sort-Object -Unique` for the verification listing) and `:237-241`
  (`Compare-Object` without `-CaseSensitive`); also
  `scripts/CSharpSourceMetrics.ps1:222-226` (preprocessor symbols deduplicated
  with `Sort-Object -Unique`).
- **Defect:** `Sort-Object -Unique` and `Compare-Object` compare strings
  case-insensitively unless `-CaseSensitive` is passed. The loop script runs on
  developer hosts, including Linux and macOS with case-sensitive checkouts,
  and treats Git paths as case-sensitive data. It collapses src/Foo.cs
  and src/foo.cs into one entry. The follow-up comparison cannot notice,
  because it is case-insensitive too. C# preprocessor symbols are
  case-sensitive, so the metrics helper likewise collapses `DEBUG` and
  `debug`.
- **Observed boundary:** in `pwsh`,
  `@("src/Foo.cs","src/foo.cs","b.txt") | Sort-Object -Unique` returned two
  items (b.txt and src/foo.cs), and
  `Compare-Object @("src/Foo.cs") @("src/foo.cs") -SyncWindow 0` reported
  no difference.
- **Impact:** the container loop can run against a snapshot that silently
  omits one of two untracked files differing only in case, so its results
  do not describe the working tree. Source metrics may parse
  `#if debug` regions under the wrong symbol set. These are developer-tool
  and metrics defects, not release-gate soundness defects.
- **Proposed fix:** use `Sort-Object -Unique -CaseSensitive` and
  `Compare-Object -CaseSensitive` in `Invoke-SharpProofLoop.ps1`, or a
  `HashSet[string]` with `StringComparer.Ordinal`, and do the same for the
  preprocessor symbol list in `CSharpSourceMetrics.ps1`.
- **Proposed regression:** a fixture test that feeds the untracked-path
  logic two paths differing only in case and asserts both are copied into
  the snapshot manifest, plus a metrics test with `DEBUG` and `debug` both
  defined.

### B57. Pilot review undercounts false positives and the pilots receipt trusts a self-declared review

**Confidence: Confirmed by code reading of the producer, reviewer and receipt scripts; not executed end to end.**

- **Location:** `scripts/Test-SharpProofPilots.ps1:303-325` (diagnostics are
  aggregated to `{ id, count }` per pilot);
  `scripts/Complete-SharpProofPilotReview.ps1:76-105` (review keys are
  `pilotId|Diagnostic|id`, and a `FalsePositive` row adds exactly 1);
  `scripts/Test-SharpProofPilotReport.ps1:100-190` (validates claims against
  `result.json` but never validates `diagnostics`); and
  `scripts/Write-SharpProofQualificationReceipt.ps1:93-97` (the `pilots` gate
  requires only `reviewStatus == 'Reviewed'` plus that validator).
- **Defect:**
  1. One review row covers every occurrence of a diagnostic ID in a pilot,
     but `falsePositiveReports` is incremented once per row, not by the
     row's `count`. A pilot with twelve `SP0027` lines marked
     `FalsePositive` records 1.
  2. The `diagnostics` list is never cross-checked against the pilot's SARIF
     or build log, so the set of items that "must be reviewed" is whatever
     the report says.
  3. The receipt accepts any report whose `reviewStatus` is `Reviewed`. It
     does not bind the review ledger (its hash or path) and does not require
     that `Complete-SharpProofPilotReview.ps1` produced the file. A report
     edited by hand to `Reviewed` with `falsePositiveReports: 0` qualifies.
- **Observed boundary:** source-traced. `Group-Object | ... count = $_.Count`
  in the producer; `$falsePositives[$pilotId] = 1 + ...` per row in the
  reviewer; no `diagnostics` validation in the validator; and receipt
  validation `reviewStatus -ceq 'Reviewed' -and (Test-SharpProofPilotReport ...)`.
- **Impact:** release qualification can publish a pilot false-positive
  figure far below reality, and a pilots receipt can be produced without
  any review. This is a release-evidence integrity gap; product
  verification is unaffected.
- **Proposed fix:** make each diagnostic *occurrence* reviewable. Emit
  `{ id, file, line, column, messageSha256 }` rows from the SARIF in
  `Test-SharpProofPilots.ps1` and key reviews by them; alternatively, add
  `count` for each `FalsePositive` row. In `Test-SharpProofPilotReport.ps1`,
  recompute the diagnostics from the `sarif` evidence file and require
  equality. In `Complete-SharpProofPilotReview.ps1`, record the ledger's
  SHA-256 in the reviewed report; in the receipt, require that field and
  re-run the ledger check against the committed ledger.
- **Proposed regression:** extend `scripts/Test-SharpProofPilotAuthorityFixtures.ps1`
  with a pilot whose diagnostic has `count = 3` and a `FalsePositive`
  review (expect 3), a report whose `diagnostics` disagree with its SARIF
  (expect rejection), and a hand-edited `Reviewed` report without a ledger
  hash (expect receipt failure).

### B60. Numeric conversions discard the operand interval, so `checked` arithmetic on widened values is never proven

**Confidence: Confirmed by probing the analyzer; root cause located by code reading.**

- **Location:** `SharpProof.Effects/ManagedAbstractFlow.cs:846-880`
  (`ConvertValue`: every non-boxing, non-nullable, non-reference conversion
  returns `TopForType(conversion.Type)`). It is consumed by `ProvesNoOverflow`
  (`:947-1010`) and by `ConversionEffectClassifier.CheckedOverflow`
  (`SharpProof.Effects/ConversionEffectClassifier.cs:175-200`).
- **Defect:** an implicit widening conversion (`byte`/`sbyte`/`short`/
  `ushort`/`char` to `int`, or `int` to `long`) is exact, but the flow
  replaces the operand's interval with the full range of the target type.
  C# promotes every small-integer operand to `int` before arithmetic, and
  `(long)a` is the standard overflow-avoidance idiom, so the overflow proof
  almost never has a bounded operand to work with.
- **Observed boundary:** in `checked` contexts, these were all reported as
  `SP0046 ... System.OverflowException` although none can overflow:
  `byte a, byte b => a * b` (at most 65,025), `short a => a + 1`,
  `char c => c + 1`, `ushort a, ushort b => a + b`, `sbyte a => -a`,
  `byte a => { int x = a; return x + 1; }`, `int a, int b => (long)a * b`,
  and `(long)a + b`. Controls: `int x = 1; checked(x + 1)` and
  `if (x > 0 && x < 100) return x * 2;` are proven, which shows the
  overflow check itself uses intervals. `a[b]` for a `byte` index into
  `new int[256]` is also proven.
- **Impact:** projects compiled with `CheckForOverflowUnderflow`, or code
  using `checked`, cannot establish `[DoesNotThrow]` for ordinary
  small-integer or long-widening arithmetic, and the checked-context fuzz
  campaign had no power. This is a precision gap only.
- **Proposed fix:** in `ConvertValue`, when the conversion is an implicit
  (or identity) numeric conversion between supported integer types, return
  the operand interval intersected with the target range. For explicit
  narrowing conversions, return the operand interval when it fits entirely
  in the target type, and otherwise the target's full range, as today.
  Add a unit test to `SharpProof.Effects.Test` for each promotion.
- **Proposed regression:** analyzer tests for the eight methods above,
  expecting no diagnostic, plus controls `int a => checked(a + 1)` and
  `checked((int)someLong)`, which must still report `OverflowException`.

### B62. The only BCL postcondition that may throw (`Math.Abs(int)`) can never be used

**Confidence: Confirmed by running the collector and worker; the design intent is confirmed by existing tests.**

- **Location:** the `bcl.math.abs.int32` row in
  `SharpProof.Specs/DefaultApiSpecCatalog.json` (throws `MayThrow(OverflowException)`,
  postcondition `Result >= 0`, and no completion condition). It is rendered
  in `docs/api-spec-catalog.generated.md` and advertised in
  `docs/coverage-and-limits.md` ("Result is non-negative on normal return").
  The rejection is pinned by the tests
  `MayThrowApiSpecWithoutCompletionConditionIsUnsupported`
  (`SharpProof.Worker.Test/WorkerTests.cs`) and
  `MayThrowSpecCallWithoutCompletionConditionIsRejected`
  (`SharpProof.Worker.Test/CompilerCallableLowererTests.cs`).
- **Defect:** the lowerer refuses any spec call that may throw unless the
  spec also states *when* the call completes normally, and the catalog
  schema and this row give no such condition. The only BCL entry with both
  a documented postcondition and a possible exception is therefore
  permanently unusable, while the docs present its postcondition as
  available to the worker.
- **Observed boundary:** with the collector and in-process worker (runtime
  references, and again with the `Microsoft.NETCore.App.Ref` 9.0.20
  reference pack), `return Math.Abs(x);` under `Ensures(Result >= 0)`,
  `Ensures(Result > 0)` and `Ensures(Result != int.MinValue)` all ended as
  `Unknown(UnsupportedBody)`. `Math.Max` has no postcondition and was also
  `UnsupportedBody`, apart from the opt-in `dotnet.scalar` pack that the
  worker tests use.
- **Impact:** no user contract can ever be proven through `Math.Abs`, and
  the documentation misleads readers about what the worker can use. This is
  a precision and documentation defect, not a soundness defect.
- **Proposed fix:** add an optional `completion` term to spec rows (for this
  row, `Parameter[0] != int.MinValue`), validate it in
  `ApiSpecTermValidator`, and have the lowerer emit it as the call's
  normal-completion condition, so the postcondition is assumed only on
  normal return. Until then, mark the row's postcondition as "not usable by
  the worker" in the generated catalog doc and in
  `docs/coverage-and-limits.md`.
- **Proposed regression:** once completion conditions exist, change the two
  tests above to expect `Proven` for `Ensures(Result >= 0)` and `Refuted`
  for `Ensures(Result > 0)` (model `x = 0`), plus a check that
  `Ensures(Result != int.MinValue)` is `Proven`, not vacuously refuted.

### B63. Defining `SHARPPROOF_CONTRACTS` checks nothing and makes every `Contract.Result`/`Old` postcondition throw

**Confidence: Confirmed by running a program built with the symbol.**

- **Location:** `SharpProof.Attributes/Contract.cs` (`Requires`, `Ensures` and
  `Assume` are `[Conditional("SHARPPROOF_CONTRACTS")]` methods with empty
  bodies; `Result<T>()` and `Old<T>()` always throw). The build-time guard in
  `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props:36-37`
  rejects the symbol only while the profile is not `off`, and its message
  describes it as enabling "runtime evaluation of ghost contracts".
  `docs/diagnostic-examples.md` (SP0025) likewise says it "changes ghost
  contract calls into runtime calls".
- **Defect:** the only configuration that accepts the symbol (profile `off`)
  turns the clauses into real calls, but the calls do nothing: a false
  `Requires` or `Ensures` is not detected. Every `Ensures` whose argument
  mentions `Contract.Result<T>()` or `Contract.Old(...)` evaluates that
  placeholder in the prologue, before the body runs, and throws
  `InvalidOperationException` on every invocation of an otherwise correct
  method.
- **Observed boundary:** a net9.0 program referencing the built
  `SharpProof.Attributes.dll` with `SHARPPROOF_CONTRACTS` defined printed
  `Pos(-5) returned -5` (`Requires(v > 0)` ignored),
  `Neg(5) returned 5` (`Ensures(v < 0)` ignored), and
  `Inc threw InvalidOperationException: Contract.Result<T>() is valid only
  inside Contract.Ensures(...)` for a correct
  `Ensures(Contract.Result<int>() > v); return v + 1;`.
- **Impact:** a user who reads "runtime evaluation" and enables the symbol,
  for example in a test build with SharpProof off, gets no checking and
  crashes in correct code. The public API offers no supported runtime-check
  mode, which is not stated anywhere.
- **Proposed fix:** either (a) make the symbol a documented,
  always-rejected reserved symbol: reject it in
  `SharpProof.ConsumerContract.props` regardless of profile, and reword the
  messages to "reserved; defining it breaks every postcondition that uses
  `Contract.Result`/`Old`". Or (b) implement a real runtime mode: `Requires`
  throws a dedicated `ContractViolationException` when false, and
  `Ensures`/`Result`/`Old` are documented as unsupported at runtime, with
  the analyzer or generator rejecting their use when the symbol is defined.
  In either case, state the behavior in `docs/public-api.md`.
- **Proposed regression:** a package test that builds a consumer with
  profile `off` and `SHARPPROOF_CONTRACTS`, expecting the chosen behavior (a
  build error for (a); a `ContractViolationException` for a false
  `Requires` for (b)), and never an `InvalidOperationException` from a
  correct method.

### B64. A violated `[EffectContract]` is reported as "analysis incomplete" (SP0047) instead of a not-proven diagnostic

**Confidence: Confirmed by probing the analyzer; the mapping was read in source.**

- **Location:** `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs:272-294`
  (the `EffectEvaluationContractKind.EffectContract` evaluation uses
  `GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule`, SP0047,
  with the reason `EffectContractDoesNotCoverBodySummary`). By contrast, the
  other effect attributes map to SP0002, SP0016, SP0045 and SP0046 in the same
  method. The descriptor catalog and `docs/diagnostic-examples.md` define
  SP0047 as "the method is outside the supported analyzer subset".
- **Defect:** when a fully analyzed body's summary is not covered by the
  declared `[EffectContract]` (for example the body writes static state
  while the contract says `SharpProofEffect.None`), the analyzer reports
  "SharpProof could not completely analyze selected method ...", which is
  the unsupported-subset diagnostic. There is no dedicated "effect contract
  not proven" rule, and `docs/diagnostic-examples.md` lists no diagnostic
  for `[EffectContract]` at all.
- **Observed boundary:**
  `[EffectContract(SharpProofEffect.None)] static int M3() { GE.N++; return 0; }`
  produced `SP0047 ... could not completely analyze selected method 'M3':
  EffectContractDoesNotCoverBodySummary`. The equivalent `[EnforcePure]`
  method produces `SP0002 ... effects do not prove observable purity`. The
  worker marks the claim `Refuted` (see `Lies2` in the modular-trust probe),
  so the build and IDE messages disagree in kind.
- **Impact:** users and severity configuration cannot tell a definite
  contract violation from an unsupported construct. Anyone who raises SP0002/
  SP0046 to errors but leaves SP0047 at `Info` (its default) silently
  tolerates violated effect contracts. The message also points users at
  language-subset limits instead of their code.
- **Proposed fix:** add a catalog descriptor (for example SP0052,
  "effect contract not proven") in `eng/diagnostics/diagnostic-descriptors.v1.json`,
  regenerate the descriptors, use it for the `EffectContract` evaluation
  whenever the summary is complete but not covered, and keep SP0047 only for
  `IncompleteEffectContract` and managed-flow incompleteness. Document it in
  `docs/diagnostic-examples.md`.
- **Proposed regression:** analyzer tests expecting the new ID for the M3
  shape, SP0047 for an `[EffectContract]` whose body hits an unsupported
  construct, and no diagnostic when the contract covers the body.

### B68. Async and iterator callees are summarized as if their bodies ran synchronously at the call

**Confidence: Confirmed by probing the analyzer; the absence of async handling was checked in source.**

- **Location:** `SharpProof.Effects/EffectMethodNodeBuilder.cs` and
  `SharpProof.Effects/EffectAnalysisSession.cs` (neither mentions
  `IsAsync` or iterators; a callee's body summary, including its `Throws`,
  is imported unchanged at each call site). Deferred execution is modeled
  only for normal completion, in `SharpProof.Effects/ManagedAbstractFlow.cs:2798`
  (`MethodCanCompleteNormally`) and
  `SharpProof.Effects/ExceptionHandlerReachability.cs:2971`.
- **Defect:** an `async` method never throws synchronously; exceptions from
  its body are stored in the returned `Task`. An iterator method's body does
  not run until enumeration. The effect summary nevertheless attributes the
  body's exceptions (and, for iterators, unknown effects) to the call
  itself. `SEMANTICS.md` already states the deferral principle for
  completion ("Async and iterator bodies execute behind a deferred call
  boundary").
- **Observed boundary:**
  `[DoesNotThrow] static Task<int> OK1() => As.ThrowSync();`, where
  `ThrowSync` is `async` and throws, reported
  `SP0046 ... may-effect summary includes disallowed exceptions`, and
  `[DoesNotThrow] static int OK2() { var e = As.Iter(); return 0; }` (an
  iterator that is never enumerated) reported `SP0046 ... ExceptionSetUnknown`.
  Neither can throw. The genuinely throwing shapes
  (`ThrowSync().Result`, `.Wait()`, `Iter().Sum()`, `Iter().Count()`) were
  reported or abstained, so the behavior is sound.
- **Impact:** `[DoesNotThrow]`/`[AllowedExceptions]` can never be proven for
  methods that start async work or create iterators whose bodies may throw.
  This is a common shape in asynchronous code. It is a precision gap only.
- **Proposed fix:** when building the summary imported for an invocation of
  an `async` method, keep the synchronous-prefix effects that the
  state-machine stub executes (argument evaluation, allocation of the task)
  and drop body exceptions, recording them on the task instead (they can
  resurface through `await`, `.Result` and `.Wait()`, which already make the
  analysis conservative). For iterator methods, import only the allocation of
  the enumerable and defer the body's effects to `MoveNext`.
- **Proposed regression:** effect tests expecting no diagnostic for OK1 and
  OK2, and `SP0046` for `.Result`, `.Wait()` and enumerating calls on the
  same callees.

### B69. An error-typed exception in a callee summary crashes the effect analyzer (AD0001) while code is being edited

**Confidence: Confirmed by probing the analyzer (found by a broken-source fuzz run).**

- **Location:** `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs:5-14`
  (`Encode` throws `InvalidOperationException` when
  `DocumentationCommentId.CreateReferenceId` returns nothing), reached from
  `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs:458-464`
  (`FormatTypes`) via `CreateSummaryEvidence` (`:441-456`) and `Evaluate`
  (`:137`). Error-typed thrown types enter `EffectSummary.Throws` through the
  callee summary builder (`SharpProof.Effects/EffectMethodNodeBuilder.cs`).
- **Defect:** when an unselected callee contains a malformed throw (for
  example `throw new InvalidOperationException()(;`, typed mid-edit), its
  summary records an exception type whose `TypeKind` is `Error`. A selected
  caller's `[DoesNotThrow]` evaluation then formats its evidence string,
  `Encode` throws on the error type, and the exception escapes the analyzer
  callback. Selected methods with invalid operations abstain with SP0047,
  but this path lies outside that guard, and the evidence formatting is not
  exception-safe.
- **Observed boundary:**
  `static int H() { throw new InvalidOperationException()(; }` plus
  `[DoesNotThrow] static int C() => E3.H();` produced
  `ANALYZER EXCEPTION System.InvalidOperationException: An exception type does not have a reference documentation ID.`
  (reported by Roslyn as AD0001), and no SharpProof diagnostic for `C`. A
  472-file token-deletion fuzz of the probe corpus found this as the only
  SharpProof crash; the other crash was a Roslyn binder stack overflow on
  300 nested lambdas. A second, larger run (1,332 files with one to three
  deletions, insertions, swaps or duplications each) hit only this crash
  again (twice).
- **Impact:** in the IDE, the analyzer throws AD0001 and drops diagnostics
  for callers whenever a callee is temporarily malformed. Builds with errors
  fail anyway, so there is no soundness or build-result effect.
- **Proposed fix:** in the callee summary builder, treat a thrown expression
  whose type is `TypeKind.Error` (or has no reference documentation ID) as an
  unknown exception (`EffectThrowSet.Unknown` with an
  `UnsupportedOperation` reason) instead of a concrete type. Also make
  `FormatTypes` total, by encoding such types as `"<error-type>"`, so
  diagnostic formatting can never throw.
- **Proposed regression:** an analyzer test with the two-method sample above,
  expecting no AD0001 and an `SP0046 ... ExceptionSetUnknown` (or `SP0047`)
  on `C`.

### B70. SARIF locations are absolute container paths even when they lie under the declared `%SRCROOT%`

**Confidence: High. Evidence: source trace plus the existing tests' expectations; consumer behavior was not exercised.**

- **Location:** `SharpProof.Worker.Launcher/SarifProjection.cs:237-246`
  (`ArtifactLocation` emits an absolute `file://` URI whenever the path is
  rooted, and uses `uriBaseId = "%SRCROOT%"` only for relative paths) and
  `:17`, `:67-69` (the run declares `originalUriBaseIds["%SRCROOT%"]` as the
  project directory). Paths come from
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerSourceLocationProjection.cs:26-58`,
  which returns `Path.GetFullPath(...)` for every real source file. The
  absolute form is pinned by
  `SharpProof.Package.Test/LauncherArgumentTests.cs:1486-1503`
  (`file:///C:/source/Subject.cs`).
- **Defect:** in practice every result location is an absolute URI of the
  machine that ran the launcher, for example
  `file:///workspace/SharpProof/src/Foo.cs` inside the canonical container,
  instead of a path relative to the `%SRCROOT%` the file itself declares.
  Relative anchoring happens only for `#line`-mapped relative paths.
- **Observed boundary:** source-traced. For an ordinary compilation, the
  collector records `GetFullPath(tree.FilePath)`, and `ArtifactLocation`
  takes the `TryAbsolutePathUri` branch for any path starting with `/` or a
  drive letter. The unit test above expects exactly that absolute URI, and
  only `RelativeCompilerMappedPathIsEscapedAndAnchoredToProjectRoot` exercises
  `%SRCROOT%`.
- **Impact:** SARIF produced inside the container (the only supported
  verification host) carries `/workspace/...` URIs. A code-scanning upload or
  SARIF viewer running against a checkout at a different path cannot map
  results to files unless it happens to relativize by that exact prefix.
  The file also leaks local absolute paths. Verification results are not
  affected.
- **Proposed fix:** in `ArtifactLocation`, when an absolute path is under the
  project directory (the `%SRCROOT%` base), emit the relative, segment-escaped
  path with `uriBaseId = "%SRCROOT%"`, and keep absolute URIs only for files
  outside it. Update the `LauncherArgumentTests` expectation accordingly.
- **Proposed regression:** a SARIF projection test with an absolute source
  path under the project directory, expecting a relative `uri` plus
  `uriBaseId`, and one with a path outside it, expecting an absolute `file://`
  URI.

### B71. Checked `++`, `--` and compound assignments are never proven overflow-free

**Confidence: Confirmed by probing the analyzer; root cause located by code reading.**

- **Location:** `SharpProof.Effects/ManagedAbstractFlow.cs:1744-1749`
  (`ManagedFlowResult.ProvesNoOverflow(IOperation)` returns `false`
  whenever `HasMutation(operation)` is true), and `:947-1010`
  (`ManagedAbstractFlow.ProvesNoOverflow(IOperation, ...)`, which handles
  binary, unary minus, increment/decrement and conversion operations but has
  no `ICompoundAssignmentOperation` case). It is consumed by
  `SharpProof.Effects/ConversionEffectClassifier.cs:175-200`
  (`CheckedOverflow`).
- **Defect:** an increment, decrement or compound assignment always mutates
  its own target, so the `HasMutation` guard rejects it before the
  increment-specific interval check (`TryIncrement`) can run. Compound
  assignments are not modeled at all. In a checked context, every `++`,
  `--` and `op=` therefore contributes `OverflowException`, even when the
  operand's value is exactly known.
- **Observed boundary:** in a checked compilation, `int i = 0; i++; return i;`,
  `int i = 0; ++i; return i;`, `int i = 5; i--; return i;` and
  `int i = 0; i += 1; return i;` under `[DoesNotThrow]` all reported
  `SP0046 ... System.OverflowException`, while the equivalent
  `int i = 0; i = i + 1; return i + 1;` was proven.
- **Impact:** in projects built with `CheckForOverflowUnderflow`, or in
  `checked` blocks, `[DoesNotThrow]` cannot be established for any method
  that uses increments or compound assignment, which covers nearly every
  counter. This is a precision gap only.
- **Proposed fix:** in `ManagedFlowResult.ProvesNoOverflow`, allow
  operations whose *only* mutation is the write to their own target (compare
  `HasMutation` on the value and the target's children, not on the
  operation itself), and evaluate them against the state recorded *before*
  the operation. Add an `ICompoundAssignmentOperation` case that applies
  `TryArithmetic(compound.OperatorKind, target, value)`. This must resolve
  flow-capture targets back to their storage . Otherwise a compound
  whose right side branches would be checked against a stale value, which
  would turn this precision gap into a false proof.
- **Proposed regression:** effect tests in a checked compilation expecting no
  diagnostic for the four shapes above, `SP0046 ... OverflowException` for
  `int i = int.MaxValue; i++;` and `i += 1`, and a branching compound-assignment control
  (`int i = 0; i += (b ? int.MaxValue : int.MaxValue); i += 1;`) that must
  stay reported.

## Historical areas checked without new findings (2026-09-16 through 2026-09-18)

The coverage below is retained from the earlier scratch-harness audits. It was
not rerun for the 2026-09-21 source review and does not establish that these
areas are bug-free at the current baseline. References below to findings
'listed above' or to former priority sections belong to that historical audit;
those removed entries are not additional active bugs in this backlog.

- **Common pure BCL specifications.** The default API catalog now covers
  nullable value reads (`HasValue`, `GetValueOrDefault`, and `Value`),
  `Math.Min`/`Math.Max`, `string.IsNullOrEmpty`, and the string indexer on
  every supported reference-pack family. Analyzer and runtime witnesses cover
  the normal and throwing paths; unsupported framework members remain
  unresolved and fail closed.

- **Cross-project source and binary contracts.** Source-only
  `Contract.Requires` clauses are ignored for external compilation
  references, matching emitted DLLs where conditional calls are absent.
  Closed parameter attributes remain checked across both reference forms, and
  regression coverage compares the two paths with and without local contract
  activation.

- **Contract.Assume effect evidence.** Direct `Contract.Assume` clauses now
  refine managed effect flow and remain in effects-only compiler artifacts as
  `UserAssume` evidence, so SP0048 still reports the declared assumption.

- **Worker postcondition soundness (differential).** A C# fuzzer generated
  contract-bearing methods over `long`/`bool` with branches, reassignment,
  early returns and calls to generated helpers, ran the real collector and
  worker, and executed every `Proven` method over a grid of boundary inputs
  (including `long.MinValue`, `-1`, `0`, `long.MaxValue`). Roughly 870
  postconditions were published `Proven` across 180 seeds (40 methods each),
  including 308 with nested helper summaries, and none was violated by a
  concrete run. A variant with `int` parameters widened into `long`
  arithmetic, which exercises the parameter source-interval assumptions,
  published 240 more `Proven` claims across 100 seeds, again with no
  concrete violation. An earlier worker-level IR fuzzer covered 10,000 programs with
  no false proof. The reverse direction was checked too: across 250 more
  seeds, every one of the 1,154 `Refuted` postconditions was replayed by
  calling the compiled method with the reported model, and in every case the
  precondition held, the method returned normally, and the postcondition
  was false. There was no false refutation.
- **Source relational summaries through branching helpers.** With
  project-wide `checked` arithmetic, postconditions over helpers with
  early returns, a constant-case `switch`, a conditional expression and a
  checked multiplication were `Proven` through `source-summary:` evidence
  when true, and false ones were `Unknown` (`CounterexampleNotReplayable`)
  rather than proven. A helper whose `Requires` or `[Positive]` parameter
  the caller does not establish was not used to prove the caller.
- **Implementation-IL summaries (differential).** `int32` helpers with
  unchecked wraparound, `checked` arithmetic, division, remainder, negation
  and nested calls were compiled into a separate library and called from
  contract-bearing methods. 118 claims were proven through `il-summary:`
  evidence with no false proof.
- **`[DoesNotThrow]` and `[EnforcePure]` (differential).** Random methods over
  `int`, `string`, `int[]` and a mutable class, with guards, try/catch/finally,
  helper calls and static writes, were run through the analyzer; every method
  the analyzer accepted silently was then executed over an input grid. 289
  accepted `[DoesNotThrow]` methods threw nothing, and 245 accepted
  `[EnforcePure]` methods mutated no static field, array or object. A later
  campaign that also generated `switch` statements and `goto` out of nested
  `try`/`finally` regions did produce violations, and all of them were the
  nested-`finally` defect in the P0 section rather than anything new. When
  the generator was then restricted so that protected regions never nest, and
  given `using` (with both a no-op and a state-writing `Dispose`), `lock`,
  `checked` blocks and single-level `try`/`catch`/`finally` instead, 600
  batches produced 202 accepted `[EnforcePure]` and 222 accepted
  `[DoesNotThrow]` methods and no violation at all. A
  `[ZeroAllocations]` campaign that measured each accepted method's managed
  allocation with `GC.GetAllocatedBytesForCurrentThread` accepted 1,379
  methods across 300 batches; the only 3 that allocated were the same
  nested-`finally` defect. Direct probes also confirmed that boxing through
  `GetHashCode`, `Equals` and `ToString` on structs that do not override them,
  `Enum.HasFlag`, `Enum.ToString` and `int.ToString` are all reported.
- **SP0027 call-site checking (differential).** 3,082 call sites with literal
  arguments and randomly generated preconditions were compared against
  concrete evaluation of the same condition: 566 diagnostics, no false
  positive. Manual probes also found no false positive for truncating
  division, negative remainder, unsigned and narrowing casts, unchecked
  wraparound, shifts, UTF-16 lengths or floating point, and no report for
  call sites that are not definitely executed (short-circuit operands,
  ternary arms, `catch` blocks, switch cases). A second campaign generated
  callers whose locals start from literals and then pass through guards,
  early returns, `switch` statements, bounded loops and increments before the
  call, and recorded at run time whether the precondition held. Of its 94
  SP0027 reports, one was the conditional-assignment false positive listed
  above, one was a call with literal arguments behind a guard that always
  returns first (the call is dead, but it would violate the precondition
  whenever it ran), and the other 92 were real violations. The only analyzer
  exception was the monotonicity crash listed above. Named arguments out of
  order, default parameter values, an assignment or increment inside an
  earlier argument, implicit numeric conversions and extension-method
  receivers were all mapped correctly, as were narrowing and wrapping casts
  in arguments (`unchecked((byte)300)`, `(long)3.9`, `(short)40000`), `char`
  arguments, and caller-info parameters (`[CallerLineNumber]`,
  `[CallerMemberName]`, `[CallerFilePath]`, `[CallerArgumentExpression]`,
  whose compiler-supplied values were used instead of the declared
  defaults). Neither SP0027 nor the effect analysis trusts a callee's
  postcondition: a helper with a false `Ensures(Result == 0)` did not make
  `Pos(Helper())` a reported violation, and a false `Ensures(Result != 0)`,
  `Ensures(Result != null)` or a `Contract.Assume` in a helper did not
  discharge a caller's division or dereference. A local set to `0` and then
  changed before the call (through a `ref` or `out` argument, a `ref`
  local, a lambda, a local function, a helper writing an object field,
  deconstruction, `+=`, `++`, a loop, `try`/`finally`, `try`/`catch`, a
  constant `switch` or a `goto`) never produced a stale SP0027, and
  relational, `not`, `or` and type-pattern guards before a call produced
  no false positive either. A violating call inside an expression-tree
  lambda (`Expression<Action> e = () => Pos(0);`) was, correctly, not
  reported, since quoted code does not run.
- **Broken code.** Methods with syntax errors, unresolved names, malformed
  contract calls, out-of-range `[EffectContract]` flags, a string
  `[InRange]` bound, non-exception `[AllowedExceptions]` types, a companion
  for an unresolved type, and missing `goto` labels produced SP0024, SP0047
  or SPCF0001 (or nothing where the compiler already errors), and never an
  analyzer exception.
- **Fail-closed behavior of the worker.** A battery of twenty methods whose
  postconditions are false for some input (goto, switch, try/finally, checked
  blocks, compound assignment, increment, casts, shifts, bitwise operations,
  arrays, `lock`, local functions, tuples, `out` parameters, string length)
  produced six `Refuted` and fourteen `Unknown` results, and no `Proven`.
  Reference-typed contracts, heap state, patterns and loops abstain rather
  than guess.
- **Effect analysis coverage.** Correct diagnostics were produced for hidden
  allocations (closures, interface boxing, generic boxing, iterators,
  collection expressions, `string.Format`, delegate combination), hidden
  exception sources (throwing type initializers, array covariance, negative
  array sizes, unboxing, `lock(null)`, null delegate invocation, checked
  conversions, `throw null`), handler semantics (filters, rethrow, throwing
  `finally`, catch-type hierarchy), compound division and remainder overflow,
  project-wide `CheckForOverflowUnderflow`, virtual/abstract/interface
  dispatch, and code synthesized by the compiler that runs user code (record
  `ToString`/`GetHashCode`, object and collection initializers, struct
  parameterless constructors). `[Conditional]` and unimplemented `partial`
  call elision were handled correctly, and `[DoesNotReturn]` is not trusted.
- **Contract binding and placement.** Contracts in branches, blocks,
  switches, local functions, or after other statements fail closed, as do
  mismatched `Contract.Result<T>()` type arguments, `Contract.Old` of a
  result, and closed attributes on `ref`, `in` and `out` parameters.
  Vacuity evidence is correctly reported for contradictory preconditions and
  for a body with no modeled normal return.
- **Real-world code at scale.** The 100 pinned open-source files in
  `SharpProof.Gates/Corpus/oss-methods.json` (aalhour/C-Sharp-Algorithms) were
  extracted and every one of their 870 methods with a body was given an effect
  contract by syntax rewriting. The analyzer ran over the whole compilation
  three times, once each for `[DoesNotThrow]`, `[EnforcePure]` and
  `[ZeroAllocations]`, with no analyzer exception. The same annotated sources
  went through the collector and worker: the manifest was emitted with no
  SP0049, the run completed, and the single `Proven` effect claim was an
  empty `Dispose()`. It is worth noting how narrow the analyzable subset is on
  real code: 840 of the 870 methods were `SP0047`/`UnsupportedCallable`, which
  matches the documented bounded-subset limits but means these contracts are
  mostly unavailable to ordinary library code.
- **Shipped samples.** `Effects`, `Preconditions`, `Outcomes` and `ContractFor`
  produce no unexpected diagnostics when compiled the way their projects do
  (`SharpProofFeatures=all`, `Nullable=enable`), and all five `Library` claims
  are `Proven` end to end, as `samples/README.md` promises. The one sample
  that does not behave as intended is `TrustedBoundary` (see the
  `Randomness` capability entry).
- **Contract binding details.** `Contract.Old` is correct for expressions over
  several reassigned parameters, for arithmetic combined with
  `Contract.Result`, inside a conditional postcondition, and for a parameter
  that is never reassigned; `Old` of a helper call abstains. Invalid shapes
  (`Contract.Result` or `Contract.Old` inside `Requires` or `Assume`) both
  fail closed in the worker and are reported as SP0024 with a placement
  message. With several postconditions on one method, outcomes land on the
  right clause: the refuted clause was reported in its own position for a
  first-false and a middle-false method, with the others proven.
- **Indirect and unsafe writes.** Pointer writes, `fixed` blocks,
  `stackalloc`, `Span` indexers, `Interlocked`/`Volatile` on a static through
  `ref`, and writes through a span view all abstain or are reported; direct
  array-element writes and `Array.Clear` on a static array are reported.
  Contracts are never used to discharge virtual dispatch, so a proven
  `[EnforcePure]` on a virtual base method does not silence a call that an
  override could serve.
- **Trusted boundaries.** The documented metadata rule works end to end: a
  referenced assembly's method carrying `[SharpProofTrusted]` and
  `[EffectContract(..., Complete = true, PreconditionFree = true)]` lets a
  caller be proven `[EnforcePure]`, while the same declaration without
  `PreconditionFree` and an undeclared method both stay `Unknown`. In the
  analyzer, trust never sharpens anything on its own: a trusted method with
  no declared summary, and a declared summary that does not cover its own
  body, are both reported. `base.M()` resolves to the base implementation
  rather than the override, in both directions. A referenced library that
  defines its own look-alike `SharpProof.Attributes.SharpProofTrustedAttribute`
  and `EffectContractAttribute` (including an assembly-level "trusted"
  attribute) could not spoof trust: a caller of its impure method stayed
  `Unknown`.
- **Manifest determinism.** The collector produced a byte-identical manifest
  (same SHA-256) for the 100-file open-source corpus across two concurrent
  runs and one sequential run, so manifest emission does not depend on
  analysis order. Analyzer diagnostics were deterministic too: 40 fuzzer
  batches, each analyzed three times with concurrent execution enabled,
  produced identical diagnostic sets.
- **Source generators.** A real incremental generator run in process emitted
  a contract-bearing method into a `partial` class alongside a hand-written
  one. With both an absolute generated-file path (under `obj`, as MSBuild
  produces) and a relative one, the collector emitted a manifest and both
  postconditions were `Proven`.
- **Helper callees with a richer statement set (differential).** A second
  effect fuzzer generated unannotated helpers with nullable values
  (`??`, `HasValue`, `GetValueOrDefault`, unguarded casts), strings,
  `is`/`as` on `object`, field reads and writes, bounded `for` and array
  `foreach` loops, `switch` statements, exhaustive switch expressions,
  `lock`, `using`, `checked` blocks, single-level `try`/`catch`/`finally`
  and a user exception type, called from annotated methods. Shapes that
  reproduce the entries above were excluded. Over 1,000 batches, 1,732
  accepted `[EnforcePure]` methods mutated nothing and 668 accepted
  `[DoesNotThrow]` methods threw nothing when run over an input grid. The
  only analyzer exception was the monotonicity crash. For 300 of those
  batches the same sources also went through the collector and worker: all
  2,008 published effect claims matched the analyzer exactly (every method
  the analyzer accepted was `Proven`, and no method it reported was
  `Proven`).
- **Helpers with patterns, properties and operators (differential).** A
  third effect fuzzer generated helpers with `is` and switch-statement
  patterns (type, property, relational, `and`/`or`/`not`, list and tuple
  patterns, `when` guards), switch expressions, property getters that write
  static state or throw, a property setter, a user struct with `+`, `/`,
  unary `-`, `==` and conversions, null-conditional chains, interpolation
  and concatenation, and `try`/`catch` with filters. Over 1,000 batches in
  each mode, 1,959 accepted `[DoesNotThrow]` methods threw nothing and
  2,501 accepted `[EnforcePure]` methods changed no static field, array or
  object when run over an input grid. Of 2,692 accepted
  `[ZeroAllocations]` methods, the only two that allocated did so through a
  caught runtime exception (see the P0 entry). The only analyzer exception
  was the monotonicity crash. For 60 of the `[DoesNotThrow]` batches, the
  collector and worker agreed with the analyzer on all 480 effect claims.
  A fourth variant added `using` over a struct resource whose `Dispose`
  writes or throws, `foreach` over a custom struct enumerator whose
  `MoveNext`, `Current` and `Dispose` write or throw, `box?.Bump()`,
  backward `goto` loops, `do`/`while (false)`, throw expressions, string
  `switch` statements, `checked` increments and a user-defined compound
  `+=` (keeping `using` out of handlers and after `try` statements, where
  the P0 entries apply). Over 800 batches per mode it accepted 1,578
  `[DoesNotThrow]`, 1,772 `[EnforcePure]` and 1,897 `[ZeroAllocations]`
  methods; the only violation was one more caught `DivideByZeroException`
  allocation. The collector and worker agreed with the analyzer on all 400
  effect claims of 50 `[EnforcePure]` batches and all 320 of 40
  `[ZeroAllocations]` batches.
- **Implicit user code and callee bodies.** Because several false proofs
  above come from code the compiler runs implicitly, the other implicit
  call shapes were checked both in the selected method and behind an
  unannotated helper: positional, list and property patterns
  (`Deconstruct`, `Length`, indexers), implicit `Index`/`Range` indexers,
  custom event `add` accessors, `foreach` with user-defined element
  conversions or an extension `GetEnumerator`, tuple equality with a
  user-defined `==`, deconstruction, `with` on record classes and record
  structs (copy constructors and `init` accessors), object initializers with
  side-effecting `init` accessors, `AppendLiteral` in a custom interpolated
  string handler, `using` with an explicit `IDisposable.Dispose` next to a
  different public `Dispose()` (and a re-implemented interface), explicit
  static constructors triggered by method calls, instantiation or static
  properties, and pointer, `ref`-local, `Span`, `MemoryMarshal`,
  `Unsafe.As`, `Interlocked` and `ref`/`out` writes inside helpers. Every one
  was reported or failed closed. The same held for 23 runtime exception
  sources (nullable unwrapping, string, span, multi-dimensional and jagged
  indexers, checked float and sign conversions, `Math.Abs`, unboxing,
  division and remainder overflow, array covariance, negative array sizes)
  in the selected method and in helpers, for `checked` compound assignments
  and increments on `byte`, `sbyte`, `short`, `ushort` and `char` (where the
  narrowing back-conversion is what overflows, even from known constants),
  for checked `uint`/`ushort`/`byte` arithmetic, and for checked enum
  increments and enum arithmetic. Implicit and explicit `base(...)` calls
  that throw or write state were reported for constructors and for
  `new` of a type whose implicit constructor chains to them.
- **Handler reachability and completion.** Apart from the nullable unwrap
  entry above, a `catch` whose only possible source was a type initializer,
  a throwing property getter or `Dispose`, unboxing, an explicit reference
  or interface cast, array covariance, a negative array size, division or
  remainder overflow, a constant zero divisor, `Math.Abs`, checked
  negation, increments and lifted arithmetic, a `long` array index, `lock`
  on null, string indexing, a throwing `ToString` in concatenation,
  `int.Parse`, a null dereference or an exception filter was correctly
  treated as reachable. Code after a call to a virtual, abstract or default
  interface method whose base body never returns was kept, and callee loops
  that exit through `goto`, `break` inside `try`/`finally`, `using` or
  `lock`, a `catch`, a `return` inside `switch`, or an exception caught
  outside the loop were all treated as able to return. Guards on locals
  assigned through `??`, `?.`, `&&`, `||` and `?:` did not prune live
  branches in the effect analysis. Apart from `using` (see the P0 entry),
  every other specially modeled construct placed inside `catch` or
  `finally` was reported: string concatenation with a side-effecting
  `ToString`, `lock`, type initialization, a `foreach` enumerator in a
  helper's `finally`, nested `try`/`catch`, explicit throws, divisions, and
  writes through locals that alias caller state. Facts that hold only on
  the normal path out of a `try` (a divisor, a null check, an index or an
  overflow bound assigned at the end of the `try` body) were not used to
  discharge obligations after a `catch` that skips the assignment.
- **Ownership, aliasing and other scopes.** Writes through locals that
  start fresh and are then pointed at caller state (reassignment, a branch,
  `??`, `?:`, a returned argument, an object or array or struct holding the
  argument, a static field, swaps) were all reported, while writes that stay
  inside fresh objects and arrays were accepted. A `params` parameter
  written by the callee was treated as caller-visible when the caller
  passes its own array. Side-effecting and throwing exception filters, a
  `ref struct` pattern `Dispose` in a helper, `extern`/`DllImport` methods
  and function pointers were reported or failed closed. The worker never
  refuted an effect claim whose `throw` is caught locally or whose
  allocation is unreachable. SP0027 handled constructor calls,
  `this(...)`/`base(...)` initializers and target-typed `new` correctly.
  `[SharpProofSuppress]` on a class hid its method-level diagnostics but
  not SP0024 contract-placement errors or SPCF companion errors.
- **Completion, operators and summary import.** Helpers whose `throw` is
  caught internally (by the exact type, a base type, a filter, a bare
  `catch`, after a nested `finally`, or after a rethrow) were treated as
  returning, so writes after the call were reported. `using` in loop
  bodies, `switch` sections, `lock` bodies, after a `goto` label, in
  `do`/`while (false)` and in `else` branches of helpers was reported.
  User-defined `operator true`/`false`, `&`/`|` under `&&`/`||`, implicit
  and explicit conversions and `++` failed closed in the selected method
  and were reported through helpers. A callee's effect summary was imported
  only when its `Requires` or `[Positive]` precondition was established at
  the call site (a violated literal, an unknown argument, a guard on another
  variable and a weaker guard all gave SP0047 `CallPreconditionNotProven`).
- **Other specially modeled constructs.** `??=` into a static field, an
  array element, an object field or with a side-effecting right-hand side
  was reported, while `??=` on locals and by-value parameters was accepted.
  Interpolated strings converted to `FormattableString` or `IFormattable`
  were reported as allocating (and, correctly, not as running `ToString`).
  `volatile` reads, `lock`, `Interlocked` and `Thread.MemoryBarrier` were
  reported under `[AllowedCapabilities(None)]`. Lifted `checked`
  increments, compound assignments, negation and multiplication on
  nullable integers were reported (only lifted *conversions* are affected by
  the checked-conversion entry above). Newer language features were handled
  conservatively: accessors that write through the `field` keyword
  (including a lazy `field ??= new()` getter) and C# 14 `extension` block
  properties and methods with side effects were reported or failed
  closed. Members that *always* throw were reported when reached through a
  helper, whether a property getter, indexer, setter, user-defined `+`,
  explicit or implicit conversion, `operator true`, custom event `add`,
  constructor, record copy constructor (`with`), or a `Length` or property
  used by a list or property pattern; deconstruction was the only
  construct where an always-throwing member was lost (see the P0 entry),
  and even there a `catch` around it was correctly treated as reachable.
  Synchronous callers of `async Task`, `async ValueTask` and `async void`
  helpers were charged with the helpers' synchronous writes, exceptions and
  allocations. Exception escape through handlers was modeled precisely: a
  catch of a subtype or sibling, a filter that may be false (including a
  call that always returns `false`), `throw;`, a new throw in a catch, a
  throw in `finally` and a rethrow into an outer unrelated catch all kept
  the exception escaping, while exact, base-type and `when (true)` catches
  removed it.
- **Newer language features and other compiler-inserted calls.** Writes
  through `ref` fields, inline-array element access, collection expressions
  (including `[CollectionBuilder]` builders, collection-initializer `Add`
  methods and spreads), `params` collections and `fixed` over a
  `GetPinnableReference` all failed closed. C# 14 null-conditional
  assignment (`t?.F = 1`, `t?.F += 1`), partial property bodies, and
  C# 11 `checked` user-defined operators and conversions (the checked
  variant was the one charged in a `checked` context) were reported
  directly or through a helper. Apart from `++`/`--` and `ITuple` (see the
  P0 entries), user-defined conversions and overrides that the compiler
  calls implicitly were all reported through helpers: in compound
  assignment (including `int += wrapper`), unary `-` and `~`, relational
  operators, array indexes, conditional-expression arms, `??`, `??=`
  (including `Wrapper? w; w ??= 5`), tuple conversions, tuple deconstruction (from a
  tuple or a `Deconstruct` with convertible `out` types), tuple equality,
  `if` and `switch` governing expressions, compound string concatenation
  with a side-effecting or throwing `ToString`, record `==`, `Equals` and
  `GetHashCode` over a field with side-effecting overrides, anonymous-type
  `Equals`, `GetHashCode` and `ToString`, `ValueTuple.Equals`, `foreach`
  element casts and unboxing (including in a `catch` around the loop),
  and list-pattern `Slice` and range indexers on user types, strings and
  lists. Instance calls on a struct with an explicit static constructor
  were charged with its type initialization, which is what the runtime
  does (a class instance was, correctly, not). The effect analysis does no
  exact-type devirtualization, so stale receiver types after a reassignment
  cannot hide an override. `new T()` through a generic helper was charged
  with the instantiated constructor's writes and exceptions, and a
  `System.Threading.Lock` in `lock` failed closed. Synthesized record
  `Deconstruct` and `ToString` members that call user-written property
  getters were charged with the getters' effects. C# 14 partial
  constructors and partial events were charged with their implementation
  bodies, and a C# 13 `^` index in a nested object initializer
  (`new Holder { Items = { [^1] = 3 } }`) was charged with the write into the
  array its getter returns. (User-defined instance compound-assignment and
  increment operators could not be tested: Roslyn 4.14 rejects them.)
- **Aliases, struct receivers and recursion.** Writes through pattern
  variables (`is`, `switch` case labels, switch-expression arms, property,
  list and `not` patterns), deconstruction variables, `out var` results,
  tuple element copies and `using var` resources that alias a caller's
  object were all reported. So were struct instance methods (including
  `this = ...`) called on an element of a caller's array, on a struct
  field of a caller's object, on a nested struct field, and through a
  `ref` local, while the same call on a by-value struct copy was accepted.
  Mutually recursive helpers with a static write or `throw` anywhere in the
  cycle were reported in either declaration order, and a helper whose
  only normal return goes through a mutually recursive call was still
  treated as able to return, so writes and handlers after it were kept.
- **Flow facts through aliases, dispatch and imported preconditions.** A
  local changed through a `ref` local, a conditional `ref`, a `ref` or
  `out` argument, a lambda, a local function, a `Span` over it, a
  deconstruction or compound assignment through a `ref`, or a `ref`
  reassignment was never assumed to keep its old value: divisions, null
  dereferences and indexing after each change were reported. Writes
  through `ref`-returning properties, indexers and methods were reported.
  Calls on a receiver whose static type is sealed were resolved to the
  override the runtime calls, including one inherited from a non-sealed
  intermediate class. A callee's effect summary was not imported when its
  `Requires` was violated or unproven at a property setter, `init`
  accessor in an object initializer, indexer getter, constructor,
  instance or extension method, compound assignment or increment call site
  (SP0047 `CallPreconditionNotProven` in every case). `ToString` on
  `Nullable<T>` of a user struct, through `+`, `+=`, interpolation and a
  direct call, was charged with the struct's override.
- **Patterns, operators and records.** Property patterns (including
  extended `{ Inner.Value: 1 }` patterns, `or`/`not` combinations, switch
  statement case labels, switch-expression arms, list-pattern elements and
  `var` subpatterns) were charged with the getters they call, including
  interface getters and a getter that always throws. `checked` user-defined
  `++` and `--` operators were selected in a `checked` context and charged.
  A user-defined `==` or `!=` that does not behave like a null check was not
  trusted as one, while `is null` was. `with` on a non-sealed record class
  was treated as a virtual clone. Facts about struct fields, object fields,
  static fields and array elements were invalidated by `ref` calls,
  mutating struct methods, calls on the object, writes through a possible
  alias and static mutators.
- **More handler reachability.** A `catch` reached only through a struct
  resource's throwing `Dispose` (a `using` statement or declaration), a
  custom enumerator's throwing `MoveNext`, `Current` or `Dispose`, a
  throwing `ToString` in `+`, `+=`, `string.Concat` or interpolation (with
  and without alignment, for classes and structs), a throwing property in a
  property pattern, switch-expression arm or `when` guard, a throwing
  `Length` in a list pattern, or a throwing `Deconstruct` in a positional
  pattern was treated as reachable, so its writes were reported. Native
  integer division was charged with both `DivideByZeroException` and the
  `MinValue / -1` `OverflowException` (conservatively, since guards on
  `nint`/`nuint` divisors are not used). Contract expressions that can be
  undefined (division or remainder by a parameter, `checked` overflow,
  `MinValue / -1`) gave `PostconditionMayBeUndefined` unless a
  short-circuit guard made them defined, and SP0027 stayed silent for calls
  after an always-throwing helper, after a folded early `return`, and in
  branches that cannot run. Exception filters that throw were modeled the
  way the runtime behaves: the filter's own exception was swallowed and the
  original exception kept escaping, and a `catch` whose filter always
  throws was treated as never entered. Because the CLR evaluates an outer
  filter before an inner `finally` runs (two-pass handling), a filter such
  as `when (d == 0 && Note())` after `try { ... } finally { d = 1; }` does
  run `Note()`; the analysis did not use the post-`finally` value of `d` to
  prune the filter, so the write and the escaping exception were both
  kept.
- **Documented SP0024 triggers and source effect declarations.** Every
  SP0024 case listed in `docs/diagnostic-examples.md` (blank
  `[SharpProofTrusted]` and `[SharpProofSuppress]` reasons, `[NotNull]` on
  a value type, `[Positive]` and `[InRange]` on unsupported types, unordered
  `[InRange]` bounds, conditional and late `Requires`, non-exception
  `[AllowedExceptions]` types and undefined capability flags) was reported
  with a specific message, on parameters and on return values. A source
  method whose `[EffectContract]` does not cover its own body was reported
  (SP0047 `EffectContractDoesNotCoverBodySummary`), and its callers were
  judged by the body rather than the declaration, so they were not proven.
- **Effect refutations.** No false `Refuted` effect claim was found. Claims
  that are true at run time only because a `finally` or `Dispose` replaces
  the thrown exception, because a `[Conditional]` call and its arguments
  are elided, because the allocation or `throw` follows a call that never
  returns, or because a `throw` is caught locally were all `Proven` or
  `Unknown`.
- **Contracts over small and unsigned integers.** Postconditions over
  `uint`, `ushort`, `byte`, `sbyte`, `short` and `char` arithmetic,
  wrapping casts, shifts and bitwise operators were all `Unknown`
  (`UnsupportedBody` or `UnsupportedExpression`) rather than guessed, and
  the two that only widened or compared values (`long r = a;` for a `uint`
  `a`, and a mixed `uint`/`int` comparison) were correctly `Proven`.
- **Resource bounds.** Apart from the stack-overflow and exponential-time
  entries above, pathological shapes stayed within budget: 40 nested
  `try`/`catch` blocks and 25 nested `try`/`finally` blocks were analyzed
  in about two seconds, and an 800-case `switch`, 200 sequential
  `try`/`catch` statements, 1,000 to 2,000 sequential `if` statements and
  300- and 400-deep nested `if` statements were cut off with SP0047
  `BlockBudgetExceeded`. Sixty sequential `if`/`else` diamonds (2^60 paths)
  were analyzed in a second. Relational summaries over doubling helper
  chains stopped growing and fell back to `Unknown` beyond ten levels, so
  manifests stayed under a megabyte. Nesting 500 `if` statements overflows
  inside Roslyn's own `CSharpOperationFactory`, which is outside SharpProof.
  Analyzer time grew linearly with the number of annotated methods (500 to
  4,000 `[DoesNotThrow]` methods, each calling two helpers, took 1.4 to 5.9
  seconds), and 800 annotated methods over one 800-deep chain of
  expression-bodied helpers took about a second, so shared summaries are
  reused across selected methods. A single method with 2,000 violating
  top-level calls produced all 2,000 SP0027 diagnostics in under five
  seconds.
- **Closed attributes and integer domains.** `[InRange]`, `[Positive]` and
  `[NotNull]` on parameters and return values produced correct `Proven` and
  `Refuted` results, and an inverted `[InRange(10, 0)]` failed closed
  (`UnsupportedContract`). Source intervals for `sbyte`, `byte`, `short`,
  `ushort`, `char`, `int` and `uint` parameters were exact at both ends: each
  false bound was refuted at the boundary value and each true bound was
  proven with a `domain:` core. `[ContractFor]` companion clauses are
  checked against the target's own body (a false companion `Ensures` was
  refuted) and are not trusted by callers. A `Requires` in an interface
  companion was not assumed when verifying a class that implements the
  interface (its postcondition was refuted with an input the companion
  excludes).
- **Reference-pack identities.** Compiled against the `netstandard2.1`
  reference pack instead of the runtime, the analyzer produced the same
  results as on .NET 9: runtime exceptions resolved to the `netstandard`
  types, `[AllowedExceptions(typeof(DivideByZeroException))]` matched them,
  the `Math.Abs(int)` specification (including its `OverflowException`)
  was found through the `netstandard` facade, and SP0027 reported a
  `[Positive]` violation.
- **Identity and configuration.** Summaries are resolved per symbol, so two
  same-named `file`-local helpers do not share evidence. `#line` directives
  (including `hidden` and span forms) do not disturb manifest emission.
  Invalid `SharpProofFeatures` values are reported as SP0025 rather than
  silently disabling analysis, and `[SharpProofSuppress]` removes reporting
  without producing a proof.
