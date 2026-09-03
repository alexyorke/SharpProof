# Coverage and limits

This document is the authoritative inventory of SharpProof 1.0's implemented
product surface. [SEMANTICS.md](../SEMANTICS.md) remains normative: if a
semantic rule here conflicts with it, `SEMANTICS.md` wins.

SharpProof admits code through explicit gates and then analyzes it
conservatively. Admission does not guarantee a proof. Missing models,
unsupported expressions, approximate facts, and exhausted budgets remain
`Unknown` or silent abstentions.

## Product capability matrix

| Surface | Runs where | Implemented behavior | Current boundary |
|---|---|---|---|
| Effect contracts | Analyzer with `SharpProofFeatures=effects` or `all`; independent replay in the opt-in container worker | Runs a bounded acyclic scalar CFG pass, then computes conservative may summaries for reads, writes, allocation, capabilities, exceptions, termination, and completeness; checks `[EnforcePure]`, `[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`, `[AllowedExceptions]`, and `[EffectContract]`; emits one accountable worker claim per selected attribute; independently replays unconditional definite managed object/array allocation, exact framework explicit-throw, empty-`lock`, and exact-`Monitor` events | Impossible refined branches are excluded. A loop disables scalar refinement but the conservative all-block scan can still prove effect absence. The worker authenticates the selected effect, capability, and exception constraints and derives each replayed witness independently. Fresh allocation remains observably pure; receiver-field, user-exception, conditional, and may-only candidates remain typed `Unknown` |
| Call-site preconditions | Analyzer with `SharpProofFeatures=contracts` or `all` | Binds source `Contract.Requires` clauses and closed parameter attributes with compiler symbols for ordinary calls and object creation; follows executable local-function, lambda, and anonymous-method child CFGs exactly once; combines exact IR replay with compilation-scoped Boolean, nullness, interval, cardinality, explicitly trusted return-annotation, approved API-spec result, and effect facts at definite call sites | Unknown or captured values, possible throws, cycles, quoted expression-tree lambdas, and exhausted analysis budgets do not become violations or proofs; unsupported explicitly selected methods report SP0047 |
| Postconditions | Optional container worker with `SharpProofFeatures=contracts` or `all`; strict enables the worker by default | Manifests `Contract.Ensures` and return attributes, including directly owned local-function, lambda, anonymous-method, and top-level claims, then proves admitted bounded obligations over normal-return paths with Boolean logic, bounded integer comparisons, checked `long` arithmetic, and replay-gated counterexamples | The additional callable forms are currently visible as `UnsupportedCallable`; `effects` excludes postcondition claims; this is bounded `Ensures` verification, not arbitrary deep, recursive, looping, heap, or sequence verification |
| Relational callees | Build-time compiler collector plus the container worker | Infers quantifier-free relations for direct acyclic static scalar source methods and exact implementation IL, or imports an explicitly enabled schema-1 audited pack; composes every relation into the caller's Z3 obligation with a sealed transitive evidence closure | Boolean/supported-integer inputs and results only; no virtual/instance dispatch, generics, `ref`, heap, loops, recursion, reference-assembly body authority, or arbitrary pack files; unsupported cases remain `Unknown` |
| Worker body execution | Compiler collector plus opt-in container worker | The compiler emits portable whole-body CFG/IR; the worker executes its bounded acyclic subset with locals, reassignment, branches, multiple returns, entry-state `Old`, supported expressions, and eligible resolved API specs | Loops, stateful instructions outside the narrow admitted model, unresolved calls, unsupported mutation, and exceeded bounds abstain |
| `ContractFor` validation | Incremental generator loaded with any non-`off` profile | Validates companion type and member identity, including receiver, overload, generic constraints, ref/scoped kinds, nullability, defaults, and return shape | It validates and binds existing source; it emits no generated source and does not make an unsupported contract provable |
| External calls | Analyzer, compiler collector, and worker | Analyzer/compiler stages resolve exact original symbols against `ApiSpecTable`; bounded postcondition calls can instead use exact implementation-IL relations or explicitly selected audited relational packs; the artifact binds each admitted call to canonical evidence that the worker revalidates | The worker does not turn arbitrary trusted metadata contracts, reference assemblies, or consumer-supplied pack files into proof facts; missing, ambiguous, untrusted, incomplete, or target-framework-inapplicable models fail closed |
| SMT | Worker only | Encodes the admitted Boolean and bounded-integer obligations; creates `Proven` only after unsat-core hygiene and `Refuted` only after executable replay | No Z3 or verifier payload is loaded into the IDE analyzer |

Effect exception contracts cover modeled synchronous managed exception flows.
Ambient catastrophic runtime failures such as memory or stack exhaustion are
outside that universe unless source or an exact boundary explicitly throws or
declares them. An unmodeled ordinary synchronous exception remains incomplete;
it is not silently excluded.

Not active as 1.0 preview product features:

- complexity classification or complexity diagnostics;
- regex-to-SMT translation;
- metadata IL effect inference;
- standalone runtime-hazard queries;
- nullable-contract diagnostics;
- general, recursive, virtual, or heap-aware source-callee verification beyond
  the direct acyclic scalar relational-summary boundary;
- a mutable heap or general points-to model;
- arbitrary loops, recursion, reference equality, sequence elements, or broad
  SMT theories.

## Analyzer language gate

The exact decision table is `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`. The
following matrix summarizes that checked table.

| Category | Admitted | Rejected |
|---|---|---|
| Callable kinds | Non-generic ordinary methods, instance and static constructors, property getters/setters, event add/remove accessors, and explicit interface implementations | Async methods, generic source methods, ref returns, ref parameters, declarations without an operation root, and unsupported method kinds |
| Types | Primitive and admitted named/reference types, strings, and arrays whose element type is admitted | Open type parameters, delegates, dynamic, pointers, function pointers, ref-like types, and admitted containers whose nested type is unsupported |
| Statements and flow | Blocks, locals, assignments, return/throw, `if`, `for`, `while`, `do`, constant-clause `switch`, `try`/`catch`/`finally`, `using`, `lock`, labels/branches, object and array initialization | `foreach`, async/iterator flow, local functions, closures, event raising, queries, deconstruction, switch expressions, patterns, `with`, ranges, inline arrays, collection expressions, and spread |
| Expressions | Literals, locals/parameters/instance, fields/properties, array access, built-in unary/binary/conversion operations, conditional/coalesce, `is` type, `typeof`, `nameof`, ordinary interpolation, object/array creation, and direct calls that pass shape checks | User-defined operators or conversions, delegates, dynamic operations, function pointers, anonymous objects, tuples, unsafe/address operations, custom interpolated-string handlers, implicit indexers, and future unknown Roslyn operation kinds |
| Calls | Direct non-delegate calls without ref arguments; closed constructed generic calls only when an exact `ApiSpec` resolves | Local/delegate/function-pointer calls, ref arguments, open generic shapes, and closed generic calls with no exact resolved spec |

The frontend has a second, expression-level exactness classifier. For example,
an operation kind can pass the analyzer gate while a lifted operator, narrowing
conversion, unsupported member access, or unsupported invocation form still
causes frontend abstention. The effect scanner can likewise return an
incomplete summary for admitted syntax.

The table describes selected effect and verifier-body admission. The separate
call-site precondition pass traverses Roslyn child CFGs for executable local
functions, lambdas, and anonymous methods without admitting those callable
forms to effect or postcondition verification. Nested outcomes remain attached
to their owner and are not folded into the containing method. Expression-tree
lambdas remain quoted, non-executing code for this pass.

The verifier body subset is narrower than the analyzer gate: compiler artifact
lowering and the worker executor accept only acyclic, bounded instructions they
can substitute and model exactly. Analyzer admission must not be read as worker
support.

## Contract surface

### Compiler-bound calls

| Contract | Binding and use |
|---|---|
| `Contract.Requires(condition)` | A precondition. The analyzer uses exact replay or managed CFG facts at definite call sites. The worker can use a bound precondition as a justified entry assumption. |
| `Contract.Ensures(condition)` | A normal-return postcondition and worker proof goal. The analyzer does not prove postconditions. |
| `Contract.Assume(condition)` | Explicit user evidence. It remains visible as a user-assumed proof justification. |
| `Contract.Result<T>()` | Valid only inside `Ensures`; substitutes the callable's normal return value. A direct runtime call throws. |
| `Contract.Old(value)` | Valid only inside `Ensures`; substitutes the entry-state value. Nested or otherwise invalid uses fail closed. A direct runtime call throws. |

The compiler elides `Requires`, `Ensures`, and `Assume` calls unless
`SHARPPROOF_CONTRACTS` is defined. SharpProof binds their compiler operations;
it does not parse free-form contract strings. Enabled analysis rejects that
reserved symbol through both package configuration and the final compiler
compilation, including source-local directives and generated syntax trees.

### Closed attributes

The binder currently consumes these attributes on ordinary methods and
constructors:

| Attribute placement | Bound clause | Current consumer |
|---|---|---|
| `[NotNull]` on a parameter | `parameter != null` precondition for reference, string, or sequence IR values | Analyzer exact replay/managed facts and worker entry assumptions |
| `[NotNull]` on a return value | `result != null` postcondition | Worker |
| `[Positive]` on a parameter | `parameter > 0` integer precondition | Analyzer exact replay/managed facts and worker entry assumptions |
| `[Positive]` on a return value | `result > 0` integer postcondition | Worker |
| `[InRange(min, max)]` on a parameter | Inclusive integer precondition `min <= parameter && parameter <= max` | Analyzer exact replay/managed facts and worker entry assumptions |
| `[InRange(min, max)]` on a return value | Inclusive integer postcondition | Worker |

Invalid value types, invalid ranges, and malformed intrinsic use make contract
binding fail closed. The three closed attributes are declared only for
parameter and return-value targets. The inactive `[Pure]` attribute has been
removed; `[EnforcePure]` is the implemented effect contract.

`[ContractFor(typeof(Target))]` permits a static companion class to hold
compiler-bound clauses for a target type. Instance target members use an
explicit first receiver parameter. The generator validates exact symbol shape;
see [Diagnostics](diagnostic-examples.md#contractfor-generator-diagnostics).
Any valid direct target clause owns the complete clause source. When no valid
direct clause exists, a valid companion remains usable even if the target has
a misplaced clause. That misplaced clause is SP0024 and is omitted as a whole
compiler-elided call, including its argument evaluation.

## Resolved API specification inventory

The default table has eleven BCL rows. Every row resolves by documentation
comment ID and original symbol identity across the supported reference
surfaces. Effects, allocation, throws, nullness, and cardinality are separate
facets; an exact fact in one facet does not make an unknown facet exact.

| Spec ID and row | Effects | Allocation | Throws | Result fact |
|---|---|---|---|---|
| `bcl.array.empty` - `System.Array.Empty<T>()` | Unknown because the generic cache can trigger type initialization | Unknown | Does not throw | Non-null, empty sequence |
| `bcl.exception.ctor` - `System.Exception..ctor()` | Writes the fresh receiver | None at the call boundary | Does not throw | None |
| `bcl.exception.ctor.string` - `System.Exception..ctor(string)` | Writes the fresh receiver | None at the call boundary | Does not throw | None |
| `bcl.invalid-operation-exception.ctor` - `System.InvalidOperationException..ctor()` | Writes the fresh receiver | None at the call boundary | Does not throw | None |
| `bcl.invalid-operation-exception.ctor.string` - `System.InvalidOperationException..ctor(string)` | Writes the fresh receiver | None at the call boundary | Does not throw | None |
| `bcl.object.ctor` - `System.Object..ctor()` | None at the call boundary | None at the call boundary | Does not throw | None |
| `bcl.string.length` - `System.String.Length` getter | Reads receiver state | None | Does not throw | Result equals receiver length |
| `bcl.string.concat.string-string` - `System.String.Concat(string, string)` | None | May allocate | Does not throw | Non-null string |
| `bcl.list.add` - `List<T>.Add(T)` | Writes receiver state | May allocate | Unknown | None |
| `bcl.math.abs.int32` - `Math.Abs(int)` | None | None | May throw `OverflowException` | Result is non-negative on normal return |
| `bcl.enumerable.empty` - `Enumerable.Empty<T>()` | Unknown because the generic cache can trigger type initialization | Unknown | Does not throw | Non-null, empty sequence |

These eleven rows are the complete supported built-in BCL surface. Anything
outside this table, or any row that does not resolve exactly for the current
target framework, fails closed. Object creation always analyzes a source
constructor or resolves an exact catalog row; exception constructors are not
implicitly trusted. In particular,
`AggregateException(IEnumerable<Exception>)` is unmodeled and produces an
incomplete effect result. A direct `throw new` refutation witness is available
only when the exact constructor has approved `DoesNotThrow` and `Terminates`
facets; either facet remaining unknown prevents a definite witness.

The worker projects validated call-result facets only into bounded proxies:

- null equality for exact string, reference, and array call results;
- direct `Length` observations for array results;
- source-width and cardinality bounds represented as integer facts.

`Array.Empty<T>()` has one narrow normal-return path that consumes its adjacent
memory-only havoc and uses non-null/empty array facts without claiming the call
is pure. `Enumerable.Empty<T>()` remains unsupported for cardinality proof
because `IEnumerable<T>` is not array-backed sequence IR. There is no general
reference/sequence SMT sort, alias analysis, element model, or heap model.

The table also contains compiler-bound ghost rows for `Contract.Requires`,
`Ensures`, `Assume`, `Result`, and `Old`. Those rows describe compiler-elided
contract semantics and the throwing behavior of direct `Result`/`Old`
invocation; they are not BCL coverage.

Contract API symbols are accepted only from the `SharpProof.Attributes`
assembly identity matching the analyzer payload. Clause methods also require
the exact supported signatures and one real
`Conditional("SHARPPROOF_CONTRACTS")` attribute. Rejected source/project
shadows, identity mismatches, and malformed lookalikes remain visibly selected
but contribute no contract, effect, trust, suppression, or compiler-bound
ghost specification evidence.

## Outcomes, accountability, and cache boundary

- `Proven` requires a hygienic core containing only lowered facts, resolved
  specs, verified contracts, or explicit user assumptions.
- `Refuted` requires independent replay. For a postcondition candidate, the
  proof kernel first checks exact backend-model closure and re-evaluates
  the lowered assumptions and goal. The worker then independently executes the
  compiler-produced whole-body program along the concrete CFG path and
  evaluates the original postcondition over the reconstructed post-state.
  Contract-only ordinary `void` methods replay as exact zero-step programs.
  Constructor postconditions are `UnsupportedBody` until base-constructor and
  field-initializer semantics are lowered.
  An executed API-spec or relational-summary call becomes `Unknown` with
  `CounterexampleNotReplayable`. Any other unsupported or inconsistent replay
  state is a fatal `CounterexampleReplayFailed`; one on an unselected path
  does not block the refutation. Result models expose only canonical user
  variables.
  For an effect candidate, compiler artifact schema 18 admits unconditional
  definite managed object/array allocation, exact framework explicit-throw,
  empty-`lock`, and exact-`Monitor` events. The worker recomputes each event's
  constraint and operation identities, checks its source-tree identity/span
  and sealed witness, and independently derives its effects, capabilities, and
  exact exception hierarchy. It evaluates the authenticated allowed-effect,
  capability, and exception constraints before publishing `Refuted`. Fresh
  allocation remains compatible with observable `EnforcePure`.
- `Unknown` covers unsupported, unresolved, approximate, method-time-limited,
  or resource-exhausted claim analysis. Unsupported unannotated analyzer
  callables are silent; unsupported selected callables produce SP0047.
- Protocol version 11 binds a compiler-manifest artifact and separately records
  run status, callable coverage, and one
  outcome for each stable manifest claim ID. Exact manifest/result equality is
  mandatory.
- The compiler artifact carries `SharpProofFeatures` as `WorkerFeatureSet`.
  `contracts` excludes effect-only annotations, `effects` excludes
  postcondition claims and contract assumptions, and `all` selects both. The
  compiler gives every selected effect-attribute occurrence one typed claim
  backed by sealed compiler evidence. Repeated attributes share the effective
  combined constraint/evidence while retaining distinct IDs and ordinals.
- Effect evidence distinguishes complete and incomplete may-effect summaries,
  trusted complete boundaries, definite direct violations, and unavailable
  evidence. A disallowed effect in a may-effect summary is not a replayed
  counterexample, so it remains `Unknown(EffectContractNotEstablished)`.
  Definite receiver-field, user-constructed exception,
  static-initialization-sensitive allocation, and other unsupported direct
  candidates are not published as refutations; they become
  `Unknown(CounterexampleNotReplayable)`. Conditional, path-dependent, and
  may-only conflicts without definite replay evidence remain
  `Unknown(EffectContractNotEstablished)`. Structural replay-artifact tamper
  is malformed compiler evidence and fails as `CompilerManifestMismatch`; a
  semantic disagreement during an otherwise valid replay becomes the fatal
  `Unknown(CounterexampleReplayFailed)`.
- Proven postconditions expose `ContradictoryPreconditions` or
  `NoModeledNormalReturn` vacuity evidence in JSON and SARIF. Proven claims are
  not disk-cache entries.
- Caller cancellation is run status `Canceled`, project timeout is
  `TimedOut`, and infrastructure/protocol/backend/replay failure is `Failed`.
  None is a successful claim outcome.
- Cache schema version 13 stores only complete, postcondition-only, all-refuted
  payloads. Cache reads are checked against the entire current manifest, then
  every supported scalar model is reconstructed and whole-body replayed.
  Proven claims, effect claims, unsupported models, `Unknown`, cancellation,
  timeout, malformed result, infrastructure failure, and failed replay are not
  semantic cache entries. `require-proven` disables the local semantic cache.
- `SharpProofVerifyPolicy` maps incomplete selected analysis to informational,
  warning, or error SP0047 reporting. `SharpProofAssumptionPolicy` maps user or
  trusted evidence to SP0048. These policies do not make fatal runs successful.

## Closed compiler artifact and remaining limits

During container verification, the production analyzer captures compiler
artifact schema version 18 from the post-generator compilation. The artifact
contains:

- the feature-selected, sealed claim manifest;
- one record per selected callable, containing either a typed lowering failure
  or portable whole-body CFG/IR with bound contract clauses, canonical
  variables, body-entry state, parameter mappings, and exact API-spec witness
  metadata;
- canonical source, exact implementation-IL, and explicitly enabled audited
  specification-pack summary calls, including complete transitive evidence
  closure under relational-summary schema version 2 and specification-pack
  schema version 1;
- compiler-neutral ordered replay evidence for admitted unconditional
  allocation, exact-framework-throw, and synchronization events, including
  authenticated selected-constraint and semantic-operation hashes plus
  source-tree identity and span;
- compiler error diagnostics with mapped locations;
- handwritten and generated tree hashes with language version, documentation
  mode, source kind, preprocessor symbols, and parse features;
- a bounded proof-relevant compilation-option set, assembly and target
  identity, and compiler identity/MVID provenance; and
- reference provenance: manifest and linked-module paths, image hashes, byte
  sizes, names, MVIDs, symbol identity, image kind, embed-interop flag, and
  aliases. A module is capped at 256 MiB, the complete closure at 1 GiB, and
  the closure at 4,096 modules.

The bounded compilation-option record covers output kind, explicit main-type
selection, optimization, platform, nullable context, metadata import, checked overflow, unsafe mode,
determinism, global usings, warning level, general and per-diagnostic reporting
options, reference-supersession state, the supported Default/Desktop
assembly-identity comparer profile, and the fixed evidence-only resolver
policy. Realized compiler error diagnostics are sealed alongside those
options so opaque syntax-tree diagnostic providers also affect the fingerprint.

Source text and reference image bytes are not embedded. The compiler must be
able to read each file-backed `PortableExecutableReference` to record its
provenance, but the worker never rereads it. Resolver-dependent `#r`/`#load`,
missing-assembly resolver mode, reference supersession, custom
assembly-identity comparers, and non-file or unreadable references fail
artifact collection as SP0049. `AdditionalFiles` are sealed by canonical path
and content hash without embedding their raw contents. Analyzer configuration
and reporting policies are represented by their observable effects on the
final compilation and effective SharpProof options.

The launcher binds the artifact path to the compiler-produced evidence. The
worker reads those bytes once, validates the closed portable graph, requires
the embedded maximum expression depth to match the request, and requires exact
manifest/lowered-callable equality before any cache lookup or backend creation.
It does not construct a Roslyn compilation, parse source, or read references.
Compiler build identities and reference metadata are provenance and cache
identity rather than runtime compatibility gates. Compiler errors become
`CompilationFailure`; malformed lowered evidence, claim/assumption drift, or an
option mismatch becomes `CompilerManifestMismatch`.

Generated claims and supported bodies are therefore visible and executable
from compiler-produced IR, and candidate refutations receive independent
whole-body replay. The three-package split is complete. Each package has a
matching portable-PDB `.snupkg` with SourceLink bound to the package repository
commit, and package builds run SDK package validation. The package workflow
publishes the six NuGet artifacts for canonical `master` builds.

The remaining integration limits are owner configuration of protected tags
and the private/public NuGet environments, the first publications, and broader
host qualification. Deterministic SARIF 2.1.0 is available as an opt-in
projection of the validated worker response. The workflow already promotes
only the tested bytes after revalidating tag, version, master ancestry,
predecessor-tag order, repository identity, and package
inventory. Before publication, each of the three target V3 main-package
identities at the release version must be absent. Main and symbol packages are
published separately in dependency order without duplicate skipping. The
symbol service has no symmetric V3 download surface, so a symbol-package
collision is detected by the push and fails the release. Any partial or
conflicting publication requires a new version. These limits are not
worker-side compilation reconstruction, postcondition-counterexample replay,
the admitted allocation/capability/exception effect replay, package separation,
SARIF, or external release-artifact attestation work. Replay support for receiver-field,
user-exception, conditional, and other effect events remains an explicit
preview.2 blocker.

See [Typed abstention reasons](unknown-reasons.md) for the exact enums and
[Analysis limits](analysis-limits.md) for configured budgets.
