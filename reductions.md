# Code reduction ledger

This is the canonical working ledger for the reduction pass on
`codex/apply-code-reductions`. The original 288 KB survey is preserved at
`d1c769a:reductions.md` in Git history. Its numbered proposal headings map to
`R001` through `R228` in document order.

The original survey mixed proposals, duplicate measurements, formatting-only
ideas, refuted claims, public API changes, and feature removals. An item is
applied only after its cited evidence is rechecked against the current tree and
the smallest relevant containerized test target passes.

## Status definitions

- `applied`: implemented and tested.
- `pending`: still needs current-tree validation.
- `deferred`: would remove or alter meaningful behavior/API, or is not worth the
  review risk for line-count alone.
- `refuted`: current-tree evidence disproves the proposal.
- `merged`: duplicate of another canonical item; handled with that item.

## Applied

| IDs | Reduction | Validation |
|---|---|---|
| R004 | Use `WorkerExecutionEnvelope` for launcher hard-timeout arithmetic | Package.Test: 298 passed, 1 host skip |
| R005 | Remove the unused three-argument effect-result assembler overload | Worker.Test: 695 passed |
| R006 | Inline worker projection forwarders and unify launcher policy presentation | Worker.Test: 695; launcher tests: 75 passed |
| R010 | Reuse compiler source-location copy/equality and effect-witness equality | Worker.Test: 695 passed |
| R011 | Share manifest-evidence mapping and compiler-method normalization | Worker.Test: 695 passed |
| R012 | Share replay witness identity selection; keep reset blocks where an out-parameter helper would add lines | EffectCounterexampleReplayTests: 31 passed |
| R014 | Use a primary constructor for the public summary-signature carrier; retain internal constructors on the other public types | Summaries.Test: 14 passed |
| R015 | Share the duplicate operation reference comparer within Frontend; retain the Specs-local comparer to avoid a new public cross-assembly API | Frontend.Test: 108 passed |
| R016 | Reuse the canonical meta-analyzer type-identity comparison; skip alias-only formatting churn | Meta.Analyzers.Test: 163 passed |
| R017 | Merge generic `Result`/`Old` shape validation; retain the null wrapper required by `ConcurrentDictionary` | Frontend.Test: 108 passed |
| R018 | Share `ConfigureAwait` unwrapping with optional awaited-type validation | Meta.Analyzers.Test: 163 passed |
| R021 | Reuse canonical IR child traversal in the SMT depth validator | Smt.Test: 30 passed |
| R025 | Inline private SMT forwarders and use readonly encoded-value structs | Smt.Test: 30 passed |
| R026, R150, R200 | Replace repository-root helpers/messages with `TestRepository.FindRoot` | `test-changed`: all 18 projects and 36 package shards passed |
| R046 | Inline the one-call analyzer diagnostic placement forwarder | Analyzer.Test: 476 passed |
| R048, R106 | Compact generated get-only properties at their template sources | Both generators passed `-Verify`; `test-changed` passed all 18 projects and 36 package shards |
| R050 | Reuse canonical IR child traversal in the differential oracle while preserving opaque/unknown rejection | Testing.Test: 13 passed |
| R051 | Collapse internal resolver overloads with default cancellation tokens; retain public and cached method-group paths | Contracts.Test: 142 passed |
| R053 | Table-drive deterministic IR operator selection | Testing.Test: 13 passed |
| R063 | Hoist shared test-project properties and package references | `test-changed`: all 18 projects and 36 package shards passed |
| R064, R158 | Remove unused per-command Compose service aliases; retain `dev`, `loop`, and `tooling` | Compose config, container authority, and 25 build-scheduling tests passed |
| R035 | Centralize single-type potential-exception construction | Effects.Test: 323 passed |
| R036 | Share canonical corpus snapshot line parsing between validation and loading | CorpusGateTests: 23 passed |
| R037 | Share MSBuild default-property lookup | PerformanceGateTests: 29 passed |
| R042 | Reuse reachable-operation enumeration for anonymous functions | Analyzer.Test: 476 passed |
| R043 | Share selected-analysis-incomplete reporting and subset descriptions | Analyzer.Test: 476 passed |
| R044 | Reuse the contract-companion diagnostic factory; retain the non-record carrier for netstandard2.0 compatibility | Analyzer.Test: 476 passed |
| R045 | Share the single-primary-constructor match tail | Analyzer.Test: 476 passed |
| R086 | Inline the one-call deconstruction forwarding overload | Meta.Analyzers.Test: 163 passed |
| R113 | Remove dead package-consumer script locals, cleanup branch, and parameter plumbing | PowerShell parse plus targeted Architecture test passed |
| R130 | Remove the single-arm container entrypoint `case` | `bash -n` plus targeted Architecture test passed |
| R135 | Remove two uncalled `AnalyzerGateHost` members | Gates.Test: 63 passed |
| R139 | Share the package-build pair runner while preserving execution order | PerformanceGateTests: 29 passed |
| R140 | Replace four corpus-name switches with one tuple switch | CorpusGateTests: 23 passed |
| R143 | Reuse the canonical corpus directory helper | CorpusGateTests: 23 passed |
| R145 | Reuse one MSBuild-list splitter | PerformanceGateTests: 29 passed |
| R146 | Remove positivity checks already guaranteed by contract loading | PerformanceGateTests: 29 passed |
| R147 | Reuse `CountSourceFiles` inside its own catalog | CorpusGateTests: 23 passed |
| R157 | Remove the analyzer suppression scoped to deleted `ApiSpecModel.cs` | Specs.Test: 82 passed |
| R159 | Remove the default-timeout process-runner overload | Gates.Test: 63 passed |
| R108 | Keep generated constructor declarations on one line when they fit | Both generators passed `-Verify`; Ir.Test: 114; Contracts.Test: 142 |
| R115 | Reuse iterative IR variable collection in both SMT fuzzers while preserving deterministic order | Fuzz.Test: 39 passed |
| R116 | Share generated-expression leaf construction and compact its carrier properties | Fuzz.Test: 39 passed |
| R192 | Remove unused `GhostProbe.TouchObject` | Specs.Test: 82 passed |
| R199 | Remove the duplicate undefined-operation check | Frontend.Test: 108 passed |
| R228 | Remove exception catches subsumed by `IOException` | Gates.Test: 63 passed |
| R055 | Share analyzer diagnostic-ID assertions | Analyzer.Test: 476 passed |
| R056 | Share Effects `Sample` method lookup and analysis | Effects.Test: 323 passed |
| R059 | Share protocol error-code assertions | ProtocolJsonTests: 108 passed |
| R062 | Merge the duplicate protocol scaling tests | ProtocolJsonTests: 108 passed |
| R075 | Table-drive mutation shard timing fields; retain the uniqueness preflight because its helper is declared after the preflight executes | PowerShell parse; mutation Architecture tests: 12 passed |
| R101 | Share lowercase SHA-256 encoding | Generator verification and affected project tests passed |
| R148 | Table-drive repeated acceptance contract assertions | Architecture.Test: 516 passed |
| R151, R153 | Share architecture project-graph helpers and validate the production catalog | Architecture.Test: 516 passed |
| R152 | Share architecture workflow enumeration | Focused Architecture tests: 11 passed |
| R154 | Share and compact qualification receipt fixtures | Focused qualification tests passed |
| R164 | Share dictionary-backed analyzer configuration options | Analyzer.Test: 476; ContractForGenerator.Test: 121; Gates.Test: 63 |
| R166-R168, R209 | Share neutral API-spec facet fixtures across Effects and Specs | Effects.Test: 323; Specs.Test: 82 |
| R186-R190 | Table-drive contract witnesses, exact types, postconditions, and facet filtering | Specs.Test: 82 passed |
| R191 | Unify runtime allocation and throw edges | Specs.Test: 82 passed |
| R193 | Share Contracts test compilation and diagnostic validation | Contracts.Test: 142 passed |
| R195 | Table-drive contract default-value exactness | ContractForGenerator.Test: 121 passed |
| R196 | Remove unused protocol root metadata | Generator verification; Worker.Test: 695 passed |
| R198 | Remove unread generator-run compilations | ContractForGenerator.Test: 121 passed |
| R206 | Merge constant-loop completion fixtures | Focused Effects tests: 8 passed |
| R207 | Share operation-completion test setup | Effects.Test: 323 passed |
| R208 | Share exception-handler reachability fixtures | Focused Effects tests: 2 passed |
| R210 | Table-drive array completion cases | Focused Effects tests: 2 passed |
| R212 | Share specification-term parse context | Worker.Test: 695 passed |
| R214 | Share replay-variable projection | Worker.Test: 695 passed |
| R219 | Table-drive default and unary lowering cases | Frontend.Test: 121 passed |
| R220 | Cache flattened lowered-program instructions | Frontend.Test: 121 passed |
| R222 | Share generated-domain lattice and havoc property tests | Dataflow.Test: 50 passed |
| R225 | Table-drive invalid oracle bindings | Testing.Test: 13 passed |
| R226 | Table-drive undefined proof goals | Verify.Test: 14 passed |
| R230 | Route source-tree verifier package checks directly through consumer configuration validation | Package.Test: 2 passed |
| R231 | Remove no-op empty `SharpProofSpecificationPacks` assignments from source-tree, package, and self-application props | Package.Test and Analyzer.Test: 1 passed each |
| R232 | Remove the deleted `PortableAnalyzer` project from production-project classification while retaining absence regression guards | ArchitectureTest: 1 passed |
| R234 | Remove redundant implicit-usings declarations and the empty Attributes global-usings file | Changed-project tests: 2,567 total; focused Architecture preview: 1 passed; Attributes.Test: 11 passed; release fixture: 1 passed |
| R235 | Replace the no-op generator's redundant global-usings file with its one required Roslyn using | ContractForGenerator.Test: 121 passed |
| R236 | Consolidate repeated editorconfig analyzer suppressions with scoped brace globs | BuildTasks and Worker builds; Worker.Test: 695 passed |
| R237 | Generate isolated coverage selectors from the shared managed runsettings template and remove the inventory's obsolete Gates-file exception | Focused Architecture coverage and production-inventory tests: 5 passed |
| R238 | Share Compose environment entries and cache-volume aliases across the common, dev, and loop services | `docker compose config --quiet`; ContainerAuthorityScriptTests: 15 passed |
| R239 | Replace four build-profile configuration switch arms with one forced-configuration table | PowerShell parse; DependencyAutomationTests: 8 passed |
| R241 | Remove ownership commands redundant with GNU `install -d -o -g` in the Linux container entrypoint | Container-local ownership check; bash syntax; ContainerSourceCleanlinessTests: 39 passed |
| R242 | Remove the stale four-space indentation from the top-level container task-workspace block | bash syntax; ContainerSourceCleanlinessTests: 39 passed |
| R243 | Resolve the installed container-contract marker through `SHARPPROOF_CONTAINER_CONTRACT` in runtime and build-entry checks | Compose config; bash and PowerShell parses; Architecture: 16; Worker ContainerContractTests: 5 |
| R244 | Fold the single-consumer Dockerfile `dev` stage into the final `toolchain` stage | `docker compose config --quiet`; ContainerAuthorityScriptTests: 15 passed; tooling image rebuilt successfully |
| R245 | Remove duplicate Dev Container environment overrides and the empty port-forward list | DevContainerIsNonRootPinnedAndDoesNotNestDocker: 1 passed; JSON parse |
| R246 | Route CI, nightly, coverage, and reusable security image setup through the existing qualified-packages action with downloads disabled | DependencyAutomationTests: 8 passed |
| R248 | Remove the generic, superseded `.cursorrules` file | `test-changed`: ArchitectureTest 389 passed |
| R249 | Consolidate the eight generator-local C# string-literal escapers into `GeneratedFileHelpers.ps1` | All eight generator `-Verify` checks; `test-changed`: ArchitectureTest 389 and package shards passed |
| R250 | Consolidate the exact shared generator validators in `GeneratedFileHelpers.ps1`, retaining schema-specific identifier and type grammars locally | All generator `-Verify` checks; `test-changed`: ArchitectureTest 389 and package shards passed |
| R251 | Share repository-root discovery and default-path normalization across the 14 file-writing generators | All generator `-Verify` checks; `test-changed`: ArchitectureTest 389 and package shards passed |
| R252 | Normalize generated C# headers to the paired `// <auto-generated>` and `// </auto-generated>` form | CSharpScalar, Contract API, and IR generators plus generated outputs; `test-changed`: 14 project suites and 36 package shards passed |
| R253 | Rename the documentation validator to `Test-SharpProofReadme.ps1` and remove its output-only `-Verify` switch | Documentation validator; DocumentationSupportContractTests: 23; ContainedPathAuthorityTests: 1; `test-changed`: ArchitectureTest 389 and 36 package shards passed |
| R266 | Remove the undefined `SHARPPROOF_PORTABLE_ARGUMENT_GUARD` preprocessor term | Ir.Test: 114; Dataflow.Test: 50; Smt.Test: 30 |
| R267 | Forward the duplicate `ArgumentNullGuard` `int` overloads to their `long` implementations | Ir.Test: 114; Dataflow.Test: 50; Smt.Test: 30 |
| R268 | Consolidate residual generator schema-reading helpers in `GeneratedFileHelpers.ps1`, retaining compatibility wrappers and schema-specific validators | Five generator `-Verify` checks; `test-changed`: ArchitectureTest 389 and 36 package shards passed |
| R269 | Centralize the dotnet-wrapper path and checked external-command invocation in `SharpProof.ContainerExecution.psm1`, while retaining the existing timeout, argument, and static-graph behavior | ArchitectureTest: 389 passed; `test-changed`: ArchitectureTest 389 and 36 package shards passed |
| R271 | Reuse `Test-SharpProofReleaseVersionSyntax` from the publication-plan identity module instead of carrying a duplicate SemVer predicate | Publication-plan identity fixtures (`version-syntax`); release/publication tests passed |
| R289 | Replace the private ordinal string-sequence helper with framework `SequenceEqual` | `Test-SharpProofMutationEvidence.ps1`: behavioral fixtures passed |
| R297 | Reuse the shared `DictionaryAnalyzerConfigOptions` in `FinalCompilationCollectorTests` and remove its duplicate private options class | `SharpProof.Analyzer.Test`: 476 passed |
| R317 | Correct the active bug/status figures and include both documents in the maintained-document gate | `Test-SharpProofReadme.ps1` passed |
| R324 | Centralize the two repeated `AttributeTargets` masks used by the eight public attributes | `SharpProof.Attributes.Test`: 11; `SharpProof.Package.Test`: 295 passed, 1 skipped |
| R322 | Reuse compiler-artifact specification-pack identity validation in the manifest producer | `SharpProof.Worker.Test`: CompilerManifestArtifactTests, 91 passed |
| R314 | Centralize compiler summary-origin wire prefixes across artifact, worker, and collector code | `SharpProof.Worker.Test`: WorkerTests, 148 passed; `SharpProof.Analyzer.Test`: FinalCompilationCollectorTests, 55 passed |
| R315 | Share effect-contract violation classification between response authority and counterexample replay | `SharpProof.Worker.Test`: EffectCounterexampleReplayTests, 31 passed; CompilerManifestArtifactTests, 91 passed |
| R812 | Compare classified lowered calls with the existing indexed call set instead of recounting the IR | `SharpProof.Worker.Test`: CompilerCallableLowererTests, 20 passed |
| R816 | Restore the security solution once, then run audit and build with `--no-restore` | `SharpProof.ArchitectureTest`: DependencyAutomationTests, 8 passed |
| R817 | Reuse the container restore for the final package-consumer test invocation | `SharpProof.ArchitectureTest`: ContainerPackageConsumersRestoreBeforeBuildingOfflineFeed, 1 passed |
| R819 | Reuse the canonical pilot-project path set instead of rescanning relative paths for uniqueness | `SharpProof.ArchitectureTest`: PilotAuthorityTests passed |
| R820 | Remove two outcome-family assertions implied by exact concrete outcome checks | `SharpProof.Verify.Test`: ProofKernelTests, 14 passed |
| R821 | Reuse the already-computed generated-domain join in lattice-law assertions | `SharpProof.Dataflow.Test`: 50 passed |
| R822 | Seal replay fixtures only at validation boundaries instead of during construction and again after mutation | `SharpProof.Worker.Test`: CompilerEffectReplayArtifactCodecTests, 8 passed |
| R823 | Share the JSON-document/reflection invocation wrapper across specification-pack parser tests | `SharpProof.Worker.Test`: CompilerSpecificationPackProviderTests passed |
| R824 | Share probe JSON array framing between raw-row and string-array serialization | `SharpProof.Package.Test`: CompilerProbeSnapshotTests (5), CompilerProbeInputConsistencyTests (1) passed |
| R825 | Rename the empty-tree validation predicate to describe its non-empty fast path and zero-length representation check | `SharpProof.Worker.Test`: CompilerManifestArtifactTests passed |
| R826 | Share scoped identifier hash mixing between IR and specification identifiers | `SharpProof.Ir.Test`: 114; `SharpProof.Specs.Test`: identifier and API-spec suites passed |
| R827 | Remove the internal callable-replay overload that re-filters clauses; pass prepared ensures lists from test fixtures | `SharpProof.Worker.Test`: CallableCounterexampleReplayerTests 15; WorkerTcbEdgeCaseTests 44 |
| R830 | Remove the unused launcher assumption total local | `SharpProof.Package.Test`: launcher argument and validation tests passed |
| R831 | Use logical boolean operators for launcher policy predicates | `SharpProof.Package.Test`: LauncherArgumentTests 75 passed |
| R368 | Inline cacheability type checks and remove the unused `OutcomeCachePolicy` wrapper | `SharpProof.Verify.Test`: ProofKernelTests, 14 passed |
| R369 | Move Boolean IR-term validation into the owning `IrFactory` and remove `FactoryGuards` | `SharpProof.Verify.Test`: ProofKernelTests, 14 passed |
| R370 | Use Roslyn `LanguageVersionFacts.TryParse` for evaluated C# language versions | `SharpProof.ArchitectureTest`: Production inventory authority checks passed; parser exercised by production-complexity inventory |
| R402 | Reuse the owning `IrFactory` Boolean-term guard across semantic-term and program-builder validation, retaining their diagnostics | `SharpProof.Ir.Test`: 114 passed |
| R327 | Remove 22 project-local `TreatWarningsAsErrors` declarations now supplied by the central production policy, retaining the two excluded-project declarations | `test-changed`: 2,846 tests passed; 36 package shards passed with 1 expected unsupported-host skip |
| R328 | Collapse the repeated compiler-visible property declarations into semicolon lists at each build entry point, preserving standalone analyzer-consumer behavior and the closed portable/verifier package policy boundaries | `test-changed`: 2,857 tests passed; 36 package shards passed with 1 expected unsupported-host skip |
| R329 | Share verifier path resolution between initialization and cleanup through `_SharpProofResolveVerificationPaths`, retaining the distinct cleanup properties and target ordering | `SharpProof.Package.Test`: 5 targeted multi-target, cleanup, and SARIF tests passed |
| R330 | Centralize Linux process-control ABI constants for `PR_SET_PDEATHSIG`, `pidfd_open`, and `pidfd_send_signal` in the host assembly while retaining separate native wrappers | `SharpProof.Package.Test`: 141 BuildTask, supervisor, and launcher tests passed |
| R332 | Remove explicit `GeneratePackageOnBuild=false` declarations from the three package projects because the SDK default is already false | `SharpProof.Package.Test`: package/build integration tests passed; canonical MSBuild evaluation confirms `false` |
| R333 | Reuse one class-level valid supervisor nonce fixture across the five BuildTaskTests methods that exercise it | `SharpProof.Package.Test`: 141 BuildTask, supervisor, and launcher tests passed |
| R334 | Reuse one class-level schema-3 corpus snapshot header fixture across the corpus format tests | `SharpProof.Gates.Test`: 23 corpus gate tests passed |
| R335 | Reuse one class-level valid input-hash fixture across the three LauncherArgumentTests methods that exercise it | `SharpProof.Package.Test`: LauncherArgumentTests passed |
| R336 | Share the verification-cache filename suffix between production path generation/validation and worker edge-case fixtures | `SharpProof.Worker.Test`: WorkerTcbEdgeCaseTests 44 passed |
| R339 | Derive `EnforceExtendedAnalyzerRules=true` centrally for every `IsRoslynAnalyzer` project | Analyzer and generator builds; canonical MSBuild property evaluation passed |
| R340 | Replace the duplicate fuzz oracle enum with an assembly-wide alias to `SharpProof.Testing.DifferentialStatus` | Fuzz project build; existing Testing differential tests passed |
| R345 | Inline the two single-call `DefiniteOperationFacts.IsDefinitelyString` forwarders in `SharpProof.Analyzer.Core` | `SharpProof.Analyzer.Test`: 149 focused requires/effect tests passed |
| R346 | Reuse the canonical linked `HashEncoding` implementation for protocol SHA-256 formatting while retaining `WorkerProtocolJson.ComputeSha256` | `SharpProof.Worker.Test`: ProtocolJsonTests 108 passed |
| R347 | Merge the package-consumer, pilot, and release-plan switch arms in `build.ps1` while retaining package-source validation and dispatch | PowerShell parse; dependency/build scheduling tests passed |
| R348 | Fold `performance-smoke` into the shared `SharpProof.Gates` restore/run switch arm | PowerShell parse; build scheduling tests passed |
| R349 | Hook consumer configuration validation directly before `CoreCompile` and remove the empty analyzer-consumer shim target | `SharpProof.ArchitectureTest`: build scheduling and consumer configuration tests passed |
| R352 | Centralize tuple-aware C# type-name validation in `GeneratedFileHelpers.ps1` and remove three generator-local copies | Three generator `-Verify` checks; canonical `SharpProof.sln` build succeeded |
| R358 | Reuse the linked IR UTF-16 well-formedness scan in protocol JSON validation while keeping protocol-specific error behavior | `SharpProof.Worker.Test`: ProtocolJsonTests 108 passed; canonical solution build succeeded |
| R359 | Share lowercase SHA-256 validation through the canonical hash helper used by IR, summaries, and protocol serialization | `SharpProof.Worker.Test`: ProtocolJsonTests 108; `SharpProof.Summaries.Test`: 14 passed |
| R360 | Make `FrameworkTypeMetadataNames.Monitor` a compile-time constant like its sibling metadata identities | Canonical solution build succeeded |
| R371 | Remove redundant PowerShell 7 compression assembly loads from pilot package authority scripts | Pilot authority fixtures passed |
| R356 | Centralize the shared libc `close` and `syscall` imports for the verifier build tasks while retaining task-specific native calls | `SharpProof.Package.Test`: 141 build-task, supervisor, and launcher tests passed |
| R373 | Share compiler-probe option and path normalization helpers between generator and snapshot implementations | `SharpProof.Package.Test`: six compiler-probe tests passed |
| R461 | Replace the interval modular-distance branch ladder with the existing `Normalize` helper | `SharpProof.Dataflow.Test`: 50 passed |
| R462 | Remove the shadowed `modulus.IsOne` boundary normalization branch from `IntervalDomain.Create` | `SharpProof.Dataflow.Test`: 50 passed |
| R376 | Avoid re-sorting adjacency lists that are already ordered by the graph's canonical edge sort | `SharpProof.Dataflow.Test`: 50 passed |
| R381 | Reuse `CompilerCallableArtifactReasonCatalog` from the collector instead of emitting a duplicate generated catalog | `SharpProof.Analyzer.Test`: FinalCompilationCollectorTests 55 passed; generator verification passed |
| R384 | Reuse `HashEncoding.ToLowerHex` for compiler checksum bytes instead of manual nibble formatting | `SharpProof.Worker.Test`: CompilerManifestArtifactTests 91 passed |
| R382 | Delegate compiler effect replay source-tree uniqueness to `CompilerSourceLocationAuthority.FindUniqueTree` | `SharpProof.Worker.Test`: source-location and effect-replay tests 44 passed |
| R383 | Share the bounded stream-to-byte-array reader between runtime-component and compiler-manifest paths | `SharpProof.Worker.Test`: compiler manifest/replay tests 99 passed |
| R392 | Reuse one `DefiniteOperationFacts` instance for completion and static-initialization checks | `SharpProof.Effects.Test`: operation-completion/static-initialization tests 7 passed |
| R389 | Share harmless parenthesized/implicit-conversion unwrapping and reuse the disposal nullness helper | `SharpProof.Effects.Test`: null, disposal, and completion tests 67 passed |
| R388 | Share captured-local region classification across conversion ownership paths | `SharpProof.Effects.Test`: focused effects tests passed |
| R391 | Add non-allocating two- and three-summary overloads for high-frequency effect joins | `SharpProof.Effects.Test`: 323 passed |
| R390 | Use foreach and one sort-boundary cancellation check in effect call-graph ordering | `SharpProof.Effects.Test`: EffectCallGraph cancellation tests 2 passed |
| R387 | Share member-initializer syntax-to-operation extraction across effect scanning and completion checks | `SharpProof.Effects.Test`: 323 passed |
| R400 | Call the generated IR operator catalog directly instead of pass-through service wrappers | `SharpProof.Ir.Test`: 114; `SharpProof.Contracts.Test`: 142 |
| R403 | Remove the single-call contract expression binding forwarder | `SharpProof.Contracts.Test`: 142 passed |
| R405 | Share method signature type registration in contract canonicalization | `SharpProof.Contracts.Test`: 142 passed |
| R406 | Reuse the validated primary-constructor factory argument for IR term interpretation | `SharpProof.Ir.Test`: 114 passed |
| R407 | Remove the duplicate launcher exit-code consistency check | `SharpProof.Package.Test`: LauncherArgumentTests 75 passed |
| R411 | Inline the one-use launcher final-timeout arithmetic | `SharpProof.Package.Test`: LauncherArgumentTests 75 passed |
| R401 | Fold direct contract-clause fallback branches and inline the private resolution constructor wrapper | `SharpProof.Contracts.Test`: 142 passed |
| R404 | Share partial-property accessor selection across contract inventory paths | `SharpProof.Contracts.Test`: 142 passed |
| R423 | Reuse the release-version authority helper across container, pilot, and documentation scripts | Architecture release/documentation tests: 30 passed |
| R433 | Remove the unreferenced primary-constructor declaration predicate | `SharpProof.Analyzer.Test`: 476 passed |
| R434 | Remove the redundant method-symbol generated-code forwarding overload | `SharpProof.Analyzer.Test`: 476 passed |
| R435 | Share exact namespace matching between Meta analyzer rule families | `SharpProof.Meta.Analyzers.Test`: 163 passed |
| R436 | Share operation unwrapping between Meta analyzer rule families | `SharpProof.Meta.Analyzers.Test`: 163 passed |
| R438 | Inline the contract validation diagnostic factory wrapper | `SharpProof.Analyzer.Test`: 476 passed |
| R441 | Skip generated files before reading them and derive complexity line counts from the loaded source | Architecture complexity gate tests passed |
| R442 | Remove the unreachable second coverage-module identity check | `SharpProof.ArchitectureTest`: coverage tests passed |
| R445 | Share XML writer settings and disposal through one coverage save helper | PowerShell parse; `SharpProof.ArchitectureTest`: coverage tests passed |
| R456 | Let the SDK derive `PackageVersion` from the authoritative `Version` property | `SharpProof.ArchitectureTest`: release/package tests 73 passed |
| R460 | Combine equivalent unconstrained interval-format switch arms | `SharpProof.Dataflow.Test`: 50 passed |
| R464 | Share assembly-metadata value extraction between contract identity readers | `SharpProof.Frontend.Test`: 121 passed |
| R466 | Parameterize the uncached contract-binding wrapper while retaining separate caches | `SharpProof.Contracts.Test`: 142 passed |
| R467 | Share symbol/type documentation-ID fallback handling | `SharpProof.Frontend.Test`: 121 passed |
| R468 | Centralize the shared Roslyn runtime copy target for Gates projects | `SharpProof.Gates.Test`: 63; `SharpProof.ArchitectureTest`: 389 |
| R472 | Derive the effect-capability unknown marker from the enum catalog expression | `SharpProof.Effects.Test`: 323 passed |
| R471 | Inherit effect-summary equivalence from the closed-domain base while retaining Widen forwarding | `SharpProof.Effects.Test`: 323 passed |
| R473 | Share method/property/type/assembly scope enumeration across analyzer, collector, and effects policies | `SharpProof.Analyzer.Test`: 476; `SharpProof.Effects.Test`: 323 |
| R475 | Share direct by-value call admission checks between spec and summary lowering | `SharpProof.Worker.Test`: 23 focused compiler-call tests |
| R478 | Share bounded JSON file opening between hashing and UTF-8 readers | `SharpProof.Worker.Test`: ProtocolJsonTests 108; full Worker.Test 695 |
| R480 | Update the container contract gate to assert the environment-based marker path | `SharpProof.ArchitectureTest`: ContainerAuthorityScriptTests 15 |
| R484 | Share the canonical path-within-directory comparison used by publication and mount checks | `SharpProof.Worker.Test`: LinuxPublicationSetTests 34 |
| R485 | Share initial publication-path filtering and topology validation | `SharpProof.Worker.Test`: LinuxPublicationSetTests 34 |
| R498 | Reuse the built-in string-concatenation predicate in the binary effect resolver | `SharpProof.Effects.Test`: StringConcatenation tests 3 passed |
| R503 | Remove redundant and inert `.gitignore` negation rules | `git check-ignore`: 4 probes and 940 tracked paths passed |
| R555 | Remove the unread release-authority path local | `SharpProof.ArchitectureTest`: ReleaseConfigurationScript 1 passed |
| R556 | Remove the misleading release-configuration set-membership forwarder | `SharpProof.ArchitectureTest`: ReleaseConfigurationScript 1 passed |
| R570 | Remove the unused Docker Compose version probe from the build entry point | `SharpProof.ArchitectureTest`: LocalProfilesMatchTheWorkflowCommands 1 passed |
| R559 | Share the loop command's relative-path safety guard | `SharpProof.ArchitectureTest`: ContainerSourceCleanlinessTests 39 passed |
| R554 | Remove the unreferenced package-license graph helper | `SharpProof.ArchitectureTest`: PackageDependencyAuthority 45 passed |
| R548 | Share the deterministic differential integer boundary corpus | `SharpProof.Fuzz.Test`: FuzzRunnerTests 32 passed |
| R547 | Parameterize implicit-conversion unwrapping for reference-only callers | `SharpProof.Frontend.Test`: 121 passed |
| R501 | Resolve closed-contract attribute symbols once per analyzer compilation | `SharpProof.Analyzer.Test`: 476 passed |
| R499 | Share unsupported-value abstention classification in the Roslyn lowerer | `SharpProof.Frontend.Test`: 121 passed |
| R500 | Reuse the supported-unknown count during corpus outcome validation | `SharpProof.Gates.Test`: CorpusGateTests 23 passed |
| R504 | Express container script modes with `COPY --chmod` | `SharpProof.ArchitectureTest`: ContainerAuthorityScriptTests 15; `docker compose build tooling` passed |
| R505 | Keep ignored `nupkgs/` inputs in the persistent loop snapshot and workspace | `SharpProof.ArchitectureTest`: HostLoopSnapshotAvoidsBindMountGitDiffScanning 1 passed; shell/PowerShell parses passed |
| R581 | Reject null non-pack summary evidence identities without dereferencing them | `SharpProof.Worker.Test`: CompilerManifestArtifactTests 91 passed |
| R582 | Fold user-assumption collection into the proven-core validation pass | `SharpProof.Worker.Test`: WorkerTcbEdgeCaseTests 44 passed |
| R583 | Remove unreachable contradictory-precondition branches after early return | `SharpProof.Worker.Test`: 695 passed |
| R584 | Reuse protocol SHA-256 formatting in valid test fixtures | `SharpProof.Worker.Test`: 695; `SharpProof.Package.Test`: 75 passed, 1 expected skip |
| R576 | Centralize package integration verification-target MSBuild arguments | `SharpProof.Package.Test`: WorkerMsBuildIntegrationTests 75 passed, 1 expected skip |
| R509 | Share callable assumption-evidence projection | `SharpProof.Worker.Test`: 695 passed |
| R515 | Reuse canonical IR child enumeration in charged summary walks | `SharpProof.Summaries.Test`: 14 passed |
| R517 | Share required-reason validation across public attributes | `SharpProof.Attributes.Test`: 11 passed |
| R447 | Share finite-domain formula validation between oracle entry points | `SharpProof.Fuzz.Test`: 39 passed |
| R454 | Cache the generated-code decision during analyzer method-attribute validation | `SharpProof.Analyzer.Test`: 476 passed |
| R457 | Reuse one symbol-attribute snapshot during control-attribute validation | `SharpProof.Analyzer.Test`: 476 passed |
| R419 | Remove the redundant `BackendCheckResult` forwarding constructor | `SharpProof.Verify.Test`: 14 passed |
| R425 | Centralize the diagnostic descriptor assertion test link | `SharpProof.Analyzer.Test`: 476; `SharpProof.ContractForGenerator.Test`: 121; `SharpProof.Meta.Analyzers.Test`: 163 |
| R414 | Fuse model-variable validation and Z3 symbol construction | `SharpProof.Smt.Test`: 30 passed |
| R418 | Reuse encoded arithmetic operands across SMT binary operators | `SharpProof.Smt.Test`: 30 passed |
| R431 | Resolve the corpus license fixture from the repository root helper | `SharpProof.Gates.Test`: corpus tests passed |
| R521 | Share IR factory nullable-type validation | `SharpProof.Ir.Test`: 114 passed |
| R522 | Cache response hash validity during protocol validation | `SharpProof.Worker.Test`: protocol tests passed |
| R523 | Reuse the cache filename hexadecimal-digit predicate | `SharpProof.Worker.Test`: 695 passed |
| R529 | Delegate string ordering validation to the generic fingerprint helper | `SharpProof.Worker.Test`: 695 passed |
| R541 | Share canonical corpus snapshot data validation | `SharpProof.Gates.Test`: corpus tests passed |
| R518 | Share potential-null effect handling for receivers and locks | `SharpProof.Effects.Test`: 323 passed |
| R524 | Share callable proof-label normalization | `SharpProof.Worker.Test`: 695 passed |
| R526 | Share order-insensitive assumption comparison across protocol layers | `SharpProof.Worker.Test`: 695 passed |
| R539 | Aggregate trusted-boundary assumption flags in one protocol pass | `SharpProof.Worker.Test`: 695 passed |
| R513 | Share conditional truth operator return-expression extraction | `SharpProof.Effects.Test`: 323 passed |
| R532 | Reuse compiler source-location copy/equality helpers during replay | `SharpProof.Worker.Test`: 695 passed |
| R533 | Share the zero source-location sentinel predicate | `SharpProof.Worker.Test`: 695 passed |
| R422 | Reuse package archive assembly-name extraction within each payload pass | PowerShell parse; package payload authority tests passed |
| R534 | Derive reset marker paths from already-canonical publication paths | `SharpProof.Worker.Test`: 695 passed |
| R538 | Reuse shared sequential/reverse reachability stack helpers | `SharpProof.Effects.Test`: 323 passed |
| R528 | Share the generated allocation-uncertainty marker predicate | `SharpProof.Effects.Test`: 323 passed |
| R316 | Consolidate friend-assembly declarations into SDK `<InternalsVisibleTo>` items and remove IVT-only `AssemblyInfo.cs` files | `test-changed`: 16 focused suites, ArchitectureTest 389, and 36 package shards passed |
| R320 | Remove the unreferenced `Format-CSharp.ps1` output-only `-Verify` branch while retaining developer formatting | PowerShell parse; `test-changed` formatting/build paths passed |
| R789 | Derive offline framework source mappings from the copied package catalog | PowerShell parse; `ContainerConsumerMatrixUsesCatalogOwnedNet8ReferencePacks`: 1 passed |
| R791 | Share timed phase execution between developer-check and package-test orchestrators | PowerShell parses; timing helper behavior; Architecture scheduling/plan tests: 28 passed |
| R792 | Reuse the shared C# string encoder in the API-spec runtime-witness generator | Generator `-Verify`; `SharpProof.Specs.Test`: 12 passed |
| R794 | Reuse shared package identity parsing in pilot qualification | Pilot authority fixtures; `PilotAuthorityTests`: 1 passed |
| R793 | Share repository-scoped Git text execution between inventory and coverage scripts | PowerShell parses; Git helper behavior; authority tests: 36 passed (1 pre-existing complexity-cap failure) |
| R790 | Drive package-consumer framework coverage from the acceptance contract | PowerShell parse; `ContainerPackageConsumersRestoreBeforeBuildingOfflineFeed`: 1 passed |
| R797 | Share the staged-worker version projection between launcher input-hash and response-version calculations | `SharpProof.Package.Test`: `LauncherArgumentTests`, 75 passed |
| R798 | Validate launcher path topology once before snapshot/request projection, then retain only the manifest-dependent final pass | `SharpProof.Package.Test`: `LauncherArgumentTests`, 75 passed |
| R801 | Merge incoming-environment completeness and difference checks into one forward scan | `SharpProof.Worker.Test`: `AcyclicBlockPredicateExecutorTests`, 14 passed with `-Fast` |
| R802 | Let the shared release-bundle topology helper own the six-artifact/cardinality precondition | PowerShell parse; `ReleaseJsonAuthorityTests` fixture coverage |
| R803 | Partition sorted manifest claims in one pass for postconditions and effects | `SharpProof.Package.Test`: focused compiler-manifest tests with `-Fast` |

The final worktree removes 3,965 net lines: 2,136 net lines outside this ledger and
1,829 net lines from replacing the duplicated 288 KB survey with this canonical
status document.

## Refuted or rejected

| IDs | Decision |
|---|---|
| R002 | Refuted in the original audit: the proposed overload merge does not compile without changing public API and would skip a validation path. |
| R003 | Rejected after current-tree audit: a generic JSON scalar reader needs parser delegates or type switches, does not unify string whitespace validation, and is not a net code reduction. |
| R023 | Rejected: the carrier class is already a primary-constructor class, while record structs require `IsExternalInit`, which the netstandard2.0 IR project intentionally does not provide. |
| R040 | Rejected: replacing a closed allocation-free type switch with a dictionary adds static state and lookup overhead for line count alone. |
| R047 | Refuted/stale: `AnalyzerConfigurationOption` is already a primary-constructor class; making it a record is incompatible with netstandard2.0, and the rest is formatting-only. |
| R049 | Rejected: replacing twelve typed fields with a string-keyed nullable dictionary adds lookup state and weakens compile-time identity for formatting savings. |
| R052 | Rejected: the boolean constructor discriminator preserves the difference between a public omitted resolver and an invalid internal null resolver. |
| R054 | Rejected: a symbol-count dictionary is not shorter than the existing matched-array multiset and adds hashing machinery to a tiny private comparison. |
| R065 | Refuted in the current tree: warning defaults are already conditional by production/test role, while Package and Verifier rely on SDK packability defaults and cannot accept one global `IsPackable=false`. |
| R071 | Rejected: moving the mutation catalog to JSON relocates rather than removes the authoritative data and adds a parsing boundary. |
| R117 | Rejected: dictionaries for thirteen fixed fuzz counters add hashing/allocation and are less direct than the closed switches. |
| R121-R124, R161-R163 | Rejected as documentation deletion/rearrangement rather than code reduction; the content remains useful navigation, rationale, or audit evidence. |
| R077 | Refuted in the original audit: both PowerShell parameters have live C# callers and behavioral branches. |
| R109 | Rejected in the original audit: positional generated records change constructor visibility and equality/API shape. |
| R128 | Refuted against the current tree: `SharpProof.Frontend.csproj` invokes `Get-SharpProofModuleVersionId.ps1`. |
| R223 | Refuted against the current tree: `ConfirmAncestorIdentity` is called after publication locks are acquired and protects a live TOCTOU boundary. |
| R197 | Rejected after implementation and Contracts testing: the pairwise matcher adds 13 formatted lines and cannot safely index the two-argument predicate site. |
| R465 | Rejected after implementation and Frontend testing: the generic descriptor helper adds four lines and delegate indirection to two small type-specific loops, so it is not a net reduction. |
| R213 | Rejected after implementation: the helper adds eight lines and an argument-array allocation on the summary path. |
| R215 | Rejected after implementation and 695 Worker tests: the helper adds three formatted lines. |
| R216 | Rejected after implementation: the all-unknown helper adds ten formatted lines. |
| R057 | Refuted against the current tree: only three tests retain the single-invocation shape; the remaining flow tests select distinct operations or assert graph-specific state. |
| R204 | Rejected by canonical pack validation: removing the project-side `PackageId` changes restore identity, causing the locked Verifier dependency graph to fail with NU1004 before packing. |
| R299 | Refuted against the current tree: the contract now pins the 248-entry mutation catalog, the registration script checks that count before execution, and checksum identity was intentionally removed from the package/inventory pipeline. |
| R270 | Refuted against the current tree: the SPDX/SBOM release-evidence producer and validator, including `Get-SpdxPackageId`, were removed with the package-integrity pipeline; package layout and dependency checks remain separate. |
| R303 | Refuted with R270: the SBOM producer/validator comparison no longer exists after the package-integrity pipeline removal. |
| R738 | Refuted/stale: `PreviewConfigurationInterfaceMatchesFrozenSnapshot` already compares both dogfood compiler-visible property lists with the shipping union. |

## Deferred

| IDs | Reason |
|---|---|
| R001 | Positional records would change constructor visibility and equality semantics. |
| R019, R224 | These remove public or semantically meaningful summary facets; write-only repository evidence is not enough. |
| R020 | The dataflow arithmetic is a real capability even if production callers are absent; deletion can be revisited as an explicit feature/API decision. |
| R013 | Re-threading recursive API-spec validation through mutable context changes soundness-critical state ownership for cosmetic call-site savings. |
| R022 | A generic bottom-up fold would obscure two small performance-sensitive algorithms and add delegate/short-circuit machinery. |
| R024 | `ClosedAbstractDomain.Merge` and `Compare` are public API, and `OwnedCount` supports a load-bearing disposal test. |
| R032-R034 | These are broad Effects/Gates control-flow and process-lifetime refactors; the copies have environment-specific predicates and failure semantics. |
| R038-R039, R041 | These alter soundness-sensitive traversal, pattern, or replay-candidate ordering; defer to a dedicated semantic refactor. |
| R007-R009 | Compiler-probe JSON bytes, artifact authority, and IL opcode admission are compatibility/soundness boundaries; defer to focused format work. |
| R027-R031 | Generalizing process, temporary-directory, and package-test setup changes cleanup/lifetime semantics across many fixtures; defer after the shared root/default work already removed the exact duplication. |
| R057-R058, R060, R073, R087-R096, R104-R105 | These parameterize or abstract large test fixtures; keep named failure isolation and local arrange/assert evidence in this reduction pass. |
| R066-R070 | These change sample/pilot inheritance, scheduled validation, packaged imports, workflow setup, or automatic production-project classification. |
| R072, R074, R076 | Shared shard/coverage/timing orchestration would centralize timeout, process, and atomic-publication semantics; treat as dedicated infrastructure work. |
| R078-R080, R082-R085 | Soundness-critical recursive traversal, dispatch, alias, and abstract-value changes are deferred as requested. |
| R099-R100, R102-R103 | Cross-project metadata-reference and verification-algorithm helpers have ordering, filtering, identity, or performance differences that need dedicated design. |
| R107 | Consolidating helpers across eleven generators is a broad generator-maintenance change; the output-compaction changes already provide the safe generated-code reduction. |
| R110-R112, R114 | Release identity, Git byte capture, package IDs, and canonical JSON comparison are release-authority code and remain explicit. |
| R118 | A new build-task base class changes the task hierarchy and cancellation surface used by packaged MSBuild tasks. |
| R119 | The fuzz oracle compilation paths have distinct failure-isolation behavior; defer their unification. |
| R125-R127, R129 | Acceptance assertions, CPU budgeting, and container command execution are operational authority paths, not formatting helpers. |
| R131, R133-R134 | Docker target aliases, CI environment scope, and permission declarations are user/CI behavior and security documentation. |
| R136-R138, R141-R142, R144 | Gates proposals combine test-fixture churn with CLI envelope or model-shape changes; retain explicit gate boundaries. |
| R149 | The remaining shared PowerShell fixture runner changes failure-envelope presentation across architecture fixtures. |
| R156, R160 | Release-authority closure and transaction recovery are security/recovery behavior and are deferred. |
| R165, R169, R171-R185, R194 | Cross-suite fixture and parameterization proposals still need current-tree validation. |
| R202-R204 | Literal catalogs and NuGet metadata require an authority decision, not automatic replacement by another indirection. |
| R211 | The CompilerCollector block-context carrier still needs current-tree validation. |
| R217-R218, R221 | The remaining low-level parameterization and shared-host proposals still need current-tree validation. |
| R081 | The unreachable conversion arm represents intended null-receiver behavior; deleting it would hide a latent soundness bug rather than simplify a working path. |
| R095, R097, R098, R170 | Formatting-only line-count reductions do not improve maintenance. |
| R120 | Stale build-output directories are not tracked code and do not belong in this branch. |
| R155 | Trimming generic `.gitignore` boilerplate is not a code reduction and has negligible maintenance value. |
| R227 | The approximation types are a documented reserved design slot. |
| R247 | Retain the editor-integration fixture: `.opencode` is explicitly bound by architecture tests and the usefulness audit, so deleting it would remove a user-facing tool rather than product/build duplication. |

## Merged duplicates

| Canonical item | Merged IDs | Scope |
|---|---|---|
| R026 | R150, R200 | Repository-root discovery and its divergent error messages |
| R028 | R142, R203 | Temporary-directory naming and cleanup scaffolding |
| R058 | R172 | Analyzer embedded-fixture preamble |
| R064 | R158 | Compose service/environment duplication |
| R069 | R132 | Repeated workflow checkout/build-tooling prelude |
| R099 | R061, R201 | Trusted-platform-assembly metadata references |
| R104 | R221 | Compile-and-find-`Target` Roslyn test host |
| R107 | R205 | Shared generator/schema/header helpers |
| R121 | R163 | Code-usefulness audit ledger/prose collapse |
| R149 | R165 | Architecture PowerShell fixture runner |

Merged IDs are not separate work items and must not be counted twice.

## Pending queue

The active follow-up queue is R058, R060, R073, R087-R094, R096, R104-R105,
R107, R149, R165, R169, R171-R185, R194, R211, R217-R218, and R221.
Each still requires current-tree validation before implementation. The other
items in the Deferred table are intentional behavior, public API, release
authority, security, or soundness decisions and remain deferred under the
original instruction not to remove important features merely for line count.
Merged IDs inherit the status of their canonical item.

## Final gate

After the pending queue is exhausted or explicitly deferred, run
`docker compose run --rm tooling test-changed`, inspect generated/package
contents for touched generators or packaging code, and report the final diff
line count with all test results and remaining intentional deferrals.

## Second survey (2026-09-01): new candidates R229-R262

A fresh read-only pass over the whole tree, including build, container,
workflow, generator, catalog, and test infrastructure. Nothing below has been
implemented or validated; every item is `pending` until its evidence is
rechecked and the smallest relevant containerized target passes. Line counts are
from the working tree at the time of the survey. Items that refine an existing
deferred entry say so explicitly and do not lift that deferral.

### Build and packaging authority

| ID | Finding | Evidence |
|---|---|---|
| R229 | The analyzer dependency closure is declared twice, in full, with no shared authority. `SharpProof.AnalyzerConsumer.props` lists 15 portable `<Analyzer Include>` assemblies plus 7 collector assemblies; `SharpProof.Package/buildTransitive/SharpProof.targets` lists the same 15 and the same 7 against `$(_SharpProofSharedDirectory)`. Both must be edited together whenever a transitive dependency changes, and a drift surfaces only as an analyzer load failure in a consumer build. The source-tree copy already proves globbing works in this position (`<Analyzer Update="...netstandard2.0\*" ... />`), so each list is a candidate for one wildcard plus the existing role metadata. | `SharpProof.AnalyzerConsumer.props:35-49,60-67`; `SharpProof.Package/buildTransitive/SharpProof.targets:20-35,46-53` |
| R233 | The root build file has to know every consumer of every shared source file. `SharpProofUsesIrIdentifiers` is a 19-term `Or` chain of project names, and four further `ItemGroup`s gate `eng/testing` sources by explicit project-name lists (12 more names). An opt-in property set in each consuming `.csproj` inverts the coupling and removes roughly 40 lines of central condition. Narrower than deferred R070: this is shared-source gating, not production classification. | `Directory.Build.props:38-58,74-101` |
| R262 | `samples/Directory.Build.props` and `eng/pilots/Directory.Build.props` share nine identical property lines (`TargetFramework`, `LangVersion`, `Nullable`, `ImplicitUsings`, `Deterministic`, `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `NuGetAudit`, `OutputType`). Both deliberately skip the root props and import only `SharpProof.Release.props`, so a shared consumer-defaults props beside it is the natural home. Refines deferred R066-R070 with the exact overlap rather than a general inheritance change. | `samples/Directory.Build.props:4-12`; `eng/pilots/Directory.Build.props:4-12` |

### Container, Compose, and workflow

| ID | Finding | Evidence |
|---|---|---|
| R240 | The command vocabulary is declared in four places that must agree: the `build.ps1` `ValidateSet` (21 profiles), the `Invoke-SharpProofContainer.ps1` `ValidateSet` (37 commands), and the `requires_clean_exact_commit_source` (11) and `requires_git_source` (8) case lists in `entrypoint.sh`. Adding a command means editing between two and four of them, and nothing checks that the sets remain consistent. | `build.ps1:4-10`; `scripts/Invoke-SharpProofContainer.ps1:3`; `eng/container/entrypoint.sh:53-76` |

### Generators

| ID | Finding | Evidence |
|---|---|---|

### Catalogs and generated models

| ID | Finding | Evidence |
|---|---|---|
| R254 | The 13-member effect-capability vocabulary is declared three times and emitted as three structurally identical flags enums: `EffectCapabilityKind` and `EffectContractCapabilityKind` in the Effects catalog, and `WorkerEffectCapabilitySet` in the protocol schema. The three differ only in whether they carry `AllKnown` and `Unknown`. Drift between them is silent at the catalog level and is caught only downstream by the wire-mapping tables in R255. | `SharpProof.Effects/EffectContractMappings.catalog.json:10-27,97-111`; `SharpProof.Worker.Protocol/ProtocolModel.schema.json:538` |
| R255 | 15 of the 18 declared wire-mapping tables in the compiler-artifact schema are pure identity mappings: 89 rows of `{"source": X, "target": X}` whose only content is that two enums share member names. Only `AssemblyIdentityComparer`, `BoundContractEvidenceWorker`, and `ContractBindingFailure` carry information. A generator that matches by name and asserts member-name-set equality keeps the exhaustiveness proof while removing the 89 rows and their generated switch arms. Note that these tables currently *are* the proof of alignment, so the replacement must add that assertion, not merely delete the rows. | `SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json`, mapping tables |
| R256 | In the Effects catalog `capabilities` table, all 13 rows satisfy `contract == analysis`; only the `effect` column is informative. The contract-to-analysis half is an identity projection written out longhand. | `SharpProof.Effects/EffectContractMappings.catalog.json:170-184` |

### C# production and test code

| ID | Finding | Evidence |
|---|---|---|
| R257 | SHA-256 hex encoding still has three implementations after applied R101. `HashEncoding.ComputeSha256Hex` is the canonical one; `ProtocolJson.ComputeSha256` open-codes the identical body; `OpenSourceCorpusCatalog` and `OpenSourceCorpusImporter` use a third spelling, `Convert.ToHexString(...).ToLowerInvariant()`. `SharpProof.Worker.Protocol` has no reference to `SharpProof.Ir` and `HashEncoding` is `internal`, so unification needs a project reference or an `InternalsVisibleTo`; that is why this is a candidate rather than an obvious fix. Separately, `HashEncoding.ToLowerHex` builds its string with LINQ and a per-byte `ToString("x2")` on the `CanonicalHashWriter.Finish` path, where `Convert.ToHexString` over the span would allocate once. | `SharpProof.Ir/HashEncoding.cs`; `SharpProof.Worker.Protocol/ProtocolJson.cs:1062-1067`; `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:66,364`; `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:160` |
| R258 | `SharpProof.ContractForGenerator` ships an `IIncrementalGenerator` whose `Initialize` is deliberately empty. Its test project then builds a `CSharpGeneratorDriver` around that no-op with `IncrementalGeneratorOutputKind.None`; `runResult.Diagnostics` and `driverDiagnostics` are therefore always empty, and every real assertion comes from `AnalyzeFinalCompilation`, which runs `SharpProofAnalyzer` and filters `SPCF*`. About 2,000 lines of analyzer tests reach the analyzer through generator-driver scaffolding they do not need. Two tests do use the driver, but only to assert emptiness (`GeneratedTrees, Is.Empty`, cached step reasons empty), a regression guard that could stand alone in a handful of lines. Moving the `SPCF` cases to a plain analyzer host is a test migration, not a mechanical edit. | `SharpProof.ContractForGenerator/ContractForValidatorGenerator.cs`; `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:96-192`; `SharpProof.ContractForGenerator.Test/ContractForValidatorGeneratorTests.cs:1874-1966` |
| R259 | Process-runner scaffolding is duplicated across test projects: an identical `RunFixtureAsync(string mutation)` in 6 `ArchitectureTest` files, a `RunAsync(workingDirectory, fileName, params string[])` `ProcessStartInfo` runner in 13 files spanning `ArchitectureTest` and `Package.Test`, and 3 in-file copies of the `pwsh -Command` variant inside `BuildSchedulingTests.cs` alone; 19 `FileName = "pwsh"` sites in total. Refines deferred R027-R031 and R149 with current-tree counts. The failure-envelope objection still applies to the `RunFixtureAsync` family, but the plain `RunAsync` runner has no such difference between its copies. | `SharpProof.ArchitectureTest/*.cs`; `SharpProof.Package.Test/*.cs` |
| R260 | Fixture mutation names are declared twice, once as a PowerShell `ValidateSet` and once as C# `[TestCase]` attributes. `Test-SharpProofPublicationDestinationFixtures.ps1` and `PublicationDestinationAuthorityTests.cs` each list the same 37 names, and 14 `Test-SharpProof*Fixtures.ps1` scripts follow the pattern. Adding a mutation means editing both sides in step, in two languages, with nothing checking that they still agree. | `scripts/Test-SharpProof*Fixtures.ps1`; `SharpProof.ArchitectureTest/PublicationDestinationAuthorityTests.cs:10-45` |
| R261 | Several hundred assertions read a build or script file and assert `Does.Contain` on literal source fragments, including PowerShell variable names such as `"$packageLayoutBuckets"` and `"$nextIsExclusive"`, and code fragments such as `"'--no-restore', '--no-build', '--nologo'"`. `ArchitectureTest` holds 439 `Does.Contain` calls, concentrated in `BuildSchedulingTests.cs` (150), `ArchitectureTests.cs` (121), `ReleaseCoverageBaselineTests.cs` (47), and `DependencyAutomationTests.cs` (28). These pin identifiers rather than behaviour: a rename that changes nothing observable still breaks them, and they pass unchanged if the pinned text is present but wrong. Where the same file is already exercised through a fixture script that produces JSON, the text assertion adds no coverage. This is a large, judgement-heavy cleanup and should be scoped per fixture, not swept. | `SharpProof.ArchitectureTest/BuildSchedulingTests.cs:26-130`; `SharpProof.ArchitectureTest/ArchitectureTests.cs`; `SharpProof.ArchitectureTest/PublicationDestinationAuthorityTests.cs:58-72` |

### Checked and not proposed

- `.github/workflows/security-reusable.yml` is called by both `security.yml` and
  the `security` job in `package-consumers.yml`, so the reusable-workflow
  indirection has two consumers and is not surplus.
- The 47 `packages.lock.json` files (6,911 lines) are a deliberate supply-chain
  control paired with `RestoreLockedMode` and `NuGetAudit`, not incidental bulk.
- The `PortableAnalyzer` references in `SharpProof.ConsumerContract.props` and
  `Test-SharpProofPackageConsumers.ps1` are regression guards asserting absence
  and should stay. Only the `Directory.Build.props` classification arm in R232 is
  dead.
- No `TODO`, `HACK`, or `FIXME` markers and no commented-out code blocks were
  found in tracked C# or PowerShell sources. `#pragma warning disable` and
  `SuppressMessage` use is sparse and carries justifying comments.
- Untracked stale directories on disk (`scratch-pdb/`, `SharpProof.PortableAnalyzer/`,
  `SharpProof.Verifier.Win-x64/`, `eng/pilots/*/bin`, `eng/pilots/*/obj`,
  `artifacts/mutation/workspace-*`) remain out of scope under deferred R120.

### Status

R229, R233, R240, and R254 through R262 are `pending`, and the active follow-up queue above is
extended by them; the existing entries in that queue are unchanged. Items that
refine a deferred entry (R233 under R070, R246 under R069/R132, R259 under
R027-R031 and R149, R262 under R066-R070) do not lift that deferral. R241, R255,
R257, R258, and R261 each carry a stated risk that has to be settled before
implementation rather than during it.

## Second survey, part two: R263-R276

Continuation of the same read-only pass, covering the areas part one only
sampled: the shipped verifier MSBuild targets, preprocessor and shared-source
plumbing, the non-generator PowerShell families (test invocation, release
evidence, publication identity), diagnostic release tracking, and the solution
files. Same rules as part one: nothing implemented, nothing validated, all
`pending`.

### Shipped verifier MSBuild targets

| ID | Finding | Evidence |
|---|---|---|
| R263 | The invocation-directory containment check is implemented twice: once in `_SharpProofInitializeVerify` and once in `_SharpProofCleanupInvocation`, the second with a `Cleanup` prefix on all nine derived properties. Both compute verify directory, runs directory, ID safety, expected directory, full path, canonical, matches-expected, parent, and contained, and both raise the same two errors, one of them verbatim. The only real difference is that the cleanup copy defaults `_SharpProofInvocationDirectory` when unset. Duplicating a path-containment security check is worse than duplicating ordinary code, because the two copies can drift while both keep passing. Any fix must preserve the override semantics described under "Checked and not proposed" below. | `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:69-85,131-151` |
| R264 | The "re-root a configured path under `$(TargetFramework)` when the project is multi-targeted" idiom is written out four times verbatim, for request, result, compiler-manifest, and SARIF files: twelve property lines whose only difference is the property name. | `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:52-64` |
| R265 | `_SharpProofCompilerOwnedOutput` adds both the SDK-provided property and an open-coded fallback path for the same artifact in five cases (`GeneratedAssemblyInfoFile` and `$(MSBuildProjectName).AssemblyInfo.cs`, `GeneratedGlobalUsingsFile` and `$(MSBuildProjectName).GlobalUsings.g.cs`, `GeneratedMSBuildEditorConfigFile` and its open-coded twin, `IntermediateRefAssembly` and `ref/$(TargetName)$(TargetExt)`, `$(IntermediateOutputPath)$(TargetName).pdb` and `ChangeExtension($(TargetPath), '.pdb')`). When the SDK property is set, the item carries the same file twice. Belt-and-braces coverage may be intended, in which case the duplicate is deliberate and this should be recorded as such rather than removed. | `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:93-111` |

### Preprocessor and shared-source plumbing

| ID | Finding | Evidence |
|---|---|---|

### PowerShell outside the generators

| ID | Finding | Evidence |
|---|---|---|
| R272 | "Resolve HEAD and require an exact 40-hex commit" is open-coded in roughly seventeen places across scripts, `build.ps1`, and a workflow, most of them also repeating the `$LASTEXITCODE -ne 0 -or` guard. Fourteen use case-insensitive `-notmatch '^[0-9a-f]{40}$'` and three use case-sensitive `-cnotmatch`/`-cmatch`. The case-insensitive form accepts an uppercase SHA, which then will not compare `Ordinal`-equal to the lowercase form used elsewhere in the same evidence chain. Worth treating as a correctness inconsistency first and a duplication second: one `Get-SharpProofExactCommit` helper fixes both. | `scripts/Get-SharpProofProductionInventory.ps1:316`; `scripts/Invoke-SharpProofGateEvidence.ps1:22`; `scripts/New-SharpProofReleaseEvidence.ps1:550`; `scripts/Publish-SharpProofRelease.ps1:78,232`; `scripts/Resolve-SharpProofReleaseCoverageBaseline.ps1:73`; `scripts/SharpProof.FuzzEvidenceLifecycle.ps1:27`; `scripts/Test-SharpProof*.ps1`; `build.ps1:82`; `.github/workflows/coverage.yml:40,51` |

### Diagnostic release tracking

| ID | Finding | Evidence |
|---|---|---|
| R273 | `AnalyzerReleases.Unshipped.md` hand-restates the category and severity of 13 rules that `eng/diagnostics/diagnostic-descriptors.v1.json` already carries as `category` and `defaultSeverity`, making it a third copy of part of the diagnostic vocabulary and a generation candidate for `Generate-DiagnosticDescriptors.ps1` (the Notes column would have to move into the catalog). Separately, only `SharpProof.Analyzer` tracks releases at all: `SharpProof.ContractForGenerator` and `SharpProof.Meta.Analyzers` set `NoWarn RS2008`, so 21 of the catalog's 34 IDs (all `SPCF*` and `SPMETA*`) are untracked. Whether that asymmetry is intended is a policy question, not a reduction. | `SharpProof.Analyzer/AnalyzerReleases.Unshipped.md`; `eng/diagnostics/diagnostic-descriptors.v1.json`; `SharpProof.ContractForGenerator/SharpProof.ContractForGenerator.csproj:9` |

### Solution files

| ID | Finding | Evidence |
|---|---|---|
| R274 | `SharpProof.sln` is 300 lines for 47 projects, of which roughly 190 are per-project GUID configuration mappings and 94 are `Project`/`EndProject` pairs. The repository has already adopted the `.slnx` format for `samples/SharpProof.Samples.slnx`; the equivalent main-solution file is about 49 lines. This is an authority decision, not a mechanical edit, because the file is load-bearing in several places: `TestRepository.FindRoot` uses its presence as the repository-root marker, `Get-SharpProofProductionInventory.ps1` parses it with a regex (R275), the three `.slnf` filters name it in their `solution.path`, `Invoke-SharpProofChangedTests.ps1` treats it as a change trigger by exact name, and `build.ps1`, `eng/acceptance/Verify.ps1`, `Format-CSharp.ps1`, and `.devcontainer/devcontainer.json` reference it by name. | `SharpProof.sln`; `samples/SharpProof.Samples.slnx`; `eng/testing/TestRepository.cs:8`; `SharpProof.{Dev,Portable,Semantic}.Tests.slnf` |
| R275 | `Get-SolutionProjectPaths` parses `SharpProof.sln` with a hand-written regex to enumerate projects, inside the canonical container where `dotnet sln list` is available. It also hard-codes the legacy solution format and would have to be rewritten under R274. | `scripts/Get-SharpProofProductionInventory.ps1:141-147` |

### Test fixtures

| ID | Finding | Evidence |
|---|---|---|
| R276 | Exact repeats of multi-line raw-string test fixtures are modest and mechanical: 36 redundant copies across 1,219 such literals in the test projects, concentrated in `CompilerManifestArtifactTests.cs` (10 redundant of 35), `WorkerTests.cs` (8 of 89), and `GeneratedContractForAnalyzerTests.cs` (5 of 21). This is a much smaller and safer set than the near-duplicate fixture *prefixes* that deferred R087-R096 covers, where fixtures share an opening but diverge in the tail and named failure isolation is the reason to keep them apart. | `SharpProof.Worker.Test/CompilerManifestArtifactTests.cs`; `SharpProof.Worker.Test/WorkerTests.cs`; `SharpProof.Analyzer.Test/GeneratedContractForAnalyzerTests.cs` |

### Checked and not proposed (part two)

- **Do not "simplify" the verifier invocation-directory checks.** In
  `SharpProof.Verifier.targets`, `_SharpProofInvocationDirectory` is assigned
  `$(_SharpProofExpectedInvocationDirectory)` on the line above the canonical,
  matches-expected, and contained comparisons, which makes the three look like
  tautologies. They are not: neither `_SharpProofInvocationId` nor
  `_SharpProofInvocationDirectory` appears in the file's `TreatAsLocalProperty`
  list, while every derived property does, so both can be overridden by a global
  property and the triple is what makes that override safe. R263 proposes
  removing the duplicate *copy* of this check, not the check.
- `SharpProof.sln` and the three `.slnf` filters are exactly consistent with the
  47 tracked non-sample project files: no orphaned entries, no missing projects,
  and every filtered project exists in the solution.
- `BannedSymbols.txt` holds 39 entries with no duplicates.
- The sample projects are already minimal; five of the eight `.csproj` files are
  two lines, and the rest carry only the property each sample exists to
  demonstrate.
- Exact duplicate method bodies are nearly absent from production C#: a
  repo-wide scan of brace-matched bodies of eight lines or more found four
  duplicate groups, all either the trivial guard overloads in R267 or test
  helpers already covered by R259. The earlier reduction passes did clear this
  class.
- `eng/diagnostics/diagnostic-descriptors.v1.json` and
  `SharpProof.Analyzer.Core/AnalyzerDiagnostic.catalog.json` share no diagnostic
  IDs and serve different roles; they are not two copies of one catalog.
- `SP0013`, `SP0015`, and `SP0030` are declared, tracked, and documented but
  never emitted ("Reserved ... currently not emitted"). These are reserved design
  slots of the same kind as deferred R227 and are not proposed for removal.

### Status (part two)

R263-R265 and R270, R272-R276 are `pending` and extend the same follow-up queue. R263, R265,
R270, R272, R273, and R274 each carry a stated constraint - a security check that
must survive, a possibly deliberate belt-and-braces item, release-evidence
caution under R110-R112, a correctness inconsistency to settle first, a policy
question about release-tracking scope, and a migration surface across nine
consumers respectively - and none of them should be treated as mechanical.

### Survey conditions

The working tree moved while this survey ran: the set of modified files at the
end differs from the set at the start, and `scripts/Get-SharpProofReleaseDigests.ps1`
was deleted mid-pass. Treat every line number in R229-R276 as approximate and
resolve findings by symbol, property, or literal rather than by line. The
substance of R232, R243, R266, R269, R270, R271, R272, and R275 was re-verified
against the tree after those changes landed and still holds, with line drift of
a few lines in `SharpProof.Host/ContainerContract.cs` (21 to 19),
`SharpProof.ArchitectureTest/ArchitectureTests.cs` (1429 to 1423), and
`scripts/Get-SharpProofProductionInventory.ps1` (141 to 142). The remaining
items were measured before those changes and are the usual current-tree recheck
that this ledger already requires before any item moves out of `pending`.

## Second survey, part three: R277-R284

Continuation into the production assemblies themselves: process-handshake and
CLI vocabulary, the boundary between generated authority and hand-typed
literals, and the shared-source mechanism. Same rules: nothing implemented,
nothing validated, all `pending`.

### Duplicated wire vocabulary in production C#

| ID | Finding | Evidence |
|---|---|---|
| R277 | The verifier process-handshake tokens are declared four times over. `SharpProof.Start/1` exists as a `public const` in `LinuxWorkerProcess`, as `ProcessGateStartMessage` in `RunVerifier`, as `StartMessage` in `VerifierProcessSupervisor`, and as a bare literal in `WorkerPerformanceProbe`. `SharpProof.Armed/1` and `SharpProof.Cleanup/1` are each declared twice, as private consts in the two files that form the two ends of the same handshake: the supervisor writes them, `RunVerifier` reads them, and neither shares the token. `SharpProof.BuildTasks` already has a `ProjectReference` to `SharpProof.Host`, and `SharpProof.Worker/Program.cs:168` already uses `LinuxWorkerProcess.StartMessage`, so the shared public const is both reachable and the established pattern; these four sites simply do not use it. | `SharpProof.Host/LinuxWorkerProcess.cs:19`; `SharpProof.BuildTasks/RunVerifier.cs:40-42`; `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:18-20`; `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs:720`; `SharpProof.BuildTasks/SharpProof.BuildTasks.csproj:18` |
| R278 | POSIX signal numbers are redeclared as private consts across three files in two assemblies: `SignalKill = 9` three times, `SignalStop = 19` twice, `SignalTerminate = 15` twice, plus `SignalNone = 0` once. Eight declarations of four distinct constants. | `SharpProof.BuildTasks/RunVerifier.cs:36-38`; `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:14-16`; `SharpProof.Host/LinuxWorkerProcess.cs:21-22` |
| R279 | The worker's own command line has no declarative authority even though the launcher's does. `SharpProof.Worker/Program.cs` matches an exact positional list pattern, `args is not ["verify", "--request", var requestValue, "--result", var resultValue, "--start-stdin"]`; `SharpProof.Worker.Launcher/Program.cs` builds that same array by hand; and `WorkerPerformanceProbe` locates the request path by scanning for the `"--request"` literal. Meanwhile `LauncherArguments.catalog.json` and its generated `LauncherArguments.generated.cs` are exactly this kind of authority for the launcher's *own* options. Because the worker side is a positional pattern, a reordering or rename is a runtime handshake failure rather than a compile error, and no test would catch a change made consistently in only two of the three places. | `SharpProof.Worker/Program.cs:124-125`; `SharpProof.Worker.Launcher/Program.cs:254-255`; `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs:714`; `SharpProof.Worker.Launcher/LauncherArguments.catalog.json` |
| R280 | Generated wire names are bypassed by hand-typed literals at production sites. `"explicit-throw"` is declared in `EffectContractMappings.catalog.json` and emitted as `(EffectDirectEventKind.ExplicitThrow, "explicit-throw")`, yet two production sites re-type the literal instead of using the mapping. `"worker.timeout"` is emitted into `LauncherProjections.generated.cs` and then re-typed at three further hand-written sites. This is the failure mode the catalogs exist to prevent, and it is invisible to the generator's `-Verify` check, which only proves the generated file matches the catalog. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs:160`; `SharpProof.Worker/EffectCounterexampleReplayer.cs:161`; `SharpProof.Effects/EffectContractMappings.catalog.json:186`; `SharpProof.Worker/SharpProofWorker.cs:101`; `SharpProof.Worker.Launcher/Program.cs:847`; `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:294` |

### Duplicated algorithms

| ID | Finding | Evidence |
|---|---|---|
| R281 | `CompilerProbeAnalyzer` re-implements `AtomicFile.WriteUtf8`: staged temporary path, `FileMode.CreateNew` with flush-to-disk, `File.Replace` when the destination exists and `File.Move` otherwise, delete-in-`finally`. The probe already links one shared source file from `SharpProof.Ir` (`HashEncoding.cs`, via `Compile Include ... Link`), so the mechanism is available and in use; the caveat is that `AtomicFile` sits in namespace `SharpProof.Ir` and relies on implicit usings the probe disables, which is the same problem `ArgumentNullGuard.cs` already solves with `#if` namespace switching. A third implementation of the same publish-atomically algorithm lives in PowerShell in `Update-SharpProofGeneratedFile`, and eight further scripts do a temp-then-move by hand. | `SharpProof.CompilerProbe.TestAsset/CompilerProbeAnalyzer.cs:80-108`; `SharpProof.Ir/AtomicFile.cs:41-78`; `SharpProof.CompilerProbe.TestAsset/SharpProof.CompilerProbe.TestAsset.csproj:14`; `scripts/GeneratedFileHelpers.ps1` |
| R282 | Twenty-six private methods whose whole body is a single delegating `return` still have exactly one call site, the pattern already applied in R046 and R086. Inline only where the forwarder name carries no information: several of them are genuine naming abstractions (`GeometricMean`, `HullLower`/`HullUpper`, `IsData`, `PurityKey`) and are worth keeping, while others are pure indirection (`LowerSha` forwarding to `HashEncoding.ComputeSha256Hex`, `ConvertCapabilities` forwarding to `EffectContractMappings.ToAnalysisCapabilities`, `IsDefinitelyString` forwarding to `DefiniteOperationFacts.IsDefinitelyString`). This should be triaged per site, not applied wholesale. | 26 sites, including `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs:682`; `SharpProof.Effects/ExternalEffectResolver.cs:372,390`; `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs:692`; `SharpProof.CompilerArtifact/PortableIrGraphCodec.cs:683,839` |
| R283 | `OpenSourceCorpusRunner` injects a hardcoded seven-namespace `global using` preamble as a raw string literal so the vendored corpus sources compile. That restates, as text, the same implicit-usings set that R234 covers on the project side. | `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs:25-31` |

### Split diagnostic authority

| ID | Finding | Evidence |
|---|---|---|
| R284 | The severity and the escalation of the same five rules live in two files: `.globalconfig` sets `CA1811`, `CS8019`, `IDE0051`, `IDE0052`, and `IDE0060` severities, and `Directory.Build.props` lists the same five IDs in `WarningsAsErrors`. Adding or retiring a dead-code rule means editing both, and neither file references the other. The split may be deliberate (severity is a diagnostic property, escalation is a build policy), in which case it should be recorded as such; but the ID list is duplicated either way. | `.globalconfig:4-14`; `Directory.Build.props:21` |

### Checked and not proposed (part three)

- **The 13 unreferenced corpus source files are not dead payload.** `oss-methods.json`
  embeds 100 files with full content but only 87 carry method rows, which looks
  like 25 KB of unused vendored source. It is not:
  `OpenSourceCorpusRunner.ObserveAsync` parses *every* `document.Files` entry into
  one compilation, so the unreferenced entries (`IGraph.cs`, `IEdge.cs`,
  `AVLTreeNode.cs`, `RedBlackTreeNode.cs`, and similar) are the compilation
  dependencies that let the files with method rows bind. Removing them would
  break the corpus gate.
- **Identity mapping tables are not a house style**, which strengthens R255 rather
  than excusing it. `SharpProof.Projection.catalog.json` declares 27 switch
  methods: exactly one is fully identity (and it has a single row), six are
  partially identity, and twenty have no identity rows at all. The 15-of-18
  identity ratio in `CompilerArtifactModel.schema.json` is an outlier within the
  repository's own conventions.
- `ProbeJson.cs` is a 140-line hand-rolled JSON writer, but
  `SharpProof.CompilerProbe.TestAsset` is a bare Roslyn analyzer asset that
  deliberately carries no `System.Text.Json` dependency; adding one to an
  analyzer risks assembly-load conflicts in the compiler host. Not proposed for
  unification with `ProtocolJson`.
- `eng/acceptance/Verify.ps1` is already table-driven in the way R148 applied to
  the architecture tests: six `Assert-Equal` call sites, all inside `foreach`
  loops over assertion tables. No repeated-assertion reduction is available there.
- Production interfaces are not a speculative abstraction layer: seven exist in
  total, and the two with a single implementation are real seams -
  `ICompilerAdditionalTextSnapshot` is implemented only by a test double so the
  collector can be driven without a real `AdditionalText`, and
  `IWorkerResponseEvidenceAuthority` inverts a `Worker.Protocol` to
  `CompilerArtifact` dependency that would otherwise be a reference cycle.
- `SHARPPROOF_NEGATIVE_PROBE` and `SHARPPROOF_PROBE_GENERATED` are both live
  (set by `Test-SharpProofPilots.ps1` and by the packaged consumer contract
  respectively); only the symbol in R266 is dead.

### Status (part three)

R277 and R278 are applied: process-handshake strings and POSIX signal numbers
now use the host-owned constants. R280 and R283 remain pending because they
cross generated/protocol and corpus-compilation boundaries. R279 is the one to
weigh first: it is not a line-count reduction at all
but a missing authority over a runtime handshake, and closing it would probably
add lines while removing a class of silent failure. R281 and R282 need per-site
triage rather than a sweep, and R284 may turn out to be an intentional split that
should be documented instead of merged.

## Second survey, part four: R285-R289

Continuation into operation-traversal helpers, abstract-value predicates, and
the last unexamined root files. One entry here (R287) is a correctness
observation rather than a reduction, and is filed as such.

### Repeated operation traversal

| ID | Finding | Evidence |
|---|---|---|
| R285 | `IsInsideNestedCallable(IOperation operation, IOperation root)` is implemented identically in two assemblies, differing only in the loop-variable name and line breaks: walk parents until `root`, return true on `IAnonymousFunctionOperation or ILocalFunctionOperation`. A third, root-less variant answers the same question in `SharpProof.Analyzer.Core`. The Effects copy is already `internal` and correctly reused within its own assembly by `UsingDisposalEffectResolver`, so only the cross-assembly copy is surplus. Caveat: `SharpProof.Meta.Analyzers` declares no `ProjectReference` at all - only package references - so sharing needs either a new project reference or the `Compile Include ... Link` mechanism the repository already uses for `ArgumentNullGuard.cs` and `HashEncoding.cs`. That standalone posture may be deliberate for a meta-analyzer that analyzes SharpProof's own source. | `SharpProof.Effects/ConversionOwnershipClassifier.cs:654-665`; `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1372-1383`; `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1062-1067`; `SharpProof.Meta.Analyzers/SharpProof.Meta.Analyzers.csproj` |
| R286 | Seventeen hand-rolled `IOperation` parent-chain walks (`for (var current = x.Parent; current != null; current = current.Parent)`) are spread across six assemblies. `RequiresCallSiteDiscovery` already factors the shape into a private `Ancestors(IOperation)` iterator, so the abstraction exists but is not shared. Extracting the *iterator* is safe; changing what each loop decides is not, because the predicates inside them are the soundness-bearing part deferred under R038-R039 and R078-R085. Any implementation must keep each loop's own termination condition, since they differ (unbounded, bounded by `root`, and bounded by `HasSameSite`). | `SharpProof.Analyzer.Core/LanguageSubsetGate.cs:107`, `RequiresCallSiteDiscovery.cs:1072`, `RequiresCallSiteTreeAnalyzer.cs:1242,1343`; `SharpProof.Contracts/ContractIntrinsicValidator.cs:125`; `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs:267`; `SharpProof.Effects` (6 sites); `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs` (4 sites) |

### Correctness observation, not a reduction

| ID | Finding | Evidence |
|---|---|---|
| R287 | Four `IsDefinitelyNull` implementations combine the same three oracles - the operation's `ConstantValue`, the abstract flow, and `DefiniteOperationFacts` - in three different ways, and two of them disagree. `ConversionEffectClassifier` consults the abstract flow only. `UsingDisposalEffectResolver` accepts a constant `null` *or* the abstract flow. `ExceptionHandlerReachability` accepts the abstract flow (via `ProvesNull`, a different API) *or* a constant `null` *or* `DefiniteOperationFacts`. `ManagedAbstractFlow` exposes a fourth, purely syntactic static. The consequence: when no abstract flow is available, `ConversionEffectClassifier.IsDefinitelyNull` returns false for a literal `null` where `UsingDisposalEffectResolver.IsDefinitelyNull` returns true. Whether that asymmetry is intended is an owner question about abstract-value semantics, which is exactly the class deferred under R078-R085. This is filed as an observation for a soundness owner, **not** as a proposal to merge the four into one - merging them would change verdicts. | `SharpProof.Effects/ConversionEffectClassifier.cs:309-313`; `SharpProof.Effects/UsingDisposalEffectResolver.cs:255-260`; `SharpProof.Effects/ExceptionHandlerReachability.cs:1965-1970`; `SharpProof.Effects/ManagedAbstractFlow.cs:2879` |

### Root files and small helpers

| ID | Finding | Evidence |
|---|---|---|
| R288 | `.gitattributes` is 59 lines carrying exactly one active directive, `* text=auto eol=lf`. The other 58 are the stock Visual Studio template's commented-out merge drivers for project types this repository does not have (`.vbproj`, `.vcxproj`, `.dbproj`, `.wixproj`, `.modelproj`, `.sqlproj`, `.wwaproj`), `astextplain` diff rules for Word and PDF, and image binary rules. This is the same class as rejected R155, but the ratio is far more extreme - 98 percent inert here against `.gitignore`'s 203 active patterns in 301 lines - so it is recorded for an explicit decision rather than assumed rejected by that precedent. The one live directive matters and must survive: `eol=lf` underpins the Linux-only container contract. | `.gitattributes` |
| R289 | `Test-OrdinalStringSequenceEqual` is 18 lines for what `[Linq.Enumerable]::SequenceEqual($Left, $Right, [StringComparer]::Ordinal)` expresses in one. Note that its sibling `Get-OrdinalSortedUniqueStrings` in the same module is **not** reducible the same way: `Sort-Object -Unique -CaseSensitive` is culture-aware rather than ordinal, so the explicit `HashSet` plus `List.Sort([StringComparer]::Ordinal)` is the correct form and should stay. | `scripts/SharpProof.MutationEvidence.psm1:295-333` |

### Checked and not proposed (part four)

- **There are no dead production types.** A scan for `internal` and `public`
  types with zero references from any other production file returned 75
  candidates, and every one resolves: MSBuild tasks, analyzers, and source
  generators are loaded reflectively and so have no direct callers; the rest are
  either file-local helper types or types reached through member access rather
  than by name, such as the `Spec*Facet` records consumed as
  `spec.Facets.Effects.Effects`. `CA1811` as a warning-as-error is doing its job.
- **The write-only-facet hypothesis does not hold on the current tree**, which
  supports the R019/R224 deferral rather than reopening it. All six
  `ApiSpecFacets` members are read in production (`Effects` and `Allocation` in
  `ApiSpecResolution`, `Throws`, `Nullness`, `Cardinality`, and `Termination`
  elsewhere), and `template.Postconditions` is read by `CompilerCallableLowerer`
  and `ApiSpecInstantiator.InstantiatePostconditions`.
- `SharpProof.Effects.Test/EffectAnalysisTests.cs` is the largest file in the
  repository at 8,987 lines, but it holds 147 distinct test attributes at roughly
  61 lines each and already routes through the shared `Analyze(` helper from
  applied R056. No mechanical reduction is available beyond the fixture
  parameterization already deferred under R087-R096.
- `.gitignore` has 203 active patterns in 301 lines and remains the R155 case:
  generic but functioning, and not worth churning.
- `global.json` and `NuGet.Config` are minimal and load-bearing: a pinned SDK with
  `rollForward: disable`, and a cleared-then-single-source feed with audit sources
  and package-source mapping. Nothing to remove.

### Status (part four)

R285 and R286 remain `pending` because cross-assembly operation traversal
sharing would add a dependency seam. R288 is applied: `.gitattributes` retains
the live LF normalization rule and drops inert template directives. Applied
R289 replaces the private sequence helper with the ordinal framework comparer.
R287 is not a reduction and should not
be treated as one: it is a possible soundness inconsistency between four
predicates that answer the same question, and merging them would change analysis
verdicts. It belongs to an owner of the abstract-value semantics, under the same
deferral as R078-R085.

## Second survey, part five: R290-R293, and a correction to R229

The packaging layer. This part supersedes part one's R229, which undercounted
the problem.

### Correction to R229

R229 recorded the analyzer dependency closure as declared **twice**, in
`SharpProof.AnalyzerConsumer.props` and `SharpProof.Package/buildTransitive/SharpProof.targets`.
That is wrong. It is declared **four times over the same 22 assemblies**, with a
fifth partial copy, and R290 below replaces R229's evidence and sizing. Treat
R229 as merged into R290; do not count them separately.

### The package payload closure

| ID | Finding | Evidence |
|---|---|---|
| R290 | **Supersedes R229.** The analyzer package's shared-assembly closure - the same 22 assemblies - is hand-maintained in four places, in four different spellings, plus a fifth partial copy: `SharpProof.AnalyzerConsumer.props` lists 22 `<Analyzer Include>` entries under a `bin\$(Configuration)` path; `SharpProof.Package/buildTransitive/SharpProof.targets` lists the same 22 under `$(_SharpProofSharedDirectory)`; `SharpProof.Package/SharpProof.nuspec` lists 32 `tools\shared\netstandard2.0` entries (the 22 dlls plus 10 pdbs) under source-tree `bin` paths; and `PackageLayoutSmokeTests.ExpectedConditionalAnalyzerEntries` lists 26 package-relative paths covering the same 22 plus the entry points and catalog. `PackageLayoutSmokeTests.ExpectedAnalyzerDependencyFileNames` is a fifth, 15-element bare-name subset. The verifier package repeats the pattern with its own closure: 33 `tools/net9` entries in `SharpProof.Verifier.nuspec`, 22 in `ExpectedToolEntries`, and an 8-entry `runtimeCompanionFiles` list in `LauncherArguments.catalog.json` that *is* generated (into `LauncherRuntimeCompanionInventory`) and so shows what the declarative treatment of this data looks like. Adding one transitive dependency means editing four to five lists across three languages. | `SharpProof.AnalyzerConsumer.props:35-49,60-67`; `SharpProof.Package/buildTransitive/SharpProof.targets:20-35,46-53`; `SharpProof.Package/SharpProof.nuspec:33-69`; `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:36-52,84-110,112-134`; `SharpProof.Verifier/SharpProof.Verifier.nuspec:28-62`; `SharpProof.Worker.Launcher/LauncherArguments.catalog.json`; `SharpProof.BuildTasks/LauncherRuntimeCompanionInventory.generated.cs` |
| R291 | The two `.nuspec` files restate metadata that `SharpProof.Release.props` already owns, byte for byte. `<description>` is character-identical to `SharpProofProductDescription`, `<authors>` to `SharpProofPublisher`, and `<projectUrl>` and the `<repository url>` to `SharpProofProjectUrl`. `<license type="expression">MIT</license>` and `<readme>README.md</readme>` likewise duplicate `PackageLicenseExpression` and `PackageReadmeFile` from `SharpProof.PackageMetadata.props`. The nuspec token-substitution mechanism is demonstrably already in use in these very files - `$version$`, `$configuration$`, `$repositorycommit$`, `$nativeroot$` - so the duplication is not forced by the format. | `SharpProof.Package/SharpProof.nuspec:4-16`; `SharpProof.Verifier/SharpProof.Verifier.nuspec:4-16`; `SharpProof.Release.props`; `SharpProof.PackageMetadata.props` |
| R292 | Every symbol file is paired with its assembly by hand: 13 explicit dll/pdb pairs in `SharpProof.nuspec` and 11 in `SharpProof.Verifier.nuspec`, roughly 48 lines. The pairing is exactly regular - every SharpProof-owned dll has a pdb entry and only third-party assemblies lack one - so a `SharpProof.*.pdb` wildcard reproduces the current set precisely, and `src` wildcards are supported by the nuspec format. The maintenance argument is stronger than the line count: today, shipping symbols for a newly added SharpProof assembly is a thing a human has to remember. | `SharpProof.Package/SharpProof.nuspec:39-69`; `SharpProof.Verifier/SharpProof.Verifier.nuspec:33-62` |
| R293 | Nothing reconciles the `.nuspec` `<files>` lists against the props/targets closure or the generated `LauncherRuntimeCompanionInventory` statically. No script or test reads the source `.nuspec` files for this purpose - the only reconciliation is `PackageLayoutSmokeTests` asserting over an already-packed `.nupkg`, and its expectations are themselves two more hand-maintained copies of the same vocabulary (R290). So the check that would catch drift is written in the same words as the thing it checks, and it only runs after a full pack. Whatever is done about R290, this is the part worth fixing first: derive at least one of the five lists from another, so a mismatch is a build failure rather than a pack-time surprise. | `scripts/Test-SharpProofPackagePayloads.ps1`; `SharpProof.Package.Test/PackageLayoutSmokeTests.cs`; no reader of `SharpProof.Package/SharpProof.nuspec` outside packing |

### Checked and not proposed (part five)

- `LauncherArguments.catalog.json` -> `LauncherRuntimeCompanionInventory.generated.cs`
  -> `InvalidatePublishedResult` is the pattern the rest of R290 should follow:
  one declaration, generated into code, consumed by name, cross-checked by
  `LauncherArgumentTests`. It is working and should not be disturbed.
- The nuspec ordering oddities (`RelationalSpecPackCatalog.json` sitting between
  the `SharpProof.Specs` dll/pdb pair and `SharpProof.Summaries.dll`; `libz3.so`
  sitting between `System.Text.Json.dll` and the launcher block) are cosmetic
  evidence of hand-editing, not defects, and are not worth a separate item.
- `SharpProof.Package` and `SharpProof.Verifier` set `ImplicitUsings=disable` and
  carry no `GlobalUsings.cs`, so they are correctly outside R234's scope.

### Status (part five)

R290 through R293 are `pending`. R290 is the largest single structural finding of
this survey and R293 is its cheapest partial mitigation; if only one thing from
parts one through five is done, R293 is the one that converts a class of silent
drift into a build error. R291 and R292 are mechanical.

## Second survey, part six: R294-R295, and a correction to R254

The public `SharpProof.Attributes` surface. This part supersedes part one's
R254, which - like R229 before it - undercounted.

### Correction to R254

R254 recorded the effect-capability vocabulary as declared **three** times, all
of them internal catalogs. That missed the public assembly entirely. The same
14-member set is written out **eight** times, and the copy that matters most -
the one users compile against, which cannot change without a breaking release -
is hand-written C# that no generator produces. R294 replaces R254's evidence and
sizing. Treat R254 as merged into R294; do not count them separately.

### One vocabulary, eight declarations

| ID | Finding | Evidence |
|---|---|---|
| R294 | **Supersedes R254.** The capability vocabulary - `None` plus `IO`, `FileRead`, `FileWrite`, `Network`, `Console`, `Process`, `Environment`, `Registry`, `Clock`, `Randomness`, `Reflection`, `Synchronization`, `NativeInterop` - is declared eight times. Member-by-member comparison confirms all eight agree exactly today; only sentinel members differ (`EffectCapabilityKind` adds `AllKnown` and `Unknown`, `WorkerEffectCapabilitySet` adds `AllKnown`). The declarations are: (1) `SharpProofCapability` in the public Attributes assembly, hand-written C# using `1 << n`; (2) 14 entries in `PublicAPI.Shipped.txt`; (3) 14 entries in the hand-written `SharpProof.Attributes.xml`; (4) `EffectCapabilityKind` and (5) `EffectContractCapabilityKind`, both in `EffectContractMappings.catalog.json` with literal values; (6) `WorkerEffectCapabilitySet` in `ProtocolModel.schema.json`; (7) the 14-row identity table in `CompilerArtifactModel.schema.json` that bridges (5) to (6); and (8) the 13-row `capabilities` identity table in the Effects catalog. The effect vocabulary repeats the shape at 17 members across five declarations plus its own 17-row identity table: `SharpProofEffect`, `PublicAPI.Shipped.txt`, `SharpProof.Attributes.xml`, catalog `EffectContractKind`, and schema `WorkerEffectSet`. Only (2) is mechanically protected, by `RS0016`/`RS0017` as warnings-as-errors; the identity tables in (7) and R255 protect (5) against (6) at generation time. Declarations (1), (3), (4), and (6) are independently hand-authored, and adding one capability means editing at least six files across three formats. This also reframes R255 and R256: those identity tables are not gratuitous, they are the seam that keeps two already-identical declarations aligned, so they should be removed only together with the duplication that made them necessary. | `SharpProof.Attributes/SharpProofCapability.cs`; `SharpProof.Attributes/SharpProofEffect.cs`; `SharpProof.Attributes/PublicAPI.Shipped.txt`; `SharpProof.Attributes/SharpProof.Attributes.xml`; `SharpProof.Effects/EffectContractMappings.catalog.json:10-27,97-111,170-184`; `SharpProof.Worker.Protocol/ProtocolModel.schema.json:538`; `SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json` |
| R295 | `SharpProof.Attributes.xml` is 268 hand-maintained lines of XML documentation, packed to `lib/netstandard2.0/` for consumer IntelliSense, for an assembly whose sources contain **zero** `///` comments. The project does not set `GenerateDocumentationFile`, so the compiler never emits a file to compare against and nothing detects divergence between the hand-written `T:`/`M:`/`P:`/`F:` member ids and the actual assembly. It is in sync right now - a member-by-member check found every public type and every enum member documented, with no orphaned entries - but that is maintenance discipline, not a guarantee. Moving the prose onto the members as `///` comments and enabling `GenerateDocumentationFile` makes the file generated and correct by construction, removes 268 hand-maintained lines, and removes declarations (3) of both vocabularies in R294. Note the repository's own `.globalconfig` already records the related motivation: "IDE0005 cannot run during command-line builds unless every project emits an XML documentation file." The cost is handling `CS1591` for anything left undocumented. | `SharpProof.Attributes/SharpProof.Attributes.xml`; `SharpProof.Attributes/SharpProof.Attributes.csproj`; `.globalconfig:2-5` |

### Checked and not proposed (part six)

- `PublicAPI.Shipped.txt` carrying 81 entries while the package version is
  `1.0.0-preview.1` (nothing has actually shipped) is a release-process question,
  not a reduction, and `RS0016`/`RS0017` as warnings-as-errors mean the file is
  doing real work either way.
- `SharpProof.Attributes.xml` is currently accurate; the finding in R295 is the
  absence of enforcement, not an observed defect. Do not record it as drift.
- The `SharpProof.Attributes` sources are otherwise minimal: 14 files, 220 lines,
  one type per file, no dead members.

### Status (part six)

R294 and R295 are `pending`. R294 is the root cause behind R254, R255, and R256,
and it is the item to reason about first: the identity mapping tables those
entries propose removing exist *because* of R294, so removing them without
addressing the duplication would delete the only check that keeps two
declarations aligned. R295 is a self-contained prerequisite that removes two of
the eight declarations on its own.

## Second survey, part seven: R296-R298

A consolidated measurement of exact duplication, replacing the scattered counts
in R249, R250, R268, R269, R270, and R271 with one authoritative figure, plus
two findings that census surfaced.

### The duplication is almost entirely in PowerShell

| ID | Finding | Evidence |
|---|---|---|
| R296 | A normalized whole-body census across every tracked source file gives the shape of the problem. **PowerShell**: 13 duplicate function-body groups, 18 redundant copies, 141 redundant body lines across `scripts/*.ps1` and `scripts/*.psm1`. **C#**: 4 duplicate method-body groups, 4 redundant copies, 36 redundant lines across the entire repository excluding generated files - and two of those four are the `ArgumentNullGuard` numeric overloads from R267, which are arguably not duplication at all. The asymmetry is the point: C# here is close to duplication-free because the compiler, `CA1811` as a warning-as-error, and several prior reduction passes have squeezed it, while the PowerShell layer has no compiler, no unused-member diagnostic, and no equivalent pass. Any future effort is better spent on the 141 lines in PowerShell than on hunting further C# copies. Items R249, R250, R268, R269, R270, and R271 are the itemized breakdown of this figure and should be counted against it, not in addition to it. | census over all tracked `*.ps1`, `*.psm1`, `*.cs` |

### Residual of an applied item

| ID | Finding | Evidence |
|---|---|---|

### Near-duplicates the exact census does not count

| ID | Finding | Evidence |
|---|---|---|
| R298 | Two helper families sit just below the exact-match threshold and so are absent from R296's figure. `Get-RequiredProperty` has a **fourth** variant in `Publish-SharpProofRelease.ps1` beyond the three exact copies in R250: same purpose, same `PSObject.Properties` lookup, different parameter names and a different error message, so it does not hash-match. And `ConvertTo-OrdinalSortedArray` in `Test-SharpProofCoverage.ps1` is exactly `Get-OrdinalSortedUniqueStrings` in `SharpProof.MutationEvidence.psm1` minus the deduplication step - one `HashSet` pass apart. Unifying either means picking one error message or one deduplication policy, which is a small behavioural decision rather than a mechanical merge, so both belong in a lower tier than R296's exact copies. | `scripts/Publish-SharpProofRelease.ps1:53-70`; `scripts/Generate-BoundContractModel.ps1:23`; `scripts/Generate-DeclarativeModels.ps1:18`; `scripts/Generate-ProjectionCatalog.ps1:19`; `scripts/Test-SharpProofCoverage.ps1:31-43`; `scripts/SharpProof.MutationEvidence.psm1:295-312` |

### Checked and not proposed (part seven)

- The C# census result is itself the finding worth remembering: outside the
  `ArgumentNullGuard` overloads, the whole repository contains two duplicated
  method bodies, both in tests (`CountOrdinal` across two architecture test files,
  and the R297 case). Future passes should not spend effort re-searching for C#
  copy-paste; it is not there.
- `Publish-SharpProofRelease.ps1`, `Test-SharpProofCoverage.ps1`, and
  `Invoke-SharpProofCoverage.ps1` were read in full for this census. Beyond the
  helpers in R298 they contain no duplicated bodies; their length is
  release-authority and coverage-attribution logic, not repetition.

### Status (part seven)

R296 is a measurement rather than a work item - it exists so that the itemized
PowerShell findings are not double-counted and so that a future pass knows where
duplication actually lives. Applied R297 closes a gap in already-applied R164.
R298 is deliberately filed below the mechanical tier because each merge
requires choosing between two behaviours.

## Second survey, part eight: R299-R300

Two staleness findings. **R299 is not a reduction candidate - it is a currently
broken release gate on this branch**, found while auditing the acceptance
contract for duplicated authority. It is recorded here because this is where the
evidence is, but it should be fixed rather than queued.

### A pinned catalog identity that no longer matches its catalog

| ID | Finding | Evidence |
|---|---|---|
| R299 | **Defect, not a reduction.** `eng/acceptance/contract.json` pins `mutationEvidence.expectedCatalogCount: 261` and `expectedCatalogSha256: 1c48975c...`, but the mutation catalog in `Test-SharpProofTrustedMutations.ps1` (the `$mutations = @(...)` array, lines 69-2261) now holds **256** entries. A commit-by-commit check locates the divergence exactly: at `fe6b39ce0` and `1b613e3a2` the catalog held 261 and matched; `c63701c40` ("fix: validate portable CodeView age") took it to 262 without bumping the contract; `9c1ee8d62` ("Remove release checksum machinery") dropped it to 257; and HEAD `76dd9d90b` ("Remove inventory and build checksum evidence") leaves it at 256. The pinned SHA-256 is necessarily stale as well, since the catalog contents changed. Consequence: `Invoke-SharpProofTrustedMutationsParallel.ps1:445-447` compares the produced result count and catalog hash against the contract and throws, so `tooling mutation` fails - which is the `nightly` profile and the `release-qualification` job. Nothing catches this earlier, because the only comparison happens against a *produced evidence file* after a full campaign; there is no static check that the catalog array agrees with the contract. That missing static check is the reduction-adjacent part: one assertion comparing `$mutations.Count` and the computed catalog hash to the contract, runnable in seconds, would have failed at `c63701c40` instead of at release qualification. | `eng/acceptance/contract.json` `mutationEvidence`; `scripts/Test-SharpProofTrustedMutations.ps1:69-2261`; `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:39-40,445-447`; `scripts/Test-SharpProofMutationCatalog.ps1:33-50,63`; commits `fe6b39ce0`, `1b613e3a2`, `c63701c40`, `9c1ee8d62`, `76dd9d90b` |
| R300 | `eng/agent-notes/status.md` restates two contract figures and both are now wrong: it claims "the 261-entry mutation catalog identity" (the catalog holds 256, per R299) and "the 348-path TCB inventory" (the contract declares 350 deduped paths - 3 in `trustedKernel` plus 347 across 40 `trustedComputingBase` components, with no overlap). Unlike the audit document, `status.md` presents itself as *active* status rather than dated evidence, yet it is not listed in `$currentMaintainedDocuments` in `Generate-Readme.ps1`, so no currency check validates its numbers. Either add it to the maintained set - which would have caught R299 through the documentation gate - or stop restating contract-owned figures in prose. | `eng/agent-notes/status.md`; `eng/acceptance/contract.json`; `scripts/Generate-Readme.ps1:36-55` |

### Checked and not proposed (part eight)

- **`docs/code-usefulness-audit.md` staleness is by design, not a defect.** The
  document fixes its baseline at commit `18083cd7` and is listed in
  `$datedEvidenceDocuments`, which `Generate-Readme.ps1` deliberately excludes
  from the two currency checks at lines 356 and 1042. Measured against the
  current tree, 12 of its 785 audited paths no longer exist and 218 of the first
  400 still-tracked rows carry a different blob SHA than recorded. That is what a
  dated snapshot looks like after a month of work - but it is worth stating
  plainly, because several deferrals in this ledger cite the audit as evidence
  and roughly half its per-file rows no longer describe the current tree.
- **The `$currentMaintainedDocuments` / `$datedEvidenceDocuments` split is
  behavioural and must not be collapsed.** It looks redundant - the two lists are
  concatenated into `$maintainedDocuments` on the line after they are declared -
  but `$currentMaintainedDocuments` is used on its own at lines 356 and 1042 to
  apply checks that dated evidence is exempt from. This was checked before being
  filed and is *not* a finding.
- `eng/agent-notes/archive/` was explicitly considered and retained by the
  code-usefulness audit as fixed historical evidence, and `docs/README.md` says
  the same. Not revisited here.

### Status (part eight)

R299 is refuted on the current tree: its stale 261-entry and SHA-256 claims
describe the pre-checksum-removal state, while the live registration count and
contract agree at 248 and the count assertion already runs before a campaign.
R300 is applied in the active status document.

## Second survey, part nine: R301-R302

Impact analysis and repository composition. R301 is the operational consequence
of R233 and, like R299, is closer to a defect than to a reduction.

### Shared sources invisible to changed-test selection

| ID | Finding | Evidence |
|---|---|---|
| R301 | `Invoke-SharpProofChangedTests.ps1` builds each project's compiled-file set by parsing `<Compile Include>` nodes out of the **`.csproj`** only. It never reads `Directory.Build.props`, so the five shared test sources that props injects are invisible to impact analysis: `TestRepository.cs`, `TempDirectory.cs`, `TestMetadataReferences.cs`, `DictionaryAnalyzerConfigOptions.cs`, and `ApiSpecTestFacets.cs`. Editing `eng/testing/TestRepository.cs` - which `Directory.Build.props` compiles into **every** test project - matches no project's file set, so selection falls through to the generic `eng/` rule that adds only `SharpProof.ArchitectureTest`. The trap is that `eng/testing/DiagnosticDescriptorCatalogAssertions.cs`, sitting in the same directory, *is* declared in three `.csproj` files and is therefore tracked correctly; nothing at the point of use distinguishes the two kinds. `SharpProof.Ir/IrIdentifierAliases.cs` is also props-injected but happens to be covered, because it lives inside a project directory and so matches that project by path prefix - coverage by file placement rather than by mechanism. This is the operational cost of R233: the build file that injects sources and the script that computes impacted tests read different inputs. Note `Directory.Build.props` *itself* correctly sets `globalImpact` and runs everything; it is only the files it injects that are missed. | `scripts/Invoke-SharpProofChangedTests.ps1:96-108,140-151,163-172`; `Directory.Build.props:70-100` |

### Repository composition

| ID | Finding | Evidence |
|---|---|---|
| R302 | Measured across all 959 tracked files and 286,065 lines: tests 122,141 lines (42.7 percent), production C# 86,341 (30.2), build and release scripts 36,795 (12.9), config and catalogs 16,326 (5.7), docs 7,338 (2.6), lockfiles 6,958 (2.4), generated C# 6,891 (2.4). Tests are the single largest category, larger than the product they test, which is why the deferred fixture-parameterization items (R087-R096) dominate the remaining theoretical headroom and why R296's finding that C# is duplication-free matters. Separately, test-project naming does not track what is actually tested: `SharpProof.Analyzer.Core` has 10,148 production lines and no `Analyzer.Core.Test`, while `SharpProof.Analyzer.Test` holds 17,338 lines against a 40-line shim project (433x), and `ContractForGenerator.Test` holds 2,370 against 28 lines (84x, see R258). Eight substantial production assemblies have no same-named test project: `Analyzer.Core` (10,148), `CompilerArtifact` (7,438), `CompilerCollector` (6,954), `Worker.Protocol` (3,149), `Tools/Fuzz` (3,798), `BuildTasks` (2,483), `Host` (1,853), `Worker.Launcher` (1,619). This is a discovery cost, not a coverage gap - see the note below - and renaming test projects is a large mechanical change with no line-count benefit, so it is recorded as context for future work rather than proposed. | census over all tracked files |

### Checked and not proposed (part nine)

- **Every generated version pin is in sync; only the hand-counted one drifted.**
  The acceptance contract pins protocol 11, cache schema 13, manifest schema 4,
  compiler artifact schema 18, relational summary schema 2, and specification
  pack schema 1, and each matches its generated constant
  (`WorkerProtocolVersions.Current`, `WorkerCacheVersions.Current`,
  `WorkerManifestVersions.Current`, `CompilerManifestArtifactVersions.Current`,
  `CompilerRelationalSummaryVersions.Current`,
  `CompilerSpecificationPackVersions.Current`) exactly. The single pinned figure
  that has drifted is `mutationEvidence.expectedCatalogCount` in R299 - the one
  counted by hand against a hand-maintained array rather than generated. That
  contrast is the argument for the static assertion R299 proposes.
- Every path list in the acceptance contract resolves against the current tree:
  116 `releaseAuthorityClosure` paths, 3 `trustedKernel` paths, 347
  `trustedComputingBase` paths across 40 components, and all 14
  `mutationProjectWeights` project keys. No orphans.
- All 34 diagnostic IDs declared in `eng/diagnostics/diagnostic-descriptors.v1.json`
  are referenced from production code. There is no declared-but-unemitted rule
  beyond the three documented reserved slots (`SP0013`, `SP0015`, `SP0030`).
- **The test-project naming asymmetry causes no selection gap.**
  `Invoke-SharpProofChangedTests.ps1` walks the `ProjectReference` graph
  transitively rather than matching names, so a change in `Analyzer.Core`
  correctly selects every test project that references it. Only the
  props-injected files in R301 escape.

### Status (part nine)

R301 should be treated like R299: a correctness gap in a gate rather than a
reduction, and cheap to close either by having the selection script read
`Directory.Build.props` or by moving the five injected files into the `.csproj`
files that use them - which is also what R233 proposes for unrelated reasons, so
the two fixes coincide. R302 is a measurement recorded as context, not a work
item.

## Second survey, part ten: R303-R305

Fragment-level duplication between scripts. R296 measured whole identical
*functions*; this part measures identical *runs of lines* inside differently
named functions, which R296 cannot see. The two measurements are disjoint and
should be added, not compared.

### Fragment-level overlap between scripts

| ID | Finding | Evidence |
|---|---|---|
| R304 | `Invoke-SharpProofPackageTests.ps1` (799 lines) and `Invoke-SharpProofSemanticTests.ps1` (594 lines) share **123 identical lines in 16 runs**, 22 percent of the smaller file - the largest script-pair overlap in the repository. Roughly half of it is a complete second copy of a bounded parallel `dotnet test` runner: `ProcessStartInfo` construction (6 lines), argument loop and `Start` (8), running-set bookkeeping (5), deadline `Kill($true)` sweep (7), completion drain and `WaitForExit` (10), stdout/stderr echo (6), and coverage argument assembly (5). The rest is shared preamble: parameter block and container preflight (7), `-Fast`/`-NoBuild` validation and module import (6), coverage-enabled derivation and paired-argument validation (9), coverage path resolution (13), isolated output root (15), timing path derivation with the `-fast` suffix (5 and 10), and isolated-output cleanup (6). The decisive detail is that both scripts already import `SharpProof.ContainerExecution.psm1`, which already exports `Invoke-SharpProofParallelDotnetBuilds` - the same bounded-parallel shape for builds. The module is the established home and the precedent exists; the test runner simply was not factored the same way. This refines deferred R072, R074, and R076 with a measurement and a precedent rather than reopening them: their objection - that shared shard, coverage, and timing orchestration centralizes timeout and process semantics - still stands, but it applies to code that is already duplicated once. | `scripts/Invoke-SharpProofPackageTests.ps1:17-28,58-100,247-306,584-678,794`; `scripts/Invoke-SharpProofSemanticTests.ps1:17-28,46-85,135-157,436-531,565`; `scripts/SharpProof.ContainerExecution.psm1:270` |
| R305 | A pairwise census over all `scripts/*.ps1` and `*.psm1` finds **18 file pairs sharing at least 20 lines in runs of at least 5**. Beyond R303 and R304 the notable ones are: `Generate-CompilerArtifactModel` / `Generate-ProtocolModel` (58 lines, 7 runs); `Generate-CSharpScalarSemantics` / `Generate-ContractApiCatalog` (52 lines, 4 runs, 12 percent); `Generate-DeclarativeModels` / `Generate-ProjectionCatalog` (36 lines, 27 percent); `Assert-SharpProofFuzzRunnerResult` / `SharpProof.FuzzEvidenceLifecycle` (32 lines, 15 percent); `Generate-AnalyzerDiagnosticCatalog` / `Generate-OperationSupportCatalog` (27 lines, **42 percent of the smaller generator**); and `Invoke-SharpProofGateEvidence` / `Test-SharpProofDependencyAudit` (27 lines, 17 percent). The generator pairs are the same material as R249, R250, and R268 seen at fragment rather than function granularity, so they should be counted there; the fuzz and gate-evidence pairs are new and unclaimed. | pairwise census over 97 tracked PowerShell files |

### Checked and not proposed (part ten)

- **The `package-consumers.yml` workflow is properly cross-validated, not
  duplicated.** `eng/release/environment-contract.json` declares the publish tag
  ruleset, workflow job ids, environments, variables, and secrets, all of which
  also appear in `.github/workflows/package-consumers.yml` - but
  `Test-SharpProofReleaseConfiguration.ps1` reads both and compares them, so the
  two cannot silently diverge. This is the shape R290 and R299 are missing, and
  it is worth pointing at as the in-repository example of doing it right.
- Three scripts each open-code
  `Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/package-consumers.yml') -Raw`
  before calling the shared `Test-SharpProofSbomAttestationWorkflow`. That is six
  lines of duplicated argument expression, not duplicated logic - the parsing
  itself is already shared. Too small to file.
- The coverage gate does **not** share R301's blind spot: `Test-SharpProofCoverage.ps1`
  constrains `git diff --name-only` to the canonical TCB path list from
  `Get-SharpProofTcbPaths`, recomputed against the production inventory, rather
  than to project-parsed file sets. Shared and injected sources are covered as
  long as they are in the declared TCB.

### Status (part ten)

R304 and R305 are `pending`. R304 is the largest single duplication in the
repository by line count and has a ready home in a module both scripts already
import, but it is process-lifetime and timeout code, so the deferral reasoning in
R072, R074, and R076 governs how carefully it is done - not whether it is real.
R270 and R303 are refuted because the package-integrity pipeline they described
was removed.

## Second survey, part eleven: R306-R307, and a correction to R285

The same fragment-level census applied to production C#. It found exactly one
file pair above threshold - which confirms R296 - and that one pair exposes an
undercount in R285.

### Correction to R285

R285 recorded the nested-callable predicate as implemented in **three** places
and reported the Effects copy as correctly shared within its own assembly. Both
statements were wrong. The predicate exists in **six** places under **three**
names, and three of the six are in `SharpProof.Effects` alone - the assembly R285
described as already sharing it. The name-based scan that produced R285 missed
the copy hiding under a different name. R306 replaces it; treat R285 as merged
into R306.

### The nested-callable predicate

| ID | Finding | Evidence |
|---|---|---|
| R306 | **Supersedes R285.** "Is this operation inside a nested lambda or local function" is implemented six times under three names. Three of them are in `SharpProof.Effects`: `ConversionOwnershipClassifier.IsInsideNestedCallable` (already `internal`, so directly callable by the other two), `UsingDisposalEffectResolver.IsInsideNestedCallable` (`private`, identical body), and `ExceptionHandlerReachability.HasNestedCallableParent` (identical body under a different name - which is why a name-based search missed it). That sub-case needs no project reference, no `Compile Link`, and no cross-assembly decision: two of the three can simply call the one that is already `internal`. The other three are `RequiresCallSiteDiscovery.IsInsideNestedCallable` in `Analyzer.Core`, which is the root-less variant built on a private `Ancestors` iterator, and two in `CacheSoundnessRules` in `Meta.Analyzers` - one over `IOperation` and one over `SyntaxNode`. The `SyntaxNode` twin is a legitimate dual, since it walks a different tree; the `IOperation` one is the cross-assembly copy R285 described, still blocked by `Meta.Analyzers` declaring no `ProjectReference` at all. | `SharpProof.Effects/ConversionOwnershipClassifier.cs:654`; `SharpProof.Effects/UsingDisposalEffectResolver.cs:387`; `SharpProof.Effects/ExceptionHandlerReachability.cs:2732`; `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1062`; `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1372,1819` |

### Duplicated disposal-reachability computation

| ID | Finding | Evidence |
|---|---|---|
| R307 | `ExceptionHandlerReachability` and `UsingDisposalEffectResolver`, both in `SharpProof.Effects`, each contain their own copy of the `using`-declaration acquisition loop: the same `List<(ITypeSymbol Type, IOperation Resource, IOperation Origin)>`, the same `allInitializersComplete` and `reachableDisposalCount` tracking, the same `SelectMany` over declarators, and the same `scopeExitReachable && allInitializersComplete` conclusion. The bodies are identical apart from calling `CanExitAbruptly(...)` as a method in one and `canExitAbruptly(...)` as a delegate parameter in the other - which is exactly the seam a shared helper would take as an argument. This is 18 of the 33 shared lines between the two files. It matters more than its size: the two copies decide *which resources count as reachably disposed*, and exception-reachability analysis and disposal-effect analysis must agree about the same `using` statement. They agree today because the code is identical; nothing keeps them agreeing. Unlike R287, this is not an existing inconsistency, so it is a clean shared-helper candidate rather than a soundness question - but it is still `SharpProof.Effects` traversal code and belongs under the same care as deferred R078-R085. | `SharpProof.Effects/ExceptionHandlerReachability.cs:2086-2112,2382`; `SharpProof.Effects/UsingDisposalEffectResolver.cs:141-168,204` |

### Checked and not proposed (part eleven)

- **The C# fragment census confirms R296 rather than adding to it.** A pairwise
  comparison of every production C# file pair, matching runs of six or more
  normalized lines, found exactly **one** pair above an 18-line threshold: the
  R306/R307 pair. R296 measured whole identical functions and found four groups;
  this measured shared fragments inside differently named functions and found one
  pair. Both point the same way - production C# is close to duplication-free, and
  the remaining copy-paste lives in PowerShell (R303, R304, R305).
- The two `CacheSoundnessRules` copies are not both surplus: one takes
  `IOperation` and one takes `SyntaxNode`, and the analyzer genuinely walks both
  trees. Only the `IOperation` one is a candidate.

### Status (part eleven)

R306 and R307 are `pending`. The `SharpProof.Effects`-internal part of R306 is the
cheapest real cleanup found in production C# during this whole survey: three
copies in one assembly, one of them already `internal`, no cross-assembly
question to settle. R307 is small but should be done deliberately, because the
computation is soundness-bearing even though the copies currently agree.

## Second survey, part twelve: R308-R309

The fragment-level census applied to the 176 test files. The headline result is
negative and useful: the test projects are **not** duplicated at scale. Two
specific harnesses are.

### Duplicated test harnesses

| ID | Finding | Evidence |
|---|---|---|
| R308 | `CompilerArtifactModelSchemaTests.cs` and `ProtocolModelSchemaTests.cs` sit in the same assembly and same directory, ask the same question - does the generated C# conform to its schema JSON? - and each carries its own copy of the machinery to ask it: a 20-line recursive `SchemaType(Type, ...)` renderer that maps CLR types to schema type strings (special-casing `long`, stripping the backtick from generic names, joining generic arguments recursively), an 11-line `switch (declaration.GetProperty("kind").GetString())` dispatch over `staticClass`/`enum`/`class` with the matching `IsAbstract`/`IsSealed` assertions, and an 11-line JSON wire-property-order assertion. 51 shared lines in 4 runs, 10 percent of the smaller file. Because both files are in `SharpProof.Worker.Test`, extracting a shared internal harness needs no project reference, no linked source, and no cross-assembly decision - the two schema suites simply become the same harness parameterized by schema. | `SharpProof.Worker.Test/CompilerArtifactModelSchemaTests.cs:51,599,884`; `SharpProof.Worker.Test/ProtocolModelSchemaTests.cs:58,419,623` |
| R309 | **24 hand-written synthetic `namespace SharpProof.Attributes { ... }` source fixtures** are embedded as raw string literals across 12 files in 6 test assemblies: `Analyzer.Test` (9 across 3 files, 7 of them in `ContractApiIdentityAnalyzerTests.cs` alone), `Effects.Test` (6 across 3), `Contracts.Test` (4 across 3), `ContractForGenerator.Test` (3), `Frontend.Test` (1), and `Worker.Test` (1). Each re-declares some subset of the public contract surface - `Contract.ConditionalSymbol`, `Requires`, `Ensures`, `Assume`, `Result<T>`, `Old<T>`, plus `NotNullAttribute`, `EffectContractAttribute`, `SharpProofTrustedAttribute` - so a test can compile against a *different* Attributes assembly with a controllable `AssemblyVersion` or a toggled `[Conditional]`. The technique is legitimate and cannot be replaced by referencing the real assembly, which is the whole point of the fixtures. What is surplus is that there is no shared builder: the real `SharpProof.Attributes` can gain, rename, or re-signature a member and all 24 fixtures keep compiling against the old shape, so the tests keep passing while no longer resembling the shipped API. `SharpProof.Effects.Test/EffectTestHost.cs` already demonstrates the intended shape by hosting the fixture for its own assembly, and `eng/testing/` is the established home for cross-assembly test sources, so both the pattern and the location already exist. **Note for R294:** these fixtures deliberately declare *truncated* types - `public enum SharpProofEffect { None = 0 }` - so they are **not** further copies of the capability or effect vocabulary. The drift risk here is against the `Contract` API shape, not against the enums. | 12 files listed above; `SharpProof.Effects.Test/EffectTestHost.cs:105-165`; `SharpProof.Frontend.Test/ContractApiIdentityResolverTests.cs:198-260` |

### Checked and not proposed (part twelve)

- **The test projects are not fragment-duplicated at scale.** A pairwise
  comparison of all 176 test files, matching runs of eight or more normalized
  lines, found only 6 pairs sharing 30 lines or more, and all but one sit between
  3 and 15 percent of the smaller file. The 122,141 test lines measured in R302
  are large because they cover many distinct cases, not because they are copied.
  This materially reframes the deferred fixture-parameterization items
  (R087-R096): there is no large pile of copy-paste waiting to be collapsed
  there, so those deferrals cost less than their line counts suggest.
- The remaining pairs above threshold are accounted for elsewhere or are
  incidental: `AnalyzerModeAndEffectTests`/`ClaimManifestBuilderTests` (67 lines
  but only 3 percent, shared fixture preambles),
  `ContractForValidatorGeneratorTests`/`ContractBinderTests` (49 lines, 4
  percent), and two `ArchitectureTest` pairs already covered by R259's process
  runners.
- The `CacheSoundnessRules` observation from part eleven holds here too: apparent
  duplication between a `SyntaxNode` path and an `IOperation` path is usually a
  real dual, not a copy.

### Status (part twelve)

R308 and R309 are `pending`. R308 is the cheapest of the two - one assembly, no
boundaries to cross - and is a clean parameterization rather than a behavioural
change. R309 is larger and its value is in drift protection rather than line
count: 24 fixtures is not much code, but nothing currently ties any of them to
the API they impersonate.

## Second survey, part thirteen: R310-R311

The production-complexity gate. R310 matters specifically *because* this is a
reduction branch: the mechanism that bounds complexity growth is loosening as a
side effect of the removals, and nothing in the ledger's final gate reclaims it.

### The complexity ratchet only ratchets upward

| ID | Finding | Evidence |
|---|---|---|
| R310 | `Test-ProductionCSharpComplexity.ps1` compares the measured aggregate against the contract with `-le`: it is a one-sided ceiling, not a fixed ratchet. `eng/acceptance/contract.json` pins `maximumExpressionNodes: 218647`, `maximumDecisionPoints: 12875`, `maximumMembers: 5808`. Every removal on this branch lowers the measured values while the ceilings stay put, so the gate keeps passing with monotonically growing slack and stops constraining anything. The rationale's own most recent entry - dated 2026-09-01, this branch - *raised* the ceilings to accommodate added logic, so the branch has already claimed its increases while leaving every decrease unclaimed. The ledger's "Final gate" section asks only for a diff line count and test results; re-measuring and re-tightening the three ceilings to the post-reduction values belongs there too. Otherwise the reduction's most durable benefit - a tighter bound on future growth - is spent rather than banked, and the next feature can grow back into the slack this work created without any gate objecting. This is not a code reduction; it is the step that makes the code reduction stick. | `scripts/Test-ProductionCSharpComplexity.ps1:170-173`; `eng/acceptance/contract.json` `productionComplexity`; the "Final gate" section of this ledger |
| R311 | `productionComplexity.ceilingRationale` is a 2,463-character, 279-word changelog with 9 dated entries, stored as a single JSON string inside a machine-checked contract. The gate validates exactly 26 characters of it - that it contains the literal token `ceilings:218647/12875/5808` - which is 1.1 percent of the field; the remaining 2,437 characters are unchecked prose that grows by a paragraph on every ceiling change. The binding check is genuinely valuable and should stay: it forces whoever moves a ceiling to state the current numbers in the same edit. The accumulated narrative is not contract data, and `CHANGELOG.md` already exists for it. Moving the history out and keeping a one-line current rationale plus the binding token preserves every property the gate actually enforces. | `eng/acceptance/contract.json` `productionComplexity.ceilingRationale`; `scripts/Test-ProductionCSharpComplexity.ps1:33-45` |

### Checked and not proposed (part thirteen)

- **`eng/coverage/baseline.json` is the mirror image of R310 and is much less
  harmful.** It pins `minimumAggregateLinePercent: 86`, a
  `minimumChangedTcbLinePercent` of 73.32, and 23 per-project floors. Like the
  complexity ceiling it is one-sided, so rising coverage leaves the floor behind -
  but a floor that lags actual coverage merely under-protects, whereas a ceiling
  that lags actual complexity actively grants budget for regrowth. The per-project
  values also carry two decimals and look freshly measured, unlike the complexity
  ceilings. Not proposed.
- Worth noting inside that baseline: `SharpProof.ContractForGenerator: 93.92` is
  a coverage floor on the project whose entire production surface is the 14-line
  no-op generator from R258. It is not wrong - the number is real - but it is a
  reminder that per-project coverage floors measure whatever the project happens
  to contain, and this one measures almost nothing.
- The complexity gate's own design is sound in the parts this survey can check:
  it excludes generated files by an approved-outputs list, derives production
  roots from `Directory.Build.props` rather than a hand-list, measures Roslyn
  expression nodes rather than physical lines, and verifies a
  formatting-invariance probe so that reformatting cannot move the numbers. The
  problem in R310 is the comparison direction and the maintenance ritual around
  it, not the measurement.

### Status (part thirteen)

R310 should be added to this ledger's "Final gate" section rather than to the
pending queue: it is a closing step for the reduction work as a whole, not an
individual item, and it is the difference between the branch removing code and
the branch durably lowering the ceiling on future code. R311 is `pending` and
mechanical.

## Second survey, part fourteen: R312

The other half of the complexity gate: what the per-file ratchets actually cover.

| ID | Finding | Evidence |
|---|---|---|
| R312 | `productionCoordinatorComplexity` declares 13 per-file ratchets, each pinning `maximumExpressionNodes` and `maximumDecisionPoints` for one named coordination layer. All 13 paths resolve against the current tree - no stale entries. But they cover **10,044 of 86,656 handwritten production C# lines, 12 percent, across 13 of 281 files**, and only **9 percent of the ten largest files**. The largest ratcheted file, `CompilerImplementationIlSummaryLowerer.cs` at 1,824 lines, is only the seventh largest production file. The six larger ones have no per-file ceiling at all: `ExceptionHandlerReachability.cs` (3,193), `ManagedAbstractFlow.cs` (2,926), `RequiresCallSiteDiscovery.cs` (2,248), `Tools/SharpProof.Fuzz/FrontendFuzzing.cs` (2,033), `CacheSoundnessRules.cs` (1,963), and `PerformanceGate.cs` (1,851). This is a scope choice rather than a defect - the field is named for *coordinators* and its `measurement` says "coordination layers", so excluding analysis engines is deliberate. The finding is what that choice combines with: R310 shows the only repository-wide bound is a one-sided ceiling that this branch's removals are steadily loosening, so for the 88 percent of production C# outside the named layers there is now no effective constraint on growth in either mechanism. The largest and most complex files in the repository are precisely the ones neither gate holds. A reduction branch is the natural moment to decide whether that is intended - either by extending the layer list to the largest analysis files, or by recording explicitly that analysis engines are bounded by review rather than by ratchet. | `eng/acceptance/contract.json` `productionCoordinatorComplexity`; `scripts/Test-ProductionCSharpComplexity.ps1`; file census over 281 handwritten production C# files |

### Checked and not proposed (part fourteen)

- All 13 declared coordinator-layer paths exist and resolve. Unlike the mutation
  catalog in R299, this list has not drifted from the tree.
- The layer ceilings themselves look proportionate to their files - roughly 1.5 to
  2.5 expression nodes per line across the 13 - so they appear to have been
  measured rather than guessed. The concern in R312 is coverage, not calibration.
- `Tools/SharpProof.Fuzz/FrontendFuzzing.cs` (2,033 lines) appears in the
  unratcheted list, but note it is a developer tool rather than shipped product
  code; if the layer list is extended, it is the weakest candidate of the six.

### Status (part fourteen)

R312 is `pending` and is a policy question rather than a code change: nothing here
proposes moving or splitting the six large files. It pairs with R310 - together
they describe a complexity-control system that currently constrains 12 percent of
the code with per-file ceilings and the remaining 88 percent with an aggregate
ceiling that is drifting upward. Deciding that this is acceptable is a valid
outcome; discovering it later is not.

## Second survey, part fifteen: R313

Signature shape across the analysis assemblies (Effects, Worker, Summaries,
Dataflow, Smt). One outlier, and its fix already exists next door.

| ID | Finding | Evidence |
|---|---|---|
| R313 | `UsingDisposalEffectResolver` and `ExceptionHandlerReachability` sit in the same assembly, do closely related work - they are the R307 pair that share the `using`-acquisition loop - and use **opposite conventions for the same problem**. `ExceptionHandlerReachability` takes its analysis-callback bundle once, in a primary constructor: seven `Func<>` predicates (`canCompleteNormally`, `canMethodCompleteNormally`, `canCompoundValueComplete`, `canIncrementValueComplete`, `canWithCloneComplete`, `getReachableListPatternMembers`, `isKnownNonThrowing`) plus `conversionEffects`, `apiSpecs`, and `knownSymbols`, after which every method uses them as captured state. `UsingDisposalEffectResolver` takes only `(compilation, caller, calls, flow)` and then re-threads five predicates - `classifyRegion`, `canCompleteNormally`, `canMethodCompleteNormally`, `canMethodThrow`, `canExitAbruptly` - through the whole call chain, producing a 15-parameter `ResolveResources` and a 13-parameter `Scan`. `canMethodCompleteNormally` alone appears in six of the file's ten signatures. A second bundle rides along with it: `(resourceType, resource, origin)` appears together in four signatures, and the file *already has a tuple type for exactly that triple* - the `List<(ITypeSymbol Type, IOperation Resource, IOperation Origin)>` built in the acquisition loop from R307 - which is destructured back into three separate parameters at every call boundary. This composes with R307: if the resolver adopted its sibling's constructor-bundle shape, both sides would hold the predicates as state and the shared acquisition helper R307 wants would take just the declaration group, rather than needing five delegate parameters to bridge the two conventions. | `SharpProof.Effects/UsingDisposalEffectResolver.cs:18,32,112,200,214,233,262`; `SharpProof.Effects/ExceptionHandlerReachability.cs:6-20` |

### Checked and not proposed (part fifteen)

- **The widest signatures in the repository are not findings.** A census of
  production methods with seven or more parameters returned 87 hits, but the
  extremes are record primary constructors for result carriers -
  `PerformanceGateResult` (29), `CorpusGateResult` (23),
  `AcceptancePerformanceContract` (16), `FrontendFuzzCoverage` (13),
  `FuzzSummary` (12), `OpenSourceCorpusMethod` (10). A wide positional record is
  the correct shape for a result payload, and collapsing them into nested
  structures would be churn. Recorded so a later pass does not "fix" them.
- **Callback threading is not systemic.** Across all of Effects, Worker,
  Summaries, Dataflow, and Smt, the only parameters threaded through five or more
  signatures in one file are `cancellationToken` (in `EffectAnalysisSession`,
  `EffectMethodNodeBuilder`, `ManagedAbstractFlow`, and `IrSmtBackend`), which is
  idiomatic and correct, and `canMethodCompleteNormally` in the single outlier
  above. R313 is one file, not an assembly-wide pattern, and is scoped that way
  deliberately.
- `AcyclicBlockPredicateExecutor.Execute` (12 parameters) and `.Run` (14) are
  genuine wide methods rather than record carriers, but their parameters are
  distinct symbolic-execution inputs rather than a repeated bundle - no two of
  them travel together across signatures. Not proposed.

### Status (part fifteen)

R313 is `pending` and should be sequenced with R307: doing the constructor-bundle
change first makes the shared acquisition helper straightforward, whereas doing
R307 alone would require passing the five predicates into the new helper and
would leave the wide signatures in place. Both are `SharpProof.Effects` traversal
code and inherit the care required by deferred R078-R085.

## Second survey, part sixteen: R314-R315

Enum dispatch across assembly boundaries. R315 is the most soundness-relevant
duplication found in this survey.

### A wire vocabulary with no catalog entry

| ID | Finding | Evidence |
|---|---|---|
| R314 | **Third instance of R280.** The `CompilerSummaryOrigin` to wire-prefix mapping - `Source` to `"source-summary"`, `ImplementationIl` to `"il-summary"`, `SpecificationPack` to `"spec-pack"` - is hand-written three times across two assemblies, nine string literals in total: once as a named `SummaryPrefix` helper in `CompilerResponseEvidenceAuthority` (returning `null` on unknown), and twice inline in `CallableEvidenceBuilder` (returning `string.Empty` in one and throwing in the other, with the third arm extended to `"spec-pack:" + item.EvidenceIdentity`). The token also serves as an identifier namespace in a **fourth** assembly, where `CompilerSpecificationPackProvider` builds `"spec-pack:parameter:N"`, `"spec-pack:result"`, `"spec-pack:entry"`, and `"spec-pack:return"` by hand, and a test hard-codes `"spec-pack:dotnet.scalar@1:"`. None of these tokens appears in any catalog or schema. The decisive detail is that `SharpProof.Projection.catalog.json` already contains the identical shape, generated: `WorkerProjections.ClauseLabel` maps `CompilerContractKind` to `"requires"`/`"assume"`/`"ensures"`, and `LauncherPresentation.EffectKind` maps `WorkerEffectContractKind` to its names. One enum-to-wire-token projection is declared once and generated; its sibling is typed out three times in two assemblies. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:516-522`; `SharpProof.Worker/CallableEvidenceBuilder.cs:215-221,235-243`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:152,184,187,191`; `SharpProof.Projection.catalog.json` |

### The effect-contract violation rule, twice

| ID | Finding | Evidence |
|---|---|---|
| R315 | The rule that decides **whether an observed effect witness violates a contract** is implemented twice, in two assemblies, and the two copies must agree for the system to be sound. `CompilerResponseEvidenceAuthority` uses it to judge whether compiler-supplied evidence is valid; `EffectCounterexampleReplayer` uses it to judge whether a replayed counterexample actually demonstrates a violation. Both contain: an identical eight-member `const WorkerEffectSet impureState` mask (`ReadsCapturedState | ReadsStaticState | ReadsAmbientState | WritesReceiverState | WritesArgumentState | WritesCapturedState | WritesStaticState | WritesAmbientState`), declared verbatim in both files and nowhere else in the repository; the same `unexpectedCapabilities = Capabilities & ~Constraint.AllowedCapabilities` derivation; and the same six-arm `switch (evidence.ContractKind)` over `EnforcePure`, `ZeroAllocations`, `AllowedCapabilities`, `DoesNotThrow`, `AllowedExceptions`, and `EffectContract`, with matching bodies. They differ only in that the replayer factors the forbidden-exception test into a named `HasForbiddenException` helper and reuses it for two arms, whereas the authority inlines the same three-line expression twice - so the *replayer* is the better-factored copy of the shared rule. The direction of any fix is already open: `SharpProof.Worker` has a `ProjectReference` to `SharpProof.CompilerArtifact`, so the Worker copy can call into the authority. Like R307 this is not an existing inconsistency - the two agree today - but the stakes are higher: R307 duplicates a traversal detail, whereas this duplicates the *definition of a violation*. If a contract kind is added or the impure-state mask is adjusted in one place only, the evidence authority and the replayer will disagree about the same witness, and the disagreement will not be a compile error. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:645-683`; `SharpProof.Worker/EffectCounterexampleReplayer.cs:225-268`; `SharpProof.Worker/SharpProof.Worker.csproj:20` |

### Checked and not proposed (part sixteen)

- A census of enum types dispatched by `switch` at three or more production sites
  returned 14 enums, but most are legitimately local: `GeneratedExpressionKind`
  (8 sites, all inside the fuzz tool), `ILOpCode` (3 sites, all inside the IL
  lowerer), `PartialTermSemanticOutcome` (3 sites, one file), `CorpusVariant` (2
  sites, one file). Repeated dispatch inside a single file over that file's own
  vocabulary is not duplication.
- `IrBinaryOperator` is switched at 5 sites across `Ir`, `Smt`, and `Testing`,
  but the three do genuinely different things - interpretation, SMT encoding, and
  C# rendering for the differential oracle - so they are three translations of one
  vocabulary rather than three copies of one mapping. Applied R053 already
  table-drove the part of this that was mechanical, and rejected R040 already
  settled that a dictionary is not an improvement over a closed switch here.
- `CompilerVariableRole` (3 sites) and `SpecTargetMemberKind` (2 sites) are small
  dispatches whose arms differ in what they compute, not just in a returned name.
  Not proposed.

### Status (part sixteen)

R314 is `applied`: compiler-artifact owns the summary-origin prefix mapping and
the worker and collector reuse it, preserving their existing unknown-origin
handling. R315 is `applied`: response authority and counterexample replay now
share one effect-contract violation classifier, including the impure-state mask
and forbidden-exception handling. It remains the item in
this survey most worth an owner's attention: it is small in line count, currently
correct, and positioned so that a future edit to one copy silently desynchronizes
a soundness rule across an assembly boundary. It belongs with R287 in the set of
findings that are about *risk of divergence* rather than about size.

## Second survey, part seventeen: R316

Assembly-visibility declarations, and a check of the process-lifetime code in
`BuildTasks` and `Host`.

| ID | Finding | Evidence |
|---|---|---|

### Checked and not proposed (part seventeen)

- **The three process-deadline implementations are legitimately distinct, and
  this confirms deferred R032-R034 rather than reopening them.**
  `RunVerifier` (MSBuild task), `VerifierProcessSupervisor` (supervisor process),
  and `LinuxWorkerProcess` (worker host) each implement "wait for a process with a
  deadline, then escalate to `SIGKILL`", but with genuinely different policies:
  `RunVerifier` computes a process timeout with an added grace window and a
  separate inner verifier deadline; the supervisor polls `WaitForExit(25)` then
  allows `WaitForExit(1000)` before signalling; the host polls `WaitForExit(0)`
  against an interlocked `_terminationDeadlineTimestamp` that another thread can
  move. Those are three roles with three failure semantics, not three copies. The
  extractable part is the shared *constants* they duplicate - the handshake tokens
  in R277 and the POSIX signal numbers in R278 - and those are already filed.
- The three `SharpProof.Dataflow` domains (`IntervalDomain`, `NullnessDomain`,
  `SequenceCardinalityDomain`) each implement `Join`, `Widen`, `LessThanOrEqual`,
  and `Havoc`. That is `IAbstractDomain` being implemented three times, which is
  the point of the interface, not duplication. Applied R222 already shared the
  lattice property *tests*; the implementations themselves are distinct
  abstract-domain algebras.
- `SharpProof.Smt`, `SharpProof.Summaries`, and `SharpProof.Verify` produced no
  findings in the whole-body census (R296), the fragment census (part eleven), the
  wide-signature census (part fifteen), or the enum-dispatch census (part
  sixteen). `IrSmtBackend.cs` threads `cancellationToken` through seven
  signatures, which is idiomatic. These three assemblies appear clean.

### Status (part seventeen)

Applied R316 consolidates all IVT-only assembly metadata into the SDK item form;
projects retaining other assembly attributes (such as NUnit parallelism) keep
their `AssemblyInfo.cs` files. The two duplicate grants in `SharpProof.Ir` were
idempotent and are now represented once in the project metadata.

## Second survey, part eighteen: R317

Repository documents that make checkable claims nothing checks.

| ID | Finding | Evidence |
|---|---|---|

### Checked and not proposed (part eighteen)

- **Supporting detail for R247.** `.github/dependabot.yml` configures exactly two
  ecosystems, `nuget` (daily, with Roslyn and test-platform groups and deliberate
  `>= 4.15.0` ignores to hold the documented Roslyn 4.14 host line) and
  `github-actions` (weekly). It does **not** configure `npm`. Combined with
  `Test-SharpProofDependencyAudit.ps1` covering NuGet only, this means the
  431-line `.opencode/package-lock.json` sits outside *both* the repository's
  dependency-audit gate and its automated update path - it has no vulnerability
  reporting and no update mechanism of any kind. That strengthens R247 without
  changing it.
- `CHANGELOG.md` is current and correctly shaped: a single `Unreleased` section
  for a repository at `1.0.0-preview.1`, with no stale released-version headings.
  Not a finding.
- `.github/CODEOWNERS` is one line (`* @alexyorke`) and `stale-issues.yml` is a
  standard 34-line action. Nothing to reduce.
- The dependabot `ignore` entries are load-bearing rather than accidental: they
  pin the compiler-facing runtime dependency line to Roslyn 4.14 with an inline
  rationale, which matches the `NETCoreSdkVersion` floor enforced in
  `SharpProof.targets`. Not a finding.

### Status (part eighteen)

R317 is applied: `BUGS.md` now reports its one actual open bug, and both it and
`eng/agent-notes/status.md` are included in `$currentMaintainedDocuments` so the
documentation gate checks their encoding, links, and stale-code claims.

## Second survey, part nineteen: R318

The fuzz tool's configuration, checked against the acceptance contract using the
method that found R299.

| ID | Finding | Evidence |
|---|---|---|
| R318 | `eng/acceptance/contract.json` `fuzz` pins `pullRequestCases: 1000`, `nightlyCases: 10000`, `maximumCampaignCases: 1000000`, and `maximumParallelism: 4`. `Tools/SharpProof.Fuzz/FuzzOptions.cs` independently hard-codes three of the same four as C# constants - `DefaultCases = 1000`, `MaximumCases = 1_000_000`, `DefaultMaximumParallelism = 4` - and **nothing binds the two**: a repository-wide search for those constant names returns no hit outside the file that declares them, so there is no generator, no test, and no assertion comparing them to the contract. A fourth restatement sits in `Test-SharpProofFuzzEvidenceLifecycle.ps1`, which passes a literal `-MaximumCases 1000000` rather than reading the contract. The values agree today. The reason this matters less than R299 but is still worth recording is *where* a drift would surface: every production caller already reads the contract correctly - `Invoke-SharpProofFuzzCampaign.ps1` passes `--cases` and `--max-parallelism` from `$contract.fuzz.*`, and `eng/acceptance/Verify.ps1` uses `$contract.fuzz.pullRequestCases` - so the C# constants are only reached when a developer runs the fuzzer by hand. A divergence would therefore not fail CI; it would quietly give local runs a different budget from the gated ones, which is the harder kind to notice. Related: `FuzzOptions.Parse` is a hand-rolled `switch` over `--cases`/`--seed`/`--max-parallelism` and is one of only two hand-written CLI parsers left in production code - the other being `Worker.Launcher/Program.cs`, which is generated from `LauncherArguments.catalog.json`. That extends R279's observation from the worker to the fuzz tool: of the three command-line surfaces in the repository, one is declaratively generated and two are hand-written. | `eng/acceptance/contract.json` `fuzz`; `Tools/SharpProof.Fuzz/FuzzOptions.cs:7-10,36-45`; `scripts/Test-SharpProofFuzzEvidenceLifecycle.ps1:176,181`; `scripts/Invoke-SharpProofFuzzCampaign.ps1:36-42,67-69,170-175`; `eng/acceptance/Verify.ps1:20-22` |

### Checked and not proposed (part nineteen)

- The fuzz **callers** are correct and should not be touched.
  `Invoke-SharpProofFuzzCampaign.ps1` reads `nightlyCases`,
  `maximumCampaignCases`, and `maximumParallelism` from the contract and validates
  each through `Assert-SharpProofFuzzCaseBudget` before use, and checks the
  resulting parallelism against the contract again after the run. The gap in R318
  is entirely on the C# side.
- `FuzzOptions` itself is well-formed for a hand-rolled parser - it validates
  positive integers, rejects missing values, has an explicit usage exception, and
  bounds cases against `MaximumCases`. R318 is about where its numbers come from,
  not how it parses.
- `Tools/SharpProof.Fuzz` produced no duplication findings in the whole-body
  census (R296) or the production fragment census (part eleven). Its one apparent
  outlier - `GeneratedExpressionKind` switched at 8 sites - is entirely within
  `FrontendFuzzing.cs` and `FuzzRunner.cs` over the fuzzer's own vocabulary, which
  part sixteen already established is not duplication.

### Status (part nineteen)

R318 is `pending` and small. It belongs with R299 as the same species - a
hand-maintained number restating a contract-pinned number with nothing comparing
them - and the two together suggest the general fix is a single assertion that
walks the contract's numeric leaves and checks each against its consumer, rather
than four separate one-off bindings.

## Second survey, part twenty: R319, and an exhaustive contract-value sweep

Every numeric leaf in `eng/acceptance/contract.json` was extracted and searched
for as a standalone literal across all tracked `.cs`, `.ps1`, `.psm1`, `.props`,
`.targets`, and `.yml` files. The result is mostly reassuring, and it bounds the
R299 class: two unbound restatements exist in the whole repository, R318 and the
one below.

| ID | Finding | Evidence |
|---|---|---|
| R319 | `worker.queryRlimit = 3000000` is declared four times, and three of the four are deliberately checked: the contract itself; a hard-coded `Expected = 3000000` literal in `eng/acceptance/Verify.ps1` (an intentional two-person rule, see below); and `SharpProofVerifyQueryRlimit` in the shipped `SharpProof.Verifier.props`, which `Verify.ps1:519` cross-validates against the contract. The fourth is `SharpProof.Smt/IrSmtBackendOptions.cs:5`, `public const uint DefaultQueryRlimit = 3_000_000`, which nothing compares to anything. As with R318 the live risk is bounded rather than absent: production always passes an explicit budget - `SharpProofWorker.cs:49` constructs `new IrSmtBackendOptions(budgets.QueryRlimit)` - so the constant is reached only through the parameterless `IrSmtBackend()` overload, whose four callers are the two `SharpProof.Smt.Test` files and the two SMT fuzzers in `Tools/SharpProof.Fuzz`. A drift would therefore leave the shipped verifier correct while silently changing the resource budget that the SMT tests and fuzzers run under - which is precisely the budget those suites exist to exercise. `worker.methodRlimit = 20000000` has no equivalent stray copy; it appears only in the contract, the `Verify.ps1` literal, and the cross-validated props. | `SharpProof.Smt/IrSmtBackendOptions.cs:5`; `SharpProof.Worker/SharpProofWorker.cs:49`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:18-19`; `eng/acceptance/Verify.ps1:484-485,519-520`; `SharpProof.Smt/IrSmtBackend.cs:16` |

### Checked and not proposed (part twenty)

- **`eng/acceptance/Verify.ps1`'s 34 `Actual = $contract.X; Expected = <literal>`
  assertions are a deliberate two-person rule, not duplication, and must not be
  "reduced".** The contract declares a value and the acceptance verifier
  independently restates it, so editing the contract alone cannot silently change
  the product - a change must be made in two files, on purpose. Collapsing these
  into `Expected = $contract.X` would make every assertion vacuously true and
  delete the control. This is the same class of intentional redundancy as the
  identity mapping tables in R255, and it is worth flagging loudly because a
  line-count-driven pass would delete it on sight.
- **The container limits are cross-validated.** `container.defaultCpuLimit` and
  `container.defaultMemoryMiB` (40960) are checked against `compose.yaml` by
  `Test-SharpProofContainerContract.ps1:384-387`, which computes
  `defaultMemoryMiB / 1024` to compare against the `40g` form. Contract and
  Compose cannot diverge. Another instance of the repository doing this correctly,
  alongside the workflow cross-validation noted in part ten.
- **The sweep found no other unbound restatements.** Of roughly forty numeric
  contract leaves, the distinctive values all resolve cleanly:
  `maximumCompilerReferenceModuleBytes` (268435456) and
  `maximumCompilerReferenceClosureBytes` (1073741824) appear only in
  `CompilerCompilationModel.generated.cs` (generated) and `Verify.ps1` (checked);
  the schema and protocol versions are generated constants already confirmed in
  sync in part nine; `mutationShardWallSeconds` (3600) matches only coincidental
  hour-in-seconds uses. The remaining apparent matches are small integers - 3, 4,
  5, 8, 32, 100 - whose hundreds of hits across the tree are numeric coincidence,
  not restatement, and were discarded on inspection rather than counted.

### Status (part twenty)

R319 is `pending` and small. Its value, with R318, is that the exhaustive sweep
now bounds the problem: the R299 failure mode - a hand-maintained number
restating a contract-pinned number with nothing comparing them - occurs in exactly
three places repository-wide, and two of them (R318, R319) are fallback defaults
outside the gated path rather than live gate inputs. R299 remains the only one
that currently breaks a gate.

## Second survey, part twenty-one: R320

A reachability sweep over `scripts/`: every one of the 95 tracked PowerShell
scripts and modules was searched for by filename across all 956 tracked files of
every type. The result is one finding and a strong negative.

| ID | Finding | Evidence |
|---|---|---|

### Checked and not proposed (part twenty-one)

- **No orphaned scripts.** Of 95 tracked scripts and modules, 94 are reachable
  from a container command, workflow, test, `.csproj`, or another script. For a
  `scripts/` directory of this size that is an unusually clean result and is worth
  recording so a future pass does not re-run the search.
- **A correction to my own method, and a defence of existing R128.** The first
  version of this sweep omitted `.csproj` files from the searched set and
  therefore reported `Get-SharpProofModuleVersionId.ps1` as referenced only by
  documentation - which would have made the ledger's refutation of R128 look
  stale. It is not: `SharpProof.Frontend/SharpProof.Frontend.csproj:47` invokes
  the script through an `<Exec>` task with the path HTML-escaped. **R128's
  refutation stands.** The corrected sweep searched all 956 tracked files of every
  type.
- The eleven scripts with exactly one non-documentation reference are all
  `Test-SharpProof*Fixtures.ps1` files invoked from their single matching
  `ArchitectureTest` fixture, plus `Invoke-SharpProofLoop.ps1` referenced from
  `BuildSchedulingTests.cs`. A one-to-one script-to-test relationship is the
  intended design of that family, not under-use; it is the same structure
  described in R260.

### Status (part twenty-one)

R320 is applied: the unreferenced developer formatter keeps its apply mode while
dropping the redundant `-Verify` branch and its duplicate whitespace pass. The
build remains the authoritative formatting gate.

## Second survey, part twenty-two: R321 - the documentation gate

`SEMANTICS.md` and the `docs/` set, completing the survey. This part resolves the
theme running through R295, R299, R300, and R317.

| ID | Finding | Evidence |
|---|---|---|
| R321 | The documentation gate in `Generate-Readme.ps1` has two structural gaps. **First, its coverage is 55 percent and the exclusions are where the staleness is.** Of 47 tracked markdown files, 26 are listed in `$currentMaintainedDocuments` (19) or `$datedEvidenceDocuments` (7); the other **21 are under no gate at all** - not even the generic UTF-8/LF, markdown-link, anchor, and fence checks. That set includes `SECURITY.md`, `CONTRIBUTING.md`, `AGENTS.md`, both `AnalyzerReleases` files, all of `eng/agent-notes/`, `eng/release/README.md` and `eng/pilots/README.md` (while `eng/acceptance/README.md` *is* gated), and 4 of the 10 `docs/soundness-notes/` files while the other 6 are gated - an inconsistent split within single directories with no evident rule. The decisive evidence that this gap is not hypothetical: **both stale documents found anywhere in this survey are in the ungated set** - `BUGS.md` with its three mutually inconsistent self-counts (R317) and `eng/agent-notes/status.md` with its stale 261-mutation and 348-path figures (R300). **Second, the checks that do run are a blocklist of past mistakes rather than a positive correspondence check.** Beyond hygiene, the gate forbids three obsolete worker terms (`WorkerVerificationStatus`, `WorkerVerificationReason`, `DeepEnsures`), one obsolete public-key claim by regex, and three obsolete host-bootstrap strings - seven literals, each presumably appended after its own incident, catching only the seven historical mistakes and nothing new. The application is also uneven: `README.md` gets genuine code-derived assertions (analyzer options and values, diagnostic IDs, worker text, protocol enums, version strings), while **`SEMANTICS.md` - 446 lines, the specification of what the analysis means, and the largest maintained document - gets only the generic checks.** It names 78 backticked identifiers, 45 of which are checkable code symbols; a scan confirms **all 45 currently resolve**, so this is about enforcement rather than observed staleness, exactly as in R295. That check took seconds to write. | `scripts/Generate-Readme.ps1:36-65,329-371`; `SEMANTICS.md`; `BUGS.md`; `eng/agent-notes/status.md`; 47 tracked markdown files surveyed |

### Checked and not proposed (part twenty-two)

- **`SEMANTICS.md` is accurate.** Every one of the 45 unambiguous code identifiers
  it names - contract methods, capability names, outcome and reason enum members
  such as `NoModeledNormalReturn`, `ContradictoryPreconditions`,
  `CounterexampleNotReplayable`, `DefiniteViolation`, plus `OperationKind` and
  `WorkerFeatureSet` - resolves against tracked C# or JSON. Do not record it as
  stale; R321 is about the absent mechanism, not a present defect.
- The generic checks that *are* applied are worth keeping and are well built:
  UTF-8/LF enforcement, markdown link and anchor resolution across documents,
  fence parseability including XML and PowerShell fences, and repository-link
  validation inside the diagnostic descriptor JSON. Nothing here proposes
  weakening them.
- `docs/api-spec-catalog.generated.md` sits in the ungated set but is covered in
  practice: it is generated by `Generate-ApiSpecCatalog.ps1`, whose `-Verify` mode
  compares the file byte-for-byte. It should not be added to the maintained list,
  since generated files are the one category that genuinely does not need a prose
  gate.

### Status (part twenty-two)

R321 is `pending`. It subsumes the *mechanism* half of R300 and R317 - adding
`BUGS.md` and `eng/agent-notes/status.md` to `$currentMaintainedDocuments` closes
both of those as a category rather than as instances - while those two items
remain separately valid for the stale content they describe. The second half, a
positive identifier-correspondence check for `SEMANTICS.md`, is the cheapest
durable improvement to the documentation gate found in this survey and is close in
spirit to what R295 proposes for `SharpProof.Attributes.xml`.

## Second survey, part twenty-three: R322-R324

A second pass over `Analyzer.Core`, `CompilerCollector`, `Contracts`, `Specs`,
and `Frontend`, applying the two techniques that only emerged late in this
survey: duplicated flag masks (which found R315) and **method-level**
cross-assembly body similarity. The method-level pass matters because R315 lived
inside two large files, where file-level diffing diluted it below threshold.

| ID | Finding | Evidence |
|---|---|---|
| R322 | The specification-pack identity rule is implemented twice across an assembly boundary, with **divergent strictness**. `CompilerSpecificationPackAuthorityValidation.IsValidPackIdentity` in `SharpProof.CompilerArtifact` and `CompilerManifestArtifactProducer.IsSelectedPackIdentity` in `SharpProof.CompilerCollector` are 84 percent identical: both split the shared generated constant `CompilerSpecificationPackCatalogVersions.PackIdentities` on the same separator, do an ordinal `Contains`, and then split the identity at its last `@`. The private `static readonly char[] PackIdentitySeparators = [';']` is declared separately in each file. The authority version additionally bounds the identity to `Length: > 0 and <= 128` and null-checks the selected-pack array; the producer version does neither. The asymmetry runs in the safe direction - the validator is stricter than the producer, so an over-long identity is rejected rather than accepted - but it means the producer can emit an identity the authority will refuse, and the two definitions of "a well-formed pack identity" can drift further apart independently. The seam for a fix is already open: `SharpProof.CompilerCollector` has a `ProjectReference` to `SharpProof.CompilerArtifact`, and `IsValidPackIdentity` is already `internal` with `<InternalsVisibleTo Include="SharpProof.CompilerCollector" />` declared on the authority assembly, so the producer can simply call it. | `SharpProof.CompilerArtifact/CompilerSpecificationPackAuthority.cs:5,39-52`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs:6,150,163-175`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:25`; `SharpProof.CompilerArtifact/SharpProof.CompilerArtifact.csproj:29` |
| R323 | **Second instance of R281.** `GetStableAdditionalText` is 92 percent identical between `CompilerCompilationCapture` in `SharpProof.CompilerCollector` and `CompilerProbeSnapshot` in `SharpProof.CompilerProbe.TestAsset` - the same `ICompilerAdditionalTextSnapshot` fast path and the same reasoning about `AdditionalTextFile`'s shared `Lazy<SourceText>` not being safe to re-read after generation. R281 already recorded the probe re-implementing `AtomicFile.WriteUtf8`; this is the same relationship for a second piece of product logic, and it strengthens that item rather than standing alone: the probe carries at least two independent copies of `CompilerCollector` behaviour, and the copies encode subtle compiler-host reasoning that is easy to update in one place only. As in R281, the probe already links one shared source file from `SharpProof.Ir` (`HashEncoding.cs`), so the `Compile Include ... Link` mechanism is available and in use. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:423-436`; `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs:559` |

### Checked and not proposed (part twenty-three)

- **The flag-mask sweep found only three repeated masks repository-wide**, and one
  of the three is R315's `impureState`, which it independently confirms. The other
  two are the `AttributeTargets` groups in R324. There is no other duplicated
  multi-member flag combination in production C#.
- **The cross-assembly method-similarity sweep found only two pairs above 75
  percent**, out of 1,175 indexed production methods - R322 and R323. Applied at
  method rather than file granularity specifically to catch the R315 shape, it
  found nothing else of that kind in `Analyzer.Core`, `Contracts`, `Specs`, or
  `Frontend`. Those four assemblies produced no findings in this second pass.
- `CompilerSpecificationPackCatalogVersions.PackIdentities` is a shared generated
  constant that both sides of R322 already consume, so the *catalog* half of that
  vocabulary is correctly declared once. Only the validation rule around it is
  duplicated.

### Status (part twenty-three)

R322 is `applied`: the manifest producer now reuses the compiler-artifact
authority's identity validation, including its length and selected-pack checks,
so the producer and validator cannot drift. R323 folds into R281 and should be
done with it. Applied R324 centralizes the two compile-time target masks without
changing attribute metadata.

With this part, every assembly in the repository has been examined twice - once in
the first pass by size and structure, and once with the later techniques - and the
five assemblies named as under-examined are now covered.

## Second survey, part twenty-four: R325-R326, and a correction to R272

A new technique: extracting every anchored regex literal from every tracked
`.cs`, `.ps1`, `.psm1`, `.props`, `.targets`, and `.json` file and grouping by
pattern. Fourteen anchored patterns appear more than once. Two of them are
findings; the rest independently confirm items already filed.

### Correction to R272

R272 described the exact-commit check as split two ways, between case-insensitive
`-notmatch` and case-sensitive `-cnotmatch`. That was the right observation on the
wrong axis, and it undercounted. The split is **five ways across two independent
axes** - the regex character class *and* the matching operator - over 27 sites.
R325 replaces it; treat R272 as merged into R325.

| ID | Finding | Evidence |
|---|---|---|
| R325 | **Supersedes R272.** "Is this a valid 40-hex commit SHA?" is answered at **27 sites in five different ways**, varying independently in character class and in operator case-sensitivity: `[0-9a-f]` with case-insensitive `-match`/`-notmatch` (14 sites, accepts uppercase); `[0-9a-f]` in C#, `ValidatePattern`, or bash where the operator carries no case flag (6); `[0-9a-f]` with case-sensitive `-cmatch`/`-cnotmatch` (3, the only ones that reject uppercase); `[0-9a-fA-F]` with a case-insensitive operator (2); and `[0-9a-fA-F]` elsewhere (2). So of 27 validations of the same concept, **3 reject an uppercase SHA and at least 18 accept one**. Git emits lowercase, so this rarely bites in practice - but the value that passes these checks is subsequently compared with `Ordinal` equality across the evidence chain, so an uppercase SHA admitted by one gate would fail an equality check downstream rather than being rejected at the boundary. The same split exists one level down for SHA-256: `WorkerProtocolJson.IsSha256` is a strict lowercase-only predicate (`>= '0' and <= '9' or >= 'a' and <= 'f'`), while `Test-SharpProofCoverage.ps1:117` accepts `[0-9a-fA-F]{64}` for the same concept, and 14 test sites across 7 files re-implement `^[0-9a-f]{64}$` inline rather than using the existing helper. | 27 sites including `build.ps1:82`; `scripts/Get-SharpProofProductionInventory.ps1:316`; `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:9`; `scripts/Resolve-SharpProofReleaseCoverageBaseline.ps1:8`; `scripts/SharpProof.PackageIdentity.psm1:85`; `scripts/Test-SharpProofCoverage.ps1:117`; `SharpProof.ArchitectureTest/DependencyAutomationTests.cs:255`; `SharpProof.Worker.Protocol/ProtocolJson.cs:1056-1060` |
| R326 | GitHub's markdown anchor-slug algorithm is implemented twice, **in two languages, with a real divergence**. `scripts/Generate-Readme.ps1:126-136` and `eng/testing/DiagnosticDescriptorCatalogAssertions.cs:190-201` share the identical heading regex `^(?:#{1,6})[ \t]+(?<heading>.+?)[ \t]*#*[ \t]*$` and then both strip HTML tags and backticks, case-fold, drop non-word characters, and hyphenate whitespace. But the PowerShell version additionally rewrites markdown link syntax, `\[([^\]]+)\]\([^)]+\)` to `$1`, and the C# version does not. A heading containing a markdown link therefore produces different slugs from the two implementations. This matters because both are gates over the same artifacts: `Generate-Readme.ps1` validates that every in-document anchor link resolves, while `DiagnosticDescriptorCatalogAssertions` - compiled into three test projects - computes anchors for diagnostic help links. For a link-bearing heading the two would disagree, and at most one of them can match the anchor GitHub actually generates. | `scripts/Generate-Readme.ps1:112-140`; `eng/testing/DiagnosticDescriptorCatalogAssertions.cs:188-205` |

### Checked and not proposed (part twenty-four)

- **The regex census independently confirms four already-filed items**, which is
  useful corroboration from a technique that shares no method with the earlier
  passes: `^[A-Za-z_][A-Za-z0-9_]*$` at 12 sites across 11 generators and its four
  variants at 5, 5, 4, and 3 sites confirm R250 and R268; `\A[0-9a-f]{32}\z` twice
  inside `SharpProof.Verifier.targets` confirms the Initialize/Cleanup duplication
  in R263; and `^[^:]+\.generated\.cs$` twice confirms the
  `Generate-DeclarativeModels`/`Generate-ProjectionCatalog` overlap measured in
  R305.
- `^[0-9]+:[0-9]+$` appears three times across `SharpProof.PublicationDestination.ps1`,
  `SharpProof.PublicationPlanTopology.ps1`, and `SharpProof.ReleaseBundle.ps1`.
  These are three release-authority files validating the same `N:M` shape, but the
  publication family is explicitly deferred under R110-R112 and R156, and a
  three-token pattern is below the threshold where extraction is worth a
  cross-file dependency in release-authority code. Not proposed.
- `^$escapedVersion\s+\[(?<root>.+)\]$` twice within `CSharpSourceMetrics.ps1` is
  one pattern used for two adjacent `dotnet --list-sdks` parses in the same
  function. Not duplication worth extracting.

### Status (part twenty-four)

R325 is `pending`. Its practical priority is low - Git emits lowercase - but it is
cheap to close and the fix is the one already implied by R272's status note: a
single `Get-SharpProofExactCommit` helper, plus deciding once whether uppercase is
valid. R326 is `pending` and is the more interesting of the two, because unlike
most items in this ledger the two copies **already disagree** rather than merely
being able to; it belongs with R287 and R322 in the set of duplications with an
observed divergence rather than a hypothetical one.

## Second survey, part twenty-five: R327-R329

This pass returned to the build graph after checking current MSBuild evaluation,
not just source text. R327 is deliberately narrower than the earlier broad warning-policy
proposals: it targets only exact project-local declarations that the central
policy already provides, while preserving the projects that the central
classification excludes.

| ID | Finding | Evidence |
|---|---|---|

### Checked and not proposed (part twenty-five)

- R269 is now applied: the shared container-execution module owns the dotnet
  wrapper path and checked-command body, while the existing process and timeout
  behavior remains in the callers.
- R327 is now applied: the 22 production-project declarations were removed;
  `SharpProof.Testing` and `SharpProof.CompilerProbe.TestAsset` retain their
  intentional local declarations.
- R328 is now applied: each build entry point keeps its existing property set and
  conditions while using a single semicolon-delimited `CompilerVisibleProperty`
  item, and the evaluation/documentation tests split that list at the boundary.
- R329 is now applied: `_SharpProofResolveVerificationPaths` owns the shared
  default/configured/framework-scoped path selection, while initialization and
  cleanup retain their separate property names and lifecycle timing.

### Status (part twenty-five)

No pending item remains in this part.

## Second survey, part twenty-six: R330-R339

This pass narrowed the remaining build and small-fixture repeats with exact-value
and same-file checks. These are smaller than R327-R329, but they are concrete
maintenance seams rather than style preferences.

| ID | Finding | Evidence |
|---|---|---|
| R331 | **The two custom-nuspec project files copy the same packaging skeleton.** `SharpProof.Package.csproj` and `SharpProof.Verifier.csproj` each import `SharpProof.PackageMetadata.props` and repeat the same `Nullable=disable`, `ImplicitUsings=disable`, `TargetFramework=netstandard2.0`, `IncludeBuildOutput=false`, `GeneratePackageOnBuild=false`, `NuspecBasePath`, `NU5128` suppression, `Copyright`, and `NoPackageAnalysis` settings. Their `_SharpProofPrepareNuspecProperties` targets also share the same name, timing, and `version/configuration/repositorycommit` property prefix. A shared custom-nuspec props/target fragment could own this stable skeleton while leaving package IDs, nuspec filenames, native-root validation, and project references explicit. This refines R291's metadata duplication at the project-file layer. | `SharpProof.Package/SharpProof.Package.csproj:1-20,42-45`; `SharpProof.Verifier/SharpProof.Verifier.csproj:1-20,47-52` |
| R337 | **The stable Roslyn additional-text implementation type name is repeated beside the duplicated guard from R323.** Both `CompilerCompilationCapture` and `CompilerProbeSnapshot` privately declare `CommandLineAdditionalTextTypeName = "Microsoft.CodeAnalysis.AdditionalTextFile"` and compare against it with ordinal equality. R323 already records the larger `GetStableAdditionalText` algorithm duplication; even if the methods remain separate because the probe has no collector project reference, the exact type-name value is a second independent drift point and should be included in that sharing decision. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:58-59,437-442`; `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs:7-8,563-573`; R323 |
| R338 | **Three analyzer components repeat the same `NoWarn` suppression bundle.** `SharpProof.Analyzer.Core`, `SharpProof.Analyzer`, and `SharpProof.CompilerCollector` each append exactly `RS2002;RS2003` to `NoWarn`. These projects share generated diagnostic-descriptor/analyzer infrastructure, while the other Roslyn components use different suppression sets (`RS2008`, `RS1035`, or none). A narrowly scoped analyzer-component property or shared props fragment could remove the three repeated lines, but the reason for suppressing each rule must be confirmed before centralization; this is not evidence that the warnings themselves are unnecessary. | `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:8`; `SharpProof.Analyzer/SharpProof.Analyzer.csproj:8`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:9` |

### Checked and not proposed (part twenty-six)

- R330 is now applied: `LinuxProcessControlConstants` owns the three shared ABI
  values in `SharpProof.Host`, and build tasks consume them through the existing
  host internals boundary; native wrappers and failure semantics remain local.
- R332 is now applied: the SDK's `GeneratePackageOnBuild` default remains
  `false` for Attributes, Package, and Verifier without project-local overrides.
- R333 is now applied: `BuildTaskTests` keeps one class-level
  `ValidSupervisorNonce` fixture and uses local aliases only where a test mutates
  or combines the value.
- R334 is now applied: `CorpusGateTests` uses one class-level
  `CorpusSnapshotHeader` fixture for all schema-3 format cases.
- R335 is now applied: `LauncherArgumentTests` uses one class-level
  `ValidInputHash` fixture for its repeated response-validation cases.
- R336 is now applied: `VerificationCache.CacheFileSuffix` is the single
  production/test authority for cache filenames, including wildcard scans and
  path-validation fixtures.
- R339 is now applied: `Directory.Build.targets` derives the extended Roslyn
  analyzer rules from each project's existing `IsRoslynAnalyzer` marker.
- R340 is now applied: the fuzz project and its test assembly keep their
  existing `FuzzOracleStatus` source spelling through compile-wide aliases, but
  the enum type is owned by `SharpProof.Testing`.
- R345 is now applied: the two analyzer call sites invoke
  `DefiniteOperationFacts.IsDefinitelyString` directly, removing their private
  single-call wrappers.
- R346 is now applied: the protocol wrapper delegates to the linked canonical
  hash implementation with a protocol-local type name, avoiding duplicate
  fully-qualified types when consumers reference both protocol and IR.
- R347 is now applied: `build.ps1` uses one package-source dispatch arm for
  package consumers, pilots, and release plans.
- R348 is now applied: `Invoke-SharpProofContainer.ps1` routes
  `performance-smoke` through the shared Gates restore/run arm.
- R349 is now applied: consumer configuration validation itself hooks
  `CoreCompile`, so the analyzer-consumer import no longer needs an empty shim.
- The repeated `SHARPPROOF_CONTRACTS` string spans the public conditional symbol,
  compilation fingerprinting, and synthetic source fixtures. The fixture copies
  are part of R309, while the fingerprint intentionally has a separate
  `SharpProof.CompilerArtifact` dependency boundary; it is not counted again as
  a simple literal reduction.
- `IsExternalInit.cs` is linked identically into four netstandard2.0 projects,
  but each link is a compatibility requirement for a project that uses `init`
  or records and the source file is already the single authority. A new shared
  props condition would add project classification and is not a net reduction
  without a tested import design.
- `Microsoft.CodeAnalysis.Analyzers` and its `PrivateAssets`/`IncludeAssets`
  metadata repeat across six projects. The projects do not all share the same
  analyzer role, and one (`SharpProof.Analyzer.Core`) is intentionally not
  marked `IsRoslynAnalyzer`, so a blind central package reference would broaden
  dependency scope. No separate item is filed beyond R338-R339.

### Status (part twenty-six)

R331, R337-R338 are `pending`. R331 and R337 touch packaging or cross-assembly
authorities and need boundary-aware implementations. R338 is a smaller,
mechanically testable build reduction.

## Second survey, part twenty-seven: R340-R349

This pass examined cross-assembly test fixtures, MSBuild dependency declarations,
build-task path helpers, PowerShell generator self-duplications, and single-call
forwarders across `SharpProof.Testing`, `Tools/SharpProof.Fuzz`, `SharpProof.BuildTasks`,
`SharpProof.Worker.Protocol`, `SharpProof.Analyzer.Core`, and the build scripts.

| ID | Finding | Evidence |
|---|---|---|
| R341 | **24 test and tooling files independently re-parse `TRUSTED_PLATFORM_ASSEMBLIES` because `TestMetadataReferences.cs` is restricted to two test projects.** `eng/testing/TestMetadataReferences.cs` already provides `CreatePlatformReferences()` caching `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")`, but `Directory.Build.props:83-87` compiles it only into `SharpProof.Contracts.Test` and `SharpProof.Worker.Test`. As a result, 24 test hosts, test fixtures, benchmarks, and fuzzers re-implement the same `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` split on `Path.PathSeparator` to create Roslyn metadata references. Hoisting `TestMetadataReferences.cs` to all test projects (`Condition="'$(SharpProofTestProject)' == 'true'"`, matching `TestRepository.cs`) centralizes reference creation across the test suite. | `eng/testing/TestMetadataReferences.cs:16-25`; `Directory.Build.props:83-87`; 24 sites including `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:259`; `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:197`; `SharpProof.Effects.Test/EffectTestHost.cs:303`; `SharpProof.Frontend.Test/FrontendLoweringTests.cs:1473`; `SharpProof.Package.Test/BuildTaskTests.cs:1645`; `SharpProof.Specs.Test/ApiSpecTests.cs:1186`; `SharpProof.Testing/IrCSharpDifferentialOracle.cs:584`; `SharpProof.Worker.Test/ExceptionIdentityReplayTests.cs:459`; `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1779` |
| R342 | **`GeneratedFileHelpers.ps1` declares duplicate function pairs within itself and is bypassed by generator-local re-declarations.** Within `GeneratedFileHelpers.ps1`, `Get-RequiredMember` (lines 3-17) and `Required` (lines 119-127) are identical; `Assert-Identifier` (lines 99-107) and `Identifier` (lines 129-136) are identical regex guards; `Assert-TypeName` (lines 109-117) and `TypeName` (lines 138-145) are identical; `Assert-EnumName` (lines 74-77) is an alias for `Assert-EnumValue`; and `Assert-CSharpIdentifier` (lines 94-97) is an alias for `Assert-PascalCaseIdentifier`. Furthermore, `Generate-IrModel.ps1:23-70` dot-sources `GeneratedFileHelpers.ps1` but re-defines `Get-OptionalArray` (identical to `Get-MemberArray`), `Get-OptionalString`, and `Get-OptionalBoolean` locally. | `scripts/GeneratedFileHelpers.ps1:3-17,74-97,99-145`; `scripts/Generate-IrModel.ps1:23-70` |
| R343 | **The 15 portable and 7 collector analyzer dependency DLL lists are duplicated in full across three MSBuild entry points.** `SharpProof.Package/buildTransitive/SharpProof.targets:22-56`, `SharpProof.AnalyzerConsumer.props:34-65`, and `eng/self-application/SharpProof.SelfApplication.props:49-67` each enumerate the identical 15 portable analyzer dependency assemblies (`SharpProof.Analyzer.Core.dll`, `SharpProof.Contracts.dll`, `SharpProof.Dataflow.dll`, `SharpProof.Effects.dll`, `SharpProof.Frontend.dll`, `SharpProof.Ir.dll`, `SharpProof.Specs.dll`, plus 8 `System.*` assemblies) and the identical 7 collector dependencies (`Microsoft.Bcl.AsyncInterfaces.dll`, `SharpProof.CompilerArtifact.dll`, `SharpProof.Summaries.dll`, `SharpProof.Worker.Protocol.dll`, `System.IO.Pipelines.dll`, `System.Text.Encodings.Web.dll`, `System.Text.Json.dll`). A shared item definition or props fragment would ensure all three entrypoints resolve the identical dependency closure without drifting when Roslyn dependencies are updated. | `SharpProof.Package/buildTransitive/SharpProof.targets:22-56`; `SharpProof.AnalyzerConsumer.props:34-65`; `eng/self-application/SharpProof.SelfApplication.props:49-67` |
| R344 | **`CancelableBuildTask.ResolveProjectRelativePath` is re-implemented across sibling build tasks.** `CancelableBuildTask.cs:64-74` provides a protected static helper `ResolveProjectRelativePath(projectDirectory, path)` handling `CurrentDirectory` fallback and `LinuxPathIdentity.RequireLocalPath`. `ResetPublishedVerification` consumes it. However, `InvalidatePublishedResult` (inheriting `CancelableBuildTask`) re-declares a 10-line local closure `ResolveLexicalPath`/`ResolvePath` doing the exact same resolution; and `ValidatePublishedVerificationResult` (inheriting `Microsoft.Build.Utilities.Task` rather than `CancelableBuildTask`) inlines an identical 11-line `ResolvePath` function. Inheriting `CancelableBuildTask` or consuming the static helper removes 21 redundant lines across the two tasks. | `SharpProof.BuildTasks/CancelableBuildTask.cs:64-74`; `SharpProof.BuildTasks/InvalidatePublishedResult.cs:48-58`; `SharpProof.BuildTasks/ValidatePublishedVerificationResult.cs:29-39` |

### Checked and not proposed (part twenty-seven)

- `ApiSpecTestFacets.cs` in `eng/testing/` is shared across `Effects.Test` and
  `Specs.Test` (per R166-R168, R209), which confirms the linked-test-source
  pattern established in `Directory.Build.props`.
- `OperationSupportCatalog.cs` in `SharpProof.Frontend` contains an internal enum
  `OperationSupportStage` (`ContractExpressionLowering`, `EffectDiscovery`) and a
  single forwarder method `IsSupported`. Although small (23 lines), it sits at the
  boundary of declarative catalog projections generated from `OperationSupport.catalog.json`
  and is part of the clean compiler-facing stage separation. Retained as-is.
- `CancellationBoundaryAnalyzer.cs` in `SharpProof.Meta.Analyzers` defines `IsOrDerivesFrom`
  to recursively check `BaseType`. This walks Roslyn symbols rather than runtime types
  and is specific to compiler analyzer semantics. Not proposed for cross-assembly sharing.

### Status (part twenty-seven)

R341-R343 remain `pending`; R344 is applied: invalidation and published-result
validation now use the shared project-relative path resolver, retaining the
same current-directory fallback and Linux-local path enforcement.
reductions. R341 and R343 reduce substantial build/test configuration duplication
across multiple entrypoints. R344 harmonizes path resolution between build tasks.

## Second survey, part twenty-eight: R350-R355

This pass examined build property evaluation, enum projections, script regex validation,
and hashing abstractions across `Directory.Build.props`, `SharpProof.CompilerArtifact`,
`SharpProof.Ir`, `SharpProof.Gates`, and the generator infrastructure.

| ID | Finding | Evidence |
|---|---|---|
| R350 | **`SharpProofUsesIrIdentifiers` lists 18 project names in a 20-line compound condition in `Directory.Build.props`.** `Directory.Build.props:37-56` evaluates `SharpProofUsesIrIdentifiers` with 19 `Or` clauses matching project names (`SharpProof.Analyzer.Core`, `SharpProof.CompilerArtifact`, `SharpProof.CompilerCollector`, `SharpProof.Contracts`, etc.) to conditionally link `IrIdentifierAliases.cs`. `IrIdentifierAliases.cs` contains only 9 `global using` declarations of `ScopedIrId<T>`. Every project in that list references `SharpProof.Ir` directly or transitively. Deriving the link from a standard property or linking it for all `SharpProofProductionProject` and test projects removes 20 lines of hardcoded project-name conditions that require manual maintenance whenever projects are added. | `Directory.Build.props:37-57,59-66`; `SharpProof.Ir/IrIdentifierAliases.cs:1-25` |
| R351 | **`CompilerLoweredArtifact.ManifestEvidence` manually table-drives an array mapping generated by `CompilerWireMappings.generated.cs`.** `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:5-10,1176-1183` manually declares `private static readonly WorkerClaimEvidence[] ManifestEvidenceMap = [ WorkerClaimEvidence.DirectClause, WorkerClaimEvidence.ReturnAttribute, WorkerClaimEvidence.CompanionClause ]` and indexes it with `(int)value`. Meanwhile, `CompilerWireMappings.generated.cs:276-284` in `SharpProof.CompilerCollector` generates `ToWorkerEvidence(BoundContractEvidence value)` mapping `CompilerBoundInvocation -> DirectClause`, `ClosedAttribute -> ReturnAttribute`, `Companion -> CompanionClause`. The enum values and target mappings are identical representations of contract evidence taxonomy. | `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:5-10,1176-1183`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerWireMappings.generated.cs:276-284` |
| R353 | **`CanonicalHashWriter.Add(params object?[] values)` introduces boxing allocations across high-throughput fingerprinting loops.** `CanonicalHashWriter.cs:117-137` defines a `params object?[]` overload that boxes value types (`bool`, `int`, `uint`, `long`, `Enum`) and performs runtime type-switching via `_ = value switch { ... }`. In `CompilationFingerprint.cs`, `CompilerFeatureScopeFingerprint.cs`, and `SemanticClaimIdentity.cs`, callers invoke `hash.Add(...)` with mixtures of strings, integers, and enums. `CanonicalHashWriter` already provides strongly typed, non-allocating overloads (`Add(string?)`, `Add(bool)`, `Add(int)`, `Add(uint)`, `Add(long)`, `Add(byte[])`). Removing the `params object?[]` overload or refactoring callers to fluent typed calls avoids boxing allocations during compiler capture and fingerprinting. | `SharpProof.Ir/CanonicalHashWriter.cs:9-35,117-137`; `SharpProof.CompilerArtifact/CompilationFingerprint.cs:21-28,36-43,53-63`; `SharpProof.CompilerArtifact/CompilerFeatureScopeFingerprint.cs:14-25` |
| R354 | **`RepositoryLayout.FindRoot` in `SharpProof.Gates` duplicates `TestRepository.FindRoot`.** `SharpProof.Gates/RepositoryLayout.cs:5-25` implements an upwards directory traversal searching for `SharpProof.sln` and `eng/acceptance/contract.json` to return the repository root (throwing `InvalidOperationException`). `eng/testing/TestRepository.cs:3-21` implements the identical upwards traversal checking for `SharpProof.sln` and `SharpProof.Release.props` (throwing `DirectoryNotFoundException`). `RepositoryLayout.FindRoot` is used across `SharpProof.Gates` and `SharpProof.Gates.Test`. Consolidating on a single root-resolution strategy eliminates the duplicated directory search algorithm. | `SharpProof.Gates/RepositoryLayout.cs:5-25`; `eng/testing/TestRepository.cs:3-21`; `SharpProof.Gates.Test/PerformanceGateTests.cs:18,395,432,479,559,568,665,826,860,884,907` |
| R355 | **`ArgumentNullGuard.cs` carries complex conditional compilation for namespace switching across only two linked projects.** `SharpProof.Ir/ArgumentNullGuard.cs:1-18` wraps its declarations in `#if SHARPPROOF_DATAFLOW_ARGUMENT_GUARD` and `#elif SHARPPROOF_SMT_ARGUMENT_GUARD`, re-declaring a synthetic `System.Diagnostics.CodeAnalysis.NotNullAttribute` and switching between `SharpProof.Dataflow`, `SharpProof.Smt`, and `SharpProof`. `SharpProof.Dataflow.csproj:5` and `SharpProof.Smt.csproj:5` define those constants to link the file. Placing `ArgumentNullGuard` in the root `SharpProof` namespace (or using file-scoped namespaces with standard `using SharpProof;`) allows all linking projects to consume it directly without conditional compilation symbols or synthetic attribute declarations. | `SharpProof.Ir/ArgumentNullGuard.cs:1-18`; `SharpProof.Dataflow/SharpProof.Dataflow.csproj:5,11-13`; `SharpProof.Smt/SharpProof.Smt.csproj:5,12-14` |

### Checked and not proposed (part twenty-eight)

- `DiagnosticDescriptorCatalogAssertions.cs` in `eng/testing/` validates generated
  diagnostic descriptors against `diagnostic-descriptors.v1.json`. It is referenced
  directly by three test projects and serves as a verified test assertion oracle.
  Retained as a single-authority test fixture.
- `Z3ExpressionOwner.cs` in `SharpProof.Smt` manually tracks and disposes native
  Z3 AST handles. This memory management is essential to prevent native leak during
  long-running verification and is not accidental complexity. Retained as-is.
- `CompilerConstantAdmission.cs` in `SharpProof.Frontend` isolates boundary checks
  for `int.MinValue`/`int.MaxValue` and literal negation. It is small (37 lines) but
  represents a deliberate semantic gate for constant evaluation.
- R358 is now applied: protocol JSON delegates surrogate scanning to the linked
  IR implementation through a protocol-local type name, preserving the existing
  `JsonException` message and avoiding duplicate fully-qualified types.
- R360 is now applied: `FrameworkTypeMetadataNames.Monitor` is a `const` like
  every other framework metadata identity.
- R359 is now applied: IR's canonical hash helper owns lowercase SHA-256
  validation, with summaries and protocol retaining their existing entrypoints.

### Status (part twenty-eight)

R350-R351, R353-R355 are `pending`. R350 and R355 are clean build/script
maintenance simplifications. R351, R353, and R354 address projection/mapping redundancy,
allocation overhead in canonical hashing, and duplicate repository root resolution.

## Second survey, part twenty-nine: R356-R357

This pass narrowed the remaining native interop and Roslyn project-policy repeats.
The native declarations are exact duplicates, while the project setting is a
smaller role-policy candidate that needs package-layout validation.

| ID | Finding | Evidence |
|---|---|---|
| R357 | **Three Roslyn dependency-producing projects repeat the same assembly-copy policy.** `SharpProof.Analyzer.Core`, `SharpProof.Analyzer`, and `SharpProof.CompilerCollector` each set `CopyLocalLockFileAssemblies=true`. They form the analyzer/compiler dependency-producing path and R343 already shows that their packaged dependency closure is maintained as one vocabulary. A narrowly scoped analyzer-component props fragment could own this policy and remove three identical project declarations, but the package and test output layouts must be checked before centralization because `Analyzer.Core` is intentionally not marked `IsRoslynAnalyzer`. | `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:6`; `SharpProof.Analyzer/SharpProof.Analyzer.csproj:6`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:6`; R343 |

### Checked and not proposed (part twenty-nine)

- The replay fixture classes repeat three exception identity constants, but one
  uses `Assembly::` and the other uses `assembly::`. The casing difference sits
  on a protocol identity boundary and may be intentional coverage of canonical
  normalization; it is not safe to collapse the fixtures without first proving
  the codec's case semantics.
- The NuGet v3 URL is repeated in package-feed helpers and audit/architecture
  assertions. It is a small fixed external authority already covered by the
  deferred R202-R204 family, so no separate reduction is counted here.
- R356 is now applied: the verifier tasks share one internal libc import owner;
  task-specific `prctl`, `waitpid`, and `kill` bindings remain local.

### Status (part twenty-nine)

R357 is `pending`. R357 needs package-output
tests because its three consumers do not have identical analyzer markers.

## Second survey, part thirty: R358-R367

This pass surveyed protocol serialization, string/surrogate validation, metadata
naming conventions, polyfill distributions, resource limit defaults, and SMT/dataflow
domain bounds across `SharpProof.Worker.Protocol`, `SharpProof.Ir`, `SharpProof.Specs`,
`SharpProof.Effects`, `SharpProof.Smt`, `SharpProof.ArchitectureTest`, and `SharpProof.Worker`.

| ID | Finding | Evidence |
|---|---|---|
| R361 | **`NullableAttributes.cs` in `SharpProof.Specs/Polyfills/` is linked into only one downstream project while sibling polyfills are declared independently.** `SharpProof.Specs/Polyfills/NullableAttributes.cs` defines `NotNullWhenAttribute(bool returnValue)`. `SharpProof.Effects/SharpProof.Effects.csproj:14-15` links it with `<Compile Include="..\SharpProof.Specs\Polyfills\NullableAttributes.cs" Link="Polyfills\NullableAttributes.cs" />`. Meanwhile, `SharpProof.Ir/ArgumentNullGuard.cs:1-8` conditionally compiles its own `NotNullAttribute`. Consolidating BCL code-analysis attribute polyfills under `SharpProof.Specs/Polyfills/` or in `Directory.Build.props` for netstandard2.0 removes ad-hoc per-file linking. | `SharpProof.Specs/Polyfills/NullableAttributes.cs:1-8`; `SharpProof.Effects/SharpProof.Effects.csproj:14-15`; `SharpProof.Ir/ArgumentNullGuard.cs:1-8` |
| R362 | **`ArchitectureRepository.ProductionProjects` hardcodes a 22-element project array duplicating the classification logic in `Directory.Build.props`.** `SharpProof.ArchitectureTest/ArchitectureRepository.cs:7-30` hardcodes an array of 22 production project names. `Directory.Build.props:33-36` classifies `SharpProofProductionProject` using regex exclusions. When new production projects are added, `ArchitectureRepository.ProductionProjects` must be manually updated in addition to build props and solution files. Deriving production project membership dynamically from project evaluation or solution structure avoids configuration drift. | `SharpProof.ArchitectureTest/ArchitectureRepository.cs:7-30`; `Directory.Build.props:33-36` |
| R363 | **The default rlimit budget of 3,000,000 resource units is defined independently across three disconnected project layers.** `SharpProof.Smt/IrSmtBackendOptions.cs:5` hardcodes `public const uint DefaultQueryRlimit = 3_000_000;`. `SharpProof.Worker.Protocol/ProtocolModel.schema.json:520` generates `WorkerBudgets.DefaultQueryRlimit = 3000000U;`. `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:18` defines `<SharpProofVerifyQueryRlimit Condition="'$(SharpProofVerifyQueryRlimit)' == ''">3000000</SharpProofVerifyQueryRlimit>`. If the default query rlimit is adjusted, all three independent declarations must be synchronized manually. | `SharpProof.Smt/IrSmtBackendOptions.cs:5`; `SharpProof.Worker.Protocol/ProtocolModel.schema.json:520`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:18` |
| R364 | **`MethodResourceBudget.RequireNonnegative` duplicates `ArgumentNullGuard.RequireNonnegative`.** `SharpProof.Worker/MethodResourceBudget.cs:49-53` defines a local static helper `RequireNonnegative(long count) => count >= 0 ? count : throw new InvalidOperationException("The backend resource counter cannot be negative.");`. `SharpProof.Ir/ArgumentNullGuard.cs` already provides `RequireNonnegative(long value, string paramName)`. `MethodResourceBudget` already consumes `ArgumentNullGuard.RequirePositive` on line 7; replacing the local helper with `ArgumentNullGuard.RequireNonnegative` removes 5 redundant lines. | `SharpProof.Worker/MethodResourceBudget.cs:7-12,49-53`; `SharpProof.Ir/ArgumentNullGuard.cs:52-62` |
| R365 | **`FinalCompilationCollector.ParseSpecificationPacks` implements custom semicolon list splitting and validation.** `SharpProof.CompilerCollector/FinalCompilationCollector.cs:87-111` implements custom splitting on `[';']`, trimming, blank checks, and uniqueness verification for `SharpProofSpecificationPacks`. Similar semicolon list parsing logic exists in `scripts/CSharpSourceMetrics.ps1:258-262` and MSBuild option decoders. Centralizing delimited property parsing in a shared option helper standardizes error handling for list-valued build properties. | `SharpProof.CompilerCollector/FinalCompilationCollector.cs:87-111`; `scripts/CSharpSourceMetrics.ps1:258-262` |
| R366 | **`CompilerSourceIntegerDomain.Contains` is a trivial 14-line wrapper around basic interval comparisons.** `SharpProof.CompilerArtifact/CompilerSourceIntegerDomain.cs:5-12` defines `Contains(CompilerIntegerInterval? interval, IrValue value)` checking `value.Kind == IrValueKind.Integer && value.Integer >= bounds.Minimum && value.Integer <= bounds.Maximum`. `SharpProof.Dataflow/IntervalValue.cs:73-89` already provides rich `Contains(long value)` domain containment. Inlining or unifying the interval bounds check avoids creating single-method static domain wrappers. | `SharpProof.CompilerArtifact/CompilerSourceIntegerDomain.cs:5-12`; `SharpProof.Dataflow/IntervalValue.cs:73-89` |
| R367 | **`EffectClaimResultAssembler` repeats `CallableClaimResultAssembler.Create` calls with repetitive argument bundles across six branches.** `SharpProof.Worker/EffectClaimResultAssembler.cs:33-39,43-49,53-67,77-83,85-93,95-100` calls `CallableClaimResultAssembler.Create(target, evidence.ClaimId, outcome, reason, certainty)` six separate times across different feasibility and replay outcome branches. Refactoring to a unified result assembly builder reduces repetitive parameter forwarding across the 117-line file. | `SharpProof.Worker/EffectClaimResultAssembler.cs:33-100` |

### Checked and not proposed (part thirty)

- `IntervalValue.ToString()` in `SharpProof.Dataflow` formats intervals with invariant culture
  (handling negative signs under `sv-SE`/`fi-FI` where minus becomes U+2212). This culture
  isolation is verified by test fixtures and is required for deterministic string representations.
- `Z3ExpressionOwner` finalization pattern in `SharpProof.Smt` safely disposes native Z3 ASTs.
  Retained as essential native interop safety.

### Status (part thirty)

R361-R367 are `pending`. R364 is a direct, safe code reduction.
R361, R362, and R363 reduce cross-layer configuration drift between MSBuild properties,
Roslyn analyzers, and verification workers.

## Second survey, part thirty-one: R368-R372

This pass investigated outcome caching policies, verification factory term guards,
PowerShell Roslyn parse options parsing, and legacy assembly references across
`SharpProof.Verify`, `SharpProof.Ir`, `SharpProof.Summaries`, and `scripts/`.

| ID | Finding | Evidence |
|---|---|---|
| R368 | **`OutcomeCachePolicy.IsCacheable` is a 7-line single-method class wrapping an inline type check.** `SharpProof.Verify/Outcomes.cs:42-49` defines `public static class OutcomeCachePolicy { public static bool IsCacheable(ProofOutcome outcome) => outcome == null ? throw new ArgumentNullException(nameof(outcome)) : outcome is ProvenOutcome or RefutedOutcome; }`. The class exists solely to test `outcome is ProvenOutcome or RefutedOutcome`. Inlining the pattern directly at call sites eliminates the trivial wrapper class. | `SharpProof.Verify/Outcomes.cs:42-49` |
| R369 | **`FactoryGuards.RequireBooleanTerm` in `SharpProof.Verify` repeats IR factory term validation.** `SharpProof.Verify/Evidence.cs:106-124` defines `internal static IrTerm RequireBooleanTerm(IrFactory factory, IrTerm term, string parameterName)` checking null guards, `factory.EnsureTerm(term)`, and `term.Type != factory.BooleanType`. `SharpProof.Ir` already owns term type verification; hoisting this helper to `IrFactory` (e.g. `factory.RequireBooleanTerm(term)`) eliminates the isolated guard class in `SharpProof.Verify`. | `SharpProof.Verify/Evidence.cs:106-124`; `SharpProof.Verify/Backend.cs:97,100` |
| R370 | **`New-SharpProofCSharpParseOptions` in `scripts/CSharpSourceMetrics.ps1` contains a 56-line custom C# language version parser.** `scripts/CSharpSourceMetrics.ps1:210-266` manually parses version strings (`latest` -> `Latest`, `preview` -> `Preview`, `9.0` -> `CSharp9`, `7.3` -> `CSharp7_3`) via regex matching and switch statements. Because Roslyn's `LanguageVersionFacts` or `[Enum]::Parse([Microsoft.CodeAnalysis.CSharp.LanguageVersion], ...)` already handles standard version strings, the custom 56-line parsing logic can be significantly streamlined. | `scripts/CSharpSourceMetrics.ps1:210-266` |
| R372 | **`IrSummarySignature` repeats canonical member and variable mapping in `SharpProof.Summaries`.** `SharpProof.Summaries/IrRelationalSummary.cs:81-100` defines `IrSummarySignature` holding `Member`, `Receiver`, `Parameters`, `Result`, and `Provenance`. `SharpProof.CompilerArtifact/CompilerCallablePreparation` and `CompilerManifestArtifact` mirror these identical member signature properties. Consolidating the callable signature abstraction reduces field-by-field conversion boilerplate between compiler artifacts and summary models. | `SharpProof.Summaries/IrRelationalSummary.cs:81-100`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:30-45` |

### Checked and not proposed (part thirty-one)

- `CSharpSourceMetricsEngine.Measure` in `scripts/CSharpSourceMetrics.ps1` compiles an in-memory
  Roslyn syntax walker via C# snippet. This avoids slow PowerShell AST walking over large codebases
  and is an intentional performance optimization. Retained as-is.
- `SpecResultDomainProjection.cs` in `SharpProof.Worker` projects relational summary results into
  the verifier domain. The logic is verifier-specific and properly decoupled from compiler lowering.
- R371 is now applied: PowerShell 7's built-in compression types are used directly
  by the pilot authority scripts, and the authority fixtures still pass.

### Status (part thirty-one)

R372 is `pending`. R368 is applied: verification tests now use the
cacheability type pattern directly after removing the unused wrapper. R369 is
applied: Boolean-term validation now lives in the owning `IrFactory`, preserving
the existing error and factory-scope checks while removing `FactoryGuards`. R370
is applied: source metrics now delegate standard version parsing to Roslyn while
retaining the existing blank/unsupported diagnostics. R372
streamline script language parsing and relational summary signature models.

## Second survey, part thirty-two: R373-R374

This pass checked the compiler-probe asset's local utility surface and the
contract-runtime preprocessor symbol across source, generated, and MSBuild
boundaries.

| ID | Finding | Evidence |
|---|---|---|
| R374 | **The contract-runtime preprocessor symbol is independently authored at four boundaries.** `SharpProof.Attributes.Contract.ConditionalSymbol` defines `SHARPPROOF_CONTRACTS`; `SharpProof.Frontend/ContractApi.catalog.json` repeats it and generates `ContractApiCatalog.ConditionalSymbol`; `SharpProof.CompilerArtifact/CompilationFingerprint` repeats it to reject runtime-contract compilations; and `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props` repeats it in both its detection regex and diagnostic text. R309 covers the intentionally synthetic test fixtures, but no check currently proves these production/build values remain equal. Directly importing one assembly constant is constrained by the analyzer and MSBuild boundaries; a generated shared value or a consistency gate could remove the independent literals and make symbol drift fail early. | `SharpProof.Attributes/Contract.cs:7`; `SharpProof.Frontend/ContractApi.catalog.json:5`; `SharpProof.Frontend/ContractApiMetadata.generated.cs:64-65,220-221`; `SharpProof.CompilerArtifact/CompilationFingerprint.cs:7-8`; `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props:10,27`; R309 |

### Checked and not proposed (part thirty-two)

- `CompilerProbeGenerator.GetOption` and `CompilerProbeSnapshot.GetOption` are
  exact duplicates, but the broader `CompilerProbeSnapshot` and collector
  algorithm sharing is already tracked by R281, R323, and R337. R373 is limited
  to the two small helpers so it does not double-count those larger seams.
- The generated `ContractApiMetadata.generated.cs` copy is derived from the
  catalog rather than independently edited. It is counted in R374 as a
  generated projection; the drift concern is the catalog/build/public split,
  not generated-file maintenance by itself.
- R373 is now applied: compiler-probe generator and snapshot code use one
  same-assembly helper for option lookup and path normalization.

### Status (part thirty-two)

R374 is `pending`. R374 needs a source-of-truth decision across C#, generated metadata, compiler
fingerprinting, and MSBuild before any literal is removed.

## Second survey, part thirty-two (continued): R461-R462, R375-R379

This pass surveyed abstract domains, transfer functions, modular arithmetic bounds,
lattice joins, graph representations, and fixpoint solvers in `SharpProof.Dataflow`.

| ID | Finding | Evidence |
|---|---|---|
| R375 | **`ClosedAbstractDomain<T>` forces duplicate `Havoc` and `Widen` implementations across all derived domains.** `SharpProof.Dataflow/ClosedAbstractDomain.cs:18-19` defines `Havoc` and `Widen` as abstract methods. Every derived domain repeats identical logic: `NullnessDomain.cs:49-53`, `SequenceCardinalityDomain.cs:136-140`, and `IntervalDomain.cs:178-181` each implement `Havoc` as `value.IsBottom ? Bottom : Top`, and finite lattices implement `Widen` as `Join(previous, candidate)`. Providing virtual default implementations in `ClosedAbstractDomain<T>` eliminates boilerplate overrides in all subclasses. | `SharpProof.Dataflow/ClosedAbstractDomain.cs:18-19`; `SharpProof.Dataflow/NullnessDomain.cs:44-53`; `SharpProof.Dataflow/SequenceCardinalityDomain.cs:136-140`; `SharpProof.Dataflow/IntervalDomain.cs:178-181` |
| R377 | **4-point flat diamond lattice join and partial order logic is duplicated across enum domains.** `SharpProof.Dataflow/NullnessDomain.cs:17-42` and `SharpProof.Dataflow/SequenceCardinalityDomain.cs:142-169` implement identical 4-element flat diamond lattices ($\bot < \{A, B\} < \top$). Both duplicate identical branch cascades for identity, bottom absorption, and top collapse. Unifying diamond lattice operations reduces duplicated lattice decision trees across abstract domains. | `SharpProof.Dataflow/NullnessDomain.cs:17-42`; `SharpProof.Dataflow/SequenceCardinalityDomain.cs:142-169` |
| R378 | **`DataflowEdge` contains manual property backing and constructor boilerplate on a `readonly record struct`.** `SharpProof.Dataflow/DataflowGraph.cs:10-27` spans 18 lines manually declaring constructor parameter guards and property assignments for a 2-field record struct. Converting to primary constructor property initializers (`public readonly record struct DataflowEdge(int SourceId, int TargetId) { public int SourceId { get; } = ArgumentNullGuard.RequireNonnegative(SourceId, nameof(SourceId)); ... }`) reduces 18 lines to 5 lines while preserving all validation, deconstructors, and value equality. | `SharpProof.Dataflow/DataflowGraph.cs:10-27` |
| R379 | **`ForwardDataflowAnalysis` allocates an intermediate dictionary and performs two redundant collection passes per solver round.** `SharpProof.Dataflow/ForwardDataflowAnalysis.cs:138-171` allocates `var changedOutputs = new Dictionary<int, T>()` each round, collects changed block states, loops to write them into `outputs`, and loops a third time to gather successors into `affected`. Because block transfers within a round read invariant `inputs`, `outputs[blockId]` can be updated directly and successors enqueued immediately, eliminating intermediate dictionary allocations and two iteration loops per fixpoint round. | `SharpProof.Dataflow/ForwardDataflowAnalysis.cs:138-171` |

### Checked and not proposed (part thirty-two continued)

- `IntervalValue.SingletonValue` throwing on non-singletons is a fail-closed guard preventing
  symbolic interval evaluation on indeterminate bounds. Retained as-is.
- Explicit non-negative interval constraints on sequence cardinality length domain prevent
  negative length inferences. Retained as-is.
- R461 is now applied: `TryCongruentBoundary` uses `Normalize` for both modular
  directions, preserving the same signed-distance result.
- R462 is now applied: the earlier `modulus.IsOne` boundary rewrite was removed;
  the later extreme-bound handling remains the sole normalization path.
- R376 is now applied: `DataflowGraph` freezes its already-canonical adjacency
  order without a second per-list sort.

### Status (part thirty-two continued)

R375, R377, and R379 are `pending`. R378 is deferred: the primary-constructor
form requires an `IsExternalInit` compatibility shim in the netstandard2.0
project, which would add more infrastructure than the boilerplate removes.
R375 and R377 generalize abstract domain hierarchy contracts. R379 optimizes fixpoint solver throughput.

## Second survey, part thirty-three: R380-R385

This pass surveyed compiler artifact models, code generation scripts, diagnostic ordering,
stream readers, and location authority helpers across `SharpProof.CompilerArtifact`,
`SharpProof.CompilerCollector`, and `scripts/Generate-CompilerArtifactModel.ps1`.

| ID | Finding | Evidence |
|---|---|---|
| R380 | **`CompilerDiagnosticArtifactOrdering` duplicates an 11-stage comparison ladder between LINQ `Canonicalize` and imperative `Compare`.** `SharpProof.CompilerArtifact/CompilationFingerprint.cs:438-453` applies an 11-level chained LINQ sort (`OrderBy(Code).ThenBy(Message)...ThenBy(SourceLineMapSha256)`). Lines 463-521 re-implement the identical 11-stage comparison ladder across 58 lines of manual `StringComparer.Ordinal.Compare` and field-by-field branching in `Compare`. Unifying both paths on a single `IComparer<CompilerDiagnosticArtifact>` eliminates 58 lines of redundant ladder code and prevents ordering divergence. | `SharpProof.CompilerArtifact/CompilationFingerprint.cs:438-453, 463-521` |
| R385 | **`ReplayEventComparer` manually inlines location hashing and comparison instead of reusing `CompilerSourceLocationAuthority`.** `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:365-371` manually hashes all five fields of `WorkerSourceLocation` (`Path`, `Start`, `Length`, `Line`, `Column`) with bespoke null checks instead of using `CompilerSourceLocationAuthority.GetLocationHashCode` (`CompilerSourceLocationAuthority.cs:228-241`). Reusing the authority keeps location equality and hash distribution centralized. | `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:365-371`; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:228-241` |

### Checked and not proposed (part thirty-three)

- `CompilationFingerprint.ComputeSha256` ensures deterministic invariant hashing across platform
  line endings and culture settings; canonical serialization order is required for verifiable builds.
- `CompilerModelValues.cs` contains low-level value packers for compiler wire formats. These are
  isolated for fast serialization and decoupled from Roslyn symbols.
- R381 is now applied: the compiler collector uses the artifact assembly's
  generated callable-reason catalog, so the wire-mapping output no longer emits
  a duplicate catalog.
- R384 is now applied: compiler capture formats Roslyn checksum bytes through
  the shared `HashEncoding` implementation.
- R382 is now applied: effect replay uses the shared source-location authority
  for unique physical-tree binding and retains its existing hash projection.
- R383 is now applied: runtime-component and compiler-manifest reads share one
  bounded exact-reader while retaining their distinct failure messages.

### Status (part thirty-three)

R380 is applied: canonical diagnostic sorting and canonical-order checks now
share one comparer. R385 is refuted on the current tree: the cited
`GetLocationHashCode` authority is absent; `CompilerSourceLocationAuthority`
provides `LocationsEqual`, while the replay comparer owns a broader artifact
hash that cannot be delegated to a nonexistent location-only helper.

## Second survey, part thirty-four: R386-R392

This pass surveyed the effect system, exception reachability, using disposal unwinding,
ownership classification, call graph ordering, and summary operations in `SharpProof.Effects`.

| ID | Finding | Evidence |
|---|---|---|
| R386 | **`ExceptionHandlerReachability` duplicates the using-disposal unwinding loop and completion predicates from `UsingDisposalEffectResolver`.** `UsingDisposalEffectResolver.ResolveResources` (`SharpProof.Effects/UsingDisposalEffectResolver.cs:141-198`) and `ExceptionHandlerReachability.GetUsingDisposalExceptions` (`SharpProof.Effects/ExceptionHandlerReachability.cs:2085-2141`) implement the identical 55-line algorithm for tracking acquired `IVariableDeclarationGroupOperation` declarators, checking `allInitializersComplete`, reversing acquired items, and unwinding disposals. Furthermore, `ExceptionHandlerReachability.cs:2379-2438` duplicates four completion/unwinding predicates (`CanDisposalsCompleteNormally`, `CanDisposalCompleteNormally`, `CanDisposalUnwind`, `IsDefinitelyNullResource`) nearly verbatim from `UsingDisposalEffectResolver.cs:200-254`. Hoisting the shared unwinding loop and predicates into `UsingDisposalGraph` eliminates ~90 lines of duplicate disposal simulation code. | `SharpProof.Effects/UsingDisposalEffectResolver.cs:141-254`; `SharpProof.Effects/ExceptionHandlerReachability.cs:2085-2141, 2379-2438` |
| R387 | **Three separate routines duplicate member initializer syntax-to-operation extraction loops.** `EffectMethodNodeBuilder.EnsureBeforeFieldInitNode` (`SharpProof.Effects/EffectMethodNodeBuilder.cs:267-285`), `EffectMethodNodeBuilder.ScanMemberInitializers` (`lines 326-343`), and `ManagedAbstractFlow.ConstructorMayCompleteNormally` (`SharpProof.Effects/ManagedAbstractFlow.cs:2245-2266`) each iterate `GetMemberInitializerReferences`, fetch syntax via `reference.GetSyntax()`, extract initializer expressions with `EffectProjections.GetInitializerExpression`, and query the `SemanticModel` for operations. Providing a centralized `GetMemberInitializerOperations` helper removes ~40 lines of repetitive Roslyn resolution. | `SharpProof.Effects/EffectMethodNodeBuilder.cs:267-285, 326-343`; `SharpProof.Effects/ManagedAbstractFlow.cs:2245-2266` |
| R388 | **`ConversionOwnershipClassifier` duplicates local vs captured symbol region classification across three methods.** `ClassifyLocalStorage` (`SharpProof.Effects/ConversionOwnershipClassifier.cs:542-553`), `ClassifyRefLocalStorage` (`lines 148-161`), and `ClassifyLocal` (`lines 842-855`) each perform identical checks for `SymbolEqualityComparer.Default.Equals(local.ContainingSymbol?.OriginalDefinition, _method.OriginalDefinition)` and fall back to the exact same captured ordinal extraction (`local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0`). Extracting a shared `ClassifyCapturedLocal` helper eliminates ~20 lines of duplicate symbol span inspection. | `SharpProof.Effects/ConversionOwnershipClassifier.cs:148-161, 542-553, 842-855` |
| R390 | **`EffectCallGraph.OrderMethods` contains redundant while-enumerator cancellation checks and comparer overhead.** `SharpProof.Effects/EffectCallGraph.cs:95-115` writes a manual `while (true)` enumerator loop checking `cancellationToken.ThrowIfCancellationRequested()` four times per iteration, while `CancellationAwareMethodComparer` (`lines 121-133`) checks cancellation twice for every pairwise comparison during sorting. Standardizing on `foreach` and sort boundary cancellation simplifies ~25 lines of manual enumerator plumbing. | `SharpProof.Effects/EffectCallGraph.cs:95-115, 121-133` |
| R391 | **`EffectSummaryOperations.Join` causes high-frequency param-array allocations across over 45 AST scanning call sites.** `SharpProof.Effects/EffectSummaryOperations.cs:7-20` defines only `Join(params EffectSummary[] summaries)`, routing all calls through array allocation. Over 45 call sites in `OperationEffectScanner.cs`, `OperationEffectScanner.Assignments.cs`, and `OperationEffectScanner.Expressions.cs` pass 2 or 3 arguments, allocating an array per AST node visit. Providing non-allocating 2- and 3-argument overloads or standardizing on `EffectSummaryDomain.Instance.Join(a, b)` eliminates heap churn during AST scanning. | `SharpProof.Effects/EffectSummaryOperations.cs:7-20`; `SharpProof.Effects/OperationEffectScanner.cs:187, 337, 466, 504, 511, 519, 522, 589, 830, 836, 917`; `SharpProof.Effects/OperationEffectScanner.Expressions.cs:187, 212, 437, 453, 474, 502, 541, 555, 595, 606, 613, 630, 743, 758, 777, 826, 860, 864` |

### Checked and not proposed (part thirty-four)

- `PropertyDispatchFacts.cs` evaluates auto-property and accessor dispatch paths with strict
  Roslyn syntax guards. Retained as-is for contract verification safety.
- `StringConcatenationEffectResolver` implements multi-part string interpolation formatting
  semantics. Retained as-is to preserve exact BCL format string exception behaviors.
- R392 is now applied: `OperationCompletionEvaluator` shares its completion facts
  for static-initialization checks instead of allocating a second equivalent object.
- R389 is now applied: effect nullness checks share the harmless-conversion unwrapping
  and disposal resolver predicate.
- R388 is now applied: conversion ownership paths share captured-local region
  classification.
- R391 is now applied: frequent two- and three-summary joins avoid params-array
  allocations while retaining the params overload for general callers.
- R390 is now applied: call-graph ordering uses foreach and the canonical symbol
  comparer with cancellation checked at the sort boundary.
- R387 is now applied: member initializer operation extraction is centralized
  while null-operation handling remains at each caller.

### Status (part thirty-four)

R386 is `pending`.
R386 eliminates major algorithmic duplication between exception reachability and using disposal.
R391 removes AST-scanning allocation churn.
R400, R403, R405, and R406 are now applied: IR catalog calls, contract binding,
signature registration, and factory initialization no longer carry redundant
forwarders or guards.
R401 and R404 are now applied: direct contract fallback construction and partial
property accessor selection share their local logic.

## Second survey, part thirty-five: R393-R399

This pass surveyed relational summaries, API specifications, spec term instantiation,
content hashing, assembly validation, and summary lowerers across `SharpProof.Summaries`,
`SharpProof.Specs`, and `SharpProof.CompilerCollector`.

| ID | Finding | Evidence |
|---|---|---|
| R393 | **`CompilerSpecificationPackProvider` duplicates the relational spec pack AST, JSON parser, and term instantiator from `SharpProof.Specs`.** `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:258-297, 559-693, 821-849` defines a private 7-node term AST, an 85-line JSON parser (`ParseTerm`), operator string mappers, and a recursive term instantiator. `SharpProof.Specs` (referenced by `CompilerCollector`) already provides the identical declarative term hierarchy (`SpecTermDeclaration` in `DefaultApiSpecCatalog.generated.cs:126-159`) and term instantiation engine (`ApiSpecInstantiator.InstantiatePostconditions` in `ApiSpecInstantiation.cs:132-326`) for `RelationalSpecPackCatalog.json`. Consolidating relational spec pack term parsing onto `SharpProof.Specs` eliminates >150 lines of duplicate AST definitions, switch tables, and recursive AST instantiators. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:258-297, 559-693, 821-849`; `SharpProof.Specs/DefaultApiSpecCatalog.generated.cs:126-159`; `SharpProof.Specs/ApiSpecInstantiation.cs:132-326` |
| R394 | **`ApiSpecInstantiator.InstantiatePostconditions` allocates three fresh dictionaries and performs linear variable scans per call.** `SharpProof.Specs/ApiSpecInstantiation.cs:42-44, 71-74` allocates three immutable dictionaries on every instantiation call (`substitutions.ToImmutableDictionary()`, `template.Variables.ToImmutableDictionary(Id)`, and `template.Variables.ToImmutableDictionary((Role, Ordinal))`). In addition, `ApiSpecContentDigest.cs:92-94` performs a linear scan `variables.Single(item => item.Role == variable.Role && item.Ordinal == variable.Ordinal)` for every variable term across all postconditions. Pre-indexing variable mappings on immutable `ApiSpecTemplate` during table compilation eliminates per-instantiation dictionary allocations and linear scans. | `SharpProof.Specs/ApiSpecInstantiation.cs:42-44, 71-74, 166-171`; `SharpProof.Specs/ApiSpecContentDigest.cs:92-94`; `SharpProof.Specs/ApiSpecTable.cs:120-121` |
| R395 | **Three summary compiler lowerers duplicate IR variable synthesis, signature construction, and environment initialization.** `CompilerRelationalSummaryProvider.TryBuildSource` (`SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:231-243`), `CompilerImplementationIlSummaryLowerer.TryBuild` (`CompilerImplementationIlSummaryLowerer.cs:248-266`), and `CompilerSpecificationPackProvider.TryBuild` (`CompilerSpecificationPackProvider.cs:150-211`) all independently synthesize parameter/result IR variables, construct `IrSummarySignature`, map the parameter environment dictionary, and invoke `IrRelationalSummaryBuilder.Build`. Centralizing this canonical signature/environment sequence in `SharpProof.Summaries` eliminates ~70 lines of repetitive lowerer boilerplate. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:231-243, 293-308`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs:248-266, 288-296`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:150-211` |
| R396 | **Lack of value equality on `IrSummaryProvenance` forces ad-hoc 4-tuple dictionary keys and duplicate 4-stage sorting chains.** `SharpProof.Summaries/IrRelationalSummary.cs:28-79` defines `IrSummaryProvenance` as a standard class without `IEquatable`. Consequently, `IrRelationalSummaryBuilder.cs:190-194` declares `Dictionary<(IrSummaryOrigin Origin, string EvidenceCallIdentity, string EvidenceIdentity, string EvidenceSha256), IrSummaryProvenance>`, allocates 4-tuples on every dependency add (lines 523-528), and executes a 4-level `OrderBy().ThenBy().ThenBy().ThenBy()` chain (lines 279-287). `CompilerRelationalSummaryProvider.cs:42-47` repeats the same 4-level sorting chain. Declaring `IrSummaryProvenance` as a record class or implementing `IEquatable` removes custom tuple keys and duplicate sorting logic. | `SharpProof.Summaries/IrRelationalSummary.cs:28-79`; `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:190-194, 279-287, 521-529`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:42-47` |
| R397 | **`ApiSpecTable.ValidateDeclaration` uses ad-hoc string concatenation and LINQ for approved assembly uniqueness checks.** `SharpProof.Specs/ApiSpecTable.cs:229-238` checks assembly uniqueness by concatenating `assembly.Name + "\u001f" + assembly.PublicKeyToken.ToUpperInvariant() + "\u001f" + (int)assembly.ReferenceFamily` and counting `.Distinct().Count()`. Because `ApiSpecAssemblyIdentity` is already a record holding `(Name, PublicKeyToken, ReferenceFamily)`, checking uniqueness via `IEqualityComparer<ApiSpecAssemblyIdentity>` or a `HashSet` avoids allocating formatted delimiter strings and LINQ enumerators for every target assembly during spec table validation. | `SharpProof.Specs/ApiSpecTable.cs:229-238`; `SharpProof.Specs/DefaultApiSpecCatalog.generated.cs:112-116` |
| R398 | **Constructor-to-property boilerplate across `IrRelationalSummary`, `IrRelationalSummaryBuildResult`, and `IrSummaryInstantiation`.** `SharpProof.Summaries/IrRelationalSummary.cs:102-181` contains ~60 lines of repetitive parameter-to-field/property mapping in classic constructor syntax. Adopting C# 12 primary constructors or positional records on these carrier models eliminates ~40 lines of constructor plumbing while preserving full immutability and API compatibility. | `SharpProof.Summaries/IrRelationalSummary.cs:102-181` |
| R399 | **`ApiSpecTermValidator.TryNegate` duplicates integer negation overflow handling from `IrScalarOperations`.** `SharpProof.Specs/ApiSpecTermValidator.cs:275-287` implements a local helper catching `OverflowException` from `checked(-value)` while line 295 delegates binary arithmetic directly to `IrScalarOperations.Evaluate`. Hoisting unary negation into `IrScalarOperations` unifies scalar evaluation across IR interpretation and spec validation, eliminating ad-hoc overflow-catching arithmetic in `ApiSpecTermValidator`. | `SharpProof.Specs/ApiSpecTermValidator.cs:275-287`; `SharpProof.Ir/IrInterpreter.cs:18-48` |

### Checked and not proposed (part thirty-five)

- `ApiSpecContentDigest.ComputeSha256` ensures deterministic invariant cryptographic signatures
  over normalized API specifications. Retained as-is for spec security and auditability.
- `DefaultApiSpecCatalog.generated.cs` code generation maps specification catalogs from JSON;
  the generator ensures strict schema validation at build time.

### Status (part thirty-five)

R393-R399 are `pending`. R393 eliminates a major AST and JSON parsing duplication in `CompilerCollector`.
R394, R396, and R397 optimize term instantiation, dependency sorting, and assembly validation.
R395, R398, and R399 streamline summary lowerers and scalar arithmetic.

## Second survey, part thirty-six: R400-R406

This pass surveyed the IR model, operator catalogs, contract resolvers, type specialization,
and program builders across `SharpProof.Ir`, `SharpProof.Contracts`, and `SharpProof.Attributes`.

| ID | Finding | Evidence |
|---|---|---|
| R400 | **`IrTermServices.cs` declares redundant pass-through forwarders for `IrOperatorCatalog`.** `SharpProof.Ir/IrTermServices.cs:201-204, 277-282` defines `IsNullable(IrTypeKind)` and `GetBuiltInType(IrFactory, IrTypeKind)` which do nothing other than forward to `IrOperatorCatalog.IsNullable` and `IrOperatorCatalog.GetBuiltInType` (`SharpProof.Ir/IrOperatorCatalog.generated.cs:55-73`). Both methods have identical internal visibility in `SharpProof.Ir`. Calling `IrOperatorCatalog` directly at all 5 call sites in `IrFactory.cs` (lines 302, 394, 536, 543, 551) and `IrTermServices.cs` eliminates 10 lines of forwarding boilerplate. | `SharpProof.Ir/IrTermServices.cs:201-204, 277-282`; `SharpProof.Ir/IrOperatorCatalog.generated.cs:55-73`; `SharpProof.Ir/IrFactory.cs:302, 394, 434, 536, 543, 551` |
| R401 | **`EffectiveContractSourceResolver` duplicates direct-clause fallback resolution across three branches and uses an unnecessary constructor wrapper.** `SharpProof.Contracts/EffectiveContractSourceResolver.cs:88-153` contains three branches returning direct clause resolution: lines 88-98 and 100-108 can be folded into a single guard (`if (direct.HasPlacementErrors || direct.Clauses.Any(static clause => clause.IsValid))`). In addition, lines 155-168 declare a private static helper `Create` that merely invokes `new EffectiveContractSourceResolution(...)`. Inlining the constructor call eliminates 14 boilerplate lines. | `SharpProof.Contracts/EffectiveContractSourceResolver.cs:88-108, 145-168`; `SharpProof.Contracts/EffectiveContractModels.generated.cs:12-25` |
| R402 | **Boolean term validation guards are duplicated across `IrSemanticTerms`, `IrProgramBuilder`, and `IrFactory`.** `SharpProof.Ir/IrSemanticTerms.cs:104-119` (`ValidateBooleanTerm`), `SharpProof.Ir/IrProgramBuilder.cs:302-311` (`ValidateBoolean`), and `SharpProof.Ir/IrFactory.cs:496-499` each independently check null arguments, call `EnsureTerm`, and verify `term.Type == BooleanType`. Hoisting a canonical `factory.RequireBooleanTerm(term, parameterName)` helper onto `IrFactory` removes repetitive validation logic across IR producers. | `SharpProof.Ir/IrSemanticTerms.cs:104-119`; `SharpProof.Ir/IrProgramBuilder.cs:302-311`; `SharpProof.Ir/IrFactory.cs:496-499` |
| R403 | **`ContractExpressionBinder.Bind` is an unnecessary single-expression wrapper around private `BindWithFrontend`.** `SharpProof.Contracts/ContractExpressionBinder.cs:42-45` defines `internal ExpressionBindingResult Bind(IOperation operation) => BindWithFrontend(operation);`, where `BindWithFrontend` (`lines 103-136`) has only that single caller. Renaming `BindWithFrontend` directly to `Bind` eliminates the forwarding layer. | `SharpProof.Contracts/ContractExpressionBinder.cs:42-45, 103-136` |
| R404 | **`ContractClauseInventoryBuilder` repeats partial property-accessor extraction logic across three methods.** `GetPartialImplementation` (`SharpProof.Contracts/ContractClauseInventoryBuilder.cs:390-398`), `NormalizeCallable` (`lines 456-462`), and `GetPartialDefinition` (`lines 477-485`) each repeat the exact same conditional selection between `property.GetMethod` and `property.SetMethod` based on `method.MethodKind == MethodKind.PropertyGet`. Centralizing accessor resolution in a shared helper simplifies partial property contract tracking. | `SharpProof.Contracts/ContractClauseInventoryBuilder.cs:382-399, 454-464, 466-487` |
| R405 | **`ContractCanonicalization.TypeSpecializer` duplicates method signature type registration loops.** `SharpProof.Contracts/ContractCanonicalization.cs:44-52` and lines 61-70 execute the identical return-type and parameter loop to register definition and constructed types with `AddSignatureType`. Factoring out `AddMethodSignature(IMethodSymbol definition, IMethodSymbol constructed)` eliminates the duplicate loop. | `SharpProof.Contracts/ContractCanonicalization.cs:44-52, 61-70` |
| R406 | **`IrProgramInterpreter` primary constructor duplicates argument null guards on adjacent field initializers.** `SharpProof.Ir/IrProgramInterpreter.cs:11-16` calls `ArgumentNullGuard.NotNull(factory, nameof(factory))` twice in adjacent field initializers (`_factory` and `_terms = new(ArgumentNullGuard.NotNull(factory, nameof(factory)))`), while `IrInterpreter` constructor also validates `factory != null`. Passing `_factory` directly to `_terms` removes the redundant guard call. | `SharpProof.Ir/IrProgramInterpreter.cs:11-16`; `SharpProof.Ir/IrInterpreter.cs:104-105` |

### Checked and not proposed (part thirty-six)

- `IrSubstitution.cs` performs variable substitution via immutable term rewriting trees.
  Retained as-is for pure functional IR safety and acyclic AST preservation.
- `ContractRuntimePolicy.ThrowIfRuntimeEvaluationEnabled` enforces fail-closed static analyzer
  boundaries against runtime contract reflection.

### Status (part thirty-six)

R402 is now applied: semantic-term and program-builder Boolean validation reuse
the owning `IrFactory` guard while retaining their existing diagnostics. R400,
R401, and R403-R406 are applied direct refactoring simplifications.
R401, R402, and R404 streamline contract resolution flow and IR validation.
R423 is now applied: release scripts use `Get-SharpProofReleaseVersion` rather
than parsing the props file independently.
R433-R436 and R438 are now applied: dead/forwarding analyzer helpers and the
duplicated namespace/operation plumbing were removed without changing diagnostics.
R441, R442, and R445 are now applied: coverage/complexity scripts avoid redundant
I/O, dead validation, and duplicated XML serialization setup.
R456 is now applied: release props keep one package-version authority via
`Version` and the SDK default.
R460 is now applied: zero and unit-modulus interval formatting share one arm.
R447 is now applied: finite-domain oracle entry points share their formula
precondition validation.
R454 and R457 are now applied: analyzer validation reuses generated-code and
attribute snapshots instead of repeating Roslyn queries.

## Second survey, part thirty-seven: R407-R412

This pass surveyed worker execution, protocol serialization, process launching,
and budget tracking across `SharpProof.Worker`, `SharpProof.Worker.Protocol`,
`SharpProof.Worker.Launcher`, and `SharpProof.Host`.

| ID | Finding | Evidence |
|---|---|---|
| R407 | **`SharpProof.Worker.Launcher/Program.cs` contains a verbatim duplicate exit-code inconsistency check.** In `ValidateAndReport` (`SharpProof.Worker.Launcher/Program.cs:409-422`), lines 409-415 and lines 416-422 are identical consecutive `if` blocks checking `workerExitCode is not (null or 0) && response?.RunStatus == WorkerRunStatus.Complete` and printing the identical error message. Deleting the duplicate block removes 7 lines with zero risk. | `SharpProof.Worker.Launcher/Program.cs:409-422` |
| R408 | **Callable and effect verifiers duplicate vacuous contradictory precondition claim assembly.** `CallableVerifier.ContradictoryPostconditions` (`SharpProof.Worker/CallableVerifier.cs:317-340`), `CallableVerifier.VerifyPostconditionsAsync` (`lines 298-308`), and `EffectClaimResultAssembler.Assemble` (`SharpProof.Worker/EffectClaimResultAssembler.cs:51-67`) execute the identical multi-step mutation sequence to set `Vacuity = ContradictoryPreconditions`, assign `ProofCore = [.. entryFeasibility.ProofCore]`, and map `Assumptions = MarkAssumptionsUsed(...)`. Extracting `CallableClaimResultAssembler.CreateContradictory` unifies vacuous claim construction. | `SharpProof.Worker/CallableVerifier.cs:298-308, 317-340`; `SharpProof.Worker/EffectClaimResultAssembler.cs:51-67` |
| R409 | **`WorkerProtocolJson.ValidateForRequest` overloads duplicate a 28-line parameter guard sequence and force launcher branching.** `SharpProof.Worker.Protocol/ProtocolJson.cs:197-227` and lines 229-266 repeat the identical 28-line parameter validation and invariant checking sequence. Because the two overloads are separate, `SharpProof.Worker.Launcher/Program.cs:385-403` contains an 18-line `if/else` block duplicating 8 parameter lines per branch. Making `evidenceAuthority = null` optional on a single method removes ~40 lines across protocol and launcher. | `SharpProof.Worker.Protocol/ProtocolJson.cs:197-266`; `SharpProof.Worker.Launcher/Program.cs:385-403` |
| R410 | **`CallableVerifier.VerifyPostconditionsAsync` duplicates resource limit abort handling and `SharpProofWorker` open-codes resource reading.** In `CallableVerifier.cs:235-239` and lines 244-250, resource limit exhaustion before and after query dispatch executes the identical recovery block (`records.AddRange(CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.ResourceLimit).Skip(index)); break;`). Furthermore, `SharpProofWorker.cs:20-23` open-codes backend resource retrieval instead of calling `ReadResources(backend)` (`lines 607-609`). Unifying the abort sequence and reader call cleans up resource enforcement. | `SharpProof.Worker/CallableVerifier.cs:235-250`; `SharpProof.Worker/SharpProofWorker.cs:20-23, 607-609` |
| R411 | **`SharpProof.Worker.Launcher/Program.cs` contains an isolated single-call arithmetic wrapper `ComputeFinalLimit`.** `SharpProof.Worker.Launcher/Program.cs:303-307` defines `internal static int ComputeFinalLimit(int projectMilliseconds, int terminationGraceMilliseconds) => checked(projectMilliseconds + terminationGraceMilliseconds);` called only once at line 249. Inlining the expression or delegating to `WorkerExecutionEnvelope` consolidates timeout arithmetic. | `SharpProof.Worker.Launcher/Program.cs:249-251, 303-307`; `SharpProof.Worker.Protocol/WorkerExecutionEnvelope.cs:8-26` |
| R412 | **Redundant partial class wrapper `LauncherPresentation.Level` exists solely to expose a private generated method.** `SharpProof.Worker.Launcher/LauncherProjections.generated.cs:64-76` generates `private static string Level(object policy, string advisory)`, forcing `Program.cs:897-903` to declare a 7-line partial class wrapper `internal static string Level(Enum policy, string advisory)`. Emitting the generated method as `internal` directly in `Generate-ProjectionCatalog.ps1` allows deleting the handwritten wrapper. | `SharpProof.Worker.Launcher/Program.cs:897-903`; `SharpProof.Worker.Launcher/LauncherProjections.generated.cs:64-76` |

### Checked and not proposed (part thirty-seven)

- `VerificationCache.cs` calculates deterministic cache keys from canonical compiler manifests
  and input snapshots. Cache eviction and key hashing are required for incremental verification.
- `AcyclicBlockPredicateExecutor.cs` verifies acyclic method control flow bounds. Retained as-is.

### Status (part thirty-seven)

R408-R410 and R412 are `pending`. R407 and R411 are applied trivial launcher cleanups. R408 and R410 unify
claim assembly and budget enforcement in the verification worker. R409 and R412 reduce protocol and projection boilerplate.

## Second survey, part thirty-eight: R413-R419

This pass surveyed SMT backend encoding, Z3 expression management, proof kernel justifications,
cancellation handling, and unsat core validation across `SharpProof.Smt` and `SharpProof.Verify`.

| ID | Finding | Evidence |
|---|---|---|
| R413 | **`Justification` in `SharpProof.Verify` is an unused, empty top-level inheritance layer.** `SharpProof.Verify/Evidence.cs:29-41` declares `public abstract class Justification` with a private protected constructor and zero members, subclassed only by `public abstract class ProofJustification : Justification`. Across the entire repository, all models, proof core collections, justifications, and declarative schemas operate exclusively on `ProofJustification`. Collapsing `Justification` and `ProofJustification` eliminates an empty, redundant inheritance level. | `SharpProof.Verify/Evidence.cs:29-41`; `SharpProof.Verify/DeclarativeModels.generated.cs:64,68`; `SharpProof.DeclarativeModels.catalog.json:941,948` |
| R414 | **`QueryEncoder` constructor performs double iteration over `Variables` and contains an unreachable defensive guard.** In `SharpProof.Smt/IrSmtBackend.cs:407-445`, `QueryEncoder` loops through `Variables` twice: once to filter integer variables and once by index to construct Z3 constants. Both passes call `meter.Consume()` and look up `_factory.GetVariableInfo(variable).Type`. In addition, the second loop contains an unreachable exception branch (`"The model-variable type was not prevalidated."`). Fusing into a single loop avoids duplicate traversals, dictionary lookups, and dead code. | `SharpProof.Smt/IrSmtBackend.cs:407-445` |
| R415 | **Native Z3 integer constant AST handles are re-allocated afresh per query operation.** In `SharpProof.Smt/IrSmtBackend.cs`, `_owner.Own(_context.MkInt(long.MinValue))`, `_context.MkInt(long.MaxValue)`, `_context.MkInt(0)`, and `_context.MkInt(-1)` are called repeatedly across `CheckCore` (lines 173-178), `Bounded` (lines 687-692), `DivideTowardZero` (line 706), and `DivisionDefined` (lines 727-736). Pre-allocating and caching fixed integer constants (`Zero`, `MinusOne`, `LongMin`, `LongMax`) once per query on `QueryEncoder` eliminates repetitive native Z3 handle allocations and reduces `Z3ExpressionOwner` tracking overhead. | `SharpProof.Smt/IrSmtBackend.cs:173-179, 687-692, 706, 727-736` |
| R416 | **Cancellation polling and exception filtering are duplicated between `IrSmtBackend` and `ProofKernel`.** In `SharpProof.Smt/IrSmtBackend.cs:77-116`, `cancellationToken.ThrowIfCancellationRequested()` is called 7 times in 40 lines across catch blocks for `QueryResourceLimitException`, `UnsupportedIrEncodingException`, and general `Exception`. In `SharpProof.Verify/ProofKernel.cs:20-29`, `catch (OperationCanceledException) { throw; }` precedes `catch (Exception)`. Adding `and not OperationCanceledException` to the exception filter in `ProofKernel` and routing `IrSmtBackend` errors through a single failure wrapper cleans up repetitive cancellation checks. | `SharpProof.Smt/IrSmtBackend.cs:77-116`; `SharpProof.Verify/ProofKernel.cs:20-29` |
| R417 | **`ProofKernel.CreateProven` performs redundant `.Distinct()` calls and multi-pass traversal over `UnsatCore`.** `SharpProof.Smt/IrSmtBackend.cs:316-317` already deduplicates and sorts `UnsatCore` (`BackendCheckResult.Unsatisfiable(core.Distinct().OrderBy(...))`). In `SharpProof.Verify/ProofKernel.cs:58-68`, `CreateProven` executes `result.UnsatCore.Any(...)` bounds checking followed by `foreach (var index in result.UnsatCore.Distinct())`. Calling `.Distinct()` allocates an unnecessary hash set over an already-distinct immutable array; fusing bounds checks and justification mapping into a single loop avoids multiple traversals. | `SharpProof.Smt/IrSmtBackend.cs:316-317`; `SharpProof.Verify/ProofKernel.cs:58-68` |
| R418 | **`QueryEncoder.EncodeBinary` repeats `Integer(left)` and `Integer(right)` typechecks and comparison wrappers across 7 switch arms.** In `SharpProof.Smt/IrSmtBackend.cs:597-612`, `Integer(left)` and `Integer(right)` (each doing runtime typecasting) are called 14 times across 7 arithmetic/relational operator arms, each wrapping the result in `new EncodedValue(_owner.Own(...), defined)`. Extracting integer operands once upfront reduces 14 runtime typechecks to 2 and removes repetitive wrapper allocation. | `SharpProof.Smt/IrSmtBackend.cs:597-612` |
| R419 | **`BackendCheckResult` defines a redundant private forwarding constructor.** `SharpProof.Verify/Backend.cs:35-42` defines a private constructor `BackendCheckResult(status, unsatCore, model, failureReason) : this(..., default)` whose sole purpose is to forward to the generated internal constructor in `DeclarativeModels.generated.cs:43-50`. Factory methods `Unsatisfiable`, `Satisfiable`, and `Unknown` can directly invoke the generated constructor, eliminating the redundant overload. | `SharpProof.Verify/Backend.cs:35-42`; `SharpProof.Verify/DeclarativeModels.generated.cs:43-50` |

### Checked and not proposed (part thirty-eight)

- `Z3ExpressionOwner` disposes native Z3 AST handles upon solver query completion.
  Retained as essential native memory safety.
- `OutcomeCachePolicy` evaluates cacheability based on terminal proof outcomes.

### Status (part thirty-eight)

R413, R415-R417 remain `pending`; R414, R418, and R419 are applied and
validated by the SMT and Verify test suites.
R413 is the remaining clean public-model simplification.
R415, R416, and R417 eliminate native handle allocations, cancellation clutter, and redundant LINQ passes.

## Second survey, part thirty-nine: R420-R426

This pass surveyed MSBuild properties, build targets, packaging scripts, release version
extraction, and test compilation links across `scripts/`, `Directory.Build.props`, `eng/`,
and `SharpProof.Verifier`.

| ID | Finding | Evidence |
|---|---|---|
| R420 | **Missing import causes dead item evaluation for compiler-visible properties in self-application.** `eng/self-application/SharpProof.SelfApplication.props:29-34` declares `<CompilerVisibleProperty Include="@(_SharpProofCompilerVisibleProperty)" />` when self-application is enabled and portable packages are absent. However, `SharpProof.CompilerVisibleProperties.props` (which defines `@(_SharpProofCompilerVisibleProperty)`) is never imported in `SharpProof.SelfApplication.props` or `Directory.Build.targets`. Consequently, the item list evaluates to empty and injects 0 properties during self-application builds. Importing the props file restores the intended 9 compiler-visible properties. | `eng/self-application/SharpProof.SelfApplication.props:29-34`; `SharpProof.Package/buildTransitive/SharpProof.CompilerVisibleProperties.props:1-33` |
| R421 | **`_SharpProofPackageBuildTasksPath` duplicates `_SharpProofBuildTasksPath` and creates redundant targets condition re-evaluation.** In `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:11-17`, `_SharpProofBuildTasksPath` and `_SharpProofPackageBuildTasksPath` have character-identical definitions and identical test overrides. In `SharpProof.Verifier.targets:12-13, 35`, both properties are re-evaluated and compared with `!=` on line 35 (which can never trigger). Consolidating on `_SharpProofBuildTasksPath` eliminates 6 lines of redundant property definitions and dead condition checks. | `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:11-17`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:12-13, 35` |
| R422 | **`Test-SharpProofPackagePayloads.ps1` performs redundant disk I/O and reflection double-unpacking for every DLL.** In `scripts/Test-SharpProofPackagePayloads.ps1:236-256`, `Get-SharpProofArchiveAssemblyName` writes the entry bytes to a temp file, queries reflection, and deletes the file at line 237. Lines 248-255 call `Get-SharpProofArchiveAssemblyName` a second time for every DLL entry to build `$payloadEvidence`. Reusing the extracted assembly name avoids duplicate disk I/O and reflection inspection. | `scripts/Test-SharpProofPackagePayloads.ps1:236-256` |
| R423 | **Release version extraction and XML parsing is duplicated across three scripts bypassing `Get-SharpProofReleaseVersion`.** `Invoke-SharpProofContainer.ps1:580-584`, `Test-SharpProofPilots.ps1:91-96`, and `Test-SharpProofReadme.ps1:376-382` each manually parse `SharpProof.Release.props` XML and perform string replacement instead of calling `Get-SharpProofReleaseVersion` (`scripts/Get-SharpProofReleaseVersion.ps1:12-36`). Consolidating onto the central helper enforces release version syntax validation and removes 18 lines of duplicated XML parsing. | `scripts/Invoke-SharpProofContainer.ps1:580-584`; `scripts/Test-SharpProofPilots.ps1:91-96`; `scripts/Test-SharpProofReadme.ps1:376-382`; `scripts/Get-SharpProofReleaseVersion.ps1:12-36` |
| R424 | **Verbose default `IncludeAssets` on `Microsoft.CodeAnalysis.Analyzers` package references adds boilerplate across 5 analyzer projects.** `SharpProof.Analyzer.csproj:9-13`, `SharpProof.Analyzer.Core.csproj:11-15`, `SharpProof.CompilerCollector.csproj:10-14`, `SharpProof.ContractForGenerator.csproj:10-14`, and `SharpProof.CompilerProbe.TestAsset.csproj:15-19` explicitly declare `IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive"` (the default asset list). Compacting to `<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />` or moving it to `Directory.Build.props` removes 15 lines of XML boilerplate. | `SharpProof.Analyzer/SharpProof.Analyzer.csproj:9-13`; `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:11-15`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:10-14`; `SharpProof.ContractForGenerator/SharpProof.ContractForGenerator.csproj:10-14`; `SharpProof.CompilerProbe.TestAsset/SharpProof.CompilerProbe.TestAsset.csproj:15-19` |
| R425 | **`DiagnosticDescriptorCatalogAssertions.cs` link is duplicated across three test projects instead of centrally in `Directory.Build.props`.** `SharpProof.Analyzer.Test.csproj:18-19`, `SharpProof.ContractForGenerator.Test.csproj:16-17`, and `SharpProof.Meta.Analyzers.Test.csproj:12-13` repeat identical 2-line `<Compile Include="..\eng\testing\DiagnosticDescriptorCatalogAssertions.cs" Link="..." />` elements. Hoisting to `Directory.Build.props:89-94` alongside `DictionaryAnalyzerConfigOptions.cs` centralizes test infrastructure linking. | `SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj:18-19`; `SharpProof.ContractForGenerator.Test/SharpProof.ContractForGenerator.Test.csproj:16-17`; `SharpProof.Meta.Analyzers.Test/SharpProof.Meta.Analyzers.Test.csproj:12-13`; `Directory.Build.props:89-94` |
| R426 | **Package source directory and symbol pairing verification is duplicated between consumer testing and release evidence scripts.** `scripts/Test-SharpProofPackageConsumers.ps1:40-93` and `scripts/New-SharpProofReleaseEvidence.ps1:312-418` duplicate a 40-line verification sequence (enumerating 3 `.nupkg`/`.snupkg` pairs, matching `$SharpProofPackageIds`, asserting single version, verifying git commit, and pairing symbol packages). Moving the shared logic to `scripts/SharpProof.PackageIdentity.psm1` eliminates ~40 duplicate script lines. | `scripts/Test-SharpProofPackageConsumers.ps1:40-93`; `scripts/New-SharpProofReleaseEvidence.ps1:312-418`; `scripts/SharpProof.PackageIdentity.psm1:1-111` |

### Checked and not proposed (part thirty-nine)

- `Directory.Packages.props` central package management pins package versions centrally;
  retained as-is for dependency security.
- `Get-SharpProofTcbPaths.ps1` resolves cryptographic trust boundary inventory.

### Status (part thirty-nine)

R420 is merged into applied R328: the proposed shared item import was not needed;
the self-application entry point now directly carries the grouped property list,
so no dead item evaluation remains. R421, R424, and R426 remain `pending`;
R424 is deferred after the compact form changed package asset behavior and
introduced an `AD0001` analyzer failure, so the explicit asset boundary stays.
R422 reuses each archive assembly-name extraction within a payload pass, and
R425 centralizes the shared test link.

## Second survey, part forty: R427-R432

This pass surveyed architecture test suites, temporary directory lifecycles, gate process runners,
differential oracles, and gate compilation hosts across `SharpProof.ArchitectureTest`, `SharpProof.Gates`,
`SharpProof.Testing`, and `Tools/SharpProof.Fuzz`.

| ID | Finding | Evidence |
|---|---|---|
| R427 | **`ProcessResult` record and `RunAsync` process runner are duplicated across 8 test files in `SharpProof.ArchitectureTest`.** `AcceptanceScriptTests.cs:260-287`, `ContainerAuthorityScriptTests.cs:260-281`, `CoverageScriptTests.cs:1615-1642`, `FuzzRunnerEvidenceTests.cs:103-120`, and 4 other test suites each declare private `ProcessResult` records and near-identical asynchronous process spawning wrappers. Furthermore, 8 single-test files roll 20-line `pwsh -NoLogo -File` execution boilerplate. Hoisting `RunProcessAsync` and `ProcessResult` to `ArchitectureRepository.cs` eliminates 8 duplicate records and >200 lines of subprocess boilerplate. | `SharpProof.ArchitectureTest/AcceptanceScriptTests.cs:260-287`; `SharpProof.ArchitectureTest/ContainerAuthorityScriptTests.cs:260-281`; `SharpProof.ArchitectureTest/CoverageScriptTests.cs:1615-1642`; `SharpProof.ArchitectureTest/ArchitectureRepository.cs:5-132` |
| R428 | **28 test methods roll manual temporary directory creation and `try/finally` cleanup bypassing linked `TempDirectory`.** `SharpProof.Package.Test/BuildTaskTests.cs` (across 27 test methods) and `SharpProof.Gates.Test/AcceptancePerformanceContractTests.cs:35-58` manually create temp directories and write `try { ... } finally { directory.Delete(recursive: true); }` blocks, despite `eng/testing/TempDirectory.cs` already being linked into both projects via `Directory.Build.props:76-81`. Adopting `using var temp = new TempDirectory(...)` eliminates ~100 lines of boilerplate and prevents directory leaks on test failures. | `SharpProof.Package.Test/BuildTaskTests.cs:423-438, 446-455, 492-500, 536-545, 596-605, 701-710, 755-765, 936-945, 991-1000, 1163-1175, 1219-1230, 1243-1255, 1278-1295, 1330-1345, 1432-1445, 1467-1480, 1506-1520, 1555-1570, 1600-1615, 1719-1730, 1800-1815, 1871-1885, 1999-2015, 2075-2090, 2122-2135, 2160-2175, 2197-2210`; `SharpProof.Gates.Test/AcceptancePerformanceContractTests.cs:35-58`; `eng/testing/TempDirectory.cs:1-20` |
| R429 | **Subprocess stream piping and timeout termination in `OpenSourceCorpusImporter` and `PerformanceGate` duplicate `GateProcess`.** `OpenSourceCorpusImporter.ReadGitBlobAsync` (`SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:448-489`) and `PerformanceGate.cs:645-706` (`RunDotnetAsync` and `TerminateProcessAsync`) manually re-implement process startup, standard stream reading, timeout cancellation linked tokens, and tree termination instead of delegating to `GateProcess.cs:5-48`. Consolidating onto `GateProcess` standardizes process management across gates and removes ~45 duplicate lines. | `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:448-489`; `SharpProof.Gates/Performance/PerformanceGate.cs:645-706`; `SharpProof.Gates/GateProcess.cs:5-48` |
| R430 | **Diagnostic error formatting and evaluation status descriptions are duplicated between differential oracle and fuzzing.** `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1760-1774` (`FormatErrors`) and `FrontendFuzzing.cs:1747-1758` (`Describe`) duplicate Roslyn error filtering/formatting and `IrEvaluationResult` string formatting from `SharpProof.Testing/IrCSharpDifferentialOracle.cs:59-64, 472-488`. Because `SharpProof.Fuzz` references `SharpProof.Testing`, exposing these formatting helpers in `SharpProof.Testing` eliminates ~30 lines of duplicate formatting logic. | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1747-1774`; `SharpProof.Testing/IrCSharpDifferentialOracle.cs:59-64, 472-488` |
| R431 | **`CorpusGateTests` uses brittle 4-level relative path navigation instead of canonical repository root resolution.** `CorpusGateTests.OssImporterRejectsMitLicenseWithAppendedRestrictions` (`SharpProof.Gates.Test/CorpusGateTests.cs:17-25`) locates license fixtures via `Path.Combine(TestDirectory, "..", "..", "..", "..", "SharpProof.Gates", ...)`. Using `RepositoryLayout.FindRoot()` aligns the fixture path with repository root discovery standards and avoids breakage under multi-TFM build layouts. | `SharpProof.Gates.Test/CorpusGateTests.cs:17-25`; `SharpProof.Gates/RepositoryLayout.cs:5-25` |
| R432 | **Redundant LINQ `.Cast<MetadataReference>()` and unaligned parse options in `AnalyzerGateHost.cs`.** In `SharpProof.Gates/AnalyzerGateHost.cs:160-167`, `trustedPlatformAssemblies.Split(...).Select(...).Cast<MetadataReference>()` invokes a redundant LINQ cast on `PortableExecutableReference` instances. In addition, lines 27-28 duplicate `CSharpParseOptions` instantiation from `AnalyzerTestHost.cs:14-15`. Removing the no-op cast and aligning parser setup cleans up gate compilation infrastructure. | `SharpProof.Gates/AnalyzerGateHost.cs:27-28, 160-167`; `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:14-15` |

### Checked and not proposed (part forty)

- `IrCSharpDifferentialOracle.cs` differential execution compares native IR execution against
  Roslyn Roslyn-compiled dynamic assemblies. Preserved as the gold-standard fuzzing oracle.
- `AcceptancePerformanceContractTests.cs` validates wall-clock and memory thresholds under container bounds.

### Status (part forty)

R427-R430 remain `pending`; R432 is applied for its redundant metadata-reference
cast, while the intentionally separate gate/test parse-option fixtures remain
unchanged. R431 now uses the canonical repository root helper for the license
fixture. R427-R429 eliminate test boilerplate and subprocess duplication; R430
cleans up gate-host and oracle diagnostics.

## Second survey, part forty-one: R433-R439

This pass surveyed Roslyn analyzer engines, diagnostic descriptors, symbol matchers,
generated code policies, and AST/CFG traversal routines across `SharpProof.Analyzer`,
`SharpProof.Analyzer.Core`, and `SharpProof.Meta.Analyzers`.

| ID | Finding | Evidence |
|---|---|---|
| R433 | **`PrimaryConstructorCallableInventory.IsDeclaration` is completely unreferenced dead code.** `SharpProof.Analyzer.Core/PrimaryConstructorCallableInventory.cs:59-71` defines `IsDeclaration(IMethodSymbol, SyntaxNode?, SemanticModel?, CancellationToken)` across 13 lines. Across the entire codebase and test suite, only `TryGet` and `TryGetSynthesizedDefault` are consumed. Deleting this unused method removes 13 lines of dead code. | `SharpProof.Analyzer.Core/PrimaryConstructorCallableInventory.cs:59-71` |
| R434 | **`AnalyzerGeneratedCodePolicy.IsGenerated` defines a redundant single-casting forwarding overload.** `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs:18-29` defines `IsGenerated(IMethodSymbol, SyntaxTree, Compilation, CancellationToken)` which simply casts `method` to `(ISymbol)method` and forwards to `IsGenerated(ISymbol, ...)`. Because `IMethodSymbol` inherits from `ISymbol`, every call passing an `IMethodSymbol` binds directly to the `ISymbol` overload. Removing this forwarding overload eliminates 12 lines of boilerplate. | `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs:18-29` |
| R435 | **`IsExactNamespace` symbol identity matcher is duplicated in `SharpProof.Meta.Analyzers`.** `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1855-1876` and `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:969-984` contain character-identical implementations of `IsExactNamespace(INamespaceSymbol?, params string[])` that traverse parent namespaces. Exposing `SharpProofSoundnessAnalyzer.IsExactNamespace` internally and reusing it removes 22 duplicate lines. | `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1855-1876`; `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:969-984` |
| R436 | **Conversion and parenthesized operation unwrapping loop is duplicated in `SharpProof.Meta.Analyzers`.** `CacheSoundnessRules.UnwrapValue` (`SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:524-541`) and `CancellationBoundaryAnalyzer.Unwrap` (`SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:357-373`) implement identical `while (operation is IConversionOperation or IParenthesizedOperation)` loops to unwrap underlying operands. Consolidating into a single shared helper eliminates 18 redundant lines. | `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:524-541`; `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:357-373` |
| R437 | **`ContractForCompanionValidator.Validate` executes redundant dual-pass nested loops over symmetric comparisons.** `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:63-93` runs two sequential $O(N \times M)$ nested loops over `targets` and `candidates` evaluating `ContractForSymbolMatcher.MemberSignaturesMatch(target, candidate)`: lines 63-77 populate `byTarget` and lines 79-93 populate `byCandidate`. Fusing into a single nested loop over `targets` $\times$ `candidates` populates both maps simultaneously and halves symbol comparison overhead. | `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:63-93` |
| R438 | **`ContractForCompanionValidator.At` is a trivial 7-line wrapper around `Diagnostic.Create`.** `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:267-273` defines `At(descriptor, location, arguments) => Diagnostic.Create(descriptor, location, arguments);`, forcing `ContractForValidationEngine.cs` to declare `using static ContractForCompanionValidator;`. Calling `Diagnostic.Create` directly eliminates unnecessary indirection. | `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:267-273`; `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs:3, 34-39, 88-90` |
| R439 | **CFG `BasicBlock` operations and branch value concatenation is duplicated and open-coded.** `RequiresCallSiteTreeAnalyzer.cs:1093-1100` and `CacheSoundnessRules.cs:1244-1248` define `BlockOperations(BasicBlock)` to concatenate `block.Operations` and `block.BranchValue`, yet lines 1539-1542 and `RequiresCallSiteDiscovery.cs:152-154` inline this concatenation manually. Reusing `BlockOperations` across `SharpProof.Analyzer.Core` standardizes CFG block traversal. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1093-1100, 1539-1542`; `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:152-154`; `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1244-1248` |

### Checked and not proposed (part forty-one)

- `LanguageSubsetGate.cs` performs fail-closed syntax and language version checks on incoming syntax trees.
  Retained as essential subset safety.
- `RequiresCallSiteDiscovery.cs` constructs static call graphs across Roslyn control-flow blocks.

### Status (part forty-one)

R437 and R439 are `pending`. R433-R436 and R438 are applied direct code and AST
unwrapping cleanups. R437 and R439 optimize validator loops and CFG traversal.

## Second survey, part forty-two: R440-R445

This pass surveyed C# metrics, complexity analyzers, code coverage reporters, and baseline
reconciliation scripts across `scripts/CSharpSourceMetrics.ps1`, `scripts/Test-ProductionCSharpComplexity.ps1`,
`scripts/Invoke-SharpProofCoverage.ps1`, and `scripts/Test-SharpProofCoverage.ps1`.

| ID | Finding | Evidence |
|---|---|---|
| R440 | **`CSharpSourceMetricsEngine.Measure` executes dual full-tree AST traversals and uses reflection-based syntax kind conversions.** `scripts/CSharpSourceMetrics.ps1:69-152` iterates over `root.DescendantTokens()` on lines 72-78 and subsequently iterates over `root.DescendantNodes()` on lines 84-142, performing two separate full-tree traversals per file. In addition, `Get-CSharpSyntaxKindName` (`lines 48-57`) and `scripts/GeneratedFileHelpers.ps1:231, 245` convert tokens and node types to strings via `[Enum]::GetName` and reflection rather than comparing integer raw kinds directly. Combining node/token counting into a single traversal and using integer kind checks eliminates repetitive tree walks and string allocations. | `scripts/CSharpSourceMetrics.ps1:48-57, 69-152`; `scripts/GeneratedFileHelpers.ps1:228-261` |
| R441 | **`Test-ProductionCSharpComplexity.ps1` reads files before checking exclusions and performs redundant second disk reads for line counts.** In `scripts/Test-ProductionCSharpComplexity.ps1:131-168`, line 132 loads every file from disk with `Get-Content -Raw` prior to checking `if ($path.Replace('\', '/') -in $approvedGeneratedFiles)` on line 133, discarding generated files after I/O. Then on line 157, line count measurement invokes `$lines = @(Get-Content -LiteralPath $path)`—a second disk read of the same file. Checking exclusions upfront and deriving line counts from the in-memory string eliminates a redundant disk read per file. | `scripts/Test-ProductionCSharpComplexity.ps1:131-168` |
| R442 | **`Test-SharpProofCoverage.ps1` performs redundant string splitting, sorting, and unreachable verification of module identities.** In `scripts/Test-SharpProofCoverage.ps1:408-424`, lines 409-415 verify exact string equality `[string]$authorityNode.modules -cne $expectedModuleIdentityText` (which is already sorted). Lines 416-424 immediately follow by splitting `$authorityNode.modules` by comma, executing `Sort-Object`, rejoining into a string, and asserting inequality again. Because the first condition guaranteed exact match, lines 416-424 are dead defensive array allocations and sorts. | `scripts/Test-SharpProofCoverage.ps1:408-424` |
| R443 | **`Test-SharpProofCoverage.ps1` executes a redundant second-pass sequence-point traversal for aggregate coverage.** `Measure-Coverage` (`scripts/Test-SharpProofCoverage.ps1:515-542`) is called per-project in lines 547-588 to calculate project metrics. On line 592, the script calls `Measure-Coverage -Paths $productionPaths`, re-traversing every sequence point in all files a second time. Because production projects partition the source file universe without overlap, aggregate covered/coverable lines can be accumulated directly during the first loop, eliminating repository-wide second-pass traversal. | `scripts/Test-SharpProofCoverage.ps1:515-542, 544-596` |
| R444 | **Per-file Git diff subprocess spawning in changed-TCB coverage calculation.** In `scripts/Test-SharpProofCoverage.ps1:670-727`, lines 673-708 spawn an individual `git diff` process for each changed file in a loop. In addition, `$changedMetadataFiles` is filtered with `Where-Object` on line 711 while lines 719-727 repeat the exclusion check. Spawning a single `git diff` with all changed paths and recording metadata inline eliminates multi-process invocation latency and duplicate set filtering. | `scripts/Test-SharpProofCoverage.ps1:670-727` |
| R445 | **Duplicated `XmlWriterSettings` setup and stream disposal boilerplate across coverage scripts.** `scripts/Invoke-SharpProofCoverage.ps1:107-118` and lines 289-304 repeat identical 16-line blocks of `XmlWriterSettings` instantiation (UTF-8 without BOM, Indent = true, NewLineHandling = Replace) and `try/finally` disposal logic for runsettings and Cobertura report rewriting. Extracting a shared `Save-XmlDocument` helper centralizes XML encoding policies and eliminates 16 lines of repetitive stream boilerplate. | `scripts/Invoke-SharpProofCoverage.ps1:107-118, 289-304` |

### Checked and not proposed (part forty-two)

- `Resolve-SharpProofReleaseCoverageBaseline.ps1` calculates deterministic minimum line coverage thresholds.
  Threshold verification is essential for regression prevention.
- `CSharpSourceMetrics.ps1` token and trivia counting enforces statement and branch complexity budgets.

### Status (part forty-two)

R440, R443, and R444 are `pending`. R441, R442, and R445 are applied direct I/O,
dead-check, and XML-writer simplifications. R440, R443, and R444 optimize AST
traversals, sequence-point aggregation, and git-diff process spawning.

## Second survey, part forty-three: R446-R450

This pass inspected the verifier budget bridge, finite-domain fuzz oracle, switch-expression flow analysis, and effect-flow helpers.

| ID | Finding | Evidence |
|---|---|---|
| R446 | **Verifier defaults are independently hard-coded in the protocol model, shipped MSBuild props, and the build-task surface.** The generated protocol model declares `DefaultMethodRlimit=20000000`, `DefaultMethodWallTimeMilliseconds=10000`, `DefaultProjectWallTimeMilliseconds=300000`, `MaximumParallelism=4`, `DefaultMaximumExpressionDepth=64`, `WorkerLauncherDefaults.TerminationGraceMilliseconds=1000`, and `WorkerCacheOptions.DefaultMaximumBytes=536870912`; `SharpProof.Verifier.props` repeats the same values as seven literal MSBuild defaults, and `RunVerifier` repeats the project wall-time and termination-grace values as task-property initializers. R319/R363 already record the query-rlimit copy, so this entry covers the remaining budget vocabulary. A generated props fragment or one checked source-of-truth for protocol and MSBuild defaults would remove the drift surface while preserving the consumer override conditions. | `SharpProof.Worker.Protocol/ProtocolModel.schema.json:514,520-521`; `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:27-30,75-95`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:18-26`; `SharpProof.BuildTasks/RunVerifier.cs:79-81` |
| R447 | **`FiniteDomainSmtDifferentialOracle` validates the same formula contract twice.** `IsDefinedForAllAssignments` and `CompareAsync` each null-check `factory`, null-check `formula`, compare `formula.Type` to `factory.BooleanType`, and throw the same Boolean-formula `ArgumentException` (`Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:32-52` and `:125-145`). The methods intentionally retain different static/instance APIs and then perform different oracle work, but the common precondition can be one private helper, avoiding two copies of validation and message text. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:32-52,125-145` |
| R448 | **`SwitchExpressionFacts` repeats the per-arm selection and stop-condition algorithm across known, unknown, and unmatched-path queries.** The constant-value branch of `GetArms` (`lines 145-174`) and `GetArmsForUnknownValue` (`lines 270-298`) both compute a pattern selection, apply the guard, optionally add the arm, and stop on an always-selected arm or an abruptly evaluated pattern/guard. `HasReachableUnmatchedPath` (`lines 200-249`) repeats the same stop checks without the add step. In the known-value branch, `GetArms` and `HasReachableUnmatchedPath` compute `pattern` and then call `GetArmSelection`, which recomputes that pattern before applying the guard. A parameterized per-arm evaluator can preserve the distinct known/unknown selection functions while removing the duplicated traversal and the repeated selection calculation. | `SharpProof.Effects/SwitchExpressionFacts.cs:145-174,200-249,257-298` |
| R449 | **`CoalesceAssignmentFlowCaptures` and `ConditionalTruthOperatorFlowCaptures` duplicate the capture table, ambiguity handling, and reference-resolution loop.** Both classes own the same `HashSet<CaptureId>` plus `Dictionary<CaptureId, IOperation>`, record the first value, mark a capture ambiguous when a later value has a different identity, and follow capture references with a cycle-protected loop until no mapping remains. Only the capture-selection predicate and the small public surface differ: coalesce assignment uses a syntax predicate and exposes `Resolve`, while logical-and/or uses an inline predicate and exposes `TryResolve`. A shared internal capture-resolution component parameterized by the predicate would remove the duplicated state machine within the same `SharpProof.Effects` assembly. | `SharpProof.Effects/CoalesceAssignmentFlowCaptures.cs:6-35`; `SharpProof.Effects/ConditionalTruthOperatorFlowCaptures.cs:6-38` |
| R450 | **`EffectExceptionFlow` duplicates the catch-chain escape state machine for known and unknown thrown types.** `CanEscape` and `CanUnknownEscape` each initialize `canReachNext`/`canEscape`, iterate catches, skip `CatchSelection.Never`, propagate `ContainsRethrow`, and terminate after `CatchSelection.Always`; their only material difference is how the catch type selection is computed (`EffectExceptionFlow.cs:145-171,177-208`). A shared loop taking the type-selection function would centralize the ordered-catch semantics and leave the known/unknown type rules explicit at the call sites. | `SharpProof.Effects/EffectExceptionFlow.cs:145-208` |

### Checked and not proposed (part forty-three)

- `VerificationCache` repeats cleanup scaffolding, but its read and write paths have different rollback ownership and publication semantics; keep the transaction behavior separate until a state-machine extraction can prove those paths equivalent.
- The generated protocol model is not a hand-edited target; the reduction target in R446 is the independent MSBuild/build-task literals and their source-of-truth linkage.

### Status (part forty-three)

R446 and R448-R450 are `pending`. R447 is applied; R446 is a cross-language
default-authority reduction and R448-R450 are local helper extractions.

## Second survey, part forty-four: R451-R453

This pass inspected cache soundness local resolution and the PowerShell process/JSON readers.

| ID | Finding | Evidence |
|---|---|---|
| R451 | **`CacheSoundnessRules` duplicates the local-reference resolution wrapper for value factories and numeric enums.** `ResolveValueFactoryLocal` and `ResolveNumericEnumLocal` independently perform the same cycle guard, `GetReachingLocalValues` call, self-reference check, `Any` traversal, and `finally` cleanup; the latter adds only the `enumType` argument and selects `IsNonCacheableNumericEnumValue` instead of `IsNonCacheableValueFactory`. A private resolver that owns the `LocalResolution` bookkeeping and accepts the value predicate (or a shared reaching-write result) would centralize this fragile cycle/cleanup protocol. `ResolveCacheLocal` and the general semantic-answer resolver have different cycle outcomes and empty-write behavior, so they should not be folded into this reduction without preserving those semantics. | `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:370-394,789-818` |
| R452 | **The gate, dependency-audit, and fuzz-campaign scripts each build an encoded `pwsh` child process by hand.** The first two scripts repeat argument quoting, wrapper escaping, command construction, UTF-16 Base64 encoding, and `Start-Process` with `-NoLogo`, `-NoProfile`, `-EncodedCommand`, working-directory, wait, pass-through, and redirected output/error (`Invoke-SharpProofGateEvidence.ps1:77-107`; `Test-SharpProofDependencyAudit.ps1:223-249`). `Invoke-SharpProofFuzzCampaign.ps1:97-124` has the same protocol behind `Invoke-BoundedDotnetProcess`, expressed as a hashtable and with optional redirection. A common low-level encoded-PowerShell runner could own quoting and launch invariants while callers supply the wrapper, arguments, timeout, and output policy; this complements R305's pairwise census and removes a three-script drift surface. | `scripts/Invoke-SharpProofGateEvidence.ps1:77-107`; `scripts/Test-SharpProofDependencyAudit.ps1:223-249`; `scripts/Invoke-SharpProofFuzzCampaign.ps1:97-124` |
| R453 | **The two fuzz-evidence validators duplicate the full bounded UTF-8 JSON read protocol.** `Assert-SharpProofFuzzRunnerResult` and `Read-SharpProofRetainedFuzzSeedManifest` each open a shared-read `FileStream`, reject empty or over-1048576-byte input, fill an exact-size byte array with a loop that rejects short reads, check `ReadByte()` for concurrent growth, decode with strict UTF-8, and parse a `JsonDocument`; only the error labels and schema validation differ. A helper returning the parsed document (with an explicit disposal contract) or the validated byte/text payload can preserve the different schemas while centralizing the size, encoding, and race checks. | `scripts/Assert-SharpProofFuzzRunnerResult.ps1:62-96`; `scripts/SharpProof.FuzzEvidenceLifecycle.ps1:82-114` |

### Status (part forty-four)

R451-R453 are `pending`. They are scoped helper extractions; no implementation or build files were edited.

## Second survey, part forty-five: R454-R456

This pass inspected the analyzer feature pipeline and verifier/release MSBuild properties.

| ID | Finding | Evidence |
|---|---|---|
| R454 | **`AnalyzerFeaturePipeline.ValidateMethodAttributes` repeats a generated-code check for the same declaration.** The method obtains `method.DeclaringSyntaxReferences[0]` at entry and calls `AnalyzerGeneratedCodePolicy.IsGenerated` with that tree before any analysis. Inside the only later branch that can continue for a concrete semicolon accessor, it obtains the same first declaring syntax reference again and repeats the same four-argument check; no declaration, compilation, or cancellation context has changed. Cache the initial result (or the syntax tree) and reuse it, eliminating the second syntax lookup and generated-code scan while retaining the early-return behavior. | `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs:86-94,115-126`; `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs:31-46` |
| R455 | **`SharpProof.Verifier.targets` copies one verification-active condition across four evaluation points.** `_SharpProofValidateRuntimeClosure`, the initialization `PropertyGroup`, the compiler-owned-output `ItemGroup`, and the public `SharpProofVerify` target each spell out the same conjunction of `SharpProofVerify`, normalized profile, supported host, non-design-time build, and `BuildingProject`. A private `_SharpProofVerificationActive` property evaluated at the same import/evaluation phase, or a narrowly scoped shared target condition, could own this policy once; the weaker invalidation and unsupported-host conditions should remain separate because they intentionally run when verification is inactive or unsupported. | `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets:33-39,86-113,244-249` |
| R456 | **`SharpProof.Release.props` explicitly assigns `PackageVersion` to the same value already assigned to `Version`.** The release props set both to `$(SharpProofPackageVersion)`, while the SDK normally derives `PackageVersion` from `Version`; retaining both creates a second authority with no differing value. Remove the explicit `PackageVersion` assignment or keep it only if packaging tests prove a deliberate override is required, and preserve the independent assembly/file/informational version assignments. | `SharpProof.Release.props:7-12` |

### Status (part forty-five)

R455 is `pending` review-only. R454 is applied for generated-code reuse; R456
is applied because the SDK derives PackageVersion from Version without a
duplicate assignment.

## Second survey, part forty-six: R457-R460

This pass inspected analyzer control-attribute validation and the small dataflow domains.

| ID | Finding | Evidence |
|---|---|---|
| R457 | **`SharpProofControlAttributePolicy.ValidateDeclaredScope` walks the same symbol attributes twice.** It first calls `ValidateScope`, whose loop enumerates `symbol.GetAttributes()` to classify and validate `[SharpProofSuppress]`/`[SharpProofTrusted]`, then immediately calls `symbol.GetAttributes()` again to find rejected control attributes. Cache the immutable attribute snapshot and pass it into the validation routine, or combine the operations with an explicit diagnostic-order policy; this removes repeated Roslyn attribute retrieval without conflating the two diagnostic categories. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:19-45,147-172` |
| R458 | **Control-attribute invalid-reason reporting repeats the same diagnostic assembly in two paths.** `ValidateNestedCallableDeclaration` extracts a constant argument and then checks for a non-empty reason, marks the attribute, substitutes `<empty>`, selects `[SharpProofSuppress]` or `[SharpProofTrusted]`, and creates `InvalidContractArgumentDiagnostics`; `ReportInvalidReason` performs the same mark/empty-label/attribute-name/reason sequence for symbol-level attributes. A shared reporting helper taking the already-extracted reason and location can retain the different syntax/metadata extraction while centralizing the diagnostic contract. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:93-115,174-193` |
| R459 | **`SharpProofControlAttributePolicy` duplicates the suppress/trusted tri-state decision in two overloads.** The `AttributeData` overload and the `INamedTypeSymbol` overload both return `true` for Suppress, `false` for Trusted, and `null` otherwise; only the equality adapter differs (`ContractSelectionInventory.Is` versus direct symbol comparison). A single helper over an attribute-type identity (with the same original-definition normalization) could own the tri-state decision and leave the two adapters thin. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:196-220`; `SharpProof.Contracts/ContractSelectionInventory.cs:229-237` |
| R460 | **`IntervalValue.ToString` has two switch arms with identical output.** The `Modulus.IsZero` and `Modulus.IsOne` arms each return the same invariant-culture `[$lower, $upper]` representation; only the fallback includes congruence details. Combining the guards (`IsZero || IsOne`) removes a redundant branch while preserving the canonical text for both unconstrained and singleton congruence forms. | `SharpProof.Dataflow/IntervalValue.cs:144-149` |

### Status (part forty-six)

R458-R459 are `pending` review-only candidates. R457 and R460 are applied:
attribute snapshots and equivalent interval-format switch arms are shared.

## Second survey, part forty-seven: R463, and two ledger repairs

### Ledger repairs made in this part

Two defects in this ledger itself were found and fixed while resuming after an
interrupted session:

- **Duplicate section and ID.** A second section titled "part thirty-two" reused
  `R373` and `R374` for Dataflow findings unrelated to the compiler-probe and
  preprocessor-symbol findings already holding those IDs. The Dataflow section is
  now "part thirty-two (continued)" and its two colliding entries are renumbered
  **R461** and **R462**. Its other entries, R375-R379, were already unique and are
  unchanged. No finding text was altered.
- **A duplicate append.** A re-entry of "part twenty-five" was appended out of
  order after part forty-five and has been removed. The surviving part twenty-five
  (R327-R329) is the original.

The only remaining repeated IDs are `R289` and `R299`, which appear once in a
status table and once in their originating section - the same convention already
used by R026, R057, and R064.

### Correction: R299 is resolved

Earlier progress notes in this session repeatedly listed R299 as a standing
defect. **That is out of date.** `eng/acceptance/contract.json` now pins
`expectedCatalogCount: 248` and the catalog array in
`Test-SharpProofTrustedMutations.ps1` holds exactly 248 entries. The ledger's
refuted table already records this. The three items still open from that group are
R301, R310, and R315, each re-verified against the current tree in this part.

### The finding

| ID | Finding | Evidence |
|---|---|---|
| R463 | The 14 generators use **two different mechanisms to declare what they produce, and only one of them scales**. `Generate-DeclarativeModels.ps1` and `Generate-ProjectionCatalog.ps1` are data-driven: each hard-codes exactly one path in the script - its catalog input - and reads every output location from that catalog's `outputs[].path`, ten each. Between them they own **20 of the 42 generated files, 48 percent, from 2 hard-coded paths**. The other twelve generators are code-driven, declaring **33 output paths** across their `param()` blocks and default-assignment blocks. Adding an output to a declarative-model or projection family is a single catalog edit; adding one to any other generator means editing the parameter block, adding another `if IsNullOrWhiteSpace($X) { $X = Join-Path ... }` default block, adding a `GetFullPath` normalization, and adding another `Update-SharpProofGeneratedFile` call. That is the root cause of R251's 43 repetitions of the default-path idiom, and it recasts both R251 and the generator half of deferred R107: the useful change is not a shared helper for the idiom, it is adopting the catalog-driven output declaration that two of the fourteen generators already use and that already covers half the generated tree. The pattern is proven in-repository. | `scripts/Generate-DeclarativeModels.ps1:163-190`; `scripts/Generate-ProjectionCatalog.ps1`; `SharpProof.DeclarativeModels.catalog.json` (10 `outputs[].path`); `SharpProof.Projection.catalog.json` (10); the remaining 12 `scripts/Generate-*.ps1` (33 hard-coded paths) |

### Checked and not proposed (part forty-seven)

- **The generated-output approval list is exactly complete for its scope.**
  `eng/generated/approved-outputs.v1.json` holds 41 entries against 42 tracked
  generated files. The single difference,
  `SharpProof.Specs.Test/ApiSpecRuntimeWitnesses.generated.cs`, is correctly
  excluded: `BoundaryEnforcementTests` asserts exact set equality between the
  approval list and generated files discovered under the *production*
  (`BannedApiProjects`) set, and that file is in a test project. It is still
  protected from staleness, because `Generate-ApiSpecCatalog.ps1` emits it via
  `$RuntimeWitnessOutputPath` and compares it under `-Verify`. No gap.
- **Every one of the 42 generated files has a producing generator.** A file-to-script
  mapping found zero orphans: no generated file survives whose generator was
  removed, and no generator emits an untracked or unapproved file.
- The approval check is stronger than a path list: `BoundaryEnforcementTests`
  discovers generated files both by `*.{g,generated}.cs` filename **and** by
  scanning contents for an `// <auto-generated>` header, then requires the union
  to equal the approved set exactly. A hand-written file that gained the header,
  or a generated file that lost the naming convention, would both be caught.

### Status (part forty-seven)

R463 is `pending` and is a re-framing rather than a new work item: it proposes no
change to any generated output, only that the remaining twelve generators adopt
the one of two existing in-repository conventions that scales. It should be
considered before R251 or the generator half of R107 are actioned, because
adopting the catalog-driven form dissolves most of what those two propose to
factor into helpers.

## Second survey, part forty-eight: R464-R467

This pass inspected contract-API identity, descriptor lookup, binding, and compiler identity helpers.

| ID | Finding | Evidence |
|---|---|---|
| R464 | **`ContractApiIdentityResolver` duplicates assembly-metadata extraction in its two expected-value readers.** `ReadExpectedPayloadSha256` and `ReadExpectedModuleVersionId` each enumerate `typeof(ContractApiIdentityResolver).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()`, filter one key with ordinal comparison, select `Value`, deduplicate, and materialize an immutable array; only the key-specific decoding differs. A private `ReadMetadataValues(string key)` helper can own the query and leave the SHA-256 byte parsing and MVID parsing independent. | `SharpProof.Frontend/ContractApiIdentityResolver.cs:309-322,350-361` |
| R465 | **`ContractApiMetadata` repeats the same linear descriptor lookup loop for methods and attributes.** `TryGetMethod` and `TryGetAttribute` both iterate a generated descriptor collection, compare one string field with `StringComparison.Ordinal`, assign the matching descriptor, and otherwise assign `default` and return `false`; only the collection and selector differ. A small generic `TryFind` helper or generated lookup dictionaries can centralize this repeated lookup protocol without changing the generated descriptor shape. | `SharpProof.Frontend/ContractApiMetadataRuntime.cs:17-55` |
| R466 | **`ContractBinder` duplicates the uncached `BindCore` wrapper for full and requires-only bindings.** `BindUncached` and `BindRequiresUncached` each pass the same target, `implementationBody: null`, and `CancellationToken.None` to `BindCore`, differing only in `requiresOnly: false` versus `true`. A single parameterized cache callback (or one helper accepting `requiresOnly`) can remove the duplicate wrapper while preserving the separate binding dictionaries and public semantics. | `SharpProof.Contracts/ContractBinder.cs:72-100,124-140` |
| R467 | **`CompilerIdentityBridge` duplicates the documentation-ID fallback template for symbols and types.** `SymbolReference` and `TypeReference` each attempt a Roslyn documentation ID, test for a non-empty result, and fall back to `FallbackReference`; only `CreateDeclarationId` versus `CreateReferenceId` differs. A shared helper that accepts the ID-producing delegate can centralize the fallback rule while retaining the distinct Roslyn ID APIs. | `SharpProof.Frontend/CompilerIdentityBridge.cs:187-212` |

### Status (part forty-eight)

R464 is applied: both
contract identity readers now share one ordinal metadata-value query, while the
SHA-256 and MVID decoding rules remain independent. R465 is rejected: a generic
descriptor helper adds lines and delegate indirection to two already-small,
type-specific loops, so it is not a net reduction. R466 is applied: both
uncached binding paths share one parameterized wrapper, with their dictionaries
and requires-only flags unchanged. R467 is applied: symbol and type display
references share the fallback decision while retaining their distinct Roslyn
documentation-ID factories.


## Second survey, part forty-nine: verifying the ledger's own Applied table

A different kind of pass. Rather than looking for new reductions, this one audits
**this ledger against the tree**: every Applied row whose reduction is a removal
makes a falsifiable claim, so each can be re-checked. This matters because the
Applied table is the artifact a reader trusts when deciding what remains to do,
and because part forty-seven already found one status claim that had gone stale in
the opposite direction (R299, recorded as refuted after the contract was
corrected).

Twenty-three Applied rows describe a removal. Fourteen are mechanically
verifiable without building; all fourteen **verified as still applied**:

| Applied item | Claim | Current tree |
|---|---|---|
| applied R064, R158 | Retain only `dev`, `loop`, `tooling` Compose services | exactly those three services present |
| applied R130 | Remove the single-arm entrypoint `case` | the three remaining `case` statements are the multi-arm source-requirement and untracked-path ones |
| applied R157 | Remove the suppression scoped to deleted `ApiSpecModel.cs` | no `ApiSpecModel` reference in `.editorconfig` or `.globalconfig` |
| applied R192 | Remove unused `GhostProbe.TouchObject` | no `TouchObject` anywhere in tracked C# |
| applied R231 | Remove no-op empty `SharpProofSpecificationPacks` assignments | zero remaining `== ''` self-assignments in any `.props` |
| applied R232 | Remove `PortableAnalyzer` from production classification | zero occurrences in `Directory.Build.props` |
| applied R234 | Remove redundant implicit-usings and the empty Attributes global-usings file | `SharpProof.Attributes/GlobalUsings.cs` gone |
| applied R236 | Consolidate editorconfig suppressions into brace globs | 9 per-file sections remain, and all 15 glob-expanded targets exist |
| applied R241 | Remove `chown` redundant with `install -d -o -g` | zero `chown sharpproof:sharpproof` in the entrypoint |
| applied R245 | Remove duplicate Dev Container env overrides and empty `forwardPorts` | zero occurrences of either in `devcontainer.json` |
| applied R248 | Remove the superseded `.cursorrules` | file gone |
| applied R266 | Remove the undefined `SHARPPROOF_PORTABLE_ARGUMENT_GUARD` term | zero occurrences in `ArgumentNullGuard.cs` |
| applied R289 | Replace the ordinal string-sequence helper with `SequenceEqual` | `Test-OrdinalStringSequenceEqual` no longer defined |
| applied R320 | Remove the unreferenced `-Verify` branch, retain the formatting tool | `Format-CSharp.ps1` is now 76 lines with `param()` empty and zero `Verify` references |

**R327 needed a second look and is correct.** Its wording - "retaining the two
excluded-project declarations" - reads as a discrepancy, because three `.csproj`
files still declare `TreatWarningsAsErrors`. They are not three retentions:
`SharpProof.Testing` and `SharpProof.CompilerProbe.TestAsset` set it to `true` and
are the two production-excluded projects the entry means, while
`samples/Diagnostics` sets it to **`false`** - a deliberate sample override,
outside the production classification entirely and a different kind of
declaration. The claim holds; only the phrasing invites the misreading.

### Checked and not proposed (part forty-nine)

- The nine Applied removals not listed above - R005, R113, R135, R146, R159,
  R196, R198, R199, R228 - describe the removal of specific overloads, branches,
  members, or catch clauses whose absence cannot be distinguished from "never
  existed" by searching the current tree alone. Confirming them needs the
  pre-change revision, which is outside what this read-only pass should assume.
  They are recorded here as *unverified in this pass*, not as doubtful.
- No Applied removal was found to have regressed. For a branch that has been
  edited concurrently during this survey, that is a meaningful result about the
  ledger's reliability, not a formality.

### Status (part forty-nine)

This part adds no `pending` item. It exists so that a reader can rely on the
Applied table: fourteen of its removal claims were re-derived from the current
tree in this pass, one (R327) was re-read carefully and confirmed against a
misleading phrasing, and one status claim found earlier in the session (R299) had
already been corrected in the ledger before this pass reached it. The three items
still genuinely open from the defect group are **R301**, **R310**, and **R315**,
each re-verified against the current tree in part forty-seven.


## Second survey, part fifty: R468-R470 - the MSBuild target graph

A build-system path not previously traced: every `<Target>` declared across all
tracked `.props`, `.targets`, and `.csproj` files, with its `BeforeTargets`,
`AfterTargets`, and `DependsOnTargets` hooks. Nineteen targets are declared. Two
target *names* are each declared twice.

| ID | Finding | Evidence |
|---|---|---|
| R468 | **`_CopySharpProofCompilerRuntime` is declared byte-identically in two projects.** `SharpProof.Gates.csproj` and `SharpProof.Gates.Test.csproj` each define a target of the same name, with the same `AfterTargets="Build"`, copying the same two files - `$(RoslynTargetsPath)\bincore\Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` - to `$(TargetDir)`. The bodies are identical, not merely similar. `Directory.Build.props` already has the mechanism to place shared content into named projects (it does exactly this for five shared test sources), so this is a candidate for one conditional declaration rather than two copies, or for a small shared `.targets` import. | `SharpProof.Gates/SharpProof.Gates.csproj:42-47`; `SharpProof.Gates.Test/SharpProof.Gates.Test.csproj:16-21` |
| R469 | **`_SharpProofPrepareNuspecProperties` is declared twice with the same name and different bodies.** `SharpProof.Package.csproj` sets `NuspecProperties` to `version;configuration;repositorycommit`; `SharpProof.Verifier.csproj` sets the same three plus `nativeroot`, and additionally guards that the build is inside the canonical container. Each project legitimately packs its own nuspec, so this is not redundant work - but two different implementations sharing one target name is a legibility hazard: a reader grepping for the target finds two answers and no indication which applies. The shared `version=$(SharpProofPackageVersion);configuration=$(Configuration);repositorycommit=$(RepositoryCommit)` triple is genuinely duplicated, and it restates the same values R291 already found duplicated between `SharpProof.Release.props` and the two `.nuspec` files - so the nuspec-property assembly is a third site for that vocabulary. Distinct names, or one shared target taking the extra property as input, would remove both problems. | `SharpProof.Package/SharpProof.Package.csproj:42-48`; `SharpProof.Verifier/SharpProof.Verifier.csproj:47-56`; R291 |
| R470 | **Eight targets hook `CoreCompile` and five of them are `BeforeTargets` validators that throw, with no declared order among them.** The five are `_SharpProofValidateSourceTreeConfiguration` and `_SharpProofRequireSourceTreeVerifierPackage` (source-tree consumer), `_SharpProofValidateConfiguration` and `_SharpProofRequireVerifierPackage` (packaged consumer), and `_SharpProofValidateSelfApplication`; `GenerateSharpProofAttributesPayloadIdentity` also runs `Before=CoreCompile`, and `SharpProofVerify` and `SharpProofRejectUnsupportedWorkerHost` run `After`. MSBuild does not guarantee an order between targets sharing a `BeforeTargets` hook, so when more than one validator's condition is satisfied the *diagnostic the user sees* depends on evaluation order rather than on which problem is more fundamental. The source-tree and package families are gated by different presence properties and are not expected to be active together, so this is a latent legibility risk rather than an observed defect - but it is worth recording because the two families are near-mirrors of each other (see R229/R290 for the same source-tree/package split in the assembly closure, and R230 for the empty relay target that exists only to preserve the symmetry). Making the intended precedence explicit through `DependsOnTargets` would make the first reported error deterministic. | `SharpProof.AnalyzerConsumer.props:98,104`; `SharpProof.Package/buildTransitive/SharpProof.targets:60,70`; `eng/self-application/SharpProof.SelfApplication.props:63`; `SharpProof.Frontend/SharpProof.Frontend.csproj:29`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets` |

### Checked and not proposed (part fifty)

- **Nineteen targets across the whole build is a small, legible surface**, and
  seventeen of the nineteen names are unique. The hook points used are all
  standard SDK extension points (`CoreCompile`, `Build`, `Clean`, `Restore`,
  `PrepareForBuild`, `GenerateNuspec`, `ResolvePackageAssets`,
  `AssignProjectConfiguration`, `GenerateMSBuildEditorConfigFile`), with no
  redefinition of SDK targets and no `Inputs`/`Outputs` incremental-build claims
  that could silently skip. Nothing here suggests the build has grown an
  unmanaged extension surface.
- `_SharpProofCleanupInvocation`, `_SharpProofValidateConsumerConfiguration`, and
  `_SharpProofValidateRuntimeClosure` declare no hook of their own and are reached
  only through `DependsOnTargets` or `OnError`. That is correct for
  helper/cleanup targets and is not an orphan.
- `_RequireSharpProofCanonicalContainer` hooking both `Restore` and
  `PrepareForBuild` is deliberate double coverage for the container gate, since
  restore runs in a separate pass from build. Not duplication.

### Status (part fifty)

R468 is applied: the two identical Gates project targets now live once in
`Directory.Build.targets`, guarded by the two project names. R469 is `pending`
and is primarily a naming fix,
with a small shared-value component that belongs with R291. R470 is `pending` and
is a determinism-of-diagnostics question rather than a reduction - it removes no
lines and may add a few, so it should be judged on whether deterministic error
reporting is wanted, not on size.

## Second survey, part fifty-one: R471-R475

This pass inspected effect-domain defaults, capability encoding, trust scopes, analysis result construction, and compiler-call preparation.

| ID | Finding | Evidence |
|---|---|---|
| R471 | **`EffectSummaryDomain` reimplements the equivalence default provided by `ClosedAbstractDomain<T>`.** It implements `IAbstractDomain<EffectSummary>` directly and repeats `AreEquivalent` as two order checks; `SharpProof.Dataflow.ClosedAbstractDomain<T>` supplies that exact implementation while leaving `Widen` abstract. Deriving this domain from the shared base and retaining its effect-specific `LessThanOrEqual`, `Join`, `Widen`, and `Havoc` logic removes the duplicate equivalence plumbing while preserving the public type and null-guard behavior. | `SharpProof.Effects/EffectSummary.cs:150-192,223-233`; `SharpProof.Dataflow/ClosedAbstractDomain.cs:6-24` |
| R472 | **`EffectCapabilitySet.IsUnknown` repeats the unknown-bit value numerically.** The constructor and validation logic already derive the unknown marker from `EffectCapabilityKind.Unknown & ~EffectCapabilityKind.AllKnown`, but the property tests `(EffectCapabilityKind)(1 << 13)` directly. Reading the marker from the enum/catalog expression once removes a hidden second authority and keeps the predicate correct if the capability layout changes. | `SharpProof.Effects/EffectValues.cs:5-27`; `SharpProof.Effects/EffectContractValues.cs:15-18`; `SharpProof.Effects/EffectContractMappings.catalog.json:10-27` |
| R473 | **`TrustedBoundaryPolicy.EnumerateScopes` duplicates `SharpProofControlAttributePolicy.EnumerateScopes` across assembly boundaries.** Both iterators yield the method, its associated property, every containing type, and its containing assembly in the same order. A neutral shared contract-scope enumerator in a lower dependency layer could serve both policies; the trust predicate and rejected-attribute handling should remain separate. | `SharpProof.Effects/TrustedBoundaryPolicy.cs:50-69`; `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:119-136` |
| R474 | **`EffectAnalysisSession.Analyze` and `AnalyzeAll` duplicate effect-result assembly.** Both compute `EffectModuleInitialization.SummarizeBeforeEntry`, combine it with the method summary through `EffectStep`, and expose direct witnesses only when initialization cannot prevent body entry; the single-method path does this at lines 113-129 and the all-method path repeats it inside the projection loop at lines 141-157. A private result factory taking the method, summary, initialization, and optional node witnesses can preserve the locking and lookup differences while centralizing the result semantics. | `SharpProof.Effects/EffectAnalysisSession.cs:104-129,132-157` |
| R475 | **`CompilerCallableLowerer.TryPrepareSpecCall` and `TryPrepareSummaryCall` repeat the same direct-call admission guards.** Each clears its out result, requires an IR target, requires `RoslynProgramLowerer.IsDirectInvocation`, and rejects any ref/in parameter before entering its spec- or summary-specific lookup. A shared `TryGetAdmissibleByValueCall` precheck can remove the repeated guards while keeping the two distinct resolution and preparation paths. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs:273-319` |

### Status (part fifty-one)

R474 is a `pending` review-only reduction candidate. R471 is applied:
`EffectSummaryDomain` now derives from `ClosedAbstractDomain<EffectSummary>` and
retains its explicit Widen forwarding, while inheriting the shared equivalence
implementation. R472 is applied:
`EffectCapabilitySet` now uses one compile-time unknown-marker expression for
validation and `IsUnknown`, removing the numeric bit-position duplicate. R473
is applied: analyzer, collector, and effects policies now consume one shared
scope iterator with the same ordering as the former local copies.
R475 is applied: both callable-lowering paths now share the direct, by-value
admission predicate while retaining their distinct spec/summary validation.


## Second survey, part fifty-two: R476 - the shipped consumer property surface

A new angle: enumerating every `SharpProof*` MSBuild property across all build
files, separating definitions from uses, and then comparing the *shipped*
consumer surface against the documentation. The dead-property half of this
produced nothing (see below); the public-surface half produced one finding.

| ID | Finding | Evidence |
|---|---|---|
| R476 | The shipped consumer build files maintain a clear naming convention - **84 underscore-prefixed `_SharpProof*` internals** against **17 non-underscore properties with settable defaults**, the latter being the consumer-facing API. But three of those seventeen are **path overrides that redirect where analyzer and verifier binaries are loaded from, are settable by any consumer, and appear in no tracked documentation**: `SharpProofAnalyzerDirectory` and `SharpProofCollectorDirectory` in `SharpProof.props`, and `SharpProofToolsDirectory` in `SharpProof.Verifier.props`. `SharpProofCompilerCollectorPath` in `SharpProof.targets` is a fourth, settable through a different guard shape. The repository's own text confirms the intent: `SharpProof.ConsumerContract.props:17` tells a consumer to "use `SharpProofAnalyzerDirectory` only for package testing". So these are test seams wearing the public naming form, shipped inside the NuGet package, with nothing in the property name or the documentation marking them as unsupported. This is more than cosmetic because of what they control - a consumer who sets `SharpProofAnalyzerDirectory` changes which analyzer assemblies the compiler loads, which is exactly the substitution the package's own layout checks (R290) exist to pin down. Either the underscore convention should extend to them, or the "package testing only" note should be documentation rather than a sentence inside an unrelated error message. | `SharpProof.Package/buildTransitive/SharpProof.props:4,9`; `SharpProof.Package/buildTransitive/SharpProof.targets:11`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props`; `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props:17` |

### Checked and not proposed (part fifty-two)

- **There are no dead MSBuild properties.** 152 distinct `SharpProof*` and
  `_SharpProof*` names appear across the build files. A first scan reported six
  as defined-but-never-referenced - `_SharpProofAttributesPayloadIdentity`,
  `_SharpProofDefaultAnalyzerDependency`, `_SharpProofDefaultCollectorDependency`,
  `_SharpProofImmutableRuntime`, `_SharpProofMappedAnalyzerProjectReference`,
  `_SharpProofMappedCollectorProjectReference`. **All six are false positives**:
  each is an `ItemGroup` item consumed through `@(...)` rather than `$(...)`, and
  each was verified live. Recorded explicitly because a `$()`-only scan is the
  obvious way to run this check and it is wrong six times out of six here.
- **There are no undefined-property bugs.** Eleven names are referenced as
  `$(...)` without a definition in any build file, and every one is a legitimate
  external input: two are deliberate removed-property guards that error when set
  (`SharpProofMode`, `SharpProofPortableAnalyzerPath` - the latter belonging to
  the same removal as R232); three are test-injection hooks set by
  `WorkerMsBuildIntegrationTests` (`_SharpProofTestBuildTasksPath`,
  `_SharpProofTestContractForGeneratorPath`, `_SharpProofTestWorkerProtocolPath`);
  two are documented consumer opt-ins (`SharpProofVerifyCacheDirectory`,
  `SharpProofVerifySarifFile`, both described in `docs/analysis-limits.md`); two
  are set by container tooling (`SharpProofSelfApplication`,
  `SharpProofSourceCommit`); and two are MSBuild task `Output` bindings
  (`_SharpProofVerifierExitCode`, `_SharpProofVerifierHasStructuredError`).
- The remaining thirteen consumer-settable properties are all documented and all
  follow the `SharpProofVerify*` naming family: cache enablement and size,
  parallelism, expression depth, the two rlimits, the three wall-time budgets,
  policy and assumption policy, and the request/result/manifest file paths. That
  surface is coherent; R471 is about the four that sit outside it.

### Status (part fifty-two)

R476 is `pending` and is a naming/documentation decision rather than a reduction -
it removes no lines. It is filed because the property surface is the package's
public contract, and four settable entries on it currently have neither a
convention marking nor a documented meaning while controlling analyzer binary
resolution.

## Second survey, part fifty-three: R477-R478 - worker input and protocol file plumbing

| ID | Finding | Evidence |
|---|---|---|
| R477 | **`WorkerInputSnapshot.LoadAsync` does no asynchronous work.** The method performs synchronous path resolution, file I/O, decoding, JSON parsing, hashing, and cancellation checks, then returns `Task.FromResult`; its only caller awaits the already-completed task. This makes the name and `async`-shaped API suggest nonblocking I/O while retaining all blocking work on the caller thread. Either expose a synchronous `Load` and let the caller remain explicit, or implement genuinely asynchronous file reads if the worker boundary needs to avoid blocking; keeping the current shape is accidental complexity at the scheduling boundary. | `SharpProof.Worker/WorkerInputSnapshot.cs:7-55`; `SharpProof.Worker/SharpProofWorker.cs:112-121` |
| R478 | **`WorkerProtocolJson.ComputeFileSha256` and `OpenJsonReader` duplicate the bounded JSON-file opening protocol.** Both inspect `FileInfo(path).Length`, reject empty/oversized input, open a sequential shared-read `FileStream`, verify that the length did not change, and wrap it in `BoundedReadStream`; one then copies raw bytes for hashing while the other wraps the stream in a strict-UTF-8 `StreamReader`. A shared `OpenBoundedJsonFile` helper (with ownership and expected-length semantics kept explicit) could centralize the size, race, sharing, and stream-limit rules without merging the distinct hash and text consumers. | `SharpProof.Worker.Protocol/ProtocolJson.cs:41-67,100-138` |

### Status (part fifty-three)

R477 remains a `pending` review-only candidate because changing a synchronous
implementation behind an async API needs a separate scheduling decision. R478
is applied: hashing and UTF-8 readers share size, race, sequential-open, and
bounded-stream checks while retaining distinct empty-file errors and consumers.


## Second survey, part fifty-five: R480 - a check left behind by applied R243

**This is a defect, not a reduction candidate**, and it is the second instance of
the same failure mode as R299: an applied reduction updated the implementation but
not the assertion that pins it.

| ID | Finding | Evidence |
|---|---|---|
| R480 | **`Test-SharpProofContainerContract.ps1` still requires `Directory.Build.targets` to contain a literal that applied R243 removed, so the container-contract gate throws at HEAD.** R243 correctly replaced the hardcoded marker path in `Directory.Build.targets` with `Exists('$(SHARPPROOF_CONTAINER_CONTRACT)')`, resolving the marker through the environment variable the Dockerfile defines. But `Test-SharpProofContainerContract.ps1:377-381` reads `Directory.Build.targets` into `$directoryBuildTargets` and throws `'Repository MSBuild entry points must reject host execution.'` unless that text case-sensitively matches **three** patterns: `_RequireSharpProofCanonicalContainer`, `SHARPPROOF_CONTAINER`, and `/etc/sharpproof/container-contract\.json`. The first two still match; the third cannot, because the literal is gone - `Directory.Build.targets` contains zero occurrences of it. The `-cnotmatch` therefore fires and the script throws. Blast radius: the script is invoked by `eng/acceptance/Verify.ps1:249` (the acceptance gate) and twice by `Invoke-SharpProofContainer.ps1`, at line 120 for the **`contract`** command and line 222 for the **`pr-gates`** command - which is what `docker compose run --rm tooling pr` runs in CI. The fix is to assert the property reference (`SHARPPROOF_CONTAINER_CONTRACT`) rather than the resolved literal, which is what the other two patterns in the same condition already do. | `scripts/Test-SharpProofContainerContract.ps1:33-34,377-381`; `Directory.Build.targets:5-9`; `eng/acceptance/Verify.ps1:249`; `scripts/Invoke-SharpProofContainer.ps1:118-121,216-222`; applied R243 |

### The pattern this completes

R299 and this item are the same failure: a reduction removed a literal, and a
separate file that asserted the presence of that literal was not updated with it.
Both were introduced by work on this branch, both break a gate, and neither is
detectable by reading the changed file alone - the assertion lives somewhere else
and names the removed text as a string. Every applied item that removes a **named
literal, symbol, or path** carries this risk. From the Applied table, the removals
of that shape are R157, R192, R231, R232, R234, R241, R245, R248, R266, R320, and
R243; part forty-nine re-verified the *removals* themselves but did not check for
orphaned assertions elsewhere, which is the gap this item exposes.

### Checked and not proposed (part fifty-five)

- `eng/container/dev-init.sh` is now correct and is the model for the fix: it reads
  `contract_path="${SHARPPROOF_CONTAINER_CONTRACT:-}"` and tests that, rather than
  hardcoding the path. Four literal occurrences remain repository-wide and all are
  legitimate: the `ENV` definition in `eng/container/Dockerfile:22`, the default in
  `SharpProof.Host/ContainerContract.cs:19`, a test expectation in
  `SharpProof.Worker.Test/ContainerContractTests.cs:68`, and the broken assertion
  above.
- `eng/container/dev-init.sh` as a whole is clean: it validates the origin URL,
  refuses to clone into a nonempty non-Git workspace, gates on the contract file,
  and ends with `sp contract` and `sp restore`. No redundancy found.
- The other two `-cnotmatch` patterns in the same condition
  (`_RequireSharpProofCanonicalContainer`, `SHARPPROOF_CONTAINER`) are exactly the
  right shape - they assert the *mechanism* rather than a resolved value - and
  should be the template.

### Status (part fifty-five)

R480 is fixed: the contract gate now checks the `SHARPPROOF_CONTAINER_CONTRACT`
property reference rather than the removed hard-coded path. It is also the
reason to run one more targeted pass: for each applied removal of a named
literal, search the tree for surviving assertions that still name it. That search
is cheap and would have caught both this and R299 at the time.


## Second survey, part fifty-four: R479 - the orphaned-assertion sweep

The pass R480 called for: for every applied removal of a **named literal, symbol,
or path**, search the tree for surviving assertions that still name it. Eleven
applied items are of that shape. All 753 candidate files were scanned.

**Result: exactly one orphan, and it is R480 itself.** The other ten are clean,
and three of them were handled in the best possible way - their assertions were
*inverted* to require the removed thing's absence rather than deleted:

- **R232** - `BoundaryEnforcementTests.cs:47` now asserts
  `.And.Not.Contain("PortableAnalyzer")` against the production-classification
  condition, so the removal is pinned in place and cannot silently return.
- **R245** - `ArchitectureTests.cs:1355-1361` now asserts
  `TryGetProperty("containerEnv") Is.False` and
  `TryGetProperty("forwardPorts") Is.False`, pinning both removals from
  `devcontainer.json`.
- **R231** - the surviving `SharpProofSpecificationPacks` references are all
  legitimate `CompilerVisibleProperty` declarations and consumers; no assertion
  required the removed no-op assignment.

R157, R192, R234, R241, R248, R266, and R289 left no surviving reference of any
kind. This closes the concern raised in part fifty-three: the failure mode is real
but occurred once, not systematically.

### The finding this sweep surfaced

| ID | Finding | Evidence |
|---|---|---|
| R479 | `SharpProof.AnalyzerConsumer.props` and `eng/self-application/SharpProof.SelfApplication.props` declare **byte-identical nine-property `CompilerVisibleProperty` lists** - `SharpProofProfile`, `SharpProofFeatures`, `SharpProofVerifyPolicy`, `SharpProofAssumptionPolicy`, `SharpProofSpecificationPacks`, `_SharpProofCompilerManifestPath`, `_SharpProofCompilationTargetFramework`, `_SharpProofProjectDirectory`, `SharpProofVerifyMaximumExpressionDepth`. The packaged consumer declares a three-property subset of the same list in `SharpProof.Package/buildTransitive/SharpProof.props`. These are the properties the analyzer reads through `AnalyzerConfigOptions`, so the list is a real interface contract between the build and the analyzer - and it is now maintained in three places, two of them character-for-character equal. Adding a compiler-visible option means editing two identical lists plus deciding about the third. This is the same source-tree/self-application/package triplication already recorded for the assembly closure in R290 and for the validation targets in R470, now visible in the analyzer's option surface. | `SharpProof.AnalyzerConsumer.props:12`; `eng/self-application/SharpProof.SelfApplication.props:33`; `SharpProof.Package/buildTransitive/SharpProof.props:14` |

### Checked and not proposed (part fifty-four)

- The three lists are not wrongly *divergent* - the package subset is smaller
  because the packaged consumer does not expose verify-policy or manifest-path
  options, which is correct. The finding is the duplication of the two identical
  lists, not a mismatch.
- R231's application went further than its description: it also collapsed the
  previously separate `<CompilerVisibleProperty Include="X" />` elements into
  single semicolon-delimited `Include` attributes. That is a genuine improvement
  and is why the duplication above is now visible as one comparable string per
  file rather than nine scattered elements.

### Status (part fifty-four)

R479 is `pending`. The sweep itself produced no new defects, which is the more
important result: R480 is an isolated miss rather than evidence of a pattern
across the applied work.

## Second survey, part fifty-six: R481 - symbolic call operand admission

| ID | Finding | Evidence |
|---|---|---|
| R481 | **`AcyclicBlockPredicateExecutor` repeats the receiver/argument substitution and definedness-guard loop for spec and summary calls.** `ApplySpec` substitutes the receiver when present, constrains normal execution, then repeats that sequence for every argument while threading the guard; `ApplySummary` repeats the same receiver and argument loop with the same `Substitute` and `ConstrainNormalExecution` checks. The two paths legitimately diverge after operand admission - spec calls build substitutions and instantiate postconditions, while summary calls validate free variables and record a relation - but a shared operand-admission helper can own the repeated guard threading and leave those semantic tails separate. | `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs:347-379,446-472` |

### Status (part fifty-six)

R481 is `pending` and review-only. No implementation or build files were edited.


## Second survey, part fifty-six-b: R482 - the framework metadata-name authority

A new technique: auditing the reflection and string-based type-lookup surface.
The headline result is that this surface is unusually disciplined, with one
consistent leak.

| ID | Finding | Evidence |
|---|---|---|
| R482 | `SharpProof.Specs/FrameworkTypeMetadataNames` declares 26 BCL type-name constants and states its own policy in its doc comment: *"Keep framework type-name declarations in the spec layer so consumers do not grow independent string-based BCL authorities."* **Eight production sites hold bare `System.*` type-name literals that bypass it**, and they split cleanly by reachability. Three are in `SharpProof.Analyzer.Core`, which **already has a `ProjectReference` to `SharpProof.Specs`** and so could use the constants directly: `"System.Delegate"` and `"System.IAsyncDisposable"` have no constant yet, but `"System.IDisposable"` **duplicates a constant that already exists and sits unused** - the literal is on the line immediately after `"System.IAsyncDisposable"` in the same ternary, so the policy's own constant is bypassed one line from where it applies. The other five are in `SharpProof.Meta.Analyzers` (`System.OperationCanceledException`, `System.Threading.CancellationToken`, `System.String`, `System.Runtime.CompilerServices.RuntimeHelpers`, and `` System.Threading.Tasks.Task`1 ``), which declares **no `ProjectReference` at all** and therefore cannot reach the constants without a new reference or the `Compile Include ... Link` mechanism - the same standalone posture documented in R306. Separately, the class has an internal inconsistency: 25 members are `public const string` while `Monitor` alone is `public static readonly string`, which excludes it from `const` contexts and attribute arguments for no evident reason. | `SharpProof.Specs/FrameworkTypeMetadataNames.cs:39`; `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1323-1325`; `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1274`; `SharpProof.Meta.Analyzers/SharpProofSoundnessAnalyzer.cs:23,24,27,41,1037`; `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:32` |

### Checked and not proposed (part fifty-six-b)

- **The reflection surface is genuinely disciplined and should be recorded as
  such.** Across all production C# there are **zero** uses of `Type.GetType`,
  `Activator.CreateInstance`, `Assembly.Load`, or `GetField(string)`, and only two
  `GetMethod(string)` calls. Type resolution runs through
  `Compilation.GetTypeByMetadataName` at 43 sites, and all but the eight above
  pass a named constant from `FrameworkTypeMetadataNames` or
  `ContractApiMetadata`. For an analyzer codebase this is a notably small implicit
  coupling surface, and no reflection-based dead-code or hidden-dependency risk
  was found.
- The two `System.*` literals in `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs`
  are **assembly file names** (`System.Private.CoreLib.dll`, `System.Runtime.dll`)
  rather than type metadata names. They are correctly outside the policy and are
  not counted in the eight.
- `AppContext.GetData` appears at 5 sites, all resolving
  `TRUSTED_PLATFORM_ASSEMBLIES`. That duplication is already tracked as deferred
  R099/R061/R201 and is not re-filed here.

### Status (part fifty-six-b)

R482 is partially applied: the `System.IDisposable` site now uses the existing
spec-layer constant, and `Monitor` was already a `const` on the current tree.
The `System.Delegate`/`System.IAsyncDisposable` names have no existing
constants, while the five Meta.Analyzers sites inherit R306's deliberate
standalone-isolation question; those remain deferred rather than adding a new
reference or vocabulary solely for line reduction.

## Second survey, part fifty-seven: R483-R485 - publication-path identity plumbing

| ID | Finding | Evidence |
|---|---|---|
| R483 | **`LinuxPathIdentity.BindPublicationSet` ensures each pending metadata directory twice.** The first loop calls `EnsurePublicationMetadataDirectory(markerPath)` for every canonical path before checking whether a marker exists. Every path without an existing marker is then placed in `pending`, and the next loop calls the same helper again for each pending marker path. The second pass has no intervening state change that requires a new directory validation; removing it, or moving the first call into the pending branch, preserves the ownership checks while eliminating duplicate filesystem inspection and setup. | `SharpProof.Host/LinuxPathIdentity.cs:527-554` |
| R484 | **`LinuxPathIdentity` duplicates its canonical path-within-directory predicate.** Public `IsSameOrDescendant` canonicalizes both arguments and then checks equality or a directory-separator-aware prefix; private `IsPathWithin` performs the same equality/prefix test for already-canonical mount paths. A shared canonical-string helper can keep the public validation boundary and the mount-info parsing separate while removing the repeated comparison logic. | `SharpProof.Host/LinuxPathIdentity.cs:315-330,799-846` |
| R485 | **`ResetPublicationSet` and `AcquirePublicationSet` repeat publication-path preparation.** Both filter blank paths, materialize an array, canonicalize through `RequireLocalPath`, and run `ValidatePublicationTopology` plus `ValidatePublicationMetadataAliases` before their operation-specific work. A private preparation helper returning the canonical path array can preserve the reset-specific empty-set no-op and acquire-specific nonempty-set error while centralizing the shared validation sequence. | `SharpProof.Host/LinuxPathIdentity.cs:176-187,232-256` |

### Status (part fifty-seven)

R483 remains `pending` because its repeated filesystem validation is part of
publication ownership and lock sequencing. R485 is applied: reset and acquire
share initial filtering, canonicalization, topology, and metadata-alias checks,
while Acquire still revalidates the captured paths after locking.
R484 is applied: public path canonicalization and private mount parsing now
share one already-canonical equality/prefix predicate.

## Second survey, part fifty-eight: R486-R487 - verifier wait-loop constants

| ID | Finding | Evidence |
|---|---|---|
| R486 | **`RunVerifier` duplicates the wait-loop timing scaffold in two helpers.** `WaitForOutputCompletion` and `WaitForSupervisorReadiness` each start a stopwatch, call `RemainingMilliseconds`, cap a wait slice at `OutputDrainPollingMilliseconds`, invoke an optional test wait delegate, and loop until a timeout or completion condition. Their completion predicates intentionally differ, but a small polling helper or shared deadline iterator can own the timing/delegate mechanics and leave the output-specific and supervisor-specific state checks at the call sites. | `SharpProof.BuildTasks/RunVerifier.cs:405-488` |
| R487 | **`RunVerifier.WaitForExitOrCancellation` hard-codes the output polling interval.** The method waits with `Math.Min(remaining, 25)` even though the same class declares `OutputDrainPollingMilliseconds = 25` and uses that named constant in both other polling helpers. Reusing the existing constant removes a second authority for the interval and keeps later tuning from changing only one wait path. | `SharpProof.BuildTasks/RunVerifier.cs:31-32,826-846` |

### Status (part fifty-eight)

R486 remains `pending` because the larger wait-loop abstraction would need to
preserve two distinct completion protocols and test hooks. R487 is applied:
`WaitForExitOrCancellation` now reuses `OutputDrainPollingMilliseconds` for its
bounded wait slice, leaving one timing authority in `RunVerifier`.

## Second survey, part fifty-nine: R488 - supervisor record parsing

| ID | Finding | Evidence |
|---|---|---|
| R488 | **`RunVerifier.ReadBoundedOutputAsync` duplicates authenticated-record recognition.** For each completed protocol line it separately constructs and compares `SupervisorArmedMessage + " " + supervisorNonce` and `SupervisorCleanupMessage + " " + supervisorNonce`, then ORs the matching flag and conditionally completes the corresponding signal. The line trimming, ordinal comparison, accumulation, and signal-setting protocol is identical; a small record descriptor table or helper can preserve the two independent outputs while removing the duplicated block. | `SharpProof.BuildTasks/RunVerifier.cs:554-587` |

### Status (part fifty-nine)

R488 is `pending` and review-only. No implementation or build files were edited.


## Second survey, part fifty-seven-b: R489 - reflective coupling in test code

Part fifty-six audited the reflection surface of *production* code and found it
disciplined. This part does the same for *test* code, where reflection usually
accumulates.

| ID | Finding | Evidence |
|---|---|---|
| R489 | **Sixteen non-public members are reached from tests by string name**, across ten test files and eight assemblies: `State`, `Range`, `Target`, `AnalyzeEnabledCompilation`, `CreatePerformanceProbeProject`, `RunDotnetAsync`, `MatchesTarget`, `TryCreateProgram`, `Encoder`, `UnknownReasons`, `ReadBoundedJson`, `IsKnown`, `GetManifestName`, `Code`, `Main`, and `WithRecursiveAliases`. Renaming any of them compiles cleanly and fails only at test runtime, on the `!` after the reflective lookup - there is no compiler error and no analyzer warning, and `CA1811`'s dead-code detection cannot see these uses either, so a member reached *only* this way looks unused to every static check the repository runs. **All fifteen SharpProof-owned names currently resolve**, so this is a brittleness surface rather than an existing defect. The sixteenth is different in kind and worth separating: `WithRecursiveAliases` is a **non-public Roslyn API** on `MetadataReferenceProperties`, not a SharpProof member. That coupling is guarded at the call site with an explicit `?? throw new InvalidOperationException("Recursive reference aliases are unavailable.")`, so it degrades to a clear failure rather than a crash - but it means a Roslyn upgrade can break a test through an API that Roslyn never promised. This is an **unstated second reason** why the `dependabot.yml` pin matters: that file blocks `Microsoft.CodeAnalysis.*` at `>= 4.15.0` and justifies it only as "Analyzer binaries must remain loadable by the documented Roslyn 4.14 host", with no mention that a test also binds a Roslyn internal by reflection. | `SharpProof.Analyzer.Test/FinalCompilationCollectorTests.cs:905`; `SharpProof.Gates.Test/PerformanceGateTests.cs:463,972,988`; `SharpProof.Worker.Test/ProtocolModelSchemaTests.cs:199,233,275`; `SharpProof.Worker.Test/CompilerArtifactModelSchemaTests.cs:203,262`; `SharpProof.Worker.Test/ContainerContractTests.cs:18`; `SharpProof.Worker.Test/WorkerProgramTests.cs:389`; `SharpProof.Specs.Test/ApiSpecTests.cs:1006`; `SharpProof.Testing.Test/IrCSharpDifferentialOracleTests.cs:72`; `SharpProof.Frontend.Test/FrontendLoweringTests.cs:1267`; `SharpProof.Analyzer.Test/RuntimeFlagshipOracleTests.cs:107`; `SharpProof.Analyzer.Test/RuntimeRequiresOracleTests.cs:67`; `.github/dependabot.yml` |

### Checked and not proposed (part fifty-seven-b)

- **No stale reflective reference exists.** Every one of the fifteen
  SharpProof-owned member names was resolved against production source, and every
  fully-qualified production type name used as a string in tests resolves too.
  The reflective surface is currently accurate.
- **`"SharpProof.Attributes.PureAttribute"` is not a stale reference**, despite
  naming a type that does not exist. `ContractApiTests.cs:93-95` asserts
  `assembly.GetType("SharpProof.Attributes.PureAttribute")` `Is.Null` - a
  deliberate absence guard confirming the type stays removed, the same correct
  pattern part fifty-four found for `PortableAnalyzer` and `forwardPorts`. It was
  checked specifically because the assembly declares `EnforcePureAttribute` and
  the shorter name looked like a rename that had been missed.
- The 694 `GetProperty("...")` calls in test code are overwhelmingly
  `JsonElement` navigation rather than reflection, and were excluded by requiring
  a `BindingFlags` argument. A naive count of reflective calls in this repository
  is wrong by roughly two orders of magnitude, which is worth recording for anyone
  repeating the measurement.
- `Activator.CreateInstance` (10 sites) and `Assembly.Load` (4) appear only in
  test code and only for loading emitted test assemblies, which is the intended
  use. No production code uses either.

### Status (part fifty-seven-b)

R489 is `pending` and is not a reduction - it removes no lines and any fix would
add them. It is filed because the fifteen internal couplings are invisible to
every static check the repository runs, including the `CA1811` dead-member
analyzer that is otherwise a warning-as-error, and because the sixteenth records
a dependency constraint that currently exists only as an unwritten reason behind a
dependabot pin.


## Second survey, part fifty-eight-b: catalog and generated-model cross-references - no findings

A technique with no positive result, recorded so it is not repeated: cross-checking
every string identifier in the generator catalogs against the code, and then
checking the reverse direction for generated types nothing consumes.

### Checked and not proposed (part fifty-eight-b)

- **Every namespace declared in a catalog resolves.** Across
  `SharpProof.DeclarativeModels.catalog.json` (8 namespaces),
  `SharpProof.Projection.catalog.json` (9), and
  `SharpProof.Frontend/ContractApi.catalog.json` (1), all 18 match a
  `namespace X;` declaration in tracked C#. No catalog emits into a namespace that
  does not exist.
- **Every type reference in the catalogs resolves.** Extracting `type`,
  `returnType`, `sourceType`, and `targetType` values from the declarative-model
  and projection catalogs and matching them against declared types produced three
  apparent misses - `EffectProjection`, `IrVarId?`, and
  `SequenceCardinalityValue` - and **all three are false positives**.
  `EffectProjection` and `SequenceCardinalityValue` are declared as
  `readonly record struct`, and a naive `(?:class|struct|record)\s+(\w+)` regex
  captures `struct` from `record struct` rather than the type name; `IrVarId` is a
  global-using alias from `IrIdentifierAliases.cs`, not a declared type. Both traps
  are worth recording alongside the `$()`-versus-`@()` trap from part fifty-two
  and the `JsonElement.GetProperty` trap from part fifty-seven: three of the four
  scans in this survey that produced apparent defects produced only regex
  artifacts, and each needed individual verification before it could be discarded.
- **No dead generated types.** 345 types are declared across the generated files.
  Nine are referenced by no hand-written code - `CSharpBinarySemantics`,
  `CSharpUnarySemantics`, `CSharpIntegerConversionSemantics`,
  `CompilerEffectConstraintRule`, `EffectEvidenceRule`, `StorageTag`,
  `WorkerManifestIdentityField`, `WorkerManifestIdentityOrder`, and
  `WorkerManifestIdentityCollection` - but every one is consumed inside its own
  generated model, and the `WorkerManifestIdentity*` trio is reached in production
  through `WorkerManifestIdentityCatalog`, which `ProtocolManifestPayload.cs:7`
  uses. There is no dead generated code and no catalog entry producing an unused
  type. This matters because generated files are excluded from the complexity
  ratchet and `CA1811` does not cover types, so dead generated code would be
  invisible to every gate the repository runs - it simply is not there.

### Status (part fifty-eight-b)

No new `pending` item. This part exists to close three checks as clean and to
record the regex traps that make them easy to get wrong.


## Second survey, part fifty-nine-b: R490 - duplicated test scenarios across projects

A new technique: comparing every test method name across the repository. 2,341
test methods carry 2,329 distinct names, which is an unusually clean result on its
own. Nine names are reused, and seven of the nine are correct. Two are not.

| ID | Finding | Evidence |
|---|---|---|
| R490 | **`ContractForValidatorGeneratorTests` and `ContractBinderTests` contain two identically named tests that exercise the same contract-binding semantics twice through different hosts.** `OpenGenericConstraintOrderIsSemanticallyMatched` is 73 percent similar between the two files with 20 identical lines, and `NestedGenericOwnerScopesDoNotAliasByOrdinal` is 77 percent similar with 17 identical lines **and a byte-identical embedded fixture source**. The duplication is more than textual: per R258 the `ContractForGenerator` test project drives a `CSharpGeneratorDriver` wrapped around a deliberately empty generator, and every assertion in it actually comes from running `SharpProofAnalyzer` over the final compilation - so both files are testing the same `SharpProof.Contracts` binding logic, one directly and one through the no-op generator scaffolding. Whichever host is kept, the second copy adds a maintenance obligation rather than coverage: a change to open-generic constraint matching must be reflected in two fixtures, and one of the two is already an exact copy. This composes with R258 (the ~2,000 lines of analyzer tests hosted behind a no-op generator) and R309 (the 24 synthetic `SharpProof.Attributes` fixtures) as the third measurement of the same underlying issue - test scaffolding duplicated across the generator and contracts projects. | `SharpProof.ContractForGenerator.Test/ContractForValidatorGeneratorTests.cs:812,1504`; `SharpProof.Contracts.Test/ContractBinderTests.cs:1015,1416`; R258; R309 |

### Checked and not proposed (part fifty-nine-b)

- **Test naming is otherwise clean: 2,341 methods, 2,329 distinct names.** Seven of
  the nine reused names are correct by construction and should not be touched:
  `HavocIsConservative` (3x) and `RefinementTransfersAreMonotone` (2x) are the same
  lattice property asserted for each `SharpProof.Dataflow` domain;
  `RuntimeDescriptorsMatchTheAuthoritativeCatalog` (3x) is the same catalog check
  run by each of the three analyzer assemblies through the shared
  `eng/testing/DiagnosticDescriptorCatalogAssertions.cs`; and
  `WideningTerminatesOnGeneratedAscendingChains` (3x) and
  `GeneratedTransfersAreMonotone` (2x) sit in **different sealed subclasses** of the
  generic `GeneratedDomainPropertyTests<T>` base - one per domain - which is the
  intended shape of a parameterized property suite, not a collision.
- `Run` and `Dispose` were flagged by the scan but are helper methods that happen
  to follow a `[Test]` attribute within the eight-line context window used to
  detect test methods. Neither is a test. A fourth regex artifact, consistent with
  the three already recorded in part fifty-eight.
- No test name is duplicated *within* a single class, which C# would reject
  anyway; the same-file cases above are all cross-class.

### Status (part fifty-nine-b)

R490 is `pending` and should be decided together with R258 rather than on its
own: if the analyzer tests move out of the generator project as R258 suggests,
these two duplicates disappear as a side effect. Filed separately because it is
concrete and independently verifiable, while R258 is a larger migration.


## Second survey, part sixty: R491 - unvalidated SBOM component versions

Cross-referencing the release configuration JSON files against the tree. Most of
this area is clean (below); one value is not.

| ID | Finding | Evidence |
|---|---|---|
| R491 | **The third-party component *versions* that flow into the attested SPDX SBOM are hand-maintained and validated against nothing.** `eng/release/third-party-components.json` declares 13 components across the three packages, each with an `id`, `version`, `license`, and `entries` list. The `entries` are genuinely enforced: `Test-PackageThirdPartyInventory` opens the built `.nupkg`, enumerates every non-`SharpProof.*` `.dll` and `.so`, and requires the set to equal the declared entries exactly. The `version` field is **never read by that function, or by any other check**. `Test-SharpProofThirdPartyComponentProjection` looks like it validates the components, but both of its arguments are derived from the same manifest file - `$thirdPartyComponents` is built from `$thirdPartyManifest.packages` and `$catalogComponents` from `Get-SharpProofThirdPartyComponentGraph` reading the same JSON - so it compares the manifest to itself and checks projection shape, not correctness. The declared versions then flow into `SharpProof.spdx.json`, which `package-consumers.yml` attests with `actions/attest` and `sbom-path`. A manual reconciliation performed for this survey confirms **all 13 currently match a version resolved in the 47 `packages.lock.json` files**, so this is not an observed defect - but ten of the thirteen are transitive dependencies with no `PackageVersion` entry in `Directory.Packages.props`, meaning their resolved versions move when a direct dependency updates, and six already resolve to multiple versions across the repository's lockfiles (`System.Buffers`, `System.Memory`, `System.Numerics.Vectors`, `System.Reflection.Metadata`, `System.Runtime.CompilerServices.Unsafe`, `System.Threading.Tasks.Extensions`). The reconciliation this survey did by hand is exactly what a gate should do, and it is cheap: the lockfiles already record the ground truth. | `eng/release/third-party-components.json`; `scripts/New-SharpProofReleaseEvidence.ps1:178-220,428-457`; `scripts/Test-SharpProofPackageDependencies.ps1:248-310`; `.github/workflows/package-consumers.yml` attest step; 47 `packages.lock.json` files |

### Checked and not proposed (part sixty)

- **`eng/release/first-party-assemblies.json` is exactly right and is enforced.**
  Its 20 assembly names are precisely the set of `SharpProof.*` assemblies shipped
  across both nuspecs - verified by set comparison, with no entry on either side
  unmatched - and `Test-SharpProofPackagePayloads.ps1` validates it against the
  built package archive rather than against the nuspec source.
- **`scripts/package-projects.json`** lists three packable projects, all tracked
  and all real.
- **Third-party versions are internally consistent.** No component id is declared
  at two different versions across the three packages, and the three that *are*
  centrally pinned - `Microsoft.Z3` 4.12.2, `System.Collections.Immutable` 9.0.18,
  `System.Text.Json` 10.0.10 - all agree with `Directory.Packages.props`. All 18
  declared payload entries appear in the nuspecs.
- The only existing lockfile assertion, in `ArchitectureTests.cs:117-122`, checks
  that every project *has* a `packages.lock.json`. It does not read any version
  from them, so the lockfiles are currently untapped as a validation source
  despite being the repository's most precise record of resolved dependencies.

### Status (part sixty)

R491 is `pending`. It is the same species as R318 and R319 - a hand-maintained
value restating a derived one with nothing comparing them - but with materially
higher stakes, because these values are published as attested provenance rather
than used as a local default. Unlike those two, the ground truth here is already
committed to the repository in the lockfiles, so the check needs no new data
source.


## Second survey, part sixty-one: R492 - toolchain versions inside the Dockerfile

Auditing the container toolchain declaration. `eng/container/toolchain.json` is the
declared authority: `New-ContainerContract.ps1` projects it into
`/etc/sharpproof/container-contract.json`, and `SharpProof.Host/ContainerContract.cs`
validates that contract at runtime against the same catalog. The image-level half
of that chain is well guarded; the version-level half has a gap.

| ID | Finding | Evidence |
|---|---|---|
| R492 | **The Dockerfile restates three toolchain versions fifteen times as bare literals, and nothing reconciles them with `toolchain.json`.** `Assert-DockerfileAuthority` in `Test-SharpProofContainerContract.ps1` is thorough about *images*: it requires each of the five `ARG *_IMAGE` declarations to equal exactly `image@digest` from the catalog, requires them before the first `FROM`, requires exactly the five canonical `FROM` stages in order, and rejects an unpinned syntax frontend. It does **not** look at the version numbers embedded in the `COPY` paths and `RUN` verification commands: `8.0.16` (`minimumSdkFrameworkVersion`) appears 8 times, `9.0.300` (`minimumSdkVersion`) 4 times, and `8.0.29` (`testRuntimeVersion`) 3 times, all as bare literals. The image does self-verify those three at build time - `dotnet --list-sdks | grep -F '9.0.300 ...'`, `test -d .../8.0.16`, `dotnet --list-runtimes | grep -F 'Microsoft.NETCore.App 8.0.29'` - but it verifies them against **literals in the same file**, not against the catalog. So a Dockerfile edited consistently (COPY plus grep) but not mirrored into `toolchain.json` builds and self-checks cleanly, while `New-ContainerContract.ps1` projects the *catalog's* values into the runtime container contract that `ContainerContract.cs:96-98` then treats as authoritative. The result would be a container advertising `dotnetTestRuntimeVersion` it does not contain, with every gate green. Extending `Assert-DockerfileAuthority` to check the three version literals against the catalog closes it, and is the same shape as the image checks already there. | `eng/container/Dockerfile:27-31,34-37`; `eng/container/toolchain.json`; `scripts/Test-SharpProofContainerContract.ps1:89-175,349`; `eng/container/New-ContainerContract.ps1:14-31`; `SharpProof.Host/ContainerContract.cs:65-98` |

### Checked and not proposed (part sixty-one)

- **The image and digest half of the toolchain chain is properly enforced**, and
  is worth naming as another in-repository example of doing this right: five
  images pinned by digest in `toolchain.json`, restated once each as an `ARG`, and
  asserted character-for-character by `Assert-DockerfileAuthority`, with stage
  names and ordering pinned as well.
- **Applied R244 updated its assertion in step**, unlike R480. Folding the
  single-consumer `dev` stage into `toolchain` required `compose.yaml` to change
  its build target, and `Test-SharpProofContainerContract.ps1:288` now requires
  `target: toolchain`. Both sides moved together. This was checked specifically
  because the earlier reading of `compose.yaml` in this survey recorded
  `target: dev`, which would have been a second orphaned assertion had the test
  not been updated.
- `New-ContainerContract.ps1` is a clean 35-line projection of the catalog with no
  duplicated logic, and `ContainerContract.cs` validates the produced contract
  field-by-field against the same catalog rather than against constants. The
  round-trip is sound; only the Dockerfile's version literals sit outside it.
- `toolchain.json`'s `support` block (`nativeHostInstallSupported`,
  `arm64Supported`, `sharedNetworkPublicationSupported`, all `false`) is
  documentation of deliberate non-support rather than dead configuration, and is
  consumed by the package-consumer and payload scripts.

### Status (part sixty-one)

R492 is `pending`. It is the same species as R491 and R318/R319 - a value
declared in one place and restated in another with no reconciliation - but the
failure mode here is the most concrete of the three: the runtime container
contract would advertise a toolchain version the image does not contain, and both
the build and every existing gate would pass.


## Second survey, part sixty-two: R493-R496 - host guards, path reuse, and process metadata

The next pass follows the Linux-host and build-task paths where correctness is
important but several local implementations repeat the same protocol.

| ID | Finding | Evidence |
|---|---|---|
| R493 | **The Linux amd64 admission policy is spelled independently across host and verifier boundaries.** `ContainerContract.ValidateRequired`, `LinuxWorkerProcess.EnsureLinux`, `LinuxPathIdentity.EnsureLinux`, and `RunVerifier.OpenPidFdRequired` each test `!OperatingSystem.IsLinux()` together with `RuntimeInformation.ProcessArchitecture != Architecture.X64` and then throw a platform exception. `VerifierProcessSupervisor.Run` contains the same predicate as part of its native-operation guard. The three host helpers even differ only in component-specific error text, while `LinuxPathIdentity.SyncDirectory` checks the policy and then calls `Canonicalize`, which checks it again. A shared predicate or guard at an appropriate dependency layer can own the deployment policy, with call-site messages and the supervisor's additional `prctl` result check preserved. | `SharpProof.Host/ContainerContract.cs:23-30`; `SharpProof.Host/LinuxWorkerProcess.cs:323-330`; `SharpProof.Host/LinuxPathIdentity.cs:148-152,857-864`; `SharpProof.BuildTasks/RunVerifier.cs:1053-1059`; `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:24-30` |
| R494 | **`InvalidatePublishedResult.ExecuteCore` resolves the same logical paths repeatedly within one execution.** `ResultPath` and `SarifPath` flow through both `outputPaths` and `publicationPaths`; `RequestPath` and `ManifestPath` flow through publication and input arrays; launcher and worker paths are resolved again after their raw companion paths are assembled; and launcher runtime companions are passed through another `ResolvePath` projection. Every call reaches `LinuxPathIdentity.RequireLocalPath`, so the repeated projections repeat full-path normalization and Linux filesystem/mount validation before alias checks. A per-execution map keyed by the lexical input, or resolving named properties once and deriving companions from the canonical primary path, can remove this work while preserving duplicate array entries, alias detection, and the no-cache-across-executions boundary. This is distinct from R344's duplicate resolver implementation across tasks. | `SharpProof.BuildTasks/InvalidatePublishedResult.cs:48-121`; `SharpProof.Host/LinuxPathIdentity.cs:113-118` |
| R495 | **`ContainerContract` duplicates both typed JSON parsing and expected-value comparison wrappers.** `RequireInteger` and `RequireInteger64` repeat property lookup, number-kind validation, typed extraction, and the same invalid-property exception, differing only in the numeric width. The expected overloads for integer, integer64, and string each repeat the same `RequireX(...)`-then-compare pattern and the same toolchain-mismatch exception, differing only in the accessor and comparison operation. A shared property/typed-value helper plus one expected-value comparison helper can reduce the protocol plumbing while retaining the `int` versus `long` distinction, string whitespace rule, and current diagnostics. | `SharpProof.Host/ContainerContract.cs:83-116,228-300` |
| R496 | **Two components independently parse Linux `/proc/<pid>/stat` with the same fragile field-offset protocol.** `LinuxWorkerProcess.TryReadProcessStat` and `VerifierProcessSupervisor.ReadProcessParents` both read `stat`, locate the last `)`, split the fields after the process state, and take `fields[1]` as the parent PID; both tolerate disappearing processes and access failures. The worker parser additionally reads `fields[19]` as the start time for PID-reuse protection, while the supervisor only needs ancestry. A common parser/reader in a suitable shared layer can return the parent and optional start time without merging the distinct snapshot and live-process algorithms or their failure policies. Centralizing the `/proc` field offsets would remove a security-sensitive source of drift. | `SharpProof.Host/LinuxWorkerProcess.cs:222-234,272-300`; `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:411-451` |

### Status (part sixty-two)

R493-R496 are `pending`. This pass made no implementation changes; it records
review candidates only, and the proposed consolidations must preserve Linux
platform boundaries, path-alias checks, and process-identity semantics.


## Second survey, part sixty-two-b: R497 - the toolchain image is rebuilt per job with no cache

Analysing the CI job graph rather than the workflow text. This one is about cost
and repetition rather than lines of code.

| ID | Finding | Evidence |
|---|---|---|
| R497 | **The digest-pinned toolchain image is built from scratch once per CI job, with no layer cache and without the prebuilt-image mechanism the repository already provides.** The `prepare-qualified-packages` composite action - whose only unconditional step is `docker compose build tooling` - is invoked **nine times** across the workflows: once each in `ci.yml`, `coverage.yml`, `nightly.yml`, and `security-reusable.yml`, and five times within `package-consumers.yml`. Because each GitHub-hosted job runs on a fresh VM, the local Docker layer cache is empty every time, and **no cache is configured anywhere**: there is no `buildx` setup, no `cache-from`/`cache-to`, no `type=gha`, and no registry cache in any workflow or in `compose.yaml`. A tag push therefore triggers five full builds of the same image within `package-consumers.yml` alone - the `security` job (via `security-reusable.yml`), `package`, `container-verifier`, `release-qualification`, and whichever publish job matches - and a `master` push triggers five more across four workflows (`ci.yml`, `coverage.yml`, `security.yml`, and `package-consumers.yml`'s `package` and `container-verifier`). Note that `ci.yml`, `coverage.yml`, and `security.yml` are filtered to `push: branches: [master]` and so do **not** run on a tag push; an earlier draft of this entry incorrectly added them to the tag-push count. That image is not cheap: it pulls five digest-pinned base images, copies SDK, targeting-pack, and runtime directories between stages, downloads and unpacks the 31.5 MB Z3 archive, and runs verification commands. The sharpest part is that the escape hatch already exists and is unused - `compose.yaml:8` resolves `image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}`, `docs/container-development.md:236` documents it, and two tests plus `Test-SharpProofContainerContract.ps1:263` pin that exact line - yet **no workflow ever sets `SHARPPROOF_TOOLING_IMAGE`**. Building once and publishing to a registry (or enabling a buildx cache) would let the remaining jobs pull instead of rebuild, and the image's full digest pinning is precisely the property that makes reuse safe. | `.github/actions/prepare-qualified-packages/action.yml:19-21`; `.github/workflows/{ci,coverage,nightly,security-reusable}.yml`; `.github/workflows/package-consumers.yml:40,72,131,236,275`; `compose.yaml:8`; `docs/container-development.md:236`; `eng/container/Dockerfile` |

### Checked and not proposed (part sixty-two-b)

- **Applied R246 is confirmed in place.** That item observed that `ci.yml`,
  `nightly.yml`, `coverage.yml`, and `security-reusable.yml` each open-coded
  `docker compose build tooling` rather than reusing the composite action. All
  four now use `- uses: ./.github/actions/prepare-qualified-packages`. The
  consolidation is what made the nine-invocation count above easy to measure.
- **The repeated `tooling` commands across jobs are mostly deliberate.**
  `acceptance` runs twice by design (Release and Debug configurations);
  `release-publish` twice because the private-preview and public publish jobs are
  mutually exclusive; and `package-consumers` twice because
  `release-qualification` explicitly re-runs it against the *downloaded* artifacts
  under the step name "Revalidate downloaded packages with a real proof". Only
  `release-tag` is an unexplained repeat - the `package` job validates the tag and
  `release-qualification` validates it again with the same command and
  environment - and it is cheap enough not to be worth filing on its own.
- No job graph cycle or missing `needs` edge was found: `release-qualification`
  correctly depends on `package`, `container-verifier`, `security`, and
  `portable-consumers`, and both publish jobs depend on it.

### Status (part sixty-two-b)

R497 is `pending`. It removes no source lines and is the only finding in this
survey whose payoff is CI wall-clock and cost rather than maintainability - but it
is filed on the same evidence standard as the rest, and the mechanism it asks for
is already built, documented, and test-pinned.


## Second survey, part sixty-three: R498-R502 - resolver predicates and repeated analyzer work

This pass continues through the frontend, analyzer, effects, and corpus gate
implementations, keeping behavior-specific branches separate from shared plumbing.

| ID | Finding | Evidence |
|---|---|---|
| R498 | **`StringConcatenationEffectResolver.Resolve(IBinaryOperation, ...)` reimplements a predicate already provided by `IsBuiltInStringConcatenation(IBinaryOperation)`.** The helper checks the operator, string result type, absence of a constant value, and absence of an operator method. The resolver repeats the first three as an early-return condition and then repeats the operator-method check in a second condition before doing the same allocation and formatted-operand work. Calling the existing helper would keep the binary and compound-assignment admission rules visibly aligned without changing the intentional constant-folding distinction. | `SharpProof.Effects/StringConcatenationEffectResolver.cs:30-37,47-88,90-118` |
| R499 | **`RoslynOperationLowerer.DefaultVisit` and `VisitFieldReference` duplicate the unsupported-value fallback classification.** Both test whether a constant has a non-built-in value type and select `FrontendAbstention.UnsupportedType` versus `UnsupportedOperationKind`; `VisitFieldReference` reaches this exact fallback after its catalog-integer special case. A shared fallback helper can preserve `DefaultVisit`'s additional `TypeKind.Error` classification and the field-specific constant admission while removing the repeated value-type branch. | `SharpProof.Frontend/RoslynOperationLowerer.cs:518-532,546-563` |
| R500 | **`CorpusGate.RunAsync` computes supported-Unknown cases twice.** It builds `casesById` and counts observations whose case is supported and whose verdict is `Unknown` or `SilentUnknown` at lines 192-205, then `ValidateSupportedOutcomes` rebuilds the same dictionary and evaluates the same predicate at lines 254-272. The first count is needed for the result metrics and the second only to format a failure; a shared private counter or an overload accepting the precomputed count can keep the public validation helper while removing duplicate dictionary construction and filtering. | `SharpProof.Gates/Corpus/CorpusGate.cs:192-205,254-272` |
| R501 | **`SharpProofAnalyzerEngine` resolves the same three closed-contract attribute symbols inside every source-reference iteration.** `GetClosedContractAttributes(compilation)` depends only on the outer compilation, but `MayContainExternalClosedPreconditions` calls it inside the `CompilationReference` loop after the source compilation check and immediately uses the result for assembly/module namespace scans. Moving the lookup once before the loop preserves the fail-closed branches and recursive checks while avoiding three metadata-name queries per source reference. | `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs:336-398,457-470` |
| R502 | **`EffectContractDiagnostics.ValidateArguments` repeats attribute selection and argument decoding that `Evaluate` performs immediately afterward in the analyzer pipeline.** Both retrieve `ContractSelectionInventory.GetCallableAttributes(method)`, select `AllowedCapabilities` and `AllowedExceptions`, and call `DecodeCapabilities` plus `DecodeAllowedExceptions`; `AnalyzerFeaturePipeline` invokes `ValidateArguments` before `Analyze`, whose `Evaluate` path repeats the work. A per-method validation snapshot or a shared decoded-arguments result can let diagnostics and evaluation consume one selection/decode pass, while keeping `ValidateArguments`' effect-contract-only invalid-attribute reporting and `Evaluate`' semantic summary logic distinct. | `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs:5-33,35-95`; `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs:97-99,159-165,365-369,427-434` |

### Status (part sixty-three)

R498, R499, and R501 are applied: binary string-concatenation resolution now
uses the existing admission predicate, unsupported value-type classification is
shared between default and field visits, and closed-contract attribute symbols
are resolved once per analyzer compilation instead of once per source reference.
R500 is applied: corpus outcome validation accepts the count already computed
for result metrics, avoiding a second dictionary build and predicate pass while
its standalone test helper still computes the count when called directly.
R502 remains a pending review-only candidate; any sharing must retain
fail-closed behavior and the analyzer's distinct diagnostic and
semantic-evaluation responsibilities.


## Second survey, part sixty-three-b: R503 - .gitignore negation rules

Rejected R155 declined to trim generic `.gitignore` boilerplate, and that
reasoning stands for the file's 203 ordinary patterns. Negations are a different
case: they exist only to counteract an earlier rule, they are order-dependent, and
a wrong one silently hides a file. There are five, and they were tested
individually with `git check-ignore`.

| ID | Finding | Evidence |
|---|---|---|
| R503 | Of the five negation rules in `.gitignore`, **one is redundant and three are inert**. `!eng/release/` (line 20) is genuinely load-bearing: `[Rr]elease/` on line 18 would otherwise ignore all six tracked files under `eng/release/`. But `!eng/release/third-party-components.json` on line 21 adds nothing - `git check-ignore` confirms a hypothetical new file in that directory is already un-ignored by line 20 alone, and the other five files there are tracked without any negation of their own. The remaining three - `!.axoCover/settings.json` (line 123), `!**/[Pp]ackages/build/` (line 171), and `!?*.[Cc]ache/` (line 195) - match **zero tracked files** and are stock Visual Studio template negations for tooling this repository does not use. Unlike the ordinary patterns R155 declined to touch, these four are not merely unused: a negation that references a path no longer in the repository is actively misleading, because a reader reasonably infers that something under it is meant to be tracked. | `.gitignore:18-21,123,171,195`; `git check-ignore -v eng/release/newfile.json` |

### Checked and not proposed (part sixty-three-b)

- **No tracked file is matched by an ignore pattern.** Running every one of the
  956 tracked paths through `git check-ignore --no-index` returns zero hits, so
  there is no file that is tracked-but-ignored - the state that produces the
  "why won't git see my change" class of confusion. The ignore file and the index
  agree completely.
- `!eng/release/` is correct and must stay. It is the only negation doing work,
  and removing it would silently drop six release-authority files -
  `environment-contract.json`, `first-party-assemblies.json`,
  `package-dependency-contract.json`, `third-party-components.json`, and two
  markdown documents - from version control.
- The 203 non-negation patterns remain the R155 case: generic, unused in large
  part, but harmless and not worth churning. This item is deliberately scoped to
  the five negations only.

### Status (part sixty-three-b)

R503 is applied: the one load-bearing `!eng/release/` exception remains, while
the redundant file-specific exception and three unused template negations are
gone. The tracked-file and representative-probe checks still agree with the
intended ignore policy.


## Second survey, part sixty-four: R504 - Dockerfile layer boundaries

Layer-level analysis of the container build, distinct from the toolchain-version
audit in part sixty-one. Two boundaries are drawn in places that cost more than
they need to.

| ID | Finding | Evidence |
|---|---|---|
| R504 | **The 31.5 MB Z3 download shares a cache layer with a trivial JSON projection, and is invalidated by an unrelated script.** Lines 40-42 copy `toolchain.json`, `Prepare-NativePayload.ps1`, and `New-ContainerContract.ps1`; line 43 then runs *both* payload preparation - which calls `Invoke-WebRequest` on the Z3 archive (`Prepare-NativePayload.ps1:37`) - and container-contract generation, a 35-line pure JSON transform, in a single `RUN`. That layer's cache key covers all three copied files, so **editing `New-ContainerContract.ps1` forces a full re-download of the Z3 archive** even though the two operations share nothing but a catalog input. Splitting the contract generation into its own `COPY` plus `RUN` after the payload step makes edits to it free, and costs one extra layer. Separately, lines 50-58 copy four shell scripts and then `RUN chmod 0755` over the same four paths; BuildKit's `COPY --chmod=0755` expresses this inline, removing six lines and one layer. This compounds with R497: because CI builds this image five times per event with no cache, a needlessly wide cache key is paid repeatedly rather than once. | `eng/container/Dockerfile:40-48,50-58`; `eng/container/Prepare-NativePayload.ps1:37` |

### Checked and not proposed (part sixty-four)

- **The multi-stage structure is efficient and correct.** Four extraction stages
  (`powershell`, `test-runtime`, `minimum-sdk`, `minimum-framework`) exist purely
  to `COPY --from`, so none of their layers ship in the final image - only the
  copied directories do. That is the right shape for assembling a toolchain from
  several base images, and applied R244 removed the one stage that was not pulling
  its weight.
- **Applied R243 reached the Dockerfile too.** Line 48 now writes the contract to
  `"${SHARPPROOF_CONTAINER_CONTRACT}"` rather than the hardcoded
  `/etc/sharpproof/container-contract.json`, consistent with the env-var
  resolution that item introduced. Only the assertion in
  `Test-SharpProofContainerContract.ps1` was left behind, which is R480.
- The platform guard on line 25 (`RUN test "${TARGETOS}" = "linux" && test
  "${TARGETARCH}" = "amd64"`) is its own layer, but it is near-free and placed
  first so it fails fast before any expensive step. Correct as written.
- Ordering is otherwise sound: the volatile repository-owned scripts are copied
  *after* the expensive base-image assembly and payload download, so editing them
  does not invalidate the costly layers. The only inversion is the one in the
  finding above.

### Status (part sixty-four)

R504 is partially applied: the four script copies now set their executable mode
inline with `COPY --chmod=0755`, removing the separate chmod layer and six lines;
the independent payload/contract cache split remains deferred because it adds a
layer and does not simplify the build graph. The authority tests and a real
tooling-image build pass.


## Second survey, part sixty-five: R505 - two source-materialization paths that disagree

`eng/container/entrypoint.sh` and `eng/container/loop-command.sh` both materialize
the host working tree into a private container workspace before running a command.
A line-level diff finds **zero shared runs of three or more lines**, so this is not
textual duplication - but they implement the same guarantee with different code,
and they do not materialize the same source.

| ID | Finding | Evidence |
|---|---|---|
| R505 | **The loop path copies a strictly smaller source set than the task path, while accepting the same command set.** `entrypoint.sh` performs three `git ls-files` passes: a clean-source check, an untracked-file copy (`--others --exclude-standard`), and - critically - a third pass copying **ignored** package inputs (`--others --ignored --exclude-standard -- nupkgs/`), with a comment explaining that "Package jobs download nupkg/snupkg inputs under nupkgs/. Those file extensions are intentionally ignored by Git, so the general untracked copy above cannot see them." `loop-command.sh` performs only the untracked pass and has **no `nupkgs` handling of any kind**, yet it ends with an unrestricted `sp "$@"`, so it accepts all 37 commands in the container dispatcher's `ValidateSet` - including `package-consumers`, `pilots`, `release-plan`, and `release-qualification`, every one of which takes `-PackageSource nupkgs`. Running any of those through the loop therefore executes against a workspace where the package inputs were never copied. The asymmetry runs both ways: the loop has a target-manifest reconciliation pass that removes stale untracked files from its **reused** workspace, which `entrypoint.sh` does not need because it materializes into a fresh `mktemp -d` each time. Each path has a correctness property the other lacks, and neither documents the difference. | `eng/container/entrypoint.sh:92,132,143`; `eng/container/loop-command.sh:125,166-179,205`; `scripts/Invoke-SharpProofContainer.ps1:3` |

### Checked and not proposed (part sixty-five)

- **This is not a de-duplication candidate**, and the measurement is the reason
  to say so explicitly. The two scripts share the same git verb vocabulary -
  `clone`, `checkout`, `config`, `diff`, `ls-files`, `remote`, `rev-parse`,
  `apply` - but a normalized line-level comparison of their 145 and 189
  significant lines finds **no shared run of three lines**. Extracting a common
  implementation would be a rewrite, not a merge, and the differences are
  deliberate: `core.filemode false` for the Docker Desktop bind mount versus
  `true` for the private Linux volume (commented in place), a process lock the
  task path does not need, and optional snapshot input. This is the same shape
  part seventeen found for the three process-deadline implementations, and the
  R032-R034 deferral reasoning applies unchanged.
- `loop-command.sh`'s path validation is careful and worth leaving alone: it
  rejects a target equal to the source or to `/`, requires the target under
  `/workspace`, validates every inventory path against `""`, `/*`, `../*`, and
  `*/../*`, and constrains an optional snapshot root to a specific artifacts
  subdirectory before use.
- The lock is correct: it uses `mkdir` for atomicity, records the owner PID,
  reclaims the lock only after confirming the owner is dead with `kill -0`, and
  releases through a trap on `EXIT HUP INT TERM`.

### Status (part sixty-five)

R505 is applied as a parity fix: host loop snapshots and the persistent
container workspace now include ignored `nupkgs/` package inputs, and stale
package files are reconciled with the same manifest. The deliberate source
materialization differences remain otherwise unchanged.


## Second survey, part sixty-six: R506 - analyzer release tracking is disabled six ways

Validating every diagnostic ID that appears in configuration. No ID is
mistyped and no suppression is undocumented - but the analyzer-release-tracking
family shows a pattern that extends R273.

| ID | Finding | Evidence |
|---|---|---|
| R506 | **All six analyzer-shipping projects disable analyzer release tracking, in two different combinations, and only one of them has release-tracking files at all.** `SharpProof.Analyzer`, `SharpProof.Analyzer.Core`, and `SharpProof.CompilerCollector` each `NoWarn` **RS2002 and RS2003** - the rules that forbid re-adding a removed diagnostic ID to the unshipped release and forbid a shipped ID appearing as unshipped. `SharpProof.ContractForGenerator`, `SharpProof.Meta.Analyzers`, and `SharpProof.CompilerProbe.TestAsset` each `NoWarn` **RS2008** - the rule requiring an analyzer to *have* release tracking. Meanwhile `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` exist in exactly one project, `SharpProof.Analyzer`. So the mechanism is on for one assembly, off for three, and partially relaxed for three - and the one assembly that does track releases has the two consistency rules for that tracking switched off. Combined with R273's measurement that 21 of the descriptor catalog's 34 IDs (every `SPCF*` and `SPMETA*`) are untracked, the effective state is that release tracking constrains only the 13 `SP*` rules in one file, and even there cannot object to a removed-then-readded or shipped-then-unshipped ID. None of the six suppressions carries an explanatory comment, unlike the neighbouring `SP0024` suppression in `SharpProof.SelfApplication.props`, which does. | `SharpProof.Analyzer/SharpProof.Analyzer.csproj:7`; `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:7`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:8`; `SharpProof.ContractForGenerator/SharpProof.ContractForGenerator.csproj:9`; `SharpProof.Meta.Analyzers`, `SharpProof.CompilerProbe.TestAsset` csproj; `SharpProof.Analyzer/AnalyzerReleases.*.md`; R273 |

### Checked and not proposed (part sixty-six)

- **Every diagnostic ID in configuration is real.** The 17 IDs in `.editorconfig`
  and `.globalconfig` and the 29 in `WarningsAsErrors`/`NoWarn` across all build
  files were checked: all resolve to genuine CA, CS, IDE, NU, RS, or AD rules,
  plus `SP0024`, which is SharpProof's own and present in the descriptor catalog.
  A mistyped suppression silently does nothing, so this was worth confirming;
  there are none.
- **`SharpProof.Smoke.Net472` escalates rather than suppresses, and does it
  well.** Its `WarningsAsErrors` adds `AD0001`, `CS8032`, `CS8034`, and `CS8785` -
  analyzer crash, analyzer load failure, generator load failure, and generator
  execution failure. That turns the net472 smoke project into an assertion that
  the shipped analyzer actually loads and runs on the legacy framework, which is
  exactly what a smoke test of that kind should do. Worth recording as a
  deliberate, well-chosen escalation rather than leaving it to look like noise.
- `NU5128` in the two packaging projects is standard for tools-only packages with
  no `lib/` assemblies, and `SP0024` in the self-application props carries an
  inline comment explaining it covers a deliberate negative fixture. Both correct.

### Status (part sixty-six)

R506 is `pending` and is a policy question rather than a reduction - it removes
no lines. It is filed because six suppressions of one rule family, in two
combinations, with no comments and only one participating project, is more likely
to be accumulated drift than a considered position, and R273 already found the
tracking gap from the other direction.

## Second survey, part sixty-seven: R507-R511 - metadata, source locations, and lowering scaffolds

| R507 | **Two metadata readers independently decode a custom attribute's declaring type.** `ApiSpecResolution.IsAttribute` switches over `MemberReference`/`MethodDefinition`, obtains the parent or declaring type, then switches over `TypeReference`/`TypeDefinition` to compare namespace and name. `SharpProofAnalyzerEngine.IsClosedContractAttribute` repeats the same four-handle cases and string extraction; only the final name predicate differs. A small shared metadata helper that resolves the attribute type identity can preserve each caller's fail-closed behavior and matching policy while removing a fragile copy of the ECMA metadata walk. | `SharpProof.Effects/ApiSpecResolution.cs:304-329`; `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs:523-563` |
| R508 | **Compiler and diagnostic paths each rebuild `WorkerSourceLocation` from a Roslyn `Location`.** `ClaimManifestBuilder.ToSourceLocation` and `CompilerManifestArtifactProducer.CreateDiagnostic` independently obtain the mapped line span, select a path, and copy source start/length plus one-based line/column into the same protocol model. Their edge policies differ - the manifest builder uses a compiler-generated fallback and remembers a syntax-tree ordinal, while diagnostics reject a source location with no path and bind it afterward - so a shared conversion core with explicit caller policy would reduce field-mapping drift without erasing those distinctions. | `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs:679-707`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs:203-229` |
| R509 | **`CallableClaimResultAssembler` repeats the assumption-evidence projection in three result paths.** `FromOutcome`, `Create`, and `MarkAssumptionsUsed` each enumerate `target.Entry.Assumptions` and construct a fresh `WorkerAssumptionEvidence` with the same `Id` and `Kind`; only the `Used` expression changes (user-assumption membership, existing evidence state, or OR with a supplied set). A shared projector accepting the used-state selector can centralize the protocol-object construction while retaining the three deliberately different usage policies. | `SharpProof.Worker/CallableClaimResultAssembler.cs:66-71,114-135` |
| R510 | **Assignment effect scanning repeats the same completion-and-commit scaffold across three forms.** `ScanSimpleAssignment`, `ScanReadModifyWrite`, and `ScanCoalesceAssignment` each accumulate an `EffectStep`, return its summary when an intermediate step cannot complete normally, and finally emit `ScanWriteTarget`; the middle operation differs (value, read-modify-write operation, or nullable-flow proof), but the surrounding early-return and write-commit protocol is copied. A narrow helper for sequencing a completing step and committing a write could remove this accidental control-flow complexity while keeping ref-assignment, operator, and coalesce-specific semantics outside the helper. | `SharpProof.Effects/OperationEffectScanner.Assignments.cs:49-69,198-255` |
| R511 | **Four Roslyn reference visitors repeat the supported-domain/opaque-fallback branch.** `VisitParameterReference`, `VisitFlowCaptureReference`, and `VisitInstanceReference` each test `IsSupportedValueDomain(operation.Type)` and otherwise create an `Opaque` expression with `UnsupportedType`; `VisitLocalReference` performs the same branch after its separate `RefKind` mutation guard. A shared `LowerSupportedReference` helper taking the exact-value factory can own this repeated gate while preserving the distinct variable, capture, and instance lookups and the local mutation rejection. | `SharpProof.Frontend/RoslynOperationLowerer.cs:565-620` |

### Status (part sixty-seven)

R509 and R511 are applied: callable result, creation, and assumption-marking
paths share one evidence projector with caller-specific used-state predicates;
local, parameter, flow-capture, and instance references share one
supported-domain/opaque fallback helper while local mutation rejection and
distinct lookups remain explicit. R507, R508, and R510 remain pending because
their metadata, source-location, and effect semantics differ at more
boundaries.


## Second survey, part sixty-seven (continued): coverage configuration - no findings

Cross-checking the coverage baseline and runsettings against the project set.
No new finding; three applied items confirmed, and one earlier conclusion
strengthened.

### Checked and not proposed (part sixty-seven, continued)

- **The coverage baseline exactly matches the production classification.** All 23
  entries in `eng/coverage/baseline.json` resolve to tracked projects, **no**
  production-classified project is missing from it, and **no** production-excluded
  project appears in it. Those are two independently maintained lists - the
  `SharpProofProductionProject` regex in `Directory.Build.props` and the baseline's
  `projects` map - and they agree perfectly. `declarationOnlyTcbFiles` names one
  file, `SharpProof.Analyzer.Core/EffectEvaluationTypes.cs`, which exists.
- **Applied R232 is reflected here.** The production-exclusion regex now names
  five projects (`Testing`, `Package`, `Verifier`, `Smoke.Net472`,
  `CompilerProbe.TestAsset`) rather than six; `PortableAnalyzer` is gone, and the
  coverage baseline's contents are consistent with the five-project form.
- **Applied R237 was carried out completely, including its consumers.** The three
  runsettings files are now one, `SharpProof.Managed.runsettings`, with the
  isolated selectors generated from it. Crucially the *consumer* was removed too:
  `Get-CoverageExtraProjectNames` in `Get-SharpProofProductionInventory.ps1`,
  which read `eng/coverage/SharpProof.Gates.runsettings` directly, no longer
  exists. Had it been left behind it would have thrown on a missing file - the
  exact shape of R480.
- **This strengthens the R480 conclusion.** Part fifty-four swept eleven applied
  removals of named literals and found one orphaned assertion. R237 is a twelfth
  case, and it too was handled correctly: files removed, consuming function
  removed, and `ArchitectureTests.cs:1828` now asserts
  `Does.Not.Contain("SharpProof.Gates.runsettings")` as an absence guard. That is
  the fourth applied removal confirmed to use the absence-guard pattern, after
  R231, R232, and R245. R480 remains the single exception across twelve checked
  removals, not the leading edge of a pattern.

### Status (part sixty-seven, continued)

No new `pending` item. Recorded to close the coverage-configuration area and to
add a twelfth data point to the applied-removal audit begun in part forty-nine and
continued in part fifty-four.

## Second survey, part sixty-eight: R512-R513 - configuration and syntax-shape helpers

| R512 | **`AnalyzerConfiguration` duplicates the option-validation scaffold for global and tree configuration.** `GetInvalidGlobalConfigurationValues` and `GetInvalidTreeConfigurationValues` both enumerate `AnalyzerConfigurationOptionRegistry.All`, call `TryGetConflictingAliases`, inspect presence/validity, build `InvalidAnalyzerConfigurationValue` records, and then independently check the retired `sharpproof_mode` aliases. The policies diverge afterward - global values are parsed and tree values are rejected as misplaced, with an optional global comparison - so a shared per-option validation iterator or callback can remove the repeated conflict/retired-mode plumbing without conflating those semantics. | `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs:71-102,138-173` |
| R513 | **`ConditionalTruthOperatorFacts.ReturnsConstant` repeats method/operator syntax extraction four times.** Expression-bodied methods and operators use parallel patterns, and one-return block methods and operators use another parallel pair; only operators additionally permit the harmless-discard form. A helper over the common `BaseMethodDeclarationSyntax`/body shape can extract the expression once, then leave the operator-only discard fallback explicit, reducing branching and the chance that one declaration form gains a different constant-admission rule. | `SharpProof.Effects/ConditionalTruthOperatorFacts.cs:50-88,106-127` |

### Status (part sixty-eight)

R512 remains `pending`; R513 shares only return-expression extraction for
methods and operators, preserving the operator-only harmless-discard policy.

## Second survey, part sixty-nine: R514-R517 - artifact validation and IR traversal seams

| R514 | **`CompilerManifestArtifact.HasValidEffectReplayTrees` revalidates geometry immediately after the codec already validated it.** For each effect claim it first calls `CompilerEffectClaimArtifactCodec.HasValidReplayGeometry`, which checks the event's tree ordinal, tree SHA-256, snapshot SHA-256, source bounds, unique-tree binding, and location span. It then loops over the same events and repeats the null/ordinal checks, tree lookup, tree and snapshot hashes, and source bounds, omitting only the codec's line-map and location-binding checks. Removing the second loop or making the codec expose the needed validation result preserves the manifest-level claim walk while eliminating two authorities for the same replay geometry. | `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs:42-88`; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:927-970` |
| R515 | **`IrRelationalSummaryBuilder.Charge` open-codes the IR child-kind switch already owned by `IrTraversal.GetChildren`.** The summary builder's bounded walk pushes receiver/arguments, unary, binary, conditional, cast, length, and sequence children in a private switch while tracking its own visited set and `Spend()` budget. `IrTraversal.GetChildren` has the same complete child enumeration and is already the canonical helper used by IR analysis and substitution. Reusing it inside the charged loop keeps the budget and visited semantics local but removes a second list of IR node kinds that can drift when the IR grows. | `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:773-829`; `SharpProof.Ir/IrTraversal.cs:4-18` |
| R516 | **Two bounded IR predicates duplicate the same explicit stack/visited traversal scaffold.** `PostconditionObligationBuilder.IsSupportedProofDomain` and `ManagedContractFacts.ContainsPotentiallyFailingCast` each allocate a stack and `HashSet<IrId>`, push a root, skip visited terms, inspect each term, enumerate `IrTraversal.GetChildren`, and return a boolean. Their term predicates differ - supported-domain admission versus cast detection - so they should not be merged semantically, but a predicate-based `IrTraversal.Any` helper can centralize the non-recursive DAG walk and keep both depth-safe checks consistent. | `SharpProof.Worker/PostconditionObligationBuilder.cs:181-213`; `SharpProof.Analyzer.Core/ManagedContractFacts.cs:32-53`; `SharpProof.Ir/IrTraversal.cs:27-49` |
| R517 | **`SharpProofTrustedAttribute` and `SharpProofSuppressAttribute` duplicate their complete reason-validation constructor.** Both reject whitespace-only reasons with the same `string.IsNullOrWhiteSpace` guard, assign the same immutable `Reason` property, and differ only in the exception wording and `AttributeUsage` metadata. A small internal attribute-reason validator can preserve the distinct public attribute types and messages while removing the duplicated constructor policy. | `SharpProof.Attributes/SharpProofTrustedAttribute.cs:6-22`; `SharpProof.Attributes/SharpProofSuppressAttribute.cs:7-23` |

### Status (part sixty-nine)

R515-R517 are applied: charged summary traversal now uses
`IrTraversal.GetChildren` in reverse push order, preserving the prior
visit/budget order; the two bounded IR predicates share a cycle-safe
`IrTraversal.Any` walk; and both public attributes share required-reason
validation while keeping their distinct messages and metadata. R514 remains
pending because validation changes must retain their security and geometry
semantics.

## Second survey, part seventy: R518-R520 - effect-scanner phases and native-loader cleanup

| R518 | **`OperationEffectScanner` duplicates the null-check-to-throw helper for receivers and locks.** `PotentialNullReceiver` and `PotentialNullLock` both return an empty summary when `_nullnessEvaluator.IsProvenNonNull` succeeds and otherwise call `Throw` with a framework exception identity; only the input parameter name and exception (`NullReferenceException` versus `ArgumentNullException`) differ. A parameterized `PotentialNullAccess` helper can centralize the branch while preserving the distinct C# failure semantics at each call site. | `SharpProof.Effects/OperationEffectScanner.cs:1216-1235` |
| R519 | **Deconstruction target descent is implemented twice for two phases with the same declaration/tuple recursion.** `ScanDeconstructionTargetEvaluations` and `ScanDeconstructionTargetWrites` both unwrap `IDeclarationExpressionOperation`, recurse through `ITupleOperation.Elements`, sequence child results with `EffectStep.Then`, and stop after a non-completing step. The phases must remain separate because C# evaluates all target locations before right-hand values and writes later, but a generic target-tree walker with a phase-specific leaf action can remove the duplicated structural recursion without changing that ordering. | `SharpProof.Effects/OperationEffectScanner.Expressions.cs:54-124` |
| R520 | **`ContainerNativeLibrary.InstallZ3ResolverRequired` has a cancellation catch with no cancellation source and an unsafe cleanup asymmetry.** The method accepts no `CancellationToken` and its `NativeLibrary.Load`, field writes, and `SetDllImportResolver` sequence has no cancellable operation, yet it singles out `OperationCanceledException` and rethrows before the general catch resets `_z3Handle`, clears `_z3Assembly`, and frees the native handle. The branch is therefore accidental complexity today and, if an OCE ever emerges from the loader boundary, leaves partially installed state; one cleanup path in a `finally`/general catch is easier to reason about. | `SharpProof.Host/ContainerNativeLibrary.cs:19-50` |

### Status (part seventy)

R519-R520 remain `pending`; R518 shares only the nullness branch and retains
the receiver/lock exception identities. R519 keeps the two evaluation phases
separate, and R520 is recorded as a cleanup/exception-path concern rather than
an instruction to broaden the native-loading surface.

## Second survey, part seventy-one: R521 - IR null construction validation

| R521 | **`IrFactory` repeats the same nullable-type admission guard for values and terms.** `CreateNullValue` and `Null` each enter the factory lock, call `GetTypeInfoCore(type, nameof(type))`, test `IrTermServices.IsNullable(...)`, and throw the identical `Null requires a string, reference, or sequence type.` exception. The successful paths necessarily differ - one creates an `IrValue`, the other interns an `IrNullTerm` - but a private `RequireNullableTypeCore` helper can own the shared lookup and validation while preserving both lock boundaries and result representations. | `SharpProof.Ir/IrFactory.cs:298-307,390-399` |

### Status (part seventy-one)

R521 is applied and validated by the IR test suite. The shared seam covers
only common type validation; value and term construction remain distinct.

## Second survey, part seventy-two: R522 - protocol hash validation reuse

| R522 | **`WorkerProtocolJson.ValidateResponse` recomputes each response hash validity predicate.** It calls `IsSha256(response.RequestHash)` once while adding the structural error and again before checking an expected request hash; the same two-call pattern is repeated for `InputHash`. The predicate scans the entire 64-character string each time. Storing `requestHashValid` and `inputHashValid` once preserves the current conditional mismatch checks and error ordering while removing four repeated scans from every response validation. | `SharpProof.Worker.Protocol/ProtocolJson.cs:326-339` |

### Status (part seventy-two)

R522 is applied and validated by the worker protocol tests. The change is
local memoization only; hash comparison and validation semantics remain
unchanged.

## Second survey, part seventy-three: R523 - cache filename hex predicate

| R523 | **`VerificationCache.IsOwnedCacheEntry` inlines the same hex-digit predicate already factored as `IsHexDigit`.** `IsHexMarker` calls `IsHexDigit` for each transaction marker character, but the cache-entry test separately spells out the identical lowercase hexadecimal ranges for the first 64 filename characters. Reusing `IsHexDigit` in the LINQ predicate leaves the cache filename length and suffix checks unchanged while removing a second authority for accepted cache-name characters. | `SharpProof.Worker/VerificationCache.cs:367-379,523-529` |

### Status (part seventy-three)

R523 is applied and validated by the worker tests. It is a local predicate
reuse and does not alter the cache transaction or filename-shape policy.

## Second survey, part seventy-four: R524-R525 - worker claim projection helpers

| R524 | **`CallableProofCore.Create` and `Merge` duplicate proof-label normalization.** After their distinct inputs are prepared, both methods apply `Distinct(StringComparer.Ordinal)` followed by ordinal `OrderBy` and materialize the same sorted string array. `Create` additionally fails closed when a justification has no label, while `Merge` combines two label sequences, so those policies should remain separate; a private `NormalizeLabels` helper can own only the shared deduplication and ordering. | `SharpProof.Worker/CallableEntryFeasibility.cs:166-195` |
| R525 | **Callable verification and response validation duplicate the core unknown-claim-to-coverage precedence.** `CallableVerificationPolicy.ProjectCallableReason` maps no unknowns to `None`, all unsupported-callable claims to `UnsupportedCallable`, all unsupported-contract claims to `UnsupportedContract`, any infrastructure/backend/malformed reason to `InfrastructureFailure`, and the remainder to `SemanticUnknown`. `WorkerResultAssembler.MatchesCallableProjection` repeats that same sequence before adding timeout, cancellation, missing-claim, and compatibility cases. Extracting a shared core projection and layering the response-specific cases around it reduces drift without collapsing the broader response-state policy. | `SharpProof.Worker/CallableVerificationPolicy.cs:113-136`; `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:204-290` |

### Status (part seventy-four)

R525 remains `pending`; R524 shares only proof-label deduplication and ordering
while preserving the distinct label-failure and response-state policies.

## Second survey, part seventy-five: R526-R527 - protocol normalization and result classification

| R526 | **Compiler and worker protocol layers duplicate order-insensitive assumption comparison.** `CompilerResponseEvidenceAuthority.SameAssumptions` and `WorkerProtocolJson.SameAssumptionDeclarations` each filter null entries, sort by assumption ID with ordinal comparison, project `(Id, Kind)`, and call `SequenceEqual`. The only visible difference is `StringComparer.Ordinal` versus the protocol's cached ordinal comparer. A shared protocol-level comparison helper can remove the two private normalization authorities while leaving canonical output ordering (`IsCanonicalAssumptions`) separate. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:575-589`; `SharpProof.Worker.Protocol/ProtocolJson.cs:961-973` |
| R527 | **`WorkerResultAssembler.Classify` rescans each materialized reason array for every classification flag.** Callable reasons are traversed separately for infrastructure failure, missing claim, cancellation, and timeout; claim reasons are traversed separately for four failure precedences, cancellation, and timeout. A single aggregation pass or small flags helper can preserve the existing callable/claim failure precedence and status ordering while avoiding repeated full-array scans. | `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:133-169` |

### Status (part seventy-five)

R527 remains `pending`; R526 shares only duplicate order-insensitive comparison
plumbing across the protocol and compiler-artifact layers. Canonical
serialization remains distinct. R527 targets enumeration mechanics, not the
deliberate failure and status precedence.

## Second survey, part seventy-six: R528 - allocation sentinel reuse

| R528 | **Effect allocation uncertainty is recognized by the same magic bit in two places.** `EffectSummary.ValidateAllocation` derives the unknown marker as the third bit of `EffectAllocationKind` and rejects mixed values, while `EffectSummaryProjector.Project` independently tests `(EffectAllocationKind)(1 << 2)` to suppress the `Allocates` projection and completeness. The generated enum currently defines `Unknown = 7` after the known values `0..3`, so the checks agree today, but the literal is a second authority. Exposing one enum-derived `UnknownMarker` or `IsUnknown` predicate and reusing it in validation and projection keeps the sentinel tied to the generated catalog. | `SharpProof.Effects/EffectSummary.cs:134-145`; `SharpProof.Effects/EffectProjection.cs:12-21`; `SharpProof.Effects/EffectContractMappings.generated.cs:11-18` |

### Status (part seventy-six)

R528 is applied and validated by the Effects suite. The shared predicate keeps
the allocation lattice and projection policy unchanged; it removes only the
duplicated sentinel encoding.

## Second survey, part seventy-seven: R529-R530 - compiler ordering and wire validation

| R529 | **`CompilationFingerprint` duplicates its adjacent-order predicate in two `IsOrdered` overloads.** The string-array overload and the generic key-selector overload both null-check, zip each array with `Skip(1)`, compare adjacent keys with `StringComparer.Ordinal`, and choose `< 0` versus `<= 0` from `unique`. The string overload can delegate to the generic implementation with the identity selector, or both can use one shared adjacent-order helper, leaving the caller-specific key extraction intact. | `SharpProof.CompilerArtifact/CompilationFingerprint.cs:395-413` |
| R530 | **`CompilerManifestArtifactJson.Deserialize` performs the full manifest validation twice.** After deserialization it calls `Validate(artifact, cancellationToken)`, then calls `Serialize(artifact, cancellationToken)` to test canonical JSON; `Serialize` canonicalizes the collections and calls the same full `Validate` again before serializing. Splitting canonicalization from validation or making the canonical-check path reuse the first validation result can remove one expensive decodability, replay-geometry, source-binding, and fingerprint-validation pass while retaining the canonical-byte comparison. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:386-409,333-379` |

### Status (part seventy-seven)

R530 remains `pending`; R529 is applied and validated by the worker tests as a
local generic-helper deduplication. R530 preserves the intentional
canonical-wire check and targets only the repeated validation work around it.

## Second survey, part seventy-eight: R531 - replay identity authority

| R531 | **`EffectCounterexampleReplayer` reimplements the compiler replay identity hashes.** Its `ComputeConstraintIdentity` repeats the codec's domain/version, contract kind, allowed-effect/capability, and ordinal-sorted exception-type hashing, while `ComputeOperationIdentity` repeats the codec's replay-operation field sequence, array loops, and location hashing. The worker spells the domain/version literals again and does not apply the codec's `MemberIdentity ?? string.Empty` and nullable-array normalization, so malformed or newly added replay fields can make the two authorities diverge before a compiler-produced artifact is replayed. Reusing the codec's internal identity methods (or moving the shared hash builder to the artifact layer) leaves replay validation in the worker but gives sealing, validation, and replay one canonical digest definition. | `SharpProof.Worker/EffectCounterexampleReplayer.cs:326-390`; `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs:229-254,292-329`; `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:495-499` |

### Status (part seventy-eight)

R531 is a `pending` reduction candidate with correctness implications beyond
line-count reduction. The proposed seam preserves the worker's validation and
interpretation policy while centralizing only the digest construction.

## Second survey, part seventy-nine: R532-R533 - source-location sentinels

| R532 | **The worker replay path duplicates compiler-artifact source-location helpers.** `EffectCounterexampleReplayer.WitnessesEqual` uses a private five-field tuple comparison, and its `Copy` method rebuilds `WorkerSourceLocation` field by field. `CompilerSourceLocationAuthority` already owns `LocationsEqual` and `CopyLocation`, and the worker already consumes that artifact assembly for replay validation and fingerprints. Calling the shared helpers removes a second equality/copy definition and keeps future source-location fields from being silently omitted by the replay path. | `SharpProof.Worker/EffectCounterexampleReplayer.cs:275-311`; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:258-291` |
| R533 | **The all-zero source-location sentinel is spelled twice.** `WorkerProtocolJson.HasValidLocationOrNone` checks `Path`, `Start`, `Length`, `Line`, and `Column` individually after the normal location predicate fails; `CompilerSourceLocationAuthority.IsNone` defines the same five-field zero shape with a property pattern. A shared protocol-level `IsNone` helper can be used by both validation layers, preserving `HasValidLocationOrNone`'s valid-location alternative and the compiler authority's binding policy while removing a second sentinel definition. | `SharpProof.Worker.Protocol/ProtocolJson.cs:826-840`; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:19-29` |

### Status (part seventy-nine)

R532 reuses the compiler source-location copy/equality helpers without changing
ordinary location validity. R533 now centralizes only the zero-location
representation in the protocol layer while preserving the artifact-layer
compatibility wrapper.

## Second survey, part eighty: R534-R535 - host path and tool resolution reuse

| R534 | **`ResetPublicationSet` recanonicalizes every path solely to derive marker paths.** It first converts `requestedPaths` through `CanonicalPublicationPaths`, which calls `RequireLocalPath` and returns canonical strings, then calls the public `PublicationMarkerPath` for each of those strings; that public wrapper calls `Canonicalize` again before reaching `PublicationMetadataPath`. `AcquirePublicationSet` already uses the canonical-only `PublicationLockNameForCanonicalPath` form. Mapping reset markers through the same canonical-only helper removes a full Linux path walk per publication member while retaining the initial validation and reset ownership checks. | `SharpProof.Host/LinuxPathIdentity.cs:176-193,232-250`; `SharpProof.Host/LinuxPathIdentity.cs:126-136,469-480` |
| R535 | **`RunVerifier.ResolveDotNetHost` repeats explicit-host validation already performed by `ValidateDotNetInstallation`.** For a non-`dotnet` executable it checks `Path.IsPathRooted` and the basename, then immediately calls `ValidateDotNetInstallation`, which repeats the same rooted/basename checks before canonicalizing and verifying the installation. Calling the validator directly for explicit paths preserves the same diagnostic and trusted-file comparison while removing duplicate path-shape checks; the special bare `dotnet` branch remains separate. | `SharpProof.BuildTasks/RunVerifier.cs:1230-1259,1295-1318` |

### Status (part eighty)

R535 is applied: explicit verifier paths now go directly through
`ValidateDotNetInstallation`, while the bare `dotnet` lookup remains separate
and the trusted-installation comparison is unchanged.

## Second survey, part eighty-one: R536 - duplicated shrinker engines

| R536 | **The fuzz tool carries two near-duplicate structural shrinker engines.** `IrStructuralShrinker` and `CSharpStructuralShrinker` each validate the input and mismatch callback, repeatedly enumerate smaller candidates until no change remains, filter direct children by type, add domain-specific constants, recursively shrink every child, rebuild the parent, deduplicate candidates, and catch invalid rebuilds. Their expression models and child-replacement constructors must remain type-specific, and the IR callback is asynchronous, but a generic shrink loop/candidate traversal scaffold could own the shared termination, size, seen-set, and recursive mechanics. Keeping the model-specific child enumeration and replacement policy at the edges would remove a second implementation of the fuzzer's most complex minimization control flow. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:420-626`; `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1846-2016` |

### Status (part eighty-one)

R536 is a `pending` candidate. The proposed abstraction targets the shrinker
control flow only; it does not merge the IR and C# expression vocabularies or
their different synchronous/asynchronous mismatch contracts.

## Second survey, part eighty-two: R537 - IR child enumeration authority

| R537 | **`IrStructuralShrinker.Children` duplicates the canonical IR child switch.** The fuzz shrinker has a private switch covering opaque receivers and arguments, unary, binary, conditional, cast, length, and sequence-access terms; `SharpProof.Ir.IrTraversal.GetChildren` already owns the same complete child enumeration for traversal, substitution, and analysis. The only reason the shrinker cannot call it today is that the helper is internal and `SharpProof.Fuzz` is not a friend assembly. Exposing a narrow supported child-enumeration seam or granting the intended tooling friend access would remove a second list of IR node kinds, reducing drift when the IR grows. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:570-591`; `SharpProof.Ir/IrTraversal.cs:4-18`; `SharpProof.Ir/SharpProof.Ir.csproj:17-34` |

### Status (part eighty-two)

R537 is a `pending` candidate. This is narrower than R536: it targets an
exact duplicate of the IR traversal authority, while leaving the shrinker's
model-specific rebuild and minimization policy intact.

## Second survey, part eighty-three: R538-R539 - traversal and assumption scans

| R538 | **`ExceptionHandlerReachability.GetPotentialExceptions` retains local copies of its stack-push helpers.** Its local `PushSequential` builds the reachable prefix, stops after the first child that cannot complete normally, and reverses it through `PushAll`; the class-level `PushSequentialCore` and `PushAllCore` immediately below implement the same list and stack protocol. The `PushChildren` local wrapper remains useful because it supplies the captured switch scheduling state, but the sequential/reverse helpers can call the existing core methods with `remaining`, removing likely refactor residue and a second maintenance point. | `SharpProof.Effects/ExceptionHandlerReachability.cs:1193-1209,1434-1457` |
| R539 | **`WorkerProtocolJson.ValidateClaimResult` scans the assumption array twice for one effect-evidence tuple.** It separately calls `Any` to detect a trusted-boundary assumption and to detect a used trusted-boundary assumption, rescanning the same nullable array and repeating its null/kind predicate. A single pass or compact summary helper can produce both flags while preserving `MatchesEffectEvidenceTuple` argument order and validation behavior. | `SharpProof.Worker.Protocol/ProtocolJson.cs:629-644` |

### Status (part eighty-three)

R539 keeps the two evidence flags distinct while avoiding repeated
assumption-array scans. R538 now reuses the shared sequential/reverse stack
helpers without merging the reachability-specific dispatcher.

## Second survey, part eighty-four: R540 - callable projection scans

| R540 | **`WorkerResultAssembler.MatchesCallableProjection` repeatedly scans each callable's unknown-reason array.** After materializing `reasons`, it performs separate `All`/`Any` passes for unsupported-callable, unsupported-contract, infrastructure, method-timeout, project-timeout, and cancellation cases, with an earlier `owned.All` pass over the same claims. A single aggregation pass can record the relevant flags and preserve the current precedence (`UnsupportedCallable`, `UnsupportedContract`, infrastructure, method timeout, project timeout, canceled, semantic unknown) while reducing repeated enumeration and making the projection policy easier to audit. | `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:228-274` |

### Status (part eighty-four)

R540 is a `pending` reduction candidate. The proposed change is limited to
classification aggregation; it does not alter the compatibility exceptions
handled after the primary projection.

## Second survey, part eighty-five: R541-R543 - snapshot, flow, and contract predicates

| R541 | **`CorpusSnapshotFormat.Render` and `Parse` duplicate the canonical data validation.** Both paths run the same `Any(!IsCanonicalData)` check followed by the same ordinal line-order check, differing only in whether the input is the caller's data lines or the parsed data suffix. A private `ValidateCanonicalData` helper can centralize the snapshot invariant while keeping byte-level parsing and rendering responsibilities separate. | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:17-23,69-77` |
| R542 | **The summary builder and worker executor carry two implementations of the acyclic IR flow engine.** `IrRelationalSummaryBuilder.Run` and `AcyclicBlockPredicateExecutor.Run` independently walk instructions, propagate environments, constrain branches, sort and merge incoming states, create an acyclic order, reject false incoming predicates, substitute terms, and enforce depth/resource support. Their call handling and side effects differ (summary dependencies/provenance and `mayThrow` versus worker specs/summaries and memory-havoc checks), so the whole classes should not be merged; a shared IR flow/merge engine with policy callbacks could own the control-flow scaffold and leave those semantic tails at the edges. R515 is the narrower child-enumeration instance inside this broader overlap. | `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:294-384,647-771,843-856`; `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs:91-205,225-329,531-618` |
| R543 | **Closed-attribute recognition is duplicated across the contracts and effects layers.** `ContractSelectionInventory.IsClosedContract` checks the attribute's original definition against `NotNull`, `Positive`, and `InRange`; `ConservativeEffectCallPreconditionPolicy.IsClosedPrecondition` repeats the same three comparisons against its separately resolved symbols. Moving the small membership predicate to a lower-level shared metadata seam (or exposing it from the generated contract catalog) would remove a second authority without coupling `SharpProof.Effects` back to `SharpProof.Contracts`; the full validator must remain separate because it checks types, ref kinds, and range arguments. | `SharpProof.Contracts/ContractSelectionInventory.cs:103-108`; `SharpProof.Effects/EffectCallPreconditionPolicy.cs:125-144` |

### Status (part eighty-five)

R542-R543 remain `pending`; R541 shares only the canonical snapshot-data
invariant between parsing and rendering. R542 deliberately calls for a policy
seam rather than a blind merge, and R543 preserves the current project
dependency direction as part of the proposed design constraint.

## Second survey, part eighty-six: R544 - finite-domain enumeration

| R544 | **`FiniteDomainSmtDifferentialOracle` duplicates the recursive finite-assignment walk.** `IsDefinedForAllAssignments` and `IsSatisfiableByEnumeration` independently enumerate Boolean and integer variable values into the same mutable environment, perform the same variable-type lookup, cancellation check, unsupported-type fallback, and cleanup. Only the leaf condition (any non-exception Boolean value versus a true Boolean value) and the resulting short-circuit polarity differ. A shared assignment-search helper with a leaf predicate or an enumeration callback can remove the duplicated recursion while preserving the two oracle meanings and their early-exit behavior. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:32-103,249-312` |

### Status (part eighty-six)

R544 is a `pending` reduction candidate. The shared helper should retain the
current cancellation and mutable-environment cleanup semantics; it should not
collapse the definedness and satisfiability predicates into one policy.

## Second survey, part eighty-seven: R545 - analyzer test host plumbing

| R545 | **`AnalyzerTestHost` duplicates analyzer execution setup and diagnostic ordering.** The `AnalyzeAsync` overload that accepts a dictionary plus additional files and the overload that accepts an `AnalyzerConfigOptionsProvider` each perform the same compilation-error guard, analyzer selection, `CompilationWithAnalyzersOptions` construction, analyzer execution, source-span/ID ordering, and immutable-array materialization. A private core helper taking the prepared `AnalyzerOptions` and cancellation token could centralize that harness plumbing while leaving the two public overloads responsible for their distinct option-provider inputs. | `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:144-195` |

### Status (part eighty-seven)

R545 is applied: the dictionary and options-provider overloads now prepare their
own `AnalyzerOptions` and delegate execution, compilation-error checks, analyzer
selection, ordering, and cancellation to one private core. The full analyzer
test suite passes (476 tests).

## Second survey, part eighty-eight: R546 - differential type mapping

| R546 | **`IrCSharpDifferentialOracle` maintains two parallel IR-type mappings.** `TryGetCSharpType` maps Boolean, integer, string, object-reference, and recursively nested sequence types to generated C# type names, while `TryGetRuntimeType` repeats the same kind checks and sequence recursion to produce `System.Type` values. Keeping the source-name and runtime-type projections separate may be necessary at the final boundary, but a shared supported-type descriptor or canonical IR-type classification would remove the repeated vocabulary and reduce the chance that generated code and runtime argument construction drift apart. | `SharpProof.Testing/IrCSharpDifferentialOracle.cs:349-365,451-470` |

### Status (part eighty-eight)

R546 is applied: one recursive supported-type projection now produces both the
generated C# name and runtime `System.Type`; the existing boundary helpers only
select the requested projection. Object identity, nested sequence support, and
unsupported-type abstention are unchanged. Testing tests pass (13 tests).

## Second survey, part eighty-nine: R547 - conversion unwrapping

| R547 | **`RoslynOperationLowerer` duplicates implicit-conversion unwrapping.** `UnwrapImplicitConversions` and `UnwrapImplicitReferenceConversions` both loop through implicit, operator-method-free `IConversionOperation` nodes and replace the current operation with its operand; the second adds only `Conversion.IsReference` to its predicate. A predicate-driven `UnwrapImplicitConversions` helper can preserve the reference-only comparison policy while removing the duplicated loop and making future conversion-shape changes happen in one place. | `SharpProof.Frontend/RoslynOperationLowerer.cs:236-263` |

### Status (part eighty-nine)

R547 is applied: one conversion-unwrapping loop now accepts a reference-only
filter, preserving the stopping behavior for user-defined and non-reference
conversions at the specialized callers.

## Second survey, part ninety: R548 - shared fuzz boundary corpus

| R548 | **`WellSortedIrGenerator` and `SmallCSharpCaseGenerator` duplicate the same integer boundary corpus.** Both fuzz generators declare an `InterestingIntegers` array with the identical eight values (`long.MinValue`, `-3`, `-1`, `0`, `1`, `2`, `3`, and `long.MaxValue`). Since these generators feed related IR/C# differential cases, separate copies can silently drift and weaken cross-representation coverage. A shared testing corpus or common boundary-value provider could make the intended alignment explicit while keeping the C# generator's separate `LiteralIntegers` subset and each generator's random selection behavior local. | `SharpProof.Testing/WellSortedIrGenerator.cs:32-40`; `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:654-667` |

### Status (part ninety)

R548 is applied: both generators now consume the shared eight-value boundary
corpus, while the C# generator's smaller literal subset remains local.

## Second survey, part ninety-one: R549 - repeated release-tag gate

| R549 | **`package-consumers.yml` runs the same release-tag validation in two jobs.** The `package` job conditionally invokes `tooling release-tag` for every version-tag ref before packing, and the later `release-qualification` job invokes the same command again after downloading or rebuilding the qualified inputs. Both pass the same `GITHUB_REF`, `GITHUB_REF_NAME`, and `GITHUB_SHA` identity fields to the same `Invoke-SharpProofReleaseContainer.ps1 -Mode ValidateTag` logic. Keeping one check at the package boundary and making qualification consume a recorded tag-validation receipt, or explicitly documenting the second invocation as defense-in-depth, would remove a repeated in-container Git/tag traversal and clarify whether two validations are required. | `.github/workflows/package-consumers.yml:40-49,131-137`; `scripts/Invoke-SharpProofReleaseContainer.ps1:41-75` |

### Status (part ninety-one)

R549 is a `pending` reduction candidate. Do not remove the qualification-time
identity guarantee unless the package artifact and the exact checkout identity
are bound by an equivalent immutable receipt.

## Second survey, part ninety-two: R550 - publication URI validation

| R550 | **`SharpProof.PublicationPlanIdentity` bypasses the shared HTTPS destination validator.** Its registry branch independently checks both destinations and the package base address for an absolute HTTPS URI with a nonblank host and no user info, query, or fragment. `SharpProof.PublicationDestination.Resolve-SharpProofPublicationHttpsDestination` already owns the same URI predicate and is used when creating the authority; the plan validator's package-base branch adds only the separate canonical trailing-slash comparison. Calling the shared validator and retaining that canonical-form check would remove a second URI-policy implementation while preserving the plan-specific schema and normalization requirements. | `scripts/SharpProof.PublicationPlanIdentity.psm1:89-131`; `scripts/SharpProof.PublicationDestination.ps1:8-24,191-195,321-323` |

### Status (part ninety-two)

R550 is applied: publication-plan registry destinations now use the shared HTTPS
resolver, while the validator retains its generic registry error and the
canonical package-base-address check. Canonical and registry-canonical fixtures
pass.

## Second survey, part ninety-three: R551 - fixture package identity parsing

| R551 | **`Get-SharpProofPublicationFixtureArchiveCatalog` repeats package identity extraction.** For each fixture archive it first calls the shared `Get-SharpProofNuspecMetadata`, then independently selects the `n:id` and `n:version` nodes, checks their cardinality, and converts them to strings. `Get-SharpProofPackageIdentity` in `SharpProof.PackageIdentity.psm1` already performs the same metadata lookup and ID/version extraction through that helper; the fixture path additionally needs its own package-name and release-version policy, which should remain explicit. Reusing the shared identity projection for the common fields would remove duplicate XML-query plumbing while retaining fixture-specific role, archive, and version validation. | `scripts/SharpProof.PublicationDestination.ps1:57-96`; `scripts/SharpProof.PackageIdentity.psm1:16-73` |

### Status (part ninety-three)

R551 is a `pending` reduction candidate. Keep the fixture archive's exact-one
identity-node and release-version checks, and do not broaden the shared package
identity helper's repository requirements for fixture packages.

## Second survey, part ninety-four: R552 - package-consumer identity reuse

| R552 | **`Test-SharpProofPackageConsumers` reparses the package source after resolving it.** `Resolve-SharpProofPackageSource` already opens every `.nupkg` and `.snupkg`, obtains each `SharpProofPackageIdentity`, and validates that all package and symbol versions agree. After that function returns only the directory path, `Get-SharpProofPortablePackageVersion` enumerates the `.nupkg` files and calls `Get-SharpProofPackageIdentity` again to find the `SharpProof` version. Returning a small validation result containing the canonical identities/version, or caching that result behind a private helper, would avoid repeating archive/nuspec parsing while preserving the source-validation-only and framework-consumer call paths. | `scripts/Test-SharpProofPackageConsumers.ps1:40-76,97-111,479-499` |

### Status (part ninety-four)

R552 is a `pending` reduction candidate. Preserve the existing exact package and
symbol-set checks and the public source/path behavior; only reuse the already
validated identity/version data rather than weakening validation.

## Second survey, part ninety-five: R553 - release package archive rescans

| R553 | **`New-SharpProofReleaseEvidence` scans each package archive twice for third-party payloads.** The package-payload loop calls `Test-SharpProofPackagePayload`, which opens every `.nupkg` and enumerates its payload entries while collecting third-party paths. Later, the third-party inventory loop calls `Test-PackageThirdPartyInventory` for those same `.nupkg` files; that helper reopens each archive and rebuilds the actual third-party entry set before checking the same declared entries and reading notices. A shared archive-inspection result, or one combined validation/projection helper, could reuse the entry set and notice text without weakening the distinct payload-evidence and notice-validation rules. | `scripts/New-SharpProofReleaseEvidence.ps1:379-395,428-438`; `scripts/Test-SharpProofPackagePayloads.ps1:166-191`; `scripts/New-SharpProofReleaseEvidence.ps1:191-248` |

### Status (part ninety-five)

R553 is a `pending` reduction candidate. Keep payload closure, assembly identity,
third-party notice, and manifest projection checks separately observable; the
reduction is about reusing one archive snapshot, not dropping any validation.

## Second survey, part ninety-six: R554 - unreferenced package-license helper

| R554 | **`Get-SharpProofPackageLicenseGraph` appears to be dead script code.** The helper is defined in `Test-SharpProofPackageDependencies.ps1`, but a repository-wide search finds no call site; the architecture harness invokes `Get-SharpProofPackageDependencyGraph`, and release scripts use the third-party component graph instead. If this dot-sourced script is not intentionally exposing an external function contract, remove the unreferenced helper. If the license projection is still needed, fold it into the dependency-authority result so it does not reload the same contract after validation. | `scripts/Test-SharpProofPackageDependencies.ps1:224-246`; `SharpProof.ArchitectureTest/PackageDependencyAuthorityTests.cs:371-385` |

### Status (part ninety-six)

R554 is applied: repository-wide search found no caller for the license-only
projection, so the unreferenced helper was removed; dependency and license
authority checks remain in their active paths.

## Second survey, part ninety-seven: R555 - unused release-authority local

| R555 | **`Get-SharpProofReleaseVersionAuthority` computes an unused path.** After normalizing `$RepositoryRoot`, the function assigns `$path = Join-Path $root 'SharpProof.Release.props'`, but never reads it; the returned authority record uses the literal relative path and delegates version extraction to `Get-SharpProofReleaseVersion`. Removing the assignment makes the function's actual inputs and outputs clearer and avoids implying that the returned path was resolved or validated there. | `scripts/Get-SharpProofReleaseVersion.ps1:38-48` |

### Status (part ninety-seven)

R555 is applied: the authority projection no longer computes an unread absolute
props path; root normalization and delegated version lookup remain unchanged.

## Second survey, part ninety-eight: R556 - release-config forwarding wrapper

| R556 | **`Require-SetMembers` is a misleading pure forwarding wrapper.** In `Test-SharpProofReleaseConfiguration.ps1` the helper accepts an actual string set and an expected object set, then immediately calls `Require-ExactSet` with the same values. Both of its call sites therefore enforce exact equality, not subset membership; removing the wrapper and calling the canonical helper directly eliminates an extra name and avoids suggesting a weaker policy than the code actually applies. | `scripts/Test-SharpProofReleaseConfiguration.ps1:59-67,281-295` |

### Status (part ninety-eight)

R556 is applied: both environment set checks call `Require-ExactSet` directly;
expected-value conversion and duplicate detection remain centralized there.

## Second survey, part ninety-nine: R557 - publication-plan revalidation loop

| R557 | **`Publish-SharpProofRelease` revalidates the entire publication plan around every artifact push.** Inside the six-package loop it calls `Test-SharpProofPublicationPlanIdentity` before each main push and again before the corresponding symbol push. That validator rereads and byte-checks all seven planned artifacts, rechecks all package decisions, and for fixture destinations rebuilds the current fixture snapshot. The plan and local artifact files are otherwise immutable during this loop, so the same full validation is repeated up to twelve times; a single pre-loop validation plus a narrow per-artifact freshness check, or an explicitly documented post-main-push boundary check, would retain tamper detection while removing repeated whole-plan traversal. | `scripts/Publish-SharpProofRelease.ps1:822-853`; `scripts/SharpProof.PublicationPlanIdentity.psm1:24-403` |

### Status (part ninety-nine)

R557 is a `pending` reduction candidate. Preserve any intended defense-in-depth
against local artifact mutation during a network push; first establish which
boundary must be revalidated, then avoid rerunning unrelated package and fixture
identity checks for every package.

## Second survey, part one hundred: R558 - release-bundle name-set duplication

| R558 | **`Test-SharpProofReleaseBundleTopology` duplicates the expected-name validation already owned by `Test-SharpProofExactRegularFileSet`.** The topology wrapper creates ordinal and case-insensitive hash sets, checks every artifact filename for blank/path-like/duplicate values, and then passes that set to the exact-file-set helper, which rebuilds both hash sets and repeats the same filename validation before scanning the directory. Since the six-artifact count and package/symbol-kind checks already establish the expected cardinality, the helper can accept the manifest name plus artifact names directly and own the single duplicate/path validation pass. | `scripts/SharpProof.ReleaseBundle.ps1:3-50,52-92` |

### Status (part one hundred)

R558 is a `pending` reduction candidate. Keep the topology-specific artifact-kind
and six-artifact checks; consolidate only the repeated filename-set construction
and validation before the regular-file scan.

## Second survey, part one hundred one: R559 - loop inventory path guard

| R559 | **`loop-command.sh` duplicates the same relative-path safety case.** The target-workspace reconciliation loop and the source-file materialization loop each reject an empty path, an absolute path, and paths containing `../` with an identical seven-line `case` block and different error text. A small `validate_relative_path` shell function can centralize the traversal guard while allowing the caller to keep its target/source-specific diagnostic. | `eng/container/loop-command.sh:167-180,189-203` |

### Status (part one hundred one)

R559 is applied: target reconciliation and source materialization now call one
relative-path validator, while its inventory-specific diagnostics and validation
ordering remain distinct.

## Second survey, part one hundred two: R560 - production inventory path validation

| R560 | **`Get-SharpProofProductionInventory` repeats canonical path and file checks.** `Resolve-RepositoryPath` validates blank/rooted/relative input, repository containment, and canonical relative segments; `Get-GeneratedManifest` manually repeats the blank/rooted/`//`/dot-segment predicate before joining the root and checking a leaf; `Get-CanonicalFileRecord` then repeats the root-relative join and leaf existence check for evaluated compile files. A shared canonical-relative-path/file-record seam can preserve the manifest's duplicate-set check and context-specific errors while removing this drift-prone validation triplication. | `scripts/Get-SharpProofProductionInventory.ps1:34-50,118-129` |

### Status (part one hundred two)

R560 is a `pending` reduction candidate. Keep manifest-specific uniqueness and approved-output semantics; only centralize common path normalization and existence checks without weakening repository containment.

## Second survey, part one hundred three: R561 - exact JSON property validators

| R561 | **The standalone-gate and fuzz-result validators each implement an exact JSON-object property-set assertion.** `Assert-ExactJsonProperties` enumerates `PSObject.Properties.Name`, compares count, and rejects unexpected names, while `Assert-ExactJsonObjectProperties` enumerates `JsonElement` properties, also rejects duplicate names, and rejects unexpected names. The representations and strictness differ, but a shared adapter or common helper with explicit options could own the property-set comparison instead of maintaining two nearly identical contracts; retain the fuzz validator's duplicate-name and JSON-value-kind checks. | `scripts/Assert-SharpProofStandaloneGateResult.ps1:3-15`; `scripts/Assert-SharpProofFuzzRunnerResult.ps1:3-20` |

### Status (part one hundred three)

R561 is a `pending` reduction candidate. Preserve the JsonElement-only duplicate-property protection and the distinct error/typing behavior; centralize only the common expected-name comparison if the abstraction stays clearer than the two small local helpers.

## Second survey, part one hundred four: R562-R563 - qualification artifacts and corpus sizing

| R562 | **The pilots qualification receipt revalidates package-artifact records that its report validator has just validated more completely.** `Write-SharpProofQualificationReceipt` projects every `packageArtifacts` row, checks leaf names, `.nupkg`/`.snupkg` suffixes, positive sizes, and six unique names, then the `pilots` branch calls `Test-SharpProofPilotReport`, whose package-artifact pass checks the same six records plus package IDs, version, repository commit, and exact expected names. A report-validation result that returns the normalized package rows, or a receipt path that relies on the stronger validator for pilots, can remove the repeated scan while keeping the lighter generic checks for other package-backed gates. | `scripts/Write-SharpProofQualificationReceipt.ps1:31-50,90-95`; `scripts/Test-SharpProofPilotReport.ps1:58-81` |
| R563 | **The OSS importer and corpus validator maintain the same method-count boundary as separate literals.** `OpenSourceCorpusImporter` selects exactly `TargetMethodCount = 200` candidates, while `OpenSourceCorpusCatalog` accepts a checked-in document only when its method count is at least `MinimumMethodCount = 200` (and at most 500). If the minimum coverage policy changes, these authorities can drift: an import can keep producing the old count while validation expects another minimum. Reuse the catalog's lower-bound constant for the default import target, or make the importer target an explicit policy input, while retaining the catalog's independent upper bound. | `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:27,142-148`; `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:15-17,128-133` |

### Status (part one hundred four)

R562 remains pending because pilot-report validation is intentionally stronger
than the generic receipt checks. R563 is applied: the importer now derives its
200-method target from `OpenSourceCorpusCatalog.MinimumMethodCount`, while the
catalog's independent 500-method upper bound remains explicit.

## Second survey, part one hundred five: R564 - performance policy overload defect

| R564 | **Defect, not a reduction: the four-document `ValidateAdvisoryPackagePolicy` overload passes the wrong document.** The overload taking `portableProps`, `portableTargets`, `verifierProps`, and `verifierTargets` forwards `portableTargets` as the `portableContract` argument to the five-document implementation. That implementation then searches the targets document for the contract's default `SharpProofProfile`, `SharpProofFeatures`, and `SharpProofVerify` properties, so the canonical four-document call cannot validate the intended policy and mutation tests can pass only because they throw for this unrelated reason. The forwarding call should supply a real `portableContract`, or the overload should be removed if all callers can use the five-document form. | `SharpProof.Gates/Performance/PerformanceGate.cs:1513-1524,1527-1547`; `SharpProof.Gates.Test/PerformanceGateTests.cs:749-818` |

### Status (part one hundred five)

R564 is an unimplemented defect finding. Do not fold it into a generic helper reduction until the four-document test contract has a valid canonical success case and the mutation tests demonstrate that their failures target the requested mutations.

## Second survey, part one hundred six: R565 - package-build statistic re-sorting

| R565 | **Potential redundant validation and sorting in `PackageBuildEstimator.Estimate`.** The estimator derives `baselineFirst`, `unannotatedAdvisoryFirst`, `balancedRatios`, and `ratios` from `PackageBuildSample` values, then calls `Median` on three of those collections and `NearestRankPercentile` on `ratios`. Each call runs `ValidateAndSort`, so the same already-positive, finite values are re-enumerated and sorted; the full `ratios` collection is sorted twice for the raw median and P95. A private normalized-statistics path could validate/sort each derived collection once or calculate the median and percentile from one sorted full-ratio array, while leaving the public `Median` input validation intact. Keep the sample index/order-balance checks and the geometric pair construction separate because they enforce different invariants. | `SharpProof.Gates/Performance/PackageBuildEstimator.cs:8-49,102-159,162-213` |

### Status (part one hundred six)

R565 is a pending reduction candidate. Its payoff is small for the current sample sizes, but the repeated full-array work adds avoidable complexity and would scale with package-build repetitions.

## Second survey, part one hundred seven: R566 - duplicate percentile algorithms

| R566 | **`PerformanceGate` and `PackageBuildEstimator` duplicate nearest-rank percentile selection.** `PerformanceGate.Percentile` sorts an enumerable, checks that it is nonempty, clamps `ceil(rank * count) - 1`, and returns that element; `PackageBuildEstimator.NearestRankPercentile` repeats the same sort/index/clamp algorithm through `ValidateAndSort`, adding the estimator's finite-positive checks. Move the common nearest-rank selection into one shared helper (or expose a validated statistics utility with an explicit validation boundary) so changes to percentile semantics cannot drift. Preserve the stricter estimator input validation and the existing empty-input behavior. | `SharpProof.Gates/Performance/PerformanceGate.cs:173-182,1089-1102`; `SharpProof.Gates/Performance/PackageBuildEstimator.cs:158,184-193` |

### Status (part one hundred seven)

R566 is applied: `PerformanceGate` now delegates nearest-rank selection to
`PackageBuildEstimator` with validation disabled, while package-build statistics
retain their finite-positive sample checks. The shared implementation preserves
the existing empty-input and rank-clamping behavior.

## Second survey, part one hundred eight: R567 - duplicate mutation-ledger comparison

| R567 | **`Invoke-SharpProofTrustedMutationsParallel.ps1` reimplements the module's ordinal sequence comparison.** Its local `Test-ExactStringSequence` counts two string arrays and loops through them with case-sensitive `-cne`, while `SharpProof.MutationEvidence.psm1` already uses `[Linq.Enumerable]::SequenceEqual(..., [StringComparer]::Ordinal)` for the same expected-versus-actual mutation-ledger comparison. The local helper is used only for the shard checks, so calling the shared ordinal comparison (or exporting one small helper) removes a second definition of the ledger equality contract. Preserve the explicit count/duplicate checks around the comparison and verify null/empty-array behavior before changing the call sites. | `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:82-97,170-177,197-204`; `scripts/SharpProof.MutationEvidence.psm1:535-545` |

### Status (part one hundred eight)

R567 is applied: `Test-SharpProofOrdinalStringSequence` now lives in
`SharpProof.MutationEvidence.psm1` and is used by both the evidence parser and
trusted-mutation shard checks, preserving ordinal case-sensitive comparison and
the existing null/empty handling.

## Second survey, part one hundred nine: R568 - acceptance dotnet wrapper

| R568 | **`eng/acceptance/Verify.ps1` keeps a local checked-dotnet wrapper after the shared container helper was introduced.** The acceptance script resolves `scripts/Invoke-SharpProofDotnet.ps1` itself and defines `Invoke-SharpProofDotnet` solely to forward an argument array, timeout, and nonzero-exit exception. `SharpProof.ContainerExecution.psm1` already exports `Get-SharpProofDotnetWrapperPath` and `Invoke-SharpProofRequiredDotnet` for that exact path-and-check protocol; the acceptance calls do not need a distinct quiet or output mode. Importing the shared module and passing its existing timeout parameter removes a second wrapper contract and keeps acceptance aligned with the other container entrypoints. | `eng/acceptance/Verify.ps1:17,218-231,234-236,630-634,671-701`; `scripts/SharpProof.ContainerExecution.psm1:4-49,537`; R269 |

### Status (part one hundred nine)

R568 is applied: `Verify.ps1` now imports `SharpProof.ContainerExecution.psm1` and
uses `Invoke-SharpProofRequiredDotnet` directly, preserving the acceptance-specific
timing phases and timeout values without a local process-forwarding shim.

## Second survey, part one hundred ten: R569 - acceptance timing record duplication

| R569 | **`Verify.ps1` builds acceptance timing phase records in two helpers.** `Add-AcceptanceTimingPhase` and `Complete-AcceptanceTimingPhase` each create the same ordered object with `name`, `startedUtc`, `completedUtc`, `elapsedMilliseconds`, and `status`; they differ only in whether the elapsed duration comes from a skipped/manual phase or the active phase's start offset. After `Complete` computes that duration, it can delegate record creation to `Add`, reducing two parallel serialization paths and keeping timestamp formatting in one place. Retain the active-phase guard, stopwatch stop, and state reset around that delegation. | `eng/acceptance/Verify.ps1:89-153` |

### Status (part one hundred ten)

R569 is applied: `Complete-AcceptanceTimingPhase` now computes the active
phase's exact start/completion ticks and delegates record construction to
`Add-AcceptanceTimingPhase`. Optional explicit bounds preserve the active
phase timestamps, while skipped/manual phases retain their derived timing.

## Second survey, part one hundred eleven: R570 - unused Compose version probe

| R570 | **`build.ps1` probes the Docker Compose version and discards the result.** `Build-ToolingImage` invokes `docker compose version --short` only through `Invoke-Docker`, then immediately invokes `docker compose build tooling`; no value from the version command is consumed and the build command already provides the operational availability check. Unless the probe is intended as a separately documented diagnostic (in which case its output should be labeled and used), removing it eliminates one extra external process and one failure point from every profile invocation while preserving the actual tooling-image build. | `build.ps1:31-39` |

### Status (part one hundred eleven)

R570 is applied: `build.ps1` now invokes the checked tooling-image build directly;
the discarded Compose version probe and its extra failure point are gone.

## Second survey, part one hundred twelve: R571 - repeated collectible runtime fixture

| R571 | **Three runtime-oracle test fixtures duplicate the same collectible `AssemblyLoadContext` harness.** `RuntimeFlagshipOracleTests`, `RuntimeRequiresOracleTests`, and `RuntimeEffectOracleTests` each create a collectible context, attach `ResolveFromDefaultContext`, load an emitted image from a non-writable `MemoryStream`, invoke a callback, detach the resolver, and unload in `finally`; they also repeat the same assembly-name resolver that scans `AppDomain.CurrentDomain.GetAssemblies()` with `AssemblyName.ReferenceMatchesDefinition`. Only the context name and emitted-image wrapper type differ. A shared test helper linked from `eng/testing` can parameterize those two seams and centralize the load/unload lifetime, leaving each oracle's runtime assertions and image representation explicit. | `SharpProof.Analyzer.Test/RuntimeFlagshipOracleTests.cs:229-262`; `SharpProof.Analyzer.Test/RuntimeRequiresOracleTests.cs:116-149`; `SharpProof.Effects.Test/RuntimeEffectOracleTests.cs:439-472` |

### Status (part one hundred twelve)

R571 is a pending test-infrastructure reduction candidate. Preserve collectible unloading and resolver detach in `finally`; the helper should not merge the distinct oracle behavior or hide failures from the callback.

## Second survey, part one hundred fourteen: R573 - incomplete baseline identity preflight

| R573 | **`Test-CompleteBaseline` computes a canonical invocation and discards it.** The parallel mutation driver calls `Get-SharpProofMutationBaselineInvocation` for every saved baseline row, but never compares the returned `Identity` with the row's persisted `invocation` field. The baseline writer does persist that field, and the child `Test-SharpProofTrustedMutations.ps1` later performs the real identity comparison, so the outer preflight adds only a non-empty-field check and defers a malformed or tampered identity failure until shard startup. Compare the saved identity in this preflight (or remove the unused result if this layer is intentionally only a shape check) and keep the child validation as the direct-entrypoint boundary. | `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:218-256`; `scripts/Test-SharpProofTrustedMutations.ps1:2492-2543,2640-2646` |

### Status (part one hundred fourteen)

R573 is a pending release/evidence-pipeline reduction and validation candidate. Preserve the child-side check; the outer check should either validate the field it reads or stop constructing an unused identity object.

## Second survey, part one hundred fifteen: R574 - repeated mutation-baseline parsing

| R574 | **`Invoke-SharpProofTrustedMutationsParallel` reparses its baseline JSON across one decision path.** `Test-CompleteBaseline` reads and converts `baseline.json` to decide whether it is reusable; after that decision the script reads and converts the same file again to obtain the timing and test-count fields. When a fresh baseline is generated, the post-write `Test-CompleteBaseline` call adds a third full read/parse before the unconditional read at the timing projection. Returning the validated object from the helper, or caching the post-write result while preserving the second validation boundary, can remove redundant disk I/O and JSON parsing without weakening stale-baseline rejection. | `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:218-281` |

### Status (part one hundred fifteen)

R574 is a pending mutation-pipeline reduction candidate. Preserve validation after baseline generation; only reuse the already-parsed, successfully validated document for the subsequent timing projection.

## Second survey, part one hundred sixteen: R575 - repeated mutation-shard validation

| R575 | **`Invoke-SharpProofTrustedMutationsParallel` reparses and revalidates each shard before consuming it.** `Test-CompleteShard` reads the shard and all referenced TRX evidence; a reused shard is then read and converted to JSON again for `New-ShardTiming`, a newly finished shard is validated and immediately read again for timing, and the final aggregation validates every shard once more before reading it again to collect mutations. A helper that returns the validated evidence object (or a per-shard cache keyed by the immutable receipt path) can preserve the post-process and final catalog-coverage checks while removing repeated JSON reads and TRX parsing. | `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:118-213,318-326,382-397,411-425` |

### Status (part one hundred sixteen)

R575 is a pending mutation-pipeline reduction candidate. Keep the final catalog-count and uniqueness assertions and the validation boundary after each child process; only avoid reparsing evidence that has already passed the same checks.

## Second survey, part one hundred seventeen: R576 - duplicate verification-target command construction

| R576 | **`WorkerMsBuildIntegrationTests.ConsumerProject` duplicates the full verification-target process setup.** `RunVerificationTargetAsync` and `RunVerificationTargetWithInvocationIdAsync` each build the same `dotnet msbuild /t:_SharpProofVerifyCore` argument list, including the request/result/cache paths and fixed configuration properties; the only difference is that one creates a new GUID while the other receives an explicit invocation ID. A single core helper or overload can own the command construction and let the generated-ID overload delegate to it, preserving the tests that intentionally reuse an invocation ID while removing a second copy of the command contract. | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:4079-4137` |

### Status (part one hundred seventeen)

R576 is applied: generated-ID and explicit-ID verification-target helpers now
delegate to one argument builder, preserving per-call paths and explicit
invocation IDs. The package integration suite passes with its expected
unsupported-host skip.

## Second survey, part one hundred eighteen: R577 - duplicated package-project topology

| R577 | **The package-project manifest is checked against repeated hard-coded copies of the same topology.** `scripts/package-projects.json` is read as the package-project authority, but `PackagedProductFeed.ReadPackageProjects` separately embeds the three product project paths and `eng/acceptance/Verify.ps1` embeds the same ordered list before checking the manifest and each file. Adding, removing, or reordering a product therefore requires updating the manifest and multiple validators; a shared contract reader or one generated/consumed expected list can retain the explicit count/order and existence checks without maintaining identical path literals in each consumer. | `scripts/package-projects.json:1-8`; `SharpProof.Package.Test/PackagedProductFeed.cs:291-326`; `eng/acceptance/Verify.ps1:529-543` |

### Status (part one hundred eighteen)

R577 is a pending package-topology reduction candidate. Keep an explicit policy check that the manifest contains exactly the supported products and remains in dependency order; centralize only the repeated project-path vocabulary.

## Second survey, part one hundred nineteen: R578 - duplicate IL-candidate normalization

| R578 | **`CompilerImplementationIlSummaryLowerer.TryBuild` normalizes its method candidate twice.** The method is first passed through `SemanticClaimIdentity.NormalizeCandidate(...).OriginalDefinition` at the start of `TryBuild`, then `IsCandidate` repeats the same normalization before checking method kind, staticness, genericity, assembly, parameters, and return type. An already-normalized predicate or a single normalization boundary can remove that repeated symbol traversal while preserving `IsCandidate`'s standalone-call behavior. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs:116-127,140-157` |

### Status (part one hundred nineteen)

R578 remains deferred. The normalized-predicate extraction passed the focused
implementation-IL tests, but added seven measured expression nodes to a
coordinator layer already at its 4,544-node ceiling. Keeping the acceptance
ratchet intact would require moving the candidate policy across a trusted-file
boundary, which is more structural complexity than this small runtime saving.

## Second survey, part one hundred twenty: R579 - duplicated effect-authority projection

| R579 | **`CompilerManifestArtifactProducer.Create` duplicates effect-claim projection across its two callable-construction branches.** When compiler diagnostics exist and when normal lowering succeeds, each branch copies `item.EffectClaims` into `EffectClaims` and iterates the same claims to call `CompilerEffectAuthority.BindSourceTree` and populate `EffectAuthorities`. Only the callable failure reason/body differs. Constructing the branch-specific artifact first and applying one shared effect-evidence attachment helper would remove the parallel projection and keep source-tree binding behavior consistent. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs:31-75` |

### Status (part one hundred twenty)

R579 is applied: both callable-construction branches now attach effect evidence
and bind source-tree authorities through one helper. Their diagnostic-failure
versus lowering artifacts remain separate, including their distinct failure
metadata.

## Second survey, part one hundred twenty-one: R580 - discarded specification-pack evidence

| R580 | **`CompilerSpecificationPackProvider` validates pack-level evidence and then drops it.** `ParsePack` requires the catalog's `evidence` property, but `PackDefinition` stores only `Id`, `Version`, and `Methods`; no provider or artifact path reads the evidence string afterward. The catalog-wide SHA still authenticates the bytes, so changing the field changes the authority digest, but the field contributes no surfaced provenance while imposing a schema and parser obligation. Either carry the evidence into the emitted specification-pack authority/provenance or remove the unused field and validation if it is only stale documentation. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:417-431,799-804`; `SharpProof.Specs/RelationalSpecPackCatalog.json:7-9` |

### Status (part one hundred twenty-one)

R580 is a pending specification-pack schema reduction candidate. Preserve catalog digest binding and method evidence identity; only remove or surface the currently discarded pack-level evidence value.

## Second survey, part one hundred twenty-two: R581 - nullable summary-evidence validation defect

| R581 | **`CompilationFingerprint.ValidSummaryEvidence` can throw while validating malformed JSON.** For non-specification-pack rows, the final condition reads `row.EvidenceIdentity.Length` without first rejecting a null identity. JSON deserialization can populate a non-nullable string property with null; `CompilerManifestArtifactJson` then reaches this envelope check before the nearby `ValidSummaryEvidenceRow` null-shape guard, so a malformed non-pack summary row can escape as `NullReferenceException` instead of being rejected as invalid evidence. Add a null-safe shape check before reading the identity or route both paths through the existing row validator. | `SharpProof.CompilerArtifact/CompilationFingerprint.cs:110-142,153-171`; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:494-514` |

### Status (part one hundred twenty-two)

R581 is fixed: non-pack summary evidence now rejects a null identity before
checking its length, preserving the stricter authority-mode checks and canonical
summary ordering. The focused compiler-manifest suite passes.

## Second survey, part one hundred twenty-three: R582 - duplicate proof-core traversal

| R582 | **`CallableClaimResultAssembler.FromOutcome` walks a proven core twice.** The first loop resolves every `ProofJustification` through `assumptionLabels` and builds the sorted proof-core labels, then a second loop over the same `proven.Core` collects user-assumption IDs. The second pass can be folded into the successful first pass (after the label lookup succeeds), preserving the malformed-evidence short-circuit and the separate label/assumption projections while removing a repeated traversal of each backend proof core. | `SharpProof.Worker/CallableClaimResultAssembler.cs:17-45` |

### Status (part one hundred twenty-three)

R582 is applied: proven-core labels and user-assumption IDs are collected in one
validated traversal; malformed evidence clears the provisional IDs before the
failure result is returned, preserving the prior fail-closed behavior.

## Second survey, part one hundred twenty-four: R583 - unreachable contradictory-vacuity branches

| R583 | **`CallableVerifier.VerifyPostconditionsAsync` retains contradictory-entry branches after returning for that state.** The method handles `entryFeasibility.IsContradictory` at lines 103-109 by returning `ContradictoryPostconditions`; therefore the later `!entryFeasibility.IsContradictory` condition around the normal-completion probe and the `ContradictoryPreconditions` arm in the per-record vacuity selection can never be reached in this method. Removing those dead alternatives simplifies the state machine while leaving the dedicated contradictory-postcondition path intact. | `SharpProof.Worker/CallableVerifier.cs:103-109,153-155,266-268` |

### Status (part one hundred twenty-four)

R583 is applied: once contradictory entry feasibility returns the dedicated
postcondition result, the normal path no longer carries contradictory-vacuity
branches or their proof/assumption plumbing. The full Worker suite passes.

## Second survey, part one hundred twenty-five: R584 - repeated test SHA-256 formatting

| R584 | **Worker and package test fixtures reimplement the lowercase SHA-256 formatter.** `WorkerTests.TestProject.CreateRequest`, `ScalarDifferentialMatrixTests`, `WorkerTcbEdgeCaseTests`, and two `WorkerMsBuildIntegrationTests` sites each call `SHA256.HashData` and concatenate per-byte `ToString("x2", CultureInfo.InvariantCulture)` results, even though the referenced Worker.Protocol assembly exposes `WorkerProtocolJson.ComputeSha256` for the same canonical wire representation. A shared test helper or direct use of that formatter can remove five copies of byte-to-hex conversion and keep fixture hashes aligned with the protocol's lowercase policy; retain independent hashing only where a test intentionally exercises a different case or malformed digest. | `SharpProof.Worker.Test/WorkerTests.cs:7260-7269`; `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:917-926`; `SharpProof.Worker.Test/WorkerTcbEdgeCaseTests.cs:1440-1444`; `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:443-446,3408-3410` |

### Status (part one hundred twenty-five)

R584 is applied: valid fixture hashes now use `WorkerProtocolJson.ComputeSha256`,
while intentionally synthetic, uppercase, and malformed hash values remain
independent. Worker and package integration suites pass.

## Second survey, part one hundred twenty-six: R585 - repeated throwing backend fixture

| R585 | **Three Worker test fixtures duplicate the same unexpected-backend fake.** `AcyclicBlockPredicateExecutorTests`, `CompilerCallableLowererTests`, and `WorkerTcbEdgeCaseTests` each define an `UnexpectedBackend` with an interlocked call counter, a `CallCount` accessor, and a `CheckAsync` implementation that increments the counter and throws an assertion exception; only the message differs. A parameterized shared `ThrowingBackend` test helper can retain the per-test diagnostic and call-count assertions while removing three copies of the backend plumbing. | `SharpProof.Worker.Test/AcyclicBlockPredicateExecutorTests.cs:861-872`; `SharpProof.Worker.Test/CompilerCallableLowererTests.cs:692-706`; `SharpProof.Worker.Test/WorkerTcbEdgeCaseTests.cs:1779-1793` |

### Status (part one hundred twenty-six)

R585 is a pending Worker test-infrastructure reduction candidate. Keep the message and counter behavior configurable so tests still prove that malformed or unsupported inputs never reach the backend.

## Second survey, part one hundred twenty-seven: R586 - duplicate compiler-artifact factory setup

| R586 | **`CompilerManifestArtifactTests` rebuilds the same compilation-to-artifact pipeline in three helpers.** `CreateArtifact`, `CreateContractArtifact`, and `CreateFeatureArtifact` each create a C# compilation, run `ClaimManifestBuilder`, and call `CompilerManifestArtifactProducer.Create` with the same work directory, target framework, cancellation token, and default expression-depth budget; the latter two also repeat the feature-set plumbing. A single parameterized factory can own compilation, discovery, and producer construction while the small wrappers retain their distinct source defaults, contract-reference choice, feature set, and optional specification-pack argument. | `SharpProof.Worker.Test/CompilerManifestArtifactTests.cs:2538-2618` |

### Status (part one hundred twenty-seven)

R586 is a pending Worker test-infrastructure reduction candidate. Preserve the separate malformed-capture and feature-selection scenarios; centralize only the repeated artifact-construction pipeline.

## Second survey, part one hundred twenty-eight: R587 - duplicate bounded postcondition replay engines

| R587 | **The compiler response authority and worker replayer implement the same bounded postcondition execution engine.** `CompilerResponseEvidenceAuthority.TryReplayPostcondition` and `CallableCounterexampleReplayer.Replay` both reconstruct a model, bind parameter variables, execute a prepared program under its instruction bound, project the return value, restore pre-state variables, enforce source integer intervals, and evaluate an `ensures` condition. They intentionally differ at the edges - the authority returns a fail-closed boolean and resolves the target clause by claim ID, while the worker reports `CounterexampleNotReplayable` for unsupported spec/summary calls and exposes reason-specific outcomes - but the shared state-transition core is still maintained twice. A lower-layer replay result/core can own the common execution and validation mechanics while adapters retain those distinct error and claim-selection policies. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:670-818`; `SharpProof.Worker/CallableCounterexampleReplayer.cs:7-125` |

### Status (part one hundred twenty-eight)

R587 is a pending cross-layer replay reduction candidate. Preserve the compiler artifact assembly's dependency direction and each caller's fail-closed/reason-specific boundary; share only the common model, program, state, domain, and condition replay mechanics.

## Second survey, part one hundred twenty-nine: R588 - unreachable opaque receiver branch

| R588 | **`IrTermServices.ValidateCallShape` contains a contradictory null-receiver branch for opaque calls.** When an instance member has `receiver == null`, the `opaque` path first calls `ArgumentNullGuard.NotNull(receiver, ...)`, which always throws; the following `ArgumentException` is therefore unreachable for opaque calls. Removing the nested throw or separating the intended exception policy makes the validation flow explicit without changing the static-member and non-opaque call checks. | `SharpProof.Ir/IrTermServices.cs:25-37` |

### Status (part one hundred twenty-nine)

R588 is a pending IR validation reduction candidate. Preserve whichever exception type and message the public/internal callers intentionally expose; simplify only the branch whose second throw cannot execute after the null guard.

## Second survey, part one hundred thirty: R589 - duplicate program condition evaluation

| R589 | **`IrProgramInterpreter.Execute` repeats the same condition-evaluation scaffold for assumptions/assertions and branches.** Both paths evaluate an IR condition, propagate a non-value result through `FromEvaluation`, and reject a non-boolean runtime value with `InvalidCondition`; only the final action differs (stop on a false assume/assert versus select the next block). A helper that returns the validated boolean or its evaluation failure can own this shared behavior while preserving the two distinct execution statuses and control-transfer paths. | `SharpProof.Ir/IrProgramInterpreter.cs:70-105,253-265` |

### Status (part one hundred thirty)

R589 is a pending IR interpreter reduction candidate. Keep instruction-specific status and step/instruction reporting at the call sites; centralize only evaluation, failure propagation, and boolean validation.

## Second survey, part one hundred thirty-one: R590 - duplicate sequence-type lookup

| R590 | **`IrFactory.GetOrCreateSequenceType(IrTypeId)` looks up the same type key twice.** The overload first probes `_typeIds` with `(IrTypeKind.Sequence, -1, elementType.Value)` so it can avoid building a display name for an existing type, then calls `GetOrCreateTypeCore`, which recomputes that exact key and probes the same dictionary again. A lazy-name or sequence-specific core path can retain the allocation avoidance while making one routine own the lookup and insertion. | `SharpProof.Ir/IrFactory.cs:153-165,723-735` |

### Status (part one hundred thirty-one)

R590 is a pending IR factory reduction candidate. Preserve the existing interning key and the optimization of not materializing a display name for an already-interned element type; remove only the duplicate dictionary probe.

## Second survey, part one hundred thirty-two: R591 - recursive differential-oracle traversal

| R591 | **`IrCSharpDifferentialOracle.TryCollectTerms` recursively traverses arbitrary IR terms even though the IR layer provides an explicit-stack traversal seam.** Its visited-set walk calls itself for every child before appending the postorder term list, so a deeply nested or adversarial term can overflow the test process before the differential comparison reports a mismatch; the production interpreter and substitution code explicitly avoid this failure mode. An explicit-stack postorder walk, or a small extension of `IrTraversal.FoldBottomUp` that carries the oracle's validation and variable collection, can preserve declaration order and early-abstention reasons without maintaining a second recursive traversal. | `SharpProof.Testing/IrCSharpDifferentialOracle.cs:201-247`; `SharpProof.Ir/IrTraversal.cs:55-96` |

### Status (part one hundred thirty-two)

R591 is a pending differential-testing reduction candidate. Preserve the current fail-fast checks for unsupported terms and missing/ill-typed variables, and preserve child-before-parent declaration order; remove only the unbounded recursive walk.

## Second survey, part one hundred thirty-three: R592 - duplicate differential type projections

| R592 | **`IrCSharpDifferentialOracle` maintains two copies of the executable IR type policy.** `TryGetCSharpType` maps boolean, integer, string, the factory's object type, and recursively supported sequences to source-language names, while `TryGetRuntimeType` repeats the same kind checks and recursive sequence admission to produce `System.Type` values. The projections have different return types but share the supported-type boundary, so a canonical type-shape/helper can own admission and recursion while the two callers retain their string-versus-runtime representations. | `SharpProof.Testing/IrCSharpDifferentialOracle.cs:349-365,451-470` |

### Status (part one hundred thirty-three)

R592 is a pending differential-testing reduction candidate. Keep the factory-specific object-type check and recursive array behavior identical; centralize only the shared supported-type policy, not the final projection format.

## Second survey, part one hundred thirty-four: R593 - repeated generator sequence interning

| R593 | **`WellSortedIrGenerator` interns the same integer-sequence type twice during field initialization.** `_integerSequence` already stores `factory.GetOrCreateSequenceType(factory.IntegerType)`, but `_values` immediately invokes the same factory call again instead of using `_integerSequence`; the factory's interning makes the second call return the same ID after repeating lookup work. Reusing the earlier field removes the redundant call and makes the shared variable/value type relationship explicit. | `SharpProof.Testing/WellSortedIrGenerator.cs:60-63` |

### Status (part one hundred thirty-four)

R593 remains deferred: the direct field reuse is rejected by C# because primary-
constructor field initializers cannot reference another instance field. Avoiding
the second intern request would require reshaping initialization around a tuple
or explicit constructor solely for one lookup, which adds more complexity than
it removes; the original identity-preserving behavior is retained.

## Second survey, part one hundred thirty-five: R594 - duplicate unsupported-mutation result path

| R594 | **`RoslynProgramLowerer.LowerValue` repeats the unsupported-mutation result construction.** The `IIncrementOrDecrementOperation` and `ICompoundAssignmentOperation` arms each call `LowerUnsupportedMutation` and then create/return the same `CreateHavocTemporary(..., "mutation-result", GetTypeId(mutation.Type))`; only the compound arm additionally passes its value operation for evaluation. A shared mutation-result helper or a combined admission path can preserve that side-effect distinction while removing the duplicated unknown-result plumbing. | `SharpProof.Frontend/RoslynProgramLowerer.cs:289-302` |

### Status (part one hundred thirty-five)

R594 remains deferred: the shared mutation-result helper preserved behavior but
raised `RoslynProgramLowerer` above its architecture expression-node ceiling
(2,341 versus 2,329). Retaining the current ceiling is more valuable than
removing this small duplicated result construction, so the original paths stay
in place.

## Second survey, part one hundred thirty-six: R595 - unbounded nested-operation recursion

| R595 | **`RoslynProgramLowerer.LowerNestedOperations` recursively walks Roslyn operation trees without the lowerer's depth/stack guard.** The main expression lowerer caps recursive visitor depth at 256, but this preliminary scan follows every child with a direct recursive call and can overflow before that cap is reached when a deeply nested expression is lowered in a program. An explicit stack with a bounded spend/depth policy can retain its intentional stop points for invocations, array elements, anonymous/local functions, and `nameof` while making this prepass safe and consistent with the guarded lowering path. | `SharpProof.Frontend/RoslynProgramLowerer.cs:352-382,471-503` |

### Status (part one hundred thirty-six)

R595 is a pending Frontend program-lowering robustness reduction candidate. Preserve the prepass's side-effect ordering and stop-at-boundary rules; replace only the unbounded recursive traversal.

## Second survey, part one hundred thirty-seven: R596 - duplicate IR shrinker child materialization

| R596 | **`IrStructuralShrinker.GetCandidates` materializes the same child list twice.** The method first calls `Children(term)` to offer direct child candidates, then calls `Children(term)` again before recursively shrinking each child; `Children` constructs a fresh immutable array for every IR node. Holding one local child array for both passes preserves candidate order and the complete child set while removing one allocation and traversal of the node-shape switch per shrinker step. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:500-522` |

### Status (part one hundred thirty-seven)

R596 is a pending fuzz-shrinker reduction candidate. Preserve the direct-child candidates before recursively rebuilt candidates; reuse only the already-materialized child array.

## Second survey, part one hundred thirty-eight: R597 - repeated IR shrinker size walks

| R597 | **`IrStructuralShrinker.MinimizeAsync` and `GetCandidates` repeatedly compute the same structural sizes.** Each minimization pass calculates `StructuralSize(current)`, `GetCandidates` recalculates `StructuralSize(term)`, and every candidate is sized once inside `Add` and again by the outer `StructuralSize(candidate) >= currentSize` filter. Because `StructuralSize` walks the whole shared IR DAG, threading the current/original and candidate sizes through the shrinker removes repeated whole-term traversals without changing the strictly-decreasing acceptance rule. | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:437-443,480-490` |

### Status (part one hundred thirty-eight)

R597 is a pending fuzz-shrinker performance reduction candidate. Retain the existing size comparison and candidate de-duplication; avoid only recomputing sizes already known to the same shrink iteration.

## Second survey, part one hundred thirty-nine: R598 - duplicated frontend fuzz environment binding

| R598 | **`FrontendDifferentialOracle` duplicates the lowering-variable binding loop for ordinary and semantic-edge fuzz cases.** `CreateEnvironment` and `CreateSemanticEdgeEnvironment` both enumerate `lowering.Variables`, retain only parameters belonging to the generated method, and add an IR-variable binding; they then diverge only in converting fixed `GeneratedCSharpCase` slots versus arbitrary semantic-edge arguments. A shared parameter-binding iterator or callback can own the symbol/ordinal admission and leave those two value-conversion policies separate, removing duplicated Roslyn-symbol traversal and insertion scaffolding. | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1215-1259,1364-1393` |

### Status (part one hundred thirty-nine)

R598 is a pending frontend-fuzzing infrastructure reduction candidate. Preserve the generated-case slot mapping, arbitrary argument conversion, sequence-origin tracking, and parameter-containing-symbol check; centralize only the common binding traversal.

## Second survey, part one hundred forty: R599 - duplicate architecture-test relative-path helper

| R599 | **`ArchitectureTests` and `BoundaryEnforcementTests` define the same private `Relative` helper.** Both methods call `Path.GetRelativePath(TestRepository.FindRoot(), path)` and normalize backslashes to `/` with identical code. Moving this repository-relative formatting helper into shared architecture-test infrastructure would remove the exact duplicate while preserving the test paths' current root and separator semantics. | `SharpProof.ArchitectureTest/ArchitectureTests.cs:2261-2264; SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:494-497` |

### Status (part one hundred forty)

R599 is applied: `ArchitectureTests` and `BoundaryEnforcementTests` now use the
shared `TestRepository.Relative` helper, preserving repository-root lookup and
slash normalization without local copies.

## Second survey, part one hundred forty-one: R600 - duplicate lifted-nullable operation lookup

| R600 | **`LiftedNullableConversionRegressionTests` and `LiftedNullableOperatorRegressionTests` define the same private `Operation` helper.** Both select the method's single declaring syntax, obtain its semantic model, call `GetOperation`, and throw the same diagnostic when no operation is found. A shared Effects test-host helper can own this Roslyn lookup while the two suites retain their separate conversion/operator assertions and fixtures. | `SharpProof.Effects.Test/LiftedNullableConversionRegressionTests.cs:102-113; SharpProof.Effects.Test/LiftedNullableOperatorRegressionTests.cs:116-127` |

### Status (part one hundred forty-one)

R600 is applied with R601: all affected suites now use the existing
`EffectTestHost.RootOperation` lookup, preserving single-declaration selection,
semantic-model binding, and the existing failure message.

## Second survey, part one hundred forty-two: R601 - third copy of the Effects operation lookup

| R601 | **The exact operation lookup recorded in R600 has a third copy.** `ReducedRefExtensionFlowRegressionTests.Operation` repeats the same declaring-syntax selection, semantic-model `GetOperation`, and missing-operation exception already present in the two lifted-nullable suites. Treating R600 as a three-suite shared helper opportunity removes the remaining duplicate without coupling the suites' distinct fixtures or assertions. | `SharpProof.Effects.Test/ReducedRefExtensionFlowRegressionTests.cs:62-73; R600` |

### Status (part one hundred forty-two)

R601 is applied with R600; the three suites retain their distinct fixtures and
assertions while sharing the byte-identical operation lookup.

## Second survey, part one hundred forty-three: R602 - duplicated Worker compiler-test setup

| R602 | **The Worker compiler-focused tests repeat the same Roslyn compilation harness.** `CompilerCallableLowererTests.CreateCompilation` and `CompilerRelationalSummaryProviderTests.CreateCompilation` each construct C# 12 parse options with `Contract.ConditionalSymbol`, use `TestMetadataReferences.WithSharpProof`, enable nullable DLL compilation, collect error diagnostics, and assert an empty error set. `CompilerCallableLowererWaveSixRegressionTests.CreateCompilation` carries the same setup again, adding discovery and a distinct subject path. A shared compiler-test factory parameterized by assembly/source identity and optional discovery can remove the repeated setup while preserving each suite's compilation identity and return shape. | `SharpProof.Worker.Test/CompilerCallableLowererTests.cs:669-690`; `SharpProof.Worker.Test/CompilerRelationalSummaryProviderTests.cs:187-209`; `SharpProof.Worker.Test/CompilerCallableLowererWaveSixRegressionTests.cs:139-172` |

### Status (part one hundred forty-three)

R602 is a pending Worker-test harness reduction candidate. Preserve the contract preprocessor symbol, nullable context, metadata-reference set, diagnostic assertion, and suite-specific assembly/subject names; share only the common compilation setup.

## Second survey, part one hundred forty-four: R603 - duplicated frontend expression-operation recovery

| R603 | **`FrontendLoweringTests` and `UnaryAndDefaultLoweringCoverageTests` duplicate the same Roslyn expression-operation recovery.** Both call `SemanticModel.GetOperation`, retry through `CheckedExpressionSyntax` and `ParenthesizedExpressionSyntax` when Roslyn returns no operation, and fail for any other expression shape. The only meaningful difference is the fallback message (generic in one suite and including `expression.Kind()` in the other). A shared frontend-test helper with a caller-provided failure-message factory can remove the duplicate recursion without changing either suite's diagnostics or lowering assertions. | `SharpProof.Frontend.Test/FrontendLoweringTests.cs:1351-1369`; `SharpProof.Frontend.Test/UnaryAndDefaultLoweringCoverageTests.cs:257-279` |

### Status (part one hundred forty-four)

R603 is a pending Frontend-test harness reduction candidate. Preserve the two wrapper-unwrapping cases and each suite's error detail; share only the common operation lookup and recovery.

## Second survey, part one hundred forty-five: R604 - duplicate package-test XML escaping

| R604 | **Two package-test helpers duplicate the same XML escaping and failure guard.** `FinalCompilationProbeTests.Escape` and `IsolatedPackageFeedConfiguration.Escape` both return `SecurityElement.Escape(value)` or throw an `InvalidOperationException` when escaping unexpectedly returns null; only the context-specific message differs. A shared `EscapeOrThrow` test utility accepting the message can remove the duplicate while retaining useful diagnostics at both call sites. | `SharpProof.Package.Test/FinalCompilationProbeTests.cs:1004-1009`; `SharpProof.Package.Test/IsolatedPackageFeedConfiguration.cs:67-72` |

### Status (part one hundred forty-five)

R604 is a pending package-test maintenance candidate. Preserve XML escaping and each contextual exception message; share only the common null-result guard.

## Second survey, part one hundred forty-six: R605 - duplicate packaged-container test guard

| R605 | **Two package-integration suites duplicate the canonical-container admission guard.** `PackageLayoutSmokeTests.RequireContainerWorker` and `WorkerMsBuildIntegrationTests.RequireContainerWorker` both require Linux, process and OS architecture `X64`, and `SHARPPROOF_CONTAINER=1`, then issue the same `Assert.Ignore` message. The Worker MSBuild suite additionally calls `ContainerContract.ValidateRequired()` after admission. A shared test helper can own the platform/marker predicate while leaving that extra contract validation at its existing boundary. | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:1565-1580`; `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:3451-3467` |

### Status (part one hundred forty-six)

R605 is a pending package-test infrastructure reduction candidate. Preserve all three platform checks, the environment marker, and the Worker MSBuild suite's post-guard contract validation; share only the common admission logic and message.

## Second survey, part one hundred forty-seven: R606 - repeated schema-file loader

| R606 | **Three schema-conformance suites duplicate the same schema-file loader.** `IrModelSchemaTests.ReadSchema`, `ProtocolModelSchemaTests.ReadSchema`, and `CompilerArtifactModelSchemaTests.ReadSchema` all resolve `TestRepository.FindRoot()`, combine it with a project directory and schema filename, read the entire UTF-8 text file, and parse it into a `JsonDocument`; only the project/schema path pair differs. A parameterized test utility can centralize the path and parse boilerplate while keeping each suite's schema-specific assertions and disposal boundary unchanged. | `SharpProof.Ir.Test/IrModelSchemaTests.cs:472-478`; `SharpProof.Worker.Test/ProtocolModelSchemaTests.cs:645-651`; `SharpProof.Worker.Test/CompilerArtifactModelSchemaTests.cs:924-930` |

### Status (part one hundred forty-seven)

R606 is a pending cross-assembly test utility reduction candidate. Preserve each schema's explicit project/file identity and the existing `JsonDocument` disposal at every call site; share only root resolution, path composition, file loading, and parsing.

## Second survey, part one hundred forty-eight: R607 - reuse the canonical Effects operation loader

| R607 | **The three duplicated Effects regression helpers have an existing canonical implementation.** `LiftedNullableConversionRegressionTests.Operation`, `LiftedNullableOperatorRegressionTests.Operation`, and `ReducedRefExtensionFlowRegressionTests.Operation` duplicate the same declaring-syntax/semantic-model/`GetOperation` lookup that `EffectTestHost.RootOperation` already provides for other suites. The reduction can therefore be a direct reuse of the shared host helper rather than a new fourth helper; the regression suites keep their distinct source fixtures and assertions. | `SharpProof.Effects.Test/EffectTestHost.cs:225-235`; `SharpProof.Effects.Test/LiftedNullableConversionRegressionTests.cs:102-113`; `SharpProof.Effects.Test/LiftedNullableOperatorRegressionTests.cs:116-127`; `SharpProof.Effects.Test/ReducedRefExtensionFlowRegressionTests.cs:62-73`; R600-R601 |

### Status (part one hundred forty-eight)

R607 refines R600-R601. Preserve the existing missing-operation exception and all suite-specific assertions; replace only the three private copies with calls to `EffectTestHost.RootOperation`.

## Second survey, part one hundred forty-nine: R608 - expanded Worker compiler-test harness overlap

| R608 | **R602's Worker compiler-test harness duplication spans five suites, not three.** `ClaimManifestBuilderTests.GetCompilation` and `CompilerRuntimeSymbolArtifactTests.CreateArtifact` repeat the same C# 12 parse options with `Contract.ConditionalSymbol`, `TestMetadataReferences.WithSharpProof`, nullable-enabled DLL compilation, error-diagnostic collection, and empty-error assertion already identified in `CompilerCallableLowererTests`, `CompilerRelationalSummaryProviderTests`, and the wave-six regression suite. The two added callers vary in multi-tree/output-kind support or the subsequent discovery/artifact pipeline, but those seams can remain parameters around one shared compilation-and-diagnostic helper. | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:2600-2633`; `SharpProof.Worker.Test/CompilerRuntimeSymbolArtifactTests.cs:103-128`; R602 |

### Status (part one hundred forty-nine)

R608 refines R602 to the full five-suite overlap. Preserve each caller's assembly/file identity, source cardinality, output kind, discovery, and artifact construction; share only the common parse/reference/options/error-validation setup.

## Second survey, part one hundred fifty: R609 - repeated Effects subject-method lookup

| R609 | **Three Effects regression suites manually repeat a helper that already exists.** `ConstructorRuntimeOrderRegressionTests`, `ReducedExtensionReceiverCompletionTests`, and `UsingInitializerUnwindRegressionTests` each resolve `Subject`, call `GetMembers("Exercise")`, filter to `IMethodSymbol`, and select the single result. `EffectTestHost.RequireMethod(compilation, typeMetadataName, methodName)` already centralizes the type/member lookup and additionally enforces an ordinary-method kind. Replacing the three local chains with that existing helper removes repeated Roslyn symbol plumbing while leaving each analysis and assertion sequence unchanged. | `SharpProof.Effects.Test/ConstructorRuntimeOrderRegressionTests.cs:37-40`; `SharpProof.Effects.Test/ReducedExtensionReceiverCompletionTests.cs:33-36`; `SharpProof.Effects.Test/UsingInitializerUnwindRegressionTests.cs:60-63`; `SharpProof.Effects.Test/EffectTestHost.cs:133-149` |

### Status (part one hundred fifty)

R609 is applied: the three regression suites now use
`EffectTestHost.RequireMethod(compilation, "Subject", "Exercise")`, preserving
the subject/method identity and ordinary-method invariant without repeated
symbol-plumbing chains.

## Second survey, part one hundred fifty-one: R610 - residual generator JSON uniqueness validator

| R610 | **Two generators carry the same recursive JSON property-uniqueness validator.** `Generate-LauncherArguments.ps1` and `Generate-ContractApiCatalog.ps1` both recurse through arrays and objects, maintain an ordinal `HashSet` of `JsonElement` property names, throw on duplicate names, and recurse into each property value. The applied generator-validator consolidation does not cover this residual `JsonElement` walk, whose only differences are parameter-binding syntax and named invocation style. Moving it into `GeneratedFileHelpers.ps1` would remove the duplicate recursive guard while leaving each generator's schema-specific `Assert-Properties`, choice, and type validation local. | `scripts/Generate-LauncherArguments.ps1:21-45`; `scripts/Generate-ContractApiCatalog.ps1:28-55`; `scripts/GeneratedFileHelpers.ps1` |

### Status (part one hundred fifty-one)

R610 is applied: `Assert-UniqueJsonProperties` now lives in
`GeneratedFileHelpers.ps1` and both generators reuse it, preserving ordinal
duplicate detection, array/object recursion, and each generator's schema
validation.

## Second survey, part one hundred fifty-two: R611 - duplicate pattern type projection

| R611 | **`SwitchExpressionFacts` extracts the same matched type twice.** `IsTotalPattern` and `IsPatternEvaluationUnavoidable` each switch over `ITypePatternOperation`, `IDeclarationPatternOperation`, and `IRecursivePatternOperation` to obtain `MatchedType`, returning null for every other pattern kind. A private `GetMatchedType` helper can own this identical projection while leaving the two callers' distinct list-pattern, nullability, and recursive-subpattern policies unchanged. | `SharpProof.Effects/SwitchExpressionFacts.cs:365-372,434-441` |

### Status (part one hundred fifty-two)

R611 is applied: `IsTotalPattern` and `IsPatternEvaluationUnavoidable` now
share one private `GetMatchedType` projection, while retaining their separate
input-nullability and recursive-pattern decisions.

## Second survey, part one hundred fifty-three: R612 - repeated relational operator table

| R612 | **`SwitchExpressionFacts` implements the same relational operator table three times.** `MatchRelationalPattern` maps `LessThan`, `LessThanOrEqual`, `GreaterThan`, and `GreaterThanOrEqual` from a comparison result, while `TryMatchRelationalConstants` routes floating values through `MatchesFloating` and other comparable values through `Matches`, whose switch arms repeat that same four-way mapping. A single comparison-to-selection helper can remove the duplicated decision table; retain the current NaN rejection, exact-type admission, and `ArgumentException` fallback policies at their existing boundaries. | `SharpProof.Effects/SwitchExpressionFacts.cs:604-645,688-745` |

### Status (part one hundred fifty-three)

R612 is applied: relational patterns and floating-point constant matching now
share the comparison-result selector, while explicit NaN handling and the
unsupported-comparison boundary remain unchanged.

## Second survey, part one hundred fifty-four: R613 - repeated total-pattern query

| R613 | **`OperationCompletionEvaluator.CanCompletePatternEvaluation` recomputes the same total-pattern predicate.** In both the deconstruction-subpattern loop and the property-subpattern loop, it calls `SwitchExpressionFacts.IsTotalPattern` once in the incomplete-evaluation guard and immediately calls it again for the early-return test. The predicate is pure for the same pattern/type inputs, so caching one local boolean per subpattern removes four repeated pattern traversals without changing the distinction between a failing total pattern and a non-total pattern that can be skipped. | `SharpProof.Effects/OperationCompletionEvaluator.cs:233-267` |

### Status (part one hundred fifty-four)

R613 is applied: each recursive-pattern loop now evaluates its total-pattern
predicate once after the existing completion check and reuses the result,
preserving evaluation order while removing duplicate pattern traversals.

## Second survey, part one hundred fifty-five: R614 - unreachable operator-null disjunct

| R614 | **`OperationCompletionEvaluator.CanCompleteBinary` retains an unreachable null check.** The return at the end of the user-defined conditional-operator branch is guarded by `if (binary.OperatorMethod != null)`, yet still evaluates `binary.OperatorMethod == null || CanCompleteInvocation(...)`. The first disjunct can never be true in that branch, so removing it exposes the actual required invocation check and removes accidental state-machine complexity without changing the branch result. | `SharpProof.Effects/OperationCompletionEvaluator.cs:1301-1367` |

### Status (part one hundred fifty-five)

R614 is applied: the user-defined conditional-operator path now calls
`CanCompleteInvocation` directly after its existing non-null guard, preserving
the truth-operator short-circuit cases without the unreachable null disjunct.

## Second survey, part one hundred fifty-six: R615 - duplicate control-flow region iterator

| R615 | **`EffectMethodNodeBuilder` defines the same enclosing-region iterator twice.** `CreateFinallyEntries` and `CreateExceptionalRegionOperations` each declare a local `EnclosingRegions(ControlFlowRegion?)` iterator that yields the region and walks `EnclosingRegion` until null, with identical loop structure. A single private iterator can serve both region projections while retaining their separate finally/catch mapping and ordering logic. | `SharpProof.Effects/EffectMethodNodeBuilder.cs:962-1025,1028-1081` |

### Status (part one hundred fifty-six)

R615 is applied: `CreateFinallyEntries` and
`CreateExceptionalRegionOperations` now share one class-level
`EnclosingRegions` iterator, preserving the null-terminated walk and each
builder's distinct filtering and mapping.

## Second survey, part one hundred fifty-seven: R616 - reuse static-initializable member predicate

| R616 | **`EffectMethodNodeBuilder.AllStaticInitializersSatisfy` repeats an existing member-kind switch.** Its `initializable` local reproduces the field/property/event staticness and non-const-field checks that `IsInitializableMember(member, staticInitializers: true)` already owns below and that the other initializer paths already call. Reusing that helper removes a second member-classification authority while leaving the method's predicate evaluation, syntax-reference enumeration, and semantic-model handling unchanged. | `SharpProof.Effects/EffectMethodNodeBuilder.cs:414-447,676-688` |

### Status (part one hundred fifty-seven)

R616 is applied: `AllStaticInitializersSatisfy` now reuses
`IsInitializableMember(member, staticInitializers: true)`, preserving the
static-only/non-const policy without a duplicate classification switch.

## Second survey, part one hundred fifty-eight: R617 - duplicate walk-depth scope guard

| R617 | **`ManagedAbstractFlow` repeats its recursion-depth bookkeeping.** `Transfer` and `EvaluateCore` both check `_walkDepth` against `MaximumWalkDepth`, increment it, invoke a bounded body inside `try`, and decrement it in `finally`; only the overflow fallback differs (`state.Forget()` versus `ManagedAbstractValue.Unknown`). A small callback/fallback seam or shared scope helper can own the push/pop invariant while preserving those distinct fallback values and the separate transfer/evaluation bodies. | `SharpProof.Effects/ManagedAbstractFlow.cs:197-221,552-568` |

### Status (part one hundred fifty-eight)

R617 is a pending local Effects cleanup candidate. Preserve the fail-closed fallback chosen by each caller and the `finally` decrement; consolidate only the duplicated depth guard and scope bookkeeping.

## Second survey, part one hundred fifty-nine: R618 - duplicate Meta-analyzer test host

| R618 | **`SharpProofSoundnessAnalyzerTests` duplicates its analyzer-host setup.** `Analyze` and `AnalyzeGenerated` each create a C# 12 dynamic-library compilation, collect and reject compiler errors, attach `SharpProofSoundnessAnalyzer`, and return analyzer diagnostics. The generated variant changes only the assembly name and supplies `Generated.g.cs` as the syntax-tree path. A parameterized private host can preserve that path distinction while removing the parallel compilation and diagnostic plumbing. | `SharpProof.Meta.Analyzers.Test/SharpProofSoundnessAnalyzerTests.cs:3417-3452` |

### Status (part one hundred fifty-nine)

R618 is a pending test-infrastructure reduction candidate. Preserve the generated source path and assembly identity where they are semantically relevant; consolidate only the shared compilation, error assertion, and analyzer execution sequence.

## Second survey, part one hundred sixty: R619 - duplicate cancellation-filter prelude

| R619 | **`CancellationBoundaryAnalyzer` prepares the same catch-filter evaluation twice.** `FilterIncludesAllCancellation` and `FilterExcludesCancellation` both extract the filter expression, fast-path a constant result, resolve the declared catch local, obtain the operation, and call `EvaluateCancellationFilter`; they differ only in the interpretation of the outcome and the intentionally opposite no-filter/constant polarity. A shared evaluation-prelude helper can return the normalized filter outcome (with an explicit no-filter state) while the two callers retain their distinct inclusion and exclusion policies. | `SharpProof.Meta.Analyzers/CancellationBoundaryAnalyzer.cs:88-162` |

### Status (part one hundred sixty)

R619 is a pending Meta-analyzer cleanup candidate. Preserve the current fail-closed behavior for missing catch locals and unknown outcomes, and preserve the opposite semantics for absent, constant-true, and constant-false filters; consolidate only the shared binding/evaluation setup.

## Second survey, part one hundred sixty-one: R620 - duplicate local-write traversal

| R620 | **`CacheSoundnessRules` repeats the local-write traversal and state update.** `TransferLocalValues` and `GetExceptionalLocalValues` both enumerate `BlockOperations(block).SelectMany(InEvaluationOrder(...))`, extract `GetLocalWriteValue(candidate, local)`, and replace the reaching-value set with the newly written value; the exceptional variant additionally snapshots the state before operations that may throw. A shared local-write traversal or update callback can remove the repeated enumeration and replacement protocol while keeping exceptional snapshots and the two dataflow results separate. | `SharpProof.Meta.Analyzers/CacheSoundnessRules.cs:1120-1185` |

### Status (part one hundred sixty-one)

R620 is a pending Meta-analyzer dataflow cleanup candidate. Preserve evaluation order, cancellation checks, throw snapshots, and the distinct input/output sets; share only the common candidate enumeration and local-write replacement seam.

## Second survey, part one hundred sixty-two: R621 - duplicate artifact-authority test baseline

| R621 | **`WorkerTests` repeats the artifact-authority verification baseline.** Four mutation tests each create a temporary `TestProject`, build a request, run `SharpProofWorker.VerifyAsync`, create a `CompilerResponseEvidenceAuthority`, and assert that the unmutated response passes `WorkerProtocolJson.Validate`, including the formatted validation errors. Their fixture sources, backend fakes, and mutations remain intentionally different. A shared test helper that returns the validated response/authority pair can remove only this common setup and leave each authority-forgery assertion focused on its mutation. | `SharpProof.Worker.Test/WorkerTests.cs:6910-7101` |

### Status (part one hundred sixty-two)

R621 is a pending Worker test-harness reduction candidate. Preserve each test's source, backend, cache mode, authority construction, and post-baseline mutation; share only the repeated successful-run and validation prelude.

## Second survey, part one hundred sixty-three: R622 - duplicate durable corpus write

| R622 | **`CorpusFileTransaction` implements durable file writing twice.** `WriteDurablyAsync` creates a new write-through file, writes all bytes, and flushes to disk; `Restore` repeats the same create-new/write/flush sequence for each backup, differing mainly because recovery is synchronous and has no cancellation token. A shared low-level durable-write core with asynchronous and synchronous adapters can remove the duplicate filesystem protocol while preserving staging cancellation and recovery behavior. | `SharpProof.Gates/Corpus/CorpusFileTransaction.cs:179-224` |

### Status (part one hundred sixty-three)

R622 is a pending corpus-transaction cleanup candidate. Preserve create-new semantics, write-through durability, backup restoration, and the separate async cancellation boundary; share only the common byte-write/flush protocol.

## Second survey, part one hundred sixty-four: R623 - duplicate corpus observation orchestration

| R623 | **`CorpusGate` repeats the full corpus observation orchestration.** `RunAsync` loops over synthetic cases, checks cancellation, awaits `ObserveCaseAsync`, and then appends OSS observations; `RenderActualSnapshotAsync` repeats the same synthetic loop and OSS runner call before projecting observations to canonical lines. A shared `ObserveAllAsync`/collection helper can centralize case selection, cancellation, and runner invocation, leaving execution metrics and snapshot rendering as separate consumers. | `SharpProof.Gates/Corpus/CorpusGate.cs:63-75,364-377` |

### Status (part one hundred sixty-four)

R623 is a pending corpus-gate orchestration cleanup candidate. Preserve the exact synthetic/OSS ordering, cancellation checks, and snapshot line ordering; share only observation collection and keep result accounting separate from rendering.

## Second survey, part one hundred sixty-five: R624 - duplicate corpus source-ID validation

| R624 | **`OpenSourceCorpusCatalog` validates source IDs twice on the load path.** `Validate` first calls `ValidateSourceIds`, which rejects blank IDs and duplicates, and then calls `ValidateSource` for every source; `ValidateSource` repeats the blank-ID check even though it is private and only reached after the first pass. Remove the unreachable second check or make one helper own both the reusable validation and uniqueness policy, while retaining the public `ValidateSourceIds` test contract. | `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:126,138-141,275-293,322-330` |

### Status (part one hundred sixty-five)

R624 is applied: the private `ValidateSource` path now relies on the preceding
`ValidateSourceIds` pass for blank and duplicate IDs, while retaining all
per-source URL, commit, license, and containment checks.

## Second survey, part one hundred sixty-six: R625 - repeated corpus line-ending normalization

| R625 | **Corpus hashing repeatedly normalizes already-normalized text.** `OpenSourceCorpusCatalog.Validate` normalizes each file's content before parsing and then passes that normalized string to `ComputeSha256`, which normalizes it again; `OpenSourceCorpusImporter.DiscoverSourcesAsync` has the same pattern, and declaration extraction similarly returns normalized text before hashing through the normalizing helper. Split the normalized-input hashing path or move normalization to one boundary so each content/declaration is scanned once, while keeping manifest hashes canonical across CRLF and LF input. | `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:64-75,97-101,164-168,224-229`; `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:294-307,323-326` |

### Status (part one hundred sixty-six)

R625 is a pending corpus hashing cleanup candidate. Preserve the current normalized-byte hash values and CRLF/CR compatibility; share or specialize only the normalization boundary so callers do not rescan canonical text.

## Second survey, part one hundred sixty-seven: R626 - duplicate Effects test compilation factories

| R626 | **`EffectTestHost` repeats Roslyn compilation construction for reference variants.** `CreateCompilation`, `CreateCompilationWithoutContractPackage`, and `EmitReferenceWithoutContractPackage` each parse C# 12 source, create a deterministic release DLL compilation with nullable enabled, and enforce compilation success; they differ mainly in whether the contract assembly is included and in source/assembly naming. A private compilation core accepting the reference set and parse metadata can own the common options while preserving the explicit contract-present/contract-absent fixtures. | `SharpProof.Effects.Test/EffectTestHost.cs:11-41,50-69,71-90,168-179` |

### Status (part one hundred sixty-seven)

R626 is a pending Effects-test infrastructure cleanup candidate. Preserve the contract-package boundary, assembly/source names, C# 12 parsing, deterministic release options, and typed compile failures; centralize only the shared Roslyn construction.

## Second survey, part one hundred sixty-eight: R627 - repeated ManagedAbstractFlow test analysis setup

| R627 | **`ManagedAbstractFlowTests` repeats the same Roslyn-to-CFG analysis harness.** Many tests locate the single `Calls` method, obtain its semantic model, resolve the `IMethodBodyOperation` and `IMethodSymbol`, build a `ControlFlowGraph`, and call `ManagedAbstractFlow.ForCompilation(...).Analyze(...)`; `AnalyzeSingleCall` centralizes only one narrow case while most tests re-open the sequence. A parameterized test helper can return the compilation, method, root, graph, and analysis while allowing callers to select the operation/assertion they need. | `SharpProof.Effects.Test/ManagedAbstractFlowTests.cs:15-40,267-285,347-363,376-396,409-428,623-645,658-680,731-789,854-884,930-976,1011-1049`; `SharpProof.Effects.Test/ManagedAbstractFlowTests.cs:1053-1067` |

### Status (part one hundred sixty-eight)

R627 is a pending Effects-test harness cleanup candidate. Preserve each fixture's custom references, selected operation, entry state, convergence/budget parameters, and assertion-specific analysis; share only the repeated method/operation/CFG plumbing.

## Second survey, part one hundred sixty-nine: R628 - duplicated bounded-stream overflow probes

| R628 | **`BoundedReadStream` maintains the same over-limit protocol twice.** `ProbeForOverflow` and `ProbeForOverflowAsync` both perform one extra read only after the byte budget is exhausted, throw the same limit exception when data remains, and return zero at end-of-stream; only the underlying sync/async read API differs. A small shared limit-result seam or a single-byte probe abstraction can centralize the policy while retaining synchronous behavior, cancellation, and allocation choices for each adapter. | `SharpProof.Worker.Protocol/BoundedReadStream.cs:46-49,70-76,79-91,134-157` |

### Status (part one hundred sixty-nine)

R628 is applied: synchronous and asynchronous exhausted-budget probes now
share `CompleteOverflowProbe`, preserving zero-count behavior, one-byte
overflow detection, limit diagnostics, cancellation, and disposal.

## Second survey, part one hundred seventy: R629 - redundant root JSON kind check

| R629 | **`WorkerProtocolJson.EnsureJsonShape` checks the root object kind twice.** The outer method rejects a non-object `RootElement` before calling `EnsureObjectShape`, and `EnsureObjectShape` immediately performs the same `ValueKind != Object` check for both root and nested objects. Let the recursive shape helper own the object-kind check and keep the root-type lookup/error handling outside it, removing the unreachable duplicate branch without changing the JSON exception contract. | `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs:33-39,47-55,58-70` |

### Status (part one hundred seventy)

R629 is applied: typed deserialization now lets the recursive
`EnsureObjectShape` own the object-kind check, preserving the invalid-root
error, nested-object validation, property ordering, and UTF-16 translation
without a duplicate top-level branch.

## Second survey, part one hundred seventy-one: R630 - duplicated manifest canonical ordering policy

| R630 | **Manifest canonical ordering is restated in both mutation and hashing paths.** `WorkerProtocolJson.Canonicalize` sorts callables, claims, enum arrays, claim IDs, and assumptions, while `CreateManifestPayload` repeats the corresponding callable/claim order and normalized child-array projections so hashing and equality remain stable for noncanonical objects. Reuse the existing sort/projection helpers or expose one canonical enumeration layer, but retain the payload's defensive normalization if callers may hash an uncanonicalized manifest. | `SharpProof.Worker.Protocol/ProtocolManifest.cs:10-41`; `SharpProof.Worker.Protocol/ProtocolManifestPayload.cs:7-44` |

### Status (part one hundred seventy-one)

R630 is a pending protocol-manifest complexity candidate, not a request to remove canonicalization defense. Preserve stable hashes/equality for both canonical and noncanonical manifests; consolidate only duplicated order/projection policy where the call graph proves the same normalization is safe.

## Second survey, part one hundred seventy-three: R632 - repeated PowerShell fixture cleanup

| R632 | **Six PowerShell fixture drivers repeat the same temporary-tree cleanup guard.** `Test-SharpProofMutationEvidence`, `Test-SharpProofPublicationDestinationFixtures`, `Test-SharpProofPublicationPlanIdentityFixtures`, `Test-SharpProofPublicationPlanTopologyFixtures`, `Test-SharpProofReleaseConfigurationFixtures`, and `Test-SharpProofReleaseAuthorityClosureFixtures` each finish with `if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }`, differing only in the variable name. A shared fixture-lifetime helper can own the existence check and recursive removal (and, ideally, verify the generated temp-prefix ownership), while each script retains its own fixture creation, environment restoration, and failure behavior. | `scripts/Test-SharpProofMutationEvidence.ps1:1272-1275`; `scripts/Test-SharpProofPublicationDestinationFixtures.ps1:265-268`; `scripts/Test-SharpProofPublicationPlanIdentityFixtures.ps1:266-269`; `scripts/Test-SharpProofPublicationPlanTopologyFixtures.ps1:98-101`; `scripts/Test-SharpProofReleaseConfigurationFixtures.ps1:262-265`; `scripts/Test-SharpProofReleaseAuthorityClosureFixtures.ps1:130-133` |

### Status (part one hundred seventy-three)

R632 is a pending fixture-infrastructure reduction candidate. Preserve cleanup in `finally`, per-script environment restoration, recursive deletion, and ownership safety; share only the common temporary-directory disposal protocol.

## Second survey, part one hundred seventy-four: R633 - duplicate fuzz assembly lifetime

| R633 | **`FrontendDifferentialOracle` repeats collectible generated-assembly lifetime management.** `CompareBatch` and `CompareSemanticEdges` each reset an emitted image, create a collectible `AssemblyLoadContext`, load the stream, resolve a generated runtime type, run a case loop, and unload the context in `finally`; only the assembly/type name and callback-specific work differ. A private `WithLoadedGeneratedAssembly` callback helper can own the stream-position, collectible-context, load, and unload protocol while preserving each path's method-shape checks, runtime method selection, result assembly, and cancellation behavior. | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:982-1051`; `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:1161-1189` |

### Status (part one hundred seventy-four)

R633 is a pending fuzz-oracle infrastructure reduction candidate. Preserve collectible unloading, image ownership/disposal, runtime-type lookup failures, and distinct batch versus semantic-edge result handling; share only the assembly lifetime scaffold.

## Second survey, part one hundred seventy-nine: R638 - repeated trusted-boundary traversal

| R638 | **`ClaimManifestBuilder.BuildTarget` traverses trusted-boundary attributes twice.** `SelectFeatures` calls `TrustedAttributes(method).Any()` to decide whether trusted evidence exists, and `CreateAssumptions` immediately enumerates `TrustedAttributes(target)` again to materialize the same scope/attribute pairs. Each enumeration walks `CompilerMethodScopes.Enumerate` and every scope's attributes with the same predicate. Materializing the trusted attributes once at the build-target boundary, or passing the materialized sequence through the two helpers, removes the repeated symbol/attribute traversal while preserving the separate feature-selection and assumption-evidence policies. | `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs:65-69,239-258,261-315,455-467` |

### Status (part one hundred seventy-nine)

R638 is a pending compiler-collector traversal reduction candidate. Preserve scope order, trusted-attribute filtering, selected-feature behavior, and assumption ID/rank ordering; share only the one per-target trusted-attribute enumeration.

## Second survey, part one hundred eighty: R639 - repeated specification-pack admission validation

| R639 | **A specification-pack call is fully validated once during admission and again during summary construction.** `CompilerRelationalSummaryProvider.IsAdmissiblePureCall` asks `CompilerSpecificationPackProvider.CanResolve`, which delegates to `TryResolve` and performs identity, method-shape, assembly, return-type, and parameter-type checks. If source and IL summaries do not handle the call, `TryGet` later calls `CompilerSpecificationPackProvider.TryBuild`, whose first operation is the same `TryResolve` before constructing the summary. A cached resolved definition or a cheap identity-only admission predicate can remove the duplicate validation without weakening the full `TryResolve` gate required before building a summary; source/IL admissibility and the distinct summary-building order remain explicit. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:78-85,123-146`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerSpecificationPackProvider.cs:123-145,214-256` |

### Status (part one hundred eighty)

R639 is a pending specification-pack admission/lookup reduction candidate. Preserve pack overlap rejection, method-shape/type checks, source/IL fallback ordering, and fail-closed resolution; share or cache only the repeated pack-definition validation.

## Second survey, part one hundred ninety-two: R651 - effect evidence validation twice

| R651 | **Effect evidence is validated once directly and again through authority matching.** `CompilerLoweredArtifact.DecodeEffects` calls `CompilerEffectClaimArtifactCodec.Validate(evidence, compilation)` before `CompilerEffectAuthority.Matches`, while `Matches` rebuilds the authority payload and `HasValidAuthorityPayload` seals and validates that copied evidence again. `CompilerManifestArtifactJson.HasFeatureScopeParity` repeats the same sequence with its effect rows. An internal matched-authority path can accept a caller-proven validated evidence row, or a combined routine can return the validation result, removing the duplicate codec hash/shape/replay work without weakening the independent authority-copy comparison. | `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:500-520`; `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:65-143`; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:613-646` |

### Status (part one hundred ninety-two)

R651 is deferred: the apparent duplicate work crosses an intentional trust boundary. The evidence row and the copied authority payload must each retain independent hash, shape, and replay-geometry validation; removing either check could admit a forged digest or replay witness.

## Second survey, part one hundred ninety-three: R652 - repeated pack-authority validation

| R652 | **Full manifest validation checks the same compilation pack authority through two paths.** `CompilerManifestArtifactJson.HasValidEnvelope` calls `CompilerSpecificationPackAuthorityValidation.Matches`, which validates the artifact-level and compilation-level pack authorities; the later `CompilationFingerprint.ValidateShape` call runs `ValidSnapshot`, which validates the compilation pack authority again before checking its other captured fields. A validation result passed through the manifest-validation pipeline, or a shape validator that can rely on the already-checked authority, removes the repeated catalog-version, catalog-hash, canonical-ID, and known-pack checks while preserving the outer-versus-compilation equality assertion. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:429-457,494-515`; `SharpProof.CompilerArtifact/CompilationFingerprint.cs:66-107`; `SharpProof.CompilerArtifact/CompilerSpecificationPackAuthority.cs:28-75` |

### Status (part one hundred ninety-three)

R652 is deferred: envelope equality and compilation-shape validation are intentional independent trust-boundary checks. Keep both catalog-integrity validations and their fail-closed behavior until a proof that shares results without weakening either boundary is available.

## Second survey, part one hundred ninety-five: R654 - repeated replay-row hashing

| R654 | **`CompilerEffectClaimArtifactCodec` walks replay events separately for validation and each digest.** `Validate` checks every event with `HasValidReplayEvent` and then calls `ComputeSha256`, whose `AddReplayEvent` loop traverses the same rows and fields again; `Seal` likewise computes each operation identity in one loop before the evidence digest walks the events again. A combined per-event validation/hash accumulator can retain the distinct operation-identity and whole-evidence hash domains, ordinal and nullable-field normalization, and fail-closed validation while removing the repeated replay-row traversal. | `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs:12-40,138-184,229-280` |

### Status (part one hundred ninety-five)

R654 is deferred: replay validation and the separate operation/evidence digest domains are analyzer-integrity boundaries. Their independent event walks make the field ordering, null normalization, and fail-closed checks auditable; combine them only with a proof that cannot alter those semantics.

## Second survey, part two hundred two: R661 - repeated requires call discovery

| R661 | **The requires analyzer performs a full call-site screen and then repeats call extraction for the actual analysis.** `RequiresCallSiteAnalyzer.Analysis.Run` calls `HasPotentialCallSite` before binding contracts; that screen traverses executable operations and invokes `GetCalls` for each operation. When the screen succeeds, `RequiresCallSiteDiscovery.Get` builds flow state and walks the reachable CFG, invoking `GetCalls` again over the same declaration, with a further traversal for special constructs. Cache a declaration-scoped discovery result or make the screen consume the later candidate walk, while retaining the early not-applicable fast path, flow-sensitive filtering, ownership policy, and special-case handling. | `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:14-94,96-315`; `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs:236-250` |

### Status (part two hundred two)

R661 is deferred: the applicability screen and candidate discovery intentionally run with different flow, ownership, initializer, and special-pattern semantics. Sharing their call extraction without a declaration-scoped proof could remove the early not-applicable short-circuit or admit/reject a call site differently; keep both fail-closed traversals until their boundaries can be proven equivalent.

## Second survey, part two hundred four: R663 - serial effect-body rescans

| R663 | **`EffectMethodNodeBuilder.Build` rescans the same method body for three effect dimensions.** The CFG path visits reachable block operations through `AnalyzeControlFlowGraph` to build the ordinary body summary, then `Build` invokes `ScanLexicalControlEffects` over the lexical root and `ScanUsingDisposalEffects` over the full root. Those latter passes independently enumerate operations or disposal structures that the scanner has already visited, with only their effect-specific projections differing. A coordinated body walk or shared per-operation facts can preserve lexical versus reachable semantics, disposal ordering, and direct-witness behavior while avoiding repeated operation-tree work. | `SharpProof.Effects/EffectMethodNodeBuilder.cs:45-114,720-780`; `SharpProof.Effects/OperationEffectScanner.cs:135-220` |

### Status (part two hundred four)

R663 is deferred: the ordinary CFG scan, lexical lock/throw scan, and using-disposal resolver intentionally use different roots and reachability/unwinding rules. A shared walk could alter direct-witness recording, constructor entry selection, disposal order, or fail-closed effect joins; keep the independent passes until reusable per-operation facts can be proven equivalent.

| R677 | **Acceptance static validation derives the full production inventory twice.** `eng/acceptance/Verify.ps1` invokes `Get-SharpProofProductionInventory.ps1` before TCB and coordinator checks, then calls `Test-ProductionCSharpComplexity.ps1`; that script independently invokes the same inventory generator, reparsing every project MSBuild query and rebuilding compile/options records. Pass the first inventory through a file/object seam or combine the two consumers, while retaining the complexity script's intentional Release configuration if it differs from the acceptance configuration. | `eng/acceptance/Verify.ps1:237-245,624`; `scripts/Test-ProductionCSharpComplexity.ps1:76-82` |

### Status (part two hundred thirteen)

R677 is a pending acceptance-preparation reduction. Preserve production-inventory authority, TCB/coordinator scope, and any intentional Release-versus-acceptance configuration distinction; share only inventory data.

### Status (part two hundred twenty-four)

| R690 | **`DefaultApiSpecCatalogGenerationTests` implements two recursive term describers.** `Describe(JsonElement)` and `Describe(SpecTermDeclaration)` walk the same variable, literal, unary, binary, conditional, and length tree shapes and emit the same canonical shape string; they differ only in extracting fields from JSON versus generated objects and in their unknown-kind exception type. A small normalized-term adapter or shared kind/child formatter can keep the source-versus-generated comparison while removing the second recursive walker. | `SharpProof.Specs.Test/DefaultApiSpecCatalogGenerationTests.cs:575-649` |

R690 is deferred: the JSON and generated-object walkers intentionally have different field-extraction and unknown-kind exception contracts. A shared adapter would duplicate those switches or change validation behavior; keep the separate recursive paths until a normalized representation already exists.

### Status (part two hundred twenty-six)

R692-R693 completed: report execution now shares one formatting/output helper, and rejection assertions share one callback-based failure contract while preserving serialization modes, stale-output setup, caller-selected output paths, diagnostics, and output non-publication.

### Status (part two hundred twenty-seven)

R694 completed: container-source fixtures now own repository and archive roots with scoped `TempDirectory` lifetimes, including cleanup when fixture creation fails.

### Status (part two hundred twenty-eight)

R695 completed: list and recursive pattern calls now share one resolver/completion step helper while preserving intrinsic filtering, receiver ownership, dispatch uncertainty, empty actual arguments, and child traversal.

### Status (part two hundred twenty-nine)

R696 completed: corpus token replacement now handles only the active helper/input tokens; generated method names and all variant naming/alpha-renaming remain unchanged.

### Status (part two hundred thirty)

R697 completed: actual arguments are filled in one mutable array and frozen once, preserving omitted slots, ordinal filtering, param-array skipping, and the immutable result.

### Status (part two hundred thirty-one)

R698 completed: the unused one-value `ExpectedSmt` parameter and all call-site plumbing are removed; canonical-host guards and actual sample/package verification remain unchanged.

### Status (part two hundred thirty-two)

R699 completed: accepted and rejected fuzz fixtures now share one result assertion wrapper with explicit success expectations, preserving expected-value overrides and rejection diagnostics.

### Status (part two hundred thirty-three)

R700 completed: release and standalone gate fixtures now share the canonical assertion harness while retaining validator-specific writers, expected values, rejection handling, and independent cleanup.

### Status (part two hundred thirty-four)

| R701 | **`WorkerBinaryIdentityTests` manually disposes `TempDirectory` in five tests.** The tests construct the shared disposable fixture, copy its `FullName`, execute assertions, and then use `finally { temporaryWorkspace.Dispose(); }`; a `using var` declaration preserves the same failure cleanup with less lifecycle scaffolding. Keep the separate conditional disposal in `RuntimeClosurePathsAreImmutable`, where the snapshot's ownership is deliberately dependent on an alias mutation. | `SharpProof.Worker.Test/WorkerBinaryIdentityTests.cs:11-38,42-65,108-151,216-243,260-439` |

R701 completed: worker identity tests now use scoped `TempDirectory` fixtures while preserving temporary-root cleanup, staged-file assertions, malformed-manifest coverage, and the conditional snapshot ownership behavior.

### Status (part two hundred thirty-five)

| R702 | **`CompilationModelProvider.FindOwningCompilation` performs linear visited checks inside its graph walk.** Every popped compilation scans the entire `visited` list with `Any(ReferenceEquals(...))`, so a large source-compilation-reference closure repeatedly traverses already-seen nodes. Replace the list membership check with a reference-identity hash set, retaining the separate owner detection and duplicate-tree rejection semantics. | `SharpProof.Frontend/CompilationModelProvider.cs:27-58` |

R702 completed: the compilation closure now uses a reference-identity hash set for cycle checks while preserving source-tree ownership detection, cycle termination, and the multiple-owner exception.

### Status (part two hundred thirty-six)

| R703 | **`PackageDependencyAuthorityTests` repeats manual temporary-root lifetimes.** Four test methods build a unique path under `Path.GetTempPath`, call `Directory.CreateDirectory`, and repeat `try/finally { Directory.Delete(..., recursive: true); }`. `TempDirectory` is already linked into `SharpProof.ArchitectureTest` through `Directory.Build.props`; a `using` fixture or scoped helper can remove the lifecycle scaffolding while preserving each test's isolated prefix and cleanup behavior. | `SharpProof.ArchitectureTest/PackageDependencyAuthorityTests.cs:24-156`; `eng/testing/TempDirectory.cs:1-20`; `Directory.Build.props:76-81` |

R703 completed: package-authority tests now use scoped `TempDirectory` fixtures while preserving parallel isolation, per-scenario mutation coverage, and recursive cleanup on assertion or process failure.

### Status (part two hundred thirty-seven)

| R704 | **`PackageDependencyAuthorityTests.RunAuthorityAsync` duplicates the PowerShell process lifecycle already owned by `RunPowerShellAsync`.** The package-graph path reconstructs `ProcessStartInfo`, fixed `pwsh` arguments, redirected stream reads, exit waiting, and `ProcessResult` assembly, while the component-authority path uses the existing helper for the same runner shape. Route both paths through the common runner and parameterize only the script-specific arguments, retaining the helper script path, package-path forwarding, exit code, and combined output semantics. | `SharpProof.ArchitectureTest/PackageDependencyAuthorityTests.cs:371-416,418-482` |

R704 completed: package-graph authority now routes through the shared PowerShell runner, preserving generated runner contents, argument ordering, stderr/stdout combination, and result status semantics.

### Status (part two hundred thirty-eight)

| R705 | **`UsingDisposalGraph.GetInternalGotoTargets` repeatedly scans operation and target collections while resolving one goto set.** For each label it searches `scope.Operations` for a span match, calls `IndexOf` on the matching operation (and may search again for the fallback), then materializes `allTargets` only to walk it again for active targets and lifetime escape, while `branches` is rescanned for unconditional-goto detection. A single indexed operation lookup plus one classification pass can remove the repeated enumeration and intermediate walks, preserving span-overlap precedence, fallback selection, target de-duplication, and active-lifetime flags. | `SharpProof.Effects/UsingDisposalGraph.cs:81-109` |

R705 completed: internal goto targets now use one indexed operation pass and one target-classification pass, preserving nested-span resolution, unconditional-label behavior, target order, and all three returned classifications.

### Status (part two hundred thirty-nine)

| R706 | **`EffectModuleInitialization.Discover` repeatedly linearly resolves syntax-tree ordinals.** Duplicate normalized initializers compare both syntax references through `syntaxTrees.IndexOf`, and the final ordering performs another `IndexOf` for every retained initializer. Caching the reference-to-ordinal map once per compilation removes repeated scans of the full syntax-tree list while retaining the `int.MaxValue` fallback and lexical tree/span/name tie-breakers. | `SharpProof.Effects/EffectModuleInitialization.cs:34-93,141-159` |

R706 completed: module-initializer discovery caches syntax-tree ordinals once per compilation, preserving source-tree identity, deterministic Roslyn order, duplicate normalized-method handling, and unknown-tree fallback behavior.

### Status (part two hundred forty)

| R707 | **`ClosedContractAttributeValidator.GetKind` repeats the closed-attribute membership cascade already exposed by `ContractSelectionInventory.IsClosedContract`.** Both perform the same three symbol-identity comparisons for `NotNull`, `Positive`, and `InRange`; the validator then needs the selected kind for type, ref-kind, and bound validation, but it should consume one canonical classification rather than maintain a second recognition list. Exposing a kind-returning inventory helper can remove the duplicate comparisons while keeping rejected-metadata recognition and all validator-specific checks separate. | `SharpProof.Contracts/ClosedContractAttributeValidator.cs:29-71`; `SharpProof.Contracts/ContractSelectionInventory.cs:103-123` |

R707 completed: closed-contract kind classification is now canonical in `ContractSelectionInventory`, preserving compiler-bound symbol identity, recognition precedence, rejected-attribute behavior, and validator type/ref-kind/range diagnostics.

### Status (part two hundred forty-one)

| R708 | **`Invoke-SharpProofDevCheck.ps1` does not execute the command plan it reads.** `Get-SharpProofDevCheckPlan.ps1` emits restore, solution-build, semantic-test, package-product-build, one package-pack row per manifest project, and performance-smoke commands, but the runner parses the JSON only to validate schema/configuration and test whether the package-product-build row exists; it then reconstructs the restore, build, semantic-test, package-test, and smoke invocations by hand. The plan's package-project enumeration and most command fields are therefore a second, non-authoritative description that can drift without changing the check. Either execute the planned rows or reduce the plan to the small decision data the runner actually needs, retaining the deliberate Debug-only Release package-product build and phase timing. | `scripts/Get-SharpProofDevCheckPlan.ps1:22-57`; `scripts/Invoke-SharpProofDevCheck.ps1:25-109` |

R708 completed: the developer check now resolves and validates every planned phase and package row, using plan-owned configurations and inclusion while preserving no-build/restore relationships, parallel build behavior, timing evidence, and schema validation.

### Status (part two hundred forty-two)

| R709 | **`ReferencedTypeSymbols.GetAll` rebuilds the complete type closure for separate companion consumers.** `ContractForSymbolMatcher.DiscoverCompanionRelationships` enumerates every source and referenced-assembly type to build companion descriptors, while `ConservativeEffectCallPreconditionPolicy.FindTypesWithCompanions` independently enumerates the same source-plus-reference closure and then filters `[ContractFor]` targets into a set. A compilation-scoped immutable type snapshot (or a shared raw companion inventory with adapters for the two policies) can remove the duplicate namespace/type traversal without forcing the effects assembly to depend on the contracts implementation. Do not cache partial results after cancellation, and retain the existing symbol comparer, traversal order, and each consumer's different validity/cycle policy. | `SharpProof.Frontend/ReferencedTypeSymbols.cs:5-59`; `SharpProof.Contracts/ContractForSymbolMatcher.cs:155-179`; `SharpProof.Effects/EffectCallPreconditionPolicy.cs:215-259` |

R709 is deferred: both consumers already share `ReferencedTypeSymbols.GetAll`; sharing a materialized compilation-scoped snapshot would add retention and cancellation-invalidation policy across analyzer phases without reducing the per-consumer symbol walks that carry different filters. Keep the lazy, cancellation-aware traversal until a shared snapshot lifetime is explicitly owned.

### Status (part two hundred forty-three)

| R710 | **`RequiresCallSiteDiscovery.GetDirectDelegateTargets` traverses the operation tree three times to prepare one target map.** It first scans `operationRoot.DescendantsAndSelf()` for any goto, then walks the same tree to collect direct delegate declarators, and then walks it again to record invalidating assignments/ref arguments. A seed pass that collects both the goto flag and delegate declarations, followed by one invalidation pass (or a deferred single-pass accumulator), can remove a full operation-tree traversal while preserving declaration order, ambiguity handling, nested-callable coverage, and the global goto conservatism. | `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:940-1005` |

R710 completed: delegate target discovery now collects goto state, declarators, and invalidation candidates in one operation-tree traversal while preserving conservative goto handling, ambiguous locals, invalidation order, and all operation shapes that can write through a delegate local.

| R711 | **`DirectDelegateTarget.Invalidations` grows an immutable array one operation at a time.** Each matching write updates the target with `known with { Invalidations = known.Invalidations.Add(operation) }`; `ImmutableArray.Add` copies the accumulated array for every invalidation, so a local with many writes pays quadratic copying and repeated record allocation. Collect invalidations in a mutable per-local list/builder and freeze once after discovery, retaining source traversal order, symbol equality, and the existing ambiguous-target removal policy. | `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:934-938,979-1004` |

R711 completed: delegate invalidations now accumulate in mutable per-local lists and freeze once into the immutable snapshot exposed to `TryResolveDirectDelegateTarget`, preserving invalidation ordering.

| R712 | **`RequiresCallSiteDiscovery.IsStableAtInvocation` recomputes invocation-invariant facts for every invalidation.** Inside the invalidation loop it repeatedly checks the invocation's syntax tree, `IsInsideLoop(invocation)`, `IsInsideNestedCallable(invocation)`, and `target.HasGoto`; only the invalidation's tree/position and ancestry are iteration-specific. Hoisting those invariant facts before the loop removes repeated parent walks and property checks without changing the fail-closed conditions or invalidation ordering. | `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1034-1053` |

R712 completed: invocation tree, loop, and nested-callable facts are computed once per stability check while preserving same-tree ordering, loop/nested-callable rejection, goto conservatism, and every invalidation behavior.

| R713 | **`RequiresCallSiteDiscovery.GetListPatternCalls` scans the same pattern list three times before its main loop.** `Count` computes non-slice items, `Any` detects a slice, and a separate indexed loop finds `sliceIndex`; the subsequent loop then traverses all patterns again to emit calls. Fuse the count/flag/index preparation into one indexed pass, retaining the known-length short-circuits, slice index arithmetic, and emitted-call order. | `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs:1421-1455` |

R713 completed: list-pattern length, slice presence, and slice index are prepared in one pass, preserving empty/sliced behavior, known-length rejection, and indexer-versus-slice member selection.

### Status (part two hundred forty-four)

| R714 | **`RequiresCallSiteTreeAnalyzer.IsNonExecutingObservation` resolves the same framework symbol for every local reference.** Each call performs `Compilation.GetTypeByMetadataName("System.Delegate")` before walking the reference's operation ancestors, although the symbol is invariant for the containing compilation and `TreeAnalysis`. Cache the nullable delegate symbol once per analysis (while retaining the existing null fallback and the shared metadata-name authority concern recorded elsewhere) so repeated observations do not repeat compilation lookup. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1276-1338` |

R714 completed: `TreeAnalysis` caches the nullable `System.Delegate` symbol once per analysis, preserving null fallback, delegate property identity checks, and all ancestor-based non-execution rules.

| R715 | **`TreeAnalysis.GetPatternDestinations` performs a linear duplicate search for every declared pattern local.** The pending pattern walk calls `result.Any` with `SymbolEqualityComparer.Default` before each append, producing quadratic work as a pattern tree declares more locals. Keep the ordered result list but pair it with a symbol-equality `HashSet` so membership and insertion remain separate without changing traversal order or duplicate suppression. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1341-1448` |

R715 is deferred: replacing the equality scan with `HashSet<ILocalSymbol>` changed an existing non-completing-prefix regression (the analyzer reported `get_Value` where the expected result was empty), indicating that Roslyn's symbol hash/equality behavior or the traversal's duplicate semantics are not interchangeable here. Keep the linear equality scan until a reproducer can establish a hash-safe key without changing refutation suppression.

| R716 | **`TreeAnalysis.GetReachableLocalFunctions` rescans the complete reachable set after each child graph.** It records only `reachable.Count` before `TryCollectLocalReferences`, but when the count changes it iterates every reachable local and may enqueue all currently unscanned methods again; repeated child discoveries therefore create duplicate queue entries and repeated set scans. Track the newly added locals (or maintain a pending/enqueued set) while preserving the current breadth-first discovery and conservative fallback behavior. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:381-441` |

R716 completed: local-function reachability now tracks scheduled symbols to prevent duplicate queue entries while preserving cycle termination, breadth-first discovery order, CFG-failure fallback, and anonymous-function recursion.

| R717 | **`TreeAnalysis.CanReachConsumption` computes each local-reference ordering key twice.** The reference sequence is sorted with `OrderBy(GetReferenceOrder)`, then the loop immediately calls `GetReferenceOrder(reference)` again before applying the `after` boundary. Project each reference with its order once, sort the pair, and consume the cached key while keeping the assignment-end ordering rule unchanged. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:682-704,1234-1242` |

R717 completed: each local-reference ordering key is projected once before sorting and then reused for filtering, preserving assignment-span ordering, declaration filtering, and fail-closed treatment across all blocks.

| R718 | **`BlockMayThrowBeforeAssignmentCommit` rescans the entire CFG for each qualifying assignment reference.** Once a tracked value is killed, the helper walks the reference's ancestors to find the owning simple assignment and then scans every block and descendant operation bounded by the same `after`/commit span; multiple qualifying references or repeated paths can repeat that full graph walk. Cache the bounded throw result by graph/assignment interval (or pre-index throwing operations) while retaining the current span bounds and `RoslynCfgThrowFacts` predicate. | `SharpProof.Analyzer.Core/RequiresCallSiteTreeAnalyzer.cs:1202-1232` |

R718 completed: cached each bounded assignment throw scan within a reachability query by syntax-tree/span/after interval, preserving multi-block RHS coverage, exceptional-successor semantics, and the existing false result when no owning assignment is found.

### Status (part two hundred forty-five)

| R719 | **`ApiSpecRuntimeOracleTests.ConstructorRow` executes every constructor edge twice for related facets.** The same prepared `RuntimeEdge` array is passed to `ObserveThrows` and `ObserveTermination`; both invoke and prepare each edge, while termination is exactly derivable from the throw observation's normal-completion count (`all normal` versus `any exception`). Share one isolated throw observation per edge set or derive the termination facet from it, preserving the per-edge preparation needed to isolate receiver state and the current exception classification. | `SharpProof.Specs.Test/ApiSpecRuntimeOracleTests.cs:529-552,866-912` |

R719 completed: constructor throw and termination facets now share one lazy per-edge throw observation, deriving termination from normal-completion counts while preserving edge preparation, exception classification, empty-edge behavior, and declared claim semantics.

| R720 | **`ApiSpecRuntimeOracleTests` duplicates dynamic-constructor-invoker emission.** `CreateParameterlessConstructorInvoker` and `CreateStringConstructorInvoker` each construct a `DynamicMethod`, obtain a constructor, emit receiver/argument loads plus `Call` and `Ret`, and create a delegate; only the signature, constructor lookup, and extra string argument differ. A parameterized IL-emission helper can centralize the dynamic-method lifecycle while keeping the two strongly typed delegate factories and their constructor-shape diagnostics. | `SharpProof.Specs.Test/ApiSpecRuntimeOracleTests.cs:1166-1205` |

R720 completed: parameterless and string constructor witnesses now share one dynamic-method/IL-emission helper while preserving module visibility, argument order, constructor lookup diagnostics, and exact delegate types.

### Status (part two hundred forty-six)

| R721 | **`EffectRegionSet.Create` sends singleton region sets through the full general normalization pipeline.** The 19 current `EffectRegionSet.Create(EffectRegionId.X)` call sites allocate/iterate a params array, run `Distinct().OrderBy().ToImmutableArray()`, and then scan the resulting one-element array for `Unknown`, even though singleton regions are the dominant construction shape. Add a singleton overload or a fast path that still canonicalizes `Unknown`, retaining the general enumerable overload's duplicate removal and ordering semantics. | `SharpProof.Effects/EffectRegions.cs:82-98`; singleton call sites in `SharpProof.Effects/ConversionOwnershipClassifier.cs`, `ExternalEffectResolver.cs`, and `OperationEffectScanner.cs` |

R721 completed: singleton region creation now uses a direct overload (including canonical `Unknown`) and the params overload fast-paths zero/one entries while preserving unknown absorption, sorted/deduplicated multi-region behavior, empty representation, and value equality.

| R722 | **`OperationSubsetClassifier.GetKnownOperationKinds` recomputes an invariant enum snapshot on every call.** Each invocation reflects over `OperationKind`, casts, deduplicates, sorts, and materializes a new immutable array; `CreateSnapshot` immediately calls the same method before classifying every entry. Cache one immutable known-kind array per process (returning the immutable value directly) while preserving the numeric ordering and duplicate handling used by the public snapshot. | `SharpProof.Frontend/OperationSubsetClassifier.cs:37-60` |

R722 completed: known operation kinds are reflected, deduplicated, and sorted once per process, preserving enum ordering, invalid-kind handling, and exact snapshot text.

| R723 | **`InvocationEmissionPolicy.IsElided` decodes conditional attributes afresh for every invocation of a target.** The normalized target method, its `[Conditional]` symbol names, and the conditional-attribute identity are invariant across calls, but the current path re-enumerates and filters `target.GetAttributes()` for each invocation before consulting the already cached per-tree preprocessor symbols. Add a symbol-keyed conditional-name cache, retaining empty-result caching, reduced-method normalization, and the per-tree symbol lookup. | `SharpProof.Effects/InvocationEmissionPolicy.cs:14-54` |

R723 completed: conditional attribute symbol names are cached by target method (including empty results), preserving reduced-method normalization, malformed-attribute filtering, syntax-tree-specific preprocessor symbols, and ordinal symbol matching.

## Second survey, part two hundred forty-seven: R724-R725 - the unshared process runner

A census of every tracked, non-`obj` C# file that starts a child process. This is
the largest single duplication cluster remaining in the repository, it spans four
assemblies, and unlike most items in this ledger the copies **already disagree on
what their result type means**.

| ID | Finding | Evidence |
|---|---|---|
| R724 | **"Run a child process and capture its output" is written 55 times across 43 files, and the result type is redeclared 16 times in five incompatible shapes.** Of the 55 `new ProcessStartInfo` sites, 52 set `UseShellExecute = false`, 48 set `RedirectStandardOutput = true`, and 16 files repeat the identical `foreach (var argument in arguments) { startInfo.ArgumentList.Add(argument); }` loop verbatim. Six of those files carry a near-identical ~28-line private runner (`RunAsync` in `SharpProof.ArchitectureTest`, `RunProcessAsync` in `SharpProof.Package.Test`) that differ only in the name and the result shape. The shapes are: `(int ExitCode, string Output, string Error)` (8 declarations); `(int ExitCode, string Output)` where `Output` is `stdout + Environment.NewLine + stderr` (4); `(int ExitCode, string StandardOutput, string StandardError)` - same three fields, different member names (1); `(string Output)`, whose runner asserts `ExitCode == 0` internally so it cannot express a negative test (1); plus `GateProcessResult` and `PackageProcessResult` as separately named variants. **The divergence is semantic, not cosmetic: `result.Output` means stdout in the `SharpProof.ArchitectureTest` family and stdout-concatenated-with-stderr in the `SharpProof.Package.Test` family.** An assertion of the form `Assert.That(result.Output, Does.Not.Contain(x))` therefore searches stderr in one assembly and not in the other, and moving a test between the two assemblies silently changes what it checks. The split falls exactly on assembly lines, which is how it arose: each assembly grew its own copy. Two of the shapes also drop `CreateNoWindow = true`, present at only 27 of the 55 sites. | 43 files; runners at `SharpProof.ArchitectureTest/AcceptanceScriptTests.cs:266-293`, `CoverageScriptTests.cs:1615-1642`, `ProductionInventoryAuthorityTests.cs:291-318`; `SharpProof.Package.Test/DependencyAuditScriptTests.cs:625-652`, `ReleasePublicationScriptTests.cs:898-925`, `PackageLayoutSmokeTests.cs:2402-2431`; result types at `ChangedTestSelectionTests.cs:163`, `ContainerAuthorityScriptTests.cs:299`, `ContainerSourceCleanlinessTests.cs:409`, `FuzzRunnerEvidenceTests.cs:138`, `PackageDependencyAuthorityTests.cs:484`, `ReleaseCoverageBaselineTests.cs:604`, `FinalCompilationProbeTests.cs:1012`, `PackageLayoutSmokeTests.cs:3339`, `Gates/Performance/WorkerPerformanceProbe.cs:539`, `Gates/GateProcess.cs:50`, `Package.Test/PackagedProductFeed.cs:367` |
| R725 | **The canonical implementation already exists, in production code, and no test uses it.** `SharpProof.Gates/GateProcess.cs` is a 46-line `internal static` helper with exactly the shape the 16 private copies approximate: `RunCapturedAsync(ProcessStartInfo, CancellationToken)` returning `GateProcessResult(int ExitCode, string Output, string Error)`. It is strictly better than every private copy in three respects the copies all lack - it threads a `CancellationToken` into both `ReadToEndAsync` calls and `WaitForExitAsync`, it `KillTree(entireProcessTree: true)` on cancellation and re-awaits before rethrowing, and it throws a described `InvalidOperationException` rather than `Process.Start(startInfo)!` null-forgiving. Every private copy uses the bare `!`, so a process that fails to start surfaces as a `NullReferenceException` with no indication of which executable was missing. **The sharing mechanism is also already in place and already targets the right projects**: `Directory.Build.props:76-81` links `eng/testing/TempDirectory.cs` into exactly `SharpProof.Package.Test`, `SharpProof.ArchitectureTest`, `SharpProof.Gates.Test`, and `SharpProof.Worker.Test` - a superset of the assemblies holding all six full runner copies. An `eng/testing/ProcessRunner.cs` added to that same `ItemGroup` needs no new reference, no visibility change, and no new convention. `AssertSuccessAsync` should move with it: it is byte-identical at `AcceptanceScriptTests.cs:258-264` and `CoverageScriptTests.cs:1607-1613`, with a third `Task`-returning variant at `ProductionInventoryAuthorityTests.cs:285`. | `SharpProof.Gates/GateProcess.cs:1-53`; `Directory.Build.props:68-81`; `eng/testing/` (6 existing shared test sources) |

### Checked and not proposed (part two hundred forty-seven)

- **The portable-IR shadow model is correctly gated and is not duplication.**
  `SharpProof.CompilerArtifact/PortableIrModel.generated.cs` mirrors the storage
  shape of `SharpProof.Ir/IrModel.generated.cs`, but it reuses the IR vocabulary
  enums rather than redeclaring them - `IrTypeKind`, `IrTermKind`,
  `IrLocationKind`, and `IrInstructionKind` all resolve to `SharpProof.Ir` through
  the existing `ProjectReference` and an implicit global using. More importantly the
  correspondence is machine-checked:
  `SharpProof.Worker.Test/CompilerArtifactModelSchemaTests.cs:171-201` reflects the
  real enum out of the `SharpProof.Ir` assembly and asserts
  `Enum.GetNames(enumType) == expectedKinds` in order against the schema's
  hard-coded `kinds` list. Adding an `IrTermKind` member without updating
  `CompilerArtifactModel.schema.json` fails that test. The same test also pins slot
  counts, slot roles, metadata-row argument lists, and encoder return types. This is
  a model worth citing when other cross-schema correspondences in this ledger are
  argued about; it is the strongest such gate found in the survey.
- The `portableIrSlotDomains` / `portableIrSlotMappings` key sets are additionally
  cross-checked inside the generator itself
  (`scripts/Generate-CompilerArtifactModel.ps1:547-557`), in both directions - a
  declared domain with no mapping and a mapping with no declared domain are each an
  error. No gap.
- `SharpProof.Host/LinuxWorkerProcess.cs`, `SharpProof.BuildTasks/RunVerifier.cs`,
  and `SharpProof.BuildTasks/VerifierProcessSupervisor.cs` also start processes but
  are **not** proposed for the shared runner. They are TCB or build-task code with
  their own supervision, timeout, and kill semantics, they ship to consumers, and
  folding them into a test helper would move product behaviour into `eng/testing`.
  R724 and R725 are scoped to test and gate code only.

### Status (part two hundred forty-seven)

R724 is partially completed but the remaining 55-site consolidation is deferred:
the six exact private runners in the two named test assemblies now use the shared
runner, while the broader set still has incompatible result shapes and stdout/
stderr meanings that cannot be collapsed without changing assertions. R725 is
completed: `eng/testing/ProcessRunner.cs` now owns the common start, capture,
cancellation, and process-tree cleanup path and is linked to test projects through
the existing test-source item group. The remaining R724 work should choose an
explicit output contract before migrating the other shapes.

## Second survey, part two hundred forty-eight: R726-R728 - the unshared temporary directory

The companion to the process-runner cluster. Same shape, same two assemblies plus
three more, and again a shared implementation already exists and is almost unused.

| ID | Finding | Evidence |
|---|---|---|
| R726 | **Temporary test directories are created 64 times across 37 files under three mutually incompatible naming conventions, and only one of the three can use the repository's own guarded cleanup.** The conventions are: **nested**, `Path.Combine(GetTempPath(), "<Name>", guid)` - 22 sites; **flat**, `Path.Combine(GetTempPath(), "<prefix>" + guid)` - 23 sites; and 18 further shapes that are neither. This is not cosmetic, because `TestRepository.DeleteOwnedTemporaryDirectory` guards its recursive delete by requiring the resolved path to start with `Path.Combine(GetTempPath(), rootName) + DirectorySeparatorChar`. That predicate can only be satisfied by the **nested** convention. Every one of the 23 flat sites is therefore structurally excluded from the guard and must hand-roll an unguarded delete, which is exactly what they do. The flat prefixes are themselves inconsistent in four styles that no rule distinguishes - kebab (`sharpproof-coverage-diff-`), dotted (`SharpProof.ContainerAuthority.`), Pascal-dash (`SharpProofUnreadable-`), and mixed (`SharpProof-isolated-worker-`). Meanwhile the shared `eng/testing/TempDirectory.cs`, which wraps `Directory.CreateTempSubdirectory` in an `IDisposable` and needs no naming convention at all, is used by **7 files**. | 64 `GetTempPath()` sites in 37 files; `eng/testing/TestRepository.cs:28-47`; `eng/testing/TempDirectory.cs:1-19`; nested at `SharpProof.Gates.Test/CorpusGateTests.cs`, `PerformanceGateTests.cs`, `SharpProof.Worker.Test/WorkerTests.cs:7206`, `ScalarDifferentialMatrixTests.cs:876`; flat at `SharpProof.ArchitectureTest/CoverageScriptTests.cs:723,848,937,1103`, `PackageDependencyAuthorityTests.cs:29,66,111,141`, `ProductionInventoryAuthorityTests.cs:14,102`, `SharpProof.Analyzer.Test/ContractApiIdentityAnalyzerTests.cs:365,427,496` |
| R727 | **The repository built an ownership-guarded recursive delete and 30 of the 34 files that recursively delete do not use it.** `TestRepository.DeleteOwnedTemporaryDirectory` refuses to delete a path outside the expected temp root, throwing `InvalidOperationException` rather than proceeding - a real guard against a path-construction bug recursively erasing an arbitrary directory. There are **56 `Directory.Delete(..., recursive: true)` sites across 34 files**; the guarded helper is called from **5 sites in 4 files** (`PackageLayoutSmokeTests`, `WorkerMsBuildIntegrationTests`, `ScalarDifferentialMatrixTests`, `WorkerTests`). Five further files declare their own private delete helper - `DeleteDirectory`, `DeleteTemporaryRepository` twice, `DeleteStagingDirectory`, `DeleteIfExists` - none of which carries an ownership check. The helper already ships to every test project: it lives in `eng/testing/TestRepository.cs`, linked unconditionally into every `SharpProofTestProject` by `Directory.Build.props:68-73`, so there is no reference or visibility work to do. Only the naming convention in R726 blocks adoption. | `eng/testing/TestRepository.cs:28-47`; `Directory.Build.props:68-73`; 56 sites in 34 files; private helpers at `SharpProof.ArchitectureTest/AcceptanceScriptTests.cs:295`, `CoverageScriptTests.cs:1644`, `ProductionInventoryAuthorityTests.cs:320`, `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:289`, `SharpProof.Worker.Launcher/Program.cs:866` |
| R728 | **Two functions named `DeleteTemporaryRepository` in the same assembly have different bodies, and the one missing the read-only pre-pass is the only one that actually creates a git repository.** `CoverageScriptTests.cs:1644` and `AcceptanceScriptTests.cs:295` both walk the tree with `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` and `File.SetAttributes(path, FileAttributes.Normal)` before deleting; `ProductionInventoryAuthorityTests.cs:320` is a bare `if (Directory.Exists) Directory.Delete(recursive: true)`. But `ProductionInventoryAuthorityTests` is the **only one of the three that runs `git init`** (`:273-282`, followed by `git add` and `git commit`), so it is the only one whose fixture contains `.git/objects` entries, which git creates read-only on every platform. On Windows `Directory.Delete` throws `UnauthorizedAccessException` on a read-only file, so the file that needs the pre-pass is the one that omits it, while the two that have it never create a read-only file. The failure is latent rather than observed - it does not reproduce on the Linux container where the gates run, because there the directory's write permission governs unlinking, not the file's mode - but it is a divergence in the same direction as R727: three copies of one operation, each with a different subset of the necessary behaviour. `DependencyAuditScriptTests.cs:544` is a fourth, inline, copy of the same read-only pre-pass. | `SharpProof.ArchitectureTest/ProductionInventoryAuthorityTests.cs:273-282,320-326`; `CoverageScriptTests.cs:1644-1659`; `AcceptanceScriptTests.cs:295-309`; `SharpProof.Package.Test/DependencyAuditScriptTests.cs:544` |

### Checked and not proposed (part two hundred forty-eight)

- **No temporary directory is actually leaked.** An earlier pass through this data
  flagged four files that create a temp path with no cleanup token
  (`BuildTaskTests`, `ClaimManifestBuilderTests`, `ScalarDifferentialMatrixTests`,
  `WorkerTests`); all four are accounted for. `ScalarDifferentialMatrixTests` and
  `WorkerTests` clean up through `TestRepository.DeleteOwnedTemporaryDirectory` in
  a `Dispose`, and the other two construct a path used as a marker or placeholder
  without creating a directory. Do not record a leak; R726 to R728 are about
  convergence, not resource loss.
- `SharpProof.Worker.Launcher/Program.cs:866` `DeleteIfExists` and
  `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:289`
  `DeleteStagingDirectory` are **not** proposed for the shared helper. Both are
  product code on the shipping path with their own failure semantics, and
  `TestRepository` is a test-only source linked by `SharpProofTestProject`. Folding
  them in would move test scaffolding into the TCB. Scope R727 to test and gate
  code, as with the process runner.
- `Directory.CreateTempSubdirectory`, which `TempDirectory` already uses, is the
  better primitive independent of any naming decision: it creates the directory
  atomically and, on Unix, with owner-only permissions, where
  `Path.Combine(GetTempPath(), guid)` + `Directory.CreateDirectory` inherits the
  umask. That is an argument for adopting `TempDirectory` rather than for
  standardising a prefix convention.

### Status (part two hundred forty-eight)

R726 and R727 are `pending` and are one piece of work: the naming convention
is what blocks adoption of the guard, so choosing the nested form (or moving to
`TempDirectory`, which needs no convention) is the enabling step and the delete
consolidation follows from it. R728 is completed: the production-inventory git
fixture now clears read-only file attributes before recursive cleanup, matching
the existing safe cleanup copies. The wider R726/R727 naming and ownership
migration still needs to account for each fixture's lifetime and git behavior.

## Second survey, part two hundred forty-nine: R729-R730 - platform metadata references, and why the shared sources are not adopted

The third instance of the pattern behind R724-R728, and the one that explains the
other two: the shared source is not merely under-distributed, it is ignored inside
the very projects that already compile it.

| ID | Finding | Evidence |
|---|---|---|
| R729 | **"Build the platform metadata references for a test compilation" is written 23 times, and reading one environment value is done five different ways with two different error messages.** Every copy reads `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")`, splits on `Path.PathSeparator`, and projects to `MetadataReference.CreateFromFile`. The null handling diverges five ways: `(string?)... ?? throw` (9 sites); `... as string ?? throw` (3); `((string)...!)` null-forgiving with no message at all (4, all in `SharpProof.Frontend.Test` and `SharpProof.Worker.Test`); `(string?)...` followed by an `IsNullOrWhiteSpace` guard (1); and the shared version's own form. The thrown text splits **14 sites across 11 files** saying `"Trusted platform assemblies are unavailable."` against **3 sites across 3 files** saying `"The runtime did not expose trusted platform assemblies."` - one condition, two messages, so a failure report cannot be grep-matched to a single site class. **Ordering diverges independently**: 5 files sort the split paths with `OrderBy(path, StringComparer.Ordinal)` and 18 do not, with no stated rule separating them, even though reference order is what Roslyn uses to resolve duplicate simple names within a reference set. The four null-forgiving sites are the worst of the set: an absent `TRUSTED_PLATFORM_ASSEMBLIES` surfaces there as a `NullReferenceException` inside a LINQ projection rather than as the described `InvalidOperationException` the other 14 sites raise. | 23 files; `eng/testing/TestMetadataReferences.cs:16-24`; `SharpProof.Analyzer.Test/AnalyzerTestHost.cs:264-277`; `SharpProof.Effects.Test/EffectTestHost.cs:300-310`; `SharpProof.ContractForGenerator.Test/GeneratorTestHost.cs:194-211`; `SharpProof.Gates/AnalyzerGateHost.cs:154-166`; `SharpProof.Frontend.Test/ProgramLoweringTests.cs:890-893`, `FrontendLoweringTests.cs`, `OpaqueSemanticIdentityTests.cs`; `SharpProof.Specs.Test/ApiSpecTests.cs:1186-1192`; `SharpProof.Worker.Test/WorkerTests.cs`, `ScalarDifferentialMatrixTests.cs`, `ProtocolJsonTests.cs`, `ExceptionIdentityReplayTests.cs` |
| R730 | **The shared sources are not under-distributed - they are unreferenced where they are already compiled.** `eng/testing/TestMetadataReferences.cs` is linked into exactly two projects, `SharpProof.Contracts.Test` and `SharpProof.Worker.Test` (`Directory.Build.props:84-88`). Within those two projects, **9 files call `TestMetadataReferences` and 5 files build the same references by hand instead** - `ContractApiIdentityTests`, `ExceptionIdentityReplayTests`, `ProtocolJsonTests`, `ScalarDifferentialMatrixTests`, and `WorkerTests`. The helper is on their compile line; nothing points at it. This reframes R729, R724-R725, and R726-R728 as one problem rather than three: for the process runner, the temporary directory, and the metadata references alike, a correct shared implementation exists, is reachable, and loses to a locally written copy. The missing mechanism is a check, not a helper. The repository already has the shape of one - `BoundaryEnforcementTests` asserts a *set equality* between approved generated outputs and discovered generated files, and `DependencyAutomationTests` pins automation budgets by count - so a test asserting "no file in a project that links `TestMetadataReferences` also reads `TRUSTED_PLATFORM_ASSEMBLIES` directly", or a budget on the count of such reads, is the same technique applied to a new axis. Without it, every future test file is a fresh coin flip and the counts in R729, R724 and R726 grow. | `Directory.Build.props:84-88`; users at `SharpProof.Contracts.Test/ContractForMetadataSignatureTests.cs`, `ContractTestCompilation.cs`, `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs`, `CompilerCallableLowererTests.cs`, `CompilerCallableLowererWaveSixRegressionTests.cs`, `CompilerManifestArtifactTests.cs`, `CompilerRelationalSummaryProviderTests.cs`, `CompilerRuntimeSymbolArtifactTests.cs`, `CompilerSourceLocationAuthorityTests.cs`; hand-rolled in the same two projects at `SharpProof.Contracts.Test/ContractApiIdentityTests.cs`, `SharpProof.Worker.Test/{ExceptionIdentityReplay,ProtocolJson,ScalarDifferentialMatrix,Worker}Tests.cs` |

### Checked and not proposed (part two hundred forty-nine)

- **The divergences are not all errors.** `GeneratorTestHost` appends the
  `ContractForAttribute` assembly rather than `Contract`'s, which is correct for
  what it tests; `EffectTestHost` deliberately omits any SharpProof reference; and
  `TestMetadataReferences.WithSharpProof` checks whether the contract assembly is
  already present before adding it, which `AnalyzerTestHost:274-276` and
  `AnalyzerGateHost:163-165` do not - those two can add a second
  `MetadataReference` for a file already in the trusted set. A consolidation should
  keep the three distinct reference sets the shared class already exposes
  (`Platform`, `WithSharpProof`, `CoreLibraryOnly`) and add a fourth for the
  generator case, not collapse them to one.
- The `OrderBy(..., Ordinal)` in the five sorting sites is **not** proposed for
  removal. Two of the five (`WorkerTests`, `WorkerPerformanceProbe`) also filter to
  a fixed `RequiredReferenceFileNames` set, which reads as a deliberate determinism
  choice for fixtures whose output is hashed. The finding is that the choice is
  made five times out of twenty-three with nothing recording why, not that either
  answer is wrong.
- `Tools/SharpProof.Fuzz/FrontendFuzzing.cs` and
  `SharpProof.Testing/IrCSharpDifferentialOracle.cs` are in the count but are not
  test projects and do not receive `eng/testing` sources. They would need the
  `Compile Include ... Link` treatment rather than a using-directive, so they
  belong to a later phase of R730 than the five in-project cases.

### Status (part two hundred forty-nine)

R729 is partially completed: all five direct TPA readers in the two projects
already linked to `TestMetadataReferences.cs` now use its shared platform or
filtered-reference APIs; the remaining readers are in projects that do not link
that source and remain outside this migration. R730 is completed for the linked
projects: `SharedTestInfrastructureTests` now fails if either project reintroduces
a direct `TRUSTED_PLATFORM_ASSEMBLIES` read, providing the missing anti-regression
mechanism behind the helper adoption.

## Second survey, part two hundred fifty: R731 - IsPackable is a second, ungated statement of what ships

Turning from test code to build configuration: a census of `IsPackable` across all
60 `.csproj` files against the manifest that actually decides what is packaged.

| ID | Finding | Evidence |
|---|---|---|
| R731 | **`IsPackable` is declared 24 times across the 60 projects, it decides nothing, and its default runs the wrong way.** The authority on what ships is `scripts/package-projects.json` - three projects, pinned in `eng/acceptance/contract.json` at two places, read by six scripts, and asserted by `PackagedProductFeed.cs:296-322` ("package-projects.json must list the three product ...") and `ArchitectureTests.cs:1924-1969`. Every pack invocation in the repository is manifest-driven and per-project: `Invoke-SharpProofContainer.ps1:534-550`, `Invoke-SharpProofPackageTests.ps1:389-395`, and `Test-SharpProofSamples.ps1:233-252` each read the manifest and run `dotnet pack <project>` in a loop. **Nothing ever packs the solution**, so `IsPackable` has no live effect at all. Against that, `Directory.Build.props:3-24` sets `IsPackable=false` as an unconditional root default (with a condition preserving explicit overrides), which covers all projects importing the file. The remaining 41 default to *packable*, so **23 non-test projects each hand-write `<IsPackable>false</IsPackable>`** - and **15 non-test, non-shipping projects do not**, leaving them silently packable: `SharpProof.Analyzer`, `SharpProof.CompilerCollector`, the 5 `eng/pilots/` projects, and the 8 `samples/` projects. **No test asserts anything about `IsPackable`**; outside the csproj files and this ledger the identifier appears only in `Directory.Build.props`. The result is 24 declarations of a fact that is already stated once, authoritatively and under gate, elsewhere - inconsistent across 15 projects, and with the inconsistency invisible because the property is inert. Setting `IsPackable=false` centrally and letting the three manifest projects opt in removes 23 lines, makes the default match the 57-of-60 case, and makes the property agree with the manifest instead of shadowing it. | `scripts/package-projects.json`; `Directory.Build.props:3-24`; 23 opt-outs incl. `SharpProof.Analyzer.Core`, `SharpProof.BuildTasks`, `SharpProof.CompilerArtifact`, `SharpProof.Contracts`, `SharpProof.Effects`, `SharpProof.Frontend`, `SharpProof.Gates`, `SharpProof.Host`, `SharpProof.Ir`, `SharpProof.Smt`, `SharpProof.Specs`, `SharpProof.Worker`, `SharpProof.Worker.Protocol`; silent at `SharpProof.Analyzer/SharpProof.Analyzer.csproj`, `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj`, `eng/pilots/*/`, `samples/*/`; `SharpProof.ArchitectureTest/ArchitectureTests.cs:1951-1968`; `SharpProof.Package.Test/PackagedProductFeed.cs:296-322` |

### Checked and not proposed (part two hundred fifty)

- **The `GeneratePackageOnBuild` gate is correct and should not be touched.**
  `ArchitectureTests.PackageFeedConstructionIsDemandDriven` loads each manifest
  project's XML and requires `GeneratePackageOnBuild` to be absent or `false`, with
  a message explaining that package creation is reserved for the explicit container
  `pack` command. That is the check `IsPackable` looks like it should be and is
  not. If R731 is actioned, the natural place for an `IsPackable` assertion is
  this same test.
- **No project both appears in the manifest and declares `IsPackable=false`.** The
  two statements do not currently contradict each other; the finding is that
  nothing prevents them from doing so, and that 15 projects have no statement at
  all. Recorded so this is not later reported as a live defect.
- **The three `DiagnosticDescriptorCatalogTests.cs` files are exemplary, not
  duplication.** `SharpProof.Analyzer.Test`, `SharpProof.ContractForGenerator.Test`,
  and `SharpProof.Meta.Analyzers.Test` each hold a 16-17 line file whose entire body
  is one `DiagnosticDescriptorCatalogAssertions.AssertOutput(<name>, <assembly>)`
  call. They share a name and a shape but cannot be merged: each names its own
  project's assembly, which is only referencable from that project. This is the
  correct end state for the shared-source pattern - a per-project entry point over
  one shared implementation - and is worth citing as the target shape for R724-R725,
  R726-R728, and R729-R730. Do not propose collapsing them.
- The other three `eng/testing` sources are **fully adopted**: every project that
  links `ApiSpecTestFacets`, `DiagnosticDescriptorCatalogAssertions`, or
  `DictionaryAnalyzerConfigOptions` uses it, and no project re-implements one. The
  low-adoption problem in R730 is specific to `TestMetadataReferences` and
  `TempDirectory`; it is not a property of the mechanism.

### Status (part two hundred fifty)

R731 completed: non-shipping projects now inherit a false packability default,
the three manifest projects explicitly opt in, and the isolated samples/pilots
build roots carry the same false default because they intentionally do not import
the root props. The 23 project-level false declarations are gone while the
manifest remains the packaging authority.

## Second survey, part two hundred fifty-one: R732-R733 - the analyzer project preamble

Six projects are Roslyn analyzer assemblies. Comparing their `.csproj` files
side by side against the marker property the build already defines for them.

| ID | Finding | Evidence |
|---|---|---|
| R732 | **`SharpProof.Analyzer.Core` is the only one of the six analyzer projects without `IsRoslynAnalyzer`, so the analyzer-hosting rules are off for the assembly that holds the analysis.** `Directory.Build.targets:25-27` turns on `EnforceExtendedAnalyzerRules` for `'$(IsRoslynAnalyzer)' == 'true'`. Five projects set it - `SharpProof.Analyzer`, `SharpProof.CompilerCollector`, `SharpProof.CompilerProbe.TestAsset`, `SharpProof.ContractForGenerator`, `SharpProof.Meta.Analyzers`. `SharpProof.Analyzer.Core` does not, although it is `netstandard2.0`, carries the same `Microsoft.CodeAnalysis.Analyzers` reference, suppresses the same `RS2002;RS2003`, and is what `SharpProof.Analyzer` is a thin wrapper over. **The repository's other banned-API mechanism does not cover the gap**: `BannedSymbols.txt` bans compilation mutation, speculative binding, `GetSymbolsWithName`, source reparsing, and `ToDisplayString` - soundness concerns - and contains nothing about the analyzer-hosting APIs (file and directory access, `Environment`, wall-clock time, process creation) that the extended rules exist to catch. The gap is **latent, not live**: a scan of `SharpProof.Analyzer.Core` finds no use of any such API today. But that is exactly the state in which a guard is worth having, and the rule is demonstrably active elsewhere - `SharpProof.CompilerProbe.TestAsset` explicitly suppresses `RS1035` to permit it. | `Directory.Build.targets:25-27`; `SharpProof.Analyzer.Core/SharpProof.Analyzer.Core.csproj:1-9`; `SharpProof.Analyzer/SharpProof.Analyzer.csproj:4`; `SharpProof.CompilerCollector/SharpProof.CompilerCollector.csproj:4`; `SharpProof.CompilerProbe.TestAsset/SharpProof.CompilerProbe.TestAsset.csproj:7,10`; `SharpProof.ContractForGenerator/SharpProof.ContractForGenerator.csproj:5`; `SharpProof.Meta.Analyzers/SharpProof.Meta.Analyzers.csproj:4`; `BannedSymbols.txt` |
| R733 | **The same six projects repeat one four-line package block and take three different positions on analyzer release tracking, with no rule distinguishing them.** All six carry a byte-identical `<PackageReference Include="Microsoft.CodeAnalysis.Analyzers">` with `<PrivateAssets>all</PrivateAssets>` and the same six-token `<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>` - 24 lines expressing one fact, when `Directory.Build.props` already conditions five `ItemGroup`s on marker properties and `IsRoslynAnalyzer` is the marker for exactly this set. Five of the six also reference `Microsoft.CodeAnalysis.CSharp`. The release-tracking suppressions then split three ways: `$(NoWarn);RS2002;RS2003` in `SharpProof.Analyzer.Core`, `SharpProof.Analyzer`, and `SharpProof.CompilerCollector`; `$(NoWarn);RS2008` in `SharpProof.ContractForGenerator` and `SharpProof.Meta.Analyzers`; and `$(NoWarn);RS1035;RS2008` in `SharpProof.CompilerProbe.TestAsset`. The RS2002/RS2003 group is the notable one, because **`SharpProof.Analyzer` is the only project that actually carries `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`** as `AdditionalFiles`, and it is in that group - so the release-tracking rules are suppressed on the one assembly whose release files they exist to check, while two other projects suppress the different rule that asks for tracking to be enabled at all. Six projects, three answers, nothing recording which is intended. | `SharpProof.Analyzer/SharpProof.Analyzer.csproj:8-19`; the identical block in the other five csproj files; `Directory.Build.props:92-98` for the marker-driven `ItemGroup` |

### Checked and not proposed (part two hundred fifty-one)

- **`IsRoslynAnalyzer` is not a dead property.** It is read once, at
  `Directory.Build.targets:25`. An earlier reading of this data treated it as
  declared-but-unused; that is wrong and is recorded here so it is not filed later.
- **The 23 `IsPackable=false` opt-outs and this preamble are the same shape but
  should not be merged into one item.** R731 is about a property that decides
  nothing; R733 is about a package reference that decides what compiles. Both
  are candidates for the marker-driven `ItemGroup` treatment
  `Directory.Build.props` already uses six times, which is the common fix, but they
  fail differently if got wrong.
- `Tools/SharpProof.Fuzz/SharpProof.Fuzz.csproj:12` lists
  `CA1515;CA2007;CA5394` in its `NoWarn`, which also appear at
  `Directory.Build.targets:22`. This is **not** redundant: the central line is
  conditioned on `'$(IsTestProject)' == 'true'`, which the SDK sets from the test
  package reference, and `SharpProof.Fuzz` is a tool project rather than a test
  project. Checked and correct as written.

### Status (part two hundred fifty-one)

R732 completed: `SharpProof.Analyzer.Core` now sets `IsRoslynAnalyzer`, so the
largest analyzer assembly inherits the same extended analyzer rules as its five
sibling projects; its canonical build succeeds with 0 warnings and 0 errors.
R733 is partially completed: the identical `Microsoft.CodeAnalysis.Analyzers`
package reference is now one marker-driven item in `Directory.Build.props`,
covering all six projects and removing six repeated blocks. The intentionally
different RS2002/RS2003, RS2008, and RS1035 release-tracking suppressions remain
project-local and deferred until their policy is made explicit; analyzer
architecture tests pass (10/10).

## Second survey, part two hundred fifty-two: R734-R735 - the ungated half of the assembly boundary

The project-reference graph is one of the most heavily gated things in this
repository. Its counterpart - which assemblies may see another's internals - has
78 declarations and no assertion of any kind.

| ID | Finding | Evidence |
|---|---|---|
| R734 | **Three `InternalsVisibleTo` grants are provably dead: the grantee cannot reference the granter at all.** Computing the transitive `ProjectReference` closure of all 60 projects shows `SharpProof.CompilerArtifact` reaches exactly `{SharpProof.Ir, SharpProof.Meta.Analyzers, SharpProof.Worker.Protocol}` and `SharpProof.Worker` reaches nine projects not including `SharpProof.Contracts`. Against that: `SharpProof.Contracts` grants internals to `SharpProof.CompilerArtifact` and to `SharpProof.Worker`, and `SharpProof.Frontend` grants internals to `SharpProof.CompilerArtifact`. None of the three grantees can see the granting assembly, so none of the three grants can ever take effect. This is not an artefact of the measurement: `Directory.Build.props` and `Directory.Build.targets` inject no `ProjectReference`, no source file under `SharpProof.CompilerArtifact/` names `SharpProof.Contracts` or `SharpProof.Frontend`, and neither appears in that project's generated global usings. **The grants are residue from a structure the architecture tests now forbid.** `BoundaryEnforcementTests.ThinAnalyzerHasOnlyCurrentFrontendDependencies:226-240` asserts that `SharpProof.Analyzer`'s transitive closure `Does.Not.Contain("SharpProof.CompilerArtifact")` - a separation actively defended in one direction while three csproj files still hand out internals access across it in the other. | `SharpProof.Contracts/SharpProof.Contracts.csproj:19,24`; `SharpProof.Frontend/SharpProof.Frontend.csproj:22`; `SharpProof.CompilerArtifact/SharpProof.CompilerArtifact.csproj:10-18`; `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:226-240` |
| R735 | **`InternalsVisibleTo` is the only part of the assembly boundary with no gate.** The reference graph is pinned hard: `BoundaryEnforcementTests` asserts `SharpProof.Analyzer`'s direct references by exact set equality, asserts a forbidden member of its transitive closure, requires every soundness-critical project to reference the meta-analyzer, checks the meta-analyzer does not reference itself, and pins the solution's project list literally (`:379-453`). `DependencyAutomationTests` pins automation budgets by count. Against 78 `InternalsVisibleTo` declarations across 18 projects, **nothing asserts anything** - outside the csproj files, the identifier appears in one archived note (`eng/agent-notes/archive/queue.md`) and this ledger. That asymmetry is what let R734 happen and what will let it happen again: a `ProjectReference` cannot be added without a test noticing, but internals access can be granted to any assembly, or left behind when a reference is removed, silently. The grant list is also the more consequential of the two, because it is what actually widens an assembly's API surface to another assembly. The existing shape applies directly - an exact-set assertion over the 78 grants in the same file that already pins the project list - and it would have caught all three dead grants. | 78 grants in 18 csproj files; `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:226-301,379-453`; `SharpProof.ArchitectureTest/DependencyAutomationTests.cs`; `eng/agent-notes/archive/queue.md` |

### Checked and not proposed (part two hundred fifty-two)

- **The other 75 grants all have a reference path** and are not proposed for
  removal. This part's check is the decidable one - can the grantee see the granter
  at all - not the stronger question of whether each grantee actually touches an
  `internal` member of its granter. That second question would need a compile and
  a symbol walk; it is worth doing once if R735 is actioned, since a grant that
  is reachable but unused is exactly as invisible as a dead one, but it is not
  claimed here and none of the 75 is asserted to be unnecessary.
- The three dead grants are **harmless today**, not a soundness hole: an
  `InternalsVisibleTo` to an assembly that cannot reference you grants nothing.
  R734 is about removing residue and about what its survival says, which is
  R735.
- `SharpProof.Contracts` also grants to `SharpProof.ContractForGenerator`,
  `SharpProof.Analyzer`, `SharpProof.Analyzer.Core`, and
  `SharpProof.CompilerCollector`, all of which do reference it. Only entries 19 and
  24 of that six-line block are dead, so this is not a case of a whole block being
  stale.

### Status (part two hundred fifty-two)

R734 completed: the three unreachable `InternalsVisibleTo` grants to
`SharpProof.CompilerArtifact` and `SharpProof.Worker` were removed; the
boundary tests and affected builds retain their reachable grants. R735 completed:
`BoundaryEnforcementTests` now compares the complete approved grant set against
the project files, so stale or newly added assembly access is visible; the
boundary suite passes 13/13.

## Second survey, part two hundred fifty-three: R736-R737 - repeated fixture metadata work

The next pass returns to small production helpers that are not themselves
authorities, but repeatedly recompute data that is stable for one operation.

| ID | Finding | Evidence |
|---|---|---|
| R736 | **`AnalyzerSession.GetUnrecordedSelectedSemicolonAccessors` asks Roslyn for the first declaring syntax reference twice per sort key.** After filtering the concurrent set, its `OrderBy` key calls `method.DeclaringSyntaxReferences.FirstOrDefault()` to obtain the file path and its `ThenBy` key calls the same property and search again to obtain the span. The result is stable for a given normalized method, so a single projection to `(method, firstReference)` before sorting can preserve the null-path and `int.MaxValue` ordering while removing the repeated symbol/reference lookup and making the sort's metadata dependency explicit. This is a small end-of-analysis cost, not a semantic defect; it is worth reducing because the method is already materializing a new ordered array. | `SharpProof.Analyzer.Core/AnalyzerSession.cs:262-278` |
| R737 | **`CorpusCatalog.CreateCases(CorpusSeed)` materializes every variant, then scans the materialized array once to find the baseline and again to suppress one duplicate.** `Variants.Select(...).ToArray()` creates all cases; `First` performs a second traversal to locate the baseline source; and the returned `Where` performs another traversal when `CreateSyntheticCases` consumes it. The baseline case can be created once up front and the remaining variants appended directly to a builder, comparing only the alpha-renamed case against the already-known baseline. That keeps variant order and the deliberate duplicate suppression while removing the temporary array plus two whole-array passes per seed. | `SharpProof.Gates/Corpus/CorpusCatalog.cs:24-35,264-274`; `SharpProof.Gates/Corpus/CorpusCatalog.cs:276-344` |

### Checked and not proposed (part two hundred fifty-three)

- R736 is not a proposal to cache `DeclaringSyntaxReferences` across the
  compilation. The cache would cross Roslyn lifetime and normalization boundaries;
  the proposed projection is local to one sort only.
- R737 is not a proposal to remove the alpha-renaming variant. Effect seeds
  intentionally produce an identical source for that variant, and the existing
  filter is the test corpus's explicit way to avoid duplicating that case. The
  reduction is only in how the baseline and filter are carried out.
- `CorpusCatalog.CreatePrelude` intentionally repeats the input expression in the
  `IfTrue` and reorder variants. Those strings are the metamorphic transformations
  being tested, not accidental duplicate computation in the catalog implementation.

### Status (part two hundred fifty-three)

R736 completed: selected semicolon accessors now snapshot each method's first
declaration reference once before sorting, preserving null-path and span ordering
while removing the repeated Roslyn lookup. R737 completed: synthetic corpus
construction now creates the baseline once and appends each variant to a builder,
preserving variant order and alpha-rename suppression without the temporary array
or repeated whole-array scans.

## Second survey, part two hundred fifty-four: R738, and four dead-code searches that found nothing

Two dead-code sweeps and a compiler-visible-property census. The sweeps are
negative results worth recording; the census produces one finding.

| ID | Finding | Evidence |
|---|---|---|
| R738 | **The analyzer's compiler-visible property vocabulary is declared four times in three groupings, and the two copies that are not gated are the repository's own consumer configurations.** The nine properties forwarded to the analyzer are partitioned **disjointly and exactly** between the two shipping packages - `SharpProof.Package/buildTransitive/SharpProof.props:14` declares three (`SharpProofProfile`, `SharpProofFeatures`, `SharpProofSpecificationPacks`) and `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:31` declares the other six, with empty intersection. `SharpProof.AnalyzerConsumer.props:12` and `eng/self-application/SharpProof.SelfApplication.props:33` each restate **all nine, byte-identically to each other**. `ArchitectureTests:743-901` freezes the union across the five shipping build files against `eng/acceptance/preview-interface.v1.json`, and that scoping is deliberate and correct - but it means the two internal files are outside the gate entirely. So the invariant "the repo's own consumer configuration sees exactly what a real consumer sees" is stated twice, in full, and checked nowhere. Adding a tenth property to the shipping partition updates the frozen contract and passes; the smoke consumer (`SharpProof.Smoke.Net472`, which imports `SharpProof.AnalyzerConsumer.props`) and the self-application build would silently not see it, so the repository would stop dogfooding the property it just shipped. **There is no divergence today** - both retired properties are absent from both files, and the nine match - which is the right moment to note it. The fix is not a fourth declaration: it is deriving the two internal lists from the two shipping ones, or extending the existing test to assert equality between the union of the shipping partition and each internal file. | `SharpProof.Package/buildTransitive/SharpProof.props:14`; `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.props:31`; `SharpProof.AnalyzerConsumer.props:12`; `eng/self-application/SharpProof.SelfApplication.props:33`; `SharpProof.ArchitectureTest/ArchitectureTests.cs:766-901`; `SharpProof.Smoke.Net472/SharpProof.Smoke.Net472.csproj:10`; `eng/acceptance/preview-interface.v1.json` (26 active, 2 retired) |

### Checked and not proposed (part two hundred fifty-four)

- **There is no dead code in production C# by an unreferenced-method measure.** A
  scan of every method declaration in the 23 production assemblies against a
  repository-wide identifier index found 27 names occurring exactly once - and all
  27 are framework dispatch targets, not dead code: 12 `public override Visit*`
  overrides of Roslyn's `OperationVisitor` in `RoslynOperationLowerer`, 14
  `ISignatureTypeProvider<ScalarType,...>` implementation methods in
  `CompilerImplementationIlSummaryLowerer`, and `BoundedReadStream.Seek`. **Nothing
  else is unreferenced.** This is worth recording next to R310: the production
  complexity ratchet has no slack of this kind to reclaim, so a future attempt to
  lower the measured figures must come from restructuring, not deletion.
- **There are no dead `SharpProof*` MSBuild properties.** Three candidates survived
  a `$(...)`-and-condition search and all three are read by mechanisms that search
  does not cover: `SharpProofAnalyzerRole` is `<Analyzer>` item metadata consumed by
  `PerformanceGate.cs:1398,1768` and `Test-SharpProofPackageConsumers.ps1:245,258`;
  `_SharpProofCompilationTargetFramework` and `_SharpProofProjectDirectory` are
  `CompilerVisibleProperty` entries read by `FinalCompilationCollector.cs:8-9` as
  `build_property.*` options. Recorded so none of the three is later filed as
  unused.
- **The three-key option probe is deliberate alias tolerance, not dead code.**
  `AnalyzerConfiguration.TryGet:199-221` probes `option.Key`,
  `"build_property." + option.Key`, and `"build_property." + option.BuildPropertyName`
  for every option. Only the first and third can come from MSBuild, but the second
  is reachable from a hand-written `.editorconfig` line, so it is a tolerated
  spelling rather than an unreachable branch. The same applies to
  `TryGetRetiredMode:178-197`.
- **The retired `SharpProofMode` option is defended three independent ways** and
  none of the three should be removed as redundant:
  `SharpProof.ConsumerContract.props:16-17` raises an MSBuild `<Error>`
  `BeforeTargets="CoreCompile"`; the name is absent from every
  `CompilerVisibleProperty` list, which `ArchitectureTests:861` asserts positively
  for each retired property; and `AnalyzerConfiguration` reports it as a removed
  option for the analyzer-config spelling that survives both. The layers cover
  different entry points, and `ArchitectureTests:862-865` additionally requires the
  string `"<name> was removed"` to appear in the shipping build surface. This is the
  strongest retirement mechanism found in the survey.
- The `ArchitectureTests` frozen-surface check should **not** be widened to include
  `SharpProof.AnalyzerConsumer.props` or `SharpProof.SelfApplication.props`. Its
  correctness depends on covering exactly what ships; R738 asks for a second,
  separate assertion, not a longer file list.

### Status (part two hundred fifty-four)

R738 is `refuted/stale`: the current architecture test already enforces the
proposed dogfood-to-shipping compiler-visible-property equality for both
internal consumer configurations. The focused test passed.

## Second survey, part two hundred fifty-five: R739 - duplicate substitution snapshots

The IR utility pass found one more local allocation that its own comment makes
especially easy to overlook: the code correctly snapshots a mutable view, but
then builds a second container from that snapshot.

| ID | Finding | Evidence |
|---|---|---|
| R739 | **`IrSubstitution.Substitute` materializes the replacement map twice.** The `IReadOnlyDictionary` input is first copied to `replacementSnapshot` with `ToArray()`, then copied again into `replacementMap` with `ToDictionary()`, and the array is retained only for validation and the empty check. A single `ToDictionary` enumeration is already the required one-time snapshot: validate that dictionary's entries and use its `Count`, preserving duplicate-key failure, caller-mutation isolation, type checks, and the empty-map fast path. This removes one array allocation and one full copy for every substitution call without changing the explicit non-immutable-view boundary documented immediately above the code. | `SharpProof.Ir/IrSubstitution.cs:31-53` |

### Checked and not proposed (part two hundred fifty-five)

- This does not remove the replacement dictionary. `Rewrite` performs keyed
  variable lookup throughout the IR DAG, so the dictionary remains the correct
  representation after validation.
- The one-variable overload's one-entry dictionary is a separate API convenience;
  R739 concerns the avoidable snapshot array inside the general overload.
- `IrSubstitution` still needs to validate the replacement values against the
  factory before rewriting. Removing that validation would change the fail-closed
  type boundary and is not a reduction candidate.

### Status (part two hundred fifty-five)

R739 completed: `IrSubstitution` now materializes the caller's replacement map
once, validates and counts that snapshot directly, and preserves the existing
duplicate-key, type-check, empty-map, and caller-mutation behavior.

## Second survey, part two hundred fifty-six: R740-R741 - the PowerShell twin of R724

Re-measuring PowerShell duplication across all 100 tracked `.ps1`/`.psm1` files.
Both findings are the same shape as R724 and R730 in the other language, and the
first one has a correlation that identifies the cause exactly.

| ID | Finding | Evidence |
|---|---|---|
| R740 | **Nine raw `ProcessStartInfo` constructions across eight PowerShell files repeat the same preamble, and the three that drop `CreateNoWindow` are precisely the three the shared module cannot serve.** All nine set `UseShellExecute = $false`; eight set `RedirectStandardOutput`; **six set `CreateNoWindow = $true` and three do not** - and the three are `Invoke-SharpProofPackageTests.ps1:617`, `Invoke-SharpProofSemanticTests.ps1:430`, and `Invoke-SharpProofTrustedMutationsParallel.ps1:313`, the repository's three parallel *test* schedulers. The correlation is not coincidence. `SharpProof.ContainerExecution.psm1` exports `Invoke-SharpProofDotnetInvocation`, which is **synchronous** - it shells to `Invoke-SharpProofDotnet.ps1` and waits - so a scheduler that needs concurrent `Process` handles cannot use it and writes its own. Yet the module **already contains the asynchronous form**: `Invoke-SharpProofParallelDotnetBuilds:361-420` builds the identical preamble at line 412, correctly including `CreateNoWindow = $true`, for parallel *builds*. The capability exists, is right, and is not reachable for tests, so it was rewritten three times and each rewrite lost the same option. `Test-SharpProofPilots.ps1:146-151` is a fourth variant that sets `CreateNoWindow` but redirects neither stream. Together with R724's 55 C# sites this is roughly 64 process-start sites across the repository, in two languages, each with a shared helper covering only the synchronous case. | `scripts/SharpProof.ContainerExecution.psm1:322,361-420` (async form, `CreateNoWindow` at 415); `scripts/Invoke-SharpProofPackageTests.ps1:617-622`; `scripts/Invoke-SharpProofSemanticTests.ps1:430-435`; `scripts/Invoke-SharpProofTrustedMutationsParallel.ps1:313-318`; `scripts/Test-SharpProofPilots.ps1:146-151`; `scripts/Invoke-SharpProofDotnet.ps1`; `scripts/Invoke-SharpProofLoop.ps1:27-33`; `scripts/Test-SharpProofCoverage.ps1:73-79` |
| R741 | **`Invoke-SharpProofPackageTests.ps1` and `Invoke-SharpProofSemanticTests.ps1` share 107 lines in blocks of six or more, and the shared part is the scaffolding around a core they already share correctly.** Both import `SharpProof.ContainerExecution.psm1` and both call `New-SharpProofIsolatedTestOutput` for the hard part - the hard-linked, per-shard instrumented output tree. What is duplicated is everything surrounding it: the coverage argument validation (`:59-67` / `:47-55`, "CoverageSettings and CoverageResultsDirectory must be supplied together", nine lines byte-identical), the resolution of both paths (`:68-80` / `:59-71`, thirteen lines), the `.sharpproof-coverage-output-<guid>` root construction and its deletion in `finally` (`:88-96` / `:74-82`), the prior-timing-file loop over `@($canonicalTimingOutput, $timingOutput)` under `-Fast` (`:279-288` / `:138-147`), the parameter block and `Set-StrictMode`/`$repositoryRoot` preamble (eleven lines), and the `bin/<Configuration>/net9.0` output-layout convention spelled out in both. That is 107 of 838 and 593 lines. The two schedulers genuinely differ - one dequeues shards with an exclusivity rule, the other picks slot-weighted tasks - and **that difference should be preserved**; the finding is that the coverage plumbing wrapped around both is not scheduler-specific and is the natural next export from a module both files already import. | `scripts/Invoke-SharpProofPackageTests.ps1:18-96,279-288,617-660`; `scripts/Invoke-SharpProofSemanticTests.ps1:18-82,138-147,430-473`; `scripts/SharpProof.ContainerExecution.psm1:499-553` |

### Checked and not proposed (part two hundred fifty-six)

- **PowerShell duplication is now concentrated, not diffuse.** Across 100 files
  there are only 36 eight-line windows appearing in two or more files, and they
  cluster into four pairs: the R741 pair (17 windows),
  `Invoke-SharpProofGateEvidence.ps1` / `Test-SharpProofDependencyAudit.ps1` (7),
  `Assert-SharpProofFuzzRunnerResult.ps1` / `SharpProof.FuzzEvidenceLifecycle.ps1`
  (5), and `Invoke-SharpProofSemanticTests.ps1` /
  `Invoke-SharpProofTrustedMutationsParallel.ps1` (3). Everything else is one or
  two windows. The generator family, which earlier parts measured as the largest
  source of PowerShell repetition, no longer appears above threshold except for a
  single window shared by `Generate-CompilerArtifactModel.ps1` and
  `Generate-ProtocolModel.ps1` - the schema-preamble validation already described
  by R459.
- The `Assert-SharpProofFuzzRunnerResult.ps1` / `SharpProof.FuzzEvidenceLifecycle.ps1`
  pair shares a ~30-line bounded JSON reader - a `FileStream` opened
  `Open`/`Read`/`Read`, a length check against 1 MiB, a manual read loop to fill
  the buffer, strict UTF-8 decoding, and `JsonDocument.Parse`. It is worth folding
  into one helper, but it is **not** proposed separately here: it is the same item
  as the fuzz-evidence duplication already filed, and splitting it across two IDs
  would double-count.
- `Invoke-SharpProofDotnetInvocation` is **not** at fault and should not be
  reshaped. It is correct for the synchronous case, which is the majority of
  callers. R740 asks for an additional exported async form, extracted from
  `Invoke-SharpProofParallelDotnetBuilds`, not for a change to the existing one.

### Status (part two hundred fifty-six)

R740 is `complete`: `New-SharpProofParallelProcessStartInfo` now owns the
parallel-process preamble, argument population, environment propagation, and
hidden-window/stream-redirection defaults for the build, package-test,
semantic-test, and trusted-mutation schedulers. The scheduling policies remain
local, and the architecture scheduler roster was brought back into sync with
the fixture set while validating the change. R741 is `partially applied`: the
two schedulers now share coverage validation/path resolution, isolated-root
creation and cleanup, and collector argument construction. Their parameter
blocks, scheduler-specific timing histories, and output-layout choices remain
local because those parts carry different contracts.

## Second survey, part two hundred fifty-seven: R742 - repeated primary-constructor syntax tests

The effects layer has one small duplicated Roslyn-shape predicate in a shared
ownership helper. The two callers ask the same question at different semantic
entry points.

| ID | Finding | Evidence |
|---|---|---|
| R742 | **`PrimaryConstructorParameterOwnership` repeats the declaring-syntax walk for the same primary-constructor parameter shape.** `IsReceiverBacked` enumerates `parameter.DeclaringSyntaxReferences`, parses each reference, and accepts a `ParameterSyntax` whose grandparent is a `TypeDeclarationSyntax`; `IsPositionalRecordProperty` repeats the enumeration, parse, and grandparent test with the narrower `RecordDeclarationSyntax`. `RecordDeclarationSyntax` derives from `TypeDeclarationSyntax`, and the second method already requires `property.ContainingType.IsRecord`, so a shared `IsPrimaryConstructorParameter` predicate can own the one syntax walk while the public methods retain their separate receiver/property semantics. This removes duplicated Roslyn traversal and prevents the two tests from drifting as primary-constructor syntax evolves. | `SharpProof.Effects/PrimaryConstructorParameterOwnership.cs:5-42` |

### Checked and not proposed (part two hundred fifty-seven)

- The containing-type checks must remain separate. Receiver-backed ownership
  rejects static methods, same-constructor calls, and cross-type calls; positional
  record-property ownership additionally requires an implicitly declared getter.
- The shared predicate should return only the syntax-shape fact. It should not
  infer ownership or replace the existing symbol-equality guards.
- This is distinct from R388's captured-local classification overlap; neither
  finding proposes a broader symbol-cache that could cross compilation sessions.

### Status (part two hundred fifty-seven)

R742 completed: receiver and positional-record ownership now share one
primary-constructor syntax-shape helper while retaining their separate symbol,
record, getter, and ownership guards.

## Second survey, part two hundred fifty-eight: R743-R744, and a refinement to R733

The repository has **two** diagnostic-identifier vocabularies. One is generated
from a catalog and gated four ways. The other is raw string literals.

### Refinement to R733

R733 recorded that `SharpProof.Analyzer` suppresses `RS2002;RS2003` while being
the only project carrying `AnalyzerReleases.Shipped.md` and
`AnalyzerReleases.Unshipped.md`. That observation stands, but it needs a
correction that changes its weight: **a hand-written replacement gate exists and is
stronger than the suppressed rules.**
`SharpProof.Analyzer.Test/AnalyzerArchitectureTests.cs:231-280` parses both release
files, requires `Shipped` to be empty, asserts `unshipped.Keys` is equivalent to
the runtime `SupportedDiagnostics` ids, pins the count at 13, and then compares
category, severity, and notes per descriptor. Verified independently here: the
release file's 13 ids and the `analyzer` catalog's 13 ids match exactly in both
directions. So the suppression is deliberate and covered. R733's release-tracking
half should be read as "three projects take three positions with nothing recording
why", not as "the check is missing" - and any action on it must not remove this
test.

| ID | Finding | Evidence |
|---|---|---|
| R743 | **The launcher diagnostic vocabulary is raw string literals in four production assemblies, with no shared constant, while the analyzer's thirteen ids are generated from a catalog.** `SP0047` and `SP0048` are user-visible diagnostics emitted by the verifier launcher rather than by Roslyn - `docs/diagnostic-examples.md:222` states this deliberately - so their absence from the descriptor catalog is correct and is **not** the finding. The finding is that the pair is then written out by hand at every site that produces, validates, parses, or documents it: `SharpProof.Worker.Launcher/Program.cs:474` emits `"SP0048"`; `SharpProof.Worker.Launcher/SarifProjection.cs:41` emits `"SP0048"` again for the SARIF channel; `SharpProof.Host/VerifierDiagnosticTransport.cs:93` validates `diagnostic.Code is not ("SP0047" or "SP0048")`; `SharpProof.BuildTasks/RunVerifier.cs:1170-1176` parses both, spelling **each id twice per row** - once as `Code` and once inside the marker string - for eight literals in one four-row table; and `scripts/Test-SharpProofReadme.ps1:414,522` hard-codes the pair a fifth time. A search for a `const string` bound to either id finds nothing. Adding a third launcher diagnostic means finding all five sites unaided, and `BoundaryEnforcementTests.DiagnosticDescriptorsComeOnlyFromTheGeneratedCatalog:303-347` cannot help: it is scoped to `SharpProof.Analyzer.Core` and `SharpProof.Meta.Analyzers`, so `SharpProof.BuildTasks`, `SharpProof.Host`, and `SharpProof.Worker.Launcher` are outside it entirely. | `SharpProof.Worker.Launcher/Program.cs:474`; `SharpProof.Worker.Launcher/SarifProjection.cs:41`; `SharpProof.Host/VerifierDiagnosticTransport.cs:93`; `SharpProof.BuildTasks/RunVerifier.cs:1170-1176`; `scripts/Test-SharpProofReadme.ps1:414,522`; `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:303-347`; `eng/diagnostics/diagnostic-descriptors.v1.json` (13 analyzer ids, no SP0048) |
| R744 | **One SP0048 message is rendered twice, by two different mechanisms, into two machine-read output channels.** `Program.cs:472-476` builds it with `FormattableString.Invariant` over an interpolated string for the MSBuild channel. `SarifProjection.cs:41-46` rebuilds the identical text by string concatenation for the SARIF channel, and both recompute `User + Trusted` independently. The two agree today, character for character, so a consumer diffing MSBuild output against the SARIF report sees one message; nothing keeps that true. The renderings are also not equivalent in kind: one is **explicitly culture-invariant** and the other relies on ambient `int.ToString()`, so the invariance guarantee that was thought worth stating in one channel is simply absent in the other. For a product whose output is evidence, two hand-maintained spellings of one diagnostic message in two machine-read channels is the wrong number. | `SharpProof.Worker.Launcher/Program.cs:470-478`; `SharpProof.Worker.Launcher/SarifProjection.cs:36-46` |

### Checked and not proposed (part two hundred fifty-eight)

- **`SP0048`'s absence from the analyzer descriptor catalog is correct**, documented
  at `docs/diagnostic-examples.md:220-222` ("a verifier-launcher diagnostic, not a
  Roslyn analyzer descriptor"), and gated on the documentation side by
  `Test-SharpProofReadme.ps1:522`, which requires a help anchor for each launcher
  diagnostic. Do not propose adding it to `diagnostic-descriptors.v1.json`; R743
  asks for a shared constant, not a catalog entry.
- **`SP0047` legitimately exists in both worlds.** It is an analyzer descriptor id
  at `GeneratedDiagnosticDescriptors.generated.cs:124` *and* a launcher diagnostic
  parsed by `RunVerifier`. That is by design - the same condition is reported by
  the in-process analyzer and by the out-of-process verifier - and the two are not
  duplicate definitions of one thing. Only the literal spelling of the id at the
  launcher sites is at issue.
- **The analyzer id vocabulary itself is exemplary and needs nothing.** Thirteen
  ids, one catalog, one generated descriptor file, and four independent gates:
  generation staleness via `-Verify`, `DiagnosticDescriptorsComeOnlyFromTheGeneratedCatalog`
  forbidding hand-written `DiagnosticDescriptor` construction outside the generated
  file, `DiagnosticDescriptorCatalogAssertions` comparing runtime descriptors to the
  catalog from three test projects, and `AnalyzerArchitectureTests` comparing the
  release file. Cite this as the target shape for R743.

### Status (part two hundred fifty-eight)

R743 is `complete` for the production C# and README-validation paths: the two
launcher ids now have one shared vocabulary, including the verifier marker
parser, transport validation, launcher/SARIF projections, and README checks.
Analyzer descriptor literals and the independent analyzer-authority check remain
separate intentionally because they belong to the Roslyn descriptor/catalog
boundary. R744 is `complete`: MSBuild and SARIF now call one invariant shared
renderer for the SP0048 message, so the two machine-read channels cannot drift.

## Second survey, part two hundred fifty-nine: R745 - repeated response-summary scans

The protocol response assembler has a separate local form of the same repeated
enumeration pattern already seen in its classification helpers: summary fields
are derived independently from arrays that are already materialized.

| ID | Finding | Evidence |
|---|---|---|
| R745 | **`WorkerResultAssembler.Create` scans the same result arrays separately for each summary component.** After materializing `callables` and `claims`, it groups `claims` once for `OutcomeCounts`, groups the same `claims` again for `ReasonCounts`, and passes both arrays to `SummarizeAssumptions`, which performs another concatenation/filter/grouping traversal. A single response-summary accumulator can count outcomes, reasons, and assumption identities while visiting each materialized result once, then retain the existing canonical sort in `WorkerProtocolJson.Canonicalize`. This is distinct from R527/R540, which concern classification and callable-projection scans; it targets construction of the response's summary and removes repeated work on every assembled worker response. | `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:8-40,108-127` |

### Checked and not proposed (part two hundred fifty-nine)

- The validation-side `WorkerProtocolJson.ValidateSummary` recomputation should
  remain. It independently derives expected counts from untrusted response data;
  reusing the producer's accumulator there would weaken the validation boundary.
- Canonical sorting must remain after aggregation. Stable wire ordering is a
  separate protocol requirement from efficient summary construction.
- `CreateIncomplete`'s `ToLookup` is not included: it joins malformed claims to
  manifest callables and has a different purpose from the summary aggregation.

### Status (part two hundred fifty-nine)

R745 is `complete`: `WorkerResultAssembler.Create` now builds outcome counts,
reason counts, and assumption totals/conflict state with one accumulator pass
over the materialized callable and claim arrays. Validation still recomputes its
own summary independently at the trust boundary, and canonical sorting remains
unchanged.

## Second survey, part two hundred sixty: R746 - duplicate cache-path normalization

The protocol path helper has a smaller form of repeated canonicalization. Its
result is normalized after composition, so normalizing the project root first
does not add a distinct boundary.

| ID | Finding | Evidence |
|---|---|---|
| R746 | **`WorkerCachePath.Resolve` normalizes `projectDirectory` and then normalizes the composed path again.** The method first computes `root = Path.GetFullPath(projectDirectory)`, uses that only as the first operand of `Path.Combine`, and immediately wraps the result in a second `Path.GetFullPath`. For both the default `obj/SharpProof/cache` path and a configured path (including the rooted-path override behavior of `Path.Combine`), the final full-path call is the consumed canonicalization; composing from `projectDirectory` directly removes one normalization and one local variable. This is a narrow path-helper cleanup, not a proposal to drop the final absolute-path boundary. | `SharpProof.Worker.Protocol/WorkerCachePath.cs:5-14` |

### Checked and not proposed (part two hundred sixty)

- The final `Path.GetFullPath` must remain. It converts relative configured cache
  paths to absolute paths and canonicalizes the default path.
- Rooted configured paths still need `Path.Combine`'s existing override behavior;
  the candidate changes only where the single final normalization occurs.
- This is separate from R494 and R534, which concern repeated normalization in
  publication invalidation and marker derivation, respectively.

### Status (part two hundred sixty)

R746 is `complete`: `WorkerCachePath.Resolve` now composes from the supplied
project directory and performs only the final full-path normalization. The
default and configured cache paths retain their existing rooted-path behavior.

## Second survey, part two hundred sixty-two: R747, and R505 is fixed

### Correction: R505 is resolved

R505 recorded that `eng/container/entrypoint.sh` preserved ignored package inputs
with a third `ls-files --others --ignored --exclude-standard -- nupkgs/` pass while
`eng/container/loop-command.sh` had no `nupkgs` handling at all, despite ending in
an unrestricted `sp "$@"` that accepts `package-consumers -PackageSource nupkgs`.
**That is fixed.** `loop-command.sh:126-129` now captures the ignored package
inputs into the source manifest and `:178-181` does the same for the target
manifest, so the loop workspace and a container run now materialize the same
source. The commit is `d08d1d8be`, "Align loop package inputs with container runs".
Move R505 to the applied table. The finding below is what the fix left behind.

| ID | Finding | Evidence |
|---|---|---|
| R747 | **The source-snapshot policy is implemented twice in two shell scripts, and only one of the two has a test that executes it.** "What constitutes the source" is four decisions - the tracked tree at `HEAD`, the working-tree diff captured with `--binary --full-index --no-ext-diff`, untracked-but-not-ignored paths from `ls-files --others --exclude-standard -z`, and ignored paths under `nupkgs/` from `ls-files --others --ignored --exclude-standard -z -- nupkgs/`. `entrypoint.sh:104-140` implements it inline for a disposable task clone; `loop-command.sh:126-129,178-207` implements it again through manifest files for the private loop workspace. Both also repeat `git clone --quiet --shared --no-checkout`, `git apply --binary --whitespace=nowarn`, `git config --global --add safe.directory`, and `printf '/artifacts\n' >> .git/info/exclude`. **The test coverage is asymmetric in kind, not just in amount.** `ContainerSourceCleanlinessTests` has four tests that *run* `entrypoint.sh` against real Git fixtures - `CleanAndDevelopmentInputsRemainAdmissible`, `GitBoundCommandAcceptsRepositoryWithDifferentOwner`, `GitBoundCommandPreservesIgnoredPackageInputs`, and `DevelopmentSnapshotPreservesDirtyDeletedAndUntrackedFiles`. `loop-command.sh` - 213 lines, the largest shell script in the repository, holding a PID lock protocol, a snapshot-root containment check, and stale-file reconciliation against two manifests - is **never executed by any test**. Its only coverage is substring matching on its own source text in `BuildSchedulingTests:186-196,289-320`: `Does.Contain("source_files_root")`, `Does.Contain("SHARPPROOF_LOOP_SNAPSHOT_ROOT")`, and an `IndexOf` ordering check on two literals. That kind of assertion passes whether or not the matched line is reachable. It is exactly why R505's gap could exist: `GitBoundCommandPreservesIgnoredPackageInputs` is the test for the very behaviour that was missing, and it only ever ran the other script. | `eng/container/entrypoint.sh:104-140`; `eng/container/loop-command.sh:126-129,178-207`; `SharpProof.ArchitectureTest/ContainerSourceCleanlinessTests.cs:70,108,127,187`; `SharpProof.ArchitectureTest/BuildSchedulingTests.cs:186-196,289-320`; commit `d08d1d8be` |

### Checked and not proposed (part two hundred sixty-two)

- **The two scripts should not be merged.** They materialize source for different
  purposes - a disposable per-task clone under `/tmp` versus a persistent private
  volume reconciled across runs - and `loop-command.sh` carries a lock protocol and
  a snapshot mode that `entrypoint.sh` has no use for. R747 asks for the shared
  *policy* to be expressed once (a sourced helper defining the three `ls-files`
  passes and the diff flags) and for `loop-command.sh` to gain executable coverage,
  not for the scripts to become one.
- **`compose.yaml` is well factored and needs nothing.** It already uses YAML
  anchors for the two cache volumes and for the common service body. The `dev` and
  `loop` services do repeat eight lines - the `user:` override,
  `DOTNET_CLI_USE_MSBUILD_SERVER`, `SHARPPROOF_ORIGIN_URL`, the two anchored volume
  references, and an identical `command: ["dev", "-lc", "while sleep 1000; do :;
  done"]` - which a third anchor would remove. This is **not** proposed: the two
  services differ in their workspace volume and in three environment variables, a
  further anchor would obscure that, and `BuildSchedulingTests:315-318` asserts
  `SHARPPROOF_ORIGIN_URL` appears exactly twice, which is a deliberate pin on the
  current shape.
- `ContainerSourceCleanlinessTests.cs:372-411` is a **seventh** variant of the
  process runner described in R724, adding an `environment` dictionary parameter to
  the `(int ExitCode, string Output, string Error)` family. Already counted there;
  noted because it is the variant a shared helper would need to support.

### Status (part two hundred sixty-two)

R747 is `pending`. The executable-coverage half is the valuable one and is
independent of the deduplication half: `loop-command.sh` can be given the same
fixture treatment `entrypoint.sh` already has without touching either script. Doing
that first would also make the shared-policy extraction safe to attempt.

## Second survey, part two hundred sixty-one: standing-defect re-verification, and CI is clean

No new finding. This part re-verifies every defect this survey has left standing
and closes two of them, then records two areas measured as having nothing to
reduce.

### Standing defects re-verified against HEAD

- **R480 is fixed.** The container-contract gate no longer requires the literal
  that applied R243 removed. `Test-SharpProofContainerContract.ps1:377-379` now
  tests two patterns - `_RequireSharpProofCanonicalContainer` and
  `SHARPPROOF_CONTAINER_CONTRACT` - and the third,
  `/etc/sharpproof/container-contract\.json`, is gone.
  `Directory.Build.targets:7` contains `SHARPPROOF_CONTAINER_CONTRACT`, so both
  patterns match and the script no longer throws at HEAD. The fix is exactly what
  R480 proposed: assert the property reference rather than the resolved value.
  Move R480 to applied.
- **R505 is fixed**, by commit `d08d1d8be`. See part two hundred sixty-two for
  the detail and for R747, which is what the fix left behind.
- **R301 stands, and is confirmed in more detail than when filed.**
  `Invoke-SharpProofChangedTests.ps1:88-97` still builds each project's compiled
  file set from `$xml.SelectNodes("//*[local-name()='Compile']")` over the
  `.csproj` alone, so the six sources injected by `Directory.Build.props:64-108`
  are invisible to it. The consequence is now traceable exactly: a change to
  `eng/testing/TestRepository.cs` matches the `eng/` prefix at `:127`, which sets
  `$scriptOrDocumentationImpact`, and `:196-198` translates that into
  **`SharpProof.ArchitectureTest` alone**. It does *not* set `$globalImpact`, which
  is the flag at `:146` that selects everything. `TestRepository.cs` is compiled
  into every project matching `SharpProofTestProject`, so editing it changes the
  source of nineteen test projects and reruns one.
- **R310 stands.** `Test-ProductionCSharpComplexity.ps1:178-181` is still
  `$expressionNodes -le $maximum... -and $decisionPoints -le ... -and $members -le
  ...`, with no lower bound anywhere. Every reduction this ledger produces lowers
  the measured values while the ceilings stay where they are, so the ratchet
  loosens monotonically as the ledger is worked. Note this is now sharper than when
  filed, because R738 established there is no dead code to reclaim: the measured
  figures can only fall through deliberate restructuring, which is precisely the
  kind of change a ratchet should re-pin after.
- **R315 stands.** `impureState` still appears in exactly two files -
  `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs` and
  `SharpProof.Worker/EffectCounterexampleReplayer.cs` - and nowhere else. The
  third occurrence of `WorkerEffectSet.ReadsCapturedState`, in
  `CompilerWireMappings.generated.cs`, is the generated enum mapping, not a third
  copy of the mask.

Standing defects after this part: **R301**, **R310**, **R315**.

### Checked and not proposed (part two hundred sixty-one)

- **The GitHub Actions configuration has nothing to reduce.** Across seven
  workflows and two composite actions - 567 lines - there are only three five-line
  blocks appearing in two or more files, and all three are irreducible Actions
  boilerplate: the `checkout` step with `fetch-depth: 0`, and two trigger blocks.
  The composite-action mechanism is already carrying the real weight:
  `./.github/actions/prepare-qualified-packages` is used **nine** times and
  `security-reusable.yml` is called twice as a reusable workflow.
- **Every third-party action is pinned to exactly one commit SHA, repository-wide,
  with a version comment.** `actions/checkout` at eleven uses,
  `actions/upload-artifact` at eight, `actions/download-artifact` at three, plus
  `actions/stale`, `actions/dependency-review-action`, and `NuGet/login`. A search
  for any action name bound to two different SHAs returns nothing. Do not propose
  consolidating these pins; the repetition is the mechanism.
- **The `SharpProof.Effects` traversal duplication needs no new item.** The
  repository-wide duplicate-window scan run for R724 ranks
  `ExceptionHandlerReachability.cs` / `UsingDisposalEffectResolver.cs` as the
  single most duplicated pair in the C# tree, at thirteen shared twelve-line
  windows. That pair is already the most thoroughly analysed in this ledger -
  R285, R287, R306, R307, R313, and R386 - and R287 explicitly records that
  merging the four `IsDefinitelyNull` implementations would change verdicts.
  Nothing further should be filed against it; an eighth item would be
  double-counting.

### Status (part two hundred sixty-one)

No new ID. Two standing defects closed, three re-verified with sharper evidence
than when filed, and two areas measured as exhausted.

## Second survey, part two hundred sixty-three: R748 - correcting R310, and the second ratchet

### Correction: R310 overstates the problem

R310 has been restated several times in this survey, including twice today, as
"the ratchet loosens monotonically". **That is too strong and should not stand.**
The `ceilingRationale` history in `eng/acceptance/contract.json` shows an active
maintenance practice: ceilings are raised deliberately, each raise appends a dated
sentence naming the logic that justified it, and the field records at least one
explicit downward re-pin - *"2026-08-31 validation: the ratchet was refreshed to
the audited canonical inventory after the accumulated analyzer, effect-flow,
worker-protocol, corpus-publication, and manifest-bound timeout-promotion fixes;
ceilings:218226/12839/5799."* Two later commits raised it to 218637/12874/5808 and
then 218647/12875/5808, each with its own rationale sentence. So a human re-pinning
discipline exists and is documented in-band. R310's mechanical observation - the
comparison at `Test-ProductionCSharpComplexity.ps1:178-181` is `-le` only - is
correct, but the conclusion drawn from it was not.

| ID | Finding | Evidence |
|---|---|---|
| R748 | **Both ratchets are one-sided, refresh is manual and milestone-driven, and neither reports the headroom that would prompt a refresh - which is measurable right now.** The repository has two independent complexity ratchets and they share the shape. The repo-wide one compares `-le` against three ceilings in `contract.json`. The per-file one, `ArchitectureTests:2072-2110` over `eng/acceptance/algorithm-size-ratchets.json`, tests `if (fileExpressionNodes > entry.MaximumFileExpressionNodes)` across **16 files with 4 caps each**, and its only well-formedness assertion on those 64 numbers is `Is.Positive` (`:552-555`). Neither mechanism emits measured-versus-cap, so slack accumulates invisibly between hand-driven refreshes. **The current slack is demonstrable without running either gate.** The size-ratchet manifest was last touched by `8a4c836db` on 2026-09-01. Since that commit, **9 of its 16 files have changed, for a net of +117/-330 = -213 lines**, through commits whose titles are this ledger's own application work - `Remove unused abstract-domain operations`, `Simplify interval domain normalization`, `Share Roslyn reference lowering fallback`, `Share frontend unsupported value classification`, `Apply audited code reductions`. `IntervalDomain.cs` alone is +4/-79 and `SequenceCardinalityDomain.cs` is +0/-72; a 79-line deletion cannot leave a file's expression-node count unchanged, though the exact figure needs the Roslyn count the gate performs rather than the line delta measured here. Meanwhile the repo-wide ceilings moved **upward** twice after that date and were not refreshed. So the loop closes on itself: **applying this ledger is what creates the unreclaimed headroom, and nothing surfaces it.** The useful change is not a lower bound - that would fight the deliberate-raise workflow the rationale field documents - but reporting: have both gates emit measured values beside their caps, so a refresh is prompted by data instead of by remembering. `Test-ProductionCSharpComplexity.ps1` already builds a `-Json` result object carrying the measured counts, so the repo-wide half is nearly free. | `scripts/Test-ProductionCSharpComplexity.ps1:178-181,182-206`; `SharpProof.ArchitectureTest/ArchitectureTests.cs:508-560,2072-2110`; `eng/acceptance/algorithm-size-ratchets.json`; `eng/acceptance/contract.json` `productionComplexity` and 13 `productionCoordinatorComplexity.layers` entries; commits `8a4c836db`, `7293f09e0`, `e7aae6430`, `5c7189e9c`, `a070fc530` |

### Checked and not proposed (part two hundred sixty-three)

- **The `ceilingRationale` field is not bloat and must not be trimmed.** It is a
  single very long string, which reads like an obvious consolidation target, but it
  is the only in-band record of *why* each ceiling has its value, it is appended to
  rather than rewritten, and `ArchitectureTests:530` asserts it is non-empty. A
  later pass measuring line or character counts should leave it alone.
- **The 13 `productionCoordinatorComplexity.layers` entries are a deliberate
  per-layer subdivision**, not duplication of the three repo-wide ceilings. The
  rationale explains it: *"each new semantic layer has its own narrow ratchet
  below"*, and *"Assignment evaluation is separated from the core effect scanner so
  both layers retain independent reviewable ceilings."* Their existence is the
  answer to a problem, not an instance of one.
- **The 16-path list in `AlgorithmLayerSizeRatchetManifestIsWellFormed:511-527` is
  a second declaration of the manifest's own contents**, asserted by set equality
  against it. That is the two-person rule already recorded as an anti-finding for
  `eng/acceptance/Verify.ps1`, applied correctly: the test would catch a file
  silently dropped from the manifest to dodge its cap. Do not propose deriving the
  list from the file it checks.

### Status (part two hundred sixty-three)

R748 is `pending` and **supersedes R310**, which should be read through it
rather than on its own; R310's mechanical observation stands and its
"loosens monotonically" conclusion does not. The reporting change is small,
touches no ceiling value, and would make the next refresh a response to evidence.
Standing defects after this part: **R301**, **R315**, and R748 in place of R310.

## Second survey, part two hundred sixty-four: R749-R750 - the consumer-fixture directories

`samples/` and `eng/pilots/` are the two consumer-fixture trees. They are near
twins in configuration and complete opposites in supply-chain posture, and neither
fact is recorded anywhere.

| ID | Finding | Evidence |
|---|---|---|
| R749 | **`samples/Directory.Build.props` and `eng/pilots/Directory.Build.props` share 17 of their 22 and 27 non-blank lines, and the five differences include two names for one concept.** Identical in both: `TargetFramework net8.0`, `LangVersion 12.0`, `Nullable enable`, `ImplicitUsings enable`, `Deterministic true`, `TreatWarningsAsErrors true`, `EnableNETAnalyzers false`, `NuGetAudit false`, `SharpProofFeatures all`, `OutputType Library`, and a `PackageReference Include="SharpProof" ... PrivateAssets="all"`. Both also import the same `SharpProof.Release.props` - **spelled with forward slashes in one and backslashes in the other**. The version property is the same value under two names: `SharpProofSamplePackageVersion` and `SharpProofPilotVersion`, both defaulting to `$(SharpProofPackageVersion)`, and only the pilot one is overridable. `SharpProofProfile` is `advisory` in both but conditional only in pilots, so a caller can retarget a pilot's profile and cannot retarget a sample's. **The sharpest split is invisible from either file**: "consumer fixtures do not use central package management" is achieved two entirely different ways - `eng/pilots/` gets a three-line `Directory.Packages.props` whose only purpose is to shadow the root's fourteen `PackageVersion` entries, while `samples/` has no such file and instead relies on the root `Directory.Packages.props` conditioning `ManagePackageVersionsCentrally` on `'$(SharpProofSamplePackageVersion)' == ''`, a hook that exists in the shared file solely for that one directory. Pilots additionally set the same property a second time in their own `Directory.Build.props:11`. One policy, three declarations, two mechanisms. Neither fixture tree imports the root `Directory.Build.props` - MSBuild discovery stops at the first file found walking up - which is correct and is why they restate the ten shared properties, but it is also why nothing keeps the two restatements in step. | `samples/Directory.Build.props`; `eng/pilots/Directory.Build.props`; `eng/pilots/Directory.Packages.props`; `Directory.Packages.props:4-7` |
| R750 | **The five pilots are the only projects in the repository that reference third-party packages, and they are the only projects exempt from every supply-chain control the rest of the tree treats as fatal.** `Directory.Build.props:9-11,21` sets the strictest available posture - `NuGetAudit true`, `NuGetAuditMode all`, `NuGetAuditLevel low` - and promotes `NU1901;NU1902;NU1903;NU1904` to errors, so a single low-severity advisory fails the build. That posture covers a tree whose non-Microsoft dependencies are essentially nil. Meanwhile `eng/pilots/*` reference `Ardalis.GuardClauses 5.0.0`, `FluentValidation 12.1.1`, `OneOf 3.0.271`, `Polly 8.7.0`, and `Serilog 4.4.0` - the repository's entire third-party surface - under **five independent opt-outs**: `NuGetAudit false`; no `packages.lock.json`, so `RestorePackagesWithLockFile` and CI `RestoreLockedMode` do not apply; `ManagePackageVersionsCentrally false`, so each version is pinned inline in its own csproj rather than centrally; absence from `eng/release/third-party-components.json`, which covers only what ships inside the packages; and exclusion from `SharpProofProductionProject` by the `(samples|eng/pilots)` path regex. A search for any of the five package names outside `eng/pilots/` returns nothing - no audit list, no dependency contract, no script. The exemption is defensible, because pilots are adoption fixtures that do not ship, and `eng/pilots/README.md` does say they are "not product projects" that "pin real public libraries". **What is missing is any statement that they are therefore exempt, or from what.** The five opt-outs are five absences and negations in four files, and `samples/` carries the same `NuGetAudit false` without needing it, having no third-party references at all - so the one directory that could safely audit does not, and the one that cannot is silent about why. | `Directory.Build.props:9-11,21,35`; `eng/pilots/Directory.Build.props:11`; `eng/pilots/{ArdalisContracts,FluentValidationContracts,OneOfMixedStrict,PollyEffects,SerilogEffects}/*.csproj:3-8`; `eng/release/third-party-components.json`; `eng/pilots/README.md:1-6`; 47 `packages.lock.json` files against 60 projects |

### Checked and not proposed (part two hundred sixty-four)

- **The thirteen projects without a `packages.lock.json` are exactly the five
  pilots and eight samples**, and no product or test project is missing one. The
  gap is precisely the fixture set, not a leak. Recorded so the count 47-of-60 is
  not later filed as incomplete lock coverage.
- **Central package management itself is correctly configured and needs nothing.**
  Fourteen `PackageVersion` entries, one file, `CentralPackageTransitivePinningEnabled`
  conditioned on it, and `Microsoft.Z3 4.12.2` cross-checked against
  `toolchain.json` by `Test-SharpProofContainerContract.ps1:396`. The only
  duplication of a version string between the two is that Z3 pin, and it is gated.
- **Samples referencing only SharpProof's own packages is correct**, not an
  oversight: `samples/Library` references `SharpProof.Attributes` and
  `SharpProof.Verifier`, `samples/Outcomes` references `SharpProof.Verifier`, and
  the rest take only the `SharpProof` reference injected by their shared props. The
  `MSBuildProjectName != 'Outcomes'` condition on that injection is deliberate.
- The two fixture trees should **not** be merged into one directory or one props
  file. They test different things - samples are analyzer-only smoke over
  first-party packages, pilots run `SharpProofVerify true` against real third-party
  libraries - and `SharpProofVerify` being pilots-only is the substantive difference
  the shared lines obscure. R749 asks for the shared ten properties to have one
  home and for the two version-property names to become one, not for the trees to
  converge.

### Status (part two hundred sixty-four)

R749 is `pending` and is ordinary tidying with one real hazard inside it: the
two mechanisms for disabling central package management mean a change to either
`Directory.Packages.props` can affect one fixture tree and not the other, silently.
R750 is `pending` and asks for a written policy rather than a code change; it is
filed because a supply-chain exemption that exists only as five absences is the
kind of thing that is re-discovered rather than remembered, and because the
inversion - strictest audit where there is nothing to audit, none where the
third-party surface actually is - reads as accident rather than decision.

## Second survey, part two hundred sixty-five: R751 - duplicated portable IR call fixture

| ID | Finding | Evidence |
|---|---|---|
| R751 | **Two portable-IR codec tests construct the same call graph before exercising different identity cases.** `DecoderRejectsDocumentationOnlyCallIdentitySpoof` and `DecoderPreservesSuffixBoundCallIdentityRoundTrip` each create an `IrFactory`, the same static `Transform(System.Int32)` member, argument/result variables, one entry block, one call, one return, and the same encoded root list. The first test then replaces the member documentation id with an unrelated fully qualified id and expects decode to reject it; the second replaces it with the suffix-bound id, serializes/deserializes, and expects the round trip to preserve it. A private fixture builder returning `EncodedPortableIrGraph` can own only the common graph construction, leaving those intentionally different mutations, serialization boundary, and assertions in the tests. This removes a roughly 25-line drift surface without weakening either identity rule. | `SharpProof.Worker.Test/PortableIrGraphCodecTests.cs:375-403,412-442` |

### Checked and not proposed (part two hundred sixty-five)

- The two tests should remain separate. One proves that a documentation id cannot
  spoof the call identity; the other proves that the permitted suffix-bound form
  survives the JSON wire boundary. Sharing the graph fixture does not merge those
  distinct validation assertions.
- The encoded graph should remain mutable in each test. The mutation is the point
  of both cases, so a helper that returned a fully decoded or sealed object would
  hide the boundary under test rather than reduce meaningful setup.

### Status (part two hundred sixty-five)

R751 is `complete`: the two identity-boundary tests now share one encoded call
graph fixture while retaining their distinct documentation-id mutations,
serialization boundary, and decoder assertions.

## Second survey, part two hundred sixty-six: R752 - the one ungated authority file

A census of every configuration and authority file in the repository against the
test and script corpus, asking one decidable question: does anything read this
file's contents? The answer is yes for 52 of 54. This part is about the exception.

| ID | Finding | Evidence |
|---|---|---|
| R752 | **`eng/self-application/SharpProof.SelfApplication.props` is the only substantive authority file in the repository whose contents no test asserts, it is a third parallel copy of the consumer analyzer configuration, and the command that exercises it runs in no workflow.** Of 54 tracked `.props`, `.targets`, `.json`, `.editorconfig`, `.globalconfig`, `compose.yaml`, `global.json`, `NuGet.Config`, `.sln`, and `BannedSymbols.txt` files, **52 are named by a test under `*Test*.cs`, a script under `scripts/`, or `eng/acceptance/`**. The two that are not are `.gitattributes` - one line, `* text=auto eol=lf`, self-enforcing because its loss would make every generator's byte-exact `-Verify` fail loudly - and this file. **What it contains is the problem.** At `:33` it restates the nine-entry `CompilerVisibleProperty` list verbatim, which R738 records as needing to track the disjoint partition shipped by `SharpProof.props` (three) and `SharpProof.Verifier.props` (six); at `:36-40` it restates the `SharpProofAnalyzerRole` EntryPoint/Generator/Dependency vocabulary from R733; and it wires the source-built analyzer and generator the way `SharpProof.Package/buildTransitive/SharpProof.targets` wires the shipped ones. It is therefore the third copy of the configuration, alongside `SharpProof.AnalyzerConsumer.props` and the shipping pair - and the only copy outside `ArchitectureTests`' frozen-surface check, which is deliberately scoped to the five shipping build files. **Nothing runs it in CI either.** `self-apply` appears in no `.github/workflows/*.yml`; the commands the workflows invoke are `acceptance`, `coverage`, `mutation`, `nightly`, `pack`, `package-consumers`, `pilots`, `pr`, `release-*`, and `security`. `pr` expands to `pr-gates`, which does not include it, and `nightly` expands to mutation, dependency-audit, acceptance, and fuzz-nightly, which do not either. It runs only when a person types `sp self-apply`. So the least-verified copy of the most-duplicated configuration in the repository is the one whose entire purpose is to prove the project analyzes itself - and if it drifts from the shipping partition, the dogfooding silently stops covering whatever was added. | `eng/self-application/SharpProof.SelfApplication.props:33,36-40,62`; `scripts/Invoke-SharpProofContainer.ps1:4,132-145,157,244-266`; `.github/workflows/*.yml`; `SharpProof.ArchitectureTest/ArchitectureTests.cs:766-901`; `Directory.Build.targets:2-3` |

### Checked and not proposed (part two hundred sixty-six)

- **The file is not dead.** `Invoke-SharpProofContainer.ps1:203` passes
  `-p:SharpProofSelfApplication=true` for the `self-apply` command and `:189`
  passes `false` elsewhere, and `Directory.Build.targets:2-3` imports it under that
  condition. It also carries its own `_SharpProofValidateSelfApplication` target at
  `:62`, which is a partial in-band substitute for the missing test. R752 is
  about verification and drift, not about removal.
- **The `.editorconfig` per-file suppressions have no dead entries.** Nine file
  globs carry eleven `dotnet_diagnostic.*.severity = none` rules, naming fifteen
  files once the brace expansions are resolved, and **all fifteen files exist**. A
  rename or deletion would have left a silently inert section; none has.
- **Diagnostic severity is configured in five places and that is correct scoping,
  not duplication.** `.globalconfig` sets repository-wide severities,
  `Directory.Build.props:21` promotes four NuGet-audit codes and four style codes
  to errors, `Directory.Build.targets:22` relaxes three for test projects,
  `.editorconfig` scopes eleven suppressions to individual files, and ten `.csproj`
  files carry a narrow `NoWarn`. Each mechanism has a scope none of the others can
  express. Do not propose consolidating them.
- `samples/Diagnostics/.globalconfig` sets `global_level = 100` and downgrades
  SP0045 and SP0047 to warning for one sample. That is a fixture deliberately
  demonstrating diagnostics, and the `global_level` is the documented way to
  outrank the root `.globalconfig`. Correct as written.

### Status (part two hundred sixty-six)

R752 is `partially applied`: `ArchitectureTests.PreviewConfigurationInterfaceMatchesFrozenSnapshot`
now compares both `SharpProof.AnalyzerConsumer.props` and
`SharpProof.SelfApplication.props` against the shipping compiler-visible union.
The remaining decision is unchanged and intentionally deferred: whether a
self-application lane that no automated pipeline runs should be wired into one.

## Second survey, part two hundred sixty-seven: R753 - repeated analyzer test session factories

| ID | Finding | Evidence |
|---|---|---|
| R753 | **Analyzer tests carry several local `IAnalyzerSessionFactory` implementations for the same recording pattern.** `AnalyzerModeAndEffectTests.RecordingSessionFactory` and `RequiresReplaySoundnessTests.RecordingSessionFactory` both own a `ConcurrentDictionary<string, AnalyzerSemanticOutcome>`, create an `AnalyzerSession` with a callback that `AddOrUpdate`s by method name, and combine repeated outcomes through `AnalyzerSemanticOutcomes.Combine`; the former additionally records per-method counts and the created session. `NestedRequiresCallSiteTests.RecordingSessionFactory` repeats the same session-construction and outcome-combination protocol with a `MethodIdentity` key because that suite needs overload-safe lookup, while `AdvisoryActivationTests.RecordingSessionFactory` repeats the create-count-only factory shape. A shared test factory parameterized by the key projection and optional count/session recording can centralize the callback plumbing while leaving each suite's deliberately different query surface intact. This is test-only maintenance duplication, but it is a real drift surface: changes to outcome combination or factory lifecycle semantics currently need review in four files. | `SharpProof.Analyzer.Test/AnalyzerModeAndEffectTests.cs:3719-3780`; `SharpProof.Analyzer.Test/RequiresReplaySoundnessTests.cs:577-604`; `SharpProof.Analyzer.Test/NestedRequiresCallSiteTests.cs:1812-1863`; `SharpProof.Analyzer.Test/AdvisoryActivationTests.cs:475-492` |

### Checked and not proposed (part two hundred sixty-seven)

- The suites should not be forced onto one public production abstraction. Their
  keys are intentionally different: method names are sufficient for the replay
  tests, while nested-call tests need method kind and source span to distinguish
  overloads. The reduction is about shared test plumbing or a common base helper,
  not about weakening those identities.
- `AdvisoryActivationTests`'s minimal create-count factory and the richer recording
  factories need not be merged if optional state makes the helper harder to read.
  The strongest mechanical target is the two string-keyed factories, with the
  MethodIdentity variant an adjacent extension only if it remains smaller than its
  local implementation.

### Status (part two hundred sixty-seven)

R753 is `partially applied`: the two string-keyed analyzer suites now share one
recording session factory with optional outcome counts and session capture.
The overload-sensitive `MethodIdentity` recorder and minimal create-count
recorder remain local because their key and lifecycle contracts differ.

## Second survey, part two hundred sixty-eight: R754 - repeated constructed-generic test fixture

| ID | Finding | Evidence |
|---|---|---|
| R754 | **Two constructed-generic binder tests duplicate the entire compilation and target-selection fixture.** `BindingCachePreservesConstructedMethodNullability` and `ClauseInventoryCachePreservesConstructedMethodNullability` each embed the same `Target.Echo<T>` source, the same two nullable call sites, call `CreateCompilation`, select `Target.Echo` with `GetConstructedTargets`, create a `ContractBinder`, and bind the two resulting symbols. Only the operation under test differs (`Bind` versus `GetClauseInventory`) and the result projection changes accordingly. A private fixture helper returning the compilation, constructed targets, and binder can own that setup while preserving the separate assertions about binding and clause-inventory caches. The companion-resolution test below has the same shape but intentionally uses a different interface/companion source, so it is an optional extension rather than part of the exact duplicate. | `SharpProof.Contracts.Test/ConstructedGenericContractTests.cs:328-405` |

### Checked and not proposed (part two hundred sixty-eight)

- The two cache tests should remain separate because they exercise different
  `ContractBinder` entry points and therefore different cache layers. Sharing
  their source/target fixture does not collapse those behavioral assertions.
- The `SharedCompanionResolutionCachePreservesMethodNullability` test should not
  be forced into the same source fixture: its interface and `[ContractFor]`
  companion are the behavior under test. It is evidence of a related pattern,
  not a reason to erase the distinction between direct and companion binding.

### Status (part two hundred sixty-eight)

R754 is `complete`: the binding and clause-inventory cache tests share one
constructed-generic compilation, target selection, and binder fixture while
retaining separate entry-point and assertion coverage.

## Second survey, part two hundred seventy: R756 - local Worker temporary-directory wrapper

| ID | Finding | Evidence |
|---|---|---|
| R756 | **`LinuxPublicationSetTests` reimplements the shared temporary-directory fixture already linked into `SharpProof.Worker.Test`.** The file's private `TemporaryDirectory` stores a path, creates `Path.Combine(Path.GetTempPath(), "SharpProof.PublicationSet." + Guid...)`, calls `Directory.CreateDirectory`, and deletes recursively in `Dispose`. `Directory.Build.props` already compiles `eng/testing/TempDirectory.cs` into `SharpProof.Worker.Test`; that helper uses the safer atomic `Directory.CreateTempSubdirectory(prefix)`, exposes the equivalent full path, and deletes only when the directory still exists. Replacing the local wrapper with the linked helper removes the duplicate lifecycle implementation and gives this Linux-only suite the same failure-safe cleanup used by the other Worker tests. | `SharpProof.Worker.Test/LinuxPublicationSetTests.cs:20-27,49-52,75-78,817-850`; `eng/testing/TempDirectory.cs:1-19`; `Directory.Build.props:76-82` |

### Checked and not proposed (part two hundred seventy)

- The publication-set tests' filesystem assertions remain distinct. R756 only
  changes the temporary-root fixture and its path property (`Path` to `FullName`);
  it does not alter publication locking, marker ownership, or Linux-only cases.
- This is narrower than R726-R728. Those entries measure repository-wide naming
  and recursive-delete divergence; this file is a direct local duplicate in a
  project that already receives the canonical helper, so it can be removed without
  first choosing a new naming convention.

### Status (part two hundred seventy)

R756 is `complete`: `LinuxPublicationSetTests` now uses the linked
`eng/testing/TempDirectory` helper and its guarded cleanup instead of a private
temporary-directory wrapper.

## Second survey, part two hundred sixty-nine: R755, a generator recount, and the enforced-axis pattern

Every tracked generated file, compared header by header. The result is a clean
demonstration of the pattern R735 and R747 describe: the one axis that is gated is
uniform across all 41 files, and both ungated axes have drifted.

### Correction: there are fifteen generators, not fourteen

Earlier parts, including R327 and R459, state that the repository has **14**
generators, all under `scripts/`, and describe
`SharpProof.Specs.Test/ApiSpecRuntimeWitnesses.generated.cs` as emitted by
`Generate-ApiSpecCatalog.ps1` through a `$RuntimeWitnessOutputPath` parameter.
**That is wrong in a way worth recording.** There are fifteen:
`scripts/Generate-*.ps1` is 14 files, and `SharpProof.Specs.Test/Generate-ApiSpecRuntimeWitnesses.ps1`
is a fifteenth, living outside `scripts/`. `Generate-ApiSpecCatalog.ps1:1267`
*invokes* it rather than containing it. The delegation is governed - the fifteenth
script is pinned twice in `eng/acceptance/contract.json` (`:109`, `:694`) and the
`-Verify` path reaches it through the chain - so this changes no conclusion in
R327 or R459, but their generator counts and the claim that all generators live in
`scripts/` should be read with this correction.

| ID | Finding | Evidence |
|---|---|---|
| R755 | **The generated-file header has three conventions; the one a test enforces is uniform across all 41 files and both unenforced ones have drifted.** All **41** tracked generated files open with `// <auto-generated>` - **41 of 41, no exceptions** - and that is precisely the marker `BoundaryEnforcementTests.GeneratedProductionFilesAreExplicitlyApproved` scans for when it discovers generated files by content rather than by filename. The two conventions no test checks have both broken. **The "do not edit" line**: 38 files say *"Do not edit this file directly."*, one says *"Do not edit by hand."* (`SharpProof.Frontend/ContractApiMetadata.generated.cs`), and **two have no such line at all** (`SharpProof.Frontend/CSharpScalarSemantics.generated.cs`, `SharpProof.Ir/IrOperatorCatalog.generated.cs`). **The `#nullable enable` directive**: 39 files carry it and two do not (`ContractApiMetadata.generated.cs`, `SharpProof.Specs.Test/ApiSpecRuntimeWitnesses.generated.cs`). The two deviation sets barely overlap - four distinct files deviate, only one of them on both axes - which is what independent drift looks like rather than one bad generator. The `#nullable` directive is currently redundant in every case: `Directory.Build.props:15` sets `<Nullable Condition="'$(Nullable)' == ''">enable</Nullable>`, and **no generated file lives in either of the two `Nullable=disable` projects**, `SharpProof.Package` and `SharpProof.Verifier`, so the directive changes nothing today. That is why the drift was invisible - but it also means the header carries a promise the build does not depend on, in 39 files, inconsistently. The cheap fix is to extend the check that already discovers these files by their `// <auto-generated>` marker to assert the other two lines while it is there. | 41 generated files; `SharpProof.ArchitectureTest/BoundaryEnforcementTests.cs:84-139`; `Directory.Build.props:15`; `SharpProof.Package/SharpProof.Package.csproj:4`; `SharpProof.Verifier/SharpProof.Verifier.csproj:4`; `SharpProof.Frontend/ContractApiMetadata.generated.cs:1-4`; `SharpProof.Frontend/CSharpScalarSemantics.generated.cs:1-5`; `SharpProof.Ir/IrOperatorCatalog.generated.cs:1-5`; `SharpProof.Specs.Test/ApiSpecRuntimeWitnesses.generated.cs:1-5` |

### Checked and not proposed (part two hundred sixty-nine)

- **Non-PowerShell scripting is four files and is already covered.** The repository
  tracks five non-`.ps1` scripts: `eng/container/loop-command.sh` (213),
  `entrypoint.sh` (170), `dev-init.sh` (36), `dev-command.sh` (13), and
  `.opencode/plugins/oh-my-goal.js` (1 line). The two substantial ones are R747.
  There is no Python, and the only JavaScript is a one-line plugin stub. Nothing to
  reduce.
- **Preprocessor use is minimal and deliberate.** Across the whole C# tree: 92
  `#nullable`, 24 `#pragma`, 9 `#if`/`#endif` pairs, 7 `#undef`, 6 `#define`, 4
  `#else`, 1 `#elif`. For a 286k-line codebase with a `netstandard2.0` /
  `net8.0` / `net9.0` / `net472` spread, nine conditional-compilation regions is
  low. There is no forest of `#if` variants to collapse.
- **`SharpProof.CompilerProbe.TestAsset.csproj:4` sets `<Nullable>enable</Nullable>`
  redundantly** with the conditional default at `Directory.Build.props:15`. One
  line, no behavioural difference, and the project is a deliberately odd test asset
  that states several things explicitly. Not worth an item; noted so it is not
  filed as a finding later.
- `samples/Directory.Build.props:6` and `eng/pilots/Directory.Build.props:6` also
  set `<Nullable>enable</Nullable>`, and there it is **required**, not redundant:
  neither tree imports the root `Directory.Build.props`, as R749 records.

### Status (part two hundred sixty-nine)

R755 is `pending` and small. Its value is corroborative as much as direct: this
is the third independent instance in this survey - after R735 on
`InternalsVisibleTo` and R747 on `loop-command.sh` - where the gated half of a
convention held perfectly and the ungated half drifted, with no other difference
between them. Three instances is enough to treat "which half is asserted" as the
predictor of where drift will be found next.

## Second survey, part two hundred seventy-one: R757 - the repository already has a two-sided ratchet

Closing the remaining unsurveyed areas: markdown documentation, the `eng/`
subdirectories not yet visited, and test data. One finding, from the last of those.

| ID | Finding | Evidence |
|---|---|---|
| R757 | **Three ratchet mechanisms exist; one is enforced in both directions and the other two are ceiling-only, and the two-sided one is the model the other two need.** `SharpProof.Gates/Corpus/unknown-reason-ratchet.json` declares `minimumSupportedCases: 163` and `minimumSupportedOpenSourceMethods: 1` alongside `maximumTotalUnknown: 299` and seven per-reason ceilings, and `CorpusGate.cs` enforces **both** directions - `:671` fails when `supportedCaseCount < ratchet.MinimumSupportedCases`, `:688` fails when `totalUnknownCount > ratchet.MaximumTotalUnknown`. That is a floor and a ceiling in one JSON file, validated on load at `:738-775`. Against it, both complexity ratchets are ceiling-only: `Test-ProductionCSharpComplexity.ps1:178-181` and `ArchitectureTests:2099-2110` each test one direction. **This changes what R748 should ask for.** R748 proposed reporting measured-versus-cap so a human refresh is prompted by data; that is still the cheaper half, but it is now clear the repository does not need a new idea - it needs the shape it already ships in `CorpusGate` applied to the other two files. The asymmetry is also not obviously deliberate. It reads as "capability may only rise, cost may only fall", which would be coherent - except the cost ratchets have no mechanism to make cost actually fall, so a reduction campaign like the one this ledger drives lowers the measured value and leaves the cap, exactly as R748 measured at -213 lines across 16 ratcheted files in one day. The corpus gate would have caught the equivalent drift on its own axis. | `SharpProof.Gates/Corpus/unknown-reason-ratchet.json:3-14`; `SharpProof.Gates/Corpus/CorpusGate.cs:671-692,738-775`; `scripts/Test-ProductionCSharpComplexity.ps1:178-181`; `SharpProof.ArchitectureTest/ArchitectureTests.cs:2099-2110`; `eng/acceptance/algorithm-size-ratchets.json` |

### Checked and not proposed (part two hundred seventy-one)

- **Markdown documentation has one duplicated pair and it is correctly gated.**
  Across 47 tracked `.md` files, only **7** six-line blocks appear in two or more
  files, and every one of them is in the same pair: `README.md` and
  `docs/getting-started.md` share **45 lines** - the install `PackageReference`
  snippets, the `SharpProofProfile`/`SharpProofFeatures` property block, the
  `.editorconfig` example, and the worked `Calculator` example. Both files are in
  `Test-SharpProofReadme.ps1`'s maintained list (`:41`), and the shared content is
  a front-door snippet plus a tutorial repeat, which is ordinary technical writing.
  No other markdown pair shares a single six-line block.
- **The package version in documentation is gated *and* mutation-tested.**
  `Test-SharpProofReadme.ps1:377,406` derives the real version through
  `Get-SharpProofReleaseVersion` from `SharpProof.Release.props:5,7`, and
  `Test-SharpProofDocumentationSupportFixtures.ps1` carries a
  **`package-version-drift`** mutation that rewrites the real version to
  `99.99.99-stale` and asserts the gate fails. That harness holds **22 named
  documentation mutations**. An earlier hypothesis in this survey - that the six
  schema-version rules at `Test-SharpProofReadme.ps1:1006-1044` cover schema
  versions but not the package version a user copies - is **wrong**, and is
  recorded here so it is not filed later.
- **Every `eng/` subdirectory has now been examined.** `pilots` (R749, R750),
  `container` (R747), `testing` (R724-R730), `release` (R750), `acceptance` (R748,
  and the `Verify.ps1` two-person-rule anti-finding), `agent-notes` (R300, R321),
  `coverage`, `self-application` (R752), `generated` (R459), `diagnostics` (R743),
  plus the two single-file directories not previously visited: `eng/fuzz`
  (`retained-seeds.json`) and `eng/test`
  (`architecture-parallel.runsettings`). Neither singleton duplicates anything.
- **The corpus test data is large but not duplicated.**
  `SharpProof.Gates/Corpus/oss-methods.json` is 3,017 lines and
  `expected.canonical.snapshot` is 465, but both are generated evidence with an
  importer (`Import-OssCorpus.ps1`, `OpenSourceCorpusImporter.cs`) and a canonical
  snapshot format (`CorpusSnapshotFormat.cs`). Large generated data with a producer
  and a format is not accidental complexity.

### Status (part two hundred seventy-one)

R757 is `pending` and **refines R748** rather than replacing it: R748's
reporting proposal remains the cheap first step, and this names the in-repository
implementation to copy for the durable one. With this part the survey has reached
every tracked directory: all 60 projects, all 100 PowerShell scripts, all 5 shell
and JavaScript files, all 47 markdown files, all 54 configuration and authority
files, all 41 generated files, all 13 `eng/` subdirectories, and the corpus data.

## Second survey, part two hundred seventy-two: R758 - repeated callable replay target envelopes

| ID | Finding | Evidence |
|---|---|---|
| R758 | **Three replay-boundary tests hand-build the same `CompilerCallablePreparation` and `ProgramBody` envelope.** `ReplayRejectsAnArbitraryReturnValueFromAVoidCallable`, `ReplayFailsClosedForMalformedCanonicalResultIdentity`, and `ReplayAndAuthorityRejectResultOutsideSourceIntegerInterval` each create a factory and entry block, construct a manifest with one `claim`, a single `Ensures` clause, `WorkerClaimReason.None`, and a program body with the same three empty IR preparation maps. The tests intentionally vary the return expression, canonical-variable metadata, and postcondition - void-return rejection, malformed result identity, and an out-of-source-interval result - but the target-envelope plumbing is repeated at `:71-88`, `:211-246`, and `:261-297`. A private `CreateProgramTarget` fixture helper accepting the varying manifest, clause, variables, and program could own the common envelope while preserving each boundary test's distinct assertion. This is separate from R754's duplicated constructed-generic compilation fixture and from R587's production replay-engine overlap; it is local test scaffolding drift. | `SharpProof.Worker.Test/CallableCounterexampleReplayerTests.cs:67-88,203-246,253-297` |

### Checked and not proposed (part two hundred seventy-two)

- The two incrementing-branch tests already share `CreateIncrementingBranch`, and
  the obstacle cases already share `CreateObstacle`; R758 does not propose another
  abstraction over those intentionally varied programs.
- The effect replay suite's large `CreateFixture` is a different artifact shape
  with effect evidence, source snapshots, and replay witnesses. It is not a safe
  replacement for the callable replay target helper.

### Status (part two hundred seventy-two)

R758 is `complete`: the three replay-boundary tests share a one-claim program
target factory while retaining distinct return values, canonical variables,
postconditions, and assertions.

## Second survey, part two hundred seventy-five: R761 - duplicate analyzer test compilation

| ID | Finding | Evidence |
|---|---|---|
| R761 | **`ContractRuntimePolicyTests.SourceAndGeneratedDefinitionsDisableAnalysis` compiles the same `SelectedSource` twice for one fixture.** The first `CreateCompilation(SelectedSource)` exists only to obtain its `CSharpParseOptions` for the generated syntax tree, while the second identical call is the compilation that receives `AddSyntaxTrees(generatedDefinition)`. A single `baseCompilation` can supply both the parse options and the generated-defined compilation, preserving the separate `sourceDefined` compilation, the parse configuration, and the two-policy comparison while removing one Roslyn compilation and its reference setup. | `SharpProof.Analyzer.Test/ContractRuntimePolicyTests.cs:40-59` |

### Checked and not proposed (part two hundred seventy-five)

- The `sourceDefined` compilation intentionally remains separate because it
  carries the source-level `#define`; only the two identical `SelectedSource`
  compilations can be shared.

### Status (part two hundred seventy-five)

R761 is `complete`: the generated-definition test reuses one base compilation
for parse options and added syntax trees; the source-defined preprocessor
compilation remains separate.

## Second survey, part two hundred seventy-four: R760 - repeated constructor syntax-reference lookup

| ID | Finding | Evidence |
|---|---|---|
| R760 | **`AnalyzerFeaturePipeline.AnalyzeMemberInitializer` resolves each constructor's first declaring syntax reference three times while building its candidate list.** The `OrderBy` key reads `DeclaringSyntaxReferences.FirstOrDefault()` for the file path, `ThenBy` reads it again for the span, and the `Where` predicate reads it a third time to choose the generated-code tree, falling back to the initializer tree when no reference exists. These values are stable for the candidate during this local query. Projecting each constructor once to `(candidate, firstReference)`, sorting by the projected metadata, applying the existing generated/delegating filters, and unwrapping afterward removes repeated Roslyn reference enumeration while preserving null-path ordering, `int.MaxValue` span ordering, the initializer fallback, and cancellation behavior. This is a sibling instance of the narrower two-key lookup in R736, not a request to cache syntax references across the compilation. | `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs:543-559`; `SharpProof.Analyzer.Core/AnalyzerSession.cs:262-278` |

### Checked and not proposed (part two hundred seventy-four)

- The separate `IsGenerated(symbol, initializer.SyntaxTree, ...)` check is for
  the member being initialized, not for the constructor candidates, and should
  remain separate.
- `IsThisDelegatingConstructor` still needs the complete declaration set for its
  own syntax predicate; R760 only projects the first reference used by the sort
  and generated-code filter.

### Status (part two hundred seventy-four)

R760 is `complete`: member-initializer constructor candidates now snapshot the
first declaring syntax reference once for sorting and generated-code filtering;
delegation checks still inspect the full symbol.

## Second survey, part two hundred seventy-three: R759 - duplicated call-argument normalization adapters

| ID | Finding | Evidence |
|---|---|---|
| R759 | **The requires-call-site and effect-call precondition paths duplicate the same reduced-extension and argument-alias normalization protocol around the shared classifier.** `RequiresCallSiteAnalyzer.GetAliasEvaluation` first handles non-parameters, explicit arguments, reduced-extension receivers, ordinary argument lookup, and extension-method synthetic receivers before calling `CallArgumentAliasPolicy.Classify`; `AnalyzerEffectCallPreconditionPolicy.GetArgumentEvaluation` repeats the non-parameter and reduced-receiver cases, resolves the corresponding `IArgumentOperation`, computes the same synthetic-receiver condition, and calls the same classifier. The two adapters also carry subtly different edge rules: the call-site path has `ExplicitArguments` and rejects ambiguous `ParamArray`/duplicate argument matches, while the effect path validates against the target parameter range and supports invocation or object-creation origins. A normalized call-site/argument view or a policy helper with explicit strategy callbacks could centralize the shared decision table without erasing those distinctions. Keeping the current two methods means changes to reduced-extension or synthetic-receiver semantics must be synchronized manually. | `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs:616-668`; `SharpProof.Analyzer.Core/EffectCallPreconditionPolicy.cs:162-238`; `SharpProof.Analyzer.Core/CallArgumentAliasPolicy.cs:12-52` |

### Checked and not proposed (part two hundred seventy-three)

- R759 does not propose replacing `CallArgumentAliasPolicy.Classify`; both paths
  already use that lower-level shared implementation.
- The explicit-argument, param-array ambiguity, invocation/object-creation, and
  reduced-extension rules are not interchangeable. Any reduction must preserve
  those input-model-specific guards rather than force a single generic lookup.

### Status (part two hundred seventy-three)

R759 is `pending` and is limited to analyzer adapter plumbing. No implementation
or build file was changed.

## Second survey, part two hundred seventy-six: R762 - duplicated foreign inventory fixtures

| ID | Finding | Evidence |
|---|---|---|
| R762 | **The two foreign-callable tests in `ContractClauseInventoryTests` recreate the same owner and foreign compilations.** `ForeignCallableReturnsRejectedInventoryWithoutRetainingBody` and `ForeignImplementationBodyReturnsRejectedInventoryWithoutRetainingBody` each compile `Owner` with SharpProof references and the identical `Foreign.Analyze(bool)` method containing `Contract.Requires(condition)`. The second test then additionally selects the owner symbol, foreign syntax tree, body, and operation because it exercises the overload that supplies an implementation body; the first selects only the foreign method for the overload without a body. A private fixture or shared source constants can own the identical compilation setup while leaving those intentionally different symbol/operation selections and assertions explicit. This removes duplicated Roslyn fixture construction without conflating the two foreign-ownership API paths. | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:362-420` |

### Checked and not proposed (part two hundred seventy-six)

- The owner and foreign compilations are intentionally separate compilation
  objects because the test verifies rejection across compilation ownership.
- R762 shares only the identical source/setup; it does not merge the two
  `ContractClauseInventoryBuilder.Create` overload scenarios.

### Status (part two hundred seventy-six)

R762 is `complete`: both foreign-callable tests share the owner/foreign
compilation fixture while retaining separate symbol, body, operation, and
builder-overload assertions.

## Second survey, part two hundred seventy-seven: R763 - duplicated qualification-test process setup

| ID | Finding | Evidence |
|---|---|---|
| R763 | **`ReleaseQualificationMatrixTests` duplicates its child-process setup in two helpers with different result contracts.** `RunAsync` and `RunExitCodeAsync` each construct `ProcessStartInfo` with the same working directory and redirected streams, append every argument with the same loop, start the process, and await termination. The first drains stdout/stderr and asserts a zero exit code before returning stdout; the second returns only the exit code so the receipt tests can assert both accepted and rejected evidence. A local runner returning an explicit process result, or a shared start/argument helper used by two narrowly scoped completion paths, can remove the repeated launch preamble while preserving the success assertion, output capture, and negative-test behavior. The shared path should drain both streams even for exit-code-only calls so a noisy script cannot deadlock the negative case. This is a local refinement of the broader R724 process-runner cluster, not a proposal to force the two result meanings into one ambiguous `Output` property. | `SharpProof.ArchitectureTest/ReleaseQualificationMatrixTests.cs:82-97,116-122,193-214,218-255`; R724 |

### Checked and not proposed (part two hundred seventy-seven)

- The two helpers intentionally expose different caller semantics: setup commands
  require a successful command and its stdout, while receipt cases must accept a
  nonzero exit code as an expected result.
- R763 does not propose changing the six-assembly process-runner boundary in
  R724; it records the additional same-file duplicate and its stream-drain
  requirement.

### Status (part two hundred seventy-seven)

R763 is `complete`: release-qualification commands share one process runner
that drains both streams, while success and expected-failure wrappers retain
their distinct result contracts.

## Second survey, part two hundred seventy-eight: R764 - repeated effect-capability projection

| ID | Finding | Evidence |
|---|---|---|
| R764 | **`EffectSummaryProjector.Project` traverses the same known capability flags twice.** For a summary whose capabilities are known, the method first calls `EffectContractMappings.ToContractEffects(summary.Capabilities.Kinds)` and later calls `ToContractCapabilities(summary.Capabilities.Kinds)`. Each wrapper invokes the same private `ProjectCapabilities` routine, which repeats the source-domain validation and scans every generated capability mapping to build a pair containing both outputs. A single internal projection returning both contract flag sets can be evaluated once, then its `Effects` and `Capabilities` fields can remain separate in the final `EffectProjection`. This preserves the existing unknown-capability branch, invalid-value rejection, and distinct output semantics while removing one mapping pass and one repeated validation per projected summary. | `SharpProof.Effects/EffectProjection.cs:20-43`; `SharpProof.Effects/EffectContractMappings.cs:35-70` |

### Checked and not proposed (part two hundred seventy-eight)

- The effects and capabilities fields are intentionally separate contract
  projections; R764 proposes sharing their intermediate mapping, not merging
  their wire types or changing the generated catalog.
- Unknown capabilities must continue to suppress both the effect projection and
  completeness calculation exactly as the current branch does.

### Status (part two hundred seventy-eight)

R764 is `complete`: effect capability flags are projected once into the paired
contract effect/capability result and both fields reuse that projection, while
unknown capabilities still produce the existing empty/unknown behavior.

## Second survey, part two hundred seventy-nine: R765 - duplicated sync/async atomic-file test fixtures

| ID | Finding | Evidence |
|---|---|---|
| R765 | **`AtomicFileTests` duplicates the same publication fixture and invariants for synchronous and asynchronous APIs.** `WriteUtf8CreatesParentsWithoutPreambleAndReplacesDestination` and `WriteUtf8AsyncCreatesParentsWithoutPreambleAndReplacesDestination` perform the same two writes to the same nested path and assert the same UTF-8 bytes and temporary-file cleanup; `WriteUtf8SupportsValidLongDestinationBasename` and its async counterpart likewise repeat the long-name setup, content, and cleanup assertions. A shared test-data/invariant helper, or a small API-parameterized harness, can reduce the duplicated arrangement while retaining separate sync and async calls so each implementation remains directly covered. | `SharpProof.Ir.Test/AtomicFileTests.cs:30-63` |

### Checked and not proposed (part two hundred seventy-nine)

- The synchronous and asynchronous methods should remain separately exercised;
  this finding is limited to their identical fixture and assertion plumbing.
- The production atomic-publication implementation is a separate concern
  already tracked by R281; R765 concerns only the test duplication.

### Status (part two hundred seventy-nine)

R765 is `complete`: synchronous and asynchronous atomic-file tests share the
replacement/long-name fixture harness and publication invariant while each
implementation remains directly invoked.

## Second survey, part two hundred eighty: R766 - repeated unsupported-string SMT fixture

| ID | Finding | Evidence |
|---|---|---|
| R766 | **Three `IrSmtBackendTests` repeat the same unsupported-string query harness.** `StringVariablesFailClosedWithoutNullTagEncoding`, `EmbeddedNullStringFailsClosedWithoutTruncation`, and `NullableStringConcatCannotProduceAFalseProof` each create an `IrFactory`, a string variable, a `VerificationQuery` with no assumptions, a fresh `IrSmtBackend`, and a `ProofKernel` execution, then assert an `UnknownOutcome` with `UnsupportedEncoding`; only the goal expression and precondition/postcondition diagnostic kind differ. A private helper accepting the goal and diagnostic kind can own the common query/backend/result plumbing while retaining each distinct string-boundary expression and its targeted test name. | `SharpProof.Smt.Test/IrSmtBackendTests.cs:310-387` |

### Checked and not proposed (part two hundred eighty)

- `OpaqueTermsFailClosed` is adjacent but exercises a different unsupported
  IR category, so it is not folded into this string-specific fixture candidate.
- The helper should preserve the explicit unsupported-encoding assertion and
  the distinct diagnostic kind supplied by each test.

### Status (part two hundred eighty)

R766 is `complete`: the three unsupported-string tests now share one
query/backend/unknown-outcome assertion helper while keeping their distinct
string expressions and diagnostic kinds.

## Second survey, part two hundred eighty-one: R767 - repeated effect-claim lookup for unknown batches

| ID | Finding | Evidence |
|---|---|---|
| R767 | **`CallableClaimResultAssembler` rescans effect claims once per unknown result in batch helpers.** `Unknowns` and `PostconditionUnknowns` create one record per claim by calling `Unknown`, while `Unknown` runs `target.EffectClaims.Any(evidence => evidence.ClaimId == claimId)` to choose `Unavailable` versus `Unspecified` certainty. A batch with many claim IDs therefore traverses the same effect-claim array repeatedly. Build a claim-id lookup once for each batch and pass membership into the record factory (or add a batch-specific overload), preserving each claim's certainty, current ordering, and the single-record `Unknown` behavior. | `SharpProof.Worker/CallableClaimResultAssembler.cs:83-108` |

### Checked and not proposed (part two hundred eighty-one)

- The lookup must remain claim-specific: a target can have effect evidence for some
  claim IDs but not others, so replacing the test with one batch-wide boolean would
  change the protocol result.
- `Unknown` is also used for isolated failure paths, so the reduction should not
  force those callers through a materialized batch lookup.

### Status (part two hundred eighty-one)

R767 is `complete`: `Unknowns` and `PostconditionUnknowns` build one ordinal
effect-claim ID set per batch, while the single-claim `Unknown` path retains its
direct membership check. Certainty mapping and claim ordering are unchanged.

## Second survey, part two hundred eighty-two: R768 - quadratic pilot claim matching

| ID | Finding | Evidence |
|---|---|---|
| R768 | **`Test-SharpProofPilotReport` rescans every pilot result set for each manifest claim.** For each item in `manifestClaims`, the validator runs `Where-Object` across the complete `claimResults` array, then counts the matches to reject missing or duplicate identities. This is quadratic in the number of claims and repeats the same claim-id comparisons during every report validation. Build a single ordinal claim-id index while detecting duplicate result IDs, then look up each manifest claim once; retain the current null-on-mismatch behavior, claim-kind/outcome projection, and final ordinal sort. | `scripts/Test-SharpProofPilotReport.ps1:116-128` |

### Checked and not proposed (part two hundred eighty-two)

- The index must reject duplicate result IDs rather than silently keeping one;
  the current per-claim match count is part of the validator's fail-closed contract.
- This is separate from R562, which concerns package-artifact validation repeated
  by the qualification receipt; R768 is the claim-result join inside the pilot
  report validator.

### Status (part two hundred eighty-two)

R768 is `complete`: the validator builds one ordinal claim-result index and a
duplicate-ID set per result document, then performs one lookup per manifest
claim. Missing and duplicate matches still fail closed, and report ordering is
unchanged.

## Second survey, part two hundred eighty-three: R769 - duplicated pilot claim projection

| ID | Finding | Evidence |
|---|---|---|
| R769 | **The pilot runner and its report validator independently join manifest claims to claim results.** `Test-SharpProofPilots` builds `claimEvidence` by filtering the full `$claims` array for every manifest claim, while the immediately following `Test-SharpProofPilotReport` call reconstructs the same claim-id/kind/outcome projection from the report's manifest and result arrays. The producer already has the normalized projection needed by the validator, but emits only the report and makes the validator repeat the join. Return or pass a validated claim projection (with duplicate/missing-ID checks) into report validation, or centralize the join helper, preserving fail-closed behavior and ordinal sorting. | `scripts/Test-SharpProofPilots.ps1:242-257,368-373`; `scripts/Test-SharpProofPilotReport.ps1:116-128` |

### Checked and not proposed (part two hundred eighty-three)

- The validator must remain independently callable for reviewed or externally
  supplied reports; the shared helper should preserve that boundary rather than
  trusting only the producer's in-memory result.
- R768 records the validator's repeated-array scan; R769 records the separate
  producer/validator recomputation of the same claim projection.

### Status (part two hundred eighty-three)

R769 is `complete`: producer and validator share the ordinal claim-evidence
projection helper; producer mismatches throw with its pilot-specific message,
while validator mismatches remain null-on-join and fail closed downstream.

## Second survey, part two hundred eighty-four: R770 - repeated TCB path membership scans

| ID | Finding | Evidence |
|---|---|---|
| R770 | **`Test-SharpProofReleaseAuthorityClosure` scans the complete TCB list once per derived path.** After comparing the declared and derived closures, it loops over every `$derived` path and runs `Where-Object { $_ -ceq $path }` across `$tcb` to enforce exactly-once membership. Materializing an ordinal count/set index for `$tcb` once makes the same invariant linear rather than repeatedly traversing the full list, while retaining the existing per-path failure message and the independent declared/derived closure comparison. | `scripts/Test-SharpProofReleaseAuthorityClosure.ps1:17-37` |

### Checked and not proposed (part two hundred eighty-four)

- The declared/derived `missing` and `extra` arrays serve the failure detail and
  should remain separate from the exact-once index.
- This is a local validation optimization, not a proposal to remove the
  independently derived release-authority closure.

### Status (part two hundred eighty-four)

R770 is `complete`: the validator builds one ordinal TCB-path count map and
checks each derived path against it, preserving the exact-once invariant and
failure detail.

## Second survey, part two hundred eighty-five: R771-R773 - repeated release-model lookups

| ID | Finding | Evidence |
|---|---|---|
| R771 | **`Test-SharpProofReleaseArtifacts` repeatedly filters the same release evidence by package identity.** It first traverses `$artifacts` once per kind to compare package-ID sets, then, for each of the three package IDs, filters `$artifacts` independently for the main and symbol rows, filters `$catalogComponents` for the package, and filters `$payloadSets` again for the payload evidence. These are small fixed collections, but a single package-keyed projection containing the main/symbol artifact, components, and payload entries would remove the repeated selection plumbing while retaining the separate package/symbol cardinality checks and package-specific validation. | `scripts/Test-SharpProofReleaseArtifacts.ps1:63-88,111-151` |
| R772 | **`Test-SharpProofPublicationPlanIdentity` linearly rescans the release manifest artifact list for every planned artifact.** The first loop checks that every manifest artifact has a string file name; the second loop runs `Where-Object` over the complete `$manifestArtifacts` array once for each of the six planned package and symbol artifacts to find its row and compare bytes. Building an ordinal file-name lookup during the shape pass would preserve the current exactly-one-row and byte checks without repeating the full scan. | `scripts/SharpProof.PublicationPlanIdentity.psm1:387-400` |
| R773 | **`Get-SharpProofPackageDependencyGraph` repeats contract lookups at several nested levels.** The expected package ID list is sorted again for both `.nupkg` and `.snupkg` passes; each package model scans `$expectedPackages` to find its owner; and each actual dependency group scans that owner's expected groups to find a matching target framework. A validated package-ID map with per-package framework maps can own those immutable lookups once, while leaving the extension-specific license/metadata rules, duplicate detection, and dependency-version checks explicit. | `scripts/Test-SharpProofPackageDependencies.ps1:126-203` |

### Checked and not proposed (part two hundred eighty-five)

- These are local release-validation projections, not a proposal to merge the
  distinct package, symbol, payload, or dependency policies.
- The fixed collection sizes make this primarily a clarity and accidental-
  complexity cleanup; the lookup change must retain current failure detail and
  exact ordinal/case-sensitive comparisons.

### Status (part two hundred eighty-five)

R771 is `complete`: release artifact validation now builds ordinal projections
for package/symbol artifacts, payload sets, and third-party components once,
while preserving the independent topology and licensing checks. R772 is
`complete`: publication-plan validation builds one ordinal manifest file-name
index and duplicate set, retaining exact-one-row and byte checks. R773 is
`complete`: package dependency validation builds package and framework maps
once, retaining exact graph, metadata, duplicate, and dependency-version gates.

## Second survey, part two hundred eighty-six: R774-R775 - repeated performance-gate fixture setup

| ID | Finding | Evidence |
|---|---|---|
| R774 | **`PerformanceGate` duplicates the source-builder skeleton for its call-bearing and call-free advisory fixtures.** `CreateCallBearingUnannotatedAdvisorySource` and `CreateCallFreeUnannotatedAdvisorySource` each create the same `StringBuilder`, append the same class declaration and closing brace, format the same indexed method names, and loop over the requested method count. Only the generated method body differs: one calls `System.Math.Max(Normalize(value), index)` and the other emits `value + index`. A parameterized fixture builder can own the shared class/index plumbing while retaining the intentional call-bearing versus call-free distinction used by the performance measurements. | `SharpProof.Gates/Performance/PerformanceGate.cs:1038-1077` |
| R775 | **`PerformanceGate.RunValidatedAsync` and `RunSmokeAsync` repeat the performance-gate admission and probe pipeline.** Both paths load and validate the acceptance contract at their public entry points, generate a call-bearing advisory source, call `ValidateAdvisoryPackagePolicy`, run a one-iteration analyzer configuration probe, measure unannotated-advisory package builds, and measure launcher forced termination. The metrics and thresholds intentionally differ, so the whole methods should not be merged; a shared setup/probe result carrying the source, configuration counts, package timing, and forced-termination measurement could remove the duplicated orchestration while leaving each result shape and threshold policy independent. | `SharpProof.Gates/Performance/PerformanceGate.cs:68-95,97-137,275-327` |

### Checked and not proposed (part two hundred eighty-six)

- The two fixture bodies are deliberately different experimental controls; only
  their builder mechanics are candidates for sharing.
- Smoke and full performance gates retain separate contracts, result records,
  statistical calculations, and failure thresholds; R775 concerns only their
  common setup and measurements.

### Status (part two hundred eighty-six)

R774 is `complete`: call-bearing and call-free performance fixtures now share
one class/index source builder while retaining their distinct method bodies.
R775 remains pending because its shared setup/probe orchestration crosses
distinct timing and threshold policies.

## Second survey, part two hundred eighty-seven: R776 - repeated direct-clause normalization

| ID | Finding | Evidence |
|---|---|---|
| R776 | **`CallableEvidenceBuilder` normalizes direct contract clauses twice for different evidence paths.** `Build` and `BuildEntry` each walk `target.Clauses`, skip `Ensures`, call `ApplyBodySubstitutions` with the same target variables and empty current-state map, and apply the same maximum-depth rejection. The full path intentionally retains `Assume`/`Requires` provenance and later spec, summary, domain, and completion evidence, while the entry path intentionally considers only `Requires` and additionally checks the supported proof domain. A small clause-predicate helper can own the shared substitution and depth check without merging those evidence sets or their distinct duplicate/literal-true policies. | `SharpProof.Worker/CallableEvidenceBuilder.cs:26-48,264-289`; `SharpProof.Worker/PostconditionObligationBuilder.cs:205-250` |

### Checked and not proposed (part two hundred eighty-seven)

- The helper should not combine `Build` and `BuildEntry`: their clause scopes,
  labels, proof-domain checks, and provenance dictionaries are intentionally
  different.
- `ApplyBodySubstitutions` remains the existing substitution authority; R776
  records the repeated caller-side normalization and depth guard only.

### Status (part two hundred eighty-seven)

R776 is `complete`: `Build` and `BuildEntry` share direct-clause substitution
and depth validation, while retaining their separate provenance, clause-scope,
and supported-domain policies.

## Second survey, part two hundred eighty-eight: R777 - duplicated effect-call propagation

| ID | Finding | Evidence |
|---|---|---|
| R777 | **`EffectAnalysisSession.ComputeSummaries` repeats the effect-call propagation pipeline in `Compute` and `ComputeBody`.** Both nested functions order `EffectCallSite` values, select a recursively computed or body-only target summary based on containing-type relationships, remap receiver/write-receiver/argument regions, and pass the result through `EffectExceptionFlow.KeepEscaping` before joining it into the running summary. The paths intentionally differ in call collection, before-field-init handling, depth/caching, and the extra `WrapTypeInitializationFailures` used for initialization calls. A private join/propagation helper with an explicit target resolver and optional initialization wrapper can centralize the shared remap/escape mechanics while preserving those distinct policies. | `SharpProof.Effects/EffectAnalysisSession.cs:459-501,506-553,560-570` |

### Checked and not proposed (part two hundred eighty-eight)

- This is not a proposal to merge `Compute` and `ComputeBody`: their cache
  dictionaries, recursion guards, depth accounting, and call roots have
  different semantics.
- The initialization-specific exception wrapping and target-selection rule
  must remain caller-controlled; R777 covers only the repeated call-site
  propagation scaffold.

### Status (part two hundred eighty-eight)

R777 is `complete`: `Compute` and `ComputeBody` share a local call propagation
helper for remapping, escaping, and joining summaries; initialization wrapping
and target-resolution policies remain caller-controlled.

## Second survey, part two hundred eighty-nine: R778 - repeated callable-attribute enumeration

| ID | Finding | Evidence |
|---|---|---|
| R778 | **`ContractSelectionInventory.Select` enumerates the same callable attributes twice.** The method first calls `GetCallableAttributes(method).Any(IsEffectContract)` and then calls `GetRejectedSelectionFeatures(method)`, whose `GetRejectedCallableSelectionFeatures` loop calls `GetCallableAttributes(method)` again. That second pass repeats method and associated-property attribute retrieval and rejected-feature classification for the same immutable symbols. Materializing the callable attributes once in `Select` and passing the snapshot into the rejected-feature helper can remove the duplicate enumeration while retaining the separate effect-selection and rejected-feature policies, plus the existing parameter, return, containing-type, and assembly scans. | `SharpProof.Contracts/ContractSelectionInventory.cs:146-196,249-267` |

### Checked and not proposed (part two hundred eighty-nine)

- The parameter and return attribute loops are not folded into this snapshot:
  they are separate selection inputs and must retain their current scope.
- `GetRejectedSelectionFeatures` remains independently callable; only the
  `Select` call path needs an overload or private snapshot-aware helper.

### Status (part two hundred eighty-nine)

R778 is `complete`: `Select` snapshots callable attributes once and passes them
through the rejected-feature projection, while the independently callable
rejection APIs retain their original lazy path.

## Second survey, part two hundred ninety: R779 - repeated Worker API-spec template fixture

| ID | Finding | Evidence |
|---|---|---|
| R779 | **`SpecResultDomainProjectionTests` and `WorkerTcbEdgeCaseTests` duplicate the same one-declaration `ApiSpecTemplate` builder.** Each `CreateTemplate` creates documented evidence, a single method target with the same result-type slot and empty parameter/variable lists, and the same five facets: no effects, unknown allocation, no throws, caller-supplied nullness, and caller-supplied cardinality. Only the test identity strings, evidence label, and the first suite's optional postcondition projection differ. A shared Worker-test factory accepting the target/evidence identity and optional postconditions can own this fixture shape while preserving each suite's distinct inputs and assertions. The similar `ApiSpecContentDigestTests.CreateTable` is intentionally not folded in: it tests an exception-set digest and has materially different target and throw-facet semantics. | `SharpProof.Worker.Test/SpecResultDomainProjectionTests.cs:210-252`; `SharpProof.Worker.Test/WorkerTcbEdgeCaseTests.cs:1675-1713`; `SharpProof.Specs.Test/ApiSpecContentDigestTests.cs:38-76` |
### Checked and not proposed (part two hundred ninety)

- This is limited to the shared Worker-test template arrangement; the
  projection and TCB tests retain their separate result-domain and cache/error
  scenarios.
- The exception-set digest fixture is not a third copy of this helper because
  its throw metadata and target identity are the behavior under test.

### Status (part two hundred ninety)

R779 is `complete`: Worker result-domain and TCB edge-case tests now share one
API-spec template factory, with target/evidence identities and optional
postconditions remaining explicit per suite.

## Second survey, part two hundred ninety-one: R780 - repeated direct-witness scans per effect facet

| ID | Finding | Evidence |
|---|---|---|
| R780 | **`EffectContractDiagnostics.Evaluate` rescans the same direct-witness array for each effect facet.** After selecting the contracts and computing the summary, it calls `direct.FirstOrDefault(...)` separately for purity, allocation, allowed capabilities, no-throw, allowed exceptions, and (when applicable) the declared effect contract. These predicates intentionally select different first witnesses, but they all traverse the same `result.DirectWitnesses` snapshot, and the first five scans are evaluated even when their corresponding attribute selection is empty because they are passed as arguments to `Add`. A single pass can retain the first matching witness for each predicate, with the declared-contract check kept conditional, while `Add`, validity, diagnostics, evidence, and `EffectContractMappings.Violates` retain their separate semantics. | `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs:118-218` |

### Checked and not proposed (part two hundred ninety-one)

- The predicates are not interchangeable: each facet needs its own first witness,
  and the declared effect contract uses the full summary projection. The proposed
  reduction is only one traversal that records those independent candidates.
- Contract-attribute selection, summary computation, and diagnostic projection are
  separate policies and are not folded into this finding.

### Status (part two hundred ninety-one)

R780 is `complete`: effect diagnostics now scan direct witnesses once while
retaining independent first matches for purity, allocation, capabilities,
throws, allowed exceptions, and declared contracts.

## Second survey, part two hundred ninety-two: R781 - repeated temporary Git repository bootstrap

| ID | Finding | Evidence |
|---|---|---|
| R781 | **Three ArchitectureTest fixtures repeat temporary Git-repository bootstrap.** `AcceptanceScriptTests.InitializeRepositoryAsync`, `CoverageScriptTests.InitializeRepositoryAsync`, and `ProductionInventoryAuthorityTests.InitializeRepositoryAsync` each run `git init`, set a test user email and name, and wrap every child-process call in a local success assertion. The fixtures intentionally diverge after that common boundary: Acceptance creates and commits one seed file, Coverage sets additional Git display options before its caller creates commits, and Production uses its own commit helper. A shared architecture-test repository helper parameterized by identity, optional Git settings, and seed/commit policy can own the common bootstrap while preserving those scenario-specific differences. This is a higher-level fixture duplication than R259's process-runner overlap; it does not require merging the result contracts of the runners. | `SharpProof.ArchitectureTest/AcceptanceScriptTests.cs:221-257`; `SharpProof.ArchitectureTest/CoverageScriptTests.cs:1066-1101`; `SharpProof.ArchitectureTest/ProductionInventoryAuthorityTests.cs:270-299` |

### Checked and not proposed (part two hundred ninety-two)

- `ContainerSourceCleanlinessTests.InitializeRepositoryAsync` is not counted as
  a fourth copy: it builds a different multi-file container-cleanliness topology,
  uses a different command wrapper, and its repository contents are the behavior
  under test.
- The shared helper should not erase the Acceptance seed commit, Coverage's
  canonical Git options, or Production's repeated-commit workflow.

### Status (part two hundred ninety-two)

R781 is `complete`: Acceptance, Coverage, and Production inventory fixtures
share the checked Git bootstrap with scenario-specific settings preserved. The
combined targeted run passed 51/52; the remaining failure is the pre-existing
production complexity ratchet (members 5811/5808), unrelated to this helper.

## Second survey, part two hundred ninety-three: R782 - repeated object-unboxing case matrix

| ID | Finding | Evidence |
|---|---|---|
| R782 | **`IrUnboxingDifferentialRegressionTests` rebuilds one six-case object-unboxing matrix for two execution paths.** `InterpreterUsesCSharpObjectUnboxingSemantics` and `DifferentialOracleAgreesWithCompiledCSharpObjectUnboxing` each create the same object-typed variable, integer and Boolean cast terms, and the same six inputs: null for each target, a Boolean boxed into the integer path, a `long` boxed into the Boolean path, and the correctly boxed `17L`/`true` values. The first path evaluates with `IrInterpreter` and asserts `NullReference`/`InvalidCast`/value results; the second constructs the same term/value pairs and asserts differential agreement. A shared case record or factory can own the term, input value, label, and expected direct outcome while each test keeps its distinct interpreter-versus-compiled-oracle assertion path. This is fixture sharing only: it should not merge the two tests or weaken the direct semantic assertions. | `SharpProof.Testing.Test/IrUnboxingDifferentialRegressionTests.cs:10-67,70-111` |

### Checked and not proposed (part two hundred ninety-three)

- The duplicated six cases are deliberate cross-check inputs, but their
  construction does not need to be maintained twice.
- The two assertion paths remain separate because one tests interpreter outcome
  classification and the other tests agreement with compiled C#.
- The broader generated and sequence differential suites are not counted: they
  generate or compare different term/value families rather than this fixed
  unboxing matrix.

### Status (part two hundred ninety-three)

R782 is `complete`: interpreter and differential object-unboxing tests share
one six-case fixture while retaining their separate semantic assertions.

## Second survey, part two hundred ninety-four: R783 - duplicated portable PDB reader setup

| ID | Finding | Evidence |
|---|---|---|
| R783 | **Release symbol validation and production-coverage inventory duplicate the low-level portable CodeView/PDB reader lifecycle.** `SharpProof.SymbolPackageValidator.ValidatePair` opens an assembly image, constructs a `PEReader`, selects the CodeView debug entries, requires one portable entry, reads its CodeView data, opens the paired PDB with `MetadataReaderProvider.FromPortablePdbStream`, obtains a `MetadataReader`, and disposes the provider and streams around semantic checks. `Get-SharpProofProductionInventory.Get-PortablePdbModule` repeats the same assembly/PDB existence and `PEReader`/CodeView/portable-PDB setup, including `ReadCodeViewDebugDirectoryData`, `FromPortablePdbStream`, and a `finally` block that disposes every native reader and stream. The policies after that boundary are intentionally different: symbol validation checks CodeView age, PDB identity, and Source Link; coverage inventory projects source documents and sequence points. A shared compiled reader returning the pair's CodeView/PDB identity and owning disposal, or a common PowerShell/C# adapter with an explicit lifetime contract, can remove the duplicated resource and format plumbing while preserving those separate release and coverage validations. This is distinct from R292's package-entry pairing and R426's package-source enumeration. | `scripts/SharpProof.SymbolPackageValidator.cs:190-245`; `scripts/Get-SharpProofProductionInventory.ps1:218-305`; `scripts/Test-SharpProofSymbolPackages.ps1:1-32` |

### Checked and not proposed (part two hundred ninety-four)

- The two consumers must retain separate semantic checks: the symbol package
  requires one CodeView record, age, PDB identity, and canonical Source Link,
  while coverage needs assembly metadata and per-document sequence-point output.
- This is a cross-language infrastructure candidate, so a direct call from one
  script to the other would not be a suitable reduction; the shared boundary
  needs an explicit ownership and dependency decision.

### Status (part two hundred ninety-four)

R783 is `pending` and limited to release/coverage reader plumbing. No
implementation or build file was changed.

## Second survey, part two hundred ninety-five: R784 - duplicated Source Link validation

| ID | Finding | Evidence |
|---|---|---|
| R784 | **The package smoke test and release symbol validator independently implement the same Source Link assertion.** `PackageLayoutSmokeTests.VerifyPortablePdbSourceLink` and `SharpProofSymbolPackageValidator.ValidatePortablePdb` both locate the Source Link custom-debug-information record using the same `CC110556-A091-4D38-9FEC-25AB9A351A6A` GUID, require exactly one record, decode its UTF-8 JSON, require an object-shaped `documents` property, enumerate nonempty mappings, normalize backslashes in mapping names, and require every mapping value to equal the canonical raw-GitHub URL for the authenticated commit. One reports NUnit assertion context and the other throws `InvalidDataException`, but the accepted Source Link contract is otherwise duplicated across a test assembly and the release validator. A shared Source Link model/validator with caller-specific failure adapters can own the format and canonical-URL policy while the smoke test retains its package-layout assertions and the release path retains its fail-closed exception surface. This is narrower than R783's portable-reader lifecycle and separate from R292's archive entry pairing. | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:24,2086-2132`; `scripts/SharpProof.SymbolPackageValidator.cs:12,245-303`; `scripts/Test-SharpProofSymbolPackages.ps1:1-32` |

### Checked and not proposed (part two hundred ninety-five)

- The two callers should keep different failure surfaces: NUnit diagnostics for
  package smoke tests and `InvalidDataException` for release validation.
- Package-layout checks and PDB identity/CodeView checks are not folded into this
  item; only the shared Source Link record and mapping policy is in scope.

### Status (part two hundred ninety-five)

R784 is `pending` and limited to package/release Source Link validation plumbing.
No implementation or build file was changed.

## Second survey, part two hundred ninety-six: R785 - repeated package nuspec reader

| ID | Finding | Evidence |
|---|---|---|
| R785 | **Two package-test helpers repeat ZIP/nuspec loading before projecting different fields.** `PackagedProductFeed.ReadPackage` opens a package archive, selects the single `.nuspec` entry, loads it as `XDocument`, locates `<metadata>`, and then extracts package ID and version. `PackageLayoutSmokeTests.VerifyRepositoryMetadata` independently opens the archive, selects the single `.nuspec`, loads another `XDocument`, and locates `<repository>` before asserting its type, canonical URL, and commit. Both helpers operate in the same `SharpProof.Package.Test` project over the same archive/nuspec shape. A shared reader returning the validated nuspec document or metadata element can own archive disposal, single-entry selection, and XML loading while each caller retains its distinct identity or repository assertions. This is fixture parsing only; it does not merge the package-feed lifecycle with the smoke-test layout checks. | `SharpProof.Package.Test/PackagedProductFeed.cs:262-289`; `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:2143-2166` |

### Checked and not proposed (part two hundred ninety-six)

- ID/version extraction and repository metadata validation remain separate
  projections because the callers enforce different package-feed and release
  provenance policies.
- The shared seam should preserve the single-nuspec invariant and XML disposal;
  it should not hide which fields each test actually authenticates.

### Status (part two hundred ninety-six)

R785 is `complete`: package-feed and package-layout tests share one archive/
nuspec reader while retaining distinct identity and repository projections.

## Second survey, part two hundred ninety-seven: R786 - duplicated publication job plumbing

| ID | Finding | Evidence |
|---|---|---|
| R786 | **The private-preview and public NuGet publication jobs duplicate their release-pipeline plumbing.** The two mutually exclusive jobs both declare the same publication concurrency group with `cancel-in-progress: false`, depend on `release-qualification`, run on `ubuntu-latest`, check out the full repository, invoke `prepare-qualified-packages`, and finally call `docker compose run --rm ... tooling release-publish -PackageSource nupkgs` with the same tag-bound package inputs. Their real differences are the release-tag allowlist, GitHub environment, feed-credential validation, and authentication mechanism: private preview uses a configured source and secret API key, while public publishing validates a user and exchanges an OIDC token. A reusable workflow or a narrowly scoped composite action can own the shared checkout/preparation/publish path while taking explicit feed and authentication inputs; the environment, permissions, tag policy, and secret handling should remain caller-specific. This is distinct from R497's repeated tooling-image builds and from the release-tag validation repetition noted in the package-consumers workflow review. | `.github/workflows/package-consumers.yml:218-252,256-299` |

### Checked and not proposed (part two hundred ninety-seven)

- The jobs are mutually exclusive and must retain separate environment and
  credential boundaries; the candidate is shared workflow plumbing, not merging
  the private and public authorization policies.
- `release-publish` remains in-container and package-source-bound in both jobs;
  the suggested seam should not move credential material into the container or
  bypass the preceding release qualification.

### Status (part two hundred ninety-seven)

R786 is `pending` and limited to GitHub Actions publication plumbing. No
implementation or build file was changed.

## Second survey, part two hundred ninety-eight: R787 - repeated gate dispatch projection

| ID | Finding | Evidence |
|---|---|---|
| R787 | **`SharpProof.Gates.Program.Main` repeats named-gate invocation and pass projection across the `all` and single-gate paths.** The `all` branch independently calls `CorpusGate.RunAsync(root)` and `PerformanceGate.RunAsync(root)` and combines their `Passed` values; the `corpus`/`performance` branch repeats the same conditional gate invocation, then repeats type-specific `Passed` extraction through a switch before building its standalone envelope and exit code. The output shapes are intentionally different - `all` emits a combined `{ corpus, performance }` object, while a named command emits the source-bound standalone envelope when metadata is present - so serialization should remain separate. A small named-gate dispatcher returning the result and its pass state, or a table of gate delegates plus a typed pass projection, can own the repeated invocation/`Passed` plumbing while preserving the distinct `all`, standalone, `corpus-print`, `corpus-update`, and `performance-smoke` command contracts. | `SharpProof.Gates/Program.cs:29-69,99-150` |

### Checked and not proposed (part two hundred ninety-eight)

- The combined and standalone commands must retain their different JSON
  contracts; this candidate concerns only selecting a named gate and reading
  its pass state.
- `corpus-print`, `corpus-update`, and `performance-smoke` have distinct
  side effects or output shapes and are not counted as duplicate branches.

### Status (part two hundred ninety-eight)

R787 is `complete`: named corpus/performance gate execution and pass-state
projection share one dispatcher while combined and standalone JSON envelopes
remain distinct.

## Second survey, part two hundred ninety-nine: R788 - repeated parallelism policy wrappers

| ID | Finding | Evidence |
|---|---|---|
| R788 | **`SharpProof.ContainerExecution.psm1` repeats four named parallelism wrappers around one policy engine.** `Get-SharpProofTestProjectParallelism`, `Get-SharpProofSemanticTestParallelism`, `Get-SharpProofPackageTestParallelism`, and `Get-SharpProofBuildParallelism` each redeclare a cmdlet/mandatory `RepositoryRoot` parameter block and forward it to `Get-SharpProofCpuBudget`; only the policy descriptor changes. The descriptors are meaningful and must remain visible: project tests use `testProjectCpuDivisor`, semantic tests allow all visible processors after two override names, package tests use `packageTestCpuPercent`, and builds use `buildCpuPercent`, with different invalid messages. A small policy table or one internal descriptor-driven dispatcher can own the repeated parameter/forwarding plumbing while retaining the four public semantic names, override precedence, contract-field choice, and caller-specific error text. This is wrapper plumbing only; it should not collapse the distinct CPU-budget policies into one shared default. | `scripts/SharpProof.ContainerExecution.psm1:176-235` |

### Checked and not proposed (part two hundred ninety-nine)

- The four entry points communicate different scheduler policies and should
  remain separately named at call sites; only their repeated forwarding shape
  is in scope.
- `Get-SharpProofParallelismOverride` and `Get-SharpProofCpuBudget` already own
  the shared validation and calculation logic, so this does not propose a
  second budget implementation.

### Status (part two hundred ninety-nine)

R788 is `complete`: the four public parallelism wrappers now use one explicit
policy table and dispatcher, preserving override precedence, contract fields,
and caller-specific validation messages.

## Second survey, part three hundred: R789 - duplicated offline framework package allowlist

| ID | Finding | Evidence |
|---|---|---|
| R789 | **`Test-SharpProofPackageConsumers` declares the offline framework package set twice.** `New-FrameworkPackageSource` copies six package IDs and versions into `framework-packages`, including the two `Microsoft.NETFramework.ReferenceAssemblies*` packages and the toolchain-selected `Microsoft.NETCore.App.Ref`/`Microsoft.AspNetCore.App.Ref` versions. `Test-SharpProofFrameworkConsumers` then writes a separate `NuGet.Config` whose `FrameworkOffline` source mapping lists five package patterns, with a wildcard standing in for the two reference-assembly IDs. The two lists agree today, but adding or renaming an offline framework package requires editing both; a copy-only update can leave restore unable to map the package to the offline source, while a mapping-only update permits a package that was never copied. Deriving the mapping from the copied package descriptors, or validating that every copied ID matches exactly one explicit mapping pattern and that no mapping is orphaned, can retain the deliberate wildcard while removing the unguarded second authority. | `scripts/Test-SharpProofPackageConsumers.ps1:143-149,335-353` |

### Checked and not proposed (part three hundred)

- The wildcard for `Microsoft.NETFramework.ReferenceAssemblies*` is a
  deliberate compact mapping of two package IDs, not an assertion that the
  six copied packages must have six literal mapping rows.
- The package-source isolation, framework matrix, and SharpProof package
  mapping remain separate policies; only the copied-package-to-source mapping
  correspondence is in scope.

### Status (part three hundred)

R789 is `applied`: the offline source mapping is derived from the copied
package descriptors. PowerShell parsing and the focused architecture test
passed; package-source validation reached an existing authenticated-artifact
commit mismatch.

## Second survey, part three hundred one: R790 - unconsumed supported-framework contract

| ID | Finding | Evidence |
|---|---|---|
| R790 | **The supported-target-framework contract is checked against a literal instead of driving the consumer matrix.** `eng/acceptance/contract.json` declares `supportedTargetFrameworks` as `netstandard2.0`, `net8.0`, and `net472`, but `eng/acceptance/Verify.ps1` only joins that field and compares it to a second hard-coded comma-separated string. The actual package-consumer qualification independently declares `$frameworks = @('netstandard2.0', 'net8.0', 'net472')`, and the release-qualification documentation repeats the same matrix. A contract reader can supply the framework loop and let the verifier assert the expected set/order from one authority; the consumer-specific `net472` reference-assembly branch would remain explicit. As written, changing the contract field does not change which frameworks are exercised, while changing the package-consumer list leaves the contract apparently valid only if its separate literal is also edited. | `eng/acceptance/contract.json:5-9`; `eng/acceptance/Verify.ps1:474`; `scripts/Test-SharpProofPackageConsumers.ps1:372-394`; `eng/release/preview-qualification.md:12` |

### Checked and not proposed (part three hundred one)

- The framework-specific package references and per-framework source/build
  assertions remain distinct; this candidate concerns only the matrix's
  source-of-truth and its duplicated literal.
- The verifier's scalar contract assertions can stay explicit where no
  runtime consumer uses the value; `supportedTargetFrameworks` is singled out
  because a consumer matrix already exists and should consume it directly.

### Status (part three hundred one)

R790 is `applied`: package-consumer framework creation now reads the ordered
`supportedTargetFrameworks` contract field and rejects an empty matrix. The
script parser and focused consumer architecture test passed.

## Second survey, part three hundred two: R791 - duplicated timed phase wrappers

| ID | Finding | Evidence |
|---|---|---|
| R791 | **The developer-check and package-test orchestrators duplicate the timed-phase wrapper.** `Invoke-SharpProofDevCheck.ps1` and `Invoke-SharpProofPackageTests.ps1` each start a `Stopwatch`, invoke a supplied scriptblock, stop the timer, and append the same `{ name, elapsedMilliseconds }` record to a local timing list. The package-test variant wraps invocation in `try/finally`, so failed phases still produce timing evidence; the developer-check variant records only successful phases. A shared timing helper that accepts the destination collection and an explicit failure-recording policy, or a callback for recording the result, can remove the repeated stopwatch/object plumbing while preserving those different failure semantics and each command's separate timing-file lifecycle. | `scripts/Invoke-SharpProofDevCheck.ps1:78-91`; `scripts/Invoke-SharpProofPackageTests.ps1:228-245` |

### Checked and not proposed (part three hundred two)

- The failure-recording difference is intentional evidence behavior and must
  remain explicit; this candidate is the shared timing protocol, not a change
  to which phases run or whether a failed command aborts.
- The two scripts retain separate phase names, timing schemas, and output
  paths; only the local wrapper implementation is duplicated.

### Status (part three hundred two)

R791 is `applied`: both orchestrators use the shared timed-phase helper while
retaining their distinct failure-recording policies. PowerShell parsing,
helper behavior, and focused Architecture tests passed.

## Second survey, part three hundred three: R792 - shadowed generator string helper

| ID | Finding | Evidence |
|---|---|---|
| R792 | **The API-spec witness generator shadows a shared C# string encoder with a weaker local copy.** `SharpProof.Specs.Test/Generate-ApiSpecRuntimeWitnesses.ps1` dot-sources `scripts/GeneratedFileHelpers.ps1`, which already defines `ConvertTo-CSharpString`, and then redeclares the same function locally before using it for catalog witness identifiers. The local version repeats only backslash and quote escaping and requires a non-null string; the shared helper also handles null, empty input, carriage return, line feed, and tab. The current catalog uses identifier-like values, so this is not an observed output defect, but the shadowing creates two authorities with different contracts and makes future generator inputs silently less safe. Removing the local copy and using the shared helper, or giving the generator a narrowly named wrapper with an explicit restricted contract, would eliminate the accidental override while retaining the generator's identifier validation. | `SharpProof.Specs.Test/Generate-ApiSpecRuntimeWitnesses.ps1:17,35-42,115`; `scripts/GeneratedFileHelpers.ps1:213-232` |

### Checked and not proposed (part three hundred three)

- The catalog's witness-identifier and factory-name validation remains
  generator-specific; only the string-literal encoding is duplicated.
- This is a deferred reduction, not a claim that the current identifier-only
  input is malformed; the concern is contract drift caused by the shadowed
  helper.

### Status (part three hundred three)

R792 is `applied`: the generator now uses the shared string-encoding helper.
Generator verification and the focused Specs tests passed.

## Second survey, part three hundred four: R793 - divergent Git text wrappers

| ID | Finding | Evidence |
|---|---|---|
| R793 | **Two repository scripts duplicate a Git-text abstraction with materially different process semantics.** `Get-SharpProofProductionInventory.ps1` defines `Invoke-GitText` as a direct `git -C` call that merges output streams, joins lines, trims the result, and emits a fixed error prefix. `Test-SharpProofCoverage.ps1` defines the same-named helper with `ProcessStartInfo`, an argument list, separate asynchronous stdout/stderr draining, disposal in `finally`, caller-supplied failure text, and untrimmed stdout. Both are repository-scoped text queries used for commit/ref/diff authority. A shared Git runner with explicit options for error context and output normalization would remove the duplicate process contract and make the safer argument/stream behavior available to the inventory path, while preserving the coverage script's durable-ref checks and the inventory script's canonical commit validation. | `scripts/Get-SharpProofProductionInventory.ps1:27-31,310`; `scripts/Test-SharpProofCoverage.ps1:63-105,124-150,628-678` |

### Checked and not proposed (part three hundred four)

- The callers' Git queries and authority policies remain distinct; this
  candidate concerns only process launch, stream capture, and text shaping.
- The coverage helper's caller-specific failure messages and raw output needs
  must remain configurable; the proposal is not to make all Git results use
  one implicit trimming or error policy.

### Status (part three hundred four)

R793 is `applied`: inventory and coverage now use the shared argument-safe Git
runner with explicit output/error policies. PowerShell parsing, direct helper
behavior, and authority tests passed apart from the pre-existing complexity
ceiling failure.

## Second survey, part three hundred five: R794 - pilot package identity reparse

| ID | Finding | Evidence |
|---|---|---|
| R794 | **Pilot qualification reimplements the shared package identity parser.** `Get-SharpProofPilotPackageAuthority` enumerates the six candidate archives, opens each ZIP, selects its single nuspec, reads XML metadata, and extracts package ID, version, and repository commit before projecting the file name and byte count. `SharpProof.PackageIdentity.psm1` already owns the archive/single-nuspec/metadata lifecycle and exposes `Get-SharpProofPackageIdentity`, including an optional repository-aware validation path. Routing the common identity fields through that module would remove the duplicated archive and XML-query plumbing while leaving the pilot-specific exact-six-file set, expected-version/name policy, byte-size projection, and any intentionally weaker repository policy explicit. This is distinct from R551, which identifies the same kind of reuse in a publication-destination fixture rather than the pilot qualification authority. | `scripts/Get-SharpProofPilotPackageAuthority.ps1:1-49`; `scripts/SharpProof.PackageIdentity.psm1:16-104`; `scripts/Test-SharpProofPilotAuthorityFixtures.ps1:6,41-57` |

### Checked and not proposed (part three hundred five)

- The pilot authority's file-count, package-order, filename, and byte-size
  checks remain separate; only common archive and identity extraction is in
  scope.
- The shared `-RequireRepository` option validates repository type and URL as
  well as commit syntax, so adopting it must be an explicit policy choice
  rather than an accidental tightening of the pilot contract.

### Status (part three hundred five)

R794 is `applied`: pilot qualification now routes nuspec identity extraction
through the shared package-identity module while retaining its candidate-set,
version, commit, filename, and size checks. Pilot fixtures and the focused
Architecture test passed.

## Second survey, part three hundred six: R795 - repeated PowerShell Git fixture bootstrap

| ID | Finding | Evidence |
|---|---|---|
| R795 | **PowerShell fixture drivers repeat the same temporary Git-repository bootstrap.** `Test-SharpProofFuzzEvidenceLifecycle`, `Test-SharpProofMutationEvidence`, `Test-SharpProofReleaseAuthorityClosureFixtures`, and `Test-SharpProofReleaseConfigurationFixtures` each create a temporary repository, run `git init`, configure the same `SharpProof Fixture` user identity, stage fixture files, and create an initial commit before testing their authority logic. `Test-SharpProofReleaseTagFixtures` repeats the checkout half of that sequence before adding its bare remote, source/candidate commits, and tag cases. A shared fixture helper can own repository initialization, deterministic identity, and an optional initial commit while accepting the scenario-specific seed files, branch setup, remote, and later commit/tag policy. This is distinct from R781, which covers the analogous duplication in three C# ArchitectureTest fixtures and their different post-bootstrap Git settings. | `scripts/Test-SharpProofFuzzEvidenceLifecycle.ps1:41-51`; `scripts/Test-SharpProofMutationEvidence.ps1:239-245`; `scripts/Test-SharpProofReleaseAuthorityClosureFixtures.ps1:55-62`; `scripts/Test-SharpProofReleaseConfigurationFixtures.ps1:174-181`; `scripts/Test-SharpProofReleaseTagFixtures.ps1:43-70`; R781 |

### Checked and not proposed (part three hundred six)

- The fixtures retain their different seed contents, commit messages,
  default-branch settings, remotes, tags, and post-bootstrap mutations; only
  the deterministic local repository setup is shared.
- This is fixture infrastructure, not a proposal to share the authority
  assertions or to make production release scripts depend on test helpers.

### Status (part three hundred six)

R795 is `deferred`: the five fixtures differ in branch initialization,
identity, seed/commit policy, and later remote/tag mutations. A new shared
PowerShell helper would add another script/module and an option matrix for a
small four-line bootstrap, increasing fixture coupling without reducing the
overall build surface.

## Second survey, part three hundred seven: R796 - repeated PE module identity extraction

| ID | Finding | Evidence |
|---|---|---|
| R796 | **The standalone gate-evidence path and production inventory independently decode a PE module MVID.** `SharpProof.PEMetadata.Get-SharpProofModuleVersionId` opens a file, creates a `PEReader`, obtains a metadata reader, reads `GetModuleDefinition().Mvid`, formats the GUID, and disposes the reader/stream. `Get-SharpProofProductionInventory.Get-PortablePdbModule` repeats the PE/metadata setup and the same module-definition/MVID projection before continuing into its richer assembly, CodeView, PDB, and source-document inventory. A shared metadata-to-MVID projection, or a small reader-result abstraction that lets both callers reuse the already-open metadata reader, would remove the repeated identity extraction without making standalone gate evidence depend on the inventory path. This is narrower than R783: that finding covers the broader portable-PDB/CodeView reader lifecycle, while this one isolates the module identity projection that remains duplicated even when the surrounding inventories stay separate. | `scripts/SharpProof.PEMetadata.psm1:4-29`; `scripts/Get-SharpProofProductionInventory.ps1:221-240,295-299`; `scripts/Invoke-SharpProofGateEvidence.ps1:21,58` |

### Checked and not proposed (part three hundred seven)

- The standalone helper must keep its independent file validation and disposal
  behavior; the inventory must retain its assembly, CodeView, PDB, and source
  authority checks.
- The reduction target is the common MVID decoding seam, not merging the two
  callers or reopening an assembly through the standalone helper.
- The current MVID output is not alleged to be incorrect; the concern is that
  two identity authorities can drift in reader setup or GUID formatting.

### Status (part three hundred seven)

R796 is `deferred`: the production inventory and standalone gate intentionally
own different PE/PDB reader lifetimes and module boundaries. Sharing the MVID
projection would require a new cross-script module dependency (and duplicate
fixture copies) for only one expression, adding coupling rather than reducing
the repository's script surface.

## Second survey, part three hundred eight: R797 - repeated worker version metadata reads

| ID | Finding | Evidence |
|---|---|---|
| R797 | **Launcher startup reads the staged worker's version-resource metadata twice for one run.** `RunMain` computes `expectedInputHash` and then `expectedVersions` from the same `WorkerRuntimeClosureSnapshot`, but `ComputeExpectedInputHash(WorkerVerifyRequest, byte[], WorkerRuntimeClosureSnapshot)` and `ComputeExpectedVersions` each call `FileVersionInfo.GetVersionInfo(snapshot.ExecutionWorkerPath)` independently. The first projection needs product name and product version for the input digest; the second needs product version for the response provenance, so their final values remain different, but the file metadata load and required-version handling are repeated on every launch. A single worker-version projection returning the required product name/version can feed both computations, while retaining the existing standalone overload that builds a temporary snapshot for tests and preserving the digest/provenance fields as separate outputs. | `SharpProof.Worker.Launcher/Program.cs:62-67,303-341` |

### Checked and not proposed (part three hundred eight)

- The input-hash and response-version contracts remain separate; only the
  shared read of the staged worker's version resource is in scope.
- The snapshot must remain the source of the worker binary hash and closure
  identity; the reduction should not add an independent uncaptured file read.
- This is a per-launch repeated metadata read, not a claim that either output
  field is redundant or that the public test helper overload should disappear.

### Status (part three hundred eight)

R797 is `applied`: the launcher now reads and validates the staged worker's
product name/version once per launch and feeds both projections. The existing
standalone helper overloads and digest/provenance fields remain unchanged.

## Second survey, part three hundred nine: R798 - repeated launcher path-topology validation

| ID | Finding | Evidence |
|---|---|---|
| R798 | **Launcher request construction reruns the full path-topology validator around one manifest read.** `RunMain` first calls `LauncherArguments.ValidateDistinctPaths` before the worker snapshot exists, which is an intentional early check for the static worker/launcher runtime closure. `CreateRequest` then calls the same validator before reading the compiler manifest and again after projecting the manifest's project directory. The latter two passes repeat canonicalization of the worker/runtime roots, publication paths, request/result/manifest paths, runtime-directory containment checks, and pairwise conflict checks; only the cache argument differs because the default cache path cannot be derived until the manifest is read. A split validation result that caches the stable path set and adds only the newly knowable cache path after manifest projection, or a validator with an explicit stable-topology phase plus cache-only phase, can retain the pre-snapshot fail-closed check and the post-manifest default-cache check without replaying every filesystem identity walk. This is distinct from R494, which covers repeated path resolution inside the MSBuild invalidation task rather than repeated validation phases in the launcher request projection. | `SharpProof.Worker.Launcher/Program.cs:57-66,951-971,974-1043`; `SharpProof.Worker.Protocol/WorkerCachePath.cs:5-14` |

### Checked and not proposed (part three hundred nine)

- The early pre-snapshot validation remains necessary because it rejects
  collisions before opening and staging the worker runtime.
- The final cache check remains necessary because the default cache path is
  derived from `artifact.Compilation.ProjectDirectory` after the manifest is
  read; the proposal is to isolate that new input, not to remove the check.
- Runtime snapshot component paths, publication-marker paths, symlink policy,
  and pairwise conflict semantics must remain in the retained topology result.

### Status (part three hundred nine)

R798 is `applied`: the preflight validates the configured cache path with the
stable topology, and request construction skips only that already-completed
pass while retaining the manifest-dependent final validation.

## Second survey, part three hundred ten: R799 - repeated canonicalization inside launcher path validation

| ID | Finding | Evidence |
|---|---|---|
| R799 | **`LauncherArguments.ValidateDistinctPaths` repeatedly canonicalizes the same paths within one validation pass.** Publication paths first go through `RequireLocalPath`, which canonicalizes and verifies their filesystem type, then are canonicalized again when building `writablePaths` and again in the final conflict set; worker/runtime roots and launcher companion paths are canonicalized once to derive `runtimeDirectories` and again for `paths`. The containment check then passes those already-canonical `writablePaths` and `runtimeDirectories` into `LinuxPathIdentity.IsSameOrDescendant`, whose public boundary canonicalizes both arguments again. Publication-marker derivation also canonicalizes its publication input before the resulting marker path is canonicalized for the conflict set. A local canonical-path map or internal canonical-only path predicates can preserve the public validation boundaries and all symlink/filesystem checks while reusing identities within this method. This is narrower than R798: R798 covers repeated whole validation phases around manifest loading, while R799 covers redundant path walks inside one phase. | `SharpProof.Worker.Launcher/Program.cs:985-1029`; `SharpProof.Host/LinuxPathIdentity.cs:315-330,332-335,469-480` |

### Checked and not proposed (part three hundred ten)

- `RequireLocalPath` must remain for publication admission, including its
  supported-filesystem check; the candidate is reuse of its canonical result.
- The public `LinuxPathIdentity` methods must retain their canonicalizing
  boundaries for external callers; only an explicitly canonical internal seam
  should bypass that work.
- Marker identity, runtime-component identity, symlink rejection, and pairwise
  nested/same-file conflict semantics remain part of the validation contract.

### Status (part three hundred ten)

R799 is `deferred`: canonicalization is the security boundary for the public
Linux path-identity API, and the conflict check also detects hard-link aliases.
Bypassing those public calls would require a new canonical-only API or a
second security-sensitive implementation for a small validation loop, so the
review risk outweighs the line-count saving.

## Second survey, part three hundred eleven: R800 - duplicated solver-lane construction

| ID | Finding | Evidence |
|---|---|---|
| R800 | **Initial solver-lane creation and timed-out-lane renewal duplicate the backend-to-lane projection.** `SharpProofWorker.TryCreateLanes` invokes the backend factory, rejects a null result, wraps the backend in a `CallableVerifier`, derives its consumed-resource reader, and records disposable ownership through `CreateLane`; `VerificationLane.Renew` repeats the factory/null check and then assigns the same verifier/resource-reader/owner state after accepting a replacement. The two paths must retain different policies - initial creation rejects duplicate backends across the partially built list and disposes all created lanes on setup failure, while renewal serializes replacement, rejects a backend already held by another lane, and keeps the prior backend alive until acceptance - but a shared backend-to-lane-state factory or replacement projection can centralize the repeated construction and ownership wiring without merging those failure and retirement protocols. | `SharpProof.Worker/SharpProofWorker.cs:536-589,598-609,620-667`; `SharpProof.Worker.Test/WorkerTests.cs:5476-5579,5651-5862,5937-6000` |

### Checked and not proposed (part three hundred eleven)

- The initial multi-lane allocation and renewal state machine remain separate;
  the candidate covers only backend creation, verifier/resource projection,
  and disposable-owner assignment.
- Duplicate-backend detection, lock scope, prior-owner disposal ordering,
  replacement cleanup, and typed renewal outcomes must remain explicit at
  their respective lifecycle boundaries.
- This is a deferred reduction: a helper should not hide the distinction
  between setup failure cleanup and replacement acceptance merely to remove a
  few repeated lines.

### Status (part three hundred eleven)

R800 is `deferred` pending a seam that reduces the repeated lane projection
without obscuring the distinct initial-allocation and renewal ownership rules.
No implementation or build file was changed.

## Second survey, part three hundred twelve: R801 - repeated incoming-environment scans

| ID | Finding | Evidence |
|---|---|---|
| R801 | **`AcyclicBlockPredicateExecutor.Merge` rescans every incoming environment variable before merging it.** For each variable from the first incoming state, the method first calls `values.Any` to reject a missing binding, calls a second `values.Any` to detect a differing term ID, and then makes another reverse pass over `values` to build conditional terms when the IDs differ. A per-variable accumulator can combine the missing-binding and differing-value checks in one forward pass, retaining only the reverse construction pass when a conditional is actually required. This removes a full list scan on the common complete-and-equal path while preserving incoming-state ordering, omission of partially bound variables, conditional precedence, and the existing `Spend` accounting. This is a local merge-loop reduction, not a duplicate proposal for the broader shared flow-engine structure in R542. | `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs:271-327`; `SharpProof.Worker.Test/AcyclicBlockPredicateExecutorTests.cs:1-860` |

### Checked and not proposed (part three hundred twelve)

- The predicate disjunction, sorted incoming-state order, and reverse
  conditional construction remain separate because they encode different
  semantics.
- The candidate does not remove the reverse pass when differing values need
  nested conditionals; it removes only the redundant second predicate scan and
  keeps the existing resource-budget accounting explicit.
- R542 covers duplicated control-flow machinery between the summary builder
  and worker executor; R801 is limited to repeated scans inside this one
  executor's environment merge.

### Status (part three hundred twelve)

R801 is `applied`: incoming environments now combine completeness and differing
value detection in one forward scan; conditional construction and accounting
remain unchanged. The focused worker fixture suite passed.

## Second survey, part three hundred thirteen: R802 - repeated release-bundle cardinality check

| ID | Finding | Evidence |
|---|---|---|
| R802 | **`Test-SharpProofReleaseArtifacts.ps1` repeats the release-bundle cardinality check immediately before calling its canonical helper.** The script first rejects any manifest whose artifacts are not exactly six rows with three `package` and three `symbols` rows, then calls `Test-SharpProofReleaseBundleTopology`, whose first condition performs the same directory-exists, six-row, and 3/3 kind checks before validating the file set. The caller still needs its manifest-specific package-ID, version, byte-size, payload, dependency, and repository assertions, but those checks do not depend on a second cardinality guard. Letting the shared topology helper own the bundle-shape admission, or changing it to return a validated topology result for the later checks, removes one duplicated contract and keeps R558's separate expected-name validation issue isolated. | `scripts/Test-SharpProofReleaseArtifacts.ps1:63-72`; `scripts/SharpProof.ReleaseBundle.ps1:52-65` |

### Checked and not proposed (part three hundred thirteen)

- The release-artifact script retains its package-ID and symbol-pair loops,
  actual file-name and byte checks, payload projection, third-party metadata,
  and repository/version authority checks.
- R558 concerns duplicate expected-name validation *inside* the shared
  topology helper and its exact-file-set helper; R802 concerns the caller
  repeating only the cardinality/kind precondition before invoking that helper.
- The reduction should preserve the helper's distinct directory and exact
  regular-file-set checks rather than replacing them with a caller-only test.

### Status (part three hundred thirteen)

R802 is `applied`: release-artifact validation now delegates the six-artifact
and 3/3 package/symbol shape to `Test-SharpProofReleaseBundleTopology`, which
already owns that admission check; package-specific assertions remain local.

## Second survey, part three hundred fourteen: R803 - repeated manifest claim partition scans

| ID | Finding | Evidence |
|---|---|---|
| R803 | **`CompilerManifestArtifactJson.ClaimPartitions` scans each sorted claim group twice to build two disjoint projections.** The constructor materializes one ordinal-sorted `Claims` array, then runs one `Where`/materialization pass for `Postconditions` and another for `Effects`. Each `ClaimPartitions` instance is created for a callable during feature-scope parity validation, so the same claims are traversed again even though each row can be classified into at most one of the two retained arrays. A single post-sort loop with two builders can preserve the full sorted `Claims` property, stable per-partition order, and the later independent consumers while removing the second partition scan. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:330-344,573-589`; `SharpProof.Worker.Test/CompilerManifestArtifactTests.cs` |

### Checked and not proposed (part three hundred fourteen)

- The full sorted `Claims` snapshot remains available for callers that need
  all claim kinds; only the construction of the two filtered arrays is fused.
- Postcondition and effect arrays remain separate outputs, with their current
  order and filtering rules; no claim taxonomy or feature-parity rule changes.
- The cost is bounded by each callable's claim count, so this remains a small
  deferred cleanup rather than a reason to introduce a broad collection
  abstraction into manifest validation.

### Status (part three hundred fourteen)

R803 is `applied`: the sorted claim snapshot now fills the postcondition and
effect partitions in one pass, preserving both arrays and their order.

## Second survey, part three hundred fifteen: R804 - repeated feature-scope assumption scan

| ID | Finding | Evidence |
|---|---|---|
| R804 | **`CompilerManifestArtifactJson.HasFeatureScopeParity` re-queries the same callable-assumption facts in separate policy checks.** The method first runs an `Any` predicate over `callable.Assumptions` to reject contract assumptions when contracts are not selected or allowed, then immediately runs the same kind of `Any` predicate to derive `hasContractAssumptions` for the selection-reason invariant. It also reads `callable.Assumptions.Length == 0` in both sides of that invariant. A small local assumption summary, computed once per callable, can preserve the null filtering and kind classification while removing the repeated scans and making the two policy checks share one stated fact. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:601-628` |

### Checked and not proposed (part three hundred fifteen)

- The selected-effects and selected-contracts flags remain separate because
  they govern different feature gates; this finding concerns only the shared
  assumption facts.
- The later ordered assumption projection is retained: it compares declared
  assumptions with lowered clauses and needs the full IDs and kinds, not just
  the contract-assumption presence bit.
- Null assumptions remain rejected/ignored exactly as the current predicates
  specify; the reduction is a cached summary, not a change to malformed-input
  policy.

### Status (part three hundred fifteen)

R804 is `deferred`: no implementation change is authorized in this audit, and
the repeated scan is small and bounded by one callable's assumptions. It is a
straightforward local cleanup if this validation path is being refactored, but
it does not justify editing the implementation solely to remove a few scans.

## Second survey, part three hundred sixteen: R805 - repeated feature-parity projection comparisons

| ID | Finding | Evidence |
|---|---|---|
| R805 | **`CompilerManifestArtifactJson.HasFeatureScopeParity` enumerates corresponding arrays once per projected field.** The effect check first projects `loweredEffects` and `effects` to claim IDs, then traverses both arrays again to compare contract kinds. The successful-callable postcondition check repeats the shape: one pair of projections compares claim IDs and a second pair compares manifest evidence. These are ordered, index-aligned arrays whose lengths are checked immediately before the comparisons. A bounded indexed comparison (or one shared paired projection with the required null guards) can compare the fields during one traversal per branch while preserving ordering, fail-closed malformed-row handling, and the later independent authority/evidence checks. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:635-641,703-710` |

### Checked and not proposed (part three hundred sixteen)

- R651 covers duplicate effect-evidence validation and authority matching;
  R805 is only about the repeated identity/evidence projection enumerations
  in this feature-parity method.
- The effect-authority loop and the later declared-versus-lowered assumption
  comparison remain separate because they have different inputs and policies.
- The length checks and current null-sensitive validation behavior must remain
  before any fused comparison; this is not a proposal to weaken malformed
  manifest rejection or to merge the authority checks.

### Status (part three hundred sixteen)

R805 is `deferred`: the arrays are small per callable and the cleanup is local,
but the current LINQ expressions are readable and no implementation edits are
authorized in this audit.

## Second survey, part three hundred seventeen: R806 - duplicated lowered-claim correspondence checks

| ID | Finding | Evidence |
|---|---|---|
| R806 | **The compiler-artifact validation paths duplicate the manifest-to-lowered claim correspondence rule.** `CompilerLoweredArtifact` validates the lowered artifact by filtering manifest claims and lowered clauses to postconditions, comparing count, ordinal claim IDs, and projected manifest evidence, then separately normalizing and ordinal-sorting declared versus lowered assumptions before `SequenceEqual`. `CompilerManifestArtifactJson.HasFeatureScopeParity` performs the same postcondition identity/evidence comparison and the same declared-versus-lowered assumption normalization for each callable during feature-scope validation. Their outer contracts should remain different - one throws while decoding a lowered artifact, the other returns a fail-closed parity result and has different null-tolerance - but small helpers parameterized by null/error policy can give both paths one correspondence definition and remove a drift surface. | `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:458-477`; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:701-729` |

### Checked and not proposed (part three hundred seventeen)

- R526 concerns generic assumption comparison duplicated between the compiler
  response authority and worker protocol JSON; R806 is the narrower
  manifest-versus-lowered correspondence duplicated inside compiler-artifact
  validation.
- Lowered effect claims, effect authorities, clause predicate hashes, and IR
  structure have distinct validation responsibilities and are not included in
  this proposed seam.
- Any shared helper must preserve the throwing `InvalidDataException` path,
  the boolean fail-closed path, and their existing null/malformed-row rules;
  no implementation change is made here.

### Status (part three hundred seventeen)

R806 is `deferred`: the duplicated rule is a real maintenance risk, but the
error/null-policy differences make a careless shared helper more complex than
the duplicated projections. It is best addressed only with explicit policy
parameters or a common validated correspondence result.

## Second survey, part three hundred eighteen: R807 - repeated canonical-variable validation scans

| ID | Finding | Evidence |
|---|---|---|
| R807 | **`CompilerLoweredArtifact.ValidateVariables` makes several independent passes over the same canonical-variable array before and after its row loop.** It projects all variable IDs into a set, filters and sorts parameters, separately projects labels, counts receiver and result roles, filters receiver/parameter rows once for a set and again for a dictionary, and finally filters pre-state rows twice to compare distinct current-state IDs with their count. The per-row loop still needs the indexed variable/artifact-row pairing and factory lookups, but one validation accumulator could collect role counts, labels, current-variable membership/map, and pre-state injectivity while preserving the sorted-parameter check and all row-shape checks. This removes repeated bounded scans and makes the role invariants share one source of truth. | `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:622-692` |

### Checked and not proposed (part three hundred eighteen)

- The indexed row loop remains necessary: it validates each artifact row against
  its canonical variable, derives type/interval facts, and must preserve the
  artifact-row index relationship.
- Parameter ordering is intentionally sorted and compared with a dense ordinal
  range; a fused accumulator must retain that exact ordinal invariant rather
  than merely count parameters.
- The current/pre-state distinction and `CurrentStateVariable` type checks are
  semantic validation rules, not redundant branches. Only their collection
  plumbing is a candidate for sharing.

### Status (part three hundred eighteen)

R807 is `deferred`: the method is a validation boundary and a large fused
accumulator could become harder to audit than the current clear projections.
The repeated scans are still a plausible local cleanup if profiling or a
future validation refactor makes the allocation cost material.

## Second survey, part three hundred nineteen: R808 - effect identity scans before indexed decoding

| ID | Finding | Evidence |
|---|---|---|
| R808 | **`CompilerLoweredArtifact.DecodeEffects` performs separate distinct-ID scans immediately before indexed identity checks.** After filtering the expected manifest effects and checking both array lengths, it projects `EffectClaims` to claim IDs and counts a distinct set, then repeats that pass for `EffectAuthorities`. The subsequent indexed loop compares every evidence ID and contract kind with the expected manifest row, and `CompilerEffectAuthority.Matches` compares the authority ID with both expected and evidence IDs again. Once the manifest's unique claim-ID invariant is trusted, a single indexed validation/identity accumulator can combine the needed shape checks and remove the two pre-loop scans; if this method must remain independently defensive, one shared set-building pass can at least retain both uniqueness checks without two separate LINQ pipelines. | `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs:579-609`; `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:80-96`; `SharpProof.Worker.Protocol/ProtocolJson.cs:503-516` |

### Checked and not proposed (part three hundred nineteen)

- The evidence codec validation and authority matching remain independent
  checks; R651 covers their repeated validation work and is not replaced by
  this identity-scan observation.
- The manifest-level unique-claim validation is an important precondition. A
  cleanup must not silently remove the decoder's defense if `DecodeEffects`
  can be called with an unvalidated manifest.
- Error ordering and null handling should be preserved, especially because
  the current distinct projections include null IDs and the indexed loop has
  its own malformed-row behavior.

### Status (part three hundred nineteen)

R808 is `deferred`: the scans are cheap relative to effect evidence decoding,
and retaining an independent decoder defense may be intentional. A future
change should only fuse them after documenting the manifest-validation
precondition or after introducing a shared identity accumulator.

## Second survey, part three hundred twenty: R809 - per-claim rebuilding of proof-label sets

| ID | Finding | Evidence |
|---|---|---|
| R809 | **`CompilerResponseEvidenceAuthority` rebuilds target-invariant proof-label sets for every claim.** `Validate` creates one `AssumptionShape` per target and reuses it across the target's claims, but `ValidateProofCore` calls `EntryLabels(target)` or `AllLabels(target)` afresh for each claim that needs proof-core validation. `HasAdmissibleEntryCore` independently calls `EntryLabels(target)` for the same contradictory-precondition result, so the later entry-only `ValidateProofCore` call can rebuild the identical set again. A per-target proof-label projection, containing the all-label and entry-label sets, can be created alongside `AssumptionShape` and passed through the claim validators while keeping proof-core membership, entry-only policy, and result-specific checks unchanged. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:42-79,98-166,353-440` |

### Checked and not proposed (part three hundred twenty)

- Proof-core membership remains claim-specific: the proposed cache contains
  only the allowed label vocabulary, not a shared verdict for any response
  result.
- `ClauseLabels` and domain/summary/body label rules remain intact; the
  candidate only reuses their target-derived projection across claims.
- Assumption usage, effect witnesses, model canonicalization, and replay are
  separate response-evidence policies and are not folded into this cache.

### Status (part three hundred twenty)

R809 is `deferred`: caching the label vocabulary is a clean seam, but it would
thread another target-specific object through several private validation
methods. The repeated work becomes more material for callables with many
claims; until then, the current local construction remains easy to audit.

## Second survey, part three hundred twenty-one: R810 - repeated clause-label projection

| ID | Finding | Evidence |
|---|---|---|
| R810 | **`CompilerResponseEvidenceAuthority` reconstructs the same clause-label tuples for three target-derived consumers.** `AllLabels` calls `ClauseLabels(target)` to admit every clause label, `EntryLabels` calls it again to select nontrivial `requires` labels, and `AssumptionIdsForCore` calls it once more to build the label-to-assumption map. These helpers are invoked from per-claim validation, and a single target can therefore rebuild the clause walk several times before the response-specific proof-core checks run. A target label catalog containing the clause tuples and the derived entry/assumption projections can feed all three consumers while preserving the distinct body, summary, domain, and requires-only policies. | `SharpProof.CompilerArtifact/CompilerResponseEvidenceAuthority.cs:126-138,314-319,379-435,442-529` |

### Checked and not proposed (part three hundred twenty-one)

- R809 targets rebuilding the final all-label/entry-label sets; R810 records
  the residual repeated `ClauseLabels` source projection used by those sets
  and by assumption-ID extraction.
- `requiresOnly` and `Assume` selection must remain separate, as must the
  entry-only treatment of literal-true requires clauses.
- Body, summary, and domain labels are not interchangeable with clause labels;
  they remain separate derived projections in any catalog object.

### Status (part three hundred twenty-one)

R810 is `deferred`: the repeated clause walk is target-invariant and can be
  cached, but introducing a label catalog would add plumbing to a fail-closed
  authority. It is worthwhile if response validation profiles show many claims
  per callable or if R809 is implemented.

## Second survey, part three hundred twenty-two: R811 - duplicated lowerer contract partitions

| ID | Finding | Evidence |
|---|---|---|
| R811 | **`CompilerCallableLowerer.Prepare` partitions and counts the same contract collections with four independent passes.** It filters `target.Entry.Assumptions` once for preconditions and once for user assumptions, then counts `contracts.Clauses` once for `Requires` and once for `Assume` solely to compare those counts with the two materialized arrays. The arrays are consumed later to assign assumption IDs while the clauses are projected again into `CompilerPreparedClause` values. A small partition/count result can build both assumption arrays and both clause-kind counts in one pass per source collection, preserving the ordinal assignment and the unsupported-contract failure gate while removing repeated filtering pipelines. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs:59-83` |

### Checked and not proposed (part three hundred twenty-two)

- The later clause projection still needs to preserve source clause order and
  assign independent precondition, user-assumption, and postcondition ordinals;
  those semantics are not redundant.
- Preconditions and user assumptions remain separate arrays because their IDs
  are consumed by different clause kinds; only their collection plumbing is a
  candidate for fusion.
- `HasManifestParity` and body preparation are separate validation stages and
  are not folded into this local partition helper.

### Status (part three hundred twenty-two)

R811 is `deferred`: the collections are normally small, and a fused result
object would add more setup to an already policy-heavy preparation method.
  It remains a clear cleanup if lowerer allocation or contract-heavy methods
  make the repeated passes measurable.

## Second survey, part three hundred twenty-three: R812 - recounting already-indexed lowered calls

| ID | Finding | Evidence |
|---|---|---|
| R812 | **`CompilerCallableLowerer.PrepareBody` recounts call instructions from the completed IR after already visiting every selected call.** `RoslynProgramLowerer` records each emitted `IrCallInstruction` in `selected.Calls`; the lowerer then iterates that dictionary to classify every call as a prepared specification or summary call. Its final completeness guard walks every block and instruction in `lowering.Program` again, counting `IrCallInstruction` values only to compare that number with `specCalls.Count + summaryCalls.Count`. Comparing the classified count with the already-indexed `selected.Calls.Count` (or retaining a counter alongside the classification loop) removes the second whole-program traversal while preserving the guard that every lowered call received a supported preparation. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs:174-213`; `SharpProof.Frontend/RoslynProgramLowerer.cs:77,114,404-409,791-795` |

### Checked and not proposed (part three hundred twenty-three)

- The classification loop remains necessary because specification and summary
  calls have different admission and evidence projections.
- The `selected.Calls` dictionary is the lowerer's source of call bindings and
  is populated at each call emission; the proposed comparison does not remove
  the IR program or its call instructions.
- Program validation and acyclic-body checks remain independent gates; this
  finding targets only the final call-count recount.

### Status (part three hundred twenty-three)

R812 is `applied`: the lowerer now compares classified calls with the existing
indexed call set, preserving the completeness assertion without a second full
IR traversal.

## Second survey, part three hundred twenty-four: R813 - repeated contract-statement inventory scans

| ID | Finding | Evidence |
|---|---|---|
| R813 | **`CompilerCallableLowerer.ContainsOnlyContractStatements` rescans one cached clause inventory for every expression statement.** The outer `All` walks the method body statements, while each nonempty expression calls `IsContractExpression`; that helper calls `_contracts.GetClauseInventory(target.Method)` and runs `Clauses.Any` over the complete inventory to match the expression's syntax tree and span. `ContractClauseInventoryBuilder` caches the no-body inventory, so repeated construction is avoided, but the normalization/lookup and linear clause scan still repeat for every statement. Passing one inventory or a precomputed syntax-site set into the whole-body check can preserve exact syntax-tree/span matching and the empty-statement rule while removing the per-statement projection. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs:122-125,614-635`; `SharpProof.Contracts/ContractBinder.cs:121-126`; `SharpProof.Contracts/ContractClauseInventoryBuilder.cs:42-53` |

### Checked and not proposed (part three hundred twenty-four)

- The clause inventory's placement and validity semantics remain authoritative;
  the candidate only reuses its already-computed invocation sites.
- Empty statements still pass directly, and expression statements must retain
  the current syntax-tree and exact-span comparison rather than a text-only
  or broad containment match.
- `PrepareBody`'s separate invalid-clause-site projection is not merged here;
  it serves body lowering's elision predicate and has a different consumer.

### Status (part three hundred twenty-four)

R813 is `deferred`: the inventory itself is cached and contract-only bodies are
  usually short, so the runtime gain is bounded. A per-check site set is a
  simple cleanup if large contract-only bodies or repeated lowerer preparation
  make the repeated scans visible.

## Second survey, part three hundred twenty-five: R814 - disjoint contract inventory walks

| ID | Finding | Evidence |
|---|---|---|
| R814 | **`ClaimManifestBuilder.BuildTarget` walks the same clause inventory separately for postconditions and assumptions.** `CreatePostconditions` filters `inventory.Clauses` to non-nested `Ensures` rows and fingerprints them, while `CreateAssumptions` then walks the complete inventory again to select non-nested `Requires` and `Assume` rows and fingerprint them. The projections intentionally produce different manifest objects and are augmented by different attribute sources, but a single clause pass can route each row to the appropriate candidate builder before those distinct attribute projections are appended. That would remove a repeated inventory traversal and make the disjoint clause-kind partition explicit. | `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs:78-89,178-226,261-302` |

### Checked and not proposed (part three hundred twenty-five)

- Return-type closed-contract attributes remain postcondition candidates, while
  parameter closed-contract attributes and trusted attributes remain assumption
  candidates; these are separate sources and are not merged.
- Nested-callable exclusion, invocation/attribute fingerprint choice, rank
  assignment, and manifest ordinal assignment retain their current policies.
- The contract source resolution itself remains one shared input; this finding
  concerns only the two local walks over its already materialized clauses.

### Status (part three hundred twenty-five)

R814 is `deferred`: the inventory is normally small and combining builders
  would add coordination to `BuildTarget`. It is a reasonable cleanup if the
  builder gains more clause-derived candidate kinds or profiling shows the
  repeated walk in large compilations.

## Second survey, part three hundred twenty-six: R815 - duplicated syntax-tree preflight traversal

| ID | Finding | Evidence |
|---|---|---|
| R815 | **`CompilerCompilationCapture.Capture` traverses every syntax tree once for resolver-directive rejection and again for capture.** The preflight `compilation.SyntaxTrees.Any(tree => HasResolverDirective(...))` obtains each tree's root and descends into all trivia; after that succeeds, `CaptureTrees` walks the same tree collection and `CaptureTree` reads text, line mappings, parse options, symbols, and features. The syntax-tree cache avoids rebuilding the snapshot on later captures but does not avoid the resolver-directive traversal, so repeated capture calls pay the preflight cost every time. A capture pass that records the unsupported-directive fact while constructing each snapshot, or a cache entry that includes the preflight result, can preserve fail-closed rejection while avoiding a second tree walk. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:90-112,45-56,170-229,516-520` |

### Checked and not proposed (part three hundred twenty-six)

- Resolver-directive rejection remains an explicit security/compatibility gate;
  the candidate does not allow `#load` or `#r` merely to avoid a traversal.
- Line-map, text, parse-option, preprocessor, and feature capture remain the
  independent snapshot projections required by the compilation fingerprint.
- Any cache result must account for cancellation and the compilation identity;
  a process-global directive flag detached from the cached syntax trees would
  be an unsafe shortcut.

### Status (part three hundred twenty-six)

R815 is `deferred`: one capture normally builds one cache entry and the
preflight is bounded by source size. It is a plausible cleanup for repeated
capture or large generated compilations, provided the unsupported-directive
decision remains fail-closed.

## Second survey, part three hundred twenty-seven: R816 - security pipeline restores the solution twice

| ID | Finding | Evidence |
|---|---|---|
| R816 | **The `security` container command restores the full solution twice in one pipeline.** Its first child command, `dependency-audit`, runs `dotnet restore SharpProof.sln --locked-mode` before invoking the audit script; after that child exits, `security` launches the `build` child, whose first operation is the same locked solution restore before compiling. The audit script consumes the restored project assets through `dotnet list package` and does not intentionally rewrite the solution, so the second restore is normally redundant work and a second failure point. A security-specific orchestration path can restore once, run the audit, and build with `--no-restore`, or let the child commands accept an explicit validated reuse flag while retaining the default standalone restore behavior. | `scripts/Invoke-SharpProofContainer.ps1:141-145,153-156,489-499`; `scripts/Test-SharpProofDependencyAudit.ps1:198-249` |

### Checked and not proposed (part three hundred twenty-seven)

- Locked mode, the audit's package graph query, and the subsequent solution
  build remain required; this is not a proposal to skip restore entirely.
- Standalone `dependency-audit` and `build` commands should keep restoring by
  default because callers may invoke either without the other.
- The fix must preserve the child-process failure propagation and not rely on
  mutable host assets from a different configuration or target.

### Status (part three hundred twenty-seven)

R816 is `applied`: the security command owns one locked solution restore, then
runs dependency audit and the Release build against those restored assets.

## Second survey, part three hundred twenty-eight: R817 - package-consumer test restore after solution restore

| ID | Finding | Evidence |
|---|---|---|
| R817 | **`package-consumers` restores the product test project again after the container command already restored the solution.** `Invoke-SharpProofContainer.ps1` runs `dotnet restore SharpProof.sln --locked-mode` before calling `Test-SharpProofPackageConsumers.ps1`. That script correctly restores each generated framework consumer and then builds those consumers with `--no-restore`, but its final focused `SharpProof.Package.Test` invocation passes only `test`, the project, configuration, logger, and optional filter. Without `--no-restore`, `dotnet test` can launch another restore for the already-restored product project, outside the explicit locked-mode call. Adding `--no-restore` to that focused invocation, or making the script own the product restore and removing the outer one, can preserve the package-source environment and focused test filter while avoiding a redundant restore path. | `scripts/Invoke-SharpProofContainer.ps1:354-360`; `scripts/Test-SharpProofPackageConsumers.ps1:449-476,555-573` |

### Checked and not proposed (part three hundred twenty-eight)

- The per-framework consumer restores remain necessary because those projects
  are generated under temporary SDK roots with an offline framework source.
- The generated consumer builds already use `--no-restore`; R817 concerns only
  the final in-repository package-test project.
- Locked-mode enforcement must remain on the owning solution restore; simply
  dropping both restore operations would weaken package graph validation.

### Status (part three hundred twenty-eight)

R817 is `applied`: the final package-test invocation now uses the locked solution
restore already owned by the container command.

## Second survey, part three hundred twenty-nine: R818 - unreachable unsupported-host sample branch

| ID | Finding | Evidence |
|---|---|---|
| R818 | **`Test-SharpProofSamples.ps1` rejects every unsupported host before it reaches the only branch that handles unsupported hosts.** The script computes `$isSupportedWorkerHost` and immediately throws when it is false, so execution cannot reach the later `if ($isSupportedWorkerHost)` around the strict-library sample. Its `else` branch then repeats the same unsupported-host explanation and expected failure assertion that the earlier guard has made unreachable. The two-stage policy creates dead control flow and makes the script appear to support an explicit unsupported-host test path while actually requiring the canonical host for every invocation. Either keep the fail-fast host guard and remove the unreachable `else`, or defer the guard until after the advisory sample checks if the unsupported-host behavior is intended to remain an assertion. | `scripts/Test-SharpProofSamples.ps1:18-25,369-430` |

### Checked and not proposed (part three hundred twenty-nine)

- The canonical Linux amd64 restriction itself is intentional and should remain
  fail-closed for package-backed sample execution.
- The strict-library result-file assertions, diagnostic expectations, and
  malformed-contract failure remain separate sample contracts; this finding is
  only about the unreachable host split.
- No behavior change is implied until the desired unsupported-host test mode is
  clarified: removing the branch preserves the current fail-fast behavior,
  while moving the guard would change which samples run on other hosts.

### Status (part three hundred twenty-nine)

R818 is `deferred`: the dead branch is a small clarity issue, but choosing
between fail-fast enforcement and an actually exercised unsupported-host test
changes the script's supported invocation contract.

## Second survey, part three hundred thirty: R819 - repeated pilot project uniqueness scan

| ID | Finding | Evidence |
|---|---|---|
| R819 | **`Test-SharpProofPilotReport` proves catalog project uniqueness twice.** While validating each catalog row, it canonicalizes the project path and inserts that full path into `$catalogProjects`; a duplicate physical project path immediately returns `false`. The catalog is already required to contain exactly five rows, so that hash-set pass establishes five distinct projects. The following aggregate condition nevertheless projects the original relative `project` strings through `Select-Object -Unique` and requires a count of five. That second scan adds a separate representation of the same invariant and can be removed, or replaced by an assertion over the canonical set, without changing the library-uniqueness or category-distribution checks that remain independent. | `scripts/Test-SharpProofPilotReport.ps1:62-75,91-95` |

### Checked and not proposed (part three hundred thirty)

- The separate library uniqueness check remains meaningful because the loop
  validates each library against its row but does not insert library IDs into a
  uniqueness set.
- Category counts remain a distinct matrix contract; project uniqueness does not
  establish the required effect-heavy, contract-heavy, and mixed-strict split.
- Canonical path containment and the exact five-row catalog requirement remain
  necessary; only the second project uniqueness representation is redundant.

### Status (part three hundred thirty)

R819 is `applied`: the canonical path set established during catalog-row
validation now owns project uniqueness, while library and category checks stay
independent.

## Second survey, part three hundred thirty-one: R820 - implied proof-outcome assertions

| ID | Finding | Evidence |
|---|---|---|
| R820 | **Two `ProofKernelTests` assertions restate a type assertion in a weaker form.** `UnsatCreatesAProvenOutcomeWithOnlyRequestedEvidence` first requires `outcome` to be exactly `ProvenOutcome`, and `SatBecomesRefutedOnlyAfterConcreteReplay` first requires exactly `RefutedOutcome`. Each test then asserts `outcome is ProvenOutcome or RefutedOutcome`, which is logically implied by the preceding `Is.TypeOf<T>()` assertion and cannot detect a distinct failure. Removing those two boolean assertions leaves the core-selection, model-replay, and outcome-type checks intact while eliminating test noise that looks like an independent cacheability or outcome-family contract. | `SharpProof.Verify.Test/ProofKernelTests.cs:30-35,49-53` |

### Checked and not proposed (part three hundred thirty-one)

- The exact `Is.TypeOf<ProvenOutcome>()` and `Is.TypeOf<RefutedOutcome>()`
  assertions remain necessary because the later casts and payload assertions
  depend on the concrete result type.
- The other `is ProvenOutcome or RefutedOutcome` assertions in this fixture are
  attached to paths that are expected to return `UnknownOutcome`; those are not
  implied by a preceding exact success-type assertion and remain informative.
- This is a test reduction only; no production outcome behavior is being
  changed.

### Status (part three hundred thirty-one)

R820 is `applied`: the two redundant outcome-family assertions were removed;
the exact concrete outcome and payload assertions remain.

## Second survey, part three hundred thirty-two: R821 - duplicate generated-domain join

| ID | Finding | Evidence |
|---|---|---|
| R821 | **`GeneratedDomainLawAssertions.AssertLatticeAndBottomLaws` computes the same join twice per property iteration.** The local `middle` is assigned `domain.Join(first, second)` and is used to build `upper` and check the generated transitivity premises. A few lines later, `join` is assigned another `domain.Join(first, second)` with the same operands, then used for the join upper-bound and sampled least-upper-bound checks. The two values are the same abstract-domain operation for the same pair, so reusing `middle` for the latter checks removes one domain join for each of 512 iterations without reducing coverage or changing the generated upper-bound construction. | `SharpProof.Dataflow.Test/GeneratedDomainPropertyTests.cs:44-78` |

### Checked and not proposed (part three hundred thirty-two)

- The separate `domain.Join(join, third)` / `domain.Join(middle, third)` result
  remains necessary because the law checks a generated upper bound involving a
  third value.
- The sampled upper-bound loop remains independent: it intentionally tests the
  same first/second pair against unrelated values from the generated corpus.
- This is test-harness computation only; domain join semantics and the sampled
  values remain unchanged.

### Status (part three hundred thirty-two)

R821 is `applied`: the lattice-law checks reuse the first join result for the
remaining upper-bound assertions, preserving all generated-domain laws.

## Second survey, part three hundred thirty-three: R822 - replay fixture resealing

| ID | Finding | Evidence |
|---|---|---|
| R822 | **`CompilerEffectReplayArtifactCodecTests` seals malformed replay fixtures twice.** `RefutedEvidence` seals the newly constructed evidence before returning, and `RefutedEvidence(kind)` seals the base fixture again after applying its kind-specific witness and event fields. Every `AssertRejected` call then mutates that already-sealed fixture and invokes `CompilerEffectClaimArtifactCodec.Seal` a third time before validation; the no-kind overload has the same base-seal-then-reseal pattern. The first seal in each rejection path cannot contribute to the assertion because the mutation invalidates its hashes, so a fixture-construction helper that leaves sealing to the final caller, or an explicit `Seal` option for the accepted-shape test, removes redundant hashing while preserving the required post-mutation seal and the accepted-shape validation. | `SharpProof.Worker.Test/CompilerEffectReplayArtifactCodecTests.cs:226-251,254-318,337-363` |

### Checked and not proposed (part three hundred thirty-three)

- The final seal after each mutation remains required: the rejection assertion
  is intended to validate a structurally complete but tampered artifact rather
  than an artifact with stale hashes.
- The accepted capability/exception shapes still need one seal before their
  direct `Validate` call; this finding does not remove that authentication
  setup.
- The event-kind switch and synchronization-witness setup are semantically
  distinct fixtures; only their unconditional pre-mutation sealing is shared
  work that cannot be observed by the rejection assertion.

### Status (part three hundred thirty-three)

R822 is `applied`: replay fixture constructors now leave evidence unsealed;
accepted and baseline validations seal explicitly, while malformed cases seal
once after mutation.

## Second survey, part three hundred thirty-four: R823 - repeated specification-pack JSON invocation

| ID | Finding | Evidence |
|---|---|---|
| R823 | **`CompilerSpecificationPackProviderTests` repeats the JSON-document/reflection invocation wrapper three times.** `ParseTerm`, `ParseMethod`, and `ParsePack` each parse the input string into a `JsonDocument`, pass its root element to `Invoke`, and rely on the same exception-unwrapping path; only the reflected parser and the term-depth argument differ. A small `ParseJson(MethodInfo, string, params object?[] extra)` helper can own document lifetime and root-element forwarding, leaving the test names as readable semantic entry points while removing the repeated disposal and reflection plumbing. | `SharpProof.Worker.Test/CompilerSpecificationPackProviderTests.cs:374-399` |

### Checked and not proposed (part three hundred thirty-four)

- The three named wrappers still communicate which private production parser a
  test is exercising; the proposed helper would be beneath those names rather
  than replacing the test intent with string-based dispatch.
- `Instantiate` is not folded into the parser helper: it creates a factory,
  provider, and parameter variable and invokes an instance method, so its
  setup has different ownership and lifecycle semantics.
- The shared `Invoke` exception-unwrapping routine remains useful for all
  reflection calls, including the non-JSON type and term instantiation tests.

### Status (part three hundred thirty-four)

R823 is `applied`: the three specification-pack parser tests now share one
JSON-document lifetime and reflection-invocation helper.

## Second survey, part three hundred thirty-five: R824 - duplicated probe JSON array writer

| ID | Finding | Evidence |
|---|---|---|
| R824 | **`ProbeJsonObject` implements the same comma-delimited array loop twice.** `RawArray` appends an opening bracket, tracks the first row, inserts commas between rows, appends each raw row, and closes the bracket; `AppendStringArray` repeats the same state machine and delimiter handling, differing only in whether each element is appended raw or passed through `AppendString`. A small element-writer callback or generic append helper can own the array framing and separator policy while retaining the two public serialization modes. This removes a second copy of the JSON array protocol from the probe asset without introducing a runtime JSON dependency. | `SharpProof.CompilerProbe.TestAsset/ProbeJson.cs:45-92` |

### Checked and not proposed (part three hundred thirty-five)

- `RawArray` must continue to accept already-serialized JSON rows, while
  `StringArray` must continue to quote and escape each string; only framing and
  separator emission are shared.
- The hand-rolled writer remains in place because this analyzer asset avoids a
  `System.Text.Json` dependency and compiler-host assembly-load coupling, as
  recorded in the earlier probe review.
- Object-property comma handling in `PropertyName` is a different nesting
  level and is not folded into the array helper.

### Status (part three hundred thirty-five)

R824 is `applied`: one callback-driven array writer now owns framing and
separator handling while raw and escaped element writers remain distinct.

## Second survey, part three hundred thirty-six: R825 - misleading empty-tree predicate

| ID | Finding | Evidence |
|---|---|---|
| R825 | **`CompilerCaptureAuthority.IsCanonicalEmptyTree` has a name that contradicts its accepted domain.** The predicate returns `true` immediately for any `CompilerSyntaxTreeSnapshot` with `TextLength != 0`; only zero-length trees are required to carry the empty-text hash and a deduplicated effective-symbol set. In `CompilationFingerprint.ValidTree` this is the intended policy - non-empty trees need no empty-tree special case - but the helper name suggests that a non-empty tree should return `false`, forcing readers to reconstruct the disjunctive meaning from the expression. Renaming it to describe the guard (for example, `HasValidEmptyTreeRepresentation`) or inlining the explicit `TextLength != 0 || ...` policy at its sole caller would reduce misleading indirection without changing acceptance behavior. | `SharpProof.CompilerArtifact/CompilerCaptureAuthority.cs:145-162`; `SharpProof.CompilerArtifact/CompilationFingerprint.cs:298-322` |

### Checked and not proposed (part three hundred thirty-six)

- The non-empty-tree fast path is intentional and must remain; this is not a
  proposal to reject ordinary syntax trees or to require the empty hash for
  every capture.
- The empty-text hash and effective-preprocessor-symbol comparison remain
  necessary for zero-length trees, including duplicate raw `#define` symbols.
- This is a naming/shape cleanup at one call site, not a change to the capture
  fingerprint or its fail-closed validation policy.

### Status (part three hundred thirty-six)

R825 is `applied`: the predicate now describes the accepted empty-tree
representation, including the intentional non-empty fast path.

## Second survey, part three hundred thirty-seven: R826 - duplicated scoped-identifier hashing

| ID | Finding | Evidence |
|---|---|---|
| R826 | **`SpecId` and `ScopedIrId<TTag>` duplicate the same scoped integer hash algorithm.** Both types mix the low and high halves of a `long` scope with the `int` value using the same `397` multiplier and XOR sequence, and both expose the same `Scope == 0` default test. `SpecVarId` then repeats the multiplier when extending the `SpecId` hash with its variable ordinal. The identifier types have intentionally different identity and display contracts (`SpecId` renders `specN`, while tagged IR IDs render their tag prefix), so they should not be collapsed into one public type; an internal shared `ScopedIdentifierHashCode(scope, value)` primitive can nevertheless remove the duplicated bit-mixing expression and keep the three hash implementations aligned. | `SharpProof.Specs/SpecIdentifiers.cs:3-39,51-81`; `SharpProof.Ir/ScopedIrId.cs:3-33` |

### Checked and not proposed (part three hundred thirty-seven)

- `SpecId`, `SpecVarId`, and `ScopedIrId<TTag>` remain separate types because
  their construction visibility, equality scope, generic tags, and string
  prefixes are different API contracts.
- `SpecVarId` must continue to incorporate both its owning spec identity and
  its variable value; sharing the primitive does not remove that composition.
- The `IsDefault` properties are semantically local convenience checks; the
  candidate concerns the repeated hash arithmetic rather than forcing a common
  base type onto value types.

### Status (part three hundred thirty-seven)

R826 is `applied`: IR and specification identifiers now share one internal
scoped hash primitive while retaining their distinct value and display types.

## Second survey, part three hundred thirty-eight: R827 - internal replay compatibility shim

| ID | Finding | Evidence |
|---|---|---|
| R827 | **`CallableCounterexampleReplayer` retains an internal overload that only re-filters clauses and forwards.** The compatibility partial method accepts a target, claim ordinal, and model, materializes `target.Clauses.Where(clause => clause.Kind == Ensures).ToArray()`, and immediately calls the full overload. All production callers in `CallableVerifier` and `VerificationCache` already maintain a prepared ensures array and call the five-argument overload directly; the short overload is reached by the replayer unit tests and one worker edge-case test. Because the replayer is internal, removing the shim and updating those test fixtures to pass their prepared clause arrays would eliminate a second entry path and avoid a full clause scan whenever the convenience overload is used. | `SharpProof.Worker/CallableCounterexampleReplayer.Compatibility.cs:3-19`; `SharpProof.Worker/CallableVerifier.cs:261-268`; `SharpProof.Worker/VerificationCache.cs:633-646`; test-only callers in `SharpProof.Worker.Test/CallableCounterexampleReplayerTests.cs` and `SharpProof.Worker.Test/WorkerTcbEdgeCaseTests.cs` |

### Checked and not proposed (part three hundred thirty-eight)

- The full overload must retain its prepared-clause input because production
  verification already performs that projection once and reuses it across
  claims.
- The ensures filter is not removed from the system: callers that construct a
  target from raw clauses still need the same kind/ordinal ordering before
  replay.
- Test readability is the main tradeoff; the short overload is convenient for
  fixtures, but it is not a public compatibility contract and has no distinct
  replay semantics.

### Status (part three hundred thirty-eight)

R827 is `applied`: the internal compatibility overload was removed and tests
  now pass their prepared ensures lists through the production replay entry
  point.

## Second survey, part three hundred thirty-nine: R828 - implied fuzz coverage conjunct

| ID | Finding | Evidence |
|---|---|---|
| R828 | **`FuzzSummary.Passed` checks `CoverageSatisfied` twice through an implication.** The property first requires `CoverageSatisfied == (Cases < FuzzOptions.DefaultCases || FrontendCoverage.HasExpandedCategories)`, then immediately requires `CoverageSatisfied` as a separate conjunct. Equality with the expected expression already forces the property to be `true` in the only accepted cases, so the later conjunct cannot reject an additional state. Removing that conjunct preserves the same predicate while making the pass criteria less repetitive. | `Tools/SharpProof.Fuzz/FuzzRunner.cs:81-113` |

### Checked and not proposed (part three hundred thirty-nine)

- The equality itself remains necessary because it rejects both a missing
  required expanded-coverage result and an unexpected `CoverageSatisfied`
  value for smaller campaigns.
- The other `Passed` conditions independently validate schema, case count,
  parallelism, failure storage, exception counts, abstentions, and all three
  differential-oracle agreement totals.
- This is a fuzz-summary predicate cleanup only; it does not relax coverage
  generation or oracle execution.

### Status (part three hundred thirty-nine)

R828 is `refuted`: when a full-size campaign lacks expanded frontend coverage,
  the equality can be `false == false`; the separate `CoverageSatisfied`
  conjunct intentionally rejects that state.

## Second survey, part three hundred forty: R829 - defensive exception-hierarchy deduplication

| ID | Finding | Evidence |
|---|---|---|
| R829 | **`CompilerExceptionTypeIdentity.EncodeHierarchy` deduplicates a traversal that is already a linear base-type chain.** The method appends one encoded identity for `type`, then repeatedly follows `BaseType` until null; for a valid Roslyn class symbol, that inheritance chain cannot revisit a base node. The final `Distinct(StringComparer.Ordinal)` therefore appears to be defensive normalization rather than a reachable duplicate-removal step, and it allocates another enumerable pass before sorting. If compiler symbols are guaranteed to be acyclic and the encoded identity is injective for the chain, removing `Distinct` would leave the same hierarchy and make the method's linear behavior explicit. | `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs:29-39` |

### Checked and not proposed (part three hundred forty)

- The base-type walk must remain null-safe and must preserve the existing
  ordinal sort and encoded generic-argument assembly identities.
- The deduplication may be an intentional guard for malformed metadata or
  future identity-format collisions; those boundary assumptions need tests
  before changing the output contract used by replay evidence.
- `CompilerExceptionTypeIdentity.Encode` itself is not proposed for removal:
  its documentation-ID preflight and generic-argument assembly suffix carry
  identity semantics that the hierarchy projection consumes.

### Status (part three hundred forty)

R829 is `deferred`: it is a small, potentially redundant defensive pass, but
  the exception hierarchy is serialized evidence and should not lose a
  fail-closed guard without proving the symbol and encoding invariants.

## Second survey, part three hundred forty-one: R830 - dead launcher assumption total

| ID | Finding | Evidence |
|---|---|---|
| R830 | **`ReportAssumptions` computes an unused total.** After the method has already tested `assumptions.User + assumptions.Trusted == 0`, it assigns the same sum to `total`, but no later expression reads that local; the diagnostic receives the original `WorkerAssumptionSummary`, and the return value depends only on the policy. Removing the assignment eliminates dead code without changing the zero-assumption guard, diagnostic text, or exit status. | `SharpProof.Worker.Launcher/Program.cs:497-511` |

### Checked and not proposed (part three hundred forty-one)

- The zero-total test remains necessary because it suppresses diagnostics and
  does not turn an empty assumption summary into an error.
- `LauncherPresentation.AssumptionsDeclaredMessage` remains the owner of the
  displayed total and per-kind counts; the unused local does not duplicate a
  required output value.
- No launcher exit-code or response-validation behavior is part of this
  cleanup.

### Status (part three hundred forty-one)

R830 is `applied`: the unused assumption total was removed while preserving
  the zero-assumption guard, diagnostic, and policy result.

## Second survey, part three hundred forty-two: R831 - bitwise boolean launcher flags

| ID | Finding | Evidence |
|---|---|---|
| R831 | **`ValidateAndReport` uses bitwise boolean operators where logical operators express the policy.** `incompleteError` combines two pure boolean comparisons with `&`, and the final exit expression combines the already-computed `incompleteError` and `assumptionError` with `|`. There is no side effect to preserve and both operands of the final expression have already been evaluated, so `&&`/`||` would produce the same current result while communicating ordinary boolean control flow. The bitwise spelling makes readers wonder whether forced evaluation or flag arithmetic is intentional. | `SharpProof.Worker.Launcher/Program.cs:471-475,495` |

### Checked and not proposed (part three hundred forty-two)

- `ReportAssumptions` must still run before the final return because it emits
  the assumption diagnostic; changing the final operator does not remove
  that call.
- The refuted result must continue to take precedence over incomplete or
  assumption errors through the existing conditional expression.
- This candidate concerns operator clarity only; it does not change exit-code
  values or response validation.

### Status (part three hundred forty-two)

R831 is `applied`: launcher policy predicates now use logical operators while
  preserving the already-evaluated assumption diagnostic and exit-code policy.

## Second survey, part three hundred forty-three: R832 - incomplete response array rematerialization

| ID | Finding | Evidence |
|---|---|---|
| R832 | **`WorkerResultAssembler.CreateIncomplete` materializes result arrays that `Create` copies again.** The failure-path helper converts `manifest.Callables` and `manifest.Claims` to arrays so it can build the assumption lookup and project incomplete results; it then passes those projected arrays to `Create`, whose first two statements call `callableResults.ToArray()` and `claimResults.ToArray()` again. The second materialization is unnecessary for the arrays produced locally and allocates/copies both result sets on every incomplete response. A private array-preserving core or an internal overload can keep `Create`'s general `IEnumerable` boundary while letting `CreateIncomplete` reuse its already-owned arrays. | `SharpProof.Worker.Protocol/WorkerResultAssembler.cs:7-14,61-104` |

### Checked and not proposed (part three hundred forty-three)

- `Create` still needs a materialization boundary for its general enumerable
  callers; only the locally materialized failure path needs an array-preserving
  route.
- The `ToLookup` in `CreateIncomplete` is not part of this finding: it joins
  malformed claims to callable assumptions and has separate failure-path
  semantics.
- Summary accumulation, canonicalization, and cloning of budgets remain
  necessary response-construction behavior.

### Status (part three hundred forty-three)

R832 is `deferred`: the duplicate copies occur only on incomplete/failure
  responses, but the ownership boundary is explicit and can be simplified
  without changing the wire model.

## Second survey, part three hundred forty-four: R833 - duplicate protocol result-ID projection

| ID | Finding | Evidence |
|---|---|---|
| R833 | **`ProtocolJson.ValidateResultSet` projects the same result identities twice.** After `Present` has produced the non-null result array, the helper passes `present.Select(identity)` to `ValidateUniqueIds`, whose first operation materializes that sequence, and then constructs a second `present.Select(identity)` for `ValidateExactIds`. The callable and claim paths use the same helper with different identity/error-code arguments, so the duplicated projection is per result set rather than a semantic distinction. Materializing one identity array and passing it to both validators (or returning the uniqueness snapshot) removes one traversal while preserving separate uniqueness and exact-set diagnostics. | `SharpProof.Worker.Protocol/ProtocolJson.cs:920-939`; callers at `SharpProof.Worker.Protocol/ProtocolJson.cs:584-612` |

### Checked and not proposed (part three hundred forty-four)

- `ValidateUniqueIds` and `ValidateExactIds` should remain separate because
  they report different protocol errors and enforce different invariants.
- `Present` must continue to filter malformed null rows and report the
  collection-level error before either identity check consumes the result.
- Expected IDs remain independently enumerated and ordinal-sorted; only the
  repeated projection of the already-present actual rows is in scope.

### Status (part three hundred forty-four)

R833 is `deferred`: the extra pass is bounded by one response collection, but
  it is deterministic validation work with no policy benefit and has a simple
  identity-array sharing seam.

## Second survey, part three hundred forty-five: R834 - duplicate manifest callable-ID projection

| ID | Finding | Evidence |
|---|---|---|
| R834 | **`ProtocolJson.ValidateManifestCore` projects callable IDs twice before membership checks.** It first passes `callables.Select(value => value.CallableId)` to `ValidateUniqueIds`, which materializes and validates that identity sequence, then immediately walks the same callable rows again with `Where`/`Select` to construct `callableIds` for claim membership. The nonblank/unique identity snapshot can be materialized once and reused to populate the membership set while retaining the distinct `.callable_id` diagnostic and the later `.claim_callable` lookup. | `SharpProof.Worker.Protocol/ProtocolJson.cs:503-522` |

### Checked and not proposed (part three hundred forty-five)

- Claim IDs and claim-to-callable lookup remain separate projections: the
  latter is keyed by callable ID and feeds `ToLookup` for membership checks.
- `ValidateUniqueIds` must continue to report its own code even if the same
  identity snapshot also feeds a set.
- Filtering blank IDs out of the membership set remains necessary because the
  manifest can be malformed and validation must stay fail-closed.

### Status (part three hundred forty-five)

R834 is `deferred`: the duplicate pass is linear and bounded by manifest size,
  but it repeats deterministic identity extraction immediately before using the
  same IDs for membership validation.

## Second survey, part three hundred forty-six: R835 - duplicate performance-response precondition

| ID | Finding | Evidence |
|---|---|---|
| R835 | **`WorkerPerformanceProbe` repeats the same response-validity precondition in its cancellation and timeout predicates.** `IsCompleteCancellation` and `IsCompleteProjectTimeout` each require `response.Errors.Length == 0` and `WorkerProtocolJson.Validate(response).IsValid` before checking their different run status, reason, and result-shape policies. A small `IsValidCleanResponse` helper can own those two shared checks while the cancellation-specific manifest/result assertions and timeout-specific project-timeout witness assertion remain independent. | `SharpProof.Gates/Performance/WorkerPerformanceProbe.cs:120-139,189-199` |

### Checked and not proposed (part three hundred forty-six)

- The predicates must remain separate after the shared precondition: one
  proves typed cooperative cancellation, while the other proves a project
  timeout observed through the launcher.
- The cancellation predicate's failure reason, nonempty manifest, and result
  arrays are not interchangeable with the timeout predicate's witness test.
- Protocol validation remains required; the candidate only centralizes the
  repeated clean-response guard.

### Status (part three hundred forty-six)

R835 is `deferred`: the duplicate validation is small and probe-only, but both
  predicates run on performance-gate responses and can share one explicit
  authority precondition without weakening either measurement.

## Second survey, part three hundred forty-seven: R836 - duplicate manifest materialization during response validation

| ID | Finding | Evidence |
|---|---|---|
| R836 | **`ProtocolJson.ValidateResponse` scans the response manifest twice to obtain the same callable and claim rows.** `ValidateManifestCore` calls `Present` for `manifest.Callables` and `manifest.Claims`, validates those filtered arrays, builds the callable-ID set and claim lookup, and walks both collections. After that returns, `ValidateResponse` constructs `ManifestIdentityIndexes`, which again filters the same two manifest arrays with `OfType` and walks them to build `CallablesById` and `ClaimsById` before validating results. A validation snapshot containing the filtered rows and reusable ordinal indexes can flow from the first pass into result/run/coverage validation; the expected-manifest path can retain its independent validation because it has no response-result joins. | `SharpProof.Worker.Protocol/ProtocolJson.cs:342-357,503-545,991-1005` |

### Checked and not proposed (part three hundred forty-seven)

- The snapshot must retain malformed-row filtering and the existing validation
  errors; sharing arrays must not make an invalid manifest appear valid.
- Expected manifests still require an independent validation because they are
  a separate binding input and are compared only after the actual manifest is
  valid.
- Result, run-state, unknown-coverage, and summary policies remain separate;
  only their common manifest rows/indexes are candidates for reuse.

### Status (part three hundred forty-seven)

R836 is `deferred`: response validation is a high-frequency boundary and the
  duplicate manifest walk is broader than a single identity projection, but a
  snapshot API needs careful error-order and malformed-input compatibility.

## Second survey, part three hundred forty-eight: R837 - duplicate unknown-coverage owner index

| ID | Finding | Evidence |
|---|---|---|
| R837 | **`ProtocolJson.ValidateUnknownCoverage` rebuilds a manifest claim-owner map already available in the response indexes.** The response path has just constructed `ManifestIdentityIndexes`, including an ordinal `ClaimsById` index that retains the first row for duplicate IDs. `ValidateUnknownCoverage` instead filters `manifest.Claims`, groups by claim ID, selects the first callable ID, and builds a new dictionary before checking unknown results; that is another full claim traversal and a second first-row identity policy. Passing the existing index into the helper and resolving each unknown result's claim owner through it can remove the group/dictionary allocation while preserving the current first-match behavior and the separate incomplete-callable set. | `SharpProof.Worker.Protocol/ProtocolJson.cs:349-356,681-700,991-1011` |

### Checked and not proposed (part three hundred forty-eight)

- The incomplete-callable set remains response-specific and must still be
  built from validated callable results, not from manifest metadata.
- Duplicate or null manifest rows still need the same fail-closed treatment;
  the shared index must retain its current first-entry semantics.
- `ValidateRun`'s result-claim lookup is not folded into this candidate: it
  indexes untrusted result rows, whereas R837 concerns manifest claim owners.

### Status (part three hundred forty-eight)

R837 is `deferred`: the extra owner map is linear and bounded, but it repeats
  an identity projection at a response-validation boundary that already owns a
  compatible index.
