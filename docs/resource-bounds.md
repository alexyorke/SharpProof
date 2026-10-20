# Static resource bounds

Enable the experimental, opt-in SP0060 diagnostic in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.SP0060.severity = suggestion
```

Use SharpProof's advisory or strict profile. The off profile disables this feature.
SP0060 reports one result per handwritten ordinary method declaration. It does
not require a contract attribute. Constructors, accessors and local functions do
not have independent resource results in this milestone. A local function inside
an analyzed method makes that method Unknown. Generated code is skipped.

For example:

```csharp
public static class AllocationExample
{
    public static void Allocate(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var buffer = new int[3];
        }
    }
}
```

The result is Proven: total loop iterations <= `max(0, n)`, cumulative array
allocations <= `max(0, n)`, cumulative allocated elements <= `max(0, n) * 3`.
Names in expressions refer to entry parameters. Negative scalar sizes are clamped
to zero; an invalid array dimension throws before successful allocation. In bound
expressions, the mathematical size symbol `values.Length` is defined as zero for
a null array; it is not an instruction to dereference a null value. Null
array parameters may throw during execution. These count bounds apply to every
execution prefix, including early returns and exceptional exits.

## Supported proof rules

- Strict increasing counted loops with int/long counters, a nonnegative constant
  start, and a positive constant `++`/`+=` step. Unchecked steps require a proof
  that the final update cannot wrap; checked overflow ends that execution prefix.
  For example, unchecked `int i += 2` against an arbitrary int parameter is
  Unknown, whereas a long counter against that int bound is supported.
- Strict descending loops against a nonnegative constant lower bound, with
  `--`/`-=` progress. Division by a constant greater than one is supported for
  loops ending at zero. Multiplication from one is supported for increasing
  loops when the final update is checked or cannot wrap.
- Bounds are integer literals, unchanged int/long entry parameters, the Length of an
  unchanged primitive one-dimensional array parameter, or an enclosing counter's
  proven nonnegative envelope. Dependent inner bounds and allocation sizes use
  that envelope, which may be loose. Arbitrary arithmetic bounds and other local
  values, do/foreach, break, continue and goto remain Unknown.
- Top-tested `while` loops can use increasing or decreasing local ranks.
  Increasing `<` loops require an unchanged, exact nonnegative constant start,
  with a final `++`, `+= positiveConstant`, or `*= factor` (start one and factor
  greater than one). Unchecked updates must stay representable; checked overflow
  ends the bounded execution prefix. Decreasing `>` loops start from a supported
  size and use final `--`/`-= positiveConstant` with a nonnegative constant lower
  bound, or `/= factor` toward zero. The prefix rejects other counter writes,
  aliases and jumps. Earlier mutation invalidates the initial-value fact. A
  negative or symbolic initial value cannot be clamped into an increasing rank.
  These rules do not infer ranks for arbitrary while loops.
- A two-phase `while (left > 0 || right > 0)` can select `if (left > 0)`
  followed by an else phase. Both counters must be distinct Int32/Int64 locals
  initialized from supported sizes. Each phase ends with its own decrement,
  positive constant subtraction, or division by a constant greater than one.
  No other writes to either counter are admitted. Each phase's prefix cost is
  scaled by its checked rank bound, then the phase costs add. The sum uses
  natural-number arithmetic even when it exceeds either physical counter's
  range. Split logical-or guards record one source entry per actual body entry;
  the phase-selection conditional adds no loop entry.
- Sequential costs add. Nested costs multiply. A loop's contribution
  includes its own body entries plus any nested loop entries. Branches take the
  maximum for each metric, so different metrics need not attain their maxima on
  the same path. Early return may make a bound loose.
- Scalar locals and primitive array element operations, built-in arithmetic,
  comparisons, conditionals, and ordinary returns are supported. Counter and
  parameter mutation is rejected. Entry array element mutation cannot change its
  Length and is supported.
- Single-dimensional primitive array creation without an initializer charges one
  array and its size in elements at every execution. Sizes use the same forms as
  loop bounds. Arrays of arrays, objects, nullable/decimal/user-defined types,
  initialized arrays, fields, arbitrary properties, opaque or unsupported recursive calls,
  boxing, lambdas, async/iterator bodies, ref aliasing and exception regions are
  Unknown. Unknown operations are never recursively treated as free.

`ceil(x / k)` rounds a nonnegative quotient upward. `digits(x, k)` is zero for
zero, otherwise `1 + floor(log_k(x))`; it bounds division loops ending at zero.
`ceil_log(x, k)` is zero for x <= 1, otherwise `ceil(log_k(x))`. Geometric steps
require k > 1. These are mathematical bound expressions, not C# integer arithmetic.

## Source-call composition

Static source methods without generic type parameters can compose bounds.
Arguments are evaluated and charged once, then their sizes are substituted by
parameter symbol. A call in a loop inherits that loop's execution multiplicity.
Named arguments and multilevel calls preserve binding. Unknown argument sizes
remain Unknown if a callee needs them for a resource bound.

Source calls share the root operation/depth budget. Unproved recursion returns
`RecursiveCostUnknown`, unspecified metadata calls return `OpaqueCall`, and unsupported
dispatch/signatures return `UnsupportedCall`. A possible static type initializer
returns `CallTypeInitializationUnknown`; the callee body alone cannot establish
its allocation or callback costs. These boundaries will be extended according to
the [full implementation roadmap](resource-analysis-roadmap.md).

Static void tail recursion is supported for a first `if (n <= 0) return` guard,
a final self-call using `n - positiveConstant` or `n / divisorGreaterThanOne`,
and unchanged forwarding of every other parameter. The positive rank decreases
without overflow. Per-activation prefix costs use the entry-size envelope and
scale by `ceil(max(0, n) / step)` or `digits(max(0, n), divisor)`. Multiple recursive
sites, different guards, growing arguments, and other recurrence families remain
Unknown. The zero-cost base case is checked; its work cannot be silently omitted
if it contains allocations. These allocation bounds do not yet report recursive
stack frames or count calls as loop body entries.

## Reviewed metadata assumptions

The version 1 resource catalog covers the Int32 overloads of `Math.Min` and
`Math.Max` for approved .NET 8 metadata identities. These cost summaries are
conditional on the reviewed .NET 8.0.32 implementation, pinned to
[runtime commit b9a49e4](https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Math.cs).
They are independent of purity, exception, and termination specifications.
The existing approved assembly/reference resolver must bind the exact method;
source lookalikes receive ordinary body analysis. Possible type initializers and
other runtime families remain Unknown.

Each conditional proof includes the specification witness, source commit, and
catalog digest. The digest binds the resource declarations, cost model, reviewed
runtime, source identity, and approved API-specification content. Conditional
diagnostics explicitly say "under recorded metadata assumptions". This is an
assumption about the target implementation, not a claim that the analyzer has
inspected the consumer's eventual deployment runtime. The native worker binds
these declarations to the owning callable, its declared trusted assumptions,
the .NET 8 target framework, and the captured reference assembly identity and
SHA-256 digest. Native result records expose the witness, catalog digest,
reference identity/hash, reviewed runtime model, and pinned source URL; SARIF
labels the result as conditional on resource metadata. Missing, conflicting,
or incompatible reference evidence produces Unknown. Deployment compatibility
with the declared implementation remains an explicit assumption. The existing
strict profile reports these trusted dependencies as SP0048 errors even when
all resource claims are Proven. With native verification explicitly enabled
(`SharpProofVerify=true`), the advisory profile records the assumption without
failing the build.

Results are conservative structural proofs over compiler-bound operations, not
pattern guesses, benchmarks, Z3 worker results, or native-worker claim records.
There is no new inference from diagnostic silence. Methods exceeding 4096 visited
operations, depth 128, or 16384 expression characters return
`AnalysisBudgetExceeded`. Unknown results contain no partial numeric bounds.

## Units and evidence

Loop iterations means total body entries across all loops, not CPU instructions
or elapsed time. Array elements combine element counts regardless of primitive
element type; they do not measure bytes. Runtime/JIT/GC infrastructure costs,
peak live heap, retained output, and stack memory are outside this SP0060 count model.
Native byte and logical lifetime contracts below use separate cost-model identities.
The result is an upper bound, not a tight Big-O classification or exact trip count.

SP0060 properties expose `ResourceStatus` (`Proven` or `Unknown`) and
`ResourceCostModel` (`loop-entries-and-primitive-array-elements-v3`). Proven
results include `LoopIterationsUpperBound`, `ArrayAllocationsUpperBound`,
`AllocatedElementsUpperBound`, and `ResourceExpressionModel` (`1`). Unknown results
include `UnknownReason`.
`ResourceProofBasis` distinguishes `Structural` results from
`ConditionalOnMetadataSpecifications`. Conditional results also include
`ResourceAssumptions` (one provenance record per line) and
`ResourceMetadataCatalogSha256`. Unknown results publish neither numeric bounds
nor partial metadata proof assumptions.

See [milestone progress](resource-bounds-progress.md) for verification evidence,
implementation status, and subsequent work.

## Native resource contracts

`ResourceUpperBound` selects an independent native obligation through the
existing Effects feature. Use the standard package's native verification flow
from [getting started](getting-started.md); SP0060 remains an optional source
summary and does not replace these worker outcomes.

```csharp
using SharpProof.Attributes;

public static class NativeAllocationExample
{
    [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0, "n", 1)]
    [ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, "n", 1)]
    [ResourceUpperBound(ResourceMetric.AllocatedElements, 0, "n", 3)]
    public static void Allocate(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var buffer = new int[3];
        }
    }
}
```

The mathematical bound is `constant + coefficient * entrySize`. Omit the size
and coefficient for a constant bound. A size is an Int32/Int64 parameter name or
an array parameter followed by `.Length`. Negative scalars and null arrays have
entry size zero. Arithmetic does not wrap. A supplied size requires a positive
coefficient, and both numeric terms must be nonnegative. The compiler binds the
name to its parameter ordinal and physical domain; arbitrary source expressions
and property getters are not accepted as size names.

Each attribute has its own result. For cumulative metrics, the worker derives upper
bounds from the closed resource program. Logical lifetime contracts use the owned
typed IR described below. The worker checks each declared inequality. Unsupported
bodies, unstable progress, missing initialization evidence and unbound metadata
assumptions produce Unknown. A modeled upper bound larger than the requested
limit also remains Unknown until an actual violating execution is established;
an upper envelope alone does not refute a contract.

For the three count metrics and admitted byte contracts, the worker also searches for
an exact violating execution prefix. Every candidate must satisfy the owned
entry constraints, receiver requirements, and all Requires clauses with their
definedness guards. Successful array events carry their evaluated element count;
failed allocations and observations after consuming an approximation contribute
no violation evidence. A prefix already over the bound remains a violation when
a later operation throws or concrete replay reaches its step limit.

The search uses bounded small inputs and constant thresholds, then native
feasibility/allocation models. It cannot prove a bound from samples or establish
that no violating execution exists. Failure to produce an exact witness remains
Unknown. Loop-body-entry refutations use compiler-owned guard annotations for the
admitted top-tested C# for/while families. Resource-selected lowering identifies
the source loop's strict local-counter condition and preserves its source span
and true-edge semantics through inlining. Replay counts one body entry only when
that guard evaluates true without consuming an approximation. Ordinary branches,
false guards and jumps to earlier blocks do not supply loop-entry evidence. The annotations are
part of trusted source lowering and compiler artifact schema 33; display labels
alone do not establish callable ownership or a progress proof.

Compiler-elided calls, including contract clauses, do not add runtime costs or
argument allocations. Enabled conditional calls retain their normal costs.
The reserved contract symbol remains invalid for production compilation.

Validated results and SARIF expose `resourceBound` evidence with the metric,
cost model, expression/program versions, declared-bound digest and inferred
upper bound. Refuted resource evidence contains `observedLowerBound` and
`evaluatedUpperBound`, a concrete source location, and the replayed entry model.
Both values are canonical nonnegative integers and the observed prefix must be
strictly larger. Unknown evidence contains its reason and no partial numeric bound.
Metric or declared-bound mismatches are rejected, including cached responses.
The compiler carries declarations and programs; it cannot supply a resource
proof verdict or substitute a qualitative trusted effect summary.

## Quantitative memory implementation status

An internal layout kernel now models fresh source-owned instance storage under
`coreclr-8.0.32-x64-source-storage-v1`, conditional on CoreCLR 8.0.32, x64 and
8-byte pointers. Typed shapes describe arrays, object fields, UTF-16 strings,
primitive boxing, delegates, and one fresh closure frame plus delegate. The
reviewed layout constants are pinned to the runtime source linked in the
[roadmap](resource-analysis-roadmap.md#quantitative-memory-implementation-plan).

Array/string storage uses aligned header-plus-payload arithmetic. Ordinary
non-overlapping fields use a packed lower bound and a conservative slot upper
bound; a loose interval supplies no exact cost. Referenced objects and constructor
effects are separate. A factory that returns a cached string or delegate does
not establish a fresh allocation merely because its instance shape has a size.
Unknown shapes and incompatible contexts return no partial interval.

Resource program model 4 retains storage widths for every admitted primitive
array creation, including source calls. Its strict codec rejects unrelated,
noncanonical or conflicting shape facts. The native checker validates callable
input ownership and progress before deriving a conditional cumulative byte upper
bound: sequence costs add, branch costs take a maximum, and repeated costs use
the checked iteration envelope. Untyped arrays, opaque metadata costs, invalid
progress and incompatible contexts yield Unknown with no partial byte bound.

`ResourceMetric.AllocatedBytes` declares a byte contract under
`coreclr-8.0.32-x64-source-storage-v1:cumulative-bytes-v1`. It requires a net8.0
target, compiler PlatformTarget x64, a reviewed captured core-library reference,
and a callable-owned trusted layout declaration. Results expose the model digest,
source pin, reference identity/hash and used dependency. These are explicit
assumptions about execution under that runtime model, not runtime detection.
Missing or incompatible evidence yields Unknown. The strict profile applies its
existing SP0048 rejection policy to the trusted layout dependency; advisory
verification records it when native verification is explicitly enabled.

For a net8.0 project with `<PlatformTarget>x64</PlatformTarget>`:

```csharp
using SharpProof.Attributes;
public static class ByteAllocationExample
{
    [ResourceUpperBound(ResourceMetric.AllocatedBytes, 40)]
    public static int[] Allocate() { return new int[3]; }
}
```

The inferred fresh-array storage is 40 bytes, including its header and alignment.
This is cumulative source-owned instance storage, including returned allocations.
It excludes runtime/JIT/GC infrastructure costs and does not measure peak live,
retained or stack space. Larger inferred envelopes alone do not establish a
violation. Refutations require a concrete successful allocation prefix satisfying
entry constraints and Requires. Byte replay reads typed IR element widths and
computes each fresh array's header, payload and alignment under the bound layout.
Boolean and typed 8/16/32/64-bit integer storage (including char) are admitted;
opaque/floating IR storage stays Unknown. It never guesses widths from labels.
Early returns, infeasible branches, prior exceptions and approximations cannot
invent byte events. These are exact costs in the declared source storage model,
not measured JIT/GC/process allocation traces. Refutations retain the owned
layout dependency and expose the observed prefix and evaluated entry bound.
Other allocation categories still need compiler extraction and effect accounting.
Protocol 22, cache schema 24 and compiler artifact schema 37 invalidate earlier
serialized evidence. Resource program/evidence model 4 and expression model 1
are checked together; the existing three count metrics retain their meanings.

### Managed objects and constructor effects

The source/native path also admits `new object()` and ordinary source
classes without type parameters directly derived from System.Object. Primitive and reference instance
slots receive a reviewed layout interval. Constructor arguments, field and
simple auto-property initializers, explicit constructor bodies and field object
initializers retain their own loop/array/byte costs. The instance itself adds no
array-allocation or element count. The intrinsic object base constructor follows
the existing total-lowering rule; user constructor effects are visited explicitly.

For example, a fresh object without fields has a 24-byte model footprint. An object
with byte/int fields has an upper envelope of 32 bytes and a 24-byte lower cost;
the verifier does not infer an exact 32-byte allocation from that envelope.
Concrete fresh-reference events contribute only their justified model lower cost
to numerical refutations. Exact array costs can then compose with that lower
cost when the constructor body replays without approximation.

For admitted source construction, field and simple auto-property initializers
now execute in declaration order after argument evaluation and allocation, before
the constructor body. Argument values are captured before initializer effects.
Direct field object initializers execute after the constructor body. Initializer
stores reach body/caller reads; exceptions prevent later events. This path admits
one type declaration without type parameters;
partial declarations, property-setter/nested object initializers and unsupported initializer
values retain conservative replay boundaries. Inheritance, custom layout, static
type initialization, recursive construction, COM activation, cleanup callbacks and events,
unsupported value fields and unmodeled constructor chains also remain Unknown.
Broader string and delegate/closure extraction, graph lifetimes and stack models
remain in the full roadmap. Finite primitive-array lifetime contracts are described below.

### Scalar boxing

Boolean and typed 8/16/32/64-bit integer conversions to object, including char,
now charge their operand effects and a typed box. Under the reviewed x64 model,
these primitive boxes occupy 24 bytes. Repeated boxes compose with source loops
and array events; they do not become array allocations or allocated elements.
Both constant upper bounds and concrete byte-prefix refutations retain the owned
layout dependency. An operand exception occurs before boxing and cannot supply
a box event. Floating, decimal, nullable, interface and user-defined conversions
remain Unknown until their storage and conversion rules are admitted.

### Repeated-character strings

`new string('x', count)` admits a constant character outside the surrogate range
and a supported scalar count. The pinned runtime layout bounds positive fresh
storage by `align8(22 + 2 * count)`. Zero returns the cached empty string with zero
fresh bytes; negative counts throw before string allocation. Array allocation and
element counts stay zero for the string itself. Argument effects remain separate.

Concrete replay preserves content and length. Symbolic checking preserves length
and non-null facts while allowing conservative content; fresh storage never means
unequal string values. Dynamic characters, surrogate characters, other overloads
and opaque factories remain Unknown. These are source storage bounds under the
explicit runtime assumption, not physical JIT/GC measurements or runtime helper
loop counts. String lifetime composition and physical peak heap remain pending.

### Explicit delegates

Explicit `new Action(Callback)` construction with an admitted source method target
has a 64-byte fresh delegate footprint under the reviewed runtime assumption.
Receiver evaluation and allocation costs are separate; the target body does not
run during construction. Erased constructions have zero delegate bytes, and a
null receiver failure cannot contribute a fresh event. Delegate creation adds no
array allocation or element counts.

The compiler supplies a typed delegate certificate, retained through portable IR,
rather than inferring storage from type labels. Managed delegate signatures are
accepted while unsupported bodies remain Unknown. Implicit conversions, lambdas,
capture frames, metadata/extern targets, target type initialization and unmodeled
dispatch remain Unknown. Closure allocation scope, sharing and lifetimes remain
required work in the full roadmap.

### Root parameter closures

A synchronous static method whose sole return creates a delegate capturing entry
parameters can account for its root frame separately from the delegate. Integer,
Boolean, char and reference captures use typed storage slots. A single Int32
capture has a 24-byte frame plus the 64-byte delegate under the reviewed model.
Frame field packing uses conservative upper and packed lower bounds.

The frame event occurs at entry; the delegate event occurs at the return. The
lambda body is not executed during construction. Compiler data-flow facts exclude
compile-time `nameof` references. Immutable frame certificates retain storage
through portable IR and reject conflicting class/interface/delegate metadata.

Initialized read-only root locals also retain their typed storage and owned
graphs. Up to 16 nested, synchronous anonymous functions may capture that same
root storage. Each function must capture root variables; intermediate capture
scopes and stateless nested caches remain Unknown. The factory allocates its
frame and immediately returned delegate. Nested delegates are created only when
their enclosing function is invoked later, outside that factory proof. A scalar
root such as `() => () => n` retains a 32-byte frame and 64-byte outer
delegate at factory completion. The frame includes a null reference slot for
the deferred delegate cache; that slot adds storage before any inner delegate
instance exists. This does not prove an invocation cost or a
post-invocation retained graph.

Broader control flow, intermediate-frame sharing, caches, local functions,
async lambdas and unsupported value fields remain Unknown. In particular, an
unsupported loop may allocate an entry frame before zero iterations and may add a
delegate-cache field. Its cost must not be treated as zero merely because the
lambda expression is never reached. Broader capture placement and liveness remain
part of the full roadmap.

## Logical peak auxiliary and retained bytes

`PeakAuxiliaryBytes` and `RetainedBytes` use the versioned finite strong-reference
lifetime model on normal completion. The former excludes the final returned object
graph throughout the execution; the latter counts owned storage reachable from the
returned value. These are logical storage metrics, independent of when a GC runs
or how a JIT keeps stack slots alive. Both require the same reviewed runtime layout
and compiler-bound target/reference evidence as cumulative byte contracts.

```csharp
using SharpProof.Attributes;

public static class BufferFactory
{
    [ResourceUpperBound(ResourceMetric.AllocatedBytes, 72)]
    [ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes, 32)]
    [ResourceUpperBound(ResourceMetric.RetainedBytes, 40)]
    public static int[] Create()
    {
        var temporary = new int[1];
        return new int[3];
    }
}
```

A conservative fallback also admits parametric primitive arrays in static methods
with Boolean/integer inputs and locally established array ownership. It checks
an independent cumulative-byte upper bound, then uses that as an upper envelope
for auxiliary or retained storage. For example,
`[ResourceUpperBound(ResourceMetric.RetainedBytes, 31, nameof(n), 4)]` can prove a
linear upper bound for `int[] Target(int n) => new int[n]` under the declared
layout. This can be loose: the returned array itself is excluded from auxiliary
storage, although its cumulative size remains a valid auxiliary upper bound.
Scalar and void results retain no owned output graph in this admitted family.

The proof core identifies `primitive-array-cumulative-envelope-v2` and binds both
the typed program and checked cost program. It adds no lower or tight bound.
Precise lifetime bounds take precedence. A reference may cross IR blocks when a
definition dominates its use and every reference definition and operand passes
the closed ownership check. The native checker searches for any entry-to-use path
that bypasses initialization, under the same finite analysis budget. This supports
array-length reads and conditional returns behind compiler-generated guards,
including compiler default initialization followed by an owned assignment.
Uninitialized or borrowed references, receivers, fields and opaque calls remain
Unknown in this fallback. Other precise lifetime families remain available.

Lifetime layout ownership is independent of cumulative-program availability.
A proven fixed local size can therefore support a native lifetime proof even when
the count analyzer reports Unknown.

The native finite family admits primitive arrays and repeated-character strings
with constant sizes, certified direct source classes, reference aliases, typed
field stores and reads of explicitly
stored owned references, last-use root drops,
literal copies through compiler temporaries and proven constant guards. Shared
children and cycles count once by identity; overwritten field edges can release
former children. Unproved branches, repeated regions, borrowed references,
field reads without compiler initialization/store facts, source-call lifetime
effects and unsupported storage
return Unknown without partial numbers or used layout dependencies. Numerical
refutations in the admitted finite family require a validated feasible entry and
an independently observed completed execution. Replay reconstructs identities,
aliases and last-use roots from actual instruction values. Incomplete executions,
approximation, unsupported observations or entry conditions that have not been
validated cannot supply a lifetime violation; estimates alone cannot refute a bound.

Cumulative metrics retain their exceptional-prefix semantics. Logical lifetime
metrics use `coreclr-8.0.32-x64-source-storage-v1:finite-owned-strong-reachability-v1:normal-completion`.
The distinct cost-model identity, owned layout provenance and typed-program digest
remain attached to native proof evidence. Physical peak heap, retained borrowed
inputs, recursive stack and broader lifetime composition remain separate work.

### Complete object storage facts

An admitted object layout includes every instance field, including fields that the
analyzed body never accesses and compiler backing fields. The compiler supplies an
immutable typed storage certificate; portable transport rejects mixed, inherited,
legacy, built-in and malformed certificates. Field edges use scoped member identities.
Object packing keeps a packed lower bound and a conservative slot upper bound;
replay cannot turn the upper estimate into an exact numerical violation.

The initial object family has a direct System.Object base, source declarations and
supported integral/reference storage. Custom layout, static initialization, events,
cleanup methods, unsupported value fields and broader ownership remain Unknown.
The model still describes logical source-owned storage under the reviewed layout,
with final returned graphs excluded from auxiliary bytes throughout execution.

### String lifetime identity

Repeated-character string construction uses the reviewed UTF-16 storage layout.
Positive lengths create distinct owned instances even when their contents match.
Aliases, object widening and owned string field edges preserve those identities.
Empty construction and the empty literal refer to shared non-null storage with
zero newly owned bytes. A permanent zero-byte trace anchor preserves sharing
without treating an empty string as null or resurrecting a reclaimed allocation.

Guard reads count as uses of their operands, preserving roots through a proven
reference comparison, including comparisons copied into Boolean temporaries.
String content equality is separate from reference identity;
a branch requiring unproved content equality returns Unknown. Borrowed nonempty
strings, nonempty literals, negative construction lengths and unproved lifetime
control flow also return Unknown without partial numbers. Numeric lifetime
violations still require an independently observed normal completion satisfying
all entry conditions, under the owned runtime/layout assumption.

### Complete observation paths

A lifetime refutation requires the complete ordered callback path from the owned
entry instruction through normal return. Within each block, every instruction
before its terminator must be followed by the next instruction. Transfers follow
the actual
goto target or observed Boolean branch decision. A missing or reordered callback,
including an omitted field store, cannot publish partial numbers. Foreign and
repeated instructions, unknown branch decisions, approximation, cancellation and
exceeded observation/graph budgets also cannot supply lifetime evidence.

### Compiler field defaults and scalar facts

For a certified direct source-class construction, lowering emits typed zero/null
stores immediately after allocation, before source field/property initializers,
constructor execution and object initializers. Scoped member identities preserve
backing fields and distinguish separate instances. These are compiler facts about
actual initialization; storage size alone never implies a field's value.

Finite memory analysis propagates known integer/Boolean field stores through
aliases, copies, Boolean guards and array/string extents. Unknown stores clear
prior scalar facts. Integer comparisons use mathematical values, including the
full UInt64 range. Constant integer conversions are admitted when they preserve
the value within the target range; unsupported conversions remain Unknown.

A fresh default reference read can therefore prove retained bytes of zero while
the containing object's auxiliary storage is still charged. Reads without a known
compiler store, opaque constructions and unproved control remain Unknown.
Admitted direct constructors bind their fresh receiver after evaluating arguments
and allocating storage. Finite scalar stores and guards in their bodies can
determine subsequent extents; unconstrained constructor inputs remain Unknown.
CLR zero stores carry the compiler's fresh-write marker because they precede any
source operation that can publish the object. This preserves ordinary purity
analysis while retaining the stores for memory analysis and replay.

### Static delegates and scalar boxes

Finite lifetime proofs and completed refutations admit explicitly constructed
static delegates when the compiler records an owned source binding, admitted
type initialization and the nominal delegate type. The exact allocation marker
establishes that no receiver is captured; a delegate type alone is insufficient.
Their 64-byte storage under the declared layout participates in aliasing,
reference guards, auxiliary memory and retained output. Scalar boxes use fresh
24-byte object storage for the admitted Boolean/integer payloads.

Broader closure captures, cached conversions, metadata method targets
and unsupported initialization remain Unknown without partial lifetime metrics.
Delegate invocation costs and effects require their own admission and are not
implied by allocation or lifetime evidence.

### Owned instance delegate targets

An admitted explicit instance binding that returns directly records an immutable
target edge immediately after allocation. The target and its owned child graph
therefore participate in retained output, sharing, cycles and auxiliary memory.
For example, a 64-byte delegate retaining a 24-byte target and a 32-byte child
array retains 120 newly owned bytes under the declared layout.

Both extraction and completed replay require the compiler marker, delegate type,
scoped target field and ordered initialization. Missing, mismatched, null,
overwritten or borrowed edges produce Unknown, as does a target equal to the
newly allocated delegate. Bindings whose
construction may be erased, broader closure links and unsupported target bindings remain
Unknown. Capture storage does not imply any bound on invoking the target method.

### Root closure frame links

Admitted root parameter closures initialize their certified slots in order and
link the returned delegate to that frame. Primitive captures retain their typed
storage even when input values are unknown. Inlined source factories can capture
owned references, including child graphs and shared objects. A one-reference
frame, a 64-byte delegate, a 24-byte captured object and its 32-byte child array
retain 144 newly owned bytes under the declared layout; two reference slots
sharing that same graph retain 152 bytes.

Root locals are also admitted when the body consists of initialized local
declarations followed by one returned lambda. Captured parameters and locals
must remain read-only, including inside the lambda. The frame is allocated at
entry, its local slots receive typed defaults in certificate order, and each
local initializer updates its certified slot. Owned reference captures retain
their reachable graph; aliases retain each object once. A captured one-element
Int32 array retains 120 bytes including the frame and delegate.

Extraction and replay independently check the local-frame marker, slot identity,
storage, receiver and owned value on every update. Updates to undeclared fields
and self-frame references remain Unknown. Parameter-only frames still require
immutable initialization. Missing, reordered or mismatched capture stores remain Unknown.
Borrowed non-null captures and broader scopes still require further ownership
models. Object packing intervals remain separate upper and lower bounds; a loose
upper bound cannot establish an exact violating cost. Closure invocation effects
and costs require separate admission.

Instance factories may use the same root-frame model when factory-owned captures
belong to their read-only parameters or initialized locals. The returned graph
must not capture the factory receiver, explicitly or implicitly. Receiver field
reads inside a lambda remain Unknown. Pure entry/current/pre-state copies of
an unused receiver add no owned storage; observing or returning an unowned
reference still requires an ownership model. Under the declared layout, a nested
scalar root capture retains a 32-byte frame (including its deferred delegate
cache slot) and a 64-byte outer delegate. Later invocations require separate
cost proofs.

A single nested chain may also capture read-only parameters or initialized locals
declared by deferred functions. Those child frames are created by later calls,
so the factory counts only its root fields and immediately returned delegate.
A nested delegate reserves a root cache slot only when all its free captures
belong to the root scope. Under the declared layout, returning
`item => () => n + item` retains 88 bytes at factory completion; adding an owned
two-element Int32 array to that root graph retains 128 bytes. An extra cached
intermediate delegate increases the scalar factory bound to 96 bytes. Independent
compiled controls measure later child-frame/delegate allocations separately.
Sibling intermediate scopes, mutable captured bindings, local functions and
async bodies remain Unknown; later invocations require their own cost proofs.

Direct calls on the current receiver to source methods that are not virtual can
compose an admitted factory with its caller, for both explicit and implicit this.
Argument costs are
charged once and callee parameter sizes are substituted under the shared analysis
budget. Unobserved borrowed receiver-copy chains require no owned input graph;
all owned copy reads and roots retain the declared last-use behavior. Observed
borrowed aliases and unowned overwrites of existing owned roots remain Unknown.
Virtual, overriding and arbitrary receiver calls remain outside this resource
admission.

### Logical source stack contracts

`SourceActivationDepth` bounds simultaneous logical source activations.
`SourceStackBytes` bounds their declared record storage: eight bytes per
parameter/local slot, one receiver/result slot when present, and two bookkeeping
slots per activation. Sequential calls and loop iterations reuse records;
admitted recursive ranks accumulate retained activations. Constructor initializers
execute within the constructor record.

These are source-model bounds, excluding JIT frame layout, runtime/native frames,
evaluation temporaries and tail-call elimination. They do not prove physical stack
usage or stack-overflow safety. For a static void method with one parameter that
calls a static void leaf with one parameter, depth is at most two and record
storage is at most 48 bytes.

Use `ResourceUpperBound(ResourceMetric.SourceActivationDepth, 2)` or
`ResourceUpperBound(ResourceMetric.SourceStackBytes, 48)` for that example. Unknown
frame storage, unsupported call/recurrence semantics and unproved comparisons
return Unknown. Stack refutations are currently unsupported; an inferred upper
bound alone is never used as an observed violating cost.

## Asymptotic upper bounds of resource formulas

Opt-in SP0060 diagnostics also classify each proven loop-entry, array-count and
element-count upper formula under
single-input-unbounded-expression-extension-v1. Supported orders include zero,
constants, polynomial powers, logarithmic powers and their products. For example,
a bound of n*n+n has upper order O(n^2), and n*ceil(log2(n)) has upper order
O(n * log(n)). These are bounds on the recorded resource metrics, not processor
time measurements.

C# input types have finite domains. This model explicitly extends the mathematical
bound formula to an unbounded nonnegative input for growth classification; it
does not assert that the C# method can execute at sizes beyond its physical domain.
The generic n is bound to a recorded parameter ordinal, input kind (scalar,
array length or string length), and display name. Physical maximum values do not
turn a size-dependent formula into an asymptotic constant. Renaming an input does
not change its identity.

Diagnostic properties include ResourceAsymptoticModel and, for each metric,
AsymptoticUpperBound plus the input ordinal, kind and name when present. Multiple
independent inputs and iterated logarithms currently produce an explicit
AsymptoticUnknownReason without an order. Numeric resource proofs remain available
when their single-input growth classification is unsupported. Unknown numeric
proofs publish no asymptotic orders. O(0) means the bound formula is identically
zero.

The diagnostic message labels these as upper bounds of the unbounded formula
extension. A tight source-cost classification requires an independent lower-bound
proof as well as the upper bound; this feature does not publish Theta results.
Lower-bound derivation, amortized accounting and validated distributional models
remain tracked in the full roadmap.

## Native asymptotic upper evidence

Non-vacuous native Proven resource results now carry an asymptotic record derived
from the worker's owned inferred upper expression. This uses the same single-input
unbounded formula extension as SP0060. The record includes its model, canonical
upper order, polynomial/logarithmic degrees, zero flag and input ordinal/kind/name
when present. The proof core records the growth model, and cache/SARIF projection
preserves that evidence.

A numeric upper-bound proof can remain Proven while its growth record is Unknown,
for example when the inferred formula depends on two independent inputs. Such a
record contains its reason and no partial order, degree or input data. Numeric
Unknown results and finite numeric refutations publish no asymptotic proof; a
single observed violation cannot establish an asymptotic lower bound.

Protocol 39 and cache schema 48 invalidate older responses without this evidence.
Validation requires a complete growth record for non-vacuous numeric proofs,
canonical order text matching its structured degrees, valid input identities,
and mutually exclusive known/Unknown fields. It rejects growth records attached
to numeric refutations or Unknown claims. Compiler artifact 52, manifest 6,
resource program/evidence 8 and expression model 1 identify the current certificate.

### Lower size envelopes

Resource program 8 carries lower array extents and lower loop-rank initial/limit
expressions separately from upper size envelopes. The compiler uses exact input
sizes outside repeated regions and a conservative zero lower envelope for
repeated counters. Source-call substitution preserves both envelopes. The native
checker validates ownership and lower-to-upper ordering before deriving loop
entries, array counts and element counts. Sequence adds lower costs; a choice
contributes only a bound valid on both arms. Contradictory or missing certificate
fields reject the whole proof without partial numeric evidence.

These lower costs are conditional on normal completion. Early-return bodies and
short-circuit regions use an explicit upper-only envelope. Recursive prefixes
retain a zero lower cost until their per-activation size environment is modeled.

Native results now carry a required lower-growth record. A separate native domain
check admits nonnegative scalar inputs, mathematical unbounded loop counters and
ideal primitive allocations. It tracks immutable single-definition
snapshots, proves constant and nonnegative allocation guards, and prunes only
proved branches. Reassigned divisors, narrowing to zero, opaque calls, heap reads,
nontrivial preconditions, recursion and unsupported instructions produce explicit
Unknown lower records while preserving a valid numeric upper proof. The model
identifier reports the counter extension and ideal-allocation assumption;
physical CLR limits and allocation failures remain outside that model.

For symbolic array extents, the checker recognizes the compiler's closed
array-size-limit failure choice and selects its success continuation under the
declared ideal allocation model. The exact marker, Boolean havoc, branch,
exception kind, source site and following primitive allocation must agree.
Unrelated or malformed havoc remains Unknown. This rule extends the allocation
model explicitly; it does not infer a successful physical allocation above the
CLR limit.

For admitted loop-entry, array-count and element-count claims, the record includes
the inferred lower formula and an Omega order. Matching lower and upper orders
produce Theta. Independent nesting proves Theta(n^2); decreasing division proves
Theta(log(n)); combined independent loops prove Theta(n * log(n)). A dependent
triangular loop currently proves Omega(n) against O(n^2), with
ResourceTightnessUnproved instead of a Theta claim. The bound counts logical cost
units under the selected metric. It does not measure elapsed time.

The lower formula's nested growth descriptor uses the same canonical expression
grammar as upper growth, with no further nested lower record. Protocol validation
rejects missing, recursive, contradictory and mismatched input records, larger
lower orders, partial Unknown records and tightness without matching bounds.
Protocol 39 and cache 48 invalidate prior responses; compiler artifact 53 and
resource program 8 retain their current formats. Cache and SARIF preserve the
lower record and its declared domain.

### Conditional expected upper bounds

A Boolean entry parameter can declare an explicit input model:

```csharp
using SharpProof.Attributes;

public static class ConditionalResourceExample
{
    [ResourceUpperBound(ResourceMetric.AllocatedElements, 0, nameof(n), 7)]
    public static void Process(int n, [ResourceBooleanInput(1, 3)] bool selected)
    {
        for (int i = 0; i < n; i++)
        {
            if (selected) { var a = new int[7]; }
            else { var b = new int[2]; }
        }
    }
}
```

The weights declare probability 1/4 for true and 3/4 for false, conditional on
all other entry inputs. This is a user assumption for each invocation. Source
branch shape, samples and branch frequencies never supply a distribution.
Only one modeled Boolean parameter is admitted. Each weight must be an integer
between zero and one million, with a positive total.

The compiler analyzes both immutable entry valuations. The ordinary upper proof
retains their maximum; the native worker separately derives the ceiling of
`(falseWeight * falseUpper + trueWeight * trueUpper) / totalWeight`. The example
has worst-case element bound `7*n` and expected upper bound `ceil(13*n/4)` for
nonnegative n. Negative scalar entry sizes remain zero. Ceiling preserves an
integer upper bound; it does not claim that a fractional expected cost is an
integer. The individual conditional bounds can themselves be conservative.

The declaration models one entry value reused throughout the invocation. It
does not assert fresh independent draws at each branch or loop iteration. Direct
parameter guards, negation and constants permit specialization; other supported
conditions keep their conservative envelope. Both valuations must be proved,
including a zero-weight arm. Input mutation, multiple modeled parameters,
invalid weights and unsupported costs produce Unknown. Nontrivial preconditions
or call-entry restrictions leave the expected record Unknown unless support of
the declared distribution is proved. They can retain a valid worst-case proof.

Result JSON, cache and SARIF carry expected model, input identity, weights,
conditional upper formulas and rounded expected upper formula separately from
worst-case evidence. Known expected records require the input-model proof marker and a canonical
record digest binding input identity, weights and conditional/expected formulas.
Unknown expected records contain only their reason and no partial numeric or
probability fields. Missing declarations report ResourceInputModelNotDeclared.
The admitted metrics are loop entries, primitive-array allocations, elements and
layout-bound cumulative bytes. This upper expectation does not establish an
expected lower bound, tightness, independence, elapsed time or physical residency.

### Credit potential accounting

The compiler admits an outer ranked for-loop whose first statement deposits a
fixed positive amount into a nonnegative local integer credit. Its final
statement is a guarded unit-decrement while-loop, optionally selected by an if
without an else. Other credit writes, aliases and unsupported exits are rejected.
Prefix work and the drain body remain subject to the ordinary closed resource
rules. The credit is protected from mutation while those regions are analyzed.

The potential is the remaining credit. With initial credit I, outer iteration
bound N and deposit D, total drain entries are at most I + D*N. The native worker
derives that bound from the initialization, deposit, integer width and outer rank;
the artifact does not supply a computed spend bound. Unchecked credit growth must
stay representable. Checked overflow can stop a prefix but cannot create more
credit. The outer and drain costs are composed separately for loop entries,
allocation counts, elements and layout-bound bytes. Stack composition uses the
maximum sequential activation cost.

The result carries accounting model, initial credit, deposit, width, overflow
mode and derived maximum spend. Unknown or refuted results carry no partial
accounting certificate. Conditional drains need not spend every deposited credit;
an amortized upper bound does not establish a matching allocation lower bound.
The unit-spend potential rule remains distinct from an expected-cost model.

Intrinsic string lengths are admitted as size inputs and as contract sizes such
as text.Length. Their identity is bound through the compiler's intrinsic sequence
length matcher, not a property name alone. Source calls and constructors preserve
string-size substitution, including fixed repeated-character string arguments;
literal sizes count UTF-16 code units. Unmodeled aliases, user-defined Length
getters and changing non-ranking recursive arguments remain Unknown.
