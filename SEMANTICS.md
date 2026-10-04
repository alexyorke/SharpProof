# SharpProof Semantics

This document defines the soundness boundary for SharpProof preview analysis. If an
implementation detail, optimization, diagnostic, or document conflicts with this
file, this file wins.

## Outcomes and evidence

SharpProof has three semantic outcomes:

- `Proven` means that the goal follows from lowered program facts, resolved API
  specifications, verified contracts, and any explicitly declared user
  assumptions.
- `Refuted` means that a replay-validated concrete counterexample violates the
  goal. The current effect analyzer does not emit this outcome.
- `Unknown` means that SharpProof cannot establish either result within its
  supported language, models, or resource limits.

Unsupported syntax, missing or ambiguous specifications, approximate facts,
budget exhaustion, solver timeout, undefined postcondition evaluation, and
unsupported encoding produce claim-level `Unknown`. A candidate model that
depends on a modeled call which the independent interpreter cannot execute is
also `Unknown`; it is never reported as a refutation. Backend unavailability,
infrastructure failure, malformed backend output, containment failure, and a
failed replay of an otherwise replayable counterexample make protocol version
12 mark the whole run `Failed`; these conditions are fatal under every build
policy. Unsupported unannotated analyzer callables remain silent. Explicitly
selected unsupported callables produce SP0047.

Approximate facts cannot be promoted to assumptions. A proof is valid only when
its evidence core contains lowerings, resolved specifications, verified
contracts, or explicit user assumptions. A counterexample is valid only after
replay against the executable program model. Failed replay is an encoder defect,
not a program defect.

Caller cancellation remains cancellation. It is propagated by the analyzer and
becomes run status `Canceled`, not a semantic claim outcome. A project boundary
becomes run status `TimedOut`. Cancellation, timeouts, failures, budget
exhaustion, and all `Unknown` outcomes are not reusable proof-cache entries.

## Accountable selection and worker runs

Worker protocol version 13 separates `WorkerRunStatus` from
`WorkerClaimOutcome`. The compiler-symbol-based manifest is sealed before
verification. It contains every selected callable, every discovered
postcondition, and every selected effect-attribute occurrence with a stable
semantic claim ID, evidence kind, dense ordinal,
and mapped source location. A valid response has exact manifest/result
equality: no claim may be missing, duplicated, invented, or assigned to the
wrong callable.

Selection is relative to the compiler artifact's `WorkerFeatureSet`, which is
populated from `SharpProofFeatures`. `Contracts` includes contract annotations,
assumptions, and postcondition claims while excluding effect-only annotations.
`Effects` includes effect-selected callables while excluding postcondition
claims and contract assumptions. `All` is their union. Strict accountability
applies to everything selected by that feature set; disabled features are not
silently counted as analyzed. Effect-only annotations on abstract, interface,
and `extern` declarations have no executable body and report
`BodylessEffectContractNotEnforced`; they do not apply to implementations, so
each concrete implementation must be annotated directly. Repeated effect
attributes normally receive distinct manifest claims. Repeated
`[AllowedExceptions]` attributes are the exception: their allowed types are
unioned and emitted as one combined claim at the callable location, with all
occurrences contributing to its stable identity and evidence.
Z3 decides every effect claim over the callable's Total program: a proof is a
complete may-effect summary, and a refutation names a violating site reached by
concrete replay. The compiler only declares each effect claim and its
constraint; it runs no effect analysis of its own. A trusted complete boundary
on a bodyless declaration is published as declared.
Allocation can refute `ZeroAllocations` or an `EffectContract` that excludes
`Allocates`; a write or lock can refute `EnforcePure`, `AllowedCapabilities` or
`EffectContract`; and a throw can refute `DoesNotThrow`, `AllowedExceptions` or
`EffectContract`. Observable purity still permits fresh allocation.
An `EffectContract` on an implementation bounds every effect of the body:
exceptions need `Throws` and a listed type, allocation needs `Allocates`,
reads and writes of fields and elements need every state flag of their kind
(a store through a parameter needs only `WritesArgumentState`), locks need
`Synchronizes` and the Synchronization capability, and opaque calls need their
specified effects and capabilities.

A violation whose replay reads an approximation is
`Unknown(CounterexampleNotReplayable)`. Effect claim results are never stored
in or reused from the semantic cache.

Exception constraints and exact witness hierarchies use the type-reference
documentation ID qualified by the full compiler assembly identity: name,
version, culture, and public-key token. Compiler classification and worker
artifact validation therefore cannot confuse types imported through aliases
from distinct same-simple-name assemblies. Type-reference IDs preserve
constructed generic arguments. Exact user-defined generic exception witnesses
remain outside the admitted direct-candidate subset and therefore remain
`Unknown`.

Every selected callable has explicit `Complete` or `Incomplete` coverage.
Every manifest claim has exactly one `Proven`, `Refuted`, or `Unknown` result.
The worker must never fabricate a clause-zero claim to describe a callable
failure. User assumptions and trusted boundaries have stable evidence IDs and
remain visible whether or not they enter an individual proof core.

A `Proven` postcondition also records explicit vacuity evidence when its
preconditions are proven unsatisfiable or when the bounded executor has no
modeled normal return. Nonliteral normal-completion predicates are checked
under non-user assumptions, and an inconclusive check prevents `Proven`.
These are respectively
`ContradictoryPreconditions` and `NoModeledNormalReturn`; ordinary
non-vacuous proofs record `None`. The evidence is part of the canonical JSON
claim result and therefore survives cache reuse and SARIF projection.

A `Complete` run means the worker finished and produced a structurally valid
accounting response; it does not mean every claim was proven. `TimedOut`,
`Canceled`, and `Failed` are run states, not claim outcomes. The launcher's
verification and assumption policies decide how a valid complete response
affects the build, but cannot turn a failed run or refutation into success.

## Abstract-domain concretization

For every abstract value `a`, `gamma(a)` is the set of concrete values or
execution traces represented by `a`. Domain order is semantic inclusion:
`a <= b` only when `gamma(a)` is a subset of `gamma(b)`. `Bottom` represents
the empty set, `Top` represents every value in the domain, `Join` contains the
union of both operands, and `Widen` must contain both its previous value and its
next value. `Havoc(Bottom)` remains `Bottom`; every other havoc is `Top`.

The interval/congruence value `[lower, upper] mod m = r` represents every signed
64-bit integer within the optional bounds whose normalized remainder modulo
`m` is `r`. Modulus zero represents the exact singleton `r`; modulus one
imposes no congruence restriction.

The nullness domain represents `{}`, `{null}`, `{non-null references}`, and
their union. A sequence-cardinality value represents sequences whose
non-negative length belongs to its interval and whose emptiness agrees with
`Empty`, `NonEmpty`, or `Top`.

An effect summary represents all concrete traces whose reads, writes,
allocations, capabilities, escaping exception types, and termination behavior
are contained component-wise by the summary. An incomplete or uncertain
summary remains an over-approximation, but it is not eligible to establish an
absence-of-effect proof. Thus larger abstract values lose precision; they never
authorize a stronger result.

## Contracts and trust

Postconditions use partial correctness: they apply to normal returns. Divergence
does not itself violate a postcondition, observable purity, or `DoesNotThrow`.
A postcondition is established only when its bound C# expression is both
defined and true on every normal return; a possible exception while evaluating
the postcondition produces `Unknown`. Verification assumptions include the
lowered body's normal-completion condition, so throwing executions are not
mistaken for normal-return counterexamples. Successful evaluation of every
executed assignment right-hand side contributes to that condition even when
the assigned value is never read.

`Contract.Assume` is explicit user evidence and must remain visible as
`UserAssumedJustification`. It also refines managed effect flow when an effect
claim is selected; effects-only compiler artifacts retain the clause as
`UserAssume` evidence so the assumption policy can report it. A diagnostic
suppression changes reporting only; it cannot sharpen a summary or proof. A
trust declaration can authorize only an explicitly declared contract or effect
summary. Trust without such a declaration leaves the result `Unknown`. A
complete external API specification or trusted effect summary describes the
whole observable call boundary, including any type initialization caused by
that call.

Compiler-elided `Contract.Requires`, `Contract.Ensures`, and `Contract.Assume`
calls do not evaluate their arguments. A direct runtime invocation of
`Contract.Result<T>()` or `Contract.Old<T>(...)` is invalid and may allocate and
throw `InvalidOperationException`; their effect specs describe that direct-call
behavior. Defining `SHARPPROOF_CONTRACTS` emits calls to clause methods that do
not check conditions, so the symbol is reserved and unsupported in every
profile; package builds reject it in project constants, and active analyzers
report source-local or generated definitions as SP0025.

Contract clauses and annotations are evidence only when their symbols resolve
to the `SharpProof.Attributes` assembly identity and built-DLL SHA-256 payload
matching the analyzer, and the `Contract` type has the exact supported shape.
Each clause method must carry exactly one real
`Conditional("SHARPPROOF_CONTRACTS")` attribute. A source, project, identity,
payload, or shape lookalike is selected only for accountable abstention; it
contributes no assumption, postcondition, effect, suppression, trust fact, or
compiler-bound ghost specification.

Callee postconditions may be assumed only after verification or explicit trust.

Preconditions declared only on an override or an interface implementation are
not visible through base or interface dispatch. The analyzer reports SP0024 for
such a local precondition unless an equivalent valid `Requires` contract is
already declared on an overridden or implemented member. Callers must use the
base or interface contract that is visible at the dispatch site.

## Direct callee verification

Source callees and captured implementation IL expand directly into the
caller's Total program. Verification uses the composed body and does not
import callee contract premises. Calls require direct, supported dispatch and
bounded exact lowering. Recursive expansion, unsupported operations, and
resource exhaustion abstain or retain a conservative body abstraction.

Implementation IL requires an exact file-backed captured PE whose metadata
matches the reference Roslyn compiled against. Reference assemblies and
facades cannot provide implementation authority. Verification applies to that
captured implementation; deployment must resolve the same binary. The bounded
IL subset is not a general IL interpreter or metadata effect inference.

Audited scalar specification packs require explicit selection through
`SharpProofSpecificationPacks`. Embedded pack data has exact method signature,
assembly-name, and public-key-token constraints. Consumer files cannot provide
pack authority. Embedded catalog schema 2 includes `dotnet.scalar@1`, which
lowers `System.Math.Max(int, int)` into Total operations. Absent or unsupported
packs do not supply facts. Legacy relational-summary production is retired.

## Effects

Observable purity excludes:

- reads from or writes to ambient state;
- writes to pre-existing state reachable by the caller;
- I/O, synchronization, native code, reflection, and nondeterminism; and
- unresolved effects.

Fresh allocation and writes confined to fresh owned regions are compatible with
observable purity. They are not compatible with `[ZeroAllocations]`.

Compile-time constant and enum-member references are values, not static-state
reads. Built-in compound assignments and increments over properties include
the effects of both the getter and setter. Until the computed stored value is
represented explicitly, a setter entry precondition on that value makes the
computed write incomplete rather than borrowing an operand as false evidence.

An operation that may throw and is not discharged by the analysis makes
`[DoesNotThrow]` unknown. This includes implicit exceptions from dereferences,
array and index access, division, casts, checked arithmetic, and similar runtime
operations. An unmodeled external call has unknown effects.

Exception contracts quantify over synchronous managed exception flows represented
by the admitted C# operation model or an exact API/boundary specification. They
exclude ambient catastrophic runtime-failure channels that have no modeled
exception edge, such as memory exhaustion during allocation, stack exhaustion,
runtime corruption, or process termination. An exception explicitly thrown by
source or declared by an exact or trusted boundary remains in scope even when it
has the same runtime type. Any other ordinary synchronous exception whose
semantics is unavailable makes the summary incomplete; the catastrophic-failure
exclusion is not permission to omit it.

Object, collection, and array initializers are part of the creating expression.
Instance field, property, and event initializers are part of each explicit
instance-constructor summary. Static member initializers are part of an explicit
static-constructor summary. When a source static initializer or static
constructor can run at a method or instance-constructor boundary and its
one-time execution is not modeled there, the summary is `Unknown`.
Metadata static-field access has no callable summary that can cover type
initialization and therefore fails closed.

The analyzer's effect feedback is advisory. It lowers the same Total program
the worker verifies and, by graph reachability alone, finds the first site that
could violate each declared claim: an allocation, a non-local write, a lock, a
state read, an opaque call's effects, or a throw from which the exceptional exit
is reachable. It names that site in the claim's not-verified diagnostic
(SP0002, SP0016, SP0045, SP0046 or SP0052), and reports SP0047 when the body
cannot be lowered. It never proves a claim: its semantic outcome is `Unknown`
except for a trusted complete boundary, and only the worker decides. The
analyzer's definitive SP0013, SP0015 and SP0030 diagnostics remain reserved;
direct violations are accountable through worker claim results and SARIF.
Source callees are inlined as written, so an effect claim does not depend on a
callee's precondition being established; the callee's `Requires` clauses are
separate call-site obligations.

At source call sites, a direct call to an `async` method returning the exact
BCL `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` type does not import the
callee's body exceptions when the result is returned, ignored, discarded, or
stored in a local that is never read. A possible task allocation is retained;
all other callee effects, including writes and possible nontermination, remain
conservative may-effects. Synchronous type initialization remains part of the
call. Await, `.Result`, `.Wait()`, and nested uses retain the callee summary.
Iterator body effects are omitted only when the created sequence is ignored,
discarded, or stored in an unread local. A returned, escaped, or consumed
sequence retains the source summary, so enumeration paths do not lose its
effects. These call-site projections do not expand the supported selected-body
subset or model arbitrary task or iterator propagation.

## Analyzer activation and language boundary

Analyzer behavior is selected through the compilation-global
`sharpproof_profile`/`SharpProofProfile` and
`sharpproof_features`/`SharpProofFeatures` options:

- `advisory` is the default profile. It analyzes selected contracts and keeps
  unsupported unannotated code quiet.
- `strict` requires the verifier, requires proof by default, and rejects
  user/trusted evidence by default. Explicitly disabling verification is a
  configuration error.
- `off` constructs no analysis session, contributes no analyzer/generator
  items through the package, and does not run verification.
- Package builds take `SharpProofProfile` from MSBuild because it controls
  verification and analyzer/generator inclusion. A global
  `sharpproof_profile` must match it.
- feature value `effects` enables effect contracts, `contracts` enables
  call-site contract analysis, and `all` (the default) enables both. The
  package carries the same selection into the compiler artifact and its manifest.

Advisory compilation startup may omit the heavyweight semantic session only
after a conservative syntax and assembly-attribute probe finds no current
selection or call-site trigger. Configuration validation, runtime-contract
rejection, and compiler-artifact collection still execute. Strict mode never
uses this fast path. Adding a new source form that can select analysis or
surface an analyzed call site therefore also requires extending this
discovery policy and its tests.

When ordinary calls activate advisory analysis without any local
contract/attribute candidate, SharpProof runs only the conservative call-site
precondition screen. It checks closed parameter annotations on source and
metadata targets, but ignores source-only `Contract.Requires` and companion
clauses from external compilation references because conditional contract calls
are absent from emitted assemblies. Local source clauses and closed attributes
remain available to call-site analysis. The screen does not initialize contract
inventories, companion resolution, API specifications, or effect analysis
unless a target or selected callable demands them.

Effect and incomplete-proof diagnostics are enabled informational diagnostics
by default. SP0027, at Warning, reports a call whose callee precondition is
false in every state the advisory interval interpreter reaches it with, over
the same Total program the worker verifies (source callees, including plain
constructors, run as written). It covers callable bodies and their local
functions that the IR lowers; member initializers, constructor initializers
and lambdas are not checked. It never proves a call site.
Configuration, contract-usage, and compiler-artifact errors remain enabled at
their declared warning/error severity. The removed
`SharpProofMode`/`sharpproof_mode` and `all-experimental` compatibility inputs
are rejected and do not define the release interface.

A feature diagnostic may be promoted to `Warning` only after at least four
consecutive weekly corpus cycles with no confirmed false positive, no
unexplained canonical snapshot change, and all soundness and performance gates
green. Promotion changes reporting severity only; it cannot enlarge the
supported subset or proof semantics.

Selected callables are verified when the worker can lower them: a body the IR
cannot lower produces SP0047 and stays Unknown. The callable's shape is checked
first: async, generic, by-reference, pointer, function-pointer, delegate,
dynamic, ref-like and unsafe callables are unsupported.

Effect exception flow evaluates catches in source order. A selected handler
can consume an exception or let a rethrow escape, but an exception thrown or
rethrown from that handler is never offered to later sibling catches.
Nonconstant filters and uncertain runtime subtypes retain every feasible
escape path.

The packaged verifier consumes compiler artifact schema version 28 produced
from the final post-generator compilation. The artifact contains the sealed
feature-selected manifest and, for every selected callable, either a typed
lowering failure or portable whole-body CFG/IR with bound clauses, canonical
variables, body-entry state, parameter mappings, and bound API-spec witness
metadata for the remaining legacy API-spec payload. Typed Total graphs carry
composed source and metadata bodies. The artifact also contains admitted
unconditional allocation, exact-framework-throw, and synchronization
replay events, their selected-constraint and semantic-operation hashes, and
their source-tree identities and spans. Worker protocol version 13 and semantic
cache schema version 15 carry the current wire break. The legacy relational-summary schema field remains in the envelope, but
nonempty relational-summary descriptors are rejected. Specification-pack
schema version 1 describes scalar-pack selection. The
artifact further carries compiler error
diagnostics and mapped locations, handwritten and generated tree hashes, raw
and effective per-tree preprocessor symbols, and parse evidence, plus a bounded
proof-relevant compilation-option set, assembly and target identity, and
compiler/reference provenance. An effective `SHARPPROOF_CONTRACTS` symbol
invalidates the artifact before verification. The artifact contains no source
text.

Before cache lookup or backend creation, the worker validates the artifact
digest and canonical shape, requires the compiler-visible maximum expression
depth to equal the request budget, and requires exact manifest/lowered-callable
equality, including claim ownership and declared assumptions. It hydrates
portable IR without constructing a Roslyn compilation, reparsing source, or
rereading reference files. Compiler versions and MVIDs and reference
paths/hashes/identities/aliases/sizes are provenance, not a runtime
compatibility gate. Collection and replay reject a module above 256 MiB, a
reference closure above 1 GiB, or more than 4,096 modules before hashing can
consume unbounded resources.

Artifact collection rejects resolver-dependent `#r`/`#load`, missing-assembly
resolver mode, reference supersession, custom assembly-identity comparers, and
non-file or unreadable references. `AdditionalFiles` are represented by
canonical paths and content hashes without embedding their raw contents.
Analyzer configuration is represented by its observable effects on the final
compilation and effective SharpProof options. Compiler error diagnostics fail
verification as `CompilationFailure`; malformed lowered evidence or an
expression-depth mismatch fails as `CompilerManifestMismatch`.

Postcondition verification uses the compiler's typed Total IR and native
bitvector verification conditions. Every worker construction path uses this
verifier. Entry feasibility uses body-independent predicates, and completed
claim results survive a later interruption. Bounded loop search can establish
a refutation only through original-body replay; a bounded UNSAT result cannot
prove a cyclic program. A cyclic program is proven over its cut: each natural
loop header forgets what the loop writes. When that cut cannot prove a
postcondition and bounded search finds no refutation, candidate invariants are
tried at the headers: each integer the loop carries compared (`<=`, `>=`, `==`)
with values the loop reads but does not write, the other values it carries,
zero and the constants of its conditions, and each boolean it carries. The
candidates are untrusted. The cut encoding assumes them after each header and
checks them on every edge into it, entry and back edges alike; the kernel must
prove every check, and a candidate that fails one is dropped until the rest
are inductive (Houdini). The postcondition is then proven with the surviving
invariants assumed, and the Requires clauses used by any check count as used.
An inductive invariant holds on every execution reaching its header, so every
claim decided over the cut (exceptions, allocations, purity, capabilities and
effect contracts) retries with the same invariants: for example, `i >= 0`
keeps `values[i]` in bounds in a counting loop and rules out a site guarded by
`i < 0`. After the first round, Houdini checks every surviving checkpoint in
one query before checking them one by one. When the surviving templates do
not prove a goal, Z3's Spacer engine proposes more, which join only the
surviving templates: each loop's invariant becomes an unknown relation over the
boolean and integer values it carries and reads, the checkpoints and the goal
become Horn clauses over those relations, and Spacer solves their unbounded
integer reading (arithmetic does not wrap; any other operation is a fresh
unknown; a zero-extension reads its operand unsigned and an unknown bitvector
application keeps its signed range) under a fixed resource limit. Its solution, translated back to IR and
split into conjuncts, joins the templates as further untrusted candidates for
Houdini, so the kernel checks every one over bitvectors: `s == 2 * i` proves
`s == 2 * n` after a loop adding two per step. Refutations still come only
from bounded search and replay.
Unsupported async and iterator callables abstain.

Element stores into single-dimensional arrays of bool or integer elements
carry their array, index and value (`a[i] = v`, `a[i]++`, compound
assignments). Replay applies them to the arrays by identity. In the
verification condition a read is the latest earlier store to the same array
and index on the executed path, else the array's entry contents; arrays are
compared by identity, so aliases see each other's stores. Element reads in a
body that stores elements are evaluated where they occur, so a later store
cannot change them. A postcondition reads the final contents, and
`Contract.Old` reads the entry contents, in replay as in the verification
condition. A call, an unmodeled
element write or a loop that stores elements forgets the contents: later
reads are unknown until stored again; element contents alone are forgotten by
an element write, which never changes a field. A loop whose every heap store
is an element store or a field store through a receiver over inputs it does
not write (`this`, a parameter, or a copy of one) forgets only those fields,
and element contents if it stores elements, at its header: the cut stores an
unknown value into each field, which is no write of the program, and other
fields keep their contents. The length of an array the loop does not replace
is an anchor for its candidates (`i <= values.Length`). Those fields join the loop's invariant candidates,
compared with its counters and with their entry values (a read through an Old
snapshot, which in invariants as in clauses sees the entry contents), and
they are part of the state Spacer reasons about, where references are integers
and an uninterpreted application (a field read, a length) is an unknown per
function and arguments, equal for equal arguments (Ackermann's reduction):
`count == Old(count) + i` proves
`count == Old(count) + n` after a loop that increments `count` n times.

Scalar and reference (object, string and array) instance fields of classes,
read and stored through a parameter, a local or `this`, follow the same model. A field read is a
pure `field:` member applied to the receiver widened to object, and denotes
the field's entry value; stores carry their receiver, field and value and
keep their write region. Z3 encodes each field as a function from objects to
values, and a counterexample's objects carry their decoded entry field values,
referenced objects, strings and arrays included (each model token decodes to
one identity), so replay and the kernel's model check read concrete fields. Contract clauses
may read such fields, safe only for a non-null receiver, and `Contract.Old`
reads entry values. In a class instance member `this` is a trailing input,
named `this` in counterexamples and assumed non-null on entry, and a value
outside constructors (a constructor's `this` stays a receiver only). An
inlined instance callee's `this` is the receiver as evaluated before the
arguments, unknown when the caller's own `this` is not modeled. A field of
`this` reached through a Roslyn flow capture stays approximated. Reads of non-fresh objects' fields are
state reads for EnforcePure. Static fields and struct fields stay
approximated.
Z3 decides DoesNotThrow, AllowedExceptions, ZeroAllocations and EnforcePure
claims over the Total program: a proof is a complete may-effect summary and a
refutation names a replayed violating site. Compiler effect evidence never
supplies a proof for these contracts; where Z3 stays Unknown, a compiler
violation is published only if it replays. Other effect contracts keep
compiler evidence. Native exception checks use the same passive SSA body facts.
`throw e` raises an explicit exception of e's static type, or
NullReferenceException when e is null; core-library exception constructors
taking strings and inner exceptions only allocate. Handlers match the thrown
static type, and lowering abstains when a handler's type derives from it. Each
explicit throw site carries its own exception code: an AllowedExceptions claim
admits a site whose static type derives from an allowed type, and refutes only
at a site that creates an exception of a disallowed type. DoesNotThrow is
refuted by any explicit throw.
A trusted complete effect contract on a metadata callee is its boundary: the
callee's IL is not inlined, and the call has exactly the declared effects and
capabilities. Otherwise an API specification narrows a non-dispatched opaque
call to its facets: it throws, allocates, writes or synchronizes only when the
specification says so, and uses only the capabilities it declares. A call
with neither may do all of these. Native AllowedCapabilities forbids reachable
locks and calls whose capabilities fall outside the allowed set; only a
reached lock refutes. Z3 decides AllowedCapabilities and EffectContract as
well. A trusted complete contract on a bodyless declaration also establishes
every other effect claim on that declaration that the contract satisfies.
Observable purity excludes non-local writes, locks, static field reads, and
calls that may write, synchronize, read ambient state, perform I/O, run native
code, reflect, behave nondeterministically or use any capability. Writing the
elements of an array the body created, holds only in a local, and only
indexes or measures is not observable. Reading the length or an element of a
string or array the body did not create reads state, so an EffectContract
must declare every read flag for it.
Methods, operators, conversions, property and indexer accessors,
expression-bodied properties, local functions that capture nothing, and class
constructors are lowered for claims. A constructor is lowered only when its
class derives from object, has no instance member initializers or primary
constructor, and the constructor chains to no other constructor; while `this`
is used only for its fields, its field writes initialize an object no caller
observes. Lambdas and other constructors stay Unknown. An auto-property
accessor reads or writes its backing field as an approximation. A static field read
runs no code when its type has no static initializer; the read value is an
approximation.
Claim lowering admits array element stores, increments and compound
assignments. The array and indexes evaluate first; a store then evaluates its
value, and the null and bounds checks follow (an increment or compound
assignment checks before reading). Each store writes Element state, and a
store of a reference into an array whose element type is not sealed may fail
its covariance check. A body that stores elements or calls opaque code reads
elements as approximations, and stays abstract when an Ensures clause or a
callee precondition reads elements. Arrays of any value-domain element type,
and multidimensional arrays, are references; their non-scalar or
multidimensional element reads are approximations, and multidimensional bounds
are approximated.
Uncaught exits retain guarded exception kinds across joins; allowed kinds are
checked at the exit rather than inferred from absence of a normal return.
The compiler normalizes each declared DoesNotThrow or AllowedExceptions
constraint using the bound core-library exception hierarchy and exact type
identities. A source-defined same-name type cannot allow a core-library fault.
Owned constraint rows survive artifact round-trip; malformed, duplicate,
foreign or noncanonical rows are rejected. Missing optional rows abstain,
including on contradictory entry conditions. Multiple exception claims share
one method budget while retaining their separate constraints.
SAT evidence must replay an explicit uncaught throw in the original body,
without reading approximation values. Loop cuts can prove unreachability;
finite search can only supply replayed violations. Call abstractions abstain
until their throwing behavior is represented. These qualification results do
not replace compiler effect publication.

Native allocation qualification captures core `new object()` expressions and
boxing of supported scalar values to `object`. Value-producing allocations
create fresh nonnull reference identities. Boxing evaluates its operand before
the allocation event, including any operand fault. Boxed contents, unboxing and
runtime type tests remain unsupported.
Single-dimensional zero-initialized arrays with Int32 dimensions produce fresh
sequence identities and their exact lengths. Dimension evaluation and negative
length Overflow faults precede allocation. Concrete replay initializes elements
to their CLR defaults and charges array size to its work budget; this never caps
symbolic inputs. Constant initializers carry typed literal elements in the same
allocation instruction, with exact scalar element facts in the VC and contents
in replay. Reference elements remain overapproximated. Nonempty constant
collection expressions targeting supported arrays use the same allocation and
contents model. Source params calls preserve explicit arrays and nulls;
constant expanded arguments allocate a fresh array. Empty expanded params use
the compiler's Array.Empty cache only when its owned model is available.
Nonconstant initializer evaluation, dynamic params expansion and array mutation
are not yet represented. Empty collection expressions and spreads remain
incomplete. Default-array element contents remain an
overapproximation in the VC.
Explicit delegate construction for static or nonvirtual reference receivers
records a fresh allocation without executing the target. Instance receiver
evaluation precedes construction. Directly escaping delegates retain the null
check, which throws the modeled System.ArgumentException before allocation.
Release emission can erase an unused construction and its null check; other
uses therefore carry a Boolean approximation of the check. A nonnull receiver
short-circuits that approximation, while reads on uncertain null paths prevent
concrete refutations. Universal proofs must hold for both choices. The exact
argument kind remains distinct from ArgumentNullException and
ArgumentOutOfRangeException. Generic targets,
compiler-cached method-group/lambda conversions, capturing closures and virtual,
override or value-type receivers remain unsupported until their semantics are represented.
String/string `+` chains with at most four operands after constant merging and
the owned two-string `String.Concat` model emit guarded string allocation sites.
At least two nonempty operands are required; null operands count as empty.
Constant expressions reuse their literal, and adjacent constants merge before
choosing the allocation guard. CFG captures preserve flattened operands and
defer prefix allocation until the root, after later operand evaluation. Longer
chains, object/formatting overloads and compiler-created params arrays remain
incomplete.

`string == string` and `!=` compare content: `StringEquals` is true when both
operands are null, false when exactly one is, and otherwise compares UTF-16
code units ordinally. Comparisons whose operands are typed `object` stay
reference identity, so content equality never proves that two strings are the
same object. Z3 reads content through `text: Ref -> Seq(BitVec16)` once a query
compares content: literals fix their text, a concatenation's text is its
operands' text in order (null as empty), and a non-null string's length is its
text's length; a concatenation's length is then also its operands' total in
the length sort (a total beyond `Int32.MaxValue` would throw, so it is no
result). Counterexamples decode that content for replay. Contract clauses may
concatenate strings with `+`: a clause allocates nothing, so it denotes only
the resulting content. The framework's static `string.Equals(string, string)`
is the same content comparison, in bodies and clauses. The instance
`a.Equals(string)` compares content too, after evaluating its argument, and
throws NullReferenceException on a null receiver; a clause using it is safe
only for a non-null receiver.
The shadow runtime oracle measures concrete feasible entries in
independently compiled source after argument construction and warmup. An empty
model for a zero-parameter method is distinct from an infeasible entry. Generic
closures are bounded representatives; unsupported inputs remain explicit gaps.
Conservative IL reachability also includes exception-filter handlers. These
observations do not establish a universal proof. Allocation events retain an
owned type and operation site through
artifact capture, loop transformation and passive SSA. A refutation requires
that the original program execute an allocation site without approximation
reads. A proof excludes all represented allocation and throw sites, including
caught faults that can allocate runtime exceptions. Unmodeled string allocation,
call abstractions and source static/module initialization abstain. Validated
claim ownership and complete entry initialization must survive decoding before
entry infeasibility can establish a vacuous result.

Write events classify Local, Parameter, Field, Static, Element and Unknown
regions. Native purity forbids reachable nonlocal events and permits allocation.
By-value parameter rebinding is Local. Primitive source field stores carry
nonlocal events with ordered receiver capture, RHS evaluation and null faults.
Instance field reads, and properties that are auto-properties or only return
one field of the same instance, run no code: a null receiver is their only
fault and the value read is an approximation, usable by universal proofs but
never by a concrete refutation. Type-parameter values are opaque: they may
be stored, passed, returned and type-tested (`value is int`), with an unknown
test result and no effects; the JIT folds the box such a test emits, which
runtime tests confirm. Operators, conversions and `default(T)` abstain.
A metadata call with no model, IL body or contract (an opaque call) takes
by-value arguments on a static or reference receiver. It may allocate, write,
synchronize and throw an exception of unknown type. Its result is an
approximation. Only a `catch` of `Exception` or a bare `catch` is known to
handle that exception, so a narrower handler abstains. A body with an opaque
call keeps no array element reads, because the callee may write any array.
Postconditions are still checked when a normal return exists only through
approximations.
Nonvirtual source methods and getters inline on a class receiver. The
receiver is null-checked after the arguments, and inside the callee `this` is
never null. `base.Property` reads a virtual auto-property's backing field
without dispatch. An explicit reference downcast keeps the reference and
throws InvalidCastException for a non-null value whose type test, an
approximation, fails. A generic container's declared class, interface and
type-parameter types bridge to the caller's by reference casts.
Source setters and indexers inline the same way: an accessor takes its
property's arguments, a setter takes the assigned value as its final `value`
parameter, and the assignment's value is the assigned one. Effect claims on
virtual and overriding methods verify the body as written; a postcondition on
such a method stays unsupported, since it binds every override.
Field increments and compound assignments on `this`, a parameter or a local
read the field (faulting on a null receiver) and write it back; the field is
modeled as above or approximated. Roslyn's flow captures of `this`, and of a field of `this` used as an
assignment target, stand for that receiver and field. An instance field store
is admitted when the type's static constructors are compiler-generated, and
entry initialization is effect-free when such a constructor only stores scalar
constants into readonly statics. A nonvirtual auto-property's setter only
stores its backing field, so stores, increments and compound assignments of
such a property are those of the field, and a contract clause reading a
property whose getter returns a field reads that field. Roslyn's null test for
`??` and `?.` on a reference compares it with null. A metadata constructor of
a class (`new HashSet<T>()`) evaluates its arguments, allocates the object
and runs as an opaque call that may throw; its value is the fresh object.
Claim lowering widens class, interface, delegate and array references to
object implicitly (for example the receiver a `lock` hands to
Monitor.Enter); the cast keeps the reference. A dispatched (virtual, abstract, override or interface) source call is also
opaque. Non-scalar struct and enum values are an opaque domain like type
parameters: only opaque calls read them, so a call that mutates a struct
through `this` changes nothing the IR observes. Struct and type-parameter
receivers therefore take opaque calls without a null check, and their
constants are approximations. Implicit reference conversions and boxing of
opaque values pass through to opaque callees, whose possible allocation covers
the box. Contract APIs, nonvirtual source callees, `ref` arguments and shadow
or metadata-Requires lowering never take opaque calls.
Static and volatile reads, dispatched properties, array stores, external
fields, other unsupported calls and incomplete initialization abstain.
Refutation requires an original-program write-site witness
without approximation reads. Bounded loop search never establishes a proof.

Lock events denote synchronization attempts, including Monitor.Enter/Exit and
ordinary C# lock statements. Replay stops at the attempt and may refute purity
from the owned site without claiming acquisition, release, completion or an
exception kind. All other body proofs require synchronization sites to be
unreachable under their preconditions. Allocation witnesses must still observe
an explicit allocation; a reachable lock alone yields an incomplete result.
Original postcondition replay stops at synchronization with a semantic
CounterexampleNotReplayable result, rather than an infrastructure failure.
Such complete Unknown responses remain eligible for validated caching.

Both SMT fuzz campaigns use the native Total bitvector solver. Partial-term
cases carry explicit normal-completion predicates for arithmetic faults and
short-circuiting, checked against independently executed C# operators. Finite
domain checks that exhaust their symbolic-query budget retry the same bounded
domain with exact input assignments; UNSAT requires every partition to be
UNSAT. An unsupported or exhausted partition remains an abstention.
The SMT backend accepts only Total IR; the legacy unbounded-integer encoder
and its implicit Defined channel have been removed. Arithmetic faults must
be represented explicitly in normal-completion predicates or control flow.
CSharpOperationSemantics owns the scalar type/operator metadata, explicit
Roslyn operation decisions, stage-support flags and local throw classification.
Source and IL scalar lowering share integer widths and operator rules. The
remaining analyzer range domain uses an explicit signed-long projection of
the same metadata; it does not admit UInt64 arithmetic. CFG reachability helpers
contain no scalar or throw classification rules.

Source calls and captured implementation IL expand directly into the caller's
Total program. The compiler no longer produces relational-summary descriptors.
Supported eager source calls record each callee Requires clause after argument
evaluation and before executing the callee body. Captured arguments replace the
callee Entry values in an owned boolean marker containing Safe && Value. The
marker neither assumes the precondition nor changes runtime control flow.
Artifacts validate a complete association between markers and obligation rows.
New consumers accept legacy artifacts without these shadow rows. Older strict
consumers reject the new field; artifact and worker-binary digests separate
their cache identities.
Internal native queries prove each occurrence from its execution prefix; a
refutation requires original-IR replay of the exact false marker without
approximation. Later exceptions or assumptions do not erase that observation.
Bounded loop search supplies witnesses only. Mandatory public call claims,
plain-caller discovery, metadata coverage and analyzer authority remain pending.
An additional shadow artifact collects source methods reachable from claim
roots once, retains recursive call edges, and excludes unrelated methods.
Admitted bodies carry Total leaf IR or explicitly marked source-call skeletons;
unsupported bodies and unknown dispatch stay explicit boundaries. Entry initialization is
tracked separately. This table does not supply proof or completion facts and
does not change compiler effect authority.
An internal frontend route can instead retain supported direct source calls
without expanding their bodies. Its shadow call skeleton carries an explicit
marker and is never classified as executable exact Total lowering. It preserves
argument evaluation, but supplies no callee exception or completion guarantee;
the collector records validated instruction-to-body mappings for this route.
A shadow SCC fixpoint joins local and callee may-effects without recursive
graph traversal. Cycles may diverge but do not invent allocation or write
effects. Call exceptions and normal completion remain unresolved, and caught
callee faults remain conservatively present. External facts are not yet
enrolled as modular facts in this consumer; missing IR and entry initialization
remain unknown. Its lowering reuses the same approved scalar API/specification
models as eager Total lowering, including owned Array.Empty for omitted params.
When an allocation operand is unsupported, lowering preserves earlier operand
effects and returns an unknown value with the allocation expression's type;
the enclosing body remains incomplete.
Closed generic outer types may share a source helper body when its intrinsic
parameter and result types do not depend on the outer arguments; top-level
generic callable admission is unchanged. Metadata boolean `and`, `or`, and
`xor` are exact only for normalized 0/1 stack values. Other integer bitwise
operations remain unsupported. Emitted callee contract arguments execute as
ordinary code, including throwing result placeholders; elided arguments do
not execute. Callee contract assumptions do not become caller proof premises.

This closed artifact removes worker-side compiler reconstruction. For the
admitted program subset, counterexample replay is independent of symbolic
execution: the worker executes the compiler-produced whole-body IR with a
separate interpreter and evaluates the original postcondition over the
reconstructed state. Differential compiled-C# execution remains a test
facility and is not run during user builds. A SAT result that depends on an
API-spec or relational-summary call result which the replay interpreter cannot execute becomes
`Unknown` with `CounterexampleNotReplayable`, never `Refuted`. A replay
discrepancy for an otherwise executable counterexample remains the fatal
`CounterexampleReplayFailed` run failure. Optional SARIF 2.1.0 is a
deterministic projection of the validated protocol response; it does not
participate in proof construction or change build success.

Effect refutation replay is separate from SMT and whole-body postcondition
replay. The worker interprets the compiler-neutral ordered event, recomputes
its constraint and operation identities, derives the observed effects,
capabilities, and exact exception hierarchy, and compares the result with the
sealed compiler witness. Invalid event order, source-tree identity/span, hash,
or structural shape is malformed compiler evidence and fails as
`CompilerManifestMismatch`. A structurally valid event that does not reproduce
the claimed semantic violation becomes `Unknown(CounterexampleReplayFailed)`
and makes the run `Failed`.
