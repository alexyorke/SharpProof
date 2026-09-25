# Bug backlog

## Current audit and evidence

Updated on 2026-09-24. The findings below were audited against baseline `1d96799e6` (`Fix contract semantics, worker ownership, and evidence recovery`). In this working tree, the compound-assignment false-proof, managed exception-region false-proof, completion-analysis recursion-budget and call-graph blowup, rotating-seed fuzz coverage, malformed UTF-16 canonical-hash collision, null module-reference validation, rejected-cache capacity maintenance, pilot publication-evidence binding, managed struct receiver-write, qualification evidence-admission, qualification receipt snapshot-binding, MSBuild published-result invocation binding, advisory attribute-alias activation, B5 congruence interval normalization, B6 frontend evaluation-order snapshots, B11 root-enumeration ownership, B15 catch-filter rethrow identity, B16 pilot-review handoff, B21 Linux process-stat truncation and delimiter validation, B22 run-stable release-workflow artifact naming, B23 SPMETA002 nested mutable static-state coverage, B24 cancellation-safe companion cache, B25 null/blank lowered callable ID rejection, B26 canonical lowered-variable absent-state sentinel, B27 solver-incompleteness classification, B67 suppression claim omission, B72 top-level source rebinding, B73 return-attribute active-source rebinding, B74 release-resume, B75 AggregateException cancellation forwarding, B17 cold framework-package bootstrap, B18 nullable value-type receiver, B19 signed-remainder normal-completion, B20 SARIF assumption-result kind and level consistency, B33 reachable-read-region, B34 implicit-constructor-initializer, B41 trusted-computing-base-completeness, B42 .globalconfig profile consistency, B49 contract-bearing relational-summary, B54 replayable-prefix-completion, B59 guard-clause replayability, and the B65 Z3 payload integrity and B66 inherited runtime environment findings have been fixed and verified, so they are removed from the active backlog. Proposed fixes for the other findings have not been implemented. The active backlog contains **30 findings**: 0 P0, 0 P1, 0 P2, and 30 P3. Former candidate C1 is now B6; no separate candidate remains in this audit. B18 onward come from a fifth pass on 2026-09-22 that ran a real analyzer built from an unchanged `git archive` of HEAD with SDK 9.0.318 outside the container (the pinned 9.0.316 SDK was not installed).
The fifth pass also ran generated fuzz campaigns with execution-checked ground truth, stack-exhaustion and timing runs (B47, B48, B50), and end-to-end false-proof confirmations through the collector and in-process worker. The next paragraph describes the evidence of the earlier waves only.
Evidence is scoped per finding. Probes on unchanged sources observed fuzz
scheduling, canonical hashing, interval precision, frontend IR, module-reference
validation, substitution ownership, cache maintenance, effect summaries, and
analyzer diagnostics. A bounded completion-facts probe traversed 600 helpers;
no stack-exhaustion run was attempted. Historical isolated pilot-validator
probes used result-file doubles; the working-tree B8/B9 fixtures now run the real
validator and receipt writer against a temporary Git repository, reject failed
responses and strict Refuted/Unknown claims, preserve advisory Unknown, check
every run-scoped publication file's size and hash, verify the runner emits
JSON-shaped evidence rows before in-memory validation, and bind the request,
compiler manifest, result claims, and SARIF before checking the receipt hash and
pilot IDs. Receipt probes also
exercised an actual admission switch and a simulated changing file view. A framework-source helper was tested with empty
and prepared package caches. Release acceptance on
`1e177fe605e23bda3c9054957bb364a498a8cdf2` passed 57 semantic shards, 37
package shards, 1,000 fuzz cases, 462 corpus cases, and performance checks.
The package-backed qualification also passed for all five pilot libraries on
that commit. No physical file race, native workflow, or release CI run was
executed. Worker
eligibility and false-proof consequences of frontend and effects findings
remain untested. The earlier Summaries suite passed 15/15 tests; it was not
rerun in wave four and does not reproduce or disprove these findings. The B1
change passed Effects 455/455 and Analyzer 521/521; the B8/B9 pilot authority
and receipt regression passed 1/1. Earlier coverage from the 2026-09-16 through
2026-09-18 audits is preserved below.

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
| Contracts, Frontend, ContractForGenerator, plus Analyzer/Core, Meta.Analyzers, and Attributes in wave four | Earlier exact frontend IR probes; B6 now snapshots earlier by-value arguments, receivers, and array assignment locations before later `ref`/`out` or closure effects, with six focused regression tests; real analyzer probes cover local/global effect aliases, aliased closed-contract attributes, namespace aliases, unrelated aliases, B15 direct/ref/out catch-filter mutation with bare and unchanged rethrow controls, and B18 nullable receiver calls with `.Value`, boxed-call, and reference controls; B75 covers aggregate and derived catches, filter/catch-order behavior, safe whole-aggregate rethrows, and conservative inner-exception handling; B23 covers the shipped analyzer/verifier namespaces and recursive checks for nested mutable fields, immutable/frozen collection arguments, tuples, and key/value pairs; controls cover ThreadStatic, immutable scalars, scope counters, and compilation/symbol-keyed weak caches | B6 frontend defect fixed and verified; worker consequences remain untested; B15 filter-mutation defect fixed and Meta.Analyzers.Test passes 176/176; B18 false `SP0046`/`SP0045` diagnostics removed, with Analyzer.Test 528/528 and Effects.Test 458/458; B75 aggregate-wrapped cancellation is fixed, with Meta.Analyzers.Test 177/177, ProofKernel aggregate propagation 1/1, and aggregate rethrows before its general failure fallback; B23 SPMETA002 coverage passes Meta.Analyzers.Test 203/203; the production dependency graph compiles with no SPMETA002 findings and Worker.Test passes 753/753; the production complexity gate passes at 238794/14123/6649; B14 alias activation is fixed and covered; B54 restores SP0027 after flow-proven receiver calls and safe BCL, store, conversion, constant, and bounded-loop prefixes, with unknown parameter receivers silent; B59 suppresses calls after reachable control-flow exits while retaining flow-proven infeasible-guard diagnostics; Analyzer.Test 534/534 |
| Effects, Dataflow, and shared throw facts | Bounded facts traversal reached 600 helpers; B1 now has a shared completion-depth limit and deep-chain/tree regressions; B10 now checks managed receiver and boxed-value writes against concrete runtime mutation, with unmanaged-copy controls; B5 regressions cover equivalent boundary congruences, hashes, mutual inclusion, no-op refinements, neighboring residues, strict subsets, bottom, singletons, and outside-carrier values; B24 reuses one Compilation across a canceled policy and a live policy, with companion-positive and unrelated-negative controls; the adjacent cache audit distinguishes transient CWT factory failures from cached Lazy exceptions | B1 and B10 fixed and verified; B5 endpoint normalization is fixed and Dataflow.Test passes 60/60; B54 flow-aware definite-completion regressions pass, including unknown-receiver and array/field/property controls; B24 shared companion-cache cancellation poisoning is fixed; the focused policy regression passes 1/1 and full Effects.Test passes 464/464; end-to-end worker replay remains untested |
| IR, SMT, Summaries, and Verify | Earlier B3/B11 probes and Summaries 15/15; B11 now snapshots roots once before validation and processing, with changing-list tests for empty and nonempty replacement maps; B27 solver `incomplete` answers now map to a typed semantic Unknown; full SMT suite 39/39 | B3 high/low surrogate hashes reject, replacement-character and supplementary Unicode hashes remain distinct, and custom-table lookup/digest regressions pass; B11 changing-root regressions reproduce before the fix and pass after it, with IR 128/128 and Summaries 15/15; B4 null module rows produce typed `JsonException` and structured `CompilerManifestMismatch` responses through `VerifyAsync` and CLI; B7 rejected-read capacity reconciliation passes direct and complete Worker regressions, valid-hit, ordinary-miss, and lock-failure controls; B27 nonlinear incompleteness no longer fails the worker run; worker suite 736/736 and protocol/package validation passed; foreign actuals rejected by replacement validation, null models rejected before replay; downstream gaps remain |
| Worker, Protocol, CompilerArtifact, CompilerCollector, and Specs | Earlier B3 canonical-hash and B4 validator probes; canonical hashing now rejects malformed UTF-16 while preserving valid UTF-8 bytes; null module-reference rows now reject before module-name access; rejected cache reads now stage the bad entry and reconcile capacity under the cache lock; real cache/filesystem reads compare absent, malformed, oversized, semantic-rejection, and held-lock cases; follow-up same-length, resealed source-span relocation probe; B67 method/type/assembly suppression passed collector and strict MSBuild/worker regressions; B19 int32 remainder summaries now bound the signed quotient | B3 high/low surrogate hashes reject, replacement-character and supplementary Unicode hashes remain distinct, and custom-table lookup/digest regressions pass; B4 null module rows produce typed `JsonException` and structured `CompilerManifestMismatch` responses through `VerifyAsync` and CLI; B7 rejected-read capacity reconciliation passes direct and complete Worker regressions, valid-hit, ordinary-miss, and lock-failure controls; B19 collector-worker claims prove overflow exclusion while division, long-remainder, and `(7, -1) => 0` controls pass; B49 source-summary lowering now removes conditionally-elided CFG expression statements through InvocationEmissionPolicy; regressions cover Requires, Ensures, and Assume callers plus emitted-contract and invalid-precondition controls. Worker.Test source-authority suite 34/34, including the B72 ordinary-callable full-file-span negative control; B54 now records DoesNotThrow plus Terminates for reviewed Math and String BCL calls with runtime witnesses; Specs.Test 108/108; B67 suppression now retains claims and strict verification rejects refutations; B72 projects the synthesized Main callable authority through the last top-level statement, trimming trailing trivia while containing the claim; the Worker authority regression rejects full-file spans on ordinary callables; B73 resealed call-, string-, and inactive-preprocessor-span rebindings reject; B25 null, empty, and whitespace lowered callable IDs reject as typed JsonException during deserialization and CompilerManifestMismatch during worker input loading; B26 resealed parameter rows reject -2 and int.MinValue while the -1 control round-trips; Worker.Test passes 754/754; active controls cover nested and elif branches across four symbol sets, plus directive-looking lines inside multiline raw strings and comments; downstream proof impact is untested |
| Host, BuildTasks, Launcher, Gates, scripts, Tools, and .github | Earlier B2/B8/B9/B12/B13 probes; B2 campaign scheduling now coalesces a colliding rotating/retained seed at the larger requested case count and derives budget/evidence totals from that schedule; B8 and B12 now validate response status, strict outcomes, and exact qualification evidence token types through the real receipt writer; B13 now binds validation and receipt metadata to one byte snapshot; reviewed workflow receipt producers/dependencies and exercised actual framework-source helper with empty/prepared caches; B22 uses run-stable names for package, package-consumer qualification, and portable receipts, with a partial-rerun dependency regression; B16 review handoff now binds the human ledger to the original tag-run report and package artifacts; B47 now solves source-method completion dependencies with a bounded iterative graph and memoizes definite-completion queries; build-task validation now compares published results with the exact private response from that invocation; B74 assessed standard NuGet V3 main-package download and repeated symbol-publish behavior; B17 now bootstraps missing framework archives from NuGet and validates package identities | B2 schedule fixtures cover rotating budgets below, equal to, and above retained coverage, distinct-seed budgeting, and an injected failure after the short rotating prefix; B8/B12 admission and B13 snapshot-binding boundaries covered by writer fixtures; B16 resume rejects stale, wrong-commit, and incomplete review evidence; build-task tests reject a different private invocation hash and keep a matching control, and an architecture test binds the production target to the private result path; B17 regression covers cold and prepared caches and rejects a package with mismatched identity; B47 tests cover a 1,000-method chain, a 20-method recursive graph, shared definite-completion calls, and direct self-recursion; B72 package/launcher integration reaches the worker with a top-level claim followed by a trailing newline and comment; B22 artifact-name and producer/dependency topology regression passes in ArchitectureTest; B74 retry guard is covered by mocked exact/mismatched main bytes, canonical-feed capability, digest-plan binding, and push-sequence fixtures; no production feed was contacted, no interrupted production release was resumed, and no native workflow was run; package-backed qualification of all five pilot libraries passed on `1e177fe6`; no release CI was run |

B9 now keeps verifier caches on the task-local filesystem and stores the four publication files under `artifacts/pilots/runs/<runId>/<pilotId>/evidence`. It records their exact sizes and SHA-256 values, binds the request's compiler-manifest hash to the captured manifest, and compares compiler-manifest claims with result claims. The runner emits evidence rows as property-bearing objects before passing its in-memory report to the validator. The authority fixture rejects each file when missing, empty, or altered, plus mismatched request-manifest hashes and result paths from another run.

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

## P3 - Low

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
