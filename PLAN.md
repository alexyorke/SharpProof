# SharpProof Net LOC Reduction Plan

## Objective

Reduce the repository's total handwritten code while preserving every feature and
every logical test. Reductions may delete dead internal support, consolidate
duplicate behavior, replace handwritten special cases with generic mechanisms,
or convert repeated test scaffolding into named data-driven cases.

The target portfolio identified by the audit is approximately 1,950-2,600 net
lines:

| Area | Estimated net removal |
|---|---:|
| `MethodEffectsTests` data-driven conversion | 1,000-1,400 |
| Dead or redundant test infrastructure | 316-319 |
| Removal of the resulting test-support project | 28-30 |
| Other test consolidation | 130-200 |
| `SharpProof.Symbolic` production code | 175-229 |
| Analyzer and ProofCore production code | 228-308 |
| Tooling implementation | 35-45 |
| Scripts and project configuration | 45-65 |
| Total after known overlaps | 1,950-2,600 |

The current production baseline from
`scripts/Measure-ProductionCSharp.ps1 -Json` is:

- 190 handwritten production C# files.
- 27,045 physical lines.
- 26,953 nonblank lines.
- 33,366 physical-line historical baseline.
- 6,321 physical lines already removed from that baseline.

Production-library reductions account for approximately 403-537 lines in this
plan. Most of the larger remaining opportunity is repeated test scaffolding and
dead test infrastructure.

## Non-Negotiable Constraints

- Do not remove a feature, supported language construct, diagnostic, public API,
  command, package behavior, or documented behavior.
- Do not remove a logical test case.
- Preserve the names and expanded count of NUnit cases when converting tests to
  data-driven form.
- Preserve diagnostic locations, evidence keys, provenance strings, witness
  behavior, cache identities, and stable enum values.
- Do not count generated code, formatting-only compaction, comment deletion,
  minification, or moving code into data as a reduction unless the resulting
  representation is genuinely simpler and remains maintainable.
- Keep every tranche net-negative after any helpers and regression tests added
  for it.
- Preserve unrelated user changes in a dirty worktree.
- Run all long-lived .NET commands through
  `scripts/Invoke-SharpProofDotnet.ps1`.
- Stop or change approach after one timeout or two same-family failures.

## Baseline and Measurement

Before the first implementation change:

1. Record `git status --short`.
2. Run `scripts/Measure-ProductionCSharp.ps1 -Json`.
3. Capture NUnit discovery output for both test projects, including every
   expanded case name:

   ```powershell
   .\scripts\Invoke-SharpProofDotnet.ps1 -TimeoutSeconds 600 test SharpProof.Test\SharpProof.Test.csproj --configuration Release --no-restore --list-tests
   .\scripts\Invoke-SharpProofDotnet.ps1 -TimeoutSeconds 600 test SharpProof.ToolingTest\SharpProof.ToolingTest.csproj --configuration Release --no-restore --list-tests
   ```

4. Record the discovered test count and normalized list of test names.
5. Run the existing targeted suites needed to establish a clean baseline.
6. After every tranche, measure physical and nonblank lines and compare the
   discovered test list with the baseline.

The acceptance check for test refactors is exact logical-case preservation, not
merely a green pass/fail result.

## Recommended Execution Order

The first tranche should combine the safest dead support removal with the
highest-value symbolic correctness cleanup. It should remove approximately
500-600 net lines before attempting the large test matrix.

Recommended commit sequence:

1. Remove dead test support and duplicate repository utilities.
2. Collapse `SharpProof.Testing`.
3. Replace switch-specific symbolic substitution with the generic IR rewriter.
4. Remove dead symbolic models and centralize fact factories.
5. Consolidate the remaining Symbolic implementation opportunities.
6. Consolidate Analyzer and ProofCore implementation.
7. Convert `MethodEffectsTests` to named data-driven cases.
8. Consolidate the remaining tests.
9. Simplify tooling, scripts, and validation-gated project configuration.
10. Re-run the complete build, test, package-consumer, and measurement gates.

## Phase 1: Remove Dead Test Infrastructure

Target: 316-319 net lines, low risk.

### 1.1 Delete declaration-only support

- [x] Delete `SharpProof.Testing/RoslynTestFixture.cs` (58 lines).
- [x] Delete `SharpProof.Testing/MsBuildPropertyTestResolver.cs` (19 lines).
- [x] Delete `SharpProof.Testing/SymbolicSourceQueryTestSession.cs` (53 lines).
- [x] Confirm a repository-wide exact search still finds no callers before
      deleting each type.

These are internal types in a non-packable test-support project and have no
repository consumers.

### 1.2 Shrink `AnalyzerTestHost`

In `SharpProof.Testing/AnalyzerTestHost.cs`:

- [x] Delete the unused condition-context and implication-context caches.
- [x] Delete `CreateConditionContext` and its core builder.
- [x] Delete `CreateConditionImplicationContext` and its core builder.
- [x] Delete their record structs.
- [x] Delete unused `CreateAnalyzerOptions`.
- [x] Replace the custom empty analyzer-options provider with the direct empty
      Roslyn representation if the remaining analyzer host still needs options.
- [x] Delete the custom `TestAnalyzerConfigOptionsProvider` and
      `TestAnalyzerConfigOptions` if no callers remain.

Expected reduction: approximately 93 lines.

### 1.3 Replace the sole `CreateSourceContext` caller

The sole caller is in
`SharpProof.Test/MethodEffectsTests.cs` near line 2548.

- [x] Replace `AnalyzerTestHost.CreateSourceContext` with the existing
      `SymbolicSourceCompilation.Create`.
- [x] Obtain the syntax root, semantic model, method symbol, and local symbol
      directly from the returned syntax tree and compilation.
- [x] Delete `CreateSourceContext`, `CreateSourceContextCore`,
      `SourceContextCache`, `SourceContextCacheKey`, and `SourceContext`.
- [x] Delete minimal-framework-reference construction if it becomes unused.
- [x] Delete `GetMinimalFrameworkReferences` if it becomes unused.
- [x] After deleting `SymbolicSourceQueryTestSession`, delete
      `GetTrustedPlatformReferences` if it also becomes unused.

Expected net reduction: 52-55 lines after the small caller replacement.

The framework-reference fallback behavior is not exactly identical between all
old helpers. Validate the replacement with the specific ref-local state-key
test before deleting the old setup.

### 1.4 Remove `SharpProofTargetFactory`

Only three calls remain, all in
`SharpProof.Test/SymbolicComplexityTests.cs` near lines 187-200.

- [x] Replace `LineNumber`, `AtPosition`, and `AllLines` with direct
      `SharpProofTarget` construction.
- [x] Preserve the existing test inputs, which are already valid.
- [x] Delete `SharpProof.Testing/SharpProofTargetFactory.cs`.

Expected net reduction: approximately 24 lines.

### 1.5 Reuse the existing repository-root helper

- [x] Replace the local walker in
      `SharpProof.Test/AnalyzerReleaseTrackingTests.cs` near line 52 with
      `AnalyzerTestHost.GetRepositoryRoot`.
- [x] Replace the local walker in
      `SharpProof.ToolingTest/UnifiedCliTests.cs` near line 114 the same way.
- [x] Delete both duplicate walkers.

Expected reduction: 16 lines.

### Phase 1 validation

- [x] Build both test projects through the Job Object wrapper.
- [x] Run the tests that use `AnalyzerTestHost`.
- [x] Run `MethodEffectsTests.EffectFlowStateKeyIncludesRefLocalBindings`.
- [x] Run `SymbolicComplexityTests`.
- [x] Run analyzer release-tracking and unified CLI tests.
- [x] Confirm NUnit discovery is unchanged.
- [x] Confirm the reduction is within 316-319 net lines.

## Phase 2: Collapse `SharpProof.Testing`

Target: another 28-30 net lines, low-medium risk. This phase depends on Phase
1 and must not count Phase 1 source deletion again.

After Phase 1, only the reduced `AnalyzerTestHost` and
`ReadmeExampleAttribute` should remain useful.

- [x] Compile the remaining shared support into both test projects through
      `SharpProof.Testing.props`, using explicit linked `Compile` items or a
      narrow include.
- [x] Preserve the `SharpProof.Test` namespace expected by existing tests and
      by `scripts/Generate-Readme.ps1`.
- [x] Delete `SharpProof.Testing/SharpProof.Testing.csproj` (19 lines).
- [x] Delete its three-line `AssemblyInfo.cs`.
- [x] Remove the project references from `SharpProof.Test.csproj` and
      `SharpProof.ToolingTest.csproj`.
- [x] Remove the project stanza and configuration entries from
      `SharpProof.sln`.
- [x] Remove now-unneeded `InternalsVisibleTo("SharpProof.Testing")`
      declarations from Analyzer, ProofCore, and Symbolic.
- [x] Keep direct `InternalsVisibleTo` access for `SharpProof.Test` and
      `SharpProof.ToolingTest`.

Validation:

- [x] Build `SharpProof.sln`.
- [x] Confirm both test assemblies discover the exact baseline cases.
- [x] Run README example generation and verify it still finds
      `ReadmeExampleAttribute`.
- [x] Confirm no assembly or project still references `SharpProof.Testing`.

## Phase 3: Replace Switch-Specific IR Substitution

Target: 60-65 net lines and three correctness-drift fixes.

`SharpProof.Symbolic/Smt/SwitchPathConditionBuilder.cs` contains handwritten
recursive `SubstituteCanonicalTerms` overloads near lines 266-342.
`SharpProof.Symbolic/Ir/SymbolicIrSubstitution.cs` and
`SymbolicIrTraversal.cs` already provide the generic mechanism.

- [x] Add a small name-map rewriter beside `SymbolicIrSubstitution`.
- [x] Change the call near `SwitchPathConditionBuilder.cs:209` to use the
      generic rewriter.
- [x] Delete the switch-specific term, atom, and condition substitution
      overloads.
- [x] Replace duplicate `CanCompareCanonicalTerms` with
      `SymbolicStateFactBuilder.CanCompareIrTerms`.
- [x] Preserve first-match and designation-variable semantics.

The old walker has three concrete drift defects:

1. It omits `SymbolicStringSliceTerm`.
2. It omits `SymbolicExceptionPreconditionAtom`.
3. It reconstructs `SymbolicBinaryTerm` without preserving `MayOverflow`.

Add regressions for designation variables used by switch `when` guards that
contain:

- [x] A string-slice term.
- [x] An exception-precondition atom.
- [x] Checked arithmetic whose `MayOverflow` flag must survive substitution.

Run targeted switch, source-predicate, runtime-hazard, and symbolic formula
tests before the full Symbolic suite.

## Phase 4: Consolidate Symbolic Models and Factories

Target: approximately 54-71 net lines.

### 4.1 Remove the dead invariant-summary model

Target: 20-25 lines.

- [x] Delete the unused `SymbolicInvariantFactSummary` record and its unused
      `Merge` method in `SymbolicInvariantService.cs` near lines 127-149.
- [x] Move the small count-based formatter into
      `SymbolicMergedPathFactMerger`.
- [x] In `SymbolicQueryFactSummaries.cs`, store formatted strings directly
      instead of redundant `(Text, Target)` pairs.
- [x] Delete the unreachable whitespace fallback for `Target`.
- [x] Preserve first-seen order and ordinal duplicate suppression.
- [x] Remove `CollectedProgramPoint.Position`, which is never read.

Do this as one refactor so the formatter is moved only once.

### 4.2 Centralize exact fact and condition construction

Target: 22-30 lines.

- [x] Extend the existing factory in
      `SymbolicIrLowerer.Conditions.cs` with optional `ISymbol?` and
      `evidenceKey` parameters.
- [x] Delete `ExactTruth` and `ExactRelation` from
      `SymbolicOperationLowerer.Assignments.cs`.
- [x] Route their callers through the central factory.
- [x] Delete the parallel relation factory in
      `SymbolicKnownGuardFacts.cs`.
- [x] Delete the uncalled
      `SymbolicStateFactBuilder.CreateReferenceNullCondition`.
- [x] Preserve every current symbol, evidence key, source node, and provenance
      string exactly.

### 4.3 Reuse the generic SMT value-kind classifier

Target: 12-16 lines.

- [x] Replace `SymbolicTypeLowerer.TryGetValueKind` with a delegation to
      `SymbolicFactFactory.TryGetValueKind`.
- [x] Pass `SymbolicTypeLowerer.IsIntegerSmtType` and
      `SymbolicTypeFacts.IsSymbolicReferenceLikeType` as the policies.
- [x] Delegate
      `SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType` to
      `SymbolicTypeFacts.IsBuiltInIntegralOrEnumType` if equivalence remains
      confirmed by tests.
- [x] Simplify the corresponding wrapper in `SymbolicStateFactBuilder`.

Do not collapse the policies themselves. IR lowering intentionally includes
`BigInteger`; program-point facts currently do not.

## Phase 5: Remaining Symbolic Reductions

Target: approximately 61-93 net lines.

### 5.1 Unify condition and expression unwrapping

Target: 8-12 lines.

- [x] Add an `unwrapChecked` option to the existing expression-unwrapping loop
      in `CSharpSyntaxFacts`.
- [x] Make `UnwrapConditionExpression` delegate to that loop.
- [x] Test repeated mixed nesting such as `checked((x!))`.

### 5.2 Parameterize compiler-effect read/write direction

Target: 10-15 lines.

- [x] Extract one directional argument-effect mapping helper from the read and
      write blocks in `CompilerMethodEffectAnalysis.AddSummary`.
- [x] Replace the parallel accumulator loops with
      `RecordAccess(..., bool write)`.
- [x] Retain thin `Read` and `Write` entrypoints for call-site clarity.
- [x] Reuse the existing `Map(value, write)` direction abstraction.

Preserve these invariants:

- A default `ImmutableArray<int>` means all candidate arguments.
- An explicit empty array means no relevant arguments.
- Only the default case triggers the directional `Unknown` fallback when no
  candidates exist.
- Receiver mapping uses `unboundEffects`; argument mapping currently uses
  `summary.Effects`.

### 5.3 Share collection hierarchy traversal

Target: 7-11 lines.

- [x] Introduce one ordered enumerator for self, base types, then
      `AllInterfaces`.
- [x] Reuse it from `SymbolicTypeFacts.HasInstanceInt32Member`,
      `HasInt32Indexer`, and
      `SymbolicIndexingLowerer.TryGetInt32IndexerElementType`.
- [x] Keep the declared-member predicates separate.
- [x] Preserve traversal order.

Do not reuse `CompilerMethodEffectAnalysis.FindProtocolMember`; it intentionally
visits interfaces only when the receiver itself is an interface.

### 5.4 Consolidate small symbolic helpers

- [x] Use one generic cache getter behind
      `SymbolicProofCache.TryGetResult` and `TryGetEncodedState`: 4-6 lines.
- [x] Share equality-kind and null-side selection between nullable comparison
      paths: 4-6 lines.
- [x] Preserve the difference between semantic null constants in IR lowering
      and syntactic null literals plus non-overloaded equality in flow facts.
- [x] Delete the no-op positional property redeclarations for `Status`,
      `UnknownReason`, `CacheHit`, and `Budget` in
      `SymbolicPublicModels.cs`: 4 lines.
- [x] Keep the explicit `Reason` property because it normalizes null.

### 5.5 Lower-priority bounded symbolic work

Target: 24-39 lines in total.

- [x] Share the rank-dimension multiplication fold between reference arrays and
      array creation in `SymbolicIndexingLowerer`: 4-7 lines.
- [x] Share narrowly typed validation for `System.Range` and `System.Index`
      factory and constructor shapes: 6-10 lines.
- [x] Consider replacing the generic PascalCase-to-snake builder in
      `SymbolicAnalysisLimits` with an exhaustive switch for its single enum
      member: 7-9 lines. Accept only if the compile-time maintenance tradeoff
      is preferred.
- [x] Replace identical `Sequence` and `Branch` `Cost.Max` wrappers with one
      implementation in `SymbolicComplexityAnalysisSession`: 2-5 lines.
- [x] Share only pattern-plus-guard evaluation between `TrySelectArm` and
      `TrySelectSection`: 5-8 lines.

Do not attempt a broad switch-selector abstraction. Statement switches retain
different label, default, and `goto case/default` behavior.

## Phase 6: Analyzer and ProofCore Consolidation

Target: 228-308 net lines.

### 6.1 Unify Requires and Ensures contract handling

Target: 50-65 lines, low-medium risk.

- [x] Return `ContractAttributeCondition` directly from the common collection
      path.
- [x] Delete the projection records duplicated by
      `RequiresContractHelpers` and `MethodEnsuresAnalyzer`.
- [x] Share invalid-contract filtering between Ensures and Requires.
- [x] Add one compact helper for unsupported-contract reporting.
- [x] Preserve diagnostic IDs, primary and additional locations, messages, and
      ordering.
- [x] Add a table-driven regression covering valid, invalid, unsupported,
      property-accessor, inherited, and duplicate contracts.

### 6.2 Consolidate regex parser and option state

Target: 35-45 lines, medium risk.

- [x] Replace the four repeated option branches in
      `Z3RegexPatternNormalizer` with one switch/update path.
- [x] Replace the three translator booleans with one `RegexOptionScope`.
- [x] Collapse repeated `*`, `+`, and `?` quantifier handling.
- [x] Delegate the string character-range overload to the existing range
      overload.
- [x] Remove the duplicate range merge after
      `Z3RegexCharacterRanges.Complement`.
- [x] Factor repeated parts-to-concat construction.
- [x] Add a missing `(?n)` regression case.

Use the existing regex smoke matrix as the main validation surface.

### 6.3 Remove analyzer dependency plumbing

Target: 30-37 lines, low-medium risk.

- [x] Make `SharpProofAttributeIdentityPolicy` static.
- [x] Delete its `AnalyzerSession` field and stop threading the unused session
      parameter through analyzers and helpers.
- [x] Replace the four-value symbol traversal enum with a boolean or direct
      pattern because only `None` and `PropertyForGetter` are produced.
- [x] Delete the one-layer `AnalyzerProofService`.
- [x] Let `AnalyzerSession` own `SmtAnalysisService` directly.
- [x] Update, rather than remove, the architecture assertion in
      `EffectArchitectureTests`.

### 6.4 Simplify `Z3FormulaEncoder`

Target: 28-35 lines, low risk.

- [x] Remove explicit recursive polarity cases whose children are already
      traversed by the default path.
- [x] Inline the redundant runtime-reference validation wrapper.
- [x] Inline the one-use integer-divide wrapper.
- [x] Implement `ContainsApproximateRegex` with
      `SmtFormulaTraversal.Contains`.
- [x] Remove the one-line `CanEncodeRegexOptions` forwarder.
- [x] Delete the unused regex-validation cache-count exposure chain in
      `SmtRegexValidator`, `SmtQuerySafety`, and `SmtSolver`.

Run the existing approximate-regex and regex-polarity tests.

### 6.5 Resolve ineffective nullable invalidation

Target: 20-24 lines if confirmed behaviorally redundant, medium risk.

`NullableContractAnalyzer` computes `memberFactInvalidated`, but its only branch
suppresses a diagnostic when the proof is `ProvenTrue`; the normal
non-invalidated `ProvenTrue` path also reports nothing.

- [x] Add a regression that establishes the intended behavior for a stale
      member proof.
- [x] If no downgrade is intended, delete the variable, branch, and helper and
      flatten unknown-result reporting.
- [x] If a downgrade is intended, fix the behavior instead and do not claim
      this LOC estimate.

### 6.6 Simplify expected-complexity classification

Target: 15-22 lines.

- [x] Merge the nearly identical diagnostic factories.
- [x] Replace the private classification record and enum with a named tuple
      plus existing `SymbolicComplexityComparison`.
- [x] Remove the `DeclaredComplexity` wrapper.
- [x] Remove the redundant reporting delegate.
- [x] Add compact SP0021 and SP0022 analyzer cases.

### 6.7 Remove redundant Requires call-site state

Target: 12-16 lines.

- [x] Delete the per-call-site `seen` set because
      `ContractConditionHelpers.Collect` already deduplicates conditions.
- [x] Replace `AddPropertyAccessor` ordinal mapping with
      `CreateArgumentMap`.
- [x] Retain property getter/setter and named-argument tests.

### 6.8 Rebuild SMT records with record cloning

Target: 10-15 lines.

- [x] Replace repeated unchanged-child checks and full constructors in
      `SmtFormulaTraversal.Rebuild` with a common unchanged check and `with`
      expressions.
- [x] Add a direct table covering all 14 node shapes.
- [x] Verify that regex options, conditional result kinds, and other metadata
      survive rebuilding.

### 6.9 Combine exception-contract grouping

Target: 8-14 lines.

- [x] Group identical site entries once.
- [x] Compute proven and unknown arrays within the same loop.
- [x] Share exception-list formatting.
- [x] Inline the one-call wrapper.
- [x] Add a mixed proven/unknown-site regression.

### 6.10 Small independent Analyzer and ProofCore reductions

Target: 20-35 lines total.

- [x] Merge identical exception catches in `SmtRegexValidator`.
- [x] Remove duplicated failure handling in `Z3RegexCharacterRanges`.
- [x] Simplify solver exception matching and variable collection.
- [x] Inline the one-use
      `AnalysisProofSearch.ClassifyKnownHazard`.
- [x] Inline `TypeHierarchyEnumeration.EnumerateBaseTypes`; its `includeSelf`
      argument is never false.
- [x] Match `BaseMethodDeclarationSyntax` once in
      `AnalyzerFeaturePipeline.RequiresSyntaxFallback`.
- [x] Build `AnalyzerDiagnosticCatalog` descriptor sets once.

## Phase 7: Convert `MethodEffectsTests` to a Typed Matrix

Target: 1,000-1,400 net lines, medium risk.

`SharpProof.Test/MethodEffectsTests.cs` is approximately 5,819 lines. The audit
found:

- 304 test methods or groups.
- 294 calls to `Analyze`.
- 282 uses of `Assert.Multiple`.
- 53 unique method-body structures.
- 273 methods in 22 repeated structural groups.
- Large repeated clusters around lines 119-563, 2173-2545, 2895-3782,
  3809-5365, and 5366-5604.
- 45 cases asserting `Disproven` plus `WritesStaticState`.
- 19 cases asserting `Proven` plus no `WritesStaticState`.
- 68 repeated `sealed class Box { public int State; }` preludes.
- 29 repeated `Box.Value` preludes.

### 7.1 Define the case model

- [x] Introduce an `EffectCase` containing:
  - Stable test name.
  - Source text or source builder input.
  - Target method or source line.
  - Expected purity verdict.
  - Required effect mask.
  - Forbidden effect mask.
  - Expected capability mask where applicable.
  - Expected exception facts, sites, or unknown reasons where applicable.
  - Optional custom verification callback only when the common fields cannot
    express the assertion.
- [x] Keep the case type narrow enough that adding one unusual case does not
      make all ordinary cases verbose.
- [x] Use `TestCaseData.SetName` to preserve every baseline test name.

### 7.2 Convert repeated assertion shapes

- [x] Convert the largest exact structural groups first.
- [x] Compare NUnit discovery after every batch.
- [x] Preserve each source snippet and target exactly.
- [x] Express required and forbidden effects symbolically as masks rather than
      repeated `HasFlag` assertions.
- [x] Add reusable prelude builders only where they produce a clear net
      reduction.
- [x] Keep marker/prelude helpers separate from the case runner so their
      estimated 80-90-line reduction is not counted twice.

### 7.3 Keep custom tests standalone

Do not force the cases near the following locations into an over-general model:

- Around line 1205.
- Lines 1326-1568.
- Around line 1659.
- Around line 2546.
- Around line 2719.

Also keep tests with unusual direct state inspection, symbol identity,
diagnostic-site inspection, or multi-step assertions standalone unless a
genuinely smaller reusable representation emerges.

### 7.4 Matrix validation

- [x] Compare the complete sorted expanded NUnit name list to the baseline.
- [x] Compare the expanded test count.
- [x] Run all `MethodEffectsTests`.
- [x] Run the full test suite serially if any fixture/order failures appear.
- [x] Confirm that every old case maps to one new named case.
- [x] Confirm the file is reduced by at least 1,000 net lines.

An assertion-helper-only tranche saving 500-700 lines may be used as an
intermediate step, but its savings must not be added to the final matrix
estimate.

## Phase 8: Consolidate Remaining Tests

Target: 130-200 net lines.

### 8.1 `SharpProofAnalysisSessionTests`

Target: approximately 80-115 lines.

- [x] Introduce a typed `HazardCase` for repeated
      source/session/query/predicate tests near line 289: 60-90 lines.
- [x] Share repeated proof-query setup elsewhere: another 20-25 lines.
- [x] Preserve exact hazard kind, exception type, status, operation text, and
      counterexample assertions.

### 8.2 Unified CLI tests

Target: 15-25 lines.

- [x] Extract one temporary-source command runner.
- [x] Preserve exit code, stdout, stderr, file cleanup, and argument-order
      checks.

### 8.3 Fuzz runner behavior tests

Target: 18-22 lines.

- [x] Convert the first two tests into named cases parameterized by output
      prefix, iteration, and family.
- [x] Preserve their current names.

### 8.4 Roslyn shape manifest coverage

Target: 8-12 lines.

- [x] Share the operation/syntax surface assertions.
- [x] Keep missing/extra shape diagnostics distinct.

### 8.5 ProofCore Z3 smoke tests

Target: 18-30 lines.

- [x] Use the existing `SmtTestFormula` constructors consistently.
- [x] Combine the two regex-category cases into named test data.
- [x] Preserve the existing large regex case matrix.

### 8.6 Unknown contract diagnostics

Target: 10-15 lines.

- [x] Use named cases sharing source, required diagnostic IDs, and forbidden
      diagnostic IDs.
- [x] Preserve diagnostic message and location checks.

## Phase 9: Tooling Implementation

Target: 35-45 net lines.

- [ ] In `FuzzCaseGenerator`, replace `BuildPureStringConcat` with the existing
      generic `CreateExpressionGenerator`: approximately 11 lines.
- [ ] Delete the custom empty analyzer-config provider in
      `FuzzAnalyzerConfiguration`; use `new AnalyzerOptions([])` from
      `FuzzRunner`: approximately 15 lines.
- [ ] In `SymbolicCliTestHost`, remove `BuildGate` and double-check locking;
      `Lazy<Task<string>>` already serializes initialization: 8-9 lines.
- [ ] If still net-negative, share the process runner in
      `SymbolicCliTestHost`: 6-8 lines.
- [ ] Extend `FuzzRunSummaryBuilder.Increment` with an amount and delete
      `AddAll`: approximately 3 lines.
- [ ] Fold the second Symbolic file walk in `EffectArchitectureTests` into the
      first walk: 8-10 lines.

Recalculate the bundle after implementation because the individual maxima
overlap slightly.

## Phase 10: Scripts and Project Configuration

Target: 45-65 net lines. Package and property removals are validation-gated.

### 10.1 PowerShell and package-consumer scripts

- [ ] Delete the write-only `$allExampleIds` state from
      `Generate-Readme.ps1`: approximately 4 lines.
- [ ] Share the generated-page update/report loops while retaining validation
      before writes: approximately 6 lines.
- [ ] In `Invoke-SharpProofDotnet.ps1`, construct its `List` and `HashSet`
      directly from enumerables: approximately 8 lines.
- [ ] In `Test-SharpProofPackageConsumers.ps1`, load
      `package-projects.json` and loop pack operations instead of maintaining a
      second project list and three pack blocks: 8-12 lines.
- [ ] Share package-consumer scaffolding where it remains clear: 4-6 lines.
- [ ] In `Aggregate-FuzzRun.ps1`, factor the three identical summary-union
      pipelines: 5-7 lines.
- [ ] In `scripts/package-consumers/SymbolicConsumer.cs`, simplify the
      required/graceful branch because `nativeAvailable == proofsHold`, and
      remove unreachable exit code 4: 3-4 lines.

### 10.2 Validation-gated package and property cleanup

- [ ] Verify whether the three private package references in
      `SharpProof.Test.csproj` have any compile, runtime, binding-redirect, or
      test-host role. Remove them only after a clean build/load probe.
- [ ] Delete the trailing empty `ItemGroup` in that project.
- [ ] Verify and remove the unused analyzer-testing NUnit package from
      `SharpProof.Testing.props`.
- [ ] Verify whether `SharpProofVsixVersion` is consumed by pack or release
      automation.
- [ ] Verify whether the explicit `PackageVersion` in
      `SharpProof.PackageMetadata.props` is redundant.
- [ ] Do not count any package/property line until pack and consumer validation
      succeeds.

## Overlap Ledger

The following estimates are alternatives or dependencies and must not be added
twice:

- The 500-700-line `MethodEffectsTests` assertion-helper option is an
  intermediate alternative to the 1,000-1,400-line typed matrix.
- Prelude/marker helpers are included in the matrix estimate if implemented as
  part of it.
- Deleting `CreateSourceContext` includes the minimal-framework-reference and
  source-context cache removal.
- Collapsing `SharpProof.Testing` is additional to source cleanup, but must not
  recount deleted source files.
- Contract collection and unsupported-reporting work is one combined 50-65
  estimate.
- Shared diagnostic `AdditionalLocations` helpers overlap the contract
  consolidation.
- Symbolic exact-factory and value-kind work both touch
  `SymbolicStateFactBuilder`; their estimates already exclude double-counting.
- Regex encoder, translator, and traversal changes are distinct implementation
  commits even if one ProofCore test run validates all of them.
- Tooling process-runner and lock removal estimates overlap slightly; measure
  the final bundle rather than summing maxima.
- Removing a package reference from a project that is later deleted is not an
  additional reduction.

## Explicit Non-Opportunities

Do not pursue these as LOC reductions:

- Public attribute classes or their distinct `AttributeUsage` declarations.
- Explicit `SharpProofEffect` and `SharpProofCapability` enum members and stable
  numeric values.
- `RegexTranslationFallback` distinctions such as invalid versus overlong
  patterns.
- The typed SMT formula record hierarchy.
- Analysis-proof records carrying witness and reason semantics.
- `EffectFlowValue.Merge` and `EffectFlowState.Merge`; one joins a value
  lattice and the other joins a structured state.
- A generic map merger for locals, captures, and ref locals; it saves almost
  nothing and obscures different missing-value behavior and comparers.
- Broad genericization of `SymbolicOperationLowerer`; hazard kinds are not in a
  one-to-one relationship with exception types or preconditions.
- Broad genericization of `SymbolicIndexingLowerer`; the strongest legitimate
  reductions are the small hierarchy, dimension-fold, and Range/Index helpers
  already listed.
- A generic switch arm/section selector beyond the narrow pattern/guard helper.
- Combining Roslyn and ECMA structural-method identity adapters; their input
  models and edge cases differ.
- `SymbolEq`, which intentionally centralizes Roslyn equality.
- Bounded-cache counters consumed by symbolic cache metrics.
- Linking the two project-local `IsExternalInit.cs` files through MSBuild for a
  negligible saving.
- `.sln` to `.slnx` migration because multiple tools and tests explicitly
  consume `SharpProof.sln`.
- Generated files, vendor code, `bin`, `obj`, `artifacts`, stock ignore-file
  comments, formatting-only compaction, and comment deletion.

## Final Validation Gates

After all accepted phases:

1. Confirm the complete expanded NUnit test-name lists exactly match the
   baseline except for intentional additions.
2. Run restore, build, and tests through the Job Object wrapper:

   ```powershell
   .\scripts\Invoke-SharpProofDotnet.ps1 -TimeoutSeconds 600 restore SharpProof.sln
   .\scripts\Invoke-SharpProofDotnet.ps1 -TimeoutSeconds 600 build SharpProof.sln --configuration Release --no-restore
   .\scripts\Invoke-SharpProofDotnet.ps1 -TimeoutSeconds 1200 test SharpProof.sln --configuration Release --no-build
   ```

3. If parallel or broad tests expose ordering/fixture failures, rerun those
   failures serially.
4. Run analyzer architecture and release-tracking tests.
5. Run all regex and SMT smoke tests.
6. Run all method-effect and symbolic-complexity tests.
7. Run unified CLI and fuzz-runner behavior tests.
8. Run README generation and verify no output drift except intentional
   formatting generated from the same examples.
9. Run package creation and package-consumer validation.
10. Run `scripts/Measure-ProductionCSharp.ps1 -Json`.
11. Record total physical and nonblank net removal by phase.
12. Confirm no public API or package surface changed unintentionally.
13. Confirm `git diff --check` succeeds.
14. Review `git status --short` and preserve unrelated user changes.

## Completion Criteria

The plan is complete when:

- Every implemented phase is net-negative after its added regression tests.
- No logical test case has disappeared.
- Every converted test retains a stable, recognizable name.
- The full build, tests, documentation generation, package build, and consumer
  probes pass.
- The switch substitution regressions demonstrate preservation of string-slice,
  exception-precondition, and overflow metadata.
- Diagnostic IDs, locations, evidence, provenance, witnesses, and cache
  behavior remain stable.
- Validation-gated package and property lines are removed only when their
  absence is proven safe.
- The final measured reduction is reported without adding overlapping
  alternatives.
