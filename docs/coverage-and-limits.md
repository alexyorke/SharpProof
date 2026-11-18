# Coverage and limits

SharpProof verifies a bounded C# model. This guide separates modeled behavior from universal language support. The [semantics](../SEMANTICS.md) defines the evidence required for each verdict.

## Product capability matrix

| Surface | Current behavior |
| --- | --- |
| Portable analyzer | Contract/configuration validation, companion validation, conservative effect analysis and diagnostics |
| Compiler collector | Final-compilation selection and closed typed artifact |
| Native worker | Bounded postcondition, call-precondition, exception and effect claim checks |
| Replay | Concrete validation of candidate paths, values and violating sites |
| Package consumers | Portable analyzer and canonical-container verifier checks; release smoke tests also run the packed verifier on Windows and macOS |

A callable may be admitted while a specific claim is Unknown. A method with one Proven claim is not automatically verified for every effect or exceptional behavior.

Call-precondition claims cover invocations, constructions and accessor calls from bodies that lower exactly to non-virtual source callees in the same compilation with by-value parameters. Calls into another assembly that are neither dispatched nor generic check the callee's closed parameter attributes, read from the hashed reference image (reference assemblies included), and the `Requires` of a companion the calling project declares. A project reference built in the same build also passes its [contract export](../SEMANTICS.md#contract-exports), so its `Contract.Requires` clauses that read only parameters are checked at the call. A package's or separately built assembly's `Contract.Requires` is compiled out of its binary and is not checked. Exports cover only the target framework the reference resolves to, and never dispatched calls. Every other call can violate a `Requires` without failing a build. [Semantics](../SEMANTICS.md#calls-and-loops) lists them, and the build summary counts the ones the compiler can see. That count also includes calls left out by the 24 MiB artifact cap and calls whose claims ran out of time, which do not fail `require-proven`. A tree nested too deeply to walk is skipped entirely, so its calls are neither checked nor counted.

## Callable and body admission

Modeled cases include branches, local variables, multiple returns, exact bounded integral arithmetic, conversions, reference/null conditions, strings, selected array and field operations, exception flow, source calls, and natural loops.

Width, signedness, checked overflow, nullability, dispatch, field identity, and source provenance affect admission. General floating-point, decimal, native-sized operations, arbitrary reflection/native code, async state machines, arbitrary virtual dispatch, and recursive call behavior do not have a blanket proof guarantee.

Two of these are deliberately out of scope for now, measured on 16 open-source libraries (10,543 methods; see the blocker census in [development workflows](development-workflows.md)):

- **`async` methods.** A claimed method that is `async` is refused at its signature for postconditions and effect claims. Resource-upper-bound claims on a configured async join are the one exception (see [configured async join source work](resource-bounds.md#configured-async-join-source-work)). The census finds 278 methods where that is the only blocker, mostly in template, JSON, CSV, resilience and state-machine libraries. Modeling them needs the compiler's state machine and `Task` completion, which the IR has no representation for.
- **Open-world dispatch.** A virtual or interface call whose target is not provably exact (a `base.` call, a sealed member, or a receiver of a sealed type) is an unknown call, because another assembly can override it. Among bodies that otherwise lower exactly, about 260 methods are kept from a purity proof only by such calls. Most are public, unsealed classes, where an override in another assembly is a real possibility.

The typed scalar catalog includes `sbyte`, `byte`, `short`, `ushort`, `char`, `int`, `uint`, `long`, and `ulong`, with their CLR widths and signedness. The portable range domain excludes `ulong` because its bounds use signed `long`. Consult [CSharpOperationSemantics.Scalars.cs](../src/SharpProof.Frontend/CSharpOperationSemantics.Scalars.cs) and the operation-specific lowering rules; presence in the type catalog does not admit every operation on that type.

Loops can use establishment/preservation-checked invariant candidates. Bounded execution is used for witness search, not universal loop proof. Calls can be inlined or modeled only under the compiler and specification evidence accepted by the current frontend and worker. Opaque calls retain conservative effects and approximation values.

The authoritative admission code is in [RoslynTotalProgramLowerer.cs](../src/SharpProof.Frontend/RoslynTotalProgramLowerer.cs), its partial files, and [CompilerTotalCallableLowerer.cs](../src/SharpProof.CompilerCollector/Producer/CompilerTotalCallableLowerer.cs). Worker construction and replay may impose further limits.

## Heap and reference boundaries

The worker models ordered field and element stores under path reach, entry contents, receiver non-nullness, and closed compiler runtime type evidence. Unknown mutations forget information rather than preserve stale contents.

Incompatible sealed runtime types cannot share a non-null object identity. Heap witness decoding rejects impossible aliases without adding solver assumptions. Null and compatible aliases remain allowed. Unused field observations can conservatively reject a candidate witness. Reference-valued array accesses remain an admission limitation.

There is no arbitrary object-graph, ownership, concurrent-memory, or external mutation guarantee. A modeled scalar field case does not establish support for all heap operations.

## Contract surface

Direct prologue clauses, companion contracts, and closed parameter/return attributes are described in [public API](public-api.md). Invalid clauses remain diagnostics; they cannot silently strengthen assumptions.

Postconditions apply on normal return. Entry contradictions and lack of normal completion have explicit vacuity records. Call-site precondition diagnostics require concrete false replay. Unknown or throwing predicate evaluation does not establish a violation.

Effect contracts separate read/write regions, allocations, exceptions, capabilities, synchronization, determinism, native code, and reflection. Each flag is independent. A trusted complete external boundary is reviewed declared evidence, not a checked source-body proof.

## API specifications

CI resolves the default catalog and audited intrinsics against the pinned
.NET 10 framework surface (`Microsoft.NETCore.App.Ref` 10.0.15, `net10.0`)
and requires executed runtime evidence for every modeled facet.
This is separate from executable line coverage and does not imply full API semantics.
The same gate publishes an API/facet-to-test matrix and requires executed behavioral
checks and rejection of deliberately wrong claims for each modeled default-catalog
facet/postcondition and each inventoried intrinsic. Missing or failing evidence fails CI.

The default catalog is a closed reviewed set of exact member matches and approved assembly evidence. It does not trust arbitrary same-named framework members. Its facets must be interpreted independently: an allocation or exception facet does not establish a result relation.

See the [catalog](api-spec-catalog.md) and [DefaultApiSpecCatalog.cs](../src/SharpProof.Specs/DefaultApiSpecCatalog.cs). The declarative C# source and the Markdown reference are both hand-maintained.

Specification identity, runtime domains, exception hierarchy, and result relationships participate in compiler evidence and worker validation. A modeled API result may remain unreplayable when no concrete evaluator can execute that boundary.

`SharpProofSpecificationPacks` enables embedded reviewed relational packs by semicolon-delimited ID. The current [pack catalog](../src/SharpProof.Specs/RelationalSpecPackCatalog.cs) contains `dotnet.scalar@1`, including the Int32 `Math.Max` result relation. Packs are opt-in; an effect/exception catalog entry alone does not imply that relation. Unknown IDs, duplicate IDs, and empty entries in a nonempty list fail closed.

## Outcomes, accountability, and cache boundary

Selected callables and claims remain explicit even when unsupported. Unknown is not absence. Outcome, reason, assumptions, certainty, witness, and vacuity must agree. Malformed or incomplete responses fail validation.

Complete eligible responses can be cached, including stable semantic abstentions. Transient failures are excluded. Cache corruption or unavailable storage causes a miss; a cache cannot convert a missing obligation into a success.

## Regression and acceptance evidence

| Evidence | What it checks |
| --- | --- |
| Frontend/IR/Contracts tests | Binding, typed lowering, ownership, entry domains, evaluation |
| SMT/Worker tests | Native obligations, loop cuts, heap reasoning, replay, budgets, protocol/cache boundaries |
| Analyzer tests | Reporting, configuration, companion validation, documentation C# fences |
| Package tests and samples | Actual packed consumers and MSBuild behavior |
| Golden tests | Stable lowering, worker and analyzer output |
| Corpus and metamorphic gates | Reviewed support classification, semantic outcomes, invariant diagnostics |
| Coverage gate | Line coverage of the C# sources under `src/`, merged over every CI test shard; CI fails when the merged total is below 98.5%. There is no per-project or changed-code floor |

[Corpus documentation](../tests/SharpProof.Gates/README.md) explains its source pin, classifications, and ratchet. [Golden documentation](../tests/golden/README.md) explains updates. Numeric floors are owned by checked-in acceptance data and must not be relaxed to hide unsupported behavior.

Passing these gates verifies exercised cases. It is not a completeness claim about C# or all methods in an upstream library.
