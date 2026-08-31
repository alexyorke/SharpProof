# LOC Reduction Opportunities

Findings from a parallel read-only survey of the solution (10 disjoint areas). No
`.codex-reduction-*.md` reports were present when this synthesis was checked, so
the repository evidence and file/line citations below are the source of truth.
Goal: reduce total lines of code without losing features — all tests must still pass.
Nothing here has been applied; each entry is a proposal with evidence.

## Summary

71 findings across 10 disjoint areas. **Total estimated reduction: ~5,230 lines.**

| Area | Est. LOC |
|---|---:|
| Test projects (Package.Test, ArchitectureTest, +14 suites) | ~1,580 |
| Worker.Test / Analyzer.Test / Effects.Test | ~1,190 |
| Build & repo infrastructure | ~440 |
| CompilerArtifact / CompilerCollector / CompilerProbe | ~370 |
| Effects / Gates | ~350 |
| Contracts / Attributes / ContractForGenerator / Testing | ~330 |
| Analyzer / Analyzer.Core | ~300 |
| Frontend / Summaries / Specs / Meta.Analyzers | ~260 |
| Ir / Dataflow / Smt / Verify / Verifier | ~230 (+~60 tests) |
| Worker / Worker.Protocol / Worker.Launcher / Host | ~180 |

### What the survey did *not* find

Genuinely dead code is rare in this repo. Six of the ten areas ran explicit reachability sweeps (declared-identifier frequency counts across the whole repo, excluding `artifacts/`, `bin/`, `obj/`) and found **zero** unreferenced types or methods. The two real deletions are:

- **`SharpProof.Dataflow` abstract-domain arithmetic** (~95 LOC) — no production callers, and it carries filed soundness bug **BUG-453** (`BUGS.md:251`), which the deletion closes.
- **17 unreferenced `compose.yaml` services** (~88 LOC) — no invocation anywhere in workflows, scripts, or docs.

Everything else is duplication, boilerplate, and accidental complexity.

### Suggested order of attack

1. **Mechanical, near-zero risk, highest volume** — the 49 `RepositoryRoot()` copies (~690), the duplicated process runner (~380), the temp-directory `try/finally` boilerplate (~200), and the diagnostic-id assertion helper (~380). ~1,650 LOC of pure extraction with no semantic change.
2. **Build infra** (~440) — self-verifying: if the hoists are wrong, the build breaks immediately.
3. **Cross-file de-duplication in production code** — the using-disposal graph (~130), the probe JSON writer (~180), the static-initializer scan (~65), the `ExecutableUnflowedDescendantsAndSelfCore` recursion (~100).
4. **Record/primary-constructor conversions** — safe individually, but check each type for equality semantics (dictionary keys, reference identity) before converting a class to a `record`.

### Cross-cutting caveats

- **`IrTraversal.GetChildren` is re-implemented three times** — in `SharpProof.Smt` (`IrSmtBackend.QueryEncoder.Children`), in `SharpProof.Testing` (`IrCSharpDifferentialOracle.TryCollectTerms`), and partially in `IrSubstitution`/`IrSemanticTerms`. Worth fixing once, centrally, rather than area by area.
- **Public API.** `SharpProof.Contracts` and `SharpProof.Attributes` ship publicly. Items flagged *PUBLIC API NOTE* would change a public shape; treat those as opt-in.
- **Generated files.** `*.generated.cs` comes from `scripts/Generate-*.ps1` and the `*.schema.json` models — change the generator template, never the output.
- **Meta-analyzers.** The repo ships `SharpProofSoundnessAnalyzer` and `CancellationBoundaryAnalyzer`, which pin specific type and member names in the Worker/cancellation plumbing. Any rename there must be re-checked against `SharpProof.Meta.Analyzers.Test`.
- **Estimates are estimates.** Each is a line count against the current formatting, not a measured diff.

### Validation protocol

Treat every entry as a proposal, not an approved deletion. For each change, first
re-run the cited searches and confirm the exact caller/test set is unchanged;
then run the smallest affected project test target and build. For shared helpers,
MSBuild/compose/workflow changes, run the relevant architecture, packaging, and
container-contract checks as well. Finish with the repository-prescribed broader
gate (`docker compose run --rm tooling test-changed` or the applicable release
gate), and inspect generated/package contents where the proposal touches them.
Any public API, generated file, analyzer-pinned symbol, equality implementation,
serialization shape, or deterministic test fixture should be classified
*uncertain* until its compatibility check passes. Deletion candidates must also
be re-searched after the edit and have their dedicated tests removed only when
those tests exercise no remaining behavior.

---

## Worker / Worker.Protocol / Worker.Launcher / Host

**Estimated savings: ~180 LOC**

### 1. Boilerplate carrier classes replaceable by positional records
- **Files:** `SharpProof.Host/ContainerContract.cs:8-33`; `SharpProof.Host/LinuxWorkerProcess.cs:13-24`; `SharpProof.Worker.Launcher/Program.cs:748-773`
- **Est. LOC saved:** ~50
- **Why it's safe:** All four are pure data holders with an explicit ctor + get-only properties and no equality/`ToString` contract in play. `ContainerContractInfo` is constructed in exactly one place (`ContainerContract.cs:116`) and only read via properties; `LinuxWorkerCompletion` is constructed only at `LinuxWorkerProcess.cs:113/162/165` and consumers read only `.Kind`/`.ExitCode` (`Program.cs:250`, `LauncherArgumentTests.cs:39/65/95/154/158/233`, `WorkerProgramTests.cs:114`). `PublicationMember`/`PreviousPublication` are `private sealed` nested types used only inside `Program.cs`. None is JSON-serialized (protocol DTOs live in `ProtocolModel.generated.cs`, untouched).
- **Proposed change:** Convert to positional records, deleting hand-written constructors and property declarations.

### 2. Duplicated `ValidateForRequest` overloads in the protocol validator
- **Files:** `SharpProof.Worker.Protocol/ProtocolJson.cs:193-259`; caller branch `SharpProof.Worker.Launcher/Program.cs:377-400`
- **Est. LOC saved:** ~45
- **Why it's safe:** The `internal` overload (line 224) is byte-for-byte the `public` one (line 193) plus one extra `evidenceAuthority` null-check and a final argument; both end in the same `ValidateResponse(...)` call. `ValidateResponse` already declares `IWorkerResponseEvidenceAuthority? evidenceAuthority = null` (lines 313-319), so a null authority is already supported. The launcher's `if/else if/else` at `Program.cs:378-400` exists purely to pick between the two overloads with otherwise identical arguments.
- **Proposed change:** Keep one `ValidateForRequest` with an optional `evidenceAuthority`, delete the duplicate body, collapse the launcher's three-way branch into one call.

### 3. Six near-identical `Require*` JSON helpers in `ContainerContract`
- **Files:** `SharpProof.Host/ContainerContract.cs:226-291` (plus 8 call sites at lines 82-108)
- **Est. LOC saved:** ~45
- **Why it's safe:** `RequireInteger`/`RequireInteger64`/`RequireString` differ only in `TryGetInt32` vs `TryGetInt64` vs `GetString`; each has an "expected value" twin whose body is `if (Require*(...) != expected) throw` with the same message text. All are `private static` with no callers outside this file. Behaviour is exercised by `SharpProof.Worker.Test/ContainerContractTests.cs`.
- **Proposed change:** One `RequireScalar<T>(JsonElement, string name)` plus one generic `RequireMatch<T>(actual, expected, name)`, driving the eight field comparisons from a small table.

### 4. Timeout arithmetic duplicated between Launcher and Protocol
- **Files:** `SharpProof.Worker.Launcher/Program.cs:16,236-241,293-304`; `SharpProof.Worker.Protocol/WorkerExecutionEnvelope.cs:6-27`
- **Est. LOC saved:** ~15
- **Why it's safe:** `Program.TerminationCleanupReserveMilliseconds = 100` duplicates `WorkerExecutionEnvelope.CleanupReserveMilliseconds = 100`, and `ComputeHardLimit` computes exactly `projectMs + Max(1, grace - reserve)` — the same expression as `WorkerExecutionEnvelope.MaximumElapsedMilliseconds`, already used to bound the response (`ProtocolJson.cs:219,256`). The two are asserted equal today in `LauncherArgumentTests.cs:1305-1350`, so the invariant is test-pinned.
- **Proposed change:** Delete the launcher's private reserve constant and `ComputeHardLimit`; call `WorkerExecutionEnvelope.MaximumElapsedMilliseconds(request, grace)` in `RunWorker`.

### 5. Dead `Assemble` overload in `EffectClaimResultAssembler`
- **Files:** `SharpProof.Worker/EffectClaimResultAssembler.cs:16-25`
- **Est. LOC saved:** ~11
- **Why it's safe:** Repo-wide grep finds 20 call sites; every one passes either 2 arguments (target, evidence) or 4 (…, entryFeasibility, cancellationToken). The 3-argument overload has zero callers.
- **Proposed change:** Delete the 3-argument overload.

### 6. Pure forwarder helpers
- **Files:** `SharpProof.Worker/CallableClaimResultAssembler.cs:153-163`; `SharpProof.Worker.Launcher/Program.cs:811-823`
- **Est. LOC saved:** ~12
- **Why it's safe:** `CallableClaimResultAssembler.Format` wraps `WorkerProjections.FormatValue`, called once (line 143); `MapAbstention` wraps `WorkerProjections.MapAbstention` with two in-project callers and no test references. `LauncherPresentation.Level(WorkerVerifyPolicy)` / `Level(WorkerAssumptionPolicy)` both immediately call `Level((object)policy, advisory)`.
- **Proposed change:** Inline `Format`, point the two `MapAbstention` callers at `WorkerProjections.MapAbstention`, replace the two `Level` overloads with one `Level(Enum policy, string advisory)`.

> **Caveat:** the repo ships its own meta-analyzers (`SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs`, `CancellationBoundaryAnalyzer.cs`) that reference `WorkerResultAssembler` and cancellation plumbing. Any change touching those names must be re-checked against `SharpProof.Meta.Analyzers.Test`.

---

## CompilerArtifact / CompilerCollector / CompilerProbe.TestAsset

**Estimated savings: ~370 LOC** (of ~15.2k). No dead code found — every `internal` member has at least one external caller or test. `*.generated.cs` (~1.8k lines) is script-produced from `CompilerArtifactModel.schema.json` and out of scope.

### 1. Collapse the hand-rolled JSON emission in the compiler probe snapshot
- **Files:** `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs` (606 lines; 59 `ProbeJson.*` calls span 284 lines), `SharpProof.CompilerProbe.TestAsset/ProbeJson.cs:1-147`
- **Est. LOC saved:** ~180
- **Why it's safe:** Every `ProbeJson` helper takes `(StringBuilder builder, ref bool first, string name, …)`, forcing a 4-6 line call site for one logical write (284 lines for 59 calls, avg 4.8). `.editorconfig:13` sets `max_line_length = 140`, so the vertical style is not formatter-mandated. Output is byte-identical if helper behavior is unchanged. Covered by `SharpProof.Package.Test/CompilerProbeSnapshotTests.cs` and `CompilerProbeInputConsistencyTests.cs`, which assert the produced JSON.
- **Proposed change:** Replace the static `ProbeJson` + `ref bool first` protocol with a `ProbeJsonObject` writer that owns the `StringBuilder` and `first` flag (`w.String("schema", …)`). Also folds the 7 repeated `new StringBuilder()` / `first = true` / `Append('{')` … `Append('}')` preambles (lines 14, 58, 227, 289, 378, 427, 534) into one scope helper.

### 2. Merge the two near-identical summary-evidence row validators
- **Files:** `SharpProof.CompilerArtifact/CompilationFingerprint.cs:151-205` (`ValidSummaryEvidenceRow`), `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:1131-1194` (`ValidSummaryEvidenceAuthority`)
- **Est. LOC saved:** ~50
- **Why it's safe:** Same signature, same three-case `switch` on `row.Origin`, character-identical `Source`/`ImplementationIl`/`SpecificationPack` bodies apart from three deltas: `Guid.TryParseExact(…, "D", …)` vs `Guid.TryParse`, an extra `row.SourceTreeSha256.Length == 64` conjunct, and an extra `ValidSummaryEvidenceIdentity(...)` call plus a leading guard in the pack case. Each has exactly one caller (`CompilationFingerprint.cs:140`, `CompilerLoweredArtifact.cs:1125`); both in the same assembly/namespace.
- **Proposed change:** Hoist one shared `ValidSummaryEvidenceRow(row, compilation, bool authorityMode)` and have `CompilerLoweredArtifact` call it after its own two-line guard.

### 3. Table-drive the IL operand-width switch
- **Files:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs:810-888`
- **Est. LOC saved:** ~45
- **Why it's safe:** Pure opcode→operand-size classification (byte / uint16 / sbyte / int32 / int64 / none) plus a `default: return false` whitelist gate; ~50 `case` labels each on its own line. No side effects beyond advancing the `BlobReader`. Exercised by the IL-summary tests driving `TryBuild`.
- **Proposed change:** A `static readonly Dictionary<ILOpCode, IlOperandSize>` (doubling as the supported-opcode whitelist) plus a five-arm `switch (size)` doing the actual read.

### 4. Delete duplicated location/witness equality and copy helpers
- **Files:** `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:173-190, 206-213, 285-296`; `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:923-940, 942-949`; canonical copies already at `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:224-235` (`CopyLocation`) and `:237-247` (`LocationsEqual`)
- **Est. LOC saved:** ~50
- **Why it's safe:** Three definitions of `LocationsEqual`, two of `CopyLocation`. `CompilerSourceLocationAuthority` already exposes both as `internal static` in the same namespace, semantically identical (compare/copy exactly `Path, Start, Length, Line, Column`). `WitnessesEqual` is byte-identical in the two files apart from parameter names.
- **Proposed change:** Delete the private copies in favor of `CompilerSourceLocationAuthority`'s internal versions; promote one `WitnessesEqual` to `internal static` used by both.

### 5. Deduplicate `ManifestEvidence` and `Normalize(IMethodSymbol)`
- **Files:** `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:677-687` and `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:1234-1240` (+ `ManifestEvidenceMap` at `:5`); `CompilerImplementationIlSummaryLowerer.cs:376`, `CompilerSpecificationPackProvider.cs:337`, `CompilerRelationalSummaryProvider.cs:338`
- **Est. LOC saved:** ~25
- **Why it's safe:** `ManifestEvidence` exists twice mapping the same `CompilerContractEvidence`→`WorkerClaimEvidence` — once as a 5-arm switch expression, once as an index into `ManifestEvidenceMap`; the array already encodes the mapping. `Normalize` is character-identical in two files (`ReducedFrom` → `PartialImplementationPart` → `OriginalDefinition`); the third differs only in returning `ConstructedFrom`.
- **Proposed change:** Keep `ManifestEvidenceMap` plus one `internal static ManifestEvidence(...)`, delete the switch version; extract `NormalizeCandidate(IMethodSymbol)` with the two definition-flavored callers picking `OriginalDefinition`/`ConstructedFrom`.

### 6. Factor the repeated out-parameter failure resets in the replay lowerer
- **Files:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs:392-396, 407-411, 441-445` and `:154-158, 189-193, 232-236`
- **Est. LOC saved:** ~20
- **Why it's safe:** Three verbatim 5-line blocks resetting `sourceTreeOrdinal/-Path/-Sha256/-LineMapSha256` before `return false`, and three verbatim witness-detail ternaries. Purely local; no callers outside the file.
- **Proposed change:** Add a `static bool NoSourceTree(out …)` helper returning `false`; hoist the witness-detail ternary into a one-line local function.

---

## Frontend / Summaries / Specs / Meta.Analyzers

**Estimated savings: ~260 LOC** (of ~11.5k). These projects are unusually free of dead code — every internal API spot-checked (`ReferencedTypeSymbols.GetAll`, `OperationSupportCatalog.IsSupported`, all four `ContractApiMetadataRuntime` members, `CompilerConstantAdmission`) has live callers outside its own project. Savings are concentrated in boilerplate and cross-file duplication rather than deletions.

### 1. Collapse the 5-argument recursion threading in `ApiSpecTermValidator`
- **Files:** `SharpProof.Specs/ApiSpecTermValidator.cs:11-297`
- **Est. LOC saved:** ~70
- **Why it's safe:** The class is `internal static` with exactly one external caller — `SharpProof.Specs/ApiSpecTable.cs:133`. Everything else (`Validate` private overload, `ValidateCore`, `ValidateUnary`, `ValidateBinary`, `ValidateConditional`) is private and only reachable from that entry point, so the internal signature is free to change. `SharpProof.Specs.Test` covers behavior via `ApiSpecTable`.
- **Proposed change:** Move `variables`, `facets`, and the `validated` dictionary into a context struct constructed once in the public `Validate`, leaving only `(declaration, depth)` threaded. Removes 4 × 5 parameter lines plus ~10 call sites that each span 5-7 lines.

### 2. Convert the summary data classes to primary constructors
- **Files:** `SharpProof.Summaries/IrRelationalSummary.cs:98-236` (`IrSummarySignature`, `IrExceptionalSummaryExit`, `IrRelationalSummary`, `IrRelationalSummaryBuildResult`, `IrSummaryInstantiation`)
- **Est. LOC saved:** ~55
- **Why it's safe:** All five are pure carriers: a constructor that only assigns, plus get-only properties. Primary constructors preserve the exact public surface, nullability, and `throw`-on-null guards. No equality semantics change (unlike records), so dictionary/reference usage in `IrRelationalSummaryBuilder` is unaffected. The codebase already uses this style — `FrontendVariableBinding` (`SharpProof.Frontend/FrontendSubset.cs:80`), `LoweredExpression` (`RoslynOperationLowerer.cs:1103`).
- **Proposed change:** Rewrite each as `public sealed class X(...) { public T P { get; } = p; }`, deleting constructor bodies.

### 3. De-duplicate the reference-identity `IEqualityComparer` (3 verbatim copies)
- **Files:** `SharpProof.Frontend/RoslynOperationLowerer.cs:1120-1133`, `SharpProof.Frontend/CompilerIdentityBridge.cs:196-210`, `SharpProof.Specs/ApiSpecTermValidator.cs:363-377`
- **Est. LOC saved:** ~30
- **Why it's safe:** The two Frontend copies are byte-identical (`OperationReferenceComparer`, same name and body, both `private sealed` with an `Instance` singleton); the Specs one (`DeclarationReferenceComparer`) differs only in element type. All three are private nested types, so nothing outside each file references them. All three projects reference `SharpProof.Ir`, a valid home for a shared generic.
- **Proposed change:** Add one `internal sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class` in `SharpProof.Ir`; delete the three nested copies.

### 4. Alias `KnownType`/`KnownSymbols` and share `IsSameType` in Meta.Analyzers
- **Files:** `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:505-1030`, `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:900`
- **Est. LOC saved:** ~40
- **Why it's safe:** `IsSameType(ITypeSymbol?, INamedTypeSymbol?)` exists verbatim in both files (`CancellationBoundaryAnalyzer.cs:1021`, `SharpProofSoundnessAnalyzer.cs:900`) — same body, same `OriginalDefinition` comparison, both `private static`. Separately, `SharpProofSoundnessAnalyzer.KnownType.` is spelled out 22 times in `CancellationBoundaryAnalyzer.cs`, and the qualifier forces most `symbols[...]` lookups across 3 lines.
- **Proposed change:** Add `using KnownType = …SharpProofSoundnessAnalyzer.KnownType;` (same for `KnownSymbols`) to `CancellationBoundaryAnalyzer.cs`, collapsing those indexers to one line each; delete the duplicated `IsSameType`.

### 5. Merge `HasSingleResult` / `HasSingleOld` and drop the `AttributeResolution` wrapper
- **Files:** `SharpProof.Frontend/ContractApiIdentityResolver.cs:436-500` and `:577-580`
- **Est. LOC saved:** ~30
- **Why it's safe:** Both helpers are private, each called once (from `HasValidContractShape` at line 348), and differ only in parameter-list arity — both check the identical `MethodKind/Accessibility/IsStatic/Arity: 1/ReturnsByRef` shape plus the same `HasUnconstrainedTypeParameter` and return-type checks. `AttributeResolution` (line 577) is a 4-line class whose sole purpose is boxing a nullable `INamedTypeSymbol` for `ConcurrentDictionary<string, AttributeResolution>` at line 32; `ConcurrentDictionary` stores null reference values fine.
- **Proposed change:** Fold the two into `HasSingleGenericIdentityMethod(contract, name, int parameterCount)`; replace `AttributeResolution` with `ConcurrentDictionary<string, INamedTypeSymbol?>`.

### 6. Extract the duplicated `ConfigureAwait` unwrapping block
- **Files:** `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:524-538` and `:637-650`
- **Est. LOC saved:** ~15
- **Why it's safe:** Structurally identical — same `IInvocationOperation` + `{ Name: "ConfigureAwait", IsStatic: false, Parameters.Length: 1 }` pattern, same `System_Boolean` check, same `Unwrap(configureAwait.Instance)` reassignment. The first additionally asserts the containing type is `symbols.TaskOfInt32`, which can be an optional parameter.
- **Proposed change:** Add `private static IOperation? UnwrapConfigureAwait(IOperation?, INamedTypeSymbol? awaitedType = null)`; call from both sites.

### 7. Delete the write-only `Completeness` and `Termination` summary facets
- **Files:** `SharpProof.Summaries/IrRelationalSummary.cs:10-14,34-38,159,171,192,194`, `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:296`
- **Est. LOC saved:** ~20
- **Why it's safe:** Repo-wide grep for `IrSummaryCompleteness` returns only the enum declaration, the single assignment at line 171, and the property at 194 — **no reader anywhere, including tests**. `IrSummaryTermination` likewise appears only at declaration, ctor parameter, property, and one builder call site; `IrSummaryCompleteness.Incomplete` and `IrSummaryTermination.Unknown` have zero references. (`Completeness`/`Termination` hits elsewhere are the unrelated `EffectCompleteness`/`EffectTermination`/`SpecTerminationBehavior`.) Contrast `IrSummaryEffect`, which **is** read at `CompilerImplementationIlSummaryLowerer.cs:1037` and asserted at `IrRelationalSummaryTests.cs:908` — keep that one.
- **Proposed change:** Remove both enums, their properties, the ctor parameter, and the builder argument. If they are placeholders for planned incompleteness tracking, leave a one-line comment instead.

---

## Ir / Dataflow / Smt / Verify / Verifier

**Estimated savings: ~230 LOC of production code** (plus ~60 LOC of now-redundant tests), across ~7.5k lines. Highest-confidence items are #1 (grep-verified zero production callers, carries a filed soundness bug) and #2.

### 1. Dead abstract-domain arithmetic
- **Files:** `SharpProof.Dataflow/SequenceCardinalityDomain.cs:136-186` (`Append`/`Concat`/`AssumeEmpty`/`AssumeNonEmpty`), `SharpProof.Dataflow/IntervalDomain.cs:170-200,269-…` (`Add`/`AddConstant`/`TryAddBounds`)
- **Est. LOC saved:** ~95 in-area (+ ~60 of tests in `SharpProof.Dataflow.Test`)
- **Why it's safe:** Repo-wide grep shows **zero production callers**. The only production consumers of `SequenceCardinalityDomain` are `SharpProof.Worker/SpecResultDomainProjection.cs:34` and `SharpProof.Worker/WorkerProjections.generated.cs:71-82`, which use only `Empty/NonEmpty/KnownLength/Top/Bottom`. `IntervalDomain.Add` has exactly two callers, both inside `SequenceCardinalityDomain.Append/Concat`; `AddConstant` has one (`Append`); `TryAddBounds` is private, called only from `Add`. All remaining references are tests. **`BUGS.md:251` (BUG-453) records a soundness bug that lives entirely in this dead code.**
- **Proposed change:** Delete the seven members and the tests that exist only to exercise them. Closes BUG-453 by deletion.

### 2. Duplicated IR child-enumeration in the SMT encoder
- **Files:** `SharpProof.Smt/IrSmtBackend.cs:468-487` (`QueryEncoder.Children`) vs `SharpProof.Ir/IrTraversal.cs:4-21`
- **Est. LOC saved:** ~21
- **Why it's safe:** The two switches are case-for-case identical (same 7 arms, same `_ => []` fallback). `SharpProof.Ir/AssemblyInfo.cs:6` grants `InternalsVisibleTo("SharpProof.Smt")`, and `IrTraversal` is already consumed cross-assembly (`SharpProof.Verify/Backend.cs`, `SharpProof.Worker/PostconditionObligationBuilder.cs:203`, `SharpProof.Analyzer.Core/ManagedContractFacts.cs:50`). Covered by `IrSmtBackendTests.cs` depth-limit tests and `IrTraversalTests.cs:77`.
- **Proposed change:** Delete `QueryEncoder.Children`; call `IrTraversal.GetChildren` from `ValidateDepth`.

### 3. Duplicated bottom-up post-order stack walk
- **Files:** `SharpProof.Ir/IrSubstitution.cs:55-97`, `SharpProof.Ir/IrSemanticTerms.cs:141-180` (and a third near-variant in `IrSmtBackend.ValidateDepth`)
- **Est. LOC saved:** ~25
- **Why it's safe:** Structurally identical — `Stack<(IrTerm, bool ChildrenReady)>`, `memo.ContainsKey` skip, re-push-with-children, compute-from-memoized-children — differing only in the per-node computation (`RewriteNode` vs `1 + max(child depths)`) and the substitution early-exit. Both are directly tested (`IrKernelTests.cs`, `IrTraversalTests.cs`) and used in production (`PortableIrGraphCodec.cs:439`, `IrRelationalSummaryBuilder.cs:827`, `AcyclicBlockPredicateExecutor.cs:642`).
- **Proposed change:** Add one internal `IrTraversal.FoldBottomUp<T>(root, memo, combine, shortCircuit = null)`; express `IrSubstitution.Rewrite` and `IrTermAnalysis.GetDepth` in terms of it.

### 4. Hand-written `Equals`/`GetHashCode` on `IrFactory` key structs
- **Files:** `SharpProof.Ir/IrFactory.cs:836-870` (`StructuralKey`), `:872-908` (`IntSequenceKey`), `:800-834` (`ExternalIdentityBucketKey`), `:786-798` (`ExternalIdentityEntry<T>`)
- **Est. LOC saved:** ~45
- **Why it's safe:** `StructuralKey` already delegates every operation to an inner value tuple, so a `readonly record struct` is behaviourally identical. `ExternalIdentityEntry<T>` is a private 2-property immutable holder. `IntSequenceKey` alone needs a custom body (`ImmutableArray` sequence equality), but as a record struct only `Equals`/`GetHashCode` remain. All are `private` nested types of `IrFactory` (no external surface); interning is covered by `IrFactoryInvariantRegressionTests.cs` and `IrKernelTests.cs`.
- **Proposed change:** Convert to `readonly record struct` / `record`, keeping only the custom `IntSequenceKey` comparison.

### 5. Test-only members on production types
- **Files:** `SharpProof.Dataflow/ClosedAbstractDomain.cs:28-47` (`Merge`, `Compare`), `SharpProof.Smt/Z3ExpressionOwner.cs:11` (`OwnedCount`)
- **Est. LOC saved:** ~22
- **Why it's safe:** `Merge` is a pure forwarder to `Join` with one caller (`IntervalDomainTests.cs:166`). `Compare` has three callers, all tests; its `assertMonotonicity` parameter is never passed by anyone. `OwnedCount` has two callers, both in `IrSmtBackendTests.cs:599,606`.
- **Proposed change:** Delete `Merge` and `Compare` (rewrite the three assertions in terms of `Join`/`LessThanOrEqual`/`AreEquivalent`, which they already wrap). Keep `OwnedCount` if the disposal test is load-bearing.

### 6. Trivial forwarding helpers in the SMT encoder
- **Files:** `SharpProof.Smt/IrSmtBackend.cs:274-277` (`AddResourceCount`), `:657-660` (`Comparison`), `:495-499` (`QueryEncoder.Own`), `:742-752` (`EncodedValue`/`EncodedBoolean`)
- **Est. LOC saved:** ~20
- **Why it's safe:** `AddResourceCount` is a one-statement method with a single caller (`:219`). `Comparison(BoolExpr, BoolExpr)` just calls `new EncodedValue(...)`; its four call sites in `EncodeBinary` can construct directly. `Own<T>` forwards verbatim to `_owner.Own`. `EncodedValue`/`EncodedBoolean` are private 2-property immutable holders.
- **Proposed change:** Inline the three forwarders; declare `EncodedValue`/`EncodedBoolean` as `readonly record struct`.

---

## Test projects (Package.Test, ArchitectureTest, and 14 smaller suites)

**Estimated savings: ~1,580 LOC** (of ~58k). Findings 1-3 are mechanical and account for ~1,270 of that.

### 1. ~49 copies of `RepositoryRoot()` / `FindRepositoryRoot()`
- **Files:** `SharpProof.ArchitectureTest/ArchitectureTests.cs:2407`, `BoundaryEnforcementTests.cs:624`, `PublicationPlanIdentityTests.cs:88`, `AcceptanceScriptTests.cs:306`, `CoverageScriptTests.cs:1689` (+33 more in ArchitectureTest); `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:2635`, `ReleasePublicationScriptTests.cs:972`, `DependencyAuditScriptTests.cs:349`, `FinalCompilationProbeTests.cs:964`, `PackagedProductFeed.cs:360`, `WorkerMsBuildIntegrationTests.cs:4470`; `SharpProof.Contracts.Test/ContractBinderTests.cs:1637`, `BoundContractModelTests.cs:42`; `SharpProof.ContractForGenerator.Test/ContractForValidatorGeneratorTests.cs:2089`; `SharpProof.Frontend.Test/ContractApiCatalogParityTests.cs:265`, `ContractApiCatalogTests.cs:55`; `SharpProof.Ir.Test/IrModelSchemaTests.cs:480`; `SharpProof.Specs.Test/DefaultApiSpecCatalogGenerationTests.cs:761`
- **Est. LOC saved:** ~690
- **Why it's safe:** All 49 bodies are the same walk-up-until-`SharpProof.sln` loop. Variations are cosmetic: seed is `AppContext.BaseDirectory` vs `TestContext.CurrentContext.TestDirectory` (same dir under NUnit), `while` vs `for`, and the throw type. No test asserts on that throw — it is fail-fast for a broken checkout. Zero assertions live in these helpers.
- **Proposed change:** Add `RepositoryLayout.Root()` to `eng/testing/` (the repo already links `eng/testing/DiagnosticDescriptorCatalogAssertions.cs` into test projects via `<Compile Include="..\eng\testing\…">`, e.g. `SharpProof.Meta.Analyzers.Test.csproj:18`), delete the 49 private copies, add the two-line `<Compile Include>` where missing.

### 2. Duplicated external-process runner
- **Files:** `SharpProof.ArchitectureTest/AcceptanceScriptTests.cs:260,289,321`, `ChangedTestSelectionTests.cs:137,177`, `ContainerAuthorityScriptTests.cs:287,326`, `ContainerSourceCleanlinessTests.cs:368,420`, `CoverageScriptTests.cs:1643,1672,1704`, `FuzzRunnerEvidenceTests.cs:103,152`, `ProductionInventoryAuthorityTests.cs:304,333,355`, `ReleaseCoverageBaselineTests.cs:942,959,1036`, `SbomReleaseIdentityTests.cs:141`, `PackageDependencyAuthorityTests.cs:858`; `SharpProof.Package.Test/DependencyAuditScriptTests.cs:629,658`, `PackageLayoutSmokeTests.cs:2442,3482`, `ReleasePublicationScriptTests.cs:899,1052`, `FinalCompilationProbeTests.cs:977`
- **Est. LOC saved:** ~380
- **Why it's safe:** `AcceptanceScriptTests.cs:260-287` and `CoverageScriptTests.cs:1643-1670` are byte-identical (same `ProcessStartInfo` fields, same concurrent stdout/stderr read, same `WaitForExitAsync`, same 3-tuple return). The 4 copies of `DeleteDirectory`/`DeleteTemporaryRepository` are identical. The 4 copies of `AssertSuccessAsync` differ only in whether the failure message is `result.Error` or `result.Error + result.Output` — unifying on the concatenation strictly widens the diagnostic. No assertion is dropped.
- **Proposed change:** Add `TestProcess.RunAsync(...)` returning a shared `internal sealed record ProcessResult(int ExitCode, string Output, string Error)`, plus `AssertSuccessAsync` and `DeleteDirectoryTree`, to `eng/testing/`. Note `ChangedTestSelectionTests.cs:177`, `PackageDependencyAuthorityTests.cs:858` and `ReleaseQualificationMatrixTests.cs:234` use narrower shapes — they adapt to the 3-field record without loss.

### 3. `Directory.CreateTempSubdirectory` + `try/finally` boilerplate
- **Files:** `SharpProof.Package.Test/BuildTaskTests.cs` (28 sites: `:420-446, :452, :507, :560, :620, :725, :779, :960, :1019, :1191`, …), `CompilerProbeSnapshotTests.cs` (2), `LauncherArgumentTests.cs:609` (2), `PackageLayoutSmokeTests.cs` (1); `SharpProof.ArchitectureTest/ReleaseQualificationMatrixTests.cs` (2); `SharpProof.Gates.Test/PerformanceGateTests.cs` (1)
- **Est. LOC saved:** ~200
- **Why it's safe:** Purely structural. Each site is `var directory = Directory.CreateTempSubdirectory(prefix); try { … } finally { directory.Delete(recursive: true); }`. `using var directory = new TempDirectory(prefix);` preserves identical cleanup semantics (dispose runs on success and exception) and removes 4 lines plus one indentation level per site. No assertion lives in any `finally`.
- **Proposed change:** Add `internal sealed class TempDirectory : IDisposable` (exposing `FullName`) to `eng/testing/`; convert the 36 try/finally scopes to `using`.

### 4. `RunVerifier` construction boilerplate in `BuildTaskTests`
- **Files:** `SharpProof.Package.Test/BuildTaskTests.cs:427, :463, :517, :815` and 19 other `new RunVerifier` sites
- **Est. LOC saved:** ~110
- **Why it's safe:** 19 of 23 sites repeat `BuildEngine = new RecordingBuildEngine()`, and 16 repeat the identical 2-line `Executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"`. Comparing `:427-436` with `:517-527`: only `ProjectWallTimeMilliseconds`, `TerminationGraceMilliseconds`, and an optional override delegate differ. A factory with defaults plus an initializer keeps every property value byte-identical.
- **Proposed change:** Add `private static RunVerifier CreateVerifier(directory, helper, wallTime, grace)`; hoist `DotNetHost` to a `private static readonly string`.

### 5. `RequestProjectionRejects*BeforeManifestRead` family
- **Files:** `SharpProof.Package.Test/LauncherArgumentTests.cs:461,499,557,579,682,711,772,808,836`
- **Est. LOC saved:** ~110
- **Why it's safe:** Every one ends with the same two assertions — `LauncherArguments.TryParse(arguments, out var parsed) Is.True`, then `parsed.CreateRequest(out _, out _) Throws.TypeOf<ArgumentException>()` — and only the colliding argument (`--worker` / `--request` / `--result` / `--cache-directory`) varies. All carry `[Platform("Linux")]`. Both assertions survive; the per-case name survives as the `TestCase` name, so failure identification is unchanged.
- **Proposed change:** `AssertRequestProjectionRejects(worker, request, result, cacheDirectory)` plus a `[TestCaseSource]` of named tuples. **Skip** `DisabledCachePathDoesNotParticipateInIoTopology` (`:607`) — it asserts a *successful* projection, a distinct outcome.

### 6. Collision-worker staging preamble
- **Files:** `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:2299, :2345, :2388` (+4 further `CollisionWorkerPath` users)
- **Est. LOC saved:** ~90
- **Why it's safe:** Lines 2304-2320, 2349-2364 and 2392-2407 are character-for-character identical: build baseline, assert exit zero, create collision directory, copy worker, copy `.deps.json` + `.runtimeconfig.json`, compute `collisionCompanion`. The three tests then diverge (plain alias / symlink / hard link) and keep their own distinct assertions. Extracting only the shared preamble drops no assertion.
- **Proposed change:** Extract `private static async Task<string> StageCollisionWorkerAsync(ConsumerProject project)` returning the companion path.

> **Deliberately not proposed:** `SharpProof.Meta.Analyzers.Test/SharpProofSoundnessAnalyzerTests.cs` (3,407 lines) is already fully `[TestCase]`-driven with raw-string source fixtures — the volume is fixture data, not duplication. The 43 tests in `ArchitectureTests.cs` each assert a genuinely different repository invariant. The 35 tests in `FrontendLoweringTests.cs` assert distinct lowering semantics. No `[Ignore]`/`[Explicit]` tests and no commented-out test bodies were found in these projects.

---

## Effects / Gates

**Estimated savings: ~350 LOC** (Effects ~225, Gates ~125), all from de-duplication. No dead types or methods surfaced: a repo-wide identifier-frequency scan over all 170 type names and 328 public/internal method names declared in these two projects found no declaration with zero external references.

### 1. `ExceptionHandlerReachability` and `UsingDisposalEffectResolver` carry near-identical using-disposal reachability logic
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:2508` (`CanReachDeclarationDisposal`), `:2627` (`GetConcreteResourceType`), `:2646` (`GetInternalGotoTargets`), `:2680` (`IsUnconditionalAtOperationLevel`), `:3430` (`InternalGotoTargets` record) vs `SharpProof.Effects/UsingDisposalEffectResolver.cs:90, :383, :155, :189, :400`
- **Est. LOC saved:** ~130
- **Why it's safe:** The bodies are token-for-token identical except that the `UsingDisposalEffectResolver` copy already takes the environment-dependent bits as `Func<>` delegates (`canCompleteNormally`, `canMethodCompleteNormally`, `canExitAbruptly`) while `ExceptionHandlerReachability` closes over its primary-ctor fields. `GetConcreteResourceType`, `IsUnconditionalAtOperationLevel`, `GetInternalGotoTargets` and the `InternalGotoTargets` record are all `private static` / private nested with no other references.
- **Proposed change:** Move the four helpers, the record, and the delegate-parameterised `CanReachDeclarationDisposal` into one `internal static class UsingDisposalGraph`; have `ExceptionHandlerReachability` call it with its own predicates.

### 2. Static-initializer completion scan is written out three times
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:2057` (`StaticInitializationMayComplete`), `SharpProof.Effects/EffectAnalysisSession.cs:396` (`StaticInitializationCannotComplete`), `SharpProof.Effects/OperationCompletionEvaluator.cs:1149` (`StaticInitializationMayComplete`)
- **Est. LOC saved:** ~65
- **Why it's safe:** All three contain the same ~30-line body: the `IFieldSymbol/IPropertySymbol/IEventSymbol` static-initializable switch, the `DeclaringSyntaxReferences` loop, `EffectProjections.GetInitializerExpression`, `CompilationModelProvider.GetSemanticModel`, `model.GetOperation`, and a `MayCompleteNormally` check. The only variation is the predicate applied to the operation, the trailing `StaticConstructors` check, and the returned polarity. All three are `private`, so the refactor is assembly-local.
- **Proposed change:** Add `internal static bool AllStaticInitializersSatisfy(INamedTypeSymbol, Compilation, Func<IOperation, bool>)` next to `HasPotentialStaticInitialization`; each of the three becomes ~4 lines passing its own predicate and static-ctor clause.

### 3. Five hand-rolled process-launch blocks in Gates
- **Files:** `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:392-425`, `SharpProof.Gates/Performance/PackageBuildSdkPin.cs:98-135`, `SharpProof.Gates/Performance/PerformanceGate.cs:663, :1395, :714` (`TerminateProcessAsync`)
- **Est. LOC saved:** ~70
- **Why it's safe:** `grep -rn "Process.Start\|ProcessStartInfo" SharpProof.Gates` returns exactly these five plus `WorkerPerformanceProbe.StartLauncher`. The importer and SDK-pin bodies are byte-identical from `ReadToEndAsync` through the kill-tree `catch` block; only the final error message differs.
- **Proposed change:** Add `internal static class GateProcess` with `RunCapturedAsync(ProcessStartInfo, CancellationToken)` returning `(int ExitCode, string Output, string Error)` plus a `KillTree(Process)` helper; call sites build the `ProcessStartInfo` and format their own failure message.

### 4. Repeated single-type `PotentialExceptions` construction
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:169, :215, :243, :1069, :2017, :2045, :2764`
- **Est. LOC saved:** ~30
- **Why it's safe:** Every occurrence is the same 5-7 line shape — `X is { } t ? new PotentialExceptions(ImmutableHashSet.Create<INamedTypeSymbol>(SymbolEqualityComparer.Default, t), Unknown: false) : UnknownPotential`. `PotentialExceptions` is a private nested record struct (`:3426`) used nowhere outside this file, and `EmptyPotential`/`UnknownPotential` factories already exist at `:3232`/`:3238`.
- **Proposed change:** Add a sibling `Potential(INamedTypeSymbol?)` factory; collapse each site to one line.

### 5. Corpus snapshot line parsing duplicated between the gate and the format validator
- **Files:** `SharpProof.Gates/Corpus/CorpusGate.cs:548-580` (`LoadSnapshot`), `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:80-118` (`IsCanonicalData`)
- **Est. LOC saved:** ~25
- **Why it's safe:** Both split on `'|'`, require `parts.Length != 4`, `Enum.TryParse` with `ignoreCase: false`, and build the identical sorted `diagnostics` array before constructing `SnapshotExpectation`. `CorpusGate.LoadSnapshot` already calls `CorpusSnapshotFormat.ReadDataLines`, so the dependency direction is established; `IsCanonicalData` differs only by additionally checking `Enum.IsDefined` and round-tripping through `ToCanonicalLine()`.
- **Proposed change:** Expose `internal static bool TryParse(string line, out SnapshotExpectation)` on `CorpusSnapshotFormat`; `IsCanonicalData` becomes `TryParse(...) && line == expectation.ToCanonicalLine()`.

### 6. Repeated MSBuild default-valued-property XML lookup
- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:1559, :1567, :1575, :1657, :1665`
- **Est. LOC saved:** ~30
- **Why it's safe:** Five 8-line `Descendants(name).SingleOrDefault(e => …Condition == "'$(name)' == ''")` expressions differing only in the element name (`SharpProofProfile`, `SharpProofFeatures`, `SharpProofVerify`, `SharpProofVerifyPolicy`, `SharpProofAssumptionPolicy`). Grep for `== ''",` across `SharpProof.Gates` returns exactly these five.
- **Proposed change:** Add `private static XElement? FindDefaultProperty(XDocument, string name)` building the condition string from `name`; each site becomes one line.

---

## Analyzer / Analyzer.Core

**Estimated savings: ~300 LOC** (~250 high-confidence, in findings 1, 3, 4, 5, 6). No dead methods or types: every `private`/`internal` member has at least one non-declaration reference (`EncodeHierarchy`, `ThrowIfRuntimeEvaluationEnabled`, `EffectCallPreconditionPolicy.Assess` all resolve to callers in `SharpProof.CompilerCollector`, `SharpProof.Effects`, `SharpProof.Worker.Test`). `IAnalyzerSessionFactory` has 8+ implementations across tests and `SharpProof.Gates`, so it is not over-abstraction. All `DiagnosticDescriptor` declarations already live in generated, data-driven files and should not be hand-edited.

### 1. `ExecutableUnflowedDescendantsAndSelfCore` — 30 copies of the same 6-line recursion/bail-out block
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1660-1990`
- **Est. LOC saved:** ~100
- **Why it's safe:** 33 occurrences of the call (1 declaration + 2 outer callers + 30 recursive uses); every one of the 30 is wrapped in the identical `foreach (…) { yield return descendant; }` + `if (!operationFacts.MayCompleteNormally(X)) { yield break; }` shape. It is a single private static iterator; the transformation is mechanical and each switch case preserves its own ordering. Heavily covered by the RequiresReplaySoundness / NestedRequiresCallSite suites.
- **Proposed change:** Add a local iterator `Descend(IOperation)` closing over `operationFacts`, plus `WalkSequential(params IOperation?[] children)` that yields descendants and stops at the first child where `MayCompleteNormally` is false; each case collapses from ~7-16 lines to 2-4.

### 2. Three-valued `ConstantPatternMatch` lattice duplicates `SwitchExpressionSelection` in Effects
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:2172-2385` vs `SharpProof.Effects/SwitchExpressionFacts.cs:459-520+`
- **Est. LOC saved:** ~60 conservatively (~200 if the whole evaluator is unified)
- **Why it's safe:** `SharpProof.Analyzer.Core.csproj` already project-references `SharpProof.Effects`, and `RequiresCallSiteDiscovery.cs` already calls `SwitchExpressionFacts` at lines 1368, 1397, 1407, 1415, 1468, 1470. `Negate`/`And`/`Or` are line-for-line identical modulo the enum name (`Yes/No/Unknown` ↔ `Always/Never/Maybe`); `GetPatternSelection` handles the same operation kinds with the same recursion shape. `ConstantPatternMatch` is private to this one file.
- **Proposed change:** Delete the private enum and its `Negate`/`And`/`Or`, call `SwitchExpressionFacts.GetPatternSelection`. **Caveat:** `MatchTypePattern` uses `ClassifyCommonConversion` where `SwitchExpressionFacts` uses `IsTotalPattern`, so the type-pattern branch must move into `SwitchExpressionFacts` rather than be dropped. Do the lattice helpers first, the evaluator second.

### 3. `MatchTypePattern`'s 15-case runtime-type → `SpecialType` ladder
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:2251-2285`
- **Est. LOC saved:** ~28
- **Why it's safe:** Pure, self-contained switch with no fallthrough side effects; the `_ => null` arm and the subsequent `TypeKind.Error` check preserve behavior. `SpecialType.System_SByte` appears in only one non-generated, non-test file in the repo (this one), so no other copy needs to stay in sync.
- **Proposed change:** A `static readonly Dictionary<Type, SpecialType>` plus `TryGetValue`.

### 4. Repeated candidate add-or-upgrade dedupe block
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:218-253, :277-297, :317-355`
- **Est. LOC saved:** ~35
- **Why it's safe:** Duplicate-window detection flagged lines 219/328 and 245/355 as byte-identical 10-line runs. All three construct the same `RequiresCallSiteCandidate` with the same 8 positional fields, then run the same `FindIndex` + `if (existingIndex < 0) Add else if (!existing.CanReplay && candidate.CanReplay) replace`. Only the span-match predicate differs in the third site, which parameterizes cleanly.
- **Proposed change:** Extract `AddOrUpgrade(List<…>, candidate, bool allowImplicitContainment)` and a `CreateCandidate(...)` factory.

### 5. `GetAnonymousFunctions` re-implements `ReachableOperations` verbatim
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1575-1586` and `:1620-1634`
- **Est. LOC saved:** ~13
- **Why it's safe:** `GetAnonymousFunctions(graph)` is character-for-character `ReachableOperations(graph)` plus a trailing `.OfType<IFlowAnonymousFunctionOperation>()`. Both are private statics in the same nested class.
- **Proposed change:** One-line expression body delegating to `ReachableOperations(graph).OfType<…>()`.

### 6. Repeated `SelectedAnalysisIncompleteRule` report boilerplate
- **Files:** `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs:162, :273, :403, :433`
- **Est. LOC saved:** ~18
- **Why it's safe:** All four are the same 6-9 line `context.ReportDiagnostic(Diagnostic.Create(GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule, AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(…), method.Name, <reason>))`. Two of them (`:273`, `:403`) share the identical subset-reason expression.
- **Proposed change:** Add `ReportSelectedAnalysisIncomplete(Action<Diagnostic>, Location, string, object)` plus a `DescribeSubset(LanguageSubsetDecision)` helper.

### 7. Duplicated `At(...)` helper + non-record `ResolvedCompanion`
- **Files:** `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:238-246`, `ContractForValidation/ContractForValidationEngine.cs:212-219` and `:237-248`
- **Est. LOC saved:** ~14
- **Why it's safe:** Both `At` methods are byte-identical `Diagnostic.Create` wrappers in the same namespace; `ContractForValidationEngine` already reaches across to `ContractForCompanionValidator.GetSourceLocation` (line 181), so the dependency direction is established. `ResolvedCompanion` is a 4-field immutable class with no custom equality.
- **Proposed change:** Keep one `At` (make it `internal`); convert `ResolvedCompanion` to a positional record.

### 8. Shared tail of `PrimaryConstructorCallableInventory.TryGet` / `TryGetSynthesizedDefault`
- **Files:** `SharpProof.Analyzer.Core/PrimaryConstructorCallableInventory.cs:6-36` and `:41-68`
- **Est. LOC saved:** ~10
- **Why it's safe:** The two differ only in the `Where` predicate and pre-check; the trailing `if (matches.Length != 1) return false; … NormalizeCallable(matches[0]); return true;` is identical. Both are called only from `AnalyzerFeaturePipeline.AnalyzePrimaryConstructor` (`:472-490`) and `IsDeclaration` in the same file.
- **Proposed change:** Extract `static bool TrySingle(IEnumerable<IMethodSymbol>, out IMethodSymbol)`.

### 9. `AnalyzerFeaturePipeline.DescribePlacement` is a pure forwarding wrapper with one caller
- **Files:** `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs:787-790` (caller at `:782`)
- **Est. LOC saved:** ~5
- **Why it's safe:** Repo-wide grep shows exactly two definitions — the generated `AnalyzerDiagnosticCatalog.DescribePlacement` and this wrapper — and exactly one call site. No test references it.
- **Proposed change:** Inline the generated call; delete the wrapper.

### 10. `AnalyzerConfiguration` / `AnalyzerConfigurationOption` boilerplate
- **Files:** `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs:3-33`, `Configuration/AnalyzerConfigurationOptionRegistry.cs:29-37`
- **Est. LOC saved:** ~16
- **Why it's safe:** `AnalyzerConfigurationOption` is a 3-field immutable class with no equality/`ToString` overrides, read only via `.Key`/`.AllowedValues`/`.BuildPropertyName`. `AnalyzerConfiguration` has a single private ctor and three properties each spread over 4 lines.
- **Proposed change:** Make the option a positional record; collapse the configuration's properties to one-line `{ get; }` and fold the private ctor into a primary constructor.

---

## Contracts / Attributes / ContractForGenerator / Testing / Package

**Estimated savings: ~330 LOC** (~210 low-risk/internal-only; the rest touches public type shapes or generator templates as flagged). A method-level dead-code sweep — every declared method name counted against whole-repo identifier occurrences — found **no method or type in this area with zero references**; the minimum was declaration + 1 use. All savings come from duplication and boilerplate.

### 1. Declarative model generators emit maximally verbose DTO boilerplate
- **Files:** `SharpProof.Contracts/DeclarativeModels.generated.cs:1-116`, `EffectiveContractModels.generated.cs:1-51`, `BoundContractModel.generated.cs:50-117` (templates in `scripts/Generate-DeclarativeModels.ps1`, `scripts/Generate-BoundContractModel.ps1`)
- **Est. LOC saved:** ~120
- **Why it's safe:** Pure storage types — explicit ctor that only assigns, plus get-only properties, no logic (behavior lives in the hand-written `partial` halves: `ContractClauseInventory.cs:14-25`, `BoundContracts.cs`, `EffectiveContractSourceResolver.cs:3-15`). Two PowerShell generators emit the same shape with two different formattings: `BoundContractModel.generated.cs` uses one-line `public IrTerm Condition { get; }`, `DeclarativeModels.generated.cs` uses a 4-line block for the identical construct — 17 properties × 3 extra lines.
- **Proposed change:** Unify on one emitter template and emit primary-constructor `partial` classes (or `record` for the internal-only `EffectiveContractSourceResolution`). **PUBLIC API NOTE:** `BoundContractClause`/`BoundContractVariable`/`BoundMethodContracts`/`ContractClauseOccurrence`/`ContractClauseInventory`/`ContractBindingResult` are public with *internal* constructors — a positional `record` would make the ctor public, so keep them classes and only collapse formatting (still ~50 LOC) unless a public-shape change is acceptable.

### 2. `ContractSelectionInventory` — 12 copy-pasted attribute resolutions + 12 four-line properties
- **Files:** `SharpProof.Contracts/ContractSelectionInventory.cs:25-101`
- **Est. LOC saved:** ~55
- **Why it's safe:** All 12 lines are literally `X = _identity.ResolveAttribute(ContractApiMetadata.X);` followed by a 4-line get-only property; the names already come from the single-source-of-truth catalog `ContractApiMetadata`, which `ArchitectureTests.cs:1982 ContractApiMetadataNamesHaveOneSourceOfTruth` enforces. The type is `internal`, so no public break.
- **Proposed change:** Resolve into an `ImmutableDictionary<string, INamedTypeSymbol?>` built by looping the metadata catalog; expose the named properties as one-line expression-bodied lookups (or keep only the ~5 read outside this file).

### 3. `IrCSharpDifferentialOracle.TryCollectTerms` re-implements `IrTraversal.GetChildren`
- **Files:** `SharpProof.Testing/IrCSharpDifferentialOracle.cs:201-273`; existing helper at `SharpProof.Ir/IrTraversal.cs:4-21`
- **Est. LOC saved:** ~45
- **Why it's safe:** The 55-line switch is a paired `case IrX when !TryCollectTerms(child…): return false; case IrX: break;` ladder whose only content is the child list — exactly what `IrTraversal.GetChildren` returns, and which `IrSubstitution.cs:79` and `IrSemanticTerms.cs:157` already reuse. The only extra semantics are "reject `IrOpaqueTerm`" and "reject unknown kinds", both expressible as two guards before the child loop.
- **Proposed change:** Add `[assembly: InternalsVisibleTo("SharpProof.Testing")]` to `SharpProof.Ir/AssemblyInfo.cs`; rewrite `TryCollectTerms` as type check, opaque/unknown guard, `foreach (var child in IrTraversal.GetChildren(term))` recurse.

### 4. Forwarding overload pairs that exist only to pass `CancellationToken.None`
- **Files:** `SharpProof.Contracts/EffectiveContractSourceResolver.cs:27-32,50-53,67-75`; `ContractBinder.cs:124-140`; `ContractClauseInventoryBuilder.cs:26-32,63-68`
- **Est. LOC saved:** ~45
- **Why it's safe:** All eight `CancellationToken.None` sites are pure forwarders with no other logic. `EffectiveContractSourceResolver` and its overloads are `internal`; `BindUncached`/`BindRequiresUncached`/`CreateUncached` are `private` single-call helpers used only as `GetOrAdd` delegates.
- **Proposed change:** Give the token parameter a `= default` default on the internal/private methods, delete the 3 internal forwarders, inline the three `*Uncached` helpers as lambdas at their `GetOrAdd` call sites. **PUBLIC API NOTE:** `ContractClauseInventoryBuilder.Create(IMethodSymbol, IOperation?)` is public — collapsing it adds an optional public parameter (source-compatible, binary-breaking), so either leave it or accept the signature change.

### 5. `ContractBinder`'s three-constructor chain with a boolean mode flag
- **Files:** `SharpProof.Contracts/ContractBinder.cs:16-69`
- **Est. LOC saved:** ~25
- **Why it's safe:** The `useProvidedContractSources` flag plus nullable `contractSources` exists to distinguish exactly two call shapes; the only in-repo user of the internal 4-arg ctor is `SharpProof.Analyzer.Core/AnalyzerSession.cs:85`, and every other caller (`CompilerCallableLowerer.cs:36` and ~20 test sites) uses the 2-arg public ctor.
- **Proposed change:** Keep the public 3-param ctor; replace the private+internal pair with a single internal ctor taking `EffectiveContractSourceResolver? contractSources = null`, resolving inline. The flag and one whole ctor disappear.

### 6. `WellSortedIrGenerator` operator pickers are switch ladders over contiguous ints
- **Files:** `SharpProof.Testing/WellSortedIrGenerator.cs:191-215`
- **Est. LOC saved:** ~20
- **Why it's safe:** `RandomIntegerOperator` and `RandomComparisonOperator` are private, called only from `Integer`/`Boolean` in the same file, and each maps `_random.Next(n)` onto a fixed operator list. Determinism per seed is preserved as long as array order matches current case order and `Next(n)` keeps the same `n`.
- **Proposed change:** `static readonly IrBinaryOperator[]` tables indexed by `_random.Next(table.Length)`, matching the existing `InterestingIntegers`/`NextInteger` idiom in the same file.

### 7. `UnmanagedCallingConventionTypesMatch` hand-rolled multiset match
- **Files:** `SharpProof.Contracts/ContractForSymbolMatcher.cs:702-735`
- **Est. LOC saved:** ~20
- **Why it's safe:** Private, single caller (`FunctionPointerSignaturesMatch`); the 30-line nested loop with a `matched[]` array is an unordered-multiset equality over symbols, expressible with a `Dictionary<INamedTypeSymbol,int>` keyed by the same `SymbolEqualityComparer.Default` it already uses.
- **Proposed change:** Count-based comparison, keeping the length short-circuit. (The surrounding `TypesMatch`/`*Match` family is deliberate soundness logic — not proposed for change.)

> **Checked and rejected:** `SharpProof.ContractForGenerator/ContractForValidatorGenerator.cs` is an intentionally empty package-loading facade with dedicated tests and an explicit prior-audit "retained" decision (`docs/code-usefulness-audit.md:422`); `ContractClauseSymbols` looks like a wrapper but has an independent consumer (`ContractClauseInventoryBuilder.cs:10`); `SharpProof.Package` contains no C# at all.

---

## Worker.Test / Analyzer.Test / Effects.Test

**Estimated savings: ~1,190 LOC** (~1.9% of the 62.7k lines across the three projects), all via mechanical extraction — no test method, source fixture, or assertion is removed.

> These projects use **NUnit**, so consolidation means `[TestCase]`/`[TestCaseSource]`, not xUnit `[Theory]`. Near-duplicate *whole test methods* are rare: a normalized body-similarity pass across all three projects found only 10 pairs above 0.87 similarity, and each pair asserts genuinely different expected values. The real volume is repeated boilerplate that **already has a helper somewhere in the same project**.

### 1. Collapse the 3-line "diagnostic ids" assertion into a helper
- **Files:** `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs:1749` (helper `AssertRequiresDiagnostics` already exists); re-inlined 191 times across `RequiresAndControlTests.cs`, `AnalyzerModeAndEffectTests.cs`, `RequiresCallSiteDiscoveryTests.cs`, `FinalCompilationCollectorTests.cs`, `AdvisoryActivationTests.cs`, `GeneratedCodeAnalyzerTests.cs`, `ContractApiIdentityAnalyzerTests.cs` (e.g. `RequiresAndControlTests.cs:27, :64, :130, :170`)
- **Est. LOC saved:** ~380
- **Why it's safe:** The block is verbatim `Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), <constraint>);` — 191 occurrences confirmed by regex, 198 total uses of the projection. The helper takes the same `expected` argument, so the identical NUnit constraint (`Is.EqualTo` / `Does.Contain` / `Is.Empty`) and the same failure message survive.
- **Proposed change:** Promote `AssertRequiresDiagnostics`-style helpers into `AnalyzerTestHost` as `AssertIds(diagnostics, params string[] expected)` plus `AssertIds(diagnostics, string id, int count)`; rewrite each 3-line block as one call.

### 2. Extract the repeated `"Sample"` method lookup + analyze in Effects.Test
- **Files:** `SharpProof.Effects.Test/EffectTestHost.cs` (`RequireMethod`); 106 multi-line call sites — `InfiniteLoopExitCompletionTests.cs:37, :104`, `VirtualDispatchCompletionRegressionTests.cs:29,33,37`, `ModuleInitializerOrderingRegressionTests.cs`, `EffectAnalysisTests.cs` (28 sites)
- **Est. LOC saved:** ~250
- **Why it's safe:** Every site is the same 4-line formatted call; 58 of the 106 use the literal type name `"Sample"`, and 67 additionally chain `new EffectAnalysisSession(compilation).Analyze(...)` right after. Pure formatting/indirection change — no assertion is touched.
- **Proposed change:** Add `EffectTestHost.SampleMethod(compilation, name)` and `EffectTestHost.AnalyzeSample(compilation, name)` returning `EffectMethodResult`; collapse each 4-7 line arrange block to one line.

### 3. Reuse `AnalyzeSingleCall` in `ManagedAbstractFlowTests`
- **Files:** `SharpProof.Effects.Test/ManagedAbstractFlowTests.cs:1028` (helper exists) — only 4 of 30 tests call it; 28 re-inline the CFG scaffolding (e.g. `:26-38, :52-58, :71-79, :1044-1050, :1083-1090`)
- **Est. LOC saved:** ~140
- **Why it's safe:** The inlined blocks are byte-for-byte the body of `AnalyzeSingleCall` (verified against `:1028-1042`). A second overload is needed for the `Evaluate(operation, ManagedFlowState.Empty)` variants that grab an `IArrayCreationOperation`/`IPropertyReferenceOperation` instead of the single `IInvocationOperation` — those keep their own final assertions unchanged.
- **Proposed change:** Route the 22 single-`Calls` tests through the existing `AnalyzeSingleCall`; add a sibling `EvaluateExpression(source)` helper for the `Evaluate`-based tests.

### 4. Extract the shared `Fixture`/`Positive` preamble
- **Files:** `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs` — 26 of 36 source literals open with the identical 7-line preamble (e.g. `:15-23, :44-53`)
- **Est. LOC saved:** ~155
- **Why it's safe:** Normalized class prologues compared: 26 are exactly `private static int Positive(int value) { Contract.Requires(value > 0); return value; }` with nothing else before the first `public static` member. The remaining 10 add extra members (`Holder`, `Target`, `Consume`, `Replace`) and keep their own literals. The compiled fixture text is unchanged, so every SP0027 count assertion is preserved.
- **Proposed change:** Add `private const string FixturePreamble = """…"""` and use `$$"""{{FixturePreamble}} …"""` in the 26 exact-match tests.

### 5. Helper for the `WorkerProtocolJson.Validate(...).Errors.Select(...Code)` assertion
- **Files:** `SharpProof.Worker.Test/ProtocolJsonTests.cs` — 38 exact 4-line blocks (`:275, :410, :529, :537, :568, :573, :581, :591, :608, :709, :739`, …); 79 total uses of the `error => error.Code` projection across the project
- **Est. LOC saved:** ~115
- **Why it's safe:** The block is uniformly `Assert.That(WorkerProtocolJson.Validate{,Manifest}(x).Errors.Select(static error => error.Code), <constraint>);`. A helper taking the object and the constraint preserves the exact constraint and the exact validated object. Sites that chain `.And.Contain(...)` (e.g. `:278-279`) keep the constraint-taking overload.
- **Proposed change:** Add `AssertErrorCode(request/response/manifest, params string[] codes)` and `AssertValid(...)`; collapse each block to one line.

### 6. Helper for the standard `TestProject` → request → worker → verify arrange block
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs` — 135 `TestProject.Create(...)` sites, 128 `project.CreateRequest(...)`, 136 `await worker.VerifyAsync(request)`; the exact 4-line preamble recurs at least 42 times (19× `TautologySource`, 16× `RefutationSource`)
- **Est. LOC saved:** ~110
- **Why it's safe:** Purely arrange-phase; `project`, `request` and `response` are all still handed back to the test, so any test that later mutates the project or re-verifies keeps full access. No assertion is dropped.
- **Proposed change:** Add `private static async Task<(TestProject, WorkerVerifyRequest, WorkerVerifyResponse)> RunAsync(string source, bool cacheEnabled, ISmtBackend? backend = null)`; use it for the ~40 tests on the default `CountingBackend(Unsatisfiable([]))`.

### 7. Use the file's own `GetPlatformReferences()` instead of re-inlining it twice
- **Files:** `SharpProof.Analyzer.Test/ContractApiIdentityAnalyzerTests.cs:620` (helper), duplicated inline at `:384-395` and `:449-461`
- **Est. LOC saved:** ~22
- **Why it's safe:** The two inline blocks are character-identical to the helper body (same `TRUSTED_PLATFORM_ASSEMBLIES` read, same `SharpProof.Attributes.dll` filter, same `CreateFromFile` projection); each differs only in the reference appended afterwards, which stays at the call site.
- **Proposed change:** Replace both with `GetPlatformReferences().Cast<MetadataReference>().Append(reference)`.

### 8. Merge the two identical protocol-scaling perf tests
- **Files:** `SharpProof.Worker.Test/ProtocolJsonTests.cs:28-45` and `:47-64`
- **Est. LOC saved:** ~15
- **Why it's safe:** `ValidResponseValidationDoesNotRescanManifestRows` and `ProtocolCanonicalizationDoesNotRescanManifestRows` are structurally identical (warm-up at size 4, measure small, measure large, assert `large <= small*16 + 250ms`, same message format); they differ only in the measure function and two size constants. A `[TestCaseSource]` keeps both measurements and both assertions distinct.
- **Proposed change:** One parameterized test with cases `(MeasureValidation, 1024, 8192)` and `(MeasureCanonicalization, 512, 4096)`.

> **Checked and not recommended:** no `[Ignore]`/`[Explicit]`/commented-out tests (the 7 `Assert.Ignore` calls are legitimate runtime platform gates); no dead private helpers; `SharpProof.Testing/` is not referenced by any of these three projects, so nothing is being re-implemented from it; duplicated raw-string source literals total only ~217 lines in Worker.Test, ~52 in Analyzer.Test, ~4 in Effects.Test and are mostly 5-8 line one-off pairs not worth a shared constant beyond finding 4.

---

## Build & repo infrastructure

**Estimated savings: ~440 LOC.** Largest wins: test-csproj hoist ~140, dead `compose.yaml` services ~88, non-test csproj hoist ~48.

### 1. Hoist identical test-project boilerplate into `Directory.Build.props`
- **Files:** all 19 `*.Test`/`ArchitectureTest` csproj, e.g. `SharpProof.Ir.Test/SharpProof.Ir.Test.csproj:4-12`, `SharpProof.Verify.Test/…csproj:4-12`, `SharpProof.Attributes.Test/…csproj:4-12`; target `Directory.Build.props:24`
- **Est. LOC saved:** ~140
- **Why it's safe:** Every one of the 19 declares the same `TreatWarningsAsErrors`, `IsPackable`, `IsTestProject` and the same three `PackageReference`s (`Microsoft.NET.Test.Sdk`, `NUnit`, `NUnit3TestAdapter`). Grep confirms `NUnit3TestAdapter` appears in exactly 19 csproj files — every project matching `Test$` and no other (`SharpProof.Testing`, `SharpProof.CompilerProbe.TestAsset` = 0). `Directory.Build.props:24` already computes `SharpProofTestProject` via `Regex.IsMatch($(MSBuildProjectName), 'Test$')`, which selects exactly that set.
- **Proposed change:** In `Directory.Build.props` under `Condition="'$(SharpProofTestProject)' == 'true'"`, set the three properties and add the three `PackageReference`s; delete those lines (and now-empty `ItemGroup`s) from all 19. Versions already come from `Directory.Packages.props`.

### 2. Collapse the 17 duplicate per-command `compose.yaml` services
- **Files:** `compose.yaml:79-166` (`restore`, `build`, `check`, `test-changed`, `semantic-tests`, `portable-tests`, `worker-tests`, `package-tests`, `package-consumers`, `performance`, `performance-smoke`, `coverage`, `mutation`, `dependency-audit`, `acceptance`, `pack`, `pilots`)
- **Est. LOC saved:** ~88
- **Why it's safe:** Each is `<<: *sharpproof-common` plus a `command:` and `profiles:` — behaviourally identical to `docker compose run --rm tooling <command>`. Grepping every `.yml`, `.ps1`, `.psm1`, `.sh`, `.md`, `.json` in the repo for `compose run --rm <service>` / `compose up <service>` returns **0 hits for all 17**; every real invocation goes through `tooling` (`.github/workflows/ci.yml:34`, `nightly.yml:28,32,36,41`, `coverage.yml:62`, `weekly.yml:28`, `package-consumers.yml` ×12, `security-reusable.yml:45,69`, `AGENTS.md:4`, `CONTRIBUTING.md:27`, `docs/getting-started.md:151-153`). The `sp <cmd>` names in docs are the container entrypoint's dispatch (`eng/container/entrypoint.sh:70`), not compose services. Only `dev`, `loop`, `tooling` are referenced.
- **Proposed change:** Delete the 17 task services, keeping `dev`, `loop`, `tooling`. Re-run `scripts/Test-SharpProofContainerContract.ps1` (it parses the services mapping generically from line 278) and `SharpProof.ArchitectureTest/BuildSchedulingTests.cs` to confirm neither pins a service name.

### 3. Hoist `IsPackable`/`TreatWarningsAsErrors` defaults for the non-test projects
- **Files:** ~27 library/tool csproj, e.g. `SharpProof.Ir/SharpProof.Ir.csproj:4-5`, `SharpProof.Smt/…csproj:4-5`, `SharpProof.Worker/…csproj:5-6`, `Tools/SharpProof.Fuzz/…csproj:8-9`
- **Est. LOC saved:** ~48
- **Why it's safe:** Grep across all csproj shows `<TreatWarningsAsErrors>true</…>` 46× and `<IsPackable>false</…>` 46×, always with the same value. The only exceptions are the three packable projects (`SharpProof.Attributes` `IsPackable=true`, `SharpProof.Package`, `SharpProof.Verifier`), `SharpProof.Smoke.Net472`, and `samples/Diagnostics` (`TreatWarningsAsErrors=false`) — all of which keep an explicit override.
- **Proposed change:** Default both in `Directory.Build.props` (conditioned on empty); keep explicit overrides in the ~4 exceptions; delete the repeated lines elsewhere.

### 4. Hoist the sample and pilot csproj bodies into their `Directory.Build.props`
- **Files:** `samples/*/*.csproj` (8 files, 5 byte-identical), `eng/pilots/*/*.csproj` (5 files); targets `samples/Directory.Build.props`, `eng/pilots/Directory.Build.props`
- **Est. LOC saved:** ~55
- **Why it's safe:** `samples/ContractFor`, `Effects`, `MalformedContract`, `Preconditions`, `TrustedBoundary` are literally identical (`OutputType=Library` + the same `SharpProof` PackageReference at `$(SharpProofSamplePackageVersion)`); `Diagnostics` adds one property. Four of five pilots are identical except for their one third-party package. `eng/pilots/Directory.Build.props` already demonstrates the pattern by hoisting the `SharpProof.Verifier` PackageReference.
- **Proposed change:** Move `<OutputType>Library</OutputType>` and the shared `SharpProof` PackageReference into each directory's `Directory.Build.props`; leave per-project files holding only genuine differences (`samples/Outcomes` needs the `SharpProof` ref excluded — it references only `SharpProof.Verifier`; `samples/Library` and `eng/pilots/OneOfMixedStrict` keep their extra properties).

### 5. `weekly.yml` is a strict subset of `nightly.yml`
- **Files:** `.github/workflows/weekly.yml:1-38`, cf. `.github/workflows/nightly.yml:34-37`
- **Est. LOC saved:** ~38
- **Why it's safe:** weekly's only real step is `docker compose run --rm tooling acceptance -Configuration Release`; nightly runs the identical command daily (`nightly.yml:36`) on the same runner with the same `build-tooling` action and uploads the same `artifacts` path. The only difference is artifact retention (90 vs 30 days).
- **Proposed change:** Delete `weekly.yml`; if the longer retention matters, raise `nightly.yml`'s `retention-days`, or keep weekly only as an added `schedule:` entry in `nightly.yml`.

### 6. De-duplicate the consumer-contract validation between source-tree and packaged props
- **Files:** `SharpProof.AnalyzerConsumer.props:5-14,111-136` vs `SharpProof.Package/buildTransitive/SharpProof.targets:12-20,65-94`
- **Est. LOC saved:** ~30
- **Why it's safe:** The profile/features/verify normalization block (`SharpProofProfile` default, `_SharpProofProfileNormalized`, `_SharpProofFeaturesNormalized`, `SharpProofVerify` defaulting, `_SharpProofContractsRuntimeEnabled`) is character-for-character identical in both files, and 7 of the 9 `<Error>` conditions in `_SharpProofValidateSourceTreeConfiguration` are verbatim copies of those in `_SharpProofValidateConfiguration`; `_SharpProofRequireSourceTreeVerifierPackage` duplicates `_SharpProofRequireVerifierPackage` apart from the message text.
- **Proposed change:** Extract the shared normalization + validation into one `SharpProof.ConsumerContract.props` that both import; ship the packaged copy in the nupkg's `buildTransitive` so it stays self-contained.

### 7. Composite action for the repeated checkout / download-artifact / build-tooling prelude
- **Files:** `.github/workflows/package-consumers.yml:34-40, 101-115, 180-192, 285-296, 331-341` (5 jobs)
- **Est. LOC saved:** ~25
- **Why it's safe:** Five jobs repeat the same three steps with identical pinned SHAs (`actions/checkout` with `fetch-depth: 0`, `actions/download-artifact` with the same name/path, then `uses: ./.github/actions/build-tooling`). The repo already has the composite-action pattern at `.github/actions/build-tooling/action.yml`.
- **Proposed change:** Add `.github/actions/prepare-qualified-packages/action.yml`; replace the five copies with a single `uses:` line each.

### 8. Replace the hand-maintained `SharpProofProductionProject` name list with an exclusion list
- **Files:** `Directory.Build.props:28-51`
- **Est. LOC saved:** ~14
- **Why it's safe:** The 24-name enumeration is precisely "every csproj except the test projects, `samples/`, `eng/pilots/`, and 6 named non-production projects (`SharpProof.Testing`, `SharpProof.Package`, `SharpProof.Verifier`, `SharpProof.Smoke.Net472`, `SharpProof.CompilerProbe.TestAsset`, `SharpProof.PortableAnalyzer`)" — verified against the full csproj inventory.
- **Proposed change:** Compute as `'$(SharpProofTestProject)' != 'true'` AND not in a short exclusion list, so adding a production project no longer requires editing this file.
