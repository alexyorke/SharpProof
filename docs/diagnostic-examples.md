<!-- Generated from docs/diagnostic-examples.source.md by scripts/Generate-Readme.ps1. -->

# SharpProof Diagnostic Example Gallery

This page is generated from committed example inputs and committed output
snapshots. It is the per-rule evidence catalog for the current public analyzer
surface.

Every example below is backed by a regression test. When the analyzer behavior
changes, the generator and the tests force this page to stay in sync.

## Coverage

The catalog intentionally includes at least one example for every public rule
from `SP0002` through `SP0025`.

### SP0002 - Purity not verified

The analyzer rejects ambient clock reads inside `[EnforcePure]` methods.

Backed by test: `ReadmeGeneratedExamplesTests.PurityAnalyzerExample_MatchesSnapshot`.

Source (`docs/readme-examples/purity-clock/input.cs`):

```csharp
using System;
using SharpProof.Attributes;

public sealed class Example
{
    [EnforcePure]
    public int ReadClock()
    {
        return DateTime.Now.Second;
    }
}
```

Expected analyzer diagnostics:

```text
SP0002 Error docs/readme-examples/purity-clock/input.cs:7:16 Method 'ReadClock' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure
```

### SP0003 - Misplaced [EnforcePure]

Placement diagnostics stay separate from purity proof results.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0003_MisplacedEnforcePureExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0003-misplaced-enforce-pure/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

[EnforcePure]
public sealed class TestClass
{
}
```

Expected analyzer diagnostics:

```text
SP0003 Error docs/readme-examples/sp0003-misplaced-enforce-pure/input.cs:4:2 The [EnforcePure] attribute can only be applied to method declarations
```

### SP0004 - Missing [EnforcePure]

Pure-looking methods can be suggested for explicit purity contracts.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0004_MissingEnforcePureExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0004-missing-enforce-pure/input.cs`):

```csharp
public sealed class TestClass
{
    public int Add(int left, int right)
    {
        return left + right;
    }
}
```

Expected analyzer diagnostics:

```text
SP0004 Warning docs/readme-examples/sp0004-missing-enforce-pure/input.cs:3:16 Method 'Add' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.
```

### SP0005 - Conflicting purity attributes

Contradictory method contracts are reported directly.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0005_ConflictingPurityAttributesExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0005-conflicting-purity-attributes/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [Impure]
    public int Value()
    {
        return 1;
    }
}
```

Expected analyzer diagnostics:

```text
SP0002 Error docs/readme-examples/sp0005-conflicting-purity-attributes/input.cs:8:16 Method 'Value' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure
SP0005 Warning docs/readme-examples/sp0005-conflicting-purity-attributes/input.cs:8:16 Method 'Value' has conflicting purity attributes applied
```

### SP0006 - [AllowSynchronization] without a purity contract

Synchronization exceptions only make sense on methods participating in purity analysis.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0006_AllowSynchronizationWithoutPurityExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0006-allow-sync-without-purity/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowSynchronization]
    public void Work()
    {
    }
}
```

Expected analyzer diagnostics:

```text
SP0006 Warning docs/readme-examples/sp0006-allow-sync-without-purity/input.cs:7:17 Method 'Work' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]
```

### SP0007 - Misplaced [AllowSynchronization]

Placement errors for synchronization allowances are explicit.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0007_MisplacedAllowSynchronizationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0007-misplaced-allow-synchronization/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

[AllowSynchronization]
public sealed class TestClass
{
}
```

Expected analyzer diagnostics:

```text
SP0007 Error docs/readme-examples/sp0007-misplaced-allow-synchronization/input.cs:4:2 The [AllowSynchronization] attribute can only be applied to method declarations
```

### SP0008 - Redundant [AllowSynchronization]

The analyzer can flag unnecessary synchronization allowances.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0008_RedundantAllowSynchronizationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0008-redundant-allow-synchronization/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [AllowSynchronization]
    public int Add(int left, int right)
    {
        return left + right;
    }
}
```

Expected analyzer diagnostics:

```text
SP0008 Info docs/readme-examples/sp0008-redundant-allow-synchronization/input.cs:8:16 Method 'Add' is marked with [AllowSynchronization] but contains no synchronization constructs
```

### SP0009 - Purity explanation

Optional explanation diagnostics can expose why the purity result was reached.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0009_PurityExplanationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0009-purity-explanation/input.cs`):

```csharp
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void Log()
    {
        Console.WriteLine("hello");
    }
}
```

Expected analyzer diagnostics:

```text
SP0002 Error docs/readme-examples/sp0009-purity-explanation/input.cs:8:17 Method 'Log' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure
SP0009 Info docs/readme-examples/sp0009-purity-explanation/input.cs:8:17 Purity analysis for 'Log': catalog_hit at static System.Console.WriteLine(string?)
```

### SP0010 - Exception summary

Method-level exception summaries can be emitted independently of point hazards.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0010_ExceptionSummaryExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0010-exception-summary/input.cs`):

```csharp
#pragma warning disable SP0004
public sealed class TestClass
{
    public int Divide(int value)
    {
        return value / 0;
    }
}
```

Expected analyzer diagnostics:

```text
SP0010 Info docs/readme-examples/sp0010-exception-summary/input.cs:4:16 Method 'Divide' can throw: System.DivideByZeroException
```

### SP0011 - Operation-site runtime hazard

The analyzer can point at the exact operation that may throw under the current facts.

Backed by test: `ReadmeGeneratedExamplesTests.RuntimeHazardCliExample_MatchesSnapshot`.

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
Line: 7
Runtime hazards: 1
Hazard status summary: Proven=1
Hazard exception summary: System.DivideByZeroException=1
Hazard category summary: definite_divide_by_zero=1

docs/readme-examples/runtime-hazard-divide-by-zero/input.cs:7:20 DivideByZero Proven
Exception: System.DivideByZeroException
Category: definite_divide_by_zero
Reason: ir_state_contains_condition
Node: DivideExpression 133-145
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
```

### SP0012 - BCL fallback guess

When stronger evidence is missing, SharpProof can emit an explicitly non-authoritative BCL fallback guess.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0012_BclFallbackGuessExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0012-bcl-fallback-guess/input.cs`):

```csharp
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return System.Experimental.NumericFacts.Normalize(value);
    }
}
```

Expected analyzer diagnostics:

```text
SP0002 Error docs/readme-examples/sp0012-bcl-fallback-guess/input.cs:6:16 Method 'TestMethod' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure
SP0012 Info docs/readme-examples/sp0012-bcl-fallback-guess/input.cs:6:16 BCL purity fallback for 'TestMethod': probably_pure (member returns a value-like result without ref or out parameters)
```

### SP0013 - Allocation in [ZeroAllocations] body

Zero-allocation contracts report each direct heap allocation site separately.

Backed by test: `ReadmeGeneratedExamplesTests.ZeroAllocationsAnalyzerExample_MatchesSnapshot`.

Source (`docs/readme-examples/zero-allocations/input.cs`):

```csharp
using SharpProof.Attributes;

public sealed class Example
{
    [Impure]
    [ZeroAllocations]
    public object Create()
    {
        return new object();
    }
}
```

Expected analyzer diagnostics:

```text
SP0013 Warning docs/readme-examples/zero-allocations/input.cs:9:16 Method 'Create' is marked [ZeroAllocations], but operation 'new object()' allocates
```

### SP0014 - Misplaced [ZeroAllocations]

Placement rules also apply to zero-allocation contracts.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0014_MisplacedZeroAllocationsExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0014-misplaced-zero-allocations/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

[ZeroAllocations]
public sealed class TestClass
{
}
```

Expected analyzer diagnostics:

```text
SP0014 Error docs/readme-examples/sp0014-misplaced-zero-allocations/input.cs:4:2 The [ZeroAllocations] attribute can only be applied to method declarations
```

### SP0015 - Disallowed capability use

Capability contracts report concrete operations that exceed the allowed set.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0015_CapabilityViolationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0015-capability-violation/input.cs`):

```csharp
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        Console.WriteLine("hello");
    }
}
```

Expected analyzer diagnostics:

```text
SP0015 Warning docs/readme-examples/sp0015-capability-violation/input.cs:10:9 Method 'TestMethod' is marked [AllowedCapabilities], but operation 'Console.WriteLine("hello")' requires capabilities: IO, Console
```

### SP0016 - Capability contract not fully verified

Unknown or unsupported capability cases stay conservative instead of being silently accepted.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0016_CapabilityUnknownExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0016-capability-unknown/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod(dynamic value)
    {
        value.ToString();
    }
}
```

Expected analyzer diagnostics:

```text
SP0016 Warning docs/readme-examples/sp0016-capability-unknown/input.cs:9:9 Method 'TestMethod' is marked [AllowedCapabilities], but operation 'value.ToString()' could not be capability-verified: DynamicDispatch
```

### SP0017 - Misplaced [AllowedCapabilities]

Capability contract placement is validated independently of capability reasoning.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0017_MisplacedAllowedCapabilitiesExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0017-misplaced-capabilities/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public int Value => 42;
}
```

Expected analyzer diagnostics:

```text
SP0017 Error docs/readme-examples/sp0017-misplaced-capabilities/input.cs:6:6 The [AllowedCapabilities] attribute can only be applied to method declarations
```

### SP0018 - Postcondition not proven

Method-level symbolic postconditions report the exact reachable return site that violated the declared contract.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0018_EnsuresNotProvenExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0018-ensures-failing-return/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("result > 0")]
    public int Identity()
    {
        return 0;
    }
}
```

Expected analyzer diagnostics:

```text
SP0018 Warning docs/readme-examples/sp0018-ensures-failing-return/input.cs:9:16 Method 'Identity' is marked [Ensures], but return site '0' does not prove postcondition 'result > 0'
```

### SP0019 - Postcondition could not be verified

Unsupported or out-of-scope `[Ensures]` conditions stay conservative and report why verification stopped.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0019_EnsuresUnsupportedExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0019-ensures-unsupported/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("local > 0")]
    public int Value(int input)
    {
        var local = input + 1;
        return local;
    }
}
```

Expected analyzer diagnostics:

```text
SP0019 Warning docs/readme-examples/sp0019-ensures-unsupported/input.cs:6:6 Method 'Value' is marked [Ensures], but postcondition 'local > 0' could not be verified: local variables are not supported in [Ensures] conditions
```

### SP0020 - Misplaced [Ensures]

Postconditions are restricted to method-like declarations.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0020_MisplacedEnsuresExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0020-misplaced-ensures/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("true")]
    public int Value => 42;
}
```

Expected analyzer diagnostics:

```text
SP0020 Error docs/readme-examples/sp0020-misplaced-ensures/input.cs:6:6 The [Ensures] attribute can only be applied to method-like declarations
```

### SP0021 - Expected complexity exceeded

Complexity contracts report when the best proven asymptotic bound exceeds the declared maximum.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0021_ComplexityExceededExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0021-complexity-exceeded/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class TestClass
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int SumPairs(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}
```

Expected analyzer diagnostics:

```text
SP0021 Warning docs/readme-examples/sp0021-complexity-exceeded/input.cs:7:23 Method 'SumPairs' is marked [ExpectedComplexity(O(n))], but inferred complexity 'O(n^2)' exceeds the declared bound
```

### SP0022 - Expected complexity could not be verified

Unsupported or unbounded loop shapes stay conservative instead of being treated as verified.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0022_ComplexityUnknownExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0022-complexity-unknown/input.cs`):

```csharp
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public static class TestClass
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        _ = Environment.GetEnvironmentVariable("PATH");
        return n;
    }
}
```

Expected analyzer diagnostics:

```text
SP0022 Warning docs/readme-examples/sp0022-complexity-unknown/input.cs:8:23 Method 'Work' is marked [ExpectedComplexity(O(n))], but the declared bound could not be verified conservatively: ExternalCallee
```

### SP0023 - Misplaced [ExpectedComplexity]

Expected complexity contracts are restricted to method-like declarations.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0023_MisplacedExpectedComplexityExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0023-misplaced-expected-complexity/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [ExpectedComplexity(ComplexityKind.Constant)]
    public int Value { get; set; }
}
```

Expected analyzer diagnostics:

```text
SP0023 Error docs/readme-examples/sp0023-misplaced-expected-complexity/input.cs:6:6 The [ExpectedComplexity] attribute can only be applied to method-like declarations
```

### SP0024 - Invalid contract argument

Malformed contract arguments are reported at the contract instead of falling back to a later proof diagnostic.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0024_InvalidContractArgumentExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0024-invalid-contract-argument/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("")]
    public int Value()
    {
        return 1;
    }
}
```

Expected analyzer diagnostics:

```text
SP0024 Error docs/readme-examples/sp0024-invalid-contract-argument/input.cs:6:6 SharpProof contract '[Ensures]' has invalid argument '""': condition must not be empty
```

### SP0025 - Invalid analyzer configuration

Invalid `sharpproof_*` analyzer option values are reported instead of silently falling back to defaults.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0025_InvalidAnalyzerConfigurationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0025-invalid-analyzer-configuration/input.cs`):

```csharp
public sealed class TestClass
{
}
```

Expected analyzer diagnostics:

```text
SP0025 Warning <no-location>:1:1 SharpProof analyzer option 'sharpproof_smt_mode' has invalid value 'turbo': expected one of: disabled, bounded, default, deep, aggressive, or a boolean value
```
