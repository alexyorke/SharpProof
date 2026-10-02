# SharpProof 1.0 preview architecture

SharpProof 1.0 is an effect-first, soundness-first preview. The supported
product is the effect cluster, compiler-bound call-site preconditions, and the
bounded out-of-process postcondition verifier. Unsupported code is an
abstention, not an invitation to guess.

## Dependency direction

The active production graph is a checked DAG:

```text
Attributes
Ir
Dataflow
Specs                 -> Ir
Frontend              -> Attributes (build-only payload identity), Ir
Contracts             -> Frontend, Ir
Effects               -> Dataflow, Frontend, Specs
Verify                -> Ir, Specs
Smt                   -> Ir, Verify
Summaries             -> Ir
CompilerArtifact      -> Ir, Worker.Protocol
Analyzer.Core         -> Contracts, Effects, Frontend, Ir, Specs
Analyzer              -> Analyzer.Core
ContractForGenerator  -> Analyzer.Core, Contracts
CompilerCollector     -> Analyzer.Core, CompilerArtifact, Contracts, Effects,
                         Frontend, Ir, Specs, Summaries, Worker.Protocol
BuildTasks            -> Host
Host
Worker.Protocol
Worker                -> CompilerArtifact, Dataflow, Host, Ir, Smt, Specs,
                         Verify, Worker.Protocol
```

Frontend's
Attributes edge has `ReferenceOutputAssembly=false`; it establishes build
order so the exact Attributes assembly identity can be embedded without adding
a runtime assembly dependency. The architecture suite compares every direct
project reference against this graph. The ordinary live analyzer has no static dependency on the
compiler-artifact model or worker protocol; those
dependencies belong only to the build-only compiler collector.

The Roslyn analyzer has no verifier, SMT, Z3, or native dependency. Z3 is
allowed only in `SharpProof.Smt`, which is packaged below
`tools/net9` for the worker. `Ir`, `Dataflow`, and `Specs` contain no C# syntax
types. Production semantic-model acquisition passes through the single audited
`SharpProof.Frontend.Host.CompilationModelProvider`.

<a id="mechanized-boundaries"></a>
## Mechanized boundaries

The `SPMETA001`-`SPMETA011` repository analyzers turn selected
soundness-critical construction, cancellation, cache, and semantic-identity
boundaries into build errors. Architecture and package tests complement those
rules with exact project-reference and payload checks. These checks define
mechanical enforcement boundaries; they do not expand the admitted language or
turn an unsupported result into proof.

The exact path inventories in `eng/acceptance/contract.json` cover
proof-outcome construction and each declared trusted boundary: discovery,
lowering, execution, obligation construction, SMT encoding, API specification
hand-maintained catalog tables, effect analysis, replay, policy, result assembly,
and cache validation. Compiler-input identity, typed canonical hash encoding,
and protocol validation have their own non-overlapping inventories rather than
 being hidden inside the cache component. API-spec content identity is likewise
 separate from resolution and instantiation. The declarative API-spec catalog,
 its matcher/instantiator source are one audited
 `apiSpecificationCatalog` component. The contract API vocabulary has its own
 `contractApiCatalog` component; its generated output is limited to descriptors
 and ordered tables, while lookup behavior remains handwritten.
`CSharpOperationSemantics` owns scalar widths, operator mappings, guarded
local faults, explicit Roslyn decisions and analyzer-stage support flags.
Source and IL lowering share its metadata. The remaining analyzer range domain
uses a signed-long projection; CFG traversal contains no language rules.
`IrOperatorCatalog.generated.cs` owns IR factory operator validation.
The portable IR wire-enum and slot tables and wire projection
adapters are declarative, while the codec keeps
 indexing, depth/cycle, canonicality, and malformed-input validation handwritten.
The effect-contract mapping catalog similarly owns finite capability, region,
direct-event, and reference-family mappings; effect analysis and fail-closed
validation remain handwritten.
Finite output, result-label, policy, operation-stage, and effect-wiring
projections live in the per-project `*Projections.generated.cs` tables;
replay, validation, and analysis algorithms remain handwritten.

## Semantic core

`SharpProof.Ir` owns the authoritative representation:

- hash-consed, factory-scoped typed terms;
- scoped variable, operation, block, instruction, and location identities;
- capture-free substitution and structural old-state substitution;
- deterministic printing;
- a concrete expression and program interpreter;
- compact assign/load/store/call/assume/assert/havoc/control-flow
  instructions.

Frontend expression and CFG lowering is total. A supported operation lowers to
exact IR; every unsupported case returns a typed opaque term or program
abstention. There are no `TryLower(..., out ...)` branches, syntax reparsing,
speculative semantic models, or display-string identity.

Compiler-facing stages share a closed operation-support catalog, with separate
decisions for contract-expression lowering and effect discovery. Stage-owned
shape and type validation remains independent, so sharing the inventory cannot
widen a stage's semantics. Unknown future Roslyn operation kinds fail closed.

`SharpProof.Dataflow` supplies deterministic fixpoint evaluation, partial
orders, joins, widening, havoc, and interval/congruence, sequence-cardinality,
and nullness domains. Source method effects are solved by stable SCC order.
Its canonical-domain opt-in stores validated strictly growing transfer results
directly; ordinary custom domains retain the normalizing join path. Managed
effect-flow analysis remains on that generic path until its representation
contract has independent regression coverage.
The out-of-process worker also projects validated `ApiSpec` result nullness and
array cardinality into spec-justified Boolean and integer proxies. This is a
bounded call-result integration; it does not use roslyn-analyzers entities,
points-to state, or general CFG transfer.

External effect analysis uses a symbol-resolved `ApiSpecTable` or an explicitly
trusted, complete effect contract. Compiler-side callable lowering binds only
eligible resolved `ApiSpec` rows into exact witness metadata; the worker
revalidates those witnesses against its matching table. Unmodeled or untrusted
metadata effect behavior is unknown; relational postcondition summaries do not
act as effect summaries. The separate build-time implementation-IL relation
decoder is bounded to exact static scalar bodies and is not a general IL
interpreter.

Importing a source or metadata effect summary is also conditional on the
callee's entry contract. Analyzer and compiler-artifact runs use the exact
contract binder plus call-site abstract facts to establish every `Requires`
and closed parameter precondition. Standalone effect analysis conservatively
detects direct clauses, closed attributes, and companion intent and marks the
summary incomplete when it cannot prove the obligation. Invalidly placed
clauses cannot refine a call into a complete effect proof. Both policies and
their summary-incompleteness propagation are declared parts of the
`effectAnalysis` trusted computing base.

## Contracts and modular verification

`Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old` bind as normal C#
operations. `Old` is pre-state substitution. `[ContractFor]` companions are
validated by the analyzer's compilation-end action after all generators have
contributed their syntax trees. The package's incremental generator is an
empty loading hook; validation uses exact symbol identity, including generics,
constraints, ref/scoped kinds, nullability, defaults, and return shape.

The worker composes quantifier-free callee relations inferred from exact
acyclic source bodies, exact implementation PE bodies, or explicitly enabled
audited specification packs. Each origin produces the same typed IR relation;
Z3 proves the resulting caller obligation, while summary-schema-2 evidence records the
complete transitive dependency closure. This remains a direct static scalar
boundary, not general recursive, virtual, or heap-aware source-callee modular
verification. The analyzer
combines exact compiler-bound replay with a managed CFG abstract interpreter
over Boolean, nullness, integer-interval, sequence-cardinality, and effect
facts. Comparison edges refine both scalar operands and retain only joined facts
valid on every incoming path. It reports a `Requires` violation only at a definitely executed call
whose receiver/argument prefix completes normally and whose instantiated
condition is definitely false. The worker checks only the bounded `Ensures`
subset supported by its admitted acyclic CFG executor; deep or otherwise
unsupported postconditions abstain.

Proof evidence is type-safe. Approximations cannot construct an assumption.
`Proven` is created only by the proof kernel after unsat-core hygiene checks.
For SAT, the proof kernel first requires exact assignment closure over the
requested Boolean/integer model variables and re-evaluates every lowered
assumption as true and the lowered goal as false. The worker then independently
executes the compiler-produced whole-body program along the concrete CFG path,
reconstructs its post-state, and evaluates the original `Ensures`. `Refuted` is
created only when both layers observe the violation. Contract-only ordinary
`void` methods have an exact zero-step whole-body replay. Constructor
postconditions abstain as `UnsupportedBody` until base-constructor and field-
initializer semantics are lowered.

## Manifest, protocol, determinism, and cache

IR identity is structural and factory-scoped. Formula construction, proof
cores, diagnostics, and serialized responses are stably ordered.

Protocol version 13 carries the compiler-artifact path/digest, reporting
policies, budgets, and cache controls. Source-generated System.Text.Json
metadata requires every wire property and rejects unknown properties.
Object property order is irrelevant. Semantic validation checks enum values,
nulls, claim ownership, dense ordinals, assumptions, and allowed result payloads.
Counts are derived from the callable and claim arrays rather than stored again.

The compiler produces a schema-21 closed artifact containing selected claims,
portable typed IR, relational/spec call bindings, effect constraints and replay
events, diagnostics, and mapped locations. One SHA-256 covers the full canonical
artifact bytes, including effect-only callables without a graph. Source and
reference inventories and duplicated provenance authorities are absent from the
wire. Producer reporting IDs remain stable opaque labels.

Schema 21 requires an explicit execution mode, havoc origin, and nullable
UTF-16 source span for each operation. The temporary Total mode uses signed
32-bit default integers, deterministic bitvector division and remainder, and
explicit Throw/ExceptionalExit edges. Legacy remains authoritative during
migration, and the existing SMT backend rejects Total queries. The candidate
CallableSolverSession encodes total Boolean and fixed-width integer terms in
one native solver per callable. Each query activates its own assumption subset
and negated goal; models contain only that query's requested variables. It
shares native scheduling and resource accounting with the legacy backend, while
active cancellation or recognized infrastructure failure retires only the
candidate session. An explicit candidate frontend shares typed scalar operation
rules between body evaluation and guarded clauses. Body operands are captured
in source order; local fault guards lead to Throw/ExceptionalExit. Clauses keep
safety separate from value, including through Old and lazy expressions. A shared
context gives entry inputs, mutable parameter storage, Old snapshots, and Result
distinct identities. Validated specification calls are omitted from runtime
body evaluation. Async and partial/generic contexts, ref locals, heap operations,
unsupported metadata bodies, ordinary irreducible loops and point Assume instructions inside cycles
currently abstain.
The compiler-owned source-call route admits nonrecursive same-compilation static
scalar methods. Each invocation evaluates arguments once in source order, maps
them to parameter ordinals, and owns a fresh Entry/Current/Old/Result frame.
Default arguments must be scalar constants. Callee contracts are erased without
adding their Requires or Ensures as proof premises; callee Assume closes admission.
All nested frames share the same construction cap and cancellation boundary.
Original expanded IR retains callee returns, finally bodies, and escaping throw
kind/site for replay and caller catch routing. Async, iterator, generic, ref/params,
unmodeled type-initialization calls remain unsupported. When an
outer filter can observe a callee fault, fresh frames share one program builder.
Each frame searches its own handlers first, then continues search at the caller's
captured lexical point before running the immutable inner-to-outer finally
prefix. A selected handler stops search; a fault during unwind starts fresh
search from the faulting frame. Calls within a filter finish their own unwind
before rejection resumes the original exception. Root entry and final validation
remain owned by the caller. Ordinary calls retain the separate frame composition.
Replacement exceptions can create shared filter/finally cycles; the owned loop
proof and bounded witness encodings keep conservative completed Unknown results
distinct from absent evidence. Havoc can lose correlations between earlier
filter mutations and a later replacement search, leaving a valid postcondition
Unknown when the bounded search is also inconclusive.

The typed implementation-IL route admits bounded static scalar managed methods
and object/string null, copy and identity transfers
from an exact captured implementation image. The collector verifies assembly,
module, method token, signature and backing metadata, then hashes the immutable
bytes it actually decodes. It validates every instruction and stack merge before
allocating an owned frame. IL operands use CLI stack widths; short storage and
returns truncate, loads extend, and unsigned opcodes control interpretation.
Boolean transfers require proven canonical zero/one stack values. Reference stack
shapes preserve the object/string type and null provenance through merges;
reference arithmetic, ordering and general conversions remain closed. The
all-members metadata binding compares intrinsic types and assembly identities
without requiring symbols from different compilations to be the same instance.
Body and IL
arithmetic share the same wrap values and overflow/division fault guards.
Arguments, local/parameter mutation, dependencies and returns expand into original
typed IR with fresh frame storage and one shared construction/recursion budget.
Escaping IL faults retain image/token/offset provenance and route through caller
filter search, catches and finally; concrete counterexamples replay that owned
original graph and project only canonical caller inputs.

Only AnyCPU and amd64 IL-only implementation images are admitted. Reference-only
images, effectful type initialization, module initialization, vararg/unmanaged/synchronized methods, IL
exception regions, array/length/native operations, unsupported opcodes and
cross-module dependency calls remain closed. Nonrecursive exact same-module
dependencies are supported. Broader typed metadata coverage remains required
before the Phase 2 exit.

An implementation type initializer containing only Nop instructions and Ret is
admitted after checking the captured PE body. Exception regions, locals,
synchronization and declarative security reject this empty-initializer admission.
Other initializer bodies remain unsupported by concrete lowering.

Total IR string equality denotes reference identity, matching its native Ref
encoding and implementation-IL ceq. Legacy IR retains string content equality.
The source semantic table still rejects non-null string content comparisons.
Concrete string witnesses preserve aliases; distinct empty Ref tokens abstain
because the decoder's CLR construction would return the same empty string.

The compiled metadata conversion matrix checks all 162 source/target/mode pairs
across the nine integral C# types, including char, in checked and unchecked mode.
Each pair round-trips the real collector artifact and compares original IR with
compiled execution at source limits, target limits and adjacent values within
the source domain. These boundary checks supplement the seeded metadata oracle;
they do not exhaust every input value or qualify unsupported IL operations.

The Total source candidate also admits object, string and single-dimensional
arrays of scalar, object or string elements for null, copy, identity and length
observations. Reads from scalar arrays with int/uint indexes also emit ordered
NullReference and IndexOutOfRange faults, after evaluating both operands.
Native element functions preserve each observed scalar element in decoded
array witnesses, including aliases. Array writes, reference-element reads,
built-in string content comparisons, general reference casts and allocation
remain closed. Long/ulong indexes remain closed because their native-width
conversion can overflow before a null check and source evidence does not bind
that architecture. Length reads emit guarded NullReference faults in bodies
and safe conditions in clauses. The native
candidate uses one uninterpreted Ref sort and a length function with the full
0..Int32.MaxValue range; null has mathematical length zero, while source reads
still fault. SAT model decoding preserves array and object aliases, creates
concrete string and array witnesses, and charges their size before allocation.
An oversized witness abstains without restricting the proof input domain.
These source controls do not qualify reference implementation IL or complete
the Phase 2 exit.

The fuzz campaign's schema 7 result separately accounts for generated Total
programs. Each case compares compiled execution with the original Total IR
interpreter and checks a true and false postcondition through the native
callable solver and owned replay. Cases cover scalar wrapping and checked
overflow, finally return capture, source calls, Boolean bodies, finite loops,
and reference null, identity, length and scalar-array read observations.
Array-read cases use seed-derived elements and indexes, and have a separately
validated coverage count. Loop postconditions relate
the result to the current parameter after a cut; compiled and original execution
still compare the exact finite result. Escaping null faults must also have no
modeled normal return. The campaign requires all cases to agree, and runs of
at least 1,000 cases require every body category and all thirteen input types.
The fuzz executable references Worker to exercise its callable solver through
a trusted source tooling adapter; Worker still has no compiler-facing dependency.
The separate metadata-program oracle compiles scalar implementation images,
captures them through the real collector, round-trips the artifact, and compares
compiled caller execution with original IR and native true/false goals.
Its coverage requires every case to agree; runs of at least 1,000 cases require
all ten scalar types and seven recipes: wrapping addition, checked subtraction,
multiplication, branches, same-module dependencies, division and remainder.
Source caller handlers catch modeled arithmetic faults. The tooling executable
references the collector and attributes for this adapter; the worker remains
compiler-neutral. These generators do not qualify reference implementation IL,
IL exception regions, native-width index conversions, heap writes or the
remaining Phase 2 exit conditions.

Ordinary reducible scalar loops use the owned proof/search route below.
Checked scalar Add, Subtract, Multiply and
unary Plus/Minus use the same wrap value and guarded overflow rules in bodies
and clauses. Increment/decrement and `+=`, `-=`, `*=` compound assignments preserve
the resolved promotions and checked storage conversion. Earlier operand effects
remain visible when the operator faults; its own storage write occurs only on
the normal edge. Overflow routes through the same catch/finally machinery as
other scalar faults. These rules use the existing integer widths without a
wider numeric type or a frontend solver dependency.

The candidate region route supports scalar faults, throw-null, ordered canonical
runtime catches, and nested rethrow. Each lexical catch snapshots both the
original exception kind and throwing operation, so an inner handled fault cannot
replace an outer rethrow's provenance. Each finally region is lowered once
with its own continuation selector. Return values are captured before finally mutates
storage; exceptional continuations resume the original throw explicitly after
mixed normal and exceptional joins. Construction is bounded and cancelable, and
the original graph may be cyclic. Scalar catch filters search in lexical order
before any finally unwind. A false or faulting filter retains its earlier storage
effects and resumes search for the original exception. The selected handler owns
only the finally regions left on its route; a fault during unwind replaces the
original exception and cancels the handler. Nested and sibling finally regions
form inner-to-outer continuation chains, preserving captured returns throughout.
Exception-object locals, object construction, unsupported calls, and heap effects remain
incomplete. Scalar loops through catches/finally and generated cycles that search
a shared filter again retain their original bodies for replay. Native enrollment
uses the exception-component abstraction below; unsupported instructions and
invalid pending-exception or missing-storage paths still close.
Owned scalar local storage receives typed initial values at the live body entry,
after the complete contract prologue. Valid C# definite assignment makes those
values unobservable; they preserve normal-only local assignments across a shared
finally join. Canonical parameter and Old identities retain their existing rules.

The standalone passive VC builder owns one immutable Total scalar candidate.
It derives SSA writes, guarded joins, block and edge reachability, and normal
return obligations from owned scalar programs. Unchanged incoming
versions are forwarded. Entry feasibility activates guarded Requires and the
intrinsic typed domains; body Assume filters point reachability and retains its
own core provenance. Construction is bounded and cancelable. Each callable
uses one solver session with the existing method resource meter. Refutations
must validate every SSA assignment and replay the original body and guarded
postcondition; displayed models contain canonical entry parameters only. The
source test adapter rejects mixed contexts and incomplete lowering. Legal
prologue Assume clauses carry their static Safe-and-Value filter in the original
Total program, with used UserAssume IDs retained for conditional and vacuous
proofs. Their elided arguments do not emit runtime evaluations or throws.
Late or conditional placement remains unsupported. Decoding binds each filter
to its owned clause and requires reachable prologue placement before body
execution. The optional Total artifact and shadow comparison reuse decoded
preparations; authoritative worker responses and cache claims remain legacy.
`TotalCallableVerifier` owns candidate orchestration independently of legacy
lowering success and shadow reporting. It publishes each completed claim with
canonical entry evidence and assumption usage before starting the next claim,
so later cancellation preserves earlier kernel-validated results.
The native verifier may also borrow a worker-owned backend and its matching
method resource budget. It never disposes that backend or resets the shared
budget. Both full-callable and independent entry queries use ProofKernel on
the supplied session; default standalone verification still owns its session.
The internal `SharpProofWorker.CreateNative` qualification route uses one native
session per callable factory and keeps resource counts monotonic across each
lane. It admits independently prepared typed callables even when legacy lowering
failed, projects native claims into ordinary worker responses, and retains
completed claims across a later method interruption.
The same native route retains settled results after project timeout or caller
cancellation, including earlier completed callables. Only unfinished claims
receive interruption reasons; the final response is classified from its retained
evidence and is not written to the verification cache during interruption.
Compiler effect evidence uses separately published entry feasibility; an unconstrained effect-only entry
needs no SMT query. The public worker creation path now selects native typed
verification through both the static factory and injected-backend constructor.
`NativeExceptionEffectVerifier` is the Phase 3 qualification entry point for
decoded effect-only callable artifacts. It checks entry feasibility and then
uncaught-exit reachability using the passive SSA session and method budget.
The compiler captures claim-owned allowed exception kinds using exact bound
core-library hierarchies. The codec validates claim ownership, canonical kinds
and the entry/body boundary. The worker never matches short exception names or
reconstructs Roslyn types. Missing optional constraints remain Unknown; declared
DoesNotThrow and AllowedExceptions claims share one callable session and meter.
Native admission accepts original generic declarations when every parameter and
return value has a supported domain and every operation lowers completely.
Nominal references support identity and null checks; type-parameter values,
user-defined equality, reference casts and unmodeled member operations abstain.
Constructed method symbols cannot borrow an original declaration's parameter
bindings. Constraint validity is captured before legacy language admission can
downgrade its published claim. Invalid allowances still have no native constraint,
including at contradictory entry. Model replay requires exact type and factory
ownership even when nominal display names match.
Exception kinds use predecessor-guarded phi facts. Refutations require original
IR replay and are compared with compiled C# execution in artifact qualification
tests. Bounded loop UNSAT and call abstractions cannot establish effect proofs.
The compiler effect assembler remains authoritative during this rollout.
The `SharpProof.Gates exception-shadow` command separately instruments the pinned
200-method OSS corpus with DoesNotThrow. It compares raw legacy analyzer
outcomes, published compiler evidence, and native results after production
artifact serialization and decoding. Every selected method remains in the
coverage and old-proof denominator, including unsupported bodies. `--limit 10`
runs a sorted sample; samples cannot satisfy the exhaustive retention gate.
Concrete native refutations are independently compiled and invoked when their
static, nongeneric scalar/string/null inputs can be represented. Unsupported
invocation shapes remain explicit oracle gaps. Runtime confirmations concern
counterexamples only; they do not establish universal proofs. The report
separates comparison success from the 95% old-proof retention gate and does not
qualify allocation, write, lock, capability, or call-precondition effects.
Typed `Allocate` instructions capture core `new object()` expressions and
scalar boxing to `object`. An optional reference target receives a fresh nonnull
identity, with guarded freshness facts against tracked references of the same
type. Replay creates a distinct identity on each execution. Boxing lowers its
operand before allocation; boxed contents and unboxing remain unsupported.
The allocation table also recognizes explicit delegate construction for static,
nongeneric method references. It creates a fresh delegate value without calling
its target. Cached method-group/lambda conversions, capturing closures and
receiver-dependent delegate construction retain incomplete lowering. The production artifact preserves type and site ownership; passive
SSA records guarded reachability and original-program replay observes the
allocation site. Native ZeroAllocations qualification excludes reachable
throw sites as potential implicit allocations, including caught faults. String concatenation,
body abstractions and incomplete source static/module initialization abstain.
Validated effect claim IDs are captured before legacy language admission and
decoded as a canonical owned set. Missing admission data abstains even at a
contradictory entry. Compiled C# tests independently measure thread allocation
bytes on concrete paths after delegate construction and JIT warmup.
`SharpProof.Gates allocation-shadow` measures this replacement on the same
pinned universe with ZeroAllocations annotations. Concrete feasible entries
are replayed before invoking independently compiled source through a direct
delegate. Arguments and warmup are outside thread-allocation measurements.
Generic methods use bounded int and string representative closures; unsupported
inputs and incomplete executions remain explicit gaps. Independent conservative
IL reachability checks allocation, throw and call sites, including filter handlers.
A potential IL site remains a gap when preconditions may exclude its edge.
Runtime observations do not establish a universal proof. This command does not
qualify an authority switch or complete the implicit-allocation table.
Typed Write events carry Local, Parameter, Field, Static, Element or
Unknown regions and owned operation sites through artifacts, loop transformation,
passive SSA and original-program replay. Source local assignments, increments
and compound assignments emit Local events, including by-value parameter
rebinding. Primitive source field stores emit Parameter, Field or Static events,
capturing receivers before the RHS and checking null after RHS evaluation.
Volatile, readonly, external and initialization-sensitive stores abstain; heap
reads and array stores remain incomplete. The native
purity shadow forbids reachable nonlocal events; allocation remains compatible
with purity. The allocation and purity routes share claim admission, entry
feasibility, solver budgets and witness replay in NativeEffectSiteVerifier.
`SharpProof.Gates purity-shadow` compares every pinned method against raw legacy
purity results; independent mutation oracles remain explicit gaps. Compiler
effect authority remains in place.
Typed Lock instructions record validated Monitor.Enter/Exit attempts, including
C# lock statements and source-helper frames. Concrete replay observes the
attempt and stops before synchronization. Native purity excludes reachable
attempts; postcondition, exception, allocation and normal-completion proofs also
require them to be unreachable. These guards preserve the boundary around
blocking, runtime allocation and exceptions; no monitor-state model is implied.
A deliberate replay stop at a Lock instruction is CounterexampleNotReplayable,
so it remains a complete semantic Unknown and supports validated cache reuse.
The fresh receiver in the lock-local worker golden now exposes synchronization;
its previous postcondition proof is explicitly triaged as Unknown.
Preconditions can exclude a lock path. Only the compiler's matched lockTaken
Boolean local is admitted among unnamed generated locals.
Native lanes do not instantiate the legacy callable verifier or predicate
executor. Temporary legacy comparison fixtures explicitly select their internal
route; legacy implementation retirement remains a separate gate.
The VC golden stage checks typed public verification for the retired raw
executor and obligation-builder fixtures: joins, faults, calls, domains, and
assumption usage. Native construction and session tests retain cancellation,
state-growth, graph-limit, and method-budget guards. Expression-bodied callees
bind through transparent checked and parenthesis syntax wrappers while retaining
Roslyn's overflow semantics.
Legacy comparison fixtures explicitly construct the legacy backend. An explicitly
native worker ignores the legacy shadow switch, so qualification cannot silently
run an additional legacy comparison or emit its reports.
A trial public-factory switch exposed remaining parity gaps in API specification
models (string concatenation and `Array.Empty` result facets). The native
`Array.Empty<T>()` model now covers every supported scalar, string and object
element type. It uses the approved compiler-resolved framework symbol and an
exact typed empty-array term with non-nullness, zero length and cached identity.
SMT encoding supplies those intrinsic facts, and decoded aliases replay as the
same concrete empty array. Compiled C# runtime comparisons and artifact
round-trip validation cover the model. Native two-string concatenation also uses
the approved framework symbol and preserves argument order and faults. String
literals retain non-nullness, exact UTF-16 length and canonical empty identity.
Concatenation retains empty-operand aliases and non-nullness; two nonempty operands
have no content or input-related length facts in SMT. The VC builder forwards
concatenation value expressions for nullness and length observations instead of
requiring SAT replay to reproduce a fresh allocation's identity. String content
comparisons remain unsupported at binding. VC admission also rejects non-null
string identity comparisons in callables that concatenate strings; existing
metadata identity transfers without concatenation retain their support.
Runtime comparisons cover nullable, empty and nonempty operands and aliases. Existing
proof expectations remain requirements for the transition; they have not been
weakened to accommodate these gaps. Typed scalar improvements also change several
legacy outcomes and proof cores. Public-worker tests now require exact typed
conversion proofs, replayed scalar and reference counterexamples, explicit fault
edges, and native normal-completion selectors. Integer domains are intrinsic
bitvector constraints and require no legacy domain selectors. An exact
zero-dividend encoding avoids expanding 64-bit division while retaining Z3's
division-by-zero completion; C# postcondition faults remain separate safety goals.
Native Int32 `Math.Abs` calls use the approved compiler-resolved API symbol and
the shared scalar semantic rule. The rule computes typed absolute values and
emits an explicit overflow edge for `int.MinValue`; arguments are evaluated
before that edge, and caller exception handlers remain active. Boundary cases
are compared with compiled C# execution. A source type named `System.Math` does
not receive this model.
The explicitly selected scalar specification pack also lowers into typed native
terms. Catalog version/hash, approved assembly identity and signature checks
remain in force. Its current Int32 `Math.Max` declaration keeps 32-bit signed
comparisons, evaluates arguments in source order and preserves their parameter
ordinals. Unsupported term forms or mismatched argument types abstain. This
route does not invoke the legacy relational-summary builder; disabled packs do
not provide native call models.
Source inlining discards callee specification assumptions while preserving the
caller's own declared assumptions. Elided callee contract calls stay elided;
emitted calls return to ordinary source/implementation-IL lowering, including
argument evaluation and faults. An emitted argument fault can reach the caller's
handler or eliminate normal return. No callee assumption becomes an untracked
caller premise. Unsupported emitted calls remain incomplete, and asynchronous
callee completion remains unsupported.
Native contract binding includes parameter and return `Positive`, `InRange`, and
supported reference `NotNull` attributes. Numeric predicates use the value's
signedness and width, including `ulong`; bounds outside the scalar domain fold
to Boolean comparisons rather than wrapping. Attribute source spans and manifest
evidence remain bound through the version 28 compiler artifact. Direct clauses
precede return attributes, and parameter attributes use immutable entry values.
Companion clauses bind in a separate clause context and substitute
parameters by ordinal into the target's entry, current, and Old identities.
Companion source spans and evidence remain bound to the manifest. Verification
and concrete replay execute the original target body; the companion body supplies
only its validated clauses. Ordinary nonvirtual instance bodies may use scalar
parameters and supported reference observations. Instance companion binding
omits its receiver parameter only after validating its exact declaring type;
clauses that read receiver state remain unsupported. Instance calls inside bodies
and virtual or override instance roots remain unsupported.
The shadow comparison consumes the same Total-to-worker claim projection needed
by the authority transition. It names counterexamples from canonical Total entry
parameters rather than legacy variable ids, preserves full unsigned values, and
quotes string displays to retain empty strings, whitespace and UTF-16 units in
the existing nonblank model wire format. Missing or mistyped inputs, foreign
assumptions and incomplete publications cannot publish a proof or refutation.
Normal completion has an explicit SSA definition, so even a body with no return
instructions carries kernel-validated normal-exit provenance in a vacuity proof
core. This total fresh definition does not constrain the admitted input domain.
Entry feasibility is published separately, before the normal-completion query,
and its original kernel evidence remains available after that query. A reachable
entry remains feasible when the body always throws or bounded return search is
inconclusive. Only the Requires query can establish contradictory entry; its
core maps to the manifest precondition ids. Manifest version 24 carries a separate
body-free Total entry graph. Requires binding can succeed independently of an
unsupported body or Ensures expression. Decoding rejects bodies, result roles,
non-Requires clauses, missing or foreign assumption ids, and predicates outside
canonical entry inputs. Body Assume clauses cannot constrain this entry query.
The independent native query preserves cancellation and existing resource budgets.
Effect routing still uses legacy entry evidence pending authority qualification.
Entry qualification enumerates every callable from the 54 source-comparison
fixtures and all 64 worker golden sources, including fixtures without claims.
It records enrollment and Unknown results, rejects contradictions between known
legacy/native results, and requires every known legacy entry result to remain
known. Native entry cores may use only manifest precondition ids. This bounded
qualification does not establish postcondition or effect authority retirement.

When concrete body lowering fails within the existing construction bound, a
callable with only scalar parameters and result may carry an explicitly marked
body abstraction. It preserves Entry and Old, forgets every mutable parameter
and result, and represents an arbitrary normal return. Decoding validates that
exact shape; reference signatures and body Assume clauses remain excluded.
UNSAT may prove a universally valid postcondition. SAT establishes neither a
normal-return witness nor a refutation, and publishes no model or vacuity.
This explicit marker leaves ordinary concrete replay's unread-approximation
behavior unchanged. Postcondition qualification also compares every claim in
the source and worker golden universe and rejects each lost legacy proof,
in addition to aggregate proof counts and soundness disagreements.
The native entry and postcondition gates compare directly with the qualified
legacy baseline captured at commit 86fa6ea90. The baseline accounts for all 173
callables and 253 postconditions, including Unknown results. Postconditions use
fixture, callable signature and clause ordinal as their key; metadata build
hashes are intentionally excluded. This keeps proof-retention and contradiction
checks usable after the legacy execution path is retired.

Ordinary reducible scalar loops retain their original cyclic Total program for
concrete replay. The worker derives two bounded encodings from that same owner.
For proof, each natural-loop header havocs all scalar storage written by its
loop and cuts back edges. Every finite original return is represented by its
last loop iteration, including zero trips, nested loops and control exits; Entry
and Old remain immutable. Only UNSAT of this overapproximation establishes a
proof or absence of normal completion. Cut stops are lowering facts, never user
assumptions. Ordinary irreducible loops and point Assume instructions inside
cycles remain unsupported.

Cyclic components containing modeled Throw flow use a separate proof tier. Each
external entry (or program entry inside the component) has a nondeterministic
router that havocs the union of written scalar storage and selects any component
block. Original blocks remain shared; DFS cycle-closing edges are cut. A finite
execution is represented by its suffix after its last cut edge: the router selects
that target with its actual state, and the suffix crosses no deleted edge. Entry,
Old and every unmodified variable remain exact. Internal choices and stops have
Lowered provenance, never UserAssume IDs. No pending exception is synthesized;
an encoded naked ExceptionalExit or missing nonmodified binding rejects
enrollment. Router expansion is charged and capped before allocation. This tier
establishes normal-return/poststate properties only and does not preserve skipped
effect-site reachability. Its abstract SAT models never refute an original clause.

Counterexample and normal-witness search unrolls at most four back-edge
traversals. SAT must validate all SSA facts and replay the original cyclic body
within the existing 4096-step bound. Bounded UNSAT establishes neither a proof
nor vacuity. If that search is inconclusive, an independently established
abstract Ensures proof may still be reported with unknown normal feasibility
and no vacuity. A completed bounded search reports Unknown/SolverIncomplete
and counts as checked; skipped, interrupted or refused queries stay unchecked.
Both encodings share one solver session and the existing method resource meter.
Cyclic artifact validation computes finite pending-throw and body-start states,
rejecting every body path that re-enters the contract prologue or initialization.

The artifact is trusted build output. ArtifactValidator checks its digest,
validates semantic shape and claim/type/IR bindings, and decodes each graph once.
The prepared snapshot passes through the launcher to the in-process worker.
Before cache access the worker binds it to the current artifact digest, budgets,
and runtime key. Compiler diagnostics fail as CompilationFailure; malformed IR
or an expression-depth mismatch fails as CompilerManifestMismatch. The worker
has no Roslyn dependency and does not reconstruct a compilation or reread source
and reference files.

ProofKernel alone constructs proof outcomes. For callable counterexamples it
checks the backend model, assumptions, and transformed goal, executes the
concrete IR path, reconstructs result and prestate values, checks source integer
domains, and evaluates the original Ensures clause before creating Refuted.
Unsupported instructions on other paths do not block replay. Executed calls
without a concrete registered host become CounterexampleNotReplayable;
inconsistent replay becomes CounterexampleReplayFailed.

Modeled replay tracks approximation values only when they are actually read,
including reads in the original postcondition and its explicit Total-mode
guard. Overwritten values and untaken lazy operands do not count as reads.
Input havoc binds to the original entry model; generic spec-result havoc
cannot authorize Refuted. Total replay without a successful clause guard
abstains, so deterministic completion values cannot hide an undefined clause.

Effect replay remains an interpreter of compiler-produced unconditional events.
It derives effects, capabilities, and exact exception hierarchy, evaluates the
selected constraint, and matches the witness. It does not execute user code or
invoke SMT. Conditional and may-only conflicts remain typed Unknown.

Cache schema 15 reuses every valid complete response, including effects,
semantic Unknown, and empty claim sets. The key combines the artifact digest,
worker/Z3/API-spec identities, and every semantic budget. Cancellation, timeouts,
backend failures, and infrastructure failures are not cached. Reporting policy
does not change the semantic cache payload.

RunVerifier starts one worker process with a hard deadline and kills its process
tree on timeout or cancellation. The worker's project budget includes launcher
artifact preparation and uses elapsed-time checks as well as cancellation.
The child prepares manifest, request, optional SARIF, and result in its private
invocation directory. RunVerifier holds sorted leases for the canonical stable
members through promotion and validation against that private invocation. Each
file is replaced atomically, with the result written last. Standalone launcher
publication, invalidation, and reset use the same leases. Their lock sidecars stay
in place to keep cooperating owners on the same lock. Before rewriting child
destinations, publishers reject outputs inside the declared worker runtime,
including existing leaf symlinks into it. Invalidation uses the same path check.
Input and publication failures retain the launcher's exit codes and messages.
Docker owns CPU and memory isolation.

## Activation and release gates

`SharpProofProfile` accepts `advisory`, `strict`, and `off`; the package
default is `advisory`. `SharpProofFeatures` accepts `effects`, `contracts`, and
`all`; the default is `all`. A custom host can provide the equivalent
compilation-global `sharpproof_profile` and `sharpproof_features` keys. In a
package build, `SharpProofProfile` remains an MSBuild setting because it
controls verifier activation and analyzer/generator inclusion; a global
`sharpproof_profile` must match it. The `off` profile omits analyzer/generator
items and constructs no analysis session. Unsupported unannotated analyzer
callables are silent, while
unsupported explicitly selected callables report SP0047.

The advisory analyzer has a conservative compilation-start fast path for
contract-free, unselected source. It retains configuration validation and
`SHARPPROOF_CONTRACTS` rejection while skipping semantic-session construction
and per-method callbacks. Final compiler-artifact collection is a separate
build-only analyzer and is not loaded by ordinary advisory builds. Strict mode
never takes the fast path. The activation probe is part of the declared
discovery trusted computing base; new selection or implicit-call syntax must
extend the probe and its regressions in the same change.

The advisory activation probe distinguishes contract/attribute candidates
from ordinary call-bearing code. A candidate compilation retains method
attribute, clause-placement, intrinsic, rejection, suppression, subset, and
effect processing. For otherwise contract-free source, the probe reads
portable-executable custom-attribute metadata without populating Roslyn symbol
caches. It registers operation-block precondition screening only when a
referenced assembly contains a closed SharpProof parameter or return contract;
compilation references receive the equivalent symbol check. Thus external
closed preconditions remain visible to unannotated callers, while ordinary
source and BCL calls create no semantic session. Contract inventories,
companion resolution, binders, API specifications, and effect analysis are
independently lazy and are created only on first demand.

Before allocating a precondition CFG, a sound negative screen walks calls
owned by that callable and uses the same cached binder as full analysis. It
skips the CFG only when every target binds successfully with zero entry
clauses. Operation-root or binding failure, a possible entry clause, and
relevant static initialization all retain full fail-closed analysis. The
activation probe, lazy compilation model, pipeline, screen, and binder-owning
session are part of the declared effect-analysis trusted computing base.
When the containing operation tree has a relevant nested owner, the pass
creates the root CFG once and follows Roslyn local-function and anonymous-
function child graphs recursively. Each callable is deduplicated by compiler
symbol, analyzed under its own flow state, and records its own outcome.
Expression-tree lambdas are treated as quoted code and remain unknown rather
than producing an execution diagnostic.

The verifier is optional in advisory builds and mandatory in strict builds;
explicitly setting `SharpProofVerify=false` with `strict` is a configuration
error. `SharpProofVerifyPolicy` controls incomplete selected analysis;
`SharpProofAssumptionPolicy` controls SP0048 reporting for user assumptions and
trusted evidence. A refutation, malformed response, backend/replay failure,
containment failure, or other infrastructure failure is fatal regardless of
policy. The preview interface rejects the removed `SharpProofMode` and
`all-experimental` compatibility inputs.

The current gate includes:

- exhaustive Roslyn operation-kind and architecture checks;
- compiler-enforced banned APIs;
- lattice laws and finite-CFG checks;
- executable witnesses and mutation probes for every claim-bearing API-spec
  facet and postcondition;
- IR/C# and IR/SMT differential oracles;
- replay and unsat-core checks;
- snapshot-corpus and metamorphic invariance;
- cache/concurrency/cancellation determinism;
- worker/package consumer smoke checks;
- fixed-seed fuzzing and a five-minute corpus wall-time budget.

Unannotated advisory latency samples alternate real compiler-only and
SharpProof-imported MSBuild rebuilds under the repository-selected SDK. The
fixture contains ordinary source and BCL calls, so it exercises the
no-precondition callable screen rather than only the compilation-start fast
path. The package policy separately proves that `SharpProofProfile=off` omits
analyzer items and verifier invocation, while retained-memory checks exercise
the call-free unannotated advisory analyzer driver and its no-session fast
path. The worker is isolated in
`SharpProof.Verifier`; the portable `SharpProof` package contains only
analyzer/generator assets and depends exactly on `SharpProof.Attributes`. Each
package has a portable-PDB symbol package with SourceLink, and the package
workflow records exact package identities. The corpus reports explicit, silent, and total
semantic Unknown rates. A `Supported` case producing `Unknown` or
`SilentUnknown` fails with zero tolerance. The supported-case and supported
OSS-method floors cannot decrease, while total and per-reason Unknown counts
for `IntentionallyUnsupported` cases cannot exceed the checked-in ratchet.

The active contract is `eng/acceptance`.
