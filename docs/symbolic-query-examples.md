<!-- Generated from docs/symbolic-query-examples.source.md by scripts/Generate-Readme.ps1. -->

# SharpProof Unified Analysis Examples

This page is generated from committed example inputs and unified CLI output
snapshots.

Every example uses `analyze`; facets select effects, proofs, runtime hazards, or
complexity. Unknown evidence is never rendered as a proven violation.

### Condition proof at one program point

The proofs facet checks a condition against the bounded symbolic state at a source point.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- analyze --file docs/readme-examples/invariants-positive/input.cs --target line:7:13 --facets proofs --condition "value > 0" --format text
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
Status: Succeeded
Proof: value > 0: ProvenTrue (ir_state_contains_condition)
```

### Runtime hazard proof for divide-by-zero

The symbolic CLI can report a concrete hazard at a specific operation without executing the method.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- analyze --file docs/readme-examples/runtime-hazard-divide-by-zero/input.cs --target line:7 --facets hazards --format text
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
Status: Succeeded
Hazard: CheckedIntegralOverflow Unreachable: 10 / divisor
Hazard: DivideByZero Proven: 10 / divisor
```

### Unknown external effect boundary

Framework calls remain unknown unless source, IL, or an exact effect contract establishes their effects; no member-name catalog is consulted.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- analyze --file docs/readme-examples/capabilities-console/input.cs --target line:7 --facets effects --format text
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
Status: Unknown
Effects: Unknown, DirectCall
Capabilities: None
Purity: Unknown
Allocation-free: Unknown
Does-not-throw: Unknown
  effect direct_call at 88: Console.WriteLine("hello")
  effect metadata_call at 88: Console.WriteLine("hello")
Unknown: SP-EFFECT-METADATA: malformed_or_unavailable_metadata
```

### Conservative complexity classification

Complexity queries report the best proven Big-O plus the structural drivers that justify it.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- analyze --file docs/readme-examples/complexity-linear/input.cs --target line:10 --facets complexity --format text
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
Status: Succeeded
Complexity: O(n)
```

### Conservative unknown for dynamic dispatch

Dynamic dispatch produces explicit unknown method-effect evidence instead of an optimistic verdict.

Command:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- analyze --file docs/readme-examples/symbolic-unknown-dynamic/input.cs --target line:5 --facets effects --format text
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
Status: Unknown
Effects: Unknown
Capabilities: None
Purity: Unknown
Allocation-free: Unknown
Does-not-throw: Unknown
  effect dynamic_dispatch at 93: value.ToString()
  effect dynamic_dispatch at 93: value.ToString
Unknown: SP-EFFECT-UNKNOWN: dynamic_dispatch
```
