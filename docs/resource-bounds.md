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
array parameters may throw during execution. Bounds apply to every execution prefix, including
early returns and exceptional exits, rather than guaranteeing normal completion.

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
peak live heap, retained output, and stack memory are outside the cost model.
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

Each attribute has its own result. The worker derives cumulative upper bounds
from the closed resource program and checks the declared inequality. Unsupported
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
Broader string and delegate/closure source extraction, liveness/escape, peak and
retained space and stack models remain in the full roadmap.

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
loop counts. Peak live memory and the remaining roadmap are still pending.
