# Potential duplicated-code findings

Audit date: 2026-07-14. These are review candidates, not requested code changes. They were identified by 10 independent, read-only audit passes. Prefer extracting only shared mechanics; keep domain-specific policy and diagnostics at the call sites.

## Analyzer

6. **Exception summary catalog parsing** - `SharpProof.Analyzer/ExceptionSummaryCatalog.cs:311-418`
   `AddExceptionSources`/`AddExceptionEdges` repeat source registration, while `AddExceptionSourceFacts`/`AddExceptionEdgeFacts` repeat JSON parse/project/deduplicate loops. Small helpers for source registration and fact ingestion would preserve matching malformed-input behavior.

## Symbolic and proof core

7. **Path-state encoding wrappers** - `SharpProof.Symbolic/SymbolicProofService.cs:192-253`
   `TryEncodeConditionWithPathState` and `TryEncodeFactWithPathState` share validation, state normalization, contradictory-state short-circuit, safe-divisor gate, and encoding flow. A private generic core with delegates would keep safety gates aligned.

9. **Proof-status projection** - `SharpProof.Symbolic/SymbolicProgramPointResult.cs:727-736`; `SharpProof.Symbolic/SymbolicQueryFactSummaries.cs:871-880`; `SharpProof.Symbolic/SymbolicRuntimeHazardQueryService.cs:579-587`
   Multiple source enums map to `SymbolicProofStatus`, each defaulting to unknown. Centralizing projection avoids inconsistent behavior when source enums evolve.

11. **Source-query line/span analysis** - `SharpProof.Symbolic/SymbolicSourceQueryService.cs:57-91,138-180`
    Line and span queries both validate a tree, obtain a semantic model, select nodes, then perform the same `AnalyzeAndProjectNode` materialization. Share node analysis/projection and leave selection and result metadata separate.

## Code fixes, attributes, and test infrastructure

16. **Tooling-test temporary source lifecycle** - `SharpProof.ToolingTest/SymbolicCapabilityQueryTests.cs:24-201`; `SharpProof.ToolingTest/SymbolicComplexityQueryTests.cs:26-208`; `SharpProof.ToolingTest/StandaloneCompilationProfileTests.cs:23-60`
    Tests repeatedly create GUID paths, write source, run in `try/finally`, and delete. A disposable `TemporarySourceFile` fixture would standardize naming and cleanup.

17. **CLI compact envelope assertions and invalid-mode cases** - `SharpProof.ToolingTest/SymbolicCapabilityQueryTests.cs:79-128`; `SharpProof.ToolingTest/SymbolicComplexityQueryTests.cs:40-87`
    Capability and complexity tests duplicate compact JSON envelope assertions and invalid `--all-lines` invocation checks. Parameterize command/expected mode and retain feature-specific payload assertions locally.

## Tooling and build scripts

18. **SARIF materialization from project/solution inputs** - `Tools/SharpProof.Baseline/Program.cs:87-111`; `Tools/SharpProof.CorpusReport/Program.cs:23-56`
    Both classify `.sln`/`.csproj`, create GUID temporary SARIF, invoke `DotnetSarifBuildRunner`, and delete in `finally`. A shared async materializer/disposable result would centralize extension coverage and cleanup.

19. **Repository-relative path conversion and production-source discovery** - `scripts/Get-SharpProofProductionMetrics.ps1:40-52,97-109`; `scripts/Get-SharpProofRawSmtHotspots.ps1:23-35`; `scripts/Get-SharpProofCloneInventory.ps1:31-61`
    Path containment conversion and source exclusion policy are reimplemented across audit scripts, with the clone inventory missing one containment guard. Share strict conversion plus discovery policy to keep audit scope and safety consistent.

20. **Line-scanning collectors** - `scripts/Get-SharpProofRawSmtHotspots.ps1:123-182,231-292,354-423`
    Six functions enumerate source files, read lines with a counter, and emit path/line/text records; only roots and matching predicates differ. Extract a generic source-line scanner with predicates/needles.

21. **Compact CLI result location/schema forwarding** - `Tools/SharpProof.SymbolicCli/SymbolicCompactDomainResults.cs:16-118`
    Complexity and capability result types duplicate constructor validation, evidence-schema properties, and method/source/span forwarding. A shared immutable base or descriptor would protect the compact contract from location/schema drift.

22. **Build-tool and MSBuild discovery wrappers** - `build.ps1:21-33,69-97,111-113`; `build-nuget.ps1:13-18,38-40`; `build-vsix.ps1:13-38`; `.github/workflows/ci.yml:56-60`
    Root entry points duplicate Job Object launches, MSBuild discovery, and package-project enumeration. Reuse `scripts/Invoke-SharpProofDotnet.ps1` or a shared helper/pack manifest so local and CI package coverage cannot diverge.

## Lower-priority follow-ups

- `SharpProof.Symbolic/MethodBodyOperationResolver.cs:14-45` repeats body/expression operation lookup across method-like syntax types; a shared body-node adapter may simplify it.
- `SharpProof.Package/tools/install.ps1:1-58` and `uninstall.ps1:1-65` repeat analyzer-root/language traversal and DLL loops; use a common operation helper if packaging permits it.
- `SharpProof.Analyzer/MethodAllocationAnalyzer.cs:12-15` and `SharpProof.Analyzer/ExceptionFlowQuery.cs:9-12` define the same four-option `SymbolDisplayFormat`; expose one analyzer type-identity format.
- `SharpProof.Symbolic/SymbolicSourceCompilation.cs:10-78`, `Tools/SharpProof.Fuzz.Core/FuzzRunner.cs:546-584`, and `SharpProof.Analyzer/EffectSummaryMetadataSupport.cs:665-698` independently build trusted-platform references and source compilations. A shared reference/compilation-host factory with caller profiles could centralize cache, fallback, and de-duplication policy while preserving analyzer/fuzz differences.

## Follow-up audit (2026-07-14)

23. **Analyzer distribution closure is manually maintained in multiple delivery paths** - `SharpProof.AnalyzerConsumer.props:3-14`; `SharpProof.Package/SharpProof.Package.csproj:36-54`; `SharpProof.Vsix/SharpProof.Vsix.csproj:43,51-70`
    These files independently describe the deployable analyzer/code-fix component graph (attributes, analyzer, symbolic/proof dependencies, code fixes, and runtime support DLLs). Use one shared component/dependency manifest or item list, with each consumer applying its own packaging metadata. This is distinct from build entrypoint duplication: it concerns the actual shipped payload closure.

24. **ProofCore fixed-point collection drivers** - `SharpProof.ProofCore/SmtBooleanReferenceFactCollector.cs:10-50`; `SharpProof.ProofCore/SmtConcreteFactPreprocessor.cs:160-182,1373-1390`
    Boolean, reference, integer, and string collection loops all compute the same bounded iteration count, scan conditions, early-return for non-ready state, decrement, and repeat while changed. A private generic fixed-point driver can own convergence behavior while callers supply their collector and result adapter.

## Final bounded validation (2026-07-14)

# Potential Duplicated / Near-Duplicated Code (POTENTIAL_DUPS_2)

Generated by 20 parallel review agents scanning the `C:\w\PurelySharp` codebase for
duplicated or near-duplicated code that *should* be factored out (copy-pasted logic,
repeated boilerplate that should be a shared helper, duplicated type/attribute/extension
definitions across projects, etc.). Trivial/coincidental duplication (single shared lines,
`using` aliases, property getters) was excluded by the agents.

## Summary by area

| Area | Most significant finding | Agent |
|------|--------------------------|-------|
| SharpProof.Test (A-D) | `CommonBug*` fixture helpers copied in 4 files | 1 |
| SharpProof.Test (E-G) | `GetTrustedPlatformReferences` copied 3x; `FindLine` 4x | 2 |
| SharpProof.Test (H-J) | PowerShell `ProcessStartInfo` duplication | 3 |
| SharpProof.Test (K-M) | `Box`/`Holder`/`GlobalState` types ~17x-24x | 4 |
| SharpProof.Test (N-P) | 115 near-identical solver tests; `NullForgiving` id inconsistency | 5 |
| SharpProof.Test (Q-S) | `EnforcePureAttributeSource` duplicated across tests | 6 |
| SharpProof.Test (T-V) | `PureDisposable`/`ImpureDisposable` ~80x; `using` tests overlap | 7 |
| SharpProof.Test (W-Z + Smt/Verifiers) | Verifier constructors + global-config duplicated | 8 |
| Analyzer Configuration/Rules | 8x try/catch option wrappers; JSON tree-walk 3x | 9 |
| Analyzer Engine (A-H) | BCL delegate list copy-paste; dispatch resolvers | 10 |
| Analyzer Engine (I-P) | Method/property dispatch resolution duplicated | 11 |
| Analyzer Engine (Q-Z) | Conditional/coalesce branch-walking 4x | 12 |
| Symbolic Ir | (no significant in-scope duplication found) | 13 |
| Symbolic Smt | Union-find canonical finder 2x; merge-fact shape 4x | 14 |
| Symbolic root | `BoundedConcurrentCache` wrapper duplicates ProofCore | 15 |
| Tools.EffectSummary | Dominance-resolution block 2x; chain-key 4x | 16 |
| Tools.SymbolicCli | `CountBy` duplicated; schema-version triplet 6x | 17 |
| Tools.Fuzz | `Increment` duplicated; finding-identity key 2x | 18 |
| Tools Baseline/Corpus/Vsix | SARIF traversal + SARIF-input resolution duplicated | 19 |
| Cross-project (ProofCore/Attributes/Shared/...) | SMT-formula dispatch duplicated across projects | 20 |

---

## SharpProof.Test - A-D

---

## SharpProof.Test - E-G

---

## SharpProof.Test - H-J

---

## SharpProof.Test - K-M

---

## SharpProof.Test - N-P

---

## SharpProof.Test - Q-S

### Large-scale duplicated embedded source fragments
`SemanticOracleSmtTests.cs` (~9,400 lines) and `SemanticOracleRuntimeHazardAnalyzerSmtTests.cs` (~1,900 lines)
share enormous volumes of identical inlined `TestClass`/`TestMethod` bodies.

**Recommendation:** Factor common scenario sources into shared fragment constants in `SemanticOracleSmtTestBase`.

---

## SharpProof.Test - T-V

---

## SharpProof.Test - W-Z + Smt/Verifiers

---

## Analyzer Configuration + Rules

## Analyzer Engine - A-H

## Analyzer Engine - I-P

### Repeated "create term + add fresh-ownership facts" boilerplate - `PurityResourceStateFacts.cs:366,384,407`
`AddOwnedLocalArrayFacts`/`AddFreshMutableObjectFacts`/`AddOwnedDisposableLocalFacts` repeat the same
`CreateSymbolicReferenceTerm` -> `AddFact` loop -> `WithPathState`.
**Recommendation:** `AddFreshOwnershipFacts(state, symbol, syntax, ImmutableArray<SymbolicFact>)`.

### Minor: `Dispose` name matching in two places - `PurityResourceStateFacts.Diagnostics.cs:10` vs `PurityResourceStateFacts.cs:467`
**Recommendation:** Centralize `IsDisposableType` / `IsDisposeMethod`.

---

## Analyzer Engine - Q-Z

---

## Symbolic Ir

No significant in-scope duplication found by the assigned agent.

---

## Symbolic Smt

### Duplicated canonical-finder (union-find with path compression) - `SmtSyntacticClassifier.cs:588-596` vs `SmtSyntacticClassifier.Boolean.cs:484-498`
`FindCanonical` and `FindBooleanCanonical` identical except the carried `isNegated` flag. **Recommendation:** One generic canonical-finder.

### Duplicated "pick canonical by lexicographic ToString" - `SmtSyntacticClassifier.cs:573` vs `SmtSyntacticClassifier.Boolean.cs:476-478`
Identical tie-break snippet. **Recommendation:** `SelectCanonical(SmtFormula a, SmtFormula b)`.

### Duplicated FloorDiv / CeilingDiv - `SmtSyntacticClassifier.Numeric.cs:552-566` (BigInteger) vs `:776-792` (long)
**Recommendation:** Implement `long` versions by delegating to BigInteger overloads.

### Duplicated conditional-formula "known value" recursion - `SmtSyntacticClassifier.Numeric.cs:156-159`, `ReferenceString.cs:203-206,250-255,159-163`
Same `conditionValue ? WhenTrue : WhenFalse` dispatch per kind. **Recommendation:** `TryGetConditionalKnownValue(...)` generic helper.

### Duplicated string-length-as-interval fact application - `SmtSyntacticClassifier.ReferenceString.cs:37,169-182`, `SmtSyntacticClassifier.cs:640`
**Recommendation:** Route all through single `AddStringLengthFact`.

### Duplicated merge-fact shape (Integer/String/Reference/Boolean) - `SmtSyntacticClassifier.cs:598-612,614-642,644-658`, `Boolean.cs:500-516`
Same "merge into canonical then drop alias" shape. **Recommendation:** Generic `MergeFact<T>(map, canonical, alias, combine, isConflict)`.

---

## Symbolic root

### `Shared\Constants.cs` / `Shared\BclPurityFallbackHeuristics.cs` use Analyzer namespaces
Physically in `Shared` but `namespace SharpProof.Analyzer.Engine`. **Recommendation:** Move to neutral namespace (`SharpProof.Shared.Engine`).

(Checked clean: Attributes defined once; `StructuralMethodIdentity` correctly split; `SmtResourceBudget` reused;
`IsExternalInit` polyfill single-source in `Shared\Polyfills`.)

---

## Tools - SymbolicCli

### Repeated `ISymbolicCompactResult` schema-version boilerplate
Triplet (`SchemaVersion`, `EvidenceSchemaVersion`, `EvidenceSchemaCompatibility`) copy-pasted across `SymbolicCompactDomainResults.cs:25,76,175`, `SymbolicCompactQueryModels.cs:392,644`, `SymbolicCliExplainReport.cs:36`.
**Recommendation:** `SymbolicCompactResultBase : ISymbolicCompactResult` supplying the triplet by default.

### Duplicated SMT-diagnostics passthrough property block - `SymbolicCompactInvariantResults.cs:311-325` vs `SymbolicCompactQueryModels.cs:837-851`
Same 8 members over `_smtDiagnostics`. **Recommendation:** Expose from `SymbolicCompactSmtDiagnostics` / shared projection.

### Near-duplicated per-scope dispatch - `SymbolicCliInvariantResultAdapter.TryCreate:46-85`
Four point/line/span/file `case` blocks structurally identical. **Recommendation:** Generic `Build<T>(...)` / reuse `SelectScope`.

### Repeated `string.Equals(Kind,"span",...)` - `SymbolicCompactQueryModels.cs:438-450` (7 properties)
**Recommendation:** `IsSpanScope` computed once.

### Duplicated identity-column passthroughs - `SymbolicCompactComplexityResult` vs `SymbolicCompactCapabilityResult`
Identical 9-member location block + schema triplet. **Recommendation:** Shared base/interface for method-location fields.

### Identical `FilterConditionProof*` methods - `SymbolicCliTextRenderer.cs:735,746`
**Recommendation:** One generic `Filter<T>(IReadOnlyList<T>, SymbolicCliOptions, Func<T,string>)`.

### Duplicated capability-site prefix/detail formatting - `SymbolicCliTextRenderer.cs:274-277,356-359`
**Recommendation:** `FormatSite(site)` helper.

---

## Tools - Fuzz

### Repeated `using System;`/`using SharpProof.Attributes;` in generators - many `Build*` methods re-declare manually
**Recommendation:** Route all generators through `BuildClass` (accept extra `using` directives).

### Minor: conservative/expectation classification in two sites - `FuzzRunSummaryBuilder.cs` vs `FuzzExpectation`
**Recommendation:** `FuzzExpectation.Bucket` property.

---

## Tools - Baseline / Corpus / Vsix

### Input-to-SARIF resolution + temp-file cleanup duplicated - `SharpProof.Baseline/Program.cs:87-111` vs `SharpProof.CorpusReport/Program.cs:27-56`
**Recommendation:** `ResolveSarifInputsAsync(IEnumerable<string>, List<string> tempFiles)` in `SharpProof.Tools.Shared`.

### Duplicated key/comparer for `Id`/`Symbol`/`Path` - `SharpProofBaseline.cs:222-245` vs `:605-663`
Same `hash = hash*397 ^ ...` for Id/Symbol/Path. **Recommendation:** Reusable `IdentityKey` + comparer; `BaselineKey` composes it.

### Minor: duplicate `Increment` dictionary helper - `SarifCorpusReport.cs:206,211`
**Recommendation:** Generic `Increment<TKey>`.

---

## Cross-project (ProofCore / Attributes / Shared / CodeFixes / Demo / Smoke / samples / scripts)

### SMT formula node dispatch duplicated across ProofCore and Symbolic (HIGHEST VALUE)
`SmtFormula` AST defined once (`SharpProof.ProofCore\SmtFormula.cs:44-93`) but every operation re-implements a
full `switch`/`is` dispatch over all node subtypes:
- `SharpProof.ProofCore\SmtFormulaTraversal.cs:185-229` (`GetChildren`/`Rebuild`)
- `SharpProof.ProofCore\Z3FormulaEncoder.cs:108-200` (`Encode`)
- `SharpProof.Symbolic\Smt\SmtFormulaStructuralKey.cs:12-47` (`Create`)
- `SharpProof.Symbolic\Smt\SmtSyntacticClassifier.cs:678-695,723-755` (`NormalizeAliases`, normalize)
- `SharpProof.Symbolic\Smt\SmtSyntacticFormulaOperations.cs:40-150`

Note `SmtFormulaVersionRewriter.cs:70` and `SmtFormulaReferenceScanner.cs:55` already correctly reuse `SmtFormulaTraversal`.
**Recommendation:** Expose a single shared visitor/transform (`Rewrite(Func<SmtFormula,SmtFormula>)` / `MapChildren` / `ISmtFormulaVisitor<T>.Accept`) on the `SmtFormula` base built on `GetChildren`/`Rebuild`; new node types become a one-line change.

### `Shared\Constants.cs` / `Shared\BclPurityFallbackHeuristics.cs` use Analyzer namespaces (see Symbolic root)
Single-source reuse, but placement confusing; move to neutral namespace to avoid re-creation under `SharpProof.Analyzer`.

Verified clean: `SharpProof.Attributes` defined once; `StructuralMethodIdentity` correctly split (shared model + two adapters);
`SmtResourceBudget` reused by `SmtAnalysisBudget`; `IsExternalInit` polyfill single-source in `Shared\Polyfills`;
schema-version constants centralized in `Shared`.
