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
| R269 | `Invoke-RequiredDotnet` is byte-identical in three test-invocation scripts, and `$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'` is repeated in six. `SharpProof.ContainerExecution.psm1` already exports eleven shared functions to these same scripts and is the obvious home. `Invoke-DotNet` in the container dispatcher and `Invoke-Docker` in `build.ps1` are the same run-check-`$LASTEXITCODE`-throw shape again. | `Invoke-SharpProofChangedTests.ps1:32,44`; `Invoke-SharpProofPackageTests.ps1:39,96`; `Invoke-SharpProofSemanticTests.ps1:37,82`; `Invoke-SharpProofCoverage.ps1:64`; `Invoke-SharpProofDevCheck.ps1:19`; `Invoke-SharpProofFuzzCampaign.ps1:81` |
| R270 | `Get-SpdxPackageId` is byte-identical, 18 lines, in `New-SharpProofReleaseEvidence.ps1` (which produces the SBOM package IDs) and `Test-SharpProofReleaseArtifacts.ps1` (which validates them). The validator re-implements the producer's rule instead of importing it, so a change made in both places in the same edit passes the check while changing the released identifiers. This is release-evidence code, so it belongs under the same caution as deferred R110-R112, but the duplication itself weakens the check rather than protecting it. | `scripts/New-SharpProofReleaseEvidence.ps1:55-72`; `scripts/Test-SharpProofReleaseArtifacts.ps1:19-36` |
| R271 | The release version grammar is a byte-identical six-line semver regex under two names: `Test-SharpProofReleaseVersionSyntax` in the release-version authority and `Test-SharpProofPublicationVersionSyntax` in the publication-plan identity module. | `scripts/Get-SharpProofReleaseVersion.ps1:1-10`; `scripts/SharpProof.PublicationPlanIdentity.psm1:7-16` |
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

R263-R265 and R269-R276 are `pending` and extend the same follow-up queue. R263, R265,
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

R285, R286, R288, and R289 are `pending`. R287 is not a reduction and should not
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
| R297 | `FinalCompilationCollectorTests.cs` declares a private `DictionaryOptions` class whose `TryGetValue` is byte-identical to `DictionaryAnalyzerConfigOptions.TryGetValue` in `eng/testing/DictionaryAnalyzerConfigOptions.cs`. `SharpProof.Analyzer.Test` already compiles that shared file - `Directory.Build.props` links it into exactly this project as part of applied R164 - yet the file references the shared type **zero** times. This is a call site the applied item missed rather than a new duplication. Note the neighbouring `FixedAnalyzerConfigProvider` in the same file is **not** a candidate: it throws from `GetOptions` where the shared `DictionaryAnalyzerConfigOptionsProvider` returns `Empty`, which is a deliberate strictness difference the test relies on. Only the options class is a clean swap. | `SharpProof.Analyzer.Test/FinalCompilationCollectorTests.cs:1445-1459`; `eng/testing/DictionaryAnalyzerConfigOptions.cs:6-31`; `Directory.Build.props:95-101` |

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
duplication actually lives. R297 is mechanical and closes a gap in already-applied
R164. R298 is deliberately filed below the mechanical tier because each merge
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

R299 should leave this ledger and become a fix: the contract and the catalog
disagree today, and the branch cannot pass its own mutation gate until one of
them moves. The reduction-shaped part of it - adding a static catalog-identity
assertion so the two cannot silently diverge again - is worth doing at the same
time. R300 is `pending` and is the documentation half of the same problem.

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
| R303 | **Expands R270.** `New-SharpProofReleaseEvidence.ps1` (the SBOM producer) and `Test-SharpProofReleaseArtifacts.ps1` (its validator) share 43 identical lines in 5 runs, which is 13 percent of the validator. `Get-SpdxPackageId` from R270 is only the largest run (16 lines); the rest are the `PackageSource` resolve-and-check preamble (6 lines), the `$sbom.packages`/`documentDescribes`/`relationships` unpack plus `Test-SharpProofSbomTopology` call (7 lines), the `Test-SharpProofSbomDependencyGraph`/`ComponentGraph` call sequence (8 lines), and the component-key projection plus count assertion (6 lines). `Publish-SharpProofRelease.ps1` shares a further 26 lines in 4 runs with the same validator. The pattern is consistent: the validator re-derives what the producer computed instead of importing it, so an edit applied to both in one change passes the check while altering the released artifact. | `scripts/New-SharpProofReleaseEvidence.ps1`; `scripts/Test-SharpProofReleaseArtifacts.ps1`; `scripts/Publish-SharpProofRelease.ps1` |
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

R303, R304, and R305 are `pending`. R304 is the largest single duplication in the
repository by line count and has a ready home in a module both scripts already
import, but it is process-lifetime and timeout code, so the deferral reasoning in
R072, R074, and R076 governs how carefully it is done - not whether it is real.
R303 shares R270's caveat about release-authority code.

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
| R316 | `InternalsVisibleTo` is declared **four different ways** across the 20 assemblies that use it: as an `<InternalsVisibleTo Include="..."/>` MSBuild item in the `.csproj` (10 assemblies), as a fully-qualified `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(...)]` in `AssemblyInfo.cs` (6), as `using System.Runtime.CompilerServices;` plus the short attribute in `AssemblyInfo.cs` (4), and once in the legacy `Properties/AssemblyInfo.cs` location - `SharpProof.Dataflow` is the only project in the repository that uses the `Properties/` subdirectory at all. Worse than the spelling variety, **three assemblies split the declaration across both mechanisms**, so answering "who can see this assembly's internals?" requires reading two files and taking the union: `SharpProof.Contracts` declares 5 in `AssemblyInfo.cs` and 1 in the csproj; `SharpProof.Frontend` 3 and 4; and `SharpProof.Ir` **10 and 10, for 18 distinct assemblies, with `SharpProof.CompilerCollector` and `SharpProof.Worker` declared in both places**. Those two are outright duplicate grants. Consolidating on one mechanism - the csproj item is the modern one and already the plurality - removes the split, the two duplicates, and four of the five `AssemblyInfo.cs` files that exist only to hold these attributes. | `SharpProof.Ir/AssemblyInfo.cs` and `SharpProof.Ir/SharpProof.Ir.csproj`; `SharpProof.Contracts/AssemblyInfo.cs` and its csproj; `SharpProof.Frontend/AssemblyInfo.cs` and its csproj; `SharpProof.Dataflow/Properties/AssemblyInfo.cs`; 20 assemblies surveyed |

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

R316 is `pending` and mechanical, with one caveat worth stating: the csproj
`InternalsVisibleTo` item requires the project to be SDK-style with
`GenerateAssemblyInfo` enabled, which every affected project already is, since ten
of them already use that form. The two duplicate grants in `SharpProof.Ir` are
harmless today - a repeated grant is idempotent - so this is a legibility and
maintenance item, not a correctness one.

## Second survey, part eighteen: R317

Repository documents that make checkable claims nothing checks.

| ID | Finding | Evidence |
|---|---|---|
| R317 | `BUGS.md` carries **three mutually inconsistent counts of itself** in twenty lines. Its opening line says "70 open bugs, reprioritized by impact, reachability, and affected scope." Its section headers declare `P0 - Critical (1)`, `P1 - High (25)`, `P2 - Medium (0)`, `P3 - Low (0)`, which sums to 26. The file actually contains **one** bug entry - BUG-146 under P0 - and the P1 section that claims 25 items is **empty**. So the prose says 70, the headers say 26, and the content says 1. Nothing validates the file: `BUGS.md` appears in no maintained-document list in `Generate-Readme.ps1`, in no acceptance-contract path list, and in no test. The only reference to it anywhere in the repository is the code-usefulness audit noting that *`eng/agent-notes/status.md`'s* references to it were stale and had been repaired - so this document's staleness has already been noticed once, from the outside, without the document itself being fixed. This is the same class as R300: a file asserting figures that no gate compares against reality. Either the P1 entries were removed without updating the headers, or the headers were written ahead of content that never landed; the file cannot currently be read as evidence of anything. | `BUGS.md`; `scripts/Generate-Readme.ps1:36-65`; `docs/code-usefulness-audit.md:875` |

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

R317 is `pending` and is trivially cheap to fix, but the useful part is the
pattern it completes with R299 and R300: this repository has strong machine-checked
authority over its *code* - generated version pins, TCB path lists, release
configuration cross-validated against the workflow - and essentially none over its
*prose*. Every stale figure found in this survey (the mutation catalog count, the
TCB path count in `status.md`, all three counts in `BUGS.md`) sits in a document
outside `$currentMaintainedDocuments`. Adding these two files to that list would
close the category rather than the individual instances.

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
| R320 | `scripts/Format-CSharp.ps1` (89 lines) is **the only script in `scripts/` with no code or build reference anywhere in the repository**. It is not in `Invoke-SharpProofContainer.ps1`'s 37-command `ValidateSet`, not invoked by any workflow, test, `.csproj`, `.props`, `.targets`, or other script, and not named in any acceptance-contract path list. Its only two mentions are prose: the code-usefulness audit, which retained it "after MSBuild import, workflow, package, release, or dynamic invocation review", and a soundness note describing what it does. There is no dynamic invocation - the audit's retention rationale does not hold for this file. More pointedly, the script carries a `-Verify` switch that appends `--verify-no-changes` to `dotnet format whitespace` and `dotnet format style --severity warn`, which is unmistakably a CI formatting gate, and **nothing runs it in either mode**. The likely reason nothing runs it is that its `-Verify` mode is redundant: `Directory.Build.props` sets `EnforceCodeStyleInBuild=true`, `.editorconfig` sets `dotnet_diagnostic.IDE0055.severity = warning`, and production and test projects set `TreatWarningsAsErrors=true`, so formatting violations already fail the build. That makes the verify half genuinely surplus and the fix half a developer convenience that is fine to keep - but the current state, an unreferenced script containing an unwired gate, is worth resolving deliberately rather than leaving as an open question for the next reader. | `scripts/Format-CSharp.ps1`; `scripts/Invoke-SharpProofContainer.ps1:3`; `Directory.Build.props:20`; `.editorconfig:14`; `docs/code-usefulness-audit.md:939`; `docs/soundness-notes/2026-07-29-formatting-neutral-source-metrics.md:45` |

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

R320 is `pending` and is a decision rather than a deletion: keep the fix mode as a
developer tool and drop the redundant `-Verify` mode, or wire the whole thing into
a profile. Either resolves it. The finding is recorded mainly because an
unreferenced script that contains a working but unwired gate is exactly the kind
of thing that reads as intentional to one reader and as an oversight to the next.

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
| R324 | The six effect-contract attributes in `SharpProof.Attributes` each repeat `[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property)]`, and `SharpProofSuppressAttribute` and `SharpProofTrustedAttribute` each repeat a seven-member variant (`Assembly | Class | Constructor | Interface | Method | Property | Struct`). That is the definition of "where may a SharpProof contract be applied" written out eight times across two shapes, so extending the allowed targets means editing six or two files in step and a divergence would silently let one attribute be applied where its siblings cannot. C# requires `[AttributeUsage]` per attribute class, so the attribute itself cannot be shared - but its argument can: attribute arguments must be compile-time constants, and an `internal const AttributeTargets` in the same assembly satisfies that. This is small and cosmetic next to the rest of this ledger; it is recorded because it is a public-API surface where a silent divergence would be a real behaviour change. | `SharpProof.Attributes/{AllowedCapabilities,AllowedExceptions,DoesNotThrow,EffectContract,EnforcePure,ZeroAllocations}Attribute.cs`; `SharpProof.Attributes/{SharpProofSuppress,SharpProofTrusted}Attribute.cs` |

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
visibility change. R323 folds into R281 and should be done with it. R324 is
`pending` and minor.

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
