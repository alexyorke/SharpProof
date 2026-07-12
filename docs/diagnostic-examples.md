<!-- Generated from docs/diagnostic-examples.source.md by scripts/Generate-Readme.ps1. -->

# SharpProof Diagnostic Example Gallery

This page is generated from committed example inputs and committed output
snapshots. It is the per-rule evidence catalog for the current public analyzer
surface.

Every example below is backed by a regression test. When the analyzer behavior
changes, the generator and the tests force this page to stay in sync.

## Coverage

The catalog intentionally includes at least one example for every public rule
from `SP0002` through `SP0047`.

<a id="sp0002"></a>

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

<a id="sp0003"></a>

### SP0003 - Misplaced [EnforcePure]

Purity contracts accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

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
SP0003 Error docs/readme-examples/sp0003-misplaced-enforce-pure/input.cs:4:2 The [EnforcePure]/[Pure] attributes can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0004"></a>

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

<a id="sp0005"></a>

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

<a id="sp0006"></a>

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

<a id="sp0007"></a>

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

<a id="sp0008"></a>

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

<a id="sp0009"></a>

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

<a id="sp0010"></a>

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

<a id="sp0011"></a>

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
Node: DivideExpression <offset-range>
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
  Permanently unavailable: False
  Transient retries: 0
  Recovered transient failures: 0
  Context recycles: 0
  Context generation: 0
  Max transient retries: 1
  Recycle context on transient failure: True
  Dispose context with service: False
```

<a id="sp0012"></a>

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

<a id="sp0013"></a>

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

<a id="sp0014"></a>

### SP0014 - Misplaced [ZeroAllocations]

Zero-allocation contracts accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

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
SP0014 Error docs/readme-examples/sp0014-misplaced-zero-allocations/input.cs:4:2 The [ZeroAllocations] attribute can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0015"></a>

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

<a id="sp0016"></a>

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

<a id="sp0017"></a>

### SP0017 - Misplaced [AllowedCapabilities]

Capability contracts accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0017_MisplacedAllowedCapabilitiesExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0017-misplaced-capabilities/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public int Value = 42;
}
```

Expected analyzer diagnostics:

```text
SP0017 Error docs/readme-examples/sp0017-misplaced-capabilities/input.cs:6:6 The [AllowedCapabilities] attribute can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0018"></a>

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

<a id="sp0019"></a>

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

<a id="sp0020"></a>

### SP0020 - Misplaced [Ensures]

Postconditions accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0020_MisplacedEnsuresExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0020-misplaced-ensures/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("true")]
    public int Value = 42;
}
```

Expected analyzer diagnostics:

```text
SP0020 Error docs/readme-examples/sp0020-misplaced-ensures/input.cs:6:6 The [Ensures] attribute can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0021"></a>

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

<a id="sp0022"></a>

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

<a id="sp0023"></a>

### SP0023 - Misplaced [ExpectedComplexity]

Complexity contracts accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0023_MisplacedExpectedComplexityExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0023-misplaced-expected-complexity/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [ExpectedComplexity(ComplexityKind.Constant)]
    public int Value = 42;
}
```

Expected analyzer diagnostics:

```text
SP0023 Error docs/readme-examples/sp0023-misplaced-expected-complexity/input.cs:6:6 The [ExpectedComplexity] attribute can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0024"></a>

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

<a id="sp0025"></a>

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
SP0025 Warning <no-location>:1:1 SharpProof analyzer option 'sharpproof_smt_mode' has invalid value 'turbo': expected one of: disabled, bounded, deep
```

<a id="sp0026"></a>

### SP0026 - Unrecognized attribute identity

SharpProof-looking attribute names from unaccepted namespaces are reported and ignored as contracts.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0026_UnrecognizedAttributeIdentityExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0026-unrecognized-attribute-identity/input.cs`):

```csharp
using System;

namespace ExternalContracts
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EnforcePureAttribute : Attribute
    {
    }
}

public sealed class TestClass
{
    [ExternalContracts.EnforcePure]
    public void NotSharpProof()
    {
        Console.WriteLine("not analyzed as a SharpProof contract");
    }
}
```

Expected analyzer diagnostics:

```text
SP0026 Warning docs/readme-examples/sp0026-unrecognized-attribute-identity/input.cs:13:6 Attribute 'EnforcePureAttribute' looks like a SharpProof contract, but type 'ExternalContracts.EnforcePureAttribute' is not in an accepted SharpProof attribute namespace
```

<a id="sp0027"></a>

### SP0027 - Precondition not proven

Calls to methods with `[Requires]` must prove the declared precondition at the call site.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0027_RequiresNotProvenExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0027-requires-not-proven/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Calculator
{
    [Requires("value > 0")]
    public static int Identity(int value) => value;

    public static int Demo()
    {
        return Identity(0);
    }
}
```

Expected analyzer diagnostics:

```text
SP0027 Warning docs/readme-examples/sp0027-requires-not-proven/input.cs:11:16 Call to 'Calculator.Identity(int)' does not prove precondition 'value > 0'
```

<a id="sp0028"></a>

### SP0028 - Precondition could not be verified

Unsupported `[Requires]` conditions remain conservative and report why verification stopped.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0028_RequiresUnsupportedExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0028-requires-unsupported/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Calculator
{
    [Requires("result > 0")]
    public static int Identity(int value) => value;
}
```

Expected analyzer diagnostics:

```text
SP0028 Warning docs/readme-examples/sp0028-requires-unsupported/input.cs:6:6 Precondition 'result > 0' for 'Calculator.Identity(int)' could not be verified: result placeholder is not supported in [Requires] conditions
```

<a id="sp0029"></a>

### SP0029 - Misplaced [Requires]

Preconditions are restricted to method-like declarations.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0029_MisplacedRequiresExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0029-misplaced-requires/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Calculator
{
    [Requires("true")]
    public int Value => 42;
}
```

Expected analyzer diagnostics:

```text
SP0029 Error docs/readme-examples/sp0029-misplaced-requires/input.cs:6:6 The [Requires] attribute can only be applied to method-like declarations
```

<a id="sp0030"></a>

### SP0030 - Exception contract violation

Exception contracts reject escaping exceptions that are not allowed by `[DoesNotThrow]` or `[AllowedExceptions]`.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0030_ExceptionContractViolationExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0030-exception-contract-violation/input.cs`):

```csharp
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class Worker
{
    [DoesNotThrow]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}
```

Expected analyzer diagnostics:

```text
SP0030 Warning docs/readme-examples/sp0030-exception-contract-violation/input.cs:10:9 Method 'Run' is marked [DoesNotThrow], but operation 'throw new InvalidOperationException();' can throw disallowed exceptions: System.InvalidOperationException
```

<a id="sp0031"></a>

### SP0031 - Misplaced exception contract

Exception contracts accept method-like declarations and getter-bearing property/indexer aliases while rejecting unrelated targets.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0031_MisplacedExceptionContractExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0031-misplaced-exception-contract/input.cs`):

```csharp
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Worker
{
    [DoesNotThrow]
    public int Value = 42;
}
```

Expected analyzer diagnostics:

```text
SP0031 Error docs/readme-examples/sp0031-misplaced-exception-contract/input.cs:6:6 The [DoesNotThrow] and [AllowedExceptions] attributes can only be applied to method-like declarations or getter-bearing properties and indexers
```

<a id="sp0032"></a>

### SP0032 - Invalid analyzer input file

Malformed or partially ignored analyzer AdditionalFiles are reported instead of silently dropped.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0032_InvalidAnalyzerInputExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0032-invalid-analyzer-input/input.cs`):

```csharp
public sealed class Demo
{
}
```

Expected analyzer diagnostics:

```text
SP0032 Warning <no-location>:1:1 SharpProof analyzer input file 'SharpProof.EffectSummary.json' is invalid: malformed effect-summary JSON
```

<a id="sp0033"></a>

### SP0033 - Unknown runtime-hazard candidate

Opt-in informational diagnostics expose source-visible hazard candidates whose bounded proof remains unknown.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0033_UnknownRuntimeHazardExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0033-unknown-runtime-hazard/input.cs`):

```csharp
public sealed class Demo
{
    public int Divide(int divisor) => 10 / divisor;
}
```

Expected analyzer diagnostics:

```text
SP0033 Info docs/readme-examples/sp0033-unknown-runtime-hazard/input.cs:3:39 Runtime hazard candidate 'DivideByZero' at operation '10 / divisor' could not be proven: branch_reachable
```

<a id="sp0034"></a>

### SP0034 - Inferred ZeroAllocations contract

Opt-in high-confidence adoption hints identify methods with no source-visible allocation sites.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0034_SuggestZeroAllocationsExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0034-suggest-zero-allocations/input.cs`):

```csharp
public static class AllocationCandidate
{
    public static int Identity(int value) => value;
}
```

Expected analyzer diagnostics:

```text
SP0034 Info docs/readme-examples/sp0034-suggest-zero-allocations/input.cs:3:23 Method 'Identity' has no source-visible allocation sites; consider adding [ZeroAllocations] (high confidence)
```

<a id="sp0035"></a>

### SP0035 - Inferred AllowedCapabilities contract

Opt-in high-confidence adoption hints report exact capability sets only when no capability site is unknown.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0035_SuggestCapabilitiesExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0035-suggest-capabilities/input.cs`):

```csharp
using System;

public static class CapabilityCandidate
{
    public static void Write() => Console.WriteLine(1);
}
```

Expected analyzer diagnostics:

```text
SP0035 Info docs/readme-examples/sp0035-suggest-capabilities/input.cs:5:24 Method 'Write' has the exact capability set IO, Console and no unknown capability sites; consider adding [AllowedCapabilities(SharpProofCapability.IO | SharpProofCapability.Console)] (high confidence)
```

<a id="sp0036"></a>

### SP0036 - Inferred ExpectedComplexity contract

Opt-in high-confidence adoption hints turn exact non-conservative complexity results into reviewable bounds.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0036_SuggestComplexityExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0036-suggest-complexity/input.cs`):

```csharp
public static class ComplexityCandidate
{
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            sum += i + j;
        return sum;
    }
}
```

Expected analyzer diagnostics:

```text
SP0036 Info docs/readme-examples/sp0036-suggest-complexity/input.cs:3:23 Method 'Work' has bounded symbolic complexity O(n^2) with no unknown drivers; consider adding [ExpectedComplexity(ComplexityKind.Quadratic)] (high confidence)
```

<a id="sp0037"></a>

### SP0037 - Inferred exception contract

Opt-in hints suggest DoesNotThrow for trivial closed bodies and medium-confidence AllowedExceptions for finite resolved sets.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0037_SuggestExceptionContractExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0037-suggest-exception-contract/input.cs`):

```csharp
public static class ExceptionCandidate
{
    public static int Identity(int value) => value;
}
```

Expected analyzer diagnostics:

```text
SP0037 Info docs/readme-examples/sp0037-suggest-exception-contract/input.cs:3:23 Method 'Identity' has a trivial closed body with no exception evidence; consider adding [DoesNotThrow] (high confidence)
```

<a id="sp0038"></a>

### SP0038 - Inferred Ensures contract

Opt-in high-confidence adoption hints infer simple postconditions shared by every visible return.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0038_SuggestEnsuresExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0038-suggest-ensures/input.cs`):

```csharp
public static class PostconditionCandidate
{
    public static int Identity(int value) => value;
}
```

Expected analyzer diagnostics:

```text
SP0038 Info docs/readme-examples/sp0038-suggest-ensures/input.cs:3:23 Method 'Identity' has a postcondition proved by every visible return: result == value; consider adding [Ensures("result == value")] (high confidence)
```

<a id="sp0039"></a>

### SP0039 - Inferred Requires contract

Opt-in high-confidence adoption hints infer simple preconditions from leading parameter guards that throw.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0039_SuggestRequiresExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0039-suggest-requires/input.cs`):

```csharp
using System;

public static class PreconditionCandidate
{
    public static int Positive(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}
```

Expected analyzer diagnostics:

```text
SP0039 Info docs/readme-examples/sp0039-suggest-requires/input.cs:5:23 Method 'Positive' has a leading throw guard whose normal-entry condition is value > 0; consider adding [Requires("value > 0")] (high confidence)
```

<a id="sp0040"></a>

### SP0040 - Trusted purity boundary review

Opt-in review evidence identifies the exact pure trust shortcut selected for a referenced boundary and can also expose overridden candidates.

Backed by test: `ReadmeGeneratedExamplesTests.Sp0040_TrustedBoundaryReviewExample_MatchesSnapshot`.

Source (`docs/readme-examples/sp0040-trusted-boundary-review/input.cs`):

```csharp
using SharpProof.Attributes;

public static class TrustedBoundary
{
    public static int Value(int value) => value;
}

public sealed class Consumer
{
    [EnforcePure]
    public int Read() => TrustedBoundary.Value(1);
}
```

Expected analyzer diagnostics:

```text
SP0040 Info docs/readme-examples/sp0040-trusted-boundary-review/input.cs:11:26 Purity trust source 'config_known_pure_method' for 'TrustedBoundary.Value(int)' was applied
```

<a id="sp0041"></a>

### SP0041 - Nullable return contract violated

Reachable normal returns must satisfy their declared non-null result contract.

Backed by test: `NullableContractVerificationTests.NonNullableReturn_NullLiteral_ReportsViolation`.

Source (`docs/readme-examples/sp0041-nullable-return-contract/input.cs`):

```csharp
#nullable enable
public static class NullableReturn
{
    public static string Name() => null;
}
```

Expected analyzer diagnostics:

```text
SP0041 Warning docs/readme-examples/sp0041-nullable-return-contract/input.cs:4:36 Method 'Name' can return null despite contract 'non-null return'
```

<a id="sp0042"></a>

### SP0042 - Nullable parameter postcondition violated

Conditional ref and out parameter promises are checked against each matching return.

Backed by test: `NullableContractVerificationTests.NotNullWhen_TrueWithNullOutValue_ReportsViolation`.

Source (`docs/readme-examples/sp0042-nullable-parameter-contract/input.cs`):

```csharp
#nullable enable
using System.Diagnostics.CodeAnalysis;
public static class NullableParameter
{
    public static bool TryGet([NotNullWhen(true)] out string? value)
    {
        value = null;
        return true;
    }
}
```

Expected analyzer diagnostics:

```text
SP0042 Warning docs/readme-examples/sp0042-nullable-parameter-contract/input.cs:8:9 Method 'TryGet' can complete with parameter 'value' null despite contract '[NotNullWhen(true)]'
```

<a id="sp0043"></a>

### SP0043 - Nullable member contract violated

Member-not-null contracts are verified at every relevant normal completion.

Backed by test: `NullableContractVerificationTests.MemberNotNull_EmptyInitializer_ReportsViolation`.

Source (`docs/readme-examples/sp0043-nullable-member-contract/input.cs`):

```csharp
#nullable enable
using System.Diagnostics.CodeAnalysis;
public sealed class NullableMember
{
    private string? _name;
    [MemberNotNull(nameof(_name))]
    public void Initialize() { }
}
```

Expected analyzer diagnostics:

```text
SP0043 Warning docs/readme-examples/sp0043-nullable-member-contract/input.cs:7:32 Method 'Initialize' can complete with member '_name' null despite contract '[MemberNotNull("_name")]'
```

<a id="sp0044"></a>

### SP0044 - Unsafe null-forgiving operator

A suppression is unsafe when bounded analysis finds a feasible null value.

Backed by test: `NullableContractVerificationTests.NullForgivingOperator_TracksUnsafeAndUnnecessaryUses`.

Source (`docs/readme-examples/sp0044-unsafe-null-forgiving/input.cs`):

```csharp
#nullable enable
public static class UnsafeSuppression
{
    public static int Length()
    {
        string? value = null;
        return value!.Length;
    }
}
```

Expected analyzer diagnostics:

```text
SP0044 Warning docs/readme-examples/sp0044-unsafe-null-forgiving/input.cs:7:21 Null-forgiving operator can suppress a feasible null value for 'value'
```

<a id="sp0045"></a>

### SP0045 - Unnecessary null-forgiving operator

A suppression can be removed when its operand is already proven non-null.

Backed by test: `SharpProofCodeFixTests.SP0045_RemovesUnnecessaryNullForgivingOperator`.

Source (`docs/readme-examples/sp0045-unnecessary-null-forgiving/input.cs`):

```csharp
#nullable enable
public static class UnnecessarySuppression
{
    public static int Length(string value) => value!.Length;
}
```

Expected analyzer diagnostics:

```text
SP0045 Info docs/readme-examples/sp0045-unnecessary-null-forgiving/input.cs:4:52 Null-forgiving operator is unnecessary because 'value' is proven non-null
```

<a id="sp0046"></a>

### SP0046 - Inferred nullable contract

Opt-in suggestions expose nullable contracts proved by all relevant paths.

Backed by test: `SharpProofCodeFixTests.SP0046_AddsInferredNullableReturnAttribute`.

Source (`docs/readme-examples/sp0046-suggest-nullable-contract/input.cs`):

```csharp
#nullable enable
public static class NullableSuggestion
{
    public static string? Name() => "name";
}
```

Expected analyzer diagnostics:

```text
SP0046 Info docs/readme-examples/sp0046-suggest-nullable-contract/input.cs:4:27 Method 'Name' satisfies nullable contract 'every reachable return expression is proven non-null'
```

<a id="sp0047"></a>

### SP0047 - Nullable verification inconclusive

Opt-in evidence reports when bounded nullable verification cannot establish a proof.

Backed by test: `NullableContractVerificationTests.InconclusiveNullableProof_CanBeEnabledExplicitly`.

Source (`docs/readme-examples/sp0047-nullable-inconclusive/input.cs`):

```csharp
#nullable enable
using System.Diagnostics.CodeAnalysis;
public sealed class NullableUnknown
{
    private int _reads;
    private string? Current => _reads++ == 0 ? "value" : null;
    [MemberNotNull(nameof(Current))]
    public void Initialize() { }
}
```

Expected analyzer diagnostics:

```text
SP0047 Info docs/readme-examples/sp0047-nullable-inconclusive/input.cs:8:32 Nullable contract 'Current' on 'Initialize' could not be verified: property getter stability is not proven
```
