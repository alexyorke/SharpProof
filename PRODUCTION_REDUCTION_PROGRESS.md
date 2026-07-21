# SharpProof Production Reduction Progress

This is the source of truth for the 20,000-line maintained-production reduction.
Tests are excluded from the metric and must not be deleted.

## Constraints

- Preserve diagnostics, attributes, CLI behavior, serialization, packages, and
  conservative `Unknown` semantics.
- Count tracked handwritten production C#, PowerShell scripts, generators, and
  maintained production specifications.
- Do not obtain reductions through minification, code in strings, hidden
  generated complexity, or moving maintained logic outside the metric.
- Delete superseded implementations in the same green tranche.

## Baseline

- Commit: `e6c324df`.
- Maintained production: 107,626 lines, including the reduction enforcement
  script introduced with this baseline.
- Required reduction: 20,000 lines.
- Completion ceiling: 87,626 lines.
- Baseline tests: 6,147 passing with two documented skips.

## Phase Two Baseline

- Commit: `f5e75830`.
- Maintained production: 84,434 lines (80,686 C#, 3,014 scripts, and
  734 maintained specification lines).
- Required additional reduction: 20,000 lines.
- Completion ceiling: 64,434 lines.
- Migration-scaffolding ceiling: 84,934 lines until the first semantic-owner
  deletion tranche ratchets it downward.
- Test preservation baseline: 4,469 attributed test methods. Existing methods
  and parameterized-case counts may not decrease, and no new ignore, explicit,
  or runtime-ignore marker may be introduced.
- `scripts/production-reduction-phase2-baseline.json` is the immutable phase-two
  accounting source. The original baseline remains historical evidence.

## Completed

- [x] Added a tracked maintained-production baseline covering handwritten C#,
  PowerShell build/generator scripts, and explicit production specifications.
- [x] Added a reporting command with milestone and final-target enforcement.
- [x] Added an architecture test that validates the 20,000-line target,
  baseline arithmetic, and non-test production categories.
- [x] Added the shared inferred-summary model with typed purity, effects,
  freshness, visibility, explicit unknown reasons, stable cache keys, and
  semantic differential comparison.
- [x] EffectSummary now projects its existing classification into that shared
  model without changing its serialized contract. Contracts owns the mapping
  so Analyzer can reuse it without depending on the executable tool.
- [x] Removed the manual `System.Type` and `System.RuntimeType` metadata-member
  wrapper catalogs. Generic call-graph inference now owns those classifications;
  six runtime metadata slices characterize the unchanged output.
- [x] Made `SharpProofTarget` the single query-target model across the public
  session API, Symbolic execution, Analyzer, and CLI. Deleted the duplicate
  internal target, enum, validation, and conversion adapter.
- [x] Routed divide, modulo, checked arithmetic, checked update, compound
  assignment, and checked conversion hazards through one operation-level
  candidate adapter. Deleted seven syntax-specific forwarding routes.
- [x] Removed the production `PurityPolicyAuditRegistry`, which duplicated
  analyzer behavior solely for test inspection. The retained audit tests now
  own their expected documentation contract and continue exercising the real
  configuration and precedence paths.
- [x] Made `PurityProofQuery` the only ProofCore hazard-classification request
  path and migrated wrapper-specific tests to it. Removed seven service-specific
  classification entry points, a conversion-only lowering adapter, and four
  unreachable helpers across ProofCore, Symbolic, CLI, and EffectSummary.
- [x] Deleted the 170-line `SymbolicQueryService` pass-through. Analyzer, CLI,
  session, and retained tests now use the same `SymbolicQueryExecutor`; the
  architecture test verifies that the legacy facade type is absent.
- [x] Deleted the 243-line `SymbolicSourceQueryService` facade. Query dispatchers
  now own the program-point, range, and proof components directly; retained
  source-query tests use executor-based helpers in `SharpProof.Testing`.
- [x] Removed the legacy invariant snapshot and implication entry points and
  their duplicate result DTOs. Tests now use `SymbolicProgramPointAnalysis` and
  the canonical condition-proof executor; display and fact merging live with
  their focused projection types.
- [x] Collapsed three runtime-hazard syntax-tree routes into one target-based
  dispatcher path. Removed unreachable source-proof and line-analysis helpers,
  the unused typed hazard wrapper, and a redundant project-session adapter.
- [x] Internalized the concrete public query records behind the existing
  `SharpProofQuery` factories. The session now dispatches simple requests by
  query kind, and the intentional breaking API snapshot no longer exposes eight
  constructor-heavy request implementation types.
- [x] Added one deterministic operation-first runtime-hazard lowering registry
  for arithmetic, index construction, Math.Abs/Clamp, argument guards, and
  switch no-match. Deleted their syntax dispatch branches and forwarding
  adapters; syntax remains only where Roslyn operations do not carry required
  source evidence.
- [x] Moved array/stackalloc lengths, element and Array.GetValue bounds,
  slicing, collection cardinality, and array-store mismatch discovery behind
  the same operation registry. Direct lowerer tests now exercise `IOperation`
  requests instead of compatibility-shaped metadata parameter lists.
- [x] Moved cast, nullable-value, null receiver, dynamic-binding, lock, regex,
  await, with-expression, foreach, and deconstruction hazards into operation
  lowerers. Removed the legacy syntax candidate factory; the renamed 31-line
  source factory owns only multi-descriptor throw/rethrow projection, with one
  explicit Roslyn member-operation gap retained at enumeration.
- [x] Removed the manual string-from-ReadOnlySpan and Path string-slice
  normalization wrapper rules. Generic fixed-point inference reproduces their
  runtime and analyzer classifications; the fresh-copy rule was independently
  shown to remain necessary and was retained.
- [x] Removed the remaining ReadOnlySpan Path search wrapper predicate and its
  16-member call allowlist. Runtime Path characterization proves generic
  fixed-point inference now owns these classifications; invariant casing and
  both string-concat wrapper families were independently shown to remain live
  and were retained.
- [x] Removed the stack-local char-builder and immutable string-rewrite wrapper
  rules after independent PathCore and analyzer string-suite characterization.
  Generic fixed-point inference now owns both classifications and their call
  families are deleted.
- [x] Removed char-scalar projection and guarded string-scan wrapper rules after
  runtime Char-helper and analyzer string-suite parity. Their shared manual
  identity/display parser was unreachable and is deleted with the rules.
- [x] Characterized the remaining string-hash and `System.Type` identity
  wrapper rules independently. Removing either changes its runtime catalog
  slice from `pure` to `conservative_unknown`, and string hashing also regresses
  analyzer string invariants, so both remain explicit semantic owners rather
  than legacy deletion debt.
- [x] Re-audited the CFG program-point collector, loop transfer, and
  EffectSummary assembly-document reader. Each remains reachable and owns
  distinct proof, loop-invalidation, or exception-summary behavior; none is a
  compatibility adapter that can be removed without deleting a feature.
- [x] Removed the remaining substring semantic-wrapper rule after its dedicated
  runtime slice proved generic fixed-point inference preserves the catalog.
- [x] Replaced 332 net lines of handwritten impacted-test path rules and live
  token scans with the generated inventory's exact dependencies, source-root
  ownership, and reverse project closure. Explicit analyzer modules retain
  validated partial selection; inferred production mappings conservatively
  require the full suite, so missing lexical references cannot under-test a
  semantic change.
- [x] Removed the handwritten BCL impurity member catalog for random-number,
  mutable-string, array-mutation, threading, XML, diagnostics, I/O, and assembly
  loading APIs. Generated purity summaries, configured and namespace policy,
  metadata analysis, and conservative BCL fallback now own these decisions.
  Diagnostics remain present and conservative; as an intentional unreleased
  API break, evidence sources and categories may now describe the inferred path
  instead of the deleted semantic-rule name.
- [x] Retired the three-entry handwritten analyzer test-impact manifest and its
  parallel validation, ownership, dependency-closure, and evidence engine. The
  generated repository inventory is now the sole production/project ownership
  source; inferred mappings conservatively require the full suite. Selector
  tests retain every case against the generated path, and no test was deleted.
- [x] Made the requested test lane the single project-routing owner for filtered
  runs. Deleted five handwritten fixture catalogs and the duplicate filter-name
  parser/repartitioner; impacted selection now infers Main versus Tooling from
  the generated fixture inventory. Fixed six-lane filters, worker caps,
  fail-fast settings, profiling, and concurrent execution remain intact.
- [x] Retired the completed raw-SMT migration hotspot inventory. The canonical
  operation-transfer plan records every migration phase and deletion gate as
  complete, while architecture tests enforce the supported session/query/result
  boundary and prevent raw SMT types from entering the public API. No build,
  CI, documentation, or product path consumed the duplicate source scanner.
- [x] Retired the standalone clone and risk inventories plus their stale manual
  clone adjudications. Neither scanner had a build, CI, documentation, or
  product consumer; both duplicated canonical source/architecture metrics, and
  their rules still named the removed `Shared` root and deleted query facades.
  Production metrics and generated test-impact ownership remain enforced.
- [x] Replaced the configuration-reference generator's handwritten C# source
  parser with an internal compiled-registry projection hosted by Symbolic CLI.
  The PowerShell layer now only invokes the bounded dotnet wrapper and applies
  golden-file verification. Registry defaults, scopes, value kinds, allowed
  values, descriptions, samples, and diagnostics produce byte-identical docs.
- [x] Made the full solution build the single release-validation owner. The
  retained release command delegates to `Invoke-SharpProofBuild.ps1 -Full`,
  which now explicitly enables VSIX packaging; focused validation produces the
  NuGet and VSIX artifacts with zero warnings. Removed the superseded
  historical LOC reporter and mutable baseline in favor of this enforced ledger.
- [x] Made `architecture-modules.json` the single module and dependency-graph
  owner for architecture validation, production metrics, and impacted-test
  inference. Removed the test-impact generator's duplicate 19-module table and
  unused generated project graph; selector closure now follows module identities
  directly, and all 40 selector characterizations preserve conservative routing.
- [x] Condensed 20 internal immutable query, witness, capability, complexity,
  budget, cache, and summary carriers around primary constructors. Preserved
  class identity and normalization semantics, and made the two witness property
  orders that mix stored and computed values explicit so CLI JSON remains
  byte-identical instead of depending on compiler metadata order.
- [x] Removed the condition-proof, source-query, and runtime-hazard dispatcher
  objects plus their duplicate validated-request carrier. The query executor now
  owns orchestration directly, and one immutable query context flows through
  source, range, program-point, hazard, capability, and complexity execution
  instead of being expanded into parallel option parameter lists. Architecture
  coverage prevents all four superseded adapter types from returning.
- [x] Removed ExceptionSummaryCatalog's unreachable second exception-fact graph.
  The live `SummaryExceptionInfo` and structured edge projection remains the sole
  owner consumed by diagnostics; focused source/edge tests preserve direct and
  transitive evidence, and architecture coverage prevents the duplicate fact
  model and parser from returning.
- [x] Replaced the separate array-backed and span-backed by-ref-like wrapper
  rules with one ownership-aware construction inference path. A differential
  snapshot preserved all 34,519 CoreLib purity classifications exactly, focused
  runtime slices preserve Span and MemoryMarshal behavior, and architecture
  coverage prevents the specialized manual predicates from returning.
- [x] Made `SymbolicQueryMetrics` the single aggregate count owner for source
  queries and CLI gates. Removed four independently calculated summary graphs
  and condensed condition-proof projection/copying to immutable leaves; exact
  compact, full-JSON, explain, text, package-consumer, and semantic aggregate
  tests preserve output and conservative proof behavior.
- [x] Made public `SharpProofAnalysisBudget` the sole analysis-limit model for
  sessions, Symbolic, Analyzer, project configuration, and CLI. One named
  registry now applies all eleven CLI overrides and infers analyzer-config keys;
  the duplicate engine limit class and manual truncation-code table are deleted.
  Unknown limit kinds remain explicit and architecture coverage prevents the
  parallel model from returning.
- [x] Removed the remaining built-in impurity namespace, type, and member tables.
  Generated purity summaries, configured overrides, semantic classifiers, and
  the conservative BCL shape fallback now own metadata classification. Deleted
  the `PurityCatalogSemantics` forwarding layer; SP0002 diagnostics remain
  present while evidence identifies the inferred fallback instead of a manual
  catalog hit, and architecture coverage prevents the tables from returning.
- [x] Made the exported code-fix provider the sole diagnostic dispatch owner.
  Deleted the handler interface, six one-method handler adapters, the family
  enum, and the parallel handler registry. The provider retains the exact
  fixable diagnostic set, titles, equivalence keys, and family behavior through
  direct dispatch; architecture coverage prevents both adapter files from
  returning.
- [x] Derived analyzer diagnostics from their descriptor fields and suppression
  configuration from the suppressor specifications. Deleted the manual
  descriptor list, test-only feature/configuration metadata, and two duplicate
  suppression-ID lists; focused descriptor, configuration, and suppression
  tests preserve discovery, ordering, documentation, and proof-backed behavior.
- [x] Made one unknown-reason taxonomy own proof, capability, complexity,
  runtime-hazard, ensures, and purity classification. Deleted the parallel
  domain dictionaries, consolidated raw-reason precedence, and condensed the
  related immutable proof/result metadata. Exhaustive enum, stable-code, JSON,
  retry/configuration, and architecture tests preserve conservative fallbacks.
- [x] Replaced 164 manually enumerated Span/MemoryMarshal, DateTime, and
  DateTimeOffset effect-summary roots with five family prefixes plus one
  specialized exclusion. Full callee analysis preserves all 325 preexisting
  generated catalog entries exactly and now infers 589 additional entries; the
  narrow `System.Type` list remains explicit because broadening it changed
  reflection diagnostics. A repository test prevents the three migrated
  families from returning to member-by-member maintenance.
- [x] Removed the dormant manual-catalog comparison normalization and
  aggregation engine after the inferred-purity migration made all three input
  catalogs immutable empty sets. The serialized comparison contract still
  emits its three empty arrays, generated-purity catalog construction remains
  intact, and architecture and packaging tests prevent the retired normalizers
  from returning or the boundary shape from drifting.
- [x] Removed ProofCore's general manual string-shape reconstruction before SMT.
  Exact Z3 string constraints now own length, contains, prefix, suffix, and
  overlap consistency. Retained only a narrow same-length concrete-value bridge
  needed to validate .NET regex semantics conservatively; focused regex and
  string tests preserve all outcomes, and architecture coverage prevents the
  duplicate shape engine from returning.
- [x] Removed the pre-release baseline migration and recursive compatibility
  readers from both the Baseline tool and analyzer. Current version-2 documents
  now use one canonical `diagnostics` array and exact entry field names;
  unversioned, additive-v1, nested-group, alias, and arbitrary-object traversal
  paths are rejected. The `migrate` command remains as a current-schema
  validator/normalizer, and focused analyzer, CLI, documentation, and
  architecture coverage preserves current SARIF and baseline behavior.
- [x] Made one immutable tree-configuration snapshot flow through the common
  method analysis context. Purity, suggestions, exception reporting,
  nullability, and diagnostic suppression no longer re-read AnalyzerConfig
  through feature-specific adapters. Registry value kinds now drive one
  validation path, and focused configuration tests preserve defaults, aliases,
  tree overrides, invalid-value diagnostics, and suppression behavior.
- [x] Consolidated the remaining constructor-only analysis carriers around
  primary constructors and immutable records. Symbolic source inputs, SMT
  lifecycle snapshots, analyzer method/purity contexts, effect-summary trust
  and IL contexts, exception-flow results, and BCL fallback shapes no longer
  duplicate parameter, assignment, and property ownership. Property names,
  normalization, JSON evidence, cache behavior, and fallback classifications
  remain unchanged; architecture coverage prevents the copy-constructor forms
  from returning.
- [x] Made `SharpProof.Tooling.Core` the single command-line argument cursor,
  typed value parser, and option-dispatch owner for Symbolic CLI, EffectSummary,
  Fuzz, Baseline, and CorpusReport. Deleted five independent loops, four value
  readers, Symbolic's private cursor/enum parser, and repeated host/output
  plumbing. Exact help, validation errors, exit codes, text, JSON, SARIF, and
  package behavior remain characterized; architecture coverage prevents local
  argument loops from returning.
- [x] Replaced four manually enumerated file and stream capability-member
  catalogs with type-family and operation-name inference. New framework file
  APIs such as `File.GetUnixFileMode` now classify without catalog maintenance;
  unrecognized types and operations still retain explicit conservative unknown
  behavior. Focused capability and architecture tests preserve established
  classifications and prevent the deleted member catalogs from returning.
- [x] Deleted the broad post-CFG purity compatibility traversal that rechecked
  using, foreach, throw, try, invocation, and operator behavior after canonical
  CFG analysis. Characterization moved only Roslyn's implicit try/catch/finally,
  disposal, enumerator-runtime-member, and compound-operator semantics into CFG
  finalization; ordinary throws and invocations now have one analysis path. The
  disposal rule no longer recursively rechecks resources and bodies already
  visited by CFG, and architecture coverage prevents the compatibility pass
  from returning.
- [x] Made Roslyn's formatter the single whitespace and indentation owner for
  moving misplaced Requires attributes onto property and indexer getters.
  Deleted the code-fix provider's manual leading/trailing trivia walkers,
  expression-getter builder, accessor-brace formatter, indentation reconstruction,
  and line-break preservation forest. Exact expression-bodied, existing-getter,
  target-alias, and comment-preservation fixes remain unchanged; no test was
  deleted, and architecture coverage prevents the manual formatter from returning.
- [x] Made one declarative analyzer catalog the source owner for all 75
  diagnostic descriptors. Deleted eleven constructor-heavy partial declaration
  files and four descriptor factory layers while retaining direct fields for
  Roslyn release tracking and reflection discovery. A canonical metadata hash
  matches the pre-refactor assembly exactly across IDs, titles, messages,
  categories, severities, enabled defaults, descriptions, help links, and tags;
  repository coverage prevents split declaration files and factories from
  returning.
- [x] Made the canonical visible operation tree the exception-flow owner for
  invocations, object creation, property access, user-defined operators and
  conversions, and interpolated-string-handler construction. Deleted the
  parallel syntax enumerators and the property-flow partial, and moved exact
  receiver-type resolution to the concrete-receiver owner. Constructor
  initializers, using disposal, foreach runtime members, and resolved local
  delegates remain explicit compiler-boundary projections because they are not
  all represented by the method-body operation root. Focused characterization
  covers source and EffectSummary constructor initializers plus handler
  construction, and architecture coverage prevents the removed syntax paths
  from returning.
- [x] Made primary constructors the single initialization owner for internal
  program-point, invariant, query, runtime-hazard, error, and project-context
  result carriers. Deleted the analyzer's duplicate project-analysis context;
  Symbolic CLI now consumes `SymbolicProjectQueryContext` directly and owns only
  its analyzer-diagnostic and configuration-issue projections. Property order,
  validation, diagnostics, exact CLI JSON, and conservative unknown/truncation
  behavior remain unchanged, and architecture coverage prevents the adapter
  and assignment-heavy carrier forms from returning.
- [x] Made project-level global imports the single owner for stable Roslyn,
  immutable-collection, symbolic IR, SMT, configuration, and analyzer-engine
  namespaces shared across Analyzer and Symbolic. Removed 1,105 repeated using
  directives from 314 production files without changing any code statement;
  architecture coverage prevents those project-wide imports from returning to
  individual files.
- [x] Completed project-level import ownership across Analyzer, Attributes,
  Contracts, ProofCore, Symbolic, Symbolic CLI, CorpusReport, EffectSummary,
  and Fuzz. Removed another 227 repeated namespace and alias directives from
  157 production files while keeping static imports local to avoid changing
  name binding. The architecture test now derives each project's owned imports
  from its global-using file and rejects local duplicates. Also deleted the
  `SymbolicProgramPointAnalyzer` forwarding adapter: `SymbolicInvariantService`
  now owns program-point analysis directly, and source/proof consumers bind to
  that canonical service. Source-map and invariant-summary initialization were
  consolidated without changing locations, evidence, or unknown behavior.
- [x] Made primary constructors the single initialization owner for 29 more
  internal Analyzer, ProofCore, Symbolic, CLI, and EffectSummary carriers.
  Removed repeated constructor parameter, assignment, and property blocks from
  catalog entries, exception summaries, analysis requests, interval/affine
  facts, lowering transitions, SMT options, complexity expressions, source
  contexts, and traversal frames. Normalization, null validation, equality,
  lazy hashing, JSON field names, and conservative unknown states remain
  unchanged. Order-sensitive CLI invariant projection retains its explicit
  constructor, and architecture coverage prevents the consolidated carrier
  forms from regressing.
- [x] Moved Symbolic CLI, Fuzz, and EffectSummary help documents out of C#
  option/host implementations and into explicitly named embedded resources.
  One tooling-core loader now owns resource validation, UTF-8 decoding, and
  terminal-newline normalization. Pre-change and post-change help text hashes
  are identical for all three tools, packaging assertions consume the combined
  code/resource owner, and architecture coverage locks the exact resource bytes
  while preventing the inline help blocks from returning.
- [x] Made one embedded JSON catalog the metadata owner for all 75 analyzer
  diagnostics. Public descriptor fields now bind by field identity through a
  validating immutable loader instead of repeating constructor-heavy metadata
  in C#; supported diagnostics are derived without reflection. A canonical
  runtime hash locks IDs, field names, titles, messages, categories,
  severities, enabled defaults, descriptions, help links, and tags to the
  pre-refactor assembly, while release-table and architecture tests replace
  the syntax-only RS2002/RS2003 discovery that cannot inspect data resources.
- [x] Made one embedded schema the runtime and documentation owner for all 45
  analyzer configuration options. Removed the constructor-heavy C# option
  table, three parallel documentation switches, and the single-use SMT mode
  registry; project configuration retains an explicit conservative parser at
  its boundary. The schema validates duplicate keys and enum metadata, its LF
  resource hash is locked, and the generated configuration reference remains
  byte-identical to the pre-refactor document.
- [x] Finished the preview session API's immutable query projection shape.
  `SharpProofTarget` and all five typed payloads are positional records, while
  one internal projector owns conversion from engine results and common
  metadata. The intentional breaking snapshot now describes the construction
  surface directly; all eight query kinds, concurrent caching, typed failures,
  cancellation retry, and package consumers retain their characterized
  behavior.
- [x] Split the exported code-fix provider into a thin 182-line coordinator,
  attribute-edit handlers, and inferred-contract handlers while retaining its
  declarative simple-removal registry and single MEF export. CodeFixes now uses
  the repository C# 12 level and project-owned global imports; fixable IDs,
  formatting, packaging, VSIX loading, and fix-all behavior remain unchanged.
- [x] Removed four declaration-only policy/projection helpers found by an exact
  repository-wide symbol audit: obsolete SMT-mode default construction, an
  unused array-enumerator special-case wrapper, and two unused EffectSummary
  freshness/visibility renderers. Architecture coverage prevents these retired
  entry points from returning while the live inference rules remain unchanged.
- [x] Made the bounded per-program-point reachability cache the sole cached CFG
  state owner. Deleted the second execution-root trace, observation replay,
  evidence rebasing, cache holder, and trace-specific fallback adapter while
  retaining seeded statement completion in the canonical CFG transfer path.
  Focused state parity covers branches, returns, method-entry evidence,
  concurrent roots, custom budgets, cancellation, and conservative loop
  fallback; architecture coverage prevents the duplicate trace cache from
  returning.
- [x] Made immutable `SymbolicProofInfo` the single internal proof-result and
  projection owner. Deleted the nested IR proof wrapper, status-projection
  struct, solver-pipeline adapter, and four reachability-service forwarding
  methods; the proof service now owns raw solver execution and mapping directly.
  Raw results, typed unknown reasons, stage/support metadata, budgets, cache
  evidence, and serialized property order remain characterized, while
  architecture coverage prevents the three parallel proof layers from
  returning.
- [x] Made public `SharpProofError` and its typed category the single error
  contract across the preview session API, Symbolic executor, Analyzer, and
  CLI. Deleted the parallel internal error class, category enum, validation
  graph, and session conversion adapter. Exact JSON category text, retry
  metadata, exit codes, exception classification, and analyzer fallback remain
  characterized, and architecture coverage prevents the duplicate model from
  returning.
- [x] Replaced the fresh-mutable local all-path statement walker with canonical
  symbolic freshness and alias facts. Object-typed aliases now use acquisition
  provenance instead of a static-type prefilter, while disposable lifetime
  ownership remains a separate semantic domain. Deleted the parallel syntax
  branch/assignment evaluator; focused object, delegate, lambda, local-function,
  and resource-lifetime coverage plus an architecture guard preserve the split.
- [x] Made one validated embedded registry the declarative owner for all 70 fuzz
  shapes, expectations, manifests, and deterministic source templates. The C#
  generator now retains only the five genuinely randomized builders. Exact
  metadata and four-variant source bytes remain hash-identical, the broader
  fuzz/shape suite stays green, and architecture coverage prevents the inline
  registry and template block from returning.
- [x] Made one validated embedded registry the declarative owner for the 22
  ordered EffectSummary generated-purity overrides. The first-match projection
  and the two semantic predicates remain code; exact symbols, prefixes,
  categories, visibility, and predicate identities now have one data owner.
  Representative pure, impure, prefix, and predicate runtime slices preserve
  their classifications, and architecture coverage prevents inline tables from
  returning.
- [x] Made the typed symbolic pattern dispatcher the only `is`-pattern lowering
  route. Deleted seven expression-level retry paths and their binary, unary,
  null, constant, relational, recursive, type, and type-test adapters; the
  retained term-level matchers still own normalized relation construction.
  General unary composition now runs after specialized negation matchers so
  direct inverted facts and declaration bindings remain unchanged. The full
  177-test pattern slice and an architecture guard characterize the boundary.
- [x] Converted 26 identity-insensitive Symbolic projection, witness, budget,
  capability, complexity, lifecycle, and summary DTOs to immutable positional
  records. Removed duplicated constructor-to-property assignment while keeping
  derived unknown and truncation metadata. Explicit JSON ordering and ignored
  internal span coordinates preserve compact, full, and explain output bytes;
  architecture coverage prevents the assignment-heavy forms from returning.
- [x] Made `RuleRegistry` the sole operation-kind dispatch owner for purity
  analysis. Deleted `IPurityRule`, the unused session rule list, per-rule
  applicable-kind declarations, the runtime list-to-dictionary adapter, and
  the declarative-pure rule object. The immutable handler map preserves ordered
  first-owner selection, typed rule behavior, evidence names, and fuzz-shape
  coverage without duplicating operation ownership in every rule.
- [x] Made one resolved method-like target the shared input for capability and
  complexity queries. Deleted both feature-specific target models, their
  parallel source-span/symbol/body resolution, and the generic target-factory
  plumbing while preserving feature-specific display names and declaration
  kinds. Byte-level compact CLI fixtures guard both result shapes.
- [x] Made one stateless core-operation policy owner and one typed registry
  boundary replace seventeen one-method purity-rule dispatch shells. Array,
  inline-array, and null-test operations now use the canonical child traversal;
  the remaining policies preserve their exact evidence-source names and typed
  behavior. The generic rule-base adapter is deleted, and architecture coverage
  prevents the retired shells from returning.
- [x] Made Contracts the single baseline and proof-evidence schema owner.
  Analyzer configuration, additional-file validation, the Baseline tool, the
  Symbolic CLI, and EffectSummary now share immutable baseline DTOs, schema
  validation, path normalization, deduplication, identity matching, and
  diagnostic-property projection. Deleted the Analyzer JSON reader and
  diagnostic-property adapter, the Symbolic evidence-schema facade, and the
  remaining generic JSON property reader while preserving baseline JSON, SARIF,
  suppression, validation-error, and CLI behavior.
- [x] Made `SymbolicSmtDiagnostics` the immutable SMT diagnostics snapshot
  instead of forwarding every property through a second snapshot record, and
  converted program-point analysis to one immutable record with explicit JSON
  exclusions for engine-only state. Focused lifecycle, hazard, limit, and
  program-point tests preserve solver health, evidence, truncation, and query
  behavior; architecture coverage prevents the duplicate snapshot type from
  returning.
- [x] Made `EffectSummaryCatalog` the single analyzer owner for generated purity
  and exception summaries. Deleted the parallel exception catalog, its duplicate
  parser and entry graph, the generic one-consumer entry-map/base-entry stack,
  and the generic catalog-loader adapters. Purity and exception lookups retain
  their distinct identity policies while sharing immutable entries, trust
  metadata, source precedence, and one session-scoped catalog. Focused parsing,
  trust, exception-edge, packaging, and architecture tests preserve tolerant
  malformed-input behavior and prevent all three superseded catalog paths from
  returning.
- [x] Replaced ProofCore's broad syntactic proof classifier with the canonical
  concrete-fact preprocessor already used before Z3 encoding. The retained
  `SmtConcreteFactIndex` owns only bounded alias, interval, Boolean, string, and
  reference facts; the parallel hazard classifier, its separate node budget,
  classifier-only branch probes, and the one-consumer conditional simplifier
  are deleted. Direct solver/preprocessor tests and all four SMT-heavy lanes
  preserve contradiction, zero-timeout, opaque-operation, hazard-reason, and
  conservative fallback behavior.
- [x] Made the canonical EffectSummary document and progress DTOs own both JSON
  writing and reading. Artifact-source selection, resumable generated-catalog
  merging, and both progress formats now deserialize their typed models instead
  of maintaining three parallel `JsonDocument` walkers. Structural method
  identity gained an exact immutable JSON constructor, while explicit property
  ordering preserves existing summary bytes. Focused schema, artifact-spec,
  resume, reviewed-category, sharding, and architecture tests cover the boundary.
- [x] Removed the analyzer's disposable EffectSummary JSON document facade and
  its separate assembly/method layout model. A focused parser now owns only
  current-schema validation and structured validation failures; the unified
  `EffectSummaryCatalog` directly owns tolerant purity and exception-evidence
  traversal. Architecture coverage prevents both forwarding layout types from
  returning.
- [x] Re-characterized invariant TextInfo casing and length-checked string
  concatenation independently against their runtime slices. Generic fixed-point
  inference currently changes each from pure to impure when its rule is removed,
  so both remain explicit semantic owners rather than legacy deletion debt.
- [x] Made the recursive purity engine the single interprocedural analysis
  pipeline. Deleted the whole-compilation `CallGraphBuilder`, its parallel
  operation/delegate/dispatch traversal, and `WorklistPuritySolver`; the
  compilation service now memoizes canonical recursive results and resolves
  cross-tree semantic models through one bounded cache. Cache, concurrency,
  recursion, dispatch, and metadata tests retain every case through typed seams,
  and architecture coverage prevents the prepass types and fields from returning.
- [x] Replaced the fuzz coverage manifest's parallel Roslyn operation, syntax,
  and analyzer-action tables with inference from the fuzz registry, analyzer
  rule registry, Roslyn enums, and stable kind families. The run summary is now
  one immutable positional record, and diagnostic counts share one keyed
  accumulator. Focused manifest, deterministic corpus, smoke-fuzz, JSON
  round-trip, and architecture tests preserve coverage artifacts and output.
- [x] Made Contracts own the current EffectSummary method, purity, exception,
  artifact-source, and assembly DTOs plus structural-identity validation.
  Analyzer catalog loading and additional-file validation now consume that
  typed boundary; deleted both analyzer-local JSON property/identity walkers
  while preserving per-entry malformed-input isolation, trust metadata, and
  serialized schema behavior.
- [x] Made `MethodAnalysisSnapshot` the sole analyzer method-analysis carrier
  and the analyzer the sole owner of nonfatal query-to-unknown conversion.
  Deleted the preceding request/input carriers, the test-only semantic-count
  projection, six symbolic `Try*` execution adapters, their generic result
  wrapper, and the one-method complexity service. Exception flow, contract
  proofs, capability/complexity caching, CLI error classification, and public
  session results retain their characterized behavior.
- [x] Removed the remaining analyzer and SMT test-only execution adapters.
  Cache identity and concurrent single-execution tests now assert observable
  results and factory counts directly, while implication tests consume the
  canonical typed proof outcome. `PurityEvidence` is an immutable record value
  and purity-state updates use native `with` copies instead of a nullable
  nine-argument copy adapter. Focused cache, SMT, purity-state, diagnostic-
  evidence, and architecture coverage preserves semantics and prevents the
  instrumentation paths from returning.
- [x] Removed test-only proof, reachability-cache, condition-truth, list-pattern,
  invariant-filter, corpus, and fuzz entry points from production assemblies.
  Tests now use canonical typed queries, observable cache identity/eviction,
  direct ProofCore sessions, and test-owned orchestration. The duplicate
  Symbolic proof-session interface/forwarder, result factory, and source-file
  facade are deleted; immutable source and SMT option carriers use native
  record copies. Architecture coverage prevents the retired seams from returning.
- [x] Removed the unreleased public `SharpProofDiagnostics` descriptor and ID
  facade. The diagnostic catalog is now the sole descriptor owner, tests assert
  literal external IDs and property bytes, and only 17 genuinely shared
  evidence keys remain internal. The canonical descriptor metadata hash,
  supported-diagnostic ordering, code fixes, fuzz projections, and SARIF
  evidence remain unchanged; architecture coverage prevents the facade from
  returning.
- [x] Removed the remaining unreleased query, target, structural-identity, and
  evidence-schema compatibility adapters. One constructible `SharpProofQuery`
  and `SharpProofTarget` record now carry every query shape, including hazard
  options; structural method lookup uses one canonical key; and serialized
  contracts use one numeric evidence version without a redundant compatibility
  token. Test-only target builders retain concise fixture setup, the intentional
  preview API and byte snapshots are updated, and architecture coverage prevents
  the retired factories, fallback keys, and compatibility constants from
  returning.
- [x] Consolidated the remaining BCL invocation purity exceptions around the
  canonical invocation operand traversal. Enum, Boolean, and IPAddress parsing
  plus array/span view recognition now classify only after the common receiver
  and argument checks; deleted their duplicate traversal adapters. Type, Enum,
  and FormattableString early-lowering exceptions now share one declarative
  member table and one operand checker, and the LINQ source path no longer keeps
  empty compatibility branches. Focused parsing, collection-view, string,
  networking, and reflection tests preserve the characterized semantics.
- [x] Deleted the nullable `SymbolicIrLowerer.Boundary` compatibility layer.
  The canonical typed `SymbolicSemanticPipeline` now calls specialized pattern,
  reference, string, nullable, indexing, and operator lowerers directly, while
  the two recursive IR entry points live with `SymbolicIrLowerer`. Internal
  string-length, member, pattern, reference, and nullable lowering now consume
  the underlying `TryLower*` contracts without converting through nullable
  adapters. The impacted-test inventory was regenerated after removing the
  production file.
- [x] Reset the unreleased Symbolic public API history. `PublicAPI.Shipped.txt`
  now contains only its nullable-mode header, and `PublicAPI.Unshipped.txt`
  contains the current preview API without 494 mirrored removal tombstones.
  Packaging coverage now prevents a fictional shipped surface or preview
  tombstones from returning. This removed 988 tracked snapshot lines without
  changing package identities or runtime behavior.
- [x] Migrated nine of eleven custom analysis-budget families onto the CFG
  program-point collector. Seeded queries, current-completion queries,
  finally-local targets, and for-loop initial-entry queries now retain CFG
  state and truncation semantics under non-default path, guard, merge,
  foreach, and null-depth limits. The structural fallback remains only for the
  two limits whose behavior is not yet represented by CFG joins: try-fact
  merging and scoped-block completion. Record equality now owns the cache's
  default-budget check instead of an eleven-property comparison.
- [x] Removed EffectSummary's remaining one-consumer classification and ECMA
  lookup adapters. Reviewed-entry validation now has one path; call purity has
  one symbol-based predicate; date arithmetic uses the shared type/member
  classifier directly; validation helpers use one predicate; and field,
  method, and runtime-type lookups share one generic-erasing type-specification
  decoder. Exact metadata keys and classification decisions remain unchanged.
- [x] Replaced nine immutable result, cache-key, dispatch-shape, source-location,
  regex, EffectSummary trust, and exception-resource carriers with record-owned
  state. Runtime-hazard JSON order is now explicit rather than declaration-order
  dependent, and the removed constructor/property forwarding cannot return
  unnoticed. Cache identity, proof metadata, and serialized output remain exact.
- [x] Extended record-owned state across analyzer configuration, EffectSummary
  identity/trust metadata, exception evidence, symbolic project configuration,
  and tool result carriers. Architecture coverage prevents those constructor-to-
  property copy layers from returning; validation, evidence ordering, and
  serialized property order remain unchanged.
- [x] Migrated the final two custom analysis-budget families to the canonical
  CFG program-point collector. Completed try/catch regions now use the shared
  exception-region transfer and scoped completed blocks enforce their statement
  budget during CFG traversal, so custom budgets no longer route queries to the
  structural fallback. Exception-flow attribute identity is now session-owned
  rather than stored in ambient `AsyncLocal` state, and the remaining small
  constructor-only carriers use record or primary-constructor ownership.
- [x] Replaced EffectSummary's branch-per-opcode decoding for the ECMA-defined
  `ldc.i4.m1` through `ldc.i4.8`, `ldloc.0` through `ldloc.3`, and `stloc.0`
  through `stloc.3` families with validated contiguous-range decoding. The
  short and wide operand forms remain explicit, while runtime StringComparer,
  same-assembly static-field, and full tooling coverage preserve tracked IL
  constants, locals, and stable-identity evidence.
- [x] Made source-query result filtering a CLI-owned concern instead of an
  engine request contract. Deleted the constructor-heavy 18-field
  `SymbolicSourceQueryFilter` and its query-option plumbing; the CLI now applies
  the same normalized typed predicate after canonical query execution, while
  result aggregation still recomputes every filtered summary. Also removed an
  unreachable property-getter cache branch and inferred indirect-write and
  operand-size IL families from opcode metadata. Architecture coverage prevents
  the filter adapter from returning.
- [x] Removed the ambient Symbolic CLI console/filesystem host that existed only
  for reflection-based in-process tests. CLI tests now exercise the real
  executable, including stdin and JSON requests; production uses ordinary
  process console streams and working-directory path resolution. Also removed
  one unused preview Fuzz expectation convenience method.
- [x] Consolidated nullable contract metadata traversal into one current/original
  symbol stream, including property getter and setter-input projection. Removed
  the second invocation/property/field expression-contract pass after Roslyn
  nullable-flow characterization proved it redundant; exact contract queries
  still retain their explicit conservative path.
- [x] Canonicalized 223 trivial one-return production methods as expression
  bodies across Analyzer, Symbolic, ProofCore, Contracts, CodeFixes, and tools.
  The rewrite is limited to the exact four-line single-return shape; an
  architecture test prevents block-bodied forwarding shells from accumulating
  again without compressing multi-step domain logic.
- [x] Made the canonical baseline and EffectSummary loaders own additional-file
  validation results. Deleted the 241-line `AnalyzerAdditionalFileValidator`
  and its second JSON traversal; one session-owned issue accumulator now
  receives schema, malformed-entry, unreadable-file, and stale-identity results
  from the same parsed documents used by suppression and metadata analysis.
  Exact SP0032 reason strings and reason codes remain characterized, and
  architecture coverage prevents the parallel validator from returning.
- [x] Removed the final production facade for the retired handwritten BCL
  catalogs. Absence assertions now use a test-owned fixture, so package and
  production assemblies no longer expose five permanently empty catalog sets.
  Also removed two one-property cache-holder adapters: the bounded condition-
  truth cache and nested program-point cache now live directly in their
  compilation-scoped weak tables. Catalog, cache, and architecture coverage
  preserve observable behavior and prevent all three wrappers from returning.
- [x] Moved normal prior expression-statement completion onto the canonical CFG
  collector. Framework postconditions, mutation invalidation, completion facts,
  and evidence now replay through the same operation traversal as assignments;
  non-returning calls and guard-invalidating shapes remain explicit structural
  fallbacks. The focused fallback characterization dropped from 92 routed cases
  to 74 while preserving all 659 symbolic results.
- [x] Made one immutable program-point metadata value the owner of source,
  target, span, method, and requested-position identity. Program-point results
  and their condition proofs now share that value instead of copying nineteen
  constructor parameters and properties through a second projection layer.
  Flat CLI JSON properties, ordering, source selection, and proof evidence
  remain unchanged.
- [x] Made one ordered parameter map the owner of impacted-test wrapper
  invocation. Suggested commands and executed commands no longer rebuild the
  same configuration, lane, worker, profiling, timeout, and memory options in
  parallel. Removed the single-item fixture and base-reference pass-through
  helpers; selection, evidence, JSON, filters, and execution remain unchanged.
- [x] Made one lane catalog the owner of main, SMT-shard, general, and tooling
  project/filter routing. Removed eight parallel lane variables and three
  repeated routing matrices; every established lane still executes its exact
  historical fixture count. Complexity driver and callee records now own their
  all-field equality instead of parallel string-key builders.
- [x] Made compilation-scoped method analysis state own syntax-node condition
  proofs and conservative error projection directly. Deleted the two
  `SymbolicQueryExecutor` forwarding overloads and both nullable-contract proof
  adapters; ensures and nullable analysis now call the same state-owned proof
  boundary. Unsupported requests remain explicit conservative unknowns.
- [x] Moved conditional, nullable-coalescing, short-circuit, and compound
  captured-expression current completion onto the canonical collector.
  Intermediate captures and embedded branch values are traversed without
  becoming the requested program point; the owning expression is re-resolved
  once through Roslyn's source semantic operation and then uses the existing
  assignment/update transfer. Operation-level `IIsNull` now lowers nullable
  operands through the shared `HasValue` IR while retaining reference null-test
  semantics. Differential state and evidence parity rejected capture skipping
  without source-operation resolution.
- [x] Made non-returning expression completion canonical. The shared
  `[DoesNotReturn]` exit classifier now marks the expression's normal successor
  contradictory inside operation transfer, so CFG merging prunes it without a
  scheduler-specific branch. Partial, unconditional, and all-branch exit cases
  match structural state and evidence; `[DoesNotReturnIf]` normal completion
  remains unchanged. CFG merging now also preserves contradiction when every
  incoming path is impossible.
- [x] Removed ProofCore's three remaining feasibility-only solver adapters.
  Tests now consume the same witness-capable satisfiability and path/hazard
  checks as production and project only the feasibility enum they assert. The
  duplicate path-and-impurity solver execution, implication wrapper, raw
  satisfiability helper, and their dead private tail are deleted; focused SMT,
  semantic-oracle, and architecture coverage preserves every result and
  prevents the parallel entry points from returning.
- [x] Removed the test-only cross-service SMT context recycling protocol.
  Production transient and permanent failures already recycle only the
  service-owned current-thread solver session, so the manual recycle API,
  scope/result DTOs, global generation, stale-session branch, and health-reset
  tail had no product caller. Retargeted lifecycle tests preserve automatic
  retry, local/shared cache behavior, service isolation, and disposal coverage;
  architecture coverage prevents the unused global protocol from returning.
- [x] Reduced the production-metrics script to its enforced architecture and
  line-counting responsibilities. Deleted the unconsumed top-file, oversized-
  file, and partial-type pressure reports plus their thresholds and source
  parser. The reduction gate still receives exact handwritten file/line counts,
  and architecture tests still require zero unassigned files, ambiguous owners,
  or dependency violations; script coverage prevents the dormant reports from
  returning.
- [x] Moved expression-statement flow captures onto canonical CFG transfer.
  Non-target captures are now treated as ephemeral CFG plumbing, while the
  owning expression is re-resolved once through Roslyn and replayed through the
  existing assignment, coalescing, mutation, and completion transfer. Six
  conditional, short-circuit, coalescing, conditional-access, nested-branch,
  and sole-surviving-branch cases preserve normalized state and evidence. The
  superseded capture-reference invalidation branch is deleted; control-flow and
  initializer captures remain conservative fallbacks.
- [x] Moved local-declaration initializer flow captures onto canonical CFG
  transfer. Conditional, short-circuit, coalescing, and conditional-access
  initializers now re-resolve their source declaration once and reuse the same
  typed assignment transfer as uncaptured declarations. Differential normalized
  state and evidence checks preserve the structural behavior; captures that own
  control flow outside expression and declaration statements remain explicit
  fallbacks.
- [x] Made the canonical CFG collector own non-linear `for` initializer entry.
  Initializer CFG plumbing is bypassed to the real loop header, where the
  original declaration or assignment sequence is transferred exactly once.
  This preserves conditional, coalescing, throw-guarded, and prior captured-
  declaration state and evidence while deleting the linear-prefix gate and the
  per-operation initializer support and completion adapters. Calls, compound
  updates, missing conditions, nested loop prefixes, and external member writes
  remain explicit conservative fallbacks.
- [x] Made declaration current-completion conservative inside the canonical CFG
  collector even when an initializer value cannot be lowered. The canonical
  path now performs the same nested invalidation, target-reference removal, and
  normal-completion projection as prior-statement transfer. Removed the now-
  constant unsupported-value switch; guarded unsupported declarations remain
  explicit fallbacks because their branch facts cannot be safely detached.
- [x] Made the canonical completion entry point own root and nested block
  sequencing. Removed the program-point collector's direct merge of both
  structural collectors, the parallel completed-block transfer method, and the
  root/nested shape gates. Calls, loops, try/catch, and abrupt completion now
  preserve differential normalized state and evidence through one completion
  route; blocks that mutate their entry guard remain explicit conservative
  fallbacks.
- [x] Consolidated Analyzer execution visibility around one evaluation-branch
  resolver, one condition-truth classifier, one reference-null classifier, and
  direct switch proof-status projection. Removed the parallel guarded-body
  shape switch, true/false/null forwarding layers, and constant-comparison
  wrappers while preserving version-aware path facts, guard-mutation safety,
  constant-switch goto behavior, and conservative SMT outcomes.
- [x] Made one immutable normal-completion model and collector own return,
  expression-body, and reachable body-end discovery for both nullable
  postconditions and `[Ensures]`. The same completion component now owns
  old-value-aware proof dispatch as well; duplicate completion DTOs, traversal,
  result-shape helpers, and proof branching are deleted while diagnostic policy
  and evidence remain feature-owned.
- [x] Canonicalized production C# with a repository-enforced Roslyn style
  policy. The formatter replaced remaining single-expression member shells,
  eligible constructors, switch statements, using blocks, and initializer
  scaffolding with native language forms, then normalized production code to
  same-line opening braces. The policy is scoped away from tests and enforced
  by warning-as-error builds plus an architecture assertion; the transformation
  changes no diagnostics, proof outcomes, serialized bytes, or package content.
- [x] Finished same-line opening-brace normalization across the remaining 213
  maintained production C# files. This removed 3,222 physical lines without
  changing a statement or test. The byte-sensitive Fuzz and VSIX source
  templates remain deliberately Allman-formatted inside their string literals;
  focused hash fixtures and the complete Tooling lane verify their exact bytes.
- [x] Consolidated ProofCore formula comparison and regex descendant validation
  around the immutable formula records and the canonical iterative traversal.
  Removed the parallel structural-equality node switch and two recursive regex
  descendant switches while retaining polarity-sensitive validation as the
  independent semantic owner. Focused ProofCore and regex coverage preserves
  structural rewrite detection, unsupported options, approximate-regex
  conservatism, and nested conditional handling.
- [x] Made operation transfer own conservative straight-line computed-update
  invalidation when no exact prior numeric value is available. One unwrapped
  expression operation now dispatches deconstruction, coalescing, simple, and
  computed assignments; custom analysis budgets also attempt the canonical CFG
  route before structural fallback. Guarded updates remain explicit fallbacks
  when invalidation would discard a live branch condition. The complete CFG
  differential fixture preserves state, evidence, versions, and fallback cases.
- [x] Moved `[Requires]` call-site verification into the common method analysis
  snapshot. The feature now scans the snapshot's execution-root-bounded visible
  operations and uses its session-owned proof engine and active analysis budget.
  Deleted the separate ten-kind Roslyn operation-action registration, per-call
  query executor, source reconstruction, and options adapter. Contract aliases,
  inherited contracts, and the complete analyzer SMT lane preserve diagnostics,
  proof outcomes, and conservative unknowns.
- [x] Deleted the return-specific delegate capture traversal. Returned values are
  already classified through `CheckSingleOperation`, whose delegate rule unwraps
  conversions and flow captures, recognizes return escape, and infers captured
  mutation, owned arrays, and fresh mutable objects. The delegate, delegate-type,
  lambda, local-function, and analyzer evidence fixtures preserve all outcomes.
  Removed the now-unused operation-context diagnostic reporter overload and
  regenerated the impact inventory without stale type entries.
- [x] Moved non-loop coalesce-assignment current completion off the structural
  program-point fallback. The CFG collector now obtains the canonical entry
  snapshot and applies the existing operation-kind coalesce transfer atomically,
  preserving normalized state and evidence for reference, nullable, known, and
  unknown values under default and custom budgets. This also fixes the
  characterized guarded-assignment bug that combined a pre-assignment null guard
  with the post-assignment non-null value and incorrectly marked a reachable
  completion contradictory. Loop-local current completion remains an explicit
  typed fallback until loop observations can preserve the same state contract.

- [x] Replaced the simple-assignment-only `for` initializer gate with canonical
  operation dispatch. Increment and compound initializers now produce exact
  initial-entry state through computed-update transfer; invocations, missing
  conditions, nested loops, and member targets remain typed fallbacks. This
  fixes the characterized legacy bug that ignored `index++` and `index += 1`
  and retained zero rather than one.
- [x] Removed `SymbolicIndexingLowerer`'s parallel current-assignment and
  mutation walker. Assigned `System.Index` and `System.Range` shapes now use the
  shared current-value resolver also consumed by exact runtime-type inference.
  Existing range, index, lowering, and runtime-hazard fixtures retain parity;
  the shared loop invalidation also fixes a characterized false proof that
  reused a range shape after a later loop-body mutation.
- [x] Removed the remaining internal construction and traversal adapters for
  query options, source inputs, source-predicate inlining, and mutation scans.
  Analysis limits now enter the immutable query options once, source profiles
  use one factory per input kind, predicate symbols use one lowering entry
  point, and `SymbolicMutationInventory` is the sole mutation-enumeration owner.
  Budget, source-profile, predicate, requires-entry, range/index, and CLI
  characterization remains green.
- [x] Removed four one-field symbolic lowering carriers and the remaining
  runtime-hazard type/unwrap forwarding helpers. Finite domains, loop
  invariants, and source-completion conditions now flow as immutable
  read-only collections directly through the lowering result and operation
  transfer kernel. Focused finite-domain, CFG, and operation-transfer
  characterization remains green.
- [x] Added canonical CFG exceptional-entry seeding for catch-local targets and
  moved catch parameter/filter facts out of the structural statement-transfer
  owner. Ordinary, filtered, nested, later-clause, and protected-mutation catch
  targets now match structural normalized state and evidence. This migration
  adds 115 temporary production lines; syntax fallback remains only for catch
  shapes whose prior operations the CFG transfer cannot yet lower.
- [x] Stopped traversing a protected `try` after seeding its catch-local target.
  Catch analysis now applies every nameable protected mutation directly and no
  longer falls back merely because a separate mutation target is unnameable.
  Declaration-free and reassigned catch targets retain parity. Nested catches
  and catch targets inside abrupt loops remain explicit structural fallbacks
  until the semantic graph can represent operation-indexed exceptional edges.
- [x] Moved conservative mutation invalidation for unguarded expression and
  declaration operations into the CFG program-point worklist. Unsupported
  external-member assignments now match the structural fallback's normalized
  state and normal-completion facts directly; guarded operations remain typed
  fallbacks so a mutation cannot silently preserve a stale branch condition.
  This adds eight temporary production lines that the next structural-owner
  deletion tranche must repay.
- [x] Made CFG guard frames discard only invalidated conditions while retaining
  unaffected outer guards. Branch-local scalar/reference mutations now match
  the structural state directly instead of routing through fallback. Removing
  the obsolete rejection gates repays two of the temporary migration lines;
  coalesce-assignment guards remain conservative until their merge can retain
  conditional-access and known-non-null facts.
- [x] Extended stale-guard detection to the current statement's symbol-version
  changes. Guard-mutating simple and checked/unchecked compound assignments now
  produce exact current-completion state with structural parity. This adds five
  temporary production lines; loop-local coalesce completion remains the sole
  characterized assignment-current fallback.
- [x] Removed the generic path-condition merge framework, which had exactly one
  closed construction. The merger now operates directly on canonical
  `SymbolicCondition` values while retaining deterministic keys, Cartesian
  choice bounds, guard construction, and every truncation event. This deletes
  22 maintained production lines without changing merge output.
- [x] Removed the one-use `PathConditionMergeLimits` carrier. The canonical
  immutable analysis budget now enters path merging directly, preserving all
  four configured thresholds while deleting another 11 production lines.
- [x] Unified EffectSummary's five fresh-owned write predicates behind one
  classification rule. Return-value initialization and byref-like view
  construction now produce the same `internal_only` visibility as the purity
  classifier, fixing a previously contradictory serialized classification.
- [x] Removed EffectSummary's unused inferred-summary projection. The retained
  shared contract now fails closed for unknown effect tags, and its existing
  test identities remain intact with an added conservative-unknown regression.

## Current evidence

- Phase-two maintained production: 81,044 lines (77,082 C#, 3,228 scripts,
  and 734 maintained specification lines), a net 3,390-line reduction from the
  84,434-line phase-two baseline. Another 16,610 lines remain to the 64,434
  completion ceiling.
- The test-preservation check finds all 4,469 baseline attributed test methods
  among 4,474 current methods, unchanged parameterized-case counts, and no new
  disable markers.
- Release solution build: zero warnings and errors.
- Latest six-lane run: 6,204 passing and zero reported skipped, run sequentially
  with one NUnit worker: Oracle 573, Analyzer 487, Flow 257, Core 258,
  MainGeneral 4,014, and Tooling 615. The warning-as-error Release solution
  build also completes with zero warnings and errors.

## Milestones

- [x] 3,000 lines removed: inferred-summary foundation and first migrated rules.
- [x] 7,000 lines removed: manual semantic catalogs substantially migrated.
- [x] 12,000 lines removed: canonical symbolic traversal owns all transfer paths.
- [x] 16,000 lines removed: query, analyzer policy, and EffectSummary duplication removed.
- [x] 20,000 lines removed: canonical semantic migrations and production style
  normalization complete.

## Current tranche

The 20,000-line reduction target is complete. Future work should replace only a
complete duplicated semantic owner, preferring cuts that repay
at least 200 maintained-production lines and especially 350-line or larger
cuts in Symbolic, Analyzer, ProofCore, or EffectSummary. Prefer inferred or
canonical analysis over manual catalogs when characterization proves it is
complete; otherwise preserve the explicit owner. Require focused parity before
deleting the old path in the same tranche, and do not revisit the independently
required string-hash, type-identity, CFG/loop-transfer, EffectSummary
assembly-reader, generated-purity override catalog, remaining semantic-wrapper
groups, concrete-fact index, or impacted-test orchestration owners without new
call-site ownership or generic-instantiation evidence. The fact index remains
necessary for C#-sound division/remainder preprocessing, regex validation, and
solver-free contradictions; it is no longer a parallel hazard proof engine.

The inferred fuzz coverage manifest is now a canonical owner as well. Do not
restore parallel Roslyn-kind or analyzer-action tables; extend the registry,
rule ownership, or compact family inference when Roslyn adds a new shape.

The analyzer's remaining BCL invocation overrides are also characterized as
semantic owners, not adapters. Generated summaries alone do not preserve the
Type, StringComparer, FormattableString, Enum/Boolean/IPAddress parsing, and
Unsafe semantics. Their duplicated operand traversal has now been removed, but
the compact recognition rules must remain until canonical inference preserves
dispatch, out-argument, compiler-lowering, and compile-time enum-type behavior.

The comparer invocation rules are likewise live generic-dispatch owners rather
than a replaceable purity catalog. They resolve concrete `Equals`, `GetHashCode`,
and `CompareTo` implementations for collection and LINQ operations and preserve
explicit unknown results for unresolved type-parameter or interface dispatch.
Generated method purity does not currently encode those constructed-type call
targets, so removing the tables would silently lose conservative behavior.

The EffectSummary DateTime/DateTimeOffset call-semantic helpers remain live
semantic owners. Removing them caused four focused date/time classification
regressions, so they are not legacy adapter deletion candidates without a new
canonical replacement.

The built-in EffectSummary artifact manifest is also not an aggregate duplicate.
The generated `runtime-core-bcl` catalog contributes 5,389 canonical method keys
that are absent from the union of the other 34 generated artifacts. Its 183 root
prefixes therefore cannot be removed or replaced by the narrower artifacts
without first introducing a sound metadata-driven root-selection policy.

The remaining fresh-string and immutable-string rewrite predicates are live
semantic owners. Disabling the char-replace, fresh-copy, guarded-rewrite, and
indexed-replace dispatch entries changed the focused runtime `Substring` and
both characterized `Replace` overloads from `pure` to `impure`. Generic
fixed-point call classification does not yet track that the callee's memory
writes target the caller's freshly allocated string, so these rules must remain
until ownership evidence is propagated across calls.

The test wrapper and impacted-test selector are complementary rather than
duplicate orchestration owners. `Invoke-SharpProofTests.ps1` owns lane
partitioning, worker limits, runsettings, process isolation, and execution;
`Invoke-SharpProofImpactedTests.ps1` owns changed-file evidence, generated
inventory closure, conservative fallback, and command projection. Their only
overlap is small filter/argument formatting, well below the tranche threshold,
so do not extract a shared framework unless a new caller creates real reuse.

The preview public Symbolic boundary is already the requested
`SharpProofAnalysisSession`/`SharpProofQuery`/`SharpProofQueryResult` model.
Its typed payload projection is not a retained service-specific compatibility
API: it prevents Roslyn symbols, engine states, and SMT formulas from escaping.
Do not remove that projection merely because breaking API changes are allowed.

The proposed wholesale deletion of `SymbolicProgramPointFacts` is not yet
sound. A CFG-only characterization left 51 Oracle regressions across expression
context, conditional access/coalesce reachability, array-element writes, and
monotonic loop invariants. The structural fallback therefore remains a live
semantic owner until those families migrate individually. The characterization
did expose and fix two independent soundness bugs: unknown/out `for`
initializers no longer preserve stale values, and captured-value replay no
longer crosses lambda or local-function execution roots. Metadata fallback
also recognizes modern framework value types without accepting source aliases
that cannot occur in metadata identities.

Analyzer configuration's declarative option catalog owns documentation,
validation metadata, defaults, aliases, scopes, and policy impact, while the
strongly typed configuration reader owns runtime composition and tree fallback.
The remaining handwritten mapping is not a parallel rule catalog; converting
it to a generic property bag or generated indirection would add complexity for
less than a 200-line net reduction. Revisit only if the option registry gains
typed destination/accessor metadata that can delete the runtime mapping.
