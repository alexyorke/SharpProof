# SharpProof Backlog

Research snapshot: 2026-07-12. Priorities reflect SharpProof's stated goal of
enforceable, bounded C# contracts and its current preview limitations. Items
below were checked against the repository before inclusion.

## P0 - Ship installable, verifiable preview packages

SharpProof already builds and consumer-tests three packages, but the README
states that none is published to NuGet.org. Add a gated release workflow that
promotes the exact CI-tested artifacts to NuGet.org, publishes symbols, embeds
Source Link and repository commit metadata, produces deterministic builds, and
signs and timestamps release packages. Keep publishing separate from ordinary
branch CI and require the existing package-content and clean-consumer probes.

Why this is high priority: a build analyzer is only useful to normal projects
when it can be restored by both IDE and CI builds. Microsoft also documents
Source Link plus deterministic builds as the traceability path for .NET
libraries, and package signatures as integrity and origin verification.

Acceptance criteria:

- `SharpProof`, `SharpProof.Attributes`, and `SharpProof.Symbolic` preview
  packages are installable from NuGet.org without a local feed.
- A release is promoted only from artifacts that passed release validation and
  the clean package-consumer matrix; the workflow cannot rebuild different
  binaries during promotion.
- Package metadata contains the source repository and exact commit, symbols are
  published, deterministic-build output is verified, and signed packages pass
  `dotnet nuget verify`.
- A documented rollback/deprecation procedure and versioning gate prevent an
  accidental replacement or incompatible stable release.

Evidence: [Source Link and .NET libraries](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/sourcelink),
[NuGet signed-package verification](https://learn.microsoft.com/en-us/dotnet/core/tools/nuget-signed-package-verification),
and [signing NuGet packages](https://learn.microsoft.com/en-us/nuget/create-packages/sign-a-package).

## P0 - Make native SMT proof available on the mainstream RID matrix

Upgrade the pinned Z3 4.12.2 integration and package matched managed/native
bindings for at least Windows x64/arm64, Linux x64/arm64, and macOS x64/arm64.
The repository currently guarantees native SMT only on Windows x64 and macOS
x64; Linux and every arm64 host permanently degrade to conservative unknown
unless the host happens to provide a compatible library. Z3 4.16.0 now ships
official release assets for Windows, Linux, and macOS on x64 and arm64, so the
old packaging constraint should be revisited without weakening provenance or
ABI checks.

Acceptance criteria:

- Each supported RID restores a managed binding and native library from the
  same pinned upstream release, with hashes, license, and provenance recorded.
- Clean analyzer and `SharpProof.Symbolic` consumers execute a real SMT-backed
  proof on every supported RID in CI; fallback is no longer accepted there.
- Unsupported or incompatible hosts retain the existing conservative failure
  taxonomy and never crash Roslyn, the CLI, or an API consumer.
- Upgrade tests cover compiler-server shadow copying, parallel analyzer hosts,
  native initialization recycling, package layout, and binding ABI mismatch.

Evidence: the [Z3 4.16.0 release](https://github.com/Z3Prover/z3/releases/tag/z3-4.16.0)
provides current NuGet and platform archives, and the upstream
[Z3 repository](https://github.com/Z3Prover/z3) documents its .NET binding.

## P0 - Add a C# 15 semantic coverage lane

Extend the modern C# surface matrix, Roslyn dependency strategy, symbolic IR,
runtime-hazard analysis, allocation/capability/complexity models, and fuzz
families for C# 15. Start with collection-expression constructor/factory
arguments and union types. For unions, model case conversion, case tests,
payload facts, nullability, and exhaustive switch behavior; never infer
exhaustiveness or purity from syntax alone when metadata or a preview compiler
shape is unsupported.

Why this is high priority: the repository's matrix stops at C# 14 preview while
C# 15 is the current preview language for .NET 11. These features directly
change construction, dispatch, allocation, pattern exhaustiveness, and proof
facts - all core SharpProof concerns.

Acceptance criteria:

- A dedicated C# 15 preview test lane compiles real syntax with the matching
  compiler instead of synthetic Roslyn nodes or text-only fixtures.
- Collection-expression arguments preserve constructor/factory side effects,
  thrown exceptions, allocation cost, comparer semantics, and capacity facts.
- Union case construction and matching lower to typed IR; exhaustive switches
  do not produce a false no-match hazard, while non-exhaustive/unsupported
  shapes stay conservative.
- The coverage matrix records every proof area as covered, partial,
  conservative, or gap, backed by positive, negative, and unknown regressions.

Evidence: Microsoft's [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)
lists collection-expression arguments and union types, and
[C# language versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning)
identifies C# 15 as the current preview language version.

## P1 - Model synchronization as an explicit effect and capability

Replace the current generic treatment of `lock` with typed synchronization
effects for both monitor locks and `System.Threading.Lock`. Track acquisition,
release, nesting, receiver identity, and the compiler's distinct `EnterScope`
lowering. Add a synchronization capability that composes with purity policy and
effect summaries instead of requiring a blanket `[AllowSynchronization]`
escape hatch with no proof-level lock facts.

Acceptance criteria:

- Typed IR distinguishes `Monitor.Enter`/`Exit` from
  `System.Threading.Lock.EnterScope`/`Dispose`, including implicit lowering.
- Capability and purity evidence identifies the exact lock target and mechanism
  and remains conservative for casts that change lock semantics.
- Control-flow tests prove release on all normal and exceptional exits and do
  not claim safety for unsupported aliasing, ordering, or reentrancy shapes.
- C# 13 compiler errors/warnings around async lock use and converted `Lock`
  receivers have matching current-behavior regression coverage.

Evidence: the C# 13 [`Lock` object specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-13.0/lock-object)
defines the specialized lowering, and Microsoft's
[lock diagnostics reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/lock-semantics)
documents the semantic traps around converted receivers and async methods.

## P1 - Complete the contract model with object invariants and exceptional postconditions

Add first-class declarations equivalent to object invariants and conditional
postconditions on exceptional exit. SharpProof already supports preconditions,
normal postconditions, old-value snapshots, and allowed exception sets, but it
has no `ObjectInvariant` or `EnsuresOnThrow` contract surface. These additions
should share the existing typed condition parser, hierarchy rules, SMT budgets,
unknown taxonomy, and evidence schema rather than becoming a separate checker.

Acceptance criteria:

- Object invariants are checked after construction and at supported externally
  visible method/property boundaries, with explicit rules for reentrancy,
  disposal, inheritance, and exceptional exits.
- Exceptional postconditions bind an exception type/value and can reference
  method-entry snapshots; they are proved at each matching escaping throw edge.
- Overrides preserve base guarantees and cannot silently weaken inherited
  invariants or exceptional postconditions.
- Unsupported callbacks, aliasing, async boundaries, and partial construction
  produce stable unknown evidence instead of false proofs.

Evidence: Microsoft's [Code Contracts reference](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/code-contracts)
defines object invariants, old values, and `EnsuresOnThrow` as core contract
forms, while the archived [Code Contracts overview](https://learn.microsoft.com/en-us/archive/msdn-magazine/2009/brownfield/code-contracts)
explains why contracts must be inherited across substitutable implementations.

## P1 - Give async methods task-aware contracts and exception evidence

Model the two observable phases of task-returning methods: synchronous argument
validation before a task is returned, and completion/fault/cancellation observed
when the task is awaited. Let postconditions describe the eventual `TResult`,
and let exception contracts distinguish synchronous throws, task faults, and
cooperative cancellation. Extend the same model to supported `ValueTask<T>`
shapes without assuming arbitrary custom awaiters or scheduling behavior.

Acceptance criteria:

- Analyzer, CLI, and API evidence explicitly label synchronous throw, faulted
  completion, canceled completion, and successful result paths.
- `[Ensures]` on supported `Task<T>`/`ValueTask<T>` methods proves the awaited
  result, not the task object, and call sites import that fact after `await`.
- Exception contracts preserve argument-validation timing and cancellation-token
  identity; dropped or opaque tasks never count as observed successful calls.
- Tests cover direct async methods, wrappers, `Task.FromResult`, faulted and
  canceled tasks, `ValueTask<T>`, and conservative custom awaiters.

Evidence: Microsoft's [exception best practices](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)
distinguish synchronous validation from exceptions stored in returned tasks,
and [task exception handling](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/exception-handling-task-parallel-library)
documents fault and cancellation propagation.

## P0 - Close the released C# 14 coverage gaps before the C# 15 lane

C# 14 is now the current released language for .NET 10, but SharpProof's modern
surface matrix still labels it preview and records first-class gaps for
extension properties, extension operators, static extension members, and the
`field` keyword. Add a shipping C# 14 lane and model all released features that
can alter effects or proof facts: extension members, field-backed properties,
null-conditional assignment, first-class span conversions, lambda parameter
modifiers, partial events and constructors, and user-defined compound
assignment operators. This is a prerequisite for, not a duplicate of, the C#
15 preview lane above.

Acceptance criteria:

- Tests compile with the released .NET 10 SDK and C# 14 language version; the
  matrix no longer describes shipped syntax as preview.
- Extension properties, methods, operators, and static members resolve to their
  real declarations and import the same purity, capability, exception,
  allocation, complexity, and postcondition evidence as an equivalent direct
  call.
- The synthesized storage behind `field` has stable symbol identity across
  accessors, initializers, nullability flow, mutation checks, old-value
  snapshots, and constructor postconditions.
- Null-conditional and compound assignments model receiver evaluation exactly
  once and preserve getter, operator, and setter effects in language-defined
  order.
- Implicit span conversions preserve storage origin, length, mutability, and
  allocation behavior rather than degrading to an opaque conversion.
- Partial declarations are analyzed once after symbol binding, with diagnostics
  anchored to the implementing declaration and no duplicate effects.

Evidence: Microsoft's [What's new in C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
identifies C# 14 as the current release and lists the shipped feature set; the
[extension declaration reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extension)
and [`field` specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/field-keyword)
define the binding and synthesized-storage semantics that affect proof results.

## P1 - Add constraint-aware generic math proofs

Teach symbolic lowering, dispatch, runtime-hazard analysis, effect summaries,
and complexity inference about the .NET generic-math interfaces. SharpProof has
tests for static abstract interface members, but generic dispatch is deliberately
conservative and there is no `INumber<TSelf>` model. This leaves common generic
numeric algorithms unprovable even when a concrete built-in instantiation or a
validated implementation summary supplies enough evidence.

Acceptance criteria:

- Typed IR represents static abstract operators and members using the resolved
  constrained-call identity, type argument, checked context, and numeric domain.
- Closed built-in instantiations import sound facts for identities, bounds,
  sign, comparison, bit operations, and `CreateChecked`, `CreateSaturating`, and
  `CreateTruncating`, including their distinct overflow behavior.
- Open generic code may use only guarantees actually supplied by a recognized
  interface contract or validated effect summary; it never assumes arbitrary
  user implementations obey undocumented algebraic laws.
- Purity, capabilities, exceptions, allocation, and complexity flow through a
  constrained call without requiring a blanket trust rule.
- Tests cover integral, decimal, IEEE floating-point, native integer, custom
  number, default static implementation, ambiguous implementation, and
  unsupported open-world dispatch cases.

Evidence: Microsoft's [.NET generic math reference](https://learn.microsoft.com/en-us/dotnet/standard/generics/math)
documents `INumber<TSelf>`, fine-grained operator interfaces, checked and
saturating conversions, floating-point interfaces, and static virtual members;
the [static abstract interface diagnostics](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/static-abstract-interfaces)
describe the concrete-type and most-specific-implementation rules needed for
sound dispatch.

## P1 - Add bounded byref, Span, and ownership reasoning

Expand the current local ownership classifier into typed alias and escape facts
for `ref`, `in`, `out`, `scoped`, ref fields, ref returns, `Span<T>`,
`ReadOnlySpan<T>`, inline arrays, and byref-like generic parameters. The C#
compiler already rejects illegal escapes; SharpProof should consume those
guarantees to distinguish mutation of fresh non-escaping storage from mutation
of caller-visible or shared state, without claiming a full borrow checker.

Acceptance criteria:

- Each supported managed reference carries storage origin, safe-to-escape
  scope, ref-safe-context, mutability, and a bounded may-alias set through
  assignments, calls, returns, fields, conditionals, slicing, and conversions.
- Writes through a span/ref are classified by the underlying storage owner;
  readonly views block proof-visible writes but do not imply deep immutability.
- Fresh stack/local buffers can remain pure when mutation cannot escape, while
  aliases of parameters, fields, pooled buffers, and unknown callees remain
  conservative.
- Suspended async/iterator flows and captured refs obey compiler lifetime rules
  and never retain entry facts beyond a legal scope.
- Budget exhaustion, alias-set widening, unsafe pointers, and unverifiable
  metadata return stable unknown evidence rather than a pure result.

Evidence: the C# [method parameter reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/method-parameters)
defines ref-safe-context, and the [declaration statement reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/declarations)
documents how `scoped` restricts safe-to-escape lifetimes. Microsoft's C# 14
overview also makes spans first-class in overload resolution and conversion,
increasing the importance of preserving their origins.

## P1 - Add sound IEEE floating-point and decimal proof domains

Add first-class numeric domains for `Half`, `float`, `double`, and `decimal`
instead of treating floating-point overloads as generic real arithmetic or
leaving most of them opaque. For binary floating point, preserve IEEE 754
rounding, NaN, infinities, subnormals, and positive/negative zero. Keep decimal
as a separate base-10 finite domain with its own overflow and conversion rules.

Acceptance criteria:

- SMT encoding uses floating-point sorts and explicit rounding modes for
  `Half`, `float`, and `double`; it never substitutes mathematical reals where
  rounding or special values can change a conclusion.
- Equality, ordering, `Equals`, `CompareTo`, `IsNaN`, infinity checks, signed
  zero, and min/max variants retain their distinct .NET semantics.
- Arithmetic and conversions model underflow, overflow, NaN propagation, and
  the language rule that binary floating-point division by zero does not throw;
  float/double-to-decimal exceptional conversions remain visible.
- Decimal operations model scale and checked overflow, or return a precise
  unsupported reason when the bounded representation cannot decide a query.
- Witnesses serialize special values and exact bit patterns reproducibly, and
  differential tests compare proof results with the matching .NET runtime.

Evidence: the C# specification's [floating-point type rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types#837-floating-point-types)
define rounding, NaN, infinities, signed zero, and non-throwing operations;
Microsoft's [floating-point numeric type reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types)
distinguishes binary floating point from decimal, and the
[numeric conversion reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions)
documents zero, infinity, unspecified, and overflow outcomes.

## P1 - Analyze generated code without flooding users with generated diagnostics

SharpProof currently calls `ConfigureGeneratedCodeAnalysis` with `None`, so it
neither analyzes nor reports on compiler-recognized generated code. Add separate
policies for consuming generated implementation facts and for reporting
diagnostics inside generated files. By default, analyze generated declarations
when their behavior affects user-authored callers, but suppress diagnostics at
generated locations; offer an explicit audit mode for generator authors.

Acceptance criteria:

- Generated methods, accessors, operators, partial implementations, and
  source-generated serializers contribute bounded effect summaries, exception sets,
  capabilities, allocations, complexity, and supported postconditions to calls
  from user-authored code.
- Default mode never creates a warning flood at generated locations, while
  `analyze-and-report` mode reports there with stable generator/document
  provenance.
- Diagnostics in user code explain when their evidence came from generated code
  and identify the generated symbol without depending on unstable temporary
  paths or hint-name ordering.
- Generated-code detection covers Roslyn flags, conventional attributes and
  headers, source-generated syntax trees, and partial declarations, with
  explicit precedence for configuration overrides.
- Analysis remains bounded and incremental when a generator emits thousands of
  members; cancellation and a changed generated tree invalidate only affected
  summaries.

Evidence: Roslyn's
[`GeneratedCodeAnalysisFlags`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.generatedcodeanalysisflags)
separates enabling analyzer callbacks from reporting diagnostics, which allows
SharpProof to import behavior without making generated files noisy.

## P1 - Export counterexamples and hazard witnesses as replayable unit tests

Turn materializable solver witnesses into deterministic xUnit, NUnit, or MSTest
regressions. SharpProof already exposes satisfying assignments and input-domain
summaries, but users must manually translate them into calls and assertions.
Add a CLI/API generator that targets a selected failed contract, reachable
hazard, or disproved condition and emits the smallest legal test setup supported
by the witness.

Acceptance criteria:

- The generator emits compilable tests for supported primitive, enum, nullable,
  string, array, tuple, and constructible record/class inputs, preserving exact
  numeric and string values.
- A generated test invokes the exact symbol, supplies required generic type
  arguments, checks the expected return/exception/hazard, and embeds the proof
  query, source identity, schema version, and witness provenance as comments or
  test metadata.
- Before claiming a replay, an optional validation mode builds and executes the
  test in a bounded child process; divergence is labeled `non_reproducing`
  rather than silently accepted.
- Approximate domains, opaque references, inaccessible constructors, ambient
  state, nondeterminism, and unsupported mocks yield an explicit partial
  scaffold or refusal, never invented object state.
- Output is stable for identical evidence and supports redaction of values that
  originated in paths marked sensitive.

Evidence: Microsoft's [IntelliTest input-generation model](https://learn.microsoft.com/en-us/visualstudio/test/intellitest-manual/input-generation)
uses path conditions and Z3 to select inputs, while its
[test-generation documentation](https://learn.microsoft.com/en-us/visualstudio/test/intellitest-manual/test-generation)
shows why serializing distinct inputs as unit tests creates a durable regression
suite. IntelliTest is deprecated in Visual Studio 2026, which makes a focused,
SharpProof-native witness exporter more valuable than a new dependency on it.

## P1 - Add bounded information-flow and taint contracts

Build an opt-in information-flow domain on the existing symbolic state,
capability, call-graph, and effect-summary infrastructure. Let applications and
libraries declare sources, sinks, sanitizers, validators, and sensitive return
values through attributes and validated configuration. Ship conservative starter
models for common ASP.NET request inputs and SQL, process, path, HTML, logging,
and outbound-network sinks rather than hard-coding every framework API into the
core transfer engine.

Acceptance criteria:

- Taint labels propagate field-sensitively through assignments, calls, returns,
  tuples, collections, interpolation, builders, async results, and supported
  serialization shapes, with configurable interprocedural depth and hard
  budgets.
- A sanitizer clears only the labels and contexts its contract declares;
  validation facts are path-sensitive and are invalidated after relevant
  mutation.
- Diagnostics provide a bounded source-to-sink trace, label every trusted or
  unknown boundary, and distinguish SQL, shell, path, HTML, log, and secret
  disclosure contexts.
- Cross-assembly flow consumes identity-validated summaries; a missing summary
  cannot turn an unknown boundary into safe data.
- Framework catalogs are versioned data, custom models are schema-validated,
  and severity is opt-in until corpus precision gates are met.

Evidence: Microsoft's [CA3001 SQL-injection rule](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca3001)
demonstrates whole-codebase tainted-data analysis with configurable call depth
and explicitly notes the cross-assembly limitation; the current
[.NET security rule catalog](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/security-warnings)
shows that the same source-to-sink model applies to SQL, XSS, command, path, and
other injection classes.

## P1 - Detect behavioral contract breaks between package versions

Add an assembly/package contract compatibility tool that complements .NET
ApiCompat. Extract a canonical manifest of public SharpProof contracts from a
baseline and candidate, compare them using substitutability rules, and use the
proof engine for condition implication. API signatures can remain
binary-compatible while a stronger precondition, weaker postcondition, new capability,
new exception, allocation regression, or looser complexity bound breaks callers.

Acceptance criteria:

- The manifest has stable symbol identity and canonical conditions for
  preconditions, postconditions, nullability, purity, allocations, capabilities,
  exceptions, and complexity, including inherited interface/base contracts.
- Compatibility direction is explicit: accepted inputs cannot shrink and
  promised outputs cannot widen; purity and zero-allocation guarantees cannot
  disappear; newly observable effects, exceptions, or worse complexity are
  reported as behavioral breaks.
- SMT implication proves supported condition changes. Timeout, unsupported
  syntax, ambiguous type forwarding, or missing dependencies produce
  `review-required`, never `compatible`.
- The CLI/MSBuild task compares assemblies or NuGet packages, emits SARIF/JSON
  with old/new evidence, supports reviewed suppressions with justification, and
  can fail a release gate by SemVer policy.
- Tests cover virtual/interface variance, generic constraints, multi-targeted
  packages, forwarded types, preview attributes, and a baseline restored from a
  package feed.

Evidence: [.NET Package Validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview)
establishes baseline comparison as a release gate, while Microsoft's
[breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes)
states that behavior changes, exceptions, inputs, and outputs can break
consumers even when API shape is unchanged. The
[.NET compatibility rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules)
specifically disallow shrinking accepted inputs or widening returned values.

## P0 - Publish and consume transitive proof summaries automatically

Turn SharpProof's identity-validated JSON summaries into a package and project
contract interchange. Today, built-in summaries are embedded during SharpProof's
own build and third-party summaries must be manually enabled and supplied as
`AdditionalFiles`. A library should be able to generate a versioned summary when
it packs, ship that immutable artifact beside its assembly, and have consuming
projects discover it through normal ProjectReference and PackageReference build
assets without running the library or trusting a symbol name alone.

Acceptance criteria:

- A producer target emits one canonical contract/effect summary per assembly
  and target framework after compilation, containing public contracts, purity,
  capabilities, allocations, exception sets, complexity, and stable evidence
  schema versions supported by that producer.
- Pack places the summary in a documented package location and a deterministic
  `buildTransitive` target adds the matching artifact to consumer
  `AdditionalFiles`; ordinary users need no manual path or opt-in switch.
- Project references expose the same artifact with correct target-framework
  selection and incremental inputs/outputs, including multi-targeting and
  design-time builds.
- Consumers validate package ID/version, assembly relative path, assembly hash,
  module identity, method identity, schema, and target framework before using a
  fact. A stale, mismatched, or tampered summary stays unknown and reports one
  actionable configuration diagnostic.
- Diamond dependencies, aliases, central package management, package pruning,
  duplicate assets, and conflicting versions resolve deterministically without
  duplicate diagnostics or whichever-file-loaded-first behavior.
- The consumer never executes producer code or regenerates dependency summaries
  during analysis; package restore remains hermetic and offline-capable.

Evidence: NuGet's [PackageReference asset model](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
defines `buildTransitive` assets that flow to consuming projects, and the
[NuGet package creation reference](https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package)
documents that folder as the convention for transitive MSBuild props and
targets.

## P1 - Integrate trimming and Native AOT contracts into proof evidence

Recognize the platform annotations that describe reflection and dynamic-code
requirements: `DynamicallyAccessedMembers`, `RequiresUnreferencedCode`,
`RequiresDynamicCode`, `DynamicDependency`, and justified
`UnconditionalSuppressMessage`. Propagate them through calls and public
contracts so SharpProof can explain when a pure-looking method is
deployment-dependent, while complementing rather than duplicating or suppressing the .NET
linker's IL warnings.

Acceptance criteria:

- Calls import trimming and dynamic-code requirements from source, metadata,
  overrides, interfaces, delegates, generic parameters, and generated summaries
  using exact attribute identity.
- `DynamicallyAccessedMembers` requirements flow path-sensitively across `Type`
  and string values, returns, fields, generic arguments, and supported reflection
  APIs, with correct member-set containment.
- `RequiresUnreferencedCode` and `RequiresDynamicCode` become structured
  capabilities/effects that propagate to callers unless a supported reachability
  proof excludes the call; a source suppression is recorded as trust evidence,
  not proof of compatibility.
- Runtime feature guards such as `RuntimeFeature.IsDynamicCodeSupported` refine
  only the guarded path and do not erase requirements from other paths or target
  frameworks.
- CI publishes representative consumers with trimming and Native AOT and
  cross-links SharpProof evidence to IL2026/IL207x/IL3050 diagnostics without
  changing their severity or hiding them.

Evidence: Microsoft's [library trimming guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
defines requirement propagation and `DynamicallyAccessedMembers`; the
[Native AOT warning guide](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
defines `RequiresDynamicCode`, reachability guards, and the limits of
suppression.

## P1 - Add a bounded unsafe-memory and interop hazard domain

Build on the planned byref/Span ownership work with a separate domain for raw
pointers, fixed buffers, function pointers, pinning, unmanaged allocation, and
selected `Unsafe`, `MemoryMarshal`, `Marshal`, and `NativeMemory` operations.
Raw pointer operations currently collapse to `unsafe_pointer` unsupported
evidence, even though buffer extent, lifetime, and offsets are often locally
provable. Keep P/Invoke and arbitrary native behavior conservative.

Acceptance criteria:

- Pointer facts track allocation origin, byte extent, element type/size,
  alignment, nullable address, initialized range, offset, pinned scope, ownership,
  and freed state through supported casts and arithmetic.
- Runtime-hazard queries detect locally provable null dereference, out-of-bounds
  access, pointer-arithmetic overflow, use-after-free, double free, stack/pin
  escape, uninitialized read, size overflow, and invalid overlapping copy.
- `fixed`, fixed-size buffers, `stackalloc`, GC pinning, unmanaged allocations,
  and deallocation APIs have distinct lifetime rules; a managed interior pointer
  is never treated as stable after its legal pinning scope.
- Purity and capability evidence distinguish reads from writes and managed from
  unmanaged state, while volatile, atomic, device, shared native, and unknown
  aliases remain conservative.
- P/Invoke, unmanaged callbacks, unverifiable IL, platform-dependent layout,
  unknown native allocators, and budget widening produce stable unknown reasons
  rather than a memory-safety claim.

Evidence: the C# [unsafe-code reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code)
describes pointer operations, raw allocation, fixed buffers, and their security
and stability risks; the normative
[unsafe-code specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/unsafe-code)
defines movable versus fixed storage, pinning lifetime, unmanaged referents, and
the absence of bounds checks for fixed buffers.

## P0 - Establish solution-scale analyzer performance and memory gates

Add reproducible performance suites and release thresholds for the build
analyzer, symbolic API, CLI, and effect-summary tool. SharpProof enables
concurrent Roslyn callbacks and has bounded internal caches, but it has no
checked-in workload, analyzer timing baseline, peak-memory ceiling,
incremental-edit target, or regression policy. Production adoption requires proof work to
remain predictable on large solutions as feature coverage grows.

Acceptance criteria:

- Versioned small, medium, and large workloads cover cold build, warm no-op
  build, one-file edit, one-reference edit, IDE-style cancellation, CLI project
  query, and effect-summary generation without using private customer code.
- CI captures Roslyn `/reportanalyzer` execution time, end-to-end elapsed and CPU
  time, peak working set, allocation/GC counters where stable, SMT query counts,
  cache hit/miss/eviction counts, and diagnostic totals in machine-readable
  artifacts.
- Baselines define platform-normalized median and tail thresholds plus absolute
  memory ceilings. Statistically significant regressions fail a dedicated gate;
  noisy runs are retried by a fixed policy and never silently replace the
  baseline.
- Incremental edits invalidate only affected compilations, summaries, methods,
  and proof obligations; a no-op build performs no effect-summary regeneration
  and no unnecessary solver work.
- Performance modes never weaken soundness, omit required diagnostics, or alter
  evidence determinism. Budget exits remain explicit unknown/truncation results.
- A per-feature attribution report makes expensive analyzers, rules, call-graph
  expansion, and SMT stages actionable before release.

Evidence: the C# compiler's [`ReportAnalyzer` option](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings#reportanalyzer)
exists specifically to report analyzer execution characteristics for analyzer
authors, while Roslyn's
[`EnableConcurrentExecution`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.enableconcurrentexecution)
documents both the performance benefit and the requirement that callbacks be
correct under parallel execution.

## P1 - Add first-class disposable ownership and transfer contracts

Promote SharpProof's local use-after-dispose, double-dispose, and owned-field
checks into an interprocedural resource protocol. Add public contracts for
creates-owned, borrows, consumes, transfers, returns-owned, and disposes so
ownership can cross constructors, factories, wrappers, fields, async calls, and
package boundaries without relying on method-name guesses. Support both
`IDisposable` and `IAsyncDisposable` and interoperate with established CA2000
ownership configuration.

Acceptance criteria:

- Typed state distinguishes uninitialized, live-owned, live-borrowed,
  transferred, maybe-disposed, disposed, and escaped resources, with stable
  identity through aliases and supported wrapper members.
- Contract placement and variance rules prevent a callee, override, delegate,
  or implementation from consuming a borrowed value or weakening an inherited
  disposal guarantee.
- `using`, `await using`, exception paths, early returns, iterator/async state
  machines, constructor failure, field replacement, and ownership-returning
  factories update the same resource state model.
- Diagnostics identify the allocation/acquisition site, every ownership transfer,
  and the leak, use-after-dispose, double-dispose, or invalid consume site in one
  bounded trace.
- Metadata and transitive proof summaries carry resource contracts; an unknown
  external call widens ownership conservatively instead of assuming either
  transfer or retention.
- Configured constructor ownership transfers use the same semantics as .NET
  dispose analysis, with validation for ambiguous or contradictory declarations.

Evidence: Microsoft's [CA2000 rule](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2000)
documents interprocedural disposal analysis and configurable ownership transfer
at constructors; the [.NET dispose pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)
defines deterministic release and ownership of contained disposable instances.

## P1 - Add symbolic collection contents and lazy pipeline semantics

Extend typed IR beyond collection cardinality to bounded sequences, maps,
sets, and iterator pipelines. Model selected contents and mutations for arrays,
lists, dictionaries, sets, immutable collections, and common LINQ operators.
Preserve comparer identity and deferred execution so proofs do not treat query
construction as enumeration or assume that a key lookup succeeds from `Count`
alone.

Acceptance criteria:

- Sequence/map/set terms support bounded element, key membership, associated
  value, uniqueness, order, count, and mutation-version facts with explicit
  widening limits.
- `ContainsKey`, `Contains`, `TryGetValue`, dictionary indexers, add/remove,
  set operations, and immutable updates refine contents according to the actual
  comparer and invalidate only affected facts.
- A proven missing dictionary key exposes `KeyNotFoundException`; a successful
  `TryGetValue` binds its `out` value, while the false path assigns the documented
  default without inventing a mapping.
- Common LINQ operators preserve streaming, buffering, ordering, cardinality,
  predicate, allocation, exception, and complexity behavior; side effects occur
  when enumeration actually evaluates the relevant stage.
- Multiple enumeration, source mutation, custom comparers/enumerators,
  provider-translated `IQueryable`, infinite sources, and unbounded contents stay
  conservative unless a validated summary supplies stronger facts.

Evidence: the [`IDictionary.TryGetValue` contract](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.idictionary-2.trygetvalue)
defines its path-dependent `out` value, while the
[`Dictionary` indexer contract](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.item)
defines missing-key exceptions and insertion on assignment. Microsoft's
[LINQ execution reference](https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries)
distinguishes immediate, deferred-streaming, and deferred-buffering operators.

## P1 - Model atomic operations, volatile accesses, and memory-order effects

Replace the current blanket impurity classification for `Interlocked`,
`Volatile`, volatile fields, and memory barriers with typed concurrent effects.
Represent atomic read-modify-write and ordering guarantees precisely enough to
verify bounded publication and state-transition idioms, while avoiding any claim
of a complete scheduler, race detector, or .NET memory-model proof.

Acceptance criteria:

- IR distinguishes ordinary, volatile, atomic, acquire-like, release-like, and
  full-fence effects and records the exact shared location and alias confidence.
- `CompareExchange` returns the prior value and conditionally updates the target
  as one indivisible transition; exchange, add, increment, decrement, bitwise,
  and atomic read operations preserve their documented value semantics.
- Volatile reads/writes prevent only the documented reorderings. The analyzer
  never treats `volatile` as a lock, a total order, or proof that the latest
  value is observed.
- Supported single-location initialization, flag publication, and lock-free
  retry loops can use atomic facts; ABA, multiple-location invariants, weak
  aliases, custom native atomics, and unbounded thread interleavings remain
  unknown.
- Purity and capability reports expose synchronization reads, writes, and fences
  separately, and complexity accounts for bounded retry evidence without
  assuming contention-free progress.
- Differential stress tests exercise supported patterns on every shipping
  runtime architecture and reject nondeterministic or architecture-specific
  proof upgrades.

Evidence: Microsoft's [`Volatile` reference](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile)
defines its asymmetric reordering barriers and atomic 64-bit access, while the
[synchronization overview](https://learn.microsoft.com/en-us/dotnet/standard/threading/overview-of-synchronization-primitives)
defines `Interlocked` operations as atomic. The C#
[`volatile` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/volatile)
explicitly warns that volatile access does not provide a single total order or
guarantee observation of the latest write.

## P1 - Make string proofs comparer-, culture-, and Unicode-aware

Add an explicit text-semantics profile to every symbolic string operation.
Support exact ordinal and bounded ordinal-ignore-case facts first, then model
invariant or named-culture operations only where versioned runtime data makes
them reproducible. Track Unicode normalization form and never substitute
ordinal equality for linguistic comparison, casing, search, sorting, or regex
behavior.

Acceptance criteria:

- String equality, comparison, prefix/suffix/contains, indexing, replacement,
  casing, sorting, hashing, dictionary comparers, and regex entry points retain
  their overload's exact `StringComparison`, `StringComparer`, `CultureInfo`,
  `CompareOptions`, and normalization context.
- Ordinal reasoning uses UTF-16 code-unit semantics including embedded nulls,
  surrogate code units, and positive/negative search results;
  ordinal-ignore-case uses the runtime's documented invariant case mapping rather than ASCII
  lowercasing.
- `Normalize` and `IsNormalized` preserve NFC, NFD, NFKC, and NFKD facts and
  expose unsupported or invalid Unicode shapes explicitly.
- Current-culture operations carry ambient-state capability evidence and cannot
  be cached across culture mutation. Named-culture conclusions record runtime,
  globalization mode, Unicode/ICU or NLS version, and platform provenance.
- Cross-platform or version-dependent collation returns unknown unless the
  query pins a compatible globalization profile; invariant mode is not silently
  treated as ordinal mode.
- Witnesses serialize exact UTF-16 content and comparison context so
  counterexamples containing combining marks, surrogate pairs, nulls, or
  culture-specific casing remain reproducible.

Evidence: Microsoft's [string comparison guidance](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)
documents differing defaults, embedded-null behavior, ordinal versus linguistic
semantics, ICU/NLS differences, and Unicode-version sensitivity. The
[`String.Normalize` reference](https://learn.microsoft.com/en-us/dotnet/api/system.string.normalize)
defines the four Unicode normalization forms supported by .NET.

## P0 - Isolate native SMT work from compiler and IDE hosts

Move Z3 execution behind a versioned local worker protocol for the shipping
analyzer and IDE path. SharpProof currently loads the native Z3 library into the
analysis host and can translate managed failures into unknown results, but that
cannot contain a native crash, process-wide out-of-memory failure, or solver
hang. Preserve an explicitly opt-in in-process mode for trusted standalone API
callers only if benchmarks justify it.

Acceptance criteria:

- A bounded worker pool communicates over an authenticated same-user local pipe
  or equivalent transport with a length-delimited protocol, schema handshake,
  solver version, SharpProof version, runtime identifier, and hard message-size
  limits.
- Requests contain normalized proof obligations and the minimum required symbol
  metadata rather than whole source files; responses carry the existing truth,
  witness, evidence, and stable unknown-reason model.
- Every request has cancellation, wall-time, solver-resource, and response-size
  budgets. Workers also have process-level memory and CPU ceilings and are
  killed and replaced after a hang, crash, protocol violation, or limit breach.
- Worker startup, queueing, retry, and recycling are bounded. Backpressure
  prevents parallel compilations or rapid IDE edits from creating an unbounded
  process, request, or memory backlog.
- Crash, timeout, out-of-memory, cancellation, incompatible protocol, and native
  load failures produce distinct deterministic unknown reasons without
  terminating or wedging `csc`, the compiler server, MSBuild, or Visual Studio.
- Packaged workers and native assets are signed and RID-specific, never listen
  on a network interface, do not download code, and reject clients outside the
  intended user and session boundary.
- Fault-injection tests force access violation or abrupt exit, infinite work,
  memory exhaustion, malformed and oversized messages, cancellation races, and
  stale-worker reuse; the build and IDE host must remain responsive and recover
  without manual cleanup.

Evidence: Z3's official [parameter reference](https://microsoft.github.io/z3guide/programming/Parameters/)
documents that solver timeout and memory limits are configurable and otherwise
can be unlimited. Roslyn's [compiler server architecture](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Compiler%20Server.md)
uses a recoverable local client/server protocol and explicitly treats server
termination as safe, a useful precedent for containing native analysis work.

## P1 - Add bounded lock-order and deadlock analysis

Build on the synchronization capability and atomic-effect work with a bounded,
interprocedural lock-order graph. Diagnose feasible circular waits and invalid
lock-mode transitions across `lock`, `System.Threading.Lock`, `Monitor`,
`Mutex`, `SemaphoreSlim`, and `ReaderWriterLockSlim` without claiming a complete
scheduler or race proof.

Acceptance criteria:

- The analysis assigns stable abstract identities to lock objects and records an
  edge from every lock currently held to a newly acquired lock, including edges
  imported from validated transitive summaries.
- A diagnostic requires a feasible bounded cycle and reports each acquisition,
  call edge, held-lock fact, and source location in cycle order; alias ambiguity
  weakens the result instead of inventing distinct locks.
- Acquisition and release semantics cover `lock`, `Enter`/`Exit`, scoped locks,
  `TryEnter`, timed and cancellation-aware waits, semaphore counts, mutex
  ownership, reader, writer, and upgradeable modes, and configured recursion
  policies.
- Conditional acquisitions add held-lock facts only on their success paths.
  Exceptional exits, unbalanced release, recursion, upgrades, downgrades, and
  disposal update or invalidate the graph according to the documented API.
- The same model flags blocking waits, callbacks, or unknown external calls
  while holding a lock and composes with SharpProof's existing sync-over-async
  diagnostic, while compiler-rejected `await` inside `lock` is not duplicated.
- Users can declare a reviewed global lock order or suppress a specific
  intentional cycle with justification; metadata summaries preserve identities
  only when their ownership and alias meaning are stable across assemblies.
- Tests include opposite-order calls through interfaces and recursion, reader
  upgrades, failed `TryEnter`, cancellation, shared public locks, and paths whose
  guards make an apparent cycle infeasible.

Evidence: Microsoft's [guidance for preventing application hangs](https://learn.microsoft.com/en-us/windows/win32/win7appqual/preventing-hangs-in-windows-applications)
explains circular wait and recommends a consistent acquisition order. The
[`ReaderWriterLockSlim` reference](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-threading-readerwriterlockslim)
documents mode, recursion, upgrade, and invalid-transition rules that a sound
lock-state model must preserve.

## P1 - Verify equality, hashing, and comparer contracts

Go beyond the platform's declaration-shape warnings by proving bounded
behavioral consistency among `Equals`, `IEquatable<T>.Equals`, equality
operators, `GetHashCode`, and custom `IEqualityComparer<T>` implementations.
Feed validated results into symbolic collection contents rather than assuming a
comparer is lawful because it implements the interface.

Acceptance criteria:

- Generated obligations cover reflexivity, symmetry, transitivity, null
  behavior, `Equals(object)` and typed-`Equals` agreement, equality-operator
  agreement, and the rule that equal values have equal hash codes.
- Counterexamples identify the minimum conflicting methods and concrete object
  state. Unbounded heaps, native identity, reflection, randomness, mutable
  global state, and unsupported inheritance return explicit unknown results.
- Records, tuples, anonymous types, enums, primitive types, nullable values, and
  compiler-generated equality are recognized from their actual runtime and
  language semantics rather than re-proved as opaque user code.
- Inheritance analysis respects runtime type checks, sealed versus extensible
  types, base-state participation, overrides, and interface dispatch; it does
  not assume equality can be made symmetric across an open hierarchy.
- Hash proofs compare only the required equality relationship. They never
  require stable numeric hash values across processes, runtimes, architectures,
  or randomized string-hashing seeds.
- Stateful and culture-sensitive comparers retain identity, configuration, and
  mutation dependencies. Dictionary and set proofs use their exact comparer and
  downgrade when its lawfulness or stability is unknown.
- Differential and mutation tests include NaN and signed zero, strings under
  different comparers, inheritance traps, mutable equality fields, constant
  hashes, records, and deliberately asymmetric or nontransitive comparers.

Evidence: the [`Object.GetHashCode` contract](https://learn.microsoft.com/en-us/dotnet/api/system.object.gethashcode)
requires values considered equal to produce the same hash code, while the
[framework equality-operator guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/equality-operators)
require operators and `Object.Equals` to have the same semantics. Microsoft's
[value-equality guidance](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/statements-expressions-operators/how-to-define-value-equality-for-a-type)
also documents the coordinated implementation surface.

## P1 - Analyze regex complexity, timeouts, and engine semantics

Extend the existing partial regex lowering and null-input hazard checks with a
security and performance analysis for .NET regular expressions. Track pattern,
input trust, engine, options, and effective timeout so attacker-controlled or
unbounded inputs cannot silently reach catastrophic backtracking.

Acceptance criteria:

- The model distinguishes the standard backtracking engine,
  `RegexOptions.NonBacktracking`, source-generated regexes, compiled regexes,
  and unsupported custom or provider engines without treating compilation as a
  complexity guarantee.
- Constant patterns receive a conservative structural complexity class that
  accounts for ambiguous alternation, nested or overlapping quantifiers,
  backreferences, lookarounds, atomic groups, anchors, and near-match behavior;
  dynamic patterns retain taint and become unknown when structure is unavailable.
- Effective timeout flows through constructors, static overloads, generated
  regex metadata, and the application default. `Regex.InfiniteMatchTimeout` or
  an absent default remains explicitly unbounded.
- A high-confidence diagnostic requires a risky backtracking pattern plus an
  unbounded or attacker-controlled input and no effective finite timeout. A
  separate lower-severity result can recommend `NonBacktracking` only when the
  pattern uses no incompatible constructs or capture behavior.
- Runtime-hazard proofs model invalid patterns, invalid option combinations,
  invalid timeouts, and `RegexMatchTimeoutException`; catch-and-retry loops do
  not count as mitigation unless they impose a proven total bound.
- Symbolic match results preserve exact options, culture, engine restrictions,
  and unsupported constructs. Approximate Z3 translations never upgrade a
  runtime result or performance claim to proven.
- Tests use matching, rejecting, and near-matching corpora with length scaling,
  tainted patterns and inputs, application defaults, generated regexes, and
  differential checks against every supported .NET runtime.

Evidence: Microsoft's [.NET regex best practices](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-regex)
warn that untrusted input without a timeout can enable denial of service and
that some near matches can take hours or days. The official
[backtracking reference](https://learn.microsoft.com/en-us/dotnet/standard/base-types/backtracking-in-regular-expressions)
documents the default infinite timeout and the linear-time
`RegexOptions.NonBacktracking` alternative with its semantic restrictions.

## P1 - Verify nullable flow attributes against their implementations

Stop treating source-declared `System.Diagnostics.CodeAnalysis` attributes as
unconditionally trustworthy. Prove the positive promises made by `NotNull`,
`NotNullWhen`, `NotNullIfNotNull`, `MemberNotNull`, `MemberNotNullWhen`,
`DoesNotReturn`, and `DoesNotReturnIf` before those facts can strengthen other
SharpProof results. Keep permissive annotations such as `MaybeNull` and
`AllowNull` as type-contract inputs rather than obligations they do not express.

Acceptance criteria:

- Each reachable normal completion is checked against unconditional nullable
  postconditions; conditional postconditions are checked only on return paths
  whose Boolean or input-null condition matches the attribute argument.
- `ref`, `out`, return, property, field, indexer, and named-member targets use
  the same alias-aware null-state model as `[Ensures]`, including exceptional
  exits, constructor completion, and supported async result paths.
- `DoesNotReturn` proves that no normal completion is reachable, and
  `DoesNotReturnIf` proves that property only for the designated parameter
  value. Infinite loops count only when nontermination is itself established
  within a documented bound.
- The implementation proof cannot assume the attribute currently being
  validated or another contract in the same dependency cycle. Contradictory,
  malformed, inaccessible, or missing member names produce a direct diagnostic
  rather than an optimistic flow fact.
- Failed proofs report the attribute, promised state, violating return or
  fall-through path, and a bounded counterexample. Unsupported bodies remain
  explicit unknowns and cannot strengthen downstream proofs in strict mode.
- Metadata-only framework and package annotations have configurable trust
  levels. Validated transitive summaries record provenance, schema version, and
  assembly identity so package consumers do not repeat source proofs.
- Override, interface, delegate, and package-compatibility checks consume the
  validated nullable contract and reject substitutability breaks without
  duplicating ordinary compiler nullable warnings.

Evidence: the C# reference for [nullable static-analysis attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
defines each precondition, postcondition, conditional postcondition, and
nonreturning promise, and explicitly states that the attributes do not enable
additional checks on their implementations. That creates a direct trust
boundary for SharpProof, which already imports those facts into stronger proofs.

## P1 - Add cancellation protocol and responsiveness contracts

Extend the task-aware contract item with a general cooperative-cancellation
state model. Track token identity, linkage, observation, forwarding, callback
registration, and canceled completion across synchronous and asynchronous code.
This should prove protocol correctness and bounded observation points, not
promise wall-clock latency or preemptive cancellation.

Acceptance criteria:

- Symbolic state distinguishes a noncancelable token, a cancelable token with
  unknown state, cancellation requested, request observed, and a token linked
  to one or more source tokens, while retaining stable token identity through
  fields, parameters, and supported wrappers.
- `IsCancellationRequested`, `ThrowIfCancellationRequested`, cancellation
  callbacks, `Cancel`, `CancelAfter`, and linked token sources refine state with
  their documented races, synchronous-callback behavior, and exception paths.
- A cancellation contract can require that a public operation forward a
  designated token to every cancelable transitive operation and reach an
  observation point on every bounded loop or work chunk before a declared work
  bound is exceeded.
- `OperationCanceledException` carries the originating token. Canceled versus
  faulted task state follows token matching, and catch filters that swallow,
  replace, or retry cancellation are visible in exception evidence.
- `CancellationToken.Register` and source lifetimes integrate with disposable
  ownership analysis; escaping registrations, leaked linked sources, callbacks
  under locks, and disposal races remain conservative unless proven safe.
- The analyzer does not report a duplicate of CA2016 for simple forwarding.
  It reports only contract violations that require path, identity, loop, or
  interprocedural evidence, with a trace from entry token to the missing or
  mismatched observation site.
- Tests cover default tokens, already-canceled tokens, linked sources, timeouts,
  token mismatch, nested wrappers, CPU loops, async streams, callback races,
  catch-and-rethrow, and cleanup that must run during cancellation.

Evidence: Microsoft's [task cancellation model](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation)
defines cancellation as cooperation and explains that an
`OperationCanceledException` with a mismatched token faults rather than cancels
a task. The platform's [CA2250 guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2250)
identifies `ThrowIfCancellationRequested` as the canonical observation and
throw operation.

## P1 - Add a versioned temporal proof domain

Introduce typed symbolic semantics for `DateTime`, `DateTimeOffset`, `DateOnly`,
`TimeOnly`, `TimeSpan`, calendars, and `TimeZoneInfo`. Separate civil time,
offset time, UTC instants, and ambient clock reads so arithmetic and conversion
proofs preserve `DateTime.Kind`, offsets, daylight-saving transitions, calendar
ranges, and platform time-zone provenance.

Acceptance criteria:

- Terms represent ticks or supported calendar fields with exact valid ranges,
  `DateTimeKind`, UTC offset, and optional time-zone identity. Constructors and
  arithmetic expose documented range and invalid-component exceptions.
- Equality, ordering, subtraction, Unix-time conversion, date-only and time-only
  projection, parsing, formatting, and round-trip formats preserve the distinct
  semantics of each temporal type instead of reducing them to plain integers.
- Time-zone conversion recognizes unique, ambiguous, and invalid local times.
  A fold produces both possible instants unless policy resolves it; a gap
  exposes the documented failure rather than inventing an instant.
- Arithmetic distinguishes elapsed-duration calculations on instants from
  civil-time calculations under a zone's adjustment rules. It does not assume
  that every local day has 24 hours or that a `DateTimeOffset` identifies a
  geographic time zone.
- `Now`, `UtcNow`, `Today`, local-zone access, and mutable time providers retain
  `Clock` or environment capability evidence. Injected `TimeProvider` instances
  can be modeled from validated summaries without turning ambient time into a
  constant.
- Culture-dependent parse and format operations compose with the text-semantics
  profile. Unknown calendar, custom format provider, leap-second policy, or
  unsupported calendar arithmetic returns unknown.
- Zone-dependent proof evidence pins operating system, Windows or IANA zone ID,
  adjustment-rule database version, globalization mode, and rule snapshot;
  caches invalidate when that profile changes.

Evidence: Microsoft's [temporal type selection guidance](https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime)
distinguishes civil values from unambiguous `DateTimeOffset` instants and notes
that an offset is not a time-zone identity. The official
[time-zone arithmetic guidance](https://learn.microsoft.com/en-us/dotnet/standard/datetime/use-time-zones-in-arithmetic)
states that ordinary `DateTime` and `DateTimeOffset` arithmetic does not apply
time-zone adjustment rules, while [`TimeZoneInfo.ConvertTime`](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.converttime)
documents ambiguous-time interpretation and invalid-time exceptions.

## P1 - Verify effective System.Text.Json contracts and round trips

Grow the existing serialization-cycle and attribute-mismatch diagnostics into
a bounded `System.Text.Json` contract model. Resolve the effective type shape
and options at each call, then prove requiredness, nullability, constructor
binding, naming, polymorphism, and supported round-trip properties. Keep custom
converters and runtime-mutated metadata conservative unless their behavior is
available through a validated summary.

Acceptance criteria:

- The effective contract merges attributes, `JsonSerializerOptions`, generated
  `JsonSerializerContext`, `JsonTypeInfo`, resolver chains, naming and number
  policies, ignore rules, include rules, reference handling, and framework
  defaults for the selected target runtime.
- Deserialization distinguishes a missing member from an explicit JSON `null`.
  It models `required`, `JsonRequired`, required constructor parameters,
  `RespectNullableAnnotations`, defaults, extension data, unmapped-member
  policy, and the resulting `JsonException` paths.
- Constructor and member binding proves unique effective JSON names after
  case-sensitivity and naming policies, respects accessibility and init-only
  behavior, and diagnoses collisions or reserved metadata names before runtime.
- Polymorphic analysis validates unique discriminators, declared derived types,
  unknown-derived and unknown-discriminator policies, base fallback data loss,
  interface hierarchies, and reference-preservation metadata.
- A round-trip contract states which observable members, runtime type, object
  identity relationships, and comparer or temporal context must survive
  `Deserialize(Serialize(value))`; lossy options and ignored members are part of
  the stated projection rather than assumed preserved.
- Source-generation mode and reflection mode use their actual supported feature
  sets. Unsupported fast-path fallbacks and custom converters stay unknown and
  integrate with, but do not duplicate, trimming and Native AOT diagnostics.
- Diagnostics include the serializer call, effective contract provenance,
  conflicting member or option, payload shape witness where bounded, and the
  runtime exception or round-trip fact at risk.

Evidence: Microsoft's [required-property guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties)
documents required members, constructor-parameter policy, and missing-member
exceptions. The [nullable-annotation reference](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations)
explains that nullability and presence are independent, while the official
[polymorphism reference](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)
documents explicit derived-type opt-in, discriminator behavior, fallback, and
source-generation restrictions.

## P1 - Add exact-width integral, overflow, and bit-vector semantics

Replace the current opaque fallback for wrap-capable arithmetic with a typed
fixed-width domain for C# integral values. Preserve width, signedness, numeric
promotion, overflow context, and target architecture through arithmetic,
conversions, bit operations, comparisons, enums, and native integers, while
bridging to mathematical integers only when proven ranges make that sound.

Acceptance criteria:

- IR represents `sbyte`, `byte`, `short`, `ushort`, `char`, `int`, `uint`,
  `long`, `ulong`, enums, `nint`, and `nuint` at their actual width and
  signedness. Native-width terms pin a 32-bit or 64-bit runtime profile.
- Unary and binary numeric promotion, constant-expression typing, compound
  assignment conversion, enum underlying types, and implicit and explicit
  conversions follow the C# language rules rather than operand spelling.
- `checked`, `unchecked`, checked user-defined operators, and the project-wide
  overflow option produce exact normal and `OverflowException` paths. Decimal
  and floating-point operations remain in their dedicated domains.
- Unchecked addition, subtraction, multiplication, negation, increment,
  decrement, and narrowing conversions truncate or wrap at the correct width.
  Checked operations prove the mathematical result is in range before exposing
  a normal value.
- Complement, AND, OR, XOR, left shift, arithmetic right shift, unsigned right
  shift, rotate, leading/trailing-zero, and population-count models preserve
  promotions, sign extension, discarded bits, and C# shift-count masking.
- Division and remainder retain divide-by-zero and signed-minimum divided by
  negative-one hazards. Algebraic rewrites are applied only when they preserve
  finite-width and exceptional behavior.
- Witnesses show decimal and fixed-width hexadecimal values, type, overflow
  context, and architecture. Differential tests cover every boundary, negative
  operand, oversize and negative shift count, cast pair, compiler option, and
  supported runtime architecture.

Evidence: the C# [`checked` and `unchecked` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked)
defines throwing versus high-bit truncation, the affected operations, constant
expressions, and the project default. The official
[bitwise and shift reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators)
defines integral promotion, signed and unsigned right shifts, and masked shift
counts, while the [type specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types)
defines widths, ranges, and native-integer precision.

## P1 - Give iterators and async streams state-machine contracts

Model iterator creation, enumeration, suspension, completion, failure, and
disposal as distinct phases. Let contracts describe each yielded element,
bounded sequence properties, enumeration-time effects and exceptions, and
cleanup guarantees for compiler-generated and source-visible enumerators. This
extends lazy collection semantics to custom state machines rather than treating
an iterator method like an eagerly executed ordinary method.

Acceptance criteria:

- Calling an iterator method creates an enumerable or enumerator without
  executing deferred body effects. `MoveNext` or `MoveNextAsync` advances a
  typed program counter, publishes `Current`, and resumes after the matching
  `yield return`.
- Contracts can express an invariant for every yielded element, a relation to
  inputs and prior elements, finite lower or upper cardinality when provable,
  and conditions for normal exhaustion. Infinite or data-dependent streams
  remain explicitly unbounded.
- Capabilities, allocation, complexity, exceptions, ownership, and mutations
  are assigned to creation, each advancement, successful yield, exhaustion,
  and disposal at the phase where they actually occur.
- Early `break`, consumer exception, failed advancement, `yield break`, normal
  exhaustion, `Dispose`, and `DisposeAsync` execute the correct pending
  `finally` and `using` cleanup paths exactly once within the modeled protocol.
- Re-enumerable values, single-use enumerators, struct enumerators, custom
  pattern-based enumerators, multiple enumeration, and concurrent or reentrant
  advancement retain distinct identity and validity states.
- Async iterator tokens combine according to `EnumeratorCancellation` and
  `GetAsyncEnumerator`; cancellation, faults, successful yields, and async
  cleanup compose with the cancellation and task-aware contract models.
- Tests cover no-enumeration, partial and repeated enumeration, consumer
  failure, resources spanning yields, nested iterators, async cancellation,
  failed disposal, C# 13 byref-safe shapes, and unsupported custom awaiters.

Evidence: the C# [`yield` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/yield)
documents deferred execution, suspension and resumption, early-disposal
cleanup, and async iterators. The language specification for
[`EnumeratorCancellation`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes#2358-the-enumeratorcancellation-attribute)
defines how method and enumeration tokens are linked and observed by
`MoveNextAsync`.

## P1 - Add first-class higher-order and callback contracts

Allow delegate types and parameters to carry preconditions, postconditions,
effects, capabilities, exceptions, allocation, complexity, and invocation-count
requirements. Verify a supplied method group, lambda, local function, closure,
or composed delegate against that contract and import the contract at indirect
calls when the concrete target is not statically recoverable.

Acceptance criteria:

- A callback contract defines parameter and result relations, allowed effects
  and exceptions, and lower and upper invocation counts, including zero,
  exactly once, at most once, per element, and unknown or unbounded invocation.
- Delegate variance checks use behavioral substitutability: accepted input
  domains cannot narrow, promised results cannot weaken, and effect, exception,
  allocation, and complexity budgets cannot exceed the receiving contract.
- Known method groups, static and capturing lambdas, anonymous methods, local
  functions, delegate fields, conditionals, assignments, returns, and bounded
  invocation lists retain target sets and validated summaries across calls.
- Closures capture variables by reference with extended lifetime. Reads,
  writes, shared aliases, disposal, heap allocation, and post-creation mutation
  of captured state flow into purity, ownership, race, and zero-allocation
  results; static lambdas prove the absence of captures.
- Multicast invocation preserves target order, last-result behavior, per-target
  effects, and exception short-circuiting. Addition, removal, null delegates,
  variance conversion, and unsupported `DynamicInvoke` never collapse to a
  single optimistic target.
- Expression trees remain data until interpreted or compiled. Compilation
  exposes dynamic-code, allocation, capture-lifetime, and subsequent delegate
  behavior, while provider translation stays behind its own validated boundary.
- Diagnostics trace a callback requirement to the incompatible target or
  invocation site. Tests cover target reassignment, mixed invocation lists,
  closure mutation, disposed captures, async callbacks, variance, expression
  trees, recursion, and unknown external callback storage.

Evidence: Microsoft's [lambda and delegate guide](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/delegates-lambdas)
defines closures as a lambda plus its captured variables and distinguishes
capture-free static lambdas. The official [delegate guide](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates)
documents multicast invocation and exception propagation, while the
[expression-tree execution reference](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/expression-trees/expression-trees-execution)
documents compilation and captured-resource lifetime hazards.

## P1 - Generalize SP0061 into bounded data-race analysis

Extend the current warning for unsynchronized mutation in known parallel
callbacks into a shared-location and happens-before analysis. Report two
feasible conflicting accesses when at least one writes and no modeled ordering
or mutual exclusion protects them. Reuse the synchronization, atomic, lock,
ownership, and callback backlog models without claiming exhaustive thread
interleaving or a stronger memory model than the selected .NET runtime profile.

Acceptance criteria:

- Shared-location identity covers static and instance fields, captured locals,
  array elements, supported span or byref aliases, and owned objects that escape
  to multiple concurrency roots, with field and index sensitivity under bounds.
- Concurrency roots include threads, tasks, parallel loops, timers, registered
  callbacks, continuations, events, async-stream consumers, and recognized
  framework schedulers. Sequential invocation or a proven join does not create
  a false overlap.
- Conflicting read/write and write/write pairs carry path and lifetime
  feasibility. Thread confinement, immutability, ownership transfer, and
  nonoverlapping partitions eliminate candidates only with positive evidence.
- Happens-before edges cover thread or task start and completion, awaited joins,
  monitor and lock release/acquire, supported synchronization primitives,
  interlocked operations, volatile publication within its documented limits,
  and validated library summaries.
- Compound operations such as increment and check-then-act remain non-atomic
  even when their component reads and writes are individually atomic. Volatile
  access never substitutes for a multi-location invariant or mutual exclusion.
- A diagnostic shows both access traces, the common abstract location,
  concurrency roots, held locks and ordering facts, and the first missing edge.
  Low-confidence alias or scheduler cases remain reviewable unknown evidence.
- Differential stress fixtures and deterministic scheduler tests cover
  publication, double-checked initialization, parallel partitions, escaping
  closures, dispose races, atomics, false sharing without a semantic race, and
  architecture-specific atomicity boundaries.

Evidence: Microsoft's [multithreaded data-synchronization guidance](https://learn.microsoft.com/en-us/dotnet/standard/threading/synchronizing-data-for-multithreading)
states that unsynchronized concurrent member access can interrupt an operation
and leave an object invalid. Its [security race guidance](https://learn.microsoft.com/en-us/dotnet/standard/security/security-and-race-conditions)
shows exploitable dispose, construction, cache, and finalizer races. The .NET
runtime's [ECMA-335 augments](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Ecma-335-Augments.md)
also delimit which aligned primitive reads and writes the runtime guarantees to
be atomic.

## P1 - Add inductive loop invariants and termination variants

Add a source-level contract surface for loop invariants and well-founded
decreases measures. Use invariants inductively instead of relying only on
bounded unrolling, and use variants to prove termination of loops and recursive
call components when total correctness is requested. Keep an explicit partial
correctness mode for intentional servers, event loops, and streams.

Acceptance criteria:

- A stable analyzer-recognized syntax attaches one or more invariant expressions,
  a modifies set, and an optional decreases tuple to `for`, `foreach`, `while`,
  and `do` statements without changing release runtime behavior.
- Verification proves every invariant before the first test, assumes it at an
  arbitrary iteration, proves it after every normal back edge, and combines it
  with the negated guard on normal loop exit. It never treats sampled iterations
  as an induction proof.
- State modified by the loop body is conservatively havoced before the
  inductive step except where the invariant, frame, ownership, or alias facts
  retain a relation. `break`, `continue`, `return`, `goto`, exceptions, and
  cancellation use their actual exit edges.
- Decreases expressions are side-effect free, bounded below in a documented
  well-founded order, and strictly smaller on every feasible back edge.
  Lexicographic tuples support nested phases and mutually recursive call graphs.
- Simple counter loops and structural recursion may infer candidate invariants
  or variants, but inferred facts are accepted only after the same proof as
  explicit annotations and are shown in evidence.
- Total-correctness contracts distinguish normal termination, exceptional
  completion, cancellation, and intentionally permitted divergence. Unknown
  termination cannot silently satisfy `[DoesNotThrow]`, postconditions, cleanup,
  or complexity claims that require completion.
- Diagnostics identify invariant initialization or preservation failure, the
  precise back edge, old and new variant values, modified state, and a bounded
  counterexample. Tests cover nested loops, mutation through aliases, recursion
  components, iterator loops, async loops, early exits, and deliberate infinity.

Evidence: the official Dafny [reference manual](https://dafny.org/dafny/DafnyRef/DafnyRef)
explains that unbounded loop reasoning requires invariants that hold initially
and after each body execution. Its [termination guide](https://dafny.org/dafny/OnlineTutorial/Termination)
defines bounded, strictly decreasing measures for loops, recursion, tuples, and
explicitly permitted divergence.

## P1 - Add explicit reads and modifies frame contracts

Let methods declare the heap locations they may read and modify. Use these frame
contracts both as checked effect guarantees and as modular proof boundaries, so
a caller preserves facts about locations outside a callee's write frame instead
of invalidating the whole reachable heap or trusting purity as an all-or-nothing
substitute.

Acceptance criteria:

- Public contracts express exact instance and static fields, array or span
  regions, object-field sets, owned object graphs, parameters, `this`, fresh
  allocations, and a deliberate wildcard. Invalid, inaccessible, or unstable
  target paths are rejected at the declaration.
- Every direct and indirect write through assignment, property or event access,
  `ref`/`out`, collection mutation, interlocked operation, unsafe memory, native
  call, callback, or transitive callee is proved inside the effective modifies
  frame or reported with its alias path.
- Reads contracts bound dependencies for pure functions, memoization,
  determinism, and callback summaries. Ambient capabilities remain separate
  even when they do not correspond to a managed heap location.
- At a call, only the declared write frame and conservatively aliasing locations
  are havoced. Facts for disjoint locations survive, while unknown external or
  wildcard frames never preserve optimistic state.
- Constructors, initializers, setters, events, iterators, async state machines,
  ownership transfer, disposal, and exception paths use phase-appropriate
  frames; newly allocated unescaped objects do not count as caller-visible
  mutation until they escape.
- Overrides and interface implementations may narrow but not widen an inherited
  modification guarantee. Delegate, package-compatibility, and transitive proof
  summaries carry the same canonical frame identity and schema.
- Frame checks compose with object invariants, data-race analysis, and lock
  protection. Tests cover aliasing parameters, field replacement versus
  mutation of the referenced object, partial array regions, callbacks, native
  writes, hidden setters, and disjoint-state preservation.

Evidence: Dafny's [framing specification](https://dafny.org/dafny/DafnyRef/DafnyRef#sec-frame-expression)
defines reads and modifies expressions as sets of heap locations and explains
that they enable sound one-method-at-a-time reasoning. Its
[modifies-clause reference](https://dafny.org/latest/HowToFAQ/FAQModifiesThis)
distinguishes modifying an object, a referenced object, and one specific field.

## P1 - Support signed, versioned third-party library model packs

Create a data-only model-pack format for libraries whose source was not built
with SharpProof. A model pack should provide reviewed contracts and symbolic
semantics for exact package or assembly versions, distributed independently or
with the library through NuGet. This extends transitive SharpProof summaries;
it does not turn broad `[PureExternal]` assertions into complete proof models.

Acceptance criteria:

- The versioned schema can describe preconditions, postconditions, exceptional
  outcomes, frames, ownership, nullability, capabilities, allocation,
  complexity, callback behavior, determinism, and supported symbolic lowering
  for canonical member identities.
- Applicability is bound to package identity and version, assembly name, public
  API fingerprint, module or file hash where available, target framework,
  runtime version range, RID or architecture when relevant, and model-schema
  compatibility. A near match is rejected rather than guessed.
- Packs are declarative and cannot execute arbitrary code in the compiler or
  IDE process. Parsing has size, depth, count, and time budgets and produces
  stable diagnostics for malformed or unsupported content.
- NuGet packages can deliver model assets through explicit analyzer-visible or
  build-transitive items. Direct project configuration and a lock file record
  the resolved pack, source, content hash, signer, trust decision, and precedence.
- Built-in, package-supplied, organization, and project-local models have a
  deterministic override policy. Conflicting facts fail closed and identify
  both sources instead of selecting by load order.
- Tooling validates member binding, satisfiability, contradictory contracts,
  schema compatibility, and optional conformance suites against reference
  source or runtime behavior. Expired, revoked, or vulnerable packs can be
  diagnosed without silently downloading replacements during a build.
- Evidence and SARIF identify every imported fact and trust boundary. Tests
  cover overload drift, assembly replacement, multi-target packages, type
  forwarding, signed and unsigned packs, conflict resolution, offline restore,
  and malicious oversized inputs.

Evidence: Roslyn exposes [analyzer additional files](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analyzeroptions.additionalfiles)
as non-code text inputs available to analyzers. NuGet's
[PackageReference asset model](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
defines analyzer, content, build, and `buildTransitive` assets plus propagation
controls, providing the standard delivery path for declarative model files.

## P1 - Export portable proof obligations for deterministic replay

Add an opt-in artifact that captures the exact solver question behind a proof,
counterexample, or unknown result. Emit canonical SMT-LIB plus a SharpProof
manifest that maps declarations and assertions back to source and typed IR, so
users can reproduce failures outside the analyzer host, minimize regressions,
compare solver versions, and audit what was actually proved.

Acceptance criteria:

- Each export contains a canonical standalone SMT-LIB script, named assertions,
  selected logic, options and resource limits, expected result, and a manifest
  with SharpProof, evidence-schema, solver, runtime, architecture, and encoding
  versions.
- The manifest records source, assembly, configuration, model-pack, summary,
  and normalized-IR content hashes plus a bidirectional map from assertions and
  model symbols to contract clauses, paths, spans, and witness fields.
- A hermetic replay command runs the exact supported solver build without source
  reanalysis, enforces the recorded budgets, verifies parser output, and reports
  match, changed result, timeout, unsupported feature, malformed artifact, or
  nondeterministic evidence with stable exit codes.
- Satisfiable obligations preserve model values needed to reconstruct the
  witness. Unsatisfiable obligations optionally include named unsat cores and
  solver proof logs only when the selected engine and theory support them;
  replay alone is not mislabeled as an independently checked certificate.
- Canonicalization removes timestamps, temporary paths, random symbol names,
  locale, and host ordering. Repeated exports of the same normalized obligation
  are byte-identical across equivalent machines and safe to use as cache keys.
- Export is off by default in IDE builds, bounded in size, and applies explicit
  redaction or allow-list policy to string literals, paths, symbol names, and
  model values. A redacted artifact states which replay properties were lost.
- A reducer can minimize assertions while preserving the selected result and
  source map. CI fixtures replay a corpus across the shipping solver matrix and
  detect encoding drift separately from source-analysis drift.

Evidence: the official [SMT-LIB standard](https://smt-lib.org/papers/smt-lib-reference-v2.7-r2025-07-07.pdf)
defines portable scripts, solver options, model, proof, and unsat-core commands.
Z3 exposes [SMT-LIB interaction logging](https://microsoft.github.io/z3guide/programming/Parameters/)
and its [proof-log guide](https://microsoft.github.io/z3guide/programming/Proof%20Logs/)
documents command-line inference logging, replay, checking, and the limitations
of its SMT-LIB extension.

## P1 - Add bounded universal and existential contract quantifiers

Extend the contract language with `forall` and `exists` over finite integer
ranges and supported collection domains. This lets preconditions, postconditions,
loop invariants, object invariants, and model packs express element-wise facts,
uniqueness, membership, and permutation properties that cannot be reduced to a
few sampled indexes.

Acceptance criteria:

- The grammar supports typed bound variables, integer half-open ranges, arrays,
  spans, bounded sequences, sets, and map keys or entries, with explicit nesting
  and normal C#-like name shadowing and member binding rules.
- Quantifier predicates are side-effect free and may use supported pure calls,
  `old(...)`, `result`, parameters, current-instance state, prior bound
  variables, and collection facts without capturing mutable state ambiguously.
- Empty-domain semantics are exact: universal statements are true and
  existential statements are false. Invalid bounds, mutation during evaluation,
  overflow in range construction, and unsupported infinite sources remain
  visible rather than silently changing the domain.
- Small proven domains expand deterministically; larger domains use guarded SMT
  quantifiers only in supported theory fragments. Instantiation count, nesting,
  trigger generation, model-based instantiation, and solver work have explicit
  budgets and stable unknown reasons.
- Users do not write solver-specific trigger syntax in ordinary contracts.
  Generated patterns are recorded in exported proof obligations, and matching
  loops or incomplete quantifier reasoning can never produce a false proof.
- Failed universal obligations identify a violating element and collection
  state when the model supplies one. Failed existential obligations explain the
  bounded domain and relevant exclusion facts without inventing an absent
  witness.
- Derived forms cover `All`, `Any`, element uniqueness, bounded counts, and
  supported sequence equality or permutation. Tests include empty and singleton
  domains, nested quantifiers, aliases, old arrays, maps, large symbolic bounds,
  timeouts, and deliberately adversarial triggers.

Evidence: Microsoft's [.NET Code Contracts reference](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/code-contracts)
documents `Contract.ForAll` and `Contract.Exists`, including their interaction
with old values and bound variables. Z3's official
[quantifier guide](https://microsoft.github.io/z3guide/docs/logic/Quantifiers/)
explains that quantified formulas are generally incomplete and details pattern
selection, matching loops, and model-based instantiation, which require explicit
budgets and conservative unknown results.

## P1 - Add deterministic-behavior contracts

Introduce a contract that requires the same explicit inputs and declared read
state to produce the same result, exception category, ordered externally visible
writes, and yielded sequence under a pinned execution profile. Distinguish
determinism from purity: a method can mutate only its allowed frame and still be
deterministic, while a read-only method can depend on time, randomness, culture,
process state, scheduling, or randomized hashes.

Acceptance criteria:

- The contract defines its observation boundary: return or yielded values,
  escaping exceptions, caller-visible frame writes, output ordering, and
  optional serialized bytes. Timing and performance are excluded unless a
  separate resource contract states them.
- Explicit inputs include reachable values in the reads frame and the initial
  state of declared deterministic providers. Mutable singleton, static, native,
  environment, or thread state not present in that frame is an ambient
  dependency and fails or weakens the proof.
- Known nondeterminism sources include clock and time-zone state, unseeded or
  shared randomness, cryptographic randomness, GUID creation, process and thread
  identity, environment and current culture, unstable file or network input,
  unordered concurrent completion, and randomized hashing or enumeration order.
- A stateful seeded generator can be deterministic only when its algorithm,
  complete initial state, call order, and runtime version are part of the
  profile. A numeric seed alone is not assumed portable across .NET versions.
- Stable sorting, explicit ordinal or pinned-culture comparison, canonical
  serialization, immutable input, and validated provider abstractions can
  discharge nondeterminism with evidence; `EnforcePure` alone cannot.
- Concurrency requires a proven race-free, order-independent result or a pinned
  deterministic scheduler contract. Floating point, native code, hardware
  intrinsics, and parallel reductions retain architecture and runtime provenance.
- Overrides, delegates, iterators, async completions, summaries, and model packs
  preserve the determinism guarantee. A diagnostic traces the first ambient or
  unordered dependency and, where bounded, shows two executions with divergent
  observations.

Evidence: the [`Random` contract](https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0)
warns that the same seed is not guaranteed to produce the same sequence across
major .NET versions. The [`HashCode` contract](https://learn.microsoft.com/en-us/dotnet/api/system.hashcode?view=net-10.0)
uses a process-random seed and warns against persisting its output, while
[`Guid.NewGuid`](https://learn.microsoft.com/en-us/dotnet/api/system.guid.newguid?view=net-10.0)
creates a new entropy-backed value. These are common cases where purity and
repeatability differ.

## P1 - Add allocation, peak-space, and stack-depth contracts

Generalize `[ZeroAllocations]` and CPU complexity into resource contracts for
managed allocation count and bytes, asymptotic allocation volume, peak live
managed space, large-object risk, stack allocation, and maximum synchronous
stack depth. Keep managed, native, pooled, and compiler-generated storage
separate and state which metrics are statically proven versus profile-calibrated.

Acceptance criteria:

- Contracts can bound exact or symbolic managed allocation count and bytes for
  supported sizes, allocation complexity, peak live managed space, total
  `stackalloc` bytes, and synchronous call depth. Zero allocation is the exact
  zero special case of the same model.
- Object, array, string, closure, delegate, boxing, params collection, iterator,
  async state machine, exception, and framework-helper allocations use a pinned
  runtime, architecture, object-layout, and optimization profile or remain size
  unknown while retaining their count.
- Sequential paths combine allocation totals by addition and peak live space by
  liveness-aware maximum; branches use worst feasible cases; loops and recursion
  use proven bounds or complexity; ownership escape and disposal refine lifetime
  without predicting a garbage-collection instant.
- Pool rent and return, stack storage, unmanaged allocation, memory mapping, and
  native libraries are distinct resources. Pooling does not count as zero work
  or zero retained memory, and an unknown pool miss cannot be assumed free.
- Large-object classification uses the selected runtime's threshold and exact
  requested size when known. `stackalloc` in loops, recursive frames, large
  frames, and unsafe pointers surface cumulative stack risk and process-failure
  evidence rather than a catchable exception claim.
- Interprocedural summaries carry formulas over input sizes, type arguments,
  target runtime, and callback cardinality. Unknown external allocation widens
  only the affected metric and does not erase proven CPU complexity.
- Static results are calibrated against versioned allocation measurements and
  benchmarks without treating measurements as proofs. Evidence itemizes every
  allocation site, size formula, escape lifetime, stack contributor, bound, and
  profile assumption.

Evidence: [`GC.GetAllocatedBytesForCurrentThread`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0)
measures total managed bytes allocated, explicitly excluding survivor size and
native allocation. Microsoft's [GC performance guidance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance)
connects allocation rate to collection cost, while the
[large-object heap reference](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
documents its separate threshold and costs. The official
[unsafe-code guidance](https://learn.microsoft.com/en-us/dotnet/standard/unsafe-code/best-practices#14-stackalloc)
warns that looped `stackalloc` accumulates until method return and can terminate
the process through stack overflow.

## P1 - Add an out-of-process IDE proof explorer

Turn the current diagnostics-and-code-fixes VSIX into an interactive proof
experience backed by the existing project-aware explain schema. Show concise
status in the editor and a navigable tool window for proof facts, obligations,
counterexamples, hazards, capabilities, complexity, unknown reasons, and
truncation. Keep expensive or fault-prone work outside the Visual Studio process.

Acceptance criteria:

- CodeLens or an equivalent low-noise editor adornment shows proven, violated,
  unknown, stale, and suppressed status for contracted members. QuickInfo gives
  a bounded summary and opens the full explorer without obscuring compiler data.
- The explorer consumes the same versioned explain result used by CLI and API,
  including the active project, target framework, analyzer configuration,
  AdditionalFiles, summaries, baselines, and source snapshot. It does not run a
  divergent second semantics pipeline.
- A tree links contract clauses to path facts, calls, hazards, capability sites,
  complexity drivers, frame changes, solver outcomes, and source spans.
  Counterexamples show reproducible inputs and unknowns show actionable budget,
  unsupported-shape, model, or native-solver causes.
- Document edits debounce and cancel stale work. Every response carries solution,
  project, document, text, configuration, and analysis-version identity and is
  discarded if any identity no longer matches the visible snapshot.
- Live analysis uses strict latency and memory budgets; deeper proof, witness
  minimization, obligation export, and all-path expansion are explicit commands
  with progress, cancellation, and bounded result sizes.
- The UI can compare the current result with the last successful build or
  baseline, copy stable JSON or Markdown evidence, navigate cross-links, apply
  existing code fixes, and explain why a diagnostic is suppressed.
- Analysis and rich UI run out of process or behind the isolated solver worker;
  crashes, hangs, malformed output, and extension upgrades cannot wedge the IDE.
  No source or proof data leaves the machine without a separate explicit action.
- Keyboard navigation, screen-reader labels, high-contrast themes, localization,
  and large-solution performance are release gates. VSIX integration tests cover
  rapid edits, project switches, cancellation, worker restart, and stale results.

Evidence: the [Visual Studio SDK overview](https://learn.microsoft.com/en-us/visualstudio/extensibility/visual-studio-sdk?view=visualstudio)
identifies tool windows, editor extensions, IntelliSense, and light-bulb actions
as supported extension surfaces. The modern
[editor extensibility reference](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/editor/editor?view=visualstudio)
defines CodeLens as actionable contextual information, and the
[VisualStudio.Extensibility architecture](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility?view=visualstudio)
uses asynchronous out-of-process extensions to improve IDE performance and
reliability.

## P1 - Model type and module initialization as hidden control flow

Treat static field initialization, explicit type initializers, and module
initializers as implicit calls that can precede otherwise ordinary operations.
Their effects, failures, locks, and partially initialized state must participate
in purity, exception, capability, allocation, determinism, and temporal proofs.

Acceptance criteria:

- Maintain initialization state for each relevant module and constructed type:
  not started, running, succeeded, or failed. Distinct closed generic types and
  load identities are not collapsed into one global state.
- Model the trigger difference between an explicit `.cctor` and a
  `beforefieldinit` type. Instance construction, static method calls, static
  field access, reflection, and explicit runtime initialization helpers trigger
  only the initialization guaranteed by the selected runtime semantics.
- Initialize static storage to default values, then execute field initializers
  in their specified textual order and the type initializer at the correct
  point. Unspecified ordering across partial declarations, modules, or runtime
  scheduling is represented as alternatives rather than source-order folklore.
- Import allocations, global writes, capabilities, nondeterminism, blocking,
  locks, and exceptions from hidden initialization into the triggering path.
  A method cannot be proved pure or no-throw merely because its visible body is.
- A failed type initializer records the permanent failed state for the analysis
  lifetime and models the runtime wrapper and inner exception. Later triggers do
  not rerun it or assume initialized fields.
- Recursive and mutually recursive initialization can observe default or
  partially assigned static fields. Reentrancy and blocking under the runtime's
  initialization lock produce explicit cycle or deadlock evidence instead of a
  fully initialized assumption.
- Module initializers run once on module load and in the compiler-defined order
  within a module. Cross-module load order remains conditional on the analyzed
  entry point and load profile.
- Summaries record initialization prerequisites and post-state without charging
  the one-time cost on every call. Witnesses show the hidden trigger, initializer
  chain, state transition, and any wrapped failure. Tests cover generics,
  inheritance, cycles, module initializers, `beforefieldinit`, and repeated use.

Evidence: Microsoft's [static constructor guidance](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)
documents one-time execution, default and field-initializer ordering, permanent
failure after an exception, the initialization lock, and deadlock risk. The
[`beforefieldinit` guidance in CA1810](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1810)
shows that inline initialization has weaker and potentially earlier trigger
semantics than an explicit static constructor. The C#
[module initializer specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-9.0/module-initializers)
defines eager one-time module initialization and deterministic ordering of the
compiler-emitted calls.

## P1 - Resolve bounded reflection and dynamic binding semantics

Recover precise targets for reflection and C# dynamic operations when their
runtime types, member names, flags, and arguments are symbolically bounded. Feed
the selected member through the normal summary and contract engine while keeping
open-world or custom-binder cases explicitly unknown.

Acceptance criteria:

- Track finite `Type` values from `typeof`, bounded `GetType` results, known type
  names, generic construction, array construction, and type tokens. Preserve
  assembly, target framework, load identity, and trimming provenance.
- Resolve `GetMethod`, `GetConstructor`, `GetProperty`, and related lookup for a
  finite receiver type and constant member name, honoring supported
  `BindingFlags`, visibility, inheritance, generic arity, parameter types,
  default-binder conversions, missing members, and ambiguous matches.
- Model `Activator.CreateInstance`, constructor and method invocation, and
  property get or set by importing the chosen target's contracts, effects,
  capabilities, allocations, exceptions, return value, and `ref` or `out`
  updates. Validate receiver and argument shape before target execution.
- Preserve reflection-specific exception behavior, including lookup and argument
  failures and the configured wrapping of exceptions thrown by the invoked
  target. Evidence distinguishes binder failure from a target-body failure.
- Bind C# dynamic member access, invocation, indexing, operators, conversions,
  and construction from finite runtime receiver and argument types plus the
  call-site context. Report feasible non-null missing-member, overload,
  conversion, accessibility, and indexing failures as runtime binder hazards.
- Treat `IDynamicMetaObjectProvider`, custom binders, emitted members, mutable
  `ExpandoObject` shapes, unresolved assemblies, and unbounded type or name sets
  conservatively unless a versioned model supplies their semantics.
- Memoize resolution by complete semantic key, cap target-set expansion, and
  merge per-target results without losing provenance. A diagnostic identifies
  the first value that made resolution open and suggests a type, name, flag, or
  model bound that would restore precision.
- Compose with the trimming and Native AOT feature: preservation annotations can
  establish member availability but do not prove invocation behavior, while
  semantic resolution does not imply that metadata survives publication.
  Differential tests compare bounded results with the runtime binder and
  reflection APIs across supported target frameworks.

Evidence: the C# specification states that failed
[dynamic binding reports exceptions at run time](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions),
and [`RuntimeBinderException`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.csharp.runtimebinder.runtimebinderexception?view=net-10.0)
represents ordinary runtime binding failure. The
[`Type.GetMethod` contract](https://learn.microsoft.com/en-us/dotnet/api/system.type.getmethod?view=net-10.0)
shows that reflection lookup depends on binding flags, overload selection, type
coercion, and parameter shape. Microsoft's
[reflection invocation compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/reflection-invoke-exceptions)
also documents target-exception wrapping across the invocation API family.

## P1 - Make contract authoring refactoring-safe and editor-aware

Give the string expressions in `[Requires]`, `[Ensures]`, and related attributes
first-class language tooling. Preserve their metadata-compatible representation,
but bind identifiers to Roslyn symbols so rename, navigation, completion, and
diagnostics operate on contract syntax instead of treating it as opaque text.

Acceptance criteria:

- Publish a stable SharpProof contract-language identifier and annotate every
  public contract-string parameter with `StringSyntaxAttribute`. Document the
  grammar version and keep the annotation available on supported target
  frameworks without adding runtime evaluation.
- Parse each literal or compile-time constant into the same versioned contract
  AST used by the analyzer, CLI, explain output, summaries, and model packs.
  There is one binder and precedence table, not editor-only approximations.
- Map every inner token and diagnostic back to an exact C# source span across
  regular, verbatim, raw, escaped, interpolated-constant, concatenated, and
  `nameof`-based forms. Unsupported source forms retain analysis but disable only
  edits that cannot round-trip safely.
- Provide syntax coloring, brace matching, completion, signature help, hover,
  go-to-definition, find references, and rename for parameters, result values,
  old-state expressions, types, and permitted members inside contracts.
- Rename participates in the containing Roslyn rename transaction and updates
  only identifiers bound to the renamed symbol. It detects metadata contracts or
  generated sources that cannot be edited and reports a precise partial-update
  warning instead of silently leaving stale text.
- Code fixes can introduce `nameof` fragments for stable identifier references,
  normalize a contract, qualify an ambiguous symbol, and migrate older grammar.
  They preserve trivia and literal style and show the semantic diff before a
  multi-document edit.
- Invalid syntax, unresolved or inaccessible symbols, unsupported operations,
  and type errors are underlined at the inner expression rather than only at the
  attribute. Completion and speculative binding obey the active project, target
  framework, nullable context, aliases, and source snapshot.
- Contract text remains a compile-time constant stored in metadata and requires
  no delegate, expression-tree compilation, reflection, or runtime parser.
  Integration tests cover renames, escaped and raw literals, linked files,
  generated code, overloads, target-framework differences, and old binaries.

Evidence: [`StringSyntaxAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.stringsyntaxattribute?view=net-10.0)
is the .NET mechanism for identifying the syntax carried by a string parameter,
field, or property. The C# [`nameof` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/nameof)
defines a compile-time constant that is validated by the compiler and updated by
rename refactoring, including inside attribute arguments. Together they provide
standard building blocks, but SharpProof still needs a semantic language service
for full expressions and exact source mapping.

## P1 - Add a long-lived Language Server Protocol proof service

Expose project-aware diagnostics and proof exploration through a reusable LSP
server so VS Code, Neovim, Emacs, and other clients can share one supported
integration. Keep it a thin protocol adapter over the same analysis and explain
schemas as the CLI, API, analyzer, and Visual Studio proof explorer.

Acceptance criteria:

- Ship a versioned language-server executable with standard input/output as the
  default transport and an explicitly enabled local socket option. It implements
  LSP 3.18 initialization, shutdown, exit, capability negotiation, cancellation,
  progress, and structured JSON-RPC errors without writing logs to the protocol
  stream.
- Support incremental open, change, save, and close synchronization with strict
  document-version checks. Responses carry workspace, project, target framework,
  configuration, source snapshot, analysis version, and solver-profile identity;
  clients discard stale results.
- Publish or serve pull diagnostics as negotiated, and provide hover, CodeLens,
  code actions, definition links, workspace symbols, and explicit commands for
  deeper proof, witness minimization, obligation export, and baseline comparison.
  Each feature reuses the canonical explain and fix data.
- Load solutions and projects with their real MSBuild context, including
  multi-root workspaces, linked files, generated sources, target frameworks,
  analyzer configuration, AdditionalFiles, model packs, and summaries. If a
  project cannot load, return an actionable degraded-mode reason.
- Debounce edits, prioritize visible-document work, share bounded caches, and
  cancel obsolete solver requests. Do not start a new compiler, project load, or
  SMT process for every hover or diagnostic request.
- Run expensive proof behind the isolated worker and enforce per-request time,
  memory, output, and concurrency budgets. A worker crash or malformed response
  fails the request, preserves the server, and returns restartable evidence.
- Respect workspace trust: never execute arbitrary client commands, project
  build targets, generators, or downloaded model code without the configured
  policy. Source and proof data stay local unless the user invokes a separately
  declared export action; telemetry is off by default.
- Provide one reference client configuration and protocol-level conformance
  tests, while keeping editor-specific UI optional. Tests cover rapid edits,
  cancellation, dynamic capability registration, multi-root changes, server
  restart, backpressure, malformed messages, and stale diagnostic suppression.

Evidence: the official [Language Server Protocol overview](https://microsoft.github.io/language-server-protocol/)
defines a JSON-RPC boundary that lets one language server be reused by multiple
development tools. The current [LSP 3.18 specification](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/)
standardizes lifecycle, document synchronization, language, workspace, window,
progress, and cancellation messages needed for a responsive proof service.

## P0 - Enforce behavioral subtyping across contract-bearing hierarchies

Verify every source override and interface implementation against the contracts
observable through each base slot. Resolving virtual dispatch is not sufficient:
an implementation must remain callable by clients that know only the base or
interface preconditions and must preserve every guarantee those clients were
promised across all SharpProof contract domains.

Acceptance criteria:

- Build the effective inherited contract independently for every overridden
  method, property accessor, default interface member, and explicit or implicit
  interface implementation after generic substitution and nullable annotation
  binding. Preserve the declaring slot and source of every clause.
- For each base slot, prove that its accepted-state predicate implies the
  implementation precondition. A derived member may accept more inputs but may
  not require a stronger condition from a caller dispatched through that slot.
- Under each base precondition, prove that every normal implementation exit
  implies that slot's postconditions, return nullability, frame guarantees, and
  object invariants. Derived-only stronger guarantees remain available to
  callers whose static contract includes them.
- Require implementation purity, allocation bounds, capabilities, exception
  sets, exceptional postconditions, complexity, determinism, cancellation, and
  temporal guarantees to be no weaker than every applicable inherited promise.
  Async contracts compare synchronous, successful, faulted, and canceled phases
  separately.
- Multiple interfaces and diamond inheritance are checked per originating slot,
  not merged into a convenient average. Incompatible inherited promises produce
  a diagnostic on the implementation with the conflicting contract paths.
- Handle covariant returns, generic constraints, reabstraction, sealed
  overrides, interface remapping, accessor asymmetry, and implementations
  supplied by a base class. A hidden `new` member is not mistaken for an
  override, while calls through an inherited virtual slot use the actual target.
- A source body can discharge an inherited contract even when it repeats no
  attributes. Metadata-only implementations require validated contract or
  effect summaries; missing or unsupported clauses produce review-required
  evidence and never silently erase a base guarantee.
- Diagnostics show the failed implication or widened effect with a bounded
  counterexample, base slot, implementation, generic substitution, and contract
  provenance. Summaries publish the effective dispatch contract, and tests cover
  cross-assembly inheritance, default interface methods, diamonds, and each
  contract family.

Evidence: the C# [interface specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces)
shows that a call through an interface can execute a later derived override,
which is why the implementation must preserve the interface-visible behavior.
Microsoft's [.NET library breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes)
identifies changes to exceptions, inputs, outputs, and other observable behavior
as contract breaks even when API shape remains compatible.

## P1 - Add verified assertions, governed assumptions, and unreachable proofs

Add statement-level proof controls for facts that are local to an algorithm and
do not belong in a public precondition or postcondition. Assertions create proof
obligations; assumptions introduce explicitly trusted facts under configurable
policy; unreachable markers prove that a control-flow edge cannot execute.

Acceptance criteria:

- Provide stable `SharpProof.Assert(bool)`, `Assume(bool)`, and `Unreachable()`
  source patterns, plus exact-identity recognition for supported legacy
  `Contract.Assert`, `Contract.Assume`, `Debug.Assert`, and throw-based
  unreachable idioms. Message arguments are evidence only, never parsed as facts.
- Evaluate an assertion in the incoming symbolic state. A proved assertion adds
  its condition to the successor state, a violated assertion reports a minimized
  witness, and an unknown assertion reports its stable reason and does not refine
  later analysis.
- An assumption can refine the successor state without proof only when the
  active policy permits it. Every dependent diagnostic, summary, proof result,
  witness, and exported obligation carries the assumption's source span,
  condition, justification, and trust classification.
- Support policies for forbid, audit, and allow, scoped by project, path, symbol,
  build profile, and assumption kind. CI can fail on any new assumption, missing
  justification, expired review, or proof that depends on a forbidden trust
  source.
- Assertion and assumption conditions use the ordinary typed expression
  semantics, must be side-effect-free, and respect checked arithmetic,
  short-circuiting, nullable state, aliases, and active target framework. An
  unsupported condition cannot be accepted as an uninterpreted true fact.
- `Debug.Assert` and conditionally compiled Code Contracts honor the actual
  preprocessor and runtime profile. A debug-only call is not treated as a
  release-mode guard, and an assertion API whose failure may continue execution
  refines state only after static proof, not because a dialog might appear.
- `Unreachable` is modeled as an assertion of false plus termination only when
  its runtime form cannot return. Exhaustive switches, exception filters,
  `[DoesNotReturn]`, and `UnreachableException` compose with reachability without
  converting an unproved developer belief into dead code.
- Interprocedural summaries distinguish proved facts from assumption-dependent
  facts and list their transitive trust roots. Code fixes can convert a proved
  assertion into a comment or contract, but never insert an assumption merely to
  silence a diagnostic. Tests cover loops, generics, async state machines,
  conditional compilation, and assumption-policy changes.

Evidence: Microsoft's [`Contract.Assert` reference](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.contracts.contract.assert?view=net-10.0)
documents both its conditional compilation and failure-policy behavior, so its
presence alone is not a universal runtime guard. The broader
[Code Contracts reference](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/code-contracts)
defines assertion, assumption, precondition, postcondition, and invariant roles,
while [`UnreachableException`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception?view=net-10.0)
represents execution of a path believed to be unreachable rather than proof that
the path cannot occur.

## P1 - Verify construction-phase and required-member contracts

Model the complete interval from storage creation through constructor chaining
and object initialization to first safe publication. Verify that required and
invariant-relevant members have valid values, and audit
`SetsRequiredMembersAttribute` as a claim about the constructor body rather than
trusting its presence unconditionally.

Acceptance criteria:

- Represent default storage, instance field and property initializers, primary
  constructor capture, `base` and `this` constructor chains, constructor bodies,
  object or collection initializer assignments, and final publication in their
  language-defined order. Each step contributes effects, exceptions, facts, and
  frame changes.
- Compute the effective required-member set across base types, overrides, and
  hiding rules for each constructor. Distinguish C# call-site satisfaction from
  semantic validity: assigning `null` or `default` can satisfy `required` syntax
  while still violating nullability, a range contract, or an object invariant.
- Verify every source constructor marked `SetsRequiredMembers` actually
  establishes each effective member on all normal exits. Constructor chaining,
  helper calls with verified postconditions, and record copy constructors can
  discharge the claim; an attribute with an unsupported body remains unknown.
- Model `init` and ordinary setters as calls that can validate, normalize, throw,
  allocate, invoke callbacks, or mutate other state. Preserve initializer source
  order and do not replace an accessor with a field assignment unless its body is
  proven equivalent.
- Check the completed object invariant after the constructor and initializer
  both finish, before the value becomes normally visible. Constructor failure
  does not create a normal result, but any escape of `this` to static state, a
  callback, virtual call, event, task, or another thread exposes a partial object
  and triggers an early-publication diagnostic.
- Cover classes, structs, records, copy and `with` expressions, target-typed and
  implicit `new`, attributes, generic construction, factories, and nested
  initializers. Preserve the specified differences between `default(T)` and
  `new T()` for structs with required members.
- Reflection, deserialization, uninitialized-object APIs, dynamic construction,
  and external factories do not inherit source-level initialization guarantees
  automatically. They require bounded semantic models or return an explicit
  partially initialized or unknown state that composes with the reflection and
  serialization backlog items.
- Evidence lists the construction stage, member, last assignment, constructor
  path, escape site, and violated condition. Tests cover inheritance, nullable
  required members, throwing setters, record copying, primary constructors,
  factory summaries, and false `SetsRequiredMembers` assertions.

Evidence: the C# [required-members specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-11.0/required-members)
states that `SetsRequiredMembersAttribute` removes call-site requirements but the
constructor body is not validated. The [`required` reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/required)
also warns that the compiler does not verify the attribute's claim and that a
required member may still be initialized to `null` or `default`. Microsoft's
[object-initializer guidance](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/object-and-collection-initializers)
documents the post-constructor member assignments and `init` accessors that form
the rest of the construction phase.

## P1 - Model execution-context flow, schedulers, and affinity

Extend task-aware contracts beyond completion state to the ambient logical
context and scheduling constraints that cross async and callback boundaries.
Keep logical execution context, synchronization context, task scheduler, and
physical thread identity separate so purity, capability, deadlock, determinism,
and thread-affinity proofs do not rely on the wrong notion of context.

Acceptance criteria:

- Track finite `AsyncLocal<T>` slots and other supported ambient values in a
  versioned execution-context frame. Reads are ambient dependencies; writes are
  scoped effects that can trigger value-change callbacks and flow to child work
  according to the selected runtime API.
- Model `ExecutionContext.Capture`, `Run`, `SuppressFlow`, restoration, and
  default versus unsafe queueing. Capture snapshots logical state; suppressing
  flow affects newly scheduled work and does not erase the current frame.
- Represent synchronization-context and task-scheduler identity and cardinality
  separately from execution context. Model `Post`, `Send`, task creation,
  continuations, thread-pool dispatch, and supported custom schedulers through
  higher-order callback summaries and explicit scheduling capabilities.
- `await` and `ConfigureAwait` preserve task completion facts while applying the
  documented attempt to resume on a captured context. `ConfigureAwait(false)`
  removes that affinity request but never proves a different thread, parallel
  execution, or loss of `ExecutionContext` and `AsyncLocal` state.
- Thread-affine calls require evidence for the current dispatcher, context, or
  scheduler. Framework-specific WPF, Windows Forms, MAUI, ASP.NET, and test-runner
  behavior comes from versioned model packs rather than a universal UI-thread
  heuristic.
- A synchronous wait reports a context deadlock only when a bounded dependency
  cycle requires a continuation on a non-reentrant, capacity-exhausted context.
  Unknown custom pumps and schedulers remain unknown instead of producing the
  blanket claim that every blocking wait deadlocks.
- Culture, principal, tracing activity, and other selected ambient values compose
  with their existing determinism and capability domains when they flow through
  execution context. `ThreadLocal<T>`, thread-static state, and OS thread identity
  remain physical-thread facts and do not follow `AsyncLocal<T>` rules.
- Summaries describe capture, suppression, scheduling, affinity requirements,
  and ambient reads or writes. Witnesses show the scheduling edge and context
  transition; tests compare safe and unsafe thread-pool APIs, nested suppression,
  callbacks, custom schedulers, and supported target frameworks.

Evidence: [`AsyncLocal<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.threading.asynclocal-1?view=net-10.0)
is explicitly ambient data local to an asynchronous control flow and can change
on context transitions. [`ExecutionContext`](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext?view=net-10.0)
captures and transfers logical-thread information across runtime asynchronous
points, with flow suppression as a separate operation. The
[`Task.ConfigureAwait` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.configureawait?view=net-10.0)
promises only an attempt to marshal to the captured context, which is weaker than
a guarantee about physical thread identity.

## P1 - Add whole-solution proof health reports and debt budgets

Add a batch command and CI surface that answers how much of a project or solution
is proved, violated, unknown, suppressed, skipped, or not analyzed. Turn stable
unknown reasons and contract results into an adoption metric without conflating
absence of diagnostics with proof or rewarding teams for shrinking the scan.

Acceptance criteria:

- Define explicit denominators for contracted members, public API, selected
  assemblies, and all analyzable members. Every member and obligation receives
  exactly one terminal status per proof domain: proven, violated, unknown,
  suppressed, skipped-by-policy, unsupported, or not analyzed due to failure.
- Report purity, preconditions, postconditions, invariants, exceptions,
  allocation, capabilities, complexity, hazards, determinism, termination, and
  other enabled domains separately. A member with no contract is not counted as
  proved merely because analysis emitted no diagnostic.
- Preserve project, configuration, target framework, runtime identifier,
  language version, analyzer options, solver profile, model-pack set, generated
  code policy, and source revision. Multi-target results show each target and a
  conservative intersection rather than averaging incompatible proofs.
- Aggregate by project, namespace, owner, member, contract family, diagnostic,
  unknown reason, unsupported operation, dependency, and trust source. Rank the
  smallest actionable causes that account for the most proof debt and link each
  count to bounded member-level evidence.
- Support absolute and percentage budgets for violations, unknowns,
  assumption-dependent results, suppressions, unsupported members, and coverage.
  Gates can apply to totals or only new and worsened debt, and cannot pass when a
  project fails to load or the requested denominator was not scanned completely.
- Compare with a versioned baseline using semantic member identity and stable
  result fingerprints. Classify new, unchanged, updated, and absent results;
  distinguish a real fix from deletion, exclusion, rename, policy change, or
  analyzer failure.
- Emit deterministic JSON, SARIF, and concise Markdown or HTML summaries with
  schema and truncation metadata. SARIF includes fingerprints, baseline state,
  code flows, related locations, suppressions, and proof-specific properties
  while remaining usable by standard consumers.
- Reuse project loads, compilations, summaries, and solver workers across the
  scan; enforce cancellation and resource budgets without converting budget
  exits to success. Tests cover linked files, generated code, multi-targeting,
  partial load failure, baselines, renamed symbols, and attempts to game the
  denominator.

Evidence: the OASIS [SARIF 2.1.0 standard](https://docs.oasis-open.org/sarif/sarif/v2.1.0/os/sarif-v2.1.0-os.html)
defines comprehensive baseline states and fingerprints for measuring new,
unchanged, updated, and absent analysis results. GitHub's
[SARIF support guidance](https://docs.github.com/en/code-security/reference/code-scanning/sarif-files/sarif-support)
uses partial fingerprints to keep findings stable across runs, while Microsoft's
[code-analysis configuration reference](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options)
establishes project-wide severity and build-failure gates as normal analyzer
adoption controls.

## P1 - Add platform and runtime-availability proof profiles

Import operating-system and version requirements into path reasoning, contracts,
summaries, and witnesses. Complement the platform compatibility analyzer by
showing why a guarded path is feasible or impossible for a declared deployment
profile and by propagating availability requirements through SharpProof's other
proof domains without suppressing CA1416 diagnostics.

Acceptance criteria:

- Define a versioned profile over target framework, supported runtime platform,
  OS family and version, process architecture, runtime implementation, and
  relevant feature switches. Distinguish compile-time API availability, minimum
  supported deployment version, and facts about the current runtime instance.
- Derive the default profile from the loaded project's canonical TFM,
  `SupportedOSPlatformVersion`, runtime identifier, architecture properties,
  preprocessor symbols, and configuration. CLI and API overrides are explicit,
  serialized in evidence, and cannot contradict the compilation silently.
- Consume `SupportedOSPlatform`, `UnsupportedOSPlatform`, their guard
  attributes, assembly annotations, and inherited type or member annotations
  using the same platform-name, version-interval, and subset rules as the .NET
  platform analyzer.
- Recognize exact framework guards such as `OperatingSystem.IsWindows()` and
  version-at-least forms, `RuntimeInformation.IsOSPlatform`, and annotated custom
  guards. Boolean composition and path facts refine only the guarded branch;
  mutable or unrecognized cached checks remain conservative.
- Attach an availability requirement to each call, field, property, event,
  native import, and imported summary. A caller either proves the profile or
  propagates the requirement; an infeasible platform path contributes no effects,
  exceptions, capabilities, allocations, or complexity.
- On a feasible unsupported path, preserve the real body behavior when known and
  report structured availability evidence, including possible
  `PlatformNotSupportedException` or missing-entry-point behavior where the
  selected API contract defines it. An annotation is not rewritten into an
  unconditional throw.
- Analyze every target framework and runtime identifier independently. Reports
  show per-target results and their conservative intersection rather than using
  a successful Windows or newer-OS proof to certify all package assets.
- Cross-link SharpProof evidence to platform-analyzer diagnostics without
  changing their severity or claiming that symbolic reachability proves package
  installation, workload availability, native asset presence, or real-device
  behavior. Tests cover version boundaries, iOS and Mac Catalyst subset rules,
  custom guards, multi-targeting, architecture branches, and conflicting
  annotations.

Evidence: Microsoft's [platform compatibility analyzer reference](https://learn.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer)
defines supported and unsupported attributes, versioned platform intervals,
platform subsets, and recognized custom guards. The
[target-framework reference](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
distinguishes OS-specific TFMs, target platform versions, and minimum supported
runtime versions, which must remain separate in proof provenance.

## P1 - Support namespaced project-defined capabilities

Let teams extend the fixed IO, clock, randomness, reflection, synchronization,
and interop taxonomy with domain effects such as database writes, secret access,
tenant changes, billing, message publication, or privileged administration.
Keep the extension declarative, identity-stable, and compositional with built-in
capabilities rather than loading project code into the analyzer.

Acceptance criteria:

- Accept a versioned capability-schema AdditionalFile with globally namespaced,
  case-sensitive identifiers, display metadata, parent implications, aliases,
  deprecations, and optional documentation links. Reject duplicate IDs, cycles,
  invalid aliases, built-in shadowing, and ambiguous schema precedence.
- Extend the contract surface with repeatable string capability IDs while
  retaining the existing enum for source and binary compatibility. Unknown IDs
  are configuration errors, never ignored bits or implicitly allowed effects.
- Map exact structural method, property, field, event, attribute, and type keys to
  required capabilities. Namespace or assembly patterns require an explicit
  opt-in rule and report their match provenance; arbitrary executable predicates,
  reflection callbacks, scripts, and analyzer-loaded plugins are forbidden.
- Normalize the capability graph transitively and deterministically. Allowing a
  parent can allow documented children, requiring a child implies its parents,
  and a cycle or schema conflict invalidates only the affected schema while
  producing one actionable diagnostic.
- Propagate custom IDs through source calls, callbacks, virtual dispatch,
  summaries, model packs, package contracts, witnesses, explain output, SARIF,
  LSP, and proof-health reports. Behavioral subtyping prevents an implementation
  from adding a custom capability not permitted by its base slot.
- Every summary records the canonical schema digest and capability identities.
  A consumer missing the exact schema or alias migration reports unknown instead
  of interpreting a name under a different organization's meaning.
- Support project, target-framework, and path-scoped allowlists plus CI gates for
  forbidden or newly introduced capabilities. Inference and code fixes preserve
  stable IDs and never grant a broad parent merely because one child was seen.
- Validate schemas and mappings without executing target assemblies or attribute
  constructors. Tests cover schema diamonds, aliases, package transitiveness,
  conflicting AdditionalFiles, metadata-only symbols, generic members, and
  malicious or oversized input.

Evidence: Roslyn exposes [AdditionalFiles](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analyzeroptions.additionalfiles?view=roslyn-dotnet-4.14.0)
as non-code text available to analyzers, and Microsoft's
[code-analysis configuration reference](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options)
explicitly permits third-party analyzers to define custom keys and value formats.
C# [custom attributes](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/reflection-and-attributes/creating-custom-attributes)
provide metadata tags for source declarations, while SharpProof must supply the
validation, propagation, and trust semantics that give those tags meaning.

## P1 - Model GC reachability, finalization, weak references, and resurrection

Add a bounded managed-lifetime domain for code whose correctness depends on the
difference between lexical scope and GC reachability. Integrate it with disposable
ownership and unsafe pinning, but never predict an exact collection or finalizer
time that the runtime does not guarantee.

Acceptance criteria:

- Track relevant objects as strongly reachable, finalization-eligible, queued,
  finalizing, finalized, resurrected, or reclaimed, with explicit widening when
  aliases or heap roots escape the bounded object graph. Ordinary local scope is
  not treated as a guaranteed strong root after the last observable use.
- Model finalizer registration on construction, `GC.SuppressFinalize`,
  `ReRegisterForFinalize`, and the finalization state transition. A finalizer may
  run only after loss of strong reachability and at an unspecified later time;
  ordering between unrelated finalizers remains nondeterministic.
- Import finalizer effects, native-resource release, capabilities, exceptions,
  synchronization, and resurrection into lifetime evidence without charging an
  arbitrary foreground method for every unrelated finalizer in the process.
  Creating or publishing a finalizable object records its latent cleanup effect.
- Model short and resurrection-tracking `WeakReference` and `WeakReference<T>`
  semantics. A successful target read establishes a temporary strong reference;
  separate liveness checks and later reads are not fused across a possible
  collection, finalization, or concurrent update.
- Treat resurrection as a new strong root with post-finalizer state and no second
  finalization unless explicitly re-registered. Long weak references can observe
  that state but do not restore pre-finalizer invariants or resource ownership.
- `GC.KeepAlive(value)` extends the relevant strong lifetime through the call and
  contributes no fabricated value fact. Explicit collection and
  `WaitForPendingFinalizers` constrain ordering only to the extent documented and
  do not prove that an otherwise reachable object was reclaimed.
- Model normal, pinned, weak, and resurrection-tracking `GCHandle` roots and
  release. Pinning composes with the unsafe-memory domain; a freed handle cannot
  keep storage alive or stable, and leaked handles remain retention evidence.
- Verify the Dispose/finalize pattern, including idempotent resource release,
  suppression after successful deterministic cleanup, SafeHandle ownership, and
  no use of already finalized managed dependencies. Witnesses show the last
  strong use and lifetime transition; tests cover early collection, resurrection,
  weak caches, pinning, finalizer cycles, and target-framework differences.

Evidence: Microsoft's [weak-reference guidance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/weak-references)
distinguishes short references from resurrection tracking and warns that
post-finalization state is unpredictable. The
[`Object.Finalize` runtime reference](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-object-finalize)
defines eligibility and suppression, while the official
[Dispose pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)
uses `GC.SuppressFinalize` after deterministic cleanup and recommends SafeHandle
for unmanaged resources.

## P1 - Add a content-addressed persistent proof cache

Persist method summaries, normalized obligations, solver outcomes, and bounded
evidence across processes and CI runs so unchanged proofs are reused by builds,
the CLI, IDE, LSP, and proof-health scans. Make reuse an exact semantic match with
auditable provenance, not a timestamp shortcut that can return stale proof.

Acceptance criteria:

- Define immutable cache records for canonical method inputs, dependency
  summaries, proof obligations, outcomes, witnesses, and evidence. Each record is
  addressed by a cryptographic digest of its canonical bytes and includes schema,
  length, producer, and complete dependency digests.
- The action key covers normalized source or IL identity, symbol and generic
  substitution, compiler and language options, target framework and RID, SDK and
  runtime profile, SharpProof and schema versions, model packs, capability schema,
  analyzer configuration, solver backend and options, budgets, and feature flags.
- Maintain a dependency manifest so an edit invalidates only affected methods,
  callers, dispatch sets, summaries, and obligations. Missing inputs, unstable
  identities, unsupported generators, unresolved references, or open-world
  dependencies disable the hit rather than weakening the key.
- Reuse proven, violated, and unknown outcomes only for an exact compatible key.
  A timeout under one budget cannot satisfy a larger-budget request, and a cached
  result from a weaker model or different solver is never promoted to proof.
- Provide a bounded local cache by default with atomic writes, process-safe
  readers, corruption-as-miss behavior, deterministic eviction, size and age
  limits, explicit inspection, and clear commands. Interrupted writes or version
  upgrades cannot poison later analysis.
- Make shared or remote storage opt-in. Content hashes prove integrity, not who
  produced a result: remote proof hits require an authenticated trusted namespace,
  signature or attestation policy, or deterministic local replay before they can
  discharge an obligation. Untrusted entries may only be scheduling hints.
- Exclude secrets and machine-specific absolute paths from records, disclose
  whether source snippets or witnesses are stored, support repository-scoped
  encryption and retention policy, and never execute content recovered from a
  cache. Low-trust pull requests receive read-only or isolated namespaces.
- Emit hit, miss, invalidation, replay, corruption, bytes, latency, and saved-SMT
  metrics by stage without logging source. Performance gates verify warm no-op and
  one-file-edit reuse, while differential tests sample cached proofs against clean
  recomputation across machines, paths, concurrent writers, and schema upgrades.

Evidence: [MSBuild incremental-build guidance](https://learn.microsoft.com/en-us/visualstudio/msbuild/incremental-builds?view=visualstudio)
requires complete input/output relationships before work can be reused. The
[Remote Execution API](https://github.com/bazelbuild/remote-apis) demonstrates
digest-addressed inputs and cached results at build scale, while GitHub's
[cache security guidance](https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching)
warns that shared cache contents are not authenticated and can expose secrets or
inject malicious files. Proof reuse therefore needs stricter trust than ordinary
dependency caching.

## P1 - Publish and consume interprocedural runtime-hazard summaries

Carry conditional runtime hazards across method boundaries so a caller can prove
that a callee's divide-by-zero, overflow, invalid cast, null dereference, bounds,
or similar failure is impossible on the actual call path. Reuse the existing
local hazard domain, but preserve the difference between a possible operation
failure, an exception contract, and an exception deliberately thrown by the
callee.

Acceptance criteria:

- Define a versioned summary record for every supported hazard containing the
  hazard kind, trigger predicate, exception type, source provenance, required
  models, path precondition, and any relevant receiver, argument, return,
  `ref`, `out`, and bounded heap identities.
- At a call site, substitute actual receiver and argument expressions into each
  imported predicate. Suppress a hazard only when the caller proves its trigger
  false, report it when feasible, and preserve a conditional or unknown result
  when substitution, aliasing, or the solver cannot decide it.
- Compose hazards through recursion, strongly connected call-graph components,
  generic instantiations, delegates, local functions, virtual and interface
  dispatch, and bounded target sets. Widening or an unresolved target produces
  explicit unknown evidence instead of silently dropping a callee.
- Include hidden calls from operators, conversions, constructors, property and
  event accessors, collection initializers, `Dispose`, `await`, iterator
  advancement, callbacks, and type initialization. Deferred execution records
  the hazard on the operation that can actually run the callee.
- Integrate with `Throws`, exceptional postconditions, preconditions, path
  feasibility, and model packs. A permitted exception is not proof that its
  triggering hazard is safe, and a method that catches or translates the exact
  exception does not leak the original exception to its caller.
- Persist summaries with complete semantic dependency identity, target
  framework, profile, model-pack digest, and producer version. Missing, stale,
  incompatible, or untrusted external summaries are unknown and cannot certify
  a call as hazard-free.
- Deduplicate diagnostics by the nearest actionable root while retaining a
  bounded call chain to the callee operation. Witnesses show actual arguments,
  branch facts, summary substitutions, the failing predicate, and catch or
  translation boundaries without exposing unrelated source.
- Enforce depth, dispatch, recursion, evidence, and solver budgets with
  deterministic truncation metadata. Tests compare whole-body analysis with
  summary reuse across sync, async, iterator, callback, multi-project, and
  package-reference boundaries.

Evidence: the C# [exception-handling reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements)
states that an unhandled exception is sought in callers up the call stack and
documents distinct propagation points for awaited async functions and advanced
iterators. The language specification's
[exception-propagation rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1345-how-exceptions-are-handled)
repeat the search in each caller, so a sound caller proof must account for
feasible failures inside callees rather than inspecting only the invocation
syntax.

## P1 - Add verifiable shallow and deep immutability contracts

Let APIs state and prove whether an object itself cannot change, whether its
entire reachable representation cannot change, or whether only its public
observations remain stable. Make the level explicit so C# `readonly` syntax is
never mistaken for a transitive heap guarantee.

Acceptance criteria:

- Define source and metadata contracts for shallow, deep, and observational
  immutability, with stable names and an exact freeze or publication boundary.
  Contract absence and a compiler `readonly` modifier do not imply a stronger
  SharpProof guarantee.
- For shallow immutability, prove that instance storage cannot be reassigned
  after construction. For deep immutability, prove recursively that every
  reachable object and element is deeply immutable or owned and permanently
  frozen; mutable arrays, collections, delegates, and reference-typed fields are
  not accepted merely because their containing field is `readonly`.
- Model constructor, object-initializer, `init`, `required`, deserialization, and
  builder-to-frozen transitions as bounded construction phases. Reject mutation
  after escape or publication and explain the first alias or operation that
  prevents a valid freeze.
- Compose the proof with reads/modifies frames, ownership transfer, ref escapes,
  disposal, data races, deterministic behavior, equality, hashing, and object
  invariants. Returning a mutable alias or accepting a callback that can mutate
  the representation invalidates the applicable guarantee.
- Support generic types, records, inheritance, interface contracts, arrays,
  tuples, immutable collections, frozen collections, and approved model packs.
  A generic deep guarantee records and enforces the required immutability of each
  type argument and comparer.
- Permit a lazy cache only under a separately declared observational contract
  that proves externally visible results, equality, hashing, exceptions, and
  capabilities are unchanged and that concurrent initialization is safe. It
  does not satisfy structural deep immutability.
- Treat reflection, unsafe writes, interop callbacks, runtime serialization,
  mutable statics, and unknown external aliases conservatively. Imported
  immutability summaries carry assembly identity, schema, target profile, and
  trust provenance.
- Emit witnesses with the shortest mutability path from the contracted root to
  the writable location and the operation or escape that exposes it. Tests cover
  cycles, covariance, builders, record copying, array elements, nested generics,
  lazy fields, inheritance, and target-framework-specific collection models.

Evidence: Microsoft's [structure type reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/struct#readonly-struct)
explicitly notes that a mutable reference member of a `readonly struct` can still
mutate its own state. The official
[`System.Collections.Immutable` reference](https://learn.microsoft.com/en-us/dotnet/api/system.collections.immutable?view=net-10.0)
defines immutable collection abstractions and builder transitions, while
[`System.Collections.Frozen`](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen?view=net-10.0)
provides separate immutable, read-only lookup collections. These distinctions
need precise heap and construction semantics in a proof contract.

## P1 - Add closed-union and payload-aware exhaustiveness proofs

Allow a library to declare a verifiably closed set of cases and let SharpProof
prove exhaustive handling of both case tags and their payload constraints. Build
on existing path reasoning and compiler-warning proof support without assuming
that an arbitrary class hierarchy, enum, or package can never gain another case.

Acceptance criteria:

- Define a versioned closed-union contract or declarative model whose manifest
  names every case, discriminator, payload projection, invariant, null policy,
  and declaring assembly identity. Support sealed record hierarchies, enums,
  generated unions, and modeled Option, Result, and OneOf-style APIs.
- Prove closure from enforceable construction and accessibility rules or from an
  exact generated manifest. Open inheritance, public unmodeled constructors,
  reflection, dynamic activation, deserialization of unknown tags, and missing
  package assets invalidate closure or yield explicit unknown evidence.
- Analyze switch statements, switch expressions, nested property and positional
  patterns, `or`, `and`, `not`, guards, null, enum aliases, unnamed enum values,
  default arms, and discard arms. A guard counts only for the payload states it
  actually covers.
- Refine path facts with the selected case, its invariant, and typed payload.
  Prove payload-specific arms and propagate their postconditions rather than
  treating exhaustiveness as a tag-only syntax check.
- Diagnose a feasible missing case with a concrete case and payload witness.
  Separately identify unreachable, redundant, stale default, and catch-all arms
  that conceal newly added cases; do not remove or suppress compiler diagnostics
  unless the corresponding exact proof is closed.
- Treat adding, removing, renaming, or weakening a case as a versioned behavioral
  contract change. Invalidate summaries and caches for consumers and integrate
  union compatibility with package-contract and behavioral-subtyping checks.
- Compose exhaustive matches through generic unions, nullable wrappers, async
  results, callbacks, iterators, and exception or outcome contracts. Unknown
  comparers, user-defined conversions, or active patterns remain conservative.
- Emit deterministic evidence listing the closed case set, closure proof,
  patterns and guards covering each case, remaining state, model provenance, and
  target profile. Tests cover version skew, enum holes, overlapping guards,
  nested unions, generated code, malicious manifests, and open-world fallbacks.

Evidence: Microsoft's [pattern-matching diagnostic reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/pattern-matching-warnings#pattern-completeness-and-redundancy)
defines separate warnings for non-exhaustive switches, unnamed enum values,
guards, unreachable arms, and patterns too complex for the compiler to analyze.
That syntax-level coverage does not establish that a user-defined hierarchy or
third-party result type is a closed semantic union, which requires an explicit
and versioned proof boundary.

## P1 - Add explicit outcome-use and result-state contracts

Model APIs that return success, failure, absence, or another explicit outcome so
callers must inspect, propagate, transform, or intentionally ignore the result.
Generalize the .NET `Try*` relationship between a discriminator and payload to
Result, Option, error-code, and asynchronous outcome types without imposing one
library's naming convention.

Acceptance criteria:

- Define declarative source and model-pack contracts for a must-use outcome,
  its complete states, discriminator predicates, success and error payloads,
  `ref` or `out` relationships, and permitted propagation or explicit-ignore
  operations. Validate that states are disjoint and complete before trusting the
  schema.
- Track each produced outcome as unexamined, narrowed to a state, propagated,
  transformed, consumed, or explicitly ignored with a policy-recognized reason.
  Diagnose discards, overwritten values, abandoned fluent chains, and values
  that leave scope without a sound terminal action.
- Refine path facts and null state from boolean returns, tag checks, patterns,
  deconstruction, match or fold callbacks, and approved helper methods. Reading
  a state-specific payload requires proof of that state; a property that throws
  in other states contributes the corresponding hazard and exception.
- Support `Try*` boolean-plus-`out` APIs, nullable and sentinel results, enum or
  numeric error codes, generic Result and Option families, tuples, and configured
  domain types. Ordinary booleans or values are not treated as outcomes merely
  because their names resemble `Success` or `Error`.
- Carry state through `Task<T>`, `ValueTask<T>`, iterators, async streams,
  callbacks, LINQ, and collection storage. Losing a correlation through aliasing,
  concurrency, or an unknown callback yields unknown rather than a guessed
  state.
- Integrate with exception contracts, cancellation, ownership, disposal,
  capabilities, and behavioral subtyping. Converting an error into an exception
  must satisfy the declared exception contract, and discarding a successful
  disposable payload cannot hide an ownership leak.
- Import package outcome summaries only with exact method identity, generic
  substitution, schema digest, target profile, and trust provenance. Version
  changes to cases or discriminator semantics invalidate caller proofs.
- Offer bounded fixes for assigning and checking the result, propagating it, or
  adding an explicit ignore with a justification placeholder. Evidence names the
  producing call, last state transition, missing state or terminal action, and a
  concrete witness; tests cover nested outcomes, early returns, async flows,
  overloads, aliases, and partial model packs.

Evidence: Microsoft's [nullable-analysis attribute reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis#conditional-post-conditions-notnullwhen-maybenullwhen-and-notnullifnotnull)
documents the standard `Try*` idiom in which a boolean return conditionally
determines the null state of an `out` payload. It also notes that such attributes
improve caller analysis but do not verify the implementation, leaving room for
SharpProof to prove the discriminator-payload contract and require that callers
handle the resulting state.

## P1 - Add a solution-wide contract adoption assistant

Turn existing high-confidence inferred contract suggestions into a reviewable,
dependency-aware adoption workflow for a project or solution. Let teams add
proved contracts in controlled batches without clicking one code fix per method
or confusing a speculative annotation with a verified migration.

Acceptance criteria:

- Add a CLI and API adoption command over a project or solution with dry-run as
  the default. It consumes only existing closed-proof inference results and
  never converts unsupported, budget-exhausted, assumption-dependent, or unknown
  evidence into a proposed contract.
- Analyze and stage candidates bottom-up by call graph, override hierarchy,
  interface slot, partial declaration, project dependency, target framework, and
  generated source origin. Detect conflicting candidates instead of choosing an
  arbitrary target or broadening a base contract silently.
- Produce a deterministic manifest containing member identity, proposed change,
  proof domain, confidence, evidence digest, source fingerprint, dependencies,
  affected targets, compatibility impact, and reason for every exclusion. Allow
  filtering by project, path, domain, confidence, and public API status.
- Generate a reviewable unified patch while preserving trivia, file encoding,
  line endings, attribute ordering, nullable context, and formatting policy.
  Generated, vendored, metadata-only, read-only, or dirty-conflicting files are
  excluded unless an explicit safe policy selects them.
- Speculatively apply each batch in an isolated workspace, reload affected
  compilations, and rerun relevant proofs. Retain a proposal only when it remains
  valid and introduces no new contract, behavioral-subtyping, build, or analyzer
  diagnostics across every selected target.
- Make explicit application atomic, cancelable, and idempotent. A stale source
  fingerprint, changed proof environment, partial write, formatter failure, or
  compilation failure aborts or rolls back the affected batch and leaves a
  machine-readable recovery report.
- Support CI check mode that reports newly eligible, stale, regressed, and
  policy-blocked candidates without editing files. Baselines and proof-health
  reports link to the adoption manifest but do not count an uncommitted proposal
  as adopted coverage.
- Reuse analyzer, IDE, and LSP code-action logic through deterministic Fix All
  semantics where safe, with previews and bounded batches. Tests cover linked
  files, multi-targeting, hierarchy conflicts, conditional compilation, partial
  types, concurrent edits, no-op reruns, rollback, and repositories with mixed
  formatting policies.

Evidence: the official [Roslyn analyzer and code-fix tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
describes source modifications with rich previews as the standard companion to
analyzer diagnostics. [`dotnet format`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
already applies analyzer fixes at project or solution scope, supports diagnostic
and path filters, emits a report, and provides a verify-without-changing mode.
SharpProof needs additional proof revalidation, dependency ordering, and
transactional evidence before using that workflow to add behavioral contracts.

## P1 - Prove secret-independent control flow and access patterns

Add a bounded relational proof domain for cryptographic and authentication code
whose observable execution shape must not depend on secret values. Build on the
information-flow domain, but report a precise secret-dependent branch, loop, or
memory access rather than claiming that high-level C# analysis proves physical
wall-clock constancy on every JIT, CPU, and runtime.

Acceptance criteria:

- Define contracts for secret, public, and deliberately declassified inputs,
  fields, return values, lengths, and state. Labels compose through assignments,
  aliases, generic containers, spans, callbacks, summaries, and model packs;
  naming conventions alone never classify a value.
- Use relational self-composition or an equivalent sound encoding: compare two
  executions with identical public state and independently varying secret state.
  Prove equivalence of the selected observable trace, not merely equality of the
  final return value.
- Trace conditional branches, switch choices, loop trip counts, exception and
  early-return paths, call targets, allocation sizes, collection probes, and
  memory addresses or indexes at the precision supported by the bounded heap.
  Secret-dependent diagnostic text and externally visible capability use are
  observable under an explicit policy.
- Allow documented public leakage such as message length only through an exact
  contract. A fixed-time comparison whose behavior depends on unequal lengths
  requires those lengths to be public or proved equal; it does not certify the
  surrounding caller automatically.
- Import a call only when its summary states the relevant trace guarantee and
  exact public parameters. Unknown native code, hardware intrinsics, reflection,
  virtual targets, custom comparers, and unmodeled cryptographic libraries yield
  unknown rather than an assumed constant-time operation.
- Record compiler, optimization, JIT or AOT, runtime, architecture, and model
  identity. A source-level trace proof is labeled separately from an IL or native
  validation, and never promises immunity to caches, branch predictors,
  speculative execution, GC pauses, scheduling, or shared-hardware leakage that
  its profile does not model.
- Compose with exceptions, bounds hazards, zeroization and ownership, taint,
  allocation, capabilities, and deterministic behavior. Async suspension,
  synchronization, and contention add explicit observables or make the proof
  unknown under profiles that cannot bound them.
- Emit a paired witness containing equal public inputs, differing secret inputs,
  and the first divergent trace event with both call paths. Tests cover early-exit
  comparisons, table lookups, secret loop bounds, equal and unequal lengths,
  declassification, spans, inlining summaries, and target-profile drift.

Evidence: .NET's
[`CryptographicOperations.FixedTimeEquals`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0)
contract says execution time depends on sequence length but not contents, and
documents a length-mismatch short circuit. That distinction demonstrates why a
proof must track both secret-dependent values and explicitly public dimensions
instead of treating a method name or ordinary equality as constant time.

## P0 - Add restricted and isolated analysis modes for untrusted workspaces

Prevent project-aware analysis from turning an unknown repository into arbitrary
code execution in the user's account. Separate a non-executing compilation-input
mode from a sandboxed project-evaluation mode and the current trusted-workspace
experience, with visible fidelity limits for each.

Acceptance criteria:

- Define `restricted`, `isolated`, and `trusted` workspace modes for CLI, API,
  IDE, LSP, adoption, proof-health, and model-generation entry points. The chosen
  mode, trust decision, and every executable component are recorded in evidence;
  no fallback silently promotes a less trusted mode.
- Restricted mode never invokes MSBuild, restore, project targets, property
  functions, source generators, analyzers, compiler plugins, custom loggers, or
  target assemblies. It consumes an explicit, versioned compilation manifest of
  source, generated-source snapshots, references, options, and AdditionalFiles
  produced by a trusted build.
- If the manifest is absent or incomplete, restricted mode may analyze explicit
  source files conservatively but reports missing generated code, references,
  options, targets, and project graph as fidelity gaps. It cannot label such a
  partial scan as a complete project or solution proof.
- Isolated mode evaluates projects only in a disposable worker with a dedicated
  low-privilege identity, read-only source mount, bounded scratch space, scrubbed
  environment, no inherited credentials, disabled user MSBuild extensions, and
  network and process policies. Restore and package scripts require separate
  explicit policy.
- Inventory and gate imported SDKs, props, targets, response files, tasks,
  analyzers, generators, plugins, native libraries, environment properties, and
  package build assets before execution. Resolve paths canonically and reject
  imports, symlinks, outputs, and IPC endpoints that escape approved roots.
- Enforce wall-time, CPU, memory, process-count, output, file-count, and log-size
  limits. Cancellation terminates the whole worker tree; crashes, violations,
  denied access, and truncation become structured unknown or failed-load results,
  never a successful proof.
- Partition caches, temporary files, solver workers, and model artifacts by trust
  mode and repository identity. Untrusted outputs cannot populate a trusted proof
  cache or load executable content into compiler, IDE, or language-server hosts.
- Provide a preflight report and machine-readable audit log without leaking
  environment secrets. Adversarial tests cover malicious targets, inline tasks,
  generators, analyzer packages, parent-directory imports, response files,
  symlink escapes, restore hooks, process spawning, network access, and denial of
  service on every supported host OS.

Evidence: Microsoft's [secure MSBuild guidance](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-security-best-practices?view=visualstudio)
states that opening or building unknown project sources can execute arbitrary
build logic and that packages can automatically add build and analyzer assets.
The official [`dotnet format` reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
likewise warns that project or solution processing may restore, compile, and run
analyzers and should be invoked only on trusted code.

## P0 - Gate releases on framework-model conformance and drift

Continuously verify that every trusted built-in framework model and generated
summary still matches the exact reference and runtime assets it claims to
describe. Consolidate the existing per-domain differential tests into a release
gate that detects semantic drift before a stale model can turn an invalid program
into a proof.

Acceptance criteria:

- Maintain a machine-readable inventory of every built-in semantic model,
  fallback, effect summary, intrinsic, comparer, and special-case rule with exact
  member identity, supported behavior, source provenance, owner, test strategy,
  target frameworks, runtime versions, RIDs, and architectures.
- Distinguish compile-time API shape from runtime behavior. Bind reference
  assemblies, implementation assemblies, package assets, module versions, method
  bodies, runtime configuration, globalization data, and platform switches to the
  model revision with cryptographic digests.
- Generate deterministic boundary, equivalence-class, exceptional, and randomized
  test vectors for supported inputs. In isolated runtime workers, compare return,
  `ref` and `out` state, mutations, exceptions, allocation or capability
  observables, and any documented deferred behavior with the symbolic result.
- Run every claimed target independently across the shipping SDK, runtime patch,
  TFM, RID, architecture, checked context, globalization mode, time-zone data,
  and relevant feature-switch matrix. Unsupported environments remain explicit
  coverage gaps rather than inheriting a nearby passing result.
- Classify mismatches as model unsoundness, incompleteness, documentation drift,
  runtime behavioral change, nondeterminism, or harness failure. Minimize and
  preserve the input, runtime identity, expected symbolic trace, and observed
  trace so the result is reproducible without rerunning the full matrix.
- Fail release and quarantine cache entries when a mismatch could make a false
  proof. A completeness-only regression may lower coverage to unknown under an
  approved policy, but no waiver can retain a contradicted trusted fact without
  an explicit version-scoped assumption and expiry.
- Apply the same schema and optional publisher conformance bundle to signed
  third-party model packs. Signatures authenticate a producer but do not replace
  behavior tests; consumers can require a minimum conformance tier and exact
  environment match.
- Publish coverage, freshness, last passing runtime, skipped dimensions, changed
  implementation hashes, quarantines, and release-gate status. Seeded reruns,
  negative harness tests, deliberate model mutations, and old-runtime fixtures
  prove that the gate catches both semantic and provenance drift.

Evidence: the .NET [target-framework reference](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
defines TFMs as selecting API sets and notes that platform versions select
specific reference assemblies and package assets. Microsoft's
[breaking-change reference](https://learn.microsoft.com/en-us/dotnet/core/compatibility/breaking-changes)
tracks behavior changes across runtime versions, while the
[library compatibility rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules)
explicitly treat changed return values, accepted ranges, parsing, exceptions,
and other behavior as compatibility concerns that a semantic model must not
silently outlive.

## P1 - Prove bounded linearizability of concurrent objects

Extend atomic, memory-order, race, temporal, and invariant reasoning with a
bounded object-level correctness condition. Show that completed concurrent
operations can be explained by a declared sequential specification at points
between invocation and response, rather than equating data-race freedom or use of
`Interlocked` with correctness of the whole abstraction.

Acceptance criteria:

- Define a contract for an abstract state, operation preconditions and sequential
  transitions, observations, exceptional outcomes, and optional candidate
  linearization points. Concrete representation fields remain connected through
  a proved abstraction relation and object invariant.
- Build bounded histories containing invocation, response, thread or task,
  arguments, results, exceptions, cancellation, and relevant shared-state events.
  Preserve real-time order when one response precedes another invocation and
  represent pending operations explicitly.
- Search for a legal sequential history with the same completed observations and
  real-time constraints. A proof covers all interleavings within declared thread,
  operation, retry, callback, heap, and scheduler bounds; exceeding any bound is
  unknown, not evidence that one sampled schedule is representative.
- Verify declared linearization points under locks, atomic read-modify-write,
  successful and failed compare-exchange, helping, retries, and elimination
  paths. An operation with different feasible points may use a disjunction or a
  validated refinement proof instead of an arbitrary source location.
- Respect the selected .NET memory-order profile, happens-before edges, aliasing,
  ownership, reclamation, ABA risk, and interference. Race freedom alone is not
  sufficient, and an unmodeled weak-memory execution prevents a proof.
- Model callbacks and user delegates as separate operations unless their effects
  are inside the documented atomic region. Repeated factory invocation, reentry,
  exceptions, cancellation, and disposal cannot be hidden inside a single
  abstract transition.
- Keep safety and progress claims separate. Linearizability does not imply
  lock-freedom, wait-freedom, starvation freedom, fairness, bounded retries, or
  deadlock freedom; those require their own evidence and budgets.
- Emit a minimal violating schedule with invocation and response history,
  concrete shared events, attempted sequential orders, the first impossible
  observation, and model provenance. Tests cover counters, stacks, queues,
  concurrent dictionaries, lazy initialization, helping, ABA, delegate reentry,
  and histories that are race-free but non-linearizable.

Evidence: Herlihy and Wing's original
[linearizability paper](https://pdos.csail.mit.edu/6.824/papers/p463-herlihy.pdf)
defines the illusion that each concurrent operation takes effect at one point
between invocation and response and relates that condition to sequential
preconditions and postconditions. The .NET
[`ConcurrentDictionary` contract](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2?view=net-10.0#remarks)
also distinguishes atomic dictionary operations from delegates executed outside
the locks, showing why method-level atomicity boundaries need explicit models.

## P1 - Add independently checkable proof certificates for supported fragments

Add a verification tier in which an unsatisfiable proof obligation is accompanied
by a certificate checked by a separately versioned implementation. Complement
portable replay and solver self-validation without mislabeling either as
independent checking or implying that a solver certificate validates SharpProof's
source-to-formula encoding.

Acceptance criteria:

- Define evidence levels for solver result, deterministic replay, solver
  self-validation, independently checked certificate, and unsupported
  certificate. Reports, SARIF, caches, package summaries, and CI policies preserve
  the level instead of collapsing all successful solver answers into `proven`.
- Bind each certificate to canonical obligation bytes, named assumptions,
  selected logic and theories, encoder schema, source map, solver and checker
  versions, options, budgets, model-pack digests, and all proof dependencies.
  Hash mismatches or omitted inputs invalidate the certificate.
- Request proof logs only for a documented supported solver fragment. Translate
  to a stable checkable format where a verified translator exists, or retain the
  native format with its exact rule set; unsupported tactics, opaque lemmas, or
  proof holes downgrade the result rather than being accepted axiomatically.
- Run an independently implemented checker in a separate constrained process and
  pin its binary or source digest. The checker consumes the obligation and proof,
  performs no network access or target-code execution, and reports every trusted
  rule, fallback, unverified step, and resource-limit exit.
- Distinguish inexpensive local rule checks from steps discharged by recursively
  invoking an SMT solver. A certificate containing solver-backed gaps is at most
  self-validated unless policy explicitly records and accepts that larger trusted
  base.
- State the remaining trusted computing base: parser, canonicalizer, source and
  IL semantics, IR lowering, model packs, SMT encoding, proof translator, and
  checker. Certificate success proves only that the encoded obligation follows
  from its encoded assumptions.
- Treat satisfiable counterexample models separately. Validate their assignments
  against the canonical obligation and reconstruct source witnesses, but never
  call a model an unsatisfiability certificate or infer real-runtime reachability
  beyond the encoding.
- Bound proof size, nesting, arithmetic, memory, and checking time; reject
  malformed or adversarial logs safely. Corpus tests cover certificate trimming,
  deliberate rule corruption, solver and checker upgrades, theory boundaries,
  cross-machine reproducibility, remote-cache poisoning, and disagreement with
  clean solver replay.

Evidence: the official Z3 [proof-log guide](https://microsoft.github.io/z3guide/programming/Proof%20Logs/)
documents inference logging, replay, built-in self-validation, proof-rule hints,
and steps that fall back to SMT solving. The
[Carcara paper](https://link.springer.com/chapter/10.1007/978-3-031-30823-9_19)
describes an independent checker for the Alethe SMT proof format, demonstrating a
separate verification tier while also making theory and format support an
explicit compatibility boundary.

## P0 - Bind proof evidence to the exact shipped assemblies

Produce a post-build proof manifest that binds every trusted result to the exact
PE, PDB, package asset, generated source, compilation input, and contract metadata
that was analyzed. Close the gap between proving a source snapshot and publishing
or deploying a different binary, without treating Source Link, an MVID, or a
successful deterministic rebuild as proof of behavioral equivalence by itself.

Acceptance criteria:

- Emit one versioned manifest per implementation assembly, target framework,
  runtime identifier, architecture, configuration, and compiler asset. Record
  cryptographic digests for the PE, portable or embedded PDB, reference assembly,
  resources, generated sources, analyzers, generators, references, response
  files, options, model packs, summaries, and normalized SharpProof inputs.
- Bind every member result to assembly identity, metadata token or stable symbol,
  method-body digest, contract digest, source document checksum, generated-source
  identity, and proof-evidence digest. Timestamps, file names, MVIDs, or public API
  signatures alone are insufficient identities.
- Verify that analysis observes the same compilation graph and generated output
  used to create the implementation binary. A design-time build, stale PDB,
  reference assembly, different conditional symbol, or regenerated source cannot
  donate proof to an implementation it did not produce.
- Map source and synthesized members through portable PDB and compiler metadata,
  then check emitted IL for the modeled control-flow and contract surface.
  Rewriters, weaving, obfuscation, signing changes, trimming, ReadyToRun, Native
  AOT, or post-link transformations require a validated stage-specific verifier
  or invalidate source-level behavioral claims.
- Preserve distinct status for source-proved, emitted-IL-verified, transformed,
  and unverified members. Missing sequence points or optimized code do not erase
  a result, but any unsupported mapping or changed body is explicit unknown
  evidence rather than a best-effort association.
- Attach the manifest to NuGet packages, symbols, archives, and release artifacts
  without mutating the already hashed subject. Multi-target packages prove each
  asset independently and list unproved compatibility fallbacks, buildTransitive
  logic, native assets, and satellite assemblies.
- Support a signed provenance attestation whose subject is the final artifact
  digest and whose predicate identifies the trusted build workflow, repository
  revision, compiler, SharpProof run, proof manifest, and policy. Signature
  identity and transparency verification are separate from semantic proof.
- Provide offline verification and stable failure reasons for missing, stale,
  mismatched, partially covered, unsigned, or untrusted evidence. Release tests
  deliberately swap DLLs, PDBs, generated files, package assets, target outputs,
  post-build rewrites, and attestations to prove that no result follows the wrong
  artifact.

Evidence: the C# compiler's [deterministic-build contract](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation#deterministic)
defines byte-identical output for identical inputs and lists analyzers,
references, resources, Source Link data, environment, and compiler version among
those inputs. GitHub's [artifact-attestation guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
binds build provenance to a SHA-256 subject digest. SharpProof needs both exact
binary identity and proof-specific member coverage before consumers can know
which shipped code a result certifies.

## P1 - Model partial I/O, buffers, and pipeline progress

Add symbolic protocol models for streams, sockets, readers, writers, pipelines,
and pooled buffers. Prove code handles short reads and writes, end of input,
partial buffer initialization, consumption, completion, cancellation, and
backpressure instead of treating an I/O call as an all-or-nothing value transfer.

Acceptance criteria:

- Model each supported read with requested range, actual count, initialized
  buffer slice, position change, zero-count request, end-of-input state, blocking
  or pending state, cancellation, and exceptional exit. Unless an exact API
  contract says otherwise, the actual count may be any permitted value below the
  request.
- Distinguish `Read`, `ReadAsync`, `ReadAtLeast`, `ReadExactly`, text decoding,
  datagram receipt, socket receipt, and implementation-specific zero-byte
  behavior. A loop proves full acquisition only from monotonic progress, correct
  offset and remaining-count updates, EOF handling, and a bounded message or
  length contract.
- Track writes according to the exact API: all-or-throw stream writes, partial
  socket sends, buffered writes, flushes, half-close, and completion are not
  interchangeable. Exceptions and cancellation preserve the documented amount
  and state instead of assuming either zero or full transfer.
- Treat rented arrays, `Memory<T>`, `ReadOnlyMemory<T>`, sequences, and writer
  spans as leases with valid slices. Only bytes below the returned count are
  initialized by a read; data cannot escape past return to a pool, owner disposal,
  pipeline advance, or the next operation that invalidates a borrowed buffer.
- Model `PipeReader` consumed and examined positions, `AdvanceTo`, final buffered
  data with `IsCompleted`, cancellation flags, and mandatory completion. Model
  `PipeWriter` get/advance/flush ordering, buffer invalidation, completion, and
  the pause and resume thresholds that create backpressure.
- Connect framing, delimiter, maximum-message, decoder, checksum, and parser
  contracts to bounded collection contents. Reject data loss, duplicate
  processing, infinite buffering, stale slices, unflushed output, and loops that
  cannot progress on truncated or delimiter-free input.
- Compose with ownership, allocation and peak-space bounds, cancellation,
  exception contracts, capabilities, async state machines, temporal protocols,
  and secret-flow labels. Imported framework or package models retain exact TFM,
  implementation, scheduler, and buffering provenance.
- Emit witnesses with requested and actual counts, buffer ranges, cursor states,
  EOF or cancellation choice, and the first invalid transition. Tests cover short
  reads and sends, zero-byte cases, truncated frames, final pipeline segments,
  wrong advance positions, pooled-buffer use-after-return, backpressure stalls,
  and completion on every exceptional path.

Evidence: the official [`Stream.Read` contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.read?view=net-10.0)
allows an implementation to return fewer bytes than requested and assigns meaning
to a zero result. Microsoft's [`System.IO.Pipelines` guidance](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines#pipereader-common-problems)
documents data loss, infinite loops, unbounded buffering, memory corruption, and
leaks caused by incorrect `AdvanceTo`, buffer lifetime, completion, and final-data
handling, as well as explicit backpressure thresholds.

## P1 - Add filesystem path, identity, and race proofs

Model paths as platform-qualified names and file operations as temporal effects
over mutable external state. Complement path-taint diagnostics by proving lexical
containment, base-directory stability, link-aware identity, and race-safe use, or
by explaining why a check-then-act sequence cannot establish those facts.

Acceptance criteria:

- Represent path roots, volumes, UNC and device prefixes, segments, separators,
  drive-relative forms, current directory, trailing separators, alternate data
  streams, and normalized dot segments under an explicit Windows, Linux, macOS,
  container, or virtual-filesystem profile.
- Distinguish ordinal path text, lexical normalization, fully qualified paths,
  and the physical identity reached after symlinks, junctions, reparse points,
  mounts, case folding, Unicode normalization, and file-system-specific aliases.
  `GetFullPath` is not treated as link resolution or proof that an object exists.
- Prove containment by root and segment identity under the selected comparer,
  never by an unqualified string prefix. Relative paths require a stable explicit
  base; ambient current-directory, drive-current-directory, environment, or home
  expansion contributes mutable capability and determinism evidence.
- Track directory or file handles and descriptor-relative operations as stable
  identities where the platform contract supports them. A path re-resolved after
  validation is a new external-state observation unless an atomic API, protected
  directory handle, or environmental assumption closes the substitution window.
- Model `File.Exists`, `Directory.Exists`, metadata queries, access checks, and
  enumeration as snapshots that can be false for multiple causes and become stale
  immediately. A successful check does not prove a later open, delete, create, or
  move acts on the same object or will succeed.
- Propagate symlink replacement, rename, mount changes, file-system watchers,
  concurrent processes, and permission changes as interference. Bounded closed
  virtual filesystems may prove stronger facts; real open external filesystems
  remain conditional unless the operation itself is race-safe.
- Compose path facts with taint validators, capabilities, exception and outcome
  contracts, ownership of handles, platform availability, deterministic behavior,
  and archive extraction. Sanitizing separators does not automatically prove
  containment, nonexistence of aliases, or safe overwrite behavior.
- Emit paired check/use traces with path forms, base, comparer, link chain,
  identities, interference event, and final target. Tests cover sibling-prefix
  attacks, `..`, rooted resets, UNC and device paths, case variation, Unicode,
  symlink swaps, exclusive create, handle-relative access, current-directory
  changes, and platform-specific invalid names.

Evidence: [`Path.GetFullPath`](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getfullpath?view=net-10.0)
documents that the one-argument overload depends on mutable current drive and
directory state and recommends an explicit base for deterministic resolution.
The [`File.Exists` contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.exists?view=net-10.0)
returns false for several errors, warns against path validation, and explicitly
warns that another process can change the file between the check and a later
operation.

## P1 - Add bounded reentrancy and call-out safety contracts

Track when external or overridable code can call back into an object, component,
thread-affine context, or operation before the original call has restored its
stable state. Make reentrancy a first-class protocol property across callbacks,
events, virtual dispatch, message pumping, synchronous continuations, and
interop, rather than assuming a held monitor or same-thread execution prevents it.

Acceptance criteria:

- Define contracts for reentrant, nonreentrant, guarded, and callback-free
  operations plus the state phases in which call-out is permitted. Inherited and
  interface contracts obey behavioral subtyping; an override cannot introduce an
  unannounced call-out or weaken a base nonreentrancy guarantee.
- Mark potential call-out sites including delegates, events, virtual or interface
  dispatch on externally controlled receivers, user comparers, converters,
  logging hooks, dispose callbacks, COM and native calls, synchronous task
  continuations, custom awaiters, message pumps, and model-pack-defined hooks.
- At each call-out, prove object invariants and reentrant-visible fields are in a
  permitted stable phase, or prove a guard rejects every callback. A monitor held
  by the current thread is recursive and therefore is not, by itself, a
  nonreentrancy guard.
- Build bounded call-out/call-back cycles with receiver identity, operation phase,
  held locks, guards, and callback target sets. Same-method recursion,
  cross-method reentry, cross-object cycles, event mutation during notification,
  and callbacks from property accessors remain distinct cases.
- Model recognized guards such as busy flags, depth counters, tokens, and
  `BlockReentrancy`-style scopes only after proving acquisition, release on every
  exit, alias integrity, and correct nested semantics. Throwing on reentry has an
  exceptional contract and is not equivalent to preventing the attempt.
- Compose with object invariants, reads/modifies frames, lock order, data races,
  linearizability, ownership, async suspension, cancellation, and type
  initialization. Reentry on the same thread can violate state without creating a
  data race, while moving a callback outside a lock can change race and atomicity
  obligations.
- Summaries expose possible call-out phases, target constraints, guards, locks,
  and reentrant effects. Unknown delegate storage, dynamic invocation, external
  native pumping, or an unbounded cycle yields explicit unknown evidence.
- Emit a witness showing the outer call, unstable mutation, call-out, callback
  target, reentry edge, violated phase or invariant, and held locks. Tests cover
  events, observable collections, recursive monitors, synchronous continuations,
  comparers, logging, disposal, COM callbacks, exceptions, and guards that leak
  or reset too early.

Evidence: .NET's [synchronization overview](https://learn.microsoft.com/en-us/dotnet/standard/threading/overview-of-synchronization-primitives#monitor-class)
states that a thread holding a monitor can acquire it again. The
[`ObservableCollection.CheckReentrancy` contract](https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1.checkreentrancy?view=net-10.0)
detects mutation attempted during a collection-change callback, while
[`ConcurrentDictionary` remarks](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2?view=net-10.0#remarks)
place user delegates outside locks and explicitly exclude their code from the
operation's atomicity.

## P1 - Prove bounded nonblocking progress guarantees

Complement safety, race, deadlock, and linearizability proofs with explicit
obstruction-free, lock-free, wait-free, starvation-free, and bounded-retry
claims. Verify progress under a declared scheduler and interference model rather
than inferring it from the presence of compare-exchange loops or from one
terminating execution.

Acceptance criteria:

- Define separate contracts for obstruction freedom, lock freedom, wait freedom,
  starvation freedom, and a quantitative per-operation step or retry bound. Each
  contract records thread count, operation set, scheduler fairness, failure and
  cancellation policy, memory model, and environmental assumptions.
- Interpret wait freedom as completion of every operation within a finite bound
  despite other participating operations; lock freedom as system-wide recurring
  completion; and obstruction freedom as completion after bounded solo execution.
  Do not substitute one level for another or equate deadlock freedom with any of
  them.
- Build a bounded concurrent transition system over reads, writes, atomic steps,
  retries, helping, yields, backoff, allocation, reclamation, and operation
  completion. Apply partial-order and symmetry reduction only with a checked
  independence argument.
- Use termination variants that decrease under the interference allowed by the
  selected guarantee. A loop variant that decreases only in an interference-free
  source trace cannot prove lock or wait freedom; helping must credit completion
  to the correct pending operation.
- Treat locks, blocking waits, synchronous I/O, unknown callbacks, scheduler
  capture, GC-dependent reclamation, allocation failure, and unbounded runtime
  services according to explicit assumptions. Calling a framework type described
  as lock-free does not make a multi-step caller protocol lock-free.
- Integrate atomic and memory-order semantics, ABA and lifetime models, bounded
  linearizability, cancellation, exceptions, and complexity. A safety proof can
  remain valid when progress is unknown, and a progress proof cannot hide an
  incorrect result or lost operation.
- Emit a lasso or finite starvation witness with scheduler choices, competing
  operations, atomic outcomes, retry counts, pending ownership, and the missing
  fairness or decreasing fact. Distinguish a real counterexample from exhaustion
  of thread, step, heap, or interference bounds.
- Test CAS counters and stacks, helping queues, unfair retries, livelock, ABA,
  spin-then-yield, cancellation, bounded contention, and algorithms that are
  linearizable but not lock-free or lock-free but not wait-free. Stress runs may
  find fixtures but never replace the universal bounded proof.

Evidence: Herlihy's original [wait-free synchronization paper](https://cs.brown.edu/~mph/Herlihy91/p124-herlihy.pdf)
defines wait-free implementations in terms of every operation completing in a
finite number of steps despite concurrent interference. The .NET
[`SpinWait` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.spinwait?view=net-10.0)
shows that real progress behavior includes architecture-sensitive spinning and
yielding intended to mitigate starvation, which must be an explicit runtime and
scheduler assumption rather than a source-level guess.

## P1 - Detect inconsistent and vacuous contract proofs

Check that contract assumptions describe at least one feasible execution before
accepting a proof. Contradictory preconditions, invariants, inherited contracts,
or model assumptions must not make every postcondition appear proved merely
because the entry state or relevant exit is unreachable.

Acceptance criteria:

- Check satisfiability of every `Requires` clause and their conjunction under
  type, nullability, range, generic, platform, configuration, invariant, and
  trusted-model facts. Report a single impossible clause separately from a
  contradiction that only appears after composition.
- Check that object invariants are realizable in each declared stable phase and
  can be established by at least one valid construction path. Construction-only
  relaxation must not silently make the first public state impossible.
- Before proving `Ensures`, exceptional, frame, temporal, or resource claims,
  establish a feasible entry and a feasible exit of the relevant kind. Distinguish
  an unreachable method, an unreachable normal return, and an unreachable named
  exception from a proof of the claim on reachable executions.
- Compose interface, base, override, partial-method, caller, and model-pack
  contracts and diagnose contradictions at the boundary where they arise.
  Behavioral-subtyping checks do not replace whole-contract consistency checks.
- Return a source-mapped unsatisfiable core containing the smallest practical set
  of conflicting clauses and implicit facts. Include provenance for generated,
  inherited, inferred, assumed, and third-party facts.
- Detect clauses that are irrelevant to a proof by checked dependency or
  mutation analysis. Label these as potentially vacuous or redundant rather than
  claiming that a syntactically relevant property fully captures user intent.
- Support bounded quantifiers, unions, old values, typestates, temporal phases,
  and platform alternatives. Solver limits, unsupported theories, or unbounded
  environmental facts produce `unknown`, never a clean consistency result.
- Expose `consistent`, `inconsistent`, `vacuous`, `redundant`, and `unknown` in
  CLI, SARIF, API, cache, baseline, and release-gate output. Tests cover false
  literals, conflicting ranges and null states, impossible invariants, inherited
  contradictions, unreachable exits, redundant clauses, and solver timeouts.

Evidence: IBM Research's [vacuity-detection paper](https://research.ibm.com/publications/efficient-detection-of-vacuity-in-temporal-model-checking)
explains that an implication can be valid only because its precondition is never
satisfied, hiding specification errors. Its follow-up
[Before and after vacuity](https://research.ibm.com/publications/before-and-after-vacuity)
frames vacuity checks as a practical complement to ordinary model checking.

## P1 - Enforce ValueTask and pooled-awaitable consumption protocols

Model `ValueTask`, `ValueTask<T>`, and `IValueTaskSource` as consumable protocol
values, not merely completed result containers. Detect duplicate, premature,
mixed, or stale consumption across struct copies and pooled source reuse.

Acceptance criteria:

- Give each logical operation a symbolic identity, backing kind, source token,
  and version. Copies of a `ValueTask` struct alias the same operation rather than
  creating independent permission to await or read it.
- Track created, pending, completed, consumed, converted, preserved, reset, and
  reused states. `await`, `GetAwaiter().GetResult()`, and `.Result` consume the
  operation according to its public contract and require completion where the API
  requires it.
- Reject multiple awaits, multiple `AsTask` calls, and mixtures of awaiting,
  result access, and task conversion unless a recognized API contract explicitly
  supplies a reusable representation. Do not infer safety from one observed
  backing implementation when the declared value permits another.
- Model `IValueTaskSource` status, continuation registration, `GetResult`, token
  comparison, and version rollover. A stale token or source reset before the
  prior consumer has finished produces a protocol counterexample.
- Cover framework producers such as async streams, pipelines, sockets, channels,
  and dispose paths, plus user sources built with
  `ManualResetValueTaskSourceCore<T>`. Configure-await wrappers and preservation
  must retain the correct operation identity.
- Propagate consumption state through assignments, returns, parameters, fields,
  closures, conditional merges, tuples, and bounded collections. Escape to
  unanalyzed code or an unknown producer yields explicit conditional evidence.
- Compose with cancellation, exceptions, ownership, disposal, async suspension,
  race analysis, and resource protocols. An exception during consumption does
  not automatically restore permission or make source reuse safe.
- Emit a trace containing producer, copies, backing kind, token/version, state
  transitions, and both conflicting consumers. Tests cover double await,
  premature result access, repeated `AsTask`, mixed consumption, pooled reset,
  stale tokens, async-enumerator moves, and safe task-backed preservation.

Evidence: The official [`ValueTask<TResult>` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0)
states that a value may be awaited only once, forbids multiple `AsTask` calls and
mixed consumption techniques, and says violations have undefined results. The
[`IValueTaskSource<TResult>` API](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.sources.ivaluetasksource-1?view=net-10.0)
exposes the token-bearing status, continuation, and result protocol needed for
allocation-free pooled producers.

## P1 - Model condition variables, semaphores, events, and barriers

Extend concurrent proofs beyond locks and atomics to the stateful signaling
protocols used for coordination. Prove wait predicates, counts, phases, and
signal retention so lost wakeups, premature passage, and invalid releases have
reproducible bounded witnesses.

Acceptance criteria:

- Define transition systems for `Monitor.Wait/Pulse/PulseAll`, `Semaphore` and
  `SemaphoreSlim`, `AutoResetEvent`, `ManualResetEvent`, `CountdownEvent`, and
  `Barrier`, including owners, wait and ready sets, counts, signaled state,
  participants, phase, cancellation, timeout, and disposal.
- Model `Monitor.Wait` releasing and reacquiring the monitor before return while
  preserving the required ownership protocol. `Pulse` requires ownership, moves
  an existing waiter to readiness, and is lost when no waiter exists.
- Prove condition waits against an explicit shared-state predicate and account
  for interference after notification and before reacquisition. A notification
  is not proof that the predicate is true; guarded loops and timeout return values
  must be verified on every path.
- Track semaphore count and maximum count, successful and failed waits, release
  deltas, overflow, cancellation, and async continuations. Detect permit leaks,
  unmatched releases, and protocols that mistake timeout or cancellation for
  acquisition.
- Distinguish auto-reset single-consumer signal retention from manual-reset
  broadcast state. Model set, reset, set-and-wait, and races between signal
  observation and reset without treating all event primitives as interchangeable.
- Track countdown zero transitions and barrier participant/phase changes,
  post-phase actions, exceptional phases, and illegal reentrancy. Dynamic
  participant changes must preserve the phase obligations of existing waiters.
- Compose signaling with happens-before facts, lock order, bounded deadlock,
  reentrancy, async context, disposal, and progress assumptions. Scheduler
  fairness and which waiter proceeds remain declared bounds, not hidden axioms.
- Emit a schedule with primitive state, waiter identity, predicate/count/phase,
  signal retention, lock ownership, and the first invalid transition. Tests cover
  pulse-before-wait, `if` versus `while`, timeout races, double release, auto versus
  manual reset, countdown underflow, barrier mutation, cancellation, and disposal.

Evidence: The official [`Monitor.Pulse` documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.pulse?view=net-10.0)
states that a pulse is not remembered when no thread is waiting and that the
selected waiter still must reacquire the monitor. The .NET
[synchronization-primitives overview](https://learn.microsoft.com/en-us/dotnet/standard/threading/overview-of-synchronization-primitives)
documents the distinct state and coordination roles of events, semaphores,
barriers, countdown events, and monitors.

## P1 - Add target-aware SIMD and hardware-intrinsic proofs

Prove lane-wise vector code and guarded hardware intrinsics under explicit
runtime, architecture, and instruction-set profiles. Preserve exact value and
memory semantics across accelerated and fallback paths instead of classifying
intrinsics only as opaque pure or impure calls.

Acceptance criteria:

- Model fixed vectors (`Vector2`, `Vector3`, `Vector4` and
  `Vector64/128/256/512<T>`) and the runtime-selected lane count of `Vector<T>`.
  A proof records element type, lane count or symbolic width, and target profile.
- Implement lane-wise integer bit-vector and IEEE floating-point operations,
  comparisons, masks, reductions, conversions, widening, narrowing, saturation,
  shuffle, permute, extract, and insert with the API's exact signedness and
  exceptional behavior.
- Treat `IsSupported` and `IsHardwareAccelerated` checks as target-profile facts
  with correct control-flow scope. A proof for an AVX2 branch does not establish
  ARM64, WebAssembly, fallback, or future wider-`Vector<T>` behavior.
- Model x86, ARM, and other supported intrinsic families from versioned runtime
  profiles. Unguarded unsupported instructions, unknown immediate constraints,
  or unavailable lowering yield a counterexample or explicit `unknown`, not an
  assumed managed equivalent.
- Verify loads, stores, gather/scatter, alignment, by-reference arithmetic,
  overlap, pinning, and unsafe bounds against the selected vector width and
  memory model. Endianness-sensitive reinterpretation remains target-aware.
- Compare accelerated and software paths when the application claims equivalent
  results, while preserving documented floating-point and reduction-order
  differences. Hardware acceleration is a performance fact, not a blanket
  semantic equivalence axiom.
- Compose vector semantics with constant-time traces, overflow policy, generic
  math, platform guards, AOT/trimming, allocation, and complexity contracts.
  Runtime dispatch and multiversioning appear in proof evidence.
- Add differential conformance tests against supported .NET runtimes and target
  architectures for edge lanes, NaNs, signed zero, overflow, masks, shifts,
  shuffles, unsupported paths, alignment, and runtime-selected vector widths.

Evidence: Microsoft's [.NET SIMD overview](https://learn.microsoft.com/en-us/dotnet/standard/simd)
states that `Vector<T>.Count` is CPU-dependent and distinguishes fixed-size
vectors from runtime-chosen vectors and hardware fallback. The official
[`Avx2.IsSupported` contract](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.avx2.issupported?view=net-10.0)
provides the runtime guard that target-specific intrinsic proofs must respect.

## P1 - Add URI, origin, redirect, and endpoint identity proofs

Model the transformations between untrusted URI text, canonical components,
HTTP origins, redirect targets, DNS answers, and pooled connections. Prove
allowlists and outbound-network policies against the identity actually reached,
not a convenient string form checked earlier.

Acceptance criteria:

- Parse absolute and relative `Uri` values into scheme, user information, host,
  port, path segments, query, and fragment while retaining original, escaped,
  display, DNS-safe, and canonical forms as distinct symbolic values.
- Reproduce .NET canonicalization for scheme and host case, default ports, IPv4
  and IPv6 forms, IDN/Punycode, percent escapes, dot-segment removal, IRI options,
  file/UNC forms, and custom parsers under an explicit runtime/platform profile.
- Define origin equality from canonical scheme, host, and effective port. Path
  containment uses decoded segment structure and scheme-specific rules, never a
  raw prefix; base-plus-relative resolution is modeled before policy checks.
- Treat escaping, unescaping, formatting, and well-formedness as contextual
  transformations rather than universal sanitizers. Taint is removed only by a
  validator whose contract proves the exact sink policy and canonical form.
- Model HTTP redirect status, method/body handling, relative `Location`, maximum
  hops, loops, authorization and cookie behavior, and secure-to-insecure rules by
  runtime profile. Revalidate scheme, origin, path, and address policy at every
  hop before following it.
- Separate a hostname or origin from its DNS answers, proxy route, connected
  address, and TLS peer. Include resolution time, address family, connection-pool
  reuse, `PooledConnectionLifetime`, and environmental rebinding in the evidence.
- Compose with outbound taint sinks, network capabilities, secrets, retries,
  cookies, filesystem URI handling, platform behavior, and determinism. SSRF
  allowlists can require both canonical-origin and resolved-address constraints;
  open DNS or proxies produce conditional results.
- Emit an end-to-end witness containing input text, every normalization and
  resolution step, redirect chain, policy decision, DNS/proxy observation, pooled
  connection identity, and final endpoint. Tests cover user-info confusion,
  default ports, IDN, alternate IP text, encoded dot segments, base resolution,
  redirect escape, DNS rebinding, proxy routing, and stale pooled connections.

Evidence: The official [`Uri` documentation](https://learn.microsoft.com/en-us/dotnet/api/system.uri?view=net-10.0)
details canonicalization of host case, ports, escapes, IPv6, and dot-segments and
warns callers to validate assumptions for untrusted input. The
[`AllowAutoRedirect` contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-10.0)
documents redirect limits, authorization and cookie handling, and runtime-specific
HTTPS downgrade behavior. Microsoft's
[`HttpClient` guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
explain that DNS is resolved only when a connection is created and that pooled
connections do not honor DNS TTL changes without an explicit lifetime.

## P1 - Prove task completion and combinator protocols

Model `TaskCompletionSource`, task combinators, and timeout wrappers as explicit
completion state machines. Prove that every exposed task reaches exactly one
valid terminal state and that callers interpret aggregate, winner, cancellation,
and timeout outcomes without inventing completion of underlying work.

Acceptance criteria:

- Track each `TaskCompletionSource` and its exposed task through pending,
  succeeded, faulted, and canceled states with result, exception set, and
  cancellation-token identity. All aliases share one completion cell.
- Prove every bounded producer path either completes the exposed task or
  transfers a documented completion obligation. Exceptions, cancellation,
  early returns, reset, disposal, callback failure, and registration failure
  must not strand existing waiters.
- Model `SetResult`, `SetException`, and `SetCanceled` as throwing on a losing
  completion race, while `TrySet*` returns whether it won. Concurrent completion
  is checked with atomic identity and bounded scheduling rather than source order.
- Treat `RunContinuationsAsynchronously` as a scheduling and reentrancy fact.
  Without it, arbitrary registered continuations may execute synchronously on
  the completing path and compose with held locks, invariants, and call-out rules.
- Give `WhenAll` exact empty-input, result-order, fault aggregation, cancellation
  precedence, and success semantics. One early fault does not prove sibling work
  is complete before the aggregate task reaches its terminal state.
- Give `WhenAny` a distinct winner task and outer completion. Prove the caller
  awaits or inspects the winner's result; outer successful completion does not
  imply that the winning task succeeded.
- Model `WaitAsync`, timeout races, delays, continuations, unwrap, and recognized
  custom async coordination summaries. Timing out or canceling a wait does not
  cancel the underlying operation unless token flow proves that separate fact.
- Emit a witness with source/task identity, waiters, competing completion sites,
  continuation mode, combinator membership, and terminal-state transitions.
  Tests cover missing completion, double `Set*`, safe `TrySet*`, reset races,
  synchronous continuation reentry, empty combinators, mixed faults/cancellation,
  lost winner faults, and timeout without operation cancellation.

Evidence: Microsoft's [task-completion guidance](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/complete-your-tasks)
states that an exposed `TaskCompletionSource` must complete on every path and
distinguishes throwing `Set*` calls from race-safe `TrySet*` calls. The official
[`Task.WhenAll` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenall?view=net-10.0)
defines fault aggregation, cancellation precedence, success, and empty-input
behavior that a compositional proof must preserve.

## P1 - Prove cryptographic key, nonce, and entropy protocols

Add purpose-aware symbolic identities for cryptographic material and operations.
Prove key/nonce uniqueness, entropy sources, algorithm parameters, authentication
order, export policy, and erasure obligations instead of treating cryptographic
APIs only as impure calls or constant-time summaries.

Acceptance criteria:

- Label values by cryptographic role including key, private key, public key,
  nonce or IV, salt, tag, associated data, seed, password, derived material, and
  plaintext. Preserve identity, origin, byte length, secrecy, and permitted uses
  through spans, arrays, pools, encodings, fields, and validated summaries.
- Distinguish cryptographically strong randomness from `Random`, deterministic
  test sources, counters, timestamps, GUIDs, and unknown entropy. Contracts state
  whether a value requires unpredictability, uniqueness, or both.
- For nonce-sensitive modes such as AES-GCM, prove that the `(key, nonce)` pair
  cannot repeat within the bounded key lifetime, including across retries,
  restarts, cloning, serialization, concurrent producers, counter wrap, and
  pooled buffers. Random generation alone is not a universal uniqueness proof.
- Model supported algorithm, key, nonce, tag, salt, iteration, and output-size
  constraints from versioned runtime and security-policy profiles. Obsolete or
  provider-specific choices remain policy findings or explicit unknowns.
- Track encryption, decryption, signing, verification, hashing, derivation,
  import, export, rotation, and disposal as typed operations. Do not expose or
  act on unauthenticated plaintext before successful verification when the API
  protocol requires authentication.
- Prove zeroization over every live writable alias and relevant pooled return
  path when a contract requires erasure. Clearing one copy does not erase strings,
  immutable encodings, exported blobs, logs, unmanaged copies, or other aliases.
- Compose with taint, secret-independent traces, ownership, disposal, exceptions,
  platform profiles, native interop, configuration, and framework-model drift.
  Hardware-backed and opaque provider operations require signed summaries of the
  guarantees actually used.
- Emit a witness with material roles, key identity, nonce history, source of
  entropy, parameter profile, copies, operation order, and first violated use.
  Tests cover repeated GCM nonces, counter wrap, `Random` keys, correct RNG use,
  weak parameters, verify-before-use, export leakage, partial erasure, and pool
  reuse containing secrets.

Evidence: .NET's [`RandomNumberGenerator.GetBytes` contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes)
identifies the platform API for cryptographically strong random bytes, while
[`CryptographicOperations.ZeroMemory`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory)
exists specifically to preserve security-motivated erasure writes. NIST
[SP 800-38D](https://csrc.nist.gov/pubs/sp/800/38/d/final) requires control of
GCM key/IV pair reuse, making relational history part of a sound AEAD proof.

## P1 - Track pooled-buffer leases and ownership

Extend byref and resource reasoning to `ArrayPool<T>`, `MemoryPool<T>`,
`IMemoryOwner<T>`, and heap-storable memory views. Prove rent, lease, transfer,
return, clearing, and alias validity so reuse cannot create data corruption,
cross-request disclosure, or use-after-return behavior.

Acceptance criteria:

- Give every rented buffer an allocation generation, pool identity, requested
  length, actual length, owner, content-initialization state, and active lease.
  `Rent(n)` guarantees sufficient capacity, not exact length or cleared contents.
- Require each `ArrayPool<T>` rental to be returned at most once to the same pool.
  Returning an arbitrary array, using any alias after return, or retaining a
  slice across a later rental of that generation is a protocol violation.
- Track `Memory<T>`, `ReadOnlyMemory<T>`, `Span<T>`, `ArraySegment<T>`, and slices
  as views over the same backing identity and range. A read-only view prevents
  mutation through that view; it does not freeze concurrent owners or extend the
  backing storage's lifetime.
- Model `IMemoryOwner<T>` as a transferable linear owner. It must be disposed
  exactly once or transferred, and all derived leases expire on disposal unless
  a specific provider contract says otherwise.
- Infer synchronous leases through return and asynchronous leases through the
  returned task's terminal state, with explicit annotations for storage or
  transfer. Callbacks, native operations, pipelines, and unknown callees cannot
  silently retain a borrowed buffer.
- Propagate initialization at element and slice granularity. Consumers may read
  only written regions, and secret-bearing rentals must satisfy a declared clear
  policy before reuse or return; clearing only the logical prefix is not assumed
  to clear spare capacity.
- Compose with partial I/O, ownership, disposal, data races, unsafe memory,
  pinning, native interop, cryptographic erasure, exceptions, and async
  suspension. Return in `finally` is valid only after all users have stopped.
- Emit a trace with pool, generation, owner transfers, aliases and ranges, task
  lifetime, clearing facts, return, and subsequent reuse. Tests cover oversized
  rentals, dirty contents, double return, wrong pool, escaped slices, async use
  after completion, concurrent consumers, owner transfer, secret remnants, and
  custom pools with incomplete summaries.

Evidence: The official [`ArrayPool<T>.Return` contract](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0)
states that return gives up ownership, forbids later use and a second return, and
calls violations high-severity security issues. Microsoft's
[`Memory<T>` usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)
define owners, consumers, leases, transfer, synchronous and task-bounded use, and
the disposal obligation of `IMemoryOwner<T>`.

## P1 - Model timers, deadlines, and timeout races

Connect temporal values to scheduled work. Prove clock-domain selection,
deadline arithmetic, timer callback concurrency, tick consumption, cancellation,
and shutdown so elapsed-time policies and timeout wrappers remain correct under
drift, coalescing, queued callbacks, and disposal races.

Acceptance criteria:

- Distinguish wall-clock instants, monotonic timestamps, elapsed durations, and
  scheduler ticks. Deadline contracts state their clock or `TimeProvider`, start
  observation, inclusivity, resolution, overflow behavior, and environmental
  assumptions.
- Model `Task.Delay`, `CancellationTokenSource.CancelAfter`, `WaitAsync`,
  `TimeProvider` timers, `System.Threading.Timer`, `System.Timers.Timer`, and
  `PeriodicTimer` with versioned due-time, period, infinite, and range semantics.
- Represent timer callbacks as thread-pool or affinity-specific concurrency
  roots. Periodic callbacks may overlap when work exceeds the interval; callback
  code must prove reentrancy, synchronization, and invariant obligations.
- Treat ordinary `Timer.Dispose` as stopping future scheduling without assuming
  already queued callbacks have finished. Recognized waiting and asynchronous
  disposal paths establish quiescence only after their documented completion.
- Model `PeriodicTimer` as a single-consumer auto-reset protocol: ticks can
  coalesce, disposal returns `false`, cancellation affects one wait but not the
  timer, and concurrent waits violate its contract.
- Prove timeout races with three distinct facts: the wait timed out, cancellation
  was requested, and underlying work terminated. Retry, cleanup, resource reuse,
  or duplicate submission cannot rely on one fact as proof of the others.
- Compose timer state with cancellation, task completion, execution context,
  scheduler fairness, progress, reentrancy, disposal, deterministic behavior,
  and platform profiles. Mock or virtual time is trusted only through validated
  provider summaries and advances all registered work consistently.
- Emit a schedule containing clock observations, arithmetic, timer generation,
  queued and running callbacks, tick coalescing, cancellation, disposal, and
  underlying operation state. Tests cover clock jumps, deadline overflow, zero
  and infinite delays, overlapping callbacks, dispose races, concurrent periodic
  waits, coalesced ticks, timeout-with-live-work, and virtual-time advancement.

Evidence: The official [`System.Threading.Timer` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.timer?view=net-10.0)
states that callbacks run on thread-pool threads, may overlap, and may execute
after ordinary disposal because they were already queued. The
[`PeriodicTimer.WaitForNextTickAsync` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync?view=net-10.0)
documents tick coalescing, its single-consumer rule, disposal result, and the fact
that canceling one wait does not stop the underlying timer.

## P1 - Add database command, transaction, and isolation proofs

Add a provider-profiled ADO.NET protocol domain for connection, command, reader,
parameter, transaction, and ambient-scope state. Prove that multi-command units
commit or roll back as intended and that isolation, retries, and parameter
binding support the application's claimed data and security invariants.

Acceptance criteria:

- Track connection identity and closed, open, broken, enlisted, and disposed
  states; transaction identity and active, committed, rolled-back, in-doubt, and
  disposed states; and command/reader ownership of the connection.
- Require a command executed during a local transaction to use the same
  connection and transaction identity. Model active readers, provider limits,
  savepoints, nested scopes, and connection closure through explicit provider
  profiles rather than assuming interchangeable databases.
- Prove each transaction path reaches an allowed terminal outcome. Commit may
  fail or become in-doubt, rollback may also fail, and disposal or connection
  closure can trigger provider-defined rollback; cleanup is not evidence that a
  requested commit succeeded.
- Model `TransactionScope` option, async-flow setting, nested voting, ambient
  enlistment, suppression, promotion to distributed coordination, and completion.
  Crossing an async boundary without the required flow is a distinct witness.
- Represent isolation guarantees and permitted anomalies for recognized
  providers, including dirty, nonrepeatable, phantom, write-skew, and lost-update
  scenarios. An isolation enum name does not prove stronger provider behavior.
- Parse supported constant command shapes and bind `DbParameter` identity,
  placeholder convention, direction, type, size, precision, scale, null/`DBNull`,
  and value. Dynamic SQL or stored procedures require a signed schema/model
  summary; parameters prove value separation, not identifier safety.
- Compose with taint, outcome-use, cancellation, partial I/O, retries,
  idempotency, disposal, secrets, capabilities, and deterministic evidence.
  Retrying an ambiguous commit or non-idempotent command is not automatically
  safe, even when the transient exception policy allows a retry.
- Emit a witness with connection and transaction identities, command text shape,
  parameter bindings, isolation profile, interleaved database actions, terminal
  outcome, and retry. Tests cover missing enlistment, wrong connection, commit and
  rollback failure, leaked readers, ambient-flow loss, savepoints, isolation
  anomalies, injection through identifiers, truncating parameters, and ambiguous
  retry outcomes.

Evidence: Microsoft's [ADO.NET local-transaction guidance](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/local-transactions)
requires commands to be explicitly enlisted in their connection's active
transaction and distinguishes commit, rollback, and automatic rollback on close.
The official [parameter guidance](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/configuring-parameters-and-parameter-data-types)
explains that parameter values are type-checked literals rather than executable
command text, while provider syntax and type mappings differ.

## P1 - Prove dependency-injection lifetime and scope safety

Build the effective dependency graph for the .NET service container and prove
that each resolution, capture, and disposal respects transient, scoped, and
singleton lifetimes. Extend the existing resolved-service disposal warning into
a path- and scope-aware protocol rather than relying only on constructor shapes.

Acceptance criteria:

- Resolve ordered registrations, implementation types and instances, factories,
  open and closed generics, keyed services, `IEnumerable<T>`, replacement and
  `TryAdd` behavior, and recognized framework registration helpers under a
  versioned container profile.
- Give the root provider and every `IServiceScope` a symbolic identity. A scoped
  service is unique per scope, a singleton is shared by the root, and a transient
  is a fresh resolution unless an explicit factory or registration says otherwise.
- Detect direct and transitive captive dependencies, including a singleton that
  constructor-injects a scoped service, resolves one through its provider, stores
  one from a callback, or captures a shorter-lived factory result. Creating and
  disposing an explicit scope may discharge the obligation.
- Verify resolution from root versus child providers, scope escape through fields,
  closures, tasks, event handlers, caches, and background services, and use after
  scope disposal. Async work cannot outlive a scope unless ownership is transferred
  through an explicit contract.
- Model container-owned synchronous and asynchronous disposal, reverse dependency
  ordering, externally supplied instances, factory failures, partial graph
  construction, and repeated disposal. Code that resolves a service does not gain
  permission to dispose the container-owned instance.
- Require singleton implementations and captured dependencies to satisfy declared
  thread-safety contracts when resolutions can be used concurrently. Promoting a
  scoped service to root lifetime also promotes its mutable state and disposal
  obligations; it is not merely an allocation issue.
- Diagnose missing, ambiguous, cyclic, invalid open-generic, and inaccessible
  registrations at each selected composition root. Dynamic reflection, third-party
  containers, runtime-mutated collections, and opaque factories require signed
  model summaries or yield explicit unknown evidence.
- Emit a graph witness with registration source, service and implementation type,
  lifetime, provider/scope identities, capture path, consumers, and disposal edge.
  Tests cover keyed and enumerable services, factory capture, root resolution,
  hosted services, async scope escape, disposal ownership, cycles, multitenant
  scopes, and development versus production scope validation.

Evidence: Microsoft's [.NET service-lifetime guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes)
defines transient, scoped, and singleton behavior, requires scoped services to be
used from a scope, and warns that resolving one from a singleton effectively
promotes it. The official [DI overview](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview)
explains that scope validation rejects scoped services resolved from the root or
injected into singletons and that the container owns their disposal.

## P1 - Model channel completion, backpressure, and item loss

Give `System.Threading.Channels` a bounded producer-consumer state model. Prove
completion ownership, queue contents, capacity, loss policy, and reader/writer
promises so an impure-call classification cannot hide dropped work, stranded
waiters, writes after closure, or unsafe synchronous continuation reentry.

Acceptance criteria:

- Represent channel identity, ordered contents, capacity, bounded or unbounded
  mode, full mode, completion exception, active readers and writers, pending
  operations, and the `SingleReader`, `SingleWriter`, and
  `AllowSynchronousContinuations` creation promises.
- Model `TryWrite`, `WriteAsync`, and `WaitToWriteAsync` separately. A successful
  readiness wait does not reserve capacity against competing writers, and a
  failed try-write does not mean the channel is complete unless state proves it.
- Implement `Wait`, `DropWrite`, `DropNewest`, and `DropOldest` at item identity
  level, including the dropped-item callback. A no-loss or exactly-once contract
  must exclude or explicitly account for every configured drop path.
- Model `TryRead`, `ReadAsync`, `WaitToReadAsync`, `TryPeek`, `ReadAllAsync`, and
  the `Completion` task through buffer drain and terminal state. Distinguish an
  empty open channel, successful completion, and completion with an exception.
- Prove exactly one winning completion and require `Complete` or `TryComplete`
  only after every producer has relinquished its write obligation. Producer
  failure and cancellation must propagate or deliberately choose a documented
  terminal policy instead of leaving consumers pending forever.
- Check single-reader and single-writer promises against all bounded concurrency
  roots. When synchronous continuations are allowed, compose a write or read with
  reentrant callback effects, held locks, invariants, and stack-depth contracts.
- Compose channel items with ownership transfer, pooled-buffer leases, task and
  `ValueTask` consumption, cancellation, progress, allocation, data races, and
  deterministic ordering. Cancellation of one pending operation does not close
  the channel or remove unrelated operations.
- Emit a schedule with queue contents, capacity, options, producers, consumers,
  wait results, item drops, completion winner, and first invalid transition.
  Tests cover all full modes, multi-producer completion, lost completion, drain
  after close, exceptional close, readiness races, violated single-party promises,
  synchronous continuation reentry, cancellation, and item ownership transfer.

Evidence: Microsoft's [`System.Threading.Channels` guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
defines bounded backpressure and all four full modes, distinguishes try, wait,
read, and write operations, and says completion should occur only after all
producers finish so consumers can drain and terminate.

## P1 - Prove TLS peer authentication and certificate policy

Model the security-relevant inputs and outcome of .NET TLS handshakes across
`HttpClient`, `SslStream`, and supported server stacks. Prove peer-name, trust,
chain, revocation, protocol, and callback policy against the endpoint actually
used rather than treating an encrypted connection as authenticated by default.

Acceptance criteria:

- Represent client and server handshake phases, target host and SNI, peer
  certificate and chain, trust anchors, validation time, policy errors, negotiated
  protocol and cipher suite, client-certificate request, and authenticated or
  rejected outcome under an explicit runtime and operating-system profile.
- Relate `SslClientAuthenticationOptions.TargetHost` and HTTP request origin to
  certificate name validation. Empty, rewritten, proxy, redirect, or IP-literal
  targets require the exact documented validation policy; DNS success does not
  establish certificate identity.
- Evaluate recognized `X509ChainPolicy` facts including system or custom roots,
  application policy, revocation mode and scope, verification flags, extra store,
  download behavior, and time. Unsupported custom chain building remains unknown.
- Analyze certificate callbacks relationally over certificate, chain, policy
  errors, request, and environment. Returning true for all inputs, using
  `DangerousAcceptAnyServerCertificateValidator`, or ignoring name/chain errors
  is an explicit authentication failure unless a narrowly scoped test policy
  excludes the shipped path.
- Support pin sets with algorithm, key or certificate identity, rotation overlap,
  expiry, backup pins, and host scope. A single permanent thumbprint comparison
  does not silently prove a maintainable or chain-valid deployment policy.
- Track protocol and cipher configuration by target. Prefer operating-system
  selection where required by policy, diagnose obsolete forced protocols, and
  preserve platform-specific unsupported or negotiation-failure paths.
- Compose with URI/redirect/DNS identity, proxies, client certificates, secret-key
  ownership, capabilities, time, callbacks, retries, connection pooling, and
  deterministic evidence. AIA or revocation downloads add bounded external I/O
  and denial-of-service assumptions to validation.
- Emit a handshake witness with logical origin, connected endpoint, target host,
  chain and errors, callback branches, trust/pin decision, negotiated parameters,
  and final use. Tests cover accept-all callbacks, missing name checks, custom
  roots, expired/revoked certificates, IP targets, redirects, proxies, client
  authentication, protocol pinning, offline revocation, and certificate rotation.

Evidence: The official [.NET TLS/SSL best practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices)
recommend operating-system protocol selection and describe custom chain policies,
trust roots, and the external I/O involved in chain validation. The
[`TargetHost` contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions.targethost?view=net-10.0)
states that the value is used for server-certificate validation, while
[`DangerousAcceptAnyServerCertificateValidator`](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.dangerousacceptanyservercertificatevalidator?view=net-10.0)
is an always-true delegate that can entirely disable validation.

## P1 - Prove process-launch arguments and executable identity

Model the boundary from managed strings to executable selection, OS argument
vectors, optional shells, and the child program's own parser. Prove command and
argument allowlists, executable identity, environment, and child lifecycle so
taint cannot cross a quoting or lookup boundary disguised as ordinary strings.

Acceptance criteria:

- Track `ProcessStartInfo.FileName`, `ArgumentList`, `Arguments`,
  `UseShellExecute`, working directory, environment, credentials, verb, redirection,
  encoding, and window settings under explicit Windows and Unix runtime profiles.
- Resolve an executable by stable absolute identity or model current directory,
  `PATH`, extensions, application aliases, shell associations, symlinks, and
  replacement races. A validated basename does not prove which file later starts.
- Preserve `ArgumentList` elements as distinct child arguments while applying the
  platform's exact launch encoding. Model `Arguments` as a single string parsed
  by platform and target conventions; quoting for one parser is not a sanitizer
  for another.
- Recognize command interpreters such as `cmd /c`, PowerShell command modes,
  `/bin/sh -c`, script hosts, and application-defined mini-languages. Once an
  argument becomes interpreter source, prove a context-specific grammar allowlist
  or retain taint through metacharacters, substitutions, redirections, and options.
- Check untrusted option injection, response files, argument prefixes, filenames
  beginning with switches, inherited environment variables, search paths, working
  directories, and child configuration files. `ArgumentList` prevents accidental
  splitting but does not make an unsafe executable or interpreter use safe.
- Compose launch with process capability, filesystem identity, secrets in
  arguments/environment, standard-stream partial I/O, cancellation, timeouts,
  disposal, kill-tree semantics, exit-code contracts, and platform availability.
  Redirected output must be drained without introducing a bounded deadlock.
- Require explicit policy for shell verbs, elevation, alternate credentials,
  inherited handles, detached children, and sandbox/container boundaries.
  Unknown child behavior remains an external capability, not a pure summary.
- Emit a witness containing taint source, executable resolution candidates,
  working directory and environment, managed argument form, encoded argv or
  command line, interpreter parse, and launched identity. Tests cover PATH
  hijacking, quoting differences, shell metacharacters, option injection, response
  files, script hosts, secret leakage, redirected-stream deadlock, and child races.

Evidence: The official [`ProcessStartInfo.ArgumentList` contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0)
states that it escapes individual arguments but still warns that untrusted data
is a security risk. [`UseShellExecute`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0)
selects shell versus direct execution and has different framework defaults, while
[CA3006](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca3006)
identifies untrusted input reaching process commands as command-injection risk.

## P1 - Add secure XML processing and serialization proofs

Add a versioned `System.Xml` domain that combines parser security, namespaces,
schema and transform behavior, and serializer shape. Prove external-resource and
expansion limits for untrusted XML while supporting bounded structural and
round-trip contracts instead of classifying XML APIs only by effects.

Acceptance criteria:

- Resolve effective settings for `XmlReader`, `XmlDocument`, LINQ to XML,
  `XPathDocument`, `XmlSerializer`, `DataSet`, XSD validation, and XSLT entry
  points, including inherited readers, custom resolvers, factories, and framework
  defaults for the selected target runtime.
- Track input trust, `DtdProcessing`, `XmlResolver`, validation type and flags,
  schemas, maximum characters, depth, entity expansion, and external URI schemes.
  DTD parsing or schema/import resolution adds explicit file or network capability
  and resource bounds; well-formedness alone is not a security proof.
- Prove that untrusted parsing cannot resolve forbidden external entities, expose
  local files or network responses, or exceed declared document, entity, depth,
  text, attribute, and schema-complexity budgets. A custom resolver requires a
  checked allowlist and redirect/path policy.
- Model elements, attributes, text, comments, processing instructions, CDATA,
  namespace declarations, expanded names, prefixes, base URI, whitespace, and
  document order. Prefix spelling is not namespace identity, and canonical or
  signed representations require an explicit canonicalization profile.
- Track XPath and XSLT expressions separately from data values. Concatenating
  tainted input into XPath, stylesheet source, extension objects, scripts, or
  document functions retains injection and capability evidence.
- Build effective `XmlSerializer` mappings for attributes, elements, arrays,
  namespaces, null/nil, defaults, polymorphic types, constructors, unknown nodes,
  and generated serializers. Round-trip claims name the observable shape and do
  not assume object identity, reference preservation, or arbitrary custom code.
- Compose with taint, URI and filesystem identity, partial I/O, disposal,
  capabilities, culture, Unicode, trimming/AOT, generated code, and secret data.
  Validation events, resolver callbacks, and transform extensions are modeled as
  call-outs with their own effects and exceptions.
- Emit a witness with input source, effective reader/resolver settings, entity or
  schema resolution chain, expansion budget, namespace/XPath steps, serializer
  mapping, and first violation. Tests cover XXE, entity bombs, resolver redirects,
  trusted DTDs, schema imports, namespace confusion, XPath injection, XSLT document
  access, serializer collisions, nil/missing values, and bounded round trips.

Evidence: Microsoft's [CA3075 guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca3075)
documents that insecure DTD processing and external resolvers can disclose local
or network data and enable denial of service. The official
[external-resource documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/xml/resolving-external-resources)
explains that XML resolvers locate external DTDs, entities, and schemas through
URIs, making resolver policy part of the parser's effective contract.

## P1 - Prove configuration binding, validation, and reload semantics

Model the effective .NET configuration and options graph from providers through
binding, validation, caching, scopes, and reload. Prove that code observes a
valid, coherent configuration version and does not confuse missing, defaulted,
stale, or secret values with checked application settings.

Acceptance criteria:

- Resolve ordered configuration providers, prefixes, path delimiters, environment
  variable normalization, command-line switches, JSON/XML/INI files, key-per-file,
  user secrets, in-memory values, chained roots, and custom provider summaries.
  Evidence records which source wins for every consumed key.
- Implement binder semantics for constructors, properties, fields, nested objects,
  collections, dictionaries, nullable values, enums, cultures, nonpublic options,
  error policies, and source-generated binding under the selected runtime profile.
  Missing, empty, explicit null, conversion failure, and a default CLR value remain
  distinct states.
- Model named options plus `IOptions<T>`, `IOptionsSnapshot<T>`, and
  `IOptionsMonitor<T>` lifetimes exactly: fixed singleton value, per-scope cached
  snapshot, and reloadable current value with notifications and selective cache
  invalidation.
- Compose data-annotation, delegate, `IValidateOptions<T>`, generated, and
  `ValidateOnStart` validators. Validation-on-first-access is not startup proof;
  nested members and collection items are checked only when the effective
  validation contract includes them.
- Treat each provider reload as a versioned external-state event. Prove invariants
  over one coherent snapshot or expose mixed-version reads, callback races,
  rejected updates, stale caches, and file-watcher limitations rather than
  assuming that related keys change atomically.
- Track `OnChange` registration, execution context, reentrancy, callback failure,
  disposal, and lifetime. A singleton consumer that stores a monitored value must
  declare whether it wants a snapshot or responds safely to later changes.
- Propagate secret and personal-data classifications from source through binding,
  validation messages, diagnostics, logs, reports, serialization, and options
  objects. Provider precedence cannot let an untrusted source override a protected
  key without explicit policy evidence.
- Emit a witness with key path, provider precedence, raw and converted state
  without secret values, options name, validation timing, version/reload events,
  scope, and first invalid observation. Tests cover missing sections, defaults,
  conversion failures, nested validation, named options, monitor races, snapshot
  stability, reload rejection, source generation, and hostile overrides.

Evidence: Microsoft's [.NET options-pattern documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
distinguishes fixed `IOptions`, scoped `IOptionsSnapshot`, reloadable
`IOptionsMonitor`, named values, validation timing, and `ValidateOnStart`. It also
notes that ordinary data-annotation validation is not recursive unless nested
objects and collection items are explicitly enabled.

## P1 - Model cache freshness, eviction, and stampede control

Add bounded cache semantics for in-memory, distributed, and recognized hybrid
caches. Prove key isolation, freshness, expiration, invalidation, population
coordination, and ownership without treating a cache hit as durable truth or a
factory helper as an atomic single-flight operation.

Acceptance criteria:

- Represent cache and node identity, key and tenant partition, serialization
  profile, missing, populating, present, stale, expired, evicted, and failed states,
  value version, writer, and all waiters or concurrent population attempts.
- Model `IMemoryCache`, `IDistributedCache`, `HybridCache`, and validated custom
  summaries with their actual sync/async get, set, remove, get-or-create, tag,
  and factory behavior. Unsupported provider consistency remains explicit unknown.
- Prove stampede-control claims. A miss followed by a factory can run concurrently
  on multiple callers unless the exact API or application lock supplies
  single-flight semantics; losing factory results still carry effects, ownership,
  exceptions, and external writes.
- Implement absolute and sliding expiration, change tokens, dependencies, refresh,
  stale windows, clock source, and cancellation. In-memory expiration is activity
  driven where documented, and no expiry time guarantees immediate physical
  removal or callback completion.
- Model size limits, entry size, priority, compaction, memory pressure, arbitrary
  early eviction, and capacity rejection. Application correctness cannot require
  an entry to remain cached, even when its nominal expiration is in the future.
- Treat eviction and refresh callbacks as asynchronous or reentrant call-outs with
  reason, state, failures, and races against replacement. Parent/child dependency
  inheritance and manual invalidation follow the selected provider contract.
- For distributed caches, express replica/node visibility, serialization fidelity,
  network partitions, write order, invalidation delay, and compare/exchange or
  lease support. A local lock cannot prove fleet-wide single population.
- Compose with mutable value aliasing, disposal, pooled buffers, secrets, taint,
  database transactions, retries, time, allocation, and deterministic behavior.
  Emit a witness with key, node, versions, misses, factories, expiry/invalidation,
  eviction, and stale read; tests cover stampedes, early eviction, sliding access,
  token races, callback reentry, tenant collisions, mutable values, and node drift.

Evidence: Microsoft's [ASP.NET Core in-memory caching guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory)
states that expiration is not driven by a background scanning timer, entries may
be evicted, and multiple requests can observe the same miss and concurrently
repopulate it. It also distinguishes local memory from distributed caches needed
to avoid multi-server consistency problems.

## P1 - Prove resilience-pipeline ordering and retry safety

Model `Microsoft.Extensions.Resilience`, HTTP resilience handlers, and validated
Polly strategies as ordered stateful protocols. Prove that retries, timeouts,
circuit breaking, hedging, fallback, and limiting preserve the operation's
idempotency, resource, deadline, and outcome contracts.

Acceptance criteria:

- Resolve the exact outer-to-inner strategy order, named or keyed pipeline,
  predicates, result and exception filters, attempt counts, delays, backoff,
  jitter, callbacks, and dynamic options version. Reordering strategies creates a
  different proof obligation and cache identity.
- Model one initial execution plus configured retries, including per-attempt state,
  delay, cancellation, handled outcomes, and final propagation. Prove replay safety
  for side effects, request bodies, streams, transactions, idempotency keys, and
  ambiguous failures before permitting a retry.
- Distinguish attempt timeout, total pipeline deadline, caller cancellation, and
  underlying work termination. A timed-out delegate may still run unless the
  exact strategy and callee token protocol prove cooperative termination.
- Give circuit breakers closed, open, half-open, isolated, and disposed states with
  sampling duration, minimum throughput, failure ratio, break duration, probe
  concurrency, partition key, and manual-control events.
- Model hedging as multiple possibly concurrent attempts with routing, delay,
  winner selection, loser cancellation, and late side effects. A successful winner
  does not erase mutations, costs, faults, or resource ownership from losing calls.
- Model fallback as a distinct result provenance, and bulkhead or rate-limiter
  rejection as an outcome rather than successful execution. Strategy callbacks
  are call-outs whose logging, metrics, exceptions, and reentrancy remain visible.
- Compute multiplicative upper bounds for attempts, concurrency, time, allocation,
  outbound calls, and rate-limit consumption across nested pipelines. Compose with
  HTTP redirects, database commits, caching, task completion, timers, and progress.
- Emit a timeline with pipeline order, options version, each attempt, handled
  outcome, breaker state, timeout/cancellation, hedge routing, callbacks, and final
  provenance. Tests cover unsafe POST retry, nonrewindable bodies, ambiguous commit,
  timeout-with-live-work, breaker races, hedge duplicates, fallback masking,
  nested budgets, option reload, and callback failure.

Evidence: Microsoft's [.NET resilience overview](https://learn.microsoft.com/en-us/dotnet/core/resilience/)
defines ordered pipelines containing retry, circuit breaker, timeout, limiting,
fallback, and hedging, and notes that three retries execute a delegate four times.
The official [HTTP resilience guidance](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
warns that retrying unsafe methods such as `POST` can duplicate data and documents
the materially significant order of total timeout, retry, breaker, and attempt
timeout strategies.

## P1 - Model rate-limiter leases, queues, and partitions

Add stateful proofs for `System.Threading.RateLimiting` so permit acquisition,
queueing, replenishment, partitioning, lease disposal, and rejection metadata are
checked as protocols. Do not infer that awaiting acquisition means it succeeded
or that a process-local limiter establishes a fleet-wide rate.

Acceptance criteria:

- Model concurrency, fixed-window, sliding-window, token-bucket, chained, and
  partitioned limiters with permit/token counts, segments, limits, replenish time,
  auto or manual replenishment, queue length, processing order, statistics, and
  disposed state under an explicit clock profile.
- Distinguish `AttemptAcquire` from `AcquireAsync`, requested permit count including
  zero, immediate rejection, queued state, cancellation, and completion with a
  failed lease. Every protected action must branch on `RateLimitLease.IsAcquired`.
- Track each lease identity, permit count, metadata, owner, active period, and
  disposal. For limiters that return concurrency permits on disposal, prove every
  acquired lease is released exactly once after protected work and never before
  escaped async work completes.
- Implement queue capacity and `OldestFirst` or `NewestFirst` admission/drop
  behavior at request identity level. Cancellation, disposal, oversized requests,
  replenishment, and competing acquisitions update queue and failure evidence
  according to the selected implementation.
- Resolve partition keys and comparers from protected resources, bound the number
  and lifetime of partition limiters, and prove tenant/user/endpoint isolation.
  Untrusted high-cardinality keys expose allocation and eviction obligations.
- Preserve `RetryAfter` and custom rejection metadata only when present on the
  actual failed lease. Metadata informs a response or retry policy; it does not
  convert rejection into acquisition or prove the client will honor the delay.
- Compose permits with resilience retries and hedges, cancellation, timers,
  disposal, DI lifetimes, HTTP outcomes, fairness, progress, and distributed-state
  assumptions. Nested limiters consume all applicable leases in declared order.
- Emit a schedule with limiter/partition, counts, queue order, replenishments,
  leases, metadata, cancellations, releases, and protected actions. Tests cover
  ignored rejection, leaked and early-disposed leases, queue overflow, newest-first
  drops, zero permits, manual replenishment, chain rollback, partition collision,
  limiter disposal, and per-process versus distributed claims.

Evidence: The official [`RateLimiter.AcquireAsync` contract](https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting.ratelimiter.acquireasync?view=aspnetcore-10.0)
states that awaiting can complete with either an acquired or denied lease and that
cancellation applies to queued requests. Microsoft's
[HTTP rate-limiter example](https://learn.microsoft.com/en-us/dotnet/core/extensions/http-ratelimiter)
checks `IsAcquired`, consumes `RetryAfter` only on rejection, configures queue
order and capacity, and disposes the limiter and each lease.

## P1 - Prove structured logging schemas and redaction

Model `ILogger` events as structured records sent through filters, scopes,
formatters, providers, and exporters. Prove stable event schemas and complete
sensitive-data handling across templates, arguments, exceptions, scopes, and
provider-specific representations rather than treating logging as one generic
taint sink.

Acceptance criteria:

- Parse constant message templates into literal text, placeholder names, order,
  format and alignment, argument identity and type. Diagnose changing templates,
  duplicate or mismatched placeholders, argument-count drift, and schema conflicts
  for the same category and `EventId`.
- Resolve extension-method, `LoggerMessage.Define`, and source-generated
  `[LoggerMessage]` events to one model with category, level, event ID/name,
  template, exception slot, skip-enabled-check behavior, and generated signature.
- Apply effective provider/category filter precedence and `IsEnabled` semantics.
  Disabled output does not retroactively remove argument-evaluation effects, and
  proof of secrecy cannot rely on a mutable production filter unless that
  configuration is part of the policy.
- Track nested scopes and structured state through `ExecutionContext`, async and
  parallel branches, disposal, suppression, and provider support. A leaked scope,
  wrong correlation ID, or secret scope value reaches every enabled provider that
  includes scopes.
- Propagate data classifications and require an effective redactor for every
  sensitive field representation. Include templates, formatted values, destructured
  objects, `ToString`, exceptions and inner exceptions, stack traces, categories,
  event names, scope state, and fallback formatter behavior.
- Model erasing, masking, HMAC, and custom redactors with taxonomy, key/version,
  collision, correlation, and failure policy. HMAC output is pseudonymous evidence,
  not automatic declassification for every privacy policy.
- Treat provider fan-out, buffering, sampling, exporter transport, storage region,
  retention, and failure as capabilities and policy inputs. Control characters and
  multiline values must remain structured or receive provider-appropriate log
  injection encoding without corrupting record boundaries.
- Emit a provider-by-provider witness with event schema, enabled rule, evaluated
  fields, classifications, scopes, redactors, formatted/exported representation,
  and first leak or schema mismatch. Tests cover interpolation, placeholder drift,
  disabled levels, source generation, exception secrets, scope leaks, HMAC rotation,
  multiline injection, provider differences, sampling, and exporter failure.

Evidence: Microsoft's [.NET logging documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview)
defines logs as structured events whose message-template placeholders become named
key-value fields and whose providers and scopes are independently configured.
[CA2254](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2254)
requires stable templates to preserve that structure, while the official
[data-redaction guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction)
describes classification-specific erasing, masking, HMAC, and custom redactors for
sensitive output.

## P1 - Prove JWT validation and claims provenance

Model the complete boundary from untrusted compact token text through JWS or JWE
validation to the resulting `ClaimsPrincipal`. Prove signature, algorithm, key,
issuer, audience, lifetime, type, replay, and claim-mapping policy before any
claim is trusted for authentication or authorization.

Acceptance criteria:

- Distinguish token parsing, decryption, signature verification, semantic
  validation, identity creation, and authorization use. Reading a header or claim
  from `JwtSecurityToken` or `JsonWebToken` never creates a trusted fact by itself.
- Resolve effective `TokenValidationParameters`, bearer-handler options, metadata
  configuration, custom delegates, and defaults for the exact IdentityModel and
  ASP.NET Core package versions. Evidence records every validator that actually ran.
- Require an allowed algorithm and token type, a trusted key suitable for that
  algorithm, successful signature validation, and a key whose issuer or tenant
  relationship is valid. Prevent key-type confusion, untrusted `jku` or `x5u`
  lookup, ambiguous `kid`, and a token-selected verification policy.
- Prove issuer and audience against exact configured sets and normalization rules.
  Audience validation is a forwarding boundary; issuer, tenant, cloud instance,
  and signing-key issuer cannot be conflated because their strings happen to match.
- Model `nbf`, `exp`, issued-at policy, clock source and skew, maximum token age,
  replay cache, nonce, and token ID. Replay protection includes cache scope,
  lifetime, atomic insertion, eviction, and distributed consistency assumptions.
- Track discovery/JWKS retrieval, caching, refresh, rollover, stale last-known-good
  configuration, network failure, and concurrent key updates. A key-refresh retry
  must revalidate the same immutable token under a coherent configuration version.
- Preserve claim provenance through inbound claim-type mapping, name and role
  selection, arrays, duplicates, actor tokens, delegation, transformations, and
  custom events. Only validated claims may satisfy authorization contracts, and a
  transformation cannot silently upgrade untrusted data.
- Emit a redacted witness with token format, selected algorithm and key identity,
  configuration version, validators, time window, issuer/audience/type decisions,
  replay result, and claim derivation. Tests cover unsigned or wrong-algorithm
  tokens, key confusion, stale rollover, audience forwarding, skew boundaries,
  replay races, duplicate claims, actor tokens, custom validators, and token leaks.

Evidence: The official [`TokenValidationParameters` API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters?view=msal-web-dotnet-latest)
exposes separate controls for algorithms, signing keys, issuer, audience, lifetime,
type, replay, clock skew, configuration retrieval, and claim mapping. Its
[`ValidateAudience` contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.validateaudience?view=msal-web-dotnet-latest)
explicitly identifies audience checking as mitigation for token-forwarding attacks.

## P1 - Verify ASP.NET Core middleware and endpoint policy coverage

Build the effective ASP.NET Core request pipeline and endpoint graph, including
branches, maps, groups, routing metadata, filters, and short circuits. Prove that
every protected request reaches the intended authentication, authorization, CORS,
antiforgery, rate, cookie, and error policies in the required order.

Acceptance criteria:

- Resolve `WebApplication` and `IApplicationBuilder` construction, `Use`, `Run`,
  `Map`, `MapWhen`, `UseWhen`, routing, endpoint maps, groups, conventions, filters,
  environment branches, and recognized framework extension methods into ordered
  request and reverse response paths.
- Model middleware invocation as a protocol over `HttpContext`: zero or one calls
  to `next`, state before and after the call, response-started state, exceptions,
  and terminal short circuit. Multiple `next` calls, forgotten awaits, or work
  after a completed response require explicit safe contracts.
- Link routing to the selected endpoint and effective metadata after group and
  convention composition. Prove `UseAuthentication` precedes every consumer of
  `User` and `UseAuthorization` evaluates the endpoint's final policy before its
  handler executes.
- Compose default, fallback, named, combined, and scheme-specific authorization
  policies with `[Authorize]`, `RequireAuthorization`, and `[AllowAnonymous]`.
  Authentication success, challenge, forbid, and anonymous bypass remain distinct
  outcomes; one protected route does not prove sibling routes are protected.
- Verify order and coverage for CORS, response caching, HTTPS/HSTS, static files,
  cookie policy, session, localization, antiforgery, rate limiting, forwarded
  headers, exception handling, and custom policy middleware under the selected
  hosting/runtime profile.
- Identify bypass surfaces including static files, health and metrics endpoints,
  fallback routes, development-only endpoints, generated endpoints, gRPC, hubs,
  preflight requests, and branches that do not rejoin the main pipeline.
- Treat dynamic endpoint data sources, reflection, external startup assemblies,
  runtime mutation, and opaque middleware as signed model boundaries or explicit
  unknowns. Generated route tables and AOT metadata are included in evidence.
- Emit a request witness with host/path/method, branch predicates, middleware
  order, endpoint selection and metadata, identity state, policy decisions, short
  circuit, and response path. Tests cover misplaced auth, anonymous overrides,
  public static files, CORS/cache order, endpoint rate limits before routing,
  branch bypass, double `next`, unawaited `next`, and environment-only exposure.

Evidence: Microsoft's [ASP.NET Core middleware documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0)
states that registration order defines request and reverse response order and is
critical for security. It requires CORS, authentication, and authorization in a
specific order and notes that static-file middleware short-circuits without
authorization checks.

## P1 - Prove distributed tracing context and telemetry hygiene

Model `Activity`, `ActivitySource`, listeners, propagators, and OpenTelemetry
export as a versioned context protocol. Prove parentage, lifecycle, trust, sampling,
and sensitive-data rules across async and process boundaries instead of treating
tracing only as an impure global-state access.

Acceptance criteria:

- Represent trace and span IDs, parent, links, flags, trace state, activity kind,
  start/stop time, status, tags, events, baggage, resource, source name/version,
  current-context slot, and recording/sampling state with stable provenance.
- Model `ActivitySource.StartActivity` returning null when no interested listener
  exists and the listener's propagation-only versus fully recorded sampling
  decisions. Application correctness must not depend on telemetry being sampled.
- Prove balanced start/stop or disposal and restoration of `Activity.Current`
  through sync calls, awaits, tasks, parallel work, execution-context suppression,
  callbacks, and exceptions. Detached work requires an explicit parent or link.
- Parse and emit W3C `traceparent` and `tracestate` plus configured baggage and
  custom propagators. Validate syntax, length, trust boundary, duplicate/conflict
  policy, and injection/extraction carrier; remote context is correlation input,
  never authorization proof.
- Track baggage separately from tags and local state because it propagates to
  downstream services. Apply sensitive-data classification, cardinality and size
  limits, allowlists, and redaction before propagation or export.
- Resolve instrumentation subscriptions, filters, samplers, processors, batching,
  exporters, resource attributes, and shutdown/flush behavior. Sampling, queues,
  backpressure, exporter failure, and process exit can drop telemetry and therefore
  cannot prove a business event occurred.
- Compose trace context with structured logging scopes, HTTP headers, messaging,
  retries and hedges, secrets, execution context, timers, and deterministic
  behavior. Retry attempts and hedges require distinct child identities and links
  without falsely merging concurrent work.
- Emit a paired local/remote witness with carrier fields, extraction, parent/link
  choice, sampling, current-context transitions, tags/baggage classifications,
  processors, and exporter outcome. Tests cover null activities, leaked current
  context, suppressed flow, malformed remote IDs, baggage injection, high
  cardinality, retry spans, sampling drops, exporter backpressure, and shutdown.

Evidence: Microsoft's [.NET distributed-tracing concepts](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts)
define `Activity` parent/child identity, automatic async and HTTP propagation,
W3C trace context, and sampling modes that may create no activity or only enough
state for propagation. The official
[instrumentation guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
also documents that `StartActivity` can return null and that disposal stops an
activity and restores lifecycle state.

## P1 - Add archive extraction and decompression safety proofs

Model ZIP, TAR, and recognized compression formats from entry enumeration through
filesystem materialization. Prove containment, collision and overwrite policy,
link handling, resource budgets, cleanup, and content ownership so hostile archives
cannot escape roots, replace sensitive files, or exhaust memory, disk, or time.

Acceptance criteria:

- Represent archive identity and format, entry order, raw and normalized name,
  entry type, compressed and declared expanded sizes, checksum, mode, timestamps,
  link target, encryption/support state, and content stream lifetime.
- Resolve every entry beneath an explicit extraction root using the filesystem
  identity domain. Reject absolute, drive, UNC, device, parent traversal, alternate
  separator, Unicode/case alias, reserved-name, and normalization collisions before
  creating any path.
- Model symlink, hard-link, junction, sparse, device, FIFO, and metadata entries by
  format and platform. Link creation or later traversal cannot redirect subsequent
  writes outside the root; unsupported special entries are rejected, not flattened
  into ordinary files optimistically.
- Enforce per-entry and total expanded bytes, entry count, path depth and length,
  compression ratio, nested archive depth, memory, disk, CPU/time, and cancellation
  budgets while streaming. Declared lengths are evidence inputs, not trusted proof
  that actual decoding will stay within budget.
- Prove duplicate-entry and overwrite policy, file-versus-directory conflicts,
  case-fold collisions, existing targets, permissions, ownership, timestamps, and
  executable-bit handling. A safe default never overwrites a target selected only
  by hostile archive metadata.
- Use staging and atomic publication when the application claims all-or-nothing
  extraction. Exceptions, checksum failure, cancellation, disk-full, and policy
  rejection clean partial output and dispose every entry/archive stream.
- Compose with partial I/O, pooled buffers, filesystem races, taint, XML/JSON
  parsing, process launching, secret data, and untrusted-workspace isolation.
  Scanning one compressed form does not certify transformed or nested content.
- Emit an entry-by-entry witness with raw name, normalized target, link chain,
  collision set, size counters, decoder progress, writes, and cleanup. Tests cover
  Zip Slip, absolute paths, case collisions, symlink pivots, hard links, duplicate
  names, zip and nested bombs, false sizes, sparse files, overwrite races, corrupt
  checksums, cancellation, and atomic staging rollback.

Evidence: Microsoft's [CA5389 archive-path rule](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca5389)
states that an unsanitized archive entry path can escape the intended extraction
directory and lead to configuration changes or remote code execution. The official
[`ZipArchiveEntry` API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry?view=net-10.0)
exposes entry names, compressed length, expanded length, and streams that supply
the inputs for containment and bounded-decompression proofs.

## P1 - Model HTTP headers, cookies, framing, and streaming lifetimes

Add protocol-aware semantics for `HttpRequestMessage`, `HttpResponseMessage`,
headers, content, cookies, and handler pipelines across HTTP versions. Prove
message framing, credential scope, body completion, replayability, and disposal
instead of reducing requests to a URI plus one eventual status code.

Acceptance criteria:

- Track request and response identity through created, headers mutable, sending,
  headers received, body streaming or buffered, complete, upgraded/tunneled, and
  disposed states. A request instance is sent at most once unless an explicit clone
  creates a new message and content ownership.
- Parse typed and raw headers with case-insensitive names, list-combination rules,
  hop-by-hop versus end-to-end scope, forbidden/control characters, trailers,
  pseudo-headers, protocol version and policy, and intermediary rewrites.
  `TryAddWithoutValidation` never proves wire validity or downstream agreement.
- Implement HTTP/1.1 body-length precedence for method/status, `Content-Length`,
  transfer coding, chunking, connection close, upgrades, and tunnels. Conflicting,
  duplicate, malformed, or differently normalized framing yields rejection or
  explicit proxy-chain uncertainty, never a smuggling-safe proof.
- Model cookie selection and mutation by host/domain, path, secure transport,
  `HttpOnly`, `SameSite`, expiry, public-suffix policy, redirects, and container
  identity. Handler pooling or factory reuse cannot share a `CookieContainer`
  across isolation domains without explicit permission.
- Track content encoding, media type, charset, decompression, multipart boundaries,
  length limits, buffering, stream position, and cancellation. Decoded and
  decompressed content consumes separate quotas and feeds archive/parser domains.
- Distinguish `ResponseContentRead` from `ResponseHeadersRead`: headers-only
  completion does not read the body, extend the client timeout to later reads, or
  enforce the configured response-buffer bound. Prove separate body deadlines,
  drain/cancel policy, and response/content disposal.
- Compose with redirects, TLS/DNS identity, authentication headers, proxies,
  resilience retries and hedges, rate limits, tracing propagation, partial I/O,
  pooled connections, secrets, and ownership. Retrying streaming content requires
  a new replayable body and must not duplicate an in-flight send.
- Emit a wire-oriented witness with logical message, handler chain, HTTP version,
  normalized headers, framing decision, cookies/credentials, body progress,
  redirects/retries, and disposal/connection outcome. Tests cover CL/TE conflicts,
  duplicate lengths, response splitting, trailer misuse, cookie leakage, header
  injection, headers-only timeout gaps, decompression bombs, body replay, early
  disposal, proxy rewriting, upgrade, and pooled-connection reuse.

Evidence: [RFC 9112](https://www.rfc-editor.org/rfc/rfc9112.html)
defines HTTP/1.1 message-length precedence and warns that conflicting
`Transfer-Encoding` and `Content-Length` interpretations enable request smuggling.
The official [`HttpCompletionOption` contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
states that `ResponseHeadersRead` leaves content unread and excludes subsequent
body reads from both the client timeout and automatic buffer-size enforcement.
Microsoft's [`HttpClient` guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
also warn that pooled handler cookie containers can leak cookies between unrelated
parts of an application.

## P1 - Prove ASP.NET Core request binding and validation boundaries

Model the effective ASP.NET Core model-binding, input-formatter, and validation
pipeline for each endpoint. Prove which untrusted request values can reach each
parameter and writable member, which validators actually run, and whether invalid
or overposted state is rejected before business or persistence effects occur.

Acceptance criteria:

- Resolve controller actions, Razor handlers, minimal API parameters, binding
  sources, metadata, custom binders, input formatters, value-provider order, API
  behavior options, filters, and endpoint conventions for the selected framework
  version into one effective per-parameter binding plan.
- Track provenance separately for route, query, header, form, file, body, service,
  and custom sources. Ambiguous names, duplicate values, culture-dependent parsing,
  collection indexes, prefixes, and fallback between sources remain visible in
  evidence rather than collapsing into one trusted value.
- Distinguish model binding from request-body formatting. Attributes such as
  `Bind`, `BindRequired`, and `BindNever` do not constrain JSON or XML input
  formatters unless an independent DTO or formatter contract establishes the same
  writable-member boundary.
- Prove an allowlisted input shape for security-sensitive create and update paths.
  Detect entity binding, nested writable graphs, constructor and init members,
  mass assignment, default-value replacement, and patch operations that can alter
  fields the caller is not authorized to control.
- Model conversion failure, nullability, required members, data annotations,
  `IValidatableObject`, custom validators, automatic 400 responses, `ModelState`,
  `ValidateNever`, manual revalidation, and validation-depth/error limits. A value
  is not valid merely because a CLR object was constructed.
- Track body stream consumption, content type and formatter selection, request and
  form size limits, buffering, cancellation through `RequestAborted`, and uploaded
  file ownership. A body consumed by one formatter is not available to a second
  binder unless explicit rewind semantics are proven.
- Compose bound-value provenance with authorization, taint, JSON/XML semantics,
  HTTP limits, database updates, file handling, logging, and error responses.
  Client-side validation and generated OpenAPI schemas are documentation inputs,
  not proof that the server enforced the same policy.
- Emit an endpoint witness with request source, selected binder or formatter,
  member writes, conversion outcomes, validators, model-state errors, rejection
  path, and first side effect. Tests cover overposting, DTO/entity drift,
  form-versus-JSON attribute differences, duplicate keys, culture parsing, nested
  graphs, patch documents, custom binders, invalid model use, oversized bodies,
  files, cancellation, and one-shot body reads.

Evidence: Microsoft's [ASP.NET Core model-binding documentation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-10.0)
states that input formatters own request-body reading, that a consumed body is not
available for another body parameter, and that form-binding attributes do not
affect JSON or XML input formatters. It recommends view models over `Bind` for
overposting protection.

## P1 - Prove ASP.NET Core Data Protection isolation and key continuity

Add protocol semantics for `IDataProtectionProvider`, `IDataProtector`, time-limited
protectors, application discriminators, purpose chains, and key rings. Prove that
protected state is isolated to its intended consumer and remains readable only for
the required deployment lifetime without exposing or silently losing key material.

Acceptance criteria:

- Represent provider and protector identity, ordered purpose chain, application
  discriminator, payload format/version, creation and expiration time, key ID,
  algorithm profile, key-ring repository, protection-at-rest mechanism, and
  `Protect` or `Unprotect` outcome with provenance.
- Treat purpose strings as security boundaries. Require stable, unique, ordered,
  versioned component purposes; untrusted input cannot be the sole purpose prefix,
  and two protectors are interchangeable only when provider identity and the full
  ordinal purpose chain are equivalent.
- Resolve effective Data Protection registration and defaults across service
  configuration, hosting model, content root, `SetApplicationName`, package/runtime
  version, persistence provider, key encryption, lifetime, automatic generation,
  revocation, and read-only key-ring settings.
- Prove intended sharing and separation across applications, tenants, deployment
  slots, containers, replicas, operating-system accounts, and environments. A
  shared repository or discriminator is rejected when its access scope is broader
  than the payload's trust domain.
- Model key creation, activation delay, propagation, expiration, rotation,
  revocation, deletion, repository unavailability, concurrent writers, and stale
  replicas. Retired keys may still be needed to read live payloads; deleting or
  losing them creates an explicit permanent-unreadability outcome.
- Verify key confidentiality and integrity at rest, repository read/write
  permissions, external wrapping-key identity and version policy, backup and
  restore, disaster recovery, and compromise assumptions. Encrypting key files
  does not protect against an attacker who can write a malicious key ring.
- Distinguish authenticity/integrity from payload lifetime and confidentiality.
  Compose Data Protection with cookies, antiforgery, authentication, secrets,
  filesystem and cache identity, clocks, configuration reload, and rolling deploys;
  it is not automatically suitable for indefinite archival encryption.
- Emit a redacted protect/unprotect witness with application and purpose identity,
  selected key and state, repository version, wrapping policy, payload age, and
  failure reason. Tests cover purpose collisions, attacker-controlled purposes,
  app-name drift, slot swaps, ephemeral containers, missing old keys, early
  deletion, rotation races, revoked keys, clock boundaries, repository compromise,
  multi-replica propagation, and time-limited payloads.

Evidence: Microsoft's [purpose-string contract](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/purpose-strings?view=aspnetcore-10.0)
defines the ordered purpose chain as the isolation boundary between cryptographic
consumers and warns against deriving it solely from untrusted input. The official
[Data Protection configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
documents application discriminators, shared repositories, key lifetime and
rotation, external key wrapping, and the limits of repository isolation.

## P1 - Model EF Core tracking, concurrency, and set-based mutations

Add an EF Core profile above the database command domain that models `DbContext`
unit-of-work identity, entity states, snapshots, relationship fixup, query tracking,
and generated persistence operations. Prove which in-memory changes will be saved
and that concurrency or bulk-update paths cannot silently lose or overwrite data.

Acceptance criteria:

- Represent context identity and lifetime, model/provider/version, entity key and
  instance identity, `Detached`, `Unchanged`, `Added`, `Modified`, and `Deleted`
  states, original/current/store values, shadow properties, modified-property set,
  concurrency tokens, and pending database operations.
- Resolve entity tracking from queries, `Add`, `Attach`, `Update`, graph traversal,
  navigation fixup, change detection, proxies or notifications, `AsNoTracking`,
  identity resolution, `ChangeTracker.Clear`, detachment, and context disposal.
- Prove that disconnected graphs and DTO updates affect only intended entities and
  properties. Detect default or temporary keys, duplicate tracked instances,
  accidental inserts, graph-wide `Modified` state, missing shadow values, and
  tenant or ownership changes introduced by attachment or relationship fixup.
- Model query translation versus client evaluation and the materialization boundary
  for supported LINQ shapes. Database collation, null, precision, ordering, and
  provider behavior remain profile-specific; an in-memory predicate is not assumed
  equivalent to translated SQL without evidence.
- For `SaveChanges`, derive inserts, updates and deletes from the tracked state,
  generated values, cascading actions, interceptors, transactions, savepoints,
  batching, cancellation, exceptions, `AcceptAllChanges`, and context state after
  success, rollback, ambiguous failure, or retry.
- Prove optimistic concurrency by carrying the original token into the update or
  delete predicate and handling `DbUpdateConcurrencyException` with an explicit
  client-wins, store-wins, or merge policy. Refreshing originals and retrying must
  not discard an unrelated concurrent change.
- Model `ExecuteUpdate` and `ExecuteDelete` as immediate set-based operations that
  bypass the change tracker and automatic concurrency control. Detect stale tracked
  instances, mixed bulk/tracked writes, missing tenant predicates, and row-count
  checks that are required to establish the intended update set.
- Emit a unit-of-work witness with context and entity identities, state transitions,
  translated command shapes, tokens, affected rows, transaction outcome, retry and
  final tracked state. Tests cover detached graphs, duplicate instances, no-tracking
  edits, relationship fixup, shadow properties, stale snapshots, concurrency merge,
  bulk-update bypass, provider divergence, cancellation, ambiguous commits, and
  context reuse after failure.

Evidence: Microsoft's [EF Core change-tracking documentation](https://learn.microsoft.com/en-us/ef/core/change-tracking/)
defines tracked entity states, original values, relationship fixup, and the
short-lived `DbContext` unit of work. Its [concurrency guidance](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
describes original concurrency tokens in update predicates and explicit conflict
resolution, while the [`ExecuteUpdate` and `ExecuteDelete` contract](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
states that set-based operations bypass both the change tracker and automatic
concurrency control.

## P1 - Model gRPC calls, streams, deadlines, and status protocols

Add protocol-aware semantics for ASP.NET Core gRPC clients and services, generated
stubs, call options, metadata, unary and streaming calls, deadlines, cancellation,
status, and disposal. Prove that every call completes, aborts, or is disposed under
the intended budget without losing messages or confusing transport success with an
application result.

Acceptance criteria:

- Represent channel and call identity, method descriptor and cardinality, request
  and response message sequence, headers, trailers, status and detail, deadline,
  cancellation source, compression and size limits, retry attempt, connectivity,
  and call/stream lifecycle state.
- Resolve generated client and server method shapes plus `CallOptions`, interceptors,
  client-factory configuration, credentials, load balancing, retry and hedging
  policy, service options, and runtime defaults for the selected gRPC and ASP.NET
  Core versions.
- Model unary, client-streaming, server-streaming, and duplex-streaming protocols.
  Enforce single-writer rules, ordered writes per stream, completion of the request
  stream, exclusive response enumeration, half-close, terminal status/trailers,
  and disposal or cancellation on every exit path.
- Prove finite deadlines where required and propagate the minimum effective
  deadline and cancellation through nested calls. A deadline is an absolute UTC
  instant, has no default, spans configured retries, and can expire independently
  on the client and server near the response boundary.
- Require services to observe `ServerCallContext.CancellationToken` and pass it to
  cancellable child work when prompt release is claimed. Client cancellation or
  call disposal does not prove remote side effects were rolled back.
- Model protobuf presence, default values, oneofs, unknown fields, schema/version
  compatibility, message and metadata size limits, compression expansion, and
  serialization failure. A default-valued scalar is not evidence that the sender
  explicitly supplied it unless presence semantics say so.
- Compose gRPC with HTTP/2 or HTTP/3 transport, TLS identity, authentication
  metadata, tracing, resilience, rate limits, partial I/O, task protocols, and
  resource ownership. Retries and hedges are allowed only for safe or explicitly
  idempotent operations and must account for attempts that reached the server.
- Emit a call witness with method/cardinality, metadata provenance, message events,
  deadlines and cancellation, attempts, transport outcome, final status/trailers,
  and disposal. Tests cover missing deadlines, expired calls, nested propagation,
  ignored server cancellation, concurrent writes, uncompleted request streams,
  abandoned response streams, partial duplex failure, status/trailer handling,
  schema drift, oversized messages, retries, hedges, and ambiguous side effects.

Evidence: Microsoft's [gRPC deadline and cancellation guidance](https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation?view=aspnetcore-10.0)
states that calls have no default deadline, deadlines cover all retries, nested
calls should propagate the smallest deadline and cancellation, and client disposal
can cancel streaming work. The official [.NET gRPC client documentation](https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0)
defines the distinct unary and streaming call shapes and recommends deadlines to
bound resource use.

## P1 - Prove broker delivery, settlement, and idempotent processing

Add provider-profiled semantics for durable queues, topics, subscriptions, message
locks or leases, acknowledgment and negative acknowledgment, delivery attempts,
dead-lettering, scheduling, sessions, and transactions. Prove the application's
claimed at-most-once or at-least-once outcome and make duplicate or loss windows
explicit around every side effect.

Acceptance criteria:

- Represent broker/entity and message identity, body and properties, partition or
  session key, sequence/order, enqueue and scheduled time, expiry, delivery count,
  receiver mode, lock owner and expiry, settlement state, dead-letter reason,
  transaction, and producer/consumer lifecycle.
- Resolve client, processor, entity, subscription, retry, prefetch, concurrency,
  auto-complete, lock-renewal, deduplication, TTL, maximum-delivery, and dead-letter
  configuration for signed provider and SDK profiles. Similar API names do not
  imply identical delivery guarantees across brokers.
- Distinguish send acceptance, durable enqueue, transfer, receive, processing,
  settlement request and confirmed settlement. An asynchronous send or `Complete`
  call that has not succeeded cannot prove that the broker durably owns or removed
  the message.
- Model receive-and-delete as an at-most-once loss window and lock/peek receive as
  an at-least-once duplicate window. Locks can expire or be lost during processing,
  renewal and settlement can fail, and another consumer may receive the same
  message after the first consumer performed an external side effect.
- Require an idempotency strategy for duplicate-sensitive processing: stable
  message or business key, atomic inbox/dedup record, conditional state transition,
  commutative operation, or transactional coupling supported by the provider.
  In-memory flags and broker delivery count alone do not prove exactly-once effects.
- Prove settlement and failure policy for success, transient error, permanent
  poison message, cancellation, shutdown, handler crash, and partial side effect.
  Bound abandon/defer/retry loops and account for TTL, maximum deliveries,
  dead-letter and transfer-dead-letter destinations plus replay governance.
- Model ordering, sessions, partitions, competing consumers, prefetch buffers,
  backpressure, scale-out, batching, transactions, scheduled messages, forwarding,
  and outbox publication. Ordering within one scope does not establish a global
  order, and database commit plus message send needs an explicit atomicity pattern.
- Emit a message-lifecycle witness with send confirmation, broker path, deliveries,
  lock windows and renewals, handler effects, idempotency decision, settlement
  confirmation, retry and terminal queue. Tests cover crash-before/after effect,
  lost locks, failed settlement, duplicate delivery, receive-and-delete loss,
  poison loops, TTL, dead-letter replay, competing consumers, session loss,
  prefetch shutdown, outbox gaps, batch partial failure, and ambiguous send results.

Evidence: Microsoft's [Azure Service Bus transfer and settlement documentation](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement)
distinguishes receive-and-delete from peek-lock, documents explicit settlement,
redelivery, volatile lock loss, renewal, maximum delivery count and dead-lettering.
Its [message loss and duplicate guidance](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-message-loss-and-duplicates)
identifies the resulting at-most-once loss and at-least-once duplicate windows and
the need for idempotent processing.

## P1 - Prove OAuth 2.0 and OpenID Connect interactive flows

Model ASP.NET Core remote-authentication handlers from challenge through browser
redirect, callback, token exchange, identity creation, refresh, and sign-out. Prove
that every authorization response belongs to the initiating browser transaction,
issuer and client and that only validated tokens and intended claims reach a local
session or downstream API.

Acceptance criteria:

- Resolve effective authentication schemes, handler options, authority metadata,
  client identity and authentication, callback paths, redirect URIs, response type
  and mode, scopes, claim actions, token-validation policy, event handlers, cookies,
  backchannel, PAR behavior, and package/runtime defaults.
- Represent an authorization transaction with browser session, scheme, issuer,
  client, redirect URI, correlation cookie, `state`, nonce, authorization code,
  PKCE verifier and challenge, response parameters, token endpoint, tokens, claims,
  ticket and terminal success, denial, error, timeout, cancellation, or replay.
- Prove exact redirect-URI and issuer binding and reject open redirects, mix-up,
  code injection, duplicate or conflicting parameters, unsolicited callbacks, and
  callbacks accepted by the wrong scheme, tenant, browser, tab, or deployment.
- Require a fresh high-entropy correlation value and nonce with correct cookie
  path, domain, secure and SameSite behavior. Validate and consume them once before
  creating a session; parallel login attempts cannot overwrite or cross-bind state.
- Prove authorization-code flow with `S256` PKCE where required. Bind the code to
  the initiating client, redirect URI and verifier, protect client credentials,
  and distinguish front-channel authorization data from authenticated backchannel
  token and user-info responses.
- Validate ID tokens by signature, issuer, audience/client, lifetime, nonce, type,
  authorized party and relevant hash claims. Access and refresh tokens remain
  opaque credentials unless their own validation contract succeeds; an ID token
  is not authorization for an API.
- Track scope and resource consent, token storage, refresh rotation and reuse,
  revocation, sign-out callbacks, session lifetime, claims mapping, incremental
  consent and downstream delegation. Redact codes, verifiers, secrets and tokens
  from logs, URLs, witnesses and exception messages.
- Emit a paired browser/backchannel witness with schemes, redirects, correlation,
  issuer metadata version, PKCE and nonce checks, token endpoints, validated claims,
  session creation and cleanup. Tests cover login CSRF, state fixation, nonce/code
  replay, parallel tabs, mix-up, redirect confusion, missing PKCE, PAR fallback,
  token substitution, refresh reuse, event overrides, logout CSRF and secret leaks.

Evidence: Microsoft's [ASP.NET Core OpenID Connect guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
recommends confidential authorization-code flow with PKCE and documents versioned
PAR behavior. [RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html) defines the
current OAuth 2.0 security best practices for redirect, code injection, PKCE,
sender constraints, token replay and mix-up defenses.

## P1 - Model ASP.NET Core Identity account and credential lifecycles

Add first-class semantics for `UserManager`, `SignInManager`, Identity stores,
password hashers, token providers, security stamps, lockout, confirmation, MFA,
external logins and application sessions. Prove account transitions atomically so
credential recovery, factor changes, or stale sessions cannot bypass policy.

Acceptance criteria:

- Resolve effective Identity registration, user and role types, stores, normalizer,
  validators, password hasher, token providers, sign-in, user, password, lockout,
  cookie, security-stamp and custom option defaults for exact package versions.
- Represent user/store identity, normalized identifiers, password-hash format and
  cost, email and phone confirmation, failed-access count, lockout end, security and
  concurrency stamps, roles, claims, logins, authenticators, recovery codes, tokens,
  active sessions and account enabled/deleted state.
- Prove registration and identifier changes under normalized uniqueness and
  concurrency. Confirmation, invite, email-change and phone-change tokens bind to
  the intended user, purpose, destination, security stamp and lifetime and are
  consumed or invalidated according to an explicit replay policy.
- Keep plaintext passwords and factor secrets ephemeral and secret-tainted. Verify
  versioned password hashes with configured work factors, constant-time comparison
  assumptions and rehash-on-success behavior; never log, persist, return or reuse a
  plaintext credential or mistake password-complexity policy for hash strength.
- Model sign-in outcomes separately: success, not allowed, locked out, requires
  two-factor, invalid credential and storage error. Failed-attempt updates and
  lockout thresholds are atomic across concurrent requests and error responses do
  not reveal whether an account, factor or external login exists.
- Prove authenticator enrollment and reset, TOTP validation windows, trusted-device
  cookies, backup and one-time recovery codes, factor removal and MFA-required
  policy. Recovery-code redemption and factor changes use atomic state transitions
  and trigger the required security-stamp or session invalidation.
- Track password reset/change, external-login link and unlink, role/claim changes,
  disable/delete and sign-out-everywhere through security-stamp regeneration and
  periodic validation. The configured validation interval creates a measurable
  stale-session window rather than immediate revocation proof.
- Emit a redacted account-state witness with operation, store version, validators,
  stamp transitions, credential/factor class, attempts, tokens, session effects and
  outcome. Tests cover normalized collisions, registration races, hash migration,
  lockout races, enumeration, token replay, stale confirmations, MFA bypass,
  recovery-code races, external-login takeover, stamp intervals and deleted users.

Evidence: Microsoft's [ASP.NET Core Identity configuration reference](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0)
documents password-hash version and cost, lockout, sign-in, token-provider and
security-stamp behavior. Its [Identity API guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)
explains MFA and the bounded delay between a security-sensitive account change and
cookie or token session invalidation.

## P1 - Prove antiforgery token binding and endpoint coverage

Add an ASP.NET Core antiforgery domain that determines which browser-authenticated
state-changing endpoints require protection and proves the cookie/request token
pair, user binding, generation, transport and validation before effects. Middleware
presence alone is not evidence that every susceptible endpoint validates a token.

Acceptance criteria:

- Classify endpoint authentication by whether a browser automatically attaches
  cookies, Basic or other credentials cross-site. Derive the methods and operations
  that can change state; a nominal GET or custom method with effects is not made
  safe merely by framework conventions.
- Resolve `IAntiforgery`, MVC and Razor filters, minimal API metadata, middleware
  order, form tag helpers, header and field names, cookie options, additional-data
  providers, global policies, `IgnoreAntiforgeryToken` overrides and runtime
  defaults into an effective per-endpoint requirement.
- Represent the antiforgery cookie token and request token as a related pair with
  application, browser, authenticated-user, purpose, additional data, issue time,
  transport and validation outcome. Possessing only the cookie does not satisfy the
  request-token obligation.
- Prove token generation and delivery for rendered forms, AJAX clients, SPAs and
  file or multipart requests. Model form field versus header selection, duplicate
  values, content-type parsing, body-read ownership and error behavior without
  exposing a reusable token to cross-origin scripts, URLs or logs.
- Bind tokens to the current authenticated identity and refresh or invalidate them
  across anonymous-to-authenticated transitions, sign-out, user change, session
  renewal, Data Protection key-ring changes and multi-replica deployments.
- Treat SameSite, Origin/Referer checks and CORS as defense-in-depth with explicit
  browser and proxy assumptions, not universal substitutes for antiforgery tokens.
  `SameSite=None` requires secure transport and cross-site login flows may need
  narrowly scoped exceptions.
- Require validation before the first business, database, file, messaging or
  external effect. Invalid, missing, expired, mismatched or malformed tokens follow
  a bounded rejection path and never fall through because feature metadata was
  inspected but its validation result ignored.
- Emit a request witness with credential attachment, origin context, endpoint
  requirement, token pair and sources, identity/additional-data binding, validation
  feature, overrides and first effect. Tests cover missing coverage, ignored global
  policy, stolen cookie only, login transition, parallel users, AJAX headers,
  multipart bodies, duplicate tokens, SameSite exceptions, farms and key rotation.

Evidence: Microsoft's [ASP.NET Core antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
defines the cookie/request token pair, authenticated-user binding, MVC and minimal
API validation rules, overrides, form and header transports and Data Protection
dependency. Its [SameSite guidance](https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0)
documents component-specific cookie defaults and cross-site authentication cases.

## P1 - Model SignalR hub connections, streaming, and reconnects

Add protocol semantics for ASP.NET Core SignalR negotiation, transports, hub
connections, invocations, client results, streaming, users, groups, keep-alives and
reconnect. Prove authorization, ordering, completion and bounded buffering across
temporary disconnects without treating a logical connection as a durable session.

Acceptance criteria:

- Represent endpoint, negotiated protocol/version and transport, connection and
  user identity, authentication expiry, hub invocation ID, method and arguments,
  result or error, stream item sequence, cancellation, group membership, buffers,
  acknowledgments, timers and connection state.
- Resolve global and per-hub options, endpoint dispatcher options, JSON or
  MessagePack protocol settings, filters, authorization metadata, client options,
  transport restrictions, scale-out provider and version defaults into one
  effective connection and invocation profile.
- Model negotiate, handshake, connected, reconnecting, reconnected, closing and
  disconnected transitions for WebSockets, Server-Sent Events and long polling.
  Transport fallback or a new connection ID does not preserve server state unless
  the selected reconnect protocol and application logic explicitly reconstruct it.
- Treat hub instances as transient invocation scopes. Track `Context`, caller,
  clients, groups, abort token, DI scope and outstanding sends or client results;
  no application invariant may depend on hub instance fields surviving calls.
- Prove per-invocation argument binding, authorization and terminal result, error,
  cancellation or disconnect. Detailed errors and message-content logging obey
  secret and privacy policy, and a send completion means the framework accepted
  the send rather than that a remote user acted on it.
- Model upload and download streams, single producer/consumer rules, item order,
  completion, cancellation, `ChannelReader` or async-enumerable ownership, stream
  buffer capacity, transport buffers, message-size limits and backpressure. Every
  exit path completes or cancels the stream and releases its producer.
- Track user and group operations as connection-scoped routing state. Reconnect,
  scale-out, membership restoration and concurrent add/remove are provider-profiled;
  groups are not authorization stores and membership is rechecked before sensitive
  delivery when required.
- Model automatic and stateful reconnect separately. Account for retry schedule,
  buffered bytes, ACK and replay windows, duplicate handling, sequence gaps,
  authentication renewal and `CloseOnAuthenticationExpiration`; replayed messages
  cannot repeat a non-idempotent client or server effect silently.
- Emit a connection timeline witness with negotiation, transport, identity,
  invocation/stream events, buffers, timeouts, reconnect attempts, ACKs, group state,
  scale-out path and terminal cleanup. Tests cover handshake failure, transport
  fallback, stale auth, transient hub fields, concurrent invocations, abandoned
  streams, backpressure, oversized messages, group races, replay duplicates,
  reconnect gaps, node loss, detailed-error leaks and shutdown.

Evidence: Microsoft's [SignalR configuration reference](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-10.0)
defines handshake, keep-alive and client timeouts, transports, message and buffer
limits, authentication expiry and ACK-based stateful reconnect. The official
[streaming guidance](https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming?view=aspnetcore-10.0)
defines client-to-server and server-to-client stream shapes and cancellation.

## P1 - Prove Generic Host and background-service lifecycle safety

Model .NET Generic Host startup, readiness, application lifetime signals, hosted
services, `BackgroundService`, graceful drain, shutdown budgets and process exit.
Prove that services become ready in the required order and that accepted work is
completed, transferred, persisted or explicitly abandoned before dependencies and
resources are disposed.

Acceptance criteria:

- Represent host and service identity plus created, starting, started, ready,
  stopping, stopped, failed and disposed states; startup order, lifetime tokens,
  service task, exception, shutdown deadline, outstanding work and process outcome.
- Resolve host builder and lifetime, `HostOptions`, `AddHostedService` order,
  `IHostedLifecycleService`, `IHostedService`, `BackgroundService`, health/readiness
  publication, service manager integration, framework version and exception-policy
  defaults into an effective lifecycle graph.
- Prove every `StartAsync` is bounded and awaited before dependent readiness.
  Account for sequential or configured concurrent startup, partial startup failure,
  rollback and disposal; a listener or health endpoint cannot advertise ready until
  prerequisites and required background loops are live.
- Treat `ExecuteAsync` as the background service's lifetime task. It must become
  asynchronous without blocking startup, observe the stopping token, surface or
  intentionally classify exceptions and have version-specific host-stop and exit
  behavior rather than becoming an unobserved detached task.
- Prove scoped work creates and disposes an explicit DI scope per valid unit because
  hosted services are singleton registrations. Prevent captured request scopes,
  concurrent reuse of non-thread-safe scoped services and disposal while work still
  references a scope.
- Model `ApplicationStarted`, `ApplicationStopping`, `ApplicationStopped`, external
  signals, explicit `StopApplication`, `RunAsync`, `StopAsync`, cancellation and
  repeated stop requests. Registration timing and callback exceptions cannot skip
  required state transitions silently.
- On shutdown, stop ingress, mark unready, propagate cancellation, drain or safely
  checkpoint queues and in-flight work, stop producers before consumers, flush
  bounded telemetry/output and dispose resources within the configured budget.
  A timeout or forced process termination is an explicit incomplete-cleanup outcome.
- Do not assume `StopAsync` runs after crashes, fail-fast, power loss or forced
  termination. Durable correctness uses transactional/checkpoint recovery and
  idempotency rather than relying on final callbacks; process exit codes preserve
  background-service failure under the selected runtime behavior.
- Emit a lifecycle witness with service ordering, readiness changes, task and scope
  ownership, work counts, signals, cancellation, deadlines, exceptions, drain and
  exit. Tests cover slow startup, dependency order, partial failure, blocking
  `ExecuteAsync`, ignored cancellation, scope capture, background exceptions,
  shutdown timeout, work accepted during drain, duplicate stop, forced death and
  restart recovery.

Evidence: Microsoft's [.NET Generic Host documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host)
defines host responsibility for startup, shutdown and hosted-service lifetime. Its
[hosted-service guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
documents ordered startup, the `BackgroundService` lifetime task, explicit scopes,
graceful-stop cancellation and the fact that `StopAsync` may not run after an
unexpected process failure.

## P1 - Prove reverse-proxy and forwarded-header trust

Model the physical transport peer and each logical request identity reconstructed
by ASP.NET Core behind proxies, load balancers, ingress controllers and TLS
terminators. Prove that forwarded client address, scheme, host and path data comes
only from the declared hop topology before security or routing decisions consume it.

Acceptance criteria:

- Represent the transport peer, ordered proxy hops, raw `Forwarded` and
  `X-Forwarded-*` fields, consumed and original values, effective remote address,
  scheme, host, port, path base, client certificate and a provenance label for every
  reconstructed request property.
- Resolve hosting integration, environment switches, middleware registration and
  order, `ForwardedHeadersOptions`, header names, known proxies and networks,
  forward limit, symmetry, allowed hosts and exact runtime/servicing defaults into
  one deployment-specific trust policy.
- Match the immediate peer before trusting its headers, then walk hop values from
  the trusted side in the framework-defined direction. Handle IPv4-mapped IPv6,
  CIDR boundaries, ports, dual-mode sockets and proxy address translation without
  widening an allowlist through textual equivalence guesses.
- Reject or retain as untrusted values beyond the permitted hop count, from an
  unknown peer, with asymmetric list lengths, invalid syntax, empty fields,
  duplicate/conflicting header families or topology disagreement. Clearing trusted
  proxy lists is an explicit trust-all boundary, never a harmless compatibility fix.
- Validate forwarded hosts with exact canonicalization, Punycode and wildcard
  semantics and bind public host, scheme, port and prefix coherently. Prevent host
  spoofing in redirects, absolute links, password-reset URLs, OAuth callbacks,
  cookies, tenant selection and cache keys.
- Prove middleware runs before HTTPS redirect/HSTS, authentication, link generation,
  routing, rate limiting, geolocation, audit and any IP/scheme/host policy. Later
  middleware can inspect both original and effective values but cannot silently
  reinterpret untrusted raw headers.
- Profile IIS integration, YARP and recognized cloud ingress configurations plus
  servicing changes that harden unknown-proxy handling. Configuration drift that
  causes redirect loops or authentication failure is distinguished from an unsafe
  fallback that accepts spoofed headers.
- Emit a hop-by-hop witness with socket peer, raw lists, trust match, consumption
  direction and count, effective request properties, middleware position and each
  security consumer. Tests cover direct-client spoofing, extra hops, asymmetric
  lists, mapped addresses, wildcard hosts, Unicode hosts, path-base confusion,
  competing headers, TLS termination, runtime upgrade and proxy rotation.

Evidence: Microsoft's [proxy and load-balancer guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
documents right-to-left processing, known proxy/network checks, forward limits,
header symmetry and host restrictions. The official [servicing change](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies?view=aspnetcore-10.0)
shows that ASP.NET Core 8.0.17 and 9.0.6 began ignoring every forwarded field from
unknown proxies as a security hardening measure.

## P1 - Model cookie-authentication tickets and session renewal

Add ASP.NET Core cookie-handler semantics from sign-in ticket creation through Data
Protection, browser storage, request authentication, principal validation, sliding
renewal, server-side ticket storage and sign-out. Prove session freshness, scope and
revocation instead of treating possession of any decryptable cookie as current
authorization.

Acceptance criteria:

- Resolve authentication scheme selection, `CookieAuthenticationOptions`, events,
  ticket format, Data Protection purpose, cookie manager, `ITicketStore`, clock,
  session and Identity integration plus runtime defaults into one effective handler
  profile per request.
- Represent ticket identity, scheme, principal and claim provenance,
  `AuthenticationProperties`, issued/expiry times, persistence, refresh permission,
  redirect state, protection key, browser cookie attributes, server-store key and
  created, active, stale, renewed, rejected, signed-out and expired states.
- Prove sign-in is performed under the intended scheme and commits headers before
  the response starts. Prevent session fixation by issuing a fresh protected ticket
  after authentication or privilege change and never carry attacker-chosen session
  identifiers into the authenticated state.
- Distinguish browser cookie lifetime, session-cookie lifetime and protected ticket
  lifetime. Model `IsPersistent`, `ExpiresUtc`, absolute expiry, sliding expiration,
  renewal thresholds, `AllowRefresh`, clock skew and concurrent renewals; sliding
  renewal cannot extend beyond an explicit absolute-session bound where required.
- Validate cookie name, domain, path, secure, `HttpOnly`, SameSite, partitioning,
  chunking and size plus host and forwarded-scheme context. Cookie scope must not
  cross applications or tenants merely because they share a parent domain or key
  ring.
- Model unprotect, ticket deserialization, scheme/purpose/version binding and
  `ValidatePrincipal` or security-stamp checks. Rejection deletes or invalidates the
  session, while replacement principals preserve only re-derived trusted claims and
  explicitly request renewal when needed.
- For `ITicketStore`, prove atomic store, retrieve, renew and remove, key entropy,
  expiry, distributed consistency and cleanup. Client cookie deletion alone does not
  revoke another copy, and a server-side record does not disappear until removal is
  confirmed or its enforced lifetime ends.
- Emit a redacted session timeline with ticket/cookie/store identities, claim
  version, key and scheme, clocks, validation, renewal race, rejection and browser
  response. Tests cover fixation, wrong scheme/purpose, stale claims, revoked users,
  absolute versus sliding expiry, clock boundaries, concurrent renewals, oversized
  chunked cookies, domain leakage, store loss, replay, response-started and sign-out.

Evidence: Microsoft's [cookie-authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0)
documents protected authentication tickets, persistent and absolute expiry,
sliding renewal and `ValidatePrincipal` for revocation. The official
[`ITicketStore` contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0)
defines the optional server-side lifecycle for storing, retrieving, renewing and
removing ticket identities.

## P1 - Prove authorization-handler and resource semantics

Add an ASP.NET Core policy-evaluation domain below endpoint authorization that
models requirements, handlers, claims, resources and explicit success or failure.
Prove the effective AND/OR composition without depending on handler order, missing
authentication or side effects that happen to run during authorization.

Acceptance criteria:

- Resolve default, fallback, named and dynamically supplied policies, authentication
  schemes, combined requirements, handler registrations, custom policy providers,
  authorization middleware result handlers and framework options for the exact
  application and runtime version.
- Represent `AuthorizationHandlerContext` with principal and authenticated identity
  provenance, resource identity/type, all and pending requirements, succeeded
  requirements, explicit failure and reasons, invoked handlers and final challenge,
  forbid or success result.
- Enforce AND across distinct requirements. Model multiple handlers for the same
  requirement as alternative success paths, while `context.Fail` makes the context
  fail even if all requirements later succeed; merely returning does not mark either
  success or guaranteed failure.
- Treat handler execution order as unspecified and account for the configured
  `InvokeHandlersAfterFailure` behavior. Handlers cannot depend on another handler's
  mutation, and logging/audit side effects that run after failure cannot grant access
  or be mistaken for a successful authorization decision.
- Prove claims by type, value, issuer, subject, authentication scheme and validated
  provenance. Role, scope, tenant and ownership strings from unrelated identities or
  untrusted transformations cannot satisfy a requirement by name collision.
- Model resource-based and operation authorization with exact resource identity,
  load version and ownership state. Prevent time-of-check/time-of-use gaps between
  authorizing a mutable entity and acting on a different or concurrently modified
  instance.
- Bound external I/O, caches, clocks and cancellation inside handlers and surface
  unavailable or stale policy data as explicit deny/unknown outcomes. Dynamic
  policies and opaque handlers need signed models rather than optimistic success.
- Emit an authorization witness with selected schemes, policy source, requirement
  graph, handler order explored, claims and resource provenance, success/failure
  calls, pending set and mapped HTTP result. Tests cover missing success, explicit
  fail, alternative handlers, order dependence, unauthenticated handler execution,
  issuer confusion, stale resource ownership, dynamic policy drift and handler I/O.

Evidence: Microsoft's [policy-based authorization documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0)
states that distinct requirements compose with AND, multiple handlers can provide
alternative success for one requirement, `Fail` is definitive, handlers may still
run after failure and handler order is unspecified. The
[`AuthorizationHandlerContext` API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationhandlercontext?view=aspnetcore-10.0)
exposes pending requirements, explicit failure and per-requirement success.

## P1 - Model raw WebSocket handshake and message lifecycles

Add protocol semantics for `WebSocket`, `ClientWebSocket` and ASP.NET Core WebSocket
acceptance from HTTP upgrade through framed text/binary messages, close handshake,
abort and disposal. Prove origin, subprotocol, concurrency, fragmentation, size and
cleanup rules for applications that operate below SignalR.

Acceptance criteria:

- Represent URI and transport identity, HTTP handshake request/response, origin,
  credentials, extensions, selected subprotocol, socket state, send and receive
  operation identity, frame/message type, fragments, bytes, UTF-8 decoder state,
  compression, close status/reason and terminal transport outcome.
- Resolve `ClientWebSocketOptions`, server acceptance options, middleware order,
  keep-alive mode and interval, proxy, TLS and certificate policy, cookies, headers,
  credentials, buffers, compression and exact runtime defaults into a connection
  profile before the upgrade makes HTTP middleware unable to replace the response.
- Validate the upgrade method and headers, WebSocket key/accept proof, version,
  request host and target, browser `Origin` allowlist, authentication and exactly one
  mutually supported subprotocol. Origin is a security input for browser clients,
  not an identity claim or substitute for authentication.
- Model connecting, open, close-sent, close-received, closed, aborted and disposed
  transitions. Allow at most one send and one receive concurrently on a connection;
  serialize multiple writers and prevent caller mutation or reuse of buffers until
  the corresponding asynchronous operation completes.
- Reassemble continuation frames into logical messages while permitting control
  frames between fragments. Enforce opcode, reserved-bit, masking-direction,
  control-frame and UTF-8 rules plus per-frame, per-message, connection and decoded
  expansion budgets before handing complete or streaming content to application code.
- Model ping/pong and framework keep-alive behavior, idle and operation deadlines,
  cancellation and half-open transport failure. A successful ping or write does not
  prove the peer consumed earlier application messages or remains authorized.
- Prove the two-sided close handshake, valid close codes and UTF-8 reason limits,
  bounded wait, peer-initiated close response, abort fallback and deterministic
  disposal. No application data is sent after close starts, and cancellation/abort
  outcomes are not reported as a clean peer-confirmed close.
- Treat reconnect as a new authenticated connection with new ordering and replay
  state. Compose with proxies, HTTP upgrade, TLS, cookies/JWT, taint, partial I/O,
  pooled buffers, hosted shutdown and rate limits without inventing delivery or
  exactly-once guarantees across disconnects.
- Emit a connection/frame witness with handshake provenance, option profile,
  concurrent operations, fragments, message boundaries, budgets, control frames,
  close exchange and disposal. Tests cover origin bypass, subprotocol confusion,
  simultaneous sends, buffer mutation, invalid masking, fragmented UTF-8, interleaved
  ping, compression bombs, oversized messages, close races, cancellation and reconnect.

Evidence: [RFC 6455](https://www.rfc-editor.org/rfc/rfc6455.html) defines the
upgrade, origin and subprotocol negotiation, masking, fragmentation, control frames
and two-sided closing handshake. Microsoft's [`ClientWebSocket.SendAsync` contract](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket.sendasync?view=net-10.0)
allows exactly one send and one receive operation in parallel and declares multiple
concurrent sends unsupported.

## P1 - Prove OpenAPI documents against executable endpoint contracts

Build a versioned correspondence between ASP.NET Core runtime endpoints and every
generated or checked-in OpenAPI document. Prove paths, binding, serialization,
responses and security metadata against executable behavior so client generation,
validation and governance do not rely on a stale or under-specified contract.

Acceptance criteria:

- Resolve controller and minimal API descriptions, route groups and conventions,
  endpoint metadata, document names, inclusion predicates, schema and operation
  transformers, generation mode, environment branches, package/runtime version and
  checked-in overlays into a deterministic document provenance graph.
- Map each public endpoint to exact server/base path, normalized route template,
  method, operation ID, parameters, request bodies, content types, response status
  and headers, callbacks/webhooks, deprecation and security requirements. Detect
  undocumented endpoints, phantom operations and path/method collisions.
- Prove parameter location, name, requiredness, style/explode encoding and duplicate
  handling against effective model binding and input formatters. Route, query,
  header and cookie parameters cannot be merged because their CLR target matches.
- Compare request and response schemas with serializer profile, converters, naming,
  nullability, required members, polymorphism, discriminators, reference handling,
  enums, numeric/string formats, file bodies and validation constraints. A schema
  accepted by tooling must describe the actual wire representation and limits.
- Enumerate reachable success and error outcomes, including validation problems,
  authentication challenge, authorization forbid, rate limits, redirects and
  exceptions, with correct bodies, content types and headers. Undeclared failures
  remain compatibility and client-safety findings rather than being hidden as 500.
- Compare document security schemes and per-operation requirements to effective
  authentication and authorization. Preserve OpenAPI's AND/OR structure and scopes;
  an empty or missing requirement cannot document a protected endpoint as public or
  a public endpoint as credential-dependent without an explicit reviewed exception.
- Model transformer execution order and arbitrary mutation, multiple documents,
  build-time versus runtime generation, trim/AOT behavior and dynamic endpoint data.
  Opaque transformers or runtime-only endpoints yield explicit gaps, not a claim
  that successful document serialization proves completeness.
- Emit canonical, reproducible documents bound to source and assembly identities,
  diff them with a version-aware compatibility policy and run positive/negative
  conformance examples against the same contract. Production exposure of documents
  and UI is separately authorized and cannot leak internal routes or schemas.
- Emit a mismatch witness with endpoint, metadata source, transformer trace,
  document pointer, runtime binding/serialization/security fact and compatibility
  impact. Tests cover hidden routes, phantom routes, nullability drift, wrong content
  types, missing errors, security OR/AND mistakes, transformer conflicts, multi-doc
  exclusion, build/runtime drift, AOT and versioned breaking changes.

Evidence: Microsoft's [ASP.NET Core OpenAPI overview](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)
distinguishes logical API operations from executable endpoints and supports both
runtime and build-time document generation. Its [customization guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0)
defines ordered schema, operation and document transformers, while the official
[OpenAPI Specification](https://spec.openapis.org/oas/) defines the wire contract
for paths, operations, schemas and security requirements.

## P1 - Prove CORS policy and preflight semantics

Add an ASP.NET Core CORS domain that derives the effective cross-origin read policy
for every endpoint and response. Prove origin, method, header and credential rules
for simple and preflighted requests without confusing a browser disclosure control
with authentication, authorization or CSRF protection.

Acceptance criteria:

- Represent the requesting origin as scheme, canonical host and port plus opaque or
  `null` cases; request mode, credentials mode, actual method and headers, preflight
  method/header request, endpoint, selected policy, response fields and browser
  allow/block outcome with provenance.
- Resolve default and named policies, middleware, endpoint `RequireCors`, enable and
  disable attributes, route groups, policy providers, origin predicates, middleware
  order and exact framework defaults into one effective policy per endpoint and
  branch. Conflicting mechanisms produce the documented composition or an explicit
  configuration finding.
- Match origins exactly after the framework's URI and host normalization. Distinguish
  scheme and port, wildcard subdomain rules, IDN/Punycode, IPv4/IPv6, loopback and
  opaque origins; suffix, substring, reflected-header or attacker-controlled
  predicate matches never prove an origin is trusted.
- Detect a valid CORS preflight from `OPTIONS`, `Origin`,
  `Access-Control-Request-Method` and requested headers. Prove the eventual method
  and every non-safelisted header are allowed, generate a coherent preflight result
  and ensure middleware ordering does not route, authorize or cache it incorrectly.
- Model allowed methods and headers, exposed response headers, credentials,
  preflight max age and wildcard behavior. An any-origin policy cannot safely grant
  credentials, and a credentialed response must identify the requesting allowed
  origin rather than emit `*`.
- Include `Vary: Origin` and other cache-key consequences whenever responses differ
  by origin. Compose CORS before response caching as required and prevent a response
  authorized for one origin, credential state or preflight from being served under
  another origin's cache key.
- Treat CORS as enforcement by conforming browsers only. Non-browser clients and
  same-origin requests can still reach the endpoint; allowed origins do not grant
  application identity or permission, and cookie-authenticated state changes still
  require authorization and antiforgery protection.
- Emit a browser/request witness with normalized origins, policy resolution,
  preflight decision, requested and emitted fields, credentials, cache variation,
  endpoint execution and observable response. Tests cover origin suffix attacks,
  scheme/port drift, `null`, wildcard credentials, failed preflight, header casing,
  endpoint overrides, missing middleware, cached origin leaks, errors and redirects.

Evidence: Microsoft's [ASP.NET Core CORS documentation](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0)
defines policy resolution, preflight processing, origins, methods, headers,
credentials and required middleware order. It explicitly rejects combining any
origin with credentials and requires CORS to run before response caching.

## P1 - Model health checks, readiness, liveness, and publishers

Add first-class semantics for `IHealthCheck`, health reports, mapped endpoints,
readiness and liveness state, publishers and orchestrator consumers. Prove that
probe status reflects the intended dependency set and freshness so automation does
not route traffic to an unready instance or restart a live but recovering process.

Acceptance criteria:

- Represent registration name, tags, timeout, failure status and check instance;
  execution start/end, cancellation, duration, result status, description, exception
  and data; aggregate report, endpoint mapping, HTTP status and publisher delivery.
- Resolve registrations, duplicate names, predicates, endpoint options and writers,
  host/authorization/CORS restrictions, publisher delay/period/timeout/predicate,
  lifecycle integration and package/runtime defaults into separate effective probe
  and publication profiles.
- Distinguish startup, readiness, liveness and diagnostic/dependency checks. Bind
  readiness to completed initialization and ingress drain state, while liveness
  reports whether process restart is appropriate; one aggregate endpoint cannot
  silently substitute for both contracts.
- Prove each predicate selects the intended checks. An empty selection can aggregate
  to healthy and therefore needs an explicit liveness-only contract rather than
  being accepted as evidence that dependencies or startup work passed.
- Model `Healthy`, `Degraded` and `Unhealthy`, configured failure status, aggregation,
  exception and timeout behavior and HTTP status mapping. A caught exception,
  canceled check or stale cached result is not converted to healthy without a
  declared fail-open policy and bounded risk window.
- Require checks to be bounded, cancellable and safe under concurrency. They must not
  mutate production state, hold scarce locks, amplify outages or exhaust the very
  dependency being measured; shared probe caches carry explicit timestamp and age.
- Model publishers independently from endpoint probes, including periodic overlap,
  cancellation, backpressure, failed delivery and shutdown. Publishing an old or
  failed report does not change local readiness, and monitoring receipt is not proof
  that the instance remains healthy afterward.
- Secure health endpoints by host/port, authorization and network policy; suppress
  caching and redact connection strings, exception text and internal topology from
  public writers while retaining detailed diagnostics for an authorized channel.
- Emit a health timeline witness with lifecycle phase, selected checks, execution
  and freshness, aggregate/status mapping, readiness transition, endpoint caller and
  publisher outcome. Tests cover empty predicates, wrong tags, startup races, drain,
  degraded mapping, timeouts, exceptions, overlapping publishers, dependency storms,
  cached reports, public detail leaks and orchestrator restart/routing decisions.

Evidence: Microsoft's [ASP.NET Core health-check documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
distinguishes readiness from liveness, documents predicate-based selection and the
empty-check liveness pattern, and defines endpoint status mapping plus periodic
`IHealthCheckPublisher` delay, period and timeout behavior.

## P1 - Prove ASP.NET Core session and TempData consistency

Model `ISession`, distributed session stores, session cookies and both TempData
providers as explicit request-to-request state protocols. Prove load, concurrent
mutation, commit and read-once behavior so ephemeral UI state cannot silently become
authorization, durable business state or a lost-update source.

Acceptance criteria:

- Represent browser/session ID and cookie scope, protected cookie payload, store and
  record version, coherent key/value blob, loaded snapshot, local mutations, idle
  expiry, I/O timeout, availability, commit and TempData item states with provenance.
- Resolve middleware position, `SessionOptions`, cookie consent/essential policy,
  Data Protection and application name, `IDistributedCache` provider, serializer,
  TempData provider and runtime defaults into one effective state profile.
- Enforce asynchronous `LoadAsync` before reads or writes where scale guarantees are
  claimed; otherwise surface the provider's synchronous fallback. Distinguish
  missing, expired, unavailable and corrupt records from an intentionally empty
  session rather than treating every lookup failure as a default value.
- Model session as a non-locking coherent record. Concurrent requests begin from
  snapshots and last commit can overwrite the entire other update even for distinct
  keys; prove serialization, versioned compare-and-swap or move conflict-sensitive
  state to a transactional store.
- Track deferred middleware commit separately from explicit `CommitAsync`. A logged
  background persistence failure after the response cannot support a success message
  or business invariant; flows that depend on the write await and handle commit
  before final response publication.
- Prove a new session cookie is issued before the response starts and has the intended
  secure, `HttpOnly`, SameSite, path, domain, consent and lifetime scope. Idle content
  expiry is distinct from browser cookie lifetime, and authentication transitions
  cannot rely on an attacker-known session ID or anonymous session contents.
- Model cookie and session TempData serialization, protection, size and read-once
  deletion plus `Peek` and `Keep`. Redirect chains, parallel requests, retries and
  failed commits cannot consume, duplicate or resurrect a message silently.
- Keep session and TempData ephemeral and non-sensitive unless separately protected.
  Compose with cookies, antiforgery, distributed cache, response caching, SignalR,
  privacy consent and multi-node key/store configuration; critical state persists in
  an authoritative database or durable workflow.
- Emit a request-pair witness with cookie/session identities, load snapshot, reads,
  mutations, competing requests, commit ordering/failure, expiry and TempData
  consumption. Tests cover lost updates, sync fallback, unavailable store, deferred
  commit lies, response-started cookies, fixation, consent, idle expiry, multi-node
  drift, TempData parallel reads, `Peek`/`Keep`, oversized cookies and redirect retry.

Evidence: Microsoft's [ASP.NET Core session-state documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/app-state?view=aspnetcore-10.0)
states that session is ephemeral, coherent and non-locking, so concurrent requests
can overwrite even different keys. It also documents explicit asynchronous load,
deferred commit failures, `CommitAsync`, cookie timing, distributed storage and the
cookie/session TempData choices.

## P1 - Model Kestrel resource limits and timeout enforcement

Add server-profile semantics for Kestrel listeners, connection and protocol limits,
request/response time budgets, body limits, data rates and per-request feature
overrides. Prove that hostile or stalled clients cannot consume unbounded sockets,
streams, memory, threads or parsing time before application policy takes effect.

Acceptance criteria:

- Represent listener and endpoint, transport and HTTP version, physical connection,
  multiplexed stream, upgraded connection, request parse/body/response phases,
  byte/window counters, configured and effective limits, deadlines, debugger mode,
  proxy server and rejection/abort outcome.
- Resolve code, configuration and endpoint Kestrel options, hosting integration,
  reverse-proxy/IIS limits, protocol-specific defaults, per-request features,
  environment branches, configuration reload and exact runtime version into one
  effective limit hierarchy.
- Prove bounds for concurrent ordinary and upgraded connections separately because
  an upgraded connection leaves the ordinary counter. Include HTTP/2 and HTTP/3
  streams, WebSockets, connection lifetime, accept backlog and graceful-drain state
  without multiplying a per-process limit optimistically across replicas.
- Enforce request-line, total and per-field header size/count, header receive timeout,
  keep-alive timeout, request body size and minimum body/response data rates at the
  correct protocol phase. Model grace periods, clock source and the documented
  debugger exemptions rather than treating debug tests as production enforcement.
- Allow endpoint or middleware body-size overrides only while the feature is mutable
  and before any body read. Compose Kestrel, IIS/proxy, multipart, decompression and
  application quotas by the smallest effective bound and surface a missing outer
  limit when buffering occurs before the application sees the request.
- Model HTTP/2 and HTTP/3 frame, header table/field, concurrent stream, flow-control
  window, reset rate and keep-alive limits with connection-versus-stream accounting.
  One slow or abusive stream cannot reserve connection windows or scheduler work
  indefinitely for every peer stream.
- Track synchronous I/O permission, memory pool and buffer ownership, response
  buffering, TLS handshake/client certificate work and cancellation. Enabling
  synchronous I/O or unlimited rates/sizes requires an explicit bounded isolation
  argument, not a generic compatibility exception.
- Prove rejection status or connection error, logging cardinality, cleanup and
  metrics without reflecting secrets or allocating in proportion to hostile
  declared sizes. Proxy retries and clients cannot turn early rejection into a
  synchronized retry storm.
- Emit a connection/stream witness with configuration source, counters, deadline and
  rate samples, feature mutability, proxy bounds, selected protocol rule and resource
  release. Tests cover slow headers/body, oversized bodies before/after reads,
  upgraded-connection exhaustion, HTTP/2 stream floods, window starvation, debugger
  differences, IIS precedence, sync I/O starvation, reload and shutdown drain.

Evidence: Microsoft's [Kestrel options and limits reference](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0)
documents separate ordinary/upgraded connection limits, request body and header
timeouts, minimum data rates, protocol-specific HTTP/2 limits, per-request feature
mutability, IIS precedence and timeout/rate exemptions while a debugger is attached.

## P1 - Prove HTTP output-cache keys and response isolation

Add protocol semantics for ASP.NET Core Output Caching and RFC-style Response
Caching Middleware. Prove cache eligibility, key variation, freshness, revalidation,
invalidation and storage so one user, tenant, origin or representation never receives
a response produced for another security or content context.

Acceptance criteria:

- Distinguish server-controlled output caching from HTTP response caching governed
  by request/response headers. Represent request and response identity, selected
  policy, cache key and partition, stored status/headers/body, validators, age,
  freshness, tags, locking and hit/miss/revalidate/bypass/evict outcomes.
- Resolve base, named and endpoint output policies, custom `IOutputCachePolicy`
  phases, response-cache attributes and features, middleware order, store provider,
  limits, case sensitivity, runtime defaults and request/response cache directives
  into one effective decision trace.
- Derive keys from canonical scheme, host, port, path/base, method and query plus all
  configured route, query, header and value variations. Include authentication,
  tenant, authorization result, culture, encoding, media type, origin and feature
  flags whenever they can change observable output, or prove the response is public
  and identical without them.
- Preserve safe defaults that restrict methods/status, authenticated requests and
  responses that set cookies. Any custom policy that caches POST, redirects,
  authenticated or cookie-setting responses must prove idempotency, replay behavior,
  body identity and user-independent content before enabling storage.
- Implement RFC cacheability and precedence for `Cache-Control`, `Pragma`,
  `Authorization`, `Set-Cookie`, `Vary`, `Expires`, `Date`, `Age`, validators and
  conditional requests. `Vary: *` is not stored, and `Vary: Origin` or encoding
  separates representations rather than being copied only as metadata.
- Prove buffered body/status/header completeness and configured entry/body/store
  size limits. Streaming, trailers, partial writes, exceptions, cancellation and
  response mutation after cache capture are rejected or modeled explicitly; a
  failed generation never publishes a partial entry.
- Model resource locking, request collapse and stampede behavior plus distributed
  store atomicity, tag invalidation, eviction and multi-node propagation. Disabled
  locking and delayed invalidation create bounded duplicate-generation or stale
  windows and cannot violate side-effect or revocation requirements.
- Prove `ETag` and date revalidation, 304 response fields, freshness extension and
  invalidation against the exact representation version. Compose with CORS,
  compression, session/TempData, cookies, authorization, configuration reload and
  deployment version so middleware order cannot cache before a safety field appears.
- Emit a cache witness with policy phases, canonical key inputs, security partition,
  eligibility fields, store operation, freshness/validator math, lock, tags,
  invalidation and served representation. Tests cover user/tenant leaks, omitted
  query/header variation, origin/encoding confusion, custom POST caching, `Set-Cookie`,
  stale authorization, 304 mistakes, stampedes, partial bodies and node invalidation.

Evidence: Microsoft's [ASP.NET Core Output Caching guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0)
defines default exclusions for authenticated and cookie-setting responses, cache-key
variation, resource locking, limits, revalidation and distributed stores. The
[Response Caching Middleware reference](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/middleware?view=aspnetcore-10.0)
defines RFC-oriented `Authorization`, `Cache-Control`, `Set-Cookie`, `Vary`,
validator and body-completeness conditions.

## P1 - Prove request timeout budgets and cancellation propagation

Add ASP.NET Core Request Timeouts Middleware semantics from policy selection through
deadline expiry, `RequestAborted` cancellation, downstream work and response
publication. Prove that each bounded endpoint actually stops or isolates work by its
deadline instead of mistaking token cancellation for request or side-effect abort.

Acceptance criteria:

- Represent request and endpoint identity, selected default/named/inline/disabled
  policy, start and deadline, clock, timeout feature state, cancellation source,
  response-started state, downstream operations, terminal response and lingering
  work with provenance.
- Resolve middleware order after routing, endpoint metadata and attributes, named
  policies, global default, dynamic feature calls, timeout status/writer, environment
  and exact framework defaults into one effective request budget. Registration
  without an applied policy is not a timeout guarantee.
- Distinguish client disconnect, host shutdown, caller cancellation and middleware
  timeout even when they converge on `HttpContext.RequestAborted`. Compose linked
  tokens without losing the earliest cause or resetting a parent deadline at each
  layer.
- Model expiry as cancellation notification: the middleware doesn't automatically
  call `Abort`, stop CPU work, roll back effects or prevent application code from
  returning a success. Prove every cancellable wait receives the token and every
  non-cancellable or detached operation is bounded or isolated separately.
- Propagate the remaining absolute budget to HTTP/gRPC/database calls, locks,
  retries, queues and streams. Connection, command and per-attempt timeouts cannot
  exceed the request budget, and retries reserve cleanup/response time rather than
  starting a fresh full duration.
- Prove timeout response status and writer execute only when the response hasn't
  started. For started or streaming responses, define abort/trailer/partial-response
  behavior explicitly; a caught `OperationCanceledException` cannot overwrite an
  already committed response or claim a transactional rollback.
- Govern endpoint and runtime calls that disable a timeout. WebSockets, downloads,
  uploads, server-sent streams and long polling require idle, progress and total
  budgets from another proven mechanism rather than an unbounded exception.
- Account for debugger behavior, timer granularity, clock jumps, scheduling delay,
  cancellation races and work that completes at the deadline. Production assertions
  are tested without an attached debugger because framework timeouts are suppressed
  during debugging.
- Emit a timeline witness with policy source, absolute/remaining budget,
  cancellation cause, downstream propagation, effects, response commit, timeout
  writer and post-timeout work. Tests cover missing policies, disabled endpoints,
  ignored tokens, CPU loops, nested retries, simultaneous disconnect, started
  responses, streaming, debugger suppression and side effects after timeout.

Evidence: Microsoft's [Request Timeouts Middleware documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0)
states that no timeout is applied merely by registering middleware, expiry cancels
`RequestAborted` without aborting the request, response writers cannot replace a
started response and timeouts don't trigger under a debugger.

## P1 - Model HTTP request decompression and response compression safety

Add content-coding semantics for ASP.NET Core request decompression and response
compression, including negotiation, stream replacement, expanded-byte limits,
cache variation and HTTPS compression side channels. Prove the exact bytes seen by
signature/parsing code and emitted to each client under bounded CPU and memory.

Acceptance criteria:

- Represent raw and decoded body identities, ordered content codings, compressed and
  expanded byte counts, provider/version, decoder state, request-body wrapper,
  response media type, acceptable encodings and quality values, selected encoding,
  secret/reflected fields, cache variant and stream lifecycle.
- Resolve request/response middleware order, default and custom providers, MIME
  inclusion/exclusion, HTTPS feature overrides, server/proxy compression, endpoint
  and server body limits, buffering and exact runtime defaults into one effective
  coding profile.
- Apply request decompression before every consumer that expects decoded content.
  Model lazy decoding on read and the removal of a handled `Content-Encoding` field;
  unsupported or multiple encodings that pass through remain encoded and cannot be
  parsed or signature-verified under a decoded-body assumption.
- Enforce the smallest endpoint/server limit on expanded request bytes, not only
  wire `Content-Length`, plus compression ratio, CPU/time, nesting and memory/disk
  budgets. Invalid/truncated streams, limit overruns and cancellation dispose the
  decoder and reject before application effects.
- Parse `Accept-Encoding` quality and wildcard/identity semantics and select only a
  supported provider allowed for the response media type and protocol. Recompute or
  remove invalidated length/digest fields and emit coherent `Content-Encoding` and
  `Vary: Accept-Encoding` for compressed and identity cache variants.
- For HTTPS, reject compression of responses that combine attacker-controlled input
  with secrets unless a reviewed mitigation eliminates length-oracle observations.
  Antiforgery, padding, rate limits and secret rotation are modeled by their actual
  guarantees rather than treating TLS as protection from CRIME/BREACH-style leakage.
- Compose with output caches/CDNs, proxies, partial I/O, streaming flush, range
  responses, gRPC/WebSockets, archives, multipart, webhooks and content sniffing.
  Prevent double encoding/decoding and prove which layer owns each stream and buffer.
- Emit a coding witness with raw/decoded hashes and sizes, header transitions,
  provider negotiation, limits, MIME/HTTPS decision, secret/reflection classification,
  cache key, bytes emitted and cleanup. Tests cover bombs, corrupt data, stacked
  encodings, unknown providers, q-values, identity, double compression, missing
  `Vary`, proxy rewrites, HTTPS oracles, ranges, cancellation and partial flush.

Evidence: Microsoft's [request decompression guidance](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-decompression?view=aspnetcore-10.0)
documents lazy stream replacement, unsupported/multiple encoding pass-through and
expanded-body enforcement against endpoint/server limits. The official
[`EnableForHttps` contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.responsecompression.responsecompressionoptions.enableforhttps?view=aspnetcore-10.0)
defaults to false and warns that compressing remotely manipulable HTTPS content can
create security problems.

## P1 - Prove webhook authenticity, replay control, and durable acceptance

Add provider-profiled webhook receiver semantics from raw HTTP delivery through
signature and timestamp validation, event classification, atomic deduplication,
durable enqueue, acknowledgment and idempotent processing. Prove both authenticity
and delivery outcome despite retries, redelivery, reordering and schema evolution.

Acceptance criteria:

- Represent provider/endpoint and tenant, subscription, event and delivery IDs,
  event type/action/version, raw body bytes, signed header fields, timestamp,
  signature candidates, secret/key version, verification, replay record, attempt,
  acknowledgment and processing state with provenance.
- Resolve provider-specific canonicalization, algorithm and header grammar, endpoint
  secret selection/rotation, timestamp tolerance, delivery behavior, source network
  hints, event schema and runtime/body middleware order into a signed versioned model.
- Preserve the exact raw bytes required by the signature scheme before JSON parsing,
  Unicode normalization, decompression, form binding or body rewriting. Construct the
  provider-defined signed input exactly, reject missing/duplicate/malformed fields
  and compare every supported MAC/signature in constant time with an allowed key.
- Bind keys to provider, endpoint, tenant and environment; support overlap during
  planned rotation without accepting a test/CLI secret in production. Keep secrets,
  signatures and full sensitive payloads out of URLs, logs, metrics and witnesses.
- Enforce a bounded timestamp window where the protocol supplies one and atomically
  claim a stable delivery/event ID before effects. Dedup retention covers the
  provider's retry/redelivery horizon; a manual redelivery with the same ID returns
  the stored outcome rather than repeating a non-idempotent effect.
- Validate event type, action, account/tenant, API/schema version and payload limits
  after authenticity. Unknown new actions, thin versus snapshot payloads and deleted
  resources produce explicit forward-compatible paths and may require an authenticated
  API fetch rather than trusting stale embedded state.
- Return success only after the delivery is durably recorded or atomically completed.
  If processing continues asynchronously, prove queue acceptance before 2xx; timeout,
  crash and ambiguous response windows tolerate provider retries without event loss.
- Compose handler effects with inbox/outbox, database transactions, broker settlement,
  ordering keys, retries and compensation. Provider IP allowlists and TLS are
  defense-in-depth, never substitutes for signature verification and replay control.
- Emit a redacted delivery witness with raw-body identity, canonical signed input,
  key version, time/replay decisions, schema dispatch, durable boundary, response and
  processing result. Tests cover body mutation, Unicode, duplicate signatures, wrong
  endpoint secret, rotation, old timestamps, replay races, redelivery, reordering,
  unknown actions, queue failure, crash-before-ack and forged source IP.

Evidence: GitHub's [webhook validation documentation](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
requires HMAC verification of the payload before processing. Its [best practices](https://docs.github.com/en/webhooks/using-webhooks/best-practices-for-using-webhooks)
recommend unique delivery IDs for replay defense, bounded acknowledgment time and
redelivery handling. Stripe's [signature troubleshooting guidance](https://docs.stripe.com/webhooks/signature)
shows that signature verification requires the original, unmodified request bytes.

## P1 - Prove distributed lease ownership and fencing

Add provider-profiled distributed lock and lease semantics for cross-process mutual
exclusion, leader election and singleton work. Prove acquisition, finite validity,
renewal, release and downstream fencing under pauses, partitions, retries and
failover instead of applying in-process mutex assumptions to an asynchronous system.

Acceptance criteria:

- Represent resource/lock namespace, provider cluster and consistency mode, owner
  request and session, unique ownership value, monotonic fencing token, lease start,
  expiry and remaining validity, quorum/replica acknowledgments, reentrancy, waiters,
  renewal, release and uncertain/lost states.
- Resolve exact provider algorithm and topology, replication/consensus and persistence
  mode, TTL and drift assumptions, acquire/operation timeouts, retry/backoff,
  auto-renewal, reentrancy, session heartbeat and client-library version into a
  declared safety/liveness profile.
- Treat acquisition as successful only after the provider's atomic or quorum
  condition completes within the remaining validity window. Clean partial acquisition
  promptly, randomize bounded contention retries and never enter the critical section
  after timeout, cancellation or an ambiguous acquire result.
- Use a high-entropy per-acquisition ownership value and compare it atomically when
  renewing or releasing. A stale owner cannot delete or extend a successor's lease,
  and cleanup failure is recorded rather than hidden by an unconditional key delete.
- Prove all critical work finishes within the minimum lease validity after acquisition
  latency, clock drift and safety margin or successfully renews before that boundary.
  GC pauses, process suspension, network partitions and delayed responses can expire
  ownership even while local code still believes it holds the lock.
- Require a monotonically increasing fencing token for effects on resources that can
  receive delayed stale-owner requests. The protected database, storage or service
  atomically rejects tokens older than its last accepted value; a random ownership
  token alone proves safe release, not downstream fencing.
- Model provider-specific partition, failover, crash/restart and persistence behavior
  plus CP/AP modes. State the availability tradeoff and bounded assumptions explicitly;
  a single-instance, replicated failover or majority algorithm cannot inherit a
  stronger mutual-exclusion claim from the word `lock`.
- Bound wait, hold, renewal attempts and retry storms; define cancellation, lease loss,
  critical-section abort, partial effect and recovery/idempotency. Leadership and
  singleton schedulers stop publishing or accepting work before ownership uncertainty
  and transfer using fenced state.
- Emit a distributed timeline witness with nodes/quorum, ownership value, fencing
  token, clock/latency margins, pauses/partitions, renewals, protected effects and
  release. Tests cover stale release, expired owners, GC pauses, split brain, quorum
  loss, delayed messages, failover, clock drift, renewal races, reentrancy, contention
  and an unfenced downstream resource.

Evidence: Redis's [distributed-lock documentation](https://redis.io/docs/latest/develop/clients/patterns/distributed-locks/)
defines unique ownership values, conditional release, finite validity, clock-drift
assumptions, partial-acquisition cleanup and partition tradeoffs. Hazelcast's
[`FencedLock` documentation](https://docs.hazelcast.com/hazelcast/5.5/data-structures/fencedlock)
demonstrates why a paused expired owner needs a monotonic fencing token that the
external resource rejects after a successor acquires the lock.

## P1 - Model cloud object storage versions, conditions, and multipart writes

Add provider-profiled semantics for blob/object containers, keys, versions,
properties, metadata, streaming and multipart transfers, conditional operations,
leases, retention and deletion. Prove atomic publication and concurrency behavior so
retries or parallel writers cannot silently overwrite, expose or orphan content.

Acceptance criteria:

- Represent provider/account/container, normalized object key, version/generation and
  delete marker, ETag and last-modified value, content length/type/encoding/checksum,
  metadata/tags, encryption state, lease, multipart/upload ID and staged parts,
  request conditions, consistency observation and operation outcome.
- Resolve SDK/service version, endpoint and region, authorization, retry policy,
  transfer thresholds/concurrency, checksum, overwrite default, versioning, soft
  delete, lifecycle/retention/legal hold, replication and exact provider consistency
  profile into one operation model.
- Prove create-only, compare-and-swap, read/copy/delete-if-version and overwrite
  intent with atomic `If-None-Match`, `If-Match`, generation or provider-equivalent
  conditions. A prior HEAD/check followed by an unconditional write is a race, and
  precondition failure triggers merge/refetch policy rather than blind retry.
- Model last-writer-wins explicitly when no condition is present and reject it for
  conflict-sensitive state. Track ETags as opaque provider/version validators rather
  than assuming they are content hashes, and bind every condition to the exact object
  identity and version observed.
- Track multipart/block upload initiation, ordered part identities/checksums,
  concurrent staging, completion manifest, final version and abort/garbage collection.
  Publish only after confirmed atomic completion; retrying parts or completion cannot
  mix upload IDs, duplicate data or report success while orphaned parts grow unbounded.
- Model leases by resource type and enforced operation set. For example, an Azure
  blob lease guards blob writes/deletes while a container lease guards container
  deletion; finite expiry, renewal, break and lease ID conditions do not imply that
  every read or container operation is exclusive.
- Prove metadata/property updates, server-side copy, rename-as-copy/delete, append
  position and batch operations against source and destination versions. Full
  metadata replacement cannot erase concurrent fields, and copy completion/source
  snapshot is not assumed synchronous without provider evidence.
- Model versioning, delete markers, soft delete, immutability/retention and lifecycle
  rules through restore and permanent deletion. Authorization or signed URLs are
  scoped by account/container/key, method, permissions, time, network and version and
  are redacted as credentials.
- Compose streaming with partial I/O, checksums, encryption, archive safety, cache/CDN,
  distributed leases, webhook events and idempotent retries. Emit a witness with key
  bytes, observed/current versions, conditions, parts, lease, retries, publication,
  replication and cleanup. Tests cover overwrite races, stale ETags, ambiguous PUT,
  multipart retry/orphans, lease expiry, copy races, metadata loss, delete markers,
  eventual observations, checksum mismatch and credential leakage.

Evidence: Microsoft's [Azure Blob concurrency guidance](https://learn.microsoft.com/en-us/azure/storage/blobs/concurrency-manage)
defines strong consistency, default last-writer-wins, ETag/`If-Match` optimistic
concurrency and operation-specific leases. Its [.NET blob lease guidance](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-lease)
defines acquire, renew, release, break, expiry and the write/delete operations a blob
lease protects. Amazon S3's [conditional-write documentation](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)
likewise requires atomic `If-None-Match` or `If-Match` conditions to prevent object
overwrites.

## P1 - Prove cloud credential selection and token scope

Add provider-profiled cloud identity semantics for Azure-style credential chains,
managed and workload identity, service principals and developer credentials. Prove
which identity is selected and what a cached access token authorizes instead of
treating a successful `GetToken` call as an environment-independent fact.

Acceptance criteria:

- Represent credential chain and attempt order, credential kind and availability,
  tenant/client/object/principal identity, authority and cloud, resource/audience,
  requested scopes and claims, token expiry/refresh, cache key and authentication
  outcome with configuration provenance.
- Resolve library version, constructor/options, environment variables, host type,
  managed/workload identity endpoint, client ID, federated token file, developer
  tool sessions and broker/interactive settings into the effective ordered chain.
  Package upgrades or environment changes that alter that order invalidate the proof.
- Distinguish an unavailable credential, which may permit the chain to continue, from
  an attempted credential that fails authentication. Do not silently fall through
  an invalid production identity to a developer login or a different principal.
- Bind the selected credential to the intended tenant, application or managed
  identity, cloud authority and deployment environment. Production profiles use an
  explicit deterministic credential where identity ambiguity is unacceptable and
  exclude IDE, CLI, PowerShell, interactive and test credentials.
- Prove the exact scopes/resource, audience, tenant and additional claims for every
  token request and downstream client. A token for one API, sovereign cloud, tenant
  or user-delegated context cannot satisfy another, and role assignment is a separate
  authorization premise rather than an inference from token acquisition.
- Model workload identity subject/audience/issuer and managed identity client or
  resource ID selection, endpoint availability and rotation. Reject ambiguous hosts
  with multiple user-assigned identities and prevent metadata endpoint or federated
  token substitution from changing the intended principal.
- Key token caches by all security-relevant request and identity dimensions, refresh
  before expiry with bounded skew, coordinate concurrent refresh and reuse long-lived
  credential/client objects. Never persist, log or expose token, assertion, client
  secret, certificate private key or federated token contents in a witness.
- Bound authentication attempts, network timeouts and retries; distinguish transient
  authority/metadata failures from invalid configuration and revocation. Cancellation,
  clock skew, cache corruption and an ambiguous refresh cannot yield an expired token
  or a retry storm.
- Emit a redacted credential witness with ordered attempts, availability/failure
  classification, selected principal identifiers, authority, scope/audience, cache
  decision, expiry/refresh and downstream authorization assumption. Tests cover chain
  reordering, developer fallback, multi-identity hosts, tenant mismatch, wrong scope,
  token expiry, concurrent refresh, revoked secrets and sovereign-cloud endpoints.

Evidence: Microsoft's [.NET credential-chain guidance](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains)
documents `DefaultAzureCredential` ordering, exclusions, environment constraints and
the debugging, performance and behavioral reasons to use a deterministic credential
in production. The [`DefaultAzureCredential` API contract](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet)
defines the concrete environment, workload, managed identity and developer credential
sources whose availability determines selection.

## P1 - Prove email composition, authentication, and delivery outcomes

Add protocol-profiled email semantics from envelope and RFC message construction
through MIME serialization, SMTP capability negotiation, authentication, acceptance,
retry and asynchronous delivery status. Prove the exact bytes and per-recipient
outcome instead of equating `Send` completion with final delivery.

Acceptance criteria:

- Represent envelope sender and recipients separately from `From`, `Sender`, `To`,
  `Cc`, `Bcc` and reply headers; track message/domain identity, message ID, date,
  subject, MIME tree, encoded bytes, attachments, SMTP session/attempt and each
  recipient's acceptance, rejection, deferral, bounce or unknown state.
- Parse and validate mailbox, domain/IDN and display-name forms with explicit
  SMTPUTF8 support. Reject CR/LF header injection, ambiguous duplicate singleton
  fields, invalid folding and recipient leakage; `Bcc` recipients remain in the
  envelope without being exposed in the serialized recipient headers.
- Build a deterministic MIME tree with unique safe boundaries, declared charsets,
  transfer encodings, content types/dispositions and sanitized attachment filenames.
  Stream ownership, length and rewindability are explicit, and size limits account
  for encoding expansion and the provider's final message limit.
- Preserve the exact post-MIME bytes covered by DKIM, including selected headers,
  body canonicalization, line endings and trailing-body rules. Sign after content
  transfer encoding and before SMTP transport transformations; bind selector, domain,
  algorithm, key version and signed-header set and protect private signing material.
- Model EHLO capabilities, STARTTLS policy, certificate/hostname validation,
  authentication mechanisms and server identity. A required encrypted session fails
  closed when STARTTLS is absent, stripped or invalid rather than downgrading to clear
  text credentials or message content.
- Track every SMTP command and reply class plus per-recipient `RCPT TO` results.
  Separate server acceptance from mailbox delivery: positive completion proves only
  the accepting server's responsibility, while later DSNs, bounces, filtering and
  expiration may change the externally observed outcome.
- Retry transient failures and deferrals with bounded backoff and expiry; do not retry
  permanent recipient or policy failures blindly. Model the ambiguous disconnect
  after acceptance, stable message IDs, outbox state and provider idempotency so a
  retry cannot silently duplicate a non-idempotent notification.
- Compose SPF/DMARC alignment and bounce/return-path handling with DKIM identity,
  forwarding and provider profiles without claiming local message construction proves
  remote authentication success. Redact addresses, bodies, credentials and attachment
  content from logs, metrics and witnesses according to data classification.
- Emit an email witness with envelope/header identities, MIME part digests, serialized
  byte identity, DKIM inputs, TLS/auth negotiation, SMTP transcript classes and
  per-recipient state. Tests cover header injection, Unicode addresses, Bcc privacy,
  boundary collision, stream failure, signature mutation, STARTTLS downgrade, mixed
  recipient replies, timeout-after-acceptance, retries, bounces and oversized mail.

Evidence: The [.NET `SmtpClient` documentation](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient?view=net-10.0)
states that it is not recommended for new development because it lacks support for
many modern protocols. [RFC 5321](https://www.rfc-editor.org/rfc/rfc5321.html)
defines SMTP command, reply, retry and server-acceptance semantics; [RFC 6376](https://www.rfc-editor.org/rfc/rfc6376.html)
defines DKIM's byte-sensitive header/body canonicalization and signing placement;
and [RFC 6531](https://www.rfc-editor.org/rfc/rfc6531.html) defines SMTPUTF8 negotiation
for internationalized addresses.

## P1 - Prove durable workflow replay and activity idempotency

Add framework-profiled durable orchestration semantics for event-sourced replay,
activities, timers, external events, entities, retries, compensation and versioned
deployment. Prove deterministic decisions and effect boundaries across suspension,
crash and replay rather than executing orchestrator code as an ordinary async method.

Acceptance criteria:

- Represent orchestration name/version and instance/execution ID, input, history
  cursor/events, replay flag, logical time, deterministic task/timer IDs, activity
  attempts, external events, entity operations, custom status and terminal outcome
  with durable persistence provenance.
- Resolve framework/backend version, serializer, task hub/namespace, storage and
  consistency settings, retry policies, concurrency, history limits and deployment
  routing into a declared orchestration profile. Instances remain bound to compatible
  code/version behavior for the lifetime of their replay history.
- Require orchestrator decisions to be a deterministic function of input and ordered
  history. Use framework APIs for current time, GUIDs, timers, activities and external
  events; reject direct I/O, threads, delays, randomness, mutable globals, ambient
  environment/configuration and unordered iteration that can diverge during replay.
- Compare scheduled operations by stable order, identity and payload shape on replay
  and surface nondeterminism before issuing new effects. Logging, telemetry and custom
  status distinguish replay from first execution so history reconstruction does not
  duplicate externally visible observations.
- Treat activities as at-least-once unless a stronger backend guarantee is proven.
  Bind each effect to instance, operation and attempt/idempotency identity, persist
  deduplication with the effect or use an atomic inbox/outbox, and model the crash
  window after the effect completes but before its result is durably recorded.
- Model durable timers, retry backoff, deadlines and cancellation in logical workflow
  time. Bound attempts and total duration, propagate cancellation intentionally and
  do not block orchestrator threads with ordinary sleeps, synchronous waits or
  long-running CPU work.
- Correlate, validate and durably buffer external events by instance, name, sender and
  stable event ID; state ordering and duplicate policy explicitly. Timeout/event races
  choose one recorded winner and preserve or discard the loser according to the
  framework contract rather than depending on wall-clock arrival timing.
- Version orchestrators, activities, schemas and serializers with safe routing for
  in-flight instances. Use compatibility-preserving changes, side-by-side versions,
  migration or explicit termination; bound history growth with `ContinueAsNew` or an
  equivalent while carrying forward deduplication and required state.
- Model failure, compensation and saga state as durable, retryable steps. Protect and
  classify persisted inputs, outputs, exceptions and event payloads because workflow
  history is durable data. Emit a replay witness and test nondeterministic time/GUIDs,
  reordered tasks, duplicate activities/events, timer races, code upgrades, backend
  outage, compensation failure, secret persistence and history exhaustion.

Evidence: Microsoft's [Durable Task programming-model overview](https://learn.microsoft.com/en-us/azure/azure-functions/durable/programming-model-overview)
documents event-sourced replay, deterministic orchestrator constraints and at-least-
once activity execution. Its [orchestration versioning guidance](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-orchestration-versioning)
explains how changed code can break replay and how instances bind to versions, while
the [external-event contract](https://learn.microsoft.com/en-us/azure/durable-task/common/durable-task-external-events)
defines durable queuing for events delivered to waiting orchestrations.

## P1 - Model background job schedules, misfires, and cluster execution

Add scheduler-profiled background-job semantics for calendars, time zones, durable
job stores, trigger acquisition, misfires, retries, concurrency and clustered
execution. Prove when a job may run and how duplicate or missed work is recovered
instead of treating a cron expression as an exactly-once clock.

Acceptance criteria:

- Represent scheduler/cluster, durable job and trigger/calendar identities, job type
  and version, schedule and time zone, previous/next/planned fire time, acquisition
  owner, fire instance, recovery/refire flag, attempt, misfire decision and execution
  outcome with persistent-store provenance.
- Resolve framework and job-store versions, serializer, schema/table prefix, cluster
  mode and node IDs, check-in/dead thresholds, polling/batch settings, lock strategy,
  thread/concurrency limits, misfire threshold and shutdown policy into an effective
  scheduling profile.
- Parse interval, calendar and cron schedules with explicit time zone and daylight-
  saving gap/overlap policy. Prove next-fire calculations at boundaries, leap cases
  and clock changes; never inherit the process-local zone or silently reinterpret a
  persisted schedule after a tzdata or configuration change.
- Model trigger acquisition, persisted fired state, execution and completion as
  separate transitions. A crash or partition in any transition may yield a missed,
  recovered or duplicate attempt, so stable execution identity and idempotent/atomic
  effect handling are required for business operations.
- Declare a misfire instruction per trigger type: fire now, skip, preserve cadence or
  reschedule. Apply it to downtime and scheduler starvation with bounded catch-up;
  default/framework-version behavior and a global threshold cannot silently decide
  whether hours of overdue work execute in a burst.
- Scope nonconcurrent execution by the scheduler's exact job key and persistent
  cluster, not merely by CLR type or process. Treat exclusion as overlap control, not
  idempotency, and use database constraints, leases/fencing or effect keys where work
  can outlive ownership or be retried after uncertain completion.
- Keep schedule cadence, recovery and application retry policies distinct. Bound
  attempts, total elapsed time and backoff, classify transient versus permanent
  failures and prevent a long retry from overlapping the next scheduled fire unless
  that policy is explicit.
- Create and dispose one dependency-injection scope per execution, propagate shutdown
  cancellation, honor interruptibility and define drain versus abandon behavior.
  Version stable durable job/trigger names, serialized payloads and handler types so
  rolling deployment cannot orphan or reinterpret persisted work.
- Emit a scheduling witness with computed fire times, acquisition/store state, node,
  misfire/recovery decision, execution/idempotency key, retry and completion. Tests
  cover DST gaps/overlaps, clock jumps, downtime backlog, thread starvation, node
  death, split brain, duplicate recovery, concurrent fire, store outage, schema/type
  upgrades, shutdown and crash-after-effect.

Evidence: Quartz.NET's [troubleshooting guidance](https://www.quartz-scheduler.net/documentation/troubleshooting.html)
defines misfires and the trigger-specific fire-now, skip and reschedule choices. Its
[job-store documentation](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/job-stores.html)
distinguishes volatile in-memory schedules from persistent database-backed state,
and the [configuration reference](https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html)
documents cluster check-ins, locking, misfire thresholds and concurrency controls
that determine recovery behavior.

## P1 - Prove metrics instruments, cardinality, and collection semantics

Expand the existing treatment of metric writes as impure calls into a semantic model
for .NET and OpenTelemetry-style instruments, measurements, tag sets, callbacks,
aggregation and export. Prove what an observation means and bound its resource cost
without turning telemetry into evidence that a business effect occurred exactly once.

Acceptance criteria:

- Represent meter provider/name/version, instrument identity and kind, numeric type,
  name/unit/description, measurement value and timestamp, canonical tag set, callback
  or recording context, view/aggregation, collection cycle and export/drop outcome.
- Resolve runtime and telemetry SDK versions, listeners/providers, views, temporality,
  histogram boundaries, collection interval, exporter and resource attributes into
  an effective pipeline. An inactive listener or changed view cannot retain a claim
  derived from a previously configured aggregation.
- Enforce instrument contracts: counters are monotonic nonnegative increments,
  up/down counters permit signed deltas, gauges report current values and histograms
  record distributions with compatible units and numeric domains. Reject unsupported
  types, overflow, `NaN`, infinities and unit/name conflicts for the same identity.
- Distinguish synchronous recordings from observable instruments whose callbacks run
  during collection. Make callback lifetime and state access thread-safe, bounded and
  nonblocking; handle exceptions explicitly and prohibit reentrant collection, I/O or
  locks that can delay or prevent the other sequential observable callbacks.
- Canonicalize tags by key and typed value and define required/optional dimensions.
  Enforce a reviewed cardinality budget before recording/export, aggregate or drop
  unbounded user/request/object IDs and keep secrets, tokens, full URLs, email
  addresses and other sensitive values out of labels.
- Model view selection, attribute filtering, sum/last-value/histogram aggregation,
  cumulative versus delta temporality, reset and process restart. Histogram bucket
  boundaries and exemplar/sampling choices are versioned interpretation data, not
  interchangeable representations.
- Bound collection, reader queues, batching, retry and exporter shutdown/flush.
  Backpressure, queue overflow, exporter failure, cancellation or process crash yields
  explicit missing/dropped telemetry rather than blocking the application or proving
  that an absent measurement means an event did not occur.
- Tie measurements to tracing/logging context and stable operation IDs where useful,
  but state the epistemic boundary: duplicate, reordered, sampled or dropped metrics
  cannot establish transaction commit, exactly-once processing, billing or security
  authorization without the underlying durable witness.
- Emit a metrics witness with instrument resolution, value validation, canonical tags
  and cardinality decision, callback/collection cycle, aggregation and export/drop
  state. Tests cover duplicate instrument conflicts, negative counters, numeric edge
  cases, slow/throwing/reentrant callbacks, tag explosions, view changes, temporality
  resets, queue saturation, exporter outage and shutdown loss.

Evidence: Microsoft's [.NET metrics instrumentation guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
defines counter, histogram and observable instrument behavior, warns that observable
callbacks execute sequentially during collection, and explains that large tag-value
cardinality can cause collection tools to allocate substantial storage. The current
SharpProof implementation recognizes selected `Meter.Create*` and `Counter<T>.Add`
calls as impure but does not model the instrument, callback, aggregation, cardinality
or export semantics above.

## P1 - Prove secret-store versions, rotation, and cache freshness

Add provider-profiled secret-vault semantics for versioned retrieval, authorization,
in-memory caching, rotation, disablement, deletion and recovery. Prove which secret
version a consumer used and for how long instead of treating a versionless name or a
successful configuration bind as a permanently valid credential.

Acceptance criteria:

- Represent provider/vault and cloud, secret name and immutable version, versionless
  alias, enabled/not-before/expiry state, content type and nonsecret metadata, access
  identity and operation, cache entry/version/age, rotation event and retrieval
  outcome without retaining the secret value.
- Resolve SDK/provider version, vault endpoint, credential and tenant, authorization
  model, retry policy, network boundary, soft-delete/recovery and purge-protection
  settings, secret-reference resolver and refresh interval into an effective profile.
- Bind every read to the intended vault, tenant, environment and secret name. Reject
  user-controlled vault URLs or names, cross-environment fallback and a local/user-
  secret provider overriding a protected production reference through configuration
  precedence.
- Distinguish a version-pinned read from a versionless latest-version read. Record the
  returned immutable version and attributes; a cached version remains that version
  after rotation, while a decrypt/verify operation that needs historical material
  cannot silently switch to the latest key or secret.
- Validate enabled, activation and expiry state against bounded clock skew and define
  behavior for absent, disabled, expired, deleted, forbidden, throttled and transient
  results. A last-known-good value is used only under an explicit bounded stale policy,
  never as an unreported indefinite fallback after revocation.
- Cache only in protected process memory, key entries by vault/tenant/name/version and
  coordinate refresh to prevent a fleet-wide thundering herd. Bound TTL and jitter by
  the rotation/revocation objective; invalidate or refresh consumers that otherwise
  capture a secret in singleton options, pools or long-lived connections.
- Model zero-downtime rotation as a protocol with new target credential, new vault
  version, propagation/refresh, validation, cutover and old-version revocation. Dual
  credentials and overlap windows have explicit ownership and deadlines; partial
  rotation or rollback cannot leave an unknown active credential.
- Reuse thread-safe SDK clients and bound retries, timeouts and concurrency under
  service throttling. Keep secret values, access tokens, connection strings, private
  keys and resolver exceptions out of logs, metrics, traces, URLs and proof witnesses.
- Emit a redacted secret witness with vault identity, requested/resolved version,
  attributes, authorization premise, cache hit/age, refresh/rotation state and result.
  Tests cover wrong vault/tenant, pinned versus latest versions, stale singleton use,
  expiry, disable/delete/recover, rotation races, dual-key cutover, throttling, cache
  stampedes, provider fallback and diagnostic leakage.

Evidence: Microsoft's [Key Vault autorotation overview](https://learn.microsoft.com/en-us/azure/key-vault/general/autorotation)
defines distinct version-creating rotation behavior for keys, secrets and certificates
and requires dependent systems to handle the new versions. The [.NET secret-client
documentation](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/security.keyvault.secrets-readme?view=azure-dotnet)
exposes create, retrieve, update, delete, purge, backup, restore and version operations,
while the [App Configuration provider guidance](https://learn.microsoft.com/en-us/azure/azure-app-configuration/reference-dotnet-provider)
documents that secret refresh has an interval independent of configuration refresh.

## P1 - Prove feature-flag evaluation and rollout consistency

Add library-profiled feature-management semantics for dynamic definitions, filters,
targeting, percentage allocation, variants, request snapshots and kill switches.
Prove which definition and subject selected a behavior so live reload cannot split one
operation across incompatible feature states or silently change authorization.

Acceptance criteria:

- Represent flag/variant identity, definition version and source, global enabled
  state, ordered filters and requirement mode, parameters, evaluation subject and
  groups, time window, percentage seed/bucket, selected variant/configuration,
  snapshot scope, fallback and decision outcome with provenance.
- Resolve feature-management and provider versions, configuration labels/selectors,
  refresh and cache settings, custom filter aliases/types, dependency-injection
  lifetime, targeting accessor and telemetry options into the effective definition.
  Missing, malformed, duplicate or unknown filters and variants fail by declared policy.
- Evaluate filters with the library's exact `Any`/`All`, negation, contextual and
  asynchronous semantics. Propagate cancellation and bound filter I/O; filter order,
  exceptions or side effects cannot accidentally become a hidden authorization path.
- Canonicalize stable subject and group identity before targeting. Percentage and
  variant allocation use a declared algorithm/seed and stable input so the same
  subject remains sticky where promised; anonymous or missing identity follows an
  explicit policy rather than receiving an unstable random bucket on every call.
- Validate variant names, typed configuration, allocation ranges, overrides and
  enabled/disabled defaults. User override precedence and a variant status override
  are visible, and the kill switch selects the reviewed disabled behavior even when
  a previous target or percentage assignment selected another variant.
- Snapshot the first decision/definition for the full request, message, workflow or
  transaction when coherent behavior is required. A live reload may affect later
  scopes but cannot make validation run under one variant and mutation, audit or
  compensation run under another.
- Treat flags as operational policy, not a security boundary: protected endpoints,
  tenant isolation and authorization remain enforced when configuration is stale,
  unavailable or attacker-readable. Client-side definitions exclude sensitive user
  lists, secrets and internal rollout metadata.
- Bound refresh staleness, provider outage and last-known-good use; support staged
  rollout, telemetry validation and deterministic rollback with a versioned definition.
  Retired flags have owners and expiry dates so dead branches and incompatible data
  formats do not persist indefinitely.
- Emit an evaluation witness with definition digest/version, context provenance,
  filters, bucket/seed, variant, snapshot and fallback without personal data. Tests
  cover mid-request reload, missing identity, group/user precedence, allocation edges,
  clock windows, unknown filters, malformed variants, provider outage, kill switch,
  stale clients, flag removal and telemetry cardinality.

Evidence: Microsoft's [.NET feature-management reference](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference)
defines built-in filters, contextual targeting, variants and percentile allocation,
and provides `IVariantFeatureManagerSnapshot` because ordinary evaluations can change
during one request after configuration refresh. The [variant management guidance](https://learn.microsoft.com/en-us/azure/azure-app-configuration/manage-feature-flags)
documents deterministic seeds, user/group override precedence, defaults and traffic
allocation behavior.

## P1 - Prove database migrations and rolling-schema compatibility

Add provider- and ORM-profiled semantics for migration histories, generated scripts,
schema locks, transactional DDL, data backfills and multi-version application rollout.
Prove that each database moves between known compatible states without concurrent
startup migration, destructive drift or an unbounded half-applied change.

Acceptance criteria:

- Represent database/provider and schema identity, model/snapshot digest, ordered
  migration IDs and product version, history-table state, pending operations, script
  or bundle identity, lock owner, transaction/batch boundaries, application version
  compatibility and apply/rollback outcome.
- Resolve EF/provider/tool versions, design-time model and startup assembly, history
  table/schema, generated SQL, execution strategy, command timeout, migration lock,
  transactional-DDL support and deployment topology into an exact migration plan.
- Compare code migrations, model snapshot and live history without assuming history
  proves the physical schema. Detect missing/reordered/rewritten applied migrations,
  divergent branches, pending model changes and provider-generated destructive or
  data-loss operations before deployment.
- Bind reviewed SQL script or migration-bundle bytes to the intended source/target
  migration range, provider, database/tenant and release artifact. Idempotent scripts
  condition each step on trusted history but do not make a non-idempotent backfill or
  manually drifted schema safe.
- Run production changes through one authorized deployment actor with bounded locking
  and least privilege. Do not let every application replica call `Migrate` at startup;
  lock timeout, crash, cancellation and lost connectivity produce a recoverable
  unknown state that is re-inspected before retry.
- Model provider-specific transactional and nontransactional DDL, implicit commits,
  statement batching and online-index behavior. Partial application records exactly
  which operations committed, and retry/rollback plans account for irreversible data
  loss and long-running locks rather than promising universal transaction rollback.
- Prove expand-and-contract compatibility across every concurrently deployed binary:
  add nullable/additive structures, dual-read/write and backfill, switch readers, then
  remove old structures only after old versions and jobs are drained. Renames, type
  changes and new constraints require staged compatibility evidence.
- Bound and checkpoint backfills by stable keys, preserve concurrent writes, throttle
  load and make batches idempotent. Validate data before `NOT NULL`, uniqueness or
  foreign-key enforcement and distinguish schema completion from data convergence.
- Emit a migration witness with model/history/script digests, database target, lock,
  statements and transactions, compatibility matrix, backfill checkpoints and result.
  Tests cover concurrent runners, wrong database, modified history, partial DDL,
  timeout-after-commit, rolling old/new binaries, dual-write gaps, failed constraints,
  rollback limits, provider drift and tenant-by-tenant interruption.

Evidence: Microsoft's [EF Core migration deployment guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
recommends reviewed production SQL, defines history-based idempotent scripts and
bundles, and warns about runtime migration concurrency, elevated privileges and
uninspected changes. The [migration management guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing)
requires reviewing generated migrations and warns against removing migrations already
applied to production databases.

## P1 - Model Kubernetes reconciliation, watches, and finalizers

Add Kubernetes-profiled semantics for list/watch caches, work queues, optimistic
resource updates, reconciliation, status conditions, ownership and deletion
finalizers. Prove convergence under duplicate, stale and missing events instead of
treating a watch callback as an exactly-once command stream.

Acceptance criteria:

- Represent cluster/API-server and group-version-kind, namespace/name/UID, spec and
  status digests, generation/observed generation, resource version, labels/selectors,
  owner references, finalizers/deletion timestamp, watch/list cursor, reconcile key,
  retry and external-effect state.
- Resolve Kubernetes and client-library versions, discovery/API conversion, cache and
  consistency mode, selectors, list paging, watch bookmarks/timeouts, work-queue
  coalescing/rate limits, update/patch strategy, field manager, RBAC and leader-
  election profile into the controller model.
- Establish initial cache state with a consistent list or supported streaming-list
  protocol, then watch from the returned collection resource version. Interpret
  object and collection resource versions by API semantics, not as numeric clocks or
  cross-resource ordering tokens.
- Handle watch disconnects, duplicate/reordered delivery, bookmarks, error events and
  expired history. On `410 Gone`, discard the invalid cursor, relist to a coherent
  state and resume; no event receipt or quiet watch proves that cached state is fresh.
- Make reconciliation level-based and idempotent: recompute desired actions from the
  latest object and observed external state, tolerate repeated keys and converge after
  missed intermediate events. Bind external creates/updates to the object UID and a
  stable operation key so delete/recreate with the same name cannot adopt stale work.
- Use current `resourceVersion` preconditions or the exact patch/apply conflict model
  for mutations. On `409 Conflict`, refetch and recompute rather than replaying a stale
  full-object update that overwrites another controller's fields; keep spec, status
  and scale subresource permissions and ownership distinct.
- Report status with observed generation and structured conditions whose type,
  status, reason, transition time and message follow stable merge rules. Do not claim
  readiness for a newer spec from success recorded against an older generation, and
  avoid update loops caused by semantically unchanged status writes.
- Add a qualified finalizer before creating an external dependent. After
  `deletionTimestamp`, stop normal creation, make cleanup repeatable, record partial
  progress and remove only the controller's finalizer after durable cleanup. Bound
  retries and surface stuck deletion; manual removal is an explicit leak decision.
- Validate owner UID, scope and `controller` role before adopting or deleting children;
  labels alone do not establish ownership and invalid cross-namespace references do
  not protect dependents. Compose active-replica behavior with fenced leader leases,
  shutdown cancellation, API throttling, backoff and least-privilege service accounts.
- Emit a reconciliation witness with cache/list/watch versions, triggering key,
  object UID/generation, desired/observed diff, API conflicts, effects, conditions and
  finalizer state. Tests cover stale cache rewind, `410` relist, duplicate events,
  name reuse, patch conflicts, spec/status races, leader failover, throttling, partial
  external creation, deletion during create and permanently failing cleanup.

Evidence: Kubernetes' [controller documentation](https://kubernetes.io/docs/concepts/architecture/controller/)
defines controllers as loops that move current state toward desired state. The
[API concepts reference](https://kubernetes.io/docs/reference/using-api/api-concepts/)
defines list/watch resource-version semantics and requires relisting when retained
history is lost with `410 Gone`; the [finalizer contract](https://kubernetes.io/docs/concepts/overview/working-with-objects/finalizers/)
defines deletion timestamps, `202 Accepted` and removal only after cleanup completes.

## P1 - Prove SSH host identity and SFTP publication

Add protocol- and library-profiled SSH/SFTP semantics for host authentication,
algorithm negotiation, user authentication, channels, remote paths and partial file
transfer. Prove the remote endpoint and atomic publication boundary instead of
equating encryption or `UploadFile` completion with the intended host and final file.

Acceptance criteria:

- Represent logical host/port and resolved endpoint, server host key/certificate and
  fingerprint, trust source and rotation state, negotiated key exchange/host-key/
  cipher/MAC/compression algorithms, session and rekey, user/authentication methods,
  SFTP version/extensions, remote path, file attributes, offsets and transfer result.
- Resolve SSH.NET/protocol and platform versions, DNS/proxy/jump-host route, host-key
  policy, allowed algorithms, timeouts/keepalives/rekey, authentication order, channel
  limits, path encoding and server-advertised SFTP extensions into one connection
  profile.
- Authenticate the server key against a pinned fingerprint, qualified known-hosts
  entry or approved host-certificate authority before exposing user credentials or
  accepting the channel. Bind trust to the canonical host and port; unknown, changed,
  revoked, wrong-algorithm or wrong-CA keys fail closed unless an auditable rotation
  updates the trust record.
- Permit trust on first use only under an explicit enrollment policy with an
  independently verified first fingerprint and protected atomic persistence.
  Automatically setting `CanTrust` or accepting any presented key does not prove host
  identity and leaves encryption vulnerable to an active intermediary.
- Enforce a current algorithm policy for key exchange, host signatures, ciphers and
  integrity in both directions. Reject no-common-algorithm and downgrade outcomes;
  record the negotiated suite and host-key type rather than inferring strength from
  the client defaults or the `ssh` scheme.
- Model password, public-key/certificate, agent and keyboard-interactive methods,
  including required multi-factor sequencing. Protect private keys and passphrases,
  constrain agent forwarding and callbacks, and distinguish server authentication
  from later user authentication and per-channel authorization.
- Normalize remote paths using the server's SFTP semantics, not local Windows rules.
  Constrain absolute/relative roots, `..`, symlinks, case and encoding; bind overwrite,
  create/truncate, permissions, owner and expected preexisting-file identity to an
  explicit policy before transferring bytes.
- Track partial reads/writes, offsets, lengths, stream ownership, cancellation,
  disconnect and ambiguous completion. Validate size and cryptographic digest where
  the workflow requires integrity; resume only against the exact same remote temp-file
  identity and verified prefix rather than appending to an unknown file.
- Publish uploads through a unique restricted temporary path and a checked final
  rename only when the server's extension/filesystem contract supplies the required
  overwrite and atomicity semantics. Otherwise expose a non-atomic result. Downloads
  likewise verify a local temporary file before atomic local publication and clean
  bounded orphaned temps without deleting another transfer's file.
- Keep command execution and interactive shells distinct from SFTP: shell quoting,
  environment, exit status, stdout/stderr and channel closure need their own proof.
  Emit a redacted session/transfer witness and test hostile host keys, rotation,
  algorithm downgrade, auth fallback, symlink escape, partial overwrite, reconnect,
  resume mismatch, rename collision, cancellation, server quota and secret leakage.

Evidence: [RFC 4253](https://www.rfc-editor.org/rfc/rfc4253.html) defines SSH transport
algorithm negotiation and requires the client to verify the server host key, warning
that acceptance without verification is insecure against active attacks. SSH.NET's
[official examples](https://sshnet.github.io/SSH.NET/examples.html) expose explicit
fingerprint/CA verification through `HostKeyReceived`, while its [`SftpClient` API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.SftpClient.html)
documents stream uploads, overwrite choice, cancellation and transfer exceptions that
must be represented rather than collapsed into a boolean success.

## P1 - Prove WebAuthn registration and assertion ceremonies

Add standards-profiled WebAuthn/passkey semantics for registration, authentication,
attestation, discoverable credentials, backup state and account recovery. Prove each
relying-party ceremony from its stored challenge through exact client/authenticator
bytes and atomic credential state instead of treating a valid signature as sufficient.

Acceptance criteria:

- Represent relying-party ID and allowed origin/top-origin set, ceremony type and
  session/account binding, challenge and expiry/consumption, user ID/handle, credential
  ID and public key/algorithm, attestation format/trust, AAGUID, extensions, transports,
  authenticator flags, signature counter, backup eligibility/state and outcome.
- Resolve WebAuthn/FIDO library and specification version, canonical RP ID, deployment
  origins, proxy/forwarded-host policy, HTTPS exception, user-verification and resident-
  key requirements, accepted algorithms, attestation policy, metadata trust anchors
  and timeout into a versioned ceremony profile.
- Generate challenges server-side with cryptographic entropy, bind them to ceremony,
  RP, account/session and intended action, expire them promptly and consume them once
  under concurrency. Reject client-selected, predictable, replayed, cross-tab/account
  or registration-versus-assertion challenge substitution.
- Parse `clientDataJSON` and authenticator data as exact protocol bytes. Verify type,
  challenge encoding/value, exact allowed origin and expected top-origin/cross-origin
  state, `rpIdHash`, reserved bits, user-presence and required user-verification flags,
  extension outputs and allowed algorithm before accepting the signature.
- During registration, validate attested credential data, unique credential ID, user
  binding and requested options. Apply explicit none/self/basic/enterprise attestation
  trust policy and metadata/revocation checks; attestation success does not by itself
  authorize enrollment or prove the authenticated account intended the registration.
- During assertion, resolve the credential by RP and credential ID, validate discoverable
  `userHandle` against its stored account, verify the signature over authenticator data
  plus the hash of the returned client data and atomically update security state only
  after every check succeeds.
- Interpret counters and backup flags according to credential capabilities. A zero or
  non-increasing counter is not universally a clone proof for synced passkeys; an
  unexpected regression, backup-eligibility change or invalid BE/BS combination enters
  a declared risk/recovery policy without locking out all legitimate multi-device use.
- Make credential enrollment, rename, disable, delete and counter/backup update atomic
  with account security-stamp and audit state. Require recent strong authentication for
  credential management, bound failed-ceremony rate limits and safe recovery with no
  weaker account-takeover path than the passkey it replaces.
- Emit a redacted ceremony witness with profile, challenge identity/state, RP/origin,
  credential/public-key identity, parsed flags, verification steps and atomic update.
  Tests cover challenge replay/races, origin and RP confusion, cross-origin frames,
  malformed CBOR/JSON, algorithm substitution, missing UV, duplicate credentials,
  discoverable-user mismatch, counter anomalies, backup transitions and recovery abuse.

Evidence: The W3C [Web Authentication Level 3 specification](https://www.w3.org/TR/webauthn-3/)
defines RP-scoped public-key credentials, randomized server challenges, origin and
`rpIdHash` validation, user-presence/verification and backup flags, signature counters,
attestation and the ordered registration/assertion verification ceremonies.

## P1 - Prove tenant-context provenance and isolation

Add end-to-end multitenancy semantics that resolve an authenticated tenant once and
carry that identity through authorization, data access, caches, messages, jobs,
storage and telemetry. Prove every shared-resource operation is partitioned for the
same tenant instead of relying on scattered predicates or ambient strings.

Acceptance criteria:

- Represent canonical tenant ID and deployment/stamp, resolution source and trust,
  authenticated subject memberships, active tenant selection, isolation model per
  resource, immutable operation context, resource partition/key and cross-tenant
  administrative purpose with provenance.
- Resolve host/path/header/token candidates, issuer-to-tenant mappings, custom domains,
  control-plane routing, configuration and deployment version into one tenant before
  protected model binding or effects. Reject conflicts, unknown tenants and untrusted
  client claims rather than choosing by precedence or falling back to a default tenant.
- Bind the active tenant to a subject whose validated identity authorizes membership
  and to the requested resource's stored ownership. A route, query, cookie, JWT claim
  or object ID that merely names another tenant cannot switch context; support staff
  impersonation and cross-tenant operations require separate explicit capabilities,
  reason, scope, expiry and audit.
- Propagate an immutable typed context through async calls and recognized execution-
  context flow, but capture it explicitly in queued messages, workflows, scheduled
  jobs and callbacks. Clear or restore ambient context at scope exit and thread reuse;
  suppressed flow, parallel fan-out and missing context fail closed for tenant effects.
- Prove database choice/schema and every shared-table read, insert, update, delete,
  join, raw SQL, bulk operation and relationship use the same tenant. Global filters,
  row-level security and connection/session context are defense layers with modeled
  bypasses; `IgnoreQueryFilters`, admin contexts and pooled connections cannot retain
  or omit a previous tenant silently.
- Partition cache keys, object paths, search indexes, broker topics/partitions, event
  streams, idempotency records, rate limits and output-cache variants by canonical
  tenant wherever state is shared. Normalize before key construction and prevent a
  delimiter, case, alias or hash collision from joining tenant namespaces.
- Bind per-tenant credentials, encryption keys, configuration, feature flags, outbound
  destinations and service clients to the same context. Reset tenant-specific headers,
  database sessions, scopes and mutable client state before returning pooled objects;
  singleton capture cannot freeze the first tenant for later requests.
- Enforce per-tenant quotas, concurrency and failure bulkheads without using telemetry
  labels that expose tenant identity or create unbounded cardinality. Deprovisioning
  inventories and drains all partitions, jobs, keys and retained data under a declared
  deletion/legal-hold policy without affecting another tenant.
- Emit a tenant-flow witness from ingress resolution through every partitioned effect,
  context transfer and cleanup. Tests cover conflicting selectors, forged claims,
  missing async context, pooled-state leakage, filter/raw-SQL bypass, cache and path
  collisions, cross-tenant message replay, admin impersonation, noisy neighbors and
  tenant deletion while work remains in flight.

Evidence: Microsoft's [multitenant tenancy-model guidance](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/considerations/tenancy-models)
defines isolation as a per-tier spectrum across shared application, messaging and data
resources. EF Core's [global query-filter documentation](https://learn.microsoft.com/en-us/ef/core/querying/filters)
requires the current tenant on the context and documents filter combination/disablement
behavior, while the [storage and data guidance](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/storage-data)
describes shared-row, database and resource isolation choices including row-level
security.

## P1 - Model GraphQL validation, resolver effects, and query cost

Add GraphQL- and Hot Chocolate-profiled semantics for schema construction, document
validation, variables, directives, field authorization, resolver scheduling, batching,
null propagation, subscriptions and resource cost. Prove the selected operation and
its partial result/effects rather than treating the HTTP status as request success.

Acceptance criteria:

- Represent schema/executor version and digest, endpoint/transport, document bytes or
  trusted operation ID/hash, operation name/type, fragments/directives, variables and
  coerced values, selection/response paths, field/resolver identities, authorization,
  estimated/actual cost, data/errors/extensions and subscription lifecycle.
- Resolve Hot Chocolate and GraphQL specification versions, schema registration and
  middleware order, endpoint overrides, scalar/coercion conventions, execution options,
  persisted-document store, cost/depth/list limits, introspection/tooling and transport
  settings into the effective executable schema.
- Parse and validate the complete document before execution: choose exactly one named
  operation where required, expand fragments with cycle and type-condition checks,
  validate selections/arguments/directives and coerce literal and variable input with
  the exact absent-versus-null, default, list and non-null rules.
- Bind trusted/persisted operation IDs to exact canonical document bytes, operation
  name, schema/environment version and authorization policy. Hash lookup or automatic
  persistence cannot let an unauthenticated caller register arbitrary documents,
  exploit collisions or execute a document validated against an incompatible schema.
- Enforce authentication and resource-aware authorization at every reachable protected
  field/type under the actual GraphQL middleware and attribute, including aliases,
  fragments, interface/union runtime types and introspection. A parent check does not
  automatically authorize nested resources, and redacted fields must not leak through
  errors, type metadata or timing-sensitive batching keys.
- Calculate bounded cost before execution across fragment expansion, repeated aliases,
  recursion/depth, list multipliers, pagination arguments and custom resolver weights.
  Bound document/variable sizes and parser work; compare static assumptions with runtime
  fan-out so DataLoader prevents N+1 I/O without turning an attacker-controlled batch
  into an unbounded query or cross-tenant cache.
- Model query field parallelism, serial top-level mutation fields and nested resolver
  callbacks with cancellation, DI scopes, database contexts, transactions and effects.
  A mutation's earlier committed effect is not rolled back because a later field or
  non-null result fails, and resolver retries require ordinary idempotency evidence.
- Implement response completion and non-null error bubbling exactly: field execution
  failures may yield partial `data` plus path-qualified `errors`, while request parse,
  validation or coercion errors halt execution with no data. HTTP 200 alone proves
  neither complete data nor successful mutation effects.
- Model subscription authentication/initialization, selected event stream, per-event
  re-execution, filtering, ordering, backpressure, cancellation, reconnect and schema
  compatibility. Emit a GraphQL witness and test alias cost bypass, fragment cycles,
  null/default coercion, field-auth gaps, DataLoader tenant leaks, partial mutation,
  null bubbling, stale persisted documents, introspection exposure and slow subscribers.

Evidence: The [GraphQL September 2025 specification](https://spec.graphql.org/September2025/)
defines document validation, input coercion, execution ordering, subscriptions and the
partial-data/non-null error propagation contract. Hot Chocolate's [official overview](https://chillicream.com/docs/hotchocolate)
documents resolver, DataLoader, cost-analysis and trusted-document behavior, while its
[authorization guidance](https://chillicream.com/docs/hotchocolate/security/authorization)
shows that GraphQL-specific field/type middleware and attributes determine protection.

## P1 - Prove event-stream concurrency, replay, and projection checkpoints

Add event-store-profiled semantics for immutable aggregate streams, conditional
appends, snapshots, schema evolution, subscriptions and materialized projections.
Prove state reconstruction and exactly which events became durable despite concurrent
commands, ambiguous writes, duplicate delivery and projection rebuilds.

Acceptance criteria:

- Represent store/database and tenant, stream/aggregate identity, event ID/type and
  schema version, payload/metadata digest, stream revision and global/log position,
  expected-revision condition, append batch/commit, snapshot revision, subscription
  cursor, projection checkpoint and read-model effect/outcome.
- Resolve client/server and protocol versions, serializer/upcaster registry, stream-
  naming and metadata policy, consistency/read preference, append idempotency window,
  retention/tombstone rules, subscription mode, checkpoint cadence and projection
  ownership into a versioned event-store profile.
- Rehydrate an aggregate from a trusted snapshot plus every subsequent stream event in
  revision order, checking aggregate/tenant identity and contiguous revision. Bind the
  snapshot to stream, exact included revision, state/schema digest and upcaster set;
  stale or corrupt snapshots are discarded rather than merged with an arbitrary tail.
- Append all events from one accepted command atomically with the exact expected stream
  revision or no-stream/existence condition. `Any` or an equivalent unconditional
  append is a declared weaker policy; on a conflict, reload, reevaluate business rules
  and produce new events instead of retrying the stale decision unchanged.
- Assign stable high-entropy event and command IDs before sending. After timeout,
  cancellation or disconnect, query/deduplicate by those identities and observed
  revision before retry; the same batch is idempotent only under the provider's exact
  contract and cannot be reconstructed with new event IDs.
- Treat event bytes as immutable facts. Evolve with explicit event types/versions and
  deterministic, side-effect-free upcasters whose chain is complete for all retained
  history. Do not rewrite production history casually or reinterpret old payloads
  using current culture, configuration, time or mutable reference data.
- Distinguish per-stream revision from global commit/log order and from timestamps.
  Define causation/correlation and ordering needed across aggregates explicitly; a
  projection cannot infer a total business order from wall-clock fields or arrival at
  different replicas.
- Process subscriptions and projections as at-least-once unless stronger evidence is
  present. Atomically couple each read-model effect with its event ID/position and
  checkpoint, or use an idempotent inbox, so a crash after the effect but before the
  checkpoint cannot duplicate balances, notifications or downstream publications.
- Version, pause, reset and rebuild projections from a declared start position into a
  new output namespace, verify parity and switch atomically. Bound lag, poison-event
  retries and parked/dead-letter state; retention/truncation cannot remove events needed
  for replay, audit or a new projection without an explicit irreversible boundary.
- Emit an event-stream witness with input state/revision, command decision, event batch
  IDs/digests, expected/committed positions, replay/upcasters, projection effects and
  checkpoint. Tests cover concurrent writers, ambiguous append, duplicate event IDs,
  snapshot mismatch, missing/upcaster drift, out-of-order assumptions, poison events,
  crash-before-checkpoint, rebuild cutover, retention gaps and tenant stream collisions.

Evidence: Microsoft's [event-sourcing pattern guidance](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)
defines append-only events, optimistic concurrency, snapshots, idempotent handlers,
eventual projections and schema-evolution tradeoffs. KurrentDB's [append contract](https://docs.kurrent.io/clients/python/v1.2/appending-events)
exposes atomic/idempotent appends with explicit current-version conditions, and its
[projection documentation](https://docs.kurrent.io/server/latest/features/projections/)
describes checkpoints, replay from reset and exclusive ownership of emitted streams.

## P1 - Prove pagination cursors, ordering, and snapshot semantics

Add protocol- and query-provider-profiled pagination semantics for bounded page sizes,
opaque continuation tokens, stable ordering, authorization and mutable collections.
Prove whether traversal is a live scan or one coherent snapshot and prevent duplicates,
gaps, loops or data leaks caused by forged/stale cursors and changing query arguments.

Acceptance criteria:

- Represent collection/provider and tenant, normalized filter/search/projection,
  deterministic sort keys and direction, authorization context, consistency/snapshot
  identity, requested/effective page size, input token/cursor and expiry, anchor values,
  returned item identities, next token and end-of-collection decision.
- Resolve API/library/provider version, offset/keyset/native-continuation strategy,
  default/maximum page size, null/collation/culture ordering, snapshot support, token
  protection/version/lifetime and backend query translation into one pagination profile.
- Require a total deterministic order with an immutable unique tie-breaker. Encode all
  ordered anchor components, direction and null semantics; ordering by a nonunique or
  mutable value alone cannot prove that equal/moved rows appear once across page
  boundaries.
- Prefer keyset or provider continuation for deep mutable scans and model offset cost
  and shift behavior explicitly. Define live traversal, which may observe concurrent
  inserts/deletes/updates under documented duplicate/gap rules, separately from
  snapshot traversal pinned to a database snapshot/version with bounded retention.
- Make tokens URL-safe and opaque to clients, integrity-protected and optionally
  confidential when anchors reveal data. Bind token state to provider/collection,
  tenant/subject or authorization version, filter, search, projection, complete order,
  direction, snapshot, token schema and expiry; reject any changed argument except a
  page-size change the protocol explicitly permits.
- Treat a token only as continuation state, never authorization. Reauthorize every
  item/page and prevent authorization or tenant changes from exposing data positioned
  by an older cursor; a signed token from one endpoint, environment or user cannot be
  replayed against another.
- Enforce positive bounded effective sizes and resource budgets. A provider may return
  fewer or even zero items before the end, so continue exactly while a valid next token
  exists; an empty token alone signals completion under protocols that define it, and
  a repeated/nonadvancing token triggers a bounded loop failure.
- Preserve provider continuation tokens as opaque bytes when composing services rather
  than decoding/reconstructing unsupported internals. Normalize generated next links
  to the trusted public origin and endpoint, encode once and retain all query semantics
  without reflecting attacker-controlled hosts or leaking signed state to logs.
- Model async page enumeration, cancellation, partial consumption, retries, rate limits
  and disposal. Retry the same page token idempotently, deduplicate only by stable item
  identity under an explicit policy and report token expiry/stale snapshot so callers
  restart intentionally instead of silently splicing a new scan into the old one.
- Emit a traversal witness with query/order digest, token protection/version, snapshot,
  anchors, item identities, next/end decision and consistency anomalies. Tests cover
  duplicate sort keys, inserts/deletes between pages, mutable keys, null/culture order,
  forged/cross-tenant/expired tokens, argument changes, zero short pages, token loops,
  deep offsets, snapshot expiry, next-link host injection and cancellation mid-scan.

Evidence: Google's approved [AIP-158 pagination standard](https://google.aip.dev/158)
requires bounded page sizes, opaque continuation tokens, unchanged subsequent request
arguments, per-request authorization and an empty token as the end signal, and warns
that adding pagination later is behaviorally incompatible. Hot Chocolate's [pagination
guidance](https://chillicream.com/docs/hotchocolate/fetching-data/pagination) explains
that offset traversal can duplicate or skip changing data and defines opaque cursor,
maximum-page-size and null-ordering behavior for .NET GraphQL providers.
