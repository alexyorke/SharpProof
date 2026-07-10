# SharpProof Direction

SharpProof is an analyzer-first symbolic proof platform for C#.

The primary workflow is:

```text
Write contracts -> build gets diagnostics -> inspect proof/evidence -> query deeper with CLI/API
```

The default user experience should be normal Roslyn analyzer diagnostics from
attributes such as `[EnforcePure]`, `[Ensures]`, `[ZeroAllocations]`,
`[AllowedCapabilities]`, and `[ExpectedComplexity]`. The symbolic CLI and .NET
API exist to explain those results and to answer deeper point-in-code questions
about invariants, reachability, runtime hazards, capabilities, and complexity.

## Architecture Spine

SharpProof should keep moving toward one bounded proof pipeline:

```text
Roslyn/C# -> Symbolic IR -> normalized state -> proof service -> bounded Z3 -> analyzer/API/CLI output
```

The analyzer should consume symbolic services rather than owning separate proof
logic. `SearchLib` should remain the solver backend. Public surfaces should
prefer source-like facts, proof statuses, and unknown reasons instead of raw SMT
terms.

## Near-Term Roadmap

- Make the README and docs contract-first: what to annotate, what diagnostics
  appear, and how to inspect proof evidence.
- Keep generated examples as the evidence surface for every public diagnostic
  and every major symbolic query mode.
- Add focused explanation flows that connect build diagnostics to CLI/API proof
  queries.
- Continue reducing runtime-hazard formula fallbacks by migrating them to IR
  exception-precondition facts.
- Consolidate proof-status, unknown-reason, and fallback wording across
  analyzer diagnostics, CLI output, and public result DTOs.
- Split large symbolic/analyzer files only when the split removes duplicated
  proof behavior or inconsistent fallback handling.

## Backlog Candidates

These are concrete feature and analyzer-improvement candidates found by reading
the current analyzer, symbolic, docs, tooling, and test surfaces. They are not
commitments for the current preview, but they are useful backlog material.

Priority is based on expected user value, correctness risk, adoption leverage,
and whether the work unblocks later features. Within each priority, items stay
grouped by their original feature area.

### High Priority Features

#### Runtime Hazard Coverage
- Promote known runtime-hazard limitations into dedicated backlog items and
  tests: richer dynamic binder modeling, array covariance stores through
  merged array identities, and broader throw-expression flow beyond proven
  `throw null` cases.
- Expand runtime-hazard modeling for common BCL patterns such as
  collection-count guards and nullable/value-task/async result shapes.
- Add a mode that reports unknown runtime-hazard candidates from the analyzer,
  not only the CLI, with conservative severity defaults and strong suppression
  support.

#### Symbolic Engine And Evidence Quality
- Grow the IR known-API lowering table beyond the current small set of string,
  object, regex, and range/index helpers, prioritizing APIs that unblock
  contracts and runtime-hazard proofs.
- Keep reducing legacy formula-shaped compatibility paths by moving useful
  facts into typed symbolic IR, typed path conditions, and source-like public
  result DTOs.
- Add a public proof/evidence schema version and compatibility policy for
  compact JSON, diagnostic properties, effect summaries, and baseline entries.
- Add a standalone source-query compilation profile for non-MSBuild API and CLI
  calls, covering language version, preprocessor symbols, nullable context,
  unsafe allowance, documentation mode, platform, optimization, and assembly
  identity, so single-file queries can intentionally match the user's compiler
  settings before the heavier `--project` path is needed.
- Move CLI-only compact projections for capability, complexity, runtime-hazard,
  and future `explain` results into `SharpProof.Symbolic` public DTOs with
  shared schema tests, instead of keeping invariant compact output in the
  library and other compact shapes as internal CLI classes.
- Add solver-model witness and input-domain synthesis for point, line, span,
  all-lines, implication, reachability, and runtime-hazard queries. Expose
  satisfying assignments and conservative domain summaries for parameters,
  locals, receiver state, integers/ranges, nullness, string length/content,
  regex or prefix/suffix predicates, collection lengths, and indexes, with
  explicit unsupported or approximate markers so users can ask what inputs
  reach a line or trigger a specific hazard.
- Add stable unknown-reason taxonomies for capability, complexity, runtime
  hazard, purity, and `[Ensures]` results so users can distinguish unsupported
  syntax, unsupported library modeling, solver budget, timeout, cancellation,
  and native solver failures.
- Centralize nullable-flow facts from Roslyn null-state analysis and
  `System.Diagnostics.CodeAnalysis` attributes such as `AllowNull`,
  `DisallowNull`, `MaybeNull`, `NotNull`, `MaybeNullWhen`, `NotNullWhen`,
  `NotNullIfNotNull`, `MemberNotNull`, `MemberNotNullWhen`, `DoesNotReturn`,
  and `DoesNotReturnIf`, then feed the same facts into `[Ensures]`,
  runtime-hazard, reachability, and purity evidence instead of maintaining
  parallel partial implementations.
- Make bounded-analysis truncation observable and configurable where it affects
  proof quality: path-condition merges, if/switch/try fact merges, foreach
  element facts, structural null-state depth, and state-merge fact caps should
  emit proof evidence or diagnostics when limits are hit instead of silently
  losing facts.
- Add SMT solver lifecycle and health controls for long-running analyzer hosts:
  reset or retry after transient Z3 failures, expose when a service has become
  permanently unavailable, and provide an intentional way to dispose or recycle
  thread-local solver contexts without losing shared-query-cache benefits.
- Triage `POTENTIAL_BUGS.md` into prioritized issues or regression tests, then
  delete or shrink entries once they are fixed, disproven, or intentionally
  accepted as conservative behavior.

#### Effect Summary Pipeline
- Make the built-in effect-summary MSBuild generation target incremental and
  hermetic: declare inputs and outputs, skip regeneration when the artifact
  spec, runtime assemblies, tool binary, and output resources are unchanged,
  support an explicit inner-loop opt-out, and capture tool failures as
  actionable build diagnostics instead of rebuilding summaries unconditionally.
- Add analyzer-visible stale-summary evidence when supplied effect summaries are
  ignored because assembly identity, module version, method token, method-body
  hash, or the artifact spec's framework/package source no longer matches the
  current compilation.
- Validate built-in effect-summary embedded resources as required artifacts:
  fail build/package tests or emit analyzer-visible evidence when expected
  generated-purity or exception-summary resources are absent, empty, corrupt, or
  skipped, instead of silently constructing an empty built-in catalog.
- Report diagnostics on malformed, empty, unsupported-version, or partially
  ignored supplied `AdditionalFiles`, including `SharpProof.Baseline.json` and
  `*.SharpProof.EffectSummary.json`, instead of silently dropping bad JSON or
  invalid entries during compilation-start parsing.
- Make effect-summary generation memory-bounded and resumable for
  `--all-runtime-assemblies`, `--include-callees`, and unbounded `--max-depth`
  runs: stream or shard per-assembly output, record progress, and bound thrown
  exception edge traversal so large runtime analyses do not require holding the
  full document graph in memory.

#### Tooling, Packaging, And Verification
- Add CI-visible package-consumer tests for all current public diagnostics, not
  only a subset, and include code-fix availability where supported by the
  package layout.
- Decide whether `SharpProof.Symbolic` is a supported public library package or
  an analyzer-private implementation assembly. If public, ship it as a real
  NuGet `lib` asset with XML docs, nullable annotations, samples, Source Link,
  and package/API compatibility baselines; if private, hide or internalize the
  accidental public query DTO surface so consumers do not build against a DLL
  that is only delivered under `analyzers/dotnet/cs`.
- Add a project-aware symbolic CLI/API mode, such as `--project` or
  `--solution`, that loads MSBuild references, parse options, `.editorconfig`
  analyzer configuration, baselines, and effect-summary AdditionalFiles so
  `explain` matches the build diagnostics users actually see.
- Add lightweight non-project input modes for editor and automation adapters:
  `--stdin`, `--source-text`, `--source-file-name`, source-map metadata, and a
  JSON request envelope that can carry source text, virtual file path, target
  location, references, parse options, implied conditions, SMT budgets, and
  output preferences without requiring a temporary file.
- Add CI-oriented exit-code gates for all symbolic query modes, not only
  `--fail-on-hazard`: fail on unproven `--implies`, capability violations or
  unknowns, complexity exceeded or unknown, conservative unknown counts, and
  compact JSON threshold breaches.
- Add a typed CLI/API error model with stable error codes, categories, JSON
  error envelopes, and exit-code mapping for invalid option combinations,
  unsupported targets, missing references, parse failures, native solver
  loading failures, timeouts, and canceled queries so automation can handle
  failures without scraping exception text.
- Add machine-readable `explain` output, such as `explain --json`,
  `explain --sarif`, and optional markdown reports, that composes invariant,
  reachability, runtime-hazard, capability, complexity, and diagnostic
  cross-links into one bounded result for IDEs, CI bots, and issue attachments.
- Audit the NuGet analyzer layout against current analyzer packaging
  conventions, especially the native Z3 payload: decide on RID/platform-specific
  native assets or a graceful SMT-disabled fallback, then add Windows, Linux,
  and macOS package-consumer coverage.
- Remove the analyzer project's `RS1035` suppression by auditing host-banned
  APIs, especially filesystem, environment, reflection, and assembly-loading
  calls used for effect summaries, then route remaining analyzer inputs through
  supported Roslyn mechanisms or document and test intentional exceptions.
- Add analyzer-host concurrency and cancellation stress tests for
  `EnableConcurrentExecution`, shared `CompilationPurityService` and SMT state,
  `AsyncLocal` catalogs, baselines, and effect-summary caches so parallel IDE
  callbacks cannot leak configuration, dispose live services, or report
  nondeterministic diagnostics.
- Prototype an operation-block-backed analyzer pipeline that computes method
  body roots, semantic facts, and symbolic query results once per method-like
  body, then feeds purity, allocation, capability, postcondition, complexity,
  and exception checks from that shared state. Keep the Roslyn action-surface
  manifest as the tracking source for why syntax-node-only analysis remains or
  is replaced.

### Medium Priority Features

#### Analyzer Contract Ergonomics
- Add inferred-contract suggestion diagnostics and code fixes beyond `SP0004`:
  suggest `[ZeroAllocations]`, `[AllowedCapabilities(...)]`,
  `[ExpectedComplexity(...)]`, exception contracts, simple `[Ensures]`, and
  future `[Requires]` where the current symbolic evidence is strong enough,
  with separate confidence levels, scope filters, and default severities so
  adoption hints do not become noisy correctness failures.
- Support property-level and indexer-level contract attributes as ergonomic
  aliases for their getter or accessor bodies where that is sound, or provide
  a sharper diagnostic and code fix that moves the attribute to the supported
  accessor location.
- Add an opt-in Roslyn `DiagnosticSuppressor` layer that suppresses external
  analyzer or compiler non-error diagnostics only when SharpProof has exact
  proof evidence for the same location, such as proven non-null dereferences,
  in-range indexes, non-zero divisors, unreachable switch arms, or unreachable
  exception paths. Include suppression descriptors, proof links, allowlists for
  supported diagnostic IDs, and tests that uncertain proofs leave the original
  diagnostics visible.
- Document and audit the policy knobs that can change purity results, including
  `sharpproof_known_pure_methods`, `sharpproof_known_impure_methods`,
  namespace/type overrides, `sharpproof_purity_profile`, assembly-level
  `[PureExternal]` and `[Impure]`, and generated purity overrides.
- Add a trusted-boundary review mode that reports every trust shortcut used by
  an analysis run, with exact symbol, source of trust, configured value or
  attribute, and whether a stronger generated summary or direct contract
  overrode it.
- Add an explicit generated-code analysis policy: keep generated files quiet by
  default, but allow opt-in analysis of contract-bearing generated or
  source-generator output, and report why a generated member was skipped when
  it carries SharpProof attributes.
- Add first-class synchronization diagnostics beyond generic `SP0002` purity
  failures: distinguish missing `[AllowSynchronization]`, unsupported lock
  targets under `[AllowSynchronization]`, volatile/interlocked/threading
  operations, and redundant allowances, with fixable guidance for each case.

#### Purity Rule Precision Targets
- Add specialized handling for custom interpolated-string handlers so handlers
  with proven pure constructors, `AppendLiteral`, and `AppendFormatted` methods
  can be accepted instead of treating the whole handler operation family as
  conservative.
- Add a narrow unsafe-analysis model for address-of and function-pointer
  operations: keep arbitrary pointer behavior conservative, but allow trusted
  intrinsic patterns, readonly fixed buffers, and explicitly annotated pure
  function-pointer targets where the call target and memory access are bounded.
- Improve dynamic-operation evidence by separating dynamic member/indexer/object
  creation categories, binder-null hazards, and unknown external dispatch so
  diagnostics are actionable instead of all appearing as generic external-call
  uncertainty.
- Improve dispatch precision for generic interface constraints, static abstract
  interface members, static virtual interface defaults, operators, and
  conversions where known sealed, struct, or exact receiver facts can prove the
  target without opening the analysis to external implementations.
- Add first-class async and iterator state-machine proof semantics beyond the
  current awaited-expression checks: model `Task` and `ValueTask` result,
  cancellation, exception, continuation, `await foreach`, async iterator, and
  `await using` effects consistently across purity, allocation, capability,
  runtime-hazard, and complexity queries.
- Model higher-order and deferred execution separately from delegate creation:
  distinguish query construction from enumeration for LINQ operators, track
  immediate versus deferred and streaming versus buffering operators, propagate
  predicate/projector effects when source is known, and keep `IEnumerable<T>`,
  `IQueryable<T>`, PLINQ, `Task.Run`, and `Parallel.ForEach` callbacks
  conservative when execution timing, provider translation, or parallel
  invocation is unknown.

#### Missing Contract Types
- Add loop invariant and loop variant annotations for methods whose safety,
  postcondition, or complexity proof needs facts that cannot be inferred from
  the current bounded loop analysis.
- Add construction and required-member contracts that reason about
  `required`, `init`, primary constructors, object and collection initializers,
  `[SetsRequiredMembers]`, non-nullable members, and constructor helper methods,
  so SharpProof can prove when a newly visible object is fully initialized and
  report when an invariant or postcondition depends on state that construction
  did not establish.
- Extend `[ZeroAllocations]` into optional allocation budgets or allocation
  categories, such as direct-only, transitive source calls, closure/state-machine
  allocations, boxing, arrays, delegates, and framework helper allocations.
- Add a user-extensible capability contract model for project-specific
  capabilities, so teams can define capabilities beyond the built-in IO,
  clock, randomness, reflection, synchronization, process, and native interop
  categories.
- Add first-class proof hint and assertion surfaces, such as
  `SharpProof.Assert`, `SharpProof.Assume`, or recognized
  `Contract.Assert`/`Contract.Assume` patterns, with clear diagnostics when an
  assertion is not proven and strict policy controls for whether assumptions
  are trusted, audited, or forbidden in production builds.
- Define contract inheritance and variance rules for overrides and interface
  implementations across `[ZeroAllocations]`, `[AllowedCapabilities]`,
  `[Ensures]`, and `[ExpectedComplexity]`, not only `[EnforcePure]` and
  `[Pure]`.

#### Runtime Hazard Coverage
- Add cross-method runtime-hazard summaries for source methods and imported
  effect summaries so site diagnostics can account for proven hazards in
  callees without reanalyzing entire method bodies in analyzer callbacks.

#### Security And Deployment Coverage
- Add secret and cryptographic misuse diagnostics for hard-coded keys/tokens,
  weak algorithms, insecure randomness used for security-sensitive values,
  certificate-validation bypass, deprecated TLS settings, and unsafe
  deserialization, while keeping general capability reporting separate from
  security severity.

#### Rust-Inspired Safety Coverage
- Consume C# ref-safety signals as first-class proof facts where the compiler
  already exposes them: `scoped`, `ref readonly`, `in`, `ref struct`,
  `readonly ref struct`, `UnscopedRefAttribute`, `RefSafetyRulesAttribute`,
  and `allows ref struct` constraints. Use those facts to distinguish provably
  stack-confined borrows from values that can escape into fields, closures,
  async state machines, iterators, interface boxing, or heap-owned wrappers.
- Add immutability-pressure diagnostics and optional contracts such as
  `[Immutable]`, `[ReadOnly]`, `[Mutates]`, or `[DoesNotMutate]` for values that
  are validated, cached, shared across threads, or captured by async/iterator
  state machines. Prefer evidence-based suggestions for `readonly`, `init`,
  records, immutable/frozen collections, and local mutation removal over broad
  style warnings.
- Add closed-union and exhaustive-handling analysis for C# union idioms such as
  abstract record hierarchies, enum-plus-payload shapes, `OneOf`-style types,
  and project `Option<T>`/`Result<T,E>` types. Report non-exhaustive
  `switch`/pattern handling, stale catch-all/default arms that hide newly added
  cases, and discarded payloads when the type is meant to model a closed set.
- Add explicit outcome-handling diagnostics for `Option`/`Maybe`,
  `Result<T,E>`, `Try*` patterns, nullable results, and `Task<Result<...>>` so
  callers must intentionally handle absence or failure before accessing values,
  throwing, blocking, or discarding the result. Integrate this with exception
  contracts rather than treating result-style and exception-style failures as
  unrelated models.
- Add unsafe-boundary diagnostics inspired by Rust's small-auditable-unsafe
  model: inventory `unsafe` blocks, function pointers, `Unsafe`,
  `MemoryMarshal`, `Marshal`, P/Invoke, COM interop, raw buffer operations, and
  mutable static state; require a documented safe abstraction or explicit
  contract at public boundaries and keep diagnostics focused on the smallest
  unsafe region that explains the risk.

#### Complexity And Capability Coverage
- Extend `ComplexityKind` beyond `Constant`, `Linear`, and `Quadratic` so
  contracts can express currently reported shapes such as `Product`, `Max`,
  recursive unknowns, and future logarithmic or linearithmic costs.
- Improve complexity inference for currently conservative loop shapes,
  especially monotone `while` and `do` loops, loops with multiple dependent
  variables, early exits, helper-method step functions, and recursion with
  recognizable decreases.
- Add memory and allocation complexity as a separate query and contract surface
  instead of treating complexity only as asymptotic CPU work.
- Add capability and complexity whole-file or whole-project aggregation to the
  API and CLI, analogous to invariant and runtime-hazard `--all-lines` modes.
- Feed effect-summary metadata into capability and complexity queries where
  source is unavailable, while keeping unknown external behavior conservative.
- Extend built-in capability taxonomy beyond synchronization primitives to
  scheduling, concurrency, and ambient-context effects such as `Task.Run`,
  `ThreadPool`, thread creation, timers, `Parallel`, `AsyncLocal`,
  `ThreadLocal`, channels, and concurrent collections, with diagnostics, query
  evidence, and docs.
- Prefer body-bearing source declarations consistently when resolving partial
  methods for purity fixed-point call graphs, capability summaries, complexity
  queries, and related transitive source queries, so declaration-only partial
  definitions do not turn implemented methods into unsupported or unknown
  targets.

#### Symbolic Engine And Evidence Quality
- Turn the documented "partial" regex and string reasoning surface into an
  explicit support matrix and burn-down list covering `Regex.IsMatch`,
  `Match`, `Matches`, `Replace`, `Split`, generated-regex methods and
  properties, timeout overloads, culture-sensitive options, `IgnoreCase`,
  `NonBacktracking`, and unsupported pattern constructs with stable unknown
  reasons.
- Normalize provenance naming across symbolic IR and analyzer-layer facts, with
  documented prefixes and compatibility tests so downstream CLI, JSON, and
  corpus-report grouping does not depend on accidental `ir.*` versus
  `analyzer.*` strings.

#### Effect Summary Pipeline
- Move generated purity classification from built-in runtime slices toward a
  documented trust pipeline for project and package assemblies, including
  source-summary generation, CI refresh, cache-key validation, and AdditionalFiles
  wiring that does not reintroduce checked-in JSON artifacts or legacy
  buildTransitive targets.
- Promote the BCL fallback inventory into a burn-down workflow: emit coverage
  percentages, top unknown or probably-impure members, manual-catalog comparison
  deltas, and explicit root-seed review queues for environment, culture, time,
  randomness, process, filesystem, threading, native, and reflection roots.
- Extend effect-summary documents beyond purity and thrown exceptions to carry
  imported capability, allocation, complexity, and runtime-hazard summaries that
  symbolic queries can consume conservatively when source is unavailable.
- Add contract drift reports that compare inferred summaries across revisions,
  packages, or effect-summary artifacts and flag newly introduced capabilities,
  allocations, thrown exceptions, runtime hazards, or complexity regressions
  before users hand-write matching contracts.
- Extend effect summaries with higher-order callback metadata: which parameters
  are invoked, whether invocation is immediate, deferred, repeated, conditional,
  parallel, stored, or escaped, and how callback purity, capabilities,
  allocation, exceptions, and complexity compose into the enclosing member.
- Add schema migration and compatibility checks for effect-summary versions,
  generated purity catalogs, fallback inventory reports, and any future
  analyzer-consumed summary fields.
- Consolidate the generated-purity and exception-summary catalog infrastructure
  so PE-file identity, runtime implementation path, and method-identity caches
  are shared instead of duplicated in separate unbounded static dictionaries.

#### Tooling, Packaging, And Verification
- Add an analyzer coverage dashboard that combines Roslyn operation coverage,
  syntax-shape fuzz coverage, effect-summary coverage, and runtime-hazard
  fallback counts into one local artifact.
- Add a command that summarizes the most expensive or most conservative proof
  failures in a project, grouped by diagnostic id, unknown reason, operation
  kind, contract type, and source member.
- Add an adoption-assistant command that can generate a reviewable patch or
  report of inferred contracts for a project or solution, grouped by confidence
  and contract type, with options to emit `.editorconfig` profiles, baselines,
  or source edits for `[EnforcePure]`, `[ZeroAllocations]`,
  `[AllowedCapabilities]`, `[ExpectedComplexity]`, and future contract
  families.
- Extend the SARIF corpus-report tool with baseline or previous-report diffing,
  thresholds, stable markdown output, and explicit ranked triage sections for
  catalog misses, unknown operation kinds, false-positive candidates, and
  exception-summary source chains.
- Add a batch or streaming symbolic query mode for file lists or JSON/NDJSON
  request sets, reusing compilation, references, and SMT services across many
  point, line, hazard, capability, and complexity queries while returning
  per-request errors and compact aggregate summaries.
- Add proof and effect graph exports for debugging and review: emit DGML,
  GraphViz DOT, or compact JSON graphs for call chains, symbolic fact
  dependencies, exception-summary edges, capability sites, complexity drivers,
  and effect-summary provenance so users can inspect why a result was
  conservative without reading raw text logs.
- Replace the hand-rolled symbolic CLI option parser with a command model that
  supports subcommands, response files, shell completion, mutually exclusive
  option groups, generated help, and shared validation metadata for docs and
  tests.
- Add editor-facing lightbulb actions for "explain this diagnostic", "generate
  baseline entry", and "open compact JSON proof evidence" when running inside
  Visual Studio or another Roslyn host.
- Add per-diagnostic help links and stable generated rule pages so IDE "View
  Help" and rule metadata can jump from every `SP*` diagnostic to its examples,
  configuration keys, suppressions, and explanation workflow.
- Ship optional package-delivered `.globalconfig` profiles through MSBuild
  `.props` wiring, so consumers can enable SharpProof migration, audit, CI, or
  strict modes with a package property while still retaining documented
  precedence and local override behavior.
- Move analyzer diagnostic and code-fix titles/messages into real
  resource-backed strings, remove scaffolded resource entries, and add package
  and VSIX tests that resource/satellite assemblies are present and code
  actions do not fall back to stale hard-coded text.
- Add real VSIX artifact and MEF composition coverage rather than relying on
  simulated-VSIX smoke tests: fail when the VSIX was not produced, verify the
  analyzer, code-fix assembly, attributes, and native solver payload are present,
  and exercise at least one diagnostic plus code fix through the actual editor
  delivery package.
- Use the existing production-size and raw-SMT hotspot probes as recurring
  architecture checks so very large files and proof fallback counts remain
  visible during refactoring.

### Low Priority Features

#### Missing Contract Types
- Add object invariant contracts for class, record, and struct validity, such
  as `[Invariant("condition")]` or compatibility with
  `Contract.Invariant(...)`. Verify invariants after constructors,
  `init`/object-initializer completion, and public mutating members; feed the
  invariant facts into callers, `[Ensures]`, runtime-hazard checks, and
  capability/complexity summaries without treating them as ordinary method
  postconditions.
- Add higher-order effect contracts for delegate parameters, events, callbacks,
  and virtual extension points, so APIs can require a `Func<>`, `Action<>`,
  `Predicate<>`, `Expression<TDelegate>`, event handler, or scheduler callback
  to be pure, allocation-free, capability-bounded, exception-bounded, or
  complexity-bounded, and callers can satisfy those requirements with lambdas,
  method groups, local functions, generated summaries, or explicit
  project-specific delegate contracts.
- Split resource ownership and lifetime analysis into first-class diagnostics
  and optional contracts for missing dispose, double dispose, use-after-dispose,
  returned ownership, ownership transfer, `IDisposable`, `IAsyncDisposable`, and
  aliasing, instead of exposing those findings only through purity evidence.
- Add quantified contract support for bounded collection and range predicates,
  such as `forall`/`exists`, `All`/`Any`, and `Contract.ForAll`-style
  conditions, with explicit limits and unknown reasons when quantifier
  expansion, symbolic array reasoning, or delegate predicate purity is
  unsupported.

#### Runtime Hazard Coverage
- Add optional regex runtime/performance hazard diagnostics for calls that use
  untrusted or nonconstant patterns, omit an explicit timeout, or could use
  `RegexOptions.NonBacktracking` or source-generated regexes, with conservative
  evidence rather than treating regex only as an ordinary purity or SMT fact.

#### Security And Deployment Coverage
- Add configurable taint/source-sink analysis that reuses symbolic path facts
  and capability sites to track untrusted data from ASP.NET/HTTP, environment,
  command-line, file, and database sources into SQL, shell/process, file path,
  XML/XPath/XAML/HTML, redirect/URL, DLL load, regex pattern,
  serialization/deserialization, logging, and response-header sinks, with
  sanitizer contracts and project-specific source/sink definitions.
- Add deployment-profile analysis for platform, trimming, and AOT
  compatibility: consume `[SupportedOSPlatform]`, `[UnsupportedOSPlatform]`,
  platform guard attributes, `OperatingSystem.Is*` guards,
  `[RequiresUnreferencedCode]`, `[RequiresDynamicCode]`,
  `[RequiresAssemblyFiles]`, `[DynamicallyAccessedMembers]`, and
  `DynamicDependency`, then report when source, generated summaries, or user
  contracts cross target-platform, trim, or NativeAOT boundaries.

#### Rust-Inspired Safety Coverage
- Add targeted borrow-style aliasing diagnostics for C# surfaces where Rust's
  ownership model has a direct safety analogue: `Span<T>`, `Memory<T>`,
  arrays, mutable collections, `ref`/`out` parameters, pooled buffers, native
  handles, and disposable owners. Report mutation through one alias while a
  read borrow is live, multiple live write aliases, returning or storing
  borrowed state past the owner's lifetime, and use after return-to-pool, while
  keeping a full Rust-style borrow checker out of scope for the preview.
- Add Clippy-style analyzer profiles for correctness, suspicious code, safety,
  performance, and pedantic adoption, with conservative default severities and
  fixability metadata so teams can enable Rust-like "make illegal states
  unrepresentable" pressure incrementally in C# projects.

## Non-Goals For The Current Preview

- SharpProof is not a whole-program execution engine.
- It does not claim a precise percent of .NET SDK coverage.
- Unsupported, timed-out, canceled, native-load-failed, or over-budget proof
  obligations must remain conservative.
- A full Rust-style borrow checker remains future work.
