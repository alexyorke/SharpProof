# Remaining PurelySharp Analyzer Backlog

This is the single canonical backlog file for remaining analyzer work.

Merged here and expected to stay deleted:

- `REFACTORING_STATUS_PURELYSHARP.md`
- `IMPLEMENTATION_TODO.md`
- `FUZZ_BACKLOG.md`

Not backlog files:

- `README.md`
- `docs/effect-summary.md`
- `PurelySharp.Analyzer/AnalyzerReleases.Shipped.md`
- `PurelySharp.Analyzer/AnalyzerReleases.Unshipped.md`

## Current repo truth

- Only top-level backlog/status markdown still present is this file.
- The analyzer already has:
  - explainable `PS0002` evidence properties
  - `PS0010` exception reporting and effect-summary tooling
  - manifest-backed Roslyn shape coverage tests
  - deterministic and random fuzz infrastructure
  - exact concrete dispatch narrowing for locals, aliases, casts, and
    same-concrete conditional, `??`, or `if`/`else` merges
  - explicit rules for anonymous object creation, inline array access, and
    implicit indexer reference
- Last confirmed full `PurelySharp.Test` baseline: `2096/2096` green.

## Immediate next actions

- [ ] Continue expanding trusted effect-summary coverage for metadata-only or
      hidden implementation methods.
- [ ] Pick the next bounded analyzer precision fix from the P0 list and land it
      behind narrow regressions.

## P0 - Real correctness and precision gaps

### 1. Opaque external calls and effect-summary coverage

Evidence:

- `PurelySharp.Test/BoundaryAttributeTests.cs`
- `PurelySharp.Test/EffectSummaryToolTests.cs`
- `PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs`
- `PurelySharp.Analyzer/ExceptionFlowAnalyzer.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Current state:

- Trusted summary boundary coverage already locks:
  - exact filename and suffixed `*.PurelySharp.EffectSummary.json` discovery
  - direct plus transitive exception-type union
  - multi-file summary merge for the same trusted symbol
  - wrong-symbol rows being ignored
  - malformed rows being ignored
- Boundary coverage now also locks generic metadata method summary matching on
  constructed calls.

Remaining:

- [ ] Expand trusted consumed summaries for metadata-only or hidden
      implementation methods.
- [ ] Add more end-to-end fixture coverage so emitted and consumed summaries
      stay aligned.
- [ ] Reduce conservative `unknown_external_call` fallbacks only when a
      trusted per-member summary exists.

Done when:

- Known external methods can be classified from trusted summaries without
  broad catalog whitelisting.

### 2. Fresh mutable return precision beyond the reviewed trusted-return subset

Evidence:

- `PurelySharp.Test/CryptographyTests.cs`
- `PurelySharp.Test/ConstantsTests.cs`
- `PurelySharp.Analyzer/Engine/Constants.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Current state:

- Reviewed trusted fresh-array producers already include:
  - `SHA256.HashData(byte[])`
  - `MD5.HashData(byte[])`
  - `Convert.FromBase64String(string)`
  - `Convert.FromHexString(string)`
  - `Guid.ToByteArray()`
  - reviewed `BitConverter.GetBytes(...)` overloads
- Signature validation exists in `ConstantsTests`.

Remaining:

- [ ] Review additional deterministic fresh-array producers one member at a
      time.
- [ ] Keep broad mutable-array and mutable-object returns conservative until
      they are individually proven or modeled.
- [ ] Keep factory, disposable, RNG, environment-backed, and stateful crypto
      APIs conservative.

Done when:

- Additional fresh-array false positives are closed through member-level
  reviewed trusted-return entries or stronger bounded proof.

### 3. Dispatch precision beyond the current narrowing

Evidence:

- `PurelySharp.Test/DynamicTypingTests.cs`
- `PurelySharp.Test/ExactConcreteDispatchTests.cs`
- `PurelySharp.Test/ExactConcreteDispatchFlowTests.cs`
- `PurelySharp.Test/ExactConcretePropertyDispatchTests.cs`
- `PurelySharp.Test/FrameworkCommonOperationsTests.cs`
- `PurelySharp.Test/InheritanceInteractionTests.cs`
- `PurelySharp.Test/StaticInterfaceMembersTests.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`
- `PurelySharp.Analyzer/Engine/Rules/PropertyReferencePurityRule.cs`
- `PurelySharp.Analyzer/Engine/Rules/DynamicOperationPurityRule.cs`

Remaining:

- [ ] Recover concrete targets through deeper heterogeneous or nested branch
      and flow-merge cases.
- [ ] Distinguish provable virtual or interface dispatch from truly unknown
      dynamic dispatch.
- [ ] Improve static abstract interface member resolution in generic contexts.
- [ ] Keep mutable field and property receiver flows conservative unless the
      target stays provable.

Done when:

- Fewer `dynamic_dispatch` or unknown-target fallbacks remain in the targeted
  suites, with a narrow regression for each recovered case.

### 4. Fresh ownership and escape precision follow-up

Evidence:

- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs`
- `PurelySharp.Analyzer/Engine/Rules/AssignmentPurityRule.cs`
- ownership-sensitive tests across `BasicOperationsTests`,
  `BasicPureTests`, and `InlineArrayTests`

Remaining:

- [ ] Improve deeper aliasing, wrapper, loop, closure, and returned-fresh
      object cases.
- [ ] Keep escape analysis local and predictable instead of turning it into a
      speculative whole-program proof.

Done when:

- More fresh-local mutation cases pass under bounded, test-backed ownership
  rules without regressing soundness.

### 5. LINQ and enumerator precision follow-up

Evidence:

- `PurelySharp.Test/LinqOperationsTests.cs`
- `PurelySharp.Test/LinqSoundnessStressTests.cs`
- `PurelySharp.Test/CollectionExpressionSpreadTests.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Remaining:

- [ ] Preserve current source and secondary-source `GetEnumerator()` analysis
      as queries become more nested and wrapper-heavy.
- [ ] Review comparer, secondary delegate, and deferred-execution paths for
      bounded precision wins.
- [ ] Keep multi-source and wrapper-heavy cases conservative unless the source
      side stays provable.

Done when:

- Additional pure LINQ cases are accepted without weakening current
  impure-source and impure-enumerator regressions.

### 6. Mutual recursion proof beyond direct self-recursion

Evidence:

- `PurelySharp.Test/FileScopedNamespacesTests.cs`
- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs`

Current state:

- Direct self-recursion is already handled more precisely.
- Mutual recursion is still intentionally conservative.

Remaining:

- [ ] Evaluate whether small mutually recursive pure cycles can be accepted
      without unsoundness.
- [ ] Keep recursion conservative when any cycle member touches mutable state,
      opaque calls, unknown dispatch, or unsupported constructs.

Done when:

- Simple provable cycles stop being flagged, while impure or unknown cycles
  still fail deterministically.

### 7. Cryptography member-level review

Evidence:

- `PurelySharp.Test/CryptographyTests.cs`

Remaining:

- [ ] Continue splitting deterministic pure member operations from factories,
      disposables, RNG, and environment-backed helpers.
- [ ] Add only member-level catalog entries or trusted summaries.
- [ ] Do not broaden `System.Security.Cryptography` as a namespace or type-wide
      allowlist.

Done when:

- Any new crypto allowances are per-member, evidence-based, and regression
  tested.

## P1 - Explicit conservative surfaces

### 1. Remaining conservative Roslyn operation shapes

Evidence:

- `PurelySharp.Test/RoslynConstructCoverageTests.cs`
- `PurelySharp.Test/FuzzToolTests.cs`
- `Tools/PurelySharp.Fuzz/Program.cs`
- `Tools/PurelySharp.Fuzz/RoslynShapeManifest.cs`

Still intentionally conservative:

- `AddressOf`
- `FunctionPointerInvocation`
- `InterpolatedStringHandlerCreation`
- `InterpolatedStringAddition`
- `InterpolatedStringAppendLiteral`
- `InterpolatedStringAppendFormatted`
- `InterpolatedStringAppendInvalid`
- `InterpolatedStringHandlerArgumentPlaceholder`

Already covered and no longer backlog drivers:

- `AnonymousObjectCreation`
- `InlineArrayAccess`
- `ImplicitIndexerReference`

Remaining:

- [ ] Either implement bounded support or keep each remaining shape locked
      behind explicit conservative-contract tests and deterministic generator
      coverage.

### 2. Runtime surfaces that should stay conservative unless root proof exists

Evidence:

- Current tests still intentionally treat dynamic runtime binding,
  reflection, environment reads, filesystem or network access, time, culture,
  threading, synchronization, and unsafe pointer flows as conservative.

Remaining:

- [ ] Narrow only with real proof inputs such as trusted summaries, concrete
      source, or bounded rule logic.
- [ ] Avoid heuristic catalog broadening for environment-dependent APIs.

## P2 - Durability and tooling

### 1. Catalog tooling and validation

Evidence:

- The analyzer still relies on large member catalogs, and catalog correctness
  remains central to trust.

Remaining:

- [ ] Keep catalog entries member-level.
- [ ] Continue conflict detection and signature validation against supported
      frameworks where practical.
- [ ] Separate generated evidence from hand-maintained policy so catalog edits
      stay reviewable.

### 2. Broader performance and caching coverage

Evidence:

- `PurelySharp.Test/CachingTests.cs` covers repeated-query and deep call-chain
  reuse, but larger dispatch-heavy, summary-heavy, and memory-behavior
  scenarios are still thinner than the main correctness suites.

Remaining:

- [ ] Add bigger dispatch-heavy and summary-heavy scenarios.
- [ ] Watch for cache growth and repeated semantic-query regressions.
- [ ] Keep perf checks deterministic and non-flaky.

### 3. Artifact-driven backlog refresh

Evidence:

- The repo has Roslyn coverage and fuzz artifacts, but this file is still
  hand-curated.

Remaining:

- [ ] Use corpus and fuzz outputs to refresh this file from evidence instead of
      ad hoc notes.
- [ ] Promote repeated findings into deterministic regressions quickly.

### 4. Documentation discipline

Remaining:

- [ ] Keep `README.md` aligned with shipped behavior.
- [ ] Keep this file as the only backlog or status doc for remaining analyzer
      work.
- [ ] Do not reintroduce parallel status, gap, todo, or fuzz-backlog markdown
      files.

## Suggested execution order

1. Confirm or revert the in-progress generic effect-summary metadata pass.
2. External-call and effect-summary coverage.
3. Fresh mutable return precision for known-pure library methods.
4. Dispatch precision.
5. Ownership or LINQ follow-up, whichever yields the next bounded green fix.
6. Mutual recursion proof.
7. Conservative Roslyn shape tranche.
8. Catalog and perf durability work.
