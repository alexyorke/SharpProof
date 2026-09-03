# SharpProof documentation map

SharpProof 1.0 is a soundness-first preview. The documents below have different
jobs; they are not interchangeable sources of truth.

## Start here

| Document | Audience | Role |
|---|---|---|
| [Project README](../README.md) | Package users | Installation, activation, examples, and the short product overview |
| [Getting started](getting-started.md) | New package users and CI authors | Task-oriented package setup, profile selection, strict verification, samples, and the first repository checks |
| [Coverage and limits](coverage-and-limits.md) | Users and contributors | Authoritative inventory of the currently implemented analyzer, worker, language, contract, and API-spec surface |
| [Supported public API](public-api.md) | Library authors | Supported contract types, package boundary, and XML-documentation guarantee |
| [Diagnostics](diagnostic-examples.md) | Analyzer and verifier users | Current `SP`, `SPCF`, SP0047, SP0048, and SP0049 diagnostics, defaults, policies, and examples |
| [Package-backed samples](../samples/README.md) | Evaluators and CI owners | Passing, diagnostic, mixed-outcome, strict-library, and host-rejection examples against packed artifacts |
| [Analysis limits](analysis-limits.md) | Build and CI owners | Shipping profile/feature/policy properties, worker bounds, and acceptance-only budgets |
| [Preview support boundary](preview-support.md) | Build and release owners | Normative container host, path, concurrency, and trusted-filesystem boundary |
| [Container development](container-development.md) | Contributors | Permanent Dev Container workflow, test concurrency, worktree isolation, and resource overrides |
| [Release constants and ownership](release-constants.md) | Maintainers | Classification and authoritative sources for pins, defaults, and derived measurements |
| [Typed abstention reasons](unknown-reasons.md) | Tool integrators | Exact typed reasons, run statuses, callable coverage, claim outcomes, and cache states |

## Normative and architectural documents

| Document | Status | Role |
|---|---|---|
| [SEMANTICS.md](../SEMANTICS.md) | Normative | Defines the soundness boundary. It wins over descriptive prose when documents conflict. |
| [SharpProof architecture](architecture.md) | Maintained design | Describes the enforced dependency graph, trusted boundaries, proof construction, and package split. |
| [SMT lifecycle](smt-lifecycle.md) | Maintained implementation reference | Describes solver ownership, proof and replay checks, and cache eligibility. |
| [Native SMT packaging](native-smt-packaging.md) | Maintained packaging reference | Describes the pinned Linux worker payload and analyzer/solver separation. |

The implementation remains the authority for enumerated surfaces:

- `SharpProof.Analyzer.Core/LanguageSubsetGate.cs` classifies analyzer callables,
  types, operation kinds, and operation shapes.
- `SharpProof.Specs/ApiSpecTable.cs` declares typed API specifications. Not
  every witnessed facet is consumed by the worker.
- `SharpProof.Specs/RelationalSpecPackCatalog.json` declares the embedded,
  explicitly enabled relational specification packs. The schema-1 catalog is
  strict data; relation parsing and identity validation remain handwritten in
  the build-time compiler collector.
- `SharpProof.Summaries` owns reusable typed-IR relational construction,
  instantiation, dependency analysis, and transitive provenance independent of
  Roslyn, PE metadata, and Z3.
- `eng/diagnostics/diagnostic-descriptors.v1.json` declares analyzer,
  `ContractFor`, and soundness-meta diagnostic IDs, severities, defaults,
  messages, order, and help links. The corresponding
  `*DiagnosticDescriptors.generated.cs` files are checked-in compiled
  projections.
- `SharpProof.Worker.Protocol/ProtocolModel.schema.json` declares protocol
  version 11, manifest schema version 4, cache schema version 13, policies, run
  statuses, callable coverage, claim outcomes/reasons, and summary records.
  `ProtocolModel.generated.cs` is the checked-in compiled projection.
- `SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json` is the
  authoritative compiler-artifact model. `CompilerArtifactModel.generated.cs`
  is its checked-in compiled projection, while
  `CompilerManifestArtifact.cs` validates the closed compiler-evidence
  envelope.
- `SharpProof.Frontend/ContractApi.catalog.json` is the authoritative contract
  API vocabulary. `Generate-ContractApiCatalog.ps1` produces declarative
  descriptors; `ContractApiMetadataRuntime.cs` contains the handwritten lookup
  behavior.
- `SharpProof.Analyzer.Core/AnalyzerDiagnostic.catalog.json` owns finite diagnostic
  wording projections for intrinsic and clause-placement failures.
  `Generate-AnalyzerDiagnosticCatalog.ps1` produces the projection; diagnostic
  selection and reporting remain handwritten.
- `SharpProof.Projection.catalog.json` owns finite output, result, clause-label,
  policy, operation-stage, and effect-wiring projections. `Generate-ProjectionCatalog.ps1` produces
  checked-in tables; validation, replay, and analysis algorithms remain
  handwritten.
- `SharpProof.DeclarativeModels.catalog.json` is the shared declarative storage
  catalog for cross-project result records and model containers.
  `Generate-DeclarativeModels.ps1` produces checked-in storage projections;
  validation, indexing, reconstruction, and fail-closed analysis algorithms
  remain handwritten.
- `SharpProof.Contracts/BoundContractModel.schema.json` is the authoritative
  bound-contract model vocabulary. `Generate-BoundContractModel.ps1` produces
  the data containers and enum projection; binding and failure construction
  remain handwritten.
- `SharpProof.Effects/EffectContractMappings.catalog.json` is the authoritative
  effect-contract, region, direct-event, and reference-family vocabulary.
  `Generate-EffectContractMappings.ps1` produces its declarative mapping
  tables; effect projection and validation algorithms remain handwritten.
- `SharpProof.Frontend/OperationSupport.catalog.json` is the authoritative
  finite Roslyn operation vocabulary for contract-expression lowering and
  effect discovery. `Generate-OperationSupportCatalog.ps1` produces its
  declarative stage tables; support queries and stage-specific validation
  remain handwritten.
- `SharpProof.Ir/IrModel.schema.json` and the
  `portableIrSlotMappings` section of the compiler-artifact schema own typed IR
  model and wire vocabulary. Their generated outputs contain declarative tables
  and wire projection adapters; indexing, validation, reconstruction, and
  fail-closed algorithms remain handwritten.
- `eng/acceptance/contract.json` declares release-gate budgets. Package
  defaults that are not release-gate fields live in the portable and verifier
  build-transitive props and targets.

## Acceptance and evidence

| Document | Status | Role |
|---|---|---|
| [Exhaustive code-usefulness audit](code-usefulness-audit.md) | Dated evidence | Records the fixed 838-file baseline, line-level coverage ledger, accepted cleanup, rejected leads, metrics, and validation. |
| [Acceptance contract](../eng/acceptance/README.md) | Active | Defines the release checks for the 1.0 preview. |
| [Release gates](../SharpProof.Gates/README.md) | Active | Documents the corpus, metamorphic, performance, and cancellation runners. |
| [Open-source corpus](../SharpProof.Gates/Corpus/README.md) | Active | Records corpus provenance, licensing, instrumentation, and update procedure. |
| [2026-08-08 relational interprocedural verification](soundness-notes/2026-08-08-relational-interprocedural-verification.md) | Dated evidence | Records the bounded source, exact implementation-IL, and audited-pack relation boundary and its executable evidence. |
| [2026-07-30 allocation effect replay](soundness-notes/2026-07-30-allocation-effect-replay.md) | Dated evidence | Records the independently interpreted allocation-effect refutation boundary and executable evidence. |
| [2026-07-29 formatting-neutral source metrics](soundness-notes/2026-07-29-formatting-neutral-source-metrics.md) | Dated evidence | Records removal of compression-oriented formatting and LOC gates. |
| [2026-07-27 product bug sweep](soundness-notes/2026-07-27-product-sweep.md) | Dated evidence | Records analyzer, contract, effect, and worker adversarial fixes plus exact validation evidence. |
| [2026-07-25 hardening audit](soundness-notes/2026-07-25-hardening.md) | Dated evidence | Records one completed hardening tranche and its remaining checkpoints. |
| [2026-07-25 API-spec result domains](soundness-notes/2026-07-25-api-spec-result-domains.md) | Dated evidence | Records the bounded worker result-projection tranche. |

Soundness notes record what was reviewed at a point in time. They are historical
evidence, not current product instructions; their old protocol, schema, host,
and test-count references are intentionally retained as evidence. They do not
replace the current coverage inventory or normative semantics.

## Known production gaps

During container verification, the production analyzer emits a deterministic
schema-18 compiler artifact from the final post-generator Roslyn
`Compilation`. It contains the selected-claim manifest and portable lowered
whole-body CFG/IR for supported selected callables, plus bounded relational
source/implementation-IL/audited-pack calls, bound contract/spec
metadata, compiler diagnostics, generated-tree hashes, bounded options, mapped
locations, and identity/provenance evidence. It contains no source text.

The worker validates and hydrates that closed artifact without constructing a
Roslyn compilation or rereading reference files. Exact manifest/lowered
callable equality and the compiler-visible expression-depth match are required
before cache or backend work. Compiler and reference identities are provenance,
not a runtime Roslyn-build gate. The compiler reconstruction portion of
production-plan Step 4 is complete for the bounded verifier subset.

Independent whole-body postcondition-counterexample replay is implemented for
the admitted scalar program subset. The proof kernel checks exact model closure
and the lowered assumptions/goal before the worker independently executes the
compiler-produced whole-body CFG. Schema 18 carries independently replayable
events for unconditional definite managed object/array allocation, exact
framework explicit throw, empty `lock`, and exact `Monitor` calls. The worker
authenticates the selected effect, capability, and exception constraints,
derives the replayed witness, and publishes only matching violations. Other
effect candidates still fail closed as typed `Unknown`, and effect results
remain noncacheable. Worker protocol 11, cache schema 13, relational-summary
schema version 2, and specification-pack schema version 1 carry the current
wire contract. The three-package split, portable SourceLink symbols,
package validation, immutable
tagged-byte validation, trusted-publishing workflow, package-backed sample
matrix, and exact public API XML coverage are implemented. The tag workflow
requires checked-in version equality, master ancestry, and predecessor-tag
order, then allowlists private `preview.1`, public `preview.2`, public `rc.1`,
and stable `1.0.0` promotion of the already-tested bytes. Publication
preflights every main package and fails if the version already exists;
duplicate skipping is never used. Main and symbol packages are then pushed
separately in dependency order. A symbol collision or partial publication
requires a new version. Deterministic SARIF 2.1.0 projection is available as an
opt-in verifier output. Owner configuration of
protected release environments and tags, pilot-library evidence, the first
private/public NuGet publications, and exact-candidate release evidence are
future work. Preview changes use the acceptance-owned solo evidence gate;
stable 1.0 governance is separate. Current behavior and limits are recorded in
[Coverage and limits](coverage-and-limits.md#closed-compiler-artifact-and-remaining-limits).

## Machine-owned Markdown

- docs/api-spec-catalog.generated.md is generated from the declarative API
  catalog; review and edit the JSON source and rerun its owning generator.
- `SharpProof.Analyzer/AnalyzerReleases.Shipped.md` and
  `AnalyzerReleases.Unshipped.md` are Roslyn release-tracking inputs. Tests
  reconcile them with the active descriptor catalog; edit them only as part of
  a diagnostic release change.

## Maintenance

Markdown is hand-maintained. `scripts/Test-SharpProofReadme.ps1` validates
code-derived versions, acceptance-contract versions, configuration values,
diagnostics, API-spec IDs, worker properties, protocol enums, local links,
anchors, XML and PowerShell fences, line endings, and BOM policy; the analyzer
test suite compiles every maintained C# fence. The script does not
generate these files. When behavior changes, update the relevant source-owned
table first, then update the coverage, diagnostic, limit, or reason reference
that mirrors it. Dated soundness notes remain subject to link and file-format
checks but are excluded from current-version drift checks. Archived agent notes
under eng/agent-notes/archive/ are historical audit material and are not an
active work queue.
