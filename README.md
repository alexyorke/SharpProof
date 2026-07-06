<!-- Generated from README.source.md by scripts/Generate-Readme.ps1. -->

# SharpProof - Bounded Symbolic C# Analysis with Z3 SMT

SharpProof is a Roslyn analyzer and symbolic query toolkit for proving bounded
facts about C# code: purity violations, direct allocation sites, capability
usage, line-level invariants, runtime hazards, and conservative complexity.

## Preview Status

SharpProof is still preview software. Treat the current branch and packages as
alpha/beta quality rather than production-hardened tooling.

The project has also been developed through rapid AI-assisted iteration, or
"vibe-coded" development in the informal sense: broad feature growth, fast
refactoring, and heavy test coverage, but not the kind of long-lived
stabilization and compatibility discipline you would expect from a mature
analysis platform.

Expect rough edges:

- analyzer false positives and false negatives
- unsupported C# or library shapes that stay conservative or unknown
- public API, CLI, configuration, and diagnostic-surface changes between preview releases

The analyzer does not execute user code and does not attempt unbounded
whole-program proof. When it cannot prove a fact within the implemented rules
and budgets, it stays conservative.

## What SharpProof Does

SharpProof is more than just a purity checker. It uses Roslyn plus bounded
symbolic reasoning to answer questions like:

- can this method be proven pure?
- which direct allocation sites violate `[ZeroAllocations]`?
- which capability categories does this method use?
- what facts hold at this line?
- is this branch reachable?
- can this operation provably throw at runtime?
- what asymptotic complexity can be justified conservatively?

Under the hood the intended spine is:

```text
Roslyn/C# -> Symbolic IR -> normalized symbolic state -> proof service -> Z3-backed conclusions -> analyzer/API/CLI outputs
```

## Who It Is For

SharpProof is for .NET developers who want static guarantees or conservative
evidence around behavior without running the code:

- library authors enforcing purity or low-allocation contracts
- teams auditing runtime hazards and side effects during builds
- engineers exploring invariants and proof results from a CLI or .NET API
- contributors expanding symbolic reasoning over C# and the .NET SDK

Use something else if you need whole-program execution prediction, exact
performance profiling, or a full borrow checker today.

## Quick Start

The intended public packages are `SharpProof` and `SharpProof.Attributes`, both
at `0.1.0-preview.1`, but they are not published to NuGet.org yet.

For local preview use, build a local feed from this repo and install from it:

```powershell
.\build-nuget.ps1 -Configuration Release
dotnet add package SharpProof --version 0.1.0-preview.1 --source .\artifacts\nuget
```

The main analyzer package already includes the attributes assembly for normal
consumers. Add `SharpProof.Attributes` separately only when you want the
attributes without the analyzer package:

```powershell
dotnet add package SharpProof.Attributes --version 0.1.0-preview.1 --source .\artifacts\nuget
```

Minimal source example:

```csharp
using System;
using SharpProof.Attributes;

public sealed class Calculator
{
    [EnforcePure]
    public int Add(int left, int right) => left + right;

    [EnforcePure]
    public int ReadClock() => DateTime.Now.Second; // SP0002
}
```

## Selected Examples

These curated blocks are generated from committed example inputs and committed
output snapshots. Each example is backed by a regression test so the README can
fail fast when the public behavior or documentation drifts.

### Purity contract catches ambient clock access

`[EnforcePure]` does not treat ambient time reads as pure. A direct read of `DateTime.Now` still produces `SP0002`.

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

### Invariant query proves a branch-local fact

At a specific program point, the symbolic CLI can report the merged invariant, prove reachability, and check whether the current facts imply another condition.

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
Requested location: docs/readme-examples/invariants-positive/input.cs:7:13 position=123 distance=0 contained=True
Method: UseValue
Merged invariant: value > 0
Invariant merge: Conjunction
Path conditions: 1
Conservative unknown conditions: 0
Invariant query: Must=1, Maybe=0, Unknown=0, CandidatePoints=1, UnreachablePoints=0
Invariant query text: value > 0
Invariant query status: Exact
Invariant query status reason: all_candidate_program_points_exact
Invariant query summary: Invariant query is exact for the selected reachable program points.
Invariant query must facts: value > 0
Invariant query target: value status=Exact reason=target_exact code=SP-SYM-TARGET-EXACT must=1 maybe=0 unknown=0
Invariant query target summary: All selected reachable program points agree on the facts for this target.
Invariant query target path: value conditions=1 smt=1 points=1 reachablePoints=1 proofs=1 unknownProofs=0 reason=target_has_path_conditions code=SP-SYM-TARGET-PATH-CONDITIONS
Invariant query target path summary: This target has source-location path conditions available for invariant queries.
Invariant query target path conditions: value > 0
Invariant conditions:
  [0] value > 0 target=value kind=SmtBinary
Reachability: Reachable
Reachability reason: branch_reachable
Implies 'value > 0' target=value kind=SmtBinary: ProvenTrue
Implication formula: value > 0
Implication source: docs/readme-examples/invariants-positive/input.cs:7:13 position=123 node=ReturnStatement programPointKind=Statement span=123-136
Implication requested location: docs/readme-examples/invariants-positive/input.cs:7:13 position=123 distance=0 contained=True
Implication reason: branch_unreachable
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
Facts:
  value > 0
```

### Runtime hazard query proves guarded divide-by-zero

The runtime-hazard query surface can prove a concrete exception path from source guards without executing the method.

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
Trigger: SmtBinaryFormula { Kind = Bool, Operator = Equal, Left = SmtVariable { Kind = Int, Name = divisor#63 }, Right = SmtIntegerConstant { Kind = Int, Value = 0 } }
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

### Direct allocation sites under [ZeroAllocations]

`[ZeroAllocations]` reports each direct heap allocation site inside the annotated method-like body instead of collapsing everything into one method-level warning.

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

### Capability query for console I/O

The symbolic CLI can classify proven side-effect capability categories at a point inside a method, including derived umbrella categories such as `IO`.

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
Declaration kind: MethodDeclarationSyntax
Span: 5:5-8:6
Capabilities: IO, Console
Conservative: False
Sites:
  - [invocation] IO, Console via System.Console.WriteLine(System.String? value) @ 7:9
```

### Conservative method complexity query

The complexity query surface reports the best proven asymptotic cost for the containing method-like body and explains the structural drivers that established it.

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
Declaration kind: method
Span: 3:5-12:6
Complexity: O(n)
Kind: Linear
Conservative: False
Drivers:
  - [ForLoop] for-loop bound O(n) from n @ 6:9
```

## What It Can Prove Today

- Analyzer contracts:
  `[EnforcePure]`, `[Pure]`, `[ZeroAllocations]`,
  `[AllowedCapabilities(...)]`, and related diagnostics from `SP0002` through
  `SP0017`.
- Symbolic queries:
  line/position invariants, implication checks, reachability checks, runtime
  hazards, capability summaries, and conservative complexity queries.
- Runtime hazards:
  direct throws, divide-by-zero, null dereference, nullable value access,
  index/range issues, checked overflow, negative lengths, and other bounded
  source-visible hazards when the current evidence supports them.
- Summary-backed metadata reasoning:
  generated built-in effect summaries embedded during build/test plus optional
  external `*.SharpProof.EffectSummary.json` additional files.
- Conservative fallback behavior:
  unsupported library shapes, unknown external calls, unsupported regex or
  pattern shapes, and budget/time-limit cases stay unknown or unproven rather
  than being upgraded optimistically.

## Deeper Docs

- [Symbolic invariants and runtime-hazard query behavior](docs/symbolic-invariants.md)
- [Capability analysis and `[AllowedCapabilities]`](docs/capability-analysis.md)
- [Complexity query behavior](docs/complexity-queries.md)
- [Effect summaries and generated metadata behavior](docs/effect-summary.md)

## Current Limits

- SharpProof is bounded and conservative, not a whole-program execution engine.
- There is no meaningful "percent of the .NET SDK covered" claim yet; coverage
  is member-level and evidence-backed.
- Regex support is partial.
- Ownership and mutation reasoning is useful but still local; there is no full
  Rust-style borrow checker.
- Deep dispatch, hidden runtime behavior, reflection-heavy flows, dynamic
  behavior, and unsupported Roslyn shapes can remain conservative.

## Development And Validation

Use the repo wrappers for local validation so long-running .NET work runs under
the expected Windows Job Object:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 build SharpProof.sln --configuration Release
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane Main
.\scripts\Invoke-SharpProofTests.ps1 -Configuration Release -NoBuild -TestLane Tooling
```

The impacted-test wrapper can accelerate local loops, but full CI remains the
truth source before merge:

```powershell
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild -ListOnly -Explain
.\scripts\Invoke-SharpProofImpactedTests.ps1 -NoBuild
```

## Help And Feedback

- Open a bug report or feature request in the
  [GitHub issue tracker](https://github.com/alexyorke/SharpProof/issues).
- Use pull requests for fixes, test additions, and analyzer/symbolic
  improvements.
- Treat the current README as a landing page; the linked docs are the better
  place for detailed behavior and edge-case reference.
