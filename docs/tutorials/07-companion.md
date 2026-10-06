# 7. Describe an interface contract

A companion places contract clauses next to an interface or class without writing an implementation into the target. SharpProof matches the relationship through compiler symbols.

## Declare a companion and a consumer

```csharp
using SharpProof.Attributes;
public interface IAmount
{
    int Normalize(int value);
}
[ContractFor(typeof(IAmount))]
public static class AmountContracts
{
    public static int Normalize(IAmount receiver, int value)
    {
        Contract.Requires(receiver != null);
        Contract.Requires(value >= 0);
        Contract.Ensures(Contract.Result<int>() == value);
        return value;
    }
}
public static class AmountConsumer
{
    public static int Use(IAmount amount)
    {
        Contract.Requires(amount != null);
        return amount.Normalize(1);
    }
}
```

For the instance target `IAmount.Normalize`, the static companion has an explicit first receiver parameter of type IAmount, followed by the target's int parameter. The result type and remaining signature must match exactly.

The companion requires a non-null receiver and a nonnegative value, then describes the normal result. `Use` declares its receiver condition and calls Normalize with a literal 1.

Companion bodies carry clauses and must exist in source. They are not runtime wrappers around interface implementations. A clean portable analysis of this example does not prove every implementation of IAmount meets the declared postcondition.

## Selection and precedence

A valid direct clause on a target member makes that member the effective source for all its clauses. Companion clauses supply the effective source when the target has no valid direct clause. The two sources are alternatives rather than additive envelopes.

Misplaced direct clauses remain errors. They do not silently override a valid companion. Clause calls are still compiler-elided static declarations.

Matching includes generic constraints, ref kinds, nullability, receiver shape, and return type. Parameter names may help readers, but textual names do not replace symbol identity.

## Recognize validation errors

- Duplicate companions produce SPCF0002.
- A nonstatic or generically mismatched companion produces SPCF0003.
- A mismatched overload/signature produces SPCF0005.
- A missing source body produces SPCF0007.
- Self-targeting and cycles produce SPCF0009 and SPCF0010.

Validation runs at compilation end after generator syntax is available. The packaged generator is a loading hook and emits no companion implementation source.

## Use companions deliberately

They are useful for separating an API contract from its implementation and for keeping a reviewed interface specification visible. Verify implementations separately, and investigate unsupported dispatch/body claims when enabling the native worker.

The [ContractFor sample](../../samples/ContractFor/ServiceContracts.cs) demonstrates the same package-backed pattern. The [diagnostic guide](../diagnostic-examples.md#contractfor-diagnostics) explains all ten errors.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[07-companion] build exit: 0
```

## Continue

[Previous: outcomes](06-outcomes.md) | [Next: trusted boundaries](08-trusted-boundary.md).
