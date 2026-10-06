# 2. Catch mistakes while compiling

This lesson deliberately violates a precondition, allocates under a zero-allocation claim, and introduces an unsupported delegate call. The portable analyzer can report these problems without loading Z3.

## Introduce three problems

```csharp
using SharpProof.Attributes;
public static class Diagnostics
{
    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
    public static int BadCall() => Positive(0);
    [ZeroAllocations]
    public static object Allocates() => new object();
    [ZeroAllocations]
    public static int DelegateCall()
    {
        System.Func<int> value = () => 1;
        return value();
    }
}
```

`BadCall` supplies 0 to a method requiring greater than zero. SP0027 reports the concretely replayed false precondition. The method name alone is not the contract; SharpProof binds the actual compiler symbol and clause.

`Allocates` creates an object under `[ZeroAllocations]`. Portable SP0045 says allocation freedom was not established. It is not the native worker's independently replayed allocation-witness result.

`DelegateCall` selects an allocation claim over a body containing a delegate invocation. Its incomplete analysis is visible as SP0047. An unsupported unannotated method may remain quiet; explicit selection makes accountability important.

## Make informational diagnostics visible in builds

The tutorial writes this `.editorconfig` beside the consumer source:

```ini
root = true

[*.cs]
dotnet_diagnostic.SP0045.severity = warning
dotnet_diagnostic.SP0047.severity = warning
```

SP0027 already defaults to Warning. SP0045 and SP0047 default to Info. Promoting those two to warnings makes them visible in ordinary build output; it does not change their evidence or turn them into native refutations.

The example does not enable warnings-as-errors, so the build succeeds with warnings. Set the severities and warning policy deliberately for your team. In native CI, verifier policy is a separate gate.

## Respond to each problem

| Result | Useful response |
| --- | --- |
| SP0027 | Correct the argument or establish a valid condition before calling |
| SP0045 | Remove the allocation, revise an inappropriate contract, or investigate incomplete call effects |
| SP0047 | Simplify the unsupported selected body or accept an explicit incomplete result under your chosen policy |

Do not suppress a warning and then claim the method was proven. `SharpProofSuppress` changes reporting only. Do not use `Assume` to conceal an actual bad input.

## Analyzer versus verifier

The portable analyzer reports conservative analysis and usage errors while you compile. The worker checks selected obligations over a closed compiler artifact. Diagnostic IDs, claim outcomes, callable coverage, and run status have different meanings.

The next lesson enables that worker and obtains four positive effect results. See [diagnostic reference](../diagnostic-examples.md) for all main and companion diagnostics.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[02-diagnostics] build exit: 0
```

Verbatim diagnostic message excerpts, with the file/project path and help-link suffix omitted:

```text
warning SP0027: Call to 'Positive' violates precondition 'value > 0'
warning SP0045: Method 'Allocates' is marked [ZeroAllocations], but allocation freedom could not be verified: 'new object()' may allocate
warning SP0047: SharpProof could not completely analyze selected method 'DelegateCall': Advisory:Lowering:UnsupportedOperationKind
```

## Continue

[Previous: first contract](01-first-contract.md) | [Next: prove effect contracts](03-effects.md).
