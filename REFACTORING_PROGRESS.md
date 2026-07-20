# SharpProof Refactoring Progress

This is the active source of truth for the comprehensive refactor. Read
`docs/refactoring-baseline.md` for the immutable starting point and
`CANONICAL_OPERATION_TRANSFER_PLAN.md` for historical semantic constraints.

## Invariants

- Preserve diagnostics, proof outcomes, conservative `Unknown` behavior,
  CLI/JSON/SARIF bytes, package contents, and attribute semantics.
- Public .NET API breaks are allowed when they produce a cleaner design.
- Land bounded green commits; delete superseded paths in the same tranche.
- Run .NET commands through `scripts/Invoke-SharpProofDotnet.ps1` or the
  repository test wrapper.

## Completed

- [x] Baseline captured in `docs/refactoring-baseline.md`.
- [x] Architecture tests enforce module ownership, dependency direction, and
  absence of cross-project source compilation.
- [x] `SharpProof.Contracts` and `SharpProof.Tooling.Core` own former `Shared`
  production code. Commit `ff682340`.
- [x] `SharpProof.Testing` owns shared fixtures; tooling tests have one owner;
  `SharpProof.SymbolicCli.Core` owns reusable CLI projections. No external
  `<Compile Include>` remains. Commit `0d9043b1`.
- [x] Analyzer method facts now live in an immutable `MethodAnalysisSnapshot`;
  feature analyzers consume the snapshot while session state exclusively owns
  symbolic query execution and caching.
- [x] `SymbolicQueryService` routes all public query families through one
  validated internal request, including common context validation, analysis
  limits, target requirements, and SMT requirements.
- [x] Public `SymbolicQueryService` is a thin coordinator; source compilation,
  dispatch, execution, and result projection live in the internal
  `SymbolicQueryExecutor`.
- [x] `SharpProofAnalysisSession`, discriminated `SharpProofQuery` records, and
  `SharpProofQueryResult` now form the primary public API. CLI, explain modes,
  samples, documentation, and package consumers use it; the former query
  service is internal and its preview removal is recorded in the API snapshot.
- [x] The preview compatibility cutoff is complete: legacy `Symbolic*` query,
  result, evidence, error, budget, project-context, and raw SMT types are
  internal. Focused immutable `SharpProof*` targets, payloads, evidence,
  errors, budgets, and solver metadata form the exported surface; the internal
  CLI host preserves the established external schemas and bytes.
- [x] Compatibility adapters were removed from the new API boundary. Payloads
  no longer retain raw symbolic results through `LegacyValue`; compact metadata
  now carries location, unknown reasons, truncation, and evidence directly.
  `FromLegacy`, `ToLegacy`, target-conversion, and error-conversion helpers were
  deleted. The internal CLI host calls the internal executor directly, keeping
  its established output without translating through the public API.
- [x] Source-query and runtime-hazard target dispatch are isolated from
  `SymbolicQueryExecutor`; the executor now owns API coordination while the
  dispatchers own source-kind validation, target routing, and node/syntax-tree
  execution. The superseded executor branches were deleted.
- [x] Condition-proof target, SMT, and source dispatch plus syntax-node proof
  execution live in `SymbolicConditionProofDispatcher`; the executor retains
  only common request validation, limits, and error coordination.
- [x] Program-point result construction is centralized in
  `SymbolicProgramPointProjector` over an immutable query context. Syntax-tree
  aggregation and direct node queries share it, and duplicate node projection
  was deleted.
- [x] Complexity result shaping and stable driver, unknown-reason, and callee
  deduplication live in `SymbolicComplexityResultProjector`, separate from the
  analysis session and cost modeling.
- [x] Complexity sequence/branch combination, loop multiplication, summary
  creation, and driver/callee evidence construction live in
  `SymbolicComplexityAlgebra`; duplicate session-owned algebra was deleted.
- [x] Complexity expression-to-cost projection lives in
  `SymbolicComplexityCostModel`, shared by loop modeling and callee
  substitution. Loop bound recognition, step validation, dependency tracking,
  and mutation analysis live in `SymbolicComplexityLoopModel`; the former
  `AnalysisSession` implementations were deleted.
- [x] Complexity call target resolution, top-level fallback traversal,
  known/external/dynamic classification, source-method recursion, parameter
  substitution, and callee evidence live in `SymbolicComplexityCallModel`.
  Recursive summary caching remains the sole responsibility of the reduced
  `AnalysisSession`, and its duplicate call-model methods were deleted.
- [x] Regex character-class normalization is isolated in
  `Z3RegexCharacterRanges`: bounded category/shorthand expansion caching,
  range merging, and complement construction are separate from Z3 encoding.
  The duplicate translator-owned caches and range algorithms were deleted.
- [x] Regex anchor stripping and inline option/trivia normalization live in
  `Z3RegexPatternNormalizer`, which produces a normalized body and explicit
  start/end semantics before Z3 parsing begins. Duplicate translator-owned
  anchor and lexical helpers were deleted.
- [x] Regex input validation and fallback classification live in
  `Z3RegexTranslationValidator` and `Z3RegexTranslationResult`. Invalid,
  oversized, normalization-failed, and unsupported-fragment outcomes are
  explicit; `Z3FormulaEncoder` consumes the typed result while preserving
  conservative unsupported behavior.
- [x] Program-point invariant execution, including initial-state and
  current-statement completion semantics, lives in
  `SymbolicProgramPointAnalyzer`. `SymbolicSourceQueryService` retains target
  resolution and aggregation; its duplicate invariant execution was deleted.
- [x] Condition parsing, speculative binding, symbolic lowering, SMT proof
  execution, witness construction, and batch proof evaluation live in
  `SymbolicConditionProofEngine`. `SymbolicSourceQueryService` retains only its
  internal facade and source-result aggregation; the superseded proof
  implementation was deleted.
- [x] Source-node indexing, expression-context selection, line/span candidate
  discovery, nearest-program-point ranking, and containment metadata live in
  `SymbolicSourceTargetSelector`. Source queries and condition proofs share it;
  the service-owned selector and cache were deleted.
- [x] `AnalyzerDiagnosticCatalog` is the single supported-diagnostics source.
  Each entry carries its descriptor, owning analyzer feature, optional
  configuration key, and documentation URI; release, documentation, and
  configuration metadata are characterized together, and the analyzer-owned
  duplicate descriptor list was deleted.
- [x] `CodeFixHandlerRegistry` is the single source for fixable diagnostic IDs,
  handler-family dispatch, simple-removal operations, titles, equivalence keys,
  and attribute targets. The exported provider no longer owns duplicated ID or
  removal-registration lists and dispatches typed families instead of IDs.
- [x] Code-fix registration execution is split into focused simple-removal,
  purity, synchronization, misplaced-requires, inferred-contract, and
  null-forgiving handlers. The exported provider resolves the typed registry
  entry and delegates once; its family-specific dispatch branches were deleted.
- [x] Diagnostic descriptor declarations are split into feature-owned partial
  files for purity, nullability, allocation, exceptions, suggestions,
  capabilities, ensures, complexity, requires, placement, and common bugs.
  `SharpProofDiagnostics` now owns only stable IDs/properties and descriptor
  factories, while `AnalyzerDiagnosticCatalog` remains the sole index.
- [x] `MethodAnalysisSnapshot` is the single immutable analyzer method-analysis
  owner for method identity, declaration, semantic model, source, operation
  blocks, root, and visible operations. The preceding request/input carriers
  and their test-only semantic-count projection are deleted.
- [x] Exception-flow (including recursive source callees), inferred exception
  contracts, ensures proofs, and nullable proofs consume the snapshot or their
  direct source target. Subnode proof validation and nonfatal error mapping are
  analyzer-owned boundaries; symbolic execution exposes only its throwing API.
- [x] `SymbolicIrProofResult` owns proof-result construction, cache-hit/status
  projection, SMT outcome mapping, proof-stage classification, and structured
  unknown-reason mapping. `SymbolicProofService` no longer owns result
  projection or mixes it with state encoding and solver orchestration.
- [x] `SymbolicProofCacheStore` owns bounded per-SMT-service cache lifetimes and
  the bounded process fallback; `SymbolicProofCache` owns proof-result and
  encoded-state namespaces plus hit/miss/eviction accounting. The nested cache,
  weak table, fallback singleton, and capacity constants were deleted from the
  proof service.
- [x] `SymbolicProofStateFacts` owns state normalization, query version
  rewriting, syntactic truth evaluation, and fact/condition
  containment/contradiction. Proof orchestration and divisor validation consume
  it directly; the service-owned state-fact implementation was deleted.
- [x] `SymbolicProofEncoder` owns condition/fact/state encoding, version-aware
  normalization, safe integer-divisor validation, conditional and
  short-circuit assumptions, and unsupported-encoding classification. Direct
  consumers use the encoder; the proof service shrank to solver-oriented
  classification, budgets, and cache coordination.
- [x] `Z3RegexExpressionFactory` owns primitive Z3 regex construction,
  all-character creation/caching, ranges, dot semantics, concatenation, loops,
  and literals. `Z3RegexTranslator` is now parser/AST orchestration over the
  existing normalizer, validator, character-range service, and expression
  factory instead of constructing primitive solver expressions itself.
- [x] The EffectSummary executable `Program` now owns only top-level invocation
  and argument-error handling. CLI orchestration moved to `EffectSummaryCli`,
  deleting the monolithic host class from the executable entry-point file while
  preserving command behavior and generated artifacts.
- [x] `EffectSummaryProgressStore` now owns sharded input fingerprints,
  artifact-spec and sharded checkpoint validation, atomic persistence, and tool
  identity. The CLI's progress adapters and duplicated JSON helpers were
  deleted; resume behavior and fingerprint mismatch failures remain exact.
- [x] `EffectSummaryOutputWriter` now owns external JSON serialization,
  stdout/file selection, directory creation, and stable dependency-manifest
  bytes. The CLI-owned output and manifest forwarding helpers were deleted.
- [x] `EffectSummaryInputResolver` now owns explicit/runtime assembly
  selection, dependency output normalization, and deterministic shard paths.
  The CLI-owned input/path adapters were deleted without changing path errors
  or assembly selection.
- [x] `GeneratedPurityCatalogReader` now owns persisted catalog metadata
  extraction, structural identity parsing, and tolerant optional-field
  projection. The CLI-owned JSON traversal and entry adapter were deleted.
- [x] `EffectSummaryAnalysisPipeline` now composes assembly summarization,
  symbol filtering, fixed-point purity classification, fallback inventory, and
  document projection. The CLI-owned document-construction helper was deleted,
  leaving the CLI responsible for command-mode orchestration.
- [x] `ToolCommandHost` provides the lightweight sync/async argument-error
  boundary for command tools. EffectSummary and Fuzz now use it with their
  existing exit codes and usage streams; duplicate top-level catch blocks were
  deleted and the architecture dependency allowlist records both edges.
- [x] Baseline and CorpusReport now execute through `ToolCommandHost`; their
  duplicate parse/catch blocks were deleted while distinct no-input behavior,
  invalid-argument exit code 64, usage text, and stderr routing remain exact.
- [x] SymbolicCli now executes through the classified `ToolCommandHost`
  overload. Its nonfatal predicate and typed error writer remain authoritative,
  fatal exceptions still escape, and the local top-level catch adapter was
  deleted.
- [x] The canonical method-analysis path has one immutable
  `MethodAnalysisSnapshot` construction point and one session-owned cache.
  Analyzer features and symbolic capability/complexity queries consume its
  shared source directly; the duplicate request and symbolic-input types are
  absent.
- [x] `SymbolicQueryContext` now owns the immutable source/target/options
  request boundary, and `SymbolicQueryOptions` owns normalized references,
  limits, implied conditions, and SMT dependencies in its own file. These
  request concerns were removed from the aggregate query API file.
- [x] `SymbolicSourceInput` now owns source/file/tree/node construction and
  source-map state, while `SymbolicQueryTarget` owns target validation and
  query-scope representation. Both model families were removed from
  `SymbolicQueryApi`, reducing it to execution and result projection.
- [x] `SymbolicQueryResult` now owns query aggregation, filtering, scope
  projection, line grouping, and truncation/evidence preservation in its own
  file. `SymbolicQueryApi` is now a 235-line executor-only coordinator.
- [x] `SymbolicSourceProgramPointExecutor` now owns point selection analysis,
  position validation, condition-proof aggregation, and program-point
  projection. The source query service's four private execution/projection
  implementations were deleted.
- [x] `SymbolicSourceRangeQueryExecutor` now owns line, nearest-line-point,
  span, line-span, and all-lines selection, execution, aggregation, ordering,
  and scope projection. The source query service's range loops and selection
  implementations were deleted, reducing it to a 243-line facade.
- [x] `SymbolicComplexityAnalysisSession` now owns recursive summary caching,
  operation traversal, branch/loop/call composition, and cycle handling. The
  complexity service is a 108-line target-resolution coordinator over the
  existing loop, call, cost-algebra, and result-projection components.
- [x] The obsolete fixed 20,000/22,000-line reduction gates and their dedicated
  production-rewrite baseline were deleted. Refactoring metrics schema v2 keeps
  LOC informational; documented completion now depends on eliminating
  duplicated responsibilities and unreachable legacy paths.
- [x] Thirteen verified ignored stale test-result directories were removed
  from the workspace. `RepositoryArchitectureTests` now rejects tracked build,
  package, coverage, test-result, log, and temporary artifacts so generated
  files cannot silently acquire source ownership.
- [x] Removed the remaining production adapter files and types. Canonical
  symbolic transfer, analyzer purity transfer, Roslyn identity, and ECMA
  identity callers now use their direct owners; an architecture test rejects
  new production `*Adapter.cs` layers.
- [x] Removed the remaining generic compatibility helpers. Baseline JSON
  reading, case-insensitive schema-property lookup, analyzer JSON projection,
  and lower-hex encoding now have direct single-purpose owners. Effect-summary
  compatibility reporting remains because it rejects untrusted schema/data;
  it is product validation rather than a legacy shim.
- [x] Continued the production-reduction follow-up by collapsing seventeen
  one-method purity-rule dispatch shells into one stateless core-operation
  policy owner and the registry's single typed boundary. Canonical child
  traversal owns structurally identical array, inline-array, and null-test
  behavior; the generic rule-base adapter is deleted.
- [x] Consolidated baseline and proof-evidence schema ownership into Contracts.
  Analyzer configuration and additional-file validation, the Baseline tool,
  Symbolic CLI, and EffectSummary now share the immutable DTOs and validation
  rules directly. The Analyzer JSON/property adapters, Symbolic schema facade,
  and generic JSON property helper were deleted; characterized JSON, SARIF,
  suppression, validation-error, and CLI boundaries remain unchanged.

## Current evidence

- Branch: `codex/nullable-contract-verification`.
- Enforced maintained production source: 94,284 lines, 13,342 fewer than the
  production-reduction baseline and 6,658 from its 20,000-line target.
- Architecture inventory: zero unassigned files and zero dependency violations.
- Release solution build: zero warnings and errors.
- Six lanes: 6,205 passing tests and two documented skips.
- Package consumers pass with native SMT required on Windows x64.
- Release validation passes with warnings as errors for ProofCore, Symbolic,
  Attributes, Analyzer, CodeFixes, NuGet packaging, Symbolic CLI, EffectSummary,
  and VSIX.
- Generated README/example pages, configuration reference, test-impact
  inventory, and production metrics all verify current. The enforced maintained
  LOC ledger is recorded in `PRODUCTION_REDUCTION_PROGRESS.md`.
- The six-lane matrix covers exact CLI/package/VSIX, seeded fuzz,
  EffectSummary, architecture, and public-API boundary fixtures; 6,205 tests
  pass with the same two documented Main skips.

## Remaining tranches

- [x] Consolidate internal analysis request, context, and immutable snapshot
  shared by analyzer and Symbolic query consumers.
- [x] Redesign and reduce the public Symbolic API; adapt CLI while preserving
  external output.
- [x] Decompose query/proof/source services and complexity/solver components by
  responsibility, deleting duplicate orchestration.
- [x] Replace monolithic analyzer diagnostic and code-fix dispatch surfaces with
  typed registries.
- [x] Decompose EffectSummary host and standardize lightweight tool hosting.
- [x] Finish test-lane/repository organization and remove dead compatibility
  paths.
- [x] Run final Release, six-lane, package-consumer, NuGet, VSIX, generated-doc,
  fuzz, EffectSummary, architecture, and public-API gates.

## Next cheapest step

The comprehensive architecture refactor remains complete. The second
20,000-line maintained-production reduction is tracked in
`PRODUCTION_REDUCTION_PROGRESS.md`; its immutable starting point is commit
`f5e75830` at 84,434 maintained-production lines.
