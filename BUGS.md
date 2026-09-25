# Bug backlog

## Current audit and evidence

Updated on 2026-09-25. The findings below were audited against baseline `1d96799e6` (`Fix contract semantics, worker ownership, and evidence recovery`). In this working tree, the compound-assignment false-proof, managed exception-region false-proof, completion-analysis recursion-budget and call-graph blowup, rotating-seed fuzz coverage, malformed UTF-16 canonical-hash collision, null module-reference validation, rejected-cache capacity maintenance, pilot publication-evidence binding, managed struct receiver-write, qualification evidence-admission, qualification receipt snapshot-binding, MSBuild published-result invocation binding, advisory attribute-alias activation, B5 congruence interval normalization, B6 frontend evaluation-order snapshots, B11 root-enumeration ownership, B15 catch-filter rethrow identity, B16 pilot-review handoff, B21 Linux process-stat truncation and delimiter validation, B22 run-stable release-workflow artifact naming, B23 SPMETA002 nested mutable static-state coverage, B24 cancellation-safe companion cache, B25 null/blank lowered callable ID rejection, B26 canonical lowered-variable absent-state sentinel, B27 solver-incompleteness classification, B28 vacuity presentation, B29 stable SP0027 source-clause messages, B30 launcher timeout attribution, B31 first-statement effect refutation replay, B32 event-accessor callable-kind alignment, B35 nameof operand traversal, B36 SPMETA009 formatting API coverage, B37 SPMETA004 comparison-boundary coverage, B38 reflective trusted-object construction coverage, B39 SPMETA010 semantic cache-write coverage, B40 changed-line block-comment coverage guard, B43 SPMETA005 descriptor-catalog ownership, B44 recursive SPMETA006 IR string-capable member detection, B45 code-page and invalid-UTF8 source rebinding, B46 local-function call-site precondition coverage, B48 linear call-site precondition prefix completion, B50 nested-try exception-handler stack exhaustion, B51 strict-proof README example, B52 nullable and generic NotNull contracts, B53 nested SP0027 call-site replay, B57 pilot diagnostic occurrence and review-ledger binding, B67 suppression claim omission, B72 top-level source rebinding, B73 return-attribute active-source rebinding, B74 release-resume, B75 AggregateException cancellation forwarding, B17 cold framework-package bootstrap, B18 nullable value-type receiver, B19 signed-remainder normal-completion, B20 SARIF assumption-result kind and level consistency, B33 reachable-read-region, B34 implicit-constructor-initializer, B41 trusted-computing-base-completeness, B42 .globalconfig profile consistency, B49 contract-bearing relational-summary, B54 replayable-prefix-completion, B59 guard-clause replayability, B60 numeric-conversion interval preservation, B62 Math.Abs normal-completion support, B63 profile-independent SHARPPROOF_CONTRACTS rejection, B64 complete effect-contract diagnostic classification, B68 async/iterator call-site deferral, B69 malformed callee exception summaries, B70 SARIF source-root-relative locations, B71 checked scalar self-mutation overflow, and the B56 case-sensitive PowerShell path and preprocessor-symbol handling, B65 Z3 payload integrity and B66 inherited runtime environment findings have been fixed and verified, so they are removed from the active backlog. The active backlog now contains **0 findings**: 0 P0, 0 P1, 0 P2, and 0 P3. Former candidate C1 is now B6; no separate candidate remains in this audit. B18 onward come from a fifth pass on 2026-09-22 that ran a real analyzer built from an unchanged `git archive` of HEAD with SDK 9.0.318 outside the container (the pinned 9.0.316 SDK was not installed).
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
pilot IDs. The B57 pilot review fixture verifies SARIF-derived diagnostic
occurrence counts, rejects diagnostic/report mismatches, and binds the reviewed
report and receipt to the complete ledger bytes and dispositions. Receipt probes also
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
| Contracts, Frontend, ContractForGenerator, plus Analyzer/Core, Meta.Analyzers, and Attributes in wave four | Earlier exact frontend IR probes; B6 now snapshots earlier by-value arguments, receivers, and array assignment locations before later `ref`/`out` or closure effects, with six focused regression tests; real analyzer probes cover local/global effect aliases, aliased closed-contract attributes, namespace aliases, unrelated aliases, B15 direct/ref/out catch-filter mutation with bare and unchanged rethrow controls, and B18 nullable receiver calls with `.Value`, boxed-call, and reference controls; B75 covers aggregate and derived catches, filter/catch-order behavior, safe whole-aggregate rethrows, and conservative inner-exception handling; B23 covers the shipped analyzer/verifier namespaces and recursive checks for nested mutable fields, immutable/frozen collection arguments, tuples, and key/value pairs; controls cover ThreadStatic, immutable scalars, scope counters, and compilation/symbol-keyed weak caches | B6 frontend defect fixed and verified; worker consequences remain untested; B15 filter-mutation defect fixed and Meta.Analyzers.Test passes 176/176; B18 false `SP0046`/`SP0045` diagnostics removed, with Analyzer.Test 528/528 and Effects.Test 458/458; B75 aggregate-wrapped cancellation is fixed, with Meta.Analyzers.Test 177/177, ProofKernel aggregate propagation 1/1, and aggregate rethrows before its general failure fallback; B23 SPMETA002 coverage passes Meta.Analyzers.Test 203/203; the production dependency graph compiles with no SPMETA002 findings and Worker.Test passes 753/753; the production complexity gate passes at 238794/14123/6649; B14 alias activation is fixed and covered; B52 accepts nullable-value and possibly-null generic `[NotNull]` contracts, rejects `int` and `T : struct`, and keeps a constructed `T=int` binding fail-closed; Contracts.Test passes 147/147 and Analyzer.Test 551/551; B54 restores SP0027 after flow-proven receiver calls and safe BCL, store, conversion, constant, and bounded-loop prefixes, with unknown parameter receivers silent; B59 suppresses calls after reachable control-flow exits while retaining flow-proven infeasible-guard diagnostics; Analyzer.Test 542/542; B29 allocation-shift, attribute, companion, and bounded-text regressions pass; B32 event add/remove accessors now produce one SP0047 UnsupportedCallable each, including an empty remove, while an empty ordinary control is Proven; B35 type, generic-type, namespace-qualified type, parameter, and member nameof cases are Proven, while a dynamic member access outside nameof still reports UnsupportedCallable; B36 adds Format/Join/Replace and StringBuilder Append/AppendFormat/Insert fragment cases plus ordinary-format controls, with Meta.Analyzers.Test 210/210; B37 now reports semantic literals used through comparer and collection predicates, ordinal string comparisons, and string-backed span SequenceEqual, while ordinary comparisons and diagnostic/log text remain clear; all 12 positive forms and controls pass Meta.Analyzers.Test 212/212; B38 reports reflective outcome, assumption, and effect-summary creation through Activator, ConstructorInfo.Invoke, FormatterServices, RuntimeHelpers, and System.Text.Json deserialization outside allowlisted owners, including same-method Type-local aliases/reassignments and constructed generic Activator method references; later assignments do not taint earlier calls, uninitialized-object APIs remain SPMETA001-forbidden in ProofKernel, and allowlisted owners avoid trusted-target SPMETA011 while unrelated Type-local activations and generic factory references stay clear; Meta.Analyzers.Test 214/214; B39 now recognizes mutable dictionary, concurrent dictionary, ConditionalWeakTable, and Lazy storage through cache/memo fields and properties, flags non-cacheable outcomes through writes and factories, and leaves local per-request storage and Proven/Refuted values clear; the 11-diagnostic focused regression and full Meta.Analyzers.Test pass (215/215), and the production build has no SPMETA010 findings; B43 SPMETA005 now authorizes only resolved descriptor symbols from the owning assembly and exact generated output paths; generated-file and simple-name spoof regressions reject while all three real catalog controls pass; Meta.Analyzers.Test passes 219/219 and Analyzer.Test passes 542/542 with Analyzer.Core compiled under the meta analyzer; the semantic descriptor audit covers target-typed creations across all nine production projects directly referencing Roslyn; B44 recursively detects string-capable fields and auto-properties through arrays, generic and containing-type arguments, tuples, object, char, and reference-capable type parameters; only exact intern, diagnostic-detail, runtime-payload, synchronization, and guarded external-identity cache members are allowed; Meta.Analyzers.Test passes 226/226 and Analyzer.Test passes 542/542; B53 replays nested expression-bodied, interpolation, constructor-initializer, object-creation, and using-declaration call sites while preserving may-throw prefixes; focused tests pass 4/4, Analyzer.Test 554/554, and Effects.Test 464/464; B46 direct local-function calls bind their local Requires clauses, normal/static and definitely assigned local arguments report SP0027, safe arguments remain clear, captured values remain Unknown, lambda and anonymous-method Requires report SP0024, and Analyzer.Test passes 545/545; B48 indexes replayable prefixes once per operation block; 4,000-call analysis measured 802 ms versus 589 ms for 500 calls (1.36x time for 8x calls), with no diagnostics; baseline was 21.709 s versus 914 ms (23.7x); a possible if/throw exit still blocks replay; Analyzer.Test passes 547/547; B64 emits SP0052 Warning for complete EffectContract summary mismatches and preserves SP0047 for incomplete analysis, including the UnsupportedCallable control; a covered body remains silent; complete state-write, intrinsic-length, and throws-only mismatch regressions assert SP0052 without changing worker outcomes; focused source-contract tests pass 4/4, severity/summary controls 2/2, full Analyzer.Test 555/555, generated-output verification 14/14, exact complexity gate at 242832/14449/6729, and ArchitectureTest 442/442; B69 error-typed callee throws now become Unknown with UnsupportedOperation instead of AD0001, while valid typed throws keep SP0046 and diagnostic type formatting falls back safely for error/unencodable symbols; focused regressions pass 3/3, full Analyzer.Test 560/560, Effects.Test 467/467, exact complexity gate 1/1 at 243600/14501/6766, and ArchitectureTest 442/442 |
| Effects, Dataflow, and shared throw facts | B60 preserves operand intervals through range-preserving integral conversions, uses operand states when compiler-generated conversion nodes lack CFG state, and keeps narrowing fail-closed unless the interval fits; bounded facts traversal reached 600 helpers; B1 now has a shared completion-depth limit and deep-chain/tree regressions; B10 now checks managed receiver and boxed-value writes against concrete runtime mutation, with unmanaged-copy controls; B5 regressions cover equivalent boundary congruences, hashes, mutual inclusion, no-op refinements, neighboring residues, strict subsets, bottom, singletons, and outside-carrier values; B24 reuses one Compilation across a canceled policy and a live policy, with companion-positive and unrelated-negative controls; B50 adds a 64-level exception-handler recursion/stack budget with a fail-closed incompleteness signal, while nested using disposal retains its existing conservative behavior | B1 and B10 fixed and verified; B5 endpoint normalization is fixed and Dataflow.Test passes 60/60; B54 flow-aware definite-completion regressions pass, including unknown-receiver and array/field/property controls; B24 shared companion-cache cancellation poisoning is fixed; the focused policy regression passes 1/1 and full Effects.Test passes 464/464; B50 focused Analyzer.Test regressions pass for 250/1,000 nested tries and the 250 nested-using control, each expecting SP0047 without a crash; full Analyzer.Test passes 550/550 and Effects.Test passes 464/464; end-to-end worker replay remains untested; B60 Effects.Test passes 465/465 and Analyzer.Test 555/555, with all widened arithmetic and a range-proven narrowing Proven while unbounded int-add and long-narrow controls retain SP0046; B68 regressions show direct Task, ValueTask, and ValueTask<T> body faults are deferred while pre-await writes and type-init throws remain; unused iterators drop body effects, while await/result/wait, foreach enumeration, and returned sequences retain them; an async task-factory chain abstains with SP0047. Effects.Test passes 467/467 and Analyzer.Test passes 557/557; B71 adds pre-operation interval checks for built-in scalar increments/decrements and add/subtract/multiply compound assignments, while ref aliases, complex lvalues, mutating RHS expressions, and boundary/unknown intervals remain Unknown; focused regression passes 1/1, full Effects.Test 467/467, full Analyzer.Test 561/561, exact complexity gate 244068/14545/6771, and ArchitectureTest 442/442. |
| IR, SMT, Summaries, and Verify | Earlier B3/B11 probes and Summaries 15/15; B11 now snapshots roots once before validation and processing, with changing-list tests for empty and nonempty replacement maps; B27 solver `incomplete` answers now map to a typed semantic Unknown; full SMT suite 39/39 | B3 high/low surrogate hashes reject, replacement-character and supplementary Unicode hashes remain distinct, and custom-table lookup/digest regressions pass; B11 changing-root regressions reproduce before the fix and pass after it, with IR 128/128 and Summaries 15/15; B4 null module rows produce typed `JsonException` and structured `CompilerManifestMismatch` responses through `VerifyAsync` and CLI; B7 rejected-read capacity reconciliation passes direct and complete Worker regressions, valid-hit, ordinary-miss, and lock-failure controls; B27 nonlinear incompleteness no longer fails the worker run; worker suite 736/736 and protocol/package validation passed; foreign actuals rejected by replacement validation, null models rejected before replay; downstream gaps remain |
| Worker, Protocol, CompilerArtifact, CompilerCollector, and Specs | Earlier B3 canonical-hash and B4 validator probes; canonical hashing now rejects malformed UTF-16 while preserving valid UTF-8 bytes; null module-reference rows now reject before module-name access; rejected cache reads now stage the bad entry and reconcile capacity under the cache lock; real cache/filesystem reads compare absent, malformed, oversized, semantic-rejection, and held-lock cases; follow-up same-length, resealed source-span relocation probe; B67 method/type/assembly suppression passed collector and strict MSBuild/worker regressions; B19 int32 remainder summaries now bound the signed quotient | B3 high/low surrogate hashes reject, replacement-character and supplementary Unicode hashes remain distinct, and custom-table lookup/digest regressions pass; B4 null module rows produce typed `JsonException` and structured `CompilerManifestMismatch` responses through `VerifyAsync` and CLI; B7 rejected-cache capacity reconciliation passes direct and complete Worker regressions, valid-hit, ordinary-miss, and lock-failure controls; B19 collector-worker claims prove overflow exclusion while division, long-remainder, and `(7, -1) => 0` controls pass; B49 source-summary lowering now removes conditionally-elided CFG expression statements through InvocationEmissionPolicy; regressions cover Requires, Ensures, and Assume callers plus emitted-contract and invalid-precondition controls. Worker.Test source-authority suite 34/34, including the B72 ordinary-callable full-file-span negative control; B54 now records DoesNotThrow plus Terminates for reviewed Math and String BCL calls with runtime witnesses; Specs.Test 108/108; B67 suppression now retains claims and strict verification rejects refutations; B72 projects the synthesized Main callable authority through the last top-level statement, trimming trailing trivia while containing the claim; the Worker authority regression rejects full-file spans on ordinary callables; B73 resealed call-, string-, and inactive-preprocessor-span rebindings reject; B25 null, empty, and whitespace lowered callable IDs reject as typed JsonException during deserialization and CompilerManifestMismatch during worker input loading; B26 resealed parameter rows reject -2 and int.MinValue while the -1 control round-trips; B31 first-statement object/array allocation, empty-lock, and explicit-throw events now refute claims despite trailing statements, while conditional and later events remain Unknown; Worker.Test passes 759/759; B45 accepts compiler-recorded code-page sources and Roslyn-compatible replacement-decoded invalid UTF-8 after registering CodePagesEncodingProvider in the launcher; SHA-256 text identity still rejects appended-byte tampering, and unknown encodings or malformed BOM data report contextual InvalidDataException; active controls cover nested and elif branches across four symbol sets, plus directive-looking lines inside multiline raw strings and comments; downstream proof impact is untested |
| Host, BuildTasks, Launcher, Gates, scripts, Tools, and .github | Earlier B2/B8/B9/B12/B13 probes; B2 campaign scheduling now coalesces a colliding rotating/retained seed at the larger requested case count and derives budget/evidence totals from that schedule; B8 and B12 now validate response status, strict outcomes, and exact qualification evidence token types through the real receipt writer; B13 now binds validation and receipt metadata to one byte snapshot; reviewed workflow receipt producers/dependencies and exercised actual framework-source helper with empty/prepared caches; B22 uses run-stable names for package, package-consumer qualification, and portable receipts, with a partial-rerun dependency regression; B16 review handoff now binds the human ledger to the original tag-run report and package artifacts; B47 now solves source-method completion dependencies with a bounded iterative graph and memoizes definite-completion queries; build-task validation now compares published results with the exact private response from that invocation; B74 assessed standard NuGet V3 main-package download and repeated symbol-publish behavior; B17 now bootstraps missing framework archives from NuGet and validates package identities; B28 makes vacuity visible in console and SARIF while preserving ordinary Proven presentation; B30 classifies timeout diagnostics from exact callable coverage reasons while retaining expected timeout status handling; B56 preserves case-sensitive Git paths in loop snapshots and distinct C# preprocessor symbols in source metrics | B2 schedule fixtures cover rotating budgets below, equal to, and above retained coverage, distinct-seed budgeting, and an injected failure after the short rotating prefix; B8/B12 admission and B13 snapshot-binding boundaries covered by writer fixtures; B16 resume rejects stale, wrong-commit, and incomplete review evidence; build-task tests reject a different private invocation hash and keep a matching control, and an architecture test binds the production target to the private result path; B17 regression covers cold and prepared caches and rejects a package with mismatched identity; B47 tests cover a 1,000-method chain, a 20-method recursive graph, shared definite-completion calls, and direct self-recursion; B72 package/launcher integration reaches the worker with a top-level claim followed by a trailing newline and comment; B22 artifact-name and producer/dependency topology regression passes in ArchitectureTest; B74 retry guard is covered by mocked exact/mismatched main bytes, canonical-feed capability, digest-plan binding, and push-sequence fixtures; B28 console and SARIF regressions cover both vacuity kinds and an ordinary-Proven control; B30 same-response MethodTimeout/UnsupportedBody and true ProjectTimeout launcher controls pass, and full Package.Test passed in 37 isolated shards; B56 `Foo.cs`/`foo.cs` snapshot and `DEBUG`/`debug` parser regressions pass in ArchitectureTest 2/2; no production feed was contacted, no interrupted production release was resumed, and no native workflow was run; package-backed qualification of all five pilot libraries passed on `1e177fe6`; B40 changed-line coverage strips complete inline block comments before checking for whitespace/brace-only lines; statement, return, and comment-transition code changes are uncovered while commented braces remain trivia; CoverageScriptTests passes 38/38; B51 proves the actual README minimal-contract fence through strict collector/worker builds with checked and unchecked compilation; the original int example remains Unknown/UnsupportedBody; targeted Package.Test passes 3/3; no release CI was run; B63 reproduces the profile-off no-op clauses and Result exception, then rejects project-local DefineConstants in all profiles before compilation; focused package tests pass 2/2, full Package.Test had one containment failure whose isolated rerun passed, and Analyzer.Test passes 555/555; B70 emits escaped paths under %SRCROOT% as relative URIs while preserving root-equal, sibling-prefix, outside-root, and existing relative-mapped behavior; projection tests pass 10/10, full Package.Test passes in 37 isolated shards, the exact production inventory gate passes at 243812/14517/6769, and ArchitectureTest passes 442/442 |

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
  identical across 23 concurrent runs; B29 now renders SP0027 source clauses with bounded allocation-independent text; worker
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
  identical diagnostics; B29 now removes IR allocation ordinals from SP0027 messages. Handler reachability (12 reachable-`catch` purity
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
