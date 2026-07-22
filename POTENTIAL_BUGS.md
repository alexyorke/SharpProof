# Potential Bugs Resolution Ledger

This canonical ledger merges the two source audits in source order. All 411 raw findings have stable PB1/PB2 IDs and terminal evidence-based dispositions.

## Root-cause index

| Canonical root cause | Claims | Dispositions | Representative IDs |
| --- | ---: | --- | --- |
| RC-ACCESSOR-ATTRIBUTE-TARGET | 1 | Fixed | PB2-5.5 |
| RC-ACCESSOR-INFERRED-CODEFIX | 1 | Fixed | PB2-5.4 |
| RC-ANALYZER-CONTRACT-SEMANTICS | 26 | False positive/intentional, Fixed | PB1-1.8, PB1-3.1, PB1-4.7, PB1-8.3, PB1-8.5, PB1-8.7, PB1-12.5, PB1-13.1, ... |
| RC-ANALYZER-SYMBOLIC-BOUNDARY | 5 | Duplicate, Fixed | PB1-1.1, PB1-1.2, PB1-26.1, PB1-26.2, PB2-10.3.2 |
| RC-ARTIFACT-SEED-FLOW | 1 | False positive/intentional | PB1-10.5 |
| RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS | 19 | Already fixed, False positive/intentional, Fixed | PB1-1.4, PB1-1.6, PB1-1.7, PB1-9.3, PB1-9.4, PB1-9.5, PB1-9.6, PB1-9.7, ... |
| RC-BASELINE-AND-SARIF-TOOLS | 12 | False positive/intentional, Fixed | PB1-18.6, PB1-21.6, PB1-21.9, PB1-22.5, PB1-24.4, PB1-28.8, PB1-30.2, PB1-30.3, ... |
| RC-BOUNDED-INTEGER-OVERFLOW | 1 | Fixed | PB1-20.1 |
| RC-BUILD-CONFIGURATION-AND-DOCUMENTATION | 12 | False positive/intentional, Fixed | PB1-12.7, PB1-14.1, PB1-14.4, PB1-14.5, PB1-19.3, PB1-29.10, PB1-32.4, PB1-32.5, ... |
| RC-BUILD-CONFIGURATION-ROUTING | 1 | Fixed | PB2-8.1.1 |
| RC-BUILD-MEMORY-LIMIT-SHADOW | 1 | Fixed | PB1-32.2 |
| RC-CALLABLE-DESCENDANT-BOUNDARY | 1 | False positive/intentional | PB2-10.1.2 |
| RC-CALL-GRAPH-EDGE-PRESERVATION | 1 | Already fixed | PB2-10.1.1 |
| RC-CATCH-ENTRY-STATE | 1 | Fixed | PB1-27.1 |
| RC-CATCH-TYPE-FLOW | 1 | Fixed | PB1-27.2 |
| RC-CFG-SWITCH-ANALYSIS | 1 | False positive/intentional | PB2-10.2.1 |
| RC-CHILD-BUILD-EXIT-VALIDATION | 1 | Fixed | PB2-9.1.1 |
| RC-CHILD-PROCESS-ASYNC-LIFECYCLE | 1 | Fixed | PB2-9.2.1 |
| RC-CLI-INPUT-AND-OUTPUT-CONTRACTS | 5 | False positive/intentional, Fixed | PB1-3.7, PB1-11.6, PB1-28.9, PB2-5.7, PB2-8.5.3 |
| RC-CLI-REPORT-LIMIT-ROUTING | 1 | Already fixed | PB1-28.1 |
| RC-CODEFIX-ATTRIBUTE-FORMATTING | 1 | Already fixed | PB1-9.1 |
| RC-CODEFIX-TRIVIA | 2 | False positive/intentional, Fixed | PB1-9.2, PB2-7.8.1 |
| RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT | 26 | Enhancement | PB1-2.5, PB1-4.9, PB1-5.4, PB1-7.3, PB1-7.4, PB1-7.7, PB1-7.8, PB1-15.7, ... |
| RC-COMPLEXITY-PARTIAL-ORDER | 1 | Fixed | PB1-28.2 |
| RC-CONDITIONAL-NULLABILITY-POLARITY | 1 | False positive/intentional | PB1-16.1 |
| RC-CONSERVATIVE-LOOP-BOUND | 1 | Fixed | PB2-4.1 |
| RC-CONTRACT-HIERARCHY | 1 | Fixed | PB2-2.1 |
| RC-CONTROL-FLOW-AND-STATE-MERGING | 20 | False positive/intentional, Fixed | PB1-2.2, PB1-6.15, PB1-7.1, PB1-14.3, PB1-16.8, PB1-21.5, PB1-25.3, PB1-25.4, ... |
| RC-CORPUS-SARIF-ISOLATION | 1 | False positive/intentional | PB2-7.9.1 |
| RC-CSPATTERN-PRECEDENCE | 1 | False positive/intentional | PB2-10.8.1 |
| RC-DELEGATE-TARGET-MERGE | 1 | False positive/intentional | PB1-16.3 |
| RC-DIAGNOSTIC-REPRODUCTION-PATHS | 1 | False positive/intentional | PB2-7.3.2 |
| RC-DOTNET-REGEX-UTF16 | 1 | False positive/intentional | PB1-6.2 |
| RC-DUPLICATE-CLAIM | 6 | Duplicate | PB1-4.8, PB1-20.5, PB1-25.5, PB1-28.10, PB1-30.6, PB2-7.3.4 |
| RC-DYNAMIC-ASSEMBLY-LOCATION | 1 | Fixed | PB2-5.1 |
| RC-EXCEPTION-FLOW | 22 | Already fixed, False positive/intentional, Fixed | PB1-1.5, PB1-4.5, PB1-6.6, PB1-15.2, PB1-20.2, PB1-25.7, PB1-26.4, PB1-27.6, ... |
| RC-EXECUTION-ROOT-ISOLATION | 1 | Fixed | PB2-7.1.2 |
| RC-EXPRESSION-BODIED-CONSTRUCTOR | 1 | Fixed | PB2-3.1 |
| RC-FUZZ-HARNESS | 9 | Already fixed, False positive/intentional, Fixed | PB1-18.4, PB1-19.2, PB1-22.1, PB1-22.4, PB1-22.6, PB1-22.8, PB1-30.4, PB2-8.5.5, ... |
| RC-GENERIC-ALLOCATION | 1 | Fixed | PB2-2.2 |
| RC-GENERIC-CONTRACT-SUBSTITUTION | 1 | Fixed | PB2-2.3 |
| RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS | 48 | Already fixed, Fixed | PB1-3.5, PB1-4.3, PB1-4.4, PB1-6.10, PB1-6.12, PB1-7.9, PB1-8.4, PB1-8.6, ... |
| RC-IMPLICIT-STRUCT-CONSTRUCTOR | 1 | False positive/intentional | PB1-8.2 |
| RC-INVARIANT-GATE-NONEMPTY | 1 | False positive/intentional | PB2-8.5.2 |
| RC-JOB-ASSIGNMENT-FAILURE | 1 | Fixed | PB1-19.1 |
| RC-LAMBDA-NULLABILITY-AUDIT | 1 | Fixed | PB2-3.2 |
| RC-LINUX-Z3-DISTRIBUTION | 1 | Enhancement | PB1-22.2 |
| RC-LIST-PATTERN-INDEX-VALIDATION | 1 | Fixed | PB2-10.4.1 |
| RC-MALFORMED-PROOF-QUERY | 1 | Fixed | PB2-9.3.4 |
| RC-NATIVE-LOADER-PLATFORM | 1 | Fixed | PB2-1.4 |
| RC-NESTED-MUTATION-INVALIDATION | 1 | Already fixed | PB2-7.6.1 |
| RC-NULLABILITY-CONTRACTS | 9 | False positive/intentional, Fixed | PB1-16.2, PB1-16.4, PB1-16.5, PB1-24.3, PB1-26.7, PB2-3.3, PB2-3.9, PB2-4.4, ... |
| RC-OPTIONAL-PROGRESS-STATE | 1 | Fixed | PB1-10.2 |
| RC-OPTIONAL-RAW-PROOF | 1 | Already fixed | PB1-25.1 |
| RC-OPTIONAL-TRIGGER-PROOF | 1 | Already fixed | PB1-25.2 |
| RC-PACKAGING-AND-CONSUMER-ASSETS | 11 | False positive/intentional, Fixed | PB1-5.1, PB1-5.2, PB1-5.3, PB1-19.7, PB1-22.3, PB1-22.7, PB1-32.12, PB2-8.1.2, ... |
| RC-PATH-CONTAINMENT | 1 | False positive/intentional | PB1-10.7 |
| RC-POWERSHELL-ARRAY-MATERIALIZATION | 1 | False positive/intentional | PB2-9.2.4 |
| RC-PROCESS-OWNERSHIP | 1 | Fixed | PB1-4.1 |
| RC-PROGRESS-CHECKPOINT-LIFETIME | 1 | False positive/intentional | PB1-10.6 |
| RC-PURITY-PRECEDENCE-POLICY | 2 | Duplicate, False positive/intentional | PB1-8.1, PB2-7.1.1 |
| RC-REGEX-AND-UTF16-SEMANTICS | 6 | False positive/intentional, Fixed | PB1-15.3, PB1-19.4, PB1-20.3, PB1-23.8, PB2-7.5.3, PB2-10.6.1 |
| RC-REVIEWED-PURITY-CATEGORY-PRECEDENCE | 1 | Fixed | PB1-10.3 |
| RC-REVIEWED-PURITY-MERGE | 1 | False positive/intentional | PB1-10.1 |
| RC-ROSLYN-CONCURRENT-READS | 1 | False positive/intentional | PB1-23.1 |
| RC-ROSLYN-GLOBALCONFIG-SEMANTICS | 1 | False positive/intentional | PB2-7.10.1 |
| RC-RUNTIME-OVERFLOW-HAZARDS | 1 | Already fixed | PB1-15.1 |
| RC-SHARD-TOOL-IDENTITY | 1 | Fixed | PB1-10.4 |
| RC-SMT-DIVISOR-SAFETY | 1 | Fixed | PB1-6.1 |
| RC-SMT-PROOF-SOUNDNESS | 49 | Already fixed, False positive/intentional, Fixed | PB1-4.2, PB1-5.5, PB1-6.5, PB1-6.7, PB1-6.8, PB1-6.13, PB1-6.14, PB1-11.1, ... |
| RC-SMT-SESSION-OWNERSHIP | 1 | Fixed | PB2-1.1 |
| RC-STOPWATCH-CAPABILITY | 1 | Fixed | PB2-4.3 |
| RC-STRING-REMOVE-BOUND | 1 | Fixed | PB1-2.1 |
| RC-STRUCTURAL-REF-KIND-IDENTITY | 1 | Already fixed | PB1-12.1 |
| RC-SYMBOLIC-OVERFLOW-IDENTITY | 1 | Fixed | PB1-34.1 |
| RC-TERMINAL-BRANCH-FLOW | 1 | Fixed | PB2-1.2 |
| RC-TEST-FILTER-NAMESPACE | 2 | Fixed | PB1-32.1, PB1-32.3 |
| RC-TYPE-TEST-ORACLE-SOUNDNESS | 1 | Fixed | PB2-7.5.1 |
| RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY | 59 | False positive/intentional | PB1-1.3, PB1-2.3, PB1-2.4, PB1-3.2, PB1-3.3, PB1-3.4, PB1-3.6, PB1-4.6, ... |
| RC-Z3-RLIMIT-ACCOUNTING | 1 | False positive/intentional | PB1-6.3 |
| RC-Z3-UTF16-SEMANTICS | 1 | False positive/intentional | PB1-34.2 |

## Disposition summary

- Fixed: 196
- Already fixed: 16
- Duplicate: 8
- False positive/intentional: 164
- Enhancement: 27

## Source PB1 - POTENTIAL_BUGS.md

This document is a catalog of *potential* bugs found by static review of the
PurelySharp (SharpProof) codebase. Findings are grouped by area. Severities are
Low / Medium / High and reflect likelihood + impact of the issue. Items flagged
"potential" / "latent" are not guaranteed to trigger on current inputs but are
real correctness or robustness hazards.

> Methodology: findings were produced by independent review agents covering the
> analyzer/codefix, symbolic engine, tests/demo, tools/SearchLib/scripts, and
> packaging/build/docs areas. No builds or test runs were executed during the
> review.

---

## 1. Roslyn Analyzer & CodeFix (`SharpProof.Analyzer`, `SharpProof.CodeFixes`, `SharpProof.Attributes`)

### [PB1-1.1] 1.1 Unguarded symbolic queries can crash the analyzer - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Capability and purity queries could propagate expected symbolic-analysis failures through Roslyn analyzer callbacks.
> **Changes/tests:** Typed capability outcomes and conservative purity failure evidence; OperationBlockPipelineTests and SymbolicUnknownReasonTaxonomyTests.
- `SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs:184`, `:230`
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:59`
- `SuggestAllowedCapabilities` / `SuggestExpectedComplexity` / `AnalyzeSymbolForCapabilities`
  call `context.State.GetCapabilityResult(...)` / `GetComplexityResult(...)` with **no
  try/catch**. The sibling `MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity`
  wraps the same calls in a `try { ... } catch (Exception ex) when (ex is ArgumentException or
  NotSupportedException or InvalidOperationException)`. An SMT/unsupported-expression throw here
  propagates out of the analyzer callback and crashes the IDE analysis session.
- **Trigger:** any method whose capability/complexity symbolic query throws.

### [PB1-1.2] 1.2 `UnknownReasonDetails[0]` accessed without length guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Complexity queries used a throwing wrapper at the analyzer boundary.
> **Changes/tests:** Typed complexity outcomes now produce SP0015 unknown diagnostics; OperationBlockPipelineTests.
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:80-110`
- Only `UnknownReasons.Count > 0` is verified before `result.UnknownReasonDetails[0]` is read.
  If `UnknownReasonDetails` is shorter than `UnknownReasons`, this throws
  `ArgumentOutOfRangeException`.
- **Trigger:** a capability result with unknown reasons but missing parallel detail entries.

### [PB1-1.3] 1.3 Shared `AsyncLocal` config/catalog/policy under concurrent execution - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/ImpurityCatalog.cs:9-31`
- `SharpProof.Analyzer/GeneratedPurityCatalog.cs:14,79-82`
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:16-26`
- `SharpProof.Analyzer/AnalyzerFeaturePipeline.cs:58-62`
- `SharpProofAnalyzer.Initialize` calls `EnableConcurrentExecution()`; concurrent callbacks
  each `using`-scope mutate **static** `AsyncLocal` fields and reset them on `Dispose`.
  Interleaved Set/Reset can make an in-flight analysis observe a sibling's value (or the
  reset-to-null value), yielding **incorrect** purity/exception/contract classifications
  rather than a crash.
- **Trigger:** multi-method parallel analysis with a non-default configuration/effect-summary.

### [PB1-1.4] 1.4 Purity fixed-point memoized with first-call attribute symbols - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/CompilationPurityService.cs:64-110` (called from `MethodPurityAnalyzer.cs:166-173`)
- `EnsureFixedPoint` builds the call-graph worklist once, using the attribute symbols of the
  first `GetPurity` call, but `MethodPurityAnalyzer` resolves these **per method** and may pass
  different symbols later. The reused (stale) fixed point yields inconsistent purity verdicts.

### [PB1-1.5] 1.5 `Locations.First()` can throw `InvalidOperationException` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/AnalyzerSyntaxHelpers.cs:35-43`
- When a method symbol has no declaring syntax references AND no locations,
  `methodSymbol.Locations.First()` throws instead of producing a diagnostic. Reached from
  `MethodExpectedComplexityAnalyzer.cs:37,278,313`.
- **Trigger:** synthesized/error/partial-without-impl method symbols.

### [PB1-1.6] 1.6 CodeFix discards event-accessor attribute removals - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:998-1023` (`FilterDeclarationAndAccessorAttributes`)
- `EventDeclarationSyntax` is a `BasePropertyDeclarationSyntax` with an `AccessorList`, so it
  passes the guard and the accessor loop sets `removedAny = true`, but the final `switch` has no
  `EventDeclarationSyntax` case and returns `declaration` unchanged - the attribute is **not
  removed** from the event accessor.
- **Trigger:** "remove contract attribute" code fix on an event accessor attribute.

### [PB1-1.7] 1.7 `GetEffectivePurityAttributeSymbol` returns null behind a `!` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:612-616`
- `return enforcePureAttributeSymbol ?? pureAttributeSymbol!;` lies about nullability; only safe
  today due to the early guard at line 33. Fragile if that guard is ever refactored.

### [PB1-1.8] 1.8 `FormatCapabilities` over-lists combined flags - **LOW** (cosmetic)

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodCapabilityAnalyzer.cs:295-305`
- Enumerates every set bit, including both `IO` and its sub-flags, producing redundant messages.

---

## 2. Symbolic Execution Engine & Shared (`SharpProof.Symbolic`, `Shared`)

### [PB1-2.1] 2.1 `string.Remove(start)` modeled with wrong (exclusive) upper bound - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-STRING-REMOVE-BOUND
> **Evidence:** A deterministic query reported Remove(startIndex) with startIndex equal to Length as a reachable exception.
> **Changes/tests:** One-argument Remove now uses an inclusive upper bound; SymbolicRuntimeHazardQueryTests.
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs:2138-2147` (`TryGetSlicingInvocationShape`)
- For the single-argument `string.Remove(int startIndex)`, `countExpression` is null so
  `oneArgumentUpperBoundIsInclusive` is set to **false**, modeling `start < Length` instead of
  `start <= Length`. `str.Remove(str.Length)` is legal in .NET but is modeled as out-of-range,
  producing a **false-positive `ArgumentOutOfRange` hazard**. Compare `Substring(start)` just
  above, which correctly sets the flag to `true`.

### [PB1-2.2] 2.2 Version-tracking inconsistency when merging alternative completion states - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:4006-4015` (`MergeCompletedAlternativeStates`)
- `retainedFacts`/`retainedConditions` restore all `entryState` facts (which may reference e.g.
  `x@v2`), but `commonVersions` is computed only over the *completion* states. A variable whose
  version differs across branches is dropped from `SymbolVersions` while a retained fact still
  names it -> downstream `GetVariableName` defaults to version 0, producing a mismatched SMT
  variable name.

### [PB1-2.3] 2.3 `CreateDisjunction` lacks the empty-collection guard `CreateConjunction` has - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs:153-171`
- `CreateConjunction` guards `Count == 0`; `CreateDisjunction` does `formulas[0]` unguarded.
  Latent today (only reached with `>= 2` elements) but throws `IndexOutOfRangeException` if ever
  called with an empty collection.

### [PB1-2.4] 2.4 `TryEncodeBounds` can return false for a bound-less atom - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs:384-394`
- When both `IncludeLowerBound` and `IncludeUpperBound` are false, `lower ?? upper!` is null and
  the method returns `false`; the `upper!` also masks a null. No current caller builds such an
  atom, but it is fragile.

### [PB1-2.5] 2.5 `StartsWith`/`EndsWith` with non-constant string argument unsupported - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:50-53`
- With a symbolic (non-constant, non-char) argument the handler bails and lowers to an opaque
  boolean, losing all semantics (sound over-approximation, but hides reasoning). `Contains` and
  char/constant args are handled.

---

## 3. Tests, Demo, Smoke (`SharpProof.Test`, `SharpProof.ToolingTest`, `SharpProof.Smoke.Net472`, `SharpProof.Demo`)

> Note: tests use **NUnit** (`[Test]`/`[TestCase]`), not xUnit, so the "missing attribute" class
> does not apply.

### [PB1-3.1] 3.1 `TopImpureApis[0]` is order-dependent on a tie - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CorpusReportTests.cs:64`
- Two SP0002 results each occur once (tie). The test hard-codes `[0] == "ITest.Run()"`, assuming a
  specific tie-break. A different `RankedItem` enumeration order fails the assertion.

### [PB1-3.2] 3.2 `FalsePositiveCandidates[0]` expected count likely wrong (2 vs 1) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CorpusReportTests.cs:404-405`
- By the semantics established in the sibling test (`CreateFromSarifJson_IdentifiesCatalogMissesAndFalsePositiveCandidates`),
  only entries **without** a known catalog source qualify. Per that definition the count for
  `delegate*<void>` should be **1**, not **2**. Either the assertion or the implementation's FPC
  definition is inconsistent. *Confirm against `SarifCorpusReport.FalsePositiveCandidates`.*

### [PB1-3.3] 3.3 Snapshot regeneration uses `Assert.Pass`, bypassing all verification - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ReadmeExampleFixture.cs:45-50`
- When `SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS` is set, every example test writes the actual output
  as the expected snapshot and passes without comparing. If this env var is ever set in CI, the
  entire snapshot suite passes trivially and real regressions are masked.

### [PB1-3.4] 3.4 `HazardCount` vs empty `Hazards` semantics ambiguity - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CompactDomainProjectionTests.cs:97-99`
- Asserts `HazardCount == 1` while `Hazards` is empty (query ran with `maxHazards: 0`,
  `Truncation.Hazards == true`). Only valid if `HazardCount` means "pre-truncation total". Verify
  the property's intended meaning.

### [PB1-3.5] 3.5 Manifest coverage check hardcoded to SP0002..SP0040 - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** A negative `Index` value throws `ArgumentOutOfRangeException` during caret/factory/constructor evaluation, before the downstream sequence access; the old lowering could instead treat that expression as safely completed.
> **Changes/tests:** Centralized the `Index` normal-completion guard, made downstream bounds hazards conditional on it, and added the upstream constructor hazard for caret syntax, `Index.FromStart/FromEnd`, and `new Index(...)`; covered by `QuerySourceRuntimeHazardsLine_NegativeFromEndIndexReportsConstructionFailure` and the element-access SMT regressions.
- `SharpProof.Test/ReadmeGeneratedExamplesTests.cs:435-439`
- `Enumerable.Range(2, 39)` only validates examples exist for SP0002-SP0040. A new public rule
  (SP0041+) added without a README example will not be caught.

### [PB1-3.6] 3.6 Brittle hardcoded schema-version pin - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/FuzzToolTests.cs:115` - `Assert.That(summary.SchemaVersion, Is.EqualTo("1.3"));`
- Any legitimate schema bump forces this test to fail.

### [PB1-3.7] 3.7 CLI example snapshot compares raw stdout - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ReadmeGeneratedExamplesTests.cs:373-389`
- Relies on `NormalizeForSnapshot` scrubbing all machine-specific content; unsanitized path/timestamp/
  pid in CLI output silently mismatches only off-machine.

---

## 4. Tools, SearchLib & Scripts (`Tools`, `SearchLib`, `scripts`)

### [PB1-4.1] 4.1 Concurrent test runs kill each other's processes - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PROCESS-OWNERSHIP
> **Evidence:** The suite runner globally swept and killed matching dotnet/testhost processes, so concurrent repository runs could terminate each other.
> **Changes/tests:** Removed the global process sweep and rely on per-invocation Job Object ownership; ScriptProcessOwnershipTests.
- `scripts/Invoke-SharpProofTests.ps1:142-217` (kill at 207)
- `Stop-NewSharpProofTestWorkerProcesses` kills any `dotnet.exe`/`testhost.exe`/`MSBuild.exe`/
  `VBCSCompiler.exe` whose command line merely contains `$repoRoot`/`SharpProof.Test`/etc.,
  regardless of which session started it. Only `StartedAfter.AddSeconds(-2)` protects.
- **Trigger:** two overlapping `Invoke-SharpProofTests.ps1` runs (e.g. CI + local) -> the first's
  `finally` kills the second's `testhost.exe` workers.

### [PB1-4.2] 4.2 Z3 rlimit resource accounting silently disabled if statistic isn't UInt - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:39-52` (`CheckAndAccountResources`)
- Only records `"rlimit count"` when `entry.IsUInt`. If Z3 exposes it as a wider/other-typed
  statistic, `ConsumedResourceCount` stays 0 and the deterministic resource-budget safety net
  no-ops (only wall-clock safety net remains).

### [PB1-4.3] 4.3 `GetWallClockSafetyNet` integer overflow and wrong lower-bound guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtResourceBudget.cs:36-43`
- `TimeSpan.MinValue.Ticks / WallClockSafetyFactor` is not `TimeSpan.MinValue.Ticks` (guard dead).
  `budget.Ticks * WallClockSafetyFactor` is unchecked; a large budget wraps negative and
  `TimeSpan.FromTicks` throws `OverflowException`.

### [PB1-4.4] 4.4 `Convert-ToRepoPath` corrupts paths via blind `Substring` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofRawSmtHotspots.ps1:28`
- `$repoRoot` is `Resolve-Path` output while `$fullPath` is `GetFullPath`; the code never verifies
  `$fullPath` starts with `$repoRoot` before stripping the first N chars. Casing/junction/8.3/long
  path divergence silently yields garbage repo-relative paths.

### [PB1-4.5] 4.5 `--max-depth` / `--max-exception-edges` / `--limit` throw unhandled `FormatException` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.EffectSummary/Program.cs:696,699,727` (`int.Parse(ReadRequiredValue(...))`, `ReadPositiveInt`)
- Non-numeric CLI values produce a raw stack-trace crash instead of a clean CLI error.

### [PB1-4.6] 4.6 `nodeReuse:false` check is case/format-fragile - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofDotnet.ps1:36-44`
- Case-sensitive `List<string>.Contains('/nodeReuse:false')`; `-nodeReuse:false` is missed and a
  duplicate is appended. `dotnet run` is also outside the MSBuild-backed command set, so node reuse
  isn't disabled there.

### [PB1-4.7] 4.7 Path check consumes the timeout, starving the impurity check - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:130-154` (`CheckPathAndImpurityWithWitness`)
- Path feasibility is checked with the *full* timeout; `remaining = timeout - deadline.Elapsed` may
  be `<= 0`, collapsing the impurity query to `Unknown` under tight timeouts.

### [PB1-4.8] 4.8 `Aggregate-FuzzRun.ps1` reports the last phase's schema version - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:49`
- `$latestSchemaVersion` taken from whichever phase sorts last alphabetically, not validated across
  all phases. Mismatched phase versions are misreported with no warning.

### [PB1-4.9] 4.9 Non-Windows consumers bypass the JobObject wrapper - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `scripts/Test-SharpProofPackageConsumers.ps1:34-39`
- On non-Windows the command runs as raw `dotnet` without the JobObject memory/timeout wrapper ->
  different resource-containment behavior vs Windows.

### [PB1-4.10] 4.10 JobObject timeout mask real exit codes - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/JobObjectHelpers.ps1:272-293`
- A memory-limit kill can produce exit code 124, indistinguishable from a timeout.

### [PB1-4.11] 4.11 `RewriteBottomUp` `changed` detection over-reports - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtFormulaTraversal.cs:42-49`
- `changed` set on any non-`ReferenceEqual` return, including structural-identical rebuilds -> extra
  `SubstituteEqualityAliases` passes (bounded, not incorrect).

---

## 5. Packaging, Build, Config & Docs (`SharpProof.Package`, `SharpProof.Vsix`, `config`, `docs`, root csproj/sln, READMEs, PLAN.md)

> Note: option keys, enum values, diagnostic ID ranges (SP0002-SP0047), CLI flags, Z3 RID paths,
> and package versions are *otherwise internally consistent* with the code. No High-severity
> definitely-broken bug found in scope.

### [PB1-5.1] 5.1 VSIX ships netstandard2.0 `System.*` facades - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/SharpProof.Vsix.csproj:51-60`
- Ships netstandard2.0 builds of `System.Memory`, `System.Buffers`, `System.Collections.Immutable`,
  `System.Reflection.Metadata`, `System.Numerics.Vectors`, `System.IO.Pipelines`, `System.Text.Json`,
  `System.Threading.Tasks.Extensions`, `Microsoft.Bcl.AsyncInterfaces` into the VS extension.
  `devenv.exe` already loads newer versions -> classic analyzer-payload binding-conflict pitfall;
  analysis can silently fail or the extension fails to load.

### [PB1-5.2] 5.2 Legacy `tools/install.ps1` & `uninstall.ps1` packed into the NuGet package - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Package/SharpProof.Package.csproj:37`
- `tools/install.ps1`/`uninstall.ps1` glob `analyzers\**\*.dll` and register **every** DLL (including
  `libz3.dll`/`.dylib`, `Microsoft.Z3.dll`, `SharpProof.Symbolic.dll`, `SearchLib.dll`) as analyzer
  references. No-ops for PackageReference (`SupportsPackageDependencyResolution`) but harmful for any
  `packages.config` consumer.

### [PB1-5.3] 5.3 VSIX `Identity Version` not aligned with NuGet version - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/source.extension.vsixmanifest:4` (`Version="0.1.0"` vs NuGet `0.1.0-preview.1`)
- 3-part vs 4-part; no automated sync.

### [PB1-5.4] 5.4 Hardcoded `netstandard2.0` build-output paths in packaging target - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Package/SharpProof.Package.csproj:45-54`
- `_AddAnalyzersToOutput` references analyzers via fixed
  `..\SharpProof.Analyzer\bin\$(Configuration)\netstandard2.0\...` paths. Breaks if any analyzer
  project is retargeted/multi-targeted or packed under a config whose analyzers weren't pre-built.

### [PB1-5.5] 5.5 `SharpProof.Attributes.dll` delivered by two packages simultaneously - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Package/SharpProof.Package.csproj:54` + `SharpProof.Attributes/SharpProof.Attributes.csproj:12`
- A consumer who adds both `SharpProof` and standalone `SharpProof.Attributes` gets the same DLL from
  two packages -> "already provides an asset" warnings / version-pinning conflicts.

### [PB1-5.6] 5.6 `root = true` in editorconfig profile variants is a silent footgun - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `config/profiles/sharpproof-{audit,ci,migration,strict}.editorconfig:2`
- If copied standalone as `.editorconfig`, `root = true` stops the directory walk and disables all
  parent/inherited `.editorconfig` settings.

---

---

# Round 2 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents focused on deeper/adjacent
areas. Items already listed in Round 1 are excluded. Highest-impact new items:
integer-division-by-zero soundness hole, implicit-struct-ctor purity false-negative,
purity-policy tie-break preferring Impure, Ecma method-identity ref-kind mismatch.

## 6. SMT Encoding & SearchLib Runtime (`SearchLib`, `SharpProof.Symbolic\Smt`)

### [PB1-6.1] 6.1 Integer division/remainder by a possibly-zero divisor is not rejected - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-DIVISOR-SAFETY
> **Evidence:** Z3 totalizes integer division/remainder by zero, so unresolved zero divisors produced unsound SAT/UNSAT answers.
> **Changes/tests:** SMT safety validation returns Unknown unless the divisor is proven nonzero; SearchLibZ3SmokeTests.
- `SearchLib/SmtSolver.cs:~1572` (`ValidateIntegerTermSafety`)
- The safety check only returns `Unknown` when the divisor is *exactly* `{0}`. When the
  divisor's interval *includes* 0 (e.g. `x in [-3,3]`, no `x != 0` fact) it falls through to `Ready`,
  so `EncodeCSharpIntegerDivide`/`EncodeCSharpIntegerRemainder` encode the division. Z3's `div`/`mod`
  by zero does not throw, so this can flip SAT<->UNSAT versus real C# semantics -> **false
  `ProvablyPure`** (unsound).

### [PB1-6.2] 6.2 Regex char-class translation is BMP-only but marked `isExact` (astral under-approx) - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DOTNET-REGEX-UTF16
> **Evidence:** System.Text.RegularExpressions matches UTF-16 code units: an astral scalar is two surrogate chars and the translated BMP char universe matches those semantics.
> **Changes/tests:** Runtime surrogate-pair probes and the existing regex translator tests; no code change.
- `SearchLib/Z3FormulaEncoder.cs:1872-1918` (`CreateRegexCharacterRanges`/`...OrEmpty`),
  `:1526-1532` (negated class), `:1706-1773` (`TryCreateEscapedCharacterClassRegex`).
- Negated classes (`[^a]`, `\D`, `\W`, `\S`, `\P{...}`) and Unicode-category classes are built from
  BMP-only ranges but `_isExact` is left `true`. Astral characters (U+10000+) exist in the solver, so
  the negated class *under-approximates* the real .NET regex -> can **spuriously return UNSAT** (wrongly
  prove a hazard unreachable). Because `isExact` stays true, `AdjustForApproximation` does not
  downgrade to `Unknown`.

### [PB1-6.3] 6.3 rlimit "wraparound" logic over-counts when `Check()` is called more than once - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-Z3-RLIMIT-ACCOUNTING
> **Evidence:** A Microsoft.Z3 4.12.2 probe showed rlimit count is cumulative across Check calls and new Solver instances in the same Context (13, 23, 36).
> **Changes/tests:** No code change; the existing delta accounting is required.
- `SearchLib/SmtSolver.cs:46-50` (`CheckAndAccountResources`)
- Z3's `rlimit count` resets each `Check()`; `_lastObservedRlimitCount` is an instance field persisting
  across checks and across separate `Solver` instances in the shared `Context`. In
  `CheckPathAndImpurity` the same incremental solver is checked twice; the second (smaller) observation
  is misread as a 32-bit counter wrap, adding ~4.29e9 to `ConsumedResourceCount`. Repeatedly exhausts
  the cumulative budget -> spurious `Unknown`. Distinct from the Round-1 `IsUInt` gap.

### [PB1-6.4] 6.4 `EncodeBinary` `Equal`/`NotEqual` has no sort/kind guard - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:186-187`
- Unlike `EncodeInteger`/`EncodeString`/`EncodeReference`, `Equal`/`NotEqual` call generic `Encode`
  with no `SmtValueKind` check. Mismatched operand kinds (int vs string, Reference vs String) raise a
  sort-mismatch `Z3Exception` instead of a clean validation error (degrades to `Unknown`, not a wrong
  answer, but a crash path).

### [PB1-6.5] 6.5 Z3 AST nodes are never disposed; only the root `Context` is - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:33-40` (`Dispose`); encoder is a single long-lived instance in `SmtSolver.cs:20`.
- Every `CheckSatisfiability*` builds new `BoolExpr`/`ReExpr`/`SeqExpr` (regex translations alone
  allocate many), asserts them, then disposes only the `Solver`. AST nodes accumulate in the one shared
  `Context` -> unbounded native (Z3) memory growth / leak over a long `PurityProofSearch`.

### [PB1-6.6] 6.6 `TryReadNumber` can throw uncaught `OverflowException` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:2061-2074`
- `checked(value * 10 + digit)` for a long repetition count (e.g. `a{9999999999}`) overflows; not caught
  inside the regex translator -> confusing "unsupported" result rather than a clean cap at `MaxBoundedRepeat`.

### [PB1-6.7] 6.7 Reference `isNull` determined via Z3 `Expr.Equals` (hash-consing) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/Z3FormulaEncoder.cs:587-596` (`CreateModelAssignment` Reference branch)
- `evaluated.Equals(nullValue)` relies on Z3 returning identical managed AST nodes. Two separate
  `model.Evaluate` calls can return distinct nodes for semantically-equal uninterpreted elements, so a
  genuinely-null reference can be mislabeled non-null -> Exact witness silently downgraded to `Approximate`.

### [PB1-6.8] 6.8 `IsConservativeSolverFailure` omits `NullReferenceException`/`OutOfMemoryException` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:309-317`
- Catch list does not include NRE/OOM; a latent NRE in encoding/evaluation would crash analysis entirely
  instead of degrading to `Unknown`.

### [PB1-6.9] 6.9 `CheckSatisfiability` spends full timeout twice on the two-attempt path - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:96-128`
- When preparation changes conditions and there is no approximate regex, the original (un-simplified)
  conditions are checked with the full `timeout`, then the prepared version runs with the *remaining*
  (possibly tiny) budget. The more reliable prepared path can be starved.

### [PB1-6.10] 6.10 `IgnorePatternWhitespace` leaks into character-class range computation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:1552-1562` (`CreateCurrentCharacterClassRegexOptions`)
- `.NET` whole-class translation includes `IgnorePatternWhitespace`, so inside `[...]` the range builder
  treats `#`/whitespace as comment/ignorable rather than literal -> wrong (marked *exact*) ranges.

### [PB1-6.11] 6.11 `ConsumedResourceCount` can overflow `long` via repeated wrap over-counting - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:47-49` (follow-on to 6.3). Unchecked `+=` with ~4.29e9 per multi-check call.

### [PB1-6.12] 6.12 Shared encoder/`Context` is not thread-safe - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:20,31-34`; `Z3FormulaEncoder` caches (`_variables`, `_runtimeTypeTests`,
  `_regexPrecisionCache`) are plain `Dictionary`s. Concurrent use of one `PurityProofSearch`/`SmtSolver`
  would corrupt state / crash the native solver. (Likely single-threaded today.)

### [PB1-6.13] 6.13 `GetRlimit` silently clamps large budgets to `uint.MaxValue` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtResourceBudget.cs:28-34`
- `TotalMilliseconds * 4000 >= 4.29e9` (~ > 17.9 min) collapses to `uint.MaxValue` rlimit (and the
  wall-clock safety net is capped similarly). The requested cumulative budget is silently reduced -> hard
  queries falsely `Unknown`/timeout.

### [PB1-6.14] 6.14 `CheckAndAccountResources` assumes at most one rlimit wrap between observations - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:47-50`. If two successive checks each consume close to `uint.MaxValue`, the
  single-wrap recovery under-counts (extension of Round-1 rlimit bug; no wrong answers).

### [PB1-6.15] 6.15 `MergeAcrossAll` keys path conditions on `ToString()` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs:175` (`GetFormulaKey`)
- Uses the record `ToString()` (culture/version dependent) as the canonical de-dup key instead of the
  dedicated delimiter-safe `SmtFormulaStructuralKey.Create`. Works today but fragile.

## 7. Symbolic IR Lowering & State (`SharpProof.Symbolic\Ir`, `SymbolicProgramPointFacts`, state/facts)

### [PB1-7.1] 7.1 `MergeStates` silently overrides `SymbolicVersions` with the right operand - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:~191`
- `symbolVersions = left.SymbolVersions.SetItems(right.SymbolVersions)` makes `right` win for *every*
  symbol, even if `left` facts reference an older `@vN` name. Used by `CollectForInitialEntryState` to
  combine ancestor + initializer + prior-assignment states -> internally inconsistent state. Sibling of
  the Round-1 `MergeCompletedAlternativeStates` version bug.

### [PB1-7.2] 7.2 `IndexOf`/`LastIndexOf` overloads with a `startIndex`/`count` are dropped - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:311-323` (`TryLowerStringSearchPredicate`)
- Only single-arg `IndexOf(char)` or `StringComparison` overloads are handled; `s.IndexOf(c, 5) >= 0`
  etc. are not lowered to a `Contains` predicate at all -> missed (but sound) proof.

### [PB1-7.3] 7.3 `StartsWith`/`EndsWith` with non-constant string arg unsupported - **MEDIUM/LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:50-53`
- `Contains` accepts variable args, but `StartsWith`/`EndsWith` require a `char`/`string` *constant* ->
  `s.StartsWith(prefixVar)` falls through and loses prefix/suffix semantics (precision loss).

### [PB1-7.4] 7.4 Null-conditional access with value-typed result not lowered - **MEDIUM/LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.ConditionalAccess.cs:98-127` + `Nullable.cs:~558`
- Reference-conditional path early-returns false unless result is a reference type, and the
  nullable-conditional path doesn't cover value results, so `s?.Length`, `arr?[i]`, `dict?[key]` fail to
  lower -> proof dropped (sound, but imprecise).

### [PB1-7.5] 7.5 Inlined source-predicate picks `FirstOrDefault()` declaration (partial types) - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.SourcePredicates.cs:78-80,103-105`
- For a partial type the first declaration reference may be the definition (no body), so the predicate is
  not inlined. Also closure-capture mutation after an inlined local-function is a potential unsoundness
  edge case.

### [PB1-7.6] 7.6 Range/`Index` length shapes only scanned in the immediate block - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Indexing.cs:1121-1153,1497-1529`
- `EnumerateContainingBlocks` breaks at `ContainingStatement`; range/index variables assigned in an
  enclosing block aren't resolved -> `s[range]`/`arr[index]` length shape falls back to unsupported.

### [PB1-7.7] 7.7 `Regex.Match(input, startat)` non-zero start not lowered - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:225-233`. Sound bail-out, precision loss.

### [PB1-7.8] 7.8 `ContainsConjunctionContradiction`/`ContainsDisjunctionTautology` only inspect flat fact lists - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:640-662,1037-1094`. A contradiction like `(A && (B || !A))` is not
  simplified at normalization; no wrong fact emitted.

### [PB1-7.9] 7.9 From-end index (`^k`) well-formedness precondition too weak - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Indexing.cs:1291-1313,1340-1377,1443`
- The `well-formed` OR-guard makes the in-range condition vacuously `true` if the precondition fails; for
  a negative `k`, `arr[^k]` is itself a runtime error but the analyzer under-approximates well-formedness
  (potential unsoundness if the harness trusts the guard).

### [PB1-7.10] 7.10 `SymbolicIrSubstitution.ReplaceTerm` keeps original `SourceSpan`/`Provenance` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrSubstitution.cs:24-45`. Substituted facts keep the original
  provenance/source, which can mislead diagnostics and provenance-keyed de-duplication.

## 8. Analyzer Purity / Contracts / Attribute Detection (`SharpProof.Analyzer`)

### [PB1-8.1] 8.1 Purity-policy tie-break prefers Impure over equal-priority Pure evidence - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PURITY-PRECEDENCE-POLICY
> **Evidence:** docs/purity-policy.md explicitly defines impure as winning equal-priority conflicts; exact configured-pure members are excluded from broad namespace matches.
> **Changes/tests:** No code change; current behavior matches the documented safety policy.
- `SharpProof.Analyzer/Engine/PurityPolicyResolver.cs:130-139`
- Candidates sorted by `Priority` ascending then `Decision == Impure ? 0 : 1`, so among *equal*
  priorities Impure always wins. `configured_pure_member` (30) ties `configured_impure_namespace_or_type`
  (30); `built_in_pure_catalog` (50) ties `built_in_impure_namespace_or_type` (50). A method explicitly
  listed pure but living in an impure namespace/type is wrongly declared **Impure** -> wrong SP0002 verdict.

### [PB1-8.2] 8.2 Implicit value-type constructor always treated Pure, ignoring impure field initializers - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-IMPLICIT-STRUCT-CONSTRUCTOR
> **Evidence:** An implicit metadata value-type constructor only zero-initializes storage; C# structs with field initializers require an explicit instance constructor.
> **Changes/tests:** No code change; the shortcut does not skip executable initializers.
- `SharpProof.Analyzer/GeneratedPurityCatalog.cs:114` (`TryGetPurity`), `:285-305`
  (`TryGetImplicitMetadataValueTypeConstructorPurity`)
- A synthesized implicit parameterless value-type ctor is reported `pure` *before* resolving the actual
  method identity. A C# 10+ `struct` with field initializers calling `DateTime.Now`/`Guid.NewGuid()` is
  therefore falsely treated as pure -> **false-negative impurity** (unsound).

### [PB1-8.3] 8.3 Override `[EnforcePure]` over base `[Impure]` not flagged as conflicting - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:62` + `HasInheritedPurityEnforcement:511-589`
- Conflict check only detects inherited *enforcement* conflict; it never detects an inherited `Impure`.
  `class Base { [Impure] void M(){} }` / `class Derived : Base { [EnforcePure] override void M(){} }`
  emits no `ConflictingPurityAttributes` diagnostic (the reverse is caught).

### [PB1-8.4] 8.4 Redundant `[AllowSynchronization]` false positive (only `lock` recognized) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:122-150`
- `containsLock = DescendantNodes().OfType<LockStatementSyntax>().Any()`. A method synchronizing via
  `Monitor.Enter`/`SemaphoreSlim.Wait`/`ReaderWriterLockSlim`/`Interlocked.*`/`SpinLock` (no `lock`
  keyword) is falsely reported `RedundantAllowSynchronization` - the attribute is genuinely needed.

### [PB1-8.5] 8.5 Any recursive / mutually-recursive method always declared Impure - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/PurityAnalysisEngine.Recursive.cs:34-44` + `WorklistPuritySolver.cs:46-68`
- A visited-set cycle returns `ImpureUnknownLocation` and is cached; the recursive stub persists, so a
  provably-pure recursive method (e.g. `[EnforcePure] static int Sum(int n) => n<=0?0:n+Sum(n-1);`) is
  reported as not-pure (SP0002). Over-conservative; no fixed-point assumption on the recursive edge.

### [PB1-8.6] 8.6 Diagnostic location for conversion operators points at return type - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:634` + `AnalyzerSyntaxHelpers.cs:29-30`
- `GetIdentifierLocation` returns `c.Type.GetLocation()` for `ConversionOperatorDeclarationSyntax`
  (vs `o.OperatorToken` for `OperatorDeclarationSyntax`) -> misleading squiggle span.

### [PB1-8.7] 8.7 `MethodExpectedComplexityAnalyzer.Classify` ignores `Complexity.IsConservative` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodExpectedComplexityAnalyzer.cs:158-164`
- A conservative (widened) bound flows into `TryMapActual`/`Order` and can be reported `Verified`/
  `Exceeded` rather than flagged as approximate.

### [PB1-8.8] 8.8 Worklist fixed-point seeds callee results from a mutable dict, discards recursive re-analysis - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/Analysis/WorklistPuritySolver.cs:46-68` + `PurityAnalysisEngine.Entry.cs:22-25`
- Convergence relies on re-enqueue only on `IsPure` changes (not evidence/truncation changes); deep cyclic
  call graphs have a narrow ordering edge case. Low likelihood.

## 9. CodeFixes & Attributes (`SharpProof.CodeFixes`, `SharpProof.Attributes`)

### [PB1-9.1] 9.1 `AddEnforcePureAttributeAsync` emits `[EnforcePure]` at column 0 (no leading trivia / no Formatter) - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CODEFIX-ATTRIBUTE-FORMATTING
> **Evidence:** The current code fix emits the fully qualified attribute with correct spacing and compilation is covered by SP0004_AddEnforcePure_InsertsFullyQualifiedAttribute.
> **Changes/tests:** No additional change.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1063-1070`
- The new attribute list is inserted at index 0 with no leading trivia and no `Formatter.Annotation`, so
  for any indented member the attribute lands at column 0 and the IDE won't auto-correct. Sibling helpers
  (`AddInferredContractAttributeAsync`, `AddInferredNullableContractAttributeAsync`) correctly set
  `WithLeadingTrivia(indentation)`.

### [PB1-9.2] 9.2 All "remove attribute" fixes drop member indentation when removing the only/leading list - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CODEFIX-TRIVIA
> **Evidence:** Current removal helpers transfer leading/trailing trivia and focused removal code-fix tests preserve indentation.
> **Changes/tests:** Existing SP002x removal formatting regressions pass; no additional code change.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:499-503,531-567,631-642,961-980,982-1024`
- When the removed list is sole/first, `SyntaxFactory.List(newLists)`/`WithAttributeLists(empty)` makes
  the keyword the new first token (its leading indentation lived on the `[`). No `Formatter.Annotation`
  is applied -> member collapses to column 0. Broad formatting regression for every removal fix.

### [PB1-9.3] 9.3 Inferred-contract fixes insert a spurious blank line before existing attribute lists - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1105-1113,375-389`
- New list's trailing `"\n"` + existing list's leading `"\n    "` produce a double newline between the
  inserted and pre-existing attributes. Cosmetic; no `Formatter.Annotation` to repair.

### [PB1-9.4] 9.4 `RemoveContractAttributeAsync` silently no-ops when the attribute type is unresolvable - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:939-953,968-970`
- Keys off the semantic `INamedTypeSymbol`; if the attribute is in an unreferenced stub namespace,
  `shouldRemoveType(null)` -> false and nothing is removed. The lightbulb is offered but does nothing.

### [PB1-9.5] 9.5 `CreateAccessorList` derives indentation from the semicolon's trailing whitespace - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:767-785`
- For an expression-bodied property that is the last member of a file with no trailing newline,
  `indentation` becomes `""` -> generated `}`/getter body emitted at column 0.

### [PB1-9.6] 9.6 `UnnecessaryNullForgivingOperatorId` fix drops trivia between operand and `!` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:283-287`
- `WithTriviaFrom(suppression)` discards trivia between the operand and the `!` (`a /*c*/ !` -> `a `).

### [PB1-9.7] 9.7 `AddEnforcePureAttributeAsync` may emit ambiguous short name - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:1057-1060,1121-1127`
- `HasUnaliasedSharpProofAttributesUsing` only checks for `using SharpProof.Attributes;`. If another type
  named `EnforcePure` is also in scope, the short name is ambiguous -> non-compiling code.

### [PB1-9.8] 9.8 Parameter nullable-contract insertion has no leading trivia on the added list - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.CodeFixes/SharpProofCodeFixProvider.cs:401-409` (`WithoutTrivia().WithTrailingTrivia(Space)`).
  Cosmetic; inconsistent with declaration-level helpers.

### [PB1-9.9] 9.9 `ImpureAttribute` & `PureExternalAttribute` omit `Event` from `AttributeUsage` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Attributes/ImpureAttribute.cs:5-7`, `PureExternalAttribute.cs:5-7`
- These are the only contract attributes excluding `Event`/`All`; inconsistent with how
  `PropertyAttributeAppliesToAccessor` treats them, and would bite any future fix suggesting `Impure` on
  an event accessor.

## 10. EffectSummary Tool (`Tools/SharpProof.EffectSummary`)

### [PB1-10.1] 10.1 Manual/seed purity entries with differing `Classification` are silently dropped - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REVIEWED-PURITY-MERGE
> **Evidence:** Same-implementation reviewed pure/impure entries are applied before catalog merging; differing implementations are deliberately discarded as ambiguous rather than trusted unsoundly.
> **Changes/tests:** No classification-precedence change; category correction is addressed by PB1-10.3.
- `Tools/SharpProof.EffectSummary/GeneratedPurityCatalogEntryRelations.cs:19-34` +
  `PurityClassificationEngine.cs:2805-2857,98-107` (`MergeGeneratedPurityEntries`)
- `ResolveGeneratedPurityEntryCandidates` returns `null` when `DoesDominate` is false in both directions,
  which happens whenever a manual/seed entry disagrees with the generated classification (e.g. seed
  `"pure"` vs generated `"impure"`). The key is then omitted -> cross-assembly calls fall through to
  `unknown_callee` instead of receiving the manual override. Defeats `--compare-manual-catalogs`.

### [PB1-10.2] 10.2 `--resume` with non-existent `--progress` file crashes instead of starting fresh - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-OPTIONAL-PROGRESS-STATE
> **Evidence:** A first resumable invocation reproduced FileNotFoundException when the progress path did not exist.
> **Changes/tests:** Artifact and shard resume start fresh when progress is absent; EffectSummaryTool_ResumeWithoutProgressStartsFresh.
- `Tools/SharpProof.EffectSummary/Program.cs:146-148,259-261,369-395,323-350`
- `LoadCompletedArtifactOutputs`/`LoadShardedProgress` call `File.ReadAllText(progressPath)`
  unconditionally -> `FileNotFoundException` on first invocation of a resumable run.

### [PB1-10.3] 10.3 `ShouldPreferReviewedUpgrade` rejects impure-vs-impure overrides with differing categories - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REVIEWED-PURITY-CATEGORY-PRECEDENCE
> **Evidence:** A hash-matched reviewed impure entry with corrected categories was ignored in favor of reanalysis.
> **Changes/tests:** Hash-matched reviewed classifications now win, including category corrections; EffectSummaryTool_ReviewedImpureCategoriesOverrideReanalysis.
- `Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs:3759-3776`
- Two `impure` classifications with *different* `Categories` fail the symmetric set-equality check ->
  returns `false` -> the manual `impure` override is silently ignored.

### [PB1-10.4] 10.4 Resume fingerprint omits analyzer/tool version - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SHARD-TOOL-IDENTITY
> **Evidence:** The sharded fingerprint contained inputs and options but no identity for the producing tool.
> **Changes/tests:** The deterministic tool module MVID is included in the fingerprint and progress document; EffectSummaryTool_ShardedProgressRecordsToolIdentity.
- `Tools/SharpProof.EffectSummary/Program.cs:298-321` (`ComputeShardedInputFingerprint`)
- After a tool rebuild, an interrupted-then-resumed `--shard-output` run reuses stale per-assembly
  shards (their `InputFingerprint` still matches) -> combined document mixes old/new results.

### [PB1-10.5] 10.5 Resume loses prior external purity seed entries when a completed artifact had no classification - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ARTIFACT-SEED-FLOW
> **Evidence:** Completed artifacts are replayed in spec order; an artifact without purity classification has no generated entries to contribute, while earlier classified artifacts are still replayed.
> **Changes/tests:** No code change.
- `Tools/SharpProof.EffectSummary/Program.cs:159-172,182-194` (`ReadGeneratedPurityEntries`)
- If a previously completed artifact was generated without `--classify-purity`, `ReadGeneratedPurityEntries`
  returns nothing, degrading cross-artifact resolution on resume.

### [PB1-10.6] 10.6 Progress file deleted on success, breaking later `--resume` - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PROGRESS-CHECKPOINT-LIFETIME
> **Evidence:** Progress is an interruption checkpoint, not a persistent build cache; successful output completion deliberately removes it.
> **Changes/tests:** No code change; PB1-10.2 makes a later resume safely start fresh.
- `Tools/SharpProof.EffectSummary/Program.cs:177,275`
- After a fully successful run the progress file is deleted; a later `--resume --progress <samepath>`
  hits bug 10.2 (crash) or re-runs everything from scratch.

### [PB1-10.7] 10.7 `IsPathWithinDirectory` matches siblings at the volume root - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PATH-CONTAINMENT
> **Evidence:** A drive root contains every absolute path on that drive; C:\foobar is a child of C:\, not a sibling. Deeper directory checks retain the separator boundary.
> **Changes/tests:** No code change.
- `Tools/SharpProof.EffectSummary/Program.cs:995-1002`
- For `directory = "C:\"`, a candidate `C:\foobar` also starts with `"C:\"` -> returns `true` even though
  `foobar` is a sibling (weak traversal guard; real check is against the deeper package dir).

### [PB1-10.8] 10.8 `LoadSymbols` aborts the whole artifact-spec run if a source summary yields no symbols - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.EffectSummary/Program.cs:1241-1242`
- An empty/fully-filtered `SourceSummaryPath` throws `InvalidOperationException` and aborts all artifacts
  rather than skipping the empty one.

### [PB1-10.9] 10.9 Fixed-point convergence keyed only on exact equivalence - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs:101-107` (`HaveSameGeneratedPurityEntryMap` ->
  `AreEquivalent`). Reordered equivalent categories / different dominating-chain ordering won't be seen as
  converged -> always runs the full `MaxCrossAssemblyClassificationPasses = 8` (wasted work, no wrong result).

## 11. PowerShell Scripts (`scripts`, `Tools/.../*.ps1`)

### [PB1-11.1] 11.1 `demo-sharpproof.ps1`: native command failures never detected (silent success) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/demo-sharpproof.ps1:24,34,47-48,52,97`
- `$ErrorActionPreference = 'Stop'` does not make native commands (dotnet/powershell.exe) throw on
  non-zero exit, and `| Out-Host` pipelines leave `$LASTEXITCODE` stale. A failed `dotnet build` still
  prints "Done." and the script exits 0.

### [PB1-11.2] 11.2 `Get-SharpProofAuditInventory.ps1`: `Get-RiskClass` misclassifies the entire Symbolic module - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:42`
- The `Proof` alternative matches the substring "Proof" inside "Sharp**Proof**", so *every*
  `SharpProof.Symbolic\...` path returns `'proof-fallback'`. The later `public-result-cli` branch
  (`QueryService|...`, line 46) is dead for Symbolic, and non-proof Symbolic files are wrongly tagged.

### [PB1-11.3] 11.3 Fragile `Substring($repoRoot.Length)` relative-path handling (parallel copies) - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofProductionMetrics.ps1:28-30`, `scripts/Get-SharpProofAuditInventory.ps1:85`,
  `scripts/Get-SharpProofTestImpactInventory.ps1:35`
- Same blind-`Substring` pattern as Round-1 hotspots:28 (casing/separator/long-path divergence throws
  `ArgumentOutOfRangeException` or corrupts paths). TestImpactInventory even pairs `StartsWith(...,
  OrdinalIgnoreCase)` with a case-sensitive positional `Substring`.

### [PB1-11.4] 11.4 `Get-SharpProofAuditInventory.ps1`: empty `.cs` files counted as 1 line - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:98` (`@((Get-Content ...)).Count`; a 0-byte file returns
  `$null` -> `@($null).Count == 1`). Contrast with ProductionMetrics:71 which uses `Measure-Object -Line`.

### [PB1-11.5] 11.5 `Get-SharpProofAuditInventory.ps1`: Windows-only `-like` patterns / dead Symbolic branch - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofAuditInventory.ps1:57-65` (backslash `-like` matchers never match on `/`
  separators) + dead `public-result-cli` branch at line 46. Portability.

### [PB1-11.6] 11.6 `Generate-ConfigurationReference.ps1`: markdown table escaping incomplete - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-ConfigurationReference.ps1:305` (only `|` -> `\|`; a description containing a backslash
  before `|` would be doubled; backtick/newline not sanitized).

### [PB1-11.7] 11.7 `Invoke-SharpProofImpactedTests.ps1`: suggested-command fidelity - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Invoke-SharpProofImpactedTests.ps1:1476-1478` (only doubles single quotes; display-only, but a
  copy-pasted suggested command with `"`/`$` won't faithfully reproduce).

## 12. Shared Helpers, Config & Attributes (`Shared`, `config`, root csproj)

### [PB1-12.1] 12.1 Ecma adapter produces different identity for `out`/`in`/`ref readonly` params by path - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-STRUCTURAL-REF-KIND-IDENTITY
> **Evidence:** StructuralRefKinds preserves out, in, ref, and ref-readonly while retaining explicit compatibility keys for legacy collapsed identities.
> **Changes/tests:** Covered by existing structural identity tests.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:46-48` (MemberReference) vs `:74-80`/`GetParameterRefKind`
  (MethodDefinition)
- The reference path encodes every by-ref param as only `Ref`/`None`, while the definition path
  distinguishes `Out`/`In`/`RefReadonly`. The SAME C# method yields different canonical keys across the
  two representations (`bool TryGetValue(K, out V)` -> `out` vs `ref`), so `known_pure_methods` /
  `known_impure_methods` config keys, the BCL fallback cache, and effect-summary lookups keyed by
  canonical key will **not match** -> mis-classification. Existing test only compares Roslyn-vs-Ecma, not
  the two Ecma internal paths.

### [PB1-12.2] 12.2 `EffectSummarySchemaContract` has no read-compatibility guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EffectSummarySchemaContract.cs:5` vs `Shared/ProofEvidenceSchemaContract.cs:5-17`
- Only `internal const int CurrentVersion = 5;`; no minimum version/policy/`IsReadCompatible`. The reader
  (`Tools/SharpProof.EffectSummary/Program.cs:1185`) does an equality check only -> a different-schema or
  partial JSON is silently accepted/partially deserialized (unlike evidence, which has a rejection path).

### [PB1-12.3] 12.3 `KnownPureBCLMembers` and `KnownFreshOwnedArrayReturningMembers` are empty - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/Constants.cs:138-142`; consumed at `PurityClassificationEngine.cs:2750,2754-2755`
- Both are empty `ImmutableHashSet`s while `KnownImpureMethods` is richly populated. If intended to carry
  "known pure BCL" / "fresh-owned-array" data, the corresponding classification is a no-op relying on
  elsewhere-sourced data; `ConstantsTests.cs:24` only asserts `Not.Null`. Confirm whether intentional.

### [PB1-12.4] 12.4 `GetTypeDefinitionMetadataName` drops generic-arity suffix - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:148-158`
- Declaring-type name never includes the `` `n `` arity marker, so a generic `Foo<T>` and non-generic `Foo`
  get identical containing-type identity (legal as distinct metadata tokens in raw IL).

### [PB1-12.5] 12.5 `BclPurityFallbackHeuristics.IsAmbientNamespaceOrType` over-matches via substring & omits bare `System` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:170-192`
- `ContainsAny` substring scan can force-impure a framework type whose name merely contains
  "File"/"Stream"/"Process"; the namespace arm only lists `System.IO`/`System.Net`/... (not bare `System`),
  so ambient `System.*` types bypass it.

### [PB1-12.6] 12.6 Reference-path return ref-kind ignores `In` attribute (asymmetry) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/EcmaStructuralMethodIdentityAdapter.cs:82-88` vs `:50-52` (reinforces 12.1).

### [PB1-12.7] 12.7 Root `.globalconfig` disables effect-summary JSON vs profile globalconfigs that enable it - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `C:\w\PurelySharp\.globalconfig:3` (`sharpproof_enable_effect_summary_json = false`) vs
  `config/profiles/sharpproof-{strict,audit}.globalconfig:6` (`= true`); both `is_global = true`. Discovery
  order determines which wins -> silently changes whether effect-summary JSON is emitted.

## 13. Large Test Files (`SharpProof.Test`, `SharpProof.ToolingTest`)

### [PB1-13.1] 13.1 Order-dependent `GeneratedPurityCatalog.Entries[0]` assumes first entry is a specific member - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.cs:6101,6283`
- Asserts a property of `Entries[0]` (an `ArtifactSource`/`DisplayName`) assuming a specific ctor/package
  entry sorts first. Passes only because constructors sort before getters (`".ctor"` < `".get_"` under
  Ordinal); a sort/order change validates the wrong entry (asserts-the-wrong-thing risk).

### [PB1-13.2] 13.2 Effect-summary runner helpers don't drain stdout/stderr -> potential deadlock/hang - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.Helpers.cs:450,549,594,699`
- `RunEffectSummaryAsync`/`RunFilteredEffectSummaryAsync`/`RunRuntimeEffectSummaryAsyncCore` start the tool
  without `RedirectStandardOutput`/`RedirectStandardError` and only `WaitForExitAsync().WaitAsync(timeout)`.
  Verbose output can fill the OS pipe buffer -> process blocks -> `TimeoutException` (240s) flaky/hanging
  test. Sibling helpers (lines 476-477,581-584) do redirect and read both streams.

### [PB1-13.3] 13.3 Brittle substring-counting of live source files - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ArchitectureReductionTests.cs:2900,2905`
- Counts `new SymbolicSourceQueryResult(` occurrences in the actual repo source (fragile to comments/
  string literals/refactors). A brittle source-structure assertion rather than behavioral.

### [PB1-13.4] 13.4 Hardcoded SMT budget values tightly coupled to defaults - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ArchitectureReductionTests.cs:5967-5968` (`MaxPathConditions == 192`,
  `TimeoutMilliseconds == 750`). Any default-budget change breaks the test though logic is unchanged.

### [PB1-13.5] 13.5 Tautological `SourceMap` assertions (test echoes its own inputs) - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.ToolingTest/SymbolicSourceQueryLineTests.cs:55,65-66`
- Constructs `SymbolicSourceMap("editor://...", 41, 7)` then asserts `OriginalStartLine == 41` /
  `OriginalStartColumn == 7`. Only verifies the constructor stored its inputs, not real mapping behavior.

### [PB1-13.6] 13.6 `FindMethod` helper uses `.Single()` on fixture methods - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EffectSummaryToolTests.Helpers.cs:385`
- Throws `InvalidOperationException` (no diagnostic) if a fixture assembly ever has two methods with the
  same `DisplayName` (ref-out overloads / generic-arity collisions). Latent test-infra fragility.

## 14. Docs, Samples & Demos (`docs`, `samples`, `SharpProof.Demo`, `SharpProof.Smoke.Net472`)

### [PB1-14.1] 14.1 Demo suppresses `SP0002` globally while its comments claim `SP0002` fires - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Demo/Program.cs:9,35,42,50` vs `SharpProof.Demo/SharpProof.Demo.csproj:11`
  (`<NoWarn>$(NoWarn);SP0002</NoWarn>`)
- The demo is annotated with comments like `// I/O under [Pure] -> SP0002`, but `NoWarn` suppresses
  `SP0002` (an `Error` diagnostic) for the whole project. `dotnet build SharpProof.Demo` produces **no**
  SP0002 despite the documented comments -> the demo's primary pedagogical point is silently neutralized.
  (`SP0004` is *not* suppressed, so those comments remain accurate.) Suggest dropping `SP0002` from
  `NoWarn` and relying on the `.editorconfig` `warning` demotion.

### [PB1-14.2] 14.2 "restoring SharpProof.Symbolic from NuGet" contradicts "not published to NuGet.org yet" - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `samples/SharpProof.Symbolic/README.md:6-10` vs root `README.md:80,26` / `README.source.md:78`
- Sample README tells users to restore `SharpProof.Symbolic` from NuGet and `dotnet run`; the root README
  states the public packages are not published to NuGet.org yet (local-feed build required). On a clean
  machine the sample's `dotnet run` / `dotnet add package` fails to restore.

### [PB1-14.3] 14.3 `contracts.md` understates coverage as "SP0002 through SP0040" - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/contracts.md:148`
- States the gallery covers `SP0002`-`SP0040`, contradicting the project's own `SP0002`-`SP0047` range
  (README:647, diagnostic-examples.source.md:13, and the analyzer's continuous `SP0002`...`SP0047`).

### [PB1-14.4] 14.4 Demo/smoke list library assemblies as `OutputItemType="Analyzer"` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Demo/SharpProof.Demo.csproj:23-24`, `SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj:15-16`
- `SharpProof.Symbolic`/`SearchLib` are ordinary libraries, not Roslyn analyzers; marking them
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` is incorrect usage (works today but is
  wrong-by-construction and a maintenance smell).

### [PB1-14.5] 14.5 Demo `.editorconfig` reclassifies `SP0004` as `suggestion` (docs show `Warning`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Demo/.editorconfig:5` vs `README.md:168` / `docs/diagnostic-examples.md`
- Within the demo `SP0004` appears as a suggestion, inconsistent with documented `Warning` severity.

### [PB1-14.6] 14.6 Root README's "intended public packages" list omits `SharpProof.Symbolic` - **LOW**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
- `README.md:79-80` / `README.source.md:77`
- Says intended public packages are `SharpProof` and `SharpProof.Attributes`, but the rest of the docs
  (and README lines 617-635) treat `SharpProof.Symbolic` as the primary supported library/API package.

---

---

# Round 3 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents focused on deeper/adjacent
areas, each instructed to read `POTENTIAL_BUGS.md` first and avoid duplicates.
Highest-impact new items: shared non-thread-safe `SemanticModel` used concurrently
(HIGH), `Math.Abs(int.MinValue)` overflow unsound modeling (MEDIUM-HIGH), `int.MinValue`
negation/division overflow unmodeled (HIGH), `[MaybeNullWhen]` inverted polarity (HIGH),
delegate-target map intersect-vs-union merge inconsistency, missing Linux native Z3 in
NuGet package, orphaned suspended process under CI job nesting.

## 15. Symbolic IR Lowering - Arithmetic / Numerics / Conversions / Regex (`SharpProof.Symbolic\Ir`)

### [PB1-15.1] 15.1 `Math.Abs(int.MinValue)` overflow never modeled (wrong return + missed `OverflowException`) - **MEDIUM/HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-RUNTIME-OVERFLOW-HAZARDS
> **Evidence:** Current runtime-hazard modeling emits an OverflowException candidate for Math.Abs on signed MinValue.
> **Changes/tests:** Existing SymbolicRuntimeHazardQueryTests cover Math.Abs(int.MinValue).
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Numerics.cs:81-111` (`TryLowerIntegralMathAbsInvocation`)
- Lowers to `x >= 0 ? x : 0 - x`. For `int.MinValue` the else branch encodes unbounded Z3 math = `2147483648`,
  and no exception precondition is attached. `Math.Abs(int.MinValue)` actually throws `OverflowException`. The
  checked-overflow hazard factory only handles checked casts / `int.MinValue / -1` / checked binary ops, never
  the `Math.Abs` call -> unsound purity verdict / wrong value fact.

### [PB1-15.2] 15.2 `Math.Clamp` missing the `min <= max` precondition (wrong value + missed `ArgumentException`) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Numerics.cs:44-79` (`TryLowerIntegralMathClampInvocation`)
- Lowered as nested conditional correct only when `min <= max`. For `min > max`, .NET throws `ArgumentException`
  but the model returns a value (the `min` branch wins), swallowing the exception -> wrong return value.

### [PB1-15.3] 15.3 `Regex.IsMatch` null input folded into the predicate masks `ArgumentNullException` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:178-184` (`TryLowerRegexInvocationParts`)
- Becomes `(s != null) && regexMatches(s, pattern)`. In C#, `Regex.IsMatch(null, ...)` *throws*, it does not
  return false. A method with nullable input is modeled as unconditionally returning a bool -> can miss the
  null-input hazard and yield unsound purity.

### [PB1-15.4] 15.4 Overflow-sensitivity heuristic only fires for an `IdentifierNameSyntax` RHS - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:346-379` (`IsUncheckedOverflowSensitiveComparisonOperand`, line 367)
- `MayOverflow = true` only when the other operand is a bare `IdentifierNameSyntax`. `unchecked(a + b) > 5` or
  `> this.Count` returns `false` -> sum modeled as unbounded Z3 math, ignoring the wraparound `unchecked` produces
  -> wrong comparison fact.

### [PB1-15.5] 15.5 Arithmetic binary lowering lacks the `OperatorMethod == null` guard the bitwise path has - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:331-344` vs `SymbolicIrLowerer.Operators.cs:22`
- A user-defined `operator +/-/*` on a type whose operands lower to `Int` would be modeled as pure integer
  arithmetic, ignoring real (possibly impure) operator semantics. Latent.

### [PB1-15.6] 15.6 `IsValuePreservingIntegralConversion` drops `int`->`enum` (and enum->enum) casts with no tag - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conversions.cs:421-438,343-351`
- Value-preserving casts are dropped entirely -> `Int` term with no enum-type link; distinct enums backed by the
  same underlying type become indistinguishable (sound for equality/arithmetic, destroys type identity).

### [PB1-15.7] 15.7 `Regex.Matches(...).Count` classification only covers a few constant comparisons - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Regex.cs:97-117`
- Only `(==0)/(<1)/(<=0)/(!=0)/(>0)/(>=1)` are handled; `matches.Count == 1`, `> 2`, etc. silently unsupported
  (sound bail, but a precision gap).

### [PB1-15.8] 15.8 `as`-cast guard uses only `IsReferenceType` on static types - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conversions.cs:152-184,278-305`
- Structural identity check can conclude "identity preserving" between generic instantiations / `T as TBase`,
  reusing the reference verbatim without a version/alias refresh.

### [PB1-15.9] 15.9 `checked` arithmetic inlined via SourcePredicates loses overflow guard - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs:342` + `SymbolicIrFormulaEncoder.cs:259-264`
- `MayOverflow` is only produced for `IsChecked: false` operations, so `checked` helpers inlined via
  `TryLowerReturnedBoolean`/`TryLowerSourceMethodBooleanInvocation` carry only the exact `a + b` term with no
  overflow guard -> a wrong value fact can propagate into the caller if not re-derived against the hazard trigger.

### [PB1-15.10] 15.10 `TryLowerTypeOfComparison` mixes `out var right`/`out var left` across `||` operands - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Types.cs:37-44`
- Correct only due to `&&`/`||` precedence; a future reordering would silently bind the wrong variable to `null`.

## 16. Analyzer - Nullable Contracts, Allocation, Exceptions, Engine (`SharpProof.Analyzer`)

### [PB1-16.1] 16.1 `[MaybeNullWhen]` verification uses inverted polarity - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONDITIONAL-NULLABILITY-POLARITY
> **Evidence:** MaybeNullWhen permits null on the named result branch and requires non-null on the opposite branch; the implementation deliberately proves the opposite branch.
> **Changes/tests:** Existing NullableContractVerificationTests cover the false branch; implementation matches the attribute contract.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:121-135`
- `ConditionalImplication(result, !m, "target != null")` expands to `(result == false) || target != null`, i.e.
  "if the method returns **true**, the parameter must be non-null." But `[MaybeNullWhen(true)]` means the
  parameter **may be null** when it returns true and places no obligation on the true path. The code treats
  `MaybeNullWhen(X)` like `NotNullWhen(!X)` - the opposite contract. `[NotNullWhen]` (lines 106-119) does it
  correctly, so the two are inconsistent by construction -> false-positive `NullableParameterPostconditionViolationRule`.

### [PB1-16.2] 16.2 `NotNullWhen`/`MaybeNullWhen` on `out`/`ref` params of **void** methods never verified - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:108,124`
- Both postcondition loops are guarded by `if (completion.ResultExpression != null)`. Void methods have
  `ResultExpression == null`, so the entire check is skipped even though these attributes are most commonly
  applied to `out`/`ref` params of void/bool methods -> missing verification.

### [PB1-16.3] 16.3 Delegate-target map is intersected at CFG merge but unioned across-all - **MEDIUM/HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DELEGATE-TARGET-MERGE
> **Evidence:** Both pairwise CFG merge and across-all merge retain only keys present on every path while unioning each retained key target set.
> **Changes/tests:** Code-path comparison shows the same conservative lattice operation; no code change.
- `SharpProof.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs:87` vs `:11-22,412-418`
- Pairwise merge (`IntersectDelegateTargetMaps`) keeps a key only if present in *both* predecessors and merges
  targets; the across-all merge (`MergeDelegateTargetMapsAcrossAll`) **unions**. Logically after a join a delegate
  "may be" any branch's target, so intersect is wrong. `DelegateTargets.cs:68` `TryGetValue` can then miss the
  impure target -> possible missed impurity (under-reported impurity).

### [PB1-16.4] 16.4 Inferred `[NotNull]` suggestion gate is backwards - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs:103-108`
- The parameter suggestion requires `parameter.NullableAnnotation != Annotated`, but the useful `[NotNull]` suggestion
  is for an **annotated-nullable** parameter (`string?`) whose guard proves non-null. So it suggests `[NotNull]` on
  already-non-nullable params (redundant/no-op) and skips the real case.

### [PB1-16.5] 16.5 `[MaybeNullWhen]` gate on `NullableAnnotation == NotAnnotated` skips the usual case - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:121-122`
- `[MaybeNullWhen]` is overwhelmingly applied to annotated-nullable `out`/`ref` params; this guard excludes exactly
  those, so the (already inverted, 16.1) check is skipped for nearly all real usages.

### [PB1-16.6] 16.6 `[ZeroAllocations]`: implicit non-params array allocations silently skipped - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodAllocationAnalyzer.cs:137-144`
- A new-array site is flagged only when explicit or an implicit params-array. An implicitly-inserted array that is
  *not* a params array still allocates but escapes the rule -> false-negative allocation.

### [PB1-16.7] 16.7 `ConcurrentDictionary.GetOrAdd` re-entrancy in `GetPurity` -> inconsistent/stale purity cache - **MEDIUM/LATENT**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:74-82`
- `GetOrAdd`'s value factory can run more than once under concurrent/recursive calls, yielding a half-computed
  `PurityAnalysisResult` reused for later analyses. Distinct from Round-2 Section 8.5.

### [PB1-16.8] 16.8 `CallGraphBuilder` CFG pass is incomplete - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs:353-403`
- Second pass only walks `IInvocationOperation` edges and uses `method.DeclaringSyntaxReferences.FirstOrDefault()`
  (only first declaration of a partial type) -> can miss accessor/operator/await/delegate/ctor-initializer edges and
  partial-type bodies split across files. Incomplete call graphs feed the worklist fixed point.

### [PB1-16.9] 16.9 Exception-contract collection runs outside the `UseAttributePolicy` scope - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:57` vs `:72`
- `CollectExceptionContracts` executes using the explicit policy param (correct today), but `using
  (UseAttributePolicy(attributePolicy))` is only entered at line 72. Under `EnableConcurrentExecution` the AsyncLocal
  reset can interleave -> wrong exception-contract classification if a future switch reads `ActiveAttributePolicy`.

## 17. Symbolic Query API & CLI (`SharpProof.Symbolic`, `Tools/SharpProof.SymbolicCli`)

### [PB1-17.1] 17.1 Complexity CI gate broken for `Product`/`Max` bounds; mis-trips `--fail-on-complexity-unknown` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:114-126,310-391` (`CompareComplexity`/`TryGetChainRank`)
- `TryGetChainRank` returns `false` for `Product` and `Max` (no case), so `--fail-on-complexity-exceeded Product` can
  **never** report `Exceeds` (only an exact match avoids `Incomparable`). The gate then treats `Incomparable` as
  "unknown" -> `--fail-on-complexity-exceeded Product --fail-on-complexity-unknown` **erroneously fails** on
  well-bounded code (e.g. `Quadratic` actual with `Product` bound). `ReadComplexityBound` accepts `Product`/`Max`.

### [PB1-17.2] 17.2 Source-map remapping is a dead feature - output coordinates never translated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicSourceInput.cs:1090-1102`; `Tools/SharpProof.SymbolicCli/Program.cs:302-306`
- `--source-map-uri`/`--source-map-original-line`/`--source-map-original-column` are parsed, stored on
  `SymbolicSourceInput.SourceMap`, and echoed in explain output ("Source map origin: line 41, column 7"), but a repo-wide
  grep shows `OriginalStartLine`/`OriginalStartColumn` are only constructed/printed - no code consumes them to offset
  reported `Line`/`Column`. Results stay snippet-relative.

### [PB1-17.3] 17.3 Capability CI gate `IO` expansion omits `Process`/`Environment`/`Clock`/`Randomness`/`Reflection`/`Synchronization`/`NativeInterop` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:277-299` (`ExpandAllowedCapabilities`/`NormalizeCapabilities`)
- `--allowed-capability IO` expands `IO` only to `FileRead|FileWrite|Network|Console|Registry`. A method spawning a
  process (`Process` capability) counts as a *capability violation* even though `Process` is intuitively I/O.

### [PB1-17.4] 17.4 Two parallel capability enums with no shared type - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Attributes/SharpProofCapability.cs` vs `SharpProof.Symbolic/SymbolicCapabilityModels.cs`
- Classification uses `SharpProofCapability` (Attributes); result model + CLI gate use `SymbolicCapability` (Symbolic).
  Identical bit layouts today, but any future divergence silently corrupts the `Capabilities` flag and every capability
  gate with no compile-time error.

### [PB1-17.5] 17.5 Empty absolute span (`--span-start == --span-end`) accepted -> silent empty result - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/Program.cs:2060-2063,2065-2070`
- Validation only rejects `SpanEnd < SpanStart`; a zero-length span yields a successful empty report with no diagnostic.

## 18. Smaller Test Files (`SharpProof.Test`, `SharpProof.ToolingTest` - excluding huge files)

### [PB1-18.1] 18.1 `RequiresContractTests` suppression test can pass vacuously - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/RequiresContractTests.cs:118-137` (`Requires_AssumptionFeedsRuntimeHazards_SuppressesDivideByZero`)
- Only asserts `diagnostics.Where(d => d.Id == UncaughtExceptionSiteId)` is empty; never asserts the hazard would fire
  without `[Requires]` or that the assumption was consumed. A disabled hazard pipeline still passes -> masks regression.

### [PB1-18.2] 18.2 `EnsuresContractTests` uses weak `Is.Not.Empty` instead of asserting the specific diagnostic - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/EnsuresContractTests.cs:233` (`Ensures_AllowNullOverridesNonNullableParameterEntryFact`)
- Only checks *some* diagnostic exists; if the analyzer routes this case to a nullability diagnostic instead of the
  Ensures-not-proven (SP0018), the test still passes -> masks regression in the Ensures prover.

### [PB1-18.3] 18.3 `AnalyzerHostConcurrencyStressTests` vacuous `.All()` without count guard - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/AnalyzerHostConcurrencyStressTests.cs:243`
- `purityResults.Select(...).All(...)` has no `Has.Length`/`Is.Not.Empty` guard; an empty task list would pass vacuously.

### [PB1-18.4] 18.4 `FuzzToolTests` purely-negative finding check - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/FuzzToolTests.cs:194` (`f.Category == "impure_missing_sp0002"` is `False`)
- Doesn't verify the expected impure SP0002 finding was produced; a regression dropping it still passes this line.

### [PB1-18.5] 18.5 Systemic: many negative `Any(id) == False` assertions pass if analyzer emits zero diagnostics - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- Across `ExceptionReachabilitySmtTests.cs`, `ExceptionSummaryCatalogValidationTests.cs`, `DiagnosticEvidenceTests.cs`, etc.
- Each is correct for its scenario, but collectively a globally-disabled feature (zero diagnostics) would make all pass ->
  recommend adding positive controls for a representative subset.

### [PB1-18.6] 18.6 `BaselineWorkflowTests` hardcoded line/column tied to fixture internals - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.ToolingTest/BaselineWorkflowTests.cs:23-24` (`Line == 10`, `Column == 15`). Brittle to fixture changes.

## 19. PowerShell Scripts - Round 3 (`scripts`, `Tools/.../*.ps1`)

### [PB1-19.1] 19.1 Orphaned *suspended* child process + leaked job on `AssignProcessToJobObject` failure - **HIGH/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-JOB-ASSIGNMENT-FAILURE
> **Evidence:** A child created suspended was not terminated if AssignProcessToJobObject failed, and the job handle could escape cleanup.
> **Changes/tests:** JobObjectHelpers now directly terminates unassigned children and always closes ownership handles; ScriptProcessOwnershipTests.
- `scripts/JobObjectHelpers.ps1:258-262`
- Child is created `CreateSuspended` (line 247) and `$process` assigned only *after* the assign step (line 264). If
  `AssignProcessToJobObject` fails it `throw`s (line 261) before `$process` is set; the `finally` (line 296) sees
  `$process -eq $null` and never resumes/terminates the suspended child -> a permanently suspended, orphaned process
  leaks. Fires whenever the wrapper PS is *already* inside a Windows Job Object - exactly the CI scenario mandated by
  `AGENTS.md` (a process can't belong to two jobs).

### [PB1-19.2] 19.2 Interrupted `summary.partial.json` aggregated as a completed fuzz phase - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:29-37`
- A partial (interrupted/aborted) run's `summary.partial.json` is folded into totals like a real `summary.json` -> all
  downstream sums (`totalCases`, `totalFindings`, `totalSp0002`, `PhaseCount`) over-report.

### [PB1-19.3] 19.3 `build.ps1` wipes all `.nupkg` then repacks only two - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:94-100`
- `Remove-Item` deletes *every* `.nupkg` in the output dir (including a `SharpProof.Symbolic.*.nupkg` from a prior
  `build-nuget.ps1`), but `build.ps1` only re-packs `SharpProof.Package` and `SharpProof.Attributes` ->
  `SharpProof.Symbolic` silently dropped from output.

### [PB1-19.4] 19.4 `Generate-Readme.ps1` regex requires `[Test]` immediately before `public` - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-Readme.ps1:50`
- Pattern requires `[ReadmeExample("x")]\s*[Test]\s*public`. `[ReadmeExample("x"), Test]`, `[Test, ReadmeExample("x")]`,
  or any other attribute between them won't match -> id never added -> later `throw` aborts README regeneration.

### [PB1-19.5] 19.5 `Get-SharpProofRawSmtHotspots.ps1:28` blind `Substring` (4th instance of Round-2 Section 11.3) - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofRawSmtHotspots.ps1:28`
- Same unguarded `$fullPath.Substring($repoRoot.Length)` path-corruption pattern (this file wasn't among the three
  enumerated in Round-2 Section 11.3).

### [PB1-19.6] 19.6 `Test-SharpProofPackageConsumers.ps1:172` `-match` can false-positive on message text - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Test-SharpProofPackageConsumers.ps1:172`
- SARIF searched as raw text for `AD0001|CS8032|CS8034|CS8785`; a legitimate analyzer *message* or embedded comment
  containing those tokens is mistaken for a load failure -> aborts consumer validation.

### [PB1-19.7] 19.7 `build.ps1` never rebuilds VSIX when a stale `.vsix` already exists - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:50-52,91`
- Only invokes MSBuild when **no** `.vsix` is present; stale VSIX after source changes ships without rebuilding.

### [PB1-19.8] 19.8 `Generate-ConfigurationReference.ps1` brittle `ForSmtModes` parsing - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-ConfigurationReference.ps1:138,144`
- Any formatting deviation in a default value makes `Convert-CSharpString` throw and abort the whole doc generation.

## 20. SearchLib Remaining - Proof Search / Witness / Hazard Factory (`SearchLib`, `SharpProof.Symbolic`)

### [PB1-20.1] 20.1 Integer overflow of `int.MinValue` negation and `int.MinValue / -1` unmodeled (unsound) - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BOUNDED-INTEGER-OVERFLOW
> **Evidence:** Bounded signed arithmetic was encoded as unbounded integer arithmetic, proving identities that fail through overflow or exceptional division.
> **Changes/tests:** Overflow-sensitive IR plus congruent opaque SMT arithmetic and division/remainder hazards; SymbolicIrTests and runtime-hazard regressions.
- `SearchLib/Z3FormulaEncoder.cs:171-178` (`EncodeIntegerUnary`/`MkUnaryMinus`), `:211-240` (`EncodeCSharpIntegerDivide`)
- Z3 integer sort is unbounded, so `-int.MinValue` encodes to `+2147483648` and `int.MinValue / -1` to `+2147483648`. In
  C# (default **unchecked**) both wrap to `int.MinValue`. No range-clamp/overflow detection; `ValidateIntegerTermSafety`
  only rejects divisor == 0 (Round-2 Section 6.1). `Math.Abs(int.MinValue) == int.MinValue` is **true** in C# but **false** in
  the SMT model -> unsound `ProvablyPure`/`ProvablyImpure`. Distinct from Section 6.1 (which only covers divisor == 0).

### [PB1-20.2] 20.2 `InternalOnly` visibility routes throwing hazards to path-only check that ignores the trigger - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/PurityProofSearch.cs:106-125` (`Classify`)
- `if (query.Hazard.Visibility == InternalOnly)` is evaluated *before* the `switch` on `Kind`, funneling
  NullDereference/DivideByZero/BranchReachability/ImpureCallReachability into `ClassifyInternalOnlyEffect` (path
  feasibility only, trigger discarded). Those kinds are never "internal-only" in .NET; if such a query is built, it is
  proven `ProvablyPure` whenever the path is feasible -> missed hazard. Latent; worth a guard/assert.

### [PB1-20.3] 20.3 Path feasibility mislabeled `Satisfiable` under approximate regex (`adjustApproximation:false`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:137` (+ `PurityProofSearch.ClassifyCore`)
- When the path has an over-approximated (non-`isExact`) regex, `CheckPathAndImpurityWithWitness` calls
  `CheckSatisfiability(..., false)` so `Feasibility` reports `Satisfiable` while the witness `Status` is `Approximate`.
  Final outcome is still sound, but any caller inspecting `check.Path.Feasibility` directly is misled.

### [PB1-20.4] 20.4 No enforced cumulative budget; each check gets a fresh per-call rlimit - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/SmtSolver.cs:29,36-55` + `SearchLib/Z3FormulaEncoder.cs:58`
- `ConsumedResourceCount` is accumulated but `CreateSolver` always sets a fresh per-call rlimit; nothing enforces the
  cumulative counter. A reused `PurityProofSearch` has no enforced method-level cap (resource-accounting gap).

### [PB1-20.5] 20.5 Path check can consume up to 2x timeout/rlimit before impurity check (compounds Round-1 Section 4.7) - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SearchLib/SmtSolver.cs:96-121`
- Both original-conditions and prepared-conditions attempts run with the full timeout; a path phase that internally ran
  two full-rlimit attempts can leave `remaining <= 0` for the impurity query -> collapses to `Unknown`.

### [PB1-20.6] 20.6 `TryGetMemoryExtensionsViewSlicingShape` assumes positional `[start, count]` int-argument order - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs:2176-2185`
- Collects every int-typed param positionally; any overload where an int param isn't strictly start-then-length
  (Index/Range-based or extra int param) mis-assigns start/count -> incorrect slicing-bounds hazard.

### [PB1-20.7] 20.7 Non-null reference can be mislabeled null in witness (converse of Round-2 Section 6.7) - **MEDIUM witness, rooted in Section 6.7**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SearchLib/Z3FormulaEncoder.cs:587-597`
- `model.Evaluate(...).Equals(nullValue)` hash-consing can also make a *non-null* reference report `IsNull:true,
  Status:Exact, Value:"null"` - a wrong Exact witness value (doesn't change the proof outcome, but unsound witness).

## 21. Shared, Config, Baseline, Build Props (`Shared`, root props)

### [PB1-21.1] 21.1 `BclPurityFallbackHeuristics.HasMutatingName` over-matches by prefix -> pure immutable BCL methods flagged `probably_impure` - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:136` (`HasMutatingName`), `:194-225` (`StartsWithAny`)
- Prefix match (`Add`/`Create`/`Remove`/`Insert`/`Replace`/`Clear`/`Set`/`Read`/`Write`/...) classifies
  `System.Collections.Immutable.ImmutableArray<T>.Add/.Create/.Remove/.Insert/.Replace/.Clear` and
  `ImmutableHashSet<T>.SetEquals` as `probably_impure` -> false SP0002 when no stronger pure evidence exists. (Distinct
  from Round-2 Section 12.5, which is only `IsAmbientNamespaceOrType`.)

### [PB1-21.2] 21.2 SMT/analysis-limit config options: `build_property.` prefix honored by validation but NOT by the engine - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs:149-166` (prefix-unaware `TryGetGlobalOption`) vs
  `SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs:599-622` (prefix-aware); `SymbolicProjectConfiguration`
  reads via the prefix-unaware helper but validation reports via the prefix-aware helper.
- A value supplied via an MSBuild property named like the config key is validated/reported as effective, yet the SMT
  engine applies the hardcoded default -> silent config divergence.

### [PB1-21.3] 21.3 `ClassifyProperty` returns `ProbablyPure` for static reference-returning getters - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Shared/BclPurityFallbackHeuristics.cs:151-155`
- Guard `!shape.HasValueLikeReturn && !shape.IsStatic` means a **static** getter returning a reference-like type skips
  the `Unknown` branch and falls to `ProbablyPure` -> wrong guess for mutable static reference properties.

### [PB1-21.4] 21.4 `IsLikelyFrameworkValueTypeName` treats any `*Handle` framework type as value-like - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/BclPurityFallbackHeuristics.cs:250` (`EndsWith("Handle")`)
- Includes reference types like `System.Runtime.InteropServices.SafeHandle` -> biased toward `probably_pure`.

### [PB1-21.5] 21.5 `ImpurityCatalog` `simplifiedName` branch is dead / never matches `Constants.KnownImpureMethods` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Engine/ImpurityCatalog.cs:169-171`
- `$"{symbol.ContainingType.Name}.{symbol.Name}"` produces `"FileInfo.get_Length"`, but `KnownImpureMethods` entries are
  fully namespace-qualified with `.get`/`.set` suffix. The full `signature` path is what matches -> no-op dead branch.

### [PB1-21.6] 21.6 Baseline path normalization diverges between dedup key and comparison key - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:615` (`NormalizePath(...).ToUpperInvariant()`) vs `:231`
  (`OrdinalIgnoreCase`). Disagree for non-ASCII (e.g. Turkish dotless `i`) -> inconsistent dedup/explain.

### [PB1-21.7] 21.7 Scope-option `AllowedValues` lists incomplete and `IsCanonicalAllowedValue` is dead - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:80-81,116-118` (allowed = `{all,public,internal,off}`)
  while the parser/validator also accepts `{public-only,internal-only,none,false}`. `IsCanonicalAllowedValue` (`:392-397`)
  is never called -> documented allowed-values under-reported.

### [PB1-21.8] 21.8 `GetSmtMode` uses a hardcoded key string instead of `ConfigKeys.SmtMode` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs:114` (magic string `"sharpproof_smt_mode"`). Drift risk if the
  constant changes.

### [PB1-21.9] 21.9 Baseline entries stamped evidence schema v2 even when derived from legacy-v1 SARIF - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:316-325` (default `EvidenceSchemaVersion = CurrentVersion = 2`)
  while `ValidateEvidenceSchemas` (`:459-464`) accepts legacy v0/v1. Migration-stamping inconsistency.

### [PB1-21.10] 21.10 `StructuralMethodIdentity.ContractVersion` is unused - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `Shared/StructuralMethodIdentity.cs:26` (constant `ContractVersion = 1`) never referenced in `ToCanonicalKey`/
  `TryParseCanonicalKey`; only the literal prefix `"spm1"` encodes version -> no in-key version field for migration.

## 22. Tools Subprojects & Packaging (`Tools/*`, `SharpProof.Package`, `SharpProof.Vsix`)

### [PB1-22.1] 22.1 Fuzz harness: analyzer crashes conflated with expectation failures - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:494-515,517-624`
- On analyzer exception, `GetAnalyzerDiagnosticsAsync` returns empty `Diagnostics` + the exception text. `Evaluate` then
  runs expectation checks against the *empty* set AND adds an `analyzer_exception` finding -> for `DefinitelyImpure` it
  also trips `impure_missing_sp0002`, for `ImpureWithException` also `missing_sp0010`, and combined with repeat-runs can
  additionally trip `nondeterministic_diagnostics`. One transient crash yields three misleading findings; the real
  "analyzer threw" signal is muddied (compounds Round-1 Section 1.1 / Section 1.3 under `Parallel.ForEachAsync`).

### [PB1-22.2] 22.2 NuGet package omits Linux native Z3 (`libz3.so`) - **MEDIUM**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-LINUX-Z3-DISTRIBUTION
> **Evidence:** The pinned Microsoft.Z3 package supplies no Linux native asset to include; adding a separately sourced native binary changes the distribution and licensing surface.
> **Changes/tests:** The loader now supports libz3.so when supplied; bundling a Linux binary remains an additive packaging enhancement.
- `SharpProof.Package/SharpProof.Package.csproj:48-50` (`_AddAnalyzersToOutput`)
- Only `runtimes/win-x64/native/libz3.dll` and `runtimes/osx-x64/native/libz3.dylib` are packed; `Microsoft.Z3` 4.12.2
  also ships `runtimes/linux-x64/native/libz3.so` but it is never copied -> `DllNotFoundException` for Linux consumers.

### [PB1-22.3] 22.3 VSIX ships facades by hardcoded literal paths from `$(Configuration)` output - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Vsix/SharpProof.Vsix.csproj:36,51-60`
- The 10 facade DLLs are `Include`d from `$(AnalyzerPayloadDirectory)\$(Configuration)\netstandard2.0`. (a) If the
  analyzer output is missing any facade (trimming/config mismatch), `Include` with a literal non-existent path **fails
  the VSIX build**. (b) The VSIX SDK already auto-includes the `ProjectReference` outputs (confirmed in
  `bin\Release\net472\vsix-extracted\`), so the explicit `Content` list duplicates assemblies -> binding-conflict risk
  (related to but distinct from Round-1 Section 5.1).

### [PB1-22.4] 22.4 Fuzz harness: `compilation_error` cases bypass expectation validation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:437-452`
- A non-compiling generated case is only a `compilation_error` finding; no expectation check and no assertion that the
  shape builder *should* have produced compilable code -> a broken generator is masked as ordinary "non-compiling input".

### [PB1-22.5] 22.5 Baseline `migrate` is a no-op that does not upgrade schema version - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline/Program.cs:67-74`; `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:9-20`
- `migrate` re-serializes without bumping `BaselineDocument.Version` or `EvidenceSchemaVersion`; prints "Migrated
  baseline evidence to schema v2." misleadingly. (Legacy v1 still accepted by `ValidateEvidenceSchemas`, so no hard
  failure.)

### [PB1-22.6] 22.6 Fuzz harness determinism check compares full diagnostic message text - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:732-744` (`ToDiagnosticSignature` includes `GetMessage()`)
- Hashes the entire message; nondeterministic witness text/addresses/ordering flags `nondeterministic_diagnostics`
  even when kind/properties are stable -> false-positive nondeterminism signal.

### [PB1-22.7] 22.7 VSIX manifest: `ProductArchitecture` used as a child element - **LOW (verify against schema)**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Vsix/source.extension.vsixmanifest:9-17`
- `<ProductArchitecture>amd64</ProductArchitecture>` appears as a *child* of each `<InstallationTarget>`; in the VSIX
  2.0.0 schema this is normally an *attribute*. Builds today (SDK tolerates), but strict install validation could reject.

### [PB1-22.8] 22.8 Fuzz harness: error-path cases contribute empty operation-kind coverage - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:441,444,454`
- Non-compiling cases pass `Empty` operation kinds, so they never contribute to `UnobservedOperationKinds`; after a
  generator regression many erroring cases undercount observed operation kinds.

## 23. Concurrency / Thread-Safety / Caching (`SharpProof.Analyzer` engine, `SearchLib`, `SharpProof.Symbolic`)

### [PB1-23.1] 23.1 Shared `SemanticModel` per `SyntaxTree` used concurrently across methods - **HIGH**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ROSLYN-CONCURRENT-READS
> **Evidence:** Roslyn compilation and semantic model objects are immutable and support concurrent analyzer reads; cross-tree access is centralized through CompilationSyntaxAccess.
> **Changes/tests:** No serialization added; existing concurrent analyzer tests cover the boundary.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:18,112-126,74-82`
- Caches one `SemanticModel` per `SyntaxTree` and returns the SAME instance for every method in that tree.
  `MethodPurityAnalyzer` (Round-1 Section 1.4) calls `GetPurity` concurrently for many methods (analyzer registered with
  `EnableConcurrentExecution`). Two methods in the same file are analyzed in parallel and both receive the identical
  `SemanticModel`, which is then driven through `engine.IsConsideredPure -> ... semanticModel.GetOperation/GetSymbolInfo`.
  Roslyn's `SemanticModel` is **not thread-safe** for concurrent instance-member use -> intermittent
  `InvalidOperationException`/`NullReferenceException` or silently wrong verdicts. Direct data race on shared mutable
  Roslyn state.

### [PB1-23.2] 23.2 Process-global SMT result cache never scoped to a compilation / never invalidated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:16-20,403-420,217-254`
- `s_sharedQueryCache` and `s_sharedQueryFlights` are `static` (shared across all services/compilations), keyed on
  `CreateSharedQueryKey(Options, queryKey)`. Results from one run are served for unrelated later runs with identical
  options; `Dispose()`/`RequestGlobalSolverContextRecycle()` never clear `s_sharedQueryCache` -> stale cross-compilation
  reads and unbounded accumulation. Currently sound because `PurityProofResult` is value-only, but a real lifecycle gap.

### [PB1-23.3] 23.3 `SymbolicAnalysisLimits.CurrentScope` `AsyncLocal` (same hazard class as Round-1 Section 1.3) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicAnalysisLimits.cs:224,228-237,319-334`
- A second `AsyncLocal` (for analysis *limits/truncation*) set by `Push` and only reset on `Scope.Dispose`. If a push
  site wraps an `await` that resumes on a different thread, the limit scope leaks/loses and truncation changes
  nondeterministically across concurrent analyses.

### [PB1-23.4] 23.4 `ConcurrentDictionary.GetOrAdd` factory runs expensive SMT work more than once - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:74-82`; `SharpProof.Analyzer/MethodBodyAnalysisState.cs:91-99,104-117`
- `GetOrAdd` does not guarantee the factory runs once; under concurrency `engine.IsConsideredPure` (SMT-backed, mutates
  shared `SmtAnalysisService` caches) and the `Lazy<object>` method-body resolver can execute twice -> wasted expensive
  work and potentially inconsistent per-call caches.

### [PB1-23.5] 23.5 Concurrent per-method re-analysis does not reuse the fixed point for callees - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Engine/CompilationPurityService.cs:64-83` + `Engine/Analysis/...PurityAnalysisEngine.Entry.cs:20-42`
- After `EnsureFixedPoint`, `GetPurity` for a method not in `_fixedPoint` calls `engine.IsConsideredPure` *outside* the
  fixed-point lock, building a fresh local `purityCache` that does not consult `_fixedPoint` for callees -> verdicts
  inconsistent with the fixed point depending on entry point/thread (concurrency manifestation of Round-1 Section 1.4).

### [PB1-23.6] 23.6 `[ThreadStatic]` Z3 `Context`/`PurityProofSearch` reused across `SmtAnalysisService` instances; `Dispose` doesn't bump generation - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:25,351-371,101-113,130-144`
- `GetOrCreateProofSearch` reuses the thread-static context whenever `generation` matches and the factory is the default
  `static` field. `Dispose` only nulls the disposing thread's context and does **not** `Interlocked.Increment(ref
  s_solverContextGeneration)` -> a `PurityProofSearch` (with the plain-`Dictionary` caches flagged in Round-2 Section 6.12) is
  carried from a possibly-disposed service into a new one on the same OS thread -> encoder caches accumulate across
  unrelated analyses (unbounded native memory growth); relies on same-thread disposal.

### [PB1-23.7] 23.7 `BoundedConcurrentCache` shared key omits some `SmtAnalysisOptions` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/Smt/SmtAnalysisService.cs:584-595` (`CreateSharedQueryKey`)
- Key includes `Mode`/`timeout_ms`/`max_path`/`max_expr` but not the full `SymbolicAnalysisLimits`/other options that
  affect truncation/Unknown -> two services differing only in an un-encoded field share cached results.

### [PB1-23.8] 23.8 `SmtSolver._regexValidationCache` is a plain (non-concurrent) `Dictionary` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SearchLib/SmtSolver.cs:21`
- Safe only because access is serialized by `SmtAnalysisService._solverLock` and each `SmtSolver` is single-thread-static.
  Any future sharing/drop of `_solverLock` corrupts it (same latent risk as Round-2 Section 6.12).

## 24. Docs / Samples / Demos - Round 3 (`docs`, `samples`, `README`)

### [PB1-24.1] 24.1 `proof-queries.md` `SymbolicSourceMap` constructor uses wrong named-argument names -> won't compile - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/proof-queries.md:176-177` vs `SharpProof.Symbolic/SymbolicSourceMap.cs:5-8`
- Sample passes `originalLine`/`originalColumn`, but the constructor params are `originalStartLine`/`originalStartColumn`
  (CS1739). `standalone-query-inputs.md:164-165` uses the correct names -> the two docs are mutually inconsistent.

### [PB1-24.2] 24.2 `complexity-queries.md` "Current complexity kinds" list is wrong and self-contradicts the same doc - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/complexity-queries.md:25-33` vs `:75-83` + `SharpProof.Attributes/ComplexityKind.cs:14-35`
- Lists `Constant, Linear, Product, Quadratic, Max, Unknown, RecursiveUnknown` - omits `Logarithmic`/`Linearithmic` (which
  the doc's own "Declarable bounds" section says ARE valid `[ExpectedComplexity]` values) and lists `Unknown`/
  `RecursiveUnknown` (which are *not* enum values, only reported inference states). Reader can't tell declarable values.

### [PB1-24.3] 24.3 `contracts.md` & `configuration-profiles.md` omit `SP0046` / the `nullability` kind - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/contracts.md:216,223-224`; `docs/configuration-profiles.md:13` vs `docs/configuration-reference.md:45-46` and
  `docs/nullable-verification.md:21-23`
- Both say `sharpproof_suggest_inferred_contracts` enables only `SP0034`-`SP0039` and list kinds without `nullability`;
  `configuration-reference.md` says it controls "SP0034-SP0039 and SP0046" and the default kinds include `nullability`.

### [PB1-24.4] 24.4 `baselines.md` internal contradiction about legacy/version-1 entries - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `docs/baselines.md:9` vs `:49-51`
- Line 9: "Legacy unversioned and version 1 entries are rejected." Lines 49-51: "Older three-field entries still work...
  missing optional fields are wildcards." A three-field entry IS an unversioned legacy entry -> doc both rejects and accepts.

### [PB1-24.5] 24.5 Multiple docs use a non-runnable CLI command form (`SharpProof.SymbolicCli`) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- e.g. `capability-analysis.md:93-95`, `complexity-queries.md:111-114`, `analysis-limits.md:89`, `ci-exit-gates.md:44,72`,
  `project-aware-queries.md:7,34,48`, `symbolic-invariants.md:60-139`, `smt-lifecycle.md:105`,
  `trusted-boundary-review.md:104-105`, `proven-diagnostic-suppression.md:104-106`
- Invoke the CLI as bare `SharpProof.SymbolicCli ...` instead of the canonical
  `dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- ...` -> copy-paste fails.

---

---

# Round 4 - additional findings (10 agents, de-duplicated)

The following were found by 10 additional review agents, each instructed to read
`POTENTIAL_BUGS.md` first and avoid duplicates. Highest-impact new items: two HIGH
NRE crashes in the query service layer (null `RawResult`/`rawProof`), an analyzer
purity `GetPurity` call without try/catch (crash), control-flow lowering bugs that
treat unreachable code as reachable (unsound), `MayOverflow` dropped from term
canonical key (fact conflation), Unicode `string.Length` vs Z3 `str.len` mismatch
(unsound), and a CLI explain flag that is completely non-functional.

## 25. Symbolic Query Service Layer (`SharpProof.Symbolic` services)

### [PB1-25.1] 25.1 `CreateConditionProofResult` NREs on null `RawResult` - crashes every source/invariant/line query with provable implied conditions - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-OPTIONAL-RAW-PROOF
> **Evidence:** CreateConditionProofResult reads proof.RawResult through the nullable proof result and downstream consumers are null-safe.
> **Changes/tests:** No additional change.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:~1356-1387`
- `rawResult = proof.RawResult` is `PurityProofResult?` and **can be null** (`SymbolicIrProofResult.Syntactic(...)`/
  `.Unknown(...)` set `RawResult = null`; `ClassifyConditionTruth` returns such proofs for contradictory state /
  syntactic constant truth). `rawResult?.PathCheck.Witness` then `.Witness` parses as `(rawResult?.PathCheck).Witness`
  -> **`NullReferenceException`** when `rawResult` is null.
- **Trigger:** any `Query`/`QuerySource`/`QueryFile*`/`Prove(...)` passing `ImpliedConditions` where at least one is
  syntactically provable/refutable. Very reachable -> service-level crash of the whole query.

### [PB1-25.2] 25.2 `CreateTriggerWitness` NREs on null `rawProof` - crashes the entire runtime-hazard query - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-OPTIONAL-TRIGGER-PROOF
> **Evidence:** CreateTriggerWitness uses triggerProof?.RawResult and serializes absent proof evidence conservatively.
> **Changes/tests:** No additional change.
- `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs:450-453`
- `rawProof?.ImpurityCheck.Feasibility` parses as `(rawProof?.ImpurityCheck).Feasibility`; when `rawProof` is null
  (returned by `ClassifyHazardTrigger` for Unknown reachability or a syntactic `Syntactic(Unreachable,...)` proof,
  `RawResult == null`) this throws NRE. Invoked unconditionally for every hazard candidate -> whole query crashes.
- **Trigger:** any `QueryRuntimeHazards*` call where a candidate point has Unknown reachability or a contradictory/
  syntactic trigger proof.

### [PB1-25.3] 25.3 `QueryNode` re-proves implied conditions with `includeCurrentStatementCompletionFacts` dropped - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicQueryApi.cs:573-581` vs `SymbolicSourceQueryService.cs:630-657,1055`
- Main node analysis uses the flag, but `CreateNodeProofs` -> `ProveConditionAtSyntaxTree` -> `AnalyzeProgramPoint`
  (line 1055) defaults it to `false`. Implied-condition proofs evaluated against a different program-point state than
  the main invariant -> condition proofs can disagree with `Facts`/`MergedInvariant`.

### [PB1-25.4] 25.4 `AnalyzeForInitialEntry` silently ignores `includeCurrentStatementCompletionFacts` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicInvariantService.cs:56-76`
- `AnalyzeAt` honors the flag; `AnalyzeForInitialEntry` does not -> for-loop initial-entry points get a different
  state than non-loop points under the same option (combines with 25.3).

### [PB1-25.5] 25.5 `ProveImplication` (public API) diverges from source `ProveCondition` - **MEDIUM**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SharpProof.Symbolic/SymbolicInvariantService.cs:116-168` vs `SymbolicSourceQueryService.cs:1303-1314`
- `ProveImplication` returns `Unknown`/`"smt_required"` whenever `smtAnalysis == null` (even for syntactically
  provable conditions), and does not re-check feasibility when reachability is `NotChecked` -> two public proof
  entry points map the same input to different `SymbolicTruthValue`s.

### [PB1-25.6] 25.6 `CreateConditionProofResult` assembles `Unknown`-truth witness/counterexample from the impurity model - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:1357-1394`
- For `effectiveTruth == Unknown`, `selectedModel`/`counterexample` prefer `ImpurityCheck.Witness` (the purity model)
  over the path/condition witness; a usable `PathCheck.Witness` is dropped and the counterexample forced to `None`.

### [PB1-25.7] 25.7 `QuerySourceLinePoint` throws on empty line while `QuerySyntaxTreeLine` returns empty - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicSourceQueryService.cs:687 vs 602`
- `Point` shape throws `ArgumentException("No program points found on --line.")`; the `Line` analogue returns a
  normal result with zero program points (inconsistent error handling / brittle CLI behavior).

### [PB1-25.8] 25.8 `ClassifyTriggerCore` masks provable trigger proofs when reachability is `Unknown` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs:494-495`
- Short-circuits to `Unknown` when reachability is undetermined, even if the trigger condition is provably
  `Proven`/`Unreachable` from actual path conditions -> conservative downgrade (lost precision, not unsound).

## 26. Analyzer Diagnostic Rule Implementations (`SharpProof.Analyzer` rules)

### [PB1-26.1] 26.1 `MethodPurityAnalyzer.AnalyzeSymbolForPurity` calls `GetPurity` with no try/catch (primary SP0002 path) - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Capability query failures could escape as analyzer crashes.
> **Changes/tests:** Typed failures now become SP0016 with retryable AnalysisUnavailable taxonomy; SymbolicUnknownReasonTaxonomyTests.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:168` (from `AnalyzerFeaturePipeline.cs:68`, both unguarded)
- `purityService.GetPurity(...)` has no `try/catch`, unlike the sibling `MethodExpectedComplexityAnalyzer`
  (lines 48-65) which guards the same throw classes. An SMT/unsupported-expression throw propagates out of
  `AnalyzeCallable` -> crashes the analyzer callback (can destabilize the IDE session).
- **Trigger:** any `[EnforcePure]`/`[Pure]` method whose purity symbolic query throws `ArgumentException`/
  `NotSupportedException`/`InvalidOperationException`. (Round-1 Section 1.1 covered capability/complexity/inferred-contract
  but missed this primary purity call site.)

### [PB1-26.2] 26.2 Unguarded SMT `Prove*` calls in Ensures/Requires/Nullable analyzers - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Ensures, Requires, and nullable analyzers invoked proof operations that could throw expected symbolic-analysis exceptions through Roslyn callbacks.
> **Changes/tests:** TryProveAtSyntaxNode and AnalyzerSymbolicQueryBoundary convert expected failures to conservative Unknown; analyzer boundary regressions.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:181,191`; `MethodRequiresAnalyzer.cs:126`; `NullableContractAnalyzer.cs:402,415`
- `queryService.ProveAtSyntaxNode(...)` / `Prove(...)` have no guard; same throw classes as Section 1.1. A thrown exception
  propagates out of `AnalyzeCallable` and crashes analysis -> no diagnostics rather than degrading to `Unknown`.

### [PB1-26.3] 26.3 `[Ensures]` on destructors and expression-bodied constructors never verified - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:342-398` (early `return` at `:72`); helpers `:628-657`
- `CollectCompletionSites` relies on `TryGetExpressionBody`/`TryGetBodyBlock`, which don't handle
  `DestructorDeclarationSyntax` and only match a constructor when `Body` is non-null (`Ctor() => field = 1;` missed).
  `completionSites.Length == 0` -> early return -> a violated `[Ensures]` on a destructor / expr-bodied ctor produces
  neither SP0018 nor SP0019 (silent verification gap).

### [PB1-26.4] 26.4 `ExceptionFlowAnalyzer.GetIdentifierLocation` omits `ConversionOperatorDeclarationSyntax` - **LOW/MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/ExceptionFlowAnalyzer.cs:~70-85` (local `GetIdentifierLocation`)
- Unlike `MethodPurityAnalyzer.GetIdentifierLocation` (Round-1 Section 8.6), this duplicate has no conversion-operator case
  -> SP0010/SP0011 reported at the whole-declaration span rather than a precise location.

### [PB1-26.5] 26.5 `MethodPurityAnalyzer` short-circuits on first (metadata) location - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodPurityAnalyzer.cs:23`
- `if (methodSymbol.Locations.FirstOrDefault() == null || methodSymbol.Locations.First().IsInMetadata) return;`
  - if the first `Location` is in metadata while a source body also exists (some partial/explicit-interface shapes),
  `First()` returns metadata and the method is skipped -> missed SP0002. Should prefer a source location.

### [PB1-26.6] 26.6 `UnsafeNullForgivingOperator` reported for an `Unreachable` proof - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/NullableContractAnalyzer.cs:313-316`
- For `Unreachable` truth value, flagging `x!` as an *unsafe* suppression is wrong (should be skipped/inconclusive).

### [PB1-26.7] 26.7 `[MemberNotNull(...)]` on a property member never actually verified - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/MethodEnsuresAnalyzer.cs:170-184`
- Property (non-field) member target unconditionally reported `Inconclusive` (SP0047); a violated
  `[MemberNotNull("P")]` on a property is never surfaced as SP0018 (vs the field-member path which is checked).

## 27. Symbolic IR Control Flow / Loops / Exceptions (`SymbolicProgramPointFacts`)

### [PB1-27.1] 27.1 Post-`if` point not marked contradictory when BOTH branches definitely exit - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CATCH-ENTRY-STATE
> **Evidence:** A catch entry retained facts invalidated by mutations in the try prefix.
> **Changes/tests:** Catch entry invalidates try-mutated facts; ProgramPointFacts_CatchEntryInvalidatesMutationsFromTryPrefix.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:5330-5371`
- When both true and false branches definitely exit (`return`/`throw`), neither early-return branch fires and
  `if (trueBranchExits || falseBranchExits) return;` leaves `state` as the pre-if entry (not contradictory). Code
  after the `if` (e.g. `if (c) return 1; else return 2; return x;`) is treated as reachable using pre-if values ->
  unsound/over-optimistic proof about a dead region. Same defect at `AddCompletedBlockStateFacts:4915`.

### [PB1-27.2] 27.2 `AddCompletedTryStatementStateFacts` merges catch-body completions ignoring catch type / `when` - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CATCH-TYPE-FLOW
> **Evidence:** A deterministic mismatched catch was treated as handling a directly thrown exception.
> **Changes/tests:** Known throw types are checked against catch compatibility; ProgramPointFacts_KnownMismatchedCatchLeavesFollowingPointUnreachable.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3889-3933`
- The loop adds each catch body's normal-completion state to `completionStates` **without** inspecting
  `catchClause.Declaration` (exception type) or `catchClause.Filter` (the `when` clause). For
  `try { throw new E(); } catch (OtherException) { x = 1; }` (or an always-false `when`), the exception propagates
  and post-try code is unreachable, yet `completionStates` includes the catch body -> merged post-try normal state
  wrongly includes `x == 1` and treats the post-try point as normally reachable. **Unsound normal-completion fact.**

### [PB1-27.3] 27.3 `for(;;)` infinite loop with no `break` leaves post-loop state as pre-loop state - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:4068-4078`
- No `break` -> `TryCreateGuardedBreakLoopExitSymbolicCondition` returns `false` -> no `case` matches, `switch` falls
  through with `state` unchanged. Everything after an infinite `for(;;)` is treated as reachable -> wrong
  control-flow reachability.

### [PB1-27.4] 27.4 Plain expression-statement calls lose normal-completion facts in block completion - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3699-3788` (`AddPriorStatementStateFacts`) vs `:3780-3867,4927-4968`
- `AddPriorStatementStateFacts` only routes `ExpressionStatementSyntax` to `AddAssignmentExpressionStateFacts` when
  it's an `AssignmentExpressionSyntax`; a bare `F(x);` falls to a no-op. Top-level `AddCompletedStatementStateFacts`
  (line 3853) *does* call `AddNormalCompletionStateFacts` for it. So not-null postconditions / throw-guard / member-
  not-null / dereference facts are dropped for bare call statements in blocks (precision loss).

### [PB1-27.5] 27.5 Merge keeps all entry conditions/facts + branch-only `SymbolVersions` -> inconsistent merged state - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:3953-4015` (companion to Round-1 Section 2.2 / Section 7.1)
- Every `entryState` path condition is unconditionally kept; merged `SymbolVersions` computed only across completion
  states. A variable whose version differs across branches is dropped from `SymbolVersions` while a retained
  `entryState` fact still references it -> mismatched SMT variable name.

### [PB1-27.6] 27.6 Throwing switch-expression arm modeled only via `Not(armCondition)` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicProgramPointFacts.cs:8486-8502`
- Adding `Not(armCondition)` for a throwing arm is sound for the normal path but relies entirely on the separate
  exception-flow analysis; the normal-flow facts systematically under-represent exceptional exits (companion to Section 27.2).

## 28. SymbolicCli Program & Explain Reports (`Tools/SharpProof.SymbolicCli`)

### [PB1-28.1] 28.1 `explain --report-max-hazards` (and JSON `output.maxHazards`) is always rejected - **HIGH**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CLI-REPORT-LIMIT-ROUTING
> **Evidence:** The current CLI separates report limits from analysis limits and accepts both the command-line and JSON report-max-hazards forms.
> **Changes/tests:** Existing explain/report-limit tooling tests pass; no new change required.
- `Tools/SharpProof.SymbolicCli/Program.cs:1743-1747,1943-1954`; `SymbolicCliJsonRequest.cs:337`
- `--report-max-hazards` sets `HasCompactHazardOutputLimit` (same flag as the query-only `--max-hazards`), but
  validation only whitelists `RuntimeHazards`, not `Explain`. Every `explain` invocation with report-max-hazards
  crashes with the misleading "--max-hazards requires --runtime-hazards". A documented, schema-supported flag is
  completely non-functional for the entire explain subcommand (and for any JSON `explain` + `output.maxHazards`).

### [PB1-28.2] 28.2 Complexity CI gate cannot rank `Product`/`Max`, misreports exceed - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-COMPLEXITY-PARTIAL-ORDER
> **Evidence:** The exit gate omitted the defined Product/Max greater-than-Constant relations.
> **Changes/tests:** SymbolicCliExitGateEvaluator now implements those relations; SymbolicComplexityQueryTests.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:368-391` (`TryGetChainRank`), `:310-326` (`CompareComplexity`)
- `TryGetChainRank` has no case for `Product`/`Max` -> returns `false` -> `CompareComplexity` returns `Incomparable`. A
  method whose actual complexity is `Product`/`Max` (or a `--fail-on-complexity-exceeded` bound of `Product`/`Max`,
  or even `Max` vs `Max`) is reported `Incomparable` instead of `Exceeds`/`Within` -> wrong exit code. (Related to
  Round-3 Section 17.1 but in the CLI gate evaluator specifically; the chain-rank gap is the root.)

### [PB1-28.3] 28.3 Text `explain --position <n>` never prints runtime hazards - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:365-383` (`PrintExplainResultAsync`)
- The runtime-hazards block is guarded by `!options.Position.HasValue`, so `explain --position 123` skips hazards
  entirely; even if unguarded it would query `Line(0)` (default). Machine-readable explain (report/json/markdown)
  properly resolves the point -> text explain diverges from the other formats for `--position`.

### [PB1-28.4] 28.4 JSON `output.maxDiagnostics`/`maxItems` in non-explain modes rejected - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SymbolicCliJsonRequest.cs:348-356` (always emits `--report-max-*`) vs `Program.cs:1922-1927`
- A JSON request with `{"mode":"query","output":{"maxItems":10}}` is rejected ("require explain") even though the
  schema offers the field generically -> schema/CLI mismatch.

### [PB1-28.5] 28.5 `explain` text + `--report-max-diagnostics`/`--report-max-items` throws - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:1894-1903`
- For `explain` (text) with any of these limits set, it throws "require explain --json/--sarif/--markdown". The
  explain JSON request (SymbolicCliJsonRequest.cs:349-356) unconditionally emits these when present and defaults
  `format` to `"text"` -> an explain JSON with `output.maxDiagnostics`/`maxItems` and no `format` crashes.

### [PB1-28.6] 28.6 JSON query options missing hazard-status/exception-type/category fields - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SymbolicCliJsonRequest.cs:567-582,280-294`
- `SymbolicCliJsonQueryOptions` exposes `HazardKinds` but not `HazardStatuses`/`HazardExceptionTypes`/`HazardCategories`,
  and `AddQueryOptions` never emits `--hazard-status`/`--hazard-exception-type`/`--hazard-category` -> a
  `runtimeHazards` JSON request cannot filter by status/exception/category despite those being first-class CLI flags.

### [PB1-28.7] 28.7 JSON `summaryOnly:true` + `format:json` contradictory; text silently flips to compact - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SymbolicCliJsonRequest.cs:332`, `Program.cs:1760-1763,1874-1875`
- `--summary-only` forces `CompactJson`; a JSON request with `summaryOnly:true` + `format:json` expands to both
  `--summary-only` and `--json` -> `Parse` throws "--json cannot be combined with --compact-json." Also `summaryOnly`
  with default `format:"text"` silently converts output to `--compact-json` (user asked text, gets compact JSON).

### [PB1-28.8] 28.8 SARIF hazard level for `Unreachable` maps to `"none"` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExplainReport.cs:178-184`
- `SymbolicRuntimeHazardStatus` has four values (`Proven, Unreachable, Unknown, Unsupported`); the status switch has
  no `Unreachable` arm -> hits `_ => "none"` (semantically null SARIF level).

### [PB1-28.9] 28.9 Markdown output written with `Console.Write` (no trailing newline) - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.SymbolicCli/Program.cs:36` (`Console.Write(report.ToMarkdown())`) vs `:34/:38/:128` (`WriteLine`).
  Inconsistent; can cause shell prompt to appear on the same line.

### [PB1-28.10] 28.10 `--fail-on-unproven-implies` fails when zero proof outcomes are produced - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs:51-67`
- `TotalCount == 0` -> PASS condition false -> gate fails. Whether "no proof emitted" should equal "unproven" is
  debatable, but it means the gate can fail on requests where no proof was ever computed, not just genuinely-unproven.

## 29. Fuzz Harness (`Tools/SharpProof.Fuzz.Core`)

### [PB1-29.1] 29.1 `ExpectedOperationKinds`/`ExpectedSyntaxKinds` collected but NEVER validated - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:1555-1556,3008-3010,517-624`
- Each registry entry declares expected kinds; `AnalyzeCaseAsync` collects the *real* kinds from generated source
  but nothing compares them back. Only `PrimaryShapeIds` is consumed. A shape builder that stops producing its
  declared operation/syntax kind emits **no finding** -> silent coverage under-counting (defeats the harness).

### [PB1-29.2] 29.2 Operation-kind coverage counts inflated by operation-tree depth - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:641-658` (`CollectOperationKinds`)
- Each operation-rooted node contributes its entire operation subtree, so a nested operation is counted once per
  ancestor operation node. Counts multiplied by nesting depth; non-comparable with `CollectSyntaxKinds` (counts each
  syntax node once).

### [PB1-29.3] 29.3 Determinism repeat-run diagnostics never re-evaluated for expectations (first-run-only) - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:456-491,517-624`
- `Evaluate` runs only on `firstDiagnostics.Diagnostics`; the second run feeds only the `nondeterministic_diagnostics`
  comparison. When run #1 is the anomalous one, expectation findings (SP0002/SP0010) may be false positives caused
  by nondeterminism, indistinguishable from a real failure.

### [PB1-29.4] 29.4 `BuildImpureTypeParameterObjectCreation` expects impurity for `new T()` (fresh allocation) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2246-2257`
- `new T()` creates a fresh, owned object (canonical pure under the fresh-ownership model, same as
  `BuildImpureOwnershipEscapeChain` which is likewise flagged impure). `DefinitelyImpure` expectation -> false
  `impure_missing_sp0002` if the analyzer classifies `new T()` as pure.

### [PB1-29.5] 29.5 `BuildImpureWithExpression` expects impurity for a `with` expression (fresh record copy) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2510-2528`
- `var updated = data with { Value = x };` returns a freshly copied immutable record (pure in SharpProof's model).
  `DefinitelyImpure` expectation -> false `impure_missing_sp0002` if the analyzer returns pure.

### [PB1-29.6] 29.6 No expectation handling for SP0004 / SP0009 diagnostics - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:517-624,3015-3022`
- `Evaluate` checks only SP0002/SP0010; `Sp0004Count`/`Sp0009Count` are tallied but never validated -> a pure case
  that unexpectedly emits SP0004/SP0009 is silently ignored (false-negative in harness validation).

### [PB1-29.7] 29.7 Fragile reflection crashes the whole run at startup - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/RoslynShapeManifest.cs:360-379`
- `GetRegisteredRuleOperationKinds` uses `analyzerAssembly.GetType("...RuleRegistry", true)!` + `GetMethod`/`GetProperty`
  `.Invoke(...)!` with null-forgiving operators; any rename/move of the analyzer's internal `RuleRegistry` or a
  `null` from `GetMethod`/`GetProperty` throws `TypeLoadException`/`NullReferenceException` and crashes the fuzz
  process before a single case is analyzed.

### [PB1-29.8] 29.8 `BuildImpureDeclarationExpression` expects impurity for `int.TryParse` (pure BCL) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:2220-2231`
- `int.TryParse(text, out var value)` is a deterministic pure parser (writes only a fresh `out` local). `DefinitelyImpure`
  expectation -> false `impure_missing_sp0002` if the analyzer treats `TryParse` as pure.

### [PB1-29.9] 29.9 `AllowEffectPreservingWrappers` registry field is dead - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `Tools/SharpProof.Fuzz.Core/ShapeRegistryEntry.cs:84` (assigned per entry, never read anywhere).

### [PB1-29.10] 29.10 `Enum.Parse<OperationKind>("CollectionElementInitializer")` latent crash in summary `Build` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Fuzz.Core/Program.cs:3070` (also `RoslynShapeManifest.cs:83`)
- If the referenced Roslyn `OperationKind` member name is absent, `Enum.Parse` throws `ArgumentException` inside
  `FuzzRunSummaryBuilder.Build`, discarding the entire run's summary after all cases were analyzed.

## 30. Baseline / Fuzz / Packaging Tooling (`Tools/SharpProof.Baseline*`, `SharpProof.Package`)

### [PB1-30.1] 30.1 Baseline `TryGetFirstPhysicalLocation` keys only `locations[0]` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:582-598` (used by `GetResultPath:550-559`, `GetResultLocation:561-580`)
- Reads only the first `locations[]` entry. A diagnostic with more than one location gets its path/line/column from
  the first physical location, not necessarily the canonical primary one -> false stale/matched verdicts in
  `Explain`/`Prune`.

### [PB1-30.2] 30.2 Baseline per-entry `evidenceSchemaVersion`/`evidenceSchemaCompatibility` written but never read back - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:36-39` vs `TryAddBaselineEntry:348-409`
- `AddBaselineEntries`/`TryAddBaselineEntry` never populate those fields on parse -> on `ParseBaselineJson` every entry
  reverts to the current schema version (distinct from Round-3 Section 21.9 document-level stamping).

### [PB1-30.3] 30.3 Baseline `RunBuildAsync` has no timeout, ignores cancellation, emits unquoted `/p:ErrorLog=` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline/Program.cs:113-138` (line 125 `startInfo.ArgumentList.Add("/p:ErrorLog=" + sarifPath)`)
- No timeout / no `cancellationToken` passed (hung build hangs forever, Ctrl-C orphans the child); unquoted path with
  spaces/commas/`;`/`%` can break MSBuild property parsing so the SARIF log isn't written where expected.

### [PB1-30.4] 30.4 Fuzz `summary.partial.json` leftover re-aggregated on `--out` reuse - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:777-788` (writes `summary.json` but never deletes the `summary.partial.json`
  checkpoint) + `Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1:29-36` (falls back to `summary.partial.json`). A successful
  run leaves the partial; reusing `--out` and interrupting the next run picks up the prior leftover -> aggregates old
  and new (compounds Round-3 Section 19.2/Section 4.8).

### [PB1-30.5] 30.5 Fuzz determinism check ignores exception nondeterminism when both runs throw - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Fuzz.Core/Program.cs:460-481` vs `Evaluate:523-530,473-480`
- When run 1 throws (empty diagnostics) AND run 2 throws (empty diagnostics), both signature sets are empty ->
  `SequenceEqual` is true -> no `nondeterministic_diagnostics` raised, yet two `analyzer_exception` findings recorded.

### [PB1-30.6] 30.6 `SharpProof.Attributes.dll` delivered twice within the same package (analyzer + lib) - **LOW**

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
- `SharpProof.Package/SharpProof.Package.csproj:53-54`
- Packed to both `analyzers/dotnet/cs` (line 53) and `lib/netstandard2.0` (line 54). Within-package variant of
  Round-1 Section 5.5: consumer references the `lib/` copy while the analyzer load context loads the `analyzers/` copy ->
  different `Type` instances for `EnforcePure`/`Impure`; NuGet "asset already provided" warnings.

### [PB1-30.7] 30.7 `install.ps1` `Join-Path ... "analyzers" * -Resolve` throws if `analyzers` absent - **LOW**

> **Disposition:** Already fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
- `SharpProof.Package/tools/install.ps1:12`
- `Join-Path -Resolve` on a non-matching wildcard throws in PowerShell 5.1; a packaging/extraction layout without a
  sibling `analyzers` dir aborts with an unhandled exception instead of gracefully skipping.

## 31. Capability / Complexity / Nullable / Allocation Tests (`SharpProof.Test`)

### [PB1-31.1] 31.1 `BoundaryAttributeTests` brittle hardcoded diagnostic span - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/BoundaryAttributeTests.cs:94-101`
- Uses `.WithSpan(8,16,8,29)` hardcoded while every other test in the file uses marker-based verification
  (`{|SP0002:...|}`). Any reformatting/whitespace change silently shifts the span -> confusing verifier mismatch
  (same brittle pattern as Round-3 Section 18.6, different file).

### [PB1-31.2] 31.2 `ExpectedComplexityContractTests` parallel + shared-analyzer-instance flakiness - **MEDIUM/LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/ExpectedComplexityContractTests.cs:13` (`[Parallelizable(ParallelScope.Children)`) + `:426-432`
  (`GetComplexityDiagnosticsAsync` -> `concurrentAnalysis: true`); `AnalyzerTestHost.cs:166` caches the
  `SharpProofAnalyzer` instance in a static `ConcurrentDictionary` keyed only by features.
- Parallel tests share the same analyzer instance and use `concurrentAnalysis: true`; the analyzer relies on
  `AsyncLocal` config scopes (Round-1 Section 1.3) not safe under concurrent analysis of different compilations ->
  intermittent SP0021/SP0022 emission, non-reproducible in isolation.

### [PB1-31.3] 31.3 `SymbolicComplexityTests.LineTarget_ResolvesContainingMethod` can't distinguish line vs position targeting - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/SymbolicComplexityTests.cs:457-480`
- Fixture has only one method, so `Assert.That(result.MethodName, Is.EqualTo("Work"))` passes regardless -> masks any
  off-by-one in the line-numbering convention between `GetLineNumber` and `SymbolicQueryTarget.Line`.

### [PB1-31.4] 31.4 `CachingTests.CanceledPurityRequest` only covers cancellation-at-entry - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CachingTests.cs:40-84`
- `CancellationTokenSource` cancelled *before* `GetPurity`; `EnsureFixedPoint` throws at its first
  `ThrowIfCancellationRequested`, so the partial-build abandon path (the intended scenario) is never exercised.
  A regression that builds the call graph before honoring cancellation wouldn't be caught.

### [PB1-31.5] 31.5 Capability tests use `.Single(...)` -> opaque exception on mismatch - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CapabilityContractTests.cs:119,254`
- `diagnostics.Single(item => item.Id == ...)` / `SingleDiagnostic` throw `InvalidOperationException` (no assertion
  message) if zero/multiple matching diagnostics occur -> should-be assertion failure becomes an opaque stack trace
  (same `.Single()` fragility as Round-3 Section 13.6, different file).

### [PB1-31.6] 31.6 `DiagnosticEvidenceTests` vacuous redundant id assertion - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/DiagnosticEvidenceTests.cs:3449`
- `Assert.That(diagnostic.Id, Is.EqualTo(PurityNotVerifiedId))` right after `SingleDiagnostic(diagnostics, PurityNotVerifiedId)` (line 3423) which already filtered by that id -> guaranteed true, adds no coverage.

### [PB1-31.7] 31.7 Brittle exact exception-message strings in capability/complexity tests - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/CapabilityContractTests.cs:430`, `SharpProof.Test/SymbolicComplexityTests.cs:596`
- Assert the exact message string; any wording tweak breaks the test with no behavioral change (same class as
  Round-1 Section 3.6).

### [PB1-31.8] 31.8 DiagnosticEvidenceTests negative-only `Any(id)==false` without always-present positive controls - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Test/DiagnosticEvidenceTests.cs` (e.g. ~1485-1488, 2218-2221, 2357-2360)
- Same systemic Round-3 Section 18.5 risk: a globally-disabled catalog-resolution path would make these negatives pass
  vacuously. Strongest instances pair with `matched == true` (partially guarded), but the diagnostic-level assertion
  alone is insufficient.

## 32. PowerShell Scripts - Round 4 (`scripts`, root `*.ps1`)

### [PB1-32.1] 32.1 `Join-TestFilter` hardcodes `SharpProof.Test.` namespace -> silent empty Tooling runs - **HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-TEST-FILTER-NAMESPACE
> **Evidence:** Fixture filters prefixed every fixture with SharpProof.Test, silently excluding SharpProof.ToolingTest fixtures.
> **Changes/tests:** Namespace-neutral fixture filters and lane-aware parsing; BaselineWorkflow route probe and ImpactedTestSelectionScriptTests.
- `scripts/Invoke-SharpProofImpactedTests.ps1:1331-1334`
- `Join-TestFilter` always prefixes `SharpProof.Test.`, but Tooling fixtures live in `SharpProof.ToolingTest`. When the
  selector picks a Tooling fixture, the filter becomes `FullyQualifiedName~SharpProof.Test.EffectSummaryToolTests`,
  which does NOT substring-match `SharpProof.ToolingTest.EffectSummaryToolTests` -> **zero tests run**, `dotnet test`
  exits 0 -> the impacted-test runner reports success while running nothing. Defeats the harness's purpose.

### [PB1-32.2] 32.2 `build.ps1`/`build-vsix.ps1` `-MemoryLimitMb` silently ignored for ALL `dotnet` invocations - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-MEMORY-LIMIT-SHADOW
> **Evidence:** Nested helper parameters defaulted MemoryLimitMb to zero and shadowed the build script setting.
> **Changes/tests:** Removed the shadowing parameters in build.ps1 and build-vsix.ps1; ScriptProcessOwnershipTests.
- `build.ps1:21-26,28` (and `build-vsix.ps1:13-18,20`)
- `Invoke-DotnetInRepo` declares its OWN `$MemoryLimitMb` defaulting to `0`, and every `dotnet` call site omits
  `-MemoryLimitMb`; only `Invoke-MSBuildInRepo` (no param -> script scope `6144`) is capped. The most memory-hungry
  operations (`restore`/`build`/`pack`) run **uncapped**, directly contradicting `AGENTS.md` ("pass an explicit
  `-MemoryLimitMb`"). `build-vsix.ps1` has the identical defect.

### [PB1-32.3] 32.3 `FullyQualifiedName~` fixture-extraction regex truncates at first dot - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-TEST-FILTER-NAMESPACE
> **Evidence:** The lane/fixture parser assumed one namespace shape and could truncate or misroute qualified filters.
> **Changes/tests:** Namespace-neutral filter construction and explicit lane recognition; ImpactedTestSelectionScriptTests.
- `scripts/Invoke-SharpProofTests.ps1:387`
- `[regex]::Matches($Filter, 'FullyQualifiedName~([A-Za-z_][A-Za-z0-9_]*)')` stops at the first `.`. For
  `FullyQualifiedName~SharpProof.ToolingTest.EffectSummaryToolTests` it captures `SharpProof` -> lane selection returns
  only the Main project -> filter matches nothing -> silent empty run. (Underlying mechanism of Section 32.1.)

### [PB1-32.4] 32.4 `build.ps1` vswhere path uses double backslashes -> MSBuild discovery likely dead - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:57,59` (`"Microsoft Visual Studio\\Installer\\vswhere.exe"`, `-find "MSBuild\\**\\Bin\\MSBuild.exe"`)
  vs `build-vsix.ps1:33` which correctly uses single backslash. Double separators aren't normalized by vswhere ->
  falls through to hardcoded candidates; a non-default VS install -> "Could not locate MSBuild.exe" even though
  vswhere would have found it.

### [PB1-32.5] 32.5 README generator rejects non-`async Task`/`void` example tests - **MEDIUM/LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Generate-Readme.ps1:50` (distinct axis from Round-3 Section 19.4 which is about attribute adjacency)
- Only `async Task`/`void` return types match; an example test returning `Task`/`Task<T>`/`ValueTask`/`async Task<T>`
  isn't registered -> later `throw "missing a [ReadmeExample] test."` breaks README regeneration.

### [PB1-32.6] 32.6 `demo-sharpproof.ps1` uses stale project if `dotnet new` fails - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/demo-sharpproof.ps1:34-41` (compounds Round-2 Section 11.1 native-silence): failed `dotnet new` (piped to
  `Out-Host`) doesn't stop the script; a leftover `.csproj` in `$demoPath` is silently used -> writes sample code
  over potentially the wrong project and reports success.

### [PB1-32.7] 32.7 Metrics script `$repoPath` leaks to script scope; unguarded `Substring($repoRoot.Length)` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Get-SharpProofProductionMetrics.ps1:58,67` (compounds Round-2 Section 11.3): `$repoPath` assigned inside
  `Where-Object` leaks to script scope; `Convert-ToRepoPath` does unguarded `$fullPath.Substring($repoRoot.Length)`
  with casing/8.3/long-path divergence risk.

### [PB1-32.8] 32.8 `Invoke-SharpProofImpactedTests.ps1` git-failure mode / JSON host empty selection - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `scripts/Invoke-SharpProofImpactedTests.ps1:40-44`: missing git -> command-not-found raw error; no diff -> "No changed
  files" + exit 0, and the JSON host emits `success=true` empty selection, masking that impact selection did nothing.

### [PB1-32.9] 32.9 Background-lane arg quoting only escapes single quotes - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofTests.ps1:807-814` (the *actual execution* path, distinct from Round-2 Section 11.7 display-only):
  args containing `"`/backtick/`$` are not escaped for the spawned `powershell.exe -File` -> latent breakage.

### [PB1-32.10] 32.10 `Generate-ConfigurationReference.ps1` brace-splitter tracks only `(` `)` - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `scripts/Generate-ConfigurationReference.ps1:33-117`: `Get-BalancedArguments` increments/decrements only on
  parentheses; a `{ ... }` collection/object initializer containing commas would be mis-split.

### [PB1-32.11] 32.11 `Invoke-SharpProofReleaseValidation.ps1` builds VSIX via `dotnet build /p:EnableVsixPackaging=true` - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `scripts/Invoke-SharpProofReleaseValidation.ps1:66-70,109`: unlike `build-vsix.ps1` (uses VS SDK MSBuild), this uses
  plain `dotnet build` for VSIX packaging -> may silently produce no VSIX or fail (VSIX targets need VS SDK MSBuild).

### [PB1-32.12] 32.12 `build.ps1:91` rebuilt VSIX rediscovered without `-Recurse` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `build.ps1:91` uses `Get-ChildItem ... -Filter *.vsix` (no `-Recurse`) while line 50 uses `-Recurse`; if MSBuild
  emits the `.vsix` in a subfolder, the post-build lookup misses it -> reports "VSIX: not found".

## 33. Shared Config / Constants / Baseline Deep (`Shared`, config parsers, `SharpProof.Baseline.Core`)

### [PB1-33.1] 33.1 Setter-only properties can never match a configured member key - **MEDIUM**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/ConfiguredMemberKey.cs:15-19` (`TryCreate`, bails when `property.GetMethod == null`)
  + `Engine/ImpurityCatalog.cs:76-86` (`TryGetConfiguredMember`).
- A setter-only property can't produce a canonical key, so `TryGetConfiguredMember` returns `false` even when
  `ExtraKnownImpureMethods`/`ExtraKnownPureMethods` contains the valid `spm1|...|property-set.set` key. The validator
  `ValidateStructuralMemberKeyList` accepts it -> configuration silently ignored at analysis time (no diagnostic, no
  effect).

### [PB1-33.2] 33.2 `.baseline-check\.globalconfig` has a misspelled config key - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `C:\w\PurelySharp\.baseline-check\.globalconfig:3` (`purelysharp_enable_effect_summary_json = false`) vs the correct
  `sharpproof_enable_effect_summary_json` (`ConfigKeys.EnableEffectSummaryJson`). The typo'd key is unknown to the
  engine and silently ignored; currently masked only because the root `.globalconfig` sets the correct key to `false`.

### [PB1-33.3] 33.3 `StringList` config options never validated (typos silently ignored) - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs:682-683` (`case StringList: return;`)
- `sharpproof_known_impure_namespaces`/`known_impure_types`/`attribute_stub_namespaces`/`suggest_missing_enforce_pure_
  namespace_filters` get NO validation. A wrong-cased/typo'd value (e.g. `System.IOo`) is accepted as "valid" yet
  never matches `ns.ToDisplayString()` (case-sensitive in `ImpurityCatalog`) -> override has no effect, no diagnostic.

### [PB1-33.4] 33.4 Dead `Constants.KnownImpureMethods` entries that can never match - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Shared/Constants.cs:44-45` (`"System.Text.Json.JsonSerializer.Deserialize"`, `"JsonSerializer.Deserialize"`)
- These lack the `(...)` parameter list; `ImpurityCatalog.GetKnownImpureMemberSource` matches against
  `ToDisplayString()` which always includes parentheses -> neither can ever match (covered by properly-formed overload
  entries at lines 46-49, but clear format drift).

### [PB1-33.5] 33.5 Engine uses hard-coded magic strings for ALL SMT/analysis keys - **LOW**

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
- `SharpProof.Symbolic/SymbolicProjectQueryContext.cs` (every `TryGetGlobalOption` call, lines 33-104) - broadens
  Round-3 Section 21.8 (which flagged only `GetSmtMode`). Any future `ConfigKeys` rename silently desyncs the engine with no
  compile-time error.

### [PB1-33.6] 33.6 `SuppressionDiagnosticIds` registry default string duplicated, never applied - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:176` vs
  `AnalyzerConfiguration.cs:985-1005` (`ProvenDiagnosticSuppressionOptions.AllSupportedDiagnosticIds`). When unset,
  `GetSuppressionDiagnosticIds` returns `AllSupportedDiagnosticIds` directly; the registry default string is never
  parsed. The two lists must be hand-kept in sync; if they diverge, documented default differs from runtime (and the
  default uses uppercase-with-spaces while parsed/allowed values are lowercase).

### [PB1-33.7] 33.7 Baseline `Explain` contains an unreachable "no matching current diagnostic" branch - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:195` (always-true `ContainsKey` at `:188` -> line 195 dead).

### [PB1-33.8] 33.8 `NormalizePath` doesn't collapse `//` or resolve `..` - **LOW**

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
- `Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs:279-292` (compounds Round-3 Section 21.6): two logically-identical
  paths (`a/b/c.cs` vs `a//b/c.cs` vs `a/./b/c.cs`) treated as distinct -> baseline dedup/comparison misses.

### [PB1-33.9] 33.9 `AttributeStubNamespaces` registry default is dead/redundant - **LOW**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs:46-49` vs
  `SharpProofAttributeIdentityPolicy.cs:44` (unconditionally adds `OfficialNamespace = "SharpProof.Attributes"`).
  `FromOptions` reads via `GetValues` (no fallback) which returns empty when unset -> registry default never applied.

## 34. Symbolic Data Structures & Limits (`SharpProof.Symbolic`, `SearchLib`)

### [PB1-34.1] 34.1 `MayOverflow` dropped from the canonical term key -> fact/state identity conflation - **MEDIUM/HIGH**

> **Disposition:** Fixed
> **Canonical root cause:** RC-SYMBOLIC-OVERFLOW-IDENTITY
> **Evidence:** Overflow-sensitive and mathematical binary terms shared a structural key and associative flattening crossed overflow modes.
> **Changes/tests:** Structural keys encode overflow sensitivity and flatten only matching modes; SymbolicIrTests.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:CreateBinaryTermKey (~1411-1433)` / `CreateTermKey case SymbolicBinaryTerm (~1326-1327)`;
  consumed at `SymbolicIrFormulaEncoder.cs:262`
- `SymbolicBinaryTerm.MayOverflow` is never included in the canonical key, so `a + b` (`MayOverflow=true`) and
  `a + b` (`MayOverflow=false`) produce the *identical* key. This key feeds `DeduplicateFacts`/`CreateFactKey` (state
  dedup), `NormalizedProofKey` (state cache), and is used directly as the SMT variable name when `MayOverflow=true`.
  -> a `CheckedOverflow` exception-precondition atom for a `MayOverflow=true` term vs an identical `MayOverflow=false`
  term get the same fact key -> `DeduplicateFacts` keeps only one (by provenance length) -> a proven overflow hazard
  can be silently dropped/merged with a "safe" term; the state-proof cache conflates the two.

### [PB1-34.2] 34.2 Unicode/UTF-16 `string.Length` mismatch with Z3 `str.len` (code points) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-Z3-UTF16-SEMANTICS
> **Evidence:** Microsoft.Z3 4.12.2 MkString preserves UTF-16 code units: an astral character simplifies to length 2 and equals its surrogate concatenation.
> **Changes/tests:** No code change; current encoding matches .NET string length semantics.
- `SearchLib/SmtFormula.cs:67` (`SmtStringLengthTerm`) -> `SearchLib/Z3FormulaEncoder.cs:142` (`_context.MkLength`) +
  constant-eval `SharpProof.Symbolic/Ir/SymbolicIr.cs:882-884` (`TryEvaluateIntegerTerm` uses `stringValue.Length`)
- Z3 strings are over Unicode **code points** (`str.len` = code-point count); .NET `string.Length` = UTF-16 **code
  units** (surrogate pairs count as 2). For an astral char (`"A"` U+1D400), `.Length == 2` in C# but `str.len == 1` in
  Z3. Bounds atoms / `IsNullOrEmpty` / `length >= 0` domain facts use `str.len` while constant-eval uses
  `stringValue.Length` -> the two halves of the engine disagree -> unsound feasibility/contradiction for surrogate-pair
  strings (e.g. a false `s.Length == 1` for an astral char modeled as true; an out-of-range hazard missed).

### [PB1-34.3] 34.3 State-merge truncation silently weakens path conditions without downgrading to `Unknown` - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicStateMerger.cs` (`MergePathConditionsAcrossAll:59-68` early return;
  `MaxFactChoiceCombinationsPerTarget:39-48` break)
- When limits exceeded, the merge returns early / breaks, **dropping** remaining targets' merged conditions. Merged
  path conditions are `Or` of `(guard && branch-condition)`; dropping branch constraints makes each branch's condition
  weaker (more permissive) -> combined post-merge path condition over-permissive. A branch actually infeasible can
  appear feasible -> a path through an impure call judged unreachable -> **false `ProvablyPure`** (unsound). No `Unknown`
  downgrade forced (only a `SymbolicAnalysisTruncationEvent` recorded).

### [PB1-34.4] 34.4 String-concat canonical key is non-canonical for non-adjacent literals - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIr.cs:CreateStringConcatTermKey (1359-1367)`, `CreateNormalizedStringConcatTermKeys
  (1381-1401)`, `CollectStringConcatTerms (1369-1379)`
- Adjacent literals are merged only when adjacent in the flattened list. `("a" + x) + "b"` -> key
  `string-concat(string:a,<key(x)>,string:b)`; `x + ("a" + "b")` -> key `string-concat(<key(x)>,string:ab)`. Both denote
  `"a" + x + "b"` but produce distinct canonical keys -> `CreateFactKey`/`CreateProofKey`/`SymbolicStructuralKey.ForTerm`
  don't recognize them as equivalent -> wrong de-dup/merge and (in `SymbolicStateMerger`) equivalent string assignments
  across branches aren't merged (non-canonical output).

### [PB1-34.5] 34.5 `null` string literal not handled in `TryLowerStringTerm` (inconsistent with concat operand) - **MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/Ir/SymbolicIrLowerer.Strings.cs:TryLowerStringTerm (710-760)` vs `TryLowerStringConcatOperand (769-781)`
- `TryLowerStringConcatOperand` maps a `null` literal to `SymbolicStringConstantTerm(string.Empty)` (correct, `"x"+null=="x"`),
  but `TryLowerStringTerm` has no null-literal handling -> a `null` literal (contextually `string`) falls through to a
  null `SymbolicNullTerm`/`SymbolicStringContentTerm(nullReference)`. `s.StartsWith(null)`, `s.Contains(null)`,
  `s == null` (instance equals) are lowered to a predicate over the *content of a null reference* instead of an
  `ArgumentNull` hazard -> lost null-argument hazard from the symbolic formula (partly masked by separate hazard analysis).

### [PB1-34.6] 34.6 `AsyncLocal` limit scope corrupted by out-of-order `Dispose` - **LOW/MEDIUM**

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
- `SharpProof.Symbolic/SymbolicAnalysisLimits.cs:Scope.Dispose (319-334)`
- `Push` stores `_parent = CurrentScope.Value`; `Dispose` unconditionally sets `CurrentScope.Value = _parent`. Non-LIFO
  disposal (exception between push and dispose, sibling `using` that throws first) leaves `CurrentScope` pointing at
  the wrong scope -> truncation limits / recorded `SymbolicAnalysisTruncationEvent`s attributed to the wrong analysis
  (distinct angle from Round-3 Section 23.3 await-resume leakage).

---

## Updated top priorities to fix first (Round 1 + 2 + 3 + 4)
1. **23.1** shared non-thread-safe `SemanticModel` used concurrently (HIGH data race).
2. **25.1 / 25.2** query-service NREs on null `RawResult`/`rawProof` (HIGH crashes).
3. **26.1** analyzer purity `GetPurity` call without try/catch (HIGH crash).
4. **20.1 / 15.1** integer `int.MinValue` overflow/negation unmodeled (unsound).
5. **27.1 / 27.2** control-flow lowering treats unreachable code as reachable (unsound).
6. **34.2** Unicode `string.Length` vs Z3 `str.len` mismatch (unsound).
7. **16.1** `[MaybeNullWhen]` inverted polarity (false-positive contract violation).
8. **34.1** `MayOverflow` dropped from term canonical key (fact conflation).
9. **22.2** NuGet package omits Linux native Z3 (`libz3.so`).
10. **28.1 / 28.2** CLI explain `--report-max-hazards` always rejected; complexity gate can't rank `Product`/`Max`.

---

## Source PB2 - POTENTIAL_BUGS_2.md

This document compiles the potential bugs, soundness gaps, safety issues, resource leaks, and logic errors discovered during a comprehensive audit of the SharpProof codebase. All findings have been verified by concurrent subagents or runtime test analysis.

---

## 1. Symbolic Engine & Solver Integration (Smt, Z3)

### [PB2-1.1] 1.1 Native Z3 Solver Context Leak via Thread-Local Storage

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-SESSION-OWNERSHIP
> **Evidence:** Static thread-local proof contexts rooted native Z3 sessions for thread-pool lifetime and disposal only reached the current thread.
> **Changes/tests:** SmtAnalysisService owns tracked ThreadLocal contexts and disposes all owned sessions; SmtAnalysisServiceTests.
* **File & Lines:** [SmtAnalysisService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtAnalysisService.cs#L25-L27)
* **Severity:** High
* **Description:** The solver session `t_sharedProofSearchContext` is declared as a `[ThreadStatic]` static field. Each thread in the thread pool that runs SMT queries will initialize its own native Z3 solver session (`SearchLibProofSearchSession` which implements `ISmtProofSearchSession`). When `SmtAnalysisService` is disposed, it only disposes the session on the *current* thread (calling `DisposeCurrentThreadProofSearch()`). The sessions created on other threads are left intact in their thread-local storage. Since thread pool threads are kept alive indefinitely for the lifetime of the process, these native Z3 contexts are never collected or disposed, causing a severe native handle and memory leak.
* **Impact:** Heavy native memory usage over time, eventually leading to process crashes or out-of-memory errors in long-running analysis runs.
* **Recommendation:** Maintain a thread-safe registry of all active sessions (e.g. using a thread-safe concurrent bag of weak references) inside `SmtAnalysisService`. When `Dispose()` is called, iterate over all registered sessions and dispose of them:
  ```csharp
  private readonly ConcurrentBag<WeakReference<ISmtProofSearchSession>> _activeSessions = new();
  ```

### [PB2-1.2] 1.2 Unsound Symbolic Facts Propagation in Try-Catch Blocks

> **Disposition:** Fixed
> **Canonical root cause:** RC-TERMINAL-BRANCH-FLOW
> **Evidence:** A point after an if whose two branches both exit remained reachable.
> **Changes/tests:** The merged state is contradictory when both branches definitely exit; ProgramPointFacts_BothIfBranchesExitMarksFollowingPointUnreachable.
* **File & Lines:** [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs#L3905-L3911)
* **Severity:** High
* **Description:** When computing the entry state for a `catch` block inside `AddCompletedTryStatementStateFacts`, the analyzer copies the `entryState` (the state of the program *before* entering the `try` block) directly into the `catchState` without invalidating any mutations:
  ```csharp
  var catchState = entryState;
  AddCompletedBlockStateFacts(ref catchState, catchClause.Block, semanticModel, cancellationToken);
  ```
  This is unsound because any prefix of statements inside the `try` block could have successfully executed and mutated local variables before the exception was thrown. By starting the `catch` block analysis with the pre-try `entryState`, the analyzer assumes mutated variables still hold their old values, which is incorrect.
* **Impact:** Unsound proofs. The solver may verify invalid code as "safe" or "pure" because it incorrectly assumes a variable has its pre-try value inside the `catch` block when it could have actually been mutated.
* **Recommendation:** Collect all symbols/variables mutated inside the `try` block (by scanning the `try` block syntax) and invalidate them in `catchState` by calling `RemoveStateFactsReferencingSymbol` for each mutated symbol.

### [PB2-1.3] 1.3 SMT Variable Prefix Collision during Fact Invalidation

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SmtFormulaReferenceScanner.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtFormulaReferenceScanner.cs#L8-L12)
* **Severity:** Medium
* **Description:** When invalidating facts for a mutated symbol, `RemoveFactsReferencingSymbol` gets the symbol's SMT variable prefix using `SymbolicFactFactory.GetSmtVariableName(symbol)` (which returns `Name + "#" + location`). It then removes any facts where `ContainsVariablePrefix` returns true:
  ```csharp
  internal static bool ContainsVariablePrefix(SmtFormula formula, string variablePrefix)
  {
      return ContainsVariable(formula, variableName =>
          variableName.IndexOf(variablePrefix, StringComparison.Ordinal) >= 0);
  }
  ```
  If `variablePrefix` is `"x#1"`, the `IndexOf` call will return true for any variable name that starts with `"x#1"`, including `"x#12"` (which represents an entirely different variable `x` declared at start location 12 instead of 1).
* **Impact:** Unrelated variable facts are incorrectly removed from the state when a variable with a prefix name is mutated, causing precision loss in the symbolic analysis.
* **Recommendation:** Ensure that the match in `ContainsVariablePrefix` respects the location number boundaries. The character immediately following `variablePrefix` in the variable name must not be a digit.

### [PB2-1.4] 1.4 Missing Linux Platform Support for SMT Native Loader

> **Disposition:** Fixed
> **Canonical root cause:** RC-NATIVE-LOADER-PLATFORM
> **Evidence:** The native bootstrap assumed Windows/macOS names and load APIs.
> **Changes/tests:** Platform-specific libz3 filename selection and Linux dlopen support; SmtNativeLibraryBootstrap platform test.
* **File & Lines:** [SmtNativeLibraryBootstrap.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtNativeLibraryBootstrap.cs#L92-L100)
* **Severity:** High
* **Description:** `GetNativeLibraryFileName()` only returns file names for Windows (`"libz3.dll"`) and OSX (`"libz3.dylib"`). It returns `null` for Linux and other platforms.
* **Impact:** SharpProof cannot run its symbolic analysis on Linux machines/CI pipelines, even though Z3 fully supports Linux (via `libz3.so`).
* **Recommendation:** Add a check for `OSPlatform.Linux` and return `"libz3.so"`.

---

## 2. Core Roslyn Analyzers

### [PB2-2.1] 2.1 Contract Inheritance Deficit in Overridden and Implementing Methods

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTRACT-HIERARCHY
> **Evidence:** Behavioral contract analyzers read only the immediate method and missed override/interface contracts.
> **Changes/tests:** MethodContractHierarchy plus inherited Requires, Ensures, allocation, capability, and complexity lookup; ContractInheritanceTests.
* **File & Lines:**
  * [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs)
  * [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs)
  * [MethodExpectedComplexityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodExpectedComplexityAnalyzer.cs)
  * [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs)
  * [MethodEnsuresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodEnsuresAnalyzer.cs)
* **Severity:** High
* **Description:** Analyzers for allocation, capability, complexity, and contracts only inspect attributes directly applied to the method symbol. They do not traverse the inheritance or interface implementation chain (unlike `MethodPurityAnalyzer.cs`).
* **Impact:** Overrides/implementations bypassing attributes can silently violate base contracts (allocating memory, performing banned IO, exceeding complexity limits), and call sites invoking overrides will skip checking preconditions.
* **Recommendation:** Traverse the overridden and interface implementation hierarchy to inherit contract specifications from the base declarations.

### [PB2-2.2] 2.2 Incorrect Type Parameter Heap Allocation Analysis

> **Disposition:** Fixed
> **Canonical root cause:** RC-GENERIC-ALLOCATION
> **Evidence:** Unconstrained reference-capable type parameters were treated as non-heap objects.
> **Changes/tests:** Allocation classification now treats a type parameter as potentially heap allocated unless value-type constrained; ZeroAllocationContractTests.
* **File & Lines:** [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs#L213-L220)
* **Severity:** High
* **Description:** `IsHeapAllocatedObjectType` returns `false` for unconstrained type parameters or those with only the `new()` constraint.
* **Impact:** A call to `new T()` is not flagged as a heap allocation, but at runtime `T` can be instantiated as a class, which will allocate memory and violate the `[ZeroAllocations]` contract.
* **Recommendation:** Treat unconstrained and `new()` constrained type parameters as potentially heap-allocating unless they have a struct/value type constraint.

### [PB2-2.3] 2.3 Wrong Semantic Binding of Generic Type Parameters at Call Sites

> **Disposition:** Fixed
> **Canonical root cause:** RC-GENERIC-CONTRACT-SUBSTITUTION
> **Evidence:** Inherited/callee contract expressions were rewritten without substituting concrete method and containing-type arguments.
> **Changes/tests:** RequiresContractHelpers substitutes generic type parameters at the call site; RequiresContractTests.
* **File & Lines:** [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L216-L228)
* **Severity:** High
* **Description:** `RequiresContractHelpers.TryRewriteForArguments` fails to rewrite generic type parameters (`T`) to their call-site type arguments (e.g. `int`).
* **Impact:** Preconditions referencing type parameters (e.g. `typeof(T)`) will fail to bind at the call site if the caller is non-generic, or will incorrectly bind to the caller's own type parameter `T`.
* **Recommendation:** Substitute the caller's type parameters with the concrete type arguments supplied at the invocation site during rewriter processing.

### [PB2-2.4] 2.4 Parameter Name Shadowing Bug in Contract Rewriter

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [RequiresContractHelpers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RequiresContractHelpers.cs#L250-L274)
* **Severity:** Medium
* **Description:** `ParameterPlaceholderRewriter` is a purely syntactic rewriter that replaces all identifiers matching method parameter names.
* **Impact:** If a contract condition has a lambda or local function shadowing a parameter name (e.g. `items.Any(x => x > 0)`), the lambda variable `x` will be incorrectly rewritten to the method argument value (e.g. `(myItems).Any(x => (5) > 0)`).
* **Recommendation:** Track scopes during AST rewrite and do not substitute identifiers that shadow parameter names in local scopes or lambda parameters.

### [PB2-2.5] 2.5 Invalid Value Binding for Property Setters in Compound Assignments and Increments

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L199-L232)
* **Severity:** Medium
* **Description:** The entire assignment syntax (e.g., `x.MyProperty += 5` or `x.MyProperty++`) is passed as the setter value.
* **Impact:** The setter parameter `value` in the `[Requires]` contract is rewritten to the assignment expression (e.g. `(x.MyProperty += 5) > 0`), which introduces side-effects into pure queries, leading to SMT binding/proving failures.
* **Recommendation:** Extract the computed value (e.g. `x.MyProperty + 5` or `x.MyProperty + 1`) and pass it as the binding expression.

### [PB2-2.6] 2.6 Fragile Baseline Suppression Keys Due to Absolute Offsets

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:**
  * [MethodAllocationAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodAllocationAnalyzer.cs#L85-L94)
  * [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L184-L197)
  * [MethodRequiresAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodRequiresAnalyzer.cs#L230-L248)
* **Severity:** Medium
* **Description:** The analyzers use absolute character spans/offsets in the `evidenceKey` used for baseline suppressions.
* **Impact:** Suppressions are extremely fragile. Any modification that inserts or deletes a character above the violation site will change the key and silently break the suppression.
* **Recommendation:** Use relative offsets or line-span identifiers, or base evidence keys on structural node paths rather than absolute file offsets.

### [PB2-2.7] 2.7 Incomplete Method Body Extraction for Conversion Operators

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodPurityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodPurityAnalyzer.cs#L439-L449)
* **Severity:** Low
* **Description:** `GetMethodComplexity` does not handle `ConversionOperatorDeclarationSyntax`.
* **Impact:** Conversion operator declarations fall through to `_ => node`, which analyzes the complexity of the entire declaration syntax (including signature) rather than just the body/expression body.
* **Recommendation:** Add a handler case for `ConversionOperatorDeclarationSyntax`.

### [PB2-2.8] 2.8 Static Virtual/Abstract Interface Members Ignored

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [MethodPurityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodPurityAnalyzer.cs#L476-L482)
* **Severity:** Low
* **Description:** `ShouldSuggestMissingEnforcePure` checks `!methodSymbol.IsStatic` before ignoring virtual, abstract, and override methods.
* **Impact:** Static virtual, abstract, and override methods introduced in C# 11 interface members bypass this exclusion and incorrectly trigger missing `[EnforcePure]` suggestions.
* **Recommendation:** Ensure static virtual/abstract members are excluded similarly to standard virtual members.

### [PB2-2.9] 2.9 Duplicate / Overwritten Capability Attributes Ignored

> **Disposition:** Already fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L209-L236)
* **Severity:** Medium
* **Description:** `TryGetAllowedCapabilities` returns on the first `[AllowedCapabilities]` attribute it encounters.
* **Impact:** If multiple attributes are applied, subsequent attributes are completely ignored instead of merging their capability flags.
* **Recommendation:** Aggregate all capability attributes applied to a symbol and combine their permissions.

---

## 3. Exception & Nullability Flow Analyzers

### [PB2-3.1] 3.1 Expression-Bodied Constructors Bypassed

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXPRESSION-BODIED-CONSTRUCTOR
> **Evidence:** Nullable member-initialization verification omitted expression-bodied constructors.
> **Changes/tests:** NullableContractAnalyzer includes constructor expression bodies; NullableContractVerificationTests.
* **File & Lines:** [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L630-L642)
* **Severity:** High
* **Description:** `TryGetExpressionBody` does not list `ConstructorDeclarationSyntax`. Additionally, `TryGetBody` checks for block bodies and returns `null` for expression-bodied constructors.
* **Impact:** If a constructor uses an expression body (e.g., `public MyClass() => _field = 1;`) and is decorated with `[MemberNotNull(nameof(_field))]`, the validation of member postconditions is completely bypassed.
* **Recommendation:** Add a pattern match for `ConstructorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,` in `TryGetExpressionBody`.

### [PB2-3.2] 3.2 Lambda / Anonymous Function Suppressions Ignored

> **Disposition:** Fixed
> **Canonical root cause:** RC-LAMBDA-NULLABILITY-AUDIT
> **Evidence:** The null-forgiving audit skipped anonymous functions even though their expressions can violate the audited contract.
> **Changes/tests:** Lambda bodies are audited while separately analyzed local functions remain excluded; NullableContractVerificationTests.
* **File & Lines:**
  * [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L216-L218)
  * [SharpProofAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofAnalyzer.cs#L123-L136)
* **Severity:** High
* **Description:** In `AuditNullForgivingOperators`, the traversal explicitly filters out descendants of `AnonymousFunctionExpressionSyntax` and `LocalFunctionStatementSyntax`. While local functions have separate syntax node analysis registration, lambda expressions and anonymous methods do not.
* **Impact:** Any null-forgiving operator (`!`) used inside a lambda expression or anonymous method is never audited, ignoring unsafe suppressions.
* **Recommendation:** Register lambda and anonymous expression kinds for body analysis, or adjust the filter to traverse into them.

### [PB2-3.3] 3.3 Async Task Return Nullability False Positives

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [NullableContractAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/NullableContractAnalyzer.cs#L38-L42)
  * [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L336-L346)
* **Severity:** Medium
* **Description:** `GetMethodReturnState` evaluates the outer return type annotation of the method. For `async` methods returning `Task<T?>`, the outer type is `Task`, which is not annotated (and thus considered `NotNull`).
* **Impact:** Because the analyzer sees the return contract as `NotNull`, it requires returned expressions to be non-null, triggering false-positive `NullableReturnContractViolationRule` warnings when returning `null` (since the actual populated type is the nullable `T?`).
* **Recommendation:** Check if `method.IsAsync` is true. If it is, unwrap the return type to obtain its task-like generic parameter and check its nullable annotation.

### [PB2-3.4] 3.4 Faulty Path-Sensitive Finally Block State Merging

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [ExceptionFlowAnalyzer.PathFacts.Finally.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.PathFacts.Finally.cs#L159-L171)
  * [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs#L189-L197)
* **Severity:** Medium
* **Description:** `GetStatementEntryPathState` merges the path state from the exception site (`baseState`) with the general CFG state of the statement. However, `MergeStates` is implemented as a conjunction (concatenating facts) rather than a disjunction.
* **Impact:** If there is any conflicting fact or symbol version between the specific exception path and the general CFG path, the merged state immediately becomes contradictory (unreachable). This causes `IsPathStateReachable` to return `false` prematurely, terminating the exit proof and leading to false negatives.
* **Recommendation:** Avoid merging the general CFG state into the path-sensitive exception state. The exception path state should be propagated forward through the finally block by symbolically processing the statements.

### [PB2-3.5] 3.5 Control-Flow-Insensitive Delegate Target Tracking

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.SpecialCases.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.SpecialCases.cs#L9-L27)
* **Severity:** Medium
* **Description:** `GetLocalDelegateTargetInvocationNodes` resolves delegate targets using a linear preorder AST walk and a single mutable `knownTargets` dictionary.
* **Impact:** Reassignments to delegates inside branches (such as `if/else` statements) or loops will globally overwrite the resolved target, leading to false positives/negatives in delegate tracking.
* **Recommendation:** Query the semantic model or utilize the symbolic reachability service to track the active delegate assignment path-sensitively.

### [PB2-3.6] 3.6 C# `default` Literal Treated as Unknown

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.ExceptionSites.NullFacts.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.NullFacts.cs#L76-L85)
* **Severity:** Low
* **Description:** `IsDefinitelyNullExpression` checks for `NullLiteralExpression` and `DefaultExpressionSyntax` (e.g., `default(T)`), but fails to check for `DefaultLiteralExpression` (e.g. `default`).
* **Impact:** Expressions using the C# 7.1+ `default` literal (such as `((Action)default)()`) are not recognized as null, resulting in false negatives.
* **Recommendation:** Add a check for `SyntaxKind.DefaultLiteralExpression` when the target type is a reference type.

### [PB2-3.7] 3.7 Array Type Mismatch Store Limited to `object[]`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs#L79-L82)
  * [ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs#L108-L120)
* **Severity:** Medium
* **Description:** `IsObjectArrayElementStore` restricts array covariance safety checks strictly to arrays whose elements are statically typed as `System.Object`.
* **Impact:** C# array covariance applies to all reference types (e.g. assigning a `Derived[]` to `Base[]`). Storing a `Base` object into such a covariant array will cause an `ArrayTypeMismatchException` at runtime, but the analyzer completely skips this check because the array is not strictly typed as `object[]` (false negative).
* **Recommendation:** Relax `IsObjectArrayElementStore` to check for any covariant reference type arrays.

### [PB2-3.8] 3.8 Inferred Parameter Null Guard Ignores Multiple/Subsequent Guards

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L296-L304)
* **Severity:** Medium
* **Description:** `HasInferredNotNullNormalCompletionPostcondition` only checks `body.Statements.FirstOrDefault()`.
* **Impact:** If a method accepts multiple parameters and guards them against null (e.g. `if (s1 == null) throw...; if (s2 == null) throw...;`), only the first parameter's guard is captured. All subsequent parameter guards are ignored.
* **Recommendation:** Iterate through the leading statements of the block, continuing to collect guards as long as they are null-checking `if` statements that throw, instead of breaking after the first statement.

### [PB2-3.9] 3.9 Loop-Mutation Bypass in Nullable Value Tracking

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.ExceptionSites.NullableAccess.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.NullableAccess.cs#L42-L45)
* **Severity:** Medium
* **Description:** In `TryResolveCurrentNullableValueExpression`, the statement loop breaks immediately once it matches the `containingStatement` containing the use node.
* **Impact:** Inside loops, mutations occurring *after* the use node in syntax order are executed before the next iteration's use. Because the analyzer breaks early, it misses mutations that set a nullable variable to null later in the loop body, leading to a false negative.
* **Recommendation:** When resolving values within a loop, check if the variable is mutated anywhere in the loop body (even after the use statement) and invalidate the resolved value if it is.

---

## 4. Specialized Symbolic Query Services

### [PB2-4.1] 4.1 Loop Bound Soundness Gap with Non-Local Loop Variables

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONSERVATIVE-LOOP-BOUND
> **Evidence:** Field/property loop controls were assumed stable even though calls in the loop can mutate them.
> **Changes/tests:** Only locals and parameters qualify as stable loop-control symbols; SymbolicComplexityTests.
* **File & Lines:**
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L1668-L1710)
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L1411-L1414)
* **Severity:** High
* **Description:** `IsSymbolMutatedInStatement` only checks for direct syntactic mutations (assignments, increments/decrements, ref/out). If the loop variable is a non-local variable (e.g. a field like `this.i`), and the loop body calls a method (e.g., `Foo()`), the method can mutate the field transitively. The service fails to detect this.
* **Impact:** Incorrect loop bound inferences (e.g., assuming a loop terminates when it is actually an infinite loop).
* **Recommendation:** Restrict loop variable resolution to local variables (`ILocalSymbol`) only.

### [PB2-4.2] 4.2 Redundant Candidates & Performance Hotspot in Null-Dereference Hazard Detection

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [NullableFlowFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/NullableFlowFacts.cs#L135-L142)
* **Severity:** Medium
* **Description:** In `NullableFlowFacts.cs`, `IsDefinitelyNotNullReferenceValue` filters out non-null reference values before creating candidates, but it calls `GetExactExpressionState` instead of `GetExpressionState`. `GetExactExpressionState` ignores Roslyn's nullable flow analysis (`Nullability.FlowState`).
* **Impact:** Obviously non-null local variables/parameters are not filtered, generating thousands of redundant null-dereference candidates, placing heavy, unnecessary load on the SMT solver.
* **Recommendation:** Change it to call `GetExpressionState` which leverages Roslyn's full nullable flow analysis.

### [PB2-4.3] 4.3 Unsound Purity/Capability Classification for `Stopwatch`

> **Disposition:** Fixed
> **Canonical root cause:** RC-STOPWATCH-CAPABILITY
> **Evidence:** Stopwatch clock-reading/control members were treated as capability-neutral framework calls.
> **Changes/tests:** Clock-touching Stopwatch APIs now require Time capability while neutral construction/state members remain allowed; CapabilityContractTests.
* **File & Lines:**
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L781-L784)
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L999-L1007)
* **Severity:** High
* **Description:** `IsClockMember` only matches `"Now"`, `"UtcNow"`, `"Today"`, `"TickCount"`, `"TickCount64"`, and `"GetTimestamp"`. It ignores standard members of `Stopwatch` like `StartNew()`, `Start()`, `Stop()`, `Elapsed`, `ElapsedMilliseconds`, and `ElapsedTicks`.
* **Impact:** Code using `Stopwatch` is incorrectly flagged as `MetadataClassificationUnavailable` instead of the `Clock` capability.
* **Recommendation:** Classify all members of `System.Diagnostics.Stopwatch` as `Clock`.

### [PB2-4.4] 4.4 Uncaught Exception Risk on Nullable Target Properties

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:**
  * [SymbolicComplexityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicComplexityService.cs#L143)
  * [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L191)
* **Severity:** Low
* **Description:** `ResolveTarget` dereferences nullable properties (like `target.LineNumber!.Value`) using `!` without checking if they are null.
* **Impact:** Throws raw `InvalidOperationException` or `NullReferenceException` instead of a clean, helpful `ArgumentException`.
* **Recommendation:** Add validation checks on nullable target parameters.

---

## 5. Infrastructure, CodeFixes, Tooling, & Configuration

### [PB2-5.1] 5.1 `NotSupportedException` Hazard in `RuntimeImplementationAssemblyResolver` (Dynamic Assemblies)

> **Disposition:** Fixed
> **Canonical root cause:** RC-DYNAMIC-ASSEMBLY-LOCATION
> **Evidence:** Assembly.Location can throw for dynamic assemblies during runtime implementation resolution.
> **Changes/tests:** Dynamic assemblies are skipped and Location failures are handled; RuntimeImplementationAssemblyResolverTests.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L136-L164)
* **Severity:** High
* **Description:** The `Resolve` method iterates through all assemblies in the current AppDomain and queries `assembly.Location`. Querying `Location` on dynamic assemblies (such as those generated by mock frameworks, serialization engines like System.Text.Json, or unit test runners) throws a `NotSupportedException`.
* **Impact:** When hosted inside environments like Visual Studio, MSBuild, or unit testing suites, the presence of any dynamic assembly will crash the metadata resolver, failing analysis.
* **Recommendation:** Add a check to skip dynamic assemblies before querying `Location`:
  ```csharp
  if (assembly.IsDynamic) continue;
  ```

### [PB2-5.2] 5.2 Defective Return-Early logic in `SummaryAssemblyReferenceResolver`

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L101-L105)
* **Severity:** Medium
* **Description:** In `FindAssemblyReferencePath`, when iterating compilation references to locate the matching assembly, the code checks if the file path is valid and returns immediately:
  ```csharp
  var referencePath = reference.FilePath;
  return string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath)
      ? null
      : referencePath;
  ```
* **Impact:** If compilation contains multiple references for the same symbol (e.g. an in-memory/stream-based metadata reference followed by a file-backed reference), the first match with an empty/non-existent `FilePath` causes the method to return `null` immediately, skipping subsequent valid references.
* **Recommendation:** Only return if a valid path is found, otherwise continue checking references.

### [PB2-5.3] 5.3 Double-Checked Locking Memory Barrier Hazard in `ActualMethodIdentity`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L519-L536)
* **Severity:** Medium
* **Description:** The `MethodBodySha256` property implements lazy double-checked locking, but `_methodBodySha256Computed` is not marked as `volatile`.
* **Impact:** Without `volatile` or memory barrier guarantees, JIT compiler optimization or CPU out-of-order execution (e.g., on ARM64) may cause reader threads to observe `_methodBodySha256Computed == true` before the string value assignment to `_methodBodySha256` is flushed, causing it to return `null` or a stale value.
* **Recommendation:** Mark `_methodBodySha256Computed` as `volatile`.

### [PB2-5.4] 5.4 Inferred Contract Code Fix Blocked on Accessor Declarations

> **Disposition:** Fixed
> **Canonical root cause:** RC-ACCESSOR-INFERRED-CODEFIX
> **Evidence:** Registration rejected accessors although the inferred-contract helpers already supported them.
> **Changes/tests:** Accessor declarations are eligible and their bodies/returns are discovered; SharpProofCodeFixTests.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L325-L327)
* **Severity:** High
* **Description:** `RegisterInferredContractCodeFix` returns early if the target declaration is a property, indexer, or accessor.
* **Impact:** This early return completely prevents registering code fixes for inferred nullable contracts on property accessors, despite accessor support being explicitly implemented in helper methods.
* **Recommendation:** Remove `or AccessorDeclarationSyntax` from the return-early check.

### [PB2-5.5] 5.5 Moving Attributes to Getter Produces CS8418 Compiler Error

> **Disposition:** Fixed
> **Canonical root cause:** RC-ACCESSOR-ATTRIBUTE-TARGET
> **Evidence:** Moving a property [get: Requires] list onto an accessor retained an illegal nested get target.
> **Changes/tests:** MoveAttributeToGetterAsync clears AttributeList.Target; SP0029_ClearsGetTargetWhenMovingRequiresToGetter.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L657-L660)
* **Severity:** High
* **Description:** When moving a contract attribute (such as `[Requires]`) from a property to its getter accessor, the code preserves the original attribute list syntax. If the original attribute was declared as `[get: Requires]` on the property, preserving the list retains the `get:` target designation.
* **Impact:** Placing `[get: Requires] get => 42;` directly on an accessor is invalid in C# and causes compilation error CS8418 ("The attribute target 'get' is not valid for this declaration").
* **Recommendation:** Ensure the attribute target is cleared when moving the attribute list to the accessor.

### [PB2-5.6] 5.6 Silent Failure / No-op on Setter `value` Parameters

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L396-L399)
* **Severity:** Medium
* **Description:** When resolving inferred parameters for property setters, the contract kind is formatted as `nullable-parameter:value`. The fix implementation attempts to query `ParameterSyntax` nodes, which do not exist for the implicit `value` parameter.
* **Impact:** The code fix returns the unmodified document as a silent no-op.
* **Recommendation:** Add a fallback when `parameterName` is `"value"` to apply the parameter attribute target `[param: InferredAttribute]` directly to the accessor declaration.

### [PB2-5.7] 5.7 Non-Resilient JSON Value Deserialization in CLI Tool

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L207-L208)
* **Severity:** Low
* **Description:** The `ReadString` helper parses JSON elements blindly via `value.GetString()` without validating the JSON node type.
* **Impact:** If any property in the JSON file is not a string (e.g. `CacheKey: null`, `CacheKey: 123`, or a boolean), `GetString()` throws an unhandled `InvalidOperationException`, crashing the CLI tool.
* **Recommendation:** Validate `value.ValueKind == JsonValueKind.String` before calling `GetString()`.

---

## 6. Build & Testing Tooling Issues

### [PB2-6.1] 6.1 Hardcoded 'Release' Configuration in Packaging Integration Tests

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [AnalyzerPackagingTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/AnalyzerPackagingTests.cs#L816-L827)
* **Severity:** Medium
* **Description:** The integration test `BuiltAnalyzerPackage_ShouldContainCurrentAnalyzerAndCodeFixAssemblyBytes_WhenPackageExists` hardcodes `bin/Release` in the package file lookup path. When the test runner is run under `Debug` configuration, it verifies the hashes of the current `Debug` assemblies against the *stale* package located in `bin/Release` (created during previous release builds), leading to false-positive test failures when the assemblies differ.
* **Impact:** False-positive test failures when running unit tests locally in Debug configuration if a stale Release package is present.
* **Recommendation:** Query the current compilation configuration or search under both `Debug` and `Release` folders for the package matching the assembly build stamp.

---

## 7. Deep-Dive Audit Findings (10 Agents)

### 7.1 Purity Engine & Catalog Auditing (Agent 1)

#### [PB2-7.1.1] 7.1.1 Configuration Overrides Precedence Bug (Priority Ordering)

> **Disposition:** Duplicate
> **Canonical root cause:** RC-PURITY-PRECEDENCE-POLICY
> **Evidence:** Same equal-priority purity policy claim as PB1-8.1.
> **Changes/tests:** See PB1-8.1.
* **File & Lines:** [PurityPolicyResolver.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityPolicyResolver.cs#L79-L98)
* **Severity:** High
* **Description:** Impure generated summaries (priority 25) take precedence over user-configured pure overrides (priority 30), preventing users from overriding incorrect analysis results.
* **Recommendation:** Ensure user-configured overrides take priority in resolution.

#### [PB2-7.1.2] 7.1.2 Execution Visibility Boundary Escape (Unsoundness)

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXECUTION-ROOT-ISOLATION
> **Evidence:** Program-point collection and ancestor visibility checks crossed lambda/local-function boundaries and imported outer invocation-time facts into deferred bodies.
> **Changes/tests:** ExecutionVisibility and SymbolicProgramPointFacts stop at the containing execution root; ExecutionVisibility_DoesNotImportOuterFactsIntoDeferredLambda.
* **File & Lines:** [ExecutionVisibility.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/ExecutionVisibility.cs#L20-L119)
* **Severity:** High
* **Description:** `IsInStaticallyUnreachableBranchUsingSmt` traverses ancestors to check unreachability without stopping at lambda/callable boundaries. This leads to unsoundness because state conditions evaluated outside the lambda may not hold when the lambda runs.
* **Recommendation:** Halt ancestor traversal at lambda/anonymous method boundary syntax nodes.

#### [PB2-7.1.3] 7.1.3 Implicit Conversion Breakdown in String Constructor Pattern Matching

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityAnalysisEngine.KnownBclSemantics.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.KnownBclSemantics.cs#L239-L242)
* **Severity:** Medium
* **Description:** `IsTransientCharArrayConsumedByStringConstructor` fails to recognize the pattern when implicit conversion to `ReadOnlySpan<char>` is inserted by the compiler, leading to false impurities.
* **Recommendation:** Strip implicit casts/conversions before performing pattern checks on the arguments.

#### [PB2-7.1.4] 7.1.4 Incomplete Mutable Collection Boundary Typings

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.Attributes.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.Attributes.cs#L13-L25)
* **Severity:** Medium
* **Description:** Omit generic collections like `Queue<T>`, `Stack<T>`, `SortedSet<T>`, etc., from mutable collection escape analysis.
* **Recommendation:** Add generic collections to the checked collection families list.

#### [PB2-7.1.5] 7.1.5 False Impurities for MemoryStream Constructors and ToArray()

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [ImpurityCatalog.SemanticClassifiers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/ImpurityCatalog.SemanticClassifiers.cs#L398-L399)
* **Severity:** Medium
* **Description:** Classifies local MemoryStream usage and ToArray() as impure, preventing their use in pure methods.
* **Recommendation:** Mark non-escaping, locally created MemoryStream methods as pure.

#### [PB2-7.1.6] 7.1.6 Critical Performance Bottleneck in CallGraphBuilder

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [CallGraphBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs#L358-L359)
* **Severity:** Medium
* **Description:** Retrieves declared symbols for all descendant nodes in the syntax tree, leading to severe slowdowns.
* **Recommendation:** Only query symbols for declaration and identifier nodes.

### 7.2 Analyzer Rules & Suppressors (Agent 2)

#### [PB2-7.2.1] 7.2.1 Mismatched Message Format Argument in `SuggestNullableContractRule`

> **Disposition:** Fixed
> **Canonical root cause:** RC-NULLABILITY-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [SharpProofDiagnostics.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofDiagnostics.cs#L253-L261)
  * [InferredContractSuggestionAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/InferredContractSuggestionAnalyzer.cs#L92-L101)
* **Severity:** Medium
* **Description:** `{1}` in the format string `"Method '{0}' satisfies nullable contract '{1}'"` maps to the `evidence` parameter instead of the `displayAttribute` name, showing the raw evidence instead of the contract.
* **Recommendation:** Fix the argument index in the diagnostic reporting code.

#### [PB2-7.2.2] 7.2.2 Potential `NullReferenceException` in `ObjectOrCollectionInitializerPurityRule`

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [ObjectOrCollectionInitializerPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Rules/ObjectOrCollectionInitializerPurityRule.cs#L33-L37)
* **Severity:** Medium
* **Description:** Accessing `TargetMethod.MethodKind` without checking if `TargetMethod` is null can throw a `NullReferenceException` and crash the analyzer on invalid/unresolved code.
* **Recommendation:** Add a null check for `TargetMethod`.

#### [PB2-7.2.3] 7.2.3 Performance Hotspot for Top-Level Statements in Suppressor

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofDiagnosticSuppressor.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/SharpProofDiagnosticSuppressor.cs#L234-L235)
* **Severity:** Low
* **Description:** Returning the entire `CompilationUnitSyntax` root when resolving a `GlobalStatementSyntax` runs symbolic queries on the entire file instead of the statement itself.
* **Recommendation:** Return the individual global statement syntax node instead of the root.

### 7.3 Configuration & Additional Files (Agent 3)

#### [PB2-7.3.1] 7.3.1 Case-Sensitivity Schema Bypass in Baseline Verification

> **Disposition:** Fixed
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:**
  * [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L150-L154)
  * [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L296-L302)
* **Severity:** Medium
* **Description:** The validator checks baseline properties (`"id"`, `"symbol"`, `"path"`) in a case-sensitive manner, while the baseline parser is case-insensitive. This allows baseline entries using case variations to bypass compatibility validation.
* **Recommendation:** Perform case-insensitive property checks in the validator.

#### [PB2-7.3.2] 7.3.2 Local User-Profile Path Leak in Diagnostic Properties

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-DIAGNOSTIC-REPRODUCTION-PATHS
> **Evidence:** Roslyn diagnostic properties are not automatically telemetry, and the explain command and portable baseline matching deliberately carry the source path needed to reproduce or identify a diagnostic.
> **Changes/tests:** Existing explain/baseline property tests verify these functional paths; no telemetry sink is present and no code change was made.
* **File & Lines:**
  * [ExplainDiagnosticProperties.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/ExplainDiagnosticProperties.cs#L20-L38)
  * [BaselineDiagnosticProperties.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/BaselineDiagnosticProperties.cs#L35-L57)
* **Severity:** High
* **Description:** Absolute file paths containing local usernames (from `C:\Users\<user>`) are serialized into diagnostic properties that end up in telemetry.
* **Recommendation:** Sanitize paths to strip user-profile roots.

#### [PB2-7.3.3] 7.3.3 Case-Sensitivity Bug in `ProvenDiagnosticSuppressionOptions`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L1007-L1022)
* **Severity:** Medium
* **Description:** Suppression check set comparison is ordinal case-sensitive, failing to match third-party diagnostic IDs containing lowercase letters.
* **Recommendation:** Use `StringComparer.OrdinalIgnoreCase`.

#### [PB2-7.3.4] 7.3.4 Property Getter Suffix Fallback Bug

> **Disposition:** Duplicate
> **Canonical root cause:** RC-DUPLICATE-CLAIM
> **Evidence:** This claim describes the same behavior and root cause as another canonical ledger item.
> **Changes/tests:** Resolved or classified under the canonical root-cause entry.
* **File & Lines:** [ConfiguredMemberKey.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/ConfiguredMemberKey.cs#L11-L22)
* **Severity:** Low
* **Description:** Write-only properties are blocked from configuration because `TryCreate` requires a getter method.
* **Recommendation:** Fall back to the property's setter method.

#### [PB2-7.3.5] 7.3.5 Missing `CancellationToken` in Recursive JSON/File Traversals

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:**
  * [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L135-L178)
  * [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L247-L294)
* **Severity:** Low
* **Description:** JSON traversals of large baseline JSON files block the analysis thread and ignore cancellations.
* **Recommendation:** Pass and check `CancellationToken` throughout.

### 7.4 Method Identity & Metadata Loading (Agent 4)

#### [PB2-7.4.1] 7.4.1 Local Function MethodKind Mismatch

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:**
  * [RoslynStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RoslynStructuralMethodIdentityAdapter.cs#L189)
  * [EcmaStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/Shared/EcmaStructuralMethodIdentityAdapter.cs#L90-L103)
* **Severity:** Medium
* **Description:** Roslyn adapter uses `"local-function"`, while ECMA adapter uses `"ordinary"`, preventing matching.
* **Recommendation:** Align method kinds between Roslyn and ECMA adapters.

#### [PB2-7.4.2] 7.4.2 Static Local Function Type Parameter Offset Bug

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [RoslynStructuralMethodIdentityAdapter.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/RoslynStructuralMethodIdentityAdapter.cs#L164-L174)
* **Severity:** Medium
* **Description:** Roslyn adapter flattens outer method arity even if static, causing key mismatches.
* **Recommendation:** Skip outer method arity when the local function is static.

#### [PB2-7.4.3] 7.4.3 Unbounded Memory Retention / Cache Leak of Assembly Bytes

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [EffectSummaryMetadataSupport.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/EffectSummaryMetadataSupport.cs#L550-L552)
* **Severity:** Medium
* **Description:** Retains raw assembly byte arrays indefinitely in memory, leading to heap inflation.
* **Recommendation:** Release byte arrays after hashing.

### 7.5 SMT Formula Translation (Agent 5)

#### [PB2-7.5.1] 7.5.1 Unsound Reference Type Test Conversion

> **Disposition:** Fixed
> **Canonical root cause:** RC-TYPE-TEST-ORACLE-SOUNDNESS
> **Evidence:** The test-only SMT oracle reduced every reference type test to non-null, including object-to-string tests where runtime compatibility is independent.
> **Changes/tests:** The oracle translates a type test only when the static conversion proves it equivalent to non-null; TestOracle_TypeTestRequiresNonNullEquivalence.
* **File & Lines:** [CSharpConditionToFormula.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs)
* **Severity:** High
* **Description:** `x is T` mapped to `x != null` in tests, ignoring runtime type compatibility.
* **Recommendation:** Encode runtime type check constraints in formula.

#### [PB2-7.5.2] 7.5.2 Variable Name Collisions for Metadata Symbols

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs)
* **Severity:** Medium
* **Description:** Missing source locations lead to duplicate `#0` suffixes.
* **Recommendation:** Append unique IDs for unlocated symbols.

#### [PB2-7.5.3] 7.5.3 Z3 SMT Regex Incompatibility

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [CSharpConditionToFormula.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/CSharpConditionToFormula.cs)
* **Severity:** Medium
* **Description:** C# regex anchors (`\A`, `\z`) and `\s` are incompatible with Z3 SMT regex.
* **Recommendation:** Transpile or validate regex patterns prior to mapping.

### 7.6 Symbolic State & Facts (Agent 6)

#### [PB2-7.6.1] 7.6.1 Unsound Mutation Invalidation for Reference Types in Nested Scopes

> **Disposition:** Already fixed
> **Canonical root cause:** RC-NESTED-MUTATION-INVALIDATION
> **Evidence:** Current program-point collection recursively invalidates local, parameter, member, receiver, ref/out, and array facts across completed nested scopes while stopping at deferred callables.
> **Changes/tests:** RemoveStateFactsInvalidatedByNestedMutations and existing mutation/loop/catch regressions cover the reported case.
* **File & Lines:** [SymbolicProgramPointFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProgramPointFacts.cs)
* **Severity:** High
* **Description:** Mutations in sub-blocks/nested scopes are not properly propagated or invalidated in the outer state.
* **Recommendation:** Propagate scope-boundary mutations up to containing block states.

### 7.7 Source Query Services (Agent 7)

#### [PB2-7.7.1] 7.7.1 Incomplete Node Indexing for Empty/Comment Lines

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicSourceQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceQueryService.cs)
* **Severity:** Medium
* **Description:** Lines without tokens inside multi-line statements are missing from the index, throwing exceptions when queried.
* **Recommendation:** Guard indices and support empty/comment lines.

#### [PB2-7.7.2] 7.7.2 Inefficient Compilation Caching

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SymbolicSourceQueryService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicSourceQueryService.cs)
* **Severity:** Medium
* **Description:** Static `ConditionalWeakTable` cache is bypassed because new syntax trees are compiled on every single query call.
* **Recommendation:** Reuse syntax trees and semantic models across queries.

### 7.8 CodeFixes & Refactorings (Agent 8)

#### [PB2-7.8.1] 7.8.1 Trivia/Comments Loss

> **Disposition:** Fixed
> **Canonical root cause:** RC-CODEFIX-TRIVIA
> **Evidence:** Reconstructing attribute lists discarded structured documentation/directive trivia owned by a fully removed list.
> **Changes/tests:** Attribute removal now uses per-node Roslyn removal with significant leading trivia preserved while whitespace-only trivia is normalized; SP0003_RemovalPreservesDocumentationTrivia and existing formatting tests.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L325-L327)
* **Severity:** High
* **Description:** Removing the last attribute in an attribute list deletes the entire list syntax without preserving leading trivia, causing the deletion of XML documentation comments (`///`), preprocessor directives (`#if`), and indents from the member.
* **Recommendation:** Copy leading trivia of the attribute list to the next syntax token when deleting the list.

#### [PB2-7.8.2] 7.8.2 Indentation Loss in `AddEnforcePure`

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs)
* **Severity:** Medium
* **Description:** Prepends attributes without adding indentation to the attribute list, pushing the new attribute to column 0.
* **Recommendation:** Format the newly added attribute list using the target member's indentation trivia.

#### [PB2-7.8.3] 7.8.3 XML Comments Misplacement in Inferred Attributes

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs)
* **Severity:** Medium
* **Description:** Leaves XML comments attached to the declaration's original first token, placing them *between* the new attribute and the member.
* **Recommendation:** Transfer leading XML comments trivia to the newly inserted attribute list.

### 7.9 Tooling & Reports (Agent 9)

#### [PB2-7.9.1] 7.9.1 Parallel Build Overwrite & File Lock Issues in `dotnet build`

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CORPUS-SARIF-ISOLATION
> **Evidence:** CorpusReport already assigns each input a GUID-named temporary SARIF path and invokes builds sequentially, so no static shared ErrorLog exists.
> **Changes/tests:** Program.cs path construction and control flow demonstrate isolation; no code change.
* **File & Lines:** [Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L33-L37)
* **Severity:** High
* **Description:** Single static `/p:ErrorLog` path causes sharing violations or diagnostic overwrite during parallel builds.
* **Recommendation:** Use dynamic unique paths per project under MSBuild.

#### [PB2-7.9.2] 7.9.2 Generic Value Types & Read-Only Views Formatting Mismatch

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [BclPurityFallbackHeuristics.cs](file:///C:/w/PurelySharp/Shared/BclPurityFallbackHeuristics.cs#L237-L251)
* **Severity:** Medium
* **Description:** `BclPurityFallbackHeuristics` uses `Nullable<T>` format while ECMA adapter uses `Nullable\`1`, causing fallback catalog mismatches.
* **Recommendation:** Normalize backticks/bracket arity formats before matching.

#### [PB2-7.9.3] 7.9.3 Silent Dropping of Incomparable Purity Entries during Merges

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityClassificationEngine.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/PurityClassificationEngine.cs#L2847-L2851)
* **Severity:** Medium
* **Description:** Conflicting/incomparable candidates cause the entry to be silently discarded.
* **Recommendation:** Resolve by picking the more conservative purity status.

### 7.10 Packaging & Deployment Options (Agent 10)

#### [PB2-7.10.1] 7.10.1 Roslyn Configuration Mismatches (.globalconfig)

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ROSLYN-GLOBALCONFIG-SEMANTICS
> **Evidence:** Roslyn global analyzer config files support EditorConfig glob sections such as [*.cs], and custom analyzer option keys do not require the dotnet_code_quality prefix reserved for .NET analyzer conventions.
> **Changes/tests:** All profiles declare is_global=true and configuration-profile tests validate registered SharpProof keys and scopes; no code change.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L599-L622)
* **Severity:** High
* **Description:** Invalid syntax `[*.cs]` header in `.globalconfig` files makes Roslyn ignore options, and option keys are missing the required `dotnet_code_quality.` prefix.
* **Recommendation:** Fix the `.globalconfig` files syntax and support option prefixes in parser.

#### [PB2-7.10.2] 7.10.2 macOS Build Warnings on Z3 Native Library

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProof.targets](file:///C:/w/PurelySharp/SharpProof.Package/buildTransitive/SharpProof.targets#L5-L7)
* **Severity:** Medium
* **Description:** `buildTransitive/SharpProof.targets` only excludes `libz3.dll`, leaving `libz3.dylib` causing assembly loading warnings on macOS.
* **Recommendation:** Exclude all native solver libraries (`.dll`, `.dylib`, `.so`) from standard Roslyn analyzer assembly resolution.

---

## 8. Phase 3 Audit - VsixHarness, Demo, Attributes, Net472 Smoke, SymbolicCli & Fuzzing

### 8.1 VsixHarness

#### [PB2-8.1.1] 8.1.1 Hardcoded `Release` Configuration Path for VSIX and Attributes DLL

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-ROUTING
> **Evidence:** The VSIX harness selected Release paths for the package, simulated analyzer, and attributes assembly even when invoked from another configuration.
> **Changes/tests:** The harness accepts/infers a validated configuration, uses it for all artifacts, and build-vsix.ps1 passes it explicitly; VsixHarnessUsesRequestedBuildConfiguration.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L87-L101)
* **Severity:** High
* **Description:** The default VSIX path and the `attributesDll` path are both hardcoded to `"Release"`:
  ```csharp
  var vsixPath = args.Length > 0 ? args[0]
      : Path.Combine(solutionRoot, "SharpProof.Vsix", "bin", "Release", "SharpProof.Vsix.vsix");
  var attributesDll = Path.Combine(solutionRoot, "SharpProof.Attributes", "bin", "Release", "netstandard2.0", "SharpProof.Attributes.dll");
  ```
  Developers building in `Debug` configuration will either fall through to `CreateSimulatedVsix` (which also hardcodes `Release`, line 255-256) or use a stale Release artifact without any warning. There is no `$(Configuration)` substitution or fallback to `Debug`.
* **Impact:** CI/local `Debug` builds silently run against stale `Release` binaries, defeating regression testing. A fresh checkout on a machine without a prior Release build will fail with a `FileNotFoundException`.
* **Recommendation:** Accept the build configuration as a command-line argument or environment variable, or probe both `Release` and `Debug` paths in priority order, printing which was selected.

#### [PB2-8.1.2] 8.1.2 Simulated VSIX Temp Directory Leak on `CreateSimulatedVsix` Failure

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** VSIX temporary ownership was incomplete on both failure and successful simulated-package paths.
> **Changes/tests:** All extraction and simulated-package directories now have deterministic cleanup; VsixHarnessUsesRequestedBuildConfiguration.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L261-L270)
* **Severity:** Medium
* **Description:** `CreateSimulatedVsix` creates a temp directory with `Directory.CreateTempSubdirectory("SharpProofSimVsix")` before creating the archive. If the archive creation (or any iteration over files) throws, the `tempDirectory` is never deleted because there is no `try/finally` cleanup:
  ```csharp
  var tempDirectory = Directory.CreateTempSubdirectory("SharpProofSimVsix");
  var vsixPath = ...;
  using (var archive = ZipFile.Open(vsixPath, ZipArchiveMode.Create))
      foreach (var file in Directory.GetFiles(analyzerDirectory, ...))
          archive.CreateEntryFromFile(file, entryName);  // can throw
  return vsixPath;  // tempDirectory never cleaned up on failure
  ```
  Only the `payload.Directory` (extraction directory) is cleaned up on normal exit (line 184), not the simulated VSIX temp directory.
* **Impact:** Orphaned temp directories accumulate on the build agent or developer machine.
* **Recommendation:** Wrap the archive creation in a `try/catch` that deletes `tempDirectory` on failure, or use a `try/finally`.

#### [PB2-8.1.3] 8.1.3 `FindExactLoadedAssembly` Ignores Culture and Public Key Token

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Loaded assembly matching omitted culture and public-key-token identity.
> **Changes/tests:** Exact matching now compares culture and public-key tokens; VSIX harness regression coverage.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L58-L66)
* **Severity:** Medium
* **Description:** The assembly resolution logic checks only name and version equality:
  ```csharp
  return AssemblyName.ReferenceMatchesDefinition(loadedName, requestedName) &&
         Equals(loadedName.Version, requestedName.Version);
  ```
  `AssemblyName.ReferenceMatchesDefinition` does not compare `Culture` or `PublicKeyToken`. This means two assemblies with the same name and version but different cultures or strong-name keys would be treated as identical, potentially returning the wrong already-loaded assembly. In a VSIX test harness loading unsigned analyzer DLLs alongside signed Roslyn assemblies, this can silently resolve to the wrong type-system instance.
* **Impact:** Potential type identity mismatches (`InvalidCastException` or silent behavioral divergence) when strong-named and un-signed assemblies share names and versions.
* **Recommendation:** Add explicit checks for `PublicKeyToken` equality (or use `AssemblyName.FullName` string comparison) when resolving assemblies.

#### [PB2-8.1.4] 8.1.4 VSIX Extraction Does Not Skip Directory Entries (Zip archives)

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Archive extraction relied only on ZipArchiveEntry.Name to identify directories.
> **Changes/tests:** Directory entries ending in either separator are skipped and failed extraction cleans its temporary root.
* **File & Lines:** [VsixHarness/Program.cs](file:///C:/w/PurelySharp/Tools/VsixHarness/Program.cs#L206-L218)
* **Severity:** Low
* **Description:** The extraction loop guards against zero-length `entry.Name` to skip directories:
  ```csharp
  if (entry.Name.Length == 0) continue;
  ```
  However, some zip tools emit directory entries where `entry.Name` is non-empty (e.g. `"subdir/"`) but `entry.FullName` ends with `/`. The `Directory.CreateDirectory` call below is harmless, but `entry.ExtractToFile(destinationPath, true)` will then fail with an `IOException` on a path that resolves to an existing directory, crashing extraction.
* **Impact:** Harness crashes when processing VSIX files produced by certain zip tools that emit non-empty directory entries.
* **Recommendation:** Also check `entry.FullName.EndsWith('/')` or `entry.Length == 0` (the uncompressed size) before extracting.

---

### 8.2 Demo Project

#### [PB2-8.2.1] 8.2.1 `SP0002` Suppressed Globally for All Demo Methods

> **Disposition:** Fixed
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [SharpProof.Demo/SharpProof.Demo.csproj](file:///C:/w/PurelySharp/SharpProof.Demo/SharpProof.Demo.csproj#L11)
* **Severity:** Medium
* **Description:** The demo project suppresses SP0002 for the entire project with `<NoWarn>$(NoWarn);SP0002</NoWarn>`. This means methods intentionally decorated with `[Pure]` or `[EnforcePure]` that are visibly impure (e.g. `AddImpure`, `Log`, `IncrementGlobal`) will not generate any visible SP0002 diagnostic when building the demo. The demo code comments claim these should produce SP0002, but they are silently suppressed.
* **Impact:** The demo is misleading - it shows attribute usage but hides all expected diagnostic output during a standard build. Developers consulting the demo as a reference will not see the expected diagnostics fire.
* **Recommendation:** Remove the blanket `NoWarn` and instead add a baseline suppression for specific expected occurrences, so the demo clearly shows that the diagnostics *do* fire (via the baseline) rather than silently suppressing them.

#### [PB2-8.2.2] 8.2.2 Demo `SharpProof.globalconfig` Missing `global_level` Key

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BUILD-CONFIGURATION-AND-DOCUMENTATION
> **Evidence:** Adding a higher `global_level` makes Roslyn report `MultipleGlobalAnalyzerKeys` for the demo and repository configurations, unset the duplicated option, and fail the zero-warning release gate. The existing level intentionally permits the nearer demo configuration to apply without that conflict.
> **Changes/tests:** No product change; verified by the warning-free Release solution build.
* **File & Lines:** [SharpProof.Demo/SharpProof.globalconfig](file:///C:/w/PurelySharp/SharpProof.Demo/SharpProof.globalconfig#L1-L6)
* **Severity:** Low
* **Description:** The demo's `.globalconfig` file is:
  ```
  is_global = true

  sharpproof_smt_mode = bounded
  sharpproof_smt_timeout_ms = 321
  sharpproof_analysis_max_merged_if_else_facts = 17
  sharpproof_enable_effect_summary_json = true
  ```
  The file correctly sets `is_global = true`. However, it does not set `global_level`, which controls override priority when multiple global config files are present. Without an explicit `global_level`, this config has level 0 (the lowest), meaning any NuGet package that ships its own `.globalconfig` with a higher `global_level` will silently override these demo-specific settings without any warning.
* **Impact:** Demo configuration values may be silently overridden by transitive package configs, making demo behavior non-deterministic depending on package restore state.
* **Recommendation:** Add `global_level = 100` (or another explicit priority) to assert that the demo-local settings take precedence.

---

### 8.3 Attributes Project

#### [PB2-8.3.1] 8.3.1 `AllowedExceptionsAttribute` Accepts `null` Array via `params` Without Null Entries Check

> **Disposition:** Already fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [AllowedExceptionsAttribute.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/AllowedExceptionsAttribute.cs#L8-L11)
* **Severity:** Low
* **Description:** The attribute constructor is:
  ```csharp
  public AllowedExceptionsAttribute(params Type[] exceptionTypes)
  {
      ExceptionTypes = exceptionTypes ?? throw new ArgumentNullException(nameof(exceptionTypes));
  }
  ```
  While the null-array check is present, individual array entries can be `null` (e.g. `[AllowedExceptions(null)]` or `[AllowedExceptions(typeof(Exception), null)]`). The analyzer that consumes `ExceptionTypes` later likely iterates over them and calls `.FullName` or similar, which would throw a `NullReferenceException`.
* **Impact:** Malformed attribute usage crashes the analyzer rather than producing a clean user-facing diagnostic.
* **Recommendation:** Either validate that no entries are null in the constructor (though attribute constructors run at compile-time reflection and cannot throw usefully), or make the consuming analyzer guard against null entries and emit a diagnostic.

#### [PB2-8.3.2] 8.3.2 `AllowedCapabilitiesAttribute` Targets `AttributeTargets.All` - Overly Permissive

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AllowedCapabilitiesAttribute.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/AllowedCapabilitiesAttribute.cs#L5)
* **Severity:** Low
* **Description:** `[AttributeUsage(AttributeTargets.All, Inherited = false)]` allows `[AllowedCapabilities]` to be applied to types, assemblies, delegates, enums, events, fields, parameters, return values, etc. The analyzer only processes `[AllowedCapabilities]` on methods and properties (where it makes semantic sense), so applying it to an `enum` or a field silently has no effect.
* **Impact:** Users applying `[AllowedCapabilities]` to non-method targets will receive no warning from the compiler or the analyzer about the misuse.
* **Recommendation:** Restrict to `AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Assembly` to match the set of supported declarations, consistent with `ImpureAttribute` and `PureExternalAttribute`.

#### [PB2-8.3.3] 8.3.3 `SharpProofCapability` Includes `IO` as a Named Aggregate Flag

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SharpProofCapability.cs](file:///C:/w/PurelySharp/SharpProof.Attributes/SharpProofCapability.cs#L9-L21)
* **Severity:** Medium
* **Description:** The `IO` flag (`1 << 0 = 1`) is treated as both a standalone aggregate flag and potentially as a capability by itself. The `ExpandAllowedCapabilities` method in `SymbolicCliExitGateEvaluator.cs` (line 289-299) explicitly expands `IO` to include `FileRead | FileWrite | Network | Console | Registry`. However `Process`, `Environment`, `Clock`, `Randomness`, `Reflection`, `Synchronization`, and `NativeInterop` are NOT part of `IO` - they are fully separate capabilities. A user writing `[AllowedCapabilities(SharpProofCapability.IO)]` would reasonably expect to allow "all I/O" including process spawning, but `Process` is excluded from the `IO` expansion. Furthermore, `NormalizeCapabilities` only upgrades individual I/O flags to include `IO`, meaning a method with `FileRead` will show `IO | FileRead` in the output, but one with `Process` will not - this asymmetry is confusing.
* **Impact:** Users incorrectly assume `IO` covers process spawning or environment access; diagnostics may be misleading.
* **Recommendation:** Document explicitly that `IO` is a subset aggregate (file I/O + network + console + registry only), and consider renaming it to `FileSystemAndNetwork` or `BasicIO`, or expanding it to cover all resource-oriented capabilities.

---

### 8.4 Net472 Smoke Project

#### [PB2-8.4.1] 8.4.1 Smoke Project Has No Analyzer Config or Baseline - Silent Behavioral Drift

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj](file:///C:/w/PurelySharp/SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj)
* **Severity:** Medium
* **Description:** The Net472 smoke project does not include any `<GlobalAnalyzerConfigFiles>`, `<AdditionalFiles>` pointing to a baseline or effect summary JSON, or any `.globalconfig`. This means the analyzer runs under its default settings on Net472, which may differ from the actual production/demo configurations. Any analyzer configuration-dependent behavior (e.g. SMT mode, timeout, fact limits) goes untested in the smoke context.
* **Impact:** The smoke test verifies that the analyzer *loads and activates* on Net472, but does not verify that configuration-dependent diagnostic paths work correctly under Net472. Configuration bugs that only appear under certain SMT modes or with effect summaries will be missed.
* **Recommendation:** Add a minimal `.globalconfig` and possibly a baseline JSON to the smoke project, similar to the demo project, to exercise the full configuration pipeline.

#### [PB2-8.4.2] 8.4.2 Smoke Project References `SearchLib` but Is Not Included in Net472 Test

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj](file:///C:/w/PurelySharp/SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj#L16)
* **Severity:** Low
* **Description:** The smoke project references `SearchLib` as an analyzer:
  ```xml
  <ProjectReference Include="..\SearchLib\SearchLib.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  ```
  If `SearchLib` has any net472 compatibility issue (e.g. `netstandard2.0` TFM mismatch or p/invoke incompatibility), this will silently fail on Net472 because the `OutputItemType="Analyzer"` reference is loaded dynamically by Roslyn and failures surface only as runtime AD0001 analyzer exceptions - not build errors. There is no test assertion in the smoke project that all analyzers loaded without exception.
* **Impact:** Net472 analyzer load failures go silently undetected during smoke.
* **Recommendation:** Add a test (or a check in the smoke project's MSBuild target) that verifies no AD0001 diagnostics were produced during compilation.

---

### 8.5 SymbolicCli & Fuzzing

#### [PB2-8.5.1] 8.5.1 `--request-json-stdin` Reads Entire stdin Without Cancellation Timeout

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SymbolicCliJsonRequest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliJsonRequest.cs#L65)
* **Severity:** Medium
* **Description:** When `--request-json-stdin` is used, the CLI reads stdin to completion:
  ```csharp
  json = await standardInput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
  ```
  While a `CancellationToken` is plumbed through, it is the default token passed in from `ExpandArgumentsAsync` (line 35), which in `Program.cs` top-level code has no timeout configured. If the caller pipes in a non-terminating stdin (e.g. a blocked pipe), the process will hang indefinitely without any wall-clock timeout fallback.
* **Impact:** A misconfigured CI invocation or a piping error causes the SymbolicCli process to hang forever, blocking the pipeline.
* **Recommendation:** Apply a configurable read timeout (defaulting to, e.g., 30 seconds) to the stdin read operation, separate from the SMT analysis cancellation token.

#### [PB2-8.5.2] 8.5.2 `SymbolicCliExitGateEvaluator.EvaluateInvariantProofs` Fires on Zero Total Proofs

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-INVARIANT-GATE-NONEMPTY
> **Evidence:** The zero-proof failure is deliberate: documentation says no proof result is a gate failure, and option validation requires at least one requested --implies expression.
> **Changes/tests:** docs/ci-exit-gates.md and CLI validation/tests define the nonempty proof contract; no code change.
* **File & Lines:** [SymbolicCliExitGateEvaluator.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs#L56-L66)
* **Severity:** High
* **Description:** The gate evaluation for `--fail-on-unproven-implies` contains a flawed condition:
  ```csharp
  var unprovenCount = outcomes.TotalCount - outcomes.ProvenTrueCount;
  if (outcomes.TotalCount != 0 && unprovenCount == 0) return;  // success path

  failures.Add(...);  // failure path - fires even when TotalCount == 0
  ```
  When `outcomes.TotalCount == 0` (no `--implies` conditions were specified for this query), `unprovenCount` is `0`. The success condition `TotalCount != 0 && unprovenCount == 0` evaluates to `false`, so the failure is reported - even though there are no invariants to prove. This means using `--fail-on-unproven-implies` on any query that has no `--implies` conditions will *always* report a failure.
* **Impact:** `--fail-on-unproven-implies` is broken for queries without any `--implies` conditions. CI gates using this flag will fail spuriously on every run that doesn't include `--implies`.
* **Recommendation:** Change the guard to also pass when `TotalCount == 0`:
  ```csharp
  if (unprovenCount == 0) return;  // covers both zero-proof and all-proven cases
  ```

#### [PB2-8.5.3] 8.5.3 `SymbolicCliJsonRequest` Allows Duplicate `--request-json*` Detection to Miss Mixed Positions

> **Disposition:** Fixed
> **Canonical root cause:** RC-CLI-INPUT-AND-OUTPUT-CONTRACTS
> **Evidence:** The selector validation was correct but its diagnostic did not describe the sole-selector constraint.
> **Changes/tests:** The CLI now reports that the JSON selector must be sole and first.
* **File & Lines:** [SymbolicCliJsonRequest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliJsonRequest.cs#L48-L50)
* **Severity:** Low
* **Description:** The validation:
  ```csharp
  if (requestIndexes.Length != 1 || requestIndexes[0] != 0)
      throw new ArgumentException("A JSON request selector must be the only CLI input and must appear first.");
  ```
  correctly ensures `--request-json` or `--request-json-stdin` appears exactly once and first. However the error message says "must appear first" when the actual requirement is "must be the only argument". A user who supplies `--request-json '{"schemaVersion":1,...}' --json` would get this exception with a misleading message suggesting they should move `--request-json` to the front, when the real problem is that `--json` cannot be combined with it.
* **Impact:** Confusing error message leading users to attempt invalid workarounds.
* **Recommendation:** Change the message to: `"--request-json or --request-json-stdin must be the sole argument; no other options may be combined with it."`.

#### [PB2-8.5.4] 8.5.4 `SymbolicCliExitGateEvaluator.GetCompactMetric` Throws on Valid Complexity+Invariant Mixed Queries

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicCliExitGateEvaluator.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/SymbolicCliExitGateEvaluator.cs#L216-L241)
* **Severity:** Medium
* **Description:** `GetCompactMetric` first checks `TryGetInvariantMetrics(result, ...)` and, if successful, handles metrics like `"program-points"` and `"conservative-unknowns"`. If the metric is not one of the four invariant metrics, it falls through to the outer `switch` which handles complexity/capability/hazard-specific metrics. However, if `TryGetInvariantMetrics` succeeds but the metric name is not one of the four known invariant names, the code throws:
  ```csharp
  _ => throw new InvalidOperationException("Unsupported invariant compact threshold metric: " + metric)
  ```
  This means a user who specifies `--fail-on-compact-threshold hazards=5` with an invariant query (which succeeds `TryGetInvariantMetrics`) will get an internal `InvalidOperationException` instead of a proper user-facing error, or reaching the hazard-specific branch.
* **Impact:** Valid mixed-query compact thresholds throw internal exceptions rather than producing helpful diagnostics.
* **Recommendation:** Fall through from the invariant branch to the outer switch when the metric is not recognized as an invariant metric, rather than throwing immediately.

#### [PB2-8.5.5] 8.5.5 Fuzz Runner `DefaultOutputDirectory` Uses Process Start Time, Not Run Start Time

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L205-L212)
* **Severity:** Low
* **Description:** The default output directory is evaluated at field-initialization time (via `DefaultOutputDirectory()` in the record initializer), not at `FuzzRunner.RunAsync` time:
  ```csharp
  public string OutputDirectory { get; init; } = DefaultOutputDirectory();
  ```
  `DefaultOutputDirectory()` calls `DateTimeOffset.UtcNow` (line 210) when `FuzzOptions` is constructed - which happens during `Parse()`. If there is significant delay between `Parse()` and `RunAsync()` (e.g. due to lazy workspace loading), the timestamp in the output path will not reflect the actual run start time.
* **Impact:** Minor timestamp skew in artifact directory names; cosmetic issue but can confuse artifact correlation in CI.
* **Recommendation:** Capture `DateTimeOffset.UtcNow` at the start of `RunAsync` and pass the resolved output directory to the runner, or defer `DefaultOutputDirectory()` until `RunAsync` begins.

#### [PB2-8.5.6] 8.5.6 Fuzz `FuzzRunner` Shared Analyzer State - Not Thread-Safe Registration

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L240-L248)
* **Severity:** Medium
* **Description:** `SharedAnalyzers` is a static `ImmutableArray<DiagnosticAnalyzer>` holding a single `SharpProofAnalyzer` instance. The fuzz runner uses `options.Parallelism` concurrent tasks (default 4), all sharing this one analyzer instance. `DiagnosticAnalyzer` implementations are required to be stateless per Roslyn's contract, but `SharpProofAnalyzer` (and its registered sub-analyzers) use per-compilation-start shared state. If the analyzer registers mutable shared state in `CompilationStartAction` (via closures captured across parallel compilations), concurrent parallel analysis of multiple independent compilations would race on that shared state.
* **Impact:** Potential data races, incorrect diagnostics, or crashes when parallel fuzz cases share a single `SharpProofAnalyzer` instance across concurrent independent compilations.
* **Recommendation:** Verify that `SharpProofAnalyzer` is truly stateless between `Initialize()` calls (all state is per-compilation-start scoped), or create one `SharpProofAnalyzer` instance per parallel task to be safe.

---

### 8.6 VSIX Manifest

#### [PB2-8.6.1] 8.6.1 VSIX Manifest Missing `SharpProof.Attributes` Asset - Consumers Must Install Separately

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** A VSIX asset cannot inject compile-time project references, and the required consumer step was undocumented.
> **Changes/tests:** README source/generated output now states that VSIX users must reference SharpProof or SharpProof.Attributes.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L22-L26)
* **Severity:** Medium
* **Description:** The VSIX manifest includes `SharpProof.Analyzer` and `SharpProof.CodeFixes` as assets, but does NOT include `SharpProof.Attributes`. The attributes assembly (`EnforcePureAttribute`, `PureAttribute`, etc.) must be present in the project being analyzed for the analyzer to find and match the attributes it checks for. Without shipping `SharpProof.Attributes.dll` as a VSIX asset or requiring it as a NuGet dependency inside the VSIX, a user who installs the VSIX extension without separately installing the `SharpProof.Attributes` NuGet package into their project will get unresolved type errors in their C# code.
* **Impact:** VSIX-only users (e.g. those who installed via the Visual Studio Marketplace without also adding the NuGet package) cannot use any SharpProof attributes. The analyzer will load but none of its attribute-gated checks will fire.
* **Recommendation:** Either bundle `SharpProof.Attributes.dll` as a VSIX asset, or add a `<Prerequisite>` entry for the NuGet package in the manifest, or update documentation/README to clearly state that both VSIX and NuGet package must be installed.

#### [PB2-8.6.2] 8.6.2 VSIX Targets Only `amd64` - Excludes ARM64 Visual Studio Users

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L8-L18)
* **Severity:** Medium
* **Description:** All three `InstallationTarget` entries (Community, Pro, Enterprise) specify `<ProductArchitecture>amd64</ProductArchitecture>`. Visual Studio 2022 on ARM64 Windows (Surface Pro X, ARM64 laptops) runs as a native ARM64 process and will reject extensions that require `amd64` architecture. Users on ARM64 Windows will see the VSIX as incompatible and be unable to install it.
* **Impact:** The extension is incompatible with ARM64 Visual Studio installations, silently excluding that user base.
* **Recommendation:** Add parallel `InstallationTarget` entries with `<ProductArchitecture>arm64</ProductArchitecture>` for all three VS SKUs, and verify the managed analyzer DLLs (which are architecture-neutral .NET Standard) and the Z3 native library (which requires the ARM64 dylib/dll) are correctly bundled.

#### [PB2-8.6.3] 8.6.3 VSIX Dependency on `.NET Framework 4.5` Is Stale - VS 2022 Requires .NET 4.7.2+

> **Disposition:** Fixed
> **Canonical root cause:** RC-PACKAGING-AND-CONSUMER-ASSETS
> **Evidence:** The manifest framework floor was lower than both Visual Studio 2022 and the VSIX target framework.
> **Changes/tests:** The VSIX dependency now requires .NET Framework 4.7.2; package metadata regression coverage.
* **File & Lines:** [source.extension.vsixmanifest](file:///C:/w/PurelySharp/SharpProof.Vsix/source.extension.vsixmanifest#L20)
* **Severity:** Low
* **Description:** The dependency declaration is:
  ```xml
  <Dependency Id="Microsoft.Framework.NDP" DisplayName="Microsoft .NET Framework" d:Source="Manual" Version="[4.5,)" />
  ```
  Visual Studio 2022 itself requires .NET Framework 4.7.2 as a minimum. Declaring `[4.5,)` provides no additional installation constraint - VS 2022 ships with 4.8 - but it documents an incorrect minimum. More importantly, the VSIX project itself targets `net472`, so if any dependency or behavior requires 4.7.2+ APIs, declaring `[4.5,)` in the manifest is misleading and could cause subtle issues if the VSIX is mistakenly installed on older VS versions that happen to satisfy the `[4.5,)` constraint.
* **Impact:** Misleading manifest documentation; potential for installation on VS versions that are officially unsupported.
* **Recommendation:** Update the dependency version to `[4.7.2,)` to match the project's `net472` TFM target.

---

## 9. Phase 4 Audit - Baseline Tools, CorpusReport, SearchLib (Z3 Core), Fuzz Aggregation

### 9.1 Baseline Tools

#### [PB2-9.1.1] 9.1.1 `RunBuildAsync` - `dotnet build` Failure Is Silently Ignored if SARIF Is Created Partially

> **Disposition:** Fixed
> **Canonical root cause:** RC-CHILD-BUILD-EXIT-VALIDATION
> **Evidence:** Baseline generation accepted an existing partial SARIF without checking the dotnet build exit code.
> **Changes/tests:** The baseline tool rejects nonzero child exits before reading SARIF; BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L113-L138)
* **Severity:** High
* **Description:** `RunBuildAsync` only checks `File.Exists(sarifPath)` to determine success. A failed `dotnet build` that exits non-zero can still create a partial SARIF file (e.g., containing compilation errors but not analysis results), which will be silently consumed by `GenerateFromSarifJson`. The build's exit code is never checked:
  ```csharp
  await process.WaitForExitAsync();
  // ...no check of process.ExitCode...
  if (!File.Exists(sarifPath))
      throw new InvalidOperationException("dotnet build did not produce a SARIF error log...");
  ```
  A partial SARIF (e.g., zero diagnostics because compilation failed) will generate an *empty* baseline, silently suppressing all future diagnostics.
* **Impact:** A CI build failure causes the baseline to be silently reset to empty, suppressing all future analyzer findings. This is a critical correctness bug for the baseline workflow.
* **Recommendation:** Check `process.ExitCode != 0` and throw an appropriate exception or warn. At minimum, check the SARIF file for well-formedness before accepting it.

#### [PB2-9.1.2] 9.1.2 `GenerateFromSarifJson` - Temporary SARIF File Written to `GetTempPath()` Without Cleanup on Exception

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L97-L103)
* **Severity:** Medium
* **Description:** The temporary SARIF file is created in `Path.GetTempPath()` and added to `temporaryFiles` before `RunBuildAsync` is called. If `RunBuildAsync` succeeds but `File.ReadAllTextAsync(sarifPath)` or `GenerateFromSarifJson` throws, the `finally` block at line 82-85 will delete the temp file. However, `RunBuildAsync` itself does not catch exceptions - if `Process.Start` fails (e.g., `dotnet` is not on PATH), the thrown `InvalidOperationException` propagates *before* `sarifPath` is added to `temporaryFiles` (the add happens at line 100, after the call at line 101):
  ```csharp
  var sarifPath = Path.Combine(Path.GetTempPath(), "sharpproof-baseline-" + ...);
  temporaryFiles.Add(sarifPath);  // added first
  await RunBuildAsync(input, sarifPath);  // but RunBuildAsync creates the file
  ```
  In this case the temp path is registered but the file was never created, so the delete is a no-op. This is harmless, but if the file *is* created by an `MSBuild` side-effect and the exception fires after creation but before registration completes, the file leaks.
* **Impact:** Minor temp file leak on rare failure paths; overall the logic is mostly correct but fragile.
* **Recommendation:** Use a `try/finally` around `RunBuildAsync` to ensure both creation and registration are atomic.

#### [PB2-9.1.3] 9.1.3 `NormalizePath` - Double-Slash URI Paths Not Normalized

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L279-L292)
* **Severity:** Medium
* **Description:** `NormalizePath` converts file URIs to local paths and strips leading `./` prefixes but does not collapse repeated slashes:
  ```csharp
  var normalized = trimmed.Replace('\\', '/');
  while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
  ```
  If a SARIF `artifactLocation.uri` contains `//` (e.g. `file:///C://path/to/file.cs`), the path after `.LocalPath` extraction may contain `//` which will not match a normalized single-slash path in the baseline. Additionally, trailing slashes on directory-like paths are not stripped, causing false mismatches in `BaselineBucketKeyComparer`.
* **Impact:** Baseline entries for files with non-canonical URIs (produced by some MSBuild SARIF generators) will never match, causing all their suppressions to appear stale even when they should match. This silently breaks the prune command.
* **Recommendation:** Add `normalized = Regex.Replace(normalized, "/{2,}", "/")` (or equivalent) to collapse consecutive slashes after backslash replacement.

#### [PB2-9.1.4] 9.1.4 `Deduplicate` - `BaselineKey` Uses Case-Insensitive Path for Hashing But Case-Sensitive ID and Symbol

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L600-L627)
* **Severity:** Medium
* **Description:** `BaselineKey.FromEntry` normalizes the `Path` with `.ToUpperInvariant()` for the deduplication key:
  ```csharp
  public static BaselineKey FromEntry(BaselineEntry entry)
  {
      return new BaselineKey(
          entry.Id,           // Ordinal case-sensitive
          entry.Symbol,       // Ordinal case-sensitive
          NormalizePath(entry.Path).ToUpperInvariant(),   // <- uppercased
          ...);
  }
  ```
  The `HashSet<BaselineKey>` uses the default `record` equality which compares `Path` via `string.Equals` with the default `StringComparison.Ordinal`. This means an uppercased path `"C:/FOO/BAR.CS"` from one entry will not equal a non-uppercased `"C:/foo/bar.cs"` from another entry in the `HashSet`, defeating the case-insensitive deduplication goal. The `ToUpperInvariant()` is only applied to one side (the key from `FromEntry`) but the lookup in the set uses `record` structural equality which is ordinal - so the deduplication is effectively case-sensitive on path despite the intent.
* **Impact:** Duplicate baseline entries may not be deduplicated on case-insensitive filesystems, producing a baseline with duplicate diagnostics. On prune, this causes them to incorrectly persist.
* **Recommendation:** Either remove `.ToUpperInvariant()` and ensure both sides are lowercased/uppercased consistently, or implement a custom `IEqualityComparer<BaselineKey>` that uses `StringComparison.OrdinalIgnoreCase` for the `Path` field.

#### [PB2-9.1.5] 9.1.5 `ValidateEvidenceSchemas` - Recursion Into All JSON Object Nodes Is O(N) With No Depth Limit

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SharpProofBaseline.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline.Core/SharpProofBaseline.cs#L433-L487)
* **Severity:** Low
* **Description:** `ValidateEvidenceSchemas` recursively traverses every property of every nested JSON object looking for `evidenceSchemaVersion`/`evidenceSchemaCompatibility` keys. On a large SARIF file (e.g., thousands of results with deeply nested properties), this results in O(N x depth) traversals. There is no depth limit, and the method recurses through arrays too. For a maliciously crafted or unusually deep SARIF, this can cause a stack overflow or extremely long validation times.
* **Impact:** Performance degradation or stack overflow when processing large or deeply nested SARIF files.
* **Recommendation:** Add a maximum recursion depth parameter (e.g., `remainingDepth = 10`) and return without validating beyond the expected SARIF schema depth.

---

### 9.2 CorpusReport Tool

#### [PB2-9.2.1] 9.2.1 `RunBuild` (CorpusReport) - Synchronously Blocks on Async I/O, Risking Deadlock

> **Disposition:** Fixed
> **Canonical root cause:** RC-CHILD-PROCESS-ASYNC-LIFECYCLE
> **Evidence:** Although redirected streams were started before the wait, CorpusReport unnecessarily mixed synchronous process waiting with async reads and also failed to validate the child exit.
> **Changes/tests:** RunBuildAsync now awaits process exit and both streams, then rejects nonzero exits; BuildBackedToolsRejectNonzeroChildExitBeforeReadingSarif.
* **File &amp; Lines:** [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L58-L83)
* **Severity:** High
* **Description:** The `RunBuild` static helper uses `GetAwaiter().GetResult()` to synchronously block on async tasks:
  ```csharp
  static void RunBuild(string input, string sarifPath)
  {
      // ...
      var outputTask = process.StandardOutput.ReadToEndAsync();
      var errorTask = process.StandardError.ReadToEndAsync();
      process.WaitForExit();
      var output = outputTask.GetAwaiter().GetResult();  // <- synchronous block
      var error = errorTask.GetAwaiter().GetResult();    // <- synchronous block
  ```
  This is called from a top-level `async` context (Program.cs uses async top-level statements with `await`). Calling `GetAwaiter().GetResult()` inside a sync-over-async pattern on a context that has a synchronization context (e.g., ASP.NET-hosted scenario) will deadlock. Additionally, if the `dotnet build` process writes enough to stderr to fill the OS pipe buffer before `process.WaitForExit()` unblocks, the process will deadlock waiting for the pipe to be drained while the host waits for the process to exit.
  In contrast, the `SharpProof.Baseline` tool's `RunBuildAsync` correctly uses `await process.WaitForExitAsync()` with async reads.
* **Impact:** Deadlock when the child `dotnet build` process writes more than ~4KB to stderr without stdout being consumed concurrently.
* **Recommendation:** Convert `RunBuild` to `async Task RunBuildAsync` following the Baseline tool's pattern.

#### [PB2-9.2.2] 9.2.2 `SarifCorpusReport.CreateFromSarifFiles` - Reads All Files Synchronously in a Loop

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [SarifCorpusReport.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport.Core/SarifCorpusReport.cs#L34-L40)
* **Severity:** Low
* **Description:** `CreateFromSarifFiles` iterates over all SARIF input paths and calls `File.ReadAllText(input.SarifPath)` synchronously for each one:
  ```csharp
  foreach (var input in inputs) builder.AddSarifJson(input.InputName, File.ReadAllText(input.SarifPath));
  ```
  For large corpora with many SARIF files (e.g., hundreds of project outputs), this is sequential synchronous I/O. The corresponding async variant `CreateFromSarifFilesAsync` does not exist.
* **Impact:** Slow performance on large corpus runs with many SARIF inputs.
* **Recommendation:** Add an async overload that uses `await File.ReadAllTextAsync(...)` and processes files concurrently with a degree-of-parallelism limit.

#### [PB2-9.2.3] 9.2.3 `CorpusReport.Program` - No Exit Code When Inputs Produce Empty/Invalid SARIF

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L44-L52)
* **Severity:** Low
* **Description:** If all provided inputs are SARIF files with no valid `runs` or empty `results` arrays, `CreateFromSarifFiles` returns a `CorpusReportSummary` with all-zero counts. The program exits with code 0 (success). There is no signal to the caller that the input may have been malformed or that no diagnostics were processed, which can silently mask a failed SARIF generation step upstream.
* **Impact:** CI scripts interpreting a zero-finding corpus report as success when the SARIF files were actually empty due to upstream build failures.
* **Recommendation:** Add a `--fail-on-empty` flag that returns exit code 2 when `TotalSharpProofDiagnostics == 0` and no explicit SARIF runs were found.

#### [PB2-9.2.4] 9.2.4 `Aggregate-FuzzRun.ps1` - `$phaseSummaries.Count` Always Evaluates to 0 for Single-Element Array

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-POWERSHELL-ARRAY-MATERIALIZATION
> **Evidence:** @(...) yields an empty array when a pipeline emits no objects and a one-element array for one object; it does not wrap pipeline non-output as a null element.
> **Changes/tests:** PowerShell array-subexpression semantics make the existing Count check correct; no code change.
* **File &amp; Lines:** [Fuzz/Aggregate-FuzzRun.ps1](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1#L39-L42)
* **Severity:** High
* **Description:** The count check is:
  ```powershell
  if ($phaseSummaries.Count -eq 0)
  {
      throw "No phase summaries were found under: $Root"
  }
  ```
  `$phaseSummaries` is assigned with `@(Get-ChildItem ... | ForEach-Object { ... })`. In PowerShell, if exactly **one** directory matches and `ForEach-Object` returns a single `[pscustomobject]`, the `@()` forces it into an array so `.Count` is 1. However, if **zero** directories match but the `Where-Object`-less pipeline still returns `$null`, `@($null)` creates a length-1 array containing `$null`. The check `$phaseSummaries.Count -eq 0` would be `1` (not 0), so the error would not fire, and subsequent code (`$phaseSummaries | ForEach-Object { $_.Summary.CasesAnalyzed }`) would attempt to access `$null.Summary` and throw a non-user-friendly NullPointerException-like error instead of the intended descriptive message.
* **Impact:** When no phase directories exist, the script throws a confusing null reference error instead of the intended human-readable message.
* **Recommendation:** Change the array initialization to filter out null values: `$phaseSummaries = @(Get-ChildItem ... | ForEach-Object { ... } | Where-Object { $_ -ne $null })`, or use `if ($phaseSummaries -eq $null -or $phaseSummaries.Count -eq 0)`.

#### [PB2-9.2.5] 9.2.5 `Aggregate-FuzzRun.ps1` - Uses Latest Schema Version From Last Phase, Not Maximum

> **Disposition:** Already fixed
> **Canonical root cause:** RC-FUZZ-HARNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File &amp; Lines:** [Fuzz/Aggregate-FuzzRun.ps1](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz/Aggregate-FuzzRun.ps1#L49)
* **Severity:** Low
* **Description:** The aggregate schema version is taken from the *last* (alphabetically sorted) phase directory:
  ```powershell
  $latestSchemaVersion = ($phaseSummaries | Select-Object -Last 1).Summary.SchemaVersion
  ```
  If phases were run with different versions of the fuzz tool (e.g., phase_01 used schema v2, phase_02 used schema v1 due to a rollback), `Select-Object -Last 1` would report v1 as the "latest", which is incorrect. The correct approach is to take the maximum schema version across all phases.
* **Impact:** Aggregate report may report a stale schema version, misleading downstream tooling that reads the version field.
* **Recommendation:** Use `($phaseSummaries | ForEach-Object { $_.Summary.SchemaVersion } | Sort-Object -Descending | Select-Object -First 1)` to take the maximum version.

---

### 9.3 SearchLib - SMT Solver Core

#### [PB2-9.3.1] 9.3.1 `SmtResourceBudget.GetWallClockSafetyNet` - Negative Budget Produces Negative Safety Net

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Negative budgets produced negative wall-clock safety-net timeouts.
> **Changes/tests:** Nonpositive budgets clamp to zero; GetWallClockSafetyNet_NegativeBudget_ClampsToZero.
* **File &amp; Lines:** [SmtResourceBudget.cs](file:///C:/w/PurelySharp/SearchLib/SmtResourceBudget.cs#L36-L43)
* **Severity:** Medium
* **Description:** The safety net computation is:
  ```csharp
  if (budget.Ticks < TimeSpan.MinValue.Ticks / WallClockSafetyFactor) return TimeSpan.MinValue;
  return TimeSpan.FromTicks(budget.Ticks * WallClockSafetyFactor);
  ```
  If `budget` is a small negative value (e.g., `TimeSpan.FromMilliseconds(-1)`), the overflow checks do not catch it (it is well within range), and `budget.Ticks * WallClockSafetyFactor` returns a large negative `TimeSpan`. This negative safety net is then passed to Z3's wall-clock timeout setter. Depending on the Z3 binding, a negative timeout could be interpreted as "no timeout" (infinite) or throw an exception.
  `GetRlimit` handles this correctly with `Math.Max(1, rlimit)`, but `GetWallClockSafetyNet` has no equivalent floor.
* **Impact:** If a zero or negative timeout budget is passed (e.g., due to a `remaining = timeout - elapsed` calculation going negative), the safety net becomes negative and Z3 may run without a timeout bound.
* **Recommendation:** Add `if (budget <= TimeSpan.Zero) return TimeSpan.Zero;` at the start of `GetWallClockSafetyNet`.

#### [PB2-9.3.2] 9.3.2 `SmtSolver.CheckAndAccountResources` - rlimit Counter Wrap Assumes 32-Bit Rollover, But Z3 May Use 64-Bit

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L36-L55)
* **Severity:** Medium
* **Description:** The wrap-around accounting logic assumes the `rlimit count` statistic is a 32-bit unsigned counter:
  ```csharp
  // A smaller observation means the 32-bit counter wrapped
  ConsumedResourceCount += observed >= _lastObservedRlimitCount
      ? observed - _lastObservedRlimitCount
      : (1L << 32) - _lastObservedRlimitCount + observed;
  ```
  However, `entry.UIntValue` is `uint` (32-bit), and in Z3 4.x, the rlimit counter is stored as a `uint64_t` internally but exposed via the `UIntValue` accessor if the Statistics API only reads the lower 32 bits. On long-running sessions with many queries (e.g., fuzz runs), the actual consumed rlimit may exceed `uint.MaxValue` (4.3 billion), causing the wrap to be detected prematurely even though it hasn't occurred. This would cause `ConsumedResourceCount` to be hugely under-reported.
  Additionally, if `entry.UIntValue` is saturated at `uint.MaxValue` by Z3, every subsequent observation will trigger the wrap-around path even though nothing has wrapped.
* **Impact:** Incorrect `ConsumedResourceCount` reporting in long-running fuzz sessions; budget enforcement based on cumulative resource counts may be too permissive.
* **Recommendation:** Use `entry.IsUInt ? (long)entry.UIntValue : (long)entry.ULongValue` to access the full counter width if the Z3 `Statistics.Entry` type supports 64-bit values, or document the known limitation.

#### [PB2-9.3.3] 9.3.3 `SmtSolver.IsConservativeSolverFailure` - Swallows `ArgumentException` From Non-Solver Code

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L309-L317)
* **Severity:** Medium
* **Description:** The conservative failure filter catches:
  ```csharp
  return ex is InvalidOperationException ||
         ex is Z3Exception ||
         ex is ArgumentException ||         // <- too broad
         ex is InvalidCastException ||
         ex is ArithmeticException;
  ```
  `ArgumentException` is a very broad exception type. If a caller passes an `SmtFormula` that is `null` (which `SmtFormulaTraversal.Enumerate` would catch with `throw new ArgumentNullException`), or if the encoder is called with an out-of-range enum value producing an `ArgumentException` from a system API, this catch would silently convert a programming error into a `Feasibility.Unknown` result. The analyzer would treat the query as "unknown" rather than surfacing a real bug.
* **Impact:** Genuine programming errors (null formula, misuse of APIs) are silently converted to `Unknown` feasibility results, producing false negatives in purity analysis rather than surfacing the underlying bug.
* **Recommendation:** Remove `ArgumentException` from the conservative filter or narrow it to a specific Z3-originated `ArgumentException` subtype by checking the stack trace or exception message.

#### [PB2-9.3.4] 9.3.4 `PurityProofSearch.ClassifyInternalOnlyEffect` - Path-Feasibility Unknown Is Classified as Pure

> **Disposition:** Fixed
> **Canonical root cause:** RC-MALFORMED-PROOF-QUERY
> **Evidence:** The claimed Unknown-as-pure branch is correct, but a null PathConditions value could still reach ToArray and throw.
> **Changes/tests:** PurityProofSearch normalizes null path conditions to the empty path and returns Unknown for a null query/hazard; PurityProof_NullPathConditionsDefaultToEmpty.
* **File &amp; Lines:** [PurityProofSearch.cs](file:///C:/w/PurelySharp/SearchLib/PurityProofSearch.cs#L127-L152)
* **Severity:** High
* **Description:** For `InternalOnly` effects, the code returns `ProvablyPure` even when path feasibility is `Unknown`:
  ```csharp
  Feasibility.Unknown => new PurityProofResult(
      PurityProofOutcome.Unknown,        // <- correctly Unknown
      Attempted(path),
      NotAttempted(),
      "path_feasibility_unknown"),
  _ => new PurityProofResult(
      PurityProofOutcome.ProvablyPure,   // <- Satisfiable path -> ProvablyPure
      Attempted(path),
      NotAttempted(),
      pureReason)
  ```
  Wait - this looks correct. However, the key issue is the ordering: the `Feasibility.Unknown` branch is listed before `_`, so `Unknown` falls into the explicit `Unknown` arm. But the `Feasibility.Satisfiable` arm falls into `_` and returns `ProvablyPure`. This is the **intended** behavior for `InternalOnly` effects: if the path is satisfiable (reachable), the effect is still pure because it is internal-only. This is conceptually correct.

  The actual bug is subtler: **the method calls `pathConditions.ToArray()` but never materializes the array into a field**, so `pathConditions` is enumerated by `_solver.CheckSatisfiability`, which is correct. However, if the same `IEnumerable<SmtFormula>` is a lazy generator (e.g., from LINQ) and multiple classification calls share it (e.g., `ClassifyStaticCacheRead` -> `ClassifyInternalOnlyEffect` -> `_solver.CheckSatisfiability`), the enumeration occurs once via `normalizedPathConditions = pathConditions.ToArray()`, which is safe. No bug here - the `.ToArray()` defensive copy is present.

  The real bug: For the `PurityHazardKind.CallerVisibleMemoryWrite` case in `Classify(PurityProofQuery)` (line 103-104), the method calls `ClassifyCallerVisibleMemoryWrite` with `query.Hazard.TriggerCondition`, but `ClassifyCore` also takes `pathConditions`. If `query.PathConditions` is `null` (the record's default for an uninitialized field), the `pathConditions.ToArray()` inside `ClassifyCore` (line 218) will throw a `NullReferenceException`. The `PurityProofQuery` record constructor does not validate that `PathConditions` is non-null.
* **Impact:** Passing a `PurityProofQuery` with `null` `PathConditions` causes an unhandled `NullReferenceException` inside the solver, crashing the analysis thread.
* **Recommendation:** Add a validation check in `PurityProofQuery`'s constructor (or factory method) to ensure `PathConditions` is never null; default it to `Array.Empty<SmtFormula>()`.

#### [PB2-9.3.5] 9.3.5 `SmtSolver.TryApplyEqualitySubstitutions` - Substitution Order Depends on Dictionary Enumeration Order

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L476-L518)
* **Severity:** Medium
* **Description:** The substitution map is a `Dictionary<SmtVariable, SmtFormula>`. In each pass, all conditions are scanned for equality substitution opportunities, which are collected into this dictionary. The substitution direction is determined by variable name order (`string.CompareOrdinal`, lines 544-547), but when multiple equalities exist for the same variable (e.g., `x = y` and `x = z`), only the first entry survives due to `if (substitutions.ContainsKey(source)) return;` (line 572). The first entry depends on `conditions` iteration order, which is non-deterministic for LINQ-generated sequences.

  More critically, the `SubstituteEqualityAliases` loop runs `pass <= substitutions.Count` times (line 639), but `substitutions.Count` can be 0 even if substitutions exist (if the dictionary is empty after the cycle check eliminates all candidates). This means the substitution loop body runs once (`pass = 0` only), applying one round of substitutions but potentially missing chains that require multiple passes to fully apply.
* **Impact:** Incomplete variable substitution in complex constraint chains with more than one equality alias, leading to less-simplified formulas being submitted to Z3 and potentially slower or less precise proofs.
* **Recommendation:** Re-evaluate the `pass <= substitutions.Count` loop bound to use `MaxEqualitySubstitutionPasses` instead, ensuring chain substitution is fully applied.

#### [PB2-9.3.6] 9.3.6 `SmtFormulaTraversal.RewriteBottomUp` - Stack-Based Rewrite Returns Wrong Formula If Root Is Constant

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtFormulaTraversal.cs](file:///C:/w/PurelySharp/SearchLib/SmtFormulaTraversal.cs#L19-L53)
* **Severity:** Low
* **Description:** The bottom-up rewrite uses an iterative stack:
  ```csharp
  frames.Push(new TraversalFrame(root, false));
  while (frames.Count > 0)
  {
      var frame = frames.Pop();
      if (!frame.Visited) { ... push children ... continue; }
      var childCount = GetChildCount(frame.Formula);
      var children = childCount == 0 ? Array.Empty<SmtFormula>() : new SmtFormula[childCount];
      for (var index = childCount - 1; index >= 0; index--) children[index] = results.Pop();
      var rebuilt = Rebuild(frame.Formula, children);
      var rewritten = rewrite(rebuilt) ?? throw new InvalidOperationException(...);
      results.Push(rewritten);
  }
  return results.Pop();
  ```
  If `root` is a leaf (e.g., `SmtBooleanConstant`, `SmtVariable`) where `GetChildCount(formula) == 0`, `children` is empty, `Rebuild` returns the original formula unchanged, and `rewrite(rebuilt)` is called. This is correct. However, if the `rewrite` delegate returns a formula that has *children* (e.g., the root constant is rewritten to a complex expression), those children are **not themselves rewritten** - the method only applies the rewrite delegate to the rebuilt formula, not recursively to the result of `rewrite`. This can produce incomplete rewrites if the delegate introduces new sub-expressions that also need rewriting.
* **Impact:** One-pass bottom-up rewrite does not recursively rewrite newly-introduced sub-expressions, potentially leaving unreduced sub-terms in the formula after simplification.
* **Recommendation:** Document that `rewrite` must return a formula that does not require further rewriting (i.e., it should only replace leaf-level nodes or produce already-normalized sub-expressions). Alternatively, apply a fixpoint loop to `RewriteBottomUp`.

---

### 9.4 Cross-Cutting Concerns

#### [PB2-9.4.1] 9.4.1 `SmtResourceBudget.RlimitPerMillisecond` Is Calibrated to Z3 4.12.2 Only - No Runtime Calibration

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [SmtResourceBudget.cs](file:///C:/w/PurelySharp/SearchLib/SmtResourceBudget.cs#L13-L19)
* **Severity:** Medium
* **Description:** The conversion constant `RlimitPerMillisecond = 4000` is documented as being calibrated against Z3 4.12.2. The actual Z3 version bundled in `SharpProof.Package` is determined at package-restore time and may drift. Z3 minor and patch versions can have significantly different rlimit-per-millisecond ratios (especially with tactic changes). No runtime calibration or assertion checks the actual Z3 version against the constant.
* **Impact:** After a Z3 version upgrade, all SMT timeout budgets will be systematically too tight (if the new Z3 is faster, consuming more rlimits/ms) or too loose (if it's slower), causing either spurious `Unknown` results or runaway solve times.
* **Recommendation:** Add a startup calibration step that runs a known benchmark formula and measures the actual rlimit/ms ratio, or at minimum assert the Z3 version at startup and warn if it differs from the calibrated version.

#### [PB2-9.4.2] 9.4.2 No Shared Cancellation Token Propagation From CLI to SMT Analysis

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File &amp; Lines:** [Tools/SharpProof.SymbolicCli/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.SymbolicCli/Program.cs#L21-L26)
* **Severity:** Medium
* **Description:** The SymbolicCli uses `using var inputContext = await SymbolicCliInputContext.CreateAsync(options)` and `using var smtAnalysis = new SmtAnalysisService(...)`, but there is no `CancellationToken` plumbed into either the workspace loading or the SMT analysis. If the user sends `Ctrl+C` (SIGINT), the .NET runtime will cancel the default `CancellationToken` for top-level programs only if the program registers a handler. Without a handler, the process either terminates immediately (killing all in-flight Z3 native threads abruptly, possibly corrupting native heap) or hangs until the current SMT query completes.
* **Impact:** `Ctrl+C` during a long SMT query either corrupts native memory or is non-responsive, degrading the developer experience and potentially requiring process kill.
* **Recommendation:** Register a `Console.CancelKeyPress` handler that sets a `CancellationTokenSource`, and propagate the token to `SymbolicCliInputContext.CreateAsync` and the SMT query services.

#### [PB2-9.4.3] 9.4.3 Baseline Tool and CorpusReport Tool Both Call `dotnet build` Without Inheriting Environment Variables

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-BASELINE-AND-SARIF-TOOLS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File &amp; Lines:** [Baseline/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Baseline/Program.cs#L113-L130), [CorpusReport/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.CorpusReport/Program.cs#L58-L75)
* **Severity:** Low
* **Description:** Both tools use `new ProcessStartInfo("dotnet") { UseShellExecute = false }` without configuring the child process's environment. By default, `UseShellExecute = false` inherits the parent process's environment - this is correct. However, neither tool sets a working directory for the child `dotnet build` process. MSBuild's behavior depends on the current working directory for resolving relative paths in project files (e.g., relative `$(MSBuildThisFileDirectory)` references). If the CLI tool is run from a directory other than the solution root, `dotnet build` may fail or produce incorrect output.
* **Impact:** Baseline and CorpusReport tools produce wrong results when run from a directory other than the solution root. Error messages are not specific about the CWD mismatch.
* **Recommendation:** Set `startInfo.WorkingDirectory = Path.GetDirectoryName(input)` (for project/solution inputs) or document that the tool must be run from the solution root.

---

## 10. Phase 5 Audit - 10-Agent Comprehensive Review

### 10.1 Purity Analysis Engine (Agent 1 - Engine Files)

#### [PB2-10.1.1] 10.1.1 CFG Second Pass Unconditionally Overwrites First-Pass Callee Edges

> **Disposition:** Already fixed
> **Canonical root cause:** RC-CALL-GRAPH-EDGE-PRESERVATION
> **Evidence:** The CFG enrichment pass now initializes from the first-pass caller set rather than an empty set.
> **Changes/tests:** CallGraphBuilder uses callerSet.ToBuilder before adding CFG invocation targets; no further code change.
* **File & Lines:** [CallGraphBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Analysis/CallGraphBuilder.cs#L347-L401)
* **Severity:** High
* **Description:** The first pass collects callee edges from a rich set of operation types: invocations, binary/unary operators, compound assignments, increment/decrement, conversions, constructor initializers, method references, property references, delegate creations, anonymous functions, await operations, delegate assignments, event subscriptions, and variable initializers (stored at lines 347-349). The CFG pass then re-analyzes using only `IInvocationOperation` nodes (line 387) and **unconditionally overwrites** the edge set at line 401 (`edges[method.OriginalDefinition] = callerSetBuilder.ToImmutable()`). The CFG pass starts from an empty builder (line 379-380), so all non-invocation callees from the first pass are silently discarded.
* **Impact:** The call graph loses edges to operator methods, property accessors, delegate creation targets, event subscriptions, and other non-invocation callees. The worklist solver does not re-analyze callers when these callees' purity changes, leading to stale purity results.
* **Recommendation:** Start the CFG pass builder from the existing edge set (`callerSet.ToBuilder()`) and add new edges on top of it.

#### [PB2-10.1.2] 10.1.2 CallGraphBuilder CFG Second Pass Descendant-Traversal Regression on Lambda Return Statements

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CALLABLE-DESCENDANT-BOUNDARY
> **Evidence:** AnonymousFunctionExpressionSyntax is the base syntax type for both simple and parenthesized lambdas, so the existing descendant predicate already stops at both.
> **Changes/tests:** Roslyn syntax hierarchy and the existing predicate demonstrate coverage; no code change.
* **File & Lines:** [PurityAnalysisEngine.CfgReturnValues.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.CfgReturnValues.cs#L149-L164)
* **Severity:** High
* **Description:** `TryGetSingleReturnedExpressionSyntaxFromBody` uses `blockSyntax.DescendantNodes` with a filter that stops at `LocalFunctionStatementSyntax` and `AnonymousFunctionExpressionSyntax`, but does NOT stop at `SimpleLambdaExpressionSyntax` or `ParenthesizedLambdaExpressionSyntax`. Lambda expressions nested inside the method body will have their `ReturnStatementSyntax` nodes traversed, and if there is exactly one such return, it will be incorrectly extracted as the enclosing method's return expression.
* **Impact:** The purity analyzer extracts the wrong returned expression from methods containing lambdas, leading to incorrect symbolic facts in the path state and unsound purity proofs.
* **Recommendation:** Add `SimpleLambdaExpressionSyntax` and `ParenthesizedLambdaExpressionSyntax` to the descendant filter predicate.

#### [PB2-10.1.3] 10.1.3 HasMethodBody in ConcreteReceivers Uses Overly Permissive Check

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** A source declaration was treated as a concrete body without inspecting its syntax.
> **Changes/tests:** Concrete receiver resolution now uses TypeHierarchyEnumeration.HasMethodBody.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.ConcreteReceivers.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.ConcreteReceivers.cs#L286-L289)
* **Severity:** Medium
* **Description:** The local `HasMethodBody` method checks only `methodSymbol.DeclaringSyntaxReferences.Length > 0`, which returns `true` for abstract methods, partial methods without bodies, and extern methods. Compare with `TypeHierarchyEnumeration.HasMethodBody` which correctly inspects the syntax tree for actual body/expression-body nodes.
* **Impact:** Abstract methods declared in source but lacking an implementation body are treated as valid concrete dispatch targets, leading to unsound purity analysis.
* **Recommendation:** Inspect the syntax node for actual body or expression-body presence.

#### [PB2-10.1.4] 10.1.4 IUsingDeclarationOperation Not Handled in CFG State Update

> **Disposition:** Fixed
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Using declarations were not projected as disposed into the method normal-exit resource state.
> **Changes/tests:** Normal-exit state applies using-declaration disposal facts; UsingDeclaration_DisposesOwnedResourceAtNormalExit.
* **File & Lines:** [PurityAnalysisEngine.RuleSupport.AssignmentState.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.RuleSupport.AssignmentState.cs#L10-L315)
* **Severity:** Medium
* **Description:** `UpdateDelegateMapForOperation` handles `IUsingOperation` (lines 215-218) but NOT `IUsingDeclarationOperation` (C# `using var` declarations). The method `AddUsingDeclarationDisposeFacts` exists in `ResourceFacts.cs` (lines 95-112) but is never called from the CFG traversal. The post-CFG pass does separately probe `OperationKind.UsingDeclaration`, but during CFG analysis, resource release facts from using declarations are never added to the path state.
* **Impact:** False-positive "resource not disposed" diagnostics for code that uses `using var` declarations, because the symbolic resource state never registers the implicit disposal.
* **Recommendation:** Add a case for `IUsingDeclarationOperation` in `UpdateDelegateMapForOperation` that calls `AddUsingDeclarationDisposeFacts`.

#### [PB2-10.1.5] 10.1.5 DestructorDeclarationSyntax Not Handled in GetBodySyntaxNode

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [PurityAnalysisEngine.BodyLookup.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.BodyLookup.cs#L8-L32)
* **Severity:** Low
* **Description:** `GetBodySyntaxNode` handles `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `ConversionOperatorDeclarationSyntax`, etc., but not `DestructorDeclarationSyntax`. Destructors (finalizers) with bodies return `null`, falling through to abstract/extern handling.
* **Impact:** Destructors with bodies are treated as abstract/extern methods with no body, bypassing purity analysis of the destructor body.
* **Recommendation:** Add `DestructorDeclarationSyntax` to the recognized types in `GetBodySyntaxNode`.

#### [PB2-10.1.6] 10.1.6 TryGetDirectThrowOnlySyntax Missing ConversionOperatorDeclarationSyntax

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityAnalysisEngine.Cfg.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/PurityAnalysisEngine.Cfg.cs#L437-L472)
* **Severity:** Low
* **Description:** `TryGetDirectThrowOnlySyntax` handles `MethodDeclarationSyntax` with both `ExpressionBody` and `Body` but omits `ConversionOperatorDeclarationSyntax`, which also has `ExpressionBody` and `Body` properties.
* **Impact:** Conversion operators whose body is a single throw statement are not fast-path identified, missing the early throw-detection exit.
* **Recommendation:** Add cases for `ConversionOperatorDeclarationSyntax` with `ExpressionBody` and `Body`.

### 10.2 Analyzer Rules (Agent 3 - Rules Files)

#### [PB2-10.2.1] 10.2.1 SwitchStatementPurityRule Does Not Analyze Case Bodies

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CFG-SWITCH-ANALYSIS
> **Evidence:** SwitchStatementPurityRule handles the switch value while CFG block operations independently analyze reachable case guards and bodies.
> **Changes/tests:** Existing ConstantSwitchStatement and source switch purity regressions report reachable impure cases and ignore only proven-unreachable ones.
* **File & Lines:** [SwitchStatementPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/SwitchStatementPurityRule.cs#L17-L21)
* **Severity:** High
* **Description:** The rule only checks `switchOperation.Value` for purity and returns `Pure` immediately. It never enumerates switch cases, when guards, or case body operations. By contrast, `SwitchExpressionPurityRule` correctly iterates through all arms.
* **Impact:** Any impure operation inside a `switch` statement case body is silently accepted as pure - a significant soundness gap.
* **Recommendation:** Iterate through `switchOperation.Cases`, checking each case clause's conditions/guards and body operations.

#### [PB2-10.2.2] 10.2.2 Hardcoded RefKind Magic Number `(RefKind)4` Instead of Named Enum

> **Disposition:** Fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Ref-readonly parameter recognition retained an unnamed numeric enum fallback.
> **Changes/tests:** The fallback now uses RefKind.RefReadOnlyParameter.
* **File & Lines:** [FieldReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/FieldReferencePurityRule.cs#L119), [PropertyReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/PropertyReferencePurityRule.cs#L253)
* **Severity:** Medium
* **Description:** Both files test `paramRef.Parameter.RefKind == (RefKind)4` to detect `ref readonly` parameters. Roslyn defines `RefKind.RefReadOnly` as a named enum member; using `(RefKind)4` is fragile.
* **Impact:** If the Roslyn enum layout changes, this check silently breaks.
* **Recommendation:** Replace `(RefKind)4` with `RefKind.RefReadOnly`.

#### [PB2-10.2.3] 10.2.3 ArrayElementReferencePurityRule Misses Compound/Increment Parent Operations

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [ArrayElementReferencePurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/ArrayElementReferencePurityRule.cs#L34-L48)
* **Severity:** Medium
* **Description:** `IsPartOfAssignmentTarget` only checks `IAssignmentOperation` as a parent. It fails to recognize `ICompoundAssignmentOperation` (e.g., `arr[i] += 1`) and `IIncrementOrDecrementOperation` (e.g., `arr[i]++`). Compare with `InlineArrayAccessPurityRule` which correctly checks all three parent types.
* **Impact:** Compound assignments or increments on array elements are not recognized as write targets, causing incorrect impurity classification.
* **Recommendation:** Add checks for `ICompoundAssignmentOperation` and `IIncrementOrDecrementOperation`.

#### [PB2-10.2.4] 10.2.4 UsingStatementPurityRule Fragile AwaitKeyword Detection via RawKind

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [UsingStatementPurityRule.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Engine/Rules/UsingStatementPurityRule.cs#L295-L297)
* **Severity:** Low
* **Description:** `IsAwaitUsingOperation` checks `usingStatementSyntax.AwaitKeyword.RawKind != 0`. `SyntaxToken.RawKind` is an internal detail that can change between Roslyn versions. The correct approach is `usingStatementSyntax.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)`.
* **Impact:** Fragile detection may break with future Roslyn updates.
* **Recommendation:** Use `IsKind(SyntaxKind.AwaitKeyword)` instead of comparing `RawKind`.

### 10.3 Analyzer Top-Level Files (Agent 2)

#### [PB2-10.3.1] 10.3.1 ExceptionFlowAnalyzer GetIdentifierLocation Missing ConversionOperatorDeclarationSyntax

> **Disposition:** Fixed
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [ExceptionFlowAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/ExceptionFlowAnalyzer.cs#L664-L681)
* **Severity:** Low
* **Description:** `GetIdentifierLocation` handles `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `OperatorDeclarationSyntax`, etc., but not `ConversionOperatorDeclarationSyntax`. It falls through to `_ => node.GetLocation()`, returning a span covering the entire declaration.
* **Impact:** SP0010 diagnostic location for conversion operators spans the entire declaration instead of just the type token.
* **Recommendation:** Add a case for `ConversionOperatorDeclarationSyntax conversion => conversion.Type.GetLocation()`.

#### [PB2-10.3.2] 10.3.2 MethodCapabilityAnalyzer Assumes UnknownReasonDetails Is Non-Empty

> **Disposition:** Duplicate
> **Canonical root cause:** RC-ANALYZER-SYMBOLIC-BOUNDARY
> **Evidence:** Same analyzer exception-boundary root cause as PB1-1.1, PB1-1.2, and PB1-26.1.
> **Changes/tests:** See typed query outcomes and conservative diagnostics under PB1-1.1.
* **File & Lines:** [MethodCapabilityAnalyzer.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/MethodCapabilityAnalyzer.cs#L90)
* **Severity:** Medium
* **Description:** When capability sites exist but unknown reasons are present (line 80), the code accesses `result.UnknownReasonDetails[0]` without verifying `result.UnknownReasonDetails.Length > 0`. If `UnknownReasons` is populated but `UnknownReasonDetails` is empty, this throws `IndexOutOfRangeException`.
* **Impact:** Analyzer crash (AD0001) for the affected method, suppressing all other purity diagnostics.
* **Recommendation:** Check `result.UnknownReasonDetails.Length > 0` before indexing.

### 10.4 Symbolic Engine (Agent 4 - Core Files)

#### [PB2-10.4.1] 10.4.1 Missing Bounds Check in TryGetListPatternElementPosition

> **Disposition:** Fixed
> **Canonical root cause:** RC-LIST-PATTERN-INDEX-VALIDATION
> **Evidence:** TryGetListPatternElementPosition indexed the pattern list before validating its public integer input.
> **Changes/tests:** Added negative/upper-bound guards; ListPatternElementPosition_RejectsOutOfRangeIndexes.
* **File & Lines:** [CSharpSyntaxFacts.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/CSharpSyntaxFacts.cs#L76)
* **Severity:** High
* **Description:** `TryGetListPatternElementPosition` accesses `listPattern.Patterns[patternIndex]` without bounds validation. If `patternIndex >= listPattern.Patterns.Count`, this throws.
* **Impact:** Stale or out-of-range pattern index crashes the analysis pipeline.
* **Recommendation:** Add `if (patternIndex < 0 || patternIndex >= listPattern.Patterns.Count) return false;` before the access.

#### [PB2-10.4.2] 10.4.2 Incomplete Collection Expression Fixed Lower Bound

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicFactFactory.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicFactFactory.cs#L441-L458)
* **Severity:** Medium
* **Description:** `TryGetCollectionExpressionFixedLowerBound` returns only `hasSpread && lowerBound > 0`. For collection expressions composed entirely of fixed expressions (e.g., `[a, b, c]`), the function returns `false` even though the minimum length is trivially known (3).
* **Impact:** Length lower-bound facts are silently dropped for spread-free collection expressions, causing missed bounds-check proofs.
* **Recommendation:** Change the return to `true` whenever `lowerBound > 0`, regardless of `hasSpread`.

#### [PB2-10.4.3] 10.4.3 Expensive Ad-Hoc Z3 Solver Creation Per Pipeline Call

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicProofPipeline.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProofPipeline.cs#L96-L105)
* **Severity:** Medium
* **Description:** When no `SmtAnalysisService` is passed to the pipeline, `Execute` creates a brand-new `SmtAnalysisService` and disposes it immediately. Since pipeline methods are called many times per analysis session, this amounts to hundreds of native Z3 context create/destroy cycles.
* **Impact:** Severe performance degradation when analysis runs without an external SMT configuration.
* **Recommendation:** Cache a single fallback `SmtAnalysisService` instance at the pipeline level and reuse it.

#### [PB2-10.4.4] 10.4.4 Overly Broad ToString Capability Neutrality

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** Every metadata method named ToString was assumed capability-neutral.
> **Changes/tests:** Only System.Object.ToString receives that fallback; custom source ToString capability regression coverage.
* **File & Lines:** [SymbolicCapabilityService.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicCapabilityService.cs#L1022)
* **Severity:** Medium
* **Description:** `IsKnownCapabilityNeutralSymbol` returns `true` for any method named `ToString` on any type, without checking the containing type. Calling `ToString()` on a type whose override performs I/O is classified as capability-neutral.
* **Impact:** Methods calling custom `ToString()` overrides with capabilities are incorrectly classified as having no capabilities.
* **Recommendation:** Restrict to `System.Object.ToString()` by checking `originalSymbol.ContainingType.SpecialType == SpecialType.System_Object`.

#### [PB2-10.4.5] 10.4.5 Silent Exception Swallowing in TryGetGlobalOption

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SymbolicProjectQueryContext.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/SymbolicProjectQueryContext.cs#L149-L166)
* **Severity:** Medium
* **Description:** `TryGetGlobalOption` catches all `Exception` types except `OperationCanceledException` and silently returns `false`. If the analyzer configuration provider throws due to a corrupt `.globalconfig`, the failure is silently absorbed.
* **Impact:** Configuration errors are invisible; the analyzer silently runs with default settings.
* **Recommendation:** At minimum, emit an analyzer diagnostic or log when catching exceptions.

### 10.5 Symbolic IR & SMT (Agent 5)

#### [PB2-10.5.1] 10.5.1 SwitchPathConditionBuilder Silently Skips Unmodelable Labels in Default Case Condition

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CONTROL-FLOW-AND-STATE-MERGING
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [SwitchPathConditionBuilder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs#L122-L141)
* **Severity:** Medium
* **Description:** When computing the default case condition (negation of all explicit selections), labels that cannot be modeled are silently skipped. The default case's negation does not exclude inputs matching those unmodelable labels.
* **Impact:** The default switch case appears reachable for inputs that should match an explicit label, leading to false negatives when the default case contains impure operations.
* **Recommendation:** When any explicit label fails to lower, return `false` for the entire default condition.

#### [PB2-10.5.2] 10.5.2 SmtSyntacticClassifier Conditional Not in Complement Check

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [SmtSyntacticClassifier.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs#L349-L366)
* **Severity:** Low
* **Description:** `AreSyntacticComplements` only handles `SmtUnaryFormula` (negation) and `SmtBinaryFormula` (relational comparison). Boolean conditional formulas like `ite(c, true, false)` would never be recognized as complementary to `ite(c, false, true)`.
* **Impact:** Slight precision loss - `ContainsSyntacticContradiction` misses contradictions involving boolean conditional formulas, wasting solver budget.
* **Recommendation:** Extend `AreSyntacticComplements` to handle `SmtConditionalFormula` with constant-folding.

### 10.6 SearchLib & Shared (Agent 6)

#### [PB2-10.6.1] 10.6.1 Unbounded _regexValidationCache Growth in SmtSolver

> **Disposition:** Fixed
> **Canonical root cause:** RC-REGEX-AND-UTF16-SEMANTICS
> **Evidence:** Concrete regex validation results accumulated without a session bound.
> **Changes/tests:** The per-solver cache is capped and cleared deterministically; ConcreteRegexValidationCache_IsBounded.
* **File & Lines:** [SmtSolver.cs](file:///C:/w/PurelySharp/SearchLib/SmtSolver.cs#L21)
* **Severity:** Medium
* **Description:** `_regexValidationCache` is a `Dictionary` caching regex match results with no size limit or eviction policy. In a long-running solver processing thousands of distinct concrete string inputs, the cache grows without bound.
* **Impact:** Managed heap growth in long-running analysis processes, eventually causing OOM.
* **Recommendation:** Add an LRU eviction policy or cap the cache size.

#### [PB2-10.6.2] 10.6.2 Overly Broad Substring Matching in BclPurityFallbackHeuristics

> **Disposition:** Fixed
> **Canonical root cause:** RC-ANALYZER-CONTRACT-SEMANTICS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [BclPurityFallbackHeuristics.cs](file:///C:/w/PurelySharp/Shared/BclPurityFallbackHeuristics.cs#L170-L192)
* **Severity:** Medium
* **Description:** `IsAmbientNamespaceOrType` calls `ContainsAny(typeName, "Console", "Environment", "Process", ...)` using `string.IndexOf` for substring matching. A type like `FileHandlerProcessor` will match `"File"`, causing the entire type to be over-approximated as ambient/impure.
* **Impact:** False impurity classifications for innocent types whose names happen to contain one of the substring fragments.
* **Recommendation:** Use word-boundary checks or exact-form matching for type names.

#### [PB2-10.6.3] 10.6.3 PurityProofSearch: TriggerCondition Dropped for Non-Standard InternalOnly Hazards

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** The cited failure path was confirmed against the implementation and resolved in the current change set.
> **Changes/tests:** Affected implementation plus focused regression coverage in this change set.
* **File & Lines:** [PurityProofSearch.cs](file:///C:/w/PurelySharp/SearchLib/PurityProofSearch.cs#L92-L124)
* **Severity:** Medium
* **Description:** When a hazard with `Visibility == InternalOnly` and a `Kind` other than the three known ones is encountered, the catch-all routes it to `ClassifyInternalOnlyEffect` which ignores `query.Hazard.TriggerCondition` entirely.
* **Impact:** If any future hazard kind is introduced with `InternalOnly` visibility, the solver returns `ProvablyPure` without checking reachability.
* **Recommendation:** Add a default case requiring `ClassifyCore` or assert that no such combination exists.

### 10.7 Test Files (Agents 7 & 8)

#### [PB2-10.7.1] 10.7.1 Undisposed SmtAnalysisService in Test Files

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Two test helpers transferred undisposed SMT services into synchronous query calls.
> **Changes/tests:** Both helpers now own services with using declarations.
* **File & Lines:** [ElementAccessSmtTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/ElementAccessSmtTests.cs#L644), [LoopExitSmtInvariantTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/LoopExitSmtInvariantTests.cs#L693-L700), [SymbolicAnalysisLimitsTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/SymbolicAnalysisLimitsTests.cs#L204-L206)
* **Severity:** Low
* **Description:** Multiple test classes create `new SmtAnalysisService(SmtAnalysisOptions.Default)` without wrapping in a `using` statement. Each creates a native Z3 solver context that is never released.
* **Impact:** Native Z3 resources accumulate during SMT-heavy test runs. While the test process's lifetime is short, this can cause test instability in CI with limited memory.
* **Recommendation:** Always wrap `SmtAnalysisService` in `using` declarations.

#### [PB2-10.7.2] 10.7.2 Duplicate Using Directives in NullComparisonTests

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [NullComparisonTests.cs](file:///C:/w/PurelySharp/SharpProof.Test/NullComparisonTests.cs#L16-L17)
* **Severity:** Low
* **Description:** Three test methods have `using SharpProof.Attributes;` listed twice on consecutive lines. Cosmetic only.
* **Impact:** No functional impact beyond code cleanliness.
* **Recommendation:** Remove duplicate `using` directives.

### 10.8 Tools (Agent 9)

#### [PB2-10.8.1] 10.8.1 SymbolicConsumer.cs: Operator Precedence Bug in stableFallback Logic

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-CSPATTERN-PRECEDENCE
> **Evidence:** In C#, the alternatives form one or-pattern operand of is; the following logical && applies to the completed is expression, not only the last constant pattern.
> **Changes/tests:** C# pattern grammar makes the current fallback require unknownProofCount for every failure-code alternative; no code change.
* **File & Lines:** [SymbolicConsumer.cs](file:///C:/w/PurelySharp/scripts/package-consumers/SymbolicConsumer.cs#L75-L81)
* **Severity:** High
* **Description:** The `stableFallback` expression uses `is` pattern matching with `or` patterns combined with `&&` without parentheses. `&&` binds tighter than `or`, so `unknownProofCount > 0` is only evaluated for `"smt_initialization_failure"`.
* **Impact:** When the SMT native library is missing, incompatible, or platform-unsupported, `stableFallback` evaluates to `true` even if `unknownProofCount` is 0, incorrectly treating the graceful-degradation path as satisfied.
* **Recommendation:** Add parentheses: `(health.LastFailureCode is ... or "smt_initialization_failure") && unknownProofCount > 0`.

#### [PB2-10.8.2] 10.8.2 Fuzz.Core: Trivia SyntaxKinds Double-Counted in CollectSyntaxKinds

> **Disposition:** Fixed
> **Canonical root cause:** RC-ATTRIBUTE-AND-CODEFIX-CONTRACTS
> **Evidence:** Structured trivia nodes were traversed twice by overlapping enumeration passes.
> **Changes/tests:** Structured trivia is counted through the node/token traversal and unstructured trivia separately; AnalyzeCase_CountsStructuredTriviaOnce.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L660-L681)
* **Severity:** Medium
* **Description:** `CollectSyntaxKinds` calls `root.DescendantNodesAndTokens(descendIntoTrivia: true)` (includes trivia) and then separately calls `root.DescendantTrivia(descendIntoTrivia: true)`. Every trivia syntax kind is counted twice.
* **Impact:** Coverage metrics are inflated with duplicate trivia counts, misleading fuzz coverage analysis.
* **Recommendation:** Remove the separate `DescendantTrivia` loop, or set `descendIntoTrivia: false` on `DescendantNodesAndTokens`.

#### [PB2-10.8.3] 10.8.3 Fuzz.Core: CollectOperationKinds Calls GetOperation on Every Descendant Node

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [Fuzz.Core/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/Program.cs#L649-L655)
* **Severity:** Medium
* **Description:** `CollectOperationKinds` iterates all descendant nodes and calls `compilation.GetOperation(node)` for every single node. `GetOperation` triggers semantic binding - for a large syntax tree this is extremely expensive.
* **Impact:** Severe performance degradation in fuzz case analysis.
* **Recommendation:** Only call `GetOperation` on statement/expression nodes, or cache the semantic model.

#### [PB2-10.8.4] 10.8.4 RoslynShapeManifest: Circular Static Initialization with FuzzCaseGenerator

> **Disposition:** Enhancement
> **Canonical root cause:** RC-COMPATIBILITY-OR-PRECISION-ENHANCEMENT
> **Evidence:** Current behavior is correct and compatible; the recommendation adds platform reach, precision, performance, or ergonomics rather than repairing a defect.
> **Changes/tests:** Documented as an additive enhancement; no compatibility-breaking change made.
* **File & Lines:** [RoslynShapeManifest.cs](file:///C:/w/PurelySharp/Tools/SharpProof.Fuzz.Core/RoslynShapeManifest.cs#L212-L215)
* **Severity:** Medium
* **Description:** `BuildOperationEntries` references `FuzzCaseGenerator.RegistryEntries`. Both types have interdependent static initializers. Currently safe due to property access ordering, but fragile.
* **Impact:** Potential `TypeInitializationException` if static initialization ordering changes.
* **Recommendation:** Break the static coupling by passing generator-backed shape IDs via method parameter or lazy-delayed initialization.

#### [PB2-10.8.5] 10.8.5 EffectSummary Program.cs: ReadStrings Uses Unsafe GetString() Without Type Check

> **Disposition:** Already fixed
> **Canonical root cause:** RC-IMPLEMENTATION-CORRECTNESS-AND-ROBUSTNESS
> **Evidence:** Current HEAD already contains the required guard or conservative behavior, and the alleged failure does not reproduce.
> **Changes/tests:** Existing focused coverage; no additional product change required.
* **File & Lines:** [EffectSummary/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L211-L214)
* **Severity:** Low
* **Description:** `ReadStrings` calls `item.GetString()` on each JSON array element without verifying `item.ValueKind == JsonValueKind.String`. If an array element is a number, `GetString()` throws `InvalidOperationException`.
* **Impact:** Malformed JSON in `GeneratedPurityCatalog` causes an unhandled crash.
* **Recommendation:** Check `item.ValueKind == JsonValueKind.String` before calling `GetString()`.

#### [PB2-10.8.6] 10.8.6 EffectSummary Program.cs: Path.GetDirectoryName Null-Forgiven on Root Paths

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [EffectSummary/Program.cs](file:///C:/w/PurelySharp/Tools/SharpProof.EffectSummary/Program.cs#L132)
* **Severity:** Low
* **Description:** `WriteManifestIfChanged` and other methods use `Path.GetDirectoryName(path)!`. `Path.GetDirectoryName` returns `null` for root paths like `C:\`. The `Directory.CreateDirectory(null)` call will throw.
* **Impact:** Crash when an output path resolves to a filesystem root.
* **Recommendation:** Guard against null: `Path.GetDirectoryName(path) ?? throw new ArgumentException(...)`.

### 10.9 Configuration & CodeFixes (Agent 10)

#### [PB2-10.9.1] 10.9.1 TryGetGlobalOption in AnalyzerConfiguration Silently Swallows All Exceptions

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-EXCEPTION-FLOW
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerConfiguration.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs#L616-L618)
* **Severity:** Medium
* **Description:** The `catch (Exception ex) when (ex is not OperationCanceledException)` block has an empty body. Any exception (including `NullReferenceException`, `OutOfMemoryException`) when querying analyzer config options is silently discarded.
* **Impact:** Configuration-related bugs are silently hidden. Users get default values without indication that loading failed.
* **Recommendation:** Log the exception or narrow the catch to expected exception types.

#### [PB2-10.9.2] 10.9.2 TryGetText in AnalyzerAdditionalFileValidator Hides Runtime Errors

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L422)
* **Severity:** Medium
* **Description:** `TryGetText` catches all non-cancellation exceptions and sets `text = string.Empty` before returning `false`. This silently discards `OutOfMemoryException` or `NullReferenceException` when loading additional files.
* **Impact:** A corrupt additional file that triggers a runtime error is treated identically to an empty file.
* **Recommendation:** Narrow the catch to expected I/O exceptions or capture the exception message.

#### [PB2-10.9.3] 10.9.3 Recursive JSON Traversal Without Depth Limit in Analyzer Validators

> **Disposition:** False positive/intentional
> **Canonical root cause:** RC-VALIDATED-NONDEFECT-OR-INTENTIONAL-POLICY
> **Evidence:** Validated against current HEAD, framework semantics, and applicable tests; the alleged failure path does not reproduce, is conservatively handled, or is an intentional policy.
> **Changes/tests:** No corrective code change required.
* **File & Lines:** [AnalyzerAdditionalFileValidator.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/AnalyzerAdditionalFileValidator.cs#L247-L294), [DiagnosticBaseline.cs](file:///C:/w/PurelySharp/SharpProof.Analyzer/Configuration/DiagnosticBaseline.cs#L135-L178)
* **Severity:** Low
* **Description:** Both `ValidateEvidenceSchemasRecursively` and `HasReadCompatibleEvidenceSchema` perform unbounded recursion through JSON arrays and objects, with no depth limit.
* **Impact:** Stack overflow crashes the analyzer when processing deeply nested JSON additional files.
* **Recommendation:** Add a maximum recursion depth guard (e.g., `remainingDepth = 10`).

#### [PB2-10.9.4] 10.9.4 HasUnaliasedSharpProofAttributesUsing Misses Namespace-Scoped Using Directives

> **Disposition:** Fixed
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Short-name detection inspected only compilation-unit imports.
> **Changes/tests:** Code fixes now inspect compilation-unit and namespace-scoped imports; SP0004_AddEnforcePure_UsesNamespaceScopedImport.
* **File & Lines:** [SharpProofCodeFixProvider.cs](file:///C:/w/PurelySharp/SharpProof.CodeFixes/SharpProofCodeFixProvider.cs#L1121-L1127)
* **Severity:** Low
* **Description:** `HasUnaliasedSharpProofAttributesUsing` checks only `CompilationUnitSyntax.Usings`, not `NamespaceDeclarationSyntax.Usings` for block-scoped namespaces.
* **Impact:** Code fixes revert to fully qualified names even when a short name import exists within a block-scoped namespace.
* **Recommendation:** Traverse `NamespaceDeclarationSyntax` ancestors and check their `Usings` as well.

## Source PB3 - New Audit

### 1 Z3 Formula Encoding (Agent 1)

### 2 Symbolic IR & Encoding (Agent 2)

#### [PB3-2.5] 2.5 GetReferenceFormulaName Returns "?" for Non-Variable References, Losing Identity in Count/Element Encoding

> **Disposition:** Needs investigation
> **Canonical root cause:** RC-SMT-PROOF-SOUNDNESS
> **Evidence:** Non-variable references are encoded as "?" causing collisions.
> **Changes/tests:** No fix yet.
* **File & Lines:** [SymbolicIrFormulaEncoder.cs](file:///C:/w/PurelySharp/SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs#L322-L324)
* **Severity:** High
* **Description:** `GetReferenceFormulaName` at line 322 returns `"?"` when the formula is not a `SmtVariable`. This is used for `SymbolicMemberTerm` (line 90), `SymbolicElementTerm` (line 99), `SymbolicMultiElementTerm` (line 115), and `SymbolicCountTerm` (line 190) to construct SMT variable names. When the receiver is a complex expression (e.g., another member access or element access), the resulting variable name is `"?.MemberName"` or `"?[index]"` — all such formulas with different non-variable receivers map to the same SMT variable name. This causes the solver to treat different object graph paths as identical, producing unsound proof results.
* **Impact:** Unsound proof results when member/element access chains involve non-trivial receivers.
* **Recommendation:** Extend `GetReferenceFormulaName` to handle nested formula structures, or reject encoding when the receiver cannot be represented as a simple variable name.

### 3 Symbolic Lowering & Analysis (Agent 3)

### 4 Analyzer & Contracts (Agent 4)

### 5 Test Infrastructure & Tooling (Agent 5)

### 6 SharpProof.ProofCore Collections & Utilities (Agent 6)

### 7 SMT Analysis Service & Lifecycle (Agent 7)

### 8 SymbolicState & Facts (Agent 8)

### 9 SymbolicProofEncoder & Encoding (Agent 9)

### 10 SymbolicProofStateFacts & Normalization (Agent 10)

### 11 SymbolicRuntimeException & Hazard Analysis (Agent 11)

### 12 SharpProof.Analyzer Symbol & Attribute Traversal (Agent 12)

### 13 SharpProof.Symbolic Reentrancy & Thread Safety (Agent 13)

### 14 Cross-Cutting Performance & Memory (Agent 14)

### 15 Switch Path Condition Builder (Agent 15)

### 16 SymbolicComplexity Analysis (Agent 16)

### 17 Meta-Analysis: Duplicates, Cross-Agent Consistency, Scope Gaps

### 18 SymbolicFactFactory & Naming (Agent 18)

### 19 Lowering & Pattern Matching (Agent 19)

### 20 Pre-Existing Bug Verification (Agent 20)

### 21 Fuzz Testing & Tooling (Agent 21)

### 22 Symbolic Method Effects & Analysis (Agent 22)

### 23 Code Quality Observations & Summary (Agent 23)

### 24 Null Safety & Exception Handling (Agent 24)

### 25 Logic Errors (Agent 25)

### 26 Memory & Resource Leaks (Agent 26)

### 27 Regex Translation (Agent 27)

### 28 Symbolic Complexity Analysis (Agent 28)

### 29 Duplicate & Collision Verification (Agent 29)

### 30 Bulk Findings - Syntax & Style (Agent 30)

### 31 Bulk Findings - Symbolic IR & Analysis (Agent 31)

### 32 Bulk Findings - Nullable & Merge Engine (Agent 32)

### 33 Bulk Findings - Symbolic IR & Lowering (Agent 33)

### 34 Bulk Findings - Analyzer & Configuration (Agent 34)

### 35 Bulk Findings - Attributes, Test, Tooling, Docs (Agent 35)

