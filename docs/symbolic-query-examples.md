<!-- Generated from docs/symbolic-query-examples.source.md by scripts/Generate-Readme.ps1. -->

# SharpProof Symbolic Query Examples

This page is generated from committed example inputs and committed CLI output
snapshots. It focuses on the richer proof/query surfaces that do not show up as
ordinary analyzer diagnostics.

These examples are backed by tests and are meant to show the current bounded
symbolic surface honestly: invariants, reachability, implication checks,
runtime hazards, capability summaries, complexity, and conservative unknowns.

### Invariant, reachability, and implication at one point

One query can show the merged invariant at a program point, whether the point is reachable, and whether the current facts imply another condition.

Backed by test: `ReadmeGeneratedExamplesTests.InvariantsCliExample_MatchesSnapshot`.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file docs/readme-examples/invariants-positive/input.cs --line 7 --column 13 --check-reachability --implies "value > 0"
```

Source (`docs/readme-examples/invariants-positive/input.cs`):

```csharp
public static class Example
{
    public static int UseValue(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}
```

CLI output:

```text
docs/readme-examples/invariants-positive/input.cs:7:13
Node: ReturnStatement
Program point kind: Statement
Method: UseValue
Merged invariant: value > 0
Reachability: Reachable
Reachability reason: branch_reachable
Invariant query: Must=1, Maybe=0, Unknown=0
Invariant query text: value > 0
Invariant query status: Exact
Invariant query status reason: all_candidate_program_points_exact
Invariant query target: value status=Exact reason=target_exact code=SP-SYM-TARGET-EXACT
Invariant query target summary: All selected reachable program points agree on the facts for this target.
Invariant query target path: value conditions=1 smt=1 points=1 reachablePoints=1 proofs=1 unknownProofs=0 reason=target_has_path_conditions code=SP-SYM-TARGET-PATH-CONDITIONS
Invariant query target path summary: This target has source-location path conditions available for invariant queries.
Invariant query target path conditions: value > 0
Implies 'value > 0' target=value kind=SmtBinary: ProvenTrue
Implication reason: ir_state_contains_condition
Proof outcomes: Total=1, ProvenTrue=1, ProvenFalse=0, Unreachable=0, Unknown=0
SMT:
  Mode: Bounded
  Enabled: True
  Query timeout ms: 750
  Method budget ms: 5000
  Max path conditions: 192
  Max expression nodes: 2048
  Executed queries: 1
  Cache entries: 1
  Health: Ready
  Transient retries: 0
  Context recycles: 0
Facts:
  value > 0
```

### Runtime hazard proof for divide-by-zero

The symbolic CLI can report a concrete hazard at a specific operation without executing the method.

Backed by test: `ReadmeGeneratedExamplesTests.RuntimeHazardCliExample_MatchesSnapshot`.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file docs/readme-examples/runtime-hazard-divide-by-zero/input.cs --line 7 --runtime-hazards
```

Source (`docs/readme-examples/runtime-hazard-divide-by-zero/input.cs`):

```csharp
public static class Example
{
    public static int Divide(int divisor)
    {
        if (divisor == 0)
        {
            return 10 / divisor;
        }

        return 0;
    }
}
```

CLI output:

```text
docs/readme-examples/runtime-hazard-divide-by-zero/input.cs
Runtime hazards: 1
Hazard status summary: Proven=1
Hazard exception summary: System.DivideByZeroException=1
Hazard category summary: definite_divide_by_zero=1

docs/readme-examples/runtime-hazard-divide-by-zero/input.cs:7:20 DivideByZero Proven
Exception: System.DivideByZeroException
Category: definite_divide_by_zero
Reason: ir_state_contains_condition
Operation: 10 / divisor
Trigger: divisor == 0
Invariant: divisor == 0
SMT:
  Mode: Bounded
  Enabled: True
  Query timeout ms: 750
  Method budget ms: 5000
  Max path conditions: 192
  Max expression nodes: 2048
  Executed queries: 1
  Cache entries: 1
  Health: Ready
  Transient retries: 0
  Context recycles: 0
```

### Capability summary at a program point

Capability queries classify proven side-effect categories such as `Console` and the derived umbrella category `IO`.

Backed by test: `ReadmeGeneratedExamplesTests.CapabilitiesCliExample_MatchesSnapshot`.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file docs/readme-examples/capabilities-console/input.cs --line 7 --capabilities
```

Source (`docs/readme-examples/capabilities-console/input.cs`):

```csharp
using System;

public static class Example
{
    public static void Log()
    {
        Console.WriteLine("hello");
    }
}
```

CLI output:

```text
docs/readme-examples/capabilities-console/input.cs
Method: Example.Log()
Capabilities: IO, Console
Conservative: False
IO, Console: invocation at 7:9 - System.Console.WriteLine(System.String? value)
```

### Conservative complexity classification

Complexity queries report the best proven Big-O plus the structural drivers that justify it.

Backed by test: `ReadmeGeneratedExamplesTests.ComplexityCliExample_MatchesSnapshot`.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file docs/readme-examples/complexity-linear/input.cs --line 10 --complexity
```

Source (`docs/readme-examples/complexity-linear/input.cs`):

```csharp
public static class Example
{
    public static int Sum(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}
```

CLI output:

```text
docs/readme-examples/complexity-linear/input.cs
Method: int Example.Sum(int n)
Complexity: O(n)
Kind: Linear
Conservative: False
Driver: ForLoop at 6:9 - for-loop bound O(n) from n
```

### Conservative unknown for dynamic dispatch

When the engine cannot prove a capability set because of dynamic dispatch, the CLI reports an explicit unknown instead of inventing a stronger answer.

Backed by test: `ReadmeGeneratedExamplesTests.SymbolicUnknownCapabilitiesCliExample_MatchesSnapshot`.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file docs/readme-examples/symbolic-unknown-dynamic/input.cs --line 5 --capabilities
```

Source (`docs/readme-examples/symbolic-unknown-dynamic/input.cs`):

```csharp
public sealed class TestClass
{
    public string Render(dynamic value)
    {
        return value.ToString();
    }
}
```

CLI output:

```text
docs/readme-examples/symbolic-unknown-dynamic/input.cs
Method: TestClass.Render(dynamic)
Capabilities: None
Conservative: True
Unknown reasons: DynamicDispatch
Unknown: dynamic at 5:16 - value.ToString()
```
