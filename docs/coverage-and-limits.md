# Coverage and limits

This document is the authoritative inventory of SharpProof 0.2's implemented
product surface. [SEMANTICS.md](../SEMANTICS.md) remains normative: if a
semantic rule here conflicts with it, `SEMANTICS.md` wins.

SharpProof admits code through explicit gates and then analyzes it
conservatively. Admission does not guarantee a proof. Missing models,
unsupported expressions, approximate facts, and exhausted budgets remain
`Unknown` or silent abstentions.

## Product capability matrix

| Surface | Runs where | Implemented behavior | Current boundary |
|---|---|---|---|
| Effect contracts | Analyzer with `SharpProofFeatures=effects` or `all` | Computes path-insensitive may summaries for reads, writes, allocation, capabilities, exceptions, termination, and completeness; checks `[EnforcePure]`, `[ZeroAllocations]`, `[AllowedCapabilities]`, `[DoesNotThrow]`, and `[AllowedExceptions]` | A possible or unresolved violation produces a not-proven diagnostic, not a definitive effect witness |
| Call-site preconditions | Analyzer with `SharpProofFeatures=contracts` or `all` | Binds source `Contract.Requires` clauses and closed parameter attributes with compiler symbols for ordinary calls and object creation; reports only when receiver, arguments, and required prefix evaluation are exact and non-throwing and the instantiated predicate concretely evaluates to false | Unknown values and possible throws remain silent at unannotated sites; unsupported explicitly selected methods report SP0047 |
| Postconditions | Optional Windows x64 worker with `SharpProofFeatures=contracts` or `all`; strict enables the worker by default | Manifests `Contract.Ensures` and return attributes, including directly owned local-function, lambda, anonymous-method, and top-level claims, then proves admitted bounded obligations over normal-return paths with Boolean/integer SMT and replay-gated counterexamples | The additional callable forms are currently visible as `UnsupportedCallable`; `effects` excludes postcondition claims; this is bounded `Ensures` verification, not arbitrary deep, recursive, looping, heap, or sequence verification |
| Worker body execution | Opt-in Windows x64 worker | Executes a bounded acyclic CFG model with locals, reassignment, branches, multiple returns, entry-state `Old`, supported expressions, and eligible resolved API specs | Loops, stateful instructions outside the narrow admitted model, unresolved calls, unsupported mutation, and exceeded bounds abstain |
| `ContractFor` validation | Incremental generator loaded with any non-`off` profile | Validates companion type and member identity, including receiver, overload, generic constraints, ref/scoped kinds, nullability, defaults, and return shape | It validates and binds existing source; it emits no generated source and does not make an unsupported contract provable |
| External calls | Analyzer and worker | Both resolve exact original symbols against `ApiSpecTable`; effect analysis can additionally consume an explicitly trusted complete effect contract | The worker does not turn arbitrary trusted metadata contracts into proof facts; missing, ambiguous, untrusted, incomplete, or target-framework-inapplicable models fail closed |
| SMT | Worker only | Encodes supported Boolean and signed-integer obligations; creates `Proven` only after unsat-core hygiene and `Refuted` only after executable replay | No Z3 or verifier payload is loaded into the IDE analyzer |

Not active as 0.2 product features:

- complexity classification or complexity diagnostics;
- regex-to-SMT translation;
- metadata IL effect inference;
- standalone runtime-hazard queries;
- nullable-contract diagnostics;
- general source-callee assume/guarantee verification;
- a mutable heap or general points-to model;
- arbitrary loops, recursion, reference equality, sequence elements, or broad
  SMT theories.

## Analyzer language gate

The exact decision table is `SharpProof.Analyzer/LanguageSubsetGate.cs`. The
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

The worker body subset is narrower than the analyzer gate: its executor is
acyclic and bounded and accepts only instructions it can substitute and model
exactly. Analyzer admission must not be read as worker support.

## Contract surface

### Compiler-bound calls

| Contract | Binding and use |
|---|---|
| `Contract.Requires(condition)` | A precondition. The analyzer can replay it at exact call sites. The worker can use a bound precondition as a justified entry assumption. |
| `Contract.Ensures(condition)` | A normal-return postcondition and worker proof goal. The analyzer does not prove postconditions. |
| `Contract.Assume(condition)` | Explicit user evidence. It remains visible as a user-assumed proof justification. |
| `Contract.Result<T>()` | Valid only inside `Ensures`; substitutes the callable's normal return value. A direct runtime call throws. |
| `Contract.Old(value)` | Valid only inside `Ensures`; substitutes the entry-state value. Nested or otherwise invalid uses fail closed. A direct runtime call throws. |

The compiler elides `Requires`, `Ensures`, and `Assume` calls unless
`SHARPPROOF_CONTRACTS` is defined. SharpProof binds their compiler operations;
it does not parse free-form contract strings.

### Closed attributes

The binder currently consumes these attributes on ordinary methods and
constructors:

| Attribute placement | Bound clause | Current consumer |
|---|---|---|
| `[NotNull]` on a parameter | `parameter != null` precondition for reference, string, or sequence IR values | Analyzer call-site replay and worker entry assumptions |
| `[NotNull]` on a return value | `result != null` postcondition | Worker |
| `[Positive]` on a parameter | `parameter > 0` integer precondition | Analyzer call-site replay and worker entry assumptions |
| `[Positive]` on a return value | `result > 0` integer postcondition | Worker |
| `[InRange(min, max)]` on a parameter | Inclusive integer precondition `min <= parameter && parameter <= max` | Analyzer call-site replay and worker entry assumptions |
| `[InRange(min, max)]` on a return value | Inclusive integer postcondition | Worker |

Invalid value types, invalid ranges, and malformed intrinsic use make contract
binding fail closed. The three closed attributes are declared only for
parameter and return-value targets. The inactive `[Pure]` attribute has been
removed; `[EnforcePure]` is the implemented effect contract.

`[ContractFor(typeof(Target))]` permits a static companion class to hold
compiler-bound clauses for a target type. Instance target members use an
explicit first receiver parameter. The generator validates exact symbol shape;
see [Diagnostics](diagnostic-examples.md#contractfor-generator-diagnostics).

## Resolved API specification inventory

The default table has seven BCL rows. Every row resolves by documentation
comment ID and original symbol identity across the supported reference
surfaces. Effects, allocation, throws, nullness, and cardinality are separate
facets; an exact fact in one facet does not make an unknown facet exact.

| Spec ID and row | Effects | Allocation | Throws | Result fact |
|---|---|---|---|---|
| `bcl.array.empty` - `System.Array.Empty<T>()` | Unknown because the generic cache can trigger type initialization | Unknown | Does not throw | Non-null, empty sequence |
| `bcl.object.ctor` - `System.Object..ctor()` | None at the call boundary | None at the call boundary | Does not throw | None |
| `bcl.string.length` - `System.String.Length` getter | Reads receiver state | None | Does not throw | Result equals receiver length |
| `bcl.string.concat.string-string` - `System.String.Concat(string, string)` | None | May allocate | Does not throw | Non-null string |
| `bcl.list.add` - `List<T>.Add(T)` | Writes receiver state | May allocate | Unknown | None |
| `bcl.math.abs.int32` - `Math.Abs(int)` | None | None | May throw `OverflowException` | Result is non-negative on normal return |
| `bcl.enumerable.empty` - `Enumerable.Empty<T>()` | Unknown because the generic cache can trigger type initialization | Unknown | Does not throw | Non-null, empty sequence |

These seven rows are the complete supported built-in BCL surface. Anything
outside this table, or any row that does not resolve exactly for the current
target framework, fails closed.

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

## Outcomes, accountability, and cache boundary

- `Proven` requires a hygienic core containing only lowered facts, resolved
  specs, verified contracts, or explicit user assumptions.
- `Refuted` requires executable replay of the candidate model. The analyzer's
  current effect may-analysis does not produce definitive effect refutations.
  Worker replay currently uses the lowered obligation path, not the independent
  whole-body exact-CFG interpreter required by the 1.0 release gate.
- `Unknown` covers unsupported, unresolved, approximate, method-time-limited,
  or resource-exhausted claim analysis. Unsupported unannotated analyzer
  callables are silent; unsupported selected callables produce SP0047.
- Protocol version 3 separately records run status, callable coverage, and one
  outcome for each stable manifest claim ID. Exact manifest/result equality is
  mandatory.
- The request carries `SharpProofFeatures` as `WorkerFeatureSet`.
  `contracts` excludes effect-only annotations, `effects` excludes
  postcondition claims and contract assumptions, and `all` selects both. The
  current worker has no effect-proof claim kind, so an effect-selected callable
  is explicitly incomplete rather than vacuously complete.
- Caller cancellation is run status `Canceled`, project timeout is
  `TimedOut`, and infrastructure/protocol/backend/replay failure is `Failed`.
  None is a successful claim outcome.
- Cache schema version 3 stores only complete validated payloads. Cache reads
  are checked against the entire current manifest. Unknown, cancellation,
  timeout, malformed result, infrastructure failure, and failed replay are not
  semantic cache entries.
- `SharpProofVerifyPolicy` maps incomplete selected analysis to informational,
  warning, or error SP0047 reporting. `SharpProofAssumptionPolicy` maps user or
  trusted evidence to SP0048. These policies do not make fatal runs successful.

## Current compilation-integration gap

During Windows verification, the production analyzer now captures a
deterministic post-generator compilation seal. It covers the final source and
generated syntax trees, compiler and parse options, reference identities,
aliases and image hashes, `AdditionalFiles`, effective SharpProof policies,
Roslyn version, target framework, and assembly identity. An inability to emit
the requested seal is fatal SP0049.

The seal is parity and diagnostic evidence only. The worker still reconstructs
a compilation from MSBuild source and reference lists and does not consume the
seal as a closed compiler artifact. Collection requires file-backed references
and fingerprints their current on-disk images, not an exact copy of Roslyn's
loaded metadata, so the seal is not used for cache validity. Generated claims
and other compiler-only state are therefore not yet closed into the worker
manifest/IR artifact required for 1.0. Production-plan Step 4 remains
incomplete; compilation reconstruction deletion and SARIF projection are
still future work.

See [Typed abstention reasons](unknown-reasons.md) for the exact enums and
[Analysis limits](analysis-limits.md) for configured budgets.
