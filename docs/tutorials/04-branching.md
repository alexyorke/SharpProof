# 4. Verify decision logic

Postconditions describe what a method returns on normal completion. They can capture useful decision rules across branches and multiple return statements, rather than merely checking a final expression in isolation.

## Encode three behaviors

```csharp
using SharpProof.Attributes;
public static class Decisions
{
    public static bool IsAuthorized(bool administrator, bool owner)
    {
        Contract.Ensures(Contract.Result<bool>() == (administrator || owner));
        if (administrator) return true;
        return owner;
    }
    public static long SelectLimit(bool premium, long standardLimit, long premiumLimit)
    {
        Contract.Ensures(Contract.Result<long>() == (premium ? premiumLimit : standardLimit));
        if (premium) return premiumLimit;
        return standardLimit;
    }
    public static bool Flip(bool enabled)
    {
        Contract.Ensures(Contract.Result<bool>() != Contract.Old(enabled));
        enabled = !enabled;
        return enabled;
    }
}
```

`IsAuthorized` permits an administrator or an owner. Its contract captures the entire truth table in one relationship. The early return for administrators and the final return for everyone else must both satisfy it.

`SelectLimit` returns one of two supplied limits based on the premium flag. The postcondition refers to the original formal values and the normal result. SharpProof checks both reachable return paths.

`Flip` changes its parameter storage before returning. `Old(enabled)` is essential: it names the entry value, so the contract compares the returned value with the value before assignment. This is not a general deep-copy operation for arbitrary object graphs.

## Keep direct clauses in the prologue

Place Ensures before executable statements, even though it describes exit. `Result<T>` is only valid in a postcondition and must match the callable's normal result type. `Old` is a contract expression, not a normal runtime helper.

Postconditions are compiler-elided specifications. Calling Result or Old directly at runtime throws. They do not add runtime assertion checks.

## Exercise the failure boundary

Changing the administrator return to false violates the stated authorization rule for some inputs. Weakening the contract to say only "the result is a bool" loses the behavior you intended to protect.

When a worker refutation is available, inspect its model and source location before editing. A satisfiable SMT candidate that cannot be concretely replayed stays Unknown. Diagnostic suppression cannot repair a wrong decision rule.

A correct postcondition says nothing by itself about exceptions or termination. Add effect contracts where those properties matter. Inspect vacuity: a method with no modeled normal return does not supply evidence that normal returns actually happen.

## Apply this pattern

Good candidates include permission decisions, selecting configuration values, simple state transitions, and arithmetic with a precise valid-input envelope. Keep contracts aligned with real requirements and use focused counterexamples when changing behavior.

The existing [Library sample](../../samples/README.md) demonstrates the same style across multiple files and verifies five selected postconditions.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[04-branching] build exit: 0
  run: Complete; failure: None
  M:Decisions.Flip(System.Boolean)~System.Boolean | Postcondition/Unspecified | Proven | None | Unspecified | None
  M:Decisions.IsAuthorized(System.Boolean,System.Boolean)~System.Boolean | Postcondition/Unspecified | Proven | None | Unspecified | None
  M:Decisions.SelectLimit(System.Boolean,System.Int64,System.Int64)~System.Int64 | Postcondition/Unspecified | Proven | None | Unspecified | None
```

## Continue

[Previous: effect proofs](03-effects.md) | [Next: loops and state](05-loops-and-state.md).
