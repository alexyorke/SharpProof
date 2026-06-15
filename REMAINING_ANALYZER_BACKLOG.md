# Remaining PurelySharp Analyzer Backlog

This is the single canonical backlog and status file for unresolved analyzer
work. The older gap trackers have been folded into this file:

- `REFACTORING_STATUS_PURELYSHARP.md`
- `IMPLEMENTATION_TODO.md`
- `FUZZ_BACKLOG.md`

They should stay deleted. `README.md`, analyzer release notes, and focused
design docs such as `docs/effect-summary.md` are not backlog files.

## Current repo truth

- Last confirmed full `PurelySharp.Test` baseline: `2092/2092` green.
- The analyzer already has:
  - explainable `PS0002` evidence properties
  - `PS0010` exception reporting and effect-summary tooling
  - manifest-backed Roslyn shape coverage tests
  - deterministic and random fuzz infrastructure
- exact concrete dispatch narrowing for locals, aliases, casts, and
    same-concrete conditional, `??`, or `if`/`else` merges
  - explicit rules for anonymous object creation, inline array access, and
    implicit indexer reference
- Remaining work is now mostly precision and trust calibration, not broad
  syntax enablement.

## P0 - Real correctness and precision gaps

### 1. Fresh mutable return precision beyond the reviewed trusted-return subset

Evidence:
- `PurelySharp.Test/CryptographyTests.cs`
- `PurelySharp.Test/ConstantsTests.cs`
- `PurelySharp.Analyzer/Engine/Constants.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Current repo evidence:
- `SHA256.HashData(byte[])` and `MD5.HashData(byte[])` are cataloged as
  known pure members and as reviewed trusted fresh-array producers.
- `Convert.FromBase64String(string)`, `Guid.ToByteArray()`, and
  `BitConverter.GetBytes(...)` are also now in the reviewed trusted
  fresh-array subset.
- Signature validation exists in `ConstantsTests`.
- Direct and stable-local returns of those reviewed `HashData`,
  `Convert.FromBase64String`, `Guid.ToByteArray`, and `BitConverter.GetBytes`
  results now pass.
- The broader conservative policy still remains for other returned array
  producers such as `BitConverter.GetBytes`, `string.Split`, `Encoding.GetBytes`,
  `ToArray`, and direct fresh array returns.

Remaining:
- Evaluate additional reviewed members that deterministically return fresh
  owned arrays and can be trusted without broadening the whole return policy.
- Keep the current general conservative treatment for other returned mutable
  arrays and objects until they are individually proven or modeled.
- Keep factory, disposable, RNG, environment-backed, and stateful crypto
  APIs conservative.

Done when:
- Additional fresh-array false positives are closed through member-level
  reviewed trusted-return entries or stronger bounded proof, without
  broadening `System.Security.Cryptography` or other namespaces wholesale.

### 2. Dispatch precision beyond the current narrowing

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
- Recover concrete targets through deeper heterogeneous or nested branch and
  flow-merge cases beyond the currently supported exact-local, alias,
  cast-based, and same-concrete conditional, coalesce, or `if`/`else` merge
  cases.
- Distinguish provable virtual or interface dispatch from truly unknown
  dynamic dispatch.
- Improve static abstract interface member resolution in generic contexts.
- Keep mutable field and property receiver flows conservative unless the
  target is still provable.

Done when:
- Fewer `dynamic_dispatch` or unknown-target fallbacks remain in the targeted
  suites, with a narrow regression test for each newly recovered case.

### 3. Opaque external calls and effect-summary coverage

Evidence:
- `PurelySharp.Test/BoundaryAttributeTests.cs`
- `PurelySharp.Test/EffectSummaryToolTests.cs`
- `PurelySharp.Test/ExceptionSummaryCatalogValidationTests.cs`
- `PurelySharp.Analyzer/ExceptionFlowAnalyzer.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Remaining:
- Expand trusted consumed summaries for metadata-only or hidden
  implementation methods.
- Keep assembly name, hash, and MVID matching as the trust boundary.
- Add more end-to-end fixture coverage so emitted and consumed summaries stay
  aligned. Current boundary coverage now explicitly locks:
  - exact filename and suffixed `*.PurelySharp.EffectSummary.json` discovery
  - direct plus transitive exception-type union
  - multi-file summary merge for the same trusted symbol
  - wrong-symbol rows being ignored
  - malformed method rows being ignored
- Reduce conservative unknown-external-call fallbacks only when a trusted
  per-member summary exists.

Done when:
- Known external methods can be classified from trusted summaries without
  broad catalog whitelisting, and summary-boundary tests stay green.

### 4. Mutual recursion proof beyond direct self-recursion

Evidence:
- `PurelySharp.Test/FileScopedNamespacesTests.cs`
- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs`

Current repo evidence:
- Direct self-recursion is already handled more precisely.
- Mutual recursion is still intentionally conservative.

Remaining:
- Evaluate whether small mutually recursive pure cycles can be accepted
  without unsoundness.
- Keep recursion conservative when any cycle member touches mutable state,
  opaque calls, unknown dispatch, or unsupported constructs.

Done when:
- Simple provable cycles stop being flagged, while impure or unknown cycles
  still fail deterministically.

### 5. LINQ and enumerator precision follow-up

Evidence:
- `PurelySharp.Test/LinqOperationsTests.cs`
- `PurelySharp.Test/LinqSoundnessStressTests.cs`
- `PurelySharp.Test/CollectionExpressionSpreadTests.cs`
- `PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs`

Remaining:
- Preserve current source and secondary-source `GetEnumerator()` analysis as
  queries become more nested and wrapper-heavy.
- Review comparer, secondary delegate, and deferred-execution paths for more
  precise but bounded wins.
- Keep multi-source and wrapper-heavy cases conservative unless the source
  side stays provable.

Done when:
- Additional pure LINQ cases are accepted without weakening current
  impure-source and impure-enumerator regressions.

### 6. Fresh ownership and escape precision follow-up

Evidence:
- `PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs`
- `PurelySharp.Analyzer/Engine/Rules/AssignmentPurityRule.cs`
- ownership-sensitive tests across `BasicOperationsTests`, `BasicPureTests`,
  and `InlineArrayTests`

Remaining:
- Improve deeper aliasing, wrapper, loop, closure, and returned-fresh-object
  cases.
- Keep escape analysis local and predictable; do not turn it into a
  speculative whole-program proof.

Done when:
- More fresh-local mutation cases pass under bounded, test-backed ownership
  rules without regressing soundness.

### 7. Cryptography member-level review

Evidence:
- `PurelySharp.Test/CryptographyTests.cs`

Remaining:
- Continue splitting deterministic pure member operations from factories,
  disposables, RNG, and environment-backed helpers.
- Add only member-level catalog entries or trusted summaries.
- Do not broaden `System.Security.Cryptography` as a namespace or type-wide
  allowlist just to remove diagnostics.

Done when:
- Any new crypto allowances are per-member, evidence-based, and regression
  tested.

## P1 - Explicit conservative surfaces

### 1. Remaining conservative Roslyn operation shapes

Evidence:
- `PurelySharp.Test/RoslynConstructCoverageTests.cs`
- `PurelySharp.Test/FuzzToolTests.cs`
- `Tools/PurelySharp.Fuzz/Program.cs`

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

Work:
- Either implement bounded support or keep each remaining shape locked behind
  explicit conservative-contract tests and deterministic generator coverage.

### 2. Runtime surfaces that should stay conservative unless root proof exists

Evidence:
- Current tests still intentionally treat dynamic runtime binding,
  reflection, environment reads, filesystem or network access, time, culture,
  threading, synchronization, and unsafe pointer flows as conservative.

Work:
- Narrow only with real proof inputs such as trusted summaries, concrete
  source, or bounded rule logic.
- Avoid heuristic catalog broadening for environment-dependent APIs.

## P2 - Durability and tooling

### 1. Catalog tooling and validation

Evidence:
- The analyzer still relies on large member catalogs, and catalog correctness
  remains central to trust.

Remaining:
- Keep catalog entries member-level.
- Continue conflict detection and signature validation against supported
  frameworks where practical.
- Separate generated evidence from hand-maintained policy so catalog edits
  stay reviewable.

### 2. Broader performance and caching coverage

Evidence:
- `PurelySharp.Test/CachingTests.cs` covers repeated-query and deep
  call-chain reuse, but larger dispatch-heavy, summary-heavy, and
  memory-behavior scenarios are still thinner than the main correctness
  suites.

Remaining:
- Add bigger dispatch-heavy and summary-heavy scenarios.
- Watch for cache growth and repeated semantic-query regressions.
- Keep perf checks deterministic and non-flaky.

### 3. Artifact-driven backlog refresh

Evidence:
- The repo has Roslyn coverage and fuzz artifacts, but this file is still
  hand-curated.

Remaining:
- Use corpus and fuzz outputs to refresh this file from evidence instead of
  ad hoc notes.
- Promote repeated findings into deterministic regressions quickly.

### 4. Documentation discipline

Remaining:
- Keep `README.md` aligned with shipped behavior.
- Keep this file as the only backlog or status doc for remaining analyzer
  work.
- Do not reintroduce parallel status, gap, todo, or fuzz-backlog markdown
  files.

## Suggested execution order

1. Fresh mutable return precision for known-pure library methods.
2. Dispatch precision.
3. External-call and effect-summary coverage.
4. Mutual recursion proof.
5. LINQ or broader ownership follow-up, whichever yields the next bounded
   green fix.
6. Conservative Roslyn shape tranche.
7. Catalog and perf durability work.
