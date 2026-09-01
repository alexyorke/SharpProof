# LOC Reduction Opportunities

Findings from a parallel read-only survey of the solution (10 disjoint areas). No
`.codex-reduction-*.md` reports were present when this synthesis was checked, so
the repository evidence and file/line citations below are the source of truth.
Goal: reduce total lines of code without losing features — all tests must still pass.
Nothing here has been applied; each entry is a proposal with evidence.

## Summary

135 findings across two survey rounds, 20 disjoint areas. **Total estimated reduction: ~9,950 lines.**

Round one split the C# solution by project. Round two covered what that split could not see: the 36k-line PowerShell tooling layer, the code generators and their output, the four largest single files, cross-project duplication, and the docs.

| Area | Est. LOC | Round |
|---|---:|:--:|
| `EffectAnalysisTests.cs` (single file) | ~1,977 ⚠ | 2 |
| Test projects (Package.Test, ArchitectureTest, +14 suites) | ~1,580 | 1 |
| Worker.Test / Analyzer.Test / Effects.Test | ~1,190 | 1 |
| Documentation and root markdown | ~1,025 | 2 |
| Mutation / coverage / test-orchestration scripts | ~1,010 | 2 |
| Code generators + emitted output | ~673 | 2 |
| Cross-project duplication sweep | ~525 | 2 |
| `WorkerTests.cs` + 3 siblings (deep pass) | ~505 | 2 |
| Build & repo infrastructure | ~440 | 1 |
| Fuzz tools / BuildTasks / peripheral projects | ~375 | 2 |
| CompilerArtifact / CompilerCollector / CompilerProbe | ~370 | 1 |
| Effects / Gates | ~350 | 1 |
| Contracts / Attributes / ContractForGenerator / Testing | ~330 | 1 |
| Analyzer / Analyzer.Core | ~300 | 1 |
| Frontend / Summaries / Specs / Meta.Analyzers | ~260 | 1 |
| Deep pass: ExceptionHandlerReachability / ManagedAbstractFlow / CacheSoundnessRules | ~256 | 2 |
| Ir / Dataflow / Smt / Verify / Verifier | ~230 (+~60 tests) | 1 |
| Container / acceptance / orchestration tooling | ~245 | 2 |
| Release / packaging / dependency scripts | ~200 | 2 |
| Worker / Worker.Protocol / Worker.Launcher / Host | ~180 | 1 |

⚠ **~1,300 of the `EffectAnalysisTests.cs` total is pure reformatting**, not simplification — see that section. Excluding it, the substantive total is **~8,650 lines**.

### What the survey did *not* find

Genuinely dead code is rare here. Eight areas ran explicit reachability sweeps (declared-identifier frequency counts across the whole repo, excluding `artifacts/`, `bin/`, `obj/`) and found **zero** unreferenced types or methods. The real deletions are few:

- **`SharpProof.Dataflow` abstract-domain arithmetic** (~95 LOC) — no production callers, and it carries filed soundness bug **BUG-453** (`BUGS.md:251`), which the deletion closes.
- **17 unreferenced `compose.yaml` services** (~88 LOC) — no invocation anywhere in workflows, scripts, or docs.
- **An unreachable dispatch arm** at `ExceptionHandlerReachability.cs:1227-1245` (~19 LOC) — see the latent-bug note below.
- **Dead locals and an unreachable `finally`** in `Test-SharpProofPackageConsumers.ps1:256-276` (~10 LOC).
- **Two dead parameters** on `Test-SharpProofCoverage.ps1` (~20 LOC).
- **An unreferenced script**, `scripts/Get-SharpProofModuleVersionId.ps1` (30 LOC) — referenced by nothing outside the git index.
- **A degenerate single-arm `case`** wrapping 55 lines of `entrypoint.sh` (~5 LOC).
- **Three orphaned soundness notes** in `docs/` (~157 LOC), indexed by nothing.

Everything else is duplication, boilerplate, and accidental complexity.

### Two things that are not reductions

1. **A latent bug.** `ExceptionHandlerReachability.cs:1227-1245` is unreachable because the dispatch ladder tests `IConversionOperation` three times and the second test (line 823) is unguarded with every path ending in `continue`. Deleting the block is safe — but it means a method-group-conversion null-receiver check that was evidently *intended* to run never does. That deserves its own BUGS.md entry.
2. **A decaying document.** The 848-row ledger in `docs/code-usefulness-audit.md` already lists 7 paths that no longer exist.

### Suggested order of attack

1. **Mechanical, near-zero risk, highest volume** — the 49 `RepositoryRoot()` copies (~690), the 26-site `TRUSTED_PLATFORM_ASSEMBLIES` builder (~150), the duplicated process runner (~380), the temp-directory `try/finally` boilerplate (~200), the diagnostic-id assertion helper (~380). ~1,800 LOC of pure extraction, no semantic change.
2. **Two-line template edits with large multipliers** — the generator property/ctor emitters (~493 emitted lines from ~8 lines of PowerShell change), and moving the 2,238-line mutation catalog literal to a data file (~600).
3. **Build infra** (~440) — self-verifying: if the hoists are wrong, the build breaks immediately.
4. **Docs** (~1,025) — no build risk at all; the ledger collapse alone is ~800.
5. **Cross-file de-duplication in production code** — the using-disposal graph (~130), the probe JSON writer (~180), the static-initializer scan (~65), the `ExecutableUnflowedDescendantsAndSelfCore` recursion (~100), the cross-project SHA256/CFG/model helpers (~185).
6. **Record/primary-constructor conversions** — safe individually, but check each type for equality semantics (dictionary keys, reference identity) first, and see the rejected-idea note below.

### Cross-cutting caveats

- **`IrTraversal.GetChildren` is re-implemented FOUR times** — `SharpProof.Smt` (`IrSmtBackend.QueryEncoder.Children`), `SharpProof.Testing` (`IrCSharpDifferentialOracle.TryCollectTerms`), `Tools/SharpProof.Fuzz` (two copies of `CollectVariables`), and partially in `IrSubstitution`/`IrSemanticTerms`. Four independent agents found it separately. Fix once, centrally.
- **⛔ Positional records for generated types are NOT safe** and the idea is formally rejected in the generator section: all 34 constructors in `IrModel.generated.cs` are `internal` on `public sealed` types, so conversion would promote them to public and add `Deconstruct`/`with`/value-equality — a breaking API change. The repo already emits genuine positional records where the catalog asks for them, so the current shape is deliberate.
- **Public API.** `SharpProof.Contracts` and `SharpProof.Attributes` ship publicly. Items flagged *PUBLIC API NOTE* would change a public shape; treat those as opt-in.
- **Generated files.** `*.generated.cs` comes from `scripts/Generate-*.ps1` and the `*.schema.json` models — change the generator template, never the output.
- **`README.md` is NOT generated.** `scripts/Generate-Readme.ps1` has zero write calls; it is a `-Verify` consistency checker. The only generated doc is `docs/api-spec-catalog.generated.md`.
- **`eng/acceptance/contract.json`** pins script **paths only — no digests** (`grep -c sha256` → 0). Editing script contents is free; *adding* a script or `.psm1` requires an entry (path lists at `:130-147`, `:168`, `:708`).
- **Meta-analyzers.** The repo ships `SharpProofSoundnessAnalyzer` and `CancellationBoundaryAnalyzer`, which pin specific type and member names in the Worker/cancellation plumbing. Any rename must be re-checked against `SharpProof.Meta.Analyzers.Test`.
- **Estimates are estimates.** Each is a line count against current formatting, not a measured diff.

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
---

## Mutation / coverage / test-orchestration scripts (PowerShell)

**Estimated savings: ~1,010 lines** (~600 of them from relocating the mutation catalog to a data file).

> **Caveat for whoever applies these:** `eng/acceptance/contract.json` enumerates these script paths (lines 150-190, 414-416, 676-677) and pins `mutationEvidence.expectedCatalogCount`/`expectedCatalogSha256`. Adding a new `.psm1`, or moving the catalog to a data file, requires adding that path to the contract's file list. The catalog digest itself is unaffected as long as field values do not change.

### 1. Move the 262-entry mutation catalog out of the script into a data file
- **Files:** `scripts/Test-SharpProofTrustedMutations.ps1:69-2306` — 2,238 of the file's 2,960 lines are one `$mutations = @(...)` literal of 261 `[pscustomobject]@{ Name/File/Original/Mutated/Project/Filter }` blocks
- **Est. LOC saved:** ~600 script lines (and 2,238 lines of PowerShell become a pure data file)
- **Why it's safe:** The catalog's identity is already digest-pinned and consumed structurally, not lexically. `Get-SharpProofMutationCatalogSha256` (`scripts/SharpProof.MutationEvidence.psm1:4`) hashes only the six fields, and the digest/count is compared against `eng/acceptance/contract.json` `mutationEvidence.expectedCatalogCount/expectedCatalogSha256` at `Test-SharpProofTrustedMutations.ps1:2313-2320`. The same digest function is already fed plain `ConvertFrom-Json` objects in `Test-SharpProofMutationCatalog.ps1:61` and `Invoke-SharpProofTrustedMutationsParallel.ps1:458`, proving JSON-sourced catalog objects are accepted verbatim.
- **Proposed change:** Move the catalog to `eng/mutation/catalog.json` (or a `.psd1`) and load via `Get-Content | ConvertFrom-Json`. Each entry drops the `[pscustomobject]@{`/`},` wrapper lines (261 × 2) plus the 27 `@'…'@` here-string wrapper pairs. The contract digest is unchanged because the hashed field values are unchanged.

### 2. Extract the duplicated parallel test-shard scheduler
- **Files:** `scripts/Invoke-SharpProofSemanticTests.ps1:420-575` and `scripts/Invoke-SharpProofPackageTests.ps1:565-720`
- **Est. LOC saved:** ~120
- **Why it's safe:** The two loops are structurally the same: identical `ProcessStartInfo` setup (`FileName='dotnet'`, `WorkingDirectory=$repositoryRoot`, both streams redirected), identical `running` record shape (`Process`/`StartedUtc`/`StandardOutput`/`StandardError` async reads), identical deadline + `Kill($true)` block, identical `$completed = @($running | Where-Object { $_.Process.HasExited })` / `Start-Sleep -Milliseconds 100` drain, identical stdout/stderr echo and elapsed/exitCode timing record. Only the banner text, the env var (`SHARPPROOF_TEST_PROJECT_PARALLELISM` vs `SHARPPROOF_PACKAGE_SOURCE`), slot accounting vs exclusive-shard gating, and the vstest/`dotnet test` argument builder differ — all expressible as parameters/scriptblocks. Both scripts already import `SharpProof.ContainerExecution.psm1` and already call its `New-SharpProofIsolatedTestOutput` and `Get-SharpProofTestAssemblyPath`.
- **Proposed change:** Add `Invoke-SharpProofParallelTestShards` to `SharpProof.ContainerExecution.psm1` taking the shard list, parallelism, timeout, a label, an environment hashtable and an argument-builder scriptblock; both scripts keep only their argument builders.

### 3. Collapse the repeated TRX-fixture construction
- **Files:** `scripts/Test-SharpProofMutationEvidence.ps1` — 26 `Write-Fixture` call sites, 32 `New-TestParts` call sites, 28 repetitions of the `$zeroInfrastructure` counters string
- **Est. LOC saved:** ~150
- **Why it's safe:** Every `Write-Fixture` call passes exactly `-Definitions $x.Definition -Entries $x.Entry -Results $x.Result` from a single `New-TestParts` result (or the concatenation of two), and `-Counters` is always `'total="N" executed="N" passed="P" failed="F" ' + $zeroInfrastructure`. These are local helpers in a self-contained fixture script (`param()` at line 2, no exported surface), so the change is invisible outside the file.
- **Proposed change:** Add `New-TrxFixture -Name -Parts -Failed:<n>` inside the script, deriving the counters string and forwarding `Definition`/`Entry`/`Result` (concatenating when several parts are given). Each ~10-line block becomes ~3 lines.

### 4. Share the coverage-argument preamble and `Invoke-RequiredDotnet` between the two test drivers
- **Files:** `scripts/Invoke-SharpProofSemanticTests.ps1:24-34,46-90,82-89` and `scripts/Invoke-SharpProofPackageTests.ps1:22-31,58-104,96-103`
- **Est. LOC saved:** ~55
- **Why it's safe:** Byte-identical runs confirmed by diff: the `-Fast`/`-NoBuild` guard (7 lines), the `$coverageEnabled` computation plus the "CoverageSettings and CoverageResultsDirectory must be supplied together" throw (9 lines — the only two occurrences of that message in the repo), the `$resolvedCoverageSettings`/`$resolvedCoverageResults`/`$isolatedOutputRoot` blocks (13 + 18 lines), and the `Invoke-RequiredDotnet` wrapper (8 lines, defined identically in both). Both already import `SharpProof.ContainerExecution.psm1`.
- **Proposed change:** Add `Resolve-SharpProofCoverageOptions` (returning Enabled/Settings/Results/IsolatedOutputRoot) and `Invoke-SharpProofRequiredDotnet` to the shared module; delete both copies. The `$IsLinux`/`SHARPPROOF_CONTAINER` guard differs only in its message, so it takes a `-Purpose` string.

### 5. Table-drive `New-ShardTiming` and reuse `Assert-UniqueMutationTarget`
- **Files:** `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:299-343`; `scripts/Test-SharpProofTrustedMutations.ps1:2322-2347` vs `:2531-2555`
- **Est. LOC saved:** ~40
- **Why it's safe:** `New-ShardTiming` repeats the same `$(if ($null -ne $timing) { [long]$timing.X } else { 0L })` idiom six times over five field names — a loop over a name list produces identical output. Separately, the preflight loop at `:2322-2347` reimplements exactly the find-then-uniqueness `IndexOf` check that `Assert-UniqueMutationTarget` (same file, line 2531) already performs; only the reporting style differs (accumulate vs throw).
- **Proposed change:** Build the timing object by iterating a field-name array with a default of `0`; give `Assert-UniqueMutationTarget` an optional `-Collect [List[string]]` so the preflight calls it instead of duplicating the two `IndexOf` probes.

### 6. Factor the atomic timing-evidence write
- **Files:** `scripts/Invoke-SharpProofSemanticTests.ps1:574-587`, `Invoke-SharpProofPackageTests.ps1:740-781`, `Invoke-SharpProofTrustedMutationsParallel.ps1:493-520`, plus `Invoke-SharpProofDevCheck.ps1:112-126`
- **Est. LOC saved:** ~25
- **Why it's safe:** All four sites are the identical four-step sequence — `Join-Path $repositoryRoot 'artifacts/timings'`, `CreateDirectory`, `$path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'`, `ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8NoBOM`, `Move-Item -Force`. Only the payload object differs. The semantic and package tests additionally share an identical 10-line "read prior timings when `-Fast`" loop (`Invoke-SharpProofSemanticTests.ps1:141-150` = `Invoke-SharpProofPackageTests.ps1:255-264`).
- **Proposed change:** Add `Write-SharpProofTimingEvidence -Path -Payload` and a `Get-SharpProofPriorTimingPaths -Stem -Fast` helper to the shared module.

### 7. Dead parameters on the coverage validator
- **Files:** `scripts/Test-SharpProofCoverage.ps1:10` (`-BaselinePath`), `:16` (`-ReportOnly`), and their uses at `:167-176`, `:227-229`, `:885`
- **Est. LOC saved:** ~20
- **Why it's safe:** The script's only caller in the repo is `scripts/Invoke-SharpProofContainer.ps1:490`, which splats a hashtable containing only `CoverageRoot`, `ComparisonRef`, `SummaryPath`, and conditionally `IncludeWorkingTree` (`Invoke-SharpProofContainer.ps1:481-489`). A repo-wide grep for `ReportOnly` and `BaselinePath` outside `Test-SharpProofCoverage.ps1` returns no other hits (the only `BaselinePath` matches are an unrelated local `$caseBaselinePath` in `Test-SharpProofMutationEvidence.ps1`). Nothing in `.github/`, `eng/`, `docs/`, or `compose.yaml` passes them.
- **Proposed change:** Remove both parameters and inline the default baseline path; the `-ReportOnly` removal also simplifies the `ComparisonRef`-required guard at line 227 and the final failure throw at 885.

---

## Deep pass: ExceptionHandlerReachability / ManagedAbstractFlow / CacheSoundnessRules

**Estimated savings: ~256 LOC** (~121 in `ExceptionHandlerReachability.cs`, ~22 in `ManagedAbstractFlow.cs`, ~107 in `CacheSoundnessRules.cs`).

> **Deliberate non-finding:** the immutable carriers `PotentialExceptions` (`:3430`), `CatchReachability` (`:3426`), `InternalGotoTargets` (`:3434`), `SwitchCaseReachability` (`:3453`), `ManagedAbstractValue` (`:1618`) and `ManagedFlowState` are *already* records/record structs — nothing to convert. `ManagedAbstractValue`'s record-struct value equality is load-bearing for the dataflow fixpoint (`LessThanOrEqual`/`Join` convergence) and must **not** become a class.

### 1. Thread recursion context via local functions in `GetExpressionValueNames` / `GetIdentifierValueNames`
- **Files:** `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1738-1901`
- **Est. LOC saved:** ~58
- **Why it's safe:** `GetExpressionValueNames(expression, owner, syntax, resolving, resolvingNames)` is a 5-parameter recursion where 4 of the 5 arguments are passed through *verbatim, unmodified* at every one of the 10 recursive call sites (1749, 1757, 1765, 1771, 1781, 1791, 1797, 1833, 1845, 1888). Each call occupies 6-7 physical lines to convey one varying argument. A local function is a pure syntactic rewrite — same method, same arguments, same order, same mutable `resolvingNames` set identity, so the re-entrancy guard at 1865 behaves identically.
- **Proposed change:** Add `void AddNames(ExpressionSyntax e) => names.AddRange(GetExpressionValueNames(e, owner, syntax, resolving, resolvingNames));` and reduce each 6-line recursive call to one line; `GetIdentifierValueNames` (`:1888-1893`) gets the same treatment.

### 2. Same context-threading collapse in the two `IsNonCacheable*` recursive switches
- **Files:** `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:666-716` and `:772-811`
- **Est. LOC saved:** ~40
- **Why it's safe:** `IsNonCacheableSemanticAnswer(x, root, resolving)` recurses 6 times (675, 684, 689, 695, 700, 704) always forwarding `root` and `resolving` unchanged; `IsNonCacheableNumericEnumValue(x, enumType, root, resolving)` recurses 5 times (781, 787, 794, 800, 805) always forwarding `enumType, root, resolving` unchanged. Both are `switch` *expressions*, so ordering and short-circuit semantics of `||` / `.Any(...)` are preserved exactly by a local function closing over the same variables. `resolving` is a mutable `LocalResolution` — capturing it preserves the add/remove cycle identity used by `ResolveLocal`/`ResolveNumericEnumLocal`.
- **Proposed change:** In each method add `bool Recurse(IOperation value) => …;` and collapse each 4-5 line recursive arm to one line.

### 3. Extract the repeated "virtual/abstract ⇒ Unknown, else recurse" dispatch ternary
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:326-331, 373-381, 934-945, 966-971, 995-1000, 1126-1131, 1166-1172, 1183-1190, 1197-1203`
- **Est. LOC saved:** ~38
- **Why it's safe:** All nine sites compute the same expression modulo the method symbol: `m == null || m.IsVirtual || m.IsAbstract ? UnknownPotential : GetCallableExceptions(m, activeMethods, depth + 1)`. Sites 934-945 and 995-1000 add one extra `EmptyPotential` branch for compiler-intrinsic members, so those two keep their intrinsic check in front and delegate only the tail. `GetImplicitCallableExceptions:2875` already encodes a superset of this predicate (it additionally treats `TypeKind.Interface` as unknown), confirming the shape is the intended idiom — **the new helper must not include the interface clause**, or the nine sites would become more conservative.
- **Proposed change:** Add `private PotentialExceptions ResolveDispatch(IMethodSymbol? target, HashSet<IMethodSymbol> activeMethods, int depth)` returning that exact ternary; replace the nine call sites with a one-line call.

### 4. `IConversionOperation` method-group arm at 1227-1245 is UNREACHABLE
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:1227-1245`
- **Est. LOC saved:** ~19
- **Why it's safe:** The dispatch ladder in `GetPotentialExceptions` tests `operation is IConversionOperation` three times (795, 823, 1227). The arm at **823 is unguarded** — `if (operation is IConversionOperation builtInConversion) { … }` — and every path inside it ends at the `continue;` on line 846. No `IOperation` can reach line 1227. Deleting the block cannot change any result. The immediately preceding `IDelegateCreationOperation` arm (1210-1226) *is* reachable and does the same work via `MethodGroupConversionFacts.GetDelegateConstructorCheckedTarget`.
- **Proposed change:** Delete lines 1227-1245. **⚠ Separately flag to the owners:** the method-group-conversion null-receiver check was evidently intended to run and currently never does. That is a latent semantics bug, not a reduction — worth its own BUGS.md entry.

### 5. Pack `activeMethods` + `depth` into a traversal context
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs` — 14 signatures declare `HashSet<IMethodSymbol> activeMethods, int depth`; ~30 call sites pass them on separate lines (70 mentions of `activeMethods`, 65 of them a standalone `activeMethods,` argument line)
- **Est. LOC saved:** ~40
- **Why it's safe:** Purely mechanical parameter packing. `activeMethods` is a mutable `HashSet` shared by reference across the traversal (seeded at 87), so a `readonly record struct ExceptionScanContext(HashSet<IMethodSymbol> ActiveMethods, int Depth)` preserves the reference identity the recursion-cycle guard depends on. `depth` is only ever passed as `depth` or `depth + 1`, so a `Deeper()` member reproduces both forms. Neither value is ever used as a dictionary key anywhere in the file, so record-struct value equality introduces no new semantics.
- **Proposed change:** Replace the two parameters with one `ExceptionScanContext context` across 14 signatures and all call sites; `depth + 1` becomes `context.Deeper()`.

### 6. Merge the near-identical unary / conversion operator arms
- **Files:** `SharpProof.Effects/ExceptionHandlerReachability.cs:767-794` vs `:795-822`
- **Est. LOC saved:** ~24
- **Why it's safe:** Line-for-line identical after renaming (`unary`→`conversion`): same `canCompleteNormally(operand)` guard, same `ConversionEffectClassifier.SkipsLiftedOperator(op, abstractFlow)` guard, same `AddStaticInitializationPotential` → `GetOperatorExceptions` body, same `CanThrowUnknownAfterPrerequisites` tail, same `PushChildren` + `continue`. They are adjacent and mutually exclusive (an operation cannot be both), so collapsing preserves dispatch order exactly. Both helpers take `IOperation`, so no overload resolution changes.
- **Proposed change:** Precede the arm with a pattern extracting `(operatorMethod, operand)` from `IUnaryOperation { OperatorMethod: not null }` or `IConversionOperation { OperatorMethod: not null }`, then run one shared body.

### 7. Collapse the triple untracked-ref-alias guard in `TransferCore`
- **Files:** `SharpProof.Effects/ManagedAbstractFlow.cs:264-292, 293-305, 306-318`
- **Est. LOC saved:** ~12
- **Why it's safe:** All three arms open with the identical five-line `var xAliasesUntrackedStorage = IsUntrackedRefLocal(<target>); if (xAliasesUntrackedStorage) { state = state.WithUntrackedAlias(); }` and close with the matching `if (!xAliases…) { state = SetStorage(…); }`. `WithUntrackedAlias()` is a state transformation on an immutable `ManagedFlowState` called at the same point in each arm with the same argument, so the state sequence is unchanged.
- **Proposed change:** Add `private static ManagedFlowState MarkUntrackedAlias(ManagedFlowState state, IOperation target, out bool aliased)`; use at the head of all three arms.

### 8. `TopForType` / `DefaultForType` are the same three-way type dispatch
- **Files:** `SharpProof.Effects/ManagedAbstractFlow.cs:1656-1671` vs `:1673-1688`
- **Est. LOC saved:** ~10
- **Why it's safe:** Structurally identical: same `SpecialType.System_Boolean` test, same `IntegerType(type, out …)` test, same `type?.IsReferenceType is true || IsNullableType(type)` tail, same `Unknown` fallback. Only the three produced values differ, and `DefaultForType` discards the integer semantics (`out _`). Factoring the branch selection leaves both results bit-identical.
- **Proposed change:** Add `private static TypeDomain Classify(ITypeSymbol? type, out CSharpIntegerSemantics integer)`; both methods become a single `switch` expression.

### 9. Inline a single-caller pass-through overload
- **Files:** `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1393-1401` (caller at `:1357`)
- **Est. LOC saved:** ~15
- **Why it's safe:** Repo-wide grep shows the name occurring exactly twice — declaration plus one call site. The two-argument `GetDeconstructionWriteValue` is a pure forwarder to the three-argument overload with `UnwrapValue` applied; folding it in removes 9 lines with no branch change.
- **Proposed change:** Delete the two-argument overload; inline its body at line 1357. **Explicitly skipped:** the four larger `ExceptionHandlerReachability` single-caller helpers (`ApplySwitchGuard` `:1839-1854`, `GetImplicitConstructorExceptions` `:3118-3137`, `CanDisposalUnwind` `:2603-2624`, `CanCaseClauseReachBody` `:1790-1815`) — inlining saves only ~5 signature lines each and hurts readability of soundness-critical code.

---

## Deep pass: WorkerTests.cs + CompilerManifestArtifactTests + ClaimManifestBuilderTests + WorkerTcbEdgeCaseTests

**Estimated savings: ~505 LOC** (of 14,554 across the four files).

> **Negative results (checked, nothing to report):** no dead private helpers, unused fields, or unreachable setup in any of the four files — every `private static`/`private sealed` member and every static readonly array (`InvalidBudgetErrorCodes`, `RequiredReferenceFileNames`, `ReplayedAllocationWitnessKinds` at `WorkerTests.cs:23-42`; `DenseOrdinals`, `CompanionEvidence`, `UserAndTrusted` at `ClaimManifestBuilderTests.cs:18-26`) has a live reference. No strictly-subsumed tests. `TestProject` (`WorkerTests.cs:7117-7368`, ~250 lines) is **not** duplicated — it is private to that file and the other three build `CSharpCompilation` directly via the already-shared `WorkerTestMetadataReferences`.

### 1. `AssertClaimVerdict` helper for the single-claim outcome/reason assertion cluster
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs:2214, 2376, 2412, 2525, 2561, 2598, 2677, 3903, 3938, 4108, 4243, 4276, 4316, 4424` (plus 16 more using `response.ClaimResults.Single()`; 30 total)
- **Est. LOC saved:** ~150
- **Why it's safe:** Every site is the identical shape `Assert.That(response.Errors, Is.Empty); var record = response.ClaimResults.Single(); using (Assert.EnterMultipleScope()) { Assert.That(record.Outcome, …); Assert.That(record.Reason, …); … }`. A helper with optional named parameters preserves each distinct assertion — sites asserting extra facets (`:4224` asserts `Vacuity`, `ProofCore`, `Model`; `:4257` asserts `RunStatus`/`FailureReason`) pass those explicitly rather than dropping them. Sites asserting something not expressible as a facet (e.g. `:4089`, a specific `Model.Single(...).Value`) keep their own line.
- **Proposed change:** Add `AssertClaimVerdict(response, outcome, reason = None, vacuity = null, proofCoreEntry = null, runStatus = null)` next to the existing `GetClaim`/`AssertSemanticallyEquivalent` helpers (`:6211-6300`); collapse the 30 clusters to one call each.

### 2. Authority validate/forge helpers in the `ArtifactAuthority*` block
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs:6839-7032`
- **Est. LOC saved:** ~70
- **Why it's safe:** All four tests repeat two exact blocks: a 6-line baseline `Assert.That(WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest, authority).IsValid, Is.True, FormatValidationErrors(response, authority));` (4 occurrences: 6868, 6915, 6962, 7010) and a 5-line forgery check asserting a specific error code (8 occurrences: `proof_core_authority` ×2, `assumption_usage_authority`, `model_authority`, `effect_witness_authority`, `vacuity_authority`, …). Each distinct expected error code stays an explicit argument.
- **Proposed change:** Add `AssertAuthorityAccepts(response, authority)` and `AssertAuthorityRejects(response, authority, string errorCode)` beside `CreateResponseAuthority` (`:7033`) and `FormatValidationErrors` (`:7042`); the 12 sites become one line each.

### 3. Merge the three `RehashedCache*` tests into one `[TestCase]`-driven test
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs:5256-5344`
- **Est. LOC saved:** ~60
- **Why it's safe:** `RehashedCacheCannotUpgradeARefutationToProven`, `…SealedForDifferentManifestMissesAndRecomputes` and `…WithInvalidScalarModelMissesAndRecomputes` are byte-identical apart from one mutation lambda. All three assert the *same four* facts: `first.Summary.CacheStatus == Written`, `backend.CallCount == 2`, `second.Summary.CacheStatus == Written`, `second.ClaimResults.Single().Outcome == Refuted`. The only variation is which JSON node is rewritten.
- **Proposed change:** One test taking a mutation-kind string (`"outcome"`, `"manifestHash"`, `"model"`) that switches to the right `RewriteCachedClaimAsync`/`RewriteCachedPayloadAsync`; the shared 20-line body runs once.

### 4. Route hand-rolled corruption blocks through the existing `AssertMalformedCapture`, and add its positive twin
- **Files:** `SharpProof.Worker.Test/CompilerManifestArtifactTests.cs:203-434`, helper at `:2819`
- **Est. LOC saved:** ~60
- **Why it's safe:** `AssertMalformedCapture(Action<CompilerCompilationSnapshot>)` already exists and does exactly `CreateArtifact` → corrupt → recompute `CompilationSha256` → `Assert.Throws<JsonException>`. Four tests re-implement it inline: `Sp034MalformedCaptureEvidenceIsRejected` (`:203`, 7 corruption lambdas + a 12-line loop), `Sp034ReferenceRolesRejectModuleOnlyProperties` (`:375`), `Sp034SyntaxTreePathsMustBeCaptureCanonical` (`:407`), `Sp034EmptySyntaxTreesRetainDerivedCaptureValues` (`:420`). Separately the 8-line "recompute hash then `Assert.DoesNotThrow(Deserialize(Serialize(artifact)))`" round-trip appears at `:240-249, :264-278, :313-320, :331-334, :355-363, :442-446` with no helper. Both assert the same throw/no-throw outcome.
- **Proposed change:** Rewrite the four inline blocks as `AssertMalformedCapture(...)` calls; add `AssertWellFormedCapture(Action<CompilerCompilationSnapshot>)` for the six positive round-trips.

### 5. `AssertUnsupportedTargets` helper + de-duplicate two source literals across files
- **Files:** `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:778-836` and `:882-934`; source strings duplicated verbatim at `ClaimManifestBuilderTests.cs:781-803` vs `WorkerTests.cs:1103-1125`, and `ClaimManifestBuilderTests.cs:842-858` vs `WorkerTests.cs:1177-1195`
- **Est. LOC saved:** ~55
- **Why it's safe:** `UnsupportedEffectCallablesCannotCarryConcreteEvidence` (`:778`) and `UnsupportedEffectCallableShapesCannotCarryReplayEvidence` (`:882`) have *identical* 28-line assertion bodies (count, `Does.ContainKey` per name, all `!IsVerifierSupported`, all effect-claim `Outcome == Unknown`, all `Reason == UnsupportedContract`, all `Witness == null && Replay == null`) — only the expected names differ (`Generic/Async/DelegateCall` vs `.cctor/get_Value`). A `params string[] expectedNames` helper preserves all six assertions. The two `Subject` source literals are character-for-character identical across the two files.
- **Proposed change:** Add `AssertUnsupportedEffectTargets(ClaimManifestBuildResult, params string[] names)`; move the two shared `Subject` sources into a `WorkerTestSources` constants class (no existing home — `SharpProof.Testing/` holds only `IrCSharpDifferentialOracle` and `WellSortedIrGenerator`; `WorkerTestMetadataReferences.cs` is the natural neighbour).

### 6. Parameterize the precondition / normal-completion vacuity test groups
- **Files:** `SharpProof.Worker.Test/WorkerTcbEdgeCaseTests.cs:380-411 + 414-446`, `:519-543 + 545-569`, `:610-625 + 627-640 + 667-683`
- **Est. LOC saved:** ~55
- **Why it's safe:** Three groups, 7 tests.
  - `SemanticPreconditionContradictionIsExplicitVacuityEvidence` / `…ShortCircuitsUnsupportedBody`: identical 18-line arrange, differing only `body: CompilerPreparedBody.Trivial()` vs `body: null`. **The second asserts a superset** (`Reason`, `ProofCore`), so merging must apply the superset to both — that *increases* coverage. Verify the trivial-body case also yields `Reason == None` first; if not, keep the extra asserts conditional.
  - `SatisfiablePreconditionProducesOrdinaryProof` / `…DoesNotHideFalsePostcondition`: identical arrange except `Ensures(true)`/`Ensures(false)` and expected `Proven`/`Refuted`.
  - `NonliteralUnreachableNormalCompletionIsExplicitVacuityEvidence` / `NonliteralReachableNormalCompletionIsNotVacuous` / `UserAssumeCannotSupplyNormalCompletionEvidence`: each is three lines of `CreateDivisionTarget(op, postcondition, assumeCompletion?)` plus the same `Outcome == Proven` + `Vacuity == X` pair.
- **Proposed change:** Convert each group to `[TestCase]` on the varying operand plus the expected outcome/vacuity.

### 7. Merge the two reparse-point cache tests
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs:5072-5103` and `:5104-5134`
- **Est. LOC saved:** ~30
- **Why it's safe:** `ReparsePointCacheEntryFailsClosedWithoutTouchingTarget` and `ReparsePointCacheLockFailsClosedWithoutTouchingTarget` are line-for-line identical except which path is replaced by the symlink (the `*.sharp-proof-cache.json` entry vs the `.lock`) and the external filename/content string. Both assert exactly `backend.CallCount == 2`, `CacheStatus == Unavailable`, and that the external target's bytes are untouched, and both share the same `Assert.Ignore` guard.
- **Proposed change:** One `[TestCase("entry")] [TestCase("lock")]` test resolving the path to symlink from the case name.

### 8. Adopt the existing `CacheFiles(project)` helper at the remaining inline sites
- **Files:** `SharpProof.Worker.Test/WorkerTests.cs` — helper at `:6229`; inline `Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json")` at `:4814, 4830, 4867, 4885, 4915, 4922, 4950, 4998, 5028, 5081, 5162, 5215` (24 literal occurrences of the glob vs 12 uses of the helper)
- **Est. LOC saved:** ~25
- **Why it's safe:** Pure textual substitution of an existing helper with identical semantics; no assertion changes. Each inline form spans 3 lines because of wrapping.
- **Proposed change:** Replace the inline `Directory.GetFiles(...)` calls with `CacheFiles(project)`.

---

## Deep pass: EffectAnalysisTests.cs (9,004 lines — largest file in the solution)

**Estimated savings: ~1,977 LOC** (9,004 → ~7,030), no assertion dropped.

> ⚠ **Read this before acting.** ~1,300 of that total (findings 1, 3, 4) is **pure reformatting** — rewrapping to the repo's own 140-column limit and de-padding fixture strings. It reduces the line count without improving the code, produces an enormous diff across 580+ sites, and would conflict with any in-flight work on this file. It is a different *kind* of change from every other finding in this document, which remove real duplication or complexity. Treat findings 2 and 5 as the substantive wins (~570 LOC) and the reflow as an optional, separately-committed formatting pass — ideally enforced by a formatter rather than done by hand.

### 1. Reflow multi-line `Assert.That(...)` calls to the repo's own 140-column limit *(formatting)*
- **Files:** `SharpProof.Effects.Test/EffectAnalysisTests.cs` — 382 call sites (e.g. `:111-113, :167-169, :5183-5185, :6202-6511`)
- **Est. LOC saved:** ~932
- **Why it's safe:** Pure whitespace. Every argument, message string and constraint is byte-identical; no assertion is added, removed, or merged.
- **Proposed change:** `.editorconfig:13` sets `max_line_length = 140`, and 131 non-fixture lines in this file already exceed 80 columns — so the ~80-col hand-wrapping is inconsistent, not a rule. Measured breakdown of `Assert.That(` statements that fit on one line at ≤140 once joined: 2 spans of 2 lines, 235 of 3, 125 of 4, 15 of 5, 5 of 6. This subsumes the 48 wrapped `Assert.That(HasStaticWrite("X"), …)` entries at `:6202-6511` — converting that table to two `string[]` name lists on top of the reflow saves nothing further, so skip it.

### 2. Parameterize the three test families with byte-identical assertion bodies *(substantive)*
- **Files:** `:3167-3269` (`FreshArrayContents…` / `FreshObjectContents…` / `NestedFreshContainerContents…`), `:4517-4562` (`ReducedSourceExtensionRemapsItsReceiverArgument` / `RefParameterWritesRemapToTheCaller`), `:5188-5235` (`SealedReferenceArrayStore…` / `DefinitelyNullReferenceArrayStore…`)
- **Est. LOC saved:** ~60 (~36 + ~13 + ~11)
- **Why it's safe:** Every test body was diffed with fixture text stripped; within each family the code is character-for-character identical — same `Analyze(...)` call shape, same `Assert.EnterMultipleScope()`, same constraints. Only the fixture string differs. Raw string literals are compile-time constants, so `[TestCase("""…""")]` is legal in NUnit and each case keeps its own failure identity.
- **Proposed change:** Merge each family into one `[TestCase]`-driven method taking the fixture source as the parameter. **These are the only three such families in the file** — all 144 `[Test]` methods were checked, so parameterization potential beyond this is nil.

### 3. Reflow multi-line non-assertion call statements *(formatting)*
- **Files:** same file — 198 statements (e.g. `:5100-5108, :6199-6201, :8190-8191`)
- **Est. LOC saved:** ~363
- **Why it's safe:** Whitespace only; disjoint from finding 1 (that count excludes `Assert.That(` heads).
- **Proposed change:** Same 140-col reflow applied to `EffectTestHost.CreateCompilation(...)`, `session.Analyze(...)`, and `Analyze(source, "Sample", "X")` invocation tails.

### 4. Blank lines and 3-line bodies inside embedded C# fixture raw strings *(formatting)*
- **Files:** same file — 153 raw-string fixtures totalling 3,927 lines, of which 508 are blank; plus 36 three-line fixture method bodies (`:84, :97, :132, :145, :149, :185, :198, :202, :206, :315, :950, :1956, :2357, :2460, :2468`, …) and the 14 four-line `lock` methods at `:8117-8185`
- **Est. LOC saved:** ~508 blank (recommend the ~400 that only pad between type declarations) + ~114 body collapse
- **Why it's safe:** These lines are *inside* `"""…"""` literals fed to `EffectTestHost.CreateCompilation`. They are whitespace in a C# source string; no symbol, span, or diagnostic position that any assertion checks depends on them, and no assertion in the file inspects fixture line numbers. The `lock` fixture's assertions (`AssertKinds` at `:8229`) key off method *names*, not layout.
- **Proposed change:** Strip blank padding inside fixtures and write short bodies as one-liners, matching the already-dominant dense style — 49 fixtures open directly with `public static class Sample {` and carry zero blanks (e.g. `:6030-6145` packs ~115 declarations one per line).

> **Negative findings:** **Duplicate fixture strings: zero.** All 153 fixture literals are textually distinct, so no test is strictly subsumed by another (subsumption requires an identical fixture). The largest shared preamble is a 3-line `public sealed class Box { public int Value; }` in 7 fixtures — a shared constant saves ~14 lines at the cost of string concatenation at every call site; not worth it. **No dead helpers or fields:** all seven private helpers are live (`Analyze` 333 refs, `Method` 268, `AssertThrows` 70, `AssertContainsThrows` 16, `AssertDoesNotThrow` 14, `ResultKey` 13, `RequireGetter` 4, `AssertNoEffectsAndTerminates` 4).

---

## Cross-project duplication sweep (whole solution)

**Estimated savings: ~525 LOC** across 7 findings. This is the view no per-project agent had.

### 1. `TRUSTED_PLATFORM_ASSEMBLIES` → `MetadataReference` list builder — 26 sites, 12 projects
- **Files:** `SharpProof.Contracts.Test/ContractTestMetadataReferences.cs:18`, `SharpProof.Worker.Test/WorkerTestMetadataReferences.cs:21`, `SharpProof.Contracts.Test/ContractApiIdentityTests.cs:158`, `SharpProof.Frontend.Test/ContractApiIdentityResolverTests.cs:323`, `ProgramLoweringTests.cs:914`, `OpaqueSemanticIdentityTests.cs:204`, `FrontendLoweringTests.cs:1473`, `UnaryAndDefaultLoweringCoverageTests.cs:372`, `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:254`, `ContractApiIdentityAnalyzerTests.cs:385,451,623`, `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:198`, `SharpProof.Effects.Test/EffectTestHost.cs:222`, `SharpProof.Meta.Analyzers.Test/SharpProofSoundnessAnalyzerTests.cs:3472`, `SharpProof.Package.Test/BuildTaskTests.cs:1793`, `CompilerProbeInputConsistencyTests.cs:74`, `SharpProof.Worker.Test/ExceptionIdentityReplayTests.cs:459`, `ProtocolJsonTests.cs:2056`, `ScalarDifferentialMatrixTests.cs:1018`, `WorkerTests.cs:7289`, `SharpProof.Specs.Test/ApiSpecTests.cs:1201`; non-test: `SharpProof.Gates/AnalyzerGateHost.cs:189`, `Gates/Performance/WorkerPerformanceProbe.cs:736,851`, `SharpProof.Testing/IrCSharpDifferentialOracle.cs:610`, `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1817`
- **Est. LOC saved:** ~150
- **Why it's safe:** ~10 bodies were read side by side. Every one is the same three-step recipe: `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` → throw `InvalidOperationException("Trusted platform assemblies are unavailable.")` if null → `.Split(Path.PathSeparator).Select(MetadataReference.CreateFromFile)`. Per-site variation is only optional modifiers — `Distinct(OrdinalIgnoreCase)`, `OrderBy`, `Where(exclude "SharpProof.Attributes")`, `Append(<extra location>)` — all expressible as parameters. A shared home exists and is already wired: `eng/testing/DiagnosticDescriptorCatalogAssertions.cs` is linked via `<Compile Include="..\eng\testing\…">` into Analyzer.Test, ContractForGenerator.Test and Meta.Analyzers.Test.
- **Proposed change:** Add `eng/testing/TrustedPlatformReferences.cs` exposing `Create(distinct, ordered, extraLocations, filter)`, link it into the test projects, and mirror it as a public helper in `SharpProof.Testing` for the 5 Gates/Testing/Fuzz callers.

### 2. `OperationMayThrow` + `ExceptionalSuccessors` CFG helpers
- **Files:** `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1207` and `:1260`; `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1215` and `:1258`
- **Est. LOC saved:** ~75
- **Why it's safe:** Both pairs read in full. `ExceptionalSuccessors` is line-for-line identical (same `yielded` HashSet, same enclosing-region walk, same `Filter/Catch/FilterAndHandler/Finally` filter, same `graph.Blocks[handler.FirstBlockOrdinal]` yield); Meta's version only adds `cancellationToken.ThrowIfCancellationRequested()` and takes a `CancellationToken` — a strict superset signature. `OperationMayThrow` is the same ~50-case operation-kind pattern list in the same order; Analyzer.Core's copy is a strict superset (it additionally admits `IMethodReferenceOperation` and the `OperatorMethod: not null` variants).
- **⚠ Caveat:** unifying on the superset only widens "may throw" — the conservative direction for both callers — but that **is** a semantic change for Meta.Analyzers and must be confirmed against its tests. Also note Analyzer.Core references Meta.Analyzers with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` (`SharpProof.Analyzer.Core.csproj:32`), so there is **no** compile-time dependency between them. The repo already solves exactly this with linked source (`SharpProof.Dataflow.csproj:12` and `SharpProof.Smt.csproj:13` both link `..\SharpProof.Ir\ArgumentNullGuard.cs`).
- **Proposed change:** Extract both into `RoslynCfgThrowFacts.cs` and link it into both projects, keeping the Analyzer.Core operation list and the cancellation-aware `ExceptionalSuccessors`.

### 3. Lowercase-hex SHA256 formatting — 18 sites, 12 projects
- **Files:** canonical-ish home `SharpProof.Ir/CanonicalHashWriter.cs:147`; `SharpProof.Worker.Protocol/ProtocolJson.cs:1062`, `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:519,532`, `CompilerRelationalSummaryProvider.cs:351`, `CompilerSpecificationPackProvider.cs:331`, `SharpProof.Effects/ApiSpecResolution.cs:227`, `SharpProof.Gates/Program.cs:170`, `Gates/Performance/WorkerPerformanceProbe.cs:681`, `Gates/Corpus/OpenSourceCorpusCatalog.cs:66,368`, `OpenSourceCorpusImporter.cs:136`, `SharpProof.CompilerProbe.TestAsset/ProbeHash.cs:23`, `SharpProof.ArchitectureTest/ArchitectureTests.cs:752`, `SharpProof.Analyzer.Test/FinalCompilationCollectorTests.cs:1300`, `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:509,3497`, `SharpProof.Specs.Test/ApiSpecTests.cs:1183`, `SharpProof.Worker.Test/WorkerTests.cs:7214`
- **Est. LOC saved:** ~55
- **Why it's safe:** Every distinct spelling was read. They are three interchangeable encodings of one function: `string.Concat(bytes.Select(v => v.ToString("x2", InvariantCulture)))`, a `StringBuilder` loop appending `"x2"` (ProbeHash), and `Convert.ToHexString(...).ToLowerInvariant()` (Gates/Corpus). All produce identical lowercase hex, and the hashes are compared `Ordinal`/`OrdinalIgnoreCase` against each other across the collector/worker boundary, so they must already agree. `SharpProof.Ir` is referenced by Summaries, Smt, Dataflow, CompilerArtifact, Worker, Analyzer.Core and Effects.
- **Proposed change:** Add `ToLowerHex(ReadOnlySpan<byte>)` and `ComputeSha256Hex(...)` next to `CanonicalHashWriter` in `SharpProof.Ir`; leave the Gates/test-only sites that cannot reference Ir on `Convert.ToHexString(...).ToLowerInvariant()`.

### 4. Counterexample-model materialization: `TryCreateValue` + `EntryAssumptionsHold`
- **Files:** `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:909` and `:831`; `SharpProof.Worker/VerificationCache.cs:583` and `:617`
- **Est. LOC saved:** ~55
- **Why it's safe:** Both pairs read end to end. `TryCreateValue` is character-identical apart from line wrapping of the `interval.Minimum`/`Maximum` conjunct — same `factory.GetVariableInfo(...).Type` lookup, same `nameof(IrValueKind.Boolean)`/`Integer` kind strings, same `NumberStyles.AllowLeadingSign` round-trip, same `SourceIntegerInterval` range guard. `EntryAssumptionsHold` differs only in taking `IReadOnlyDictionary` vs `ImmutableDictionary`; unifying on `IReadOnlyDictionary` is source-compatible for both. The neighbouring `TryCreateModel` was **deliberately excluded** — the two copies genuinely differ (the Worker one tolerates absent non-scalar inputs by design, per its inline comment). Shared home already exists: `SharpProof.Worker.csproj:20` references CompilerArtifact, which already grants `InternalsVisibleTo("SharpProof.Worker")` (`:31`).
- **Proposed change:** Move both into `internal static class CompilerModelValues` in `SharpProof.CompilerArtifact`; call from `VerificationCache`. No project-file change needed.

### 5. Acyclic block-ordering walker `CreateOrder`
- **Files:** `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:731`, `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs:531`
- **Est. LOC saved:** ~50
- **Why it's safe:** Both bodies read fully. Same explicit-stack DFS with an `active`/`complete` HashSet pair and a `Stack<(IrBlockId, bool Exit)>`, same `Spend()` budget check, same terminator switch (`IrBranchInstruction` pushing `WhenFalse` then `WhenTrue`, `IrGotoInstruction`, `IrReturnInstruction`, default → bail), same trailing `result.Reverse(); return [.. result];`. The only difference is failure reporting: Summaries records `IrSummaryAbstentionReason.CyclicControlFlow`/`UnsupportedInstruction` before returning `default`; Worker just returns `default`. Both projects reference `SharpProof.Ir`, where `IrBlockId`/`IrBranchInstruction` live.
- **Proposed change:** Add `IrBlockOrder.TryCreateAcyclicOrder(program, spend, out IrAcyclicOrderFailure)` to `SharpProof.Ir` returning the reverse-postorder array plus a small failure enum; each caller maps the enum to its own reason type.

### 6. Test-side Roslyn fixture boilerplate
- **Files:** `AssertNoErrors`/`RequireNoErrors` (5 copies): `SharpProof.Contracts.Test/ContractApiIdentityTests.cs:174`, `ContractForMetadataSignatureTests.cs:206`, `SharpProof.Frontend.Test/ContractApiIdentityResolverTests.cs:339`, `SharpProof.Effects.Test/EffectTestHost.cs:231`, `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:214`. Surrogate attributes source (3 copies): `EffectTestHost.cs:101-165`, `ContractApiIdentityResolverTests.cs:196-300`, `ContractApiIdentityTests.cs:100-155`
- **Est. LOC saved:** ~100
- **Why it's safe:** The five `*NoErrors` bodies were diffed: all are `GetDiagnostics().Where(d => d.Severity == Error).ToImmutableArray()` plus a `string.Join(Environment.NewLine, …)` message; three use `Assert.That(errors, Is.Empty, msg)` and two throw `InvalidOperationException(msg)` — trivially unified. For the surrogate source, an 8-line-window match found 21-line and 12-line verbatim runs between `EffectTestHost.cs` and `ContractApiIdentityResolverTests.cs`, confirmed by reading: the same raw string declaring `SharpProof.Attributes.Contract` with `ConditionalSymbol`, the `{{conditional}}` hole, `Requires`/`Ensures`/`Assume`/`Result<T>`/`Old<T>`, `SharpProofEffect`, `NotNullAttribute`, `EffectContractAttribute`, `SharpProofTrustedAttribute`.
- **⚠ Lower confidence than 1-5:** the three copies are **not** identical (Frontend's adds `PositiveAttribute`/`InRangeAttribute`; Contracts' stops after `Contract`), so this is a superset-plus-flags consolidation, and the emitted assembly's contents must be verified against each test's expectations.
- **Proposed change:** Put `AssertNoErrors` and a `SurrogateAttributesAssembly.Emit(validContractShape, includeRangeAttributes)` builder in `eng/testing/`; link into the four test projects.

### 7. `*TestMetadataReferences` — a duplicated whole file
- **Files:** `SharpProof.Contracts.Test/ContractTestMetadataReferences.cs:1-40`, `SharpProof.Worker.Test/WorkerTestMetadataReferences.cs:1-43`
- **Est. LOC saved:** ~38
- **Why it's safe:** `diff` of the two whole files reports exactly three hunks: the `namespace` line, the class name, and three extra lines in the Worker copy adding a `CoreLibraryOnly` property. `CreatePlatformReferences`, `AddSharpProofReference` and the `WithSharpProof` property are byte-identical. Both projects already link shared sources from `eng/testing/`.
- **Proposed change:** Move to `eng/testing/TestMetadataReferences.cs` under a shared namespace (folding in `CoreLibraryOnly`), link into both, delete the copies. This also subsumes two sites from finding 1.

---

## Code generators and their emitted output

**Estimated savings: ~673 LOC** (~493 emitted C#, ~180 PowerShell), from three localized template/helper edits.

> **Correction to an earlier caveat:** `eng/acceptance/contract.json` pins script **paths only — it contains no sha256/digest fields** (`grep -c sha256` → 0). Editing existing script *contents* does not require touching it; only *adding a new script file* does (path lists at `:130-147`).

### 1. Collapse emitted 4-line `get;`-only property blocks to one line
- **Files:** `scripts/Generate-IrModel.ps1:586-592`, `scripts/Generate-DeclarativeModels.ps1:174-179`
- **Est. LOC saved:** ~423 emitted lines
- **Why it's safe:** Both sites emit exactly 5 lines per property (blank, signature, `{`, `get;`, `}`); the one-line `public T P { get; }` is semantically identical and **already the house style elsewhere in the generated tree** (`Generate-ApiSpecCatalog.ps1:903-910`, `Generate-ContractApiCatalog.ps1:320-334`, `Generate-ProtocolModel.ps1:647`). Exact 3-line `{ / get; / }` runs counted per file: IrModel 94, Verify/DeclarativeModels 12, Contracts/DeclarativeModels 12, Frontend/DeclarativeModels 8, Contracts/EffectiveContractModels 5, Effects/EffectResultModels 4, Specs/DeclarativeModels 3, Effects/ApiSpecResolutionModels 3 = **141 properties × 3 lines = 423**. `.editorconfig` allows 140 chars; the longest resulting line is well under. `Format-SharpProofGeneratedCSharp` only re-indents open braces, so it will not re-expand them.
- **Proposed change:** Replace the 5-`Add` block in each generator with a single `$lines.Add("$indent    $access $type $name { get; }")`. Two ~4-line edits. (Dropping the preceding blank line too would save a further ~141.)

### 2. Deduplicate copy-pasted schema/emit helpers into the existing shared file
- **Files:** `Assert-Identifier`/`Identifier` in 11 scripts (`Generate-AnalyzerDiagnosticCatalog.ps1:26`, `Generate-BoundContractModel.ps1:31`, `Generate-CompilerArtifactModel.ps1:94`, `Generate-ContractApiCatalog.ps1:97`, `Generate-DiagnosticDescriptors.ps1:47`, `Generate-EffectContractMappings.ps1:23`, `Generate-IrModel.ps1:94`, `Generate-OperationSupportCatalog.ps1:26`, `Generate-ProtocolModel.ps1:52`, `Generate-DeclarativeModels.ps1:26`, `Generate-ProjectionCatalog.ps1:27`); `Assert-TypeName`/`TypeName` in 6; `ConvertTo-CSharpString`/`ConvertTo-CSharpStringLiteral`/`Quote-CSharpString` in 8; `Get-RequiredMember`/`Get-RequiredProperty`/`Required` in 8; `Assert-Properties` in 4; `NamespaceName` in 2
- **Est. LOC saved:** ~180 net (~265 deleted, ~85 for the shared file)
- **Why it's safe:** Bodies are character-identical apart from parameter-declaration style and `$Value`/`$value` casing — e.g. `Generate-EffectContractMappings.ps1:23-37` and `Generate-CompilerArtifactModel.ps1:94-113` have byte-identical regexes, throw strings and logic. The infrastructure already exists: every generator dot-sources `scripts/GeneratedFileHelpers.ps1` for `Update-SharpProofGeneratedFile`, so extending that file needs no new wiring.
- **Proposed change:** Move one canonical copy of each helper into `scripts/GeneratedFileHelpers.ps1`; delete the 30+ duplicates. **Two regexes are not identical and must be parameterized or kept local:** `TypeName` in `Generate-ProjectionCatalog.ps1:41` permits `(` `)` (tuple types) where `Generate-DeclarativeModels.ps1:33` does not.

### 3. Emit single-line constructor signatures when they fit
- **Files:** `scripts/Generate-IrModel.ps1` (ctor param emission), `scripts/Generate-DeclarativeModels.ps1:151-157`
- **Est. LOC saved:** ~70 emitted lines
- **Why it's safe:** Both wrap every constructor parameter onto its own line unconditionally, regardless of length. Measured against a 120-char budget: IrModel 9 ctors / 47 lines, Verify/DeclarativeModels 6 / 16, Effects/ApiSpecResolutionModels 1 / 4, Contracts/DeclarativeModels 1 / 3 = **70 lines**. Example: `internal IrExceptionInfo(IrExceptionKind kind, string detail)` is 62 chars but occupies 4 lines (`IrModel.generated.cs:152-156`).
- **Proposed change:** Join parameters onto the signature line when the result is ≤120 chars; fall back to the existing wrap otherwise.

### 4. ⛔ REJECTED: positional records for generated types
- **Files:** `SharpProof.Ir/IrModel.generated.cs:150,169,201`, `SharpProof.Effects/ApiSpecResolutionModels.generated.cs:11`
- **Why it is NOT safe:** All 34 constructors in `IrModel.generated.cs` are declared `internal` on types declared `public sealed [partial] class` (0 public ctors, verified by grep). A primary constructor or positional record takes the *type's* accessibility, so converting would promote 34 internal constructors to public and add public `Deconstruct`/`with`/value-equality members — **a breaking API change**. `Generate-DeclarativeModels.ps1` already emits genuine positional records where the catalog asks for them (`ApiSpecResolutionModels.generated.cs:37,44,49`), so the class-with-internal-ctor shape is a deliberate distinction, not an oversight.
- **Note:** this independently confirms the *PUBLIC API NOTE* raised against the same idea in the Contracts area. Findings 1 and 3 above capture the line savings without touching the API surface.

> **Negative results:** all 15 `scripts/Generate-*.ps1` are wired into `eng/acceptance/contract.json`/CI — no orphaned generators. No dead generator functions — every `function` in the 15 generators plus `GeneratedFileHelpers.ps1` has a call site. Write-if-changed is already shared (`Update-SharpProofGeneratedFile`). Generated output is **not** broadly verbose — `ProtocolModel.generated.cs` (1,001 lines), `CompilerArtifactModel.generated.cs`, `IrOperatorCatalog.generated.cs` already emit one-line auto-properties; the verbosity is confined to the two emitters above. No unconsumed `*.generated.cs` (the one flag, `CompilerEffectConstraintRule`, is consumed via `CompilerEffectEvidenceCatalog.ConstraintRules` at `CompilerEffectClaimArtifactCodec.cs:130`).

---

## Release / packaging / dependency scripts (PowerShell)

**Estimated savings: ~200 LOC.** No dead functions found — every declared function in the ten files has at least one caller in `scripts/`, `eng/`, `.github/`, or `docs/`.

### 1. `Get-PackageIdentity` copy-pasted three times (nuspec reader)
- **Files:** `scripts/Publish-SharpProofRelease.ps1:149-212`, `scripts/New-SharpProofReleaseEvidence.ps1:26-91`, `scripts/Test-SharpProofPackageConsumers.ps1:29-79`
- **Est. LOC saved:** ~115
- **Why it's safe:** The three bodies are byte-identical from `[IO.Compression.ZipFile]::OpenRead` through the nuspec/namespace/metadata extraction. They diverge only in (a) two error-message wordings, (b) whether repository metadata is required, and (c) the returned property set (`id/version/repositoryCommit` lowercased vs `Id/Version/RepositoryUrl/RepositoryCommit` vs `Id/Version/Path`). `Publish-SharpProofRelease.ps1:40-48` already dot-sources five sibling files, so a shared module is trivially reachable. Two more near-copies of the same "open archive, find exactly one .nuspec" prologue exist at `Test-SharpProofPackageDependencies.ps1:181-190` and `SharpProof.PublicationDestination.ps1:79-90`, which the same helper can serve.
- **Proposed change:** Add `Get-SharpProofNuspecMetadata` (returning the parsed `metadata` XML node) plus `Get-SharpProofPackageIdentity -RequireRepository` to a shared `scripts/SharpProof.PackageIdentity.psm1`; each caller projects the property names it needs. **Note:** `eng/acceptance/contract.json` lists `.psm1` paths (lines 168, 708), so a *new* module needs an entry there.

### 2. Git-subprocess byte-capture boilerplate duplicated inside one file
- **Files:** `scripts/Get-SharpProofReleaseDigests.ps1:64-110` and `:193-250`
- **Est. LOC saved:** ~35
- **Why it's safe:** `Get-GitTreeEntries` and `Get-GitBlobBytes` both build a `ProcessStartInfo` with the same five property assignments, push `-C $resolvedRepository` plus args, start the process, `ReadToEndAsync` stderr, `CopyToAsync` stdout to a stream, `WaitForExit`, check `ExitCode`, and throw with the same message shape. Only the arg list, the destination stream (MemoryStream vs temp file), and the operation noun differ. Both are file-local — no external callers, verified by grep across `scripts/`, `eng/`, `.github/`.
- **Proposed change:** Extract one private `Invoke-GitBinary -Arguments -Operation` returning `byte[]`; drop the temp-file path in `Get-GitBlobBytes` since the MemoryStream form already works.

### 3. Hardcoded first-party package-ID triple repeated in five places
- **Files:** `scripts/Publish-SharpProofRelease.ps1:50-54`, `scripts/New-SharpProofReleaseEvidence.ps1:588-592`, `scripts/Test-SharpProofPackageConsumers.ps1:111-115`, `scripts/SharpProof.PublicationPlanIdentity.psm1:264`, `scripts/Test-SharpProofSamples.ps1:184-188`
- **Est. LOC saved:** ~20
- **Why it's safe:** All five are the same set — `SharpProof`, `SharpProof.Attributes`, `SharpProof.Verifier` — varying only in ordering (`Publish` keeps a deliberate `$packageOrder` for push sequencing; the others `| Sort-Object` immediately).
- **Proposed change:** Expose `$SharpProofPackageIds` (sorted) and `$SharpProofPackagePushOrder` from the shared module; replace the five literals. **Do not** try to derive the IDs from `scripts/package-projects.json` — `SharpProof.Package/SharpProof.Package.csproj` produces package id `SharpProof`, so the mapping is not path-derivable; hardcoding the IDs once is the honest fix.

### 4. Dead locals and an unreachable `finally` in `Invoke-ConsumerDotNet`
- **Files:** `scripts/Test-SharpProofPackageConsumers.ps1:256-276`
- **Est. LOC saved:** ~10
- **Why it's safe:** `$captureOutput` is assigned at line 258 and never read anywhere in the file. `$capturePath` is set to `$null` at line 256 and never reassigned, so the `finally` guard `if ($null -ne $capturePath …) { Remove-Item … }` at 273-275 **can never fire**. Verified: those two names appear only at 256, 258, 273, 274.
- **Proposed change:** Delete the `$captureOutput` assignment, the `$capturePath` initialization, and the dead cleanup branch, leaving `Push-Location`/`Pop-Location` in the `finally`.

### 5. "Compare shapes then compare canonical JSON, else throw" idiom repeated
- **Files:** `scripts/SharpProof.PublicationDestination.ps1:239-245` and `:329-335`; `scripts/Test-SharpProofPackageDependencies.ps1:555-558, :681-685, :743-746`
- **Est. LOC saved:** ~20
- **Why it's safe:** Every site is the same three-part predicate — property-name sequence equality via a `-join`, then `(… | ConvertTo-Json -Compress [-Depth n]) -cne (… | ConvertTo-Json -Compress [-Depth n])`, then `throw '<message>'`. Only the depth argument and the message string vary. `scripts/SharpProof.ReleaseJson.ps1:21` already hosts a partial version of this shape.
- **Proposed change:** Add `Assert-SharpProofCanonicalMatch -Actual -Expected -Depth -Message` to `SharpProof.ReleaseJson.ps1`; collapse the five sites to one line each.

> **Reinforcing evidence (not double-counted).** The atomic-write idiom flagged in the mutation-scripts area recurs here: `New-SharpProofReleaseEvidence.ps1:93-119` (`Write-AtomicText`), an inline copy at `Test-SharpProofDependencyAudit.ps1:572-588`, and `Write-SharpProofPublicationPlanAtomic` in `SharpProof.PublicationPlanTopology.ps1:130` (called from `Publish-SharpProofRelease.ps1:808`). Two further sites write JSON non-atomically with the same "resolve path, create parent dir, WriteAllText UTF8-no-BOM + `\n`" tail (`Get-SharpProofReleaseDigests.ps1:436-457`, `Get-SharpProofProductionInventory.ps1:459-465`). Folding all five into the shared atomic writer adds roughly **35 more lines** on top of that area's count.
>
> **Noted, not filed:** `.sln` project-list parsing is hand-rolled twice with different regexes (`Test-SharpProofDependencyAudit.ps1:159-190`, `Get-SharpProofProductionInventory.ps1:148-154`) — real duplication, but the two return different shapes and only ~15 lines are recoverable.

---

## Fuzz tools / BuildTasks / peripheral projects

**Estimated savings: ~375 LOC.**

> **Checked and found live — do not remove.** `SharpProof.Smoke.Net472` is in `SharpProof.sln:17` and asserted by `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:435` (only 14 source lines). `SharpProof.Verifier` is the real MSBuild-integration package (`buildTransitive/SharpProof.Verifier.targets` + `.nuspec`) with no RID-specific twin in the tree. All four BuildTasks and every `internal` test seam in `RunVerifier.cs` (`ComputeProcessTimeout`, `WaitForOutputCompletion`, `RetainedCleanupAnchorCount`, the four `*Override` hooks) have live references.

### 1. Replace two verbatim copies of `CollectVariables` with the existing `IrTermAnalysis.CollectVariables`
- **Files:** `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:414-470`, `Tools/SharpProof.Fuzz/PartialTermSmtFuzzing.cs:379-435`
- **Est. LOC saved:** ~110
- **Why it's safe:** `diff` of the two 57-line regions is empty — identical private helpers. `SharpProof.Ir/IrSemanticTerms.cs:124` already exposes `public static ImmutableHashSet<IrVarId> IrTermAnalysis.CollectVariables(IrTerm)`, delegating to the non-recursive `IrTraversal.CollectVariables` (`IrTraversal.cs:23`), which walks the same node set via `GetChildren`. Both fuzz projects already reference `SharpProof.Ir`. The only behavioral difference is ordering: the fuzz copies return an `ImmutableArray` sorted by `IrVarId.Value`.
- **Proposed change:** Delete both helpers; call `[.. IrTermAnalysis.CollectVariables(root).OrderBy(static v => v.Value)]` at the two sites, preserving deterministic ordering. **Bonus:** the Ir version is iterative, so this also removes the deep-term stack-overflow risk of the recursive copies. *(This is a fourth instance of the `IrTraversal` re-implementation pattern flagged elsewhere in this document.)*

### 2. Collapse 8 identical leaf factories + verbose auto-property block in `GeneratedCSharpExpression`
- **Files:** `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:78` (property block), `:127-222` (leaf factories)
- **Est. LOC saved:** ~85
- **Why it's safe:** `Left/Right/Condition/Text/Values/Reference/NullReference/NullString` are byte-for-byte identical bodies differing only in the `(Kind, Type)` pair — each a 10-line `return new(kind, type, 0, false, []);`. The 8 public properties are 4-line `{ get; }` blocks (28 lines for 8 get-only autoprops). No `partial`, no reflection over these members — `GeneratedCSharpExpression` is used only by `Tools/SharpProof.Fuzz` and `SharpProof.Fuzz.Test`, all by direct call.
- **Proposed change:** Add one `private static GeneratedCSharpExpression Leaf(GeneratedExpressionKind, GeneratedExpressionType)`; make the 8 factories one-line expression bodies over it; collapse the autoprops to single-line form.

### 3. Table-drive `FrontendFuzzCoverage` counting and validation
- **Files:** `Tools/SharpProof.Fuzz/FuzzRunner.cs:23-75` (record), `:428-520` (`CreateFrontendCoverage`), `:524-528` (`HasRequiredFrontendCoverage`)
- **Est. LOC saved:** ~70
- **Why it's safe:** The same 13 members are enumerated three times: `HasValidCounts` (all `>= 0`), `HasExpandedCategories` (all `> 0`), and the 13-argument positional construction. `CreateFrontendCoverage` declares 13 local counters and two switch ladders mapping one enum case to one counter each. `HasRequiredFrontendCoverage` is a 5-line wrapper with one caller (`:184`) that just returns `coverage.HasExpandedCategories`. `FrontendFuzzCoverage` is constructed only here and consumed by `FuzzSummary.Passed` and `SharpProof.Fuzz.Test/FuzzRunnerTests.cs` via named properties — the record shape must stay, its bodies need not.
- **Proposed change:** Expose a private `IEnumerable<int> Counts => [TextParameters, …]` and define the two predicates over it; tally into `Dictionary<GeneratedExpressionKind,int>` / `Dictionary<IrExceptionKind,int>` and read out at construction; inline `HasRequiredFrontendCoverage`.

### 4. Extract the duplicated cancelable-MSBuild-task scaffold
- **Files:** `SharpProof.BuildTasks/InvalidatePublishedResult.cs:10-12,48-72,247-262`, `ResetPublishedVerification.cs:7-9,24-48,82-98`, `ValidatePublishedVerificationResult.cs:29-38`
- **Est. LOC saved:** ~65
- **Why it's safe:** The first two contain a character-for-character identical `_synchronization`/`_cancelExecution`/`_canceled` field trio, `Execute()` override (create CTS, latch under lock, call `Execute(token)`, clear under lock in `finally`), `Cancel()` body, and `private static IEnumerable<string> Present(params string?[])`. All three repeat the same `projectDirectory = Path.GetFullPath(...)` + `string ResolvePath(string)` local function over `LinuxPathIdentity.RequireLocalPath`. All four tasks are live — `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:15-21` declares the `UsingTask`s and lines 114/216/229/272 invoke them — so this is refactor-only.
- **Proposed change:** Add an internal `abstract class CancelableBuildTask : Task, ICancelableTask` holding the latch, `Execute()`, `Cancel()`, `Present`, and a `ResolveProjectRelativePath` helper; the three tasks implement only `ExecuteCore(CancellationToken)`.

### 5. De-duplicate `ReturnType` and the Roslyn compile/emit plumbing in the frontend oracle
- **Files:** `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:686-697` and `:1680-1690` (`ReturnType`); `:989-1010` vs `:1155-1177` (parse/compile/emit); `:676-684` vs `:1605-1624` (source emission)
- **Est. LOC saved:** ~45
- **Why it's safe:** `ReturnType` appears twice with identical bodies (`GeneratedCSharpCase` and `FrontendDifferentialOracle`). `CompareBatch` and `CompareSemanticEdges` each repeat the same ~22-line `CSharpSyntaxTree.ParseText(LanguageVersion.CSharp12)` + `CSharpCompilation.Create(…, OutputKind.DynamicallyLinkedLibrary, Release, checkOverflow: true, NullableContextOptions.Enable)` + `MemoryStream`/`Emit` sequence, differing only in assembly name and failure-isolation callback. `GeneratedCSharpCase.Source` re-implements the single-case form of `CreateBatchSource`; `Source` is public API used by `FuzzRunner.cs:285-286` and `FuzzRunnerTests.cs:308,316,591`, so keep the property but delegate.
- **Proposed change:** Hoist one `internal static string ReturnType(GeneratedExpressionType)`; add a private `CompileGenerated(source, assemblyName, cancellationToken)` returning the tree/compilation/emit triple, called from both comparison paths.

### 6. Two stale top-level directories containing only build output
- **Files:** `SharpProof.PortableAnalyzer/`, `SharpProof.Verifier.Win-x64/`
- **Est. LOC saved:** **0 repo lines** — see below
- **Why it's safe:** Both directories contain nothing but `bin/` and `obj/` — no `.csproj`, no `.cs`. Neither name appears in `SharpProof.sln`, in any `.slnf` filter, or in any `.csproj`/`.props`/`.targets`. `docs/soundness-notes/2026-08-08-….md:11` records that the PortableAnalyzer packaging project was removed. Surviving references are deliberate: a stale-path assertion in `scripts/Test-SharpProofPackageConsumers.ps1:336` and "this property was removed" guards in `SharpProof.AnalyzerConsumer.props:116` and `SharpProof.Package/buildTransitive/SharpProof.targets:74`. `SharpProof.Verifier.Win-x64` appears only in `scripts/Generate-Readme.ps1:435` and a doc fixture named `stale-win-x64` — a name to *detect*, not a project to build.
- **⚠ Verified correction:** `git ls-files` returns **nothing** for either directory, and `.gitignore:28` (`[Bb]in/`) already covers them. They are untracked local build residue, so deleting them **removes zero lines from the repository** — it is working-tree tidiness only, not a reduction. Leave the guard/stale-detection references intact.

---

## Documentation and root markdown

**Estimated savings: ~1,025 lines** (~800 audit ledger, ~157 orphan notes, ~44 duplicated gaps prose, ~24 duplicated dev instructions).

> **⚠ Correction to a premise in this survey's own brief:** `README.md` is **not generated**. `scripts/Generate-Readme.ps1` contains **zero** `Set-Content`, `Out-File`, or `WriteAllText` calls — it is a `-Verify` consistency checker over hand-written docs, confirmed in-repo at `docs/README.md:179-180` ("The script does not generate these files."). There is no generated region and no template to change; all `README.md` reductions are ordinary hand-edits. The only genuinely generated doc is `docs/api-spec-catalog.generated.md:2`, produced by `scripts/Generate-ApiSpecCatalog.ps1`.

### 1. Collapse the 848-row per-file ledger in the code-usefulness audit
- **Files:** `docs/code-usefulness-audit.md:187-1034` (`## Per-file baseline coverage ledger`)
- **Est. LOC saved:** ~800
- **Why it's safe:** 848 rows; the verdict column is `retained` on **820**, `removed` on 7, `simplified` on 4. The rationale column has only **7 distinct** boilerplate strings covering 807 rows (`Retained as required analysis, verification, protocol, validation, or fail-closed soundness logic.` ×252; `Retained as independent behavioral, packaging, or integration evidence…` ×219; `Retained after MSBuild import, workflow, package, release, or dynamic invocation review.` ×180; `Retained with its owning catalog or generator and parity checks.` ×90; plus 3 smaller). All 11 non-retained rows are already narrated in prose with commit hashes at `:91-108`, and the rejected leads at `:109-126`. The remaining per-row data (blob SHA-1, line count) is recoverable from git at the recorded baseline commit `18083cd7783146f7b5d7a4db26b31b1f41f3561b` (verified: `git cat-file -t` → `commit`). **The ledger is already decaying** — 7 listed paths no longer exist: `scripts/Get-SharpProofCoverageAuthority.ps1`, `scripts/GitHubEvidenceArtifact.ps1`, `scripts/Invoke-SharpProofDogfood.ps1`, `SharpProof.Testing/AnalyzerTestHost.cs`, `SharpProof.Testing/ReadmeExampleAttribute.cs`, `SharpProof.Analyzer/GlobalUsings.cs`, `SharpProof.Analyzer/Resources/.gitkeep`.
- **Proposed change:** Replace the row-per-file table with a per-category count table (retained-by-rationale × 7) plus the ~28 non-plain-`retained` rows, stating that the full manifest is reproducible from the pinned baseline commit. Keep `## Scope and stop condition` through `## Validation evidence` untouched.

### 2. Delete or index the three orphaned soundness notes
- **Files:** `docs/soundness-notes/2026-07-29-production-hardening-refactor.md` (56), `2026-07-29-readable-format-coverage-baseline.md` (50), `2026-08-08-coverage-authority-and-tcb-ownership.md` (51)
- **Est. LOC saved:** ~157
- **Why it's safe:** `docs/README.md:97-110` is the index for dated evidence and lists only 6 of the 10 files in `docs/soundness-notes/`. `scripts/Generate-Readme.ps1:56-63` (`$datedEvidenceDocuments`) lists 7 — and these three are in **neither**. A repo-wide grep for each filename returns exactly one hit each: `docs/code-usefulness-audit.md`, i.e. the historical ledger proposed for collapse above. No `.ps1`, `.yml`, or maintained `.md` references them. (`2026-07-29-semantic-precondition-vacuity.md` is *not* orphaned — it is in `Generate-Readme.ps1:61`.)
- **Proposed change:** Either add all three to the `docs/README.md` dated-evidence table and `$datedEvidenceDocuments` so they become link-checked, or delete them as superseded. Deleting is the LOC win; keeping costs nothing but requires indexing.

### 3. Remove "Known production gaps" from the documentation map
- **Files:** `docs/README.md:117-162`
- **Est. LOC saved:** ~44
- **Why it's safe:** This 46-line prose blob restates `docs/coverage-and-limits.md:244-330` and **ends by naming that section as the authority** (`docs/README.md:162` links to `coverage-and-limits.md#closed-compiler-artifact-and-remaining-limits`). Overlapping facts appear in both: schema-18 artifact (`README.md:120` / `coverage-and-limits.md:246-247`), protocol 11 / cache schema 13 / relational-summary schema 2 / pack schema 1 (`:143-144` / `:256-258`, `smt-lifecycle.md:63`), SPDX 2.3 SBOM (`:146` / `:307`), "duplicate skipping is never used" (`:154` / `:319` / `native-smt-packaging.md:66` / root `README.md:235`). `docs/README.md` self-describes as a "documentation map" (`:1`) whose documents "are not interchangeable sources of truth" (`:3-4`) — status prose is off-role for it.
- **Proposed change:** Replace with a two-line pointer to the authoritative section. The anchor is checked by `scripts/Generate-Readme.ps1 -Verify`, so the link stays validated.

### 4. Fold `docs/getting-started.md` "Develop the repository" into the container guide
- **Files:** `docs/getting-started.md:136-159`
- **Est. LOC saved:** ~24
- **Why it's safe:** A third copy of the same commands. `:141-143` (`docker compose up -d dev` / `exec dev sharpproof-dev-init` / `exec dev bash`) is identical to `README.md:185-187` and `docs/container-development.md:204-206`. `:151-153` (`tooling build` / `test` / `acceptance -Configuration Release`) overlaps `README.md:170-172` and `CONTRIBUTING.md:26-27`. The section already ends (`:156-159`) by deferring to `container-development.md`, which owns workspace isolation, test targets, and resource overrides. `getting-started.md` is otherwise the *package consumer* on-ramp.
- **Proposed change:** Replace the body with one sentence linking to `container-development.md`; keep the consumer-facing sections intact.

> **Checked, no finding:** **No stale compose-service references in docs** — every `docker compose` invocation across `docs/` and root markdown names only `dev`, `loop`, or `tooling` (`docs/getting-started.md:126,127,141-143,151-153`, `docs/container-development.md:88,97-99,204-206`, `README.md:170-172,179,185-187,198`, `AGENTS.md:4`, `CONTRIBUTING.md:26-27`, `docs/code-usefulness-audit.md:56-58,180-181`). The 17 unreferenced compose services are a `compose.yaml` problem, not a docs problem. **No stale `sp <cmd>` references** — all 13 distinct `sp` commands in docs are present in the `[ValidateSet(...)]` at `scripts/Invoke-SharpProofContainer.ps1:4`, which `eng/container/dev-command.sh:11-13` dispatches to. **No orphaned top-level `docs/*.md`** — every file is referenced from at least 3 places.

---

## Container / acceptance / orchestration tooling

**Estimated savings: ~245 LOC.**

### 1. Table-drive the acceptance contract assertion ladder in `Verify.ps1`
- **Files:** `eng/acceptance/Verify.ps1:449-605` (also `:309-324` `Assert-Equal`, `:326-370` `Get-MsBuildProperty`/`Get-MsBuildDefault`)
- **Est. LOC saved:** ~80
- **Why it's safe:** A flat run of ~46 `Assert-Equal` calls with no control flow and no ordering dependency — each is `(expected literal or contract scalar)` vs `(contract field or MSBuild property)`. Three shapes only: (a) `Assert-Equal $contract.<path> <literal> '<name>'` (~26 calls, `:449-513`), (b) `Assert-Equal (Get-MsBuildProperty $doc '<prop>' '<owner>') <expected> '<prop>'` (~15 calls, `:514-605`), (c) `Get-MsBuildDefault` (4 calls, `:478-493`). Every architecture test that reads `Verify.ps1` was grepped (`ArchitectureTests.cs:1182,1345,1756,2344`, `AcceptanceScriptTests.cs`, `DocumentationSupportContractTests.cs:68`) — the only substring pins are `Test-AcceptanceTimingTimeline`, `Start-AcceptanceTimingPhase -Name 'restore'`, and the `Generate-Readme.ps1 -Verify` invocation; none touch this region. The trusted-mutation catalog's two `Verify.ps1` entries (`Test-SharpProofTrustedMutations.ps1:1971,1995`) pin those same two lines, also outside this region.
- **Proposed change:** Replace the three assertion runs with three `foreach` loops over ordered hashtables (`@{ Name; Actual; Expected }`, and `@{ Document; Property; Owner; Expected }` for the MSBuild ones), keeping `Assert-Equal`/`Get-MsBuildProperty`/`Get-MsBuildDefault` unchanged so failure messages stay byte-identical.

### 2. Collapse the four near-identical CPU-budget functions
- **Files:** `scripts/SharpProof.ContainerExecution.psm1:22-149`
- **Est. LOC saved:** ~70
- **Why it's safe:** `Get-SharpProofTestProjectParallelism`, `…SemanticTestParallelism`, `…PackageTestParallelism` and `…BuildParallelism` are 128 lines differing only in (i) whether the contract knob is a divisor (`automation.testProjectCpuDivisor`), a percent (`automation.packageTestCpuPercent`, `automation.buildCpuPercent`), or nothing (semantic = all visible CPUs), and (ii) the error text. Each repeats the same `SHARPPROOF_TEST_PROJECT_PARALLELISM` override read, the same `ProcessorCount -lt 1` throw, and the same `contract.json` load. The guarding tests are behavioral, not textual: `BuildSchedulingTests.cs:377,437,502` import the module and invoke by name; the only text pins (`:124-127`) are `function Get-SharpProofBuildParallelism` and `function Invoke-SharpProofParallelDotnetBuilds`, which survive if the four public names remain.
- **Proposed change:** Add a private `Get-SharpProofCpuBudget -RepositoryRoot -Divisor <name> | -Percent <name>` owning the override, processor-count and contract read; reduce the four exported functions to two-line wrappers. Keep all four names in `Export-ModuleMember`.

### 3. Fold the repeated `Join-Path` + `$LASTEXITCODE` guard into one helper
- **Files:** `scripts/Invoke-SharpProofContainer.ps1:75-664` — 22 `if ($LASTEXITCODE -ne 0)` blocks at `:158, 171, 197, 215, 226, 359, 370, 380, 425, 446, 479, 492, 509, 526, 538, 543, 597, 600, 608, 615, 622`, plus 20+ `& (Join-Path $repositoryRoot 'scripts/…')` invocations
- **Est. LOC saved:** ~40
- **Why it's safe:** Every branch is the identical shape `& (Join-Path $repositoryRoot '<script>') <args>; if ($LASTEXITCODE -ne 0) { throw '<message>' }`. The message is the only per-branch variation, so a helper taking `-Path`, `-Failure` and splatted arguments reproduces the exact throw. `DocumentationSupportContractTests.cs:69-70` reads this file but asserts only on the ordering of `Generate-Readme.ps1`/gate names, which a helper preserves as long as the script paths stay literals.
- **Proposed change:** Add `Invoke-RequiredScript([string]$RelativePath, [string]$Failure, [hashtable]$Arguments)` next to the existing local `Invoke-DotNet` (`:60`); rewrite the 20+ sibling-script invocations. *(Reinforces the sibling proposal for `Invoke-SharpProofRequiredDotnet` — the same invoke/check/throw shape is needed for both the `dotnet` path and the sibling-script path, and both callers live in this file.)*

### 4. Delete the unreferenced `Get-SharpProofModuleVersionId.ps1`
- **Files:** `scripts/Get-SharpProofModuleVersionId.ps1:1-30`
- **Est. LOC saved:** 30
- **Why it's safe:** A repo-wide grep for `Get-SharpProofModuleVersionId` returns hits only inside `.git/*/index` — no `.ps1`, `.psm1`, `.cs`, `.sh`, `.yml`, `.yaml`, `.md` or `.json` reference. It is **not** listed in `eng/acceptance/contract.json` and **not** in `docs/code-usefulness-audit.md` (unlike every other retained script), so no contract update is required. The MVID-reading behavior it duplicates exists in C# at `SharpProof.Gates/Program.cs:161` and `SharpProof.CompilerArtifact/CompilerCaptureAuthority.cs:66`, which is what `StandaloneGateEvidenceTests.cs:62` actually asserts on.
- **Proposed change:** Delete the file.

### 5. Replace the hand-rolled `-NoBuild`/`-Fast` splat construction with one helper
- **Files:** `scripts/Invoke-SharpProofContainer.ps1:273-303, :344-358` (and the mirrored `$fastBuildArguments` handling at `:53-58, :239-243, :260, :312, :332`)
- **Est. LOC saved:** ~20
- **Why it's safe:** The `test-changed`, `semantic-tests` and `package-tests` branches each build the same splat by hand: seed `Configuration` (plus `TestFilter`/`PackageSource` where the callee accepts them), then `if ($NoBuild) { … = $true }` and `if ($Fast) { … = $true }`. The switches are already validated once up front (`:38-52`), so the per-branch construction carries no additional logic. Splatting an absent key is identical to not passing the switch.
- **Proposed change:** Add `New-TestInvocationArguments` returning the hashtable with `Configuration` plus any set switches; use it in the three branches.

### 6. Remove the degenerate single-branch `case` in `entrypoint.sh`
- **Files:** `eng/container/entrypoint.sh:113-169`
- **Est. LOC saved:** ~5 (plus a 4-space de-indent of ~55 lines)
- **Why it's safe:** The statement is `case "${command_name}" in *) … ;; esac` — one wildcard arm that always matches, wrapping the whole task-clone-and-exec body. Removing it cannot change which code runs. Genuine command discrimination already happens earlier in `requires_clean_exact_commit_source`/`requires_git_source` (`:55-83`) and the `dev` short-circuit at `:51`.
- **⚠ Caveat that must be handled:** `scripts/Test-SharpProofTrustedMutations.ps1:2067` pins the exact string `    if [[ "${source_has_git}" = "true" ]]; then` **with its current four-space indent**, so the de-indent requires updating that catalog entry in the same change — otherwise keep the body indented and only drop the four wrapper lines.

> **`entrypoint.sh` command dispatch — checked, no dead-command finding.** Unlike `compose.yaml`'s 17 unused services, `entrypoint.sh` has no per-command dispatch table to prune; it forwards every command verbatim to `Invoke-SharpProofContainer.ps1`. Its only command lists are the two guard predicates, and every name in them is reachable.
>
> **⚠ Adjacent correctness observation (not a reduction, not counted):** `pilot-review` is the only branch that writes the `pilots` qualification receipt (`Invoke-SharpProofContainer.ps1:617-627`), yet **no workflow, doc, or script invokes `tooling pilot-review`** — `release.yml` runs `pilots` then `release-qualification`. That looks like a gap in release orchestration rather than dead code.

---

## CI workflows, Dockerfile, and compose (round 3)

**Estimated savings: ~64 LOC** of *new* findings (excluding the four earlier `.github/` findings), of which ~41 are self-contained and need no test or `contract.json` change.

### 1. Delete the never-built `build` / `test` / `package` Dockerfile stages
- **Files:** `eng/container/Dockerfile:84-106`
- **Est. LOC saved:** ~23 (Dockerfile only; ~45 more in the contract script if the cross-area change is taken)
- **Why it's safe:** Every image build in the repo targets `dev` and nothing else — `.github/actions/build-tooling/action.yml:22` (`--target dev`) and `compose.yaml:6` (`target: dev`) are the only builders, and a repo-wide grep for `--target`/`target:` returns only those two plus assertions pinning `target: dev` (`scripts/Test-SharpProofContainerContract.ps1:315`, `SharpProof.ArchitectureTest/ContainerAuthorityScriptTests.cs:84`). `portable-tests`/`pack` run through the `dev` entrypoint, never these stages. Within the dead block, `test` (`:95-98`) and `package` (`:102-105`) also re-declare `ENV SHARPPROOF_REPO_ROOT=/src`, `WORKDIR /src`, `USER sharpproof`, `ENTRYPOINT` — all inherited from `build`, so 8 of those lines are no-ops even if the stages stay.
- **⚠ Blocking dependency (outside this area):** `scripts/Test-SharpProofContainerContract.ps1:159-245` pins the exact nine-stage `$expectedStages` list and a `$stageContracts` table for `build`/`test`/`package`; those must be trimmed in the same commit. `eng/acceptance/contract.json:665` lists only the Dockerfile path, so no contract edit is needed. Note the minimum-risk subset (deleting just the 8 inherited-and-restated lines) *also* needs the script change, because `Assert-SingleMatchingLine` requires those exact lines per stage.
- **Proposed change:** Drop `FROM toolchain AS build` / `AS test` / `AS package`, trimming the contract script in the same commit.

### 2. Extend the `build-tooling` prelude to a `checkout + build-tooling` composite across 4 more workflows
- **Files:** `.github/workflows/ci.yml:25-30`, `coverage.yml:30-32` and `:57-58`, `nightly.yml:19-24`, `weekly.yml:19-24`, `security-reusable.yml:37-42` and `:60-65`
- **Est. LOC saved:** ~24 (6 job sites × ~4 lines; the composite action file itself is already accounted for by the earlier `package-consumers` prelude finding)
- **Why it's safe:** All six sites are byte-identical — `actions/checkout@3d3c42e5…` with `fetch-depth: 0`, then `uses: ./.github/actions/build-tooling`. The only variation is the step `name`, which is cosmetic. A local composite action can itself call `actions/checkout`, so the pinned SHA stays pinned in one place.
- **⚠ Two caveats:** (a) `SharpProof.ArchitectureTest/ArchitectureTests.cs:1044` asserts `package-consumers.yml` literally contains `fetch-depth: 0` — leave those sites inline or update the assertion; (b) new files under `.github/actions/` will likely need adding to `eng/acceptance/contract.json:93-97` (`releaseAuthorityClosure`) and `:686` (`releaseAuthorityDerivedLeaves`), which currently list `build-tooling/action.yml`.
- **Proposed change:** Add `.github/actions/checkout-and-build-tooling/action.yml` running the pinned checkout then the existing `build-tooling` action; replace the six inline pairs with one `uses:`.

### 3. Hoist the five per-job `COMPOSE_PROJECT_NAME` env blocks to workflow level
- **Files:** `.github/workflows/package-consumers.yml:36-37, 105-106, 176-177, 293-294, 339-340` (workflow `env:` already at `:19-20`)
- **Est. LOC saved:** ~9
- **Why it's safe:** All five follow the identical shape `sharpproof-<label>-${{ github.run_id }}-${{ github.run_attempt }}`; the label is per-job only to keep compose project namespaces distinct, and `${{ github.job }}` reproduces that uniqueness exactly. Nothing outside the workflow reads these values — grep for `COMPOSE_PROJECT_NAME` outside `artifacts/` hits only docs (`AGENTS.md:6`, `docs/container-development.md`) and the `compose.yaml:2` image-name assertions (`Test-SharpProofContainerContract.ps1:290`, `ContainerAuthorityScriptTests.cs:36`), none of which pin a specific project name. `DockerWorkflowsCapCpuUseToHostedRunnerCapacity` (`ArchitectureTests.cs:1557-1580`) only requires the literal `SHARPPROOF_CONTAINER_CPU_LIMIT: 4`, already at workflow level.
- **Proposed change:** Add `COMPOSE_PROJECT_NAME: sharpproof-${{ github.job }}-${{ github.run_id }}-${{ github.run_attempt }}` to the workflow-level `env:`; delete the five job-level blocks. The `release-qualification` job keeps its step-level `env:` at `:218`.

### 4. Drop the four job-level `permissions: contents: read` blocks that restate the workflow default
- **Files:** `.github/workflows/package-consumers.yml:34-35, 174-175, 291-292`; `security-reusable.yml:22-23`
- **Est. LOC saved:** ~8
- **Why it's safe:** Both files already declare workflow-level `permissions: contents: read` (`package-consumers.yml:12-13`, `security-reusable.yml:12-13`), and a job with no `permissions:` key inherits the workflow block verbatim. These four list *exactly* `contents: read` and nothing else, so the effective token scope is unchanged.
- **Proposed change:** Delete the four redundant two-line blocks. **Deliberately not touched:** `package-consumers.yml:27-29` (`packages: read`), `:73-77` (`id-token`/`attestations`/`artifact-metadata: write`), `:336-338` (`id-token: write`), and `security.yml:24-26` — job `permissions` *replaces* rather than merges, so those must stay complete.

> **Checked, NOT a reduction:** `security.yml` and the `package-consumers` `security` job are **not** redundant. `security.yml:3-10` triggers on `push: branches: [master]`, PRs, and a weekly cron — GitHub does not fire `branches:`-filtered push triggers for tag pushes, so the `if: startsWith(github.ref, 'refs/tags/v')` job at `package-consumers.yml:23-29` is the **only** tag-time security run, and `release-qualification` genuinely `needs: security` (`:170`). Likewise, `weekly.yml` is deletable but `nightly.yml` is not — it is pinned by path in `eng/acceptance/contract.json:96` and `:687`, and `ArchitectureTests.cs:2360-2371` requires `tooling fuzz-nightly` to appear in `nightly.yml` and in no other workflow.

---

## Deep pass: Gates + Gates.Test (round 3)

**Estimated savings: ~245 LOC.**

> **⚠ This corrects the round-one Effects/Gates entry**, which concluded "no dead public/internal types or methods surfaced" after an identifier-frequency scan. A targeted per-member grep found two `internal` members in `AnalyzerGateHost` with **zero callers anywhere**, including tests. Frequency scans miss members whose names collide with common words or with other types' members — finding 1 below is the counter-example.

### 1. Dead surface in `AnalyzerGateHost` (two members, zero callers)
- **Files:** `SharpProof.Gates/AnalyzerGateHost.cs:61-87` (`AnalyzeAsync(string, string?, CancellationToken)`), `:158-168` (`CreateOptions`)
- **Est. LOC saved:** ~38
- **Why it's safe:** Grepped `AnalyzerGateHost` across all `.cs` in the repo (excluding obj/bin): every `AnalyzeAsync` call site passes a `Compilation` (`PerformanceGate.cs:843,920,980,1014`, `OpenSourceCorpusRunner.cs:124`), and `CorpusGate` uses `AnalyzeWithSemanticOutcomesAsync`. `CreateOptions` has exactly one hit in the repo — its own definition. Neither is touched by `SharpProof.Gates.Test`. Both are `internal`; no reflection names them (the only reflection in the gate tests targets `CreatePerformanceProbeProject`, `RunDotnetAsync`, `AnalyzeEnabledCompilation`).
- **Proposed change:** Delete both. The string-source overload's inline compile-error block is already implemented verbatim by the surviving `ThrowIfCompilationHasErrors` (`:170-187`), so nothing is lost.

### 2. Three tests repeat the same four-document policy arrange block
- **Files:** `SharpProof.Gates.Test/PerformanceGateTests.cs:758-798, :800-841, :843-886`
- **Est. LOC saved:** ~45
- **Why it's safe:** Each opens the identical four `XDocument.Load(Path.Combine(root, "SharpProof.Package"|"SharpProof.Verifier", "buildTransitive", …))` — lines 762-781, 804-823 and 847-866 are character-for-character the same 20 lines — then mutates one node and asserts `InvalidDataException`. Only the mutation differs. Test-only.
- **Proposed change:** Add a private `LoadPolicyDocuments()` returning the four documents as a tuple; the two verifier-condition tests (`:800`, `:843`) can further collapse into one `[TestCase]` parameterized by the condition edit.

### 3. Two retained-memory probes are the same loop with an optional analyzer
- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:776-797` and `:799-836`
- **Est. LOC saved:** ~35
- **Why it's safe:** `MeasureCompilerOnlyRetainedBytes` and `MeasureUnannotatedAdvisoryAnalyzerRetainedBytes` share identical structure: `ForceCollection()`, `GC.GetTotalMemory(true)`, a `RetainedCompilationCount` loop calling `AnalyzerGateHost.CreateCompilation(source, $"Retained_{kind}_{index}")` + `GetDiagnostics`, then `ForceCollection`/`GetTotalMemory`/`KeepAlive`/`Math.Max(1, after - before)`. The only differences are the extra `AnalyzeUnannotatedAdvisory` call and the post-loop assertion. Both are called once each (`:148`, `:152`) with distinct `kind` strings, so assembly names — and therefore measured allocations — are unchanged. **No gate threshold moves:** the measured bytes flow into `EvaluateRetainedMemoryLimits` exactly as before.
- **Proposed change:** Merge into `MeasureRetainedBytes(source, kind, bool runAnalyzer, cancellationToken)`, adding the analyzer pass and the quiet-and-no-session assertion only when `runAnalyzer`.

### 4. `SnapshotExpectation` duplicates `CorpusObservation` field-for-field
- **Files:** `SharpProof.Gates/Corpus/CorpusModels.cs:62-72` vs `:83-93`; `CorpusGate.cs:446-455` vs `:457-466`; uses at `CorpusGate.cs:531-576`, `CorpusSnapshotFormat.cs:112-116`
- **Est. LOC saved:** ~22
- **Why it's safe:** Both are `internal sealed record (string, CorpusVerdict, AnalyzerSemanticOutcome, ImmutableArray<string>)` with a byte-identical `ToCanonicalLine()`. **Equality checked:** neither type is ever compared with `==`/`Equals` nor used as a dictionary key — `SnapshotExpectation` appears only as a dictionary *value* (`ImmutableDictionary<string, SnapshotExpectation>`), a constructor argument, and a `ToCanonicalLine()` receiver; comparison always goes through the explicit `Matches` helpers. The compiler-generated value equality (which would compare `ImmutableArray<string>` by reference anyway) is unobserved, so the merge cannot change a gate verdict.
- **Proposed change:** Delete `SnapshotExpectation`; use `CorpusObservation` for snapshot baselines; delete the redundant second `Matches` overload.

### 5. `RunBuildPairAsync` writes the same two awaits twice for ordering
- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:567-610`
- **Est. LOC saved:** ~18
- **Why it's safe:** The `if`/`else` arms are identical apart from which `RunDotnetAsync` is awaited first; both return `new PackageBuildPair(baseline, unannotatedAdvisory)` in the same argument order. Order-balancing — the whole point of `unannotatedAdvisoryFirst` — is preserved as long as sequential await order is preserved, which a local function invoked in the chosen order does exactly. No sample value or ratio computation changes.
- **Proposed change:** One local `Task<double> Run(string p) => RunDotnetAsync(p, restore: false, symbol, cancellationToken)`, awaited in the selected order; construct the pair once.

### 6. Four parallel `switch` expressions over the same variant
- **Files:** `SharpProof.Gates/Corpus/CorpusCatalog.cs:281-304`
- **Est. LOC saved:** ~14
- **Why it's safe:** `className`, `methodName`, `helperName` and `inputName` each switch on the same `variant` with the same three arms (`Rename`, `EscapedIdentifiers`, default). Collapsing to one switch returning a 4-tuple produces identical strings for every variant, so the generated corpus source — and the canonical snapshot — stay byte-identical.
- **Proposed change:** One `var (className, methodName, helperName, inputName) = variant switch { … };`.

### 7. `Program.cs` repeats the same envelope-and-exit-code shape per gate
- **Files:** `SharpProof.Gates/Program.cs:47-60, :75-88`
- **Est. LOC saved:** ~14
- **Why it's safe:** The `corpus` and `performance` branches are identical modulo the gate function and the `command` string already passed to `CreateStandaloneEnvelope`; both serialize with `JsonDefaults.Indented` and return `result.Passed ? 0 : 1`. A dispatch table keyed by command name preserves the JSON payload (the gate name is already a parameter) and the exit codes, including the fall-through usage/`2` path.
- **Proposed change:** Dispatch the two envelope commands via a dictionary or a local function taking the gate delegate; leave `all`, `corpus-print`, `corpus-update`, `performance-smoke` as-is — their output shapes genuinely differ.

### 8. Temp-directory arrange block repeated across both test fixtures
- **Files:** `SharpProof.Gates.Test/CorpusGateTests.cs:114-118, :164-168, :235-239`; `PerformanceGateTests.cs:665-669, :716-720`
- **Est. LOC saved:** ~15
- **Why it's safe:** All five are the same `Path.Combine(Path.GetTempPath(), "SharpProof.Gates.Test", Guid.NewGuid().ToString("N"))` + `Directory.CreateDirectory`. Test-only.
- **Proposed change:** One shared `internal static string CreateTestRoot()` in a small `GateTestPaths` class. While there, hoist the three copies of the snapshot `header` const in `CorpusGateTests.cs:310, 342, 360` to one fixture-level `const`.

### 9. Corpus file paths rebuilt inline instead of via `GetCorpusDirectory`
- **Files:** `SharpProof.Gates/Corpus/CorpusGate.cs:47-61, :384-388`; helper at `OpenSourceCorpusCatalog.cs:78-84`
- **Est. LOC saved:** ~12
- **Why it's safe:** All four sites build `Path.Combine(root, "SharpProof.Gates", "Corpus", <file>)`. `GetCorpusDirectory` returns the same thing modulo a `Path.GetFullPath(repositoryRoot)` normalization — and `CorpusGate.cs:396` already passes that helper's result as the transaction directory for the *same* files, so the two must already agree.
- **Proposed change:** `var corpus = OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);` then `Path.Combine(corpus, …)` in both `RunAsync` and `WriteActualSnapshotAsync`.

### 10. Third copy of the "compilation had errors" throw
- **Files:** `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs:107-121`; `AnalyzerGateHost.cs:170-187`
- **Est. LOC saved:** ~10
- **Why it's safe:** Same shape (filter `Severity == Error`, join with `Environment.NewLine`, throw), differing only by a `.Take(25)` cap, the exception type (`InvalidDataException` vs `InvalidOperationException`) and the message prefix — all parameterizable.
- **Proposed change:** Add `AnalyzerGateHost.FormatCompilationErrors(Compilation, int limit, CancellationToken) -> string?`; each caller throws its own typed exception with its own prefix.

### 11. `SplitMsBuildList` and `SplitTargetList` are identical
- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:1820-1829` and `:1831-1840`
- **Est. LOC saved:** ~10
- **Why it's safe:** Same signature, same body, differing only in the lambda parameter name (`item` vs `target`). Both `private`; call sites are `:1700`, `:1706` (target list) and `:1806`, `:1808` (MSBuild list). Nothing reflects on either name.
- **Proposed change:** Delete `SplitTargetList`; point its two call sites at `SplitMsBuildList`.

### 12. `ValidateContract` re-checks positivity that `Load` already enforces
- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:1242-1255`; `AcceptancePerformanceContract.cs:38-73, 76-90`
- **Est. LOC saved:** ~8
- **Why it's safe:** `AcceptancePerformanceContract` is constructed in exactly one place — `Load` (grep for `new AcceptancePerformanceContract` returns only the record declaration and `:33`) — and `GetPositiveFiniteDouble` already throws `InvalidDataException` for every one of the eight `double` limits re-tested at `:1242-1255` (`AcceptancePerformanceContractTests.cs:10-19` pins this). The only `with`-expression (`:88`) rewrites only `Warmups`, `Samples`, `IdeEdits` — all `int`. So the eight `<= 0` clauses are **unreachable**.
- **Proposed change:** Reduce the third `if` to the three integer non-negativity clauses (`MaximumRetainedMemoryIncreaseMiB`, `MaximumEnabledRetainedCompilations`, `MaximumEnabledRetainedMemoryIncreaseMiB` — loaded via plain `GetInt32()` and the only live checks there); leave the fixed-protocol and smoke-protocol checks untouched.

### 13. `CountSourceFiles` reimplemented inline in its own file
- **Files:** `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:266-269` vs `:86-93`
- **Est. LOC saved:** ~4
- **Why it's safe:** `document.Methods.Select(m => $"{m.SourceId}|{m.Path}").Distinct(Ordinal).Count()` is exactly what `CountSourceFiles` computes (`m.SourceId + "|" + m.Path`). Same value, so the `MinimumSourceFileCount` gate check is unchanged.
- **Proposed change:** `var sourceFileCount = CountSourceFiles(document.Methods);`

---

## Deep pass: SharpProof.ArchitectureTest (round 3)

**Estimated savings: ~1,170 LOC** (≈9.5% of the project's 12,251), of which roughly 180 lines are pure reflow of over-wrapped `Assert.That` calls inside finding 1.

> **⚠ This overturns a round-one conclusion.** The earlier pass reported that "ArchitectureTests.cs's 43 tests each assert a genuinely different repository invariant and cannot be data-driven." That is true of the *tests*, but not of their *contents*: 197 of the assertions inside them are the degenerate form `Assert.That(<local>, Does.Contain("literal"))` with no message and no computed argument, and those are table-drivable without merging any test. See finding 1.

### 1. Table-driven source-contract assertions replace 197 hand-written `Does.Contain` statements
- **Files:** `BuildSchedulingTests.cs:12-1005` (92 sites, 223 physical lines); `ArchitectureTests.cs` (69 sites, 217 lines); `ReleaseCoverageBaselineTests.cs` (19 sites, 59 lines); `DependencyAutomationTests.cs` (10 sites, 36 lines); `BoundaryEnforcementTests.cs` (7 sites, 21 lines)
- **Est. LOC saved:** ~330
- **Why it's safe:** All 197 are exactly `Assert.That(<localVariable>, Does.Contain("literal"));` / `Does.Not.Contain("literal")` — no custom message, no computed argument. Measured cost: **556 physical lines for 197 logical assertions** (2.8 lines each, purely formatter wrapping). A helper keeps one assertion per needle with the needle string as the failure label — **strictly more diagnostic than today**, where the plain form supplies no message at all. Needle count is unchanged, so no invariant is dropped. Representative blocks: `BuildSchedulingTests.cs:99-141` (17 needles across 2 files), `:70-93` (8), `:148-171` (8), `ArchitectureTests.cs:1529-1554`.
- **Proposed change:** Add `SourceContract.Assert(text, label, required: [...], forbidden: [...])`; rewrite each `Assert.EnterMultipleScope()` block whose contents are only plain `Does.Contain`/`Does.Not.Contain` as one call with two string arrays. The 40 remaining `Does.Contain` sites that carry messages or computed arguments stay untouched.
- **Reflow vs substance:** ~55% of this saving is reflow of over-wrapped calls; ~45% is genuine removal of the repeated `Assert.That(x, Does.Contain(` scaffold. Counted together because the two are not separable in one edit.

### 2. One shared pwsh fixture-script runner; ten near-pure "run script, assert exit 0" classes collapse
- **Files:** identical `RunFixtureAsync(mutation)` + `ProcessStartInfo` blocks at `PublicationPlanIdentityTests.cs:66-100`, `PublicationPlanTopologyTests.cs:56-90`, `PublicationDestinationAuthorityTests.cs:78-112`, `ReleaseVersionAuthorityTests.cs:54-88`, `ReleaseChecksumAuthorityTests.cs:90-124`, `SbomSymbolArtifactScopeTests.cs:46-80`, `DocumentationSupportContractTests.cs:36-70`, `SbomReleaseIdentityTests.cs:17,121`. Zero-mutation variants: `PilotAuthorityTests.cs:12-29`, `ContainedPathAuthorityTests.cs:13-31`, `ReleaseTagValidationTests.cs:12-33`, `ReleaseAuthorityClosureTests.cs:15-31`, `ReleaseConfigurationScriptTests.cs:14-38`, `StandaloneGateEvidenceTests.cs:13-37`, `ReleaseJsonAuthorityTests.cs:14-27`. Redundant per-file `ProcessResult`/`RunAsync`/`DeleteDirectory` sets: `AcceptanceScriptTests.cs:252-325`, `ChangedTestSelectionTests.cs:137-177`, `ContainerAuthorityScriptTests.cs:287-326`, `ContainerSourceCleanlinessTests.cs:368-424`, `CoverageScriptTests.cs:1635-1708`, `ProductionInventoryAuthorityTests.cs:298-355`, `ReleaseCoverageBaselineTests.cs:936-1042`, `FuzzRunnerEvidenceTests.cs:103-156`
- **Est. LOC saved:** ~400
- **Why it's safe:** Every one builds the same `pwsh -NoLogo -NoProfile [-NonInteractive] -File <scripts/Test-*.ps1> [-Mutation <m>]` invocation with both streams redirected, `UseShellExecute=false`, async read + `WaitForExitAsync`, asserting on exit code with stdout+stderr as the message. The *invariant* lives entirely in the PowerShell fixture script and the `[TestCase]` mutation table — neither moves.
- **⚠ Two constraints:** `FuzzRunnerEvidenceProcessSafetyTests.cs:11-30` greps `FuzzRunnerEvidenceTests.cs` for `ReadToEndAsync`/`WaitForExitAsync`/`CancelAfter`/`Kill(entireProcessTree: true)`, so the shared helper must live in that file or that test must be retargeted. **And a real hazard surfaced:** `StandaloneGateEvidenceTests.cs:31-33` uses the *blocking* `ReadToEnd()`/`WaitForExit()` that the safety test forbids elsewhere — consolidation also fixes a genuine deadlock risk.
- **Proposed change:** Add a `PwshFixtures` helper (build args, run, return `(ExitCode, Output)`, plus `AssertSucceedsAsync`); delete the 8 per-file `ProcessResult`/`RunAsync`/`DeleteDirectory` copies and the 15 inline `ProcessStartInfo` blocks. Then merge the five no-argument fixture classes (241 lines total) into one `[TestCase("Test-SharpProofXFixtures.ps1")]`-driven fixture of ~25 lines; each script name stays an explicit test case, so every gate still runs as its own NUnit case.

### 3. Twenty copies of `RepositoryRoot()` / `FindRepositoryRoot()` in this project alone
- **Files:** 20 definitions — `ArchitectureTests.cs:2407`, `BoundaryEnforcementTests.cs:624`, `CoverageScriptTests.cs:1689`, `ReleaseCoverageBaselineTests.cs:1022`, `ProductionInventoryAuthorityTests.cs`, `PilotAuthorityTests.cs:31`, `ContainedPathAuthorityTests.cs:34`, `NativeTestBootstrapTests.cs:56`, `SbomSymbolArtifactScopeTests.cs:73`, `StandaloneGateEvidenceTests.cs:69`, `FuzzRunnerEvidenceProcessSafetyTests.cs:32`, +9 more
- **Est. LOC saved:** ~240
- **Why it's safe:** All 20 walk parents from a base directory looking for `SharpProof.sln`, differing only in seed (`AppContext.BaseDirectory` vs `TestContext.CurrentContext.TestDirectory` — same directory under NUnit), exception type, and message text. No test asserts on the exception type or message of a *missing* repository root; that throw is unreachable in any run inside the repo.
- **Proposed change:** Add `internal static class RepositoryPaths { public static string Root { get; } }` (cached static) and delete the 20 copies (~13 lines each). Fold in `Relative(path)`, duplicated verbatim at `ArchitectureTests.cs:2402` and `BoundaryEnforcementTests.cs:604`. *(This is the in-project share of the ~49 repo-wide copies noted earlier.)*

### 4. Duplicated project-graph helper set across two files
- **Files:** `ArchitectureTests.cs:2233-2317` and `BoundaryEnforcementTests.cs:521-604`
- **Est. LOC saved:** ~85
- **Why it's safe:** Both define `TransitiveProjectClosure`, `ProjectReferences`/`GetProjectReferences`, `ProjectPackages`, `SourceFiles`/`ProductionSourceFiles`, `ReadProductionSources`, `ProjectFile`, `ProjectDirectory` with the same semantics: same `OutputItemType != "Analyzer"` filter on `ProjectReference`, same `Include`→filename projection, same `obj`/`bin` exclusion, same `SharpProof.Fuzz`→`Tools/SharpProof.Fuzz` special case, same ordinal ordering. The only real difference is the closure's return shape (`HashSet<string>` excluding the root vs lazily-yielded sequence including it) — one implementation returning the ordered set plus an explicit `includeRoot` flag covers both call sites without changing what either test observes. Additionally `BoundaryEnforcementTests.Count` (`:609`) and `NativeTestBootstrapTests.CountOrdinal` (`:47`) are the same 14-line ordinal occurrence counter.
- **Proposed change:** Extract one `ProjectGraph` static helper alongside `RepositoryPaths`; delete both copies and the duplicate counter.

### 5. Five different hand-rolled workflow-file enumerators
- **Files:** `ArchitectureTests.cs:982-988`, `:1531-1538`, `:1559-1566`; `DependencyAutomationTests.cs:158-174`, `:200-216`
- **Est. LOC saved:** ~45
- **Why it's safe:** All five compute the same set — every `.yml`/`.yaml` directly under `.github/workflows`. Three use `EnumerateFiles(root,"*.yml").Concat(EnumerateFiles(root,"*.yaml"))`; two use `EnumerateFiles(dir)` plus a case-insensitive extension filter and an ordinal `OrderBy`. Unifying on the ordered, case-insensitive version is a superset (globs on Windows are already case-insensitive, and a deterministic order only stabilizes failure output). Each test keeps its own distinct downstream assertion — CodeQL absence, SHA pinning, `setup-dotnet` absence, CPU cap, MSBuild switch form.
- **Proposed change:** Add `RepositoryPaths.WorkflowFiles()` returning ordinally sorted paths; replace the five inline enumerations.

### 6. `BannedApiProjects` is an unverified third copy of the production-project catalog
- **Files:** `BoundaryEnforcementTests.cs:12-36` vs `ArchitectureTests.cs:33-55`
- **Est. LOC saved:** ~24
- **Why it's safe:** `BannedApiProjects` (23 entries) is exactly `ArchitectureTests.ProductionProjects` (22) plus `SharpProof.Gates`, element for element. `ArchitectureTests.cs:1886-1894` already asserts that precise expression equals the `projects` keys of `eng/coverage/baseline.json`, confirmed to contain exactly those 23 names. So the repository's double-entry ledger is `ProductionProjects` ⟷ `baseline.json`; `BannedApiProjects` is a **third copy that nothing cross-checks and that can silently drift**. Deriving it removes a drift source rather than removing a check.
- **Proposed change:** Move `ProductionProjects` to the shared helper and define `BannedApiProjects = [.. ProductionProjects, "SharpProof.Gates"]`. **Leave `ProductionProjects` itself hardcoded, and leave `DeclarationOnlyTcbCoverageFiles` (`ArchitectureTests.cs:28-31`) hardcoded** — both are the independent side of a deliberate ledger check (`:705-724`, `:1880-1894`) and must NOT be derived from JSON.

### 7. Duplicated qualification-receipt workspace scaffolding
- **Files:** `ReleaseCoverageBaselineTests.cs:174-252` and `:258-330`
- **Est. LOC saved:** ~45
- **Why it's safe:** The two tests share verbatim the temp-workspace creation under `artifacts/qualification-fixtures/<guid>`, the `git rev-parse HEAD` capture, the 14-line `Write-SharpProofQualificationReceipt.ps1` invocation with `-Gate/-EvidencePath/-ReceiptDirectory`, the `foreach (fixture)` loop asserting `ExitCode == 0 Is.EqualTo(fixture.Valid)`, and the `finally` cleanup. Only the fixture tables differ (package-identity mutations vs failed/stale/malformed-JSON mutations) — those tables, and the extra receipt-file existence assertion at `:325-330`, stay exactly as they are.
- **Proposed change:** Extract `RunReceiptFixturesAsync(gate, evidenceFileName, (string Content, bool Valid)[] fixtures)` handling workspace lifecycle and the assert loop; both tests become their fixture table plus one call.

---

## JSON / data layer (round 3)

**Estimated savings: ~267 lines** (~153 excluding the double-entry-guard removal in finding 2).

### 1. `.gitignore` is unmodified GitHub `VisualStudio.gitignore` boilerplate for toolchains this repo does not use
- **Files:** `.gitignore:11-14, 46-49, 58-61, 90-101, 109-112, 117-123, 131-163, 177-189, 205, 212-213, 223-234, 237-263, 265-295`
- **Est. LOC saved:** ~150
- **Why it's safe:**
  - Only two things read this file, and both are *negative* assertions, so shrinking it cannot break them: `ArchitectureTests.cs:1399` reads it and its sole assertion (`:1447`) is `Does.Not.Contain("repository.bundle")`. `OpenCodePluginDependencyTests.cs:49` reads a *different* file, `.opencode/.gitignore`.
  - `scripts/Test-SharpProofMutationEvidence.ps1:127` does not read it — it *writes* a synthetic `.gitignore` into a temp fixture repo.
  - Nothing hashes or digests it: grep across `*.ps1`/`*.props`/`*.targets`/`*.csproj`/`*.json`/`*.yml` returns only those two hits.
  - The retained blocks cover Mono/Xamarin, ATL, Visual C++ (`*.ncb`, `*.sdf`, `ipch/`), Visual Studio 6 (`*.plg`, `*.vbw`, `*.opt`), TFS 2012, Silverlight/RIA, LightSwitch, BizTalk (`*.btp.cs`, `*.odx.cs`), SQL Server/BI (`*.mdf`, `*.rdl.data`, `*.bim.layout`), Azure emulator, Node (`node_modules/`), Paket, FAKE, NCrunch, MightyMoose, Chutzpah, DocProject, InstallShield, sass, Telerik JustMock, Tabs Studio, GhostDoc, CodeRush, Ionide, MFractor, healthchecksdb, Orleans. The repo is C#/PowerShell only — 47 projects, no `package.json` outside `.opencode`, and no `.vcxproj`/`.fsproj`/`.sqlproj`/`.rptproj`/`.btproj` anywhere.
  - `:291-292` is a dangling comment — `# Backup folder for Package Reference Convert tool in Visual Studio 2017` followed immediately by `# Ionide …`; the pattern it documented was already deleted.
  - `:21` `!eng/release/third-party-components.json` is redundant with `:20` `!eng/release/`.
- **Proposed change:** Trim to what this repo actually produces (VS user files, `bin/`, `obj/`, `artifacts/`, `.vs/`, `TestResults/`, `*.trx`, `*.binlog`, coverage outputs, `*.nupkg`/`*.snupkg`, `*.pdb`, the `eng/release/` re-includes, and the repo-specific audit-dump block at `:297-301`). Drop the dead comment and the redundant re-include.

### 2. `releaseAuthorityClosure.paths` is 110 entries a script independently recomputes and asserts equal
- **Files:** `eng/acceptance/contract.json:93-206`
- **Est. LOC saved:** ~114
- **Why it's safe:** `scripts/Test-SharpProofReleaseAuthorityClosure.ps1:17-25` computes `$derived = Get-SharpProofReleaseAuthorityClosure -RepositoryRoot $repositoryRoot` and throws unless the declared list has the same count and members. The declared list therefore carries **zero information the repo does not already determine**. It is also fully contained in the TCB list in the same file — measured `closure=110, tcb=346, overlap=110`, i.e. `closure - tcb == ∅` — and `:26-30` of that script separately asserts each derived path occurs exactly once in the TCB (which has no duplicates). Grep for `releaseAuthorityClosure` across `*.ps1`/`*.cs` returns exactly one consumer.
- **⚠ Honest caveat:** this is a deliberate double-entry cross-check. Deleting it removes a redundancy guard rather than dead data. It is the single largest mechanically-redundant block in the data layer — **take it only if the team accepts derived-only authority.**
- **Proposed change:** Delete the `releaseAuthorityClosure` object and have the script validate the derived closure against the TCB directly (the check it already performs at `:26-30`), dropping the declared-vs-derived equality step.

### 3. `.editorconfig` suppression for a deleted file
- **Files:** `.editorconfig:71-73`
- **Est. LOC saved:** ~3
- **Why it's safe:** The section is `[SharpProof.Specs/ApiSpecModel.cs]` with `dotnet_diagnostic.CA1720.severity = none`. That file does not exist — `SharpProof.Specs/*.cs` contains only `ApiSpecContentDigest.cs`, `ApiSpecInstantiation.cs`, `ApiSpecTable.cs`, `ApiSpecTermValidator.cs`, `DeclarativeModels.generated.cs`, `DefaultApiSpecCatalog.generated.cs`, `FrameworkTypeMetadataNames.cs`, `GlobalUsings.cs`, `SpecIdentifiers.cs`. A repo-wide grep for `ApiSpecModel` (excluding `obj/`, `artifacts/`) returns **zero** hits. Every other `.editorconfig` file section resolves to an existing file.
- **Proposed change:** Delete the section and its blank separator.

> ### Verified clean — recorded so nobody re-audits
> - **`Directory.Packages.props`:** all 14 `PackageVersion Include` entries are referenced by at least one csproj/props (lowest: `Microsoft.NETFramework.ReferenceAssemblies.net472`, 1 consumer). `eng/pilots/Directory.Packages.props` is a 5-line central-pinning opt-out with no pins.
> - **`contract.json` path staleness:** **every** path in `releaseAuthorityClosure`, `trustedKernel`, all 40 `trustedComputingBase` components, `productionCoordinatorComplexity.layers`, and `automation.mutationProjectWeights` was resolved against the filesystem — **zero missing**.
> - **`.slnf` filters:** all three are consumed (`Invoke-SharpProofCoverage.ps1:112` → Dev, `Invoke-SharpProofSemanticTests.ps1:41,295` → Semantic, `Invoke-SharpProofContainer.ps1:305` → Portable) and all are distinct (Semantic = Dev minus `Worker.Test`; Portable = Semantic minus `ArchitectureTest`/`Fuzz.Test`/`Gates.Test`). Every listed csproj exists.
> - **`eng/coverage/baseline.json`:** `SharpProof.Fuzz` *looks* stale but is not — the project lives at `Tools/SharpProof.Fuzz/` (also referenced at `Directory.Build.props:39,58`). All 23 project keys resolve.
> - **`eng/acceptance/algorithm-size-ratchets.json`** (16 paths), **`eng/generated/approved-outputs.v1.json`** (41), **`eng/pilots/catalog.json`** (5 pilot dirs): every referenced path exists.
> - **`eng/acceptance/preview-interface.v1.json`:** all 26 `msbuildProperties` are referenced in `SharpProof.Package`/`SharpProof.Verifier` props/targets. `retiredMsbuildProperties` is a deliberate absence guard — left alone.
> - **`SharpProof.DeclarativeModels.catalog.json` / `SharpProof.Projection.catalog.json`:** every declared record, class, container and projection method name resolves to a use outside its own generated file. (`LauncherPresentation.EffectKind` initially flagged but is consumed by `ClaimKind` at `SharpProof.Worker.Launcher/LauncherProjections.generated.cs:43`.)


---

## Round 4 — Additional findings

These findings were synthesized from the nine `.codex-round4-*.md` reports and
deduplicated against the entries above. Reports that found no credible new
opportunity were not repeated.

### 1. Share the duplicated `dev`/`loop` Compose environment declaration

- **Files:** `compose.yaml:38-51` (`dev`) and `compose.yaml:56-70` (`loop`)
- **Est. LOC saved:** ~8-10
- **Why it's safe:** Both services repeat the same development values for
  `NUGET_PACKAGES`, `DOTNET_CLI_HOME`, `DOTNET_CLI_USE_MSBUILD_SERVER`,
  `SHARPPROOF_REPO_ROOT`, and test parallelism. Their origin/ref/container,
  mounts, and loop-specific source/artifact settings remain distinct.
- **Proposed change:** Add a narrowly scoped environment anchor for the shared
  interactive-development variables and merge it into both services, retaining
  service-specific entries inline.
- **Confidence:** Medium; Compose mapping-merge behavior and the contract
  parser must accept the chosen syntax.
- **Validation needed:** Run `docker compose config` and
  `scripts/Test-SharpProofContainerContract.ps1`; verify that
  `SHARPPROOF_DEV_CONTAINER`, loop mounts, and all service-specific variables
  remain present.

### 2. Inline the default-timeout `RunDotnetAsync` forwarding overload

- **Files:** `SharpProof.Gates/Performance/PerformanceGate.cs:612-624`, with
  callers at `:437-447`, `:576-586`, and `:594-604`
- **Est. LOC saved:** ~13
- **Why it's safe:** The four-parameter overload only forwards to the
  five-parameter implementation with `PackageBuildProcessTimeout`. All visible
  callers use the four-parameter form and no caller supplies a custom timeout.
- **Proposed change:** Pass `PackageBuildProcessTimeout` at each caller and
  delete the forwarding overload.
- **Confidence:** High.
- **Validation needed:** Re-search `RunDotnetAsync(`, confirm every call has the
  intended timeout, run `tooling test -Target SharpProof.Gates.Test
  -TestFilter PerformanceGateTests`, and build the affected project.

### 3. Remove nullable noise from transaction recovery entries

- **Files:** `SharpProof.Gates/Corpus/CorpusFileTransaction.cs:41-42, 73-78,
  101-105, 118-140, 193-243, 261-269`
- **Est. LOC saved:** ~8-12
- **Why it's safe:** The staging array is fully assigned before publication;
  normal restore runs only after `markerPublished`, and recovery rejects
  invalid or empty markers before calling `Restore`. The nullable collection
  shape therefore adds filters and null-forgiving operators without representing
  a valid published state.
- **Proposed change:** Change `Restore` and `Cleanup` to
  `IEnumerable<TransactionEntry>`, remove null filters/null-forgiving operators,
  and preserve the existing publication and marker validation order.
- **Confidence:** Medium-high; compiler nullable-flow behavior for the local
  staging array should be checked.
- **Validation needed:** Run the corpus rollback and interrupted-batch tests,
  the targeted `SharpProof.Gates.Test` suite, the affected build, and inspect
  nullable warnings.

### 4. Collapse the root README's duplicate documentation navigation

- **Files:** `README.md:212-231`; duplicate destinations in
  `docs/README.md:6-20`
- **Est. LOC saved:** ~13
- **Why it's safe:** The root README already directs readers to the documentation
  map, then repeats six links that the map owns. Repository search found no
  machine consumer of this list; the README verifier checks links and anchors,
  not the list's presence.
- **Proposed change:** Replace the repeated list with one sentence linking to
  `docs/README.md`, retaining surrounding support-boundary and policy text.
- **Confidence:** High.
- **Validation needed:** Run the README/documentation link verifier and confirm
  all six destinations remain reachable from `docs/README.md`.

### 5. Remove duplicated sample-matrix instructions from getting started

- **Files:** `docs/getting-started.md:121-134`; authoritative detail in
  `samples/README.md:18-34`
- **Est. LOC saved:** ~14 (or ~8 if a short link remains)
- **Why it's safe:** Both sections describe the same packaged sample matrix and
  validation behavior, while the dedicated sample guide also contains the
  release-candidate `-PackageSource` mode and fixture runner.
- **Proposed change:** Keep the authoritative command and behavior description
  in `samples/README.md`; reduce the getting-started section to a short link.
- **Confidence:** Medium; the differing `tooling samples` versus
  `tooling dev -lc` forms may encode an intentional wrapper distinction.
- **Validation needed:** Confirm the supported public invocation, then run the
  documentation link/check workflow and the sample command documented as
  authoritative.

### 6. Remove repeated fixed-baseline prose from the usefulness audit

- **Files:** `docs/code-usefulness-audit.md:14-18,34-45`; ledger begins at `:187`
- **Est. LOC saved:** ~20
- **Why it's safe:** The opening scope and fixed-baseline table repeat tracked
  file counts and line metrics already represented by the ledger and metrics
  sections. The ledger remains the line-level historical evidence.
- **Proposed change:** Reduce the baseline table to the audit date/commit and a
  pointer to the ledger/metrics, preserving the exact historical values in the
  remaining evidence section.
- **Confidence:** Medium-high.
- **Validation needed:** Recompute and compare the baseline numbers before the
  edit; verify the audit's internal references and documentation checks.
---

## Round 5 — Repo-wide clone detection (window-hash sweep)

Method: normalized 14-line sliding-window hashing across all 1,058 non-generated `.cs` files, keeping only windows that are fully non-blank and not repetitive filler, then reporting hashes that occur in **more than one file**. This found 32 distinct cross-file duplicate block families. Most confirm findings already recorded above (the process runner, `RepositoryRoot()`, `OperationMayThrow`); the items below are the ones **not** previously reported.

### 1. `AnalyzerConfigOptions` / `AnalyzerConfigOptionsProvider` reimplemented in 8 files
- **Files:** `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:266-302` (`TestOptionsProvider` + `TestOptions`, 37 lines), `SharpProof.Gates/AnalyzerGateHost.cs:241-283` (`GateOptionsProvider` + `GateOptions`, 45 lines), `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:229-259` (`TestAnalyzerConfigOptionsProvider` + `TestAnalyzerConfigOptions`, 31 lines), `SharpProof.Analyzer.Test/AnalyzerConfigurationUnitTests.cs:116` (`DictionaryOptions` + `DictionaryProvider`). Four further implementations exist at `SharpProof.Analyzer.Test/AnalyzerModeAndEffectTests.cs:3754` (`FailingOptionsProvider`), `FinalCompilationCollectorTests.cs:1396` (`TreeOptionsProvider`), `SharpProof.Package.Test/CompilerProbeInputConsistencyTests.cs:115` (`ProbeOptionsProvider`), `CompilerProbeSnapshotTests.cs:185` (`OutputPathOptionsProvider`).
- **Est. LOC saved:** ~90
- **Why it's safe:** The `TestOptions` (`AnalyzerTestHost.cs:287-302`) and `GateOptions` (`AnalyzerGateHost.cs:264-279`) bodies are **character-for-character identical** apart from the class name — same primary constructor `(IReadOnlyDictionary<string, string> values)`, same `TryGetValue` with the same `values.TryGetValue(key, out var found)` / `value = string.Empty; return false` shape. `GeneratorTestHost.cs:249-259` and `AnalyzerConfigurationUnitTests.cs:116` are the same dictionary-backed lookup written two more ways (`values.TryGetValue(key, out value!)`). This is a **production/test boundary crossing**: `SharpProof.Gates` is production code carrying a private copy of what a test host also defines.
- **⚠ Only four of the eight are duplicates.** The other four have deliberately distinct behaviour and must stay: `FailingOptionsProvider` exists to *throw* (it tests failure handling), `TreeOptionsProvider` maps per-syntax-tree options, `ProbeOptionsProvider` and `OutputPathOptionsProvider` return a single fixed value. Do not fold those in.
- **Proposed change:** Put one dictionary-backed `AnalyzerConfigOptions` + provider pair in `eng/testing/` (linked into the test projects, as `DiagnosticDescriptorCatalogAssertions.cs` already is) and a mirror in `SharpProof.Testing` for `SharpProof.Gates`; delete the four plain copies. Keep the four behaviour-specific providers.

### 2. Nine-site fixture-invocation block inside ArchitectureTest
- **Files:** `SharpProof.ArchitectureTest/DevCheckCommandPlanTests.cs:83`, `DocumentationSupportContractTests.cs:34`, `PublicationDestinationAuthorityTests.cs:74` and `:76`, `PublicationPlanIdentityTests.cs:62`, `PublicationPlanTopologyTests.cs:52`, plus 3 more
- **Est. LOC saved:** — (already counted)
- **Why it's noted:** This is the **largest single duplicate family in the repo** by site count (9 files sharing one identical 14-line window, and a second 7-file family overlapping it). It independently confirms, by a completely different method, the ArchitectureTest `PwshFixtures` finding recorded above — that finding's ~400-line estimate is corroborated, not additional. Recorded here only as cross-validation.

### 3. Cross-project duplication between `Effects.Test` and `Specs.Test`
- **Files:** `SharpProof.Effects.Test/EffectAnalysisTests.cs:8332`, `SharpProof.Effects.Test/MetadataApiSpecTypeInitializationTests.cs:63`, `SharpProof.Specs.Test/ApiSpecTests.cs:1061`
- **Est. LOC saved:** ~30
- **Why it's safe:** A 14-line identical block spanning two *different test projects*, so neither project's own deep pass would see it. Both projects already link shared sources from `eng/testing/`, which is the natural home.
- **Proposed change:** Read the three sites and hoist the shared block into `eng/testing/`. Verify first that the Specs copy has not diverged semantically — the window match proves 14 identical lines, not identical intent.

### 4. Three-site duplicate within `SharpProof.Specs.Test`
- **Files:** `SharpProof.Specs.Test/ApiSpecConditionalNullInstantiationTests.cs:90`, `ApiSpecExpressionDepthTests.cs:79`, `ApiSpecInstantiationCoverageTests.cs:606`
- **Est. LOC saved:** ~30
- **Why it's safe:** Identical 14-line arrange window across three fixtures in one project — the classic "helper exists or should" shape seen repeatedly in this repo.
- **Proposed change:** Extract to a fixture-level helper in `SharpProof.Specs.Test`.

### 5. Two-site duplicate within `SharpProof.Effects.Test`
- **Files:** `SharpProof.Effects.Test/ExceptionHandlerReachabilityTests.cs:71` and `:149`, `StaticFieldTypeInitializationTests.cs:216`
- **Est. LOC saved:** ~30
- **Why it's safe:** The same 14-line block appears twice within one file and once in a sibling — an intra-file duplicate is the strongest possible signal that a helper is missing.
- **Proposed change:** Extract one helper used by all three sites.

### 6. `Analyzer.Test` ↔ `Contracts.Test` shared preamble
- **Files:** `SharpProof.Analyzer.Test/ContractApiIdentityAnalyzerTests.cs:13`, `SharpProof.Contracts.Test/ContractBinderTests.cs:1503`
- **Est. LOC saved:** ~20
- **Why it's safe:** Cross-project, so invisible to either project's own pass. Both projects can link from `eng/testing/`.
- **Proposed change:** Hoist after confirming the two copies have not diverged.

> **Method note for future passes.** Window-hashing found real duplication that both identifier-frequency scanning and per-project reading missed, and it is cheap (one pass over 1,058 files). It is also self-limiting: it detects *exact* normalized matches only, so near-duplicates that differ by a renamed variable — the majority of what the reading-based agents found — are invisible to it. The two methods are complementary; neither alone is sufficient.

### 7. Repo-wide `Assert.That(` wrapping — measured, not recommended
- **Files:** 4,327 sites across all test projects. Densest: `EffectAnalysisTests.cs` (425), `WorkerTests.cs` (359), `AnalyzerModeAndEffectTests.cs` (213), `WorkerMsBuildIntegrationTests.cs` (210), `ArchitectureTests.cs` (182), `ClaimManifestBuilderTests.cs` (167), `PackageLayoutSmokeTests.cs` (153)
- **Est. LOC saved:** potentially ~3,000-4,000 — **not counted in this document's total**
- **Why it is recorded but not recommended:** Every one of these is a `Assert.That(` head whose arguments were wrapped onto following lines despite `.editorconfig:13` permitting 140 columns. Rewrapping is behaviour-preserving and would remove several thousand lines, but it is **pure formatting churn**: it improves nothing, produces an unreviewable diff across every test file in the solution, and would conflict with all other work in flight. Recorded so the number is known and nobody rediscovers it as a "win".
- **Proposed change:** If ever done, do it as a mechanical formatter pass in one isolated commit touching nothing else — never by hand, and never mixed with a substantive change.

---

## Deep pass: Analyzer.Test big files (round 5)

**Estimated savings: ~1,225 LOC, all substantive** — no pure-reflow items included in this total.

### 1. Table-drive the 59 single-source / single-assert tests in `RequiresAndControlTests`
- **Files:** `SharpProof.Analyzer.Test/RequiresAndControlTests.cs` — 59 methods (e.g. `:1384, :1412, :1561, :1586, :1624, :1689, :1743, :1890, :1930, :2006, :2558, :2697`, plus 47 more); **1,815 of the file's 3,145 lines sit in these methods**
- **Est. LOC saved:** ~530 (net **~410** beyond the already-proposed `AssertIds` helper, which this subsumes for these sites)
- **Why it's safe:** All 59 bodies are structurally identical modulo three values — the embedded source literal, the mode, and the enabled/expected diagnostic id list. 51 of the 59 use the *exact* same `("contracts", ["SP0027"])` pair; the rest vary only in mode (`null`, `"effects"`, `"all-experimental"`) and id (`SP0002`/`SP0024`/`SP0047`). The single assertion is always `Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo([...]))` or `Is.Empty`. **No test in this set carries a second distinct assertion**, so nothing is dropped. `.SetName(...)` on each `TestCaseData` preserves the current test names verbatim in runner output.
- **Proposed change:** One `[TestCaseSource(nameof(ReplayCases))]` method taking `(string source, string? mode, string[] enabledIds, string[] expectedIds)`; each case becomes a `TestCaseData` whose only non-source lines are the `yield return`, the two `"""` fences, and the args/`SetName` line — 14 scaffold lines down to ~5.

### 2. Shared embedded-fixture builder for the `Fixture`/`Positive` preamble in two more files
- **Files:** `AnalyzerModeAndEffectTests.cs` (45 literals with the `using SharpProof.Attributes;` + blank + `public static class Fixture {` preamble); `RequiresAndControlTests.cs` (31 with that preamble, and 38 that additionally contain the byte-identical 3-line `static void Positive(int value) { Contract.Requires(value > 0); }` — 67 total occurrences at `:1392, :1420, :1455, :1466, :1473`, …)
- **Est. LOC saved:** ~380 (135 + 245)
- **Why it's safe:** Pure text-level factoring of the *fixture source*, not of assertions. The analyzer sees a byte-identical compilation unit as long as the builder emits the same preamble; every diagnostic id, count and message assertion is untouched. **Distinct from the previously-reported `NestedRequiresCallSiteTests.cs` preamble finding** — these are two different files not covered by it.
- **⚠ Caveat:** `AnalyzerModeAndEffectTests.cs` has three tests asserting on `SourceSpan.Start` derived from `source.IndexOf(...)`; those still work because the offset is computed from the built string, but the builder must be applied *before* the `IndexOf`.
- **Proposed change:** Add `private static string Fixture(string body)` and a `FixtureWithPositive(string body)` variant to each fixture class; call sites pass only the varying member text via `$$"""…"""`.

### 3. Extract the repeated `RequiresCallSiteDiscovery` arrange block
- **Files:** `RequiresCallSiteDiscoveryTests.cs` — 17 `new RequiresCallSiteDiscovery(` sites; 17 `compilation.SyntaxTrees.Single()`, 16 `GetSemanticModel(tree)`, 12 `(IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!`. First two at `:19-40` and `:63-80`
- **Est. LOC saved:** ~145
- **Why it's safe:** The block is mechanical construction (tree → declaration → semantic model → caller symbol → `.Get(callerContracts: null)`) and contains **no assertions**. The file already proves the pattern is helper-worthy — it has `GetMethod(...)` at `:1774` for the *symbol* half but nothing for the *discovery* half, so tests re-inline ~11 lines each.
- **Proposed change:** Add `private static ImmutableArray<RequiresCallSiteCandidate>? Discover<TSyntax>(CSharpCompilation, Func<TSyntax,bool>? select = null, ContractSet? callerContracts = null)`, collapsing each 11-line arrange to 1-3 lines. Two sites pass a non-null `callerContracts` and one asserts `HasPotentialCallSite` instead of `.Get` — keep those as an overload rather than forcing them through the helper.

### 4. `AssertRequiresAt(diagnostics, source, params markers)` for the location-assert runs
- **Files:** `NestedRequiresCallSiteTests.cs` — 19 `SourceSpan.Start` assert sites (`:374, :411, :480, :521, :565, :683, :719, :755, :794, :834, :922, :1030, :1034, :1073, :1179, :1368, :1425, :1512, :1659`), 12 of which pair with `source.IndexOf("Positive(-N)", StringComparison.Ordinal)`; 26 tests call `AssertRequiresDiagnostics`
- **Est. LOC saved:** ~130
- **Why it's safe:** At every site the count assertion (`AssertRequiresDiagnostics(diagnostics, N)`) is strictly implied by the location list length, and the location assertion is always "the reported spans equal the offsets of these `Positive(-N)` markers". One helper asserting both the id sequence *and* the ordered/equivalent offsets preserves both checks exactly.
- **⚠ Caveat:** keep `Is.EquivalentTo` vs `Is.EqualTo` as a helper flag — three sites use unordered comparison, and collapsing them to ordered would *strengthen* the assertion and could flake. Pass ordering explicitly.
- **Proposed change:** Add `AssertRequiresAt(...)` beside the existing `AssertRequiresDiagnostics` (`:1778`); replace the 5-8 line assert pairs with one call.

### 5. Message-contains assertion helper
- **Files:** `AnalyzerModeAndEffectTests.cs` (13 indexed + 3 by-id sites: `:208, :231, :258, :282, :285, :310, :313, :459, :463, :467, :1196, :2070`, …), `RequiresAndControlTests.cs` (3), `FinalCompilationCollectorTests.cs:53`
- **Est. LOC saved:** ~40
- **Why it's safe:** Each site is a verbatim 3-line (indexed) or 4-line (`.Single(d => d.Id == "X")`) `Assert.That(….GetMessage(CultureInfo.InvariantCulture), Does.Contain(literal))`. Collapsing keeps the same subject and the same `Does.Contain` constraint. **Leave the ~30 further `GetMessage` calls that bind to a local `var message = …` first** — those legitimately reuse the local.
- **Proposed change:** Two one-line helpers in a shared internal `DiagnosticAssert` static class; mechanical replacement at the 20 sites.

> **Negative results.** **No dead code:** zero `[Ignore]`, `[Explicit]`, or commented-out tests across all six files. `AnalyzerTestHost.EmitImage` (`:190`), `EmitReference` (`:203`) and `FindRepositoryRoot` (`:227`) *look* single-referenced by in-file grep but are `internal` and used from other test files — **do not delete** (another instance of the trap that in-file scanning creates). **No strictly-subsumed tests:** only one duplicated source literal exists in this area (a 6-line `public sealed class Subject` fixture appearing twice in `RequiresCallSiteDiscoveryTests.cs`), and the two tests around it assert different things. The 4 delegate tests at `RequiresCallSiteDiscoveryTests.cs:1075/1110/1152/1182` share a structure but each asserts a distinct target-resolution outcome — merging would save only ~15 lines and obscure four separate behaviours; **skipped deliberately**. `FinalCompilationCollectorTests.cs` is already well-factored (`CollectorWorkspace`, `CreateCompilation`, `AnalyzeCollectorAsync`, `Options`) and already uses `[TestCase]` 37 times.

---

## Deep pass: Package.Test big files (round 5)

**Estimated savings: ~650 LOC** (of 12,677 across the four files), all non-overlapping with the six prior findings for these files.

### 1. Path-addressed JSON assertion helper for `JsonElement` navigation
- **Files:** `PackageLayoutSmokeTests.cs` (40 asserts / 184 lines), `LauncherArgumentTests.cs:1683-1740` and elsewhere (27 asserts / 96 lines), `WorkerMsBuildIntegrationTests.cs` (11 asserts / 36 lines)
- **Est. LOC saved:** ~220
- **Why it's safe:** Measured mechanically: **78 `Assert.That(...)` statements containing `GetProperty(` and spanning ≥3 physical lines, totalling 316 lines.** Each is a pure navigate-then-compare; a helper `AssertJson(root, "runs[0].results[0].level", "error")` preserves the exact expected value and the exact navigation path one-for-one. Every distinct expected value survives; nothing is merged away. Terminal accessor variance (`GetString`/`GetInt32`/`GetBoolean`/`GetArrayLength`) is handled by overloads on the expected argument. Current shape example at `LauncherArgumentTests.cs:1714-1719`.
- **Proposed change:** One shared `JsonAssert.Path(JsonElement root, string path, object expected)` (~25 lines, in a file shared by the three fixtures); rewrite the 78 multi-line asserts as single lines.

### 2. Collapse the three near-identical scratch-worker project factories
- **Files:** `WorkerMsBuildIntegrationTests.cs:3832-3896` (`CreateResultlessWorkerAsync`), `:3897-3943` (`CreateMalformedWorkerAsync`), `:3944-4002` (`CreateMalformedThenHangWorkerAsync`)
- **Est. LOC saved:** ~110
- **Why it's safe:** All three create a subdirectory, write an identical `<Project Sdk="Microsoft.NET.Sdk">` Exe/net8.0 csproj, write a `Program.cs`, run `dotnet build -c Release --nologo /nodeReuse:false`, throw on non-zero exit, and return `bin/Release/net8.0/<Name>.dll`. The only differences are directory name, project name, and the `Program.cs` body. **No assertion exists in any of the three** — they are pure fixtures, so no coverage can be lost.
- **⚠ One-off to preserve:** `CreateMalformedWorkerAsync` carries a `Condition="'$(TargetFrameworks)' == ''"` on `<TargetFramework>`, behaviourally identical here since no `TargetFrameworks` is set — keep it as an optional csproj-fragment parameter for zero risk.
- **Proposed change:** Add `CreateScratchWorkerAsync(string name, string programSource)` (~40 lines); reduce the three methods to a name plus a verbatim `Program.cs` string each.

### 3. `BuildOkAsync` / `BuildFailsAsync` wrappers around the build-then-assert-exit-code pair
- **Files:** `WorkerMsBuildIntegrationTests.cs` (70 occurrences of `Assert.That(<x>.ExitCode, Is.Zero, <x>.Output);`), `PackageLayoutSmokeTests.cs` (35 occurrences)
- **Est. LOC saved:** ~80
- **Why it's safe:** All 105 sites are the same single-line assertion immediately after a `BuildAsync`/`RestoreAsync`/`RunDotNetAsync` call, each passing the process output as the NUnit failure message. Wrapping keeps both the exit-code check and the output-as-message diagnostic verbatim. Sites asserting `Is.Not.Zero` and then checking output text are a separate, smaller family — leave those or give them their own wrapper.
- **Proposed change:** Add `BuildOkAsync(...)` to `ConsumerProject` (and the equivalent on `PackageWorkspace`) performing the assert internally and returning the result; delete the 105 standalone assert lines.

### 4. Argument-array builder for the launcher `verify` command line
- **Files:** `LauncherArgumentTests.cs` — 18 `string[] arguments = [` literals, 15 carrying the full `"verify" / --worker / --request / --result / --compiler-manifest / --verify-policy "advisory" / --assumption-policy "allow"` spine (`:471-481, :522-532, :554-564, :577-587, :605-620`)
- **Est. LOC saved:** ~90
- **Why it's safe:** The 15 blocks are ~11 lines each (~165 total) and differ only in which of the five path slots is overridden; the two policy flags are byte-identical in all 15. A builder with those as defaults and named optional overrides reproduces the exact same `string[]` fed to `LauncherArguments.TryParse`, so parser coverage is unchanged. **Distinct from the already-reported `RequestProjectionRejects*` grouping** — this applies to the argument literal itself, including in non-rejection tests (`DisabledCachePathDoesNotParticipateInIoTopology:607`, `CompletePublicationAcceptsSarif:297`).
- **Proposed change:** Add `private static string[] VerifyArgs(worker, request, result, manifest, cache, sarif)` with all-optional parameters; replace the 15 literals with one call each.

### 5. Shared tail for the worker-companion alias-rejection tests
- **Files:** `WorkerMsBuildIntegrationTests.cs:2341-2382`, `:2383-2440`, `:2298-2339`
- **Est. LOC saved:** ~40
- **Why it's safe:** Beyond the already-reported 18-line staging preamble, the *tail* is also duplicated: `collisionCompanion = Path.ChangeExtension(collisionWorker, ".deps.json")`, `expectedBytes = await File.ReadAllBytesAsync(...)`, the four-tuple `RunVerificationTargetAsync(...)` call, and an `EnterMultipleScope` block asserting non-zero exit + companion still exists + bytes unchanged. The three tests keep distinct assertions (the symlink test has no message assertion; the hard-link test adds `Does.Contain("aliases a protected file identity")`; the first asserts a different message and file existence rather than bytes) — passing the expected message as an optional parameter preserves all three.
- **Proposed change:** Add `AssertCompanionAliasRejectedAsync(project, collisionWorker, aliasPath, string? expectedMessage)` covering staging + invocation + the shared assertion cluster.

### 6. Local escape helper inside `CreateProjectXml`
- **Files:** `WorkerMsBuildIntegrationTests.cs:4316-4400`
- **Est. LOC saved:** ~35
- **Why it's safe:** Twelve consecutive `SecurityElement.Escape(Path.Combine(repository, …))` initialisations (`props`, `verifierProps`, `analyzerDirectory`, `generatorDirectory`, `collectorDirectory`, `targets`, `verifierTargets`, `worker`, `launcher`, `protocol`, `buildTasks`, `attributes`) each occupy 3-6 wrapped lines. A local `static string Esc(params string[] parts) => SecurityElement.Escape(Path.Combine(parts));` makes each one line producing byte-identical XML. Fixture construction only — no assertions involved.
- **Proposed change:** Introduce the local `Esc` function; rewrite the twelve initialisers as one-liners.

### 7. Consolidate small helpers duplicated across the four fixtures
- **Files:** `WorkerMsBuildIntegrationTests.cs:3544-3560` and `PackageLayoutSmokeTests.cs:1596-1612` (`RequireContainerWorker`, byte-identical bar the message); 21 `new System.Text.UTF8Encoding(false)` argument lines (9 + 12); `CreateSharedCompilationServerId` at `PackageLayoutSmokeTests.cs:2473` duplicated at `FinalCompilationProbeTests.cs:853`
- **Est. LOC saved:** ~45
- **Why it's safe:** `RequireContainerWorker` has identical gating logic (Linux + x64 process + x64 OS + `SHARPPROOF_CONTAINER=1`, then `ContainerContract.ValidateRequired()`), and the skip message text is already identical. A `WriteUtf8(path, text)` helper removes one argument line per call site without changing the BOM-less encoding. `CreateSharedCompilationServerId` differs only in the ID prefix and the `typeof(...)` used for the MVID.
- **Proposed change:** Move `RequireContainerWorker`, `WriteUtf8`, and a parameterised `CreateSharedCompilationServerId` into a shared static `PackageTestEnvironment` class.

### 8. Policy-escalation assertion helper
- **Files:** `WorkerMsBuildIntegrationTests.cs:1363-1392`, `:2775-2811`
- **Est. LOC saved:** ~30
- **Why it's safe:** Both tests repeat "build with one policy property → assert exit code zero/non-zero → assert output contains `info|warning|error SP004x`" four times each. A helper taking (policy name, policy value, expected diagnostic text, expected-failure flag) preserves each distinct expected string.
- **⚠ Keep inline:** the two extra assertions — `Does.Not.Contain("SharpProof verifier failed with exit code")` and `Does.Contain("total=1, user=0, trusted=1")` — must stay as separate explicit lines.
- **Proposed change:** Add `AssertPolicyDiagnosticAsync(project, (string,string) policy, string expected, bool expectFailure)` for the 7 escalation steps; keep the two trailing assertions inline.

> **Measured negatives — do not pursue.**
> **(a) The ArchitectureTest "bare `Does.Contain` run" pattern does NOT replicate here.** This was explicitly tested: the 107 `Does.Contain` calls across the four files are scattered one-or-two per test with different subjects (`build.Output`, `manifest`, `failed.Output`), not in contiguous table-able runs. Table-driving them would cost more than it saves.
> **(b)** No `[Ignore]`, `[Explicit]`, or commented-out tests in any of the four files.
> **(c)** A reflective scan for private members referenced only at their declaration found **zero** dead helpers or unused fields.
> **(d)** No test is strictly subsumed by another. The `SupervisorReadiness*` group (`BuildTaskTests.cs:281-380`, 5 tests) *looks* TestCase-shaped but each supplies a structurally different callback lambda; parameterising would require passing delegates and would not shrink the file.
> **(e)** The `Expected*` string arrays at `PackageLayoutSmokeTests.cs:26-146` (~120 lines) could be composed from prefix + shared filename lists, but that would make the expected package layout **implicit rather than literal** — low confidence, deliberately skipped.
>
> **Reflow-only (excluded from all totals):** across the four files there are **553 multi-line `Assert.That` statements totalling 2,198 lines**, most wrapping purely for an ~80-column habit. Only the 78 counted in finding 1 represent substantive structural duplication; the rest is formatting, not redundancy.

