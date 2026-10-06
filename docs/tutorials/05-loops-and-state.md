# 5. Verify loops and mutable state

This lesson moves beyond straight-line scalar examples. It checks a loop's return relationship, array-access exception freedom, and a field write followed by a read.

## Write the examples

```csharp
using SharpProof.Attributes;
public static class Loops
{
    public static int CountUp(int n)
    {
        Contract.Requires(n >= 0);
        Contract.Ensures(Contract.Result<int>() == n);
        var i = 0;
        while (i < n) i++;
        return i;
    }
    [DoesNotThrow]
    public static int Sum([NotNull] int[] values)
    {
        var total = 0;
        for (var i = 0; i < values.Length; i++) total += values[i];
        return total;
    }
}
public sealed class Counter
{
    private int count;
    public int SetThenRead()
    {
        Contract.Ensures(Contract.Result<int>() == 7);
        count = 7;
        return count;
    }
}
```

`CountUp` requires a nonnegative bound and promises that the final counter equals it. The loop's useful facts include the initial counter value, its update, and its relation to the guard. The worker uses checked invariant candidates; it does not prove an arbitrary loop by running it a fixed number of times.

`Sum` requires a non-null array and claims no escaping exception. The loop guard and index progression support the modeled bounds checks. Its unchecked Int32 addition can wrap; the method does **not** claim an unbounded mathematical sum or freedom from arithmetic wraparound.

`SetThenRead` mutates a field and promises the value read after that store. The worker models ordered stores and reads under path reach. It does not assume that the field's incoming value was already 7.

## Keep the claims precise

These three obligations are different:

- A return relationship after a loop.
- Exception behavior during array traversal.
- A normal-return value after a heap update.

None implies observable purity. In particular, writing `count` changes receiver state. Do not add EnforcePure merely because the method is small.

The NotNull input condition does not prove every future array access valid. Change the loop guard to `i <= values.Length` and the final access can be out of range. Check the resulting worker reason/witness instead of refreshing expected output blindly.

## Understand the current boundary

Natural loops can be admitted when the proof encoding can establish and preserve useful invariants. More complicated loops, opaque mutation, unresolved calls, or exhausted construction budgets can remain Unknown.

Heap reasoning is also bounded. Unknown writes can forget contents. Closed compiler type evidence constrains reference identities, and replay rejects impossible aliases. These examples do not establish arbitrary ownership, concurrent-memory, or reference-array support.

A postcondition on CountUp is a normal-return claim, not a termination theorem. The exception claim on Sum is not a correctness theorem for all possible definitions of summation.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[05-loops-and-state] build exit: 0
  run: Complete; failure: None
  M:Counter.SetThenRead~System.Int32 | Postcondition/Unspecified | Proven | None | Unspecified | None
  M:Loops.CountUp(System.Int32)~System.Int32 | Postcondition/Unspecified | Proven | None | Unspecified | None
  M:Loops.Sum(System.Int32[])~System.Int32 | Effect/DoesNotThrow | Proven | None | CompleteMayEffectSummary | None
```

## Continue

[Previous: branching](04-branching.md) | [Next: interpreting outcomes](06-outcomes.md). Reference: [current coverage boundary](../coverage-and-limits.md).
