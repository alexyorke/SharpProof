# Remaining PurelySharp Symbolic Platform Backlog

This is the single canonical backlog file for remaining symbolic platform and
analyzer work. PurelySharp remains the compatibility identity, but the product
direction is a bounded symbolic C# analysis platform where purity is one
consumer of shared facts and proofs.

Merged here and expected to stay deleted:

- `REFACTORING_STATUS_PURELYSHARP.md`
- `IMPLEMENTATION_TODO.md`
- `FUZZ_BACKLOG.md`
- backlog-style "next steps" previously carried in `docs/effect-summary.md`

Not backlog files:

- `README.md`
- `docs/effect-summary.md`
- `PurelySharp.Analyzer/AnalyzerReleases.Shipped.md`
- `PurelySharp.Analyzer/AnalyzerReleases.Unshipped.md`

## Current repo truth

- Only top-level backlog/status markdown still present is this file.
- The only markdown file that should carry remaining-work items is this file.
- The analyzer already has:
  - explainable `PS0002` evidence properties
  - `PS0010` exception reporting and effect-summary tooling
  - report-only implementation-derived purity classification in
    `Tools/PurelySharp.EffectSummary`
  - manifest-backed Roslyn shape coverage tests
  - deterministic and random fuzz infrastructure
  - exact concrete dispatch narrowing for locals, aliases, casts, and
    same-concrete conditional, `??`, or `if`/`else` merges
  - explicit rules for anonymous object creation, inline array access, and
    implicit indexer reference
- Last confirmed full `PurelySharp.Test` baseline: `2149/2149` green.
- Staged rebrand direction:
  - current compatibility identity remains `PurelySharp`
  - `SharpProof` is only a working codename pending formal availability review
  - package IDs, namespaces, diagnostics, config keys, attributes, and
    additional-file conventions are intentionally unchanged in the current
    tranche

## What still remains, at a high level

- Make the shared symbolic platform spine dominant:
  `Roslyn/C# -> Symbolic IR -> normalized symbolic state -> proof service ->
  Z3-backed conclusions -> analyzer/API/CLI outputs`.
- Keep docs and package metadata aligned with the broader symbolic-platform
  direction rather than describing the project as only a purity analyzer.
- Close evidence-backed analyzer precision gaps that still show up as
  `unknown_external_call`, `dynamic_dispatch`, `unsupported_operation`, or
  deliberately conservative test expectations.
- Retire analyzer-local path, reachability, runtime-hazard, and raw SMT formula
  construction only after equivalent symbolic IR/proof-service behavior is
  locked by tests.
- Expand trusted effect-summary coverage so metadata and hidden-runtime methods
  can be classified from concrete evidence instead of broad catalogs.
- Use the new report-only implementation-derived classifier as the gate for
  catalog retirement, rather than broad manual cleanup.
- Keep the backlog artifact-driven: promote repeated fuzz or corpus findings
  into deterministic regressions, then remove the backlog item once the gap is
  test-locked.

## Immediate next actions

- [ ] Keep routing analysis consumers through the symbolic platform spine:
      purity diagnostics, runtime hazards, invariant queries, and
      effect-summary-derived facts should share IR facts and proof services.
- [ ] Keep analyzer raw-SMT migration hotspots at zero and burn down symbolic
      direct `CSharpConditionToFormula` usage reported by
      `scripts/Get-PurelySharpRawSmtHotspots.ps1`.
- [ ] Keep `scripts/Get-PurelySharpProductionMetrics.ps1` and the raw-SMT
      inventory aligned with the architecture debt list before major refactors.
- [ ] Continue expanding trusted effect-summary coverage for metadata-only or
      hidden implementation methods.
- [ ] Use the report-only purity classifier output to choose the first narrow
      reviewed manual-catalog retirement tranche.
- [ ] Pick the next bounded analyzer precision fix from the P0 list and land it
      behind narrow regressions.

## P0 - Real correctness and precision gaps

### 0. Symbolic platform spine and raw SMT retirement

Evidence:

- `PurelySharp.Symbolic/Ir`
- `PurelySharp.Symbolic/SymbolicReachabilityService.cs`
- `PurelySharp.Symbolic/SymbolicRuntimeHazardQueryService.cs`
- `PurelySharp.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.cs`
- `PurelySharp.Analyzer/ExceptionFlowAnalyzer.PathFacts.cs`
- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs`
- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs`
- `PurelySharp.Test/ArchitectureReductionTests.cs`

Current state:

- `PurelySharp.Symbolic.Ir` is the intended owner of C#/Roslyn semantic facts,
  terms, atoms, exceptional preconditions, ownership/freshness/mutation facts,
  type tests, string/regex facts, and bounds.
- `SearchLib` is backend SMT/Z3 infrastructure and should not absorb Roslyn or
  analyzer-specific semantics.
- Runtime hazards have started moving through IR exception-precondition atoms,
  but analyzer exception flow and purity state merging still contain legacy raw
  SMT construction and direct `CSharpConditionToFormula` calls.
- Architecture tests now prevent new analyzer files from becoming raw-SMT
  hotspots outside the approved migration list.
- The current approved analyzer raw-SMT hotspot list is empty.
- `scripts/Get-PurelySharpRawSmtHotspots.ps1` also tracks symbolic direct
  translator usage and IR known-API lowering descriptors so migration progress
  is measurable.

Remaining:

- [ ] Route analyzer exception-site diagnostics through shared symbolic hazard
      results where equivalent, keeping exception-summary propagation separate
      until locked.
- [ ] Move analyzer path-condition collection and state merging to
      `SymbolicState` or equivalent normalized IR facts before encoding to SMT.
- [ ] Replace direct `CSharpConditionToFormula*` recognizer branches with
      declarative IR lowerings for string/regex, nullable, bounds, type tests,
      numeric ranges, and exceptional preconditions.
- [ ] Split large purity rules so invocation effect resolution, receiver
      dispatch, mutation/ownership, generated summaries, BCL fallback guesses,
      and evidence formatting can be tested and retired independently.
- [ ] Keep unsupported translation, SMT timeout, method budget exhaustion,
      cancellation, and native Z3 load failure conservative.

Done when:

- Analyzer diagnostics, symbolic invariant queries, runtime hazards, and
  summary-backed reasoning all consume shared IR/proof services, and analyzer
  code no longer creates C#/Roslyn-specific raw SMT formulas directly.

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
  - assembly hash, module version ID, metadata token, and method-body hash
    validation before trusting a consumed metadata summary
- Boundary coverage now also locks generic metadata method summary matching on
  constructed calls.
- Metadata fixture coverage now also locks constructor and property-getter
  summary matching through trusted assembly identity.
- End-to-end fixture coverage now also locks emitted-summary to consumed-summary
  alignment for transitive metadata exceptions, common direct metadata
  exception families, metadata methods that catch their own throws, and
  metadata rethrow paths that should still escape.
- A report-only fixed-point purity classifier now consumes the same emitted
  evidence and can compare emitted members against the current reviewed manual
  pure, impure, and fresh-array catalogs without changing live analyzer
  behavior.

Remaining:

- [ ] Expand trusted consumed summaries for metadata-only or hidden
      implementation methods.
- [ ] Add more end-to-end fixture coverage so emitted and consumed summaries
      stay aligned.
- [ ] Reduce conservative `unknown_external_call` fallbacks only when a
      trusted per-member summary exists.
- [ ] Add more validated built-in manifest entries or ad hoc local summary
      slices only after identity validation is strong enough to reject
      mismatched runtimes.
- [ ] Promote trusted generated purity rows into optional analyzer consumption
      only for exact external members after review.
- [ ] Keep runtime-native, OS, reflection, environment, time, culture,
      randomness, threading, synchronization, and unsafe roots explicit and
      evidence-labeled rather than broad heuristic allowlists.

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

- Bundled generated purity summaries now cover:
  - `SHA1.HashData(byte[])`
  - `SHA1.HashData(System.ReadOnlySpan<byte>)`
  - `SHA256.HashData(byte[])`
  - `SHA256.HashData(System.ReadOnlySpan<byte>)`
  - `SHA384.HashData(byte[])`
  - `SHA384.HashData(System.ReadOnlySpan<byte>)`
  - `SHA512.HashData(byte[])`
  - `SHA512.HashData(System.ReadOnlySpan<byte>)`
  - `MD5.HashData(byte[])`
  - `MD5.HashData(System.ReadOnlySpan<byte>)`
- The hand-maintained fresh-array catalog is currently empty; fresh-array trust
  now comes from bundled generated summaries plus the explicit `Array.Empty()`
  special case.
- Signature validation exists in `ConstantsTests`.
- Catalog proof now also explicitly locks `string(ReadOnlySpan<char>)` as a
  reviewed pure materialization path, separate from the fresh-array subset.

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
- [ ] Revisit repo-proven conservative interface/property cases such as
      framework configuration getters only if the concrete target can be
      narrowed without guessing across external implementations.

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
- [ ] Keep generic interpolation, comparer callbacks, and delegate-backed
      formatting/query helpers conservative unless their callback targets can be
      proven exactly.

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

### 8. Exception and effect-summary depth beyond the current PS0010 baseline

Evidence:

- `PurelySharp.Test/ExceptionFlowAnalysisTests.cs`
- `PurelySharp.Test/EffectSummaryToolTests.cs`
- `PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs`
- `Tools/PurelySharp.EffectSummary/Program.cs`
- `docs/effect-summary.md`

Current state:

- `PS0010` already covers source throws, simple rethrows, definite divide by
  zero, definite null dereference, basic catch suppression, same-compilation
  source propagation, and trusted consumed metadata summaries.
- The effect-summary tool already emits low-level call/effect facts, root
  candidates, thrown exception types, assembly SHA-256, method-body SHA-256,
  cache keys, and module version ID metadata.

Remaining:

- [ ] Add more fixture-based end-to-end tests that prove emitted summaries are
      consumed exactly as intended by the analyzer.
- [ ] Tighten coverage for caught-vs-escaped exceptions in metadata summaries,
      especially where IL exception handler tables matter.
- [ ] Expand library-style fixture coverage for common propagated exceptions
      such as `IndexOutOfRangeException`, `InvalidCastException`,
      `ObjectDisposedException`, `FormatException`, and `OverflowException`.
- [ ] Keep nullability, arithmetic, and path-feasibility growth bounded and
      regression-tested instead of turning PS0010 into speculative symbolic
      execution.

Done when:

- Exception diagnostics for trusted metadata methods are driven by validated
  summaries and fixture-backed regressions instead of ad hoc assumptions.

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
- [ ] Keep the manifest, generator registry, and deterministic shape-targeting
      tests in sync so every `generator_backed` shape stays reachable on
      purpose, not by fuzzing luck.

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
- [ ] Keep effect-summary generated evidence and catalog policy in distinct
      files or stages so reviewed policy changes do not get hidden inside tool
      output churn.

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
- [ ] Periodically collapse repo comments or tests that still describe fixed
      behavior as limitations so this file stays the only remaining-work view.

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
9. Keep `docs/effect-summary.md` and `README.md` descriptive only, with no
   parallel remaining-work lists.
