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
| R316 | Consolidate friend-assembly declarations into SDK `<InternalsVisibleTo>` items and remove IVT-only `AssemblyInfo.cs` files | `test-changed`: 16 focused suites, ArchitectureTest 389, and 36 package shards passed |
| R320 | Remove the unreferenced `Format-CSharp.ps1` output-only `-Verify` branch while retaining developer formatting | PowerShell parse; `test-changed` formatting/build paths passed |

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
| R213 | Rejected after implementation: the helper adds eight lines and an argument-array allocation on the summary path. |
| R215 | Rejected after implementation and 695 Worker tests: the helper adds three formatted lines. |
| R216 | Rejected after implementation: the all-unknown helper adds ten formatted lines. |
| R057 | Refuted against the current tree: only three tests retain the single-invocation shape; the remaining flow tests select distinct operations or assert graph-specific state. |
| R204 | Rejected by canonical pack validation: removing the project-side `PackageId` changes restore identity, causing the locked Verifier dependency graph to fail with NU1004 before packing. |
| R299 | Refuted against the current tree: the contract now pins the 248-entry mutation catalog, the registration script checks that count before execution, and checksum identity was intentionally removed from the package/inventory pipeline. |
| R270 | Refuted against the current tree: the SPDX/SBOM release-evidence producer and validator, including `Get-SpdxPackageId`, were removed with the package-integrity pipeline; package layout and dependency checks remain separate. |
| R303 | Refuted with R270: the SBOM producer/validator comparison no longer exists after the package-integrity pipeline removal. |

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

R277 through R284 are `pending`. R277, R278, R280, and R283 are mechanical and
low-risk. R279 is the one to weigh first: it is not a line-count reduction at all
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

R285, R286, and R288 are `pending`. Applied R289 replaces the private sequence
helper with the ordinal framework comparer. R287 is not a reduction and should not
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

R314 is `pending` and mechanical, and should be done with R280 as one change since
they are the same defect in three places. R315 is `pending` but is the item in
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

R322 is `pending` and is the most substantive of the three: it duplicates an
identity-validation rule across a producer/validator boundary with the two copies
already diverging in strictness, and the fix requires no new reference or
visibility change. R323 folds into R281 and should be done with it. Applied R324
centralizes the two compile-time target masks without changing attribute metadata.

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
- R340 is now applied: the fuzz project keeps its existing `FuzzOracleStatus`
  source spelling through a compile-wide alias, but the enum type is owned by
  `SharpProof.Testing`.
- R345 is now applied: the two analyzer call sites invoke
  `DefiniteOperationFacts.IsDefinitelyString` directly, removing their private
  single-call wrappers.
- R346 is now applied: the protocol wrapper delegates to the linked canonical
  `SharpProof.Ir.HashEncoding` source, retaining the existing internal method
  used by protocol and worker callers.
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

R341-R345 are `pending`.
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
| R352 | **`TypeName` regex validation is shadowed across four generator scripts due to missing tuple character support.** `GeneratedFileHelpers.ps1:109-117,138-145` defines `Assert-TypeName` and `TypeName` with regex `^[A-Za-z_][A-Za-z0-9_?.<>, \[\]]*$`. Because this pattern rejects tuple type syntax (e.g. `(Outcome, Reason)`), `Generate-ProjectionCatalog.ps1:17-22` shadows `TypeName` with `^[A-Za-z_(][A-Za-z0-9_?.<>, \[\]()]*$`; `Generate-CompilerArtifactModel.ps1:37-43` defines a local `Assert-TypeName`; and `Generate-IrModel.ps1:72-83` defines a local `Assert-TypeName` with `^[A-Za-z_][A-Za-z0-9_?.<>, ]*$`. Updating the central regex in `GeneratedFileHelpers.ps1` to include `(` and `)` eliminates all four shadow functions. | `scripts/GeneratedFileHelpers.ps1:109-117,138-145`; `scripts/Generate-ProjectionCatalog.ps1:17-22`; `scripts/Generate-CompilerArtifactModel.ps1:37-43`; `scripts/Generate-IrModel.ps1:72-83` |
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

### Status (part twenty-eight)

R350-R355 are `pending`. R350, R352, and R355 are clean build/script maintenance
simplifications. R351, R353, and R354 address projection/mapping redundancy,
allocation overhead in canonical hashing, and duplicate repository root resolution.

## Second survey, part twenty-nine: R356-R357

This pass narrowed the remaining native interop and Roslyn project-policy repeats.
The native declarations are exact duplicates, while the project setting is a
smaller role-policy candidate that needs package-layout validation.

| ID | Finding | Evidence |
|---|---|---|
| R356 | **The verifier's nested libc interop surface repeats three exact imports in two build tasks.** `VerifierProcessSupervisor.NativeMethods` and `RunVerifier.NativeMethods` each declare byte-for-byte `Close(int)`, `SystemCall2(nint, int, uint)`, and `SystemCall4(nint, int, int, nint, uint)` methods with the same `LibraryImport("libc", EntryPoint, SetLastError = true)` and `DefaultDllImportSearchPaths(SafeDirectories)` attributes. The containing classes still need different native methods (`prctl`/`waitpid` versus `kill`) and their wrapper-level failure semantics differ, so only these common imports should move to one internal owner. This is separate from R330's repeated syscall constants: changing an imported signature or attribute currently requires two edits as well as the constant edits. | `SharpProof.BuildTasks/VerifierProcessSupervisor.cs:495-524`; `SharpProof.BuildTasks/RunVerifier.cs:1352-1376`; R330 |
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

### Status (part twenty-nine)

R356-R357 are `pending`. R356 is a low-risk interop consolidation if the shared
owner remains internal to `SharpProof.BuildTasks`; R357 needs package-output
tests because its three consumers do not have identical analyzer markers.

## Second survey, part thirty: R358-R367

This pass surveyed protocol serialization, string/surrogate validation, metadata
naming conventions, polyfill distributions, resource limit defaults, and SMT/dataflow
domain bounds across `SharpProof.Worker.Protocol`, `SharpProof.Ir`, `SharpProof.Specs`,
`SharpProof.Effects`, `SharpProof.Smt`, `SharpProof.ArchitectureTest`, and `SharpProof.Worker`.

| ID | Finding | Evidence |
|---|---|---|
| R358 | **`ProtocolJsonSupport.EnsureNoLoneSurrogates` is a character-for-character duplicate of `Utf16WellFormedness.IsWellFormed`.** `SharpProof.Ir/Utf16WellFormedness.cs:5-29` defines internal static `bool IsWellFormed(string value)` scanning for lone high/low UTF-16 surrogates. `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs:202-223` defines private static `void EnsureNoLoneSurrogates(string? value)` executing the exact same surrogate loop to throw `JsonException("JSON strings must not contain lone UTF-16 surrogates.")`. Reusing `Utf16WellFormedness.IsWellFormed` in `ProtocolJsonSupport` eliminates 22 lines of duplicated surrogate-scanning loop logic. | `SharpProof.Ir/Utf16WellFormedness.cs:5-29`; `SharpProof.Worker.Protocol/ProtocolJsonSupport.cs:202-223` |
| R359 | **`IrSummaryProvenance.IsSha256` is an exact duplicate of `WorkerProtocolJson.IsSha256`.** `SharpProof.Summaries/IrRelationalSummary.cs:73-78` defines private static `bool IsSha256(string? value) => value != null && value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');`. `SharpProof.Worker.Protocol/ProtocolJson.cs:1056-1060` declares the identical predicate as `internal static bool IsSha256(string? value)`. Consolidating SHA-256 validation in a shared helper removes duplicate hex-validation routines across semantic summaries and protocol layers. | `SharpProof.Summaries/IrRelationalSummary.cs:73-78`; `SharpProof.Worker.Protocol/ProtocolJson.cs:1056-1060` |
| R360 | **`FrameworkTypeMetadataNames.Monitor` is declared as `public static readonly string` while 23 sibling type identities are `public const string`.** `SharpProof.Specs/FrameworkTypeMetadataNames.cs:39` defines `public static readonly string Monitor = "System.Threading.Monitor";`, whereas lines 9-47 declare 23 other framework types (`ArgumentException`, `Exception`, `NullReferenceException`, `ConditionalAttribute`, etc.) as `public const string`. Declaring `Monitor` as `readonly` instead of `const` prevents its use in compile-time constant expressions, switch cases, and Roslyn pattern matches, forcing callers in `CompilerEffectReplayLowerer.cs:220,327` and `OperationEffectScanner.cs:69,1314` to treat it as a runtime field rather than an inline constant. | `SharpProof.Specs/FrameworkTypeMetadataNames.cs:9-47`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs:220,327`; `SharpProof.Effects/OperationEffectScanner.cs:69,1314` |
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

R358-R367 are `pending`. R358, R359, R360, and R364 are direct, safe code reductions.
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
| R371 | **`Add-Type -AssemblyName System.IO.Compression.FileSystem` is redundant in PowerShell 7+ scripts.** `scripts/Get-SharpProofPilotPackageAuthority.ps1:20-21` and `scripts/Test-SharpProofPilotAuthorityFixtures.ps1:40` call `Add-Type -AssemblyName System.IO.Compression` and `System.IO.Compression.FileSystem`. In PowerShell Core (PowerShell 7+ on Linux/Windows), `System.IO.Compression` types like `[System.IO.Compression.ZipFile]` are built into the runtime and available without `Add-Type`. | `scripts/Get-SharpProofPilotPackageAuthority.ps1:20-21`; `scripts/Test-SharpProofPilotAuthorityFixtures.ps1:40` |
| R372 | **`IrSummarySignature` repeats canonical member and variable mapping in `SharpProof.Summaries`.** `SharpProof.Summaries/IrRelationalSummary.cs:81-100` defines `IrSummarySignature` holding `Member`, `Receiver`, `Parameters`, `Result`, and `Provenance`. `SharpProof.CompilerArtifact/CompilerCallablePreparation` and `CompilerManifestArtifact` mirror these identical member signature properties. Consolidating the callable signature abstraction reduces field-by-field conversion boilerplate between compiler artifacts and summary models. | `SharpProof.Summaries/IrRelationalSummary.cs:81-100`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs:30-45` |

### Checked and not proposed (part thirty-one)

- `CSharpSourceMetricsEngine.Measure` in `scripts/CSharpSourceMetrics.ps1` compiles an in-memory
  Roslyn syntax walker via C# snippet. This avoids slow PowerShell AST walking over large codebases
  and is an intentional performance optimization. Retained as-is.
- `SpecResultDomainProjection.cs` in `SharpProof.Worker` projects relational summary results into
  the verifier domain. The logic is verifier-specific and properly decoupled from compiler lowering.

### Status (part thirty-one)

R368-R372 are `pending`. R368, R369, and R371 are simple code cleanups. R370 and R372
streamline script language parsing and relational summary signature models.

## Second survey, part thirty-two: R373-R374

This pass checked the compiler-probe asset's local utility surface and the
contract-runtime preprocessor symbol across source, generated, and MSBuild
boundaries.

| ID | Finding | Evidence |
|---|---|---|
| R373 | **The compiler-probe asset repeats two exact utility methods in its generator and snapshot implementations.** `CompilerProbeGenerator` and `CompilerProbeSnapshot` each define `GetOption(AnalyzerConfigOptions, string)` with the same `TryGetValue`-or-empty behavior and `NormalizePath(string)` with the same backslash-to-slash replacement. Both classes live in `SharpProof.CompilerProbe.TestAsset`, so a small internal helper or linked source file can remove the four duplicate method bodies without changing the generator/snapshot algorithms that consume them. | `SharpProof.CompilerProbe.TestAsset/CompilerProbeGenerator.cs:114-124`; `SharpProof.CompilerProbe.TestAsset/CompilerProbeSnapshot.cs:583-593` |
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

### Status (part thirty-two)

R373-R374 are `pending`. R373 is a low-risk same-assembly helper extraction.
R374 needs a source-of-truth decision across C#, generated metadata, compiler
fingerprinting, and MSBuild before any literal is removed.

## Second survey, part thirty-two (continued): R461-R462, R375-R379

This pass surveyed abstract domains, transfer functions, modular arithmetic bounds,
lattice joins, graph representations, and fixpoint solvers in `SharpProof.Dataflow`.

| ID | Finding | Evidence |
|---|---|---|
| R461 | **`IntervalDomain.TryCongruentBoundary` re-implements modular difference normalization with nested conditionals.** `SharpProof.Dataflow/IntervalDomain.cs:254-260` computes circular modular distance using a 7-line 4-way nested ternary (`atOrAbove ? remainder >= boundaryRemainder ? ... : ... : ...`). `IntervalDomain.Normalize(BigInteger value, BigInteger modulus)` at `IntervalDomain.cs:210-214` already normalizes signed differences into $[0, \text{modulus}-1]$. Replacing the nested ternary with `Normalize(atOrAbove ? remainder - boundaryRemainder : boundaryRemainder - remainder, modulus)` eliminates 7 lines of nested branches. | `SharpProof.Dataflow/IntervalDomain.cs:210-214, 254-260` |
| R462 | **`IntervalDomain.Create` contains redundant extreme boundary normalization branches.** `SharpProof.Dataflow/IntervalDomain.cs:35-45` conditionally converts `lowerBound == long.MinValue` and `upperBound == long.MaxValue` to `null` when `modulus.IsOne`. Subsequently, lines 67-68 unconditionally convert `long.MinValue` and `long.MaxValue` to `null` for all moduli (`adjustedLower = lowerBound == long.MinValue ? null : lowerBound`). Lines 35-45 are completely shadowed by lines 67-68. | `SharpProof.Dataflow/IntervalDomain.cs:35-45, 67-68` |
| R375 | **`ClosedAbstractDomain<T>` forces duplicate `Havoc` and `Widen` implementations across all derived domains.** `SharpProof.Dataflow/ClosedAbstractDomain.cs:18-19` defines `Havoc` and `Widen` as abstract methods. Every derived domain repeats identical logic: `NullnessDomain.cs:49-53`, `SequenceCardinalityDomain.cs:136-140`, and `IntervalDomain.cs:178-181` each implement `Havoc` as `value.IsBottom ? Bottom : Top`, and finite lattices implement `Widen` as `Join(previous, candidate)`. Providing virtual default implementations in `ClosedAbstractDomain<T>` eliminates boilerplate overrides in all subclasses. | `SharpProof.Dataflow/ClosedAbstractDomain.cs:18-19`; `SharpProof.Dataflow/NullnessDomain.cs:44-53`; `SharpProof.Dataflow/SequenceCardinalityDomain.cs:136-140`; `SharpProof.Dataflow/IntervalDomain.cs:178-181` |
| R376 | **`DataflowGraph<T>` performs redundant multi-pass sorting on already-sorted adjacency lists.** `SharpProof.Dataflow/DataflowGraph.cs:77-88, 155-164` sorts `Edges` primarily by `SourceId` and secondarily by `TargetId` on line 77. Iterating `Edges` populates `successors[edge.SourceId]` in strictly sorted order. Calling `Freeze(successors)` on line 88 then re-sorts every list with `neighbors.Sort()`. Avoiding the second sort pass on pre-sorted successor lists simplifies graph construction. | `SharpProof.Dataflow/DataflowGraph.cs:77-88, 155-164` |
| R377 | **4-point flat diamond lattice join and partial order logic is duplicated across enum domains.** `SharpProof.Dataflow/NullnessDomain.cs:17-42` and `SharpProof.Dataflow/SequenceCardinalityDomain.cs:142-169` implement identical 4-element flat diamond lattices ($\bot < \{A, B\} < \top$). Both duplicate identical branch cascades for identity, bottom absorption, and top collapse. Unifying diamond lattice operations reduces duplicated lattice decision trees across abstract domains. | `SharpProof.Dataflow/NullnessDomain.cs:17-42`; `SharpProof.Dataflow/SequenceCardinalityDomain.cs:142-169` |
| R378 | **`DataflowEdge` contains manual property backing and constructor boilerplate on a `readonly record struct`.** `SharpProof.Dataflow/DataflowGraph.cs:10-27` spans 18 lines manually declaring constructor parameter guards and property assignments for a 2-field record struct. Converting to primary constructor property initializers (`public readonly record struct DataflowEdge(int SourceId, int TargetId) { public int SourceId { get; } = ArgumentNullGuard.RequireNonnegative(SourceId, nameof(SourceId)); ... }`) reduces 18 lines to 5 lines while preserving all validation, deconstructors, and value equality. | `SharpProof.Dataflow/DataflowGraph.cs:10-27` |
| R379 | **`ForwardDataflowAnalysis` allocates an intermediate dictionary and performs two redundant collection passes per solver round.** `SharpProof.Dataflow/ForwardDataflowAnalysis.cs:138-171` allocates `var changedOutputs = new Dictionary<int, T>()` each round, collects changed block states, loops to write them into `outputs`, and loops a third time to gather successors into `affected`. Because block transfers within a round read invariant `inputs`, `outputs[blockId]` can be updated directly and successors enqueued immediately, eliminating intermediate dictionary allocations and two iteration loops per fixpoint round. | `SharpProof.Dataflow/ForwardDataflowAnalysis.cs:138-171` |

### Checked and not proposed (part thirty-two continued)

- `IntervalValue.SingletonValue` throwing on non-singletons is a fail-closed guard preventing
  symbolic interval evaluation on indeterminate bounds. Retained as-is.
- Explicit non-negative interval constraints on sequence cardinality length domain prevent
  negative length inferences. Retained as-is.

### Status (part thirty-two continued)

R461, R462, and R375-R379 are `pending`. R461, R462, R376, and R378 are direct, low-risk local simplifications.
R375 and R377 generalize abstract domain hierarchy contracts. R379 optimizes fixpoint solver throughput.

## Second survey, part thirty-three: R380-R385

This pass surveyed compiler artifact models, code generation scripts, diagnostic ordering,
stream readers, and location authority helpers across `SharpProof.CompilerArtifact`,
`SharpProof.CompilerCollector`, and `scripts/Generate-CompilerArtifactModel.ps1`.

| ID | Finding | Evidence |
|---|---|---|
| R380 | **`CompilerDiagnosticArtifactOrdering` duplicates an 11-stage comparison ladder between LINQ `Canonicalize` and imperative `Compare`.** `SharpProof.CompilerArtifact/CompilationFingerprint.cs:438-453` applies an 11-level chained LINQ sort (`OrderBy(Code).ThenBy(Message)...ThenBy(SourceLineMapSha256)`). Lines 463-521 re-implement the identical 11-stage comparison ladder across 58 lines of manual `StringComparer.Ordinal.Compare` and field-by-field branching in `Compare`. Unifying both paths on a single `IComparer<CompilerDiagnosticArtifact>` eliminates 58 lines of redundant ladder code and prevents ordering divergence. | `SharpProof.CompilerArtifact/CompilationFingerprint.cs:438-453, 463-521` |
| R381 | **`Generate-CompilerArtifactModel.ps1` emits duplicate reason catalog classes into two generated files.** `scripts/Generate-CompilerArtifactModel.ps1:1488-1515` generates verbatim duplicate classes: `CompilerCallableArtifactReasonCatalog` in `CompilerArtifactModel.generated.cs:562-574` and `CompilerCallableProducerReasonCatalog` in `CompilerWireMappings.generated.cs:311-323`. Both define identical constants, failure reason arrays, and lookup methods in the same namespace `SharpProof.CompilerArtifact`. Because `SharpProof.CompilerCollector` already accesses internal types in `SharpProof.CompilerArtifact` via `InternalsVisibleTo`, `CompilerCallableProducerReasonCatalog` is completely redundant. | `scripts/Generate-CompilerArtifactModel.ps1:1488-1515`; `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:562-574`; `SharpProof.CompilerCollector/CompilerArtifact/CompilerWireMappings.generated.cs:311-323` |
| R382 | **`CompilerEffectReplayLowerer.TryResolveSource` duplicates the syntax tree loop from `CompilerSourceLocationAuthority.FindUniqueTree`.** `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs:426-455` manually loops over `capturedTrees`, checks `CompilerSourceLocationAuthority.HasValidLocationGeometry`, and verifies single-match uniqueness across 30 lines. `CompilerSourceLocationAuthority.FindUniqueTree` (`CompilerSourceLocationAuthority.cs:115-150`) already implements this exact tree-resolution and ambiguity-checking loop. Delegating `TryResolveSource` to `FindUniqueTree` removes 30 lines of duplicate loop logic. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerEffectReplayLowerer.cs:426-455`; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:115-150` |
| R383 | **`CompilerManifestArtifact.cs` implements duplicate chunked stream-to-byte-array readers.** `WorkerBinaryIdentity.ReadSnapshotBytes` (`SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:205-231`) and `CompilerManifestArtifactFile.ReadAllBytes` (`SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:980-1009`) implement near-identical bounded buffer-filling loops with EOF checks (`throw new InvalidDataException("... changed while it was read.")`) and trailing-byte verification. Extracting a single bounded stream reader helper eliminates 25+ lines of duplicate buffer-reading boilerplate. | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:205-231, 980-1009` |
| R384 | **`CompilerCompilationCapture.LowerHex` re-implements lowercase hex byte formatting.** `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:230-241` defines a private `LowerHex` method performing manual character array allocation and bit-shift arithmetic to format Roslyn checksums. `SharpProof.Ir/HashEncoding.cs:33-46` already provides `HashEncoding.ToLowerHex(ReadOnlySpan<byte>)`, which `CompilerCompilationCapture.cs` already references elsewhere. Replacing `LowerHex` with `HashEncoding.ToLowerHex` eliminates 12 lines of manual nibble arithmetic. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs:213, 230-241`; `SharpProof.Ir/HashEncoding.cs:33-46` |
| R385 | **`ReplayEventComparer` manually inlines location hashing and comparison instead of reusing `CompilerSourceLocationAuthority`.** `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:365-371` manually hashes all five fields of `WorkerSourceLocation` (`Path`, `Start`, `Length`, `Line`, `Column`) with bespoke null checks instead of using `CompilerSourceLocationAuthority.GetLocationHashCode` (`CompilerSourceLocationAuthority.cs:228-241`). Reusing the authority keeps location equality and hash distribution centralized. | `SharpProof.CompilerArtifact/CompilerEffectAuthority.cs:365-371`; `SharpProof.CompilerArtifact/CompilerSourceLocationAuthority.cs:228-241` |

### Checked and not proposed (part thirty-three)

- `CompilationFingerprint.ComputeSha256` ensures deterministic invariant hashing across platform
  line endings and culture settings; canonical serialization order is required for verifiable builds.
- `CompilerModelValues.cs` contains low-level value packers for compiler wire formats. These are
  isolated for fast serialization and decoupled from Roslyn symbols.

### Status (part thirty-three)

R380-R385 are `pending`. R381, R382, and R384 are immediate code and script generator cleanups.
R380, R383, and R385 unify comparison, streaming I/O, and location hashing authorities.

## Second survey, part thirty-four: R386-R392

This pass surveyed the effect system, exception reachability, using disposal unwinding,
ownership classification, call graph ordering, and summary operations in `SharpProof.Effects`.

| ID | Finding | Evidence |
|---|---|---|
| R386 | **`ExceptionHandlerReachability` duplicates the using-disposal unwinding loop and completion predicates from `UsingDisposalEffectResolver`.** `UsingDisposalEffectResolver.ResolveResources` (`SharpProof.Effects/UsingDisposalEffectResolver.cs:141-198`) and `ExceptionHandlerReachability.GetUsingDisposalExceptions` (`SharpProof.Effects/ExceptionHandlerReachability.cs:2085-2141`) implement the identical 55-line algorithm for tracking acquired `IVariableDeclarationGroupOperation` declarators, checking `allInitializersComplete`, reversing acquired items, and unwinding disposals. Furthermore, `ExceptionHandlerReachability.cs:2379-2438` duplicates four completion/unwinding predicates (`CanDisposalsCompleteNormally`, `CanDisposalCompleteNormally`, `CanDisposalUnwind`, `IsDefinitelyNullResource`) nearly verbatim from `UsingDisposalEffectResolver.cs:200-254`. Hoisting the shared unwinding loop and predicates into `UsingDisposalGraph` eliminates ~90 lines of duplicate disposal simulation code. | `SharpProof.Effects/UsingDisposalEffectResolver.cs:141-254`; `SharpProof.Effects/ExceptionHandlerReachability.cs:2085-2141, 2379-2438` |
| R387 | **Three separate routines duplicate member initializer syntax-to-operation extraction loops.** `EffectMethodNodeBuilder.EnsureBeforeFieldInitNode` (`SharpProof.Effects/EffectMethodNodeBuilder.cs:267-285`), `EffectMethodNodeBuilder.ScanMemberInitializers` (`lines 326-343`), and `ManagedAbstractFlow.ConstructorMayCompleteNormally` (`SharpProof.Effects/ManagedAbstractFlow.cs:2245-2266`) each iterate `GetMemberInitializerReferences`, fetch syntax via `reference.GetSyntax()`, extract initializer expressions with `EffectProjections.GetInitializerExpression`, and query the `SemanticModel` for operations. Providing a centralized `GetMemberInitializerOperations` helper removes ~40 lines of repetitive Roslyn resolution. | `SharpProof.Effects/EffectMethodNodeBuilder.cs:267-285, 326-343`; `SharpProof.Effects/ManagedAbstractFlow.cs:2245-2266` |
| R388 | **`ConversionOwnershipClassifier` duplicates local vs captured symbol region classification across three methods.** `ClassifyLocalStorage` (`SharpProof.Effects/ConversionOwnershipClassifier.cs:542-553`), `ClassifyRefLocalStorage` (`lines 148-161`), and `ClassifyLocal` (`lines 842-855`) each perform identical checks for `SymbolEqualityComparer.Default.Equals(local.ContainingSymbol?.OriginalDefinition, _method.OriginalDefinition)` and fall back to the exact same captured ordinal extraction (`local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0`). Extracting a shared `ClassifyCapturedLocal` helper eliminates ~20 lines of duplicate symbol span inspection. | `SharpProof.Effects/ConversionOwnershipClassifier.cs:148-161, 542-553, 842-855` |
| R389 | **`DefiniteOperationFacts` duplicates harmless conversion unwrapping loops.** `DefiniteOperationFacts.IsDefinitelyNonNull` (`SharpProof.Effects/ManagedAbstractFlow.cs:2857-2877`) and `DefiniteOperationFacts.IsDefinitelyNull` (`lines 2879-2901`) contain duplicate 15-line `while (operation is IParenthesizedOperation or IConversionOperation)` loops unwrapping harmless parenthesized and implicit conversion operations. In addition, `UsingDisposalEffectResolver.cs:275-280` inlines the nullness check body instead of calling its existing helper `IsDefinitelyNull` (`lines 255-260`). Consolidating unwrapping into `UnwrapHarmlessConversions` eliminates redundant loop boilerplate. | `SharpProof.Effects/ManagedAbstractFlow.cs:2857-2901`; `SharpProof.Effects/UsingDisposalEffectResolver.cs:255-260, 275-280` |
| R390 | **`EffectCallGraph.OrderMethods` contains redundant while-enumerator cancellation checks and comparer overhead.** `SharpProof.Effects/EffectCallGraph.cs:95-115` writes a manual `while (true)` enumerator loop checking `cancellationToken.ThrowIfCancellationRequested()` four times per iteration, while `CancellationAwareMethodComparer` (`lines 121-133`) checks cancellation twice for every pairwise comparison during sorting. Standardizing on `foreach` and sort boundary cancellation simplifies ~25 lines of manual enumerator plumbing. | `SharpProof.Effects/EffectCallGraph.cs:95-115, 121-133` |
| R391 | **`EffectSummaryOperations.Join` causes high-frequency param-array allocations across over 45 AST scanning call sites.** `SharpProof.Effects/EffectSummaryOperations.cs:7-20` defines only `Join(params EffectSummary[] summaries)`, routing all calls through array allocation. Over 45 call sites in `OperationEffectScanner.cs`, `OperationEffectScanner.Assignments.cs`, and `OperationEffectScanner.Expressions.cs` pass 2 or 3 arguments, allocating an array per AST node visit. Providing non-allocating 2- and 3-argument overloads or standardizing on `EffectSummaryDomain.Instance.Join(a, b)` eliminates heap churn during AST scanning. | `SharpProof.Effects/EffectSummaryOperations.cs:7-20`; `SharpProof.Effects/OperationEffectScanner.cs:187, 337, 466, 504, 511, 519, 522, 589, 830, 836, 917`; `SharpProof.Effects/OperationEffectScanner.Expressions.cs:187, 212, 437, 453, 474, 502, 541, 555, 595, 606, 613, 630, 743, 758, 777, 826, 860, 864` |
| R392 | **`OperationCompletionEvaluator` instantiates two identical fields of `DefiniteOperationFacts`.** `SharpProof.Effects/OperationCompletionEvaluator.cs:12-16, 31-36, 1203-1210` declares both `_completionFacts` and `_staticInitializationFacts` with identical constructor arguments (`session.Compilation`, `cancellationToken`). `DefiniteOperationFacts` only holds compilation and cancellation state; consolidating both onto `_completionFacts` eliminates redundant fields and constructor allocations. | `SharpProof.Effects/OperationCompletionEvaluator.cs:12-16, 31-36, 1203-1210` |

### Checked and not proposed (part thirty-four)

- `PropertyDispatchFacts.cs` evaluates auto-property and accessor dispatch paths with strict
  Roslyn syntax guards. Retained as-is for contract verification safety.
- `StringConcatenationEffectResolver` implements multi-part string interpolation formatting
  semantics. Retained as-is to preserve exact BCL format string exception behaviors.

### Status (part thirty-four)

R386-R392 are `pending`. R387, R388, R389, R390, and R392 are clean local refactorings.
R386 eliminates major algorithmic duplication between exception reachability and using disposal.
R391 removes AST-scanning allocation churn.

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

R400-R406 are `pending`. R400, R403, R405, and R406 are direct, safe refactoring simplifications.
R401, R402, and R404 streamline contract resolution flow and IR validation.

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

R407-R412 are `pending`. R407 and R411 are trivial launcher cleanups. R408 and R410 unify
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

R413-R419 are `pending`. R413, R414, R418, and R419 are clean code and AST simplifications.
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
so no dead item evaluation remains. R421-R426 are `pending` and streamline build
props, test links, and package scripts.

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

R427-R432 are `pending`. R427, R428, and R429 eliminate massive test boilerplate and subprocess duplication.
R430, R431, and R432 clean up gate hosts, fixture paths, and oracle diagnostics.

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

R433-R439 are `pending`. R433 deletes dead inventory methods. R434, R435, R436, and R438 are direct
code and AST unwrapping cleanups. R437 and R439 optimize validator loops and CFG traversal.

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

R440-R445 are `pending`. R441, R442, and R445 are direct I/O, dead check, and XML writer simplifications.
R440, R443, and R444 optimize AST traversals, sequence point aggregation, and git diff process spawning.

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

R446-R450 are `pending`. R447-R450 are local helper extractions; R446 is a cross-language default-authority reduction.

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

R454-R456 are `pending`. They are review-only reduction candidates; no implementation or build files were edited.

## Second survey, part forty-six: R457-R460

This pass inspected analyzer control-attribute validation and the small dataflow domains.

| ID | Finding | Evidence |
|---|---|---|
| R457 | **`SharpProofControlAttributePolicy.ValidateDeclaredScope` walks the same symbol attributes twice.** It first calls `ValidateScope`, whose loop enumerates `symbol.GetAttributes()` to classify and validate `[SharpProofSuppress]`/`[SharpProofTrusted]`, then immediately calls `symbol.GetAttributes()` again to find rejected control attributes. Cache the immutable attribute snapshot and pass it into the validation routine, or combine the operations with an explicit diagnostic-order policy; this removes repeated Roslyn attribute retrieval without conflating the two diagnostic categories. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:19-45,147-172` |
| R458 | **Control-attribute invalid-reason reporting repeats the same diagnostic assembly in two paths.** `ValidateNestedCallableDeclaration` extracts a constant argument and then checks for a non-empty reason, marks the attribute, substitutes `<empty>`, selects `[SharpProofSuppress]` or `[SharpProofTrusted]`, and creates `InvalidContractArgumentDiagnostics`; `ReportInvalidReason` performs the same mark/empty-label/attribute-name/reason sequence for symbol-level attributes. A shared reporting helper taking the already-extracted reason and location can retain the different syntax/metadata extraction while centralizing the diagnostic contract. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:93-115,174-193` |
| R459 | **`SharpProofControlAttributePolicy` duplicates the suppress/trusted tri-state decision in two overloads.** The `AttributeData` overload and the `INamedTypeSymbol` overload both return `true` for Suppress, `false` for Trusted, and `null` otherwise; only the equality adapter differs (`ContractSelectionInventory.Is` versus direct symbol comparison). A single helper over an attribute-type identity (with the same original-definition normalization) could own the tri-state decision and leave the two adapters thin. | `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:196-220`; `SharpProof.Contracts/ContractSelectionInventory.cs:229-237` |
| R460 | **`IntervalValue.ToString` has two switch arms with identical output.** The `Modulus.IsZero` and `Modulus.IsOne` arms each return the same invariant-culture `[$lower, $upper]` representation; only the fallback includes congruence details. Combining the guards (`IsZero || IsOne`) removes a redundant branch while preserving the canonical text for both unconstrained and singleton congruence forms. | `SharpProof.Dataflow/IntervalValue.cs:144-149` |

### Status (part forty-six)

R457-R460 are `pending`. They are review-only reduction candidates; no implementation or build files were edited.

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

R464-R467 are `pending`. They are review-only reduction candidates; no implementation or build files were edited.


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

R468 is `pending` and mechanical. R469 is `pending` and is primarily a naming fix,
with a small shared-value component that belongs with R291. R470 is `pending` and
is a determinism-of-diagnostics question rather than a reduction - it removes no
lines and may add a few, so it should be judged on whether deterministic error
reporting is wanted, not on size.

## Second survey, part fifty-one: R471-R475

This pass inspected effect-domain defaults, capability encoding, trust scopes, analysis result construction, and compiler-call preparation.

| ID | Finding | Evidence |
|---|---|---|
| R471 | **`EffectSummaryDomain` reimplements defaults already provided by `ClosedAbstractDomain<T>`.** It implements `IAbstractDomain<EffectSummary>` directly and repeats both `AreEquivalent` as two order checks and `Widen` as `Join`; `SharpProof.Dataflow.ClosedAbstractDomain<T>` already supplies those exact implementations. Deriving this domain from the shared base and retaining only its effect-specific `LessThanOrEqual`, `Join`, and `Havoc` logic removes duplicate lattice plumbing, subject to preserving the public type and null-guard behavior. | `SharpProof.Effects/EffectSummary.cs:150-192,223-233`; `SharpProof.Dataflow/ClosedAbstractDomain.cs:6-24` |
| R472 | **`EffectCapabilitySet.IsUnknown` repeats the unknown-bit value numerically.** The constructor and validation logic already derive the unknown marker from `EffectCapabilityKind.Unknown & ~EffectCapabilityKind.AllKnown`, but the property tests `(EffectCapabilityKind)(1 << 13)` directly. Reading the marker from the enum/catalog expression once removes a hidden second authority and keeps the predicate correct if the capability layout changes. | `SharpProof.Effects/EffectValues.cs:5-27`; `SharpProof.Effects/EffectContractValues.cs:15-18`; `SharpProof.Effects/EffectContractMappings.catalog.json:10-27` |
| R473 | **`TrustedBoundaryPolicy.EnumerateScopes` duplicates `SharpProofControlAttributePolicy.EnumerateScopes` across assembly boundaries.** Both iterators yield the method, its associated property, every containing type, and its containing assembly in the same order. A neutral shared contract-scope enumerator in a lower dependency layer could serve both policies; the trust predicate and rejected-attribute handling should remain separate. | `SharpProof.Effects/TrustedBoundaryPolicy.cs:50-69`; `SharpProof.Analyzer.Core/SharpProofControlAttributePolicy.cs:119-136` |
| R474 | **`EffectAnalysisSession.Analyze` and `AnalyzeAll` duplicate effect-result assembly.** Both compute `EffectModuleInitialization.SummarizeBeforeEntry`, combine it with the method summary through `EffectStep`, and expose direct witnesses only when initialization cannot prevent body entry; the single-method path does this at lines 113-129 and the all-method path repeats it inside the projection loop at lines 141-157. A private result factory taking the method, summary, initialization, and optional node witnesses can preserve the locking and lookup differences while centralizing the result semantics. | `SharpProof.Effects/EffectAnalysisSession.cs:104-129,132-157` |
| R475 | **`CompilerCallableLowerer.TryPrepareSpecCall` and `TryPrepareSummaryCall` repeat the same direct-call admission guards.** Each clears its out result, requires an IR target, requires `RoslynProgramLowerer.IsDirectInvocation`, and rejects any ref/in parameter before entering its spec- or summary-specific lookup. A shared `TryGetAdmissibleByValueCall` precheck can remove the repeated guards while keeping the two distinct resolution and preparation paths. | `SharpProof.CompilerCollector/CompilerArtifact/CompilerCallableLowerer.cs:273-319` |

### Status (part fifty-one)

R471-R475 are `pending`. They are review-only reduction candidates; no implementation or build files were edited.


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

R477-R478 are `pending` review-only candidates. No implementation or build files
were edited.


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

R480 should leave this ledger and become a fix, like R299 before it. It is also
the reason to run one more targeted pass: for each applied removal of a named
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

R482 is `pending` and splits naturally: the three `Analyzer.Core` sites are a
direct fix requiring no new reference, and the `IDisposable` one is a strict
duplication of an existing constant. The five `Meta.Analyzers` sites inherit
R306's open question about that assembly's deliberate isolation and should be
decided with it, not separately. The `Monitor` declaration is a one-word change.

## Second survey, part fifty-seven: R483-R485 - publication-path identity plumbing

| ID | Finding | Evidence |
|---|---|---|
| R483 | **`LinuxPathIdentity.BindPublicationSet` ensures each pending metadata directory twice.** The first loop calls `EnsurePublicationMetadataDirectory(markerPath)` for every canonical path before checking whether a marker exists. Every path without an existing marker is then placed in `pending`, and the next loop calls the same helper again for each pending marker path. The second pass has no intervening state change that requires a new directory validation; removing it, or moving the first call into the pending branch, preserves the ownership checks while eliminating duplicate filesystem inspection and setup. | `SharpProof.Host/LinuxPathIdentity.cs:527-554` |
| R484 | **`LinuxPathIdentity` duplicates its canonical path-within-directory predicate.** Public `IsSameOrDescendant` canonicalizes both arguments and then checks equality or a directory-separator-aware prefix; private `IsPathWithin` performs the same equality/prefix test for already-canonical mount paths. A shared canonical-string helper can keep the public validation boundary and the mount-info parsing separate while removing the repeated comparison logic. | `SharpProof.Host/LinuxPathIdentity.cs:315-330,799-846` |
| R485 | **`ResetPublicationSet` and `AcquirePublicationSet` repeat publication-path preparation.** Both filter blank paths, materialize an array, canonicalize through `RequireLocalPath`, and run `ValidatePublicationTopology` plus `ValidatePublicationMetadataAliases` before their operation-specific work. A private preparation helper returning the canonical path array can preserve the reset-specific empty-set no-op and acquire-specific nonempty-set error while centralizing the shared validation sequence. | `SharpProof.Host/LinuxPathIdentity.cs:176-187,232-256` |

### Status (part fifty-seven)

R483-R485 are `pending` review-only candidates. No implementation or build files
were edited.

## Second survey, part fifty-eight: R486-R487 - verifier wait-loop constants

| ID | Finding | Evidence |
|---|---|---|
| R486 | **`RunVerifier` duplicates the wait-loop timing scaffold in two helpers.** `WaitForOutputCompletion` and `WaitForSupervisorReadiness` each start a stopwatch, call `RemainingMilliseconds`, cap a wait slice at `OutputDrainPollingMilliseconds`, invoke an optional test wait delegate, and loop until a timeout or completion condition. Their completion predicates intentionally differ, but a small polling helper or shared deadline iterator can own the timing/delegate mechanics and leave the output-specific and supervisor-specific state checks at the call sites. | `SharpProof.BuildTasks/RunVerifier.cs:405-488` |
| R487 | **`RunVerifier.WaitForExitOrCancellation` hard-codes the output polling interval.** The method waits with `Math.Min(remaining, 25)` even though the same class declares `OutputDrainPollingMilliseconds = 25` and uses that named constant in both other polling helpers. Reusing the existing constant removes a second authority for the interval and keeps later tuning from changing only one wait path. | `SharpProof.BuildTasks/RunVerifier.cs:31-32,826-846` |

### Status (part fifty-eight)

R486-R487 are `pending` review-only candidates. No implementation or build files
were edited.

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

R498-R502 are `pending`. No implementation files were changed; these are
review-only candidates, and the suggested sharing must retain fail-closed behavior
and the analyzer's distinct diagnostic and semantic-evaluation responsibilities.


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

R503 is `pending` and is four lines. It is filed despite its size because
negation rules are the part of an ignore file where being wrong is silent, and
three of these four point at paths that do not exist in the repository.


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

R504 is `pending`. Like R497 its payoff is build time rather than
maintainability, and the two should be considered together - caching the image
addresses the repetition, while splitting the layer addresses why a cache miss
costs more than it should.


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

R505 is `pending`. The fix is small - either mirror the ignored-`nupkgs` copy
into the loop path, or have the loop reject the commands that require a
`-PackageSource` - but it should be a deliberate choice, because the two paths
are intentionally different implementations rather than one shared one.


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

R507-R511 are `pending` reduction candidates. They are intentionally framed as
small shared cores or sequencing helpers: the metadata callers retain their
different predicates, source-location callers retain their different fallback
and binding policies, and the effect/lowering callers retain their
operation-specific semantics.


## Second survey, part sixty-seven: coverage configuration - no findings

Cross-checking the coverage baseline and runsettings against the project set.
No new finding; three applied items confirmed, and one earlier conclusion
strengthened.

### Checked and not proposed (part sixty-seven)

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

### Status (part sixty-seven)

No new `pending` item. Recorded to close the coverage-configuration area and to
add a twelfth data point to the applied-removal audit begun in part forty-nine and
continued in part fifty-four.

## Second survey, part sixty-eight: R512-R513 - configuration and syntax-shape helpers

| R512 | **`AnalyzerConfiguration` duplicates the option-validation scaffold for global and tree configuration.** `GetInvalidGlobalConfigurationValues` and `GetInvalidTreeConfigurationValues` both enumerate `AnalyzerConfigurationOptionRegistry.All`, call `TryGetConflictingAliases`, inspect presence/validity, build `InvalidAnalyzerConfigurationValue` records, and then independently check the retired `sharpproof_mode` aliases. The policies diverge afterward - global values are parsed and tree values are rejected as misplaced, with an optional global comparison - so a shared per-option validation iterator or callback can remove the repeated conflict/retired-mode plumbing without conflating those semantics. | `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs:71-102,138-173` |
| R513 | **`ConditionalTruthOperatorFacts.ReturnsConstant` repeats method/operator syntax extraction four times.** Expression-bodied methods and operators use parallel patterns, and one-return block methods and operators use another parallel pair; only operators additionally permit the harmless-discard form. A helper over the common `BaseMethodDeclarationSyntax`/body shape can extract the expression once, then leave the operator-only discard fallback explicit, reducing branching and the chance that one declaration form gains a different constant-admission rule. | `SharpProof.Effects/ConditionalTruthOperatorFacts.cs:50-88,106-127` |

### Status (part sixty-eight)

R512-R513 are `pending` reduction candidates. Both preserve the distinct
validation and syntax policies; the proposed seam is only the repeated
enumeration or declaration-shape plumbing.

## Second survey, part sixty-nine: R514-R517 - artifact validation and IR traversal seams

| R514 | **`CompilerManifestArtifact.HasValidEffectReplayTrees` revalidates geometry immediately after the codec already validated it.** For each effect claim it first calls `CompilerEffectClaimArtifactCodec.HasValidReplayGeometry`, which checks the event's tree ordinal, tree SHA-256, snapshot SHA-256, source bounds, unique-tree binding, and location span. It then loops over the same events and repeats the null/ordinal checks, tree lookup, tree and snapshot hashes, and source bounds, omitting only the codec's line-map and location-binding checks. Removing the second loop or making the codec expose the needed validation result preserves the manifest-level claim walk while eliminating two authorities for the same replay geometry. | `SharpProof.CompilerArtifact/CompilerEffectClaimArtifactCodec.cs:42-88`; `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:927-970` |
| R515 | **`IrRelationalSummaryBuilder.Charge` open-codes the IR child-kind switch already owned by `IrTraversal.GetChildren`.** The summary builder's bounded walk pushes receiver/arguments, unary, binary, conditional, cast, length, and sequence children in a private switch while tracking its own visited set and `Spend()` budget. `IrTraversal.GetChildren` has the same complete child enumeration and is already the canonical helper used by IR analysis and substitution. Reusing it inside the charged loop keeps the budget and visited semantics local but removes a second list of IR node kinds that can drift when the IR grows. | `SharpProof.Summaries/IrRelationalSummaryBuilder.cs:773-829`; `SharpProof.Ir/IrTraversal.cs:4-18` |
| R516 | **Two bounded IR predicates duplicate the same explicit stack/visited traversal scaffold.** `PostconditionObligationBuilder.IsSupportedProofDomain` and `ManagedContractFacts.ContainsPotentiallyFailingCast` each allocate a stack and `HashSet<IrId>`, push a root, skip visited terms, inspect each term, enumerate `IrTraversal.GetChildren`, and return a boolean. Their term predicates differ - supported-domain admission versus cast detection - so they should not be merged semantically, but a predicate-based `IrTraversal.Any` helper can centralize the non-recursive DAG walk and keep both depth-safe checks consistent. | `SharpProof.Worker/PostconditionObligationBuilder.cs:181-213`; `SharpProof.Analyzer.Core/ManagedContractFacts.cs:32-53`; `SharpProof.Ir/IrTraversal.cs:27-49` |
| R517 | **`SharpProofTrustedAttribute` and `SharpProofSuppressAttribute` duplicate their complete reason-validation constructor.** Both reject whitespace-only reasons with the same `string.IsNullOrWhiteSpace` guard, assign the same immutable `Reason` property, and differ only in the exception wording and `AttributeUsage` metadata. A small internal attribute-reason validator can preserve the distinct public attribute types and messages while removing the duplicated constructor policy. | `SharpProof.Attributes/SharpProofTrustedAttribute.cs:6-22`; `SharpProof.Attributes/SharpProofSuppressAttribute.cs:7-23` |

### Status (part sixty-nine)

R514-R517 are `pending` reduction candidates. The validation and traversal
items deliberately preserve the security and resource-limit checks at their
existing boundaries; the reductions target only repeated geometry, child
enumeration, walk mechanics, and constructor validation.

## Second survey, part seventy: R518-R520 - effect-scanner phases and native-loader cleanup

| R518 | **`OperationEffectScanner` duplicates the null-check-to-throw helper for receivers and locks.** `PotentialNullReceiver` and `PotentialNullLock` both return an empty summary when `_nullnessEvaluator.IsProvenNonNull` succeeds and otherwise call `Throw` with a framework exception identity; only the input parameter name and exception (`NullReferenceException` versus `ArgumentNullException`) differ. A parameterized `PotentialNullAccess` helper can centralize the branch while preserving the distinct C# failure semantics at each call site. | `SharpProof.Effects/OperationEffectScanner.cs:1216-1235` |
| R519 | **Deconstruction target descent is implemented twice for two phases with the same declaration/tuple recursion.** `ScanDeconstructionTargetEvaluations` and `ScanDeconstructionTargetWrites` both unwrap `IDeclarationExpressionOperation`, recurse through `ITupleOperation.Elements`, sequence child results with `EffectStep.Then`, and stop after a non-completing step. The phases must remain separate because C# evaluates all target locations before right-hand values and writes later, but a generic target-tree walker with a phase-specific leaf action can remove the duplicated structural recursion without changing that ordering. | `SharpProof.Effects/OperationEffectScanner.Expressions.cs:54-124` |
| R520 | **`ContainerNativeLibrary.InstallZ3ResolverRequired` has a cancellation catch with no cancellation source and an unsafe cleanup asymmetry.** The method accepts no `CancellationToken` and its `NativeLibrary.Load`, field writes, and `SetDllImportResolver` sequence has no cancellable operation, yet it singles out `OperationCanceledException` and rethrows before the general catch resets `_z3Handle`, clears `_z3Assembly`, and frees the native handle. The branch is therefore accidental complexity today and, if an OCE ever emerges from the loader boundary, leaves partially installed state; one cleanup path in a `finally`/general catch is easier to reason about. | `SharpProof.Host/ContainerNativeLibrary.cs:19-50` |

### Status (part seventy)

R518-R520 are `pending` candidates. R519 keeps the two evaluation phases
separate, and R520 is recorded as a cleanup/exception-path concern rather than
an instruction to broaden the native-loading surface.
