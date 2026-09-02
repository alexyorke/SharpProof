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
R107, R149, R165, R169, R171-R185, R194, R204, R211, R217-R218, and R221.
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
