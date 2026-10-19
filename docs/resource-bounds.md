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

- `for (int i = 0; i < bound; i++)` or `++i`, with no other writes or aliases to
  the counter. The strict comparison ensures the increment cannot wrap, even at
  `int.MaxValue`.
- Bounds are int literals, unchanged int entry parameters, or the Length of an
  unchanged primitive one-dimensional array parameter. Locals, arithmetic bounds,
  dependent inner bounds, other strides/comparisons, while/do/foreach, break,
  continue and goto are Unknown.
- Sequential costs add. Independent nested costs multiply. A loop's contribution
  includes its own body entries plus any nested loop entries. Branches take the
  componentwise maximum, so different metrics need not attain their maxima on
  the same path. Early return may make a bound loose.
- Scalar locals and primitive array element operations, built-in arithmetic,
  comparisons, conditionals, and ordinary returns are supported. Counter and
  parameter mutation is rejected. Entry array element mutation cannot change its
  Length and is supported.
- Single-dimensional primitive array creation without an initializer charges one
  array and its size in elements at every execution. Sizes use the same forms as
  loop bounds. Arrays of arrays, objects, nullable/decimal/user-defined types,
  initialized arrays, fields, arbitrary properties, calls (including recursion),
  boxing, lambdas, async/iterator bodies, ref aliasing and exception regions are
  Unknown. Unknown operations are never recursively treated as free.

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
`ResourceCostModel` (`loop-entries-and-primitive-array-elements-v1`). Proven
results include `LoopIterationsUpperBound`, `ArrayAllocationsUpperBound`, and
`AllocatedElementsUpperBound`. Unknown results include `UnknownReason`.

See [milestone progress](resource-bounds-progress.md) for verification evidence,
implementation status, and subsequent work.
