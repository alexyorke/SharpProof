# SharpProof bug audit status

This file is the current, evidence-backed status ledger for the repository audit. It keeps unresolved findings, accepted limitations, deferred security/integrity work, rejected leads, and the detailed evidence needed to trace resolved fixes. The compact ledger below provides a quick index without requiring every historical report to be reread.

## Open and accepted findings

The latest audit wave ran against exact baseline
`ffe74fff1c852d073610cfbebc54c141521a25fb`. Its ten subsystem reports contain
46 candidate findings below. Each candidate remains open until the main agent
independently reproduces it, adds a regression test, implements the fix, and
removes the detailed entry in the corresponding fix commit.

## Deferred by explicit scope

The following findings concern cybersecurity, raceable trust decisions, or filesystem durability/integrity. They are recorded for a separate security review and were not implemented in this audit, per the user's explicit no-cybersecurity instruction.

## Rejected or reclassified leads

- **1-3:** `ArgumentNullGuard` assignments are intentional null-state narrowing/field initialization patterns, not correctness bugs.
- **4:** `LazyThreadSafetyMode.ExecutionAndPublication` already supplies the required synchronization; no race was reproduced.
- **5:** Documentation breadth is maintenance debt, not an independently reproducible product defect; the documentation audit is tracked separately.
- **6:** `RegisterCompilationEndAction` is a valid Roslyn registration API; the naming claim was based on a mistaken signature assumption.
- **275:** Exact `Contract.Result<T>` nullability matching is intentional contract identity behavior and is covered by binder tests.
- **279:** The original silent profile/configuration disagreement report is superseded. Current configuration parsing detects conflicting aliases and reports the authoritative invalid-configuration diagnostic; no silent shadowing remains.
- **317:** The GUID-bearing compiler-manifest path was replaced with a stable compiler-visible source path and the unchanged-build editorconfig regression is covered by `8127933fc`. Target-level verifier reuse remains intentionally deferred because an inputs/outputs-only skip could bypass repeated refutation and infrastructure checks; a persisted canonical status fingerprint is required before changing that behavior.
- **369:** Explicit-interface implementation admission is fixed by `fa58c7533`; static constructors remain intentionally fail-closed because type-initialization ordering and replay evidence are not modeled.
- **366:** Leading-double-slash path identity divergence was not reproduced by the canonical Linux/.NET path implementation; no publication split was observed.
- **371:** `[SharpProofSuppress]` is documented and tested as analyzer-reporting policy only; collector verification remaining active is intentional fail-closed behavior.
- **412:** The claimed Gates RS0030 build failure was not reproduced; the current Release Gates build is clean and the remaining mutation calls are intentional harness code.
- **417-419:** The reported Linux backslash failures were disproved by canonical PowerShell `Join-Path` normalization and passing path-authority probes.
- **Latest Scout 1, Finding 1:** Reduced extension invocations retain the
  receiver in `IInvocationOperation.Instance` and expose only the remaining
  parameters through the reduced target's argument ordinals. The focused
  analyzer regression reports exactly the receiver and declared-argument
  violations while accepting the satisfied call; the reported shift was not
  reproduced.
- **Latest Scout 1, Finding 2:** Effects call contexts use the same reduced
  invocation convention. A focused `[DoesNotThrow]` regression proves the
  satisfied receiver/argument pair and rejects each invalid actual separately;
  the alleged argument shift was not reproduced.

## Resolved in this branch

Resolved reports are removed after reproduction, implementation, regression testing, and review. This compact table preserves the local evidence anchors.

| Findings | Resolution commit(s) |
| --- | --- |
| 151 | `7e3ef5c8e` (UTF-16 sequence/null-tag SMT encoding and replay tests) |
| 280 | `8d166cad1` (defined divide/remainder cases and retained seed evidence) |
| 284 | `8d166cad1`, `4d2749126` (semantic-cache marker, field/compound alias coverage) |
| 285 | `8d166cad1` (semantic Roslyn outcome-construction architecture scan) |
| 324 | `e5850507a` (audited Roslyn construction and whole-compilation diagnostics boundary) |
| 409 | `6462246f7` (protocol answer catalog and guarded semantic-cache writes) |
| 317 | `8127933fc` (stable compiler-visible manifest source path and incremental regression) |
| 369 | `fa58c7533` (explicit-interface implementation boundary; static constructors remain fail-closed) |
| 364 | `02a645e69` (regular-file validation for private verifier paths) |
| 337 | `cc0f2bc6b` (validated recovery for interrupted release-bundle swaps) |
| 273 | `91636b24f` (directory synchronization after publication deletion) |
| 202 | `0a2c179f9` (runtime companion path validation and generated launcher coverage) |
| 257-262 | `68afb8ca1`, `c3ab72290`, `8bd08c6e0` |
| 263-270 | `0c9e0ec0d`, `0c95dad38`, `0a2c179f9`, `a7b99ca24` |
| 274, 276 | `549c76510` |
| 277 | `68afb8ca1` (bounded summary dependency regression) |
| 278 | `68afb8ca1` |
| 281-283 | `68afb8ca1`, `0a2c179f9`, `a7b99ca24` |
| 286-287 | `a7b99ca24` |
| 288 | `4d2749126` (unknown event receivers retain add/remove accessor effects) |
| 295 | `47b8d6f7b` (captured closure state and allocation effects) |
| 403 | `616f9e619`, `6448cab79`, `f00be7ef3` (authoritative pre-manifest failure rebinding) |
| 410 | `b92cba235`, `f9e77c5b4` (factory method-group delegate inspection) |
| 326-327 | `f336f1213` (meta-analyzer recursive fragments and interface storage) |
| 351 | `8e34fcfca` (nonblank retired-mode alias fallback) |
| 422 | `0ef01b488` (preflight timing evidence remains recordable) |
| 296 | `8c71195e9` (canonicalization arity guards and typed regression) |
| 311 | `9bcb41bd5` (public evidence-authority validation overloads) |
| 350 | `adc74ffaa` (banner-aware generated header detection) |
| 396 | `bf772d063`, `87da87603` (attribute-aware metadata companion prefilter and metadata-table fast path) |
| 411 | `3cf5c3747`, `39dc0ab87` (cataloged semantic strings and static-constructor-safe readonly inference) |
| 416 | `acdf88263` (inventory-driven semantic framework identity scan) |
| 423 | `87da87603` (package tests honor `SHARPPROOF_REPO_ROOT` under isolated coverage output) |
| 271 | `8774a4daa` (verified Z3 loading remains bound to the validated inode) |
| 272 | `2430007b0` (publication reads use no-follow directory handles) |
| 318 | `3be94f0b0` (retained cleanup failures stay inside the task boundary) |
| 406 | `b83fc87b8` (worker request/result inode aliases are rejected) |
| 407 | `ebfaa3e50` (resolver publication gate blocks callbacks until the verified handle is published) |

The deferred security/containment findings addressed in this branch have dedicated threat-model review and focused regression evidence in their resolution commits. Future changes to those areas should preserve that validation boundary.

## Active, deferred, and rejected findings

The 46 detailed findings below are pending independent reproduction and TDD
resolution. Historical findings remain represented by the compact resolution
and reclassification ledgers above.

## [Bug hunt 2026-08-29T18:40:38Z] Scout 1 — Core analyzer logic (SharpProof.Analyzer.Core, SharpProof.Analyzer)

## Finding 3 — Inverted receiver condition for pattern-based `foreach` over an extension `GetEnumerator`
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\RequiresCallSiteDiscovery.cs`
- **Function/method + containing type:** `GetForEachCalls` (vs. `GetAwaitCalls`) in sealed partial class `RequiresCallSiteDiscovery`
- **Line number(s):** 1467–1470 (`GetForEachCalls`), contrast 1497–1499 (`GetAwaitCalls`)
- **Bug description:** `GetForEachCalls` sets the synthesized call's receiver to the collection only when the enumerator method is a **non-reduced instance** method:
```csharp
var instance = !getEnumerator.IsStatic && getEnumerator.ReducedFrom == null
    ? forEach.Collection
    : null;
```
For the pattern-based C# 9 `GetEnumerator` **extension method** (IsStatic, `ReducedFrom != null`) the instance is dropped. Under the file's own modeling convention (the receiver is carried via `Instance`), a `Requires` on the extension enumerator's receiver parameter then resolves to a null actual → `Unknown` → the foreach's precondition is silently never verified. The sibling `GetAwaitCalls` three methods down uses the correct opposite condition (supplies the awaited operation for the reduced-extension case). The two adjacent methods implement the same concept with mutually inverted conditions; the foreach variant is the lossy one (conservative miss, no false positives).
- **Category:** logic — **Severity:** medium — **Confidence:** 0.7

## Finding 4 — `ContractFor` companion validation is skipped under `sharpproof_profile=off`, contradicting the adjacent comment
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\SharpProofAnalyzerEngine.cs`
- **Function/method + containing type:** `InitializeCompilation` in sealed partial class `SharpProofAnalyzerEngine`
- **Line number(s):** 41–51 (Off-profile early return) vs. 53–59 (registration it claims precedes all early returns)
- **Bug description:** The comment states the companion validator is registered "before configuration/activation early returns so invalid companions are never hidden by an unrelated configuration diagnostic" — but the `SharpProofProfile.Off` early return at lines 41–51 executes **before** `context.RegisterCompilationEndAction(ValidateContractForCompanions)` at line 58. With `sharpproof_profile=off`, every `[ContractFor]` companion is completely unvalidated (no SPCF0001–SPCF0008 diagnostics), which is exactly the "hidden by configuration" scenario the comment says must not happen. Either the early return is wrong (Off should still validate companions) or the comment is stale; as written the behavior contradicts the stated invariant.
- **Code excerpt:**
```csharp
if (configuration.Profile == SharpProofProfile.Off)
{
    if (!configurationDiagnostics.IsEmpty) { ... }
    return;                       // skips the registration below
}
// ContractFor validation is a final-compilation reconciliation. ... Register it
// before configuration/activation early returns ...
context.RegisterCompilationEndAction(ValidateContractForCompanions);
```
- **Category:** config / logic — **Severity:** medium — **Confidence:** 0.7

## Finding 5 — `OriginalDefinition` vs. constructed-symbol mismatch breaks nested generic local-function analysis
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\RequiresCallSiteTreeAnalyzer.cs`
- **Function/method + containing type:** `GetNestedCallables`, `TryCollectLocalReferences`, `GetReachableLocalFunctions`, `AnalyzeGraph` in nested class `TreeAnalysis`
- **Line number(s):** 343–347 and 350–368 (`GetNestedCallables`), 490–497 (`TryCollectLocalReferences`), 439–447 (`GetReachableLocalFunctions`), 196–198 (`AnalyzeGraph` ownership check)
- **Bug description:** `GetNestedCallables` normalizes local functions to their **templates**: `ContractClauseInventoryBuilder.NormalizeCallable(method).OriginalDefinition` (lines 345–347, 492–493). But `potentialOwners` is built from `NormalizeCallable(owner)` **without** `OriginalDefinition` (`RequiresCallSiteDiscovery.GetPotentialCallOwners`, lines 75–77), where `owner` comes from `GetEnclosingSymbol` and is the *constructed* symbol for local functions inside generic methods. `SymbolEqualityComparer.Default` does not equate `LocalFunc<int>` with the `LocalFunc<T>` template, so:
  - `potentialOwners.Contains(caller)` fails in `AnalyzeGraph` for generic local functions (the nested callable is never analyzed), and
  - `graph.GetLocalFunctionControlFlowGraph(method)` is called with the template (line 439), which Roslyn rejects for a graph whose `LocalFunctions` are constructed — throwing `ArgumentException`, caught at lines 442–447 which silently degrades to "all candidates reachable".
  Net effect: for generic local functions, nested requires-call-site analysis never runs and the owner is recorded `Unknown` by the leftover-owners loop (lines 160–175) — a conservative miss (never a wrong Proven/Refuted), plus `_visitedPotentialOwners` is seeded with templates (lines 352–354) that can never match the constructed owners it is meant to deduplicate against.
- **Code excerpt:**
```csharp
var localMethods = ImmutableHashSet.CreateRange<IMethodSymbol>(
    SymbolEqualityComparer.Default,
    graph.LocalFunctions.Select(
        static method => ContractClauseInventoryBuilder
            .NormalizeCallable(method).OriginalDefinition));   // template
...
child = graph.GetLocalFunctionControlFlowGraph(
    method, cancellationToken);                                // template vs constructed
```
- **Category:** logic / api-misuse — **Severity:** low — **Confidence:** 0.6

## Finding 6 — Unreachable switch arm: `ConversionOperatorDeclarationSyntax` case can never execute
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\AnalyzerSyntaxHelpers.cs`
- **Function/method + containing type:** `GetCallableDeclarationLocation(SyntaxNode)` in static class `AnalyzerSyntaxHelpers`
- **Line number(s):** 19–21 (dead arm within the switch at 7–23)
- **Bug description:** `ConversionOperatorDeclarationSyntax` derives from `OperatorDeclarationSyntax` in the Roslyn syntax API, and the switch tests `OperatorDeclarationSyntax` first. The later `ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.GetLocation()` arm is unreachable, so all conversion operators report the operator keyword position instead of the intended `implicit`/`explicit` keyword position. Dead code + wrong diagnostic location vs intent.
- **Category:** logic — **Severity:** low — **Confidence:** 0.85

Scout 1 clean areas (verified, no bugs): SharpProofAnalyzer.cs registration; AnalyzerSession.cs; AnalyzerGeneratedCodePolicy.cs; ManagedContractFacts.cs; CallArgumentAliasPolicy.cs; ClosedContractDiagnostics.cs; InvalidContractArgumentDiagnostics.cs; ContractRuntimePolicy.cs; EffectEvaluationTypes.cs; generated catalogs (EffectEvaluationProjections, EffectEvaluationProducerTupleCatalog, AnalyzerDiagnosticCatalog, DeclarativeModels); both descriptor tables (SP0002…SP0050, SPCF0001…SPCF0008); CompilerExceptionTypeIdentity.cs; Configuration/AnalyzerConfiguration*.cs; ContractForValidation/*; SynthesizedRecordCallAnalysis.cs; PrimaryConstructorCallableInventory.cs; EffectContractDiagnostics.cs; LanguageSubsetGate.cs; AnalyzerFeaturePipeline.cs; SharpProofControlAttributePolicy.cs; remainder of RequiresCallSiteDiscovery.cs / RequiresCallSiteTreeAnalyzer.cs.
## [Bug hunt 2026-08-29T18:41:58Z] Scout 2 — Dataflow & IR (SharpProof.Dataflow, SharpProof.Ir)

## Finding 1 — Non-canonical lattice representations break `IntervalValue` equality/hash contract
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Dataflow\IntervalDomain.cs` (lines 34–46, `Create`; line 285, `TryAddBounds` caller in `Add`) and `C:\w\PurelySharp-bug-hunt\SharpProof.Dataflow\IntervalValue.cs` (lines 98–129, `Equals`/`GetHashCode`)
- **Function/method + containing type:** `IntervalDomain.Create` / `IntervalValue.Equals(IntervalValue)` / `IntervalValue.GetHashCode`
- **Line number(s):** IntervalDomain.cs 34–40 (canonicalization guard), 277–288 (`TryAddBounds`); IntervalValue.cs 98–129
- **Bug description:** `[long.MinValue, k]` and `(-∞, k]` denote the *same* set of 64-bit integers, but `Create` only canonicalizes bounds to `null` when **both** bounds are null-or-extreme simultaneously (lines 34–40). The two representations are lattice-equivalent — `LessThanOrEqual` holds in both directions, so `ClosedAbstractDomain.AreEquivalent` (ClosedAbstractDomain.cs lines 21–24) returns `true` — yet `IntervalValue.Equals` compares raw nullable bounds, so they are unequal with different hash codes. Reachable at runtime: `IntervalDomain.Add(Range(long.MinValue, 0), Range(0, 5))` produces lower bound exactly `long.MinValue` via `TryAddBounds` (BigInteger sum lands in range, lines 284–286), yielding `[long.MinValue, 5]` mod 1, while `Range(null, 5)` yields `[null, 5]` — two keys for one abstract fact. Any consumer deduplicating/caching by `IntervalValue` (or `SequenceCardinalityValue`, which embeds `Length`) splits or duplicates state; only the fixpoint engine is immune because `ForwardDataflowAnalysis` uses `AreEquivalent` (ForwardDataflowAnalysis.cs lines 145, 178, 203) rather than `Equals`. Tests exercise `Range(long.MinValue, long.MaxValue)`, confirming extreme-bound values occur.
- **Code excerpt:**
```csharp
// IntervalDomain.Create, lines 34–40 — both bounds must qualify or neither is canonicalized
if (modulus.IsOne &&
    (!lowerBound.HasValue || lowerBound == long.MinValue) &&
    (!upperBound.HasValue || upperBound == long.MaxValue))
{ lowerBound = null; upperBound = null; }
...
// IntervalValue.Equals, lines 100–105 — raw bound comparison
return _hasValue == other._hasValue &&
       (!_hasValue || LowerBound == other.LowerBound && ...);
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.55

## Finding 2 — All malformed-operand evaluation failures reported as `InvalidVariableValue`
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Ir\IrInterpreter.cs`
- **Function/method + containing type:** `IrInterpreter.InvalidValue` (private static), consumed by `EvaluateUnary`, `EvaluateBinary`, `EvaluateIntegerBinary`, `EvaluateBooleanBinary`, `EvaluateEquality`, `EvaluateStringConcat`, `EvaluateConditional`, `EvaluateCast`, `EvaluateLength`, `EvaluateSequenceAccess`
- **Line number(s):** 534–537 (definition); call sites 248, 272–274, 316, 337, 350, 367, 375, 393, 438, 465, 497, 502
- **Bug description:** `InvalidValue` hardcodes `IrUnsupportedReason.InvalidVariableValue` as the reason code for *every* runtime type/shape failure — "Boolean negation requires a boolean value", "A conditional guard requires a boolean value", "Equality requires values with the same type", "Sequence access requires a sequence value", "Length requires a string or sequence value", etc. That enum member (IrModel.generated.cs line 83) is named for, and semantically means, a bad *variable binding* — the legitimate use is `EvaluateVariable` (lines 188–196, which also correctly uses `MissingVariable`). The enum has an apt member available (`UnsupportedOperation`). Downstream consumers of the public `IrEvaluationResult.Unsupported.Reason` cannot distinguish "the caller bound a wrong-typed variable" from "this IR term is ill-typed at runtime", so any switch on `Reason` misclassifies half the cases.
- **Code excerpt:**
```csharp
private static IrEvaluationResult InvalidValue(string detail)
{
    return Unsupported(IrUnsupportedReason.InvalidVariableValue, detail);
}
```
- **Category:** api-misuse — **Severity:** low — **Confidence:** 0.5

Scout 2 clean areas (verified high-suspicion items, not bugs): widening/termination in ForwardDataflowAnalysis (183–208); join/meet laws of NullnessDomain/IntervalDomain/SequenceCardinalityDomain; DataflowGraph CFG construction (contiguous IDs, edge dedup/sort, FindCyclicBlocks DFS); equality/hashing of ScopedIrId/IntervalValue/StructuralKey/IntSequenceKey/ExternalIdentityKey/IrValue; static counters IrFactory.s_nextScope vs IrProgramBuilder.s_nextScope (all 22 scope comparisons type-appropriate); checked integer arithmetic incl. long.MinValue % -1; off-by-one checks (step limit, depth caps 256/256, TryCongruentBoundary, TryAddBounds, CanonicalHashWriter frame encoding, AtomicFile.Publish retry 1 << min(attempt,6)); exception swallowing (deliberate OverflowException→Top and IO sweeps); resource handling (CanonicalHashWriter dispose idempotency, staged-file cleanup, File.Replace/Move retry guards).
## [Bug hunt 2026-08-29T18:43:50Z] Scout 3 — Solver & verification (SharpProof.Smt, SharpProof.Verifier, SharpProof.Verify)

### Finding 1
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Smt\IrSmtBackend.cs`
- **Function/method + containing type:** `AccountResources(Solver solver)` — containing type `IrSmtBackend`
- **Line number(s):** 312–346 (guard at 320–327)
- **Bug description:** Resource accounting only accumulates the Z3 `"rlimit count"` statistic when the entry is a 32-bit unsigned integer (`entry.IsUInt`). Z3's rlimit counter is context-cumulative across the per-check solvers this backend creates (one `MkSolver()` per `CheckCore`), and Z3 statistics promote values to `double` once they exceed the unsigned 32-bit range. With the default `QueryRlimit` of 3,000,000 per check, roughly 1,432 heavily-consumed checks push the cumulative counter past `uint.MaxValue`; at that point the entry is no longer `IsUInt`, the loop `continue`s past it, and accounting silently stops: `ConsumedResourceCount` freezes, `_lastResourceSnapshot` stops advancing, and `_resourceAccountingExhausted` can never be set via this path. The "retire backend on resource-accounting overflow" feature (lines 330–341, tested by `ResourceAccountingOverflowPreservesCurrentResultAndRetiresBackend`) silently degrades on long verification runs. No unsoundness (Z3 itself still enforces each check's rlimit), but the observability/exhaustion contract silently fails.
- **Code excerpt:**
```csharp
if (!string.Equals(entry.Key, "rlimit count", StringComparison.Ordinal) ||
    !entry.IsUInt)
{
    continue;
}
var observed = entry.UIntValue;
```
- **Category:** logic, **Severity:** low, **Confidence:** 0.5

### Finding 2
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Verifier\buildTransitive\SharpProof.Verifier.targets`
- **Function/method + containing type:** Property-group conditions in `_SharpProofInitializeVerify` / `_SharpProofValidateRuntimeClosure` (MSBuild targets file, no C# type)
- **Line number(s):** 9–12 (worker/launcher path defaults), 44–47 (`_SharpProofValidateRuntimeClosure` SP0054 closure validation); same pattern in `SharpProof.Verifier.props` lines 9–12 and targets lines 84–94, 154–162
- **Bug description:** Path operands in MSBuild condition comparisons are left unquoted, e.g. `Condition="$(_SharpProofToolsDirectory) != $(_SharpProofPackageToolsDirectory) OR $(_SharpProofWorkerPath) != ..."` and `Condition="!Exists($(_SharpProofPackageNativeZ3Path))"` and `Condition="$(_SharpProofWorkerPathConfigured) == ''"`. MSBuild's condition tokenizer splits unquoted operands on whitespace, so any path containing a space (or parenthesis, e.g. `Program Files (x86)`-style or redirected `NUGET_PACKAGES`/`SharpProofToolsDirectory` paths) makes the condition fail to parse (MSB4025/MSB4184-style build break) rather than evaluate. The failure is fail-closed (a broken build, not a wrong verification result), but the SP0054 diagnostics that are supposed to produce clear errors become unparseable-expression noise, and the configured-path detection at lines 9–12 errors out before the intended validation runs. Every other condition in the file correctly quotes its operands, so these are omissions.
- **Code excerpt:**
```xml
<Error Code="SP0054" Condition="$(_SharpProofToolsDirectory) != $(_SharpProofPackageToolsDirectory) OR $(_SharpProofWorkerPath) != $(_SharpProofPackageWorkerPath) OR ..."
       Text="SharpProof verifier runtime paths must resolve to the exact package-owned runtime closure." />
<Error Code="SP0054" Condition="!Exists($(_SharpProofPackageNativeZ3Path))"
       Text="SharpProof verifier package native payload is missing from its build-tool closure." />
```
- **Category:** config, **Severity:** low, **Confidence:** 0.5

### Finding 3
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Smt\IrSmtBackend.cs`
- **Function/method + containing type:** `Interrupt()` — containing type `IrSmtBackend` (interacts with `Dispose()` and `CheckAsyncCore`)
- **Line number(s):** 176–180 (`Interrupt`), 111–150 (registration scope), 182–201 (`Dispose`)
- **Bug description:** `Interrupt()` calls `Volatile.Write(ref _interrupted, true)` and `_context.Interrupt()` without taking `_gate` or checking `_disposed`. The registration (`cancellationToken.Register(..., this)`, line 125) is created and disposed entirely inside the `lock (_gate)` critical section of the check body, and `Dispose()` also requires `_gate`, so a callback cannot observe a disposed context in the normal flow — currently safe. The residual hazard is the synchronous-callback path at lines 125–127 (already-canceled token fires `Interrupt` during `Register`) combined with a user-supplied `_stringLiteralProgress` callback (line 1052) or `contextFactory` callback re-entering `Dispose()` from the same thread: `lock (_gate)` is reentrant, so `Dispose` would run `_context.Dispose()` while the same thread is mid-encoding, and the resulting `Z3Exception`/`ObjectDisposedException` escaping `Z3ExpressionOwner.Dispose()` (lines 49–52, no try/catch) propagates as a raw exception out of `CheckAsync`, violating the `ProofKernel`'s expectation that infrastructure failures surface as typed results. Only reachable through the internal test/fuzz constructor's callbacks, so impact is confined to test harnesses.
- **Code excerpt:**
```csharp
private void Interrupt()
{
    Volatile.Write(ref _interrupted, true);
    _context.Interrupt();
}
```
- **Category:** concurrency, **Severity:** low, **Confidence:** 0.3

Scout 3 clean areas (verified, not bugs): SMT term/constraint generation (QueryEncoder: AndAlso/OrElse short-circuit definedness, conditional ITE definedness/null-flag propagation, string equality with null flags, concat with null-as-empty, length-of-null undefined, truncating division/remainder definedness exclusions, Bounded overflow-as-undefined — matches IrInterpreter and pinned tests); UTF-16 prefix constraints and malformed-suffix handling; solver-result interpretation (refutation encoding, UNSAT-core label mapping with fail-closed MalformedResult, malformed-model retry budget, ClassifyUnknown); timeout/cancellation gate admission and dispose lifecycle; ProofKernel (SAT-model shape validation, assumption replay, foreign-cancellation isolation, Proven/Refuted-only cacheability); resource disposal (Z3ExpressionOwner ownership ordering, ExcludeModel/DecodeString local disposals); numeric conversions (range-guarded TryParse with correct NumberStyles).
## [Bug hunt 2026-08-29T18:44:21Z] Scout 4 — Contracts / attributes / specs (SharpProof.Contracts, SharpProof.Attributes, SharpProof.Specs)

### Finding 1 — Contradictory Nullness/Cardinality facets silently mark a nullable Result as provably non-null (unsound totality gate)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\ApiSpecTermValidator.cs` (lines 38–45), with enabling gap in `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\ApiSpecTable.cs` `NormalizeFacets` (lines 306–371)
- **Function:** `ApiSpecTermValidator.Validate`, case `SpecVariableDeclaration`
- **Bug description:** A Result-role spec variable is declared non-null if `Nullness.Result == NonNull` **or** `Cardinality.Result` is Empty/NonEmpty/Exact. `NormalizeFacets` never cross-validates Nullness against Cardinality, so a trusted spec can declare `Nullness = SpecNullness.Null` (result is always null) or `MaybeNull` together with `Cardinality = NonEmpty`/`Exact(0)`. The `&&`/`||` precedence then yields `nonNull = true` for a result the facets say can be (or is always) null. `nonNull` feeds `TermFacts.IsNonNull`, which is what makes `SpecLengthDeclaration` "total" (`value.IsTotal && value.IsNonNull`), so a postcondition like `Length(Result) == n` passes the trusted-spec totality gate even though it is undefined/unsound when the result is null. A null result is not caught anywhere else in instantiation (`ApiSpecInstantiator` builds `factory.Length(child)` unconditionally).
- **Code excerpt:**
```csharp
var nonNull = info.Role == SpecVariableRole.Receiver ||
    info.Role == SpecVariableRole.Result &&
    (facets.Nullness.Result == SpecNullness.NonNull ||
     facets.Cardinality.Result is
         SpecCardinality.Empty or
         SpecCardinality.NonEmpty or
         SpecCardinality.Exact);
return new(info.Type, true, nonNull, null, null);
```
- **Category:** logic — **Severity:** low (trusted input required) — **Confidence:** 0.4

### Finding 2 — `ApiSpecTable.Create` misclassifies null-target declarations as "duplicate witness identifier" (null group key)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\ApiSpecTable.cs`
- **Function:** `ApiSpecTable.Create` (lines 52–72)
- **Bug description:** Duplicate detection groups by `declaration.Target?.WitnessIdentifier` *before* the null-target check in `CompileTemplate`. If the input contains a declaration with `Target == null` twice (or once alongside another null target), `GroupBy` places them under a null key, `group.Count() != 1` fires, and the thrown message is `"Spec witness identifiers must be unique: " + duplicate.Key + "."` which renders as `...unique: .` with an empty identifier — the caller is told witnesses collide when the real problem is a missing target (`"A spec target is required."` is only reachable for a single null target). Wrong error path + null in message interpolation.
- **Code excerpt:**
```csharp
.GroupBy(static declaration => declaration.Target?.WitnessIdentifier, StringComparer.Ordinal)
.FirstOrDefault(static group => group.Count() != 1);
if (duplicate != null)
{
    throw new ArgumentException(
        "Spec witness identifiers must be unique: " + duplicate.Key + ".", ...);
}
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.55

### Finding 3 — `DiscoverCompanionsCore` drops companions carrying 2+ malformed `ContractFor` attributes with no failure recorded
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Contracts\ContractForSymbolMatcher.cs`
- **Function:** `DiscoverCompanionsCore` (lines 167–195)
- **Bug description:** Only `attributes.Length == 1` is handled; the `else if` preserves a *malformed single* attribute, but a type with `attributes.Length > 1` is silently skipped (no `CompanionDescriptor`, no `ContractBindingFailure`). The stated design intent in the adjacent comment is to "preserve a malformed ContractFor declaration as a failed companion instead of dropping its intent." In C# duplicates are CS0579 errors because `AllowMultiple` is false, so reachability is limited to attribute data supplied outside normal C# compilation (e.g., language variants / synthetic compilations). Not reproducible from ordinary C# source, hence low severity — but the dead `else if` means the documented fail-closed invariant does not hold for this case.
- **Category:** logic — **Severity:** low — **Confidence:** 0.3

### Finding 4 — `ApiSpecContentDigest` cannot distinguish approved-assembly tokens that differ only in hex case (digest collision on distinct records)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\ApiSpecContentDigest.cs`
- **Function:** `Compute` (lines 21–28)
- **Bug description:** Assemblies are ordered with `ThenBy(item.PublicKeyToken, StringComparer.OrdinalIgnoreCase)` but hashed via `assembly.PublicKeyToken.ToUpperInvariant()`. Two `ApiSpecAssemblyIdentity` records whose tokens differ only in case (e.g., `"B77A5C561934E089"` vs `"b77a5c561934e089"` — both pass hex validation in `ValidateDeclaration`, and `Distinct()` treats them as different records) produce byte-identical hash input (same uppercased token, same ordinal sort position for equal keys). The content digest — used as a cache/version identity (`WorkerCacheIdentity`, package trust) — therefore cannot detect a change that only alters token case. Practical impact is minimal (comparisons elsewhere are case-insensitive), but the digest's purpose is exact content identity.
- **Code excerpt:**
```csharp
.OrderBy(static item => item.Name, StringComparer.Ordinal)
.ThenBy(static item => item.PublicKeyToken, StringComparer.OrdinalIgnoreCase)
...
hash.Add("assembly", assembly.Name, assembly.PublicKeyToken.ToUpperInvariant(), assembly.ReferenceFamily);
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.35

### Finding 5 — `EffectContractAttribute.ThrownExceptions` returns a mutable array from a get-only-looking contract surface
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Attributes\EffectContractAttribute.cs` (line 13)
- **Function:** `EffectContractAttribute.ThrownExceptions` property
- **Bug description:** `public Type[] ThrownExceptions { get; set; } = [];` exposes a settable property whose getter hands out the live array; any consumer (or the analyzer, after reading the attribute) can mutate the shared array instance in place, and the setter accepts null (making `ThrownExceptions` null despite `PublicAPI.Shipped.txt` declaring `-> System.Type![]!` non-nullable). Contrast with `ContractForAttribute`/`AllowedExceptionsAttribute`, which null-guard constructor input. Named-attribute-argument support does require a setter, but the setter lacks the `?? throw` guard used by the sibling attributes, and the array contents are not defensively copied.
- **Code excerpt:**
```csharp
public Type[] ThrownExceptions { get; set; } = [];
```
- **Category:** api-misuse — **Severity:** low — **Confidence:** 0.4

### Finding 6 — `ContractApiSymbols.FindGenericIntrinsic` throws uncaught `InvalidOperationException` on ambiguous intrinsic shapes
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Contracts\ContractApiSymbols.cs`
- **Function:** `FindGenericIntrinsic` (lines 59–70), called from `TryCreate` (lines 24–35)
- **Bug description:** `contract.GetMembers(name).OfType<IMethodSymbol>().SingleOrDefault(...)` — the throwing form of `SingleOrDefault` raises `InvalidOperationException` (not caught anywhere in `TryCreate` → propagates out of `ContractBinder`'s constructor) if a compilation's `Contract` type contains two static, arity-1 methods named `Result`/`Old` matching the parameter count. A user-defined `Contract` class that passes the identity/trust checks but has two matching members crashes the analyzer instead of failing closed with `ContractApiUnavailable`, which is the designed failure for an untrusted/invalid Contract API. The neighboring `ContractClauseSymbols.GetClauseKind` by contrast returns `null` (fail-closed) for every shape mismatch.
- **Code excerpt:**
```csharp
return contract.GetMembers(name)
    .OfType<IMethodSymbol>()
    .SingleOrDefault(method =>
        method.IsStatic && method.Arity == 1 &&
        method.Parameters.Length == parameterCount);
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.3

### Finding 7 — `SpecId.GetHashCode` mixes only low bits for common scopes (quality-only)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\SpecIdentifiers.cs` (line 32)
- **Function:** `SpecId.GetHashCode`
- **Bug description:** `unchecked(((int)Scope * 397) ^ (int)(Scope >> 32) ^ Value)` — for the real workload (sequential `Interlocked.Increment` scopes fitting in 32 bits), `(int)(Scope >> 32)` is always 0 and `(int)Scope * 397` collides heavily for many (scope, value) pairs. Hash-quality/perf issue, not correctness — `Equals` is exact. Dictionaries keyed on `SpecVarId` are hot in instantiation.
- **Code excerpt:**
```csharp
return unchecked(((int)Scope * 397) ^ (int)(Scope >> 32) ^ Value);
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.2 (quality-only, no misbehavior demonstrated)

Scout 4 clean areas (verified, no bugs): all [AttributeUsage] metadata vs consumers; attribute argument validation (InRange ordering/decode, unsigned-ulong exclusion, out-param rejection, NotNull, Positive/InRange→IntegerType re-checks); rule/ID/name consistency (ContractApiCatalog, ConditionalSymbol, ClosedAttributeName, BoundContractModel.generated vs schema.json); PublicAPI txt + xml coverage; culture handling (invariant everywhere); bounds/off-by-one (InRange bounds, MaximumTermDepth/Nodes iterative traversal, GetTreeOrdinal fallback); SpecId/SpecVarId Equals/GetHashCode symmetry; null guards on public entry points; concurrency (ConditionalWeakTable caches, CompanionCache under gate); JSON↔generated catalog parity (DefaultApiSpecCatalog, tableVersion 5, RelationalSpecPackCatalog); contract.old/contract.result Reference-typed placeholder semantics; immutability of ApiSpecTable/ApiSpecTemplate/BoundContract*; CanonicalHashWriter framing.
## [Bug hunt 2026-08-29T18:46:05Z] Scout 5 — Compiler integration & meta-analyzers (SharpProof.CompilerCollector, SharpProof.CompilerArtifact, SharpProof.ContractForGenerator, SharpProof.Meta.Analyzers)

### F1 — Line-map capture cannot represent two `#line` mappings on one physical line, producing wrong mapped path/line/column evidence
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs` (also consumer `C:\w\PurelySharp-bug-hunt\SharpProof.CompilerArtifact\CompilerSourceLocationAuthority.cs`)
- **Function:** `CompilerCompilationCapture.CaptureTree` (lines 158–175) interacting with `CompilerSourceLocationAuthority.TryMap` (lines 373–418) and `HasValidLocationGeometry` (lines 130–155)
- **Lines:** CompilerCompilationCapture.cs 167–174; CompilerSourceLocationAuthority.cs 388–407
- **Bug description:** `CaptureTree` emits exactly one `CompilerSourceLineMapEntry` per physical text line. For a line containing a `#line` directive mid-line, the entry's `MappedPath/MappedLine/MappedColumn` come from `GetMappedLineSpan(line start)` (the *previous* mapping's target), while `CharacterOffset` is taken from the *first* mapping whose span starts on that line (the new directive). `TryMap` then maps *any* offset on that line using this single mixed entry (`mappedColumn = MappedColumn + max(delta - CharacterOffset, 0)`), so for positions after the mid-line directive the mapped path/line/column are computed against the wrong mapping target. Downstream, `HasValidLocationGeometry`/`FindUniqueTree` either reject valid locations (location.Line == mappedLine+1 fails) or bind a location to the wrong tree identity, and worker-side replay/location authority checks validate against this incorrect evidence. Rare source shape, but it silently corrupts compiler location evidence rather than failing loudly.
- **Code excerpt:**
```csharp
var mapped = tree.GetMappedLineSpan(new TextSpan(line.Start, 0));
return new CompilerSourceLineMapEntry
{
    SourceStart = line.Start,
    ...
    MappedLine = mapped.StartLinePosition.Line,
    MappedColumn = mapped.StartLinePosition.Character,
    CharacterOffset = lineMappings
        .Where(mapping => mapping.Span.Start.Line == line.LineNumber)
        .Select(static mapping => mapping.CharacterOffset)
        .FirstOrDefault() ?? 0
};
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.45

### F2 — String-concat cascade suppression fails through parentheses, double-reporting one construction
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Meta.Analyzers\SharpProofSoundnessAnalyzer.cs`
- **Function:** `SharpProofSoundnessAnalyzer.AnalyzeCSharpExpressionText` (lines 612–642)
- **Lines:** 628–635
- **Bug description:** The dedup heuristic reports only the outermost `+` of a chained string concatenation when `binary.Parent is IBinaryOperation { OperatorKind: Add, Type.SpecialType: System_String }`. For `a + (b + c)` the inner `Add`'s operation parent is an `IParenthesizedOperation`, not a binary Add, so the suppression does not apply: both the inner and outer expression are reported (two `SPMETA009` diagnostics at different spans for a single source construction) — exactly the cascade the accompanying comment claims is prevented. The check should unwrap parenthesized/implicit parents before testing for a string-Add parent.
- **Code excerpt:**
```csharp
// A chained concatenation produces one operation for every `+`. Report
// the outer expression only ...
if (binary.Parent is IBinaryOperation
    {
        OperatorKind: BinaryOperatorKind.Add,
        Type.SpecialType: SpecialType.System_String
    })
{
    return;
}
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.5

### F3 — Cacheability-guard whitelist does not recognize short-circuit (`&&`/`||`) guards, causing false SPMETA010
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Meta.Analyzers\CacheSoundnessRules.cs`
- **Function:** `CacheSoundnessRules.IsInsideCacheabilityGuard` (lines 785–823), called from `AnalyzeWrite` (lines 15–35)
- **Lines:** 789–796
- **Bug description:** The guard whitelist only recognizes enclosing `IConditionalOperation` ancestors (ternary / `if`). In Roslyn's operation tree, `VerificationCache.IsCacheable(x) && cache.Add(key, x)` is an `IBinaryOperation` with `ConditionalAnd` — a bare expression statement of that shape has no `IConditionalOperation` ancestor, so `IsInsideCacheabilityGuard` returns false and `SPMETA010 NonCacheableSemanticAnswer` fires for a write that *is* guarded by the `IsCacheable` check. Only the `if (IsCacheable(...)) { ... }` and ternary spellings are whitelisted. A false-positive-prone soundness rule (no unsoundness, but incorrect diagnostics on valid guarded writes).
- **Code excerpt:**
```csharp
for (var current = write.Parent; current != null; current = current.Parent)
{
    if (current is not IConditionalOperation conditional)
    {
        continue;
    }
    foreach (var guard in conditional.Condition.DescendantsAndSelf()...
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.5

### F4 — `SingleOrDefault` over `VerifyAsync` overloads can throw inside the analyzer instead of degrading gracefully
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Meta.Analyzers\SharpProofSoundnessAnalyzer.cs`
- **Type/method:** `SharpProofSoundnessAnalyzer.KnownSymbols` constructor (lines 1093–1120)
- **Lines:** 1112–1119
- **Bug description:** `worker?.GetMembers("VerifyAsync").OfType<IMethodSymbol>().SingleOrDefault(candidate => ...)` uses the throwing form of `SingleOrDefault`, which raises `InvalidOperationException` when two members match the full predicate (e.g., a future `SharpProofWorker.VerifyAsync` overload that also matches request/cancellation shape and return type). An exception thrown during `KnownSymbols` construction (registered at compilation start) surfaces as an analyzer crash/ICE banner for the whole meta-analyzer instead of treating `WorkerVerifyAsync` as unmatched (the null-tolerant style used everywhere else in this file). `.SingleOrDefault(...)` should be guarded (e.g., `Take(2).Count() == 1` pattern) like the other optional-symbol lookups.
- **Code excerpt:**
```csharp
WorkerVerifyAsync = worker?.GetMembers("VerifyAsync").OfType<IMethodSymbol>().SingleOrDefault(candidate =>
    candidate is { IsStatic: false, Arity: 0, Parameters.Length: 2 } && ...)
```
- **Category:** api-misuse — **Severity:** low — **Confidence:** 0.35

### F5 — Response evidence authority silently accepts responses that omit manifest claim results
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.CompilerArtifact\CompilerResponseEvidenceAuthority.cs`
- **Function:** `CompilerResponseEvidenceAuthority.Validate` (lines 41–94)
- **Lines:** 84–90
- **Bug description:** For each target callable the loop iterates `target.Entry.ClaimIds` and only validates a claim if `claims.TryGetValue(claimId, out var claim)` succeeds; a response that drops rows for some manifest claims produces no error at all (unlike duplicate rows, which are flagged at lines 65–69). The type doc says this adapter "binds the free-form evidence rows in a worker response to the lowered compiler artifact", and `ValidateClaimlessCallable` shows coverage is expected to be classified — but a claim-less-but-nonempty case (`Entry.ClaimIds.Length > 0` with rows missing) is neither validated nor flagged here. If per-claim coverage is enforced only in the out-of-area result assembler, this adapter trusts an incomplete response; at minimum it is an unvalidated authority gap relative to the duplicate-detection strictness a few lines above.
- **Code excerpt:**
```csharp
foreach (var claimId in target.Entry.ClaimIds)
{
    if (claims.TryGetValue(claimId, out var claim))
    {
        ValidateClaim(target, claim, indexes, errors);
    }
}
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.4 (may be deliberate; coverage may be enforced by the worker result assembler outside this area)

Scout 5 clean areas (verified, no findings): ContractForValidatorGenerator.cs (intentionally empty incremental generator); MetaDiagnosticDescriptors.generated.cs; KnownTypeNames[] vs KnownType enum (all 44 parallel-array entries aligned); CompilerWireMappings.generated.cs (ToWorkerEffects/ToWorkerCapabilities flag-for-flag vs SharpProof.Effects and Worker.Protocol); CompilerImplementationIlSummaryLowerer IL translator (WrapInt32/WrapInt64Add/WrapInt64Multiply/SplitUnsignedInt64/MultiplyUnsignedInt32 branch-by-branch vs IrInterpreter checked semantics; opcode contiguity; empty-stack proof); PortableIrGraphCodec (depth limits, cycle detection, slot catalogs); CompilerManifestArtifactJson.CreateJsonOptions (fresh WorkerProtocolJson.Options copy per call); ClaimManifestBuilder (manifest parity, ordinal continuity, duplicate-rank IDs, TryCreate-guaranteed non-empty Replay.Events); CompilerSpecificationPackProvider; FinalCompilationCollector/FinalCompilationCollectorAnalyzer; generated model files; path handling (NormalizePath, ResolveSiblingModule, NormalizeIdentityPath); concurrency (immutable statics, _active re-entrancy guard).
## [Bug hunt 2026-08-29T18:46:37Z] Scout 6 — Host / worker / IPC / frontend / testing (SharpProof.Host, SharpProof.Worker, SharpProof.Worker.Protocol, SharpProof.Worker.Launcher, SharpProof.Testing, SharpProof.Frontend)

### 1. `SarifProjection.MergeRuns` — index computed over the unfiltered run array is applied to the null-filtered list
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Worker.Launcher\SarifProjection.cs`
- **Method:** `SarifProjection.MergeRuns`
- **Lines:** 126–150 (mismatch at 138–145)
- **Bug description:** `replacement` is found by scanning `existingRuns` (the raw `JsonArray`, which may legitimately contain JSON `null` entries — the code explicitly defends against null runs). But the list it later indexes, `runs`, is built by `existingRuns.Where(run => run != null)`, removing nulls. If any null entry precedes the matched run, `runs[replacement]` replaces the **wrong run** (shifted by the number of filtered nulls) or throws `ArgumentOutOfRangeException` (e.g., `existingRuns = [null, run]`, match at index 1, `runs.Count == 1`). `ArgumentOutOfRangeException` is not in the `PublishOutputs` catch list (Program.cs lines 257–260), so it escapes as an unhandled process crash with no result file instead of a typed failure.
- **Code excerpt:**
```csharp
for (var index = 0; index < existingRuns.Count; index++)
{ if (existingRuns[index] is JsonObject run && ... ProjectRoot(run) == currentRoot) { replacement = index; break; } }
var runs = existingRuns.Where(static run => run != null)... .ToList();   // filtered!
...
if (replacement >= 0) { runs[replacement] = currentClone; }              // unfiltered index
```
- **Category:** logic — **Severity:** medium — **Confidence:** 0.7

### 2. `LinuxWorkerProcess.WaitForExit` holds the synchronization lock for the entire (potentially minutes-long) blocking wait, so `Dispose` cannot kill the child
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Host\LinuxWorkerProcess.cs`
- **Methods:** `WaitForExit` (lines 95–144) and `Dispose` (lines 169–194)
- **Bug description:** The whole wait loop — which can block until `terminationStart` (project wall time, potentially many minutes) — executes inside `lock (_synchronization)`. `Dispose` takes the same lock to terminate the process. A concurrent `Dispose` (the natural "shut down and kill the worker now" path) blocks until `WaitForExit` returns on its own; the killer cannot kill. The `WaitForExit` call in `Launcher.Program.RunWorker` (Program.cs lines 328–330) passes `CancellationToken.None`, so nothing can break the lock holder early. Latent (the current launcher is single-threaded), but the type's public API advertises `IDisposable` + concurrent cancellation, and this is exactly the lock+blocking-wait deadlock shape.
- **Code excerpt:**
```csharp
public LinuxWorkerCompletion WaitForExit(...)
{
    lock (_synchronization)
    {
        ...
        while (!process.WaitForExit(0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ...
            if (cancellationToken.WaitHandle.WaitOne(waitMilliseconds)) ...
public void Dispose()
{
    lock (_synchronization)   // blocks until WaitForExit above returns
```
- **Category:** concurrency — **Severity:** medium — **Confidence:** 0.55

### 3. `LinuxWorkerProcess.Terminate` throws before the process-group kill on grace expiry — leaked `setsid` descendants and a throwing `Dispose`
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Host\LinuxWorkerProcess.cs`
- **Methods:** `Terminate` (lines 196–241; throw at 236 before `KillProcessGroup` at 240), `Dispose` (lines 178–193)
- **Bug description:** The design comment in `Dispose` says "Terminate handles both live and already-exited leaders, so always run it during disposal," and `Terminate` ends with `KillProcessGroup(process.Id)` (SIGKILL to `-pid`, the whole setsid group). But on the final-timeout path (`!process.WaitForExit(killWait)`), the method throws `InvalidOperationException` at line 236 **before** reaching line 240, so the group kill never happens: any descendant of the setsid group survives (leaked worker processes), and the exception escapes from `Dispose`, violating dispose conventions and replacing the caller's failure classification (in `RunWorker` it surfaces as a containment failure even though a result may already have been published).
- **Code excerpt:**
```csharp
if (!process.WaitForExit(killWait))
{
    throw new InvalidOperationException(
        "The SharpProof worker did not terminate within its grace period.");
}
KillProcessGroup(process.Id);   // skipped on the throw path
```
- **Category:** resource — **Severity:** medium — **Confidence:** 0.5

### 4. Worker `WaitForStartAsync` leaks a thread-pool thread blocked on stdin after the startup deadline and never observes the abandoned read
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Worker\Program.cs`
- **Method:** `Program.WaitForStartAsync` (lines 255–282)
- **Bug description:** `Task.Run(input.ReadLine)` is raced against `Task.Delay(timeout)`; when the delay wins, the background task remains blocked in a synchronous `ReadLine` on redirected stdin for the remainder of the process lifetime (thread-pool thread leak), and the task is never awaited/observed — if `ReadLine` throws afterwards (pipe reset), the exception is an unobserved task exception. Additionally, a start message arriving just after the deadline is silently discarded while the host believes the handshake message was consumed, so the only signal is the 125 exit — acceptable but lossy. There is also no way to unblock the read on cancellation (no token plumbed into the read path).
- **Code excerpt:**
```csharp
var read = Task.Run(input.ReadLine);
var completed = await Task.WhenAny(read, Task.Delay(timeout)).ConfigureAwait(false);
if (!ReferenceEquals(completed, read) || read.Status != TaskStatus.RanToCompletion)
{ return false; }   // abandoned read keeps blocking on stdin
```
- **Category:** resource — **Severity:** low — **Confidence:** 0.6

### 5. `InvocationRunLeaseStore.IsOwnerAlive` — 5-second start-time tolerance can authenticate a PID-reusing impostor as the live owner
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Host\InvocationRunLeaseStore.cs`
- **Method:** `InvocationRunLeaseStore.IsOwnerAlive` (lines 220–242)
- **Bug description:** Liveness is "process exists AND start time matches within ±5 seconds." The stated contract (file header, lines 7–11) is that a lease is reclaimed only when the recorded process is gone **or its start time no longer matches**. With a ±5s window, a different process that recycled the PID within 5 seconds of the original's start is treated as the live owner, so its stale run directory is never reclaimed (unbounded accumulation on fast-cycling PID Linux CI). The failure direction is fail-safe (never deletes a live run), but it contradicts the reclaim contract.
- **Code excerpt:**
```csharp
return Math.Abs((actualStart - expectedStart).TotalSeconds) <= 5;
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.4

### 6. `WorkerProtocolJson.Deserialize<T>` discards its `requiredProperties` argument — dead parameter implies unenforced contract
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Worker.Protocol\ProtocolJson.cs`
- **Method:** `Deserialize<T>` (lines 1290–1295); callers at 130 and 135
- **Bug description:** Both public entry points pass `WorkerProtocolMetadata.WorkerVerifyRequestJsonProperties` / `...ResponseJsonProperties`, but the first statement is `_ = requiredProperties;`. If the intent was to enforce required/unknown properties beyond the shape table, it silently does nothing; correctness now depends solely on `ParseAndEnsureJsonShape`'s `JsonObjectShapes` lookup keyed by `typeof(T).Name` — a root type missing from the shape table would throw "root type is not declared" only as a runtime surprise, and the parameter documents a contract that is not implemented.
- **Code excerpt:**
```csharp
private static T? Deserialize<T>(string json, IEnumerable<string> requiredProperties)
{
    _ = requiredProperties;
    using var document = ParseAndEnsureJsonShape(json, typeof(T).Name);
```
- **Category:** api-misuse — **Severity:** low — **Confidence:** 0.4

### 7. `PublicationTopologyStore.Read` — TOCTOU between `File.Exists` and `File.GetAttributes` throws raw `FileNotFoundException` instead of the documented contract
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Host\PublicationTopologyStore.cs`
- **Method:** `PublicationTopologyStore.Read` (lines 40–49)
- **Bug description:** The method's contract is "return null when missing, throw `InvalidDataException` for malformed metadata," but if the metadata file is deleted between `File.Exists(path)` (line 40) and `File.GetAttributes(path)` (line 45), `GetAttributes` throws `FileNotFoundException` (an `IOException`) which is neither caught nor converted — the clean/publish path gets an untyped IO failure instead of `null`.
- **Category:** logic — **Severity:** low — **Confidence:** 0.35

Scout 6 clean areas (checked, no findings): VerifierDiagnosticTransport (prefix framing, canonical JSON validation, fail-closed catch coverage); ProtocolJsonSupport/ProtocolJson (bounded reads, strict UTF-8, depth cap 32, case-sensitive names, UnmappedMemberHandling.Disallow, canonical enums, BoundedJsonBufferWriter advance checks); ContainerNativeLibrary/Z3ResolverGate (sync-over-async cannot deadlock; TCS always completed before lock release); Worker.Program CancellationGate (Pulse/Wait protocol, balanced signal registrations); SharpProofWorker.VerifyAsync lane distribution, RecordRetirement locking, Renew backend-swap; VerificationCache (lock retry, staged-eviction rollback, double-read comparison); Launcher.Program exit-code flow (124 timeout, NormalizeNoResultExitCode, atomic failure envelopes); LinuxWorkerProcess.Start / EnterChildBoundaryRequired (PDEATHSIG race closed); SharpProof.Testing (DeterministicRandom, IrCSharpDifferentialOracle, WellSortedIrGenerator); SharpProof.Frontend (CSharpPreprocessorSymbols, CompilationModelProvider, ContractApiMetadataRuntime, CompilerIdentityBridge).
## [Bug hunt 2026-08-29T18:48:25Z] Scout 7 — Effects / gates / summaries / build tasks (SharpProof.Effects, SharpProof.Gates, SharpProof.Summaries, SharpProof.BuildTasks)

### 1. Verifier launch failures are reported as task success (`Execute()` returns `true` with no `LogError`)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.BuildTasks\RunVerifier.cs`
- **Method:** `RunVerifier.Execute()` (catch block + final return)
- **Lines:** 413–429, 479 (compare with properly-failing early return at 142–150)
- **Bug description:** When the verifier process fails to launch (e.g., `process.Start()` throws, missing `/usr/bin/setsid`, supervisor assembly missing), the catch block sets `ExitCode = -1` but only emits `Log.LogMessage(MessageImportance.High, ...)` — not `Log.LogError` — and `Execute()` unconditionally returns `true`. To MSBuild the task **succeeded**; the failure is visible only if the invoking `.targets` inspects the `[Output] ExitCode`. Every other task in this project (`WriteInvocationRunLease`, `ReclaimInvocationRuns`, `PersistPublishedVerification`, `ResetPublishedVerification`, `InvalidatePublishedResult`, `ValidatePublishedVerificationResult`) logs via `Log.LogError` and returns `false` on failure, so a caller that only honors the task return value gets a silent pass here. The comment claims "the MSBuild boundary reports every launch failure as a classified task result", but the MSBuild-visible signal (return value / error log) says success.
- **Code excerpt:**
```csharp
catch (Exception exception)
{
    TrySetTerminalCause(VerifierTerminalCause.Faulted);
    ...
    ExitCode = -1;
    Log.LogMessage(
        MessageImportance.High,
        "SharpProof verifier launch failed: {0}",
        exception.Message);
}
...
return true;   // line 479 — even after the catch above
```
- **Category:** api-misuse / logic — **Severity:** medium — **Confidence:** 0.55 (may be deliberate if every caller checks `ExitCode`, but it is inconsistent with the failure convention of every sibling task and produces no MSBuild error)

### 2. Unguarded `process.ExitCode` read can race a still-live (or not-yet-observed-exited) supervisor and misclassify the run as a launch failure
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.BuildTasks\RunVerifier.cs`
- **Method:** `RunVerifier.Execute()`
- **Lines:** 405–411 (also 388)
- **Bug description:** `ExitCode = ... : process.ExitCode;` is evaluated whenever the terminal cause is neither `Canceled` nor `Timeout`/`OutputLimit`, without first ensuring the process has been observed as exited. There are real paths reaching this line with the process not yet observed as exited: (a) `TryTerminate` returns `true` at lines 1303–1311 while the supervisor is intentionally left alive as a cleanup anchor ("terminateSent && !process.HasExited → return true"), after which output drain completion can set terminal cause `Completed` (line 351) while the supervisor is still running; (b) the stdout/stderr drain tasks complete when the child closes its pipe handles, which can happen before the `Process` object has refreshed `HasExited`. `Process.ExitCode` on a process that has not (yet) been observed as exited throws `InvalidOperationException`, which is swallowed by the generic catch and reported as "SharpProof verifier launch failed" with `ExitCode = -1` — i.e., a possibly-successful verifier run is misclassified as a launch failure. Line 388 (`process.HasExited && process.ExitCode != 125`) also has a check-then-use gap, though a much narrower one.
- **Code excerpt:**
```csharp
ExitCode = containmentFailed
    ? -1
    : canceled
        ? 143
        : timedOut
            ? 124
            : process.ExitCode;   // no HasExited guarantee
```
- **Category:** concurrency — **Severity:** medium — **Confidence:** 0.5 (code path clearly unguarded; trigger probability low because pipe EOF usually follows exit closely)

### 3. `.git` is stripped from anywhere in the upstream remote URL, not just the suffix
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Gates\Corpus\OpenSourceCorpusImporter.cs`
- **Method:** `OpenSourceCorpusImporter.NormalizeRepositoryUrl`
- **Lines:** 450–462
- **Bug description:** The importer validates the upstream checkout's `remote.origin.url` against `https://github.com/aalhour/C-Sharp-Algorithms` by normalizing SSH/HTTPS forms and stripping `.git`. `string.Replace(".git", string.Empty, OrdinalIgnoreCase)` removes `.git` from *anywhere* in the URL. A remote like `https://github.com/aalhour/C-Sharp-Algorithms.git.fork` or a repo path containing `.git` mid-path normalizes to the expected URL and is accepted, making the origin check pass for a checkout that is not the pinned upstream. Because this check is the only guard tying the imported corpus to the reviewed repository, over-matching weakens the importer's provenance validation (false positives), and unrelated URLs containing `.git` normalize incorrectly (false negatives).
- **Code excerpt:**
```csharp
return value.Trim()
    .TrimEnd('/')
    .Replace("git@github.com:", "https://github.com/",
        StringComparison.OrdinalIgnoreCase)
    .Replace(".git", string.Empty,
        StringComparison.OrdinalIgnoreCase);
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.8 (behavior is certain; exploitability is limited because it only runs when `SHARPPROOF_OSS_CORPUS_SOURCE` is set)

### 4. Unchecked `double`/`float` → `decimal` conversions are classified as never throwing, but they throw `OverflowException` regardless of checked context
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Effects\ConversionEffectClassifier.cs`
- **Method:** `ConversionEffectClassifier.Classify` (numeric branch) / `CheckedOverflow`
- **Lines:** 60–63, 96–105
- **Bug description:** All numeric/enumeration conversions are routed through `CheckedOverflow(isChecked, ...)`, which returns `EffectSummary.Empty` when the operation is not in a checked context. That is correct for integer↔integer and integer↔floating-point conversions, but the C# floating-point→`decimal` conversions (`(decimal)someDouble`, `(decimal)someFloat`) throw `OverflowException` at runtime when the value is out of the decimal range **even in unchecked context** (checked/unchecked does not apply to them). The classifier therefore under-approximates the effect summary (no `MayThrow`, complete summary), which can let the analyzer prove `[DoesNotThrow]`/purity for code containing `(decimal)doubleOperand` — a soundness gap in the effect domain, not merely a precision loss. (Symmetrically, checked widening like `checked((double)someFloat)` is conservatively reported as possibly throwing, which is only a precision issue.)
- **Code excerpt:**
```csharp
if (conversion is { IsNumeric: true } or { IsEnumeration: true })
{
    return CheckedOverflow(operation.IsChecked, operation);
}
...
return isChecked && !SkipsLiftedOperator(operation) &&
       abstractFlow?.ProvesNoOverflow(operation) != true
    ? Throw(OverflowException)
    : EffectSummary.Empty;   // unchecked: no throw modeled
```
- **Category:** logic — **Severity:** medium — **Confidence:** 0.6 (double→decimal is classified as a numeric conversion by Roslyn; the runtime throw behavior for FP→decimal is context-independent per the C#/.NET spec)

### 5. A throws-set that contains only the unknown marker projects as "does not throw" at the contract-effect level
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Effects\EffectProjection.cs`
- **Method:** `EffectSummaryProjector.Project`
- **Lines:** 23–26 (with `EffectThrowSet.Unknown` defined at `EffectValues.cs` line 106)
- **Bug description:** The projection adds `EffectContractKind.Throws` only when `summary.Throws.Types` is non-empty. `EffectThrowSet.Unknown` is constructed as `([], includesUnknown: true)` — zero concrete types plus the unknown marker — so a summary meaning "may throw any exception" projects `Effects` **without** the `Throws` bit. Consumers that compare projections bitwise rely on this bit: e.g., `EffectContractMappings.Covers` (`EffectContractMappings.cs` lines 150–152) checks `(actualProjection.Effects & ~declaredProjection.Effects) == 0` *before* the separate `ExceptionsCovered` check, so a declared summary whose throw set is unknown-only can never cover an actual concrete throw even though `ExceptionsCovered` would return true (`declared.IncludesUnknown`). Under-reporting a may-throw as a no-throw projection is also available to any other projection consumer (compiler collector summaries, wire output).
- **Code excerpt:**
```csharp
if (!summary.Throws.Types.IsDefaultOrEmpty)
{
    effects |= EffectContractKind.Throws;
}
// EffectThrowSet.Unknown == new([], includesUnknown: true) → Types empty → no Throws bit
```
- **Category:** logic — **Severity:** low/medium — **Confidence:** 0.5 (complete summaries can never carry unknown-only throws, which limits the reachable cases to *declared*/incomplete summaries on the right-hand side of coverage checks; the `IsComplete` flag does propagate the uncertainty correctly)

### 6. Summary environment merge drops variables that exist only on later incoming paths
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Summaries\IrRelationalSummaryBuilder.cs`
- **Method:** `IrRelationalSummaryBuilder.Run.Merge`
- **Lines:** 648–685 (specifically 650 and 658–662)
- **Bug description:** The merged environment is seeded from `values[0].Environment.Keys` only. A variable assigned on some incoming path but absent from the first (order-sorted) incoming state is not merged at all — even though the other paths' values are available and the code explicitly handles the "missing in some state" case (line 658) for variables present in `values[0]`. Downstream blocks referencing such a variable then fail in `Substitute` with `UnsupportedBody`, so the whole summary is abandoned instead of built. This is conservative (never unsound), but it needlessly downgrades summaries to `UnsupportedBody` for asymmetrical-path CFGs; iterating the union of keys would be strictly better. In valid C# definite-assignment makes the dropped variable unread, but this builder consumes lower-level `IrProgram` input that is not guaranteed to have been definite-assignment-checked.
- **Code excerpt:**
```csharp
foreach (var variable in values[0].Environment.Keys.OrderBy(...))
{
    ...
    if (values.Any(value =>
            !value.Environment.ContainsKey(variable)))
    {
        continue;   // handled for values[0]'s variables…
    }
    ...
}
// …but variables only present in values[1..] are never visited at all
```
- **Category:** logic — **Severity:** low — **Confidence:** 0.4 (conservative deviation; consequences are missed summaries, not wrong proofs)

Scout 7 clean areas (checked, no findings): BuildTasks other tasks (WriteInvocationRunLease, ReclaimInvocationRuns, PersistPublishedVerification, ResetPublishedVerification, InvalidatePublishedResult, ValidatePublishedVerificationResult, TaskExecutionCancellation, Program.cs supervisor arg parsing, VerifierProcessSupervisor subreaper/cleanup + /proc parsing + nonce validation, VerifierBuildDiagnosticCodes; diagnostic severity mapping safe because transport validates severity; ITaskItem handling); SharpProof.Gates (Program, RepositoryLayout, AnalyzerGateHost, PackageBuildEstimator, PerformanceGate percent math and marker spans, PackageBuildSdkPin, WorkerPerformanceProbe operator precedence, CorpusGate bookkeeping/rates, CorpusCatalog/Models/SnapshotFormat, OpenSourceCorpusRunner/Catalog, Import-OssCorpus.ps1); SharpProof.Summaries (IrRelationalSummary provenance, IrRelationalSummaryInstantiator naming/type checks, builder DFS/cycle detection/provenance/budgeting otherwise); SharpProof.Effects (EffectSummary/Domain join ordering, EffectRegions, EffectValues, EffectContractMappings + generated catalogs, EffectSummaryOperations, EffectCallGraph depth-bounded cycles, EffectAnalysisSession locking, EffectModuleInitialization, ExternalEffectResolver, TrustedBoundaryPolicy, EffectExceptionFlow catch/filter/finally semantics, OperationCompletionEvaluator divide-by-zero constant patterns, EffectCallSiteResolver, InvocationEmissionPolicy cache keying, EffectCallPreconditionPolicy otherwise, ApiSpecResolution, EffectMethodNodeBuilder CFG region mapping, CreationFlowCaptures, CoalesceAssignmentFlowCaptures, DeconstructionPhaseWalker, PropertyDispatchFacts, SwitchExpressionFacts, OperationNullnessEvaluator, UsingDisposalEffectResolver, StringConcatenationEffectResolver, PrimaryConstructorParameterOwnership, ManagedMutationFacts, generated projections/catalogs, csproj files). No mutable static state; no culture-sensitive parsing (invariant consistently used). Caveat: the three largest files (ManagedAbstractFlow.cs ~3k lines, ExceptionHandlerReachability.cs ~2.9k lines, OperationEffectScanner*.cs ~3.5k lines) were covered via targeted reads around flagged patterns, not exhaustively.
## [Bug hunt 2026-08-29T18:48:51Z] Scout 8 — Build system, packaging, CI, scripts (scripts/, eng/, Tools/, root MSBuild/props/targets, catalogs, compose, .github/, SharpProof.Package/)

## Finding 1 — compose `dev` service exports `SHARPPROOF_GIT_PARENT_ROOT` for a directory it never mounts
- **File:** `C:\w\PurelySharp-bug-hunt\compose.yaml`
- **Section:** anchor `x-sharpproof-common` (lines 16–26) vs. `services.dev` overrides (lines 37–48)
- **Bug description:** The common anchor sets `SHARPPROOF_GIT_PARENT_ROOT: /workspace/SharpProof-host-parent` together with the bind mount `- ..:/workspace/SharpProof-host-parent:ro`. The `dev` service re-declares the full `environment:` block including `SHARPPROOF_GIT_PARENT_ROOT` (line 41) but its `volumes:` key (lines 45–48) **replaces** the anchor's volume list entirely under YAML merge (`<<: *sharpproof-common`) semantics — the `..` host-parent mount and the `./artifacts` mount are gone. Result: in the canonical dev container, `SHARPPROOF_GIT_PARENT_ROOT` points at `/workspace/SharpProof-host-parent`, which does not exist. `eng/container/entrypoint.sh` `resolve_linked_worktree_git_directory()` (lines 68–69) degrades gracefully today (`[[ -n ... && -d ... ]] || return 1`), so dev shells silently lose linked-worktree git metadata resolution the variable exists to provide, and any future consumer of that variable in the dev flow fails against a phantom path.
- **Excerpt:**
```yaml
# anchor (lines 20, 23)
SHARPPROOF_GIT_PARENT_ROOT: /workspace/SharpProof-host-parent
- ..:/workspace/SharpProof-host-parent:ro
# dev service (lines 41, 45-48) — re-exports the var, drops the mount
SHARPPROOF_GIT_PARENT_ROOT: /workspace/SharpProof-host-parent
volumes:
  - sharpproof-workspace:/workspace/SharpProof
```
- **Category:** config — **Severity:** low — **Confidence:** 0.75 (behavior verified statically; YAML merge semantics are standard; impact is degraded/dead config, not a hard failure)

## Finding 2 — Canonical-container MSBuild guard is skipped for `--no-build` test/pack verbs (fail-open containment edge)
- **File:** `C:\w\PurelySharp-bug-hunt\Directory.Build.targets`
- **Target:** `_RequireSharpProofCanonicalContainer` (lines 2–6)
- **Bug description:** The guard errors when `SHARPPROOF_CONTAINER != '1'` or the contract file is missing, and is hooked only via `BeforeTargets="Restore;PrepareForBuild"`. Commands that legitimately skip both targets — `dotnet test --no-build` and `dotnet pack --no-build --no-restore` — never execute it. The repository's own sanctioned flows always route through `scripts/Invoke-SharpProofDotnet.ps1` (which re-checks the container env, line 17), but a direct host-side `dotnet pack --no-build --no-restore -c Release` on `SharpProof.Attributes.csproj`/`SharpProof.Package.csproj` produces package bytes with no container check firing at all (only `SharpProof.Verifier` has its own in-target error).
- **Excerpt:**
```xml
<Target Name="_RequireSharpProofCanonicalContainer"
        BeforeTargets="Restore;PrepareForBuild"
        Condition="'$(SHARPPROOF_CONTAINER)' != '1' Or !Exists('/etc/sharpproof/container-contract.json')">
  <Error Text="SharpProof repository restore, build, test, pack, and release commands must run through ..." />
```
- **Category:** config / logic — **Severity:** low — **Confidence:** 0.65 (mechanism is certain for MSBuild target scheduling; whether it is accepted-by-design is the residual uncertainty; the sanctioned scripts mask it in practice)

## Finding 3 — Dead MSBuild property `SharpProofTestProject` (computed, never consumed)
- **File:** `C:\w\PurelySharp-bug-hunt\Directory.Build.props`
- **Line:** 23 (PropertyGroup lines 3–24)
- **Bug description:** `SharpProofTestProject` is derived from a regex on `MSBuildProjectName` but no file in the repository consumes it (repo-wide grep finds only its definition and unrelated `Get-SharpProofTestProjectParallelism` function-name matches). Dead configuration in the most-evaluated props file; it suggests an intended test-project discriminator (e.g., for the `IsTestProject`/NoWarn block in `Directory.Build.targets` lines 11–15) that is instead keyed on the SDK's `IsTestProject`.
- **Excerpt:**
```xml
<SharpProofTestProject Condition="$([System.Text.RegularExpressions.Regex]::IsMatch('$(MSBuildProjectName)', 'Test$'))">true</SharpProofTestProject>
```
- **Category:** doc / config — **Severity:** low — **Confidence:** 0.9 (consumption definitively absent). Note: independently flagged by wave-4 `H18`; relay for dedup in BUGS.md.

Scout 8 clean areas (verified, no findings): Directory.Build.props/targets version/condition wiring (Release.props import order, CI/RestoreLockedMode gating, production-project list, banned-API wiring, WarningsAsErrors composition; all PackageReferences exist in Directory.Packages.props, no duplicates, CPM coherent); SharpProof.Release.props / PackageMetadata.props / SelfApply.targets / AnalyzerConsumer.props (version authority matches Get-SharpProofReleaseVersion parsing, Test-SharpProofReleaseArtifacts, CI tag gates; self-apply payload/Remove lists symmetric); SharpProof.Package/ (nuspec token/property names match `_SharpProofPrepareNuspecProperties`; layout matches package props/targets resolution; Verifier nuspec file list matches eng/release/third-party-components.json; package props defaults match eng/acceptance/contract.json values asserted by Test-SharpProofContainerContract.ps1); compose.yaml task services (every command maps to a ValidateSet member of Invoke-SharpProofContainer.ps1; cpus/mem_limit defaults match contract.json); Dockerfile + toolchain.json + Prepare-NativePayload.ps1 (digests, SDK/runtime/pack versions, Z3 catalog URL/SHA-256/lengths/paths, SHARPPROOF_NATIVE_ROOT → `$nativeroot$` chain); CI workflows (ci.yml, coverage.yml, nightly.yml, weekly.yml, security.yml, security-reusable.yml, package-consumers.yml, stale-issues.yml — artifact producer/consumer name pairs, env passthrough, needs/if composition, matrices all consistent); container driver chain (Invoke-SharpProofContainer.ps1, Invoke-SharpProofDotnet.ps1, SharpProof.ContainerExecution.psm1, entrypoint.sh, dev-command.sh, dev-init.sh — exit-code propagation, timeout contracts, evidence-path cleanup, disposable-clone topology); release/pack scripts (Invoke-SharpProofReleaseContainer.ps1, Test-SharpProofReleaseArtifacts.ps1, ReleaseChecksums, ReleaseJson, Publish-SharpProofRelease, New-SharpProofReleaseEvidence, SHA256SUMS ordering); test-shard drivers (Invoke-SharpProofSemanticTests.ps1, Invoke-SharpProofPackageTests.ps1, Invoke-SharpProofCoverage.ps1, Test-SharpProofCoverage.ps1, eng/coverage/*.runsettings, baseline.json floors); JSON catalogs (Projection.catalog.json read in full; DeclarativeModels.catalog.json sampled; eng/generated/approved-outputs.v1.json, eng/pilots/catalog.json, eng/release/*.json, eng/acceptance/contract.json, scripts/package-projects.json, global.json, NuGet.Config, .config/dotnet-tools.json, .devcontainer/devcontainer.json — no duplicate keys or schema mismatches found); scripts/Tools/samples parameter wiring (Test-SharpProofSamples.ps1, eng/pilots/* props, Generate-Readme.ps1).
## [Bug hunt 2026-08-29T18:50:17Z] Scout 9 — Test projects (SharpProof.*.Test, *.TestAsset)

### 1. Test accepts the failure mode it is supposed to reject (`Is.Null.Or.TypeOf<...>`)
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Package.Test\LauncherArgumentTests.cs`
- **Test method + containing type:** `LinuxWorkerMinimumGraceDoesNotRestartCleanupBudget` in `LauncherArgumentTests`
- **Line(s):** 234 (also 232–239 context)
- **Bug description:** The test starts a non-terminating worker (`trap '' TERM; while :; do sleep 1; done`) and calls `process.WaitForExit(1ms, 1ms)` with a below-minimum grace. The expected product behavior is that this is rejected with `InvalidOperationException` (the shared final deadline invariant). The assertion `Assert.That(failure, Is.Null.Or.TypeOf<InvalidOperationException>())` also accepts `failure == null`, i.e. it passes if `WaitForExit` *succeeds* — meaning if the product ever stops enforcing the minimum-grace rejection and instead returns/completes quickly (silently restarting or skipping the cleanup budget), the exception check passes vacuously and only the 300ms timing assertion remains to catch anything. The `Or.Null` disjunct neuters the regression the test name claims to guard.
- **Code excerpt:**
```csharp
Assert.That(failure, Is.Null.Or.TypeOf<InvalidOperationException>());
Assert.That(
    stopwatch.Elapsed,
    Is.LessThan(TimeSpan.FromMilliseconds(300)),
    "The minimum grace and disposal must share the original final deadline.");
```
- **Category:** logic — **Severity:** medium — **Confidence:** 0.55

### 2. Silent `return;` on non-Linux makes three test cases vacuously pass
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Worker.Test\LinuxPublicationSetTests.cs`
- **Test method + containing type:** `ConstructorFailureDisposesEveryEarlierLock` (3 `[TestCase]` variants) in `LinuxPublicationSetTests`
- **Line(s):** 355–360
- **Bug description:** The test body begins with `if (!OperatingSystem.IsLinux()) { return; }`. On Windows/macOS the test silently reports "Passed" with zero assertions executed, hiding the fact that it never ran. Every other platform-gated test in the suite uses either `[Platform("Linux")]` or `Assert.Ignore(...)` with a reason (e.g. lines 231, 536 of the same file's siblings, `WorkerTests` symlink tests, `LauncherArgumentTests` line 204), so this inconsistent pattern masks the skipped coverage in reports and can hide a future refactor that accidentally makes the test body unreachable on all platforms.
- **Code excerpt:**
```csharp
public void ConstructorFailureDisposesEveryEarlierLock(int failureIndex)
{
    if (!OperatingSystem.IsLinux())
    {
        return;
    }
```
- **Category:** config — **Severity:** low — **Confidence:** 0.85

### 3. `Thread.Sleep(100)` race means the exercised race may never be entered
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Package.Test\LauncherArgumentTests.cs`
- **Test method + containing type:** `ConcurrentWaitAndDisposeAreSerialized` in `LauncherArgumentTests`
- **Line(s):** 252–258
- **Bug description:** The test starts `LinuxWorkerProcess` for `sleep 1`, queues `WaitForExit` via `Task.Run`, sleeps a fixed 100ms, then disposes the process concurrently with the waiter. If thread-pool scheduling delays the waiter beyond 100ms (heavily loaded CI, cold thread pool), `Dispose()` completes before `WaitForExit` is ever entered and the wait/dispose serialization the test exists to verify is never exercised — yet the test still passes (`DoesNotThrow` on both). The synchronization should use an event/signaled entry point rather than a wall-clock guess; as written the test can pass without testing its own subject.
- **Code excerpt:**
```csharp
var waiter = Task.Run(() => process.WaitForExit(
    TimeSpan.FromSeconds(5),
    TimeSpan.FromSeconds(6)));
Thread.Sleep(100);

Assert.DoesNotThrow((Action)(() => process.Dispose()));
Assert.DoesNotThrow((Action)(() => waiter.GetAwaiter().GetResult()));
```
- **Category:** concurrency — **Severity:** low — **Confidence:** 0.6

### 4. Hard 100ms wall-clock bound on cancellation responsiveness
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Verify.Test\ProofKernelTests.cs`
- **Test method + containing type:** `CancellationDuringMalformedModelValidationPropagates` in `ProofKernelTests`
- **Line(s):** 561–563
- **Bug description:** The test verifies cancellation is observed during model validation (500,000 model entries) and asserts `stopwatch.Elapsed < 100ms` around the `Assert.ThrowsAsync<OperationCanceledException>` call. A hard 100ms wall-clock bound on a JIT-warmed, allocation-heavy cancellation path is timing-dependent: on a loaded runner the correct (cancellation-checking) implementation can exceed 100ms and fail spuriously. Flaky failures here erode trust in the suite and invite blanket retries, masking genuine latency regressions in the same path.
- **Code excerpt:**
```csharp
var stopwatch = Stopwatch.StartNew();
Assert.ThrowsAsync<OperationCanceledException>(action);
Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(100)));
```
- **Category:** concurrency — **Severity:** low — **Confidence:** 0.55

### 5. Fixed `Task.Delay(100)` race against a 20M-element projection
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Verify.Test\ProofKernelTests.cs`
- **Test method + containing type:** `CancellationDuringUnsatCoreProjectionDoesNotReturnProven` (with helper `CancelingUnsatBackend`) in `ProofKernelTests`
- **Line(s):** 521–532 and 605–622
- **Bug description:** The stub backend kicks off an unobserved `Task.Run` that cancels the token after a fixed `Task.Delay(100)` and synchronously returns a 20,000,000-element UnsatCore. The test expects the kernel to throw `OperationCanceledException` during core projection. This is a race: if the projection loop finishes (or observes the token only once, early) in under 100ms on a fast machine, `VerifyAsync` returns `Proven` and `Assert.ThrowsAsync` fails spuriously; if projection is fast *and* the token is never re-checked, the product defect (missing cancellation checks mid-projection) is exactly what the 100ms race is papering over. The unobserved `Task.Run` also represents a potential unobserved-task exception. A deterministic backend (e.g., cancel synchronously on Nth access) would test the same invariant without timing.
- **Code excerpt:**
```csharp
_ = Task.Run(async () =>
{
    await Task.Delay(100).ConfigureAwait(false);
    await cancellation.CancelAsync().ConfigureAwait(false);
}, CancellationToken.None);
return Task.FromResult(result);
```
- **Category:** concurrency — **Severity:** low — **Confidence:** 0.5

### 5. Negative timing assertion (`Wait(100ms) == false`) can vacuously pass
- **File:** `C:\w\PurelySharp-bug-hunt\SharpProof.Worker.Test\ContainerNativeLibrarySetupTests.cs`
- **Test method + containing type:** `Z3ResolverWaitsForVerifiedHandlePublication` in `ContainerNativeLibrarySetupTests`
- **Line(s):** 71–76
- **Bug description:** The test asserts that a resolver task has *not* completed within 100ms before publishing the handle: `Assert.That(resolution.Wait(TimeSpan.FromMilliseconds(100)), Is.False, ...)`. If the thread pool is starved and `gate.Resolve()` has not even started executing within the window, the assertion passes without exercising the "must not resolve before publication" invariant at all; the property is only genuinely checked when the task has actually begun and is blocked. The assertion cannot distinguish "correctly blocked" from "never started", so the safety property can silently go untested while the test stays green.
- **Code excerpt:**
```csharp
var resolution = System.Threading.Tasks.Task.Run(() => gate.Resolve());

Assert.That(
    resolution.Wait(TimeSpan.FromMilliseconds(100)),
    Is.False,
    "A resolver callback must not return a zero handle before publication.");
```
- **Category:** concurrency — **Severity:** low — **Confidence:** 0.4

Scout 9 clean areas (checked for de-duplication): all Assert.Ignore sites correctly gated with reasons; all 60+ "no direct Assert" [Test] methods delegate to asserting helpers (AssertRejected, AssertRequiresDiagnostics, DiagnosticDescriptorCatalogAssertions.AssertOutput, GeneratedDomainLawAssertions.*, AssertMalformedAdditionalFiles, AssertCacheState, AssertFailure, etc.); message-index assertions match fixture order (no copy-paste shift); element-wise sequence equality correct; oracle tests compare against genuinely executed runtime behavior (no self-fulfilling oracle); console redirection confined to [NonParallelizable] fixtures; working-tree diffs in SharpProof.Package.Test accompany coherent product changes that strengthen assertions.
## [Bug hunt 2026-08-29T18:50:39Z] Scout 10 — Documentation & configuration cross-check (README.md, docs/, SEMANTICS.md, .globalconfig, .editorconfig, samples)

## Finding 1: README + normative SEMANTICS.md contradict implementation and 3 other docs on reachable cycles in effect analysis
- **File:** `C:\w\PurelySharp-bug-hunt\README.md` (lines 230–234) and `C:\w\PurelySharp-bug-hunt\SEMANTICS.md` (lines 262–264)
- **Contradicting files/lines:**
  - `C:\w\PurelySharp-bug-hunt\docs\analysis-limits.md` lines 163–167 ("Reachable cycles are retained in effect summaries as `MayDiverge` termination evidence, so a cycle alone does not erase modeled effects or turn an otherwise accountable effect claim into SP0047.")
  - `C:\w\PurelySharp-bug-hunt\docs\unknown-reasons.md` lines 290–292 ("Cyclic scalar flow disables scalar refinement, but does not make an effect claim incomplete: the effect engine can still prove the claim from its conservative scan of every compiler-reachable block.")
  - `C:\w\PurelySharp-bug-hunt\docs\coverage-and-limits.md` line 16 ("A loop disables scalar refinement but the conservative all-block scan can still prove effect absence.")
  - Code: `C:\w\PurelySharp-bug-hunt\SharpProof.Effects\EffectMethodNodeBuilder.cs` lines 64–77 (comment "Cyclic scalar flow does not invalidate the conservative all-block effect scan"; `CyclicControlFlow` is explicitly excluded from the incomplete-evidence join) and lines 416–420 (cycle → `EffectSummaryOperations.Join(summary, EffectSummaryOperations.MayDiverge())`, which is `Complete` with only termination=`MayDiverge`, per `EffectSummaryOperations.cs` lines 117–120 and 172–185); `C:\w\PurelySharp-bug-hunt\SharpProof.Effects\ManagedAbstractFlow.cs` lines 124–127 (cycle yields reason `CyclicControlFlow`, filtered out by the builder); `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\EffectEvaluationProjections.generated.cs` lines 37–42 (only `BlockBudgetExceeded`/`OperationBudgetExceeded` produce incomplete claim reasons; the cycle reason never reaches claim classification).
- **Bug description:** README states "Exceeding the block or operation budget, or encountering a reachable cycle, gives every selected effect contract a typed `Unknown` result and SP0047 evidence," and SEMANTICS.md (declared normative, "wins" over other docs) states "a reachable cycle or exhausted block or operation budget makes selected effect claims `Unknown`." The implementation does the opposite for cycles: a reachable cycle only disables scalar refinement and adds `MayDiverge` termination; the complete conservative all-block scan still establishes effect contracts (no `Unknown`, no SP0047). Only budget exhaustion makes the claim incomplete. Three maintained docs and the code agree against README + SEMANTICS.
- **Category:** doc — **Severity:** high — **Confidence:** 0.9

## Finding 2: .editorconfig per-file rule targets a file that does not exist
- **File:** `C:\w\PurelySharp-bug-hunt\.editorconfig` lines 71–72
- **Contradicting file/line:** File system — `C:\w\PurelySharp-bug-hunt\SharpProof.Specs\` contains only `ApiSpecTable.cs`, `ApiSpecTermValidator.cs`, `ApiSpecInstantiation.cs`, `ApiSpecContentDigest.cs`, `DefaultApiSpecCatalog.generated.cs`, `DeclarativeModels.generated.cs`, `FrameworkTypeMetadataNames.cs`, `GlobalUsings.cs`, `SpecIdentifiers.cs`; there is no `ApiSpecModel.cs` anywhere in the repo.
- **Bug description:** The section `[SharpProof.Specs/ApiSpecModel.cs]` with `dotnet_diagnostic.CA1720.severity = none` is dead configuration. The intended CA1720 suppression is no longer applied to any file (the Specs project was evidently reorganized/renamed), so the suppression silently lapses — or, if CA1720 currently fires nowhere in Specs, the entry is stale noise that misleads readers about where naming diagnostics are suppressed.
- **Category:** config — **Severity:** low — **Confidence:** 0.9

## Finding 3: Root .globalconfig sets an analyzer option key that no code reads
- **File:** `C:\w\PurelySharp-bug-hunt\.globalconfig` line 2
- **Contradicting file/line:** `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\Configuration\AnalyzerConfiguration.cs` lines 40–96 and `C:\w\PurelySharp-bug-hunt\SharpProof.Analyzer.Core\Configuration\AnalyzerConfigurationOptionRegistry.cs` lines 5–20 — the only global option keys consumed are `sharpproof_profile`, `sharpproof_features` (plus `build_property.` variants and the retired `sharpproof_mode` in AnalyzerConfiguration.cs lines 168–193). A repo-wide search for `sharpproof_enable_effect_summary_json` / `effect_summary_json` / `EffectSummaryJson` matches only the `.globalconfig` itself.
- **Bug description:** `sharpproof_enable_effect_summary_json = false` is a no-op: no analyzer, generator, build task, script, or test reads this key. It appears to be a leftover from a removed debug/summary-dump feature. Misleading configuration suggesting an effect-summary JSON output toggle exists.
- **Category:** config — **Severity:** low — **Confidence:** 0.85

Scout 10 clean areas (verified, no findings): diagnostic IDs/severities in docs/diagnostic-examples.md vs eng/diagnostics/diagnostic-descriptors.v1.json and generated projections (SP0002/13/15/16/24/25/27/30/45/46/47/49/50, SPCF0001–SPCF0008 all Error; SP0027 Warning; SP0024 Error); reserved-ID claims hold; SP0047/SP0048 launcher diagnostics; SP0051–SP0054 in VerifierBuildDiagnosticCodes.cs; protocol/cache/manifest/artifact schema versions (protocol 11, manifest 4, cache 13, artifact 15, relational-summary 2, pack 1) match generated models, contract.json, and docs; WorkerClaimReason/WorkerRunFailureReason/WorkerCallableCoverageReason/WorkerEffectEvidenceCertainty/WorkerCacheStatus/effect-result tuples match ProtocolModel.generated.cs/schema; budgets/defaults (rlimits, wall-times, parallelism, depth, grace, cache bytes, worker/analyzer bounds) match docs and code; README claims (global.json, SDK/Roslyn minimums, package version, dependency chain, eleven BCL rows, dotnet.scalar@1, result/cache paths, SARIF multitarget insertion, closed attributes, Contract.Old<T>, [Pure] removed, effect-attribute defaults, sp command names, compose memory); docs/samples cross-references (all relative links exist, anchors exist, samples README claims match Ensures clauses, sample csproj package pins, THIRD-PARTY-NOTICES versions); .editorconfig all other per-file paths exist; charset utf-8; string-operation gate claims match LanguageSubsetGate.cs.
